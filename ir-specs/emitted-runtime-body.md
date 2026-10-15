---
feature: emitted-runtime-body
status: experimental
keywords: [runtime, golden, fragment, codegen, string, view]
category: codegen
---
# Emitted Runtime Bodies

## Documentation

A compiled program CONTAINS more code than its author declares. The entry stub `mrt_start`, the
`__mm_*`/`__slab_*` allocator, the `__str_*`/`__managed_*` families and the `__destruct_*`
thunks are all synthesized by the backend, and a `TargetIr` pin shows NONE of them: the
rendering holds only the functions parsed from the PROGRAM's own source, because the shared
scaffolding is identical in every test and would be pure noise in all of them.

The **library** is withheld by the same rule and for the same reason. The compiler compiles `stdlib/`
from source into the same module as the program, so `String.trim`, `Array.reserve` and every
grapheme helper the cone reaches are ordinary module functions the printer could render. Rendered,
they would outweigh the program in every pin — and what that noise costs is not disk: an edit
anywhere in `stdlib/` would move every pin at once, burying the signal the pins exist to carry.

That default is right for the scaffolding and wrong for a body that is itself under test.
`__str_bytes_view` publishes a zero-copy `Array with Byte` over a String's own bytes and
counts a reference on the allocation those bytes live in; getting that count wrong is a
use-after-free that BALANCES — the matching release disappears with it — so neither the
exit code, nor the leak gate, nor a pin of the program's own functions can see it.

### Naming a body to render: ```RequiredRuntime

A test opts one or more withheld functions INTO its own `TargetIr` pin with a
`RequiredRuntime` block, one name per line:

```
__str_bytes_view
```

A **stdlib** body is named the same way and by the same list — `String.trim`, `Array.reserve` —
which is how a case whose subject IS a library body pins the one body it is about. The name has to
be one the program REACHES: an unreached stdlib function is eliminated before the back end, so
naming it renders nothing and is refused exactly as a misspelling is.

Only the named functions are added, and only to that test's rendering; every test without
the block renders only the program's own functions. A name
that matches no emitted function — or one that names a function the rendering already
shows — is refused by the compiler, so a misspelling cannot silently pin nothing. An
EMPTY block is refused by the spec parser for the same reason: it would render no body at
all, leaving the pin identical to a test that never asked.

### What it pins, and what it does not

⚠ **This block pins the compiler's Target IR — what the backend DECIDED — not the bytes the
linker wrote.** It is the `printDataSection` half of the pair, not the ```RequiredData
half: the text is the printer's own rendering of a lowered, register-allocated body, so it
moves on any change to lowering, allocation or block structure, and is BLIND to anything
below it. An instruction encoder that emits different bytes for the same mnemonic and
operands leaves this pin identical. ```RequiredData / ```RequiredRdata are the gates
that read the linked image back; ```RequiredRuntime is not one of them.

### A hand-assembled chunk: the same block, rendered as BYTES

Some of what the compiler emits is not IR at all. The panic runtime (`mrt_panic` and its
backtrace walker), the green-thread pieces (`__gt_context_switch`, `__gt_trampoline`,
`__gt_morestack`) and arm64's entry stub are assembled as raw machine bytes, so they never
enter the module's function list and there is no body to print for them.

They are still named, and the same ```RequiredRuntime block reaches them: a name no IR
function answers is offered to the emitted chunks, and the one bearing it renders a hex dump
instead of a body.

```
chunk @mrt_panic {
    0000: 55 48 89 e5 …
}
```

⚠ **The bytes are the chunk's own, BEFORE linking** — every intra-module call still shows a
zero displacement (`e8 00 00 00 00`) and every imported call a zero IAT slot. That is
deliberate and it is what makes the pin usable: the linked image resolves those against
where everything landed, so a dump cut from it would move whenever anything ELSE in the
program changed size. A chunk's own buffer depends on nothing outside the chunk — the same
reason a printed IR body shows a branch LABEL rather than a displacement. What this pin
holds is therefore the ASSEMBLER's output, exactly as the IR form pins the lowering's.

## Tests

<!-- test: string-bytes-view-body -->
The byte-view runtime entry, reached through `stdlib/FilePath.maxon`'s own cone.
`__str_bytes_view`'s `bvcoown` block carries the incref that keeps the receiver alive for as long
as the buffer value exists; `bvret` is the arm an IMMORTAL record takes, which increfs nothing
because a `.rdata` record can neither be freed nor written.

⚠ **THE BODY ALLOCATES NOTHING.** A String record embeds the buffer record whole and keeps its flag
at `@48`, so `return managed` is served by handing back the receiver: one capacity test and one
`__mm_incref`. This case's `TargetIr` pin is where that shows as instructions rather than as a trace.

⚠ **THIS CASE NAMES ONE BODY.** `String.byteAtOrPanic` is `stdlib/String.maxon:299`, whose own body is
`try byteAt(index) otherwise panic(…)` — ordinary Maxon, compiled as an ordinary function, with no
runtime chunk to render. `String.addressableBytes()`'s corpus body is `return managed`, and reading a
fused wrapper's inline `managed` is exactly what mints this entry (`Parser.emitFieldLoad`).

```maxon
function main() returns ExitCode
	let p = FilePath from "a/b.txt"
	if p.fileExtension() == ".txt" 'ok'
		return 0
	end 'ok'
	return 1
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__str_bytes_view
```

```TargetIr:x64-windows
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
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
    x64.prologue 40
    x64.leaRegRdata rcx, [rip + __str_rec_37]  ; "a/b.txt"
  __il_body#5:
    x64.callDirect FilePath.create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#7
  __il_body#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#52:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_cont#51:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#35
  empty:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#32
  ifcont#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#54:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.jmp whilehdr#36
  scan#37:
    x64.leaRegRegImm32 r12, rax, -1
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.andRegImm32 rcx, rcx, 1
    x64.xorRegImm32 rax, rax, 1
    x64.xorRegImm32 rax, rax, -1
    x64.andRegReg rax, rax, r12
    x64.orRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#42
  __rc_ok#41:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.movRegReg rdx, r12
    x64.callDirect String.byteAtOrPanic
    x64.cmpRegImm32 r8, 92
    x64.jcc notEqual, ifcont#40
  isSep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#44
    x64.jmp __il_cont#32
  ifcont#40:
    x64.movRegReg rax, r12
  whilehdr#36:
    x64.cmpRegImm32 rax, 0
    x64.jcc above, scan#37
  whileexit#38:
    x64.movRegImm32 r12, 0
  __il_cont#32:
    x64.cmpRegImm32 r12, 0
    x64.jcc notEqual, ifcont#28
  noSep:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __str_retain
    x64.movRegReg r12, r8
    x64.jmp __il_body#48
  ifcont#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#50:
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
  __il_cont#49:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect String.addressableBytes
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __managed_slice
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#29
  tryerr#30:
    x64.leaRegRdata rcx, [rip + __str_blob_64]  ; "panic at FilePath.maxon:342: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#29:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r12, r13
  __il_body#48:
    x64.loadRegBaseDisp.word64 r13, [r12 + 8]
  __il_cont#47:
    x64.cmpRegImm32 r13, 1
    x64.jcc above, ifcont#12
  tooShort:
    x64.leaRegRdata rcx, [rip + __str_rec_65]  ; ""
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.jmp __il_cont#8
  ifcont#12:
    x64.movRegReg rcx, r12
    x64.callDirect String.addressableBytes
    x64.movRegReg r14, r8
  __il_body#46:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.jmp whilehdr#13
  scan#14:
    x64.leaRegRegImm32 r15, rax, -1
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.andRegImm32 rcx, rcx, 1
    x64.xorRegImm32 rax, rax, 1
    x64.xorRegImm32 rax, rax, -1
    x64.andRegReg rax, rax, r15
    x64.orRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#24
  __rc_ok#23:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect String.byteAtOrPanic
    x64.cmpRegImm32 r8, 46
    x64.jcc notEqual, ifcont#21
  isDot:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r13
    x64.callDirect __managed_slice
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#17
  tryerr#18:
    x64.leaRegRdata rcx, [rip + __str_blob_66]  ; "panic at FilePath.maxon:367: FilePath.fileExtension: slice \xe2\x80\x94 i from backwards scan, always <= nameLen\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#17:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop#19:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  exitdrop#20:
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.movRegReg r13, r15
    x64.jmp __il_cont#8
  ifcont#21:
    x64.movRegReg rax, r15
  whilehdr#13:
    x64.cmpRegImm32 rax, 1
    x64.jcc above, scan#14
  whileexit#15:
    x64.leaRegRdata rcx, [rip + __str_rec_65]  ; ""
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  exitdrop#22:
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
  __il_cont#8:
    x64.leaRegRdata rdx, [rip + __str_rec_38]  ; ".txt"
    x64.movRegReg rcx, r13
    x64.callDirect __str_eq
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont#3
  ok:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_FilePath
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont#3:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_FilePath
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at FilePath.maxon:46: FilePath: invalid path\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#42:
    x64.leaRegRdata rcx, [rip + __str_blob_155]  ; "panic at FilePath.maxon:322: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#44:
    x64.leaRegRdata rcx, [rip + __str_blob_156]  ; "panic at FilePath.maxon:325: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#24:
    x64.leaRegRdata rcx, [rip + __str_blob_157]  ; "panic at FilePath.maxon:363: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__str_bytes_view {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, bvret
  bvcoown:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_incref
  bvret:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
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
    x64.prologue 40
    x64.leaRegRdata rcx, [rip + __str_rec_37]  ; "a/b.txt"
  __il_body#5:
    x64.callDirect FilePath.create
    x64.movRegReg rbx, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr#7
  __il_body#33:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#52:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_cont#51:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, ifcont#35
  empty:
    x64.movRegImm32 r12, 0
    x64.jmp __il_cont#32
  ifcont#35:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#54:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.jmp whilehdr#36
  scan#37:
    x64.leaRegRegImm32 r12, rax, -1
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.andRegImm32 rcx, rcx, 1
    x64.xorRegImm32 rax, rax, 1
    x64.xorRegImm32 rax, rax, -1
    x64.andRegReg rax, rax, r12
    x64.orRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#42
  __rc_ok#41:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.movRegReg rdx, r12
    x64.callDirect String.byteAtOrPanic
    x64.cmpRegImm32 r8, 47
    x64.jcc notEqual, ifcont#40
  isSep:
    x64.leaRegRegImm32 r12, r12, 1
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic#44
    x64.jmp __il_cont#32
  ifcont#40:
    x64.movRegReg rax, r12
  whilehdr#36:
    x64.cmpRegImm32 rax, 0
    x64.jcc above, scan#37
  whileexit#38:
    x64.movRegImm32 r12, 0
  __il_cont#32:
    x64.cmpRegImm32 r12, 0
    x64.jcc notEqual, ifcont#28
  noSep:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect __str_retain
    x64.movRegReg r12, r8
    x64.jmp __il_body#48
  ifcont#28:
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
  __il_body#50:
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
  __il_cont#49:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 0]
    x64.callDirect String.addressableBytes
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.callDirect __managed_slice
    x64.movRegReg r12, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#29
  tryerr#30:
    x64.leaRegRdata rcx, [rip + __str_blob_63]  ; "panic at FilePath.maxon:342: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#29:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
    x64.movRegReg rcx, r12
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
  exitterminal:
    x64.movRegReg r12, r13
  __il_body#48:
    x64.loadRegBaseDisp.word64 r13, [r12 + 8]
  __il_cont#47:
    x64.cmpRegImm32 r13, 1
    x64.jcc above, ifcont#12
  tooShort:
    x64.leaRegRdata rcx, [rip + __str_rec_64]  ; ""
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.jmp __il_cont#8
  ifcont#12:
    x64.movRegReg rcx, r12
    x64.callDirect String.addressableBytes
    x64.movRegReg r14, r8
  __il_body#46:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.jmp whilehdr#13
  scan#14:
    x64.leaRegRegImm32 r15, rax, -1
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.andRegImm32 rcx, rcx, 1
    x64.xorRegImm32 rax, rax, 1
    x64.xorRegImm32 rax, rax, -1
    x64.andRegReg rax, rax, r15
    x64.orRegReg rcx, rcx, rax
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#24
  __rc_ok#23:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r15
    x64.callDirect String.byteAtOrPanic
    x64.cmpRegImm32 r8, 46
    x64.jcc notEqual, ifcont#21
  isDot:
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r13
    x64.callDirect __managed_slice
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#17
  tryerr#18:
    x64.leaRegRdata rcx, [rip + __str_blob_65]  ; "panic at FilePath.maxon:367: FilePath.fileExtension: slice \xe2\x80\x94 i from backwards scan, always <= nameLen\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#17:
    x64.movRegReg rcx, r13
    x64.callDirect __managed_retain
    x64.movRegReg rcx, r8
    x64.callDirect String.init
    x64.movRegReg r15, r8
    x64.movRegReg rcx, r13
    x64.callDirect __managed_decref
  exitdrop#19:
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  exitdrop#20:
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
    x64.movRegReg r13, r15
    x64.jmp __il_cont#8
  ifcont#21:
    x64.movRegReg rax, r15
  whilehdr#13:
    x64.cmpRegImm32 rax, 1
    x64.jcc above, scan#14
  whileexit#15:
    x64.leaRegRdata rcx, [rip + __str_rec_64]  ; ""
    x64.callDirect __str_retain
    x64.movRegReg r13, r8
    x64.movRegReg rcx, r14
    x64.callDirect __managed_decref
  exitdrop#22:
    x64.movRegReg rcx, r12
    x64.callDirect __str_decref
  __il_cont#8:
    x64.leaRegRdata rdx, [rip + __str_rec_38]  ; ".txt"
    x64.movRegReg rcx, r13
    x64.callDirect __str_eq
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r13
    x64.callDirect __str_decref
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, ifcont#3
  ok:
    x64.movRegImm32 r12, 0
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_FilePath
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont#3:
    x64.movRegImm32 r12, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __destruct_FilePath
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr#7:
    x64.leaRegRdata rcx, [rip + __str_blob_53]  ; "panic at FilePath.maxon:46: FilePath: invalid path\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#42:
    x64.leaRegRdata rcx, [rip + __str_blob_154]  ; "panic at FilePath.maxon:322: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#44:
    x64.leaRegRdata rcx, [rip + __str_blob_155]  ; "panic at FilePath.maxon:325: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#24:
    x64.leaRegRdata rcx, [rip + __str_blob_156]  ; "panic at FilePath.maxon:363: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__str_bytes_view {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 16]
    x64.cmpRegImm32 rax, -4
    x64.jcc equal, bvret
  bvcoown:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_incref
  bvret:
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_37  ; "a/b.txt"
  __il_body#5:
    arm64.bl FilePath.create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#7
  __il_body#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#52:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#51:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#35
  empty:
    arm64.movImm x20, 0
    arm64.b __il_cont#32
  ifcont#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#54:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#36
  scan#37:
    arm64.sub x20, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x20
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#42
  __rc_ok#41:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl String.byteAtOrPanic
    arm64.cmp x0, 47
    arm64.b.ne ifcont#40
  isSep:
    arm64.add x20, x20, 1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#44
    arm64.b __il_cont#32
  ifcont#40:
    arm64.movRegReg x0, x20
  whilehdr#36:
    arm64.cmp x0, 0
    arm64.b.hi scan#37
  whileexit#38:
    arm64.movImm x20, 0
  __il_cont#32:
    arm64.cmp x20, 0
    arm64.b.ne ifcont#28
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
    arm64.b __il_body#48
  ifcont#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#50:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#49:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __managed_slice
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#29
  tryerr#30:
    arm64.leaRdata x0, __str_blob_63  ; "panic at FilePath.maxon:342: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryok#29:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x20, x21
  __il_body#48:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 8]
  __il_cont#47:
    arm64.cmp x21, 1
    arm64.b.hi ifcont#12
  tooShort:
    arm64.leaRdata x0, __str_rec_64  ; ""
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.b __il_cont#8
  ifcont#12:
    arm64.movRegReg x0, x20
    arm64.bl String.addressableBytes
    arm64.movRegReg x22, x0
  __il_body#46:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.b whilehdr#13
  scan#14:
    arm64.sub x23, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x23
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#24
  __rc_ok#23:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl String.byteAtOrPanic
    arm64.cmp x0, 46
    arm64.b.ne ifcont#21
  isDot:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x21
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#17
  tryerr#18:
    arm64.leaRdata x0, __str_blob_65  ; "panic at FilePath.maxon:367: FilePath.fileExtension: slice \xe2\x80\x94 i from backwards scan, always <= nameLen\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryok#17:
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#19:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#20:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x21, x23
    arm64.b __il_cont#8
  ifcont#21:
    arm64.movRegReg x0, x23
  whilehdr#13:
    arm64.cmp x0, 1
    arm64.b.hi scan#14
  whileexit#15:
    arm64.leaRdata x0, __str_rec_64  ; ""
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#22:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#8:
    arm64.leaRdata x1, __str_rec_38  ; ".txt"
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.cbz x20, ifcont#3
  ok:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ifcont#3:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#7:
    arm64.leaRdata x0, __str_blob_53  ; "panic at FilePath.maxon:46: FilePath: invalid path\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#42:
    arm64.leaRdata x0, __str_blob_154  ; "panic at FilePath.maxon:322: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#44:
    arm64.leaRdata x0, __str_blob_155  ; "panic at FilePath.maxon:325: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#24:
    arm64.leaRdata x0, __str_blob_156  ; "panic at FilePath.maxon:363: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__str_bytes_view {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq bvret
  bvcoown:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  bvret:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_37  ; "a/b.txt"
  __il_body#5:
    arm64.bl FilePath.create
    arm64.movRegReg x19, x0
    arm64.cmp x9, 0
    arm64.b.ne tryerr#7
  __il_body#33:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#52:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_cont#51:
    arm64.cmp x0, 0
    arm64.b.ne ifcont#35
  empty:
    arm64.movImm x20, 0
    arm64.b __il_cont#32
  ifcont#35:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#54:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
    arm64.b whilehdr#36
  scan#37:
    arm64.sub x20, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x20
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#42
  __rc_ok#41:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.movRegReg x1, x20
    arm64.bl String.byteAtOrPanic
    arm64.cmp x0, 47
    arm64.b.ne ifcont#40
  isSep:
    arm64.add x20, x20, 1
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic#44
    arm64.b __il_cont#32
  ifcont#40:
    arm64.movRegReg x0, x20
  whilehdr#36:
    arm64.cmp x0, 0
    arm64.b.hi scan#37
  whileexit#38:
    arm64.movImm x20, 0
  __il_cont#32:
    arm64.cmp x20, 0
    arm64.b.ne ifcont#28
  noSep:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl __str_retain
    arm64.movRegReg x20, x0
    arm64.b __il_body#48
  ifcont#28:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
  __il_body#50:
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
  __il_cont#49:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.bl String.addressableBytes
    arm64.movRegReg x22, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.bl __managed_slice
    arm64.movRegReg x20, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#29
  tryerr#30:
    arm64.leaRdata x0, __str_blob_63  ; "panic at FilePath.maxon:342: FilePath.filename: slice \xe2\x80\x94 sepPlusOne from lastSepPlusOne, always <= len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryok#29:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
  exitterminal:
    arm64.movRegReg x20, x21
  __il_body#48:
    arm64.loadRegBaseDisp.word64 x21, [x20 + 8]
  __il_cont#47:
    arm64.cmp x21, 1
    arm64.b.hi ifcont#12
  tooShort:
    arm64.leaRdata x0, __str_rec_64  ; ""
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.b __il_cont#8
  ifcont#12:
    arm64.movRegReg x0, x20
    arm64.bl String.addressableBytes
    arm64.movRegReg x22, x0
  __il_body#46:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.b whilehdr#13
  scan#14:
    arm64.sub x23, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.and x1, x1, 1
    arm64.eor x0, x0, 1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x0, x16
    arm64.and x0, x0, x23
    arm64.orr x0, x1, x0
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#24
  __rc_ok#23:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x23
    arm64.bl String.byteAtOrPanic
    arm64.cmp x0, 46
    arm64.b.ne ifcont#21
  isDot:
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x21
    arm64.bl __managed_slice
    arm64.movRegReg x21, x0
    arm64.cmp x9, 0
    arm64.b.eq tryok#17
  tryerr#18:
    arm64.leaRdata x0, __str_blob_65  ; "panic at FilePath.maxon:367: FilePath.fileExtension: slice \xe2\x80\x94 i from backwards scan, always <= nameLen\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryok#17:
    arm64.movRegReg x0, x21
    arm64.bl __managed_retain
    arm64.bl String.init
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x21
    arm64.bl __managed_decref
  exitdrop#19:
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#20:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
    arm64.movRegReg x21, x23
    arm64.b __il_cont#8
  ifcont#21:
    arm64.movRegReg x0, x23
  whilehdr#13:
    arm64.cmp x0, 1
    arm64.b.hi scan#14
  whileexit#15:
    arm64.leaRdata x0, __str_rec_64  ; ""
    arm64.bl __str_retain
    arm64.movRegReg x21, x0
    arm64.movRegReg x0, x22
    arm64.bl __managed_decref
  exitdrop#22:
    arm64.movRegReg x0, x20
    arm64.bl __str_decref
  __il_cont#8:
    arm64.leaRdata x1, __str_rec_38  ; ".txt"
    arm64.movRegReg x0, x21
    arm64.bl __str_eq
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x21
    arm64.bl __str_decref
    arm64.cbz x20, ifcont#3
  ok:
    arm64.movImm x20, 0
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  ifcont#3:
    arm64.movImm x20, 1
    arm64.movRegReg x0, x19
    arm64.bl __destruct_FilePath
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  tryerr#7:
    arm64.leaRdata x0, __str_blob_53  ; "panic at FilePath.maxon:46: FilePath: invalid path\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#42:
    arm64.leaRdata x0, __str_blob_154  ; "panic at FilePath.maxon:322: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#44:
    arm64.leaRdata x0, __str_blob_155  ; "panic at FilePath.maxon:325: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
  __rc_panic#24:
    arm64.leaRdata x0, __str_blob_156  ; "panic at FilePath.maxon:363: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__str_bytes_view {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.cmp x0, -4
    arm64.b.eq bvret
  bvcoown:
    arm64.movRegReg x0, x19
    arm64.bl __mm_incref
  bvret:
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: allocator-shape-is-one-in-a-program-with-no-scheduler -->
**THE ALLOCATOR HAS ONE SHAPE, AND A PROGRAM WITH NO SCHEDULER CARRIES ALL OF IT.** This program builds
one array, pushes one element and returns; it spawns nothing and reads no counter. The four bodies
rendered here are what every heap program carries: `__slab_alloc` and `__slab_free` read the scheduler
word out of the slab's state head and take the raw row as its sole writer when it is 0, step that row's
raw traffic columns with a plain add, and probe the cached span's owner; `__mm_alloc` and `__mm_free`
step the tracked columns the same way. The serialised arm — the TLS read, the atomic steps and the lock —
is rendered beside them, reached only once `__sched_init_procs` has published the word. A pin that
lost the scheduler-word probe, the owner probe or a column step would be a compiler that had grown a
second shape for programs without a scheduler.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var buf = ByteArray.create()
	buf.push(1)
	return buf.count() as ExitCode
end 'main'
```
```exitcode
1
```
```RequiredRuntime
__mm_alloc
__mm_free
__slab_alloc
__slab_free
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
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at allocator-shape-is-one-in-a-program-with-no-scheduler.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegRegImm32 rcx, rbx, 24
    x64.callDirect __slab_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], r12
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.leaRegRegImm32 rdx, rax, 152152
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit
  shard#8:
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, shard#3
  shard#4:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, shard#3
  shard#6:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.cmpRegImm32 rcx, 255
    x64.jcc less, shard#7
  shard#3:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rcx, rdx, 8
    x64.lock add [rcx], rax
    x64.leaRegRegImm32 rax, rdx, 16
    x64.lock add [rax], rbx
    x64.jmp counted
  shard#7:
    x64.imulRegRegImm32 rcx, rcx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rcx
    x64.jmp shard#2
  critsplit:
    x64.movRegReg rax, rdx
  shard#2:
    x64.aluBaseDispImm32.add [rax + 8], 1
    x64.aluBaseDispReg.add [rax + 16], rbx
  counted:
    x64.leaRegRegImm32 r8, r8, 24
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_free {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rdx, [rax + 24]
    x64.leaRegRegImm32 rsi, rax, 152152
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, critsplit
  shard#8:
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, shard#3
  shard#4:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, shard#3
  shard#6:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 32]
    x64.cmpRegImm32 rdx, 255
    x64.jcc less, shard#7
  shard#3:
    x64.movRegImm32 rax, 1
    x64.lock add [rsi], rax
    x64.jmp counted
  shard#7:
    x64.imulRegRegImm32 rdx, rdx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rdx
    x64.jmp shard#2
  critsplit:
    x64.movRegReg rax, rsi
  shard#2:
    x64.aluBaseDispImm32.add [rax + 0], 1
  counted:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rax, [rsi + 8]
    x64.movRegReg rdi, rcx
    x64.movRegReg rcx, rax
    x64.x64MemFill 0x3f
    x64.movRegReg rcx, rsi
    x64.callDirect __slab_free
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__slab_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.cmpRegImm32 rcx, 32768
    x64.jcc greater, osdirect
  small:
    x64.cmpRegImm32 rcx, 1016
    x64.jcc lessEqual, cls#2
  cls#3:
    x64.leaRegRegImm32 rax, rcx, -897
    x64.sarRegImm8 rax, rax, 7
    x64.leaRegRdata rdx, [rip + __slab_size_to_class128]
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.byte rbx, [rax + 0]
    x64.jmp cls#4
  cls#2:
    x64.leaRegRegImm32 rax, rcx, 7
    x64.sarRegImm8 rax, rax, 3
    x64.leaRegRdata rdx, [rip + __slab_size_to_class8]
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.byte rbx, [rax + 0]
  cls#4:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, state#5
  critsplit#31:
    x64.movRegReg r8, rax
  state#6:
    x64.loadRegBaseDisp.word64 rax, [r8 + 24]
    x64.movRegImm32 r12, 0
    x64.leaRegRegImm32 rdx, r8, 152152
    x64.leaRegRegImm32 r13, r8, 138720
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#32
  shard#13:
    x64.mov rax, gs:[rax]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, shard#9
  shard#10:
    x64.leaRegRegImm32 r14, r8, 32
    x64.jmp shard#8
  shard#9:
    x64.loadRegBaseDisp.word64 rsi, [rax + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, shard#15
  shard#14:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 264]
    x64.aluBaseDispImm32.add [rsi + 312], 1
    x64.aluBaseDispReg.add [rsi + 328], rcx
  shard#15:
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.cmpRegImm32 r14, 0
    x64.setccReg equal, rsi
    x64.leaRegRegImm32 r15, rax, 592
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, critsplit#35
  shard#11:
    x64.loadRegBaseDisp.word64 rax, [r14 + 32]
    x64.cmpRegImm32 rax, 255
    x64.jcc greaterEqual, critsplit#34
    x64.jmp shard#12
  critsplit#35:
    x64.movRegReg r14, r15
    x64.jmp shard#8
  critsplit#34:
    x64.movRegReg r12, r14
    x64.movRegReg r14, r15
  shard#8:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rsi, rdx, 24
    x64.lock add [rsi], rax
    x64.leaRegRegImm32 rax, rdx, 40
    x64.lock add [rax], rcx
    x64.aluBaseDispImm32.add [r14 + 0], 1
    x64.leaRegRegImm32 rcx, r8, 40
    x64.iatCall 21
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.movRegReg rsi, r14
    x64.jmp routed
  shard#12:
    x64.imulRegRegImm32 rdx, rax, 48
    x64.leaRegRegImm32 rsi, r8, 139912
    x64.leaRegRegReg rdx, rsi, rdx
    x64.imulRegRegImm32 rax, rax, 544
    x64.leaRegRegReg rax, r8, rax
    x64.movRegReg rsi, rax
    x64.movRegReg rax, r14
    x64.jmp shard#7
  critsplit#32:
    x64.movRegReg rax, r12
    x64.movRegReg rsi, r13
  shard#7:
    x64.movRegImm32 r8, 0
    x64.aluBaseDispImm32.add [rdx + 24], 1
    x64.aluBaseDispReg.add [rdx + 40], rcx
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.movRegReg rsi, r8
  routed:
    x64.leaRegRegImm32 rcx, rax, 648
    x64.imulRegRegImm32 rdi, rbx, 8
    x64.leaRegRegReg r8, rcx, rdi
    x64.loadRegBaseDisp.word64 r9, [r8 + 0]
    x64.cmpRegImm32 r9, 0
    x64.jcc equal, pop#17
  pop#26:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, pop#17
  pop#27:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 48]
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, pop#17
  pop#18:
    x64.leaRegRdata rax, [rip + __slab_class_geom]
    x64.leaRegRegReg rax, rax, rdi
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rdx, rcx
    x64.andRegReg rdx, rcx, rax
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, pop#19
  pop#20:
    x64.loadRegBaseDisp.word64 rax, [r9 + 40]
    x64.sarRegImm8 rcx, rcx, 32
    x64.imulRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.word64 rdi, [r9 + 0]
    x64.leaRegRegReg rcx, rdi, rcx
    x64.cmpRegReg rax, rcx
    x64.jcc greaterEqual, pop#22
  pop#21:
    x64.leaRegRegReg rcx, rax, rdx
    x64.storeBaseDispReg.word64 [r9 + 40], rcx
    x64.jmp pop#23
  pop#19:
    x64.loadRegBaseDisp.word64 rax, [r10 + 0]
    x64.storeBaseDispReg.word64 [r9 + 8], rax
    x64.movRegReg rdi, r10
    x64.movRegReg rcx, rdx
    x64.x64MemFill 0x00
    x64.movRegReg rax, r10
  pop#23:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r9 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#25
  pop#24:
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [r8 + 0], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r9 + 48], rcx
  pop#25:
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, release
  handback:
    x64.movRegReg r8, rax
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  state#5:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __slab_state_base
    x64.loadRegSlot rcx, slot0
    x64.jmp state#6
  pop#17:
    x64.storeSlotReg slot1, rsi
    x64.storeSlotReg slot2, rdi
    x64.storeSlotReg slot3, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_refill
    x64.movRegReg r9, r8
    x64.loadRegSlot rsi, slot1
    x64.loadRegSlot rdi, slot2
    x64.loadRegSlot r8, slot3
    x64.jmp pop#18
  osdirect:
    x64.callDirect __slab_alloc_oversize
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#22:
    x64.leaRegRdata rcx, [rip + __abort_msg_86]  ; "fatal error: runtime abort 86 (slabSpanExhaustedPastItsEnd)\x0a"
    x64.movRegImm32 rdx, 60
    x64.movRegImm32 rax, 86
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  release:
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegImm32 rcx, rcx, 40
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot1, rsi
    x64.iatCall 22
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rsi, slot1
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.movRegReg r8, rax
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__slab_free {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 80
    x64.movRegReg rbx, rcx
    x64.leaRegGlobal rax, __slab_arena_map_l1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, map#4
  map#1:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 39
    x64.andRegImm32 rcx, rcx, 511
    x64.loadRegBaseIndexScale.word64 rax, [rax + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, map#4
  map#2:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 26
    x64.andRegImm32 rcx, rcx, 8191
    x64.loadRegBaseIndexScale.word64 rax, [rax + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, map#3
  map#4:
    x64.movRegImm32 r12, 0
    x64.jmp map#5
  map#3:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 13
    x64.andRegImm32 rcx, rcx, 8191
    x64.loadRegBaseIndexScale.word64 r12, [rax + rcx*8 + 0]
  map#5:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, osdirect
  tospan:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.movRegImm32 rdx, 0
    x64.leaRegRegImm32 rsi, rax, 152152
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit#43
  shard#13:
    x64.mov rdi, gs:[rcx]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, shard#8
  shard#9:
    x64.loadRegBaseDisp.word64 r8, [rdi + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, shard#15
  shard#14:
    x64.loadRegBaseDisp.word64 r8, [r8 + 264]
    x64.aluBaseDispImm32.add [r8 + 320], 1
  shard#15:
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, shard#8
  shard#11:
    x64.loadRegBaseDisp.word64 rdx, [rdi + 32]
    x64.cmpRegImm32 rdx, 255
    x64.jcc less, shard#12
  critsplit#45:
    x64.movRegReg rdx, rdi
  shard#8:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rsi, rsi, 32
    x64.lock add [rsi], rax
    x64.jmp routed
  shard#12:
    x64.imulRegRegImm32 rdx, rdx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rdx
    x64.jmp shard#7
  critsplit#43:
    x64.movRegReg rax, rsi
    x64.movRegReg rdi, rdx
  shard#7:
    x64.aluBaseDispImm32.add [rax + 32], 1
    x64.movRegReg rdx, rdi
  routed:
    x64.loadRegBaseDisp.word64 rax, [r12 + 48]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, notraw
  rawowned:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, rawlocked#18
    x64.jmp local
  notraw:
    x64.cmpRegReg rax, rdx
    x64.jcc notEqual, mine
  local:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.storeBaseDispReg.word64 [r12 + 8], rbx
    x64.aluBaseDispImm32.add [r12 + 16], 1
  push:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  mine:
    x64.cmpRegImm32 rax, -3
    x64.jcc equal, uncached
  notdead:
    x64.cmpRegImm32 rax, -2
    x64.jcc equal, parked
  remote:
    x64.movRegImm32 rcx, 1
    x64.leaRegRegImm32 rsi, r12, 64
    x64.lock add [rsi], rcx
    x64.cmpRegImm32 rdx, 0
    x64.setccReg equal, rcx
    x64.leaRegRegImm32 rsi, r12, 56
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, rpush
  rcacher:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, rpush
  rcredit:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rcx, rdx, 152
    x64.lock add [rcx], rax
  rpush:
    x64.loadRegBaseDisp.word64 rax, [rsi + 0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.lock cmpxchg [rsi], rbx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, rpush
  rpushed:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  rawlocked#18:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 rsi, rax, 32
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, rawlocked#26
  rawlocked#28:
    x64.mov rdi, gs:[rdi]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, rawlocked#26
  rawlocked#27:
    x64.leaRegRegImm32 rsi, rdi, 592
  rawlocked#26:
    x64.aluBaseDispImm32.add [rsi + 0], 1
    x64.leaRegRegImm32 rdi, rax, 40
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.movRegReg rcx, rdi
    x64.iatCall 21
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_raw
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegImm32 rax, rax, 40
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.movRegReg rcx, rax
    x64.iatCall 22
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, routed
    x64.jmp rawdone
  uncached:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, relistplain
  relistlock:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 rsi, rax, 32
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, relist#33
  relist#35:
    x64.mov rdi, gs:[rdi]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, relist#33
  relist#34:
    x64.leaRegRegImm32 rsi, rdi, 592
  relist#33:
    x64.aluBaseDispImm32.add [rsi + 0], 1
    x64.leaRegRegImm32 rdi, rax, 40
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.movRegReg rcx, rdi
    x64.iatCall 21
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_full
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegImm32 rax, rax, 40
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.movRegReg rcx, rax
    x64.iatCall 22
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.jmp relistdone
  relistplain:
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_full
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
  relistdone:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, routed
    x64.jmp relistret
  osdirect:
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_free_oversize
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  rawdone:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  relistret:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parked:
    x64.leaRegRdata rcx, [rip + __abort_msg_89]  ; "fatal error: runtime abort 89 (slabFreeOfParkedSpan)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 89
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at allocator-shape-is-one-in-a-program-with-no-scheduler.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegRegImm32 rcx, rbx, 24
    x64.callDirect __slab_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], r12
    x64.storeBaseDispReg.word64 [r8 + 8], rbx
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.leaRegRegImm32 rdx, rax, 152152
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit
  shard#8:
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, shard#3
  shard#4:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, shard#3
  shard#6:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 32]
    x64.cmpRegImm32 rcx, 255
    x64.jcc less, shard#7
  shard#3:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rcx, rdx, 8
    x64.lock add [rcx], rax
    x64.leaRegRegImm32 rax, rdx, 16
    x64.lock add [rax], rbx
    x64.jmp counted
  shard#7:
    x64.imulRegRegImm32 rcx, rcx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rcx
    x64.jmp shard#2
  critsplit:
    x64.movRegReg rax, rdx
  shard#2:
    x64.aluBaseDispImm32.add [rax + 8], 1
    x64.aluBaseDispReg.add [rax + 16], rbx
  counted:
    x64.leaRegRegImm32 r8, r8, 24
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mm_free {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rdx, [rax + 24]
    x64.leaRegRegImm32 rsi, rax, 152152
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, critsplit
  shard#8:
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, shard#3
  shard#4:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, shard#3
  shard#6:
    x64.loadRegBaseDisp.word64 rdx, [rdx + 32]
    x64.cmpRegImm32 rdx, 255
    x64.jcc less, shard#7
  shard#3:
    x64.movRegImm32 rax, 1
    x64.lock add [rsi], rax
    x64.jmp counted
  shard#7:
    x64.imulRegRegImm32 rdx, rdx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rdx
    x64.jmp shard#2
  critsplit:
    x64.movRegReg rax, rsi
  shard#2:
    x64.aluBaseDispImm32.add [rax + 0], 1
  counted:
    x64.leaRegRegImm32 rsi, rcx, -24
    x64.loadRegBaseDisp.word64 rax, [rsi + 8]
    x64.movRegReg rdi, rcx
    x64.movRegReg rcx, rax
    x64.x64MemFill 0x3f
    x64.movRegReg rcx, rsi
    x64.callDirect __slab_free
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @__slab_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 72
    x64.cmpRegImm32 rcx, 32768
    x64.jcc greater, osdirect
  small:
    x64.cmpRegImm32 rcx, 1016
    x64.jcc lessEqual, cls#2
  cls#3:
    x64.leaRegRegImm32 rax, rcx, -897
    x64.sarRegImm8 rax, rax, 7
    x64.leaRegRdata rdx, [rip + __slab_size_to_class128]
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.byte rbx, [rax + 0]
    x64.jmp cls#4
  cls#2:
    x64.leaRegRegImm32 rax, rcx, 7
    x64.sarRegImm8 rax, rax, 3
    x64.leaRegRdata rdx, [rip + __slab_size_to_class8]
    x64.leaRegRegReg rax, rdx, rax
    x64.loadRegBaseDisp.byte rbx, [rax + 0]
  cls#4:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, state#5
  critsplit#31:
    x64.movRegReg r8, rax
  state#6:
    x64.loadRegBaseDisp.word64 rax, [r8 + 24]
    x64.movRegImm32 r12, 0
    x64.leaRegRegImm32 rdx, r8, 152152
    x64.leaRegRegImm32 r13, r8, 138720
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#32
  shard#13:
    x64.mov rax, gs:[rax]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, shard#9
  shard#10:
    x64.leaRegRegImm32 r14, r8, 32
    x64.jmp shard#8
  shard#9:
    x64.loadRegBaseDisp.word64 rsi, [rax + 8]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, shard#15
  shard#14:
    x64.loadRegBaseDisp.word64 rsi, [rsi + 264]
    x64.aluBaseDispImm32.add [rsi + 312], 1
    x64.aluBaseDispReg.add [rsi + 328], rcx
  shard#15:
    x64.loadRegBaseDisp.word64 r14, [rax + 0]
    x64.cmpRegImm32 r14, 0
    x64.setccReg equal, rsi
    x64.leaRegRegImm32 r15, rax, 592
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, critsplit#35
  shard#11:
    x64.loadRegBaseDisp.word64 rax, [r14 + 32]
    x64.cmpRegImm32 rax, 255
    x64.jcc greaterEqual, critsplit#34
    x64.jmp shard#12
  critsplit#35:
    x64.movRegReg r14, r15
    x64.jmp shard#8
  critsplit#34:
    x64.movRegReg r12, r14
    x64.movRegReg r14, r15
  shard#8:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rsi, rdx, 24
    x64.lock add [rsi], rax
    x64.leaRegRegImm32 rax, rdx, 40
    x64.lock add [rax], rcx
    x64.aluBaseDispImm32.add [r14 + 0], 1
    x64.leaRegRegImm32 rcx, r8, 40
    x64.callDirect mrt_linux_lock_enter
    x64.movRegReg rdx, r12
    x64.movRegReg rax, r13
    x64.movRegReg rsi, r14
    x64.jmp routed
  shard#12:
    x64.imulRegRegImm32 rdx, rax, 48
    x64.leaRegRegImm32 rsi, r8, 139912
    x64.leaRegRegReg rdx, rsi, rdx
    x64.imulRegRegImm32 rax, rax, 544
    x64.leaRegRegReg rax, r8, rax
    x64.movRegReg rsi, rax
    x64.movRegReg rax, r14
    x64.jmp shard#7
  critsplit#32:
    x64.movRegReg rax, r12
    x64.movRegReg rsi, r13
  shard#7:
    x64.movRegImm32 r8, 0
    x64.aluBaseDispImm32.add [rdx + 24], 1
    x64.aluBaseDispReg.add [rdx + 40], rcx
    x64.movRegReg rdx, rax
    x64.movRegReg rax, rsi
    x64.movRegReg rsi, r8
  routed:
    x64.leaRegRegImm32 rcx, rax, 648
    x64.imulRegRegImm32 rdi, rbx, 8
    x64.leaRegRegReg r8, rcx, rdi
    x64.loadRegBaseDisp.word64 r9, [r8 + 0]
    x64.cmpRegImm32 r9, 0
    x64.jcc equal, pop#17
  pop#26:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, pop#17
  pop#27:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 48]
    x64.cmpRegReg rcx, rdx
    x64.jcc notEqual, pop#17
  pop#18:
    x64.leaRegRdata rax, [rip + __slab_class_geom]
    x64.leaRegRegReg rax, rax, rdi
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegImm32 rax, 4294967295
    x64.movRegReg rdx, rcx
    x64.andRegReg rdx, rcx, rax
    x64.loadRegBaseDisp.word64 r10, [r9 + 8]
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, pop#19
  pop#20:
    x64.loadRegBaseDisp.word64 rax, [r9 + 40]
    x64.sarRegImm8 rcx, rcx, 32
    x64.imulRegReg rcx, rcx, rdx
    x64.loadRegBaseDisp.word64 rdi, [r9 + 0]
    x64.leaRegRegReg rcx, rdi, rcx
    x64.cmpRegReg rax, rcx
    x64.jcc greaterEqual, pop#22
  pop#21:
    x64.leaRegRegReg rcx, rax, rdx
    x64.storeBaseDispReg.word64 [r9 + 40], rcx
    x64.jmp pop#23
  pop#19:
    x64.loadRegBaseDisp.word64 rax, [r10 + 0]
    x64.storeBaseDispReg.word64 [r9 + 8], rax
    x64.movRegReg rdi, r10
    x64.movRegReg rcx, rdx
    x64.x64MemFill 0x00
    x64.movRegReg rax, r10
  pop#23:
    x64.loadRegBaseDisp.word64 rcx, [r9 + 16]
    x64.leaRegRegImm32 rcx, rcx, -1
    x64.storeBaseDispReg.word64 [r9 + 16], rcx
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, pop#25
  pop#24:
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [r8 + 0], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [r9 + 48], rcx
  pop#25:
    x64.cmpRegImm32 rsi, 0
    x64.jcc notEqual, release
  handback:
    x64.movRegReg r8, rax
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  state#5:
    x64.storeSlotReg slot0, rcx
    x64.callDirect __slab_state_base
    x64.loadRegSlot rcx, slot0
    x64.jmp state#6
  pop#17:
    x64.storeSlotReg slot1, rsi
    x64.storeSlotReg slot2, rdi
    x64.storeSlotReg slot3, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_refill
    x64.movRegReg r9, r8
    x64.loadRegSlot rsi, slot1
    x64.loadRegSlot rdi, slot2
    x64.loadRegSlot r8, slot3
    x64.jmp pop#18
  osdirect:
    x64.callDirect __slab_alloc_oversize
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pop#22:
    x64.leaRegRdata rcx, [rip + __abort_msg_86]  ; "fatal error: runtime abort 86 (slabSpanExhaustedPastItsEnd)\x0a"
    x64.movRegImm32 rdx, 60
    x64.movRegImm32 rax, 86
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  release:
    x64.leaRegGlobal rcx, __slab_state
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.leaRegRegImm32 rcx, rcx, 40
    x64.storeSlotReg slot4, rax
    x64.storeSlotReg slot1, rsi
    x64.callDirect mrt_linux_lock_leave
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rsi, slot1
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.movRegReg r8, rax
    x64.epilogue 72
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__slab_free {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 80
    x64.movRegReg rbx, rcx
    x64.leaRegGlobal rax, __slab_arena_map_l1
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, map#4
  map#1:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 39
    x64.andRegImm32 rcx, rcx, 511
    x64.loadRegBaseIndexScale.word64 rax, [rax + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, map#4
  map#2:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 26
    x64.andRegImm32 rcx, rcx, 8191
    x64.loadRegBaseIndexScale.word64 rax, [rax + rcx*8 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, map#3
  map#4:
    x64.movRegImm32 r12, 0
    x64.jmp map#5
  map#3:
    x64.movRegReg rcx, rbx
    x64.sarRegImm8 rcx, rbx, 13
    x64.andRegImm32 rcx, rcx, 8191
    x64.loadRegBaseIndexScale.word64 r12, [rax + rcx*8 + 0]
  map#5:
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, osdirect
  tospan:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rcx, [rax + 24]
    x64.movRegImm32 rdx, 0
    x64.leaRegRegImm32 rsi, rax, 152152
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit#43
  shard#13:
    x64.mov rdi, gs:[rcx]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, shard#8
  shard#9:
    x64.loadRegBaseDisp.word64 r8, [rdi + 8]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, shard#15
  shard#14:
    x64.loadRegBaseDisp.word64 r8, [r8 + 264]
    x64.aluBaseDispImm32.add [r8 + 320], 1
  shard#15:
    x64.loadRegBaseDisp.word64 rdi, [rdi + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, shard#8
  shard#11:
    x64.loadRegBaseDisp.word64 rdx, [rdi + 32]
    x64.cmpRegImm32 rdx, 255
    x64.jcc less, shard#12
  critsplit#45:
    x64.movRegReg rdx, rdi
  shard#8:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rsi, rsi, 32
    x64.lock add [rsi], rax
    x64.jmp routed
  shard#12:
    x64.imulRegRegImm32 rdx, rdx, 48
    x64.leaRegRegImm32 rax, rax, 139912
    x64.leaRegRegReg rax, rax, rdx
    x64.jmp shard#7
  critsplit#43:
    x64.movRegReg rax, rsi
    x64.movRegReg rdi, rdx
  shard#7:
    x64.aluBaseDispImm32.add [rax + 32], 1
    x64.movRegReg rdx, rdi
  routed:
    x64.loadRegBaseDisp.word64 rax, [r12 + 48]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, notraw
  rawowned:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, rawlocked#18
    x64.jmp local
  notraw:
    x64.cmpRegReg rax, rdx
    x64.jcc notEqual, mine
  local:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.storeBaseDispReg.word64 [r12 + 8], rbx
    x64.aluBaseDispImm32.add [r12 + 16], 1
  push:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  mine:
    x64.cmpRegImm32 rax, -3
    x64.jcc equal, uncached
  notdead:
    x64.cmpRegImm32 rax, -2
    x64.jcc equal, parked
  remote:
    x64.movRegImm32 rcx, 1
    x64.leaRegRegImm32 rsi, r12, 64
    x64.lock add [rsi], rcx
    x64.cmpRegImm32 rdx, 0
    x64.setccReg equal, rcx
    x64.leaRegRegImm32 rsi, r12, 56
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, rpush
  rcacher:
    x64.cmpRegImm32 rax, 0
    x64.jcc less, rpush
  rcredit:
    x64.movRegImm32 rax, 1
    x64.leaRegRegImm32 rcx, rdx, 152
    x64.lock add [rcx], rax
  rpush:
    x64.loadRegBaseDisp.word64 rax, [rsi + 0]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.lock cmpxchg [rsi], rbx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, rpush
  rpushed:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  rawlocked#18:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 rsi, rax, 32
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, rawlocked#26
  rawlocked#28:
    x64.mov rdi, gs:[rdi]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, rawlocked#26
  rawlocked#27:
    x64.leaRegRegImm32 rsi, rdi, 592
  rawlocked#26:
    x64.aluBaseDispImm32.add [rsi + 0], 1
    x64.leaRegRegImm32 rdi, rax, 40
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.movRegReg rcx, rdi
    x64.callDirect mrt_linux_lock_enter
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_raw
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegImm32 rax, rax, 40
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.movRegReg rcx, rax
    x64.callDirect mrt_linux_lock_leave
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, routed
    x64.jmp rawdone
  uncached:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, relistplain
  relistlock:
    x64.leaRegGlobal rax, __slab_state
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegRegImm32 rsi, rax, 32
    x64.loadRegBaseDisp.word64 rdi, [rax + 24]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, relist#33
  relist#35:
    x64.mov rdi, gs:[rdi]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, relist#33
  relist#34:
    x64.leaRegRegImm32 rsi, rdi, 592
  relist#33:
    x64.aluBaseDispImm32.add [rsi + 0], 1
    x64.leaRegRegImm32 rdi, rax, 40
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.movRegReg rcx, rdi
    x64.callDirect mrt_linux_lock_enter
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_full
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.leaRegRegImm32 rax, rax, 40
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.storeSlotReg slot3, rsi
    x64.storeSlotReg slot4, r8
    x64.movRegReg rcx, rax
    x64.callDirect mrt_linux_lock_leave
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
    x64.loadRegSlot rsi, slot3
    x64.loadRegSlot r8, slot4
    x64.aluBaseDispImm32.add [rsi + 0], -1
    x64.jmp relistdone
  relistplain:
    x64.storeSlotReg slot1, rcx
    x64.storeSlotReg slot2, rdx
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __slab_free_to_full
    x64.loadRegSlot rcx, slot1
    x64.loadRegSlot rdx, slot2
  relistdone:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, routed
    x64.jmp relistret
  osdirect:
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_free_oversize
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  rawdone:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  relistret:
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parked:
    x64.leaRegRdata rcx, [rip + __abort_msg_89]  ; "fatal error: runtime abort 89 (slabFreeOfParkedSpan)\x0a"
    x64.movRegImm32 rdx, 53
    x64.movRegImm32 rax, 89
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 80
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at allocator-shape-is-one-in-a-program-with-no-scheduler.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__mm_alloc {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.add x0, x19, 24
    arm64.bl __slab_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x20
    arm64.storeBaseDispReg.word64 [x0 + 8], x19
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.add x3, x1, 152152
    arm64.cmp x2, 0
    arm64.b.eq critsplit
  shard#8:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#4:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#6:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#7
  shard#3:
    arm64.movImm x1, 1
    arm64.add x2, x3, 8
    arm64.arm64AtomicRmw.add x1, [x2], x1
    arm64.add x1, x3, 16
    arm64.arm64AtomicRmw.add x1, [x1], x19
    arm64.b counted
  shard#7:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x1, x1, 139912
    arm64.add x1, x1, x2
    arm64.b shard#2
  critsplit:
    arm64.movRegReg x1, x3
  shard#2:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, x19
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
  counted:
    arm64.add x0, x0, 24
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__mm_free {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.add x3, x1, 152152
    arm64.cmp x2, 0
    arm64.b.eq critsplit
  shard#8:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#4:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#6:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#7
  shard#3:
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x1, [x3], x1
    arm64.b counted
  shard#7:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x1, x1, 139912
    arm64.add x1, x1, x2
    arm64.b shard#2
  critsplit:
    arm64.movRegReg x1, x3
  shard#2:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  counted:
    arm64.sub x4, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x4 + 8]
    arm64.arm64MemFill 0x3f
    arm64.movRegReg x0, x4
    arm64.bl __slab_free
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @__slab_alloc {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x23
    arm64.storeSlotReg slot7, x22
    arm64.storeSlotReg slot6, x21
    arm64.storeSlotReg slot5, x20
    arm64.storeSlotReg slot4, x19
    arm64.cmp x0, 32768
    arm64.b.gt osdirect
  small:
    arm64.cmp x0, 1016
    arm64.b.le cls#2
  cls#3:
    arm64.add x1, x0, -897
    arm64.asr x1, x1, 7
    arm64.leaRdata x2, __slab_size_to_class128
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x19, [x1 + 0]
    arm64.b cls#4
  cls#2:
    arm64.add x1, x0, 7
    arm64.asr x1, x1, 3
    arm64.leaRdata x2, __slab_size_to_class8
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x19, [x1 + 0]
  cls#4:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq state#5
  state#6:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.movImm x20, 0
    arm64.add x3, x1, 152152
    arm64.add x21, x1, 138720
    arm64.cmp x2, 0
    arm64.b.eq critsplit#32
  shard#13:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.ne shard#9
  shard#10:
    arm64.add x22, x1, 32
    arm64.b shard#8
  shard#9:
    arm64.loadRegBaseDisp.word64 x4, [x2 + 8]
    arm64.cmp x4, 0
    arm64.b.eq shard#15
  shard#14:
    arm64.loadRegBaseDisp.word64 x4, [x4 + 264]
    arm64.loadRegBaseDisp.word64 x5, [x4 + 312]
    arm64.add x5, x5, 1
    arm64.storeBaseDispReg.word64 [x4 + 312], x5
    arm64.loadRegBaseDisp.word64 x5, [x4 + 328]
    arm64.add x5, x5, x0
    arm64.storeBaseDispReg.word64 [x4 + 328], x5
  shard#15:
    arm64.loadRegBaseDisp.word64 x22, [x2 + 0]
    arm64.cmp x22, 0
    arm64.cset x4, eq
    arm64.add x23, x2, 592
    arm64.cbnz x4, critsplit#35
  shard#11:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 32]
    arm64.cmp x2, 255
    arm64.b.ge critsplit#34
    arm64.b shard#12
  critsplit#35:
    arm64.movRegReg x22, x23
    arm64.b shard#8
  critsplit#34:
    arm64.movRegReg x20, x22
    arm64.movRegReg x22, x23
  shard#8:
    arm64.movImm x2, 1
    arm64.add x4, x3, 24
    arm64.arm64AtomicRmw.add x2, [x4], x2
    arm64.add x2, x3, 40
    arm64.arm64AtomicRmw.add x0, [x2], x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.add x0, x1, 40
    arm64.importCall 18
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.movRegReg x4, x22
    arm64.b routed
  shard#12:
    arm64.movImm x16, 48
    arm64.mul x3, x2, x16
    arm64.add x4, x1, 139912
    arm64.add x3, x4, x3
    arm64.movImm x16, 544
    arm64.mul x2, x2, x16
    arm64.add x1, x1, x2
    arm64.movRegReg x2, x1
    arm64.movRegReg x1, x22
    arm64.b shard#7
  critsplit#32:
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
  shard#7:
    arm64.movImm x4, 0
    arm64.loadRegBaseDisp.word64 x5, [x3 + 24]
    arm64.add x5, x5, 1
    arm64.storeBaseDispReg.word64 [x3 + 24], x5
    arm64.loadRegBaseDisp.word64 x5, [x3 + 40]
    arm64.add x0, x5, x0
    arm64.storeBaseDispReg.word64 [x3 + 40], x0
  routed:
    arm64.add x0, x2, 648
    arm64.lsl x3, x19, 3
    arm64.add x5, x0, x3
    arm64.loadRegBaseDisp.word64 x6, [x5 + 0]
    arm64.cmp x6, 0
    arm64.b.eq pop#17
  pop#26:
    arm64.loadRegBaseDisp.word64 x0, [x6 + 16]
    arm64.cmp x0, 0
    arm64.b.eq pop#17
  pop#27:
    arm64.add x16, x6, 48
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, x1
    arm64.b.ne pop#17
  pop#18:
    arm64.leaRdata x0, __slab_class_geom
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 4294967295
    arm64.and x1, x0, x1
    arm64.loadRegBaseDisp.word64 x7, [x6 + 8]
    arm64.cmp x7, 0
    arm64.b.ne pop#19
  pop#20:
    arm64.loadRegBaseDisp.word64 x2, [x6 + 40]
    arm64.asr x0, x0, 32
    arm64.mul x0, x1, x0
    arm64.loadRegBaseDisp.word64 x3, [x6 + 0]
    arm64.add x0, x3, x0
    arm64.cmp x2, x0
    arm64.b.ge pop#22
  pop#21:
    arm64.add x0, x2, x1
    arm64.storeBaseDispReg.word64 [x6 + 40], x0
    arm64.movRegReg x0, x2
    arm64.b pop#23
  pop#19:
    arm64.loadRegBaseDisp.word64 x0, [x7 + 0]
    arm64.storeBaseDispReg.word64 [x6 + 8], x0
    arm64.movRegReg x0, x7
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x7
  pop#23:
    arm64.loadRegBaseDisp.word64 x1, [x6 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x6 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#25
  pop#24:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x5 + 0], x1
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x6, 48
    arm64.stlr.word64 [x16], x1
  pop#25:
    arm64.cmp x4, 0
    arm64.b.ne release
  handback:
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  state#5:
    arm64.storeSlotReg slot0, x0
    arm64.bl __slab_state_base
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b state#6
  pop#17:
    arm64.storeSlotReg slot1, x3
    arm64.storeSlotReg slot2, x4
    arm64.storeSlotReg slot3, x5
    arm64.movRegReg x0, x19
    arm64.bl __slab_refill
    arm64.loadRegSlot x3, slot1
    arm64.loadRegSlot x4, slot2
    arm64.loadRegSlot x5, slot3
    arm64.movRegReg x6, x0
    arm64.b pop#18
  osdirect:
    arm64.bl __slab_alloc_oversize
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  pop#22:
    arm64.leaRdata x0, __abort_msg_86  ; "fatal error: runtime abort 86 (slabSpanExhaustedPastItsEnd)\x0a"
    arm64.movImm x1, 60
    arm64.movImm x2, 86
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  release:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.add x1, x1, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x4
    arm64.movRegReg x0, x1
    arm64.importCall 19
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x4, slot2
    arm64.loadRegBaseDisp.word64 x1, [x4 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x4 + 0], x1
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
}

func @__slab_free {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x20
    arm64.storeSlotReg slot5, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __slab_arena_map_l1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq map#4
  map#1:
    arm64.asr x1, x19, 39
    arm64.and x1, x1, 511
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.eq map#4
  map#2:
    arm64.asr x1, x19, 26
    arm64.and x1, x1, 8191
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.ne map#3
  map#4:
    arm64.movImm x20, 0
    arm64.b map#5
  map#3:
    arm64.asr x1, x19, 13
    arm64.and x1, x1, 8191
    arm64.loadRegBaseIndexScale.word64 x20, [x0 + x1*8 + 0]
  map#5:
    arm64.cmp x20, 0
    arm64.b.eq osdirect
  tospan:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 24]
    arm64.movImm x2, 0
    arm64.add x3, x0, 152152
    arm64.cmp x1, 0
    arm64.b.eq critsplit#43
  shard#13:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq shard#8
  shard#9:
    arm64.loadRegBaseDisp.word64 x5, [x4 + 8]
    arm64.cmp x5, 0
    arm64.b.eq shard#15
  shard#14:
    arm64.loadRegBaseDisp.word64 x5, [x5 + 264]
    arm64.loadRegBaseDisp.word64 x6, [x5 + 320]
    arm64.add x6, x6, 1
    arm64.storeBaseDispReg.word64 [x5 + 320], x6
  shard#15:
    arm64.loadRegBaseDisp.word64 x4, [x4 + 0]
    arm64.cmp x4, 0
    arm64.b.eq shard#8
  shard#11:
    arm64.loadRegBaseDisp.word64 x2, [x4 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#12
  critsplit#45:
    arm64.movRegReg x2, x4
  shard#8:
    arm64.movImm x0, 1
    arm64.add x3, x3, 32
    arm64.arm64AtomicRmw.add x0, [x3], x0
    arm64.b routed
  shard#12:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x0, x0, 139912
    arm64.add x0, x0, x2
    arm64.b shard#7
  critsplit#43:
    arm64.movRegReg x0, x3
    arm64.movRegReg x4, x2
  shard#7:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x2
    arm64.movRegReg x2, x4
  routed:
    arm64.add x16, x20, 48
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 0
    arm64.b.ne notraw
  rawowned:
    arm64.cmp x1, 0
    arm64.b.ne rawlocked#18
    arm64.b local
  notraw:
    arm64.cmp x0, x2
    arm64.b.ne mine
  local:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x20 + 8], x19
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
  push:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  mine:
    arm64.cmp x0, -3
    arm64.b.eq uncached
  notdead:
    arm64.cmp x0, -2
    arm64.b.eq parked
  remote:
    arm64.movImm x1, 1
    arm64.add x3, x20, 64
    arm64.arm64AtomicRmw.add x1, [x3], x1
    arm64.cmp x2, 0
    arm64.cset x1, eq
    arm64.add x3, x20, 56
    arm64.cbnz x1, rpush
  rcacher:
    arm64.cmp x0, 0
    arm64.b.lt rpush
  rcredit:
    arm64.movImm x0, 1
    arm64.add x1, x2, 152
    arm64.arm64AtomicRmw.add x0, [x1], x0
  rpush:
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.arm64AtomicCas x0, [x3], x0, x19
    arm64.cmp x0, 0
    arm64.b.eq rpush
  rpushed:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  rawlocked#18:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, 32
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 0
    arm64.b.eq rawlocked#26
  rawlocked#28:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x4
    arm64.cmp x4, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq rawlocked#26
  rawlocked#27:
    arm64.add x3, x4, 592
  rawlocked#26:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x4, x4, 1
    arm64.storeBaseDispReg.word64 [x3 + 0], x4
    arm64.add x4, x0, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x4
    arm64.importCall 18
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_raw
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x0, x0, 40
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.importCall 19
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x3 + 0], x0
    arm64.cmp x4, 0
    arm64.b.eq routed
    arm64.b rawdone
  uncached:
    arm64.cmp x1, 0
    arm64.b.eq relistplain
  relistlock:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, 32
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 0
    arm64.b.eq relist#33
  relist#35:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x4
    arm64.cmp x4, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq relist#33
  relist#34:
    arm64.add x3, x4, 592
  relist#33:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x4, x4, 1
    arm64.storeBaseDispReg.word64 [x3 + 0], x4
    arm64.add x4, x0, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x4
    arm64.importCall 18
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_full
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x0, x0, 40
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.importCall 19
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x3 + 0], x0
    arm64.b relistdone
  relistplain:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_full
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x4, x0
  relistdone:
    arm64.cmp x4, 0
    arm64.b.eq routed
    arm64.b relistret
  osdirect:
    arm64.movRegReg x0, x19
    arm64.bl __slab_free_oversize
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  rawdone:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  relistret:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  parked:
    arm64.leaRdata x0, __abort_msg_89  ; "fatal error: runtime abort 89 (slabFreeOfParkedSpan)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 89
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at allocator-shape-is-one-in-a-program-with-no-scheduler.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__mm_alloc {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.add x0, x19, 24
    arm64.bl __slab_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x20
    arm64.storeBaseDispReg.word64 [x0 + 8], x19
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.add x3, x1, 152152
    arm64.cmp x2, 0
    arm64.b.eq critsplit
  shard#8:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#4:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#6:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#7
  shard#3:
    arm64.movImm x1, 1
    arm64.add x2, x3, 8
    arm64.arm64AtomicRmw.add x1, [x2], x1
    arm64.add x1, x3, 16
    arm64.arm64AtomicRmw.add x1, [x1], x19
    arm64.b counted
  shard#7:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x1, x1, 139912
    arm64.add x1, x1, x2
    arm64.b shard#2
  critsplit:
    arm64.movRegReg x1, x3
  shard#2:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 8]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 8], x2
    arm64.loadRegBaseDisp.word64 x2, [x1 + 16]
    arm64.add x2, x2, x19
    arm64.storeBaseDispReg.word64 [x1 + 16], x2
  counted:
    arm64.add x0, x0, 24
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__mm_free {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.add x3, x1, 152152
    arm64.cmp x2, 0
    arm64.b.eq critsplit
  shard#8:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#4:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, 0
    arm64.b.eq shard#3
  shard#6:
    arm64.loadRegBaseDisp.word64 x2, [x2 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#7
  shard#3:
    arm64.movImm x1, 1
    arm64.arm64AtomicRmw.add x1, [x3], x1
    arm64.b counted
  shard#7:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x1, x1, 139912
    arm64.add x1, x1, x2
    arm64.b shard#2
  critsplit:
    arm64.movRegReg x1, x3
  shard#2:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
  counted:
    arm64.sub x4, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x4 + 8]
    arm64.arm64MemFill 0x3f
    arm64.movRegReg x0, x4
    arm64.bl __slab_free
    arm64.movRegReg x1, x0
    arm64.epilogue 16
    arm64.ret
}

func @__slab_alloc {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot8, x23
    arm64.storeSlotReg slot7, x22
    arm64.storeSlotReg slot6, x21
    arm64.storeSlotReg slot5, x20
    arm64.storeSlotReg slot4, x19
    arm64.cmp x0, 32768
    arm64.b.gt osdirect
  small:
    arm64.cmp x0, 1016
    arm64.b.le cls#2
  cls#3:
    arm64.add x1, x0, -897
    arm64.asr x1, x1, 7
    arm64.leaRdata x2, __slab_size_to_class128
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x19, [x1 + 0]
    arm64.b cls#4
  cls#2:
    arm64.add x1, x0, 7
    arm64.asr x1, x1, 3
    arm64.leaRdata x2, __slab_size_to_class8
    arm64.add x1, x2, x1
    arm64.loadRegBaseDisp.byte x19, [x1 + 0]
  cls#4:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.eq state#5
  state#6:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 24]
    arm64.movImm x20, 0
    arm64.add x3, x1, 152152
    arm64.add x21, x1, 138720
    arm64.cmp x2, 0
    arm64.b.eq critsplit#32
  shard#13:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x2, 0
    arm64.b.ne shard#9
  shard#10:
    arm64.add x22, x1, 32
    arm64.b shard#8
  shard#9:
    arm64.loadRegBaseDisp.word64 x4, [x2 + 8]
    arm64.cmp x4, 0
    arm64.b.eq shard#15
  shard#14:
    arm64.loadRegBaseDisp.word64 x4, [x4 + 264]
    arm64.loadRegBaseDisp.word64 x5, [x4 + 312]
    arm64.add x5, x5, 1
    arm64.storeBaseDispReg.word64 [x4 + 312], x5
    arm64.loadRegBaseDisp.word64 x5, [x4 + 328]
    arm64.add x5, x5, x0
    arm64.storeBaseDispReg.word64 [x4 + 328], x5
  shard#15:
    arm64.loadRegBaseDisp.word64 x22, [x2 + 0]
    arm64.cmp x22, 0
    arm64.cset x4, eq
    arm64.add x23, x2, 592
    arm64.cbnz x4, critsplit#35
  shard#11:
    arm64.loadRegBaseDisp.word64 x2, [x22 + 32]
    arm64.cmp x2, 255
    arm64.b.ge critsplit#34
    arm64.b shard#12
  critsplit#35:
    arm64.movRegReg x22, x23
    arm64.b shard#8
  critsplit#34:
    arm64.movRegReg x20, x22
    arm64.movRegReg x22, x23
  shard#8:
    arm64.movImm x2, 1
    arm64.add x4, x3, 24
    arm64.arm64AtomicRmw.add x2, [x4], x2
    arm64.add x2, x3, 40
    arm64.arm64AtomicRmw.add x0, [x2], x0
    arm64.loadRegBaseDisp.word64 x0, [x22 + 0]
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x22 + 0], x0
    arm64.add x0, x1, 40
    arm64.bl mrt_linux_lock_enter
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
    arm64.movRegReg x4, x22
    arm64.b routed
  shard#12:
    arm64.movImm x16, 48
    arm64.mul x3, x2, x16
    arm64.add x4, x1, 139912
    arm64.add x3, x4, x3
    arm64.movImm x16, 544
    arm64.mul x2, x2, x16
    arm64.add x1, x1, x2
    arm64.movRegReg x2, x1
    arm64.movRegReg x1, x22
    arm64.b shard#7
  critsplit#32:
    arm64.movRegReg x1, x20
    arm64.movRegReg x2, x21
  shard#7:
    arm64.movImm x4, 0
    arm64.loadRegBaseDisp.word64 x5, [x3 + 24]
    arm64.add x5, x5, 1
    arm64.storeBaseDispReg.word64 [x3 + 24], x5
    arm64.loadRegBaseDisp.word64 x5, [x3 + 40]
    arm64.add x0, x5, x0
    arm64.storeBaseDispReg.word64 [x3 + 40], x0
  routed:
    arm64.add x0, x2, 648
    arm64.lsl x3, x19, 3
    arm64.add x5, x0, x3
    arm64.loadRegBaseDisp.word64 x6, [x5 + 0]
    arm64.cmp x6, 0
    arm64.b.eq pop#17
  pop#26:
    arm64.loadRegBaseDisp.word64 x0, [x6 + 16]
    arm64.cmp x0, 0
    arm64.b.eq pop#17
  pop#27:
    arm64.add x16, x6, 48
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, x1
    arm64.b.ne pop#17
  pop#18:
    arm64.leaRdata x0, __slab_class_geom
    arm64.add x0, x0, x3
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.movImm x1, 4294967295
    arm64.and x1, x0, x1
    arm64.loadRegBaseDisp.word64 x7, [x6 + 8]
    arm64.cmp x7, 0
    arm64.b.ne pop#19
  pop#20:
    arm64.loadRegBaseDisp.word64 x2, [x6 + 40]
    arm64.asr x0, x0, 32
    arm64.mul x0, x1, x0
    arm64.loadRegBaseDisp.word64 x3, [x6 + 0]
    arm64.add x0, x3, x0
    arm64.cmp x2, x0
    arm64.b.ge pop#22
  pop#21:
    arm64.add x0, x2, x1
    arm64.storeBaseDispReg.word64 [x6 + 40], x0
    arm64.movRegReg x0, x2
    arm64.b pop#23
  pop#19:
    arm64.loadRegBaseDisp.word64 x0, [x7 + 0]
    arm64.storeBaseDispReg.word64 [x6 + 8], x0
    arm64.movRegReg x0, x7
    arm64.arm64MemFill 0x00
    arm64.movRegReg x0, x7
  pop#23:
    arm64.loadRegBaseDisp.word64 x1, [x6 + 16]
    arm64.sub x1, x1, 1
    arm64.storeBaseDispReg.word64 [x6 + 16], x1
    arm64.cmp x1, 0
    arm64.b.ne pop#25
  pop#24:
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x5 + 0], x1
    arm64.movImm x1, 18446744073709551613
    arm64.add x16, x6, 48
    arm64.stlr.word64 [x16], x1
  pop#25:
    arm64.cmp x4, 0
    arm64.b.ne release
  handback:
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  state#5:
    arm64.storeSlotReg slot0, x0
    arm64.bl __slab_state_base
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x0, slot0
    arm64.b state#6
  pop#17:
    arm64.storeSlotReg slot1, x3
    arm64.storeSlotReg slot2, x4
    arm64.storeSlotReg slot3, x5
    arm64.movRegReg x0, x19
    arm64.bl __slab_refill
    arm64.loadRegSlot x3, slot1
    arm64.loadRegSlot x4, slot2
    arm64.loadRegSlot x5, slot3
    arm64.movRegReg x6, x0
    arm64.b pop#18
  osdirect:
    arm64.bl __slab_alloc_oversize
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  pop#22:
    arm64.leaRdata x0, __abort_msg_86  ; "fatal error: runtime abort 86 (slabSpanExhaustedPastItsEnd)\x0a"
    arm64.movImm x1, 60
    arm64.movImm x2, 86
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
  release:
    arm64.leaGlobal x1, __slab_state
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.add x1, x1, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot2, x4
    arm64.movRegReg x0, x1
    arm64.bl mrt_linux_lock_leave
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x4, slot2
    arm64.loadRegBaseDisp.word64 x1, [x4 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x4 + 0], x1
    arm64.loadRegSlot x19, slot4
    arm64.loadRegSlot x20, slot5
    arm64.loadRegSlot x21, slot6
    arm64.loadRegSlot x22, slot7
    arm64.loadRegSlot x23, slot8
    arm64.epilogue 96
    arm64.ret
}

func @__slab_free {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x20
    arm64.storeSlotReg slot5, x19
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __slab_arena_map_l1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq map#4
  map#1:
    arm64.asr x1, x19, 39
    arm64.and x1, x1, 511
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.eq map#4
  map#2:
    arm64.asr x1, x19, 26
    arm64.and x1, x1, 8191
    arm64.loadRegBaseIndexScale.word64 x0, [x0 + x1*8 + 0]
    arm64.cmp x0, 0
    arm64.b.ne map#3
  map#4:
    arm64.movImm x20, 0
    arm64.b map#5
  map#3:
    arm64.asr x1, x19, 13
    arm64.and x1, x1, 8191
    arm64.loadRegBaseIndexScale.word64 x20, [x0 + x1*8 + 0]
  map#5:
    arm64.cmp x20, 0
    arm64.b.eq osdirect
  tospan:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 24]
    arm64.movImm x2, 0
    arm64.add x3, x0, 152152
    arm64.cmp x1, 0
    arm64.b.eq critsplit#43
  shard#13:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq shard#8
  shard#9:
    arm64.loadRegBaseDisp.word64 x5, [x4 + 8]
    arm64.cmp x5, 0
    arm64.b.eq shard#15
  shard#14:
    arm64.loadRegBaseDisp.word64 x5, [x5 + 264]
    arm64.loadRegBaseDisp.word64 x6, [x5 + 320]
    arm64.add x6, x6, 1
    arm64.storeBaseDispReg.word64 [x5 + 320], x6
  shard#15:
    arm64.loadRegBaseDisp.word64 x4, [x4 + 0]
    arm64.cmp x4, 0
    arm64.b.eq shard#8
  shard#11:
    arm64.loadRegBaseDisp.word64 x2, [x4 + 32]
    arm64.cmp x2, 255
    arm64.b.lt shard#12
  critsplit#45:
    arm64.movRegReg x2, x4
  shard#8:
    arm64.movImm x0, 1
    arm64.add x3, x3, 32
    arm64.arm64AtomicRmw.add x0, [x3], x0
    arm64.b routed
  shard#12:
    arm64.movImm x16, 48
    arm64.mul x2, x2, x16
    arm64.add x0, x0, 139912
    arm64.add x0, x0, x2
    arm64.b shard#7
  critsplit#43:
    arm64.movRegReg x0, x3
    arm64.movRegReg x4, x2
  shard#7:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 32]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x0 + 32], x2
    arm64.movRegReg x2, x4
  routed:
    arm64.add x16, x20, 48
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 0
    arm64.b.ne notraw
  rawowned:
    arm64.cmp x1, 0
    arm64.b.ne rawlocked#18
    arm64.b local
  notraw:
    arm64.cmp x0, x2
    arm64.b.ne mine
  local:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.storeBaseDispReg.word64 [x20 + 8], x19
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.add x0, x0, 1
    arm64.storeBaseDispReg.word64 [x20 + 16], x0
  push:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  mine:
    arm64.cmp x0, -3
    arm64.b.eq uncached
  notdead:
    arm64.cmp x0, -2
    arm64.b.eq parked
  remote:
    arm64.movImm x1, 1
    arm64.add x3, x20, 64
    arm64.arm64AtomicRmw.add x1, [x3], x1
    arm64.cmp x2, 0
    arm64.cset x1, eq
    arm64.add x3, x20, 56
    arm64.cbnz x1, rpush
  rcacher:
    arm64.cmp x0, 0
    arm64.b.lt rpush
  rcredit:
    arm64.movImm x0, 1
    arm64.add x1, x2, 152
    arm64.arm64AtomicRmw.add x0, [x1], x0
  rpush:
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.arm64AtomicCas x0, [x3], x0, x19
    arm64.cmp x0, 0
    arm64.b.eq rpush
  rpushed:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  rawlocked#18:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, 32
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 0
    arm64.b.eq rawlocked#26
  rawlocked#28:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x4
    arm64.cmp x4, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq rawlocked#26
  rawlocked#27:
    arm64.add x3, x4, 592
  rawlocked#26:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x4, x4, 1
    arm64.storeBaseDispReg.word64 [x3 + 0], x4
    arm64.add x4, x0, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x4
    arm64.bl mrt_linux_lock_enter
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_raw
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x0, x0, 40
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.bl mrt_linux_lock_leave
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x3 + 0], x0
    arm64.cmp x4, 0
    arm64.b.eq routed
    arm64.b rawdone
  uncached:
    arm64.cmp x1, 0
    arm64.b.eq relistplain
  relistlock:
    arm64.leaGlobal x0, __slab_state
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x3, x0, 32
    arm64.loadRegBaseDisp.word64 x4, [x0 + 24]
    arm64.cmp x4, 0
    arm64.b.eq relist#33
  relist#35:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x4
    arm64.cmp x4, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x4, x16, x17
    arm64.cmp x4, 0
    arm64.b.eq relist#33
  relist#34:
    arm64.add x3, x4, 592
  relist#33:
    arm64.loadRegBaseDisp.word64 x4, [x3 + 0]
    arm64.add x4, x4, 1
    arm64.storeBaseDispReg.word64 [x3 + 0], x4
    arm64.add x4, x0, 40
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.movRegReg x0, x4
    arm64.bl mrt_linux_lock_enter
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_full
    arm64.movRegReg x4, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.add x0, x0, 40
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.storeSlotReg slot3, x3
    arm64.storeSlotReg slot4, x4
    arm64.bl mrt_linux_lock_leave
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.loadRegSlot x3, slot3
    arm64.loadRegSlot x4, slot4
    arm64.loadRegBaseDisp.word64 x0, [x3 + 0]
    arm64.add x0, x0, -1
    arm64.storeBaseDispReg.word64 [x3 + 0], x0
    arm64.b relistdone
  relistplain:
    arm64.storeSlotReg slot1, x1
    arm64.storeSlotReg slot2, x2
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __slab_free_to_full
    arm64.loadRegSlot x1, slot1
    arm64.loadRegSlot x2, slot2
    arm64.movRegReg x4, x0
  relistdone:
    arm64.cmp x4, 0
    arm64.b.eq routed
    arm64.b relistret
  osdirect:
    arm64.movRegReg x0, x19
    arm64.bl __slab_free_oversize
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  rawdone:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  relistret:
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
  parked:
    arm64.leaRdata x0, __abort_msg_89  ; "fatal error: runtime abort 89 (slabFreeOfParkedSpan)\x0a"
    arm64.movImm x1, 53
    arm64.movImm x2, 89
    arm64.bl __rt_abort
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot5
    arm64.loadRegSlot x20, slot6
    arm64.epilogue 80
    arm64.ret
}
```

<!-- test: error-ordinal-in-an-emitted-body -->
An emitted body transcribes an error enum's ORDINAL as a literal — `__managed_set`'s `rejcont` block
returns ordinal 0 with the error flag set, which is the wire format an `otherwise` arm decodes. The
ordinal is a positional fact about a case LIST, and nothing else in the suite can see the literal
the runtime actually carries: the run only sees that *some* error came back.

```maxon
function main() returns ExitCode
	var names = ["a", "b"]
	try names.set(1, value: "c") otherwise panic("set rejected a valid index")
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
__managed_set
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
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_3]  ; "a"
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_4]  ; "b"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.movRegImm32 rbx, 1
    x64.leaRegRdata rcx, [rip + __str_rec_5]  ; "c"
    x64.callDirect __str_retain
    x64.loadRegBaseDisp.word64 rax, [r12 + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow
  __im_owned:
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_store
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r8
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_store:
    x64.leaRegRegImm32 rax, rax, 8
    x64.storeBaseDispReg.word64 [rax + 0], r8
  tryok:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at error-ordinal-in-an-emitted-body.test:4: set rejected a valid index\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_set {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot1, r12
    x64.storeSlotReg slot0, rax
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r12, rcx
    x64.jcc below, store
  oob:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 40]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, rejcont
  rejchk:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, rejcont
  rejdrop:
    x64.movRegReg rcx, rax
    x64.callReg rdx
  rejcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  store:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.leaRegRegImm32 r15, r12, 1
    x64.loadRegBaseDisp.word64 rbx, [rbx + 40]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, dtorh
    x64.jmp mcont
  dtorb:
    x64.movRegReg rax, r12
    x64.imulRegReg rax, r12, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, dtorstep
  dtordo:
    x64.callReg rbx
  dtorstep:
    x64.leaRegRegImm32 r12, r12, 1
  dtorh:
    x64.cmpRegReg r12, r15
    x64.jcc less, dtorb
  mcont:
    x64.cmpRegImm32 r14, 8
    x64.jcc notEqual, esbyte
  stword:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.jmp stjoin
  esbyte:
    x64.cmpRegImm32 r14, 1
    x64.jcc notEqual, stother
  stbyte:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.byte [rax + 0], rcx
    x64.jmp stjoin
  stother:
    x64.cmpRegImm32 r14, 0
    x64.jcc greaterEqual, stby
  __il_body#25:
    x64.movRegReg rax, r14
    x64.sarRegImm8 rax, r14, 63
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, r14
    x64.shlRegImm8 r14, r14, 3
    x64.andRegReg rdx, rdx, rax
    x64.andRegReg r14, r14, rcx
    x64.orRegReg rdx, rdx, r14
  __il_cont#24:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, rdx
    x64.movRegReg rsi, rax
    x64.shrRegImm8 rsi, rax, 3
    x64.andRegImm32 rax, rax, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, rdx
    x64.movRegImm rdi, 18446744073709551615
    x64.shrRegCl rdi, rdi
    x64.leaRegRegReg rdx, rax, rdx
  __il_body#26:
    x64.leaRegRegImm32 rcx, rdx, 7
    x64.shrRegImm8 rcx, rcx, 3
  __il_cont#23:
    x64.leaRegRegReg rdx, r13, rsi
    x64.loadRegBaseDisp.byte rsi, [rdx + 0]
    x64.movRegReg rcx, rax
    x64.movRegReg r8, rdi
    x64.shlRegCl r8, rdi
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg rsi, rsi, r8
    x64.loadRegSlot r8, slot0
    x64.andRegReg r8, r8, rdi
    x64.movRegReg rcx, rax
    x64.shlRegCl r8, r8
    x64.orRegReg rsi, rsi, r8
    x64.storeBaseDispReg.byte [rdx + 0], rsi
    x64.jmp stjoin
  stby:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.movRegImm32 rdx, 0
  steh:
    x64.cmpRegReg rdx, r14
    x64.jcc less, steb
  stjoin:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  steb:
    x64.imulRegRegImm32 rcx, rdx, 8
    x64.loadRegSlot rsi, slot0
    x64.shrRegCl rsi, rsi
    x64.leaRegRegReg rdi, rax, rdx
    x64.storeBaseDispReg.byte [rdi + 0], rsi
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp steh
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
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_3]  ; "a"
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __str_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.movRegReg rcx, rbx
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_4]  ; "b"
    x64.callDirect __str_retain
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.movRegImm32 rbx, 1
    x64.leaRegRdata rcx, [rip + __str_rec_5]  ; "c"
    x64.callDirect __str_retain
    x64.loadRegBaseDisp.word64 rax, [r12 + 40]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __im_slow
  __im_owned:
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, 0
    x64.jcc less, __im_slow
  __im_bounds:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
  __im_buffer:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, __im_slow
  __im_viewed:
    x64.leaRegRegImm32 rcx, rax, -24
    x64.loadRegBaseDisp.word64 rcx, [rcx + 16]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, __im_store
  __im_slow:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r8
    x64.callDirect __managed_set
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
    x64.jmp tryok
  __im_store:
    x64.leaRegRegImm32 rax, rax, 8
    x64.storeBaseDispReg.word64 [rax + 0], r8
  tryok:
    x64.movRegImm32 rbx, 0
    x64.movRegReg rcx, r12
    x64.callDirect __managed_decref
    x64.movRegReg r8, rbx
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_6]  ; "panic at error-ordinal-in-an-emitted-body.test:4: set rejected a valid index\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_set {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.storeSlotReg slot1, r12
    x64.storeSlotReg slot0, rax
    x64.loadRegBaseDisp.word64 rcx, [rbx + 8]
    x64.cmpRegReg r12, rcx
    x64.jcc below, store
  oob:
    x64.loadRegBaseDisp.word64 rdx, [rbx + 40]
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, rejcont
  rejchk:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, rejcont
  rejdrop:
    x64.movRegReg rcx, rax
    x64.callReg rdx
  rejcont:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 1
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  store:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_cow_detach
    x64.loadRegBaseDisp.word64 r13, [rbx + 0]
    x64.loadRegBaseDisp.word64 r14, [rbx + 24]
    x64.leaRegRegImm32 r15, r12, 1
    x64.loadRegBaseDisp.word64 rbx, [rbx + 40]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, dtorh
    x64.jmp mcont
  dtorb:
    x64.movRegReg rax, r12
    x64.imulRegReg rax, r12, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, dtorstep
  dtordo:
    x64.callReg rbx
  dtorstep:
    x64.leaRegRegImm32 r12, r12, 1
  dtorh:
    x64.cmpRegReg r12, r15
    x64.jcc less, dtorb
  mcont:
    x64.cmpRegImm32 r14, 8
    x64.jcc notEqual, esbyte
  stword:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.jmp stjoin
  esbyte:
    x64.cmpRegImm32 r14, 1
    x64.jcc notEqual, stother
  stbyte:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.loadRegSlot rcx, slot0
    x64.storeBaseDispReg.byte [rax + 0], rcx
    x64.jmp stjoin
  stother:
    x64.cmpRegImm32 r14, 0
    x64.jcc greaterEqual, stby
  __il_body#25:
    x64.movRegReg rax, r14
    x64.sarRegImm8 rax, r14, 63
    x64.movRegReg rcx, rax
    x64.xorRegImm32 rcx, rax, -1
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, r14
    x64.shlRegImm8 r14, r14, 3
    x64.andRegReg rdx, rdx, rax
    x64.andRegReg r14, r14, rcx
    x64.orRegReg rdx, rdx, r14
  __il_cont#24:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, rdx
    x64.movRegReg rsi, rax
    x64.shrRegImm8 rsi, rax, 3
    x64.andRegImm32 rax, rax, 7
    x64.movRegImm32 rcx, 64
    x64.subRegReg rcx, rcx, rdx
    x64.movRegImm rdi, 18446744073709551615
    x64.shrRegCl rdi, rdi
    x64.leaRegRegReg rdx, rax, rdx
  __il_body#26:
    x64.leaRegRegImm32 rcx, rdx, 7
    x64.shrRegImm8 rcx, rcx, 3
  __il_cont#23:
    x64.leaRegRegReg rdx, r13, rsi
    x64.loadRegBaseDisp.byte rsi, [rdx + 0]
    x64.movRegReg rcx, rax
    x64.movRegReg r8, rdi
    x64.shlRegCl r8, rdi
    x64.xorRegImm32 r8, r8, -1
    x64.andRegReg rsi, rsi, r8
    x64.loadRegSlot r8, slot0
    x64.andRegReg r8, r8, rdi
    x64.movRegReg rcx, rax
    x64.shlRegCl r8, r8
    x64.orRegReg rsi, rsi, r8
    x64.storeBaseDispReg.byte [rdx + 0], rsi
    x64.jmp stjoin
  stby:
    x64.loadRegSlot rax, slot1
    x64.imulRegReg rax, rax, r14
    x64.leaRegRegReg rax, r13, rax
    x64.movRegImm32 rdx, 0
  steh:
    x64.cmpRegReg rdx, r14
    x64.jcc less, steb
  stjoin:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  steb:
    x64.imulRegRegImm32 rcx, rdx, 8
    x64.loadRegSlot rsi, slot0
    x64.shrRegCl rsi, rsi
    x64.leaRegRegReg rdi, rax, rdx
    x64.storeBaseDispReg.byte [rdi + 0], rsi
    x64.leaRegRegImm32 rdx, rdx, 1
    x64.jmp steh
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_3  ; "a"
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_4  ; "b"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x19, 1
    arm64.leaRdata x0, __str_rec_5  ; "c"
    arm64.bl __str_retain
    arm64.movRegReg x2, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow
  __im_owned:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.eq __im_store
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_store:
    arm64.add x0, x0, 8
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
  tryok:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_6  ; "panic at error-ordinal-in-an-emitted-body.test:4: set rejected a valid index\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_set {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.lo store
  oob:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 40]
    arm64.cmp x1, 0
    arm64.b.eq rejcont
  rejchk:
    arm64.cmp x21, 0
    arm64.b.eq rejcont
  rejdrop:
    arm64.movRegReg x0, x21
    arm64.blr x1
  rejcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  store:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x19 + 24]
    arm64.add x24, x20, 1
    arm64.loadRegBaseDisp.word64 x19, [x19 + 40]
    arm64.cmp x19, 0
    arm64.b.eq mcont
  mwalk:
    arm64.movRegReg x25, x20
    arm64.b dtorh
  dtorb:
    arm64.mul x0, x25, x23
    arm64.add x0, x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq dtorstep
  dtordo:
    arm64.blr x19
  dtorstep:
    arm64.add x25, x25, 1
  dtorh:
    arm64.cmp x25, x24
    arm64.b.lt dtorb
  mcont:
    arm64.cmp x23, 8
    arm64.b.ne esbyte
  stword:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.b stjoin
  esbyte:
    arm64.cmp x23, 1
    arm64.b.ne stother
  stbyte:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.storeBaseDispReg.byte [x0 + 0], x21
    arm64.b stjoin
  stother:
    arm64.cmp x23, 0
    arm64.b.ge stby
  __il_body#25:
    arm64.asr x0, x23, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.movImm x2, 0
    arm64.sub x2, x2, x23
    arm64.lsl x3, x23, 3
    arm64.and x0, x2, x0
    arm64.and x1, x3, x1
    arm64.orr x0, x0, x1
  __il_cont#24:
    arm64.mul x1, x20, x0
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 64
    arm64.sub x3, x3, x0
    arm64.movImm x4, 18446744073709551615
    arm64.lsrv x3, x4, x3
    arm64.add x0, x1, x0
  __il_body#26:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
  __il_cont#23:
    arm64.add x0, x22, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x4, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x2, x2, x4
    arm64.and x3, x21, x3
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b stjoin
  stby:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.movImm x1, 0
  steh:
    arm64.cmp x1, x23
    arm64.b.lt steb
  stjoin:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  steb:
    arm64.lsl x2, x1, 3
    arm64.lsrv x2, x21, x2
    arm64.add x3, x0, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x1, x1, 1
    arm64.b steh
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_3  ; "a"
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __str_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x19
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_4  ; "b"
    arm64.bl __str_retain
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.movImm x19, 1
    arm64.leaRdata x0, __str_rec_5  ; "c"
    arm64.bl __str_retain
    arm64.movRegReg x2, x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 40]
    arm64.cmp x0, 0
    arm64.b.ne __im_slow
  __im_owned:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 16]
    arm64.cmp x0, 0
    arm64.b.lt __im_slow
  __im_bounds:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_buffer:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.eq __im_slow
  __im_viewed:
    arm64.sub x1, x0, 24
    arm64.loadRegBaseDisp.word64 x1, [x1 + 16]
    arm64.cmp x1, 0
    arm64.b.eq __im_store
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_set
    arm64.cmp x9, 0
    arm64.b.ne tryerr
    arm64.b tryok
  __im_store:
    arm64.add x0, x0, 8
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
  tryok:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_6  ; "panic at error-ordinal-in-an-emitted-body.test:4: set rejected a valid index\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @__managed_set {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.movRegReg x21, x2
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x20, x0
    arm64.b.lo store
  oob:
    arm64.loadRegBaseDisp.word64 x1, [x19 + 40]
    arm64.cmp x1, 0
    arm64.b.eq rejcont
  rejchk:
    arm64.cmp x21, 0
    arm64.b.eq rejcont
  rejdrop:
    arm64.movRegReg x0, x21
    arm64.blr x1
  rejcont:
    arm64.movImm x0, 0
    arm64.movImm x9, 1
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  store:
    arm64.movRegReg x0, x19
    arm64.bl __managed_cow_detach
    arm64.loadRegBaseDisp.word64 x22, [x19 + 0]
    arm64.loadRegBaseDisp.word64 x23, [x19 + 24]
    arm64.add x24, x20, 1
    arm64.loadRegBaseDisp.word64 x19, [x19 + 40]
    arm64.cmp x19, 0
    arm64.b.eq mcont
  mwalk:
    arm64.movRegReg x25, x20
    arm64.b dtorh
  dtorb:
    arm64.mul x0, x25, x23
    arm64.add x0, x22, x0
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq dtorstep
  dtordo:
    arm64.blr x19
  dtorstep:
    arm64.add x25, x25, 1
  dtorh:
    arm64.cmp x25, x24
    arm64.b.lt dtorb
  mcont:
    arm64.cmp x23, 8
    arm64.b.ne esbyte
  stword:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.storeBaseDispReg.word64 [x0 + 0], x21
    arm64.b stjoin
  esbyte:
    arm64.cmp x23, 1
    arm64.b.ne stother
  stbyte:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.storeBaseDispReg.byte [x0 + 0], x21
    arm64.b stjoin
  stother:
    arm64.cmp x23, 0
    arm64.b.ge stby
  __il_body#25:
    arm64.asr x0, x23, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x0, x16
    arm64.movImm x2, 0
    arm64.sub x2, x2, x23
    arm64.lsl x3, x23, 3
    arm64.and x0, x2, x0
    arm64.and x1, x3, x1
    arm64.orr x0, x0, x1
  __il_cont#24:
    arm64.mul x1, x20, x0
    arm64.lsr x2, x1, 3
    arm64.and x1, x1, 7
    arm64.movImm x3, 64
    arm64.sub x3, x3, x0
    arm64.movImm x4, 18446744073709551615
    arm64.lsrv x3, x4, x3
    arm64.add x0, x1, x0
  __il_body#26:
    arm64.add x0, x0, 7
    arm64.lsr x0, x0, 3
  __il_cont#23:
    arm64.add x0, x22, x2
    arm64.loadRegBaseDisp.byte x2, [x0 + 0]
    arm64.lslv x4, x3, x1
    arm64.movImm x16, 18446744073709551615
    arm64.eor x4, x4, x16
    arm64.and x2, x2, x4
    arm64.and x3, x21, x3
    arm64.lslv x1, x3, x1
    arm64.orr x1, x2, x1
    arm64.storeBaseDispReg.byte [x0 + 0], x1
    arm64.b stjoin
  stby:
    arm64.mul x0, x20, x23
    arm64.add x0, x22, x0
    arm64.movImm x1, 0
  steh:
    arm64.cmp x1, x23
    arm64.b.lt steb
  stjoin:
    arm64.movImm x0, 0
    arm64.movImm x9, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  steb:
    arm64.lsl x2, x1, 3
    arm64.lsrv x2, x21, x2
    arm64.add x3, x0, x1
    arm64.storeBaseDispReg.byte [x3 + 0], x2
    arm64.add x1, x1, 1
    arm64.b steh
}
```

<!-- test: entry-stub-body -->
The `mrt_` band, from the other side of `isRuntimeFunction`'s two prefixes: the entry
stub every program has and no other case's `TargetIr` pin shows.

⭐⭐ **THE FIRST THING IT DOES IS INSTALL THE FAULT HANDLER** — `mrt_runtime_init`, ahead of any user code,
so a division that traps before `main` has run still reaches a diagnostic. The stub touches the CONSOLE not
at all: encoding is decided per WRITE, inside `mrt_write_stream`, which asks each standard stream whether
its handle is a console and converts to UTF-16 for the ones that are. Nothing process-wide is set, so a
program that never writes to a console changes nothing about the one it was launched from.

⚠ **THE STUB IS PINNED HERE BECAUSE NOTHING ELSE IN THE SUITE RENDERS IT** — every `TargetIr` pin withholds
the emitted runtime unless its case names it, and this case is the one that does.

⛔⛔ **AND THE CONSOLE ROAD ITSELF IS PINNED BY NOTHING, HERE OR ANYWHERE.** Every case's output is CAPTURED
— a pipe, never a console — so every suite run takes the NOT-A-CONSOLE road from end to end, and a case
could not ask which road it took in any event: a program cannot see which API carried its own writes. Which
MECHANISM an image was built with is answerable from the image's bytes and is gated in
`tests/console-write/`; whether the conversion is CORRECT is covered by hand against a real console, and by
nothing automated. Do not read a green suite as evidence about it.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_start
```

```TargetIr:x64-windows
data {
  __mrt_console_probe_stdin@0 = i8 0
  __mrt_console_probe_stdout@1 = i8 0
  __mrt_console_probe_stderr@2 = i8 0
  __mrt_program_started@3 = i8 0
}

func @mrt_start {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.callDirect mrt_runtime_init
    x64.callDirect main
    x64.movReg32Reg32 rcx, r8
    x64.iatCall 0
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
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

func @mrt_start {
  entry:
    x64.loadRegBaseDisp.word64 rax, [rsp + 0]
    x64.leaRegRegImm32 rax, rax, 2
    x64.leaRegBaseIndexScale rax, [rsp + rax*8]
    x64.leaRegGlobal rdx, __mrt_envp
    x64.storeBaseDispReg.word64 [rdx + 0], rax
    x64.callDirect mrt_runtime_init
    x64.callDirect main
    x64.movReg32Reg32 rdi, r8
    x64.x64Syscall 231
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.movRegImm32 r8, 0
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
    arm64.epilogue 16
    arm64.ret
}

chunk @mrt_start {
    0000: 00 00 00 94 00 00 00 94 10 00 00 90 10 02 40 f9
    0010: 00 02 3f d6 00 00 20 d4
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
    arm64.epilogue 16
    arm64.ret
}

chunk @mrt_start {
    0000: f1 03 00 91 20 02 40 f9 00 08 00 91 00 f0 7d d3
    0010: 20 02 00 8b 10 00 00 90 10 02 00 91 00 02 00 f9
    0020: 00 00 00 94 00 00 00 94 c8 0b 80 d2 01 00 00 d4
    0030: 00 00 20 d4
}
```

<!-- test: panic-runtime-chunk -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE ASSERTION IS EMITTED x64 TEXT.** The ```RequiredRuntime body below is this backend's instruction
sequence; arm64 emits its own, so the pin is meaningless there rather than merely different.
The panic runtime is HAND-ASSEMBLED bytes, not `TargetOp`s — so it has no IR body, is skipped
by every rendering that does not name it, and has no other gate of any kind: not an exit code (a program
that never panics never enters it), not the leak gate, not a section pin, not a body pin. It is
installed in EVERY x64 program, so the smallest possible one pins it. The zero
displacements are the unresolved call fixups the linker fills in; see the note above.

```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```
```RequiredRuntime
mrt_panic
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
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
}

chunk @mrt_panic {
    0000: 55 48 89 e5 48 83 ec 20 e8 00 00 00 00 48 8d 0d
    0010: 28 00 00 00 e8 00 00 00 00 48 89 e9 48 89 ea 41
    0020: b8 64 00 00 00 e8 00 00 00 00 b9 01 00 00 00 ff
    0030: 15 00 00 00 00 48 83 c4 20 5d c3 00 53 74 61 63
    0040: 6b 20 74 72 61 63 65 3a 0a 00
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
    x64.movRegImm32 r8, 0
    x64.popReg rbp
    x64.ret
}

chunk @mrt_panic {
    0000: 55 48 89 e5 48 83 ec 20 e8 00 00 00 00 48 8d 0d
    0010: 28 00 00 00 e8 00 00 00 00 48 89 e9 48 89 ea 41
    0020: b8 64 00 00 00 e8 00 00 00 00 bf 01 00 00 00 b8
    0030: e7 00 00 00 0f 05 48 83 c4 20 5d c3 53 74 61 63
    0040: 6b 20 74 72 61 63 65 3a 0a 00
}
```

<!-- test: stack-growth-chunk -->
<!-- unsupported-targets: arm64-macos, arm64-linux -->
⚠ **THE ASSERTION IS EMITTED x64 TEXT**, as `panic-runtime-chunk`'s: the grower's arm64 body is a
different instruction sequence, so this pin does not describe it.
`__gt_morestack` — the relocating stack grower every green thread's prologue calls. It is installed on demand, so
the program has to actually run a green thread. Gated to x64-windows for
`async-stack-growth.md`'s reason: the grower is hand-written x64 assembly over a
`VirtualAlloc`ed stack.

```maxon
function deepRecurse(n Integer) returns Integer
	Scheduler.yield()
	if n == 0 'base'
		return 0
	end 'base'
	return deepRecurse(n - 1) + 1
end 'deepRecurse'

function main() returns ExitCode
	let p = async deepRecurse(200)
	let r = await p
	return r as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
200
```
```RequiredRuntime
__gt_morestack
```

```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @deepRecurse {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
  __il_body:
    x64.callDirect __gt_resched
  __il_cont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, ifcont
  base:
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rcx, rbx, -1
    x64.callDirect deepRecurse
    x64.leaRegRegImm32 r8, r8, 1
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegImm32 rbx, 200
    x64.leaRegFunc rcx, [rip + deepRecurse]
    x64.callDirect __gt_spawn
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 208], rbx
    x64.movRegReg rcx, r12
    x64.callDirect __gt_ready
    x64.movRegReg rcx, r12
    x64.callDirect __gt_await
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at stack-growth-chunk.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @__gt_morestack {
    0000: 49 89 62 f8 49 8d 62 f8 55 53 41 54 41 55 41 56
    0010: 41 57 51 52 50 41 51 56 57 48 83 ec 58 f2 0f 11
    0020: 44 24 20 f2 0f 11 4c 24 28 f2 0f 11 54 24 30 f2
    0030: 0f 11 5c 24 38 f2 0f 11 64 24 40 f2 0f 11 6c 24
    0040: 48 4c 89 db 65 4c 89 14 25 08 00 00 00 49 81 ea
    0050: 00 00 01 00 65 4c 89 14 25 10 00 00 00 65 4c 89
    0060: 14 25 78 14 00 00 4c 8b 63 18 4c 8b 6b 20 4d 89
    0070: e7 4d 01 ef 4d 29 c7 49 81 c7 a0 23 00 00 4d 39
    0080: ef 0f 86 dc 00 00 00 4d 89 ee 4d 01 f6 49 81 fe
    0090: 00 00 00 40 0f 87 d6 00 00 00 4d 39 fe 0f 82 e7
    00a0: ff ff ff 4c 89 f1 e8 00 00 00 00 4d 85 c0 0f 8e
    00b0: d5 00 00 00 4d 89 c7 48 8b 8c 24 b8 00 00 00 48
    00c0: 89 0b 48 89 6b 08 48 89 d9 4c 89 fa 4c 89 f0 e8
    00d0: 00 00 00 00 b9 03 00 00 00 48 89 8b 88 01 00 00
    00e0: 48 8b 4b 08 48 89 8c 24 b0 00 00 00 4c 8b 13 48
    00f0: 8b 8b 90 00 00 00 65 48 89 0c 25 08 00 00 00 48
    0100: 8b 8b 98 00 00 00 65 48 89 0c 25 10 00 00 00 48
    0110: 8b 8b c8 00 00 00 65 48 89 0c 25 78 14 00 00 f2
    0120: 0f 10 44 24 20 f2 0f 10 4c 24 28 f2 0f 10 54 24
    0130: 30 f2 0f 10 5c 24 38 f2 0f 10 64 24 40 f2 0f 10
    0140: 6c 24 48 48 83 c4 58 5f 5e 41 59 58 5a 59 41 5f
    0150: 41 5e 41 5d 41 5c 5b 5d 48 81 c4 08 00 00 00 4c
    0160: 89 d4 c3 4c 8b 94 24 b8 00 00 00 e9 7f ff ff ff
    0170: 48 8b 84 24 b8 00 00 00 48 8b 08 48 89 28 48 89
    0180: c2 49 89 c0 e8 00 00 00 00 b9 63 00 00 00 e8 00
    0190: 00 00 00
}
```

```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @deepRecurse {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
  __il_body:
    x64.callDirect __gt_resched
  __il_cont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, ifcont
  base:
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  ifcont:
    x64.leaRegRegImm32 rcx, rbx, -1
    x64.callDirect deepRecurse
    x64.leaRegRegImm32 r8, r8, 1
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegImm32 rbx, 200
    x64.leaRegFunc rcx, [rip + deepRecurse]
    x64.callDirect __gt_spawn
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 208], rbx
    x64.movRegReg rcx, r12
    x64.callDirect __gt_ready
    x64.movRegReg rcx, r12
    x64.callDirect __gt_await
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at stack-growth-chunk.test:13: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @__gt_morestack {
    0000: 49 89 62 f8 49 8d 62 f8 55 53 41 54 41 55 41 56
    0010: 41 57 51 52 50 41 51 56 57 48 83 ec 58 f2 0f 11
    0020: 44 24 20 f2 0f 11 4c 24 28 f2 0f 11 54 24 30 f2
    0030: 0f 11 5c 24 38 f2 0f 11 64 24 40 f2 0f 11 6c 24
    0040: 48 4c 89 db 4c 8b 63 18 4c 8b 6b 20 4d 89 e7 4d
    0050: 01 ef 4d 29 c7 49 81 c7 a0 03 00 00 4d 39 ef 0f
    0060: 86 ac 00 00 00 4d 89 ee 4d 01 f6 49 81 fe 00 00
    0070: 00 40 0f 87 a6 00 00 00 4d 39 fe 0f 82 e7 ff ff
    0080: ff 4c 89 f1 e8 00 00 00 00 4d 85 c0 0f 8e a5 00
    0090: 00 00 4d 89 c7 48 8b 8c 24 b8 00 00 00 48 89 0b
    00a0: 48 89 6b 08 48 89 d9 4c 89 fa 4c 89 f0 e8 00 00
    00b0: 00 00 b9 03 00 00 00 48 89 8b 88 01 00 00 48 8b
    00c0: 4b 08 48 89 8c 24 b0 00 00 00 4c 8b 13 f2 0f 10
    00d0: 44 24 20 f2 0f 10 4c 24 28 f2 0f 10 54 24 30 f2
    00e0: 0f 10 5c 24 38 f2 0f 10 64 24 40 f2 0f 10 6c 24
    00f0: 48 48 83 c4 58 5f 5e 41 59 58 5a 59 41 5f 41 5e
    0100: 41 5d 41 5c 5b 5d 48 81 c4 08 00 00 00 4c 89 d4
    0110: c3 4c 8b 94 24 b8 00 00 00 e9 af ff ff ff 48 8b
    0120: 84 24 b8 00 00 00 48 8b 08 48 89 28 48 89 c2 49
    0130: 89 c0 e8 00 00 00 00 bf 63 00 00 00 b8 e7 00 00
    0140: 00 0f 05
}
```

<!-- test: linux-lock-enter-makes-no-kernel-call-uncontended -->
<!-- unsupported-targets: x64-windows, arm64-macos -->
**THE UNCONTENDED HOST-LOCK ENTER MAKES NO KERNEL CALL.** `mrt_linux_lock_enter` is hand-assembled bytes,
so this chunk pin is its only gate. The owner token it compares and stores is the address of the calling
thread's own thread-pointer block, read in one instruction: x64 loads the block's self-address slot,
`mov rax, fs:[4088]`, and arm64 reads `mrs x0, tpidr_el0`.

⭐ **A CONTENDED ENTER SPINS BEFORE IT SLEEPS, AS GO'S `lock2` DOES.** A runtime lock is held for a few
instructions, so a thread that finds it held is likelier to see it freed by spinning than by a futex
round trip, which costs the waiter a sleep and the releaser a wake. After the first compare-and-swap fails,
the enter runs a bounded active spin, retrying the acquire between bursts of `pause` (x64 `f3 90`) or
`yield` (arm64 `3f 20 03 d5`), then a bounded passive spin of `sched_yield` (x64 `b8 18 00 00 00 0f 05`,
syscall 24; arm64 `88 0f 80 d2 01 00 00 d4`, syscall 124), and only then marks the word contended and
waits on the futex — x64 `b8 ca 00 00 00 0f 05`, arm64 `48 0c 80 d2 01 00 00 d4`. Every spin is bounded, so
a holder that does not release soon still costs the waiter one sleep rather than a core.

Those are the chunk's only `syscall` / `svc` instructions, and both are on the contended path. Any other
kernel call in these bytes, or either of these on the uncontended path, is a per-enter cost every lock
round trip pays.

The program pushes one element and spawns nothing, so it runs no scheduler: the lock still reads its token
from the main thread's block, which every Linux thread owns from process start.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var buf = ByteArray.create()
	buf.push(1)
	return buf.count() as ExitCode
end 'main'
```
```exitcode
1
```
```RequiredRuntime
mrt_linux_lock_enter
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
    x64.prologue 32
    x64.movRegImm32 rcx, 1
    x64.movRegImm32 rdx, 0
    x64.callDirect __managed_create
    x64.movRegReg rbx, r8
    x64.movRegImm32 rdx, 1
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_push
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __managed_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at linux-lock-enter-makes-no-kernel-call-uncontended.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

chunk @mrt_linux_lock_enter {
    0000: 55 48 89 e5 48 83 ec 40 48 89 4d f8 64 48 8b 04
    0010: 25 f8 0f 00 00 48 89 45 f0 48 8b 4d f8 48 8b 51
    0020: 08 48 39 c2 0f 85 14 00 00 00 48 8b 51 10 48 81
    0030: c2 01 00 00 00 48 89 51 10 e9 0f 01 00 00 48 8b
    0040: 7d f8 b8 00 00 00 00 b9 01 00 00 00 f0 48 0f b1
    0050: 0f 0f 84 e1 00 00 00 b8 04 00 00 00 48 89 45 e8
    0060: ba 1e 00 00 00 f3 90 48 81 ea 01 00 00 00 0f 85
    0070: f1 ff ff ff 48 8b 7d f8 b8 00 00 00 00 b9 01 00
    0080: 00 00 f0 48 0f b1 0f 0f 84 ab 00 00 00 48 8b 45
    0090: e8 48 81 e8 01 00 00 00 48 89 45 e8 0f 85 be ff
    00a0: ff ff b8 01 00 00 00 48 89 45 e8 b8 18 00 00 00
    00b0: 0f 05 48 8b 7d f8 b8 00 00 00 00 b9 01 00 00 00
    00c0: f0 48 0f b1 0f 0f 84 6d 00 00 00 48 8b 45 e8 48
    00d0: 81 e8 01 00 00 00 48 89 45 e8 0f 85 cb ff ff ff
    00e0: 48 8b 7d f8 b8 00 00 00 00 b9 02 00 00 00 f0 48
    00f0: 0f b1 0f 0f 84 3f 00 00 00 48 8b 7d f8 b8 01 00
    0100: 00 00 b9 02 00 00 00 f0 48 0f b1 0f 48 8b 7d f8
    0110: be 80 00 00 00 ba 02 00 00 00 41 ba 00 00 00 00
    0120: 41 b8 00 00 00 00 41 b9 00 00 00 00 b8 ca 00 00
    0130: 00 0f 05 e9 a8 ff ff ff 48 8b 4d f8 48 8b 55 f0
    0140: 48 89 51 08 ba 01 00 00 00 48 89 51 10 48 83 c4
    0150: 40 5d c3
}
```

```TargetIr:arm64-linux
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
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movImm x0, 1
    arm64.movImm x1, 0
    arm64.bl __managed_create
    arm64.movRegReg x19, x0
    arm64.movImm x1, 1
    arm64.movRegReg x0, x19
    arm64.bl __managed_push
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __managed_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at linux-lock-enter-makes-no-kernel-call-uncontended.test:8: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

chunk @mrt_linux_lock_enter {
    0000: fd 7b bd a9 fd 03 00 91 a0 0b 00 f9 40 d0 3b d5
    0010: a0 0f 00 f9 a1 0b 40 f9 22 04 40 f9 5f 00 00 eb
    0020: a1 00 00 54 23 08 40 f9 63 04 00 91 23 08 00 f9
    0030: 52 00 00 14 a0 0b 40 f9 e1 03 1f aa 22 00 80 d2
    0040: 10 fc 5f c8 1f 02 01 eb 61 00 00 54 02 fc 11 c8
    0050: 91 ff ff b5 e5 17 9f 9a 65 08 00 b5 84 00 80 d2
    0060: a4 13 00 f9 c3 03 80 d2 3f 20 03 d5 63 04 00 d1
    0070: c3 ff ff b5 a0 0b 40 f9 e1 03 1f aa 22 00 80 d2
    0080: 10 fc 5f c8 1f 02 01 eb 61 00 00 54 02 fc 11 c8
    0090: 91 ff ff b5 e5 17 9f 9a 65 06 00 b5 a4 13 40 f9
    00a0: 84 04 00 d1 a4 13 00 f9 e4 fd ff b5 24 00 80 d2
    00b0: a4 13 00 f9 88 0f 80 d2 01 00 00 d4 a0 0b 40 f9
    00c0: e1 03 1f aa 22 00 80 d2 10 fc 5f c8 1f 02 01 eb
    00d0: 61 00 00 54 02 fc 11 c8 91 ff ff b5 e5 17 9f 9a
    00e0: 25 04 00 b5 a4 13 40 f9 84 04 00 d1 a4 13 00 f9
    00f0: 24 fe ff b5 a0 0b 40 f9 e1 03 1f aa 42 00 80 d2
    0100: 10 fc 5f c8 1f 02 01 eb 61 00 00 54 02 fc 11 c8
    0110: 91 ff ff b5 e5 17 9f 9a 65 02 00 b5 a0 0b 40 f9
    0120: 21 00 80 d2 42 00 80 d2 10 fc 5f c8 1f 02 01 eb
    0130: 61 00 00 54 02 fc 11 c8 91 ff ff b5 e5 17 9f 9a
    0140: a0 0b 40 f9 01 10 80 d2 42 00 80 d2 e3 03 1f aa
    0150: e4 03 1f aa e5 03 1f aa 48 0c 80 d2 01 00 00 d4
    0160: e5 ff ff 17 a1 0b 40 f9 a2 0f 40 f9 22 04 00 f9
    0170: 22 00 80 d2 22 08 00 f9 fd 7b c3 a8 c0 03 5f d6
}
```

<!-- test: a-library-body-named-into-the-golden -->
The LIBRARY half of the same block. `String.trim` is `stdlib/String.maxon`'s own code — withheld
from every rendering by default, exactly as `mrt_start` is — and this is the route that puts one
body back. The case is this door's only gate: withdraw the stdlib half of
`TargetPrinter.isSuppliedFunction` and the block stops pinning anything new (the rendering would
show `String.trim` anyway, and the compiler refuses a name it already shows); withdraw the
NAMING half and the compiler refuses the block outright. Either way this case goes red, which is
what a case that would otherwise only assert an exit code cannot do.

⚠ Its zero-argument sibling `String.trim#` — the overload that supplies
`CharacterSet.whitespacesAndNewlines()` — is what `main` actually calls, and it is NOT named here:
one body per name, and the pin shows the two-argument scan this pins rather than the forwarder.

```maxon
function main() returns ExitCode
	let s = "  abc  "
	return s.trim().byteLength() as ExitCode
end 'main'
```
```exitcode
3
```
```RequiredRuntime
String.trim
```

```TargetIr:x64-windows
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_console_probe_stdin@32 = i8 0
  __mrt_console_probe_stdout@33 = i8 0
  __mrt_console_probe_stderr@34 = i8 0
  __mrt_program_started@35 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_12]  ; "  abc  "
  __il_body#7:
    x64.leaRegGlobal rax, __data_CharacterSet.cachedWhitespacesAndNewlines
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegFunc rdx, [rip + __clone_CharacterSet]
    x64.callDirect __mm_own_deep
    x64.movRegReg r12, r8
  __il_cont#6:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect String.trim
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_CharacterSet
  __il_body#9:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont#8:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r12, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_70]  ; "panic at a-library-body-named-into-the-golden.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @String.trim {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rdx
  __il_body#136:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.storeSlotReg slot0, rax
  __il_cont#135:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_body#140
  __il_body#6:
    x64.movRegImm32 rdx, 0
  __il_body#138:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
  __il_cont#137:
    x64.loadRegBaseDisp.byte r9, [rbx + 48]
    x64.movRegReg rcx, rbx
    x64.callDirect String.sliceBytes
  __il_cont#5:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#140:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont#139:
    x64.movRegImm32 r13, 0
    x64.jmp whilehdr#18
  __il_body#142:
    x64.loadRegBaseDisp.word64 r14, [rbx + 8]
  __il_cont#141:
    x64.cmpRegReg r13, r14
    x64.jcc aboveEqual, atEnd
  ifcont#28:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#29
  tryerr#30:
    x64.leaRegRdata rcx, [rip + __str_blob_49]  ; "panic at grapheme.maxon:56: grapheme: firstByte OOB \xe2\x80\x94 caller guarantees startPos < len\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#29:
    x64.cmpRegImm32 r8, 128
    x64.jcc greaterEqual, ifcont#54
  isAscii:
    x64.cmpRegImm32 r8, 13
    x64.jcc notEqual, ifcont#39
  isCR:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.cmpRegReg rdx, r14
    x64.jcc aboveEqual, ifcont#39
  hasNext:
    x64.movRegReg rcx, rbx
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#34
  tryerr#35:
    x64.leaRegRdata rcx, [rip + __str_blob_50]  ; "panic at grapheme.maxon:63: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#34:
    x64.cmpRegImm32 r8, 10
    x64.jcc notEqual, ifcont#39
  isCRLF:
    x64.leaRegRegImm32 r13, r13, 2
    x64.jmp __il_cont#25
  ifcont#39:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.cmpRegReg rdx, r14
    x64.jcc aboveEqual, ifelse#52
  hasNextByte:
    x64.movRegReg rcx, rbx
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#41
  tryerr#42:
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at grapheme.maxon:75: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#41:
    x64.cmpRegImm32 r8, 128
    x64.jcc less, ifelse#50
  nextIsMultibyte:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect utf8DecodeAt
    x64.movRegReg rcx, r8
    x64.callDirect graphemeBreakProperty
    x64.cmpRegImm32 r8, 4
    x64.movRegImm32 rax, 0
    x64.jcc equal, scmerge#45
  andrhs#44:
    x64.cmpRegImm32 r8, 5
    x64.setccReg notEqual, rax
  scmerge#45:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#148
  andrhs#46:
    x64.cmpRegImm32 r8, 8
    x64.setccReg notEqual, rax
    x64.jmp scmerge#47
  critsplit#148:
    x64.movRegReg rax, rcx
  scmerge#47:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#54
  notCombining:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp __il_cont#25
  ifelse#50:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp __il_cont#25
  ifcont#54:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect utf8DecodeAt
    x64.movRegReg r15, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect utf8ByteLengthAt
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot7, rax
    x64.movRegReg rcx, r15
    x64.callDirect graphemeBreakProperty
    x64.movRegReg r13, r8
  __il_body#61:
    x64.movRegImm32 rcx, 0
  __il_cont#60:
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r15
    x64.callDirect graphemeStateUpdate
    x64.loadRegSlot rdx, slot7
    x64.jmp whilehdr#55
  scan#56:
    x64.movRegReg rcx, rbx
    x64.callDirect utf8DecodeAt
    x64.movRegReg r15, r8
    x64.storeSlotReg slot4, r15
    x64.movRegReg rcx, r15
    x64.callDirect graphemeBreakProperty
    x64.storeSlotReg slot5, r8
  __il_body#63:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic#125
  __rc_chk#126:
    x64.cmpRegImm32 r13, 13
    x64.jcc greater, __rc_panic#125
  __rc_ok#124:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic#128
  __rc_chk#129:
    x64.cmpRegImm32 r8, 13
    x64.jcc greater, __rc_panic#128
  __rc_ok#127:
    x64.cmpRegImm32 r15, 0
    x64.jcc less, __rc_panic#131
  __rc_chk#132:
    x64.cmpRegImm32 r15, 1114111
    x64.jcc greater, __rc_panic#131
  __rc_ok#130:
    x64.cmpRegImm32 r13, 1
    x64.setccReg equal, rax
    x64.movRegImm32 rcx, 0
    x64.jcc notEqual, scmerge#65
  andrhs#64:
    x64.cmpRegImm32 r8, 2
    x64.setccReg equal, rcx
  scmerge#65:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont#67
  gb3:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#67:
    x64.cmpRegImm32 r13, 3
    x64.movRegImm32 rcx, 1
    x64.jcc equal, scmerge#69
  orrhs#68:
    x64.movRegReg rcx, rax
  scmerge#69:
    x64.movRegImm32 rax, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, scmerge#71
  orrhs#70:
    x64.cmpRegImm32 r13, 2
    x64.setccReg equal, rax
  scmerge#71:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#73
  gb4:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#62
  ifcont#73:
    x64.cmpRegImm32 r8, 3
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#75
  orrhs#74:
    x64.cmpRegImm32 r8, 1
    x64.setccReg equal, rax
  scmerge#75:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#152
  orrhs#76:
    x64.cmpRegImm32 r8, 2
    x64.setccReg equal, rax
    x64.jmp scmerge#77
  critsplit#152:
    x64.movRegReg rax, rcx
  scmerge#77:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#79
  gb5:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#62
  ifcont#79:
    x64.cmpRegImm32 r13, 9
    x64.jcc notEqual, ifcont#89
  gb6:
    x64.cmpRegImm32 r8, 9
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#82
  orrhs#81:
    x64.cmpRegImm32 r8, 10
    x64.setccReg equal, rax
  scmerge#82:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#154
  orrhs#83:
    x64.cmpRegImm32 r8, 12
    x64.setccReg equal, rax
    x64.jmp scmerge#84
  critsplit#154:
    x64.movRegReg rax, rcx
  scmerge#84:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#155
  orrhs#85:
    x64.cmpRegImm32 r8, 13
    x64.setccReg equal, rax
    x64.jmp scmerge#86
  critsplit#155:
    x64.movRegReg rax, rcx
  scmerge#86:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#89
  gb6_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#89:
    x64.cmpRegImm32 r13, 12
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#91
  orrhs#90:
    x64.cmpRegImm32 r13, 10
    x64.setccReg equal, rax
  scmerge#91:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#97
  gb7:
    x64.cmpRegImm32 r8, 10
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#94
  orrhs#93:
    x64.cmpRegImm32 r8, 11
    x64.setccReg equal, rax
  scmerge#94:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#97
  gb7_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#97:
    x64.cmpRegImm32 r13, 13
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#99
  orrhs#98:
    x64.cmpRegImm32 r13, 11
    x64.setccReg equal, rax
  scmerge#99:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#103
  gb8:
    x64.cmpRegImm32 r8, 11
    x64.jcc notEqual, ifcont#103
  gb8_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#103:
    x64.cmpRegImm32 r8, 4
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#105
  orrhs#104:
    x64.cmpRegImm32 r8, 5
    x64.setccReg equal, rax
  scmerge#105:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#107
  gb9:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#107:
    x64.cmpRegImm32 r8, 8
    x64.jcc notEqual, ifcont#109
  gb9a:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#109:
    x64.cmpRegImm32 r13, 7
    x64.jcc notEqual, ifcont#111
  gb9b:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#111:
    x64.cmpRegImm32 r13, 5
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, critsplit#160
  andrhs#112:
    x64.movRegReg rcx, r15
    x64.callDirect isExtendedPictographic
    x64.jmp scmerge#113
  critsplit#160:
    x64.movRegReg r8, rax
  scmerge#113:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#117
  __il_body#144:
    x64.loadRegSlot rax, slot6
    x64.andRegImm32 rax, rax, 1
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
  __il_cont#143:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#117
  gb11_check:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#117:
    x64.cmpRegImm32 r13, 6
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#119
  andrhs#118:
    x64.loadRegSlot rax, slot5
    x64.cmpRegImm32 rax, 6
    x64.setccReg equal, rax
  scmerge#119:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#123
  __il_body#134:
    x64.loadRegSlot rax, slot6
    x64.shrRegImm8 rax, rax, 1
    x64.andRegImm32 rax, rax, 1
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
  __il_cont#133:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#123
  gb12_odd:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#123:
    x64.movRegImm32 rax, 1
  __il_cont#62:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, breakFound
  ifcont#59:
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rcx, slot6
    x64.callDirect graphemeStateUpdate
    x64.movRegReg r13, r8
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect utf8ByteLengthAt
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rdx, rax, r8
    x64.loadRegSlot rax, slot5
    x64.movRegReg r8, r13
    x64.movRegReg r13, rax
  whilehdr#55:
    x64.storeSlotReg slot6, r8
    x64.storeSlotReg slot3, rdx
    x64.cmpRegReg rdx, r14
    x64.jcc below, scan#56
    x64.jmp whileexit
  atEnd:
    x64.loadRegSlot r13, slot2
    x64.jmp __il_cont#25
  ifelse#52:
    x64.movRegReg r13, rdx
    x64.jmp __il_cont#25
  breakFound:
    x64.loadRegSlot r13, slot3
    x64.jmp __il_cont#25
  whileexit:
    x64.loadRegSlot r13, slot3
  __il_cont#25:
    x64.loadRegSlot rax, slot2
    x64.movRegReg rsi, r13
    x64.subRegReg rsi, r13, rax
    x64.movRegReg rcx, r13
    x64.xorRegImm32 rcx, r13, -1
    x64.andRegReg rcx, rcx, rax
    x64.movRegReg rdx, r13
    x64.xorRegReg rdx, r13, rax
    x64.xorRegImm32 rdx, rdx, -1
    x64.andRegReg rdx, rdx, rsi
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#24
  __rc_ok#23:
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, rsi
    x64.callDirect String.makeCharFromBytes
    x64.movRegReg r14, r8
    x64.loadRegSlot rcx, slot1
    x64.movRegReg rdx, r14
    x64.callDirect CharacterSet.contains
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#22
  found#21:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp __il_cont#16
  ifcont#22:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
  whilehdr#18:
    x64.storeSlotReg slot2, r13
    x64.cmpRegReg r13, r12
    x64.jcc below, __il_body#142
  __il_cont#16:
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.cmpRegReg rcx, rax
    x64.jcc below, __il_body#8
  allMatch:
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 1
    x64.movRegReg rcx, rbx
    x64.callDirect String.sliceBytes
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#8:
    x64.loadRegSlot r12, slot0
    x64.jmp whilehdr#9
  scan#10:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect findGraphemeStart
    x64.movRegReg r13, r8
    x64.movRegReg rax, r12
    x64.subRegReg rax, r12, r13
    x64.movRegReg rcx, r12
    x64.xorRegImm32 rcx, r12, -1
    x64.andRegReg rcx, rcx, r13
    x64.movRegReg rdx, r12
    x64.xorRegReg rdx, r12, r13
    x64.xorRegImm32 rdx, rdx, -1
    x64.andRegReg rdx, rdx, rax
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#15
  __rc_ok#14:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect String.makeCharFromBytes
    x64.movRegReg r14, r8
    x64.loadRegSlot rcx, slot1
    x64.movRegReg rdx, r14
    x64.callDirect CharacterSet.contains
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#13
  found#12:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp __il_cont#7
  ifcont#13:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r12, r13
  whilehdr#9:
    x64.loadRegSlot rax, slot2
    x64.cmpRegReg r12, rax
    x64.jcc above, scan#10
  __il_cont#7:
    x64.loadRegBaseDisp.byte r9, [rbx + 48]
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect String.sliceBytes
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#125:
    x64.leaRegRdata rcx, [rip + __str_blob_104]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#128:
    x64.leaRegRdata rcx, [rip + __str_blob_104]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#131:
    x64.leaRegRdata rcx, [rip + __str_blob_105]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'Codepoint'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#24:
    x64.leaRegRdata rcx, [rip + __str_blob_95]  ; "panic at String.maxon:741: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#15:
    x64.leaRegRdata rcx, [rip + __str_blob_96]  ; "panic at String.maxon:761: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_12]  ; "  abc  "
  __il_body#7:
    x64.leaRegGlobal rax, __data_CharacterSet.cachedWhitespacesAndNewlines
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.leaRegFunc rdx, [rip + __clone_CharacterSet]
    x64.callDirect __mm_own_deep
    x64.movRegReg r12, r8
  __il_cont#6:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect String.trim
    x64.movRegReg rbx, r8
    x64.movRegReg rcx, r12
    x64.callDirect __destruct_CharacterSet
  __il_body#9:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont#8:
    x64.cmpRegImm32 r12, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r12, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, rbx
    x64.callDirect __str_decref
    x64.movRegReg r8, r12
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_70]  ; "panic at a-library-body-named-into-the-golden.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @String.trim {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 104
    x64.movRegReg rbx, rcx
    x64.storeSlotReg slot1, rdx
  __il_body#136:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.storeSlotReg slot0, rax
  __il_cont#135:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, __il_body#140
  __il_body#6:
    x64.movRegImm32 rdx, 0
  __il_body#138:
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
  __il_cont#137:
    x64.loadRegBaseDisp.byte r9, [rbx + 48]
    x64.movRegReg rcx, rbx
    x64.callDirect String.sliceBytes
  __il_cont#5:
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#140:
    x64.loadRegBaseDisp.word64 r12, [rbx + 8]
  __il_cont#139:
    x64.movRegImm32 r13, 0
    x64.jmp whilehdr#18
  __il_body#142:
    x64.loadRegBaseDisp.word64 r14, [rbx + 8]
  __il_cont#141:
    x64.cmpRegReg r13, r14
    x64.jcc aboveEqual, atEnd
  ifcont#28:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#29
  tryerr#30:
    x64.leaRegRdata rcx, [rip + __str_blob_49]  ; "panic at grapheme.maxon:56: grapheme: firstByte OOB \xe2\x80\x94 caller guarantees startPos < len\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#29:
    x64.cmpRegImm32 r8, 128
    x64.jcc greaterEqual, ifcont#54
  isAscii:
    x64.cmpRegImm32 r8, 13
    x64.jcc notEqual, ifcont#39
  isCR:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.cmpRegReg rdx, r14
    x64.jcc aboveEqual, ifcont#39
  hasNext:
    x64.movRegReg rcx, rbx
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#34
  tryerr#35:
    x64.leaRegRdata rcx, [rip + __str_blob_50]  ; "panic at grapheme.maxon:63: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#34:
    x64.cmpRegImm32 r8, 10
    x64.jcc notEqual, ifcont#39
  isCRLF:
    x64.leaRegRegImm32 r13, r13, 2
    x64.jmp __il_cont#25
  ifcont#39:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.cmpRegReg rdx, r14
    x64.jcc aboveEqual, ifelse#52
  hasNextByte:
    x64.movRegReg rcx, rbx
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok#41
  tryerr#42:
    x64.leaRegRdata rcx, [rip + __str_blob_51]  ; "panic at grapheme.maxon:75: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryok#41:
    x64.cmpRegImm32 r8, 128
    x64.jcc less, ifelse#50
  nextIsMultibyte:
    x64.leaRegRegImm32 rdx, r13, 1
    x64.movRegReg rcx, rbx
    x64.callDirect utf8DecodeAt
    x64.movRegReg rcx, r8
    x64.callDirect graphemeBreakProperty
    x64.cmpRegImm32 r8, 4
    x64.movRegImm32 rax, 0
    x64.jcc equal, scmerge#45
  andrhs#44:
    x64.cmpRegImm32 r8, 5
    x64.setccReg notEqual, rax
  scmerge#45:
    x64.movRegImm32 rcx, 0
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, critsplit#148
  andrhs#46:
    x64.cmpRegImm32 r8, 8
    x64.setccReg notEqual, rax
    x64.jmp scmerge#47
  critsplit#148:
    x64.movRegReg rax, rcx
  scmerge#47:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#54
  notCombining:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp __il_cont#25
  ifelse#50:
    x64.leaRegRegImm32 r13, r13, 1
    x64.jmp __il_cont#25
  ifcont#54:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect utf8DecodeAt
    x64.movRegReg r15, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect utf8ByteLengthAt
    x64.leaRegRegReg rax, r13, r8
    x64.storeSlotReg slot7, rax
    x64.movRegReg rcx, r15
    x64.callDirect graphemeBreakProperty
    x64.movRegReg r13, r8
  __il_body#61:
    x64.movRegImm32 rcx, 0
  __il_cont#60:
    x64.movRegReg rdx, r13
    x64.movRegReg rax, r15
    x64.callDirect graphemeStateUpdate
    x64.loadRegSlot rdx, slot7
    x64.jmp whilehdr#55
  scan#56:
    x64.movRegReg rcx, rbx
    x64.callDirect utf8DecodeAt
    x64.movRegReg r15, r8
    x64.storeSlotReg slot4, r15
    x64.movRegReg rcx, r15
    x64.callDirect graphemeBreakProperty
    x64.storeSlotReg slot5, r8
  __il_body#63:
    x64.cmpRegImm32 r13, 0
    x64.jcc less, __rc_panic#125
  __rc_chk#126:
    x64.cmpRegImm32 r13, 13
    x64.jcc greater, __rc_panic#125
  __rc_ok#124:
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic#128
  __rc_chk#129:
    x64.cmpRegImm32 r8, 13
    x64.jcc greater, __rc_panic#128
  __rc_ok#127:
    x64.cmpRegImm32 r15, 0
    x64.jcc less, __rc_panic#131
  __rc_chk#132:
    x64.cmpRegImm32 r15, 1114111
    x64.jcc greater, __rc_panic#131
  __rc_ok#130:
    x64.cmpRegImm32 r13, 1
    x64.setccReg equal, rax
    x64.movRegImm32 rcx, 0
    x64.jcc notEqual, scmerge#65
  andrhs#64:
    x64.cmpRegImm32 r8, 2
    x64.setccReg equal, rcx
  scmerge#65:
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, ifcont#67
  gb3:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#67:
    x64.cmpRegImm32 r13, 3
    x64.movRegImm32 rcx, 1
    x64.jcc equal, scmerge#69
  orrhs#68:
    x64.movRegReg rcx, rax
  scmerge#69:
    x64.movRegImm32 rax, 1
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, scmerge#71
  orrhs#70:
    x64.cmpRegImm32 r13, 2
    x64.setccReg equal, rax
  scmerge#71:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#73
  gb4:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#62
  ifcont#73:
    x64.cmpRegImm32 r8, 3
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#75
  orrhs#74:
    x64.cmpRegImm32 r8, 1
    x64.setccReg equal, rax
  scmerge#75:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#152
  orrhs#76:
    x64.cmpRegImm32 r8, 2
    x64.setccReg equal, rax
    x64.jmp scmerge#77
  critsplit#152:
    x64.movRegReg rax, rcx
  scmerge#77:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#79
  gb5:
    x64.movRegImm32 rax, 1
    x64.jmp __il_cont#62
  ifcont#79:
    x64.cmpRegImm32 r13, 9
    x64.jcc notEqual, ifcont#89
  gb6:
    x64.cmpRegImm32 r8, 9
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#82
  orrhs#81:
    x64.cmpRegImm32 r8, 10
    x64.setccReg equal, rax
  scmerge#82:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#154
  orrhs#83:
    x64.cmpRegImm32 r8, 12
    x64.setccReg equal, rax
    x64.jmp scmerge#84
  critsplit#154:
    x64.movRegReg rax, rcx
  scmerge#84:
    x64.movRegImm32 rcx, 1
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, critsplit#155
  orrhs#85:
    x64.cmpRegImm32 r8, 13
    x64.setccReg equal, rax
    x64.jmp scmerge#86
  critsplit#155:
    x64.movRegReg rax, rcx
  scmerge#86:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#89
  gb6_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#89:
    x64.cmpRegImm32 r13, 12
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#91
  orrhs#90:
    x64.cmpRegImm32 r13, 10
    x64.setccReg equal, rax
  scmerge#91:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#97
  gb7:
    x64.cmpRegImm32 r8, 10
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#94
  orrhs#93:
    x64.cmpRegImm32 r8, 11
    x64.setccReg equal, rax
  scmerge#94:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#97
  gb7_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#97:
    x64.cmpRegImm32 r13, 13
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#99
  orrhs#98:
    x64.cmpRegImm32 r13, 11
    x64.setccReg equal, rax
  scmerge#99:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#103
  gb8:
    x64.cmpRegImm32 r8, 11
    x64.jcc notEqual, ifcont#103
  gb8_inner:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#103:
    x64.cmpRegImm32 r8, 4
    x64.movRegImm32 rax, 1
    x64.jcc equal, scmerge#105
  orrhs#104:
    x64.cmpRegImm32 r8, 5
    x64.setccReg equal, rax
  scmerge#105:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#107
  gb9:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#107:
    x64.cmpRegImm32 r8, 8
    x64.jcc notEqual, ifcont#109
  gb9a:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#109:
    x64.cmpRegImm32 r13, 7
    x64.jcc notEqual, ifcont#111
  gb9b:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#111:
    x64.cmpRegImm32 r13, 5
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, critsplit#160
  andrhs#112:
    x64.movRegReg rcx, r15
    x64.callDirect isExtendedPictographic
    x64.jmp scmerge#113
  critsplit#160:
    x64.movRegReg r8, rax
  scmerge#113:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#117
  __il_body#144:
    x64.loadRegSlot rax, slot6
    x64.andRegImm32 rax, rax, 1
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
  __il_cont#143:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#117
  gb11_check:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#117:
    x64.cmpRegImm32 r13, 6
    x64.movRegImm32 rax, 0
    x64.jcc notEqual, scmerge#119
  andrhs#118:
    x64.loadRegSlot rax, slot5
    x64.cmpRegImm32 rax, 6
    x64.setccReg equal, rax
  scmerge#119:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#123
  __il_body#134:
    x64.loadRegSlot rax, slot6
    x64.shrRegImm8 rax, rax, 1
    x64.andRegImm32 rax, rax, 1
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
  __il_cont#133:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont#123
  gb12_odd:
    x64.movRegImm32 rax, 0
    x64.jmp __il_cont#62
  ifcont#123:
    x64.movRegImm32 rax, 1
  __il_cont#62:
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, breakFound
  ifcont#59:
    x64.loadRegSlot rax, slot4
    x64.loadRegSlot rdx, slot5
    x64.loadRegSlot rcx, slot6
    x64.callDirect graphemeStateUpdate
    x64.movRegReg r13, r8
    x64.loadRegSlot rdx, slot3
    x64.movRegReg rcx, rbx
    x64.callDirect utf8ByteLengthAt
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rdx, rax, r8
    x64.loadRegSlot rax, slot5
    x64.movRegReg r8, r13
    x64.movRegReg r13, rax
  whilehdr#55:
    x64.storeSlotReg slot6, r8
    x64.storeSlotReg slot3, rdx
    x64.cmpRegReg rdx, r14
    x64.jcc below, scan#56
    x64.jmp whileexit
  atEnd:
    x64.loadRegSlot r13, slot2
    x64.jmp __il_cont#25
  ifelse#52:
    x64.movRegReg r13, rdx
    x64.jmp __il_cont#25
  breakFound:
    x64.loadRegSlot r13, slot3
    x64.jmp __il_cont#25
  whileexit:
    x64.loadRegSlot r13, slot3
  __il_cont#25:
    x64.loadRegSlot rax, slot2
    x64.movRegReg rsi, r13
    x64.subRegReg rsi, r13, rax
    x64.movRegReg rcx, r13
    x64.xorRegImm32 rcx, r13, -1
    x64.andRegReg rcx, rcx, rax
    x64.movRegReg rdx, r13
    x64.xorRegReg rdx, r13, rax
    x64.xorRegImm32 rdx, rdx, -1
    x64.andRegReg rdx, rdx, rsi
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#24
  __rc_ok#23:
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, rsi
    x64.callDirect String.makeCharFromBytes
    x64.movRegReg r14, r8
    x64.loadRegSlot rcx, slot1
    x64.movRegReg rdx, r14
    x64.callDirect CharacterSet.contains
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#22
  found#21:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp __il_cont#16
  ifcont#22:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
  whilehdr#18:
    x64.storeSlotReg slot2, r13
    x64.cmpRegReg r13, r12
    x64.jcc below, __il_body#142
  __il_cont#16:
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot2
    x64.cmpRegReg rcx, rax
    x64.jcc below, __il_body#8
  allMatch:
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegImm32 r9, 1
    x64.movRegReg rcx, rbx
    x64.callDirect String.sliceBytes
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __il_body#8:
    x64.loadRegSlot r12, slot0
    x64.jmp whilehdr#9
  scan#10:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect findGraphemeStart
    x64.movRegReg r13, r8
    x64.movRegReg rax, r12
    x64.subRegReg rax, r12, r13
    x64.movRegReg rcx, r12
    x64.xorRegImm32 rcx, r12, -1
    x64.andRegReg rcx, rcx, r13
    x64.movRegReg rdx, r12
    x64.xorRegReg rdx, r12, r13
    x64.xorRegImm32 rdx, rdx, -1
    x64.andRegReg rdx, rdx, rax
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc less, __rc_panic#15
  __rc_ok#14:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.callDirect String.makeCharFromBytes
    x64.movRegReg r14, r8
    x64.loadRegSlot rcx, slot1
    x64.movRegReg rdx, r14
    x64.callDirect CharacterSet.contains
    x64.xorRegImm32 r8, r8, 1
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, ifcont#13
  found#12:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.jmp __il_cont#7
  ifcont#13:
    x64.movRegReg rcx, r14
    x64.callDirect __str_decref
    x64.movRegReg r12, r13
  whilehdr#9:
    x64.loadRegSlot rax, slot2
    x64.cmpRegReg r12, rax
    x64.jcc above, scan#10
  __il_cont#7:
    x64.loadRegBaseDisp.byte r9, [rbx + 48]
    x64.loadRegSlot rdx, slot2
    x64.movRegReg rcx, rbx
    x64.movRegReg rax, r12
    x64.callDirect String.sliceBytes
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#125:
    x64.leaRegRdata rcx, [rip + __str_blob_104]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#128:
    x64.leaRegRdata rcx, [rip + __str_blob_104]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#131:
    x64.leaRegRdata rcx, [rip + __str_blob_105]  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'Codepoint'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#24:
    x64.leaRegRdata rcx, [rip + __str_blob_95]  ; "panic at String.maxon:741: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic#15:
    x64.leaRegRdata rcx, [rip + __str_blob_96]  ; "panic at String.maxon:761: Range check failed: value outside typealias 'BytePos'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 104
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __slab_arena_list@8 = i64 0
  __slab_arena_map_l1@16 = i64 0
  __slab_state@24 = i64 0
  __mrt_program_started@32 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_12  ; "  abc  "
  __il_body#7:
    arm64.leaGlobal x0, __data_CharacterSet.cachedWhitespacesAndNewlines
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaFuncAddr x1, __clone_CharacterSet
    arm64.bl __mm_own_deep
    arm64.movRegReg x20, x0
  __il_cont#6:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl String.trim
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_CharacterSet
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont#8:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_70  ; "panic at a-library-body-named-into-the-golden.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @String.trim {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  __il_body#136:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.storeSlotReg slot0, x0
  __il_cont#135:
    arm64.cmp x0, 0
    arm64.b.ne __il_body#140
  __il_body#6:
    arm64.movImm x1, 0
  __il_body#138:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
  __il_cont#137:
    arm64.loadRegBaseDisp.byte x3, [x19 + 48]
    arm64.movRegReg x0, x19
    arm64.bl String.sliceBytes
  __il_cont#5:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __il_body#140:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
  __il_cont#139:
    arm64.movImm x22, 0
    arm64.b whilehdr#18
  __il_body#142:
    arm64.loadRegBaseDisp.word64 x23, [x19 + 8]
  __il_cont#141:
    arm64.cmp x22, x23
    arm64.b.hs atEnd
  ifcont#28:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#29
  tryerr#30:
    arm64.leaRdata x0, __str_blob_49  ; "panic at grapheme.maxon:56: grapheme: firstByte OOB \xe2\x80\x94 caller guarantees startPos < len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#29:
    arm64.cmp x0, 128
    arm64.b.ge ifcont#54
  isAscii:
    arm64.cmp x0, 13
    arm64.b.ne ifcont#39
  isCR:
    arm64.add x1, x22, 1
    arm64.cmp x1, x23
    arm64.b.hs ifcont#39
  hasNext:
    arm64.movRegReg x0, x19
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#34
  tryerr#35:
    arm64.leaRdata x0, __str_blob_50  ; "panic at grapheme.maxon:63: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#34:
    arm64.cmp x0, 10
    arm64.b.ne ifcont#39
  isCRLF:
    arm64.add x23, x22, 2
    arm64.b __il_cont#25
  ifcont#39:
    arm64.add x1, x22, 1
    arm64.cmp x1, x23
    arm64.b.hs ifelse#52
  hasNextByte:
    arm64.movRegReg x0, x19
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#41
  tryerr#42:
    arm64.leaRdata x0, __str_blob_51  ; "panic at grapheme.maxon:75: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#41:
    arm64.cmp x0, 128
    arm64.b.lt ifelse#50
  nextIsMultibyte:
    arm64.add x1, x22, 1
    arm64.movRegReg x0, x19
    arm64.bl utf8DecodeAt
    arm64.bl graphemeBreakProperty
    arm64.cmp x0, 4
    arm64.movImm x1, 0
    arm64.b.eq scmerge#45
  andrhs#44:
    arm64.cmp x0, 5
    arm64.cset x1, ne
  scmerge#45:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#148
  andrhs#46:
    arm64.cmp x0, 8
    arm64.cset x0, ne
    arm64.b scmerge#47
  critsplit#148:
    arm64.movRegReg x0, x2
  scmerge#47:
    arm64.cbz x0, ifcont#54
  notCombining:
    arm64.add x23, x22, 1
    arm64.b __il_cont#25
  ifelse#50:
    arm64.add x23, x22, 1
    arm64.b __il_cont#25
  ifcont#54:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl utf8DecodeAt
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl utf8ByteLengthAt
    arm64.add x25, x22, x0
    arm64.movRegReg x0, x24
    arm64.bl graphemeBreakProperty
    arm64.movRegReg x26, x0
  __il_body#61:
    arm64.movImm x0, 0
  __il_cont#60:
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x24
    arm64.bl graphemeStateUpdate
    arm64.movRegReg x24, x0
    arm64.b whilehdr#55
  scan#56:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.bl utf8DecodeAt
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x27
    arm64.bl graphemeBreakProperty
    arm64.movRegReg x28, x0
  __il_body#63:
    arm64.cmp x26, 0
    arm64.b.lt __rc_panic#125
  __rc_chk#126:
    arm64.cmp x26, 13
    arm64.b.gt __rc_panic#125
  __rc_ok#124:
    arm64.cmp x28, 0
    arm64.b.lt __rc_panic#128
  __rc_chk#129:
    arm64.cmp x28, 13
    arm64.b.gt __rc_panic#128
  __rc_ok#127:
    arm64.cmp x27, 0
    arm64.b.lt __rc_panic#131
  __rc_chk#132:
    arm64.movImm x16, 1114111
    arm64.cmp x27, x16
    arm64.b.gt __rc_panic#131
  __rc_ok#130:
    arm64.cmp x26, 1
    arm64.cset x0, eq
    arm64.movImm x1, 0
    arm64.b.ne scmerge#65
  andrhs#64:
    arm64.cmp x28, 2
    arm64.cset x1, eq
  scmerge#65:
    arm64.cbz x1, ifcont#67
  gb3:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#67:
    arm64.cmp x26, 3
    arm64.movImm x1, 1
    arm64.b.eq scmerge#69
  orrhs#68:
    arm64.movRegReg x1, x0
  scmerge#69:
    arm64.movImm x0, 1
    arm64.cbnz x1, scmerge#71
  orrhs#70:
    arm64.cmp x26, 2
    arm64.cset x0, eq
  scmerge#71:
    arm64.cbz x0, ifcont#73
  gb4:
    arm64.movImm x0, 1
    arm64.b __il_cont#62
  ifcont#73:
    arm64.cmp x28, 3
    arm64.movImm x0, 1
    arm64.b.eq scmerge#75
  orrhs#74:
    arm64.cmp x28, 1
    arm64.cset x0, eq
  scmerge#75:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#152
  orrhs#76:
    arm64.cmp x28, 2
    arm64.cset x0, eq
    arm64.b scmerge#77
  critsplit#152:
    arm64.movRegReg x0, x1
  scmerge#77:
    arm64.cbz x0, ifcont#79
  gb5:
    arm64.movImm x0, 1
    arm64.b __il_cont#62
  ifcont#79:
    arm64.cmp x26, 9
    arm64.b.ne ifcont#89
  gb6:
    arm64.cmp x28, 9
    arm64.movImm x0, 1
    arm64.b.eq scmerge#82
  orrhs#81:
    arm64.cmp x28, 10
    arm64.cset x0, eq
  scmerge#82:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#154
  orrhs#83:
    arm64.cmp x28, 12
    arm64.cset x0, eq
    arm64.b scmerge#84
  critsplit#154:
    arm64.movRegReg x0, x1
  scmerge#84:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#155
  orrhs#85:
    arm64.cmp x28, 13
    arm64.cset x0, eq
    arm64.b scmerge#86
  critsplit#155:
    arm64.movRegReg x0, x1
  scmerge#86:
    arm64.cbz x0, ifcont#89
  gb6_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#89:
    arm64.cmp x26, 12
    arm64.movImm x0, 1
    arm64.b.eq scmerge#91
  orrhs#90:
    arm64.cmp x26, 10
    arm64.cset x0, eq
  scmerge#91:
    arm64.cbz x0, ifcont#97
  gb7:
    arm64.cmp x28, 10
    arm64.movImm x0, 1
    arm64.b.eq scmerge#94
  orrhs#93:
    arm64.cmp x28, 11
    arm64.cset x0, eq
  scmerge#94:
    arm64.cbz x0, ifcont#97
  gb7_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#97:
    arm64.cmp x26, 13
    arm64.movImm x0, 1
    arm64.b.eq scmerge#99
  orrhs#98:
    arm64.cmp x26, 11
    arm64.cset x0, eq
  scmerge#99:
    arm64.cbz x0, ifcont#103
  gb8:
    arm64.cmp x28, 11
    arm64.b.ne ifcont#103
  gb8_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#103:
    arm64.cmp x28, 4
    arm64.movImm x0, 1
    arm64.b.eq scmerge#105
  orrhs#104:
    arm64.cmp x28, 5
    arm64.cset x0, eq
  scmerge#105:
    arm64.cbz x0, ifcont#107
  gb9:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#107:
    arm64.cmp x28, 8
    arm64.b.ne ifcont#109
  gb9a:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#109:
    arm64.cmp x26, 7
    arm64.b.ne ifcont#111
  gb9b:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#111:
    arm64.cmp x26, 5
    arm64.movImm x0, 0
    arm64.b.ne scmerge#113
  andrhs#112:
    arm64.movRegReg x0, x27
    arm64.bl isExtendedPictographic
  scmerge#113:
    arm64.cbz x0, ifcont#117
  __il_body#144:
    arm64.and x0, x24, 1
    arm64.cmp x0, 0
    arm64.cset x0, ne
  __il_cont#143:
    arm64.cbz x0, ifcont#117
  gb11_check:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#117:
    arm64.cmp x26, 6
    arm64.movImm x0, 0
    arm64.b.ne scmerge#119
  andrhs#118:
    arm64.cmp x28, 6
    arm64.cset x0, eq
  scmerge#119:
    arm64.cbz x0, ifcont#123
  __il_body#134:
    arm64.lsr x0, x24, 1
    arm64.and x0, x0, 1
    arm64.cmp x0, 0
    arm64.cset x0, ne
  __il_cont#133:
    arm64.cbz x0, ifcont#123
  gb12_odd:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#123:
    arm64.movImm x0, 1
  __il_cont#62:
    arm64.cbnz x0, breakFound
  ifcont#59:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.movRegReg x2, x27
    arm64.bl graphemeStateUpdate
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.bl utf8ByteLengthAt
    arm64.add x25, x25, x0
    arm64.movRegReg x26, x28
  whilehdr#55:
    arm64.cmp x25, x23
    arm64.b.lo scan#56
    arm64.b whileexit
  atEnd:
    arm64.movRegReg x23, x22
    arm64.b __il_cont#25
  ifelse#52:
    arm64.movRegReg x23, x1
    arm64.b __il_cont#25
  breakFound:
    arm64.movRegReg x23, x25
    arm64.b __il_cont#25
  whileexit:
    arm64.movRegReg x23, x25
  __il_cont#25:
    arm64.sub x2, x23, x22
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x23, x16
    arm64.and x0, x0, x22
    arm64.eor x1, x23, x22
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x1, x16
    arm64.and x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#24
  __rc_ok#23:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.makeCharFromBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl CharacterSet.contains
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#22
  found#21:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.b __il_cont#16
  ifcont#22:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movRegReg x22, x23
  whilehdr#18:
    arm64.cmp x22, x21
    arm64.b.lo __il_body#142
  __il_cont#16:
    arm64.loadRegSlot x0, slot0
    arm64.cmp x22, x0
    arm64.b.lo __il_body#8
  allMatch:
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.movImm x3, 1
    arm64.movRegReg x0, x19
    arm64.bl String.sliceBytes
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __il_body#8:
    arm64.loadRegSlot x21, slot0
    arm64.b whilehdr#9
  scan#10:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl findGraphemeStart
    arm64.movRegReg x23, x0
    arm64.sub x2, x21, x23
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x21, x16
    arm64.and x0, x0, x23
    arm64.eor x1, x21, x23
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x1, x16
    arm64.and x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#15
  __rc_ok#14:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl String.makeCharFromBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl CharacterSet.contains
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#13
  found#12:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.b __il_cont#7
  ifcont#13:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movRegReg x21, x23
  whilehdr#9:
    arm64.cmp x21, x22
    arm64.b.hi scan#10
  __il_cont#7:
    arm64.loadRegBaseDisp.byte x3, [x19 + 48]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl String.sliceBytes
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#125:
    arm64.leaRdata x0, __str_blob_104  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#128:
    arm64.leaRdata x0, __str_blob_104  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#131:
    arm64.leaRdata x0, __str_blob_105  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'Codepoint'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#24:
    arm64.leaRdata x0, __str_blob_95  ; "panic at String.maxon:741: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#15:
    arm64.leaRdata x0, __str_blob_96  ; "panic at String.maxon:761: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __data_CharacterSet.cachedWhitespacesAndNewlines@0 = i64 0
  __mrt_envp@8 = i64 0
  __mrt_signal_stack_bytes@16 = i64 0
  __slab_arena_list@24 = i64 0
  __slab_arena_map_l1@32 = i64 0
  __slab_state@40 = i64 0
  __mrt_program_started@48 = i8 0
}

func @main {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_12  ; "  abc  "
  __il_body#7:
    arm64.leaGlobal x0, __data_CharacterSet.cachedWhitespacesAndNewlines
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaFuncAddr x1, __clone_CharacterSet
    arm64.bl __mm_own_deep
    arm64.movRegReg x20, x0
  __il_cont#6:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl String.trim
    arm64.movRegReg x19, x0
    arm64.movRegReg x0, x20
    arm64.bl __destruct_CharacterSet
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x20, [x19 + 8]
  __il_cont#8:
    arm64.cmp x20, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x20, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x19
    arm64.bl __str_decref
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_70  ; "panic at a-library-body-named-into-the-golden.test:4: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}

func @String.trim {
  entry:
    arm64.prologue 112
    arm64.storeSlotReg slot10, x28
    arm64.storeSlotReg slot9, x27
    arm64.storeSlotReg slot8, x26
    arm64.storeSlotReg slot7, x25
    arm64.storeSlotReg slot6, x24
    arm64.storeSlotReg slot5, x23
    arm64.storeSlotReg slot4, x22
    arm64.storeSlotReg slot3, x21
    arm64.storeSlotReg slot2, x20
    arm64.storeSlotReg slot1, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  __il_body#136:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.storeSlotReg slot0, x0
  __il_cont#135:
    arm64.cmp x0, 0
    arm64.b.ne __il_body#140
  __il_body#6:
    arm64.movImm x1, 0
  __il_body#138:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 8]
  __il_cont#137:
    arm64.loadRegBaseDisp.byte x3, [x19 + 48]
    arm64.movRegReg x0, x19
    arm64.bl String.sliceBytes
  __il_cont#5:
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __il_body#140:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
  __il_cont#139:
    arm64.movImm x22, 0
    arm64.b whilehdr#18
  __il_body#142:
    arm64.loadRegBaseDisp.word64 x23, [x19 + 8]
  __il_cont#141:
    arm64.cmp x22, x23
    arm64.b.hs atEnd
  ifcont#28:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#29
  tryerr#30:
    arm64.leaRdata x0, __str_blob_49  ; "panic at grapheme.maxon:56: grapheme: firstByte OOB \xe2\x80\x94 caller guarantees startPos < len\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#29:
    arm64.cmp x0, 128
    arm64.b.ge ifcont#54
  isAscii:
    arm64.cmp x0, 13
    arm64.b.ne ifcont#39
  isCR:
    arm64.add x1, x22, 1
    arm64.cmp x1, x23
    arm64.b.hs ifcont#39
  hasNext:
    arm64.movRegReg x0, x19
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#34
  tryerr#35:
    arm64.leaRdata x0, __str_blob_50  ; "panic at grapheme.maxon:63: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#34:
    arm64.cmp x0, 10
    arm64.b.ne ifcont#39
  isCRLF:
    arm64.add x23, x22, 2
    arm64.b __il_cont#25
  ifcont#39:
    arm64.add x1, x22, 1
    arm64.cmp x1, x23
    arm64.b.hs ifelse#52
  hasNextByte:
    arm64.movRegReg x0, x19
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.eq tryok#41
  tryerr#42:
    arm64.leaRdata x0, __str_blob_51  ; "panic at grapheme.maxon:75: grapheme: nextByte OOB \xe2\x80\x94 startPos+1 < len checked above\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  tryok#41:
    arm64.cmp x0, 128
    arm64.b.lt ifelse#50
  nextIsMultibyte:
    arm64.add x1, x22, 1
    arm64.movRegReg x0, x19
    arm64.bl utf8DecodeAt
    arm64.bl graphemeBreakProperty
    arm64.cmp x0, 4
    arm64.movImm x1, 0
    arm64.b.eq scmerge#45
  andrhs#44:
    arm64.cmp x0, 5
    arm64.cset x1, ne
  scmerge#45:
    arm64.movImm x2, 0
    arm64.cbz x1, critsplit#148
  andrhs#46:
    arm64.cmp x0, 8
    arm64.cset x0, ne
    arm64.b scmerge#47
  critsplit#148:
    arm64.movRegReg x0, x2
  scmerge#47:
    arm64.cbz x0, ifcont#54
  notCombining:
    arm64.add x23, x22, 1
    arm64.b __il_cont#25
  ifelse#50:
    arm64.add x23, x22, 1
    arm64.b __il_cont#25
  ifcont#54:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl utf8DecodeAt
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl utf8ByteLengthAt
    arm64.add x25, x22, x0
    arm64.movRegReg x0, x24
    arm64.bl graphemeBreakProperty
    arm64.movRegReg x26, x0
  __il_body#61:
    arm64.movImm x0, 0
  __il_cont#60:
    arm64.movRegReg x1, x26
    arm64.movRegReg x2, x24
    arm64.bl graphemeStateUpdate
    arm64.movRegReg x24, x0
    arm64.b whilehdr#55
  scan#56:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.bl utf8DecodeAt
    arm64.movRegReg x27, x0
    arm64.movRegReg x0, x27
    arm64.bl graphemeBreakProperty
    arm64.movRegReg x28, x0
  __il_body#63:
    arm64.cmp x26, 0
    arm64.b.lt __rc_panic#125
  __rc_chk#126:
    arm64.cmp x26, 13
    arm64.b.gt __rc_panic#125
  __rc_ok#124:
    arm64.cmp x28, 0
    arm64.b.lt __rc_panic#128
  __rc_chk#129:
    arm64.cmp x28, 13
    arm64.b.gt __rc_panic#128
  __rc_ok#127:
    arm64.cmp x27, 0
    arm64.b.lt __rc_panic#131
  __rc_chk#132:
    arm64.movImm x16, 1114111
    arm64.cmp x27, x16
    arm64.b.gt __rc_panic#131
  __rc_ok#130:
    arm64.cmp x26, 1
    arm64.cset x0, eq
    arm64.movImm x1, 0
    arm64.b.ne scmerge#65
  andrhs#64:
    arm64.cmp x28, 2
    arm64.cset x1, eq
  scmerge#65:
    arm64.cbz x1, ifcont#67
  gb3:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#67:
    arm64.cmp x26, 3
    arm64.movImm x1, 1
    arm64.b.eq scmerge#69
  orrhs#68:
    arm64.movRegReg x1, x0
  scmerge#69:
    arm64.movImm x0, 1
    arm64.cbnz x1, scmerge#71
  orrhs#70:
    arm64.cmp x26, 2
    arm64.cset x0, eq
  scmerge#71:
    arm64.cbz x0, ifcont#73
  gb4:
    arm64.movImm x0, 1
    arm64.b __il_cont#62
  ifcont#73:
    arm64.cmp x28, 3
    arm64.movImm x0, 1
    arm64.b.eq scmerge#75
  orrhs#74:
    arm64.cmp x28, 1
    arm64.cset x0, eq
  scmerge#75:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#152
  orrhs#76:
    arm64.cmp x28, 2
    arm64.cset x0, eq
    arm64.b scmerge#77
  critsplit#152:
    arm64.movRegReg x0, x1
  scmerge#77:
    arm64.cbz x0, ifcont#79
  gb5:
    arm64.movImm x0, 1
    arm64.b __il_cont#62
  ifcont#79:
    arm64.cmp x26, 9
    arm64.b.ne ifcont#89
  gb6:
    arm64.cmp x28, 9
    arm64.movImm x0, 1
    arm64.b.eq scmerge#82
  orrhs#81:
    arm64.cmp x28, 10
    arm64.cset x0, eq
  scmerge#82:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#154
  orrhs#83:
    arm64.cmp x28, 12
    arm64.cset x0, eq
    arm64.b scmerge#84
  critsplit#154:
    arm64.movRegReg x0, x1
  scmerge#84:
    arm64.movImm x1, 1
    arm64.cbnz x0, critsplit#155
  orrhs#85:
    arm64.cmp x28, 13
    arm64.cset x0, eq
    arm64.b scmerge#86
  critsplit#155:
    arm64.movRegReg x0, x1
  scmerge#86:
    arm64.cbz x0, ifcont#89
  gb6_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#89:
    arm64.cmp x26, 12
    arm64.movImm x0, 1
    arm64.b.eq scmerge#91
  orrhs#90:
    arm64.cmp x26, 10
    arm64.cset x0, eq
  scmerge#91:
    arm64.cbz x0, ifcont#97
  gb7:
    arm64.cmp x28, 10
    arm64.movImm x0, 1
    arm64.b.eq scmerge#94
  orrhs#93:
    arm64.cmp x28, 11
    arm64.cset x0, eq
  scmerge#94:
    arm64.cbz x0, ifcont#97
  gb7_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#97:
    arm64.cmp x26, 13
    arm64.movImm x0, 1
    arm64.b.eq scmerge#99
  orrhs#98:
    arm64.cmp x26, 11
    arm64.cset x0, eq
  scmerge#99:
    arm64.cbz x0, ifcont#103
  gb8:
    arm64.cmp x28, 11
    arm64.b.ne ifcont#103
  gb8_inner:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#103:
    arm64.cmp x28, 4
    arm64.movImm x0, 1
    arm64.b.eq scmerge#105
  orrhs#104:
    arm64.cmp x28, 5
    arm64.cset x0, eq
  scmerge#105:
    arm64.cbz x0, ifcont#107
  gb9:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#107:
    arm64.cmp x28, 8
    arm64.b.ne ifcont#109
  gb9a:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#109:
    arm64.cmp x26, 7
    arm64.b.ne ifcont#111
  gb9b:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#111:
    arm64.cmp x26, 5
    arm64.movImm x0, 0
    arm64.b.ne scmerge#113
  andrhs#112:
    arm64.movRegReg x0, x27
    arm64.bl isExtendedPictographic
  scmerge#113:
    arm64.cbz x0, ifcont#117
  __il_body#144:
    arm64.and x0, x24, 1
    arm64.cmp x0, 0
    arm64.cset x0, ne
  __il_cont#143:
    arm64.cbz x0, ifcont#117
  gb11_check:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#117:
    arm64.cmp x26, 6
    arm64.movImm x0, 0
    arm64.b.ne scmerge#119
  andrhs#118:
    arm64.cmp x28, 6
    arm64.cset x0, eq
  scmerge#119:
    arm64.cbz x0, ifcont#123
  __il_body#134:
    arm64.lsr x0, x24, 1
    arm64.and x0, x0, 1
    arm64.cmp x0, 0
    arm64.cset x0, ne
  __il_cont#133:
    arm64.cbz x0, ifcont#123
  gb12_odd:
    arm64.movImm x0, 0
    arm64.b __il_cont#62
  ifcont#123:
    arm64.movImm x0, 1
  __il_cont#62:
    arm64.cbnz x0, breakFound
  ifcont#59:
    arm64.movRegReg x0, x24
    arm64.movRegReg x1, x28
    arm64.movRegReg x2, x27
    arm64.bl graphemeStateUpdate
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x25
    arm64.bl utf8ByteLengthAt
    arm64.add x25, x25, x0
    arm64.movRegReg x26, x28
  whilehdr#55:
    arm64.cmp x25, x23
    arm64.b.lo scan#56
    arm64.b whileexit
  atEnd:
    arm64.movRegReg x23, x22
    arm64.b __il_cont#25
  ifelse#52:
    arm64.movRegReg x23, x1
    arm64.b __il_cont#25
  breakFound:
    arm64.movRegReg x23, x25
    arm64.b __il_cont#25
  whileexit:
    arm64.movRegReg x23, x25
  __il_cont#25:
    arm64.sub x2, x23, x22
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x23, x16
    arm64.and x0, x0, x22
    arm64.eor x1, x23, x22
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x1, x16
    arm64.and x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#24
  __rc_ok#23:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.makeCharFromBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl CharacterSet.contains
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#22
  found#21:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.b __il_cont#16
  ifcont#22:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movRegReg x22, x23
  whilehdr#18:
    arm64.cmp x22, x21
    arm64.b.lo __il_body#142
  __il_cont#16:
    arm64.loadRegSlot x0, slot0
    arm64.cmp x22, x0
    arm64.b.lo __il_body#8
  allMatch:
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.movImm x3, 1
    arm64.movRegReg x0, x19
    arm64.bl String.sliceBytes
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __il_body#8:
    arm64.loadRegSlot x21, slot0
    arm64.b whilehdr#9
  scan#10:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x21
    arm64.bl findGraphemeStart
    arm64.movRegReg x23, x0
    arm64.sub x2, x21, x23
    arm64.movImm x16, 18446744073709551615
    arm64.eor x0, x21, x16
    arm64.and x0, x0, x23
    arm64.eor x1, x21, x23
    arm64.movImm x16, 18446744073709551615
    arm64.eor x1, x1, x16
    arm64.and x1, x1, x2
    arm64.orr x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic#15
  __rc_ok#14:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.bl String.makeCharFromBytes
    arm64.movRegReg x24, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x24
    arm64.bl CharacterSet.contains
    arm64.eor x0, x0, 1
    arm64.cbz x0, ifcont#13
  found#12:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.b __il_cont#7
  ifcont#13:
    arm64.movRegReg x0, x24
    arm64.bl __str_decref
    arm64.movRegReg x21, x23
  whilehdr#9:
    arm64.cmp x21, x22
    arm64.b.hi scan#10
  __il_cont#7:
    arm64.loadRegBaseDisp.byte x3, [x19 + 48]
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.movRegReg x2, x21
    arm64.bl String.sliceBytes
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#125:
    arm64.leaRdata x0, __str_blob_104  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#128:
    arm64.leaRdata x0, __str_blob_104  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'BreakProperty'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#131:
    arm64.leaRdata x0, __str_blob_105  ; "panic at grapheme.maxon:644: Range check failed: value outside typealias 'Codepoint'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#24:
    arm64.leaRdata x0, __str_blob_95  ; "panic at String.maxon:741: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
  __rc_panic#15:
    arm64.leaRdata x0, __str_blob_96  ; "panic at String.maxon:761: Range check failed: value outside typealias 'BytePos'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot1
    arm64.loadRegSlot x20, slot2
    arm64.loadRegSlot x21, slot3
    arm64.loadRegSlot x22, slot4
    arm64.loadRegSlot x23, slot5
    arm64.loadRegSlot x24, slot6
    arm64.loadRegSlot x25, slot7
    arm64.loadRegSlot x26, slot8
    arm64.loadRegSlot x27, slot9
    arm64.loadRegSlot x28, slot10
    arm64.epilogue 112
    arm64.ret
}
```

<!-- test: string-byte-at-body -->
`__managed_byte_at` — the THROWING, bounds-checked byte read behind `String.byteAt`. It leaves through the
dual-register `errorReturn` ABI carrying `__ManagedMemoryError.invalidByteRange`, which is exactly what
`stdlib/String.maxon:285` declares. The corpus's `hashString` is what installs it here, and this block is
its only gate.

⚠ **THE READ IS THE BUFFER'S.** `String.byteAt` is a corpus declaration whose own
`try managed.byteAt(index)` is the real call, so the read happens where the BUFFER's `byteAt`
happens — one entry point with one bound, rather than two graphs to keep in step. The
`RequiredRuntime` guard keeps the case from quietly pinning nothing: a name no program emits is a
loud panic (`TargetPrinter.requireEveryNameRendered`), which is the whole reason that guard exists.

```maxon
function main() returns ExitCode
	return hashString("a") mod 7
end 'main'
```
```exitcode
3
```
```RequiredRuntime
__managed_byte_at
```

```TargetIr:x64-windows
data {
  __slab_state@0 = i64 0
  __mrt_console_probe_stdin@8 = i8 0
  __mrt_console_probe_stdout@9 = i8 0
  __mrt_console_probe_stderr@10 = i8 0
  __mrt_program_started@11 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_1]  ; "a"
  __il_body#5:
    x64.movRegImm32 r12, 5381
  __il_body#13:
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
  __il_cont#12:
    x64.movRegImm32 r14, 0
    x64.jmp forhdr
  hash_loop:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.imulRegRegImm32 rax, r12, 33
    x64.leaRegRegReg rax, rax, r8
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
    x64.movRegReg r12, rax
  forhdr:
    x64.cmpRegReg r14, r13
    x64.jcc below, hash_loop
  forexit:
    x64.movRegImm32 rax, 4294967295
    x64.andRegReg r12, r12, rax
  __il_cont#4:
    x64.movRegImm rax, 2635249153387078803
    x64.mulHighReg r12
    x64.movRegReg rax, r12
    x64.subRegReg rax, r12, rdx
    x64.shrRegImm8 rax, rax, 1
    x64.leaRegRegReg rax, rax, rdx
    x64.shrRegImm8 rax, rax, 2
    x64.imulRegRegImm32 rax, rax, 7
    x64.movRegReg r8, r12
    x64.subRegReg r8, r12, rax
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg r8, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at string-byte-at-body.test:3: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_byte_at {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badrange
  chkhigh:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 24]
  __il_body#8:
    x64.movRegReg rdi, rsi
    x64.sarRegImm8 rdi, rsi, 63
    x64.movRegReg r8, rdi
    x64.xorRegImm32 r8, rdi, -1
    x64.movRegImm32 r9, 0
    x64.subRegReg r9, r9, rsi
    x64.shlRegImm8 rsi, rsi, 3
    x64.andRegReg r9, r9, rdi
    x64.andRegReg rsi, rsi, r8
    x64.orRegReg r9, r9, rsi
  __il_cont#7:
    x64.imulRegReg rax, rax, r9
  __il_body#9:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
  __il_cont#4:
    x64.cmpRegReg rdx, rax
    x64.jcc less, load
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 7
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte r8, [rax + 0]
    x64.movRegImm32 r10, 0
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.leaRegRdata rbx, [rip + __str_rec_1]  ; "a"
  __il_body#5:
    x64.movRegImm32 r12, 5381
  __il_body#13:
    x64.loadRegBaseDisp.word64 r13, [rbx + 8]
  __il_cont#12:
    x64.movRegImm32 r14, 0
    x64.jmp forhdr
  hash_loop:
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.callDirect String.byteAt
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  tryok:
    x64.imulRegRegImm32 rax, r12, 33
    x64.leaRegRegReg rax, rax, r8
  forstep:
    x64.leaRegRegImm32 r14, r14, 1
    x64.movRegReg r12, rax
  forhdr:
    x64.cmpRegReg r14, r13
    x64.jcc below, hash_loop
  forexit:
    x64.movRegImm32 rax, 4294967295
    x64.andRegReg r12, r12, rax
  __il_cont#4:
    x64.movRegImm rax, 2635249153387078803
    x64.mulHighReg r12
    x64.movRegReg rax, r12
    x64.subRegReg rax, r12, rdx
    x64.shrRegImm8 rax, rax, 1
    x64.leaRegRegReg rax, rax, rdx
    x64.shrRegImm8 rax, rax, 2
    x64.imulRegRegImm32 rax, rax, 7
    x64.movRegReg r8, r12
    x64.subRegReg r8, r12, rax
    x64.cmpRegImm32 r8, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 r8, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_2]  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_4]  ; "panic at string-byte-at-body.test:3: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__managed_byte_at {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.cmpRegImm32 rdx, 0
    x64.jcc less, badrange
  chkhigh:
    x64.loadRegBaseDisp.word64 rax, [rcx + 8]
    x64.loadRegBaseDisp.word64 rsi, [rcx + 24]
  __il_body#8:
    x64.movRegReg rdi, rsi
    x64.sarRegImm8 rdi, rsi, 63
    x64.movRegReg r8, rdi
    x64.xorRegImm32 r8, rdi, -1
    x64.movRegImm32 r9, 0
    x64.subRegReg r9, r9, rsi
    x64.shlRegImm8 rsi, rsi, 3
    x64.andRegReg r9, r9, rdi
    x64.andRegReg rsi, rsi, r8
    x64.orRegReg r9, r9, rsi
  __il_cont#7:
    x64.imulRegReg rax, rax, r9
  __il_body#9:
    x64.leaRegRegImm32 rax, rax, 7
    x64.shrRegImm8 rax, rax, 3
  __il_cont#4:
    x64.cmpRegReg rdx, rax
    x64.jcc less, load
  badrange:
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r10, 7
    x64.popReg rbp
    x64.ret
  load:
    x64.loadRegBaseDisp.word64 rax, [rcx + 0]
    x64.leaRegRegReg rax, rax, rdx
    x64.loadRegBaseDisp.byte r8, [rax + 0]
    x64.movRegImm32 r10, 0
    x64.popReg rbp
    x64.ret
}
```

```TargetIr:arm64-macos
data {
  __slab_state@0 = i64 0
  __mrt_program_started@8 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_1  ; "a"
  __il_body#5:
    arm64.movImm x20, 5381
  __il_body#13:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
  __il_cont#12:
    arm64.movImm x22, 0
    arm64.b forhdr
  hash_loop:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movImm x16, 33
    arm64.mul x1, x20, x16
    arm64.add x0, x1, x0
  forstep:
    arm64.add x22, x22, 1
    arm64.movRegReg x20, x0
  forhdr:
    arm64.cmp x22, x21
    arm64.b.lo hash_loop
  forexit:
    arm64.movImm x0, 4294967295
    arm64.and x0, x20, x0
  __il_cont#4:
    arm64.movImm x1, 2635249153387078803
    arm64.umulh x1, x1, x0
    arm64.sub x2, x0, x1
    arm64.lsr x2, x2, 1
    arm64.add x1, x2, x1
    arm64.lsr x1, x1, 2
    arm64.movImm x16, 7
    arm64.mul x1, x1, x16
    arm64.sub x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at string-byte-at-body.test:3: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @__managed_byte_at {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 24]
  __il_body#8:
    arm64.asr x4, x3, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x5, x4, x16
    arm64.movImm x6, 0
    arm64.sub x6, x6, x3
    arm64.lsl x3, x3, 3
    arm64.and x4, x6, x4
    arm64.and x3, x3, x5
    arm64.orr x3, x4, x3
  __il_cont#7:
    arm64.mul x2, x2, x3
  __il_body#9:
    arm64.add x2, x2, 7
    arm64.lsr x2, x2, 3
  __il_cont#4:
    arm64.cmp x1, x2
    arm64.b.lt load
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 7
    arm64.epilogue 16
    arm64.ret
  load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}
```

```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x19, __str_rec_1  ; "a"
  __il_body#5:
    arm64.movImm x20, 5381
  __il_body#13:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 8]
  __il_cont#12:
    arm64.movImm x22, 0
    arm64.b forhdr
  hash_loop:
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x22
    arm64.bl String.byteAt
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  tryok:
    arm64.movImm x16, 33
    arm64.mul x1, x20, x16
    arm64.add x0, x1, x0
  forstep:
    arm64.add x22, x22, 1
    arm64.movRegReg x20, x0
  forhdr:
    arm64.cmp x22, x21
    arm64.b.lo hash_loop
  forexit:
    arm64.movImm x0, 4294967295
    arm64.and x0, x20, x0
  __il_cont#4:
    arm64.movImm x1, 2635249153387078803
    arm64.umulh x1, x1, x0
    arm64.sub x2, x0, x1
    arm64.lsr x2, x2, 1
    arm64.add x1, x2, x1
    arm64.lsr x1, x1, 2
    arm64.movImm x16, 7
    arm64.mul x1, x1, x16
    arm64.sub x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  tryerr:
    arm64.leaRdata x0, __str_blob_2  ; "panic at hash.maxon:24: hashString: byteAt OOB \xe2\x80\x94 i < s.byteLength() invariant\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_4  ; "panic at string-byte-at-body.test:3: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}

func @__managed_byte_at {
  entry:
    arm64.prologue 16
    arm64.cmp x1, 0
    arm64.b.lt badrange
  chkhigh:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x3, [x0 + 24]
  __il_body#8:
    arm64.asr x4, x3, 63
    arm64.movImm x16, 18446744073709551615
    arm64.eor x5, x4, x16
    arm64.movImm x6, 0
    arm64.sub x6, x6, x3
    arm64.lsl x3, x3, 3
    arm64.and x4, x6, x4
    arm64.and x3, x3, x5
    arm64.orr x3, x4, x3
  __il_cont#7:
    arm64.mul x2, x2, x3
  __il_body#9:
    arm64.add x2, x2, 7
    arm64.lsr x2, x2, 3
  __il_cont#4:
    arm64.cmp x1, x2
    arm64.b.lt load
  badrange:
    arm64.movImm x0, 0
    arm64.movImm x9, 7
    arm64.epilogue 16
    arm64.ret
  load:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.add x0, x0, x1
    arm64.loadRegBaseDisp.byte x0, [x0 + 0]
    arm64.movImm x9, 0
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-mailbox-send-steps-its-lock-depth-with-a-plain-add -->
**THE LOCK DEPTH HAS ONE WRITER, SO ITS STEP IS A PLAIN READ-MODIFY-WRITE.** Every runtime lock enter
steps the calling machine's `MOffLocks` word up before the acquire, and every leave steps it down after the
release, so an asynchronous stop can refuse a thread that holds a runtime lock. The word belongs to ONE
machine, and only the OS thread running that machine writes it. Its readers are that same thread's own
signal handler (Linux and macOS), which program order already serves, and on x64-windows the monitor,
which reads it only while the thread is suspended. Neither needs the step to be atomic, so a locked
read-modify-write buys nothing and costs a full barrier at every enter and every leave: on x64 it is
`add qword [depth], ±1` with no `lock` prefix, and on arm64 a load, an add and a store.

`__mbox_send` is the witness because it is the smallest body that takes a runtime lock on every message:
one enter and one leave of its mailbox's lock on its append road, and one leave on its closed road. A
locked step in this pin — `x64.lock add` on an x64 lane, an atomic add on an arm64 lane — is the
per-message cost this case exists to keep out.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__mbox_send
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-mailbox-send-steps-its-lock-depth-with-a-plain-add.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mbox_send {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 21
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, sendclosed
  sendappend:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, sendchain
  sendfirst:
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.jmp sendlinked
  sendchain:
    x64.storeBaseDispReg.word64 [rax + 8], r12
  sendlinked:
    x64.storeBaseDispReg.word64 [rbx + 8], r12
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, senddone
  sendwakeparked:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_ready
    x64.jmp senddone
  sendclosed:
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __mbox_abandon
  senddone:
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-mailbox-send-steps-its-lock-depth-with-a-plain-add.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mbox_send {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, sendclosed
  sendappend:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.loadRegBaseDisp.word64 rax, [rbx + 8]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, sendchain
  sendfirst:
    x64.storeBaseDispReg.word64 [rbx + 0], r12
    x64.jmp sendlinked
  sendchain:
    x64.storeBaseDispReg.word64 [rax + 8], r12
  sendlinked:
    x64.storeBaseDispReg.word64 [rbx + 8], r12
    x64.loadRegBaseDisp.word64 r12, [rbx + 16]
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, senddone
  sendwakeparked:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_ready
    x64.jmp senddone
  sendclosed:
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __mbox_abandon
  senddone:
    x64.epilogue 32
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_program_started@512 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-mailbox-send-steps-its-lock-depth-with-a-plain-add.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__mbox_send {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x19, 64
    arm64.importCall 18
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 0
    arm64.b.ne sendclosed
  sendappend:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ne sendchain
  sendfirst:
    arm64.storeBaseDispReg.word64 [x19 + 0], x20
    arm64.b sendlinked
  sendchain:
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
  sendlinked:
    arm64.storeBaseDispReg.word64 [x19 + 8], x20
    arm64.loadRegBaseDisp.word64 x20, [x19 + 16]
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.add x0, x19, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq senddone
  sendwakeparked:
    arm64.movRegReg x0, x20
    arm64.bl __gt_ready
    arm64.b senddone
  sendclosed:
    arm64.add x0, x19, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __mbox_abandon
  senddone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-mailbox-send-steps-its-lock-depth-with-a-plain-add.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__mbox_send {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 0
    arm64.b.ne sendclosed
  sendappend:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x20 + 8], x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 8]
    arm64.cmp x0, 0
    arm64.b.ne sendchain
  sendfirst:
    arm64.storeBaseDispReg.word64 [x19 + 0], x20
    arm64.b sendlinked
  sendchain:
    arm64.storeBaseDispReg.word64 [x0 + 8], x20
  sendlinked:
    arm64.storeBaseDispReg.word64 [x19 + 8], x20
    arm64.loadRegBaseDisp.word64 x20, [x19 + 16]
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq senddone
  sendwakeparked:
    arm64.movRegReg x0, x20
    arm64.bl __gt_ready
    arm64.b senddone
  sendclosed:
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __mbox_abandon
  senddone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-mailbox-receive-takes-the-mailboxs-own-lock -->
**A RECEIVE SERIALISES ONLY WITH ITS OWN MAILBOX.** A mailbox's head, tail, closed flag and parked receiver
are guarded by a host lock the mailbox carries in its own region, not by `__sched_lock`, so two services'
traffic never meets on a lock and a service's traffic never meets the scheduler's ready, park and run-queue
work. `__mbox_recv` takes that lock around its head read and its pop on the `recvtry` road, and its `closed`
read on the empty road is inside the same hold as the head read, so "empty and closed" is one instant.

What this pin shows is the lock each road enters and leaves: the address handed to the host lock's enter
and leave is the mailbox's own lock field, computed from the `mbox` argument, and no load of `__sched_lock`
appears anywhere in the body. The park on the empty road still releases the mailbox lock before it switches
context, because the host lock is recursive per OS thread and a hold carried across a switch would be
inherited by whichever green thread that OS thread runs next.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__mbox_recv
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-mailbox-receive-takes-the-mailboxs-own-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mbox_recv {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.jmp recvtry
  recvempty:
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, r12
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, recvdrained
  recvpark:
    x64.movRegImm32 rcx, 4
    x64.movRegReg rdx, rbx
    x64.callDirect __gt_park
  recvtry:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 21
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.movRegImm32 r13, 0
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, recvempty
    x64.jmp recvpop
  recvdrained:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  recvpop:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, recvpopped
  recvlast:
    x64.storeBaseDispReg.word64 [rbx + 8], r13
  recvpopped:
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-mailbox-receive-takes-the-mailboxs-own-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__mbox_recv {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.jmp recvtry
  recvempty:
    x64.loadRegBaseDisp.word64 rax, [rbx + 24]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, r12
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, recvdrained
  recvpark:
    x64.movRegImm32 rcx, 4
    x64.movRegReg rdx, rbx
    x64.callDirect __gt_park
  recvtry:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegBaseDisp.word64 r12, [rbx + 0]
    x64.movRegImm32 r13, 0
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, recvempty
    x64.jmp recvpop
  recvdrained:
    x64.movRegReg r8, r13
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  recvpop:
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.storeBaseDispReg.word64 [rbx + 0], rax
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, recvpopped
  recvlast:
    x64.storeBaseDispReg.word64 [rbx + 8], r13
  recvpopped:
    x64.leaRegRegImm32 rcx, rbx, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, r12
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_program_started@512 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-mailbox-receive-takes-the-mailboxs-own-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__mbox_recv {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.b recvtry
  recvempty:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 0
    arm64.cset x20, eq
    arm64.add x0, x19, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cbz x20, recvdrained
  recvpark:
    arm64.movImm x0, 4
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  recvtry:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x19, 64
    arm64.importCall 18
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
    arm64.movImm x21, 0
    arm64.cmp x20, 0
    arm64.b.eq recvempty
    arm64.b recvpop
  recvdrained:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  recvpop:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.cmp x0, 0
    arm64.b.ne recvpopped
  recvlast:
    arm64.storeBaseDispReg.word64 [x19 + 8], x21
  recvpopped:
    arm64.add x0, x19, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-mailbox-receive-takes-the-mailboxs-own-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__mbox_recv {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.b recvtry
  recvempty:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.cmp x0, 0
    arm64.cset x20, eq
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cbz x20, recvdrained
  recvpark:
    arm64.movImm x0, 4
    arm64.movRegReg x1, x19
    arm64.bl __gt_park
  recvtry:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegBaseDisp.word64 x20, [x19 + 0]
    arm64.movImm x21, 0
    arm64.cmp x20, 0
    arm64.b.eq recvempty
    arm64.b recvpop
  recvdrained:
    arm64.movRegReg x0, x21
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  recvpop:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.storeBaseDispReg.word64 [x19 + 0], x0
    arm64.cmp x0, 0
    arm64.b.ne recvpopped
  recvlast:
    arm64.storeBaseDispReg.word64 [x19 + 8], x21
  recvpopped:
    arm64.add x0, x19, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x20
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}
```

<!-- test: a-windows-host-lock-spins-before-it-sleeps -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
**ON WINDOWS, A RUNTIME LOCK IS A CRITICAL SECTION WITH A SPIN COUNT.** A contended
`EnterCriticalSection` on a section initialised without one goes straight to its kernel wait, which costs a
sleep and a wake for a hold that lasts a few instructions. The runtime's host locks are therefore
initialised through `InitializeCriticalSectionAndSpinCount`, so a contended enter spins on the owner for a
bounded count before it sleeps, which is the same shape the Linux lock's enter has.

`__sched_init_procs` is the witness because it is the first body every scheduled program runs that
initialises a host lock: it cuts `__sched_lock`'s region from the slab and initialises it in place. What
this pin shows is the import that initialisation calls. Its IAT slot must be
`InitializeCriticalSectionAndSpinCount`'s, with the spin count passed in its second argument register,
and not `InitializeCriticalSection`'s. The other lanes build their locks from their own chunks, so the
case has no subject there.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__sched_init_procs
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-windows-host-lock-spins-before-it-sleeps.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_init_procs {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 40
    x64.iatCall 51
    x64.movReg32Reg32 rax, rax
    x64.cmpRegImm32 rax, 64
    x64.jcc greaterEqual, tlspastteb
  tlsinteb:
    x64.leaRegGlobal rcx, __sched_tls_index
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.imulRegRegImm32 rax, rax, 8
    x64.leaRegRegImm32 rbx, rax, 5248
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.storeBaseDispReg.word64 [rax + 0], rbx
    x64.callDirect __slab_state_base
    x64.storeBaseDispReg.word64 [r8 + 24], rbx
    x64.movRegImm32 rcx, 65535
    x64.iatCall 49
    x64.movReg32Reg32 rax, rax
    x64.cmpRegImm32 rax, 1
    x64.jcc greaterEqual, maxprocscpuok
  maxprocscpufloor:
    x64.movRegImm32 rax, 1
  maxprocscpuok:
    x64.storeSlotReg slot0, rax
    x64.movRegImm32 rbx, 32
    x64.movRegImm32 rcx, 32
    x64.callDirect __slab_alloc
    x64.movRegReg r12, r8
    x64.movRegImm32 rax, 77
    x64.storeBaseDispReg.byte [r12 + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 1], rax
    x64.movRegImm32 rax, 65
    x64.storeBaseDispReg.byte [r12 + 2], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 3], rax
    x64.movRegImm32 rax, 88
    x64.storeBaseDispReg.byte [r12 + 4], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 5], rax
    x64.movRegImm32 rax, 79
    x64.storeBaseDispReg.byte [r12 + 6], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 7], rax
    x64.movRegImm32 rax, 78
    x64.storeBaseDispReg.byte [r12 + 8], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 9], rax
    x64.movRegImm32 rax, 95
    x64.storeBaseDispReg.byte [r12 + 10], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 11], rax
    x64.movRegImm32 rax, 77
    x64.storeBaseDispReg.byte [r12 + 12], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 13], rax
    x64.movRegImm32 rax, 65
    x64.storeBaseDispReg.byte [r12 + 14], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 15], rax
    x64.movRegImm32 rax, 88
    x64.storeBaseDispReg.byte [r12 + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 17], rax
    x64.movRegImm32 rax, 95
    x64.storeBaseDispReg.byte [r12 + 18], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 19], rax
    x64.movRegImm32 rax, 80
    x64.storeBaseDispReg.byte [r12 + 20], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 21], rax
    x64.movRegImm32 rax, 82
    x64.storeBaseDispReg.byte [r12 + 22], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 23], rax
    x64.movRegImm32 rax, 79
    x64.storeBaseDispReg.byte [r12 + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 25], rax
    x64.movRegImm32 rax, 67
    x64.storeBaseDispReg.byte [r12 + 26], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 27], rax
    x64.movRegImm32 rax, 83
    x64.storeBaseDispReg.byte [r12 + 28], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 29], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 30], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [r12 + 31], rax
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 64
    x64.callDirect __slab_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.movRegReg r8, rbx
    x64.iatCall 44
    x64.movReg32Reg32 r15, rax
    x64.movRegReg rcx, r14
    x64.movRegReg rdx, r15
    x64.movRegReg rax, r13
    x64.movRegReg r9, rbx
    x64.callDirect __wt_narrow_ascii
    x64.movRegReg rcx, r14
    x64.callDirect __slab_free
    x64.movRegReg rcx, r12
    x64.callDirect __slab_free
    x64.cmpRegImm32 r15, 32
    x64.jcc less, critsplit#32
  maxprocsreread:
    x64.movRegReg rcx, r13
    x64.callDirect __slab_free
    x64.movRegImm32 rcx, 32
    x64.callDirect __slab_alloc
    x64.movRegReg rbx, r8
    x64.movRegImm32 rax, 77
    x64.storeBaseDispReg.byte [rbx + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 1], rax
    x64.movRegImm32 rax, 65
    x64.storeBaseDispReg.byte [rbx + 2], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 3], rax
    x64.movRegImm32 rax, 88
    x64.storeBaseDispReg.byte [rbx + 4], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 5], rax
    x64.movRegImm32 rax, 79
    x64.storeBaseDispReg.byte [rbx + 6], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 7], rax
    x64.movRegImm32 rax, 78
    x64.storeBaseDispReg.byte [rbx + 8], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 9], rax
    x64.movRegImm32 rax, 95
    x64.storeBaseDispReg.byte [rbx + 10], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 11], rax
    x64.movRegImm32 rax, 77
    x64.storeBaseDispReg.byte [rbx + 12], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 13], rax
    x64.movRegImm32 rax, 65
    x64.storeBaseDispReg.byte [rbx + 14], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 15], rax
    x64.movRegImm32 rax, 88
    x64.storeBaseDispReg.byte [rbx + 16], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 17], rax
    x64.movRegImm32 rax, 95
    x64.storeBaseDispReg.byte [rbx + 18], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 19], rax
    x64.movRegImm32 rax, 80
    x64.storeBaseDispReg.byte [rbx + 20], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 21], rax
    x64.movRegImm32 rax, 82
    x64.storeBaseDispReg.byte [rbx + 22], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 23], rax
    x64.movRegImm32 rax, 79
    x64.storeBaseDispReg.byte [rbx + 24], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 25], rax
    x64.movRegImm32 rax, 67
    x64.storeBaseDispReg.byte [rbx + 26], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 27], rax
    x64.movRegImm32 rax, 83
    x64.storeBaseDispReg.byte [rbx + 28], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 29], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 30], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.byte [rbx + 31], rax
    x64.movRegReg rcx, r15
    x64.callDirect __slab_alloc
    x64.movRegReg r12, r8
    x64.imulRegRegImm32 rcx, r15, 2
    x64.callDirect __slab_alloc
    x64.movRegReg r13, r8
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r13
    x64.movRegReg r8, r15
    x64.iatCall 44
    x64.movReg32Reg32 r14, rax
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r12
    x64.movRegReg r9, r15
    x64.callDirect __wt_narrow_ascii
    x64.movRegReg rcx, r13
    x64.callDirect __slab_free
    x64.movRegReg rcx, rbx
    x64.callDirect __slab_free
    x64.cmpRegReg r14, r15
    x64.jcc less, critsplit#33
  maxprocsunfit:
    x64.movRegReg rcx, r12
    x64.callDirect __slab_free
    x64.jmp maxprocsrejected
  critsplit#33:
    x64.movRegReg rcx, r12
    x64.jmp maxprocsparse
  critsplit#32:
    x64.movRegReg rcx, r13
  maxprocsparse:
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.byte rdx, [rcx + 0]
    x64.cmpRegImm32 rdx, 43
    x64.jcc notEqual, reqprocslead
  reqprocssigned:
    x64.movRegImm32 rax, 1
  reqprocslead:
    x64.leaRegRegReg rdx, rcx, rax
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.cmpRegImm32 rdx, 48
    x64.jcc less, maxprocsrefused
  decnotbelow#22:
    x64.cmpRegImm32 rdx, 57
    x64.jcc greater, maxprocsrefused
  decdigit#23:
    x64.movRegImm32 rbx, 0
  reqprocsscan:
    x64.leaRegRegReg rdx, rcx, rax
    x64.loadRegBaseDisp.byte rdx, [rdx + 0]
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, reqprocsdigit
  reqprocsended:
    x64.cmpRegImm32 rbx, 1
    x64.jcc less, maxprocsrefused
  maxprocsparsed:
    x64.callDirect __slab_free
    x64.jmp procsettled
  reqprocsdigit:
    x64.cmpRegImm32 rdx, 48
    x64.jcc less, maxprocsrefused
  decnotbelow#24:
    x64.cmpRegImm32 rdx, 57
    x64.jcc greater, maxprocsrefused
  decdigit#25:
    x64.leaRegRegImm32 rdx, rdx, -48
    x64.imulRegRegImm32 rsi, rbx, 10
    x64.leaRegRegReg rdx, rsi, rdx
    x64.leaRegRegImm32 rax, rax, 1
    x64.cmpRegImm32 rdx, 2147483647
    x64.jcc lessEqual, critsplit#35
  maxprocsrefused:
    x64.callDirect __slab_free
  maxprocsrejected:
    x64.loadRegSlot rbx, slot0
  procsettled:
    x64.leaRegGlobal rax, __sched_num_procs
    x64.storeBaseDispReg.word64 [rax + 0], rbx
    x64.movRegImm32 rcx, 64
    x64.callDirect __slab_alloc
    x64.movRegReg r12, r8
    x64.movRegReg rcx, r12
    x64.movRegImm32 rdx, 4000
    x64.iatCall 20
    x64.leaRegGlobal rax, __sched_lock
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.imulRegRegImm32 rcx, rbx, 8
    x64.callDirect __slab_alloc
    x64.leaRegGlobal rax, __sched_procs
    x64.storeBaseDispReg.word64 [rax + 0], r8
    x64.movRegImm32 r12, 0
  ploop:
    x64.cmpRegReg r12, rbx
    x64.jcc less, pbody
  adopt:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.leaRegGlobal rax, __sched_procs
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.movRegImm32 rcx, 656
    x64.callDirect __sched_arena_carve
    x64.movRegReg r12, r8
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 r8, 0
    x64.movRegImm32 r9, 0
    x64.iatCall 18
    x64.storeBaseDispReg.word64 [r12 + 496], rax
    x64.storeBaseDispReg.word64 [r12 + 512], rbx
    x64.leaRegGlobal rax, __sched_allm
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeBaseDispReg.word64 [r12 + 528], rax
    x64.leaRegGlobal rax, __sched_allm
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.leaRegGlobal rax, __sched_mcount
    x64.aluBaseDispImm32.add [rax + 0], 1
    x64.leaRegGlobal rax, __sched_main_m
    x64.storeBaseDispReg.word64 [rax + 0], r12
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [rbx + 72], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 rcx, 65536
    x64.callDirect __gt_system_stack_alloc
    x64.movRegReg rcx, r8
    x64.callDirect __slab_assert_os_alloc
    x64.leaRegRegImm32 rax, r8, 65536
    x64.storeBaseDispReg.word64 [r12 + 16], rax
    x64.leaRegRegImm32 rax, r12, 24
    x64.storeBaseDispReg.word64 [r12 + 8], rax
    x64.storeBaseDispReg.word64 [rax + 264], rax
    x64.leaRegGlobal rax, __sched_tls_index
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.movRegReg rdx, r12
    x64.iatCall 52
    x64.movRegReg rcx, r12
    x64.callDirect __gt_preempt_self_register
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r12 + 512], rax
    x64.storeBaseDispReg.word64 [rbx + 184], r12
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rdx, 4096
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r8, 12288
    x64.movRegImm32 r9, 4
    x64.iatCall 4
    x64.leaRegGlobal rcx, __sched_preempt_context
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.callDirect __np_init
    x64.callDirect __gt_now_ns
    x64.movRegReg rax, r8
    x64.leaRegGlobal rcx, __sched_lastpoll
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  pbody:
    x64.movRegImm32 rcx, 2288
    x64.callDirect __slab_alloc
    x64.movRegReg r13, r8
    x64.leaRegGlobal rax, __sched_procs
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeBaseIndexScaleReg.word64 [rax + r12*8 + 0], r13
    x64.storeBaseDispReg.word64 [r13 + 32], r12
    x64.leaRegRegImm32 r14, r12, 1
    x64.storeBaseDispReg.word64 [r13 + 40], r14
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, pnext
  poffer:
    x64.movRegReg rcx, r13
    x64.callDirect __sched_runq_empty
    x64.cmpRegImm32 r8, 1
    x64.jcc equal, poffered
  pofferheldwork:
    x64.leaRegRdata rcx, [rip + __abort_msg_95]  ; "fatal error: runtime abort 95 (schedulerIdledAProcessorHoldingWork)\x0a"
    x64.movRegImm32 rdx, 68
    x64.movRegImm32 rax, 95
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  poffered:
    x64.leaRegGlobal rax, __sched_regain_head
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, pidleputregain
  pidleputidle:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r13 + 72], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeBaseDispReg.word64 [r13 + 160], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.storeBaseDispReg.word64 [rax + 0], r13
    x64.movRegImm32 rax, 1
    x64.leaRegGlobal rcx, __sched_npidle
    x64.lock add [rcx], rax
    x64.jmp pnext
  pidleputregain:
    x64.loadRegBaseDisp.word64 rcx, [rax + 584]
    x64.leaRegGlobal rdx, __sched_regain_head
    x64.storeBaseDispReg.word64 [rdx + 0], rcx
    x64.movRegImm32 rcx, 0
    x64.storeBaseDispReg.word64 [rax + 584], rcx
    x64.storeBaseDispReg.word64 [rax + 512], r13
    x64.loadRegBaseDisp.word64 rcx, [rax + 496]
    x64.iatCall 19
  pnext:
    x64.movRegReg r12, r14
    x64.jmp ploop
  critsplit#35:
    x64.movRegReg rbx, rdx
    x64.jmp reqprocsscan
  tlspastteb:
    x64.leaRegRdata rcx, [rip + __abort_msg_121]  ; "fatal error: runtime abort 121 (schedulerTlsSlotOutsideTheTeb)\x0a"
    x64.movRegImm32 rdx, 63
    x64.movRegImm32 rax, 121
    x64.callDirect __rt_abort
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```

<!-- test: a-reply-cell-writes-only-its-own-words -->
**A REPLY CELL ZEROES WHAT IT USES, AND A RECYCLED RECORD IS NOT ZEROED WHOLE.** Every awaited service
message mints a reply cell, a green-thread record that never runs: no stack, no entry, never queued. A cell
reads and writes a handful of the record's words, so filling all `GtStructSize` bytes of a recycled record
on every message is a cost the message pays for words nothing reads. `__gt_free_get` therefore hands its
taker the record as the free list held it, and each taker writes what it needs: a green-thread or `async`
spawn clears the whole record itself, and `__gt_cell_alloc` stores the cell's own words.

The record still comes from the green-thread free list, never from a separate pool, because a stale
`awaitAny` or `isComplete` read of a recycled record relies on every record in that list having the same
shape.

What this pin shows: `__gt_free_get`'s `gfgrecycle` road returns the popped record with no `memFill` over
it, and `__gt_cell_alloc` stores every word a cell is read through before it returns the cell. A
whole-record fill in either body is the per-message cost this case exists to keep out.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__gt_cell_alloc
__gt_free_get
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-reply-cell-writes-only-its-own-words.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_free_get {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 56
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gfgnoproc
  gfoffset:
    x64.mov rax, gs:[rax]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gfgnoproc
  gfmachine:
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, gfgproc
  gfgnoproc:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.leaRegGlobal rax, __gt_gfree_head
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gfgcarve
  gfgglobalpop:
    x64.loadRegBaseDisp.word64 rax, [rbx + 56]
    x64.leaRegGlobal rcx, __gt_gfree_head
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 56], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, rbx
    x64.jmp gfgrecycle
  gfgproc:
    x64.loadRegBaseDisp.word64 rax, [rbx + 104]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gfglocalpop
  gfgrefill:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.movRegImm32 rax, 0
  gfgrefillhdr:
    x64.cmpRegImm32 rax, 32
    x64.jcc greaterEqual, gfgrefilldone
  gfgrefillpop:
    x64.leaRegGlobal rcx, __gt_gfree_head
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, gfgrefillpush
  gfgrefilldone:
    x64.loadRegBaseDisp.word64 rax, [rbx + 104]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gfgrefilled
  gfgcarve:
    x64.movRegImm32 rcx, 472
    x64.callDirect __sched_arena_carve
    x64.movRegReg rbx, r8
    x64.leaRegGlobal rax, __gt_records_carved
    x64.aluBaseDispImm32.add [rax + 0], 1
    x64.leaRegGlobal rax, __gt_allglen
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_allgcap
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegReg rax, rcx
    x64.jcc greaterEqual, allggrow
  allgstore:
    x64.leaRegGlobal rax, __gt_allg
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_allglen
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.storeBaseIndexScaleReg.word64 [rax + rcx*8 + 0], rbx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegGlobal rcx, __gt_allglen
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  allgdone:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gfgrefilled:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.jmp gfglocalpop
  gfgrefillpush:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 56]
    x64.leaRegGlobal rsi, __gt_gfree_head
    x64.storeBaseDispReg.word64 [rsi + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 56], rdx
    x64.loadRegBaseDisp.word64 rdx, [rbx + 96]
    x64.storeBaseDispReg.word64 [rcx + 56], rdx
    x64.storeBaseDispReg.word64 [rbx + 96], rcx
    x64.aluBaseDispImm32.add [rbx + 104], 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.jmp gfgrefillhdr
  gfglocalpop:
    x64.loadRegBaseDisp.word64 r8, [rbx + 96]
    x64.loadRegBaseDisp.word64 rax, [r8 + 56]
    x64.storeBaseDispReg.word64 [rbx + 96], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 56], rax
    x64.aluBaseDispImm32.add [rbx + 104], -1
    x64.aluBaseDispImm32.add [rbx + 168], 1
  gfgrecycle:
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  allggrow:
    x64.leaRegRegReg rcx, rcx, rcx
    x64.leaRegRegImm32 rcx, rcx, 64
    x64.imulRegRegImm32 rdx, rcx, 8
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 r8, 12288
    x64.movRegImm32 r9, 4
    x64.iatCall 4
    x64.movRegReg rdx, rax
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegGlobal rsi, __gt_allg
    x64.loadRegBaseDisp.word64 rsi, [rsi + 0]
    x64.cmpRegImm32 rdx, 0
    x64.movRegImm32 rdi, 0
    x64.jcc equal, allgdone
  allgcopyhead:
    x64.cmpRegReg rdi, rax
    x64.jcc less, allgcopybody
  allgpublish:
    x64.leaRegGlobal rax, __gt_allgcap
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.leaRegGlobal rax, __gt_allg
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.jmp allgstore
  allgcopybody:
    x64.imulRegRegImm32 r8, rdi, 8
    x64.leaRegRegReg r9, rsi, r8
    x64.loadRegBaseDisp.word64 r9, [r9 + 0]
    x64.leaRegRegReg r8, rdx, r8
    x64.storeBaseDispReg.word64 [r8 + 0], r9
    x64.leaRegRegImm32 rdi, rdi, 1
    x64.jmp allgcopyhead
}

func @__gt_cell_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.callDirect __gt_free_get
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.storeBaseDispReg.word64 [r8 + 64], rax
    x64.storeBaseDispReg.word64 [r8 + 40], rax
    x64.storeBaseDispReg.word64 [r8 + 88], rax
    x64.storeBaseDispReg.word64 [r8 + 184], rax
    x64.storeBaseDispReg.word64 [r8 + 256], rax
    x64.storeBaseDispReg.word64 [r8 + 344], rax
    x64.storeBaseDispReg.word64 [r8 + 368], rax
    x64.storeBaseDispReg.word64 [r8 + 424], rax
    x64.storeBaseDispReg.word64 [r8 + 432], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 264]
    x64.storeBaseDispReg.word64 [r8 + 264], rax
    x64.storeBaseDispReg.word64 [r8 + 176], rbx
    x64.movRegImm32 rax, 1
    x64.leaRegGlobal rcx, __gt_live_count
    x64.lock add [rcx], rax
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-reply-cell-writes-only-its-own-words.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_free_get {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 56
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gfgnoproc
  gfoffset:
    x64.mov rax, gs:[rax]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, gfgnoproc
  gfmachine:
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.cmpRegImm32 rbx, 0
    x64.jcc notEqual, gfgproc
  gfgnoproc:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.leaRegGlobal rax, __gt_gfree_head
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, gfgcarve
  gfgglobalpop:
    x64.loadRegBaseDisp.word64 rax, [rbx + 56]
    x64.leaRegGlobal rcx, __gt_gfree_head
    x64.storeBaseDispReg.word64 [rcx + 0], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 56], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, rbx
    x64.jmp gfgrecycle
  gfgproc:
    x64.loadRegBaseDisp.word64 rax, [rbx + 104]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gfglocalpop
  gfgrefill:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.movRegImm32 rax, 0
  gfgrefillhdr:
    x64.cmpRegImm32 rax, 32
    x64.jcc greaterEqual, gfgrefilldone
  gfgrefillpop:
    x64.leaRegGlobal rcx, __gt_gfree_head
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, gfgrefillpush
  gfgrefilldone:
    x64.loadRegBaseDisp.word64 rax, [rbx + 104]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, gfgrefilled
  gfgcarve:
    x64.movRegImm32 rcx, 472
    x64.callDirect __sched_arena_carve
    x64.movRegReg rbx, r8
    x64.leaRegGlobal rax, __gt_records_carved
    x64.aluBaseDispImm32.add [rax + 0], 1
    x64.leaRegGlobal rax, __gt_allglen
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_allgcap
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.cmpRegReg rax, rcx
    x64.jcc greaterEqual, allggrow
  allgstore:
    x64.leaRegGlobal rax, __gt_allg
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __gt_allglen
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.storeBaseIndexScaleReg.word64 [rax + rcx*8 + 0], rbx
    x64.leaRegRegImm32 rax, rcx, 1
    x64.leaRegGlobal rcx, __gt_allglen
    x64.storeBaseDispReg.word64 [rcx + 0], rax
  allgdone:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg r8, rbx
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  gfgrefilled:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.jmp gfglocalpop
  gfgrefillpush:
    x64.loadRegBaseDisp.word64 rdx, [rcx + 56]
    x64.leaRegGlobal rsi, __gt_gfree_head
    x64.storeBaseDispReg.word64 [rsi + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rcx + 56], rdx
    x64.loadRegBaseDisp.word64 rdx, [rbx + 96]
    x64.storeBaseDispReg.word64 [rcx + 56], rdx
    x64.storeBaseDispReg.word64 [rbx + 96], rcx
    x64.aluBaseDispImm32.add [rbx + 104], 1
    x64.leaRegRegImm32 rax, rax, 1
    x64.jmp gfgrefillhdr
  gfglocalpop:
    x64.loadRegBaseDisp.word64 r8, [rbx + 96]
    x64.loadRegBaseDisp.word64 rax, [r8 + 56]
    x64.storeBaseDispReg.word64 [rbx + 96], rax
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 56], rax
    x64.aluBaseDispImm32.add [rbx + 104], -1
    x64.aluBaseDispImm32.add [rbx + 168], 1
  gfgrecycle:
    x64.epilogue 56
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  allggrow:
    x64.leaRegRegReg rcx, rcx, rcx
    x64.leaRegRegImm32 rcx, rcx, 64
    x64.imulRegRegImm32 rsi, rcx, 8
    x64.storeSlotReg slot0, rax
    x64.storeSlotReg slot1, rcx
    x64.movRegImm32 rdi, 0
    x64.movRegImm32 rdx, 3
    x64.movRegImm32 r10, 34
    x64.movRegImm r8, 18446744073709551615
    x64.movRegImm32 r9, 0
    x64.x64Syscall 9
    x64.movRegReg rdx, rax
    x64.loadRegSlot rax, slot0
    x64.loadRegSlot rcx, slot1
    x64.leaRegGlobal rsi, __gt_allg
    x64.loadRegBaseDisp.word64 rsi, [rsi + 0]
    x64.cmpRegImm32 rdx, 0
    x64.movRegImm32 rdi, 0
    x64.jcc equal, allgdone
  allgcopyhead:
    x64.cmpRegReg rdi, rax
    x64.jcc less, allgcopybody
  allgpublish:
    x64.leaRegGlobal rax, __gt_allgcap
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.leaRegGlobal rax, __gt_allg
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.jmp allgstore
  allgcopybody:
    x64.imulRegRegImm32 r8, rdi, 8
    x64.leaRegRegReg r9, rsi, r8
    x64.loadRegBaseDisp.word64 r9, [r9 + 0]
    x64.leaRegRegReg r8, rdx, r8
    x64.storeBaseDispReg.word64 [r8 + 0], r9
    x64.leaRegRegImm32 rdi, rdi, 1
    x64.jmp allgcopyhead
}

func @__gt_cell_alloc {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.callDirect __gt_free_get
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r8 + 24], rax
    x64.storeBaseDispReg.word64 [r8 + 64], rax
    x64.storeBaseDispReg.word64 [r8 + 40], rax
    x64.storeBaseDispReg.word64 [r8 + 88], rax
    x64.storeBaseDispReg.word64 [r8 + 184], rax
    x64.storeBaseDispReg.word64 [r8 + 256], rax
    x64.storeBaseDispReg.word64 [r8 + 344], rax
    x64.storeBaseDispReg.word64 [r8 + 368], rax
    x64.storeBaseDispReg.word64 [r8 + 424], rax
    x64.storeBaseDispReg.word64 [r8 + 432], rax
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r8 + 16], rax
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.loadRegBaseDisp.word64 rax, [rax + 264]
    x64.storeBaseDispReg.word64 [r8 + 264], rax
    x64.storeBaseDispReg.word64 [r8 + 176], rbx
    x64.movRegImm32 rax, 1
    x64.leaRegGlobal rcx, __gt_live_count
    x64.lock add [rcx], rax
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_program_started@512 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-reply-cell-writes-only-its-own-words.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__gt_free_get {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq gfgnoproc
  gfoffset:
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
    arm64.cmp x0, 0
    arm64.b.eq gfgnoproc
  gfmachine:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.cmp x19, 0
    arm64.b.ne gfgproc
  gfgnoproc:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.leaGlobal x0, __gt_gfree_head
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.cmp x19, 0
    arm64.b.eq gfgcarve
  gfgglobalpop:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 56]
    arm64.leaGlobal x1, __gt_gfree_head
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 56], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.b gfgrecycle
  gfgproc:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 104]
    arm64.cmp x0, 0
    arm64.b.ne gfglocalpop
  gfgrefill:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.movImm x0, 0
  gfgrefillhdr:
    arm64.cmp x0, 32
    arm64.b.ge gfgrefilldone
  gfgrefillpop:
    arm64.leaGlobal x1, __gt_gfree_head
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.ne gfgrefillpush
  gfgrefilldone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 104]
    arm64.cmp x0, 0
    arm64.b.ne gfgrefilled
  gfgcarve:
    arm64.movImm x0, 472
    arm64.bl __sched_arena_carve
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __gt_records_carved
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __gt_allglen
    arm64.ldar.word64 x0, [x0]
    arm64.leaGlobal x1, __gt_allgcap
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x0, x1
    arm64.b.ge allggrow
  allgstore:
    arm64.leaGlobal x0, __gt_allg
    arm64.ldar.word64 x0, [x0]
    arm64.leaGlobal x1, __gt_allglen
    arm64.ldar.word64 x1, [x1]
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x19
    arm64.add x0, x1, 1
    arm64.leaGlobal x1, __gt_allglen
    arm64.stlr.word64 [x1], x0
  allgdone:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  gfgrefilled:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.b gfglocalpop
  gfgrefillpush:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 56]
    arm64.leaGlobal x3, __gt_gfree_head
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 56], x2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 96]
    arm64.storeBaseDispReg.word64 [x1 + 56], x2
    arm64.storeBaseDispReg.word64 [x19 + 96], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 104]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 104], x1
    arm64.add x0, x0, 1
    arm64.b gfgrefillhdr
  gfglocalpop:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 96]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.storeBaseDispReg.word64 [x19 + 96], x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 56], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 104]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x19 + 104], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 168]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 168], x1
  gfgrecycle:
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  allggrow:
    arm64.add x1, x1, x1
    arm64.add x1, x1, 64
    arm64.lsl x2, x1, 3
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movImm x0, 0
    arm64.movRegReg x1, x2
    arm64.movImm x2, 3
    arm64.movImm x3, 4098
    arm64.movImm x4, 18446744073709551615
    arm64.movImm x5, 0
    arm64.importCall 2
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.leaGlobal x3, __gt_allg
    arm64.ldar.word64 x3, [x3]
    arm64.cmp x2, 0
    arm64.movImm x4, 0
    arm64.b.eq allgdone
  allgcopyhead:
    arm64.cmp x4, x0
    arm64.b.lt allgcopybody
  allgpublish:
    arm64.leaGlobal x0, __gt_allgcap
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __gt_allg
    arm64.stlr.word64 [x0], x2
    arm64.b allgstore
  allgcopybody:
    arm64.lsl x5, x4, 3
    arm64.add x6, x3, x5
    arm64.loadRegBaseDisp.word64 x6, [x6 + 0]
    arm64.add x5, x2, x5
    arm64.storeBaseDispReg.word64 [x5 + 0], x6
    arm64.add x4, x4, 1
    arm64.b allgcopyhead
}

func @__gt_cell_alloc {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.bl __gt_free_get
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.storeBaseDispReg.word64 [x0 + 64], x1
    arm64.storeBaseDispReg.word64 [x0 + 40], x1
    arm64.storeBaseDispReg.word64 [x0 + 88], x1
    arm64.storeBaseDispReg.word64 [x0 + 184], x1
    arm64.storeBaseDispReg.word64 [x0 + 256], x1
    arm64.storeBaseDispReg.word64 [x0 + 344], x1
    arm64.storeBaseDispReg.word64 [x0 + 368], x1
    arm64.storeBaseDispReg.word64 [x0 + 424], x1
    arm64.storeBaseDispReg.word64 [x0 + 432], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 264]
    arm64.storeBaseDispReg.word64 [x0 + 264], x1
    arm64.storeBaseDispReg.word64 [x0 + 176], x19
    arm64.movImm x1, 1
    arm64.leaGlobal x2, __gt_live_count
    arm64.arm64AtomicRmw.add x1, [x2], x1
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-reply-cell-writes-only-its-own-words.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__gt_free_get {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.eq gfgnoproc
  gfoffset:
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
    arm64.cmp x0, 0
    arm64.b.eq gfgnoproc
  gfmachine:
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.cmp x19, 0
    arm64.b.ne gfgproc
  gfgnoproc:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.leaGlobal x0, __gt_gfree_head
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.cmp x19, 0
    arm64.b.eq gfgcarve
  gfgglobalpop:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 56]
    arm64.leaGlobal x1, __gt_gfree_head
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 56], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.b gfgrecycle
  gfgproc:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 104]
    arm64.cmp x0, 0
    arm64.b.ne gfglocalpop
  gfgrefill:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.movImm x0, 0
  gfgrefillhdr:
    arm64.cmp x0, 32
    arm64.b.ge gfgrefilldone
  gfgrefillpop:
    arm64.leaGlobal x1, __gt_gfree_head
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.ne gfgrefillpush
  gfgrefilldone:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 104]
    arm64.cmp x0, 0
    arm64.b.ne gfgrefilled
  gfgcarve:
    arm64.movImm x0, 472
    arm64.bl __sched_arena_carve
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __gt_records_carved
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __gt_allglen
    arm64.ldar.word64 x0, [x0]
    arm64.leaGlobal x1, __gt_allgcap
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x0, x1
    arm64.b.ge allggrow
  allgstore:
    arm64.leaGlobal x0, __gt_allg
    arm64.ldar.word64 x0, [x0]
    arm64.leaGlobal x1, __gt_allglen
    arm64.ldar.word64 x1, [x1]
    arm64.storeBaseIndexScaleReg.word64 [x0 + x1*8 + 0], x19
    arm64.add x0, x1, 1
    arm64.leaGlobal x1, __gt_allglen
    arm64.stlr.word64 [x1], x0
  allgdone:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  gfgrefilled:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.b gfglocalpop
  gfgrefillpush:
    arm64.loadRegBaseDisp.word64 x2, [x1 + 56]
    arm64.leaGlobal x3, __gt_gfree_head
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x1 + 56], x2
    arm64.loadRegBaseDisp.word64 x2, [x19 + 96]
    arm64.storeBaseDispReg.word64 [x1 + 56], x2
    arm64.storeBaseDispReg.word64 [x19 + 96], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 104]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 104], x1
    arm64.add x0, x0, 1
    arm64.b gfgrefillhdr
  gfglocalpop:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 96]
    arm64.loadRegBaseDisp.word64 x1, [x0 + 56]
    arm64.storeBaseDispReg.word64 [x19 + 96], x1
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 56], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 104]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x19 + 104], x1
    arm64.loadRegBaseDisp.word64 x1, [x19 + 168]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x19 + 168], x1
  gfgrecycle:
    arm64.loadRegSlot x19, slot2
    arm64.epilogue 48
    arm64.ret
  allggrow:
    arm64.add x1, x1, x1
    arm64.add x1, x1, 64
    arm64.lsl x2, x1, 3
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movImm x0, 0
    arm64.movRegReg x1, x2
    arm64.movImm x2, 3
    arm64.movImm x3, 34
    arm64.movImm x4, 18446744073709551615
    arm64.movImm x5, 0
    arm64.syscall 222
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
    arm64.leaGlobal x3, __gt_allg
    arm64.ldar.word64 x3, [x3]
    arm64.cmp x2, 0
    arm64.movImm x4, 0
    arm64.b.eq allgdone
  allgcopyhead:
    arm64.cmp x4, x0
    arm64.b.lt allgcopybody
  allgpublish:
    arm64.leaGlobal x0, __gt_allgcap
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __gt_allg
    arm64.stlr.word64 [x0], x2
    arm64.b allgstore
  allgcopybody:
    arm64.lsl x5, x4, 3
    arm64.add x6, x3, x5
    arm64.loadRegBaseDisp.word64 x6, [x6 + 0]
    arm64.add x5, x2, x5
    arm64.storeBaseDispReg.word64 [x5 + 0], x6
    arm64.add x4, x4, 1
    arm64.b allgcopyhead
}

func @__gt_cell_alloc {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.bl __gt_free_get
    arm64.movImm x1, 0
    arm64.storeBaseDispReg.word64 [x0 + 24], x1
    arm64.storeBaseDispReg.word64 [x0 + 64], x1
    arm64.storeBaseDispReg.word64 [x0 + 40], x1
    arm64.storeBaseDispReg.word64 [x0 + 88], x1
    arm64.storeBaseDispReg.word64 [x0 + 184], x1
    arm64.storeBaseDispReg.word64 [x0 + 256], x1
    arm64.storeBaseDispReg.word64 [x0 + 344], x1
    arm64.storeBaseDispReg.word64 [x0 + 368], x1
    arm64.storeBaseDispReg.word64 [x0 + 424], x1
    arm64.storeBaseDispReg.word64 [x0 + 432], x1
    arm64.movImm x1, 3
    arm64.storeBaseDispReg.word64 [x0 + 16], x1
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
    arm64.loadRegBaseDisp.word64 x1, [x1 + 264]
    arm64.storeBaseDispReg.word64 [x0 + 264], x1
    arm64.storeBaseDispReg.word64 [x0 + 176], x19
    arm64.movImm x1, 1
    arm64.leaGlobal x2, __gt_live_count
    arm64.arm64AtomicRmw.add x1, [x2], x1
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: a-wake-with-no-idle-processor-takes-no-lock -->
**A PUBLISH THAT HAS NO IDLE PROCESSOR TO WAKE TAKES NO LOCK — Go's `wakep` early-out.** Every green-thread
publish ends in `__sched_wake_or_spawn`, which decides whether another machine must be started for the work
just published. A machine can only be started on an idle processor, so when `__sched_npidle` reads 0 there
is nothing to do, and the body returns after one unlocked load, before it reserves a spinner and before
`__sched_startm` takes `__sched_lock` to look for a processor it would not find. At one processor that is
every publish of the program's life.

What this pin shows is the order: the `__sched_npidle` load and its test come before the spinner
reservation's compare-and-swap and before any call that takes `__sched_lock`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__sched_wake_or_spawn
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-wake-with-no-idle-processor-takes-no-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_wake_or_spawn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __sched_shutdown_flag
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, done
  idletest:
    x64.leaRegGlobal rax, __sched_npidle
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  spintest:
    x64.leaRegGlobal rax, __sched_nmspinning
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, done
  spinclaim:
    x64.leaRegGlobal rcx, __sched_nmspinning
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rdx, 1
    x64.lock cmpxchg [rcx], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  wakestart:
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 1
    x64.callDirect __sched_startm
  done:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-wake-with-no-idle-processor-takes-no-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__sched_wake_or_spawn {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegGlobal rax, __sched_shutdown_flag
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, done
  idletest:
    x64.leaRegGlobal rax, __sched_npidle
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  spintest:
    x64.leaRegGlobal rax, __sched_nmspinning
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, done
  spinclaim:
    x64.leaRegGlobal rcx, __sched_nmspinning
    x64.movRegImm32 rax, 0
    x64.movRegImm32 rdx, 1
    x64.lock cmpxchg [rcx], rdx
    x64.setccReg equal, rax
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, done
  wakestart:
    x64.movRegImm32 rcx, 0
    x64.movRegImm32 rdx, 1
    x64.callDirect __sched_startm
  done:
    x64.epilogue 32
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_program_started@512 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-wake-with-no-idle-processor-takes-no-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_wake_or_spawn {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __sched_shutdown_flag
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.ne done
  idletest:
    arm64.leaGlobal x0, __sched_npidle
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.eq done
  spintest:
    arm64.leaGlobal x0, __sched_nmspinning
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne done
  spinclaim:
    arm64.leaGlobal x0, __sched_nmspinning
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.arm64AtomicCas x0, [x0], x1, x2
    arm64.cmp x0, 0
    arm64.b.eq done
  wakestart:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.bl __sched_startm
  done:
    arm64.epilogue 16
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-wake-with-no-idle-processor-takes-no-lock.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__sched_wake_or_spawn {
  entry:
    arm64.prologue 16
    arm64.leaGlobal x0, __sched_shutdown_flag
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.ne done
  idletest:
    arm64.leaGlobal x0, __sched_npidle
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.eq done
  spintest:
    arm64.leaGlobal x0, __sched_nmspinning
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne done
  spinclaim:
    arm64.leaGlobal x0, __sched_nmspinning
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.arm64AtomicCas x0, [x0], x1, x2
    arm64.cmp x0, 0
    arm64.b.eq done
  wakestart:
    arm64.movImm x0, 0
    arm64.movImm x1, 1
    arm64.bl __sched_startm
  done:
    arm64.epilogue 16
    arm64.ret
}
```

<!-- test: a-strand-turn-takes-the-run-queue-lock-once -->
**A STRAND TURN THAT ENDS IN A PARK TAKES `__sched_lock` ONCE.** When a member of a strand parks, the strand
runner must do two things under the run-queue lock: register the park on the store the member waits on,
and pop what the strand runs next — or give the strand up when its queue is empty. Both are one critical
section's work, so the runner takes the lock once for the pair rather than once for each: a service that
receives a message and parks on its empty mailbox pays one `__sched_lock` enter and leave for that turn, not
two.

What this pin shows is the runner's park road: `__gt_run_strand` from the switch back to the next member,
with `__gt_register_park` and `__gt_strand_next` beside it. A park road that calls a registration taking
the lock and then a pop taking it again is the second lock pair this case exists to keep out.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	var calls as Integer

	static function create() returns Self
		return Self{calls: 0}
	end 'create'

	export function next(n Integer) returns Integer
		self.calls = self.calls + 1

		return n + 1
	end 'next'
end 'Counter'

function main() returns ExitCode
	let c = spawn Counter.create()
	let a = try await c.next(1) otherwise 0

	return a as ExitCode
end 'main'
```
```exitcode
2
```
```RequiredRuntime
__gt_run_strand
__gt_strand_next
__gt_register_park
```
```TargetIr:x64-windows
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 16384
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __sched_preempt_ext_lock@488 = i64 0
  __sched_preempt_context@496 = i64 0
  __slab_arena_list@504 = i64 0
  __slab_arena_map_l1@512 = i64 0
  __slab_state@520 = i64 0
  __mrt_console_probe_stdin@528 = i8 0
  __mrt_console_probe_stdout@529 = i8 0
  __mrt_console_probe_stderr@530 = i8 0
  __mrt_program_started@531 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-strand-turn-takes-the-run-queue-lock-once.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_strand_next {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  snfirst:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.cmpRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jcc equal, critsplit
  snregister:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_register_park
    x64.movRegReg r12, r8
    x64.jmp sntake
  critsplit:
    x64.movRegReg r12, r13
  sntake:
    x64.loadRegBaseDisp.word64 r13, [rbx + 416]
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, snresume
  sndequeue:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_coro_dequeue
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, sngot
  snrelease:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 352], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsnrelease
  wakesnrelease:
    x64.callDirect __sched_wake_or_spawn
  paidsnrelease:
    x64.movRegImm rax, 18446744073709551615
    x64.leaRegRegImm32 rcx, rbx, 360
    x64.lock xadd [rcx], rax <- rax
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, strandunreffed
  strandlastref:
    x64.leaRegRegImm32 rax, rbx, 256
    x64.movRegImm32 rcx, 2
    x64.lock xadd [rax], rcx <- rcx
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, strandunreffed
  teardownreclaim:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_reclaim
  strandunreffed:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  sngot:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsngot
  wakesngot:
    x64.callDirect __sched_wake_or_spawn
  paidsngot:
    x64.movRegReg rcx, r13
    x64.jmp sncandidate
  snresume:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 416], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsnresume
  wakesnresume:
    x64.callDirect __sched_wake_or_spawn
  paidsnresume:
    x64.movRegReg rcx, r13
  sncandidate:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, tscheckdropped
  tsnowork:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tscheckdropped:
    x64.loadRegBaseDisp.word64 rax, [rcx + 256]
    x64.cmpRegImm32 rax, 1
    x64.setccReg equal, rax
    x64.loadRegBaseDisp.word64 rdx, [rcx + 48]
    x64.cmpRegImm32 rdx, 0
    x64.setccReg equal, rdx
    x64.andRegReg rax, rax, rdx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tshand
  tsreclaim:
    x64.callDirect __gt_runner_done
  snpop:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, rax
    x64.jmp sntake
  tshand:
    x64.leaRegGlobal rax, __gt_hold_epoch
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tshexamine
  tshhandover:
    x64.movRegReg r8, rcx
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  timerdueask:
    x64.callDirect __gt_now_ns
    x64.cmpRegReg r12, r8
    x64.jcc lessEqual, tshfiredue
    x64.jmp tshgiveupstrand
  hpheldwork:
    x64.leaRegRdata rcx, [rip + __abort_msg_95]  ; "fatal error: runtime abort 95 (schedulerIdledAProcessorHoldingWork)\x0a"
    x64.movRegImm32 rdx, 68
    x64.movRegImm32 rax, 95
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  hpidled:
    x64.leaRegGlobal rax, __sched_regain_head
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, pidleputregain
  pidleputidle:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 72], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeBaseDispReg.word64 [rcx + 160], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.movRegImm32 rax, 1
    x64.leaRegGlobal rcx, __sched_npidle
    x64.lock add [rcx], rax
    x64.jmp pidleputplaced
  pidleputregain:
    x64.loadRegBaseDisp.word64 rdx, [rax + 584]
    x64.leaRegGlobal rsi, __sched_regain_head
    x64.storeBaseDispReg.word64 [rsi + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 584], rdx
    x64.storeBaseDispReg.word64 [rax + 512], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 496]
    x64.iatCall 19
  pidleputplaced:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.iatCall 19
    x64.callDirect __np_break
    x64.jmp tshgavestrand
  munlinkhead:
    x64.leaRegGlobal rdi, __sched_midle
    x64.storeBaseDispReg.word64 [rdi + 0], rdx
    x64.jmp munlinkmend
  munlinkinner:
    x64.storeBaseDispReg.word64 [rsi + 536], rdx
  munlinkmend:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, munlinkclear
  munlinknext:
    x64.storeBaseDispReg.word64 [rdx + 568], rsi
  munlinkclear:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r12 + 536], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r12 + 568], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 184], rdx
    x64.storeBaseDispReg.word64 [rcx + 176], rax
    x64.storeBaseDispReg.word64 [r12 + 512], rcx
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.iatCall 19
    x64.jmp tshgavestrand
  tshexamine:
    x64.loadRegBaseDisp.word64 rax, [rcx + 440]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, tshcheckpin
  tshholdback:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 592
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.iatCall 21
    x64.loadRegSlot rcx, slot0
    x64.jmp tshputback
  tshcheckpin:
    x64.loadRegBaseDisp.word64 rax, [rcx + 448]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tshhandover
  tshcomparepin:
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegReg rax, rdx
    x64.jcc equal, tshhandover
  tshstartlocked:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 592
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.iatCall 21
    x64.loadRegSlot rcx, slot0
  tshputback:
    x64.loadRegBaseDisp.word64 r12, [rcx + 448]
    x64.callDirect __gt_coro_enqueue
    x64.cmpRegImm32 r12, 0
    x64.jcc notEqual, tshwakeowner
  tshnoowner:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.jmp tshbackoff
  tshwakeowner:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, hpexamine
  hpkeep:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.iatCall 19
  tshbackoff:
    x64.movRegImm32 rax, 200
    x64.movRegImm32 rax, 0
    x64.iatCall 77
    x64.shlRegImm8 rax, rax, 32
    x64.sarRegImm8 rax, rax, 32
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegImm rax, 9223372036854775807
    x64.cmpRegReg r12, rax
    x64.jcc equal, tshgiveupstrand
    x64.jmp timerdueask
  hpexamine:
    x64.leaRegGlobal rdx, __sched_midle
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.cmpRegReg rdx, r12
    x64.setccReg equal, rdx
    x64.loadRegBaseDisp.word64 rsi, [r12 + 568]
    x64.cmpRegImm32 rsi, 0
    x64.setccReg notEqual, rsi
    x64.orRegReg rdx, rdx, rsi
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, hpgive
  hpdrain:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __sched_drain_runq
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot0
  hprelease:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 184], rdx
    x64.storeBaseDispReg.word64 [rcx + 176], rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __sched_runq_empty
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r8, 1
    x64.jcc notEqual, hpheldwork
    x64.jmp hpidled
  hpgive:
    x64.leaRegGlobal rdx, __sched_nmidle
    x64.aluBaseDispImm32.add [rdx + 0], -1
    x64.loadRegBaseDisp.word64 rdx, [r12 + 536]
    x64.loadRegBaseDisp.word64 rsi, [r12 + 568]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, munlinkhead
    x64.jmp munlinkinner
  tshfiredue:
    x64.movRegReg rcx, r8
    x64.callDirect __gt_timer_check
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, tshgiveupstrand
  tshpaywake:
    x64.callDirect __sched_wake_or_spawn
  tshgiveupstrand:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tshgavestrand:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 rax, 200
    x64.movRegImm32 rax, 0
    x64.iatCall 77
    x64.shlRegImm8 rax, rax, 32
    x64.sarRegImm8 rax, rax, 32
    x64.movRegImm r8, 18446744073709551615
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_run_strand {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegRegImm32 r13, rbx, 24
    x64.movRegImm32 r14, 0
    x64.movRegReg rdx, r14
  strandnext:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_strand_next
    x64.movRegReg r15, r8
    x64.storeSlotReg slot1, r15
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, strandsort
  strandreleased:
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  strandsort:
    x64.cmpRegImm32 r15, -1
    x64.jcc equal, strandhandedoff
  strandrun:
    x64.loadRegBaseDisp.word64 rax, [r15 + 24]
    x64.leaRegRegImm32 rax, rax, 9120
    x64.storeBaseDispReg.word64 [r15 + 80], rax
    x64.storeBaseDispReg.word64 [r15 + 48], r13
    x64.storeBaseDispReg.word64 [rbx + 8], r15
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __gt_context_switch
    x64.storeBaseDispReg.word64 [rbx + 8], r13
    x64.loadRegBaseDisp.word64 rax, [r15 + 16]
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, strandparked
  strandcompleted:
    x64.loadRegBaseDisp.word64 rax, [rbx + 624]
    x64.cmpRegReg rax, r15
    x64.jcc equal, stranddroppin
  strandreap:
    x64.movRegReg rcx, r15
    x64.callDirect __gt_runner_done
    x64.movRegReg rdx, r14
    x64.jmp strandnext
  strandparked:
    x64.loadRegBaseDisp.word64 rax, [r15 + 32]
    x64.cmpRegImm32 rax, 16384
    x64.setccReg greater, rax
    x64.loadRegBaseDisp.word64 rcx, [r15 + 288]
    x64.cmpRegImm32 rcx, 9
    x64.setccReg notEqual, rcx
    x64.andRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, stranddispatch
  strandshrink:
    x64.movRegReg rcx, r15
    x64.callDirect __gt_stack_shrink
  stranddispatch:
    x64.loadRegBaseDisp.word64 rax, [r15 + 288]
    x64.cmpRegImm32 rax, 7
    x64.jcc notEqual, strandnotyield
  strandyield:
    x64.movRegImm32 rax, 0
    x64.storeSlotReg slot0, rax
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r15, [rax + 0]
    x64.movRegImm rax, 9223372036854775807
    x64.cmpRegReg r15, rax
    x64.jcc equal, critsplit#23
  timerdueask:
    x64.callDirect __gt_now_ns
    x64.cmpRegReg r15, r8
    x64.jcc greater, critsplit#24
  pollfire:
    x64.movRegReg rcx, r8
    x64.callDirect __gt_timer_check
    x64.jmp strandpoll
  critsplit#23:
    x64.loadRegSlot rax, slot0
    x64.movRegReg r8, rax
    x64.jmp strandpoll
  critsplit#24:
    x64.loadRegSlot rax, slot0
    x64.movRegReg r8, rax
  strandpoll:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, strandplace
  strandpollwake:
    x64.callDirect __sched_wake_or_spawn
  strandplace:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.loadRegBaseDisp.word64 rax, [r12 + 272]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, r15
    x64.loadRegSlot rcx, slot1
    x64.callDirect __gt_coro_enqueue
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, strandbehindeveryone
  strandbehindsiblings:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rdx, r14
    x64.jmp strandnext
  strandnotyield:
    x64.cmpRegImm32 rax, 9
    x64.jcc notEqual, strandregister
  strandpreempted:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 21
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [r12 + 416], rax
  strandbehindeveryone:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  strandregister:
    x64.loadRegSlot rdx, slot1
    x64.jmp strandnext
  stranddroppin:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 456], rax
    x64.storeBaseDispReg.word64 [r15 + 448], rax
    x64.storeBaseDispReg.word64 [rbx + 624], rax
    x64.movRegImm rax, 18446744073709551615
    x64.leaRegGlobal rcx, __gt_hold_epoch
    x64.lock add [rcx], rax
    x64.jmp strandreap
  strandhandedoff:
    x64.movRegImm r8, 18446744073709551615
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_register_park {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 288]
    x64.loadRegBaseDisp.word64 r12, [rbx + 296]
    x64.movRegImm32 r13, 3
    x64.movRegImm32 r14, 0
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, notparktimer
  parktimer:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __gt_timer_add
    x64.cmpRegReg r12, r13
    x64.jcc greaterEqual, parktimerpaid
  parktimerwake:
    x64.movRegReg rcx, r12
    x64.callDirect __sched_wake_netpoller
  parktimerpaid:
    x64.movRegReg r8, r14
    x64.jmp parkdone
  notparktimer:
    x64.cmpRegImm32 rax, 8
    x64.setccReg equal, rcx
    x64.cmpRegImm32 rax, 10
    x64.setccReg equal, rdx
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, notparknetpoll
  parknetpoll:
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __np_pd_commit
    x64.movRegReg rbx, r8
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegReg rcx, r12
    x64.jcc greaterEqual, parknetpollpaid
  parknetpollwake:
    x64.callDirect __sched_wake_netpoller
  parknetpollpaid:
    x64.movRegReg r8, rbx
    x64.jmp parkdone
  notparknetpoll:
    x64.cmpRegImm32 rax, 4
    x64.jcc notEqual, notparkmailbox
  parkmailbox:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, r12, 64
    x64.iatCall 21
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkmboxundo
  parkmboxclosed:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkmboxundo
  parkmboxwait:
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r12 + 16], rbx
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.leaRegRegImm32 rcx, r12, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 r8, 0
    x64.jmp parkdone
  parkmboxundo:
    x64.leaRegRegImm32 rcx, r12, 64
    x64.iatCall 22
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready_locked
    x64.jmp parkdone
  notparkmailbox:
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, notparkawait
  parkawait:
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, 2
    x64.jcc equal, parkresumenow
  parkawaitregister:
    x64.loadRegBaseDisp.word64 rax, [r12 + 368]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, claimawaitertake#30
  claimawaitertwice#31:
    x64.leaRegRdata rcx, [rip + __abort_msg_102]  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    x64.movRegImm32 rdx, 62
    x64.movRegImm32 rax, 102
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  claimawaitertake#30:
    x64.storeBaseDispReg.word64 [r12 + 368], rbx
  parkawaittake:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
    x64.jmp parkdone
  notparkawait:
    x64.cmpRegImm32 rax, 6
    x64.jcc notEqual, notparkawaitany
  parkawaitany:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_any_completed
    x64.cmpRegImm32 r8, -1
    x64.jcc notEqual, parkresumenow
  parkawaitanyregister:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 376], rax
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.movRegReg rdx, rbx
    x64.orRegImm32 rdx, rbx, 1
    x64.movRegImm32 rsi, 0
  anywalkhdr:
    x64.cmpRegReg rsi, rcx
    x64.jcc less, anywalkbody
  parkawaitanyregistered:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
    x64.jmp parkdone
  anywalkbody:
    x64.loadRegBaseIndexScale.word64 rdi, [rax + rsi*8 + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, anywalknext
  anywalkslot:
    x64.loadRegBaseDisp.word64 r8, [rdi + 368]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, claimawaitertake#36
  claimawaitertwice#37:
    x64.leaRegRdata rcx, [rip + __abort_msg_102]  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    x64.movRegImm32 rdx, 62
    x64.movRegImm32 rax, 102
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  claimawaitertake#36:
    x64.storeBaseDispReg.word64 [rdi + 368], rdx
  anywalknext:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.jmp anywalkhdr
  notparkawaitany:
    x64.cmpRegImm32 rax, 11
    x64.jcc notEqual, parkunknown
  parkquiesce:
    x64.leaRegGlobal rax, __gt_quiesce_waiter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkquiescetaken
  parkquiesceregister:
    x64.leaRegGlobal rax, __gt_quiesce_waiter
    x64.lock add [rax], rbx
    x64.leaRegGlobal rax, __gt_live_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkquiescetake
  parkquiesceundo:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rbx
    x64.leaRegGlobal rcx, __gt_quiesce_waiter
    x64.lock add [rcx], rax
  parkresumenow:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready_locked
    x64.jmp parkdone
  parkquiescetake:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
  parkdone:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parkunknown:
    x64.leaRegRdata rcx, [rip + __abort_msg_93]  ; "fatal error: runtime abort 93 (schedulerUnknownParkKind)\x0a"
    x64.movRegImm32 rdx, 57
    x64.movRegImm32 rax, 93
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parkquiescetaken:
    x64.leaRegRdata rcx, [rip + __abort_msg_123]  ; "fatal error: runtime abort 123 (schedulerQuiesceWaiterTaken)\x0a"
    x64.movRegImm32 rdx, 61
    x64.movRegImm32 rax, 123
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:x64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 40
  __il_body:
    x64.movRegImm32 rbx, 0
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont:
    x64.leaRegFunc rcx, [rip + Counter.__loop]
    x64.leaRegFunc rax, [rip + Counter.__abandon]
    x64.movRegReg rdx, r8
    x64.callDirect __svc_spawn
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r12, r8
    x64.storeBaseDispReg.word64 [r12 + 0], rbx
    x64.movRegImm32 rbx, 1
    x64.movRegImm32 rcx, 0
    x64.callDirect __gt_cell_alloc
    x64.movRegReg r13, r8
    x64.movRegImm32 rcx, 32
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegImm32 rax, 1
    x64.storeBaseDispReg.word64 [r8 + 0], rax
    x64.storeBaseDispReg.word64 [r8 + 16], rbx
    x64.storeBaseDispReg.word64 [r8 + 24], r13
    x64.loadRegBaseDisp.word64 rcx, [r12 + 0]
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_send
    x64.movRegReg rcx, r13
    x64.callDirect __gt_try_await
    x64.cmpRegImm32 r10, 0
    x64.jcc equal, tryok
  tryerr:
    x64.movRegImm32 rbx, 0
    x64.jmp trycont
  tryok:
    x64.movRegReg rbx, r8
  trycont:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_handle_drop_box
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at a-strand-turn-takes-the-run-queue-lock-once.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @Counter.__loop {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  serve:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, stopping
  dispatch:
    x64.loadRegBaseDisp.word64 rax, [r8 + 0]
    x64.jumpTable rax, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.storeBaseDispReg.word64 [r12 + 56], rax
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
  stopping:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_close
  drain:
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_recv
    x64.cmpRegImm32 r8, 0
    x64.jcc notEqual, abandon
  finished:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rcx, r12
    x64.callDirect __mbox_service_exit
    x64.movRegReg rax, r8
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  abandon:
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __mbox_abandon
    x64.jmp drain
  next:
    x64.loadRegBaseDisp.word64 rax, [r8 + 16]
    x64.loadRegBaseDisp.word64 r13, [r8 + 24]
  __il_body:
    x64.aluBaseDispImm32.add [rbx + 0], 1
    x64.leaRegRegImm32 r14, rax, 1
  __il_cont:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r14
    x64.callDirect __gt_cell_complete
    x64.jmp serve
}

func @Counter.__abandon {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.prologue 40
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 0]
    x64.jumpTable rax, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 16]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
    x64.jmp abandondrop
  next:
    x64.loadRegBaseDisp.word64 rcx, [rbx + 24]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 1
    x64.callDirect __gt_cell_complete
  abandondrop:
    x64.movRegReg rcx, rbx
    x64.callDirect __mm_decref
    x64.movRegReg rax, r8
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_strand_next {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.prologue 88
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
  snfirst:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.cmpRegImm32 r12, 0
    x64.movRegImm32 r13, 0
    x64.jcc equal, critsplit
  snregister:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_register_park
    x64.movRegReg r12, r8
    x64.jmp sntake
  critsplit:
    x64.movRegReg r12, r13
  sntake:
    x64.loadRegBaseDisp.word64 r13, [rbx + 416]
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, snresume
  sndequeue:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_coro_dequeue
    x64.movRegReg r13, r8
    x64.cmpRegImm32 r13, 0
    x64.jcc notEqual, sngot
  snrelease:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 352], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsnrelease
  wakesnrelease:
    x64.callDirect __sched_wake_or_spawn
  paidsnrelease:
    x64.movRegImm rax, 18446744073709551615
    x64.leaRegRegImm32 rcx, rbx, 360
    x64.lock xadd [rcx], rax <- rax
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, strandunreffed
  strandlastref:
    x64.leaRegRegImm32 rax, rbx, 256
    x64.movRegImm32 rcx, 2
    x64.lock xadd [rax], rcx <- rcx
    x64.cmpRegImm32 rcx, 1
    x64.jcc notEqual, strandunreffed
  teardownreclaim:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_reclaim
  strandunreffed:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  sngot:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsngot
  wakesngot:
    x64.callDirect __sched_wake_or_spawn
  paidsngot:
    x64.movRegReg rcx, r13
    x64.jmp sncandidate
  snresume:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 416], rax
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.cmpRegImm32 r12, 0
    x64.jcc equal, paidsnresume
  wakesnresume:
    x64.callDirect __sched_wake_or_spawn
  paidsnresume:
    x64.movRegReg rcx, r13
  sncandidate:
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, tscheckdropped
  tsnowork:
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tscheckdropped:
    x64.loadRegBaseDisp.word64 rax, [rcx + 256]
    x64.cmpRegImm32 rax, 1
    x64.setccReg equal, rax
    x64.loadRegBaseDisp.word64 rdx, [rcx + 48]
    x64.cmpRegImm32 rdx, 0
    x64.setccReg equal, rdx
    x64.andRegReg rax, rax, rdx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tshand
  tsreclaim:
    x64.callDirect __gt_runner_done
  snpop:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.movRegImm32 rax, 0
    x64.movRegReg r12, rax
    x64.jmp sntake
  tshand:
    x64.leaRegGlobal rax, __gt_hold_epoch
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, tshexamine
  tshhandover:
    x64.movRegReg r8, rcx
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  timerdueask:
    x64.callDirect __gt_now_ns
    x64.cmpRegReg r12, r8
    x64.jcc lessEqual, tshfiredue
    x64.jmp tshgiveupstrand
  hpheldwork:
    x64.leaRegRdata rcx, [rip + __abort_msg_95]  ; "fatal error: runtime abort 95 (schedulerIdledAProcessorHoldingWork)\x0a"
    x64.movRegImm32 rdx, 68
    x64.movRegImm32 rax, 95
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  hpidled:
    x64.leaRegGlobal rax, __sched_regain_head
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, pidleputregain
  pidleputidle:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rcx + 72], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeBaseDispReg.word64 [rcx + 160], rax
    x64.leaRegGlobal rax, __sched_pidle
    x64.storeBaseDispReg.word64 [rax + 0], rcx
    x64.movRegImm32 rax, 1
    x64.leaRegGlobal rcx, __sched_npidle
    x64.lock add [rcx], rax
    x64.jmp pidleputplaced
  pidleputregain:
    x64.loadRegBaseDisp.word64 rdx, [rax + 584]
    x64.leaRegGlobal rsi, __sched_regain_head
    x64.storeBaseDispReg.word64 [rsi + 0], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 584], rdx
    x64.storeBaseDispReg.word64 [rax + 512], rcx
    x64.loadRegBaseDisp.word64 rcx, [rax + 496]
    x64.callDirect mrt_host_event_set
  pidleputplaced:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.callDirect mrt_host_event_set
    x64.callDirect __np_break
    x64.jmp tshgavestrand
  munlinkhead:
    x64.leaRegGlobal rdi, __sched_midle
    x64.storeBaseDispReg.word64 [rdi + 0], rdx
    x64.jmp munlinkmend
  munlinkinner:
    x64.storeBaseDispReg.word64 [rsi + 536], rdx
  munlinkmend:
    x64.cmpRegImm32 rdx, 0
    x64.jcc equal, munlinkclear
  munlinknext:
    x64.storeBaseDispReg.word64 [rdx + 568], rsi
  munlinkclear:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r12 + 536], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [r12 + 568], rdx
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 184], rdx
    x64.storeBaseDispReg.word64 [rcx + 176], rax
    x64.storeBaseDispReg.word64 [r12 + 512], rcx
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.callDirect mrt_host_event_set
    x64.jmp tshgavestrand
  tshexamine:
    x64.loadRegBaseDisp.word64 rax, [rcx + 440]
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, tshcheckpin
  tshholdback:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 592
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegSlot rcx, slot0
    x64.jmp tshputback
  tshcheckpin:
    x64.loadRegBaseDisp.word64 rax, [rcx + 448]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, tshhandover
  tshcomparepin:
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegReg rax, rdx
    x64.jcc equal, tshhandover
  tshstartlocked:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rdx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.mov rdx, gs:[rdx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rdx, 0
    x64.setccReg notEqual, rsi
    x64.andRegReg rax, rax, rsi
    x64.movRegImm32 rsi, 0
    x64.subRegReg rsi, rsi, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rdx, rdx, 592
    x64.leaRegGlobal rdi, __sched_lock_depth_sink
    x64.andRegReg rdx, rdx, rsi
    x64.andRegReg rdi, rdi, rax
    x64.orRegReg rdx, rdx, rdi
    x64.aluBaseDispImm32.add [rdx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.storeSlotReg slot0, rcx
    x64.movRegReg rcx, rax
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegSlot rcx, slot0
  tshputback:
    x64.loadRegBaseDisp.word64 r12, [rcx + 448]
    x64.callDirect __gt_coro_enqueue
    x64.cmpRegImm32 r12, 0
    x64.jcc notEqual, tshwakeowner
  tshnoowner:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.jmp tshbackoff
  tshwakeowner:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.mov rax, gs:[rax]
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegImm32 rcx, 0
    x64.jcc notEqual, hpexamine
  hpkeep:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.loadRegBaseDisp.word64 rcx, [r12 + 496]
    x64.callDirect mrt_host_event_set
  tshbackoff:
    x64.movRegImm32 rcx, 200
    x64.movRegImm32 rax, 0
    x64.callDirect mrt_host_netpoll_sleep
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegImm rax, 9223372036854775807
    x64.cmpRegReg r12, rax
    x64.jcc equal, tshgiveupstrand
    x64.jmp timerdueask
  hpexamine:
    x64.leaRegGlobal rdx, __sched_midle
    x64.loadRegBaseDisp.word64 rdx, [rdx + 0]
    x64.cmpRegReg rdx, r12
    x64.setccReg equal, rdx
    x64.loadRegBaseDisp.word64 rsi, [r12 + 568]
    x64.cmpRegImm32 rsi, 0
    x64.setccReg notEqual, rsi
    x64.orRegReg rdx, rdx, rsi
    x64.cmpRegImm32 rdx, 0
    x64.jcc notEqual, hpgive
  hpdrain:
    x64.storeSlotReg slot1, rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __sched_drain_runq
    x64.loadRegSlot rax, slot1
    x64.loadRegSlot rcx, slot0
  hprelease:
    x64.movRegImm32 rdx, 0
    x64.storeBaseDispReg.word64 [rax + 0], rdx
    x64.storeBaseDispReg.word64 [rcx + 184], rdx
    x64.storeBaseDispReg.word64 [rcx + 176], rax
    x64.storeSlotReg slot0, rcx
    x64.callDirect __sched_runq_empty
    x64.loadRegSlot rcx, slot0
    x64.cmpRegImm32 r8, 1
    x64.jcc notEqual, hpheldwork
    x64.jmp hpidled
  hpgive:
    x64.leaRegGlobal rdx, __sched_nmidle
    x64.aluBaseDispImm32.add [rdx + 0], -1
    x64.loadRegBaseDisp.word64 rdx, [r12 + 536]
    x64.loadRegBaseDisp.word64 rsi, [r12 + 568]
    x64.cmpRegImm32 rsi, 0
    x64.jcc equal, munlinkhead
    x64.jmp munlinkinner
  tshfiredue:
    x64.movRegReg rcx, r8
    x64.callDirect __gt_timer_check
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, tshgiveupstrand
  tshpaywake:
    x64.callDirect __sched_wake_or_spawn
  tshgiveupstrand:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 r8, 0
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  tshgavestrand:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 rcx, 200
    x64.movRegImm32 rax, 0
    x64.callDirect mrt_host_netpoll_sleep
    x64.movRegImm r8, 18446744073709551615
    x64.epilogue 88
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_run_strand {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.pushReg r15
    x64.prologue 56
    x64.movRegReg rbx, rcx
    x64.movRegReg r12, rdx
    x64.leaRegRegImm32 r13, rbx, 24
    x64.movRegImm32 r14, 0
    x64.movRegReg rdx, r14
  strandnext:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_strand_next
    x64.movRegReg r15, r8
    x64.storeSlotReg slot1, r15
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, strandsort
  strandreleased:
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  strandsort:
    x64.cmpRegImm32 r15, -1
    x64.jcc equal, strandhandedoff
  strandrun:
    x64.loadRegBaseDisp.word64 rax, [r15 + 24]
    x64.leaRegRegImm32 rax, rax, 928
    x64.storeBaseDispReg.word64 [r15 + 80], rax
    x64.storeBaseDispReg.word64 [r15 + 48], r13
    x64.storeBaseDispReg.word64 [rbx + 8], r15
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, r15
    x64.callDirect __gt_context_switch
    x64.storeBaseDispReg.word64 [rbx + 8], r13
    x64.loadRegBaseDisp.word64 rax, [r15 + 16]
    x64.cmpRegImm32 rax, 2
    x64.jcc notEqual, strandparked
  strandcompleted:
    x64.loadRegBaseDisp.word64 rax, [rbx + 624]
    x64.cmpRegReg rax, r15
    x64.jcc equal, stranddroppin
  strandreap:
    x64.movRegReg rcx, r15
    x64.callDirect __gt_runner_done
    x64.movRegReg rdx, r14
    x64.jmp strandnext
  strandparked:
    x64.loadRegBaseDisp.word64 rax, [r15 + 32]
    x64.cmpRegImm32 rax, 2048
    x64.setccReg greater, rax
    x64.loadRegBaseDisp.word64 rcx, [r15 + 288]
    x64.cmpRegImm32 rcx, 9
    x64.setccReg notEqual, rcx
    x64.andRegReg rax, rax, rcx
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, stranddispatch
  strandshrink:
    x64.movRegReg rcx, r15
    x64.callDirect __gt_stack_shrink
  stranddispatch:
    x64.loadRegBaseDisp.word64 rax, [r15 + 288]
    x64.cmpRegImm32 rax, 7
    x64.jcc notEqual, strandnotyield
  strandyield:
    x64.movRegImm32 rax, 0
    x64.storeSlotReg slot0, rax
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r15, [rax + 0]
    x64.movRegImm rax, 9223372036854775807
    x64.cmpRegReg r15, rax
    x64.jcc equal, critsplit#23
  timerdueask:
    x64.callDirect __gt_now_ns
    x64.cmpRegReg r15, r8
    x64.jcc greater, critsplit#24
  pollfire:
    x64.movRegReg rcx, r8
    x64.callDirect __gt_timer_check
    x64.jmp strandpoll
  critsplit#23:
    x64.loadRegSlot rax, slot0
    x64.movRegReg r8, rax
    x64.jmp strandpoll
  critsplit#24:
    x64.loadRegSlot rax, slot0
    x64.movRegReg r8, rax
  strandpoll:
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, strandplace
  strandpollwake:
    x64.callDirect __sched_wake_or_spawn
  strandplace:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegBaseDisp.word64 rax, [r12 + 272]
    x64.cmpRegImm32 rax, 0
    x64.setccReg equal, r15
    x64.loadRegSlot rcx, slot1
    x64.callDirect __gt_coro_enqueue
    x64.cmpRegImm32 r15, 0
    x64.jcc notEqual, strandbehindeveryone
  strandbehindsiblings:
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rdx, r14
    x64.jmp strandnext
  strandnotyield:
    x64.cmpRegImm32 rax, 9
    x64.jcc notEqual, strandregister
  strandpreempted:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegSlot rax, slot1
    x64.storeBaseDispReg.word64 [r12 + 416], rax
  strandbehindeveryone:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_enqueue
    x64.leaRegGlobal rax, __sched_lock
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.callDirect __sched_wake_or_spawn
    x64.movRegImm32 r8, 0
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  strandregister:
    x64.loadRegSlot rdx, slot1
    x64.jmp strandnext
  stranddroppin:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [r15 + 456], rax
    x64.storeBaseDispReg.word64 [r15 + 448], rax
    x64.storeBaseDispReg.word64 [rbx + 624], rax
    x64.movRegImm rax, 18446744073709551615
    x64.leaRegGlobal rcx, __gt_hold_epoch
    x64.lock add [rcx], rax
    x64.jmp strandreap
  strandhandedoff:
    x64.movRegImm r8, 18446744073709551615
    x64.epilogue 56
    x64.popReg r15
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}

func @__gt_register_park {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.pushReg rbx
    x64.pushReg r12
    x64.pushReg r13
    x64.pushReg r14
    x64.prologue 32
    x64.movRegReg rbx, rcx
    x64.loadRegBaseDisp.word64 rax, [rbx + 288]
    x64.loadRegBaseDisp.word64 r12, [rbx + 296]
    x64.movRegImm32 r13, 3
    x64.movRegImm32 r14, 0
    x64.cmpRegImm32 rax, 1
    x64.jcc notEqual, notparktimer
  parktimer:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r13, [rax + 0]
    x64.movRegImm32 rax, 0
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r12
    x64.callDirect __gt_timer_add
    x64.cmpRegReg r12, r13
    x64.jcc greaterEqual, parktimerpaid
  parktimerwake:
    x64.movRegReg rcx, r12
    x64.callDirect __sched_wake_netpoller
  parktimerpaid:
    x64.movRegReg r8, r14
    x64.jmp parkdone
  notparktimer:
    x64.cmpRegImm32 rax, 8
    x64.setccReg equal, rcx
    x64.cmpRegImm32 rax, 10
    x64.setccReg equal, rdx
    x64.orRegReg rcx, rcx, rdx
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, notparknetpoll
  parknetpoll:
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 r12, [rax + 0]
    x64.movRegReg rcx, rbx
    x64.callDirect __np_pd_commit
    x64.movRegReg rbx, r8
    x64.leaRegGlobal rax, __gt_timer_when
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.cmpRegReg rcx, r12
    x64.jcc greaterEqual, parknetpollpaid
  parknetpollwake:
    x64.callDirect __sched_wake_netpoller
  parknetpollpaid:
    x64.movRegReg r8, rbx
    x64.jmp parkdone
  notparknetpoll:
    x64.cmpRegImm32 rax, 4
    x64.jcc notEqual, notparkmailbox
  parkmailbox:
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], 1
    x64.leaRegRegImm32 rcx, r12, 64
    x64.callDirect mrt_linux_lock_enter
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkmboxundo
  parkmboxclosed:
    x64.loadRegBaseDisp.word64 rax, [r12 + 24]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkmboxundo
  parkmboxwait:
    x64.movRegImm32 rax, 3
    x64.storeBaseDispReg.word64 [r12 + 16], rbx
    x64.storeBaseDispReg.word64 [rbx + 16], rax
    x64.leaRegRegImm32 rcx, r12, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegImm32 r8, 0
    x64.jmp parkdone
  parkmboxundo:
    x64.leaRegRegImm32 rcx, r12, 64
    x64.callDirect mrt_linux_lock_leave
    x64.leaRegGlobal rax, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.leaRegGlobal rcx, __sched_tls_teb_offset
    x64.loadRegBaseDisp.word64 rcx, [rcx + 0]
    x64.mov rcx, gs:[rcx]
    x64.cmpRegImm32 rax, 0
    x64.setccReg notEqual, rax
    x64.cmpRegImm32 rcx, 0
    x64.setccReg notEqual, rdx
    x64.andRegReg rax, rax, rdx
    x64.movRegImm32 rdx, 0
    x64.subRegReg rdx, rdx, rax
    x64.leaRegRegImm32 rax, rax, -1
    x64.leaRegRegImm32 rcx, rcx, 592
    x64.leaRegGlobal rsi, __sched_lock_depth_sink
    x64.andRegReg rcx, rcx, rdx
    x64.andRegReg rsi, rsi, rax
    x64.orRegReg rcx, rcx, rsi
    x64.aluBaseDispImm32.add [rcx + 0], -1
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready_locked
    x64.jmp parkdone
  notparkmailbox:
    x64.cmpRegImm32 rax, 5
    x64.jcc notEqual, notparkawait
  parkawait:
    x64.loadRegBaseDisp.word64 rax, [r12 + 16]
    x64.cmpRegImm32 rax, 2
    x64.jcc equal, parkresumenow
  parkawaitregister:
    x64.loadRegBaseDisp.word64 rax, [r12 + 368]
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, claimawaitertake#30
  claimawaitertwice#31:
    x64.leaRegRdata rcx, [rip + __abort_msg_102]  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    x64.movRegImm32 rdx, 62
    x64.movRegImm32 rax, 102
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  claimawaitertake#30:
    x64.storeBaseDispReg.word64 [r12 + 368], rbx
  parkawaittake:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
    x64.jmp parkdone
  notparkawait:
    x64.cmpRegImm32 rax, 6
    x64.jcc notEqual, notparkawaitany
  parkawaitany:
    x64.movRegReg rcx, r12
    x64.callDirect __gt_any_completed
    x64.cmpRegImm32 r8, -1
    x64.jcc notEqual, parkresumenow
  parkawaitanyregister:
    x64.movRegImm32 rax, 0
    x64.storeBaseDispReg.word64 [rbx + 376], rax
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rcx, [r12 + 8]
    x64.movRegReg rdx, rbx
    x64.orRegImm32 rdx, rbx, 1
    x64.movRegImm32 rsi, 0
  anywalkhdr:
    x64.cmpRegReg rsi, rcx
    x64.jcc less, anywalkbody
  parkawaitanyregistered:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
    x64.jmp parkdone
  anywalkbody:
    x64.loadRegBaseIndexScale.word64 rdi, [rax + rsi*8 + 0]
    x64.cmpRegImm32 rdi, 0
    x64.jcc equal, anywalknext
  anywalkslot:
    x64.loadRegBaseDisp.word64 r8, [rdi + 368]
    x64.cmpRegImm32 r8, 0
    x64.jcc equal, claimawaitertake#36
  claimawaitertwice#37:
    x64.leaRegRdata rcx, [rip + __abort_msg_102]  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    x64.movRegImm32 rdx, 62
    x64.movRegImm32 rax, 102
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  claimawaitertake#36:
    x64.storeBaseDispReg.word64 [rdi + 368], rdx
  anywalknext:
    x64.leaRegRegImm32 rsi, rsi, 1
    x64.jmp anywalkhdr
  notparkawaitany:
    x64.cmpRegImm32 rax, 11
    x64.jcc notEqual, parkunknown
  parkquiesce:
    x64.leaRegGlobal rax, __gt_quiesce_waiter
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkquiescetaken
  parkquiesceregister:
    x64.leaRegGlobal rax, __gt_quiesce_waiter
    x64.lock add [rax], rbx
    x64.leaRegGlobal rax, __gt_live_count
    x64.loadRegBaseDisp.word64 rax, [rax + 0]
    x64.cmpRegImm32 rax, 0
    x64.jcc notEqual, parkquiescetake
  parkquiesceundo:
    x64.movRegImm32 rax, 0
    x64.subRegReg rax, rax, rbx
    x64.leaRegGlobal rcx, __gt_quiesce_waiter
    x64.lock add [rcx], rax
  parkresumenow:
    x64.movRegReg rcx, rbx
    x64.callDirect __gt_ready_locked
    x64.jmp parkdone
  parkquiescetake:
    x64.storeBaseDispReg.word64 [rbx + 16], r13
    x64.movRegReg r8, r14
  parkdone:
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parkunknown:
    x64.leaRegRdata rcx, [rip + __abort_msg_93]  ; "fatal error: runtime abort 93 (schedulerUnknownParkKind)\x0a"
    x64.movRegImm32 rdx, 57
    x64.movRegImm32 rax, 93
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  parkquiescetaken:
    x64.leaRegRdata rcx, [rip + __abort_msg_123]  ; "fatal error: runtime abort 123 (schedulerQuiesceWaiterTaken)\x0a"
    x64.movRegImm32 rdx, 61
    x64.movRegImm32 rax, 123
    x64.callDirect __rt_abort
    x64.movRegImm32 r8, 0
    x64.epilogue 32
    x64.popReg r14
    x64.popReg r13
    x64.popReg r12
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
}
```
```TargetIr:arm64-macos
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __slab_arena_list@488 = i64 0
  __slab_arena_map_l1@496 = i64 0
  __slab_state@504 = i64 0
  __mrt_program_started@512 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-strand-turn-takes-the-run-queue-lock-once.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__gt_strand_next {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  snfirst:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.cmp x20, 0
    arm64.movImm x21, 0
    arm64.b.eq critsplit
  snregister:
    arm64.movRegReg x0, x20
    arm64.bl __gt_register_park
    arm64.movRegReg x20, x0
    arm64.b sntake
  critsplit:
    arm64.movRegReg x20, x21
  sntake:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 416]
    arm64.cmp x21, 0
    arm64.b.ne snresume
  sndequeue:
    arm64.movRegReg x0, x19
    arm64.bl __gt_coro_dequeue
    arm64.movRegReg x21, x0
    arm64.cmp x21, 0
    arm64.b.ne sngot
  snrelease:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 352], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsnrelease
  wakesnrelease:
    arm64.bl __sched_wake_or_spawn
  paidsnrelease:
    arm64.movImm x0, 18446744073709551615
    arm64.add x1, x19, 360
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.cmp x0, 1
    arm64.b.ne strandunreffed
  strandlastref:
    arm64.add x0, x19, 256
    arm64.movImm x1, 2
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.cmp x0, 1
    arm64.b.ne strandunreffed
  teardownreclaim:
    arm64.movRegReg x0, x19
    arm64.bl __gt_reclaim
  strandunreffed:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  sngot:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsngot
  wakesngot:
    arm64.bl __sched_wake_or_spawn
  paidsngot:
    arm64.movRegReg x0, x21
    arm64.b sncandidate
  snresume:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 416], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsnresume
  wakesnresume:
    arm64.bl __sched_wake_or_spawn
  paidsnresume:
    arm64.movRegReg x0, x21
  sncandidate:
    arm64.cmp x0, 0
    arm64.b.ne tscheckdropped
  tsnowork:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  tscheckdropped:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 256]
    arm64.cmp x1, 1
    arm64.cset x1, eq
    arm64.loadRegBaseDisp.word64 x2, [x0 + 48]
    arm64.cmp x2, 0
    arm64.cset x2, eq
    arm64.and x1, x1, x2
    arm64.cbz x1, tshand
  tsreclaim:
    arm64.bl __gt_runner_done
  snpop:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.movImm x0, 0
    arm64.movRegReg x20, x0
    arm64.b sntake
  tshand:
    arm64.leaGlobal x1, __gt_hold_epoch
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.ne tshexamine
  tshhandover:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  timerdueask:
    arm64.bl __gt_now_ns
    arm64.cmp x20, x0
    arm64.b.le tshfiredue
    arm64.b tshgiveupstrand
  hpheldwork:
    arm64.leaRdata x0, __abort_msg_95  ; "fatal error: runtime abort 95 (schedulerIdledAProcessorHoldingWork)\x0a"
    arm64.movImm x1, 68
    arm64.movImm x2, 95
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  hpidled:
    arm64.leaGlobal x0, __sched_regain_head
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.ne pidleputregain
  pidleputidle:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x1 + 72], x0
    arm64.leaGlobal x0, __sched_pidle
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.storeBaseDispReg.word64 [x1 + 160], x0
    arm64.leaGlobal x0, __sched_pidle
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 1
    arm64.leaGlobal x1, __sched_npidle
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.b pidleputplaced
  pidleputregain:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 584]
    arm64.leaGlobal x3, __sched_regain_head
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 584], x2
    arm64.storeBaseDispReg.word64 [x0 + 512], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 496]
    arm64.bl mrt_host_event_set
  pidleputplaced:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
    arm64.bl __np_break
    arm64.b tshgavestrand
  munlinkhead:
    arm64.leaGlobal x4, __sched_midle
    arm64.storeBaseDispReg.word64 [x4 + 0], x2
    arm64.b munlinkmend
  munlinkinner:
    arm64.storeBaseDispReg.word64 [x3 + 536], x2
  munlinkmend:
    arm64.cmp x2, 0
    arm64.b.eq munlinkclear
  munlinknext:
    arm64.storeBaseDispReg.word64 [x2 + 568], x3
  munlinkclear:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x20 + 536], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x20 + 568], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x1 + 184], x2
    arm64.storeBaseDispReg.word64 [x1 + 176], x0
    arm64.storeBaseDispReg.word64 [x20 + 512], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
    arm64.b tshgavestrand
  tshexamine:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 440]
    arm64.cmp x1, 1
    arm64.b.ne tshcheckpin
  tshholdback:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 592
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.importCall 18
    arm64.loadRegSlot x0, slot0
    arm64.b tshputback
  tshcheckpin:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 448]
    arm64.cmp x1, 0
    arm64.b.eq tshhandover
  tshcomparepin:
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, x2
    arm64.b.eq tshhandover
  tshstartlocked:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 592
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.importCall 18
    arm64.loadRegSlot x0, slot0
  tshputback:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 448]
    arm64.bl __gt_coro_enqueue
    arm64.cmp x20, 0
    arm64.b.ne tshwakeowner
  tshnoowner:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.b tshbackoff
  tshwakeowner:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne hpexamine
  hpkeep:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
  tshbackoff:
    arm64.movImm x0, 200
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x20, [x0]
    arm64.movImm x0, 9223372036854775807
    arm64.cmp x20, x0
    arm64.b.eq tshgiveupstrand
    arm64.b timerdueask
  hpexamine:
    arm64.leaGlobal x2, __sched_midle
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, x20
    arm64.cset x2, eq
    arm64.loadRegBaseDisp.word64 x3, [x20 + 568]
    arm64.cmp x3, 0
    arm64.cset x3, ne
    arm64.orr x2, x2, x3
    arm64.cmp x2, 0
    arm64.b.ne hpgive
  hpdrain:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x1
    arm64.bl __sched_drain_runq
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
  hprelease:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x1 + 184], x2
    arm64.storeBaseDispReg.word64 [x1 + 176], x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x1
    arm64.bl __sched_runq_empty
    arm64.loadRegSlot x1, slot1
    arm64.cmp x0, 1
    arm64.b.ne hpheldwork
    arm64.b hpidled
  hpgive:
    arm64.leaGlobal x2, __sched_nmidle
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.add x3, x3, -1
    arm64.storeBaseDispReg.word64 [x2 + 0], x3
    arm64.loadRegBaseDisp.word64 x2, [x20 + 536]
    arm64.loadRegBaseDisp.word64 x3, [x20 + 568]
    arm64.cmp x3, 0
    arm64.b.eq munlinkhead
    arm64.b munlinkinner
  tshfiredue:
    arm64.bl __gt_timer_check
    arm64.cmp x0, 0
    arm64.b.eq tshgiveupstrand
  tshpaywake:
    arm64.bl __sched_wake_or_spawn
  tshgiveupstrand:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.movRegReg x0, x19
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  tshgavestrand:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.movRegReg x0, x19
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 200
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
    arm64.movImm x0, 18446744073709551615
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__gt_run_strand {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.add x21, x19, 24
    arm64.movImm x22, 0
    arm64.movRegReg x1, x22
  strandnext:
    arm64.movRegReg x0, x20
    arm64.bl __gt_strand_next
    arm64.movRegReg x23, x0
    arm64.cmp x23, 0
    arm64.b.ne strandsort
  strandreleased:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  strandsort:
    arm64.cmp x23, -1
    arm64.b.eq strandhandedoff
  strandrun:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 24]
    arm64.add x0, x0, 928
    arm64.storeBaseDispReg.word64 [x23 + 80], x0
    arm64.storeBaseDispReg.word64 [x23 + 48], x21
    arm64.storeBaseDispReg.word64 [x19 + 8], x23
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.bl __gt_context_switch
    arm64.storeBaseDispReg.word64 [x19 + 8], x21
    arm64.add x16, x23, 16
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 2
    arm64.b.ne strandparked
  strandcompleted:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 624]
    arm64.cmp x0, x23
    arm64.b.eq stranddroppin
  strandreap:
    arm64.movRegReg x0, x23
    arm64.bl __gt_runner_done
    arm64.movRegReg x1, x22
    arm64.b strandnext
  strandparked:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 32]
    arm64.cmp x0, 2048
    arm64.cset x0, gt
    arm64.loadRegBaseDisp.word64 x1, [x23 + 288]
    arm64.cmp x1, 9
    arm64.cset x1, ne
    arm64.and x0, x0, x1
    arm64.cbz x0, stranddispatch
  strandshrink:
    arm64.movRegReg x0, x23
    arm64.bl __gt_stack_shrink
  stranddispatch:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 288]
    arm64.cmp x0, 7
    arm64.b.ne strandnotyield
  strandyield:
    arm64.movImm x24, 0
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x25, [x0]
    arm64.movImm x0, 9223372036854775807
    arm64.cmp x25, x0
    arm64.b.eq critsplit#23
  timerdueask:
    arm64.bl __gt_now_ns
    arm64.cmp x25, x0
    arm64.b.gt critsplit#24
  pollfire:
    arm64.bl __gt_timer_check
    arm64.b strandpoll
  critsplit#23:
    arm64.movRegReg x0, x24
    arm64.b strandpoll
  critsplit#24:
    arm64.movRegReg x0, x24
  strandpoll:
    arm64.cmp x0, 0
    arm64.b.eq strandplace
  strandpollwake:
    arm64.bl __sched_wake_or_spawn
  strandplace:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.loadRegBaseDisp.word64 x0, [x20 + 272]
    arm64.cmp x0, 0
    arm64.cset x24, eq
    arm64.movRegReg x0, x23
    arm64.bl __gt_coro_enqueue
    arm64.cbnz x24, strandbehindeveryone
  strandbehindsiblings:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x1, x22
    arm64.b strandnext
  strandnotyield:
    arm64.cmp x0, 9
    arm64.b.ne strandregister
  strandpreempted:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 18
    arm64.storeBaseDispReg.word64 [x20 + 416], x23
  strandbehindeveryone:
    arm64.movRegReg x0, x20
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  strandregister:
    arm64.movRegReg x1, x23
    arm64.b strandnext
  stranddroppin:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 456], x0
    arm64.storeBaseDispReg.word64 [x23 + 448], x0
    arm64.storeBaseDispReg.word64 [x19 + 624], x0
    arm64.movImm x0, 18446744073709551615
    arm64.leaGlobal x1, __gt_hold_epoch
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.b strandreap
  strandhandedoff:
    arm64.movImm x0, 18446744073709551615
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}

func @__gt_register_park {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 288]
    arm64.loadRegBaseDisp.word64 x20, [x19 + 296]
    arm64.movImm x21, 3
    arm64.movImm x22, 0
    arm64.cmp x0, 1
    arm64.b.ne notparktimer
  parktimer:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x21, [x0]
    arm64.movImm x2, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __gt_timer_add
    arm64.cmp x20, x21
    arm64.b.ge parktimerpaid
  parktimerwake:
    arm64.movRegReg x0, x20
    arm64.bl __sched_wake_netpoller
  parktimerpaid:
    arm64.movRegReg x0, x22
    arm64.b parkdone
  notparktimer:
    arm64.cmp x0, 8
    arm64.cset x1, eq
    arm64.cmp x0, 10
    arm64.cset x2, eq
    arm64.orr x1, x1, x2
    arm64.cbz x1, notparknetpoll
  parknetpoll:
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x20, [x0]
    arm64.movRegReg x0, x19
    arm64.bl __np_pd_commit
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, x20
    arm64.b.ge parknetpollpaid
  parknetpollwake:
    arm64.bl __sched_wake_netpoller
  parknetpollpaid:
    arm64.movRegReg x0, x19
    arm64.b parkdone
  notparknetpoll:
    arm64.cmp x0, 4
    arm64.b.ne notparkmailbox
  parkmailbox:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x20, 64
    arm64.importCall 18
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.ne parkmboxundo
  parkmboxclosed:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 0
    arm64.b.ne parkmboxundo
  parkmboxwait:
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x20 + 16], x19
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.add x0, x20, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 0
    arm64.b parkdone
  parkmboxundo:
    arm64.add x0, x20, 64
    arm64.importCall 19
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, darwinMaskedReadOnly
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready_locked
    arm64.b parkdone
  notparkmailbox:
    arm64.cmp x0, 5
    arm64.b.ne notparkawait
  parkawait:
    arm64.add x16, x20, 16
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 2
    arm64.b.eq parkresumenow
  parkawaitregister:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 368]
    arm64.cmp x0, 0
    arm64.b.eq claimawaitertake#30
  claimawaitertwice#31:
    arm64.leaRdata x0, __abort_msg_102  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    arm64.movImm x1, 62
    arm64.movImm x2, 102
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  claimawaitertake#30:
    arm64.storeBaseDispReg.word64 [x20 + 368], x19
  parkawaittake:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
    arm64.b parkdone
  notparkawait:
    arm64.cmp x0, 6
    arm64.b.ne notparkawaitany
  parkawaitany:
    arm64.movRegReg x0, x20
    arm64.bl __gt_any_completed
    arm64.cmp x0, -1
    arm64.b.ne parkresumenow
  parkawaitanyregister:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 376], x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 8]
    arm64.orr x2, x19, 1
    arm64.movImm x3, 0
  anywalkhdr:
    arm64.cmp x3, x1
    arm64.b.lt anywalkbody
  parkawaitanyregistered:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
    arm64.b parkdone
  anywalkbody:
    arm64.loadRegBaseIndexScale.word64 x4, [x0 + x3*8 + 0]
    arm64.cmp x4, 0
    arm64.b.eq anywalknext
  anywalkslot:
    arm64.loadRegBaseDisp.word64 x5, [x4 + 368]
    arm64.cmp x5, 0
    arm64.b.eq claimawaitertake#36
  claimawaitertwice#37:
    arm64.leaRdata x0, __abort_msg_102  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    arm64.movImm x1, 62
    arm64.movImm x2, 102
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  claimawaitertake#36:
    arm64.storeBaseDispReg.word64 [x4 + 368], x2
  anywalknext:
    arm64.add x3, x3, 1
    arm64.b anywalkhdr
  notparkawaitany:
    arm64.cmp x0, 11
    arm64.b.ne parkunknown
  parkquiesce:
    arm64.leaGlobal x0, __gt_quiesce_waiter
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne parkquiescetaken
  parkquiesceregister:
    arm64.leaGlobal x0, __gt_quiesce_waiter
    arm64.arm64AtomicRmw.add x0, [x0], x19
    arm64.leaGlobal x0, __gt_live_count
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne parkquiescetake
  parkquiesceundo:
    arm64.movImm x0, 0
    arm64.sub x0, x0, x19
    arm64.leaGlobal x1, __gt_quiesce_waiter
    arm64.arm64AtomicRmw.add x0, [x1], x0
  parkresumenow:
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready_locked
    arm64.b parkdone
  parkquiescetake:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
  parkdone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  parkunknown:
    arm64.leaRdata x0, __abort_msg_93  ; "fatal error: runtime abort 93 (schedulerUnknownParkKind)\x0a"
    arm64.movImm x1, 57
    arm64.movImm x2, 93
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  parkquiescetaken:
    arm64.leaRdata x0, __abort_msg_123  ; "fatal error: runtime abort 123 (schedulerQuiesceWaiterTaken)\x0a"
    arm64.movImm x1, 61
    arm64.movImm x2, 123
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __gt_allg@0 = i64 0
  __gt_allglen@8 = i64 0
  __gt_allgcap@16 = i64 0
  __gt_hold_epoch@24 = i64 0
  __gt_run_queue_head@32 = i64 0
  __gt_run_queue_tail@40 = i64 0
  __gt_timer_count@48 = i64 0
  __gt_timer_heap_base@56 = i64 0
  __gt_timer_capacity@64 = i64 0
  __gt_timer_seq@72 = i64 0
  __gt_timer_when@80 = i64 9223372036854775807
  __gt_trace_counter@88 = i64 0
  __gt_live_count@96 = i64 0
  __gt_quiesce_waiter@104 = i64 0
  __gt_quiesce_stuck@112 = i64 0
  __gt_seed_bytes@120 = i64 2048
  __gt_stack_bytes_sum@128 = i64 0
  __gt_stack_bytes_count@136 = i64 0
  __gt_seed_sum_seen@144 = i64 0
  __gt_seed_count_seen@152 = i64 0
  __gt_keeps_cpu_clock@160 = i64 0
  __gt_stack_pool@168 = i64 0
  __sched_active_workers@176 = i64 1
  __sched_max_active_workers@184 = i64 1
  __sched_tls_index@192 = i64 0
  __sched_tls_teb_offset@200 = i64 0
  __sched_procs@208 = i64 0
  __sched_allm@216 = i64 0
  __sched_async_preempt_off@224 = i64 0
  __sched_midle@232 = i64 0
  __sched_pidle@240 = i64 0
  __gt_gfree_head@248 = i64 0
  __sched_arena_cursor@256 = i64 0
  __sched_arena_limit@264 = i64 0
  __gt_records_carved@272 = i64 0
  __sched_npidle@280 = i64 0
  __sched_runq_size@288 = i64 0
  __sched_global_pushes@296 = i64 0
  __sched_timer_scan_steps@304 = i64 0
  __sched_mcount@312 = i64 0
  __sched_nmidle@320 = i64 0
  __sched_main_m@328 = i64 0
  __sched_phase@336 = i64 0
  __sched_lastpoll@344 = i64 0
  __sched_poll_until@352 = i64 0
  __sched_regain_head@360 = i64 0
  __np_poller@368 = i64 0
  __np_break@376 = i64 0
  __np_wake_sig@384 = i64 0
  __np_pd_table@392 = i64 0
  __np_pd_cap@400 = i64 0
  __np_waiters@408 = i64 0
  __sched_num_procs@416 = i64 0
  __sched_shutdown_flag@424 = i64 0
  __sched_lock@432 = i64 0
  __sched_lock_depth_sink@440 = i64 0
  __sched_nmspinning@448 = i64 0
  __sched_needspinning@456 = i64 0
  __sched_sysmon_event@464 = i64 0
  __sched_sysmon_wait@472 = i64 0
  __sched_timer_starts@480 = i64 0
  __mrt_envp@488 = i64 0
  __mrt_signal_stack_bytes@496 = i64 0
  __mrt_tls_next_key@504 = i64 0
  __slab_arena_list@512 = i64 0
  __slab_arena_map_l1@520 = i64 0
  __slab_state@528 = i64 0
  __mrt_program_started@536 = i8 0
}

func @main {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
  __il_body:
    arm64.movImm x19, 0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.storeBaseDispReg.word64 [x1 + 0], x19
  __il_cont:
    arm64.leaFuncAddr x0, Counter.__loop
    arm64.leaFuncAddr x2, Counter.__abandon
    arm64.bl __svc_spawn
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x20, x0
    arm64.storeBaseDispReg.word64 [x20 + 0], x19
    arm64.movImm x19, 1
    arm64.movImm x0, 0
    arm64.bl __gt_cell_alloc
    arm64.movRegReg x21, x0
    arm64.movImm x0, 32
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x1, x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x0
    arm64.storeBaseDispReg.word64 [x1 + 16], x19
    arm64.storeBaseDispReg.word64 [x1 + 24], x21
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.bl __mbox_send
    arm64.movRegReg x0, x21
    arm64.bl __gt_try_await
    arm64.cmp x9, 0
    arm64.b.eq tryok
  tryerr:
    arm64.movImm x19, 0
    arm64.b trycont
  tryok:
    arm64.movRegReg x19, x0
  trycont:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_handle_drop_box
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at a-strand-turn-takes-the-run-queue-lock-once.test:22: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.epilogue 48
    arm64.ret
}

func @Counter.__loop {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  serve:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.cmp x0, 0
    arm64.b.eq stopping
  dispatch:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.jumpTable x1, default=stopping [[0]=__shutdown, [1]=next] @__jumptable_4
  __shutdown:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.storeBaseDispReg.word64 [x20 + 56], x1
    arm64.bl __mm_decref
  stopping:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_close
  drain:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_recv
    arm64.movRegReg x1, x0
    arm64.cmp x1, 0
    arm64.b.ne abandon
  finished:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x0, x20
    arm64.bl __mbox_service_exit
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  abandon:
    arm64.movRegReg x0, x20
    arm64.bl __mbox_abandon
    arm64.b drain
  next:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 16]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 24]
  __il_body:
    arm64.loadRegBaseDisp.word64 x2, [x19 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x19 + 0], x2
    arm64.add x22, x1, 1
  __il_cont:
    arm64.bl __mm_decref
    arm64.movImm x2, 0
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x22
    arm64.bl __gt_cell_complete
    arm64.b serve
}

func @Counter.__abandon {
  entry:
    arm64.prologue 32
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 0]
    arm64.jumpTable x0, default=abandondrop [[0]=__shutdown, [1]=next] @__jumptable_5
  __shutdown:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 16]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
    arm64.b abandondrop
  next:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 24]
    arm64.movImm x1, 0
    arm64.movImm x2, 1
    arm64.bl __gt_cell_complete
  abandondrop:
    arm64.movRegReg x0, x19
    arm64.bl __mm_decref
    arm64.movRegReg x1, x0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}

func @__gt_strand_next {
  entry:
    arm64.prologue 64
    arm64.storeSlotReg slot4, x21
    arm64.storeSlotReg slot3, x20
    arm64.storeSlotReg slot2, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
  snfirst:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.cmp x20, 0
    arm64.movImm x21, 0
    arm64.b.eq critsplit
  snregister:
    arm64.movRegReg x0, x20
    arm64.bl __gt_register_park
    arm64.movRegReg x20, x0
    arm64.b sntake
  critsplit:
    arm64.movRegReg x20, x21
  sntake:
    arm64.loadRegBaseDisp.word64 x21, [x19 + 416]
    arm64.cmp x21, 0
    arm64.b.ne snresume
  sndequeue:
    arm64.movRegReg x0, x19
    arm64.bl __gt_coro_dequeue
    arm64.movRegReg x21, x0
    arm64.cmp x21, 0
    arm64.b.ne sngot
  snrelease:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 352], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsnrelease
  wakesnrelease:
    arm64.bl __sched_wake_or_spawn
  paidsnrelease:
    arm64.movImm x0, 18446744073709551615
    arm64.add x1, x19, 360
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.cmp x0, 1
    arm64.b.ne strandunreffed
  strandlastref:
    arm64.add x0, x19, 256
    arm64.movImm x1, 2
    arm64.arm64AtomicRmw.add x0, [x0], x1
    arm64.cmp x0, 1
    arm64.b.ne strandunreffed
  teardownreclaim:
    arm64.movRegReg x0, x19
    arm64.bl __gt_reclaim
  strandunreffed:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  sngot:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsngot
  wakesngot:
    arm64.bl __sched_wake_or_spawn
  paidsngot:
    arm64.movRegReg x0, x21
    arm64.b sncandidate
  snresume:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 416], x0
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.cmp x20, 0
    arm64.b.eq paidsnresume
  wakesnresume:
    arm64.bl __sched_wake_or_spawn
  paidsnresume:
    arm64.movRegReg x0, x21
  sncandidate:
    arm64.cmp x0, 0
    arm64.b.ne tscheckdropped
  tsnowork:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  tscheckdropped:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 256]
    arm64.cmp x1, 1
    arm64.cset x1, eq
    arm64.loadRegBaseDisp.word64 x2, [x0 + 48]
    arm64.cmp x2, 0
    arm64.cset x2, eq
    arm64.and x1, x1, x2
    arm64.cbz x1, tshand
  tsreclaim:
    arm64.bl __gt_runner_done
  snpop:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.movImm x0, 0
    arm64.movRegReg x20, x0
    arm64.b sntake
  tshand:
    arm64.leaGlobal x1, __gt_hold_epoch
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.cmp x1, 0
    arm64.b.ne tshexamine
  tshhandover:
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  timerdueask:
    arm64.bl __gt_now_ns
    arm64.cmp x20, x0
    arm64.b.le tshfiredue
    arm64.b tshgiveupstrand
  hpheldwork:
    arm64.leaRdata x0, __abort_msg_95  ; "fatal error: runtime abort 95 (schedulerIdledAProcessorHoldingWork)\x0a"
    arm64.movImm x1, 68
    arm64.movImm x2, 95
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  hpidled:
    arm64.leaGlobal x0, __sched_regain_head
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.cmp x0, 0
    arm64.b.ne pidleputregain
  pidleputidle:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x1 + 72], x0
    arm64.leaGlobal x0, __sched_pidle
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.storeBaseDispReg.word64 [x1 + 160], x0
    arm64.leaGlobal x0, __sched_pidle
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 1
    arm64.leaGlobal x1, __sched_npidle
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.b pidleputplaced
  pidleputregain:
    arm64.loadRegBaseDisp.word64 x2, [x0 + 584]
    arm64.leaGlobal x3, __sched_regain_head
    arm64.storeBaseDispReg.word64 [x3 + 0], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 584], x2
    arm64.storeBaseDispReg.word64 [x0 + 512], x1
    arm64.loadRegBaseDisp.word64 x0, [x0 + 496]
    arm64.bl mrt_host_event_set
  pidleputplaced:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
    arm64.bl __np_break
    arm64.b tshgavestrand
  munlinkhead:
    arm64.leaGlobal x4, __sched_midle
    arm64.storeBaseDispReg.word64 [x4 + 0], x2
    arm64.b munlinkmend
  munlinkinner:
    arm64.storeBaseDispReg.word64 [x3 + 536], x2
  munlinkmend:
    arm64.cmp x2, 0
    arm64.b.eq munlinkclear
  munlinknext:
    arm64.storeBaseDispReg.word64 [x2 + 568], x3
  munlinkclear:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x20 + 536], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x20 + 568], x2
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x1 + 184], x2
    arm64.storeBaseDispReg.word64 [x1 + 176], x0
    arm64.storeBaseDispReg.word64 [x20 + 512], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
    arm64.b tshgavestrand
  tshexamine:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 440]
    arm64.cmp x1, 1
    arm64.b.ne tshcheckpin
  tshholdback:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 592
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegSlot x0, slot0
    arm64.b tshputback
  tshcheckpin:
    arm64.loadRegBaseDisp.word64 x1, [x0 + 448]
    arm64.cmp x1, 0
    arm64.b.eq tshhandover
  tshcomparepin:
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, x2
    arm64.b.eq tshhandover
  tshstartlocked:
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.leaGlobal x2, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x2
    arm64.cmp x2, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x2, x16, x17
    arm64.cmp x1, 0
    arm64.cset x1, ne
    arm64.cmp x2, 0
    arm64.cset x3, ne
    arm64.and x1, x1, x3
    arm64.movImm x3, 0
    arm64.sub x3, x3, x1
    arm64.sub x1, x1, 1
    arm64.add x2, x2, 592
    arm64.leaGlobal x4, __sched_lock_depth_sink
    arm64.and x2, x2, x3
    arm64.and x1, x4, x1
    arm64.orr x1, x2, x1
    arm64.loadRegBaseDisp.word64 x2, [x1 + 0]
    arm64.add x2, x2, 1
    arm64.storeBaseDispReg.word64 [x1 + 0], x2
    arm64.leaGlobal x1, __sched_lock
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.storeSlotReg slot0, x0
    arm64.movRegReg x0, x1
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegSlot x0, slot0
  tshputback:
    arm64.loadRegBaseDisp.word64 x20, [x0 + 448]
    arm64.bl __gt_coro_enqueue
    arm64.cmp x20, 0
    arm64.b.ne tshwakeowner
  tshnoowner:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.b tshbackoff
  tshwakeowner:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x0
    arm64.cmp x0, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x0, x16, x17
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.cmp x1, 0
    arm64.b.ne hpexamine
  hpkeep:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 496]
    arm64.bl mrt_host_event_set
  tshbackoff:
    arm64.movImm x0, 200
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x20, [x0]
    arm64.movImm x0, 9223372036854775807
    arm64.cmp x20, x0
    arm64.b.eq tshgiveupstrand
    arm64.b timerdueask
  hpexamine:
    arm64.leaGlobal x2, __sched_midle
    arm64.loadRegBaseDisp.word64 x2, [x2 + 0]
    arm64.cmp x2, x20
    arm64.cset x2, eq
    arm64.loadRegBaseDisp.word64 x3, [x20 + 568]
    arm64.cmp x3, 0
    arm64.cset x3, ne
    arm64.orr x2, x2, x3
    arm64.cmp x2, 0
    arm64.b.ne hpgive
  hpdrain:
    arm64.storeSlotReg slot0, x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x1
    arm64.bl __sched_drain_runq
    arm64.movRegReg x2, x0
    arm64.loadRegSlot x0, slot0
    arm64.loadRegSlot x1, slot1
  hprelease:
    arm64.movImm x2, 0
    arm64.storeBaseDispReg.word64 [x0 + 0], x2
    arm64.storeBaseDispReg.word64 [x1 + 184], x2
    arm64.storeBaseDispReg.word64 [x1 + 176], x0
    arm64.storeSlotReg slot1, x1
    arm64.movRegReg x0, x1
    arm64.bl __sched_runq_empty
    arm64.loadRegSlot x1, slot1
    arm64.cmp x0, 1
    arm64.b.ne hpheldwork
    arm64.b hpidled
  hpgive:
    arm64.leaGlobal x2, __sched_nmidle
    arm64.loadRegBaseDisp.word64 x3, [x2 + 0]
    arm64.add x3, x3, -1
    arm64.storeBaseDispReg.word64 [x2 + 0], x3
    arm64.loadRegBaseDisp.word64 x2, [x20 + 536]
    arm64.loadRegBaseDisp.word64 x3, [x20 + 568]
    arm64.cmp x3, 0
    arm64.b.eq munlinkhead
    arm64.b munlinkinner
  tshfiredue:
    arm64.bl __gt_timer_check
    arm64.cmp x0, 0
    arm64.b.eq tshgiveupstrand
  tshpaywake:
    arm64.bl __sched_wake_or_spawn
  tshgiveupstrand:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.movRegReg x0, x19
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
  tshgavestrand:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.movRegReg x0, x19
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 200
    arm64.movImm x1, 0
    arm64.bl mrt_host_netpoll_sleep
    arm64.movImm x0, 18446744073709551615
    arm64.loadRegSlot x19, slot2
    arm64.loadRegSlot x20, slot3
    arm64.loadRegSlot x21, slot4
    arm64.epilogue 64
    arm64.ret
}

func @__gt_run_strand {
  entry:
    arm64.prologue 80
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.movRegReg x20, x1
    arm64.add x21, x19, 24
    arm64.movImm x22, 0
    arm64.movRegReg x1, x22
  strandnext:
    arm64.movRegReg x0, x20
    arm64.bl __gt_strand_next
    arm64.movRegReg x23, x0
    arm64.cmp x23, 0
    arm64.b.ne strandsort
  strandreleased:
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  strandsort:
    arm64.cmp x23, -1
    arm64.b.eq strandhandedoff
  strandrun:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 24]
    arm64.add x0, x0, 928
    arm64.storeBaseDispReg.word64 [x23 + 80], x0
    arm64.storeBaseDispReg.word64 [x23 + 48], x21
    arm64.storeBaseDispReg.word64 [x19 + 8], x23
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x23
    arm64.bl __gt_context_switch
    arm64.storeBaseDispReg.word64 [x19 + 8], x21
    arm64.add x16, x23, 16
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 2
    arm64.b.ne strandparked
  strandcompleted:
    arm64.loadRegBaseDisp.word64 x0, [x19 + 624]
    arm64.cmp x0, x23
    arm64.b.eq stranddroppin
  strandreap:
    arm64.movRegReg x0, x23
    arm64.bl __gt_runner_done
    arm64.movRegReg x1, x22
    arm64.b strandnext
  strandparked:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 32]
    arm64.cmp x0, 2048
    arm64.cset x0, gt
    arm64.loadRegBaseDisp.word64 x1, [x23 + 288]
    arm64.cmp x1, 9
    arm64.cset x1, ne
    arm64.and x0, x0, x1
    arm64.cbz x0, stranddispatch
  strandshrink:
    arm64.movRegReg x0, x23
    arm64.bl __gt_stack_shrink
  stranddispatch:
    arm64.loadRegBaseDisp.word64 x0, [x23 + 288]
    arm64.cmp x0, 7
    arm64.b.ne strandnotyield
  strandyield:
    arm64.movImm x24, 0
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x25, [x0]
    arm64.movImm x0, 9223372036854775807
    arm64.cmp x25, x0
    arm64.b.eq critsplit#23
  timerdueask:
    arm64.bl __gt_now_ns
    arm64.cmp x25, x0
    arm64.b.gt critsplit#24
  pollfire:
    arm64.bl __gt_timer_check
    arm64.b strandpoll
  critsplit#23:
    arm64.movRegReg x0, x24
    arm64.b strandpoll
  critsplit#24:
    arm64.movRegReg x0, x24
  strandpoll:
    arm64.cmp x0, 0
    arm64.b.eq strandplace
  strandpollwake:
    arm64.bl __sched_wake_or_spawn
  strandplace:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegBaseDisp.word64 x0, [x20 + 272]
    arm64.cmp x0, 0
    arm64.cset x24, eq
    arm64.movRegReg x0, x23
    arm64.bl __gt_coro_enqueue
    arm64.cbnz x24, strandbehindeveryone
  strandbehindsiblings:
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x1, x22
    arm64.b strandnext
  strandnotyield:
    arm64.cmp x0, 9
    arm64.b.ne strandregister
  strandpreempted:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_enter
    arm64.storeBaseDispReg.word64 [x20 + 416], x23
  strandbehindeveryone:
    arm64.movRegReg x0, x20
    arm64.bl __gt_enqueue
    arm64.leaGlobal x0, __sched_lock
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.bl __sched_wake_or_spawn
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
  strandregister:
    arm64.movRegReg x1, x23
    arm64.b strandnext
  stranddroppin:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x23 + 456], x0
    arm64.storeBaseDispReg.word64 [x23 + 448], x0
    arm64.storeBaseDispReg.word64 [x19 + 624], x0
    arm64.movImm x0, 18446744073709551615
    arm64.leaGlobal x1, __gt_hold_epoch
    arm64.arm64AtomicRmw.add x0, [x1], x0
    arm64.b strandreap
  strandhandedoff:
    arm64.movImm x0, 18446744073709551615
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.epilogue 80
    arm64.ret
}

func @__gt_register_park {
  entry:
    arm64.prologue 48
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.movRegReg x19, x0
    arm64.loadRegBaseDisp.word64 x0, [x19 + 288]
    arm64.loadRegBaseDisp.word64 x20, [x19 + 296]
    arm64.movImm x21, 3
    arm64.movImm x22, 0
    arm64.cmp x0, 1
    arm64.b.ne notparktimer
  parktimer:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x21, [x0]
    arm64.movImm x2, 0
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x20
    arm64.bl __gt_timer_add
    arm64.cmp x20, x21
    arm64.b.ge parktimerpaid
  parktimerwake:
    arm64.movRegReg x0, x20
    arm64.bl __sched_wake_netpoller
  parktimerpaid:
    arm64.movRegReg x0, x22
    arm64.b parkdone
  notparktimer:
    arm64.cmp x0, 8
    arm64.cset x1, eq
    arm64.cmp x0, 10
    arm64.cset x2, eq
    arm64.orr x1, x1, x2
    arm64.cbz x1, notparknetpoll
  parknetpoll:
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x20, [x0]
    arm64.movRegReg x0, x19
    arm64.bl __np_pd_commit
    arm64.movRegReg x19, x0
    arm64.leaGlobal x0, __gt_timer_when
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, x20
    arm64.b.ge parknetpollpaid
  parknetpollwake:
    arm64.bl __sched_wake_netpoller
  parknetpollpaid:
    arm64.movRegReg x0, x19
    arm64.b parkdone
  notparknetpoll:
    arm64.cmp x0, 4
    arm64.b.ne notparkmailbox
  parkmailbox:
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, 1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.add x0, x20, 64
    arm64.bl mrt_linux_lock_enter
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.cmp x0, 0
    arm64.b.ne parkmboxundo
  parkmboxclosed:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 24]
    arm64.cmp x0, 0
    arm64.b.ne parkmboxundo
  parkmboxwait:
    arm64.movImm x0, 3
    arm64.storeBaseDispReg.word64 [x20 + 16], x19
    arm64.storeBaseDispReg.word64 [x19 + 16], x0
    arm64.add x0, x20, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movImm x0, 0
    arm64.b parkdone
  parkmboxundo:
    arm64.add x0, x20, 64
    arm64.bl mrt_linux_lock_leave
    arm64.leaGlobal x0, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x0, [x0 + 0]
    arm64.leaGlobal x1, __sched_tls_teb_offset
    arm64.loadRegBaseDisp.word64 x1, [x1 + 0]
    arm64.arm64ThreadPointer x16, linuxWritable
    arm64.add x16, x16, x1
    arm64.cmp x1, 0
    arm64.cset x17, ne
    arm64.loadRegBaseDisp.word64 x16, [x16 + 0]
    arm64.mul x1, x16, x17
    arm64.cmp x0, 0
    arm64.cset x0, ne
    arm64.cmp x1, 0
    arm64.cset x2, ne
    arm64.and x0, x0, x2
    arm64.movImm x2, 0
    arm64.sub x2, x2, x0
    arm64.sub x0, x0, 1
    arm64.add x1, x1, 592
    arm64.leaGlobal x3, __sched_lock_depth_sink
    arm64.and x1, x1, x2
    arm64.and x0, x3, x0
    arm64.orr x0, x1, x0
    arm64.loadRegBaseDisp.word64 x1, [x0 + 0]
    arm64.add x1, x1, -1
    arm64.storeBaseDispReg.word64 [x0 + 0], x1
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready_locked
    arm64.b parkdone
  notparkmailbox:
    arm64.cmp x0, 5
    arm64.b.ne notparkawait
  parkawait:
    arm64.add x16, x20, 16
    arm64.ldar.word64 x0, [x16]
    arm64.cmp x0, 2
    arm64.b.eq parkresumenow
  parkawaitregister:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 368]
    arm64.cmp x0, 0
    arm64.b.eq claimawaitertake#30
  claimawaitertwice#31:
    arm64.leaRdata x0, __abort_msg_102  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    arm64.movImm x1, 62
    arm64.movImm x2, 102
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  claimawaitertake#30:
    arm64.storeBaseDispReg.word64 [x20 + 368], x19
  parkawaittake:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
    arm64.b parkdone
  notparkawait:
    arm64.cmp x0, 6
    arm64.b.ne notparkawaitany
  parkawaitany:
    arm64.movRegReg x0, x20
    arm64.bl __gt_any_completed
    arm64.cmp x0, -1
    arm64.b.ne parkresumenow
  parkawaitanyregister:
    arm64.movImm x0, 0
    arm64.storeBaseDispReg.word64 [x19 + 376], x0
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x1, [x20 + 8]
    arm64.orr x2, x19, 1
    arm64.movImm x3, 0
  anywalkhdr:
    arm64.cmp x3, x1
    arm64.b.lt anywalkbody
  parkawaitanyregistered:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
    arm64.b parkdone
  anywalkbody:
    arm64.loadRegBaseIndexScale.word64 x4, [x0 + x3*8 + 0]
    arm64.cmp x4, 0
    arm64.b.eq anywalknext
  anywalkslot:
    arm64.loadRegBaseDisp.word64 x5, [x4 + 368]
    arm64.cmp x5, 0
    arm64.b.eq claimawaitertake#36
  claimawaitertwice#37:
    arm64.leaRdata x0, __abort_msg_102  ; "fatal error: runtime abort 102 (schedulerPromiseAwaitedTwice)\x0a"
    arm64.movImm x1, 62
    arm64.movImm x2, 102
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  claimawaitertake#36:
    arm64.storeBaseDispReg.word64 [x4 + 368], x2
  anywalknext:
    arm64.add x3, x3, 1
    arm64.b anywalkhdr
  notparkawaitany:
    arm64.cmp x0, 11
    arm64.b.ne parkunknown
  parkquiesce:
    arm64.leaGlobal x0, __gt_quiesce_waiter
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne parkquiescetaken
  parkquiesceregister:
    arm64.leaGlobal x0, __gt_quiesce_waiter
    arm64.arm64AtomicRmw.add x0, [x0], x19
    arm64.leaGlobal x0, __gt_live_count
    arm64.ldar.word64 x0, [x0]
    arm64.cmp x0, 0
    arm64.b.ne parkquiescetake
  parkquiesceundo:
    arm64.movImm x0, 0
    arm64.sub x0, x0, x19
    arm64.leaGlobal x1, __gt_quiesce_waiter
    arm64.arm64AtomicRmw.add x0, [x1], x0
  parkresumenow:
    arm64.movRegReg x0, x19
    arm64.bl __gt_ready_locked
    arm64.b parkdone
  parkquiescetake:
    arm64.storeBaseDispReg.word64 [x19 + 16], x21
    arm64.movRegReg x0, x22
  parkdone:
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  parkunknown:
    arm64.leaRdata x0, __abort_msg_93  ; "fatal error: runtime abort 93 (schedulerUnknownParkKind)\x0a"
    arm64.movImm x1, 57
    arm64.movImm x2, 93
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
  parkquiescetaken:
    arm64.leaRdata x0, __abort_msg_123  ; "fatal error: runtime abort 123 (schedulerQuiesceWaiterTaken)\x0a"
    arm64.movImm x1, 61
    arm64.movImm x2, 123
    arm64.bl __rt_abort
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.epilogue 48
    arm64.ret
}
```
