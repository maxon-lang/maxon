---
feature: rdata-section
status: experimental
keywords: [rdata, rodata, const, section, layout, descriptor, linker]
category: codegen
---
# The `.rdata` Section

## Documentation

A program's READ-ONLY data — string-literal blobs and records, float constants, jump
tables, witness tables and `__layout_*` descriptors — is laid down by the linker in the
target's read-only section: `.rdata` in a PE, `.rodata` in an ELF, `__DATA,__const` in a
Mach-O image.

### Pinning the bytes: ```RequiredRdata

A spec pins read-only bytes with a ```RequiredRdata block of typed values, one per line —
the twin of ```RequiredData, which pins `.data`:

```
i64 16
i64 8
```

It is read back OUT of the linked binary, never out of the compiler's opinion of it, which
is what makes it the only gate that can see a section header with a wrong RVA or a payload
the writer moved.

#### Two forms, because a line can name two different things

**How a block is compared depends on which form its lines use, and the split is about what a
line NAMES rather than about convenience.**

**Scalar and array lines — `i64 16`, `u8[] 0, 1, 2`, `f64 3.14` — describe FIELDS OF ONE
PAYLOAD.** Several of them are one blob written across several lines, so their bytes are
contiguous by construction and where that blob sits is part of the claim. Such a block is
compared as **a run from byte 0**, as a PREFIX: a program may hold more read-only data after
the bytes the block names.

⚠ **A prefix pins a prefix.** Bytes past the last one a block names are unpinned, so a change
that APPENDS to the read-only section is invisible to this gate, while one that moves the
pinned payload, reorders what precedes it, or shortens the section fails loudly. Because the
section is a concatenation in registration order, a **second program literal written above
the pinned payload displaces it** — so a case using this form should carry one program
literal, and choose a program whose read-only image is small enough that the block covers it.

**A `utf8 "…"` line names ONE INTERNED STRING BLOB** — a separately-registered payload with
its own label. Whatever sits next to that blob is decided by what else happened to register
around it: another literal, a 48-byte `String` record, alignment padding. That is a fact
about the rest of the program, not about the string being pinned, so **adjacency is not a
property worth pinning**. Such a block's lines are therefore **located independently and in
order**: each payload must appear somewhere at or after the end of the previous line's match,
and none is required to start at byte 0. The bytes and the sequence are pinned; the gaps are
not. Two blobs written in one order do not pass if the image places them in the other.

⛔ **A block may not MIX the two forms.** They disagree about whether a block's lines are one
payload or several, so a mixed block has no meaning to compute; it is refused as a malformed
pin naming both spellings, rather than resolved by guessing one.

An EMPTY ```RequiredRdata block is refused by the spec parser rather than recorded: the empty
prefix matches every image ever linked, so it cannot fail, and it cannot express "the section
is empty" either. A block on a case that expects a COMPILE ERROR is refused for the same
reason — that case never links, so there is no image to read.

### A pinned case needs no `<!-- unsupported-targets: … -->` marker

Only a native container format keeps the sections these blocks name — `.rdata`/`.data` in a
PE, `.rodata`/`.data` in an ELF, `__DATA,__const`/`__DATA,__data` in a Mach-O image. A WASI
component keeps neither: its data lives in `(data …)` segments addressed by a linear-memory
offset, with no section header a reader could name.

The runner DERIVES that restriction from the block itself, so a case carrying one runs on
every native target and is excluded from `wasm32-wasi` with nothing written down
(`SpecTestRunner.targetCanCheckSectionPins`, over `LinkedSection.targetHasLinkedSections`).
⛔ **Do not spell it as a marker.** Twenty-odd cases across four files once did, and the one
file that forgot — `enum-narrow-storage.md` — panicked reading a section out of a wasm
component, killed its worker and ABORTED THE WHOLE LANE, so the wasm gate reported nothing at
all. The cases in that file still carry no marker, deliberately: they are what proves the
derived rule still fires.

### Why it exists

A `.rdata` payload registered under a STRUCTURAL label — a `__layout_*` descriptor, a
witness table, a Unicode category table — does not mint a `{prefix}{nextStringId}` name,
so nothing a `TargetIr` pin prints moves when its bytes or its offset change. The
pin shows which label a `lea` names; only this block shows what is IN it.

## Tests

<!-- test: layout-descriptor-payload -->
`Sizer with bool`'s layout descriptor is the program's whole read-only image: nine
machine words — size, alignment, elementSize, four zero slots, elementLogicalSize, and the
`retainFunc@64` a `bool` argument leaves 0.

⚠ The ninth word is pinned deliberately. This block is checked as a PREFIX of the image, so a
descriptor that grew a slot would go on passing an eight-word pin while the prose above described
a blob that does not exist.

```maxon
typealias Integer = int(i64.min to i64.max)

type Sizer uses T
	export var dummy as T

	static function create(dummy T) returns Self
		return Self{dummy: dummy}
	end 'create'

	function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias BoolSizer = Sizer with bool

function main() returns ExitCode
	let s = BoolSizer.create(false)
	return s.typeSize()
end 'main'
```
```exitcode
1
```
```RequiredRdata
i64 8
i64 8
i64 8
i64 0
i64 0
i64 0
i64 0
i64 1
i64 0
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
    x64.prologue 40
    x64.movRegImm32 rbx, 0
  __il_body#6:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#5:
    x64.leaRegRdata rax, [rip + __layout_Sizer_bool]
  __il_body#7:
    x64.loadRegBaseDisp.word64 rbx, [rax + 56]
  __il_cont#4:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.movRegImm32 rax, 4294967295
    x64.cmpRegReg rbx, rax
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at layout-descriptor-payload.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    x64.prologue 40
    x64.movRegImm32 rbx, 0
  __il_body#6:
    x64.movRegImm32 rcx, 8
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeBaseDispReg.word64 [r8 + 0], rbx
  __il_cont#5:
    x64.leaRegRdata rax, [rip + __layout_Sizer_bool]
  __il_body#7:
    x64.loadRegBaseDisp.word64 rbx, [rax + 56]
  __il_cont#4:
    x64.cmpRegImm32 rbx, 0
    x64.jcc less, __rc_panic
  __rc_chk:
    x64.cmpRegImm32 rbx, 255
    x64.jcc greater, __rc_panic
  __rc_ok:
    x64.movRegReg rcx, r8
    x64.callDirect __mm_decref
    x64.movRegReg r8, rbx
    x64.epilogue 40
    x64.popReg rbx
    x64.popReg rbp
    x64.ret
  __rc_panic:
    x64.leaRegRdata rcx, [rip + __str_blob_1]  ; "panic at layout-descriptor-payload.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 40
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
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
  __il_body#6:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
  __il_cont#5:
    arm64.leaRdata x1, __layout_Sizer_bool
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x19, [x1 + 56]
  __il_cont#4:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at layout-descriptor-payload.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
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
    arm64.storeSlotReg slot0, x19
    arm64.movImm x19, 0
  __il_body#6:
    arm64.movImm x0, 8
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.storeBaseDispReg.word64 [x0 + 0], x19
  __il_cont#5:
    arm64.leaRdata x1, __layout_Sizer_bool
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x19, [x1 + 56]
  __il_cont#4:
    arm64.cmp x19, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x19, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.bl __mm_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_1  ; "panic at layout-descriptor-payload.test:20: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.epilogue 32
    arm64.ret
}
```

<!-- test: interned-blobs-are-located-not-adjacent -->
Three string literals, of which this block pins the FIRST and the THIRD. `MIDDLE-PAYLOAD`'s
blob sits between them in the image, and each blob is padded to an 8-byte boundary, so the
two pinned payloads are neither at byte 0 together nor adjacent to each other.

⭐ **This is the case the located form exists for, and it would FAIL under a run-from-byte-0
comparison** — that reading expects `region-two\0` to begin the byte after `region-one\0`
ends, where the image has padding and a whole other blob there. What the spec can honestly
claim about an interned blob is its BYTES and its ORDER relative to the block's other blobs,
which is what is checked: `region-one` is found, and `region-two` is found after it.

⚠ Reordering the two lines therefore FAILS, and that is the point — the order is a real
constraint, not an "appears somewhere" containment that a re-ordered image would slip past.

```maxon
function main() returns ExitCode
	let first = "region-one"
	let middle = "MIDDLE-PAYLOAD"
	let last = "region-two"
	return first.byteLength() + middle.byteLength() + last.byteLength()
end 'main'
```
```exitcode
34
```
```RequiredRdata
utf8 "region-one\0"
utf8 "region-two\0"
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
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "region-one"
    x64.leaRegRdata rcx, [rip + __str_rec_4]  ; "MIDDLE-PAYLOAD"
    x64.leaRegRdata rdx, [rip + __str_rec_5]  ; "region-two"
  __il_body#7:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg rax, rax, rcx
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
  __il_cont#4:
    x64.leaRegRegReg r8, rax, rcx
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
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at interned-blobs-are-located-not-adjacent.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    x64.pushReg rbp
    x64.movRegReg rbp, rsp
    x64.prologue 32
    x64.leaRegRdata rax, [rip + __str_rec_3]  ; "region-one"
    x64.leaRegRdata rcx, [rip + __str_rec_4]  ; "MIDDLE-PAYLOAD"
    x64.leaRegRdata rdx, [rip + __str_rec_5]  ; "region-two"
  __il_body#7:
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
  __il_body#8:
    x64.loadRegBaseDisp.word64 rcx, [rcx + 8]
  __il_cont#5:
    x64.leaRegRegReg rax, rax, rcx
  __il_body#9:
    x64.loadRegBaseDisp.word64 rcx, [rdx + 8]
  __il_cont#4:
    x64.leaRegRegReg r8, rax, rcx
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
    x64.leaRegRdata rcx, [rip + __str_blob_7]  ; "panic at interned-blobs-are-located-not-adjacent.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    x64.callDirect mrt_panic
    x64.movRegImm32 r8, 0
    x64.epilogue 32
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
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_3  ; "region-one"
    arm64.leaRdata x1, __str_rec_4  ; "MIDDLE-PAYLOAD"
    arm64.leaRdata x2, __str_rec_5  ; "region-two"
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x0, x0, x1
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 8]
  __il_cont#4:
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_7  ; "panic at interned-blobs-are-located-not-adjacent.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
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
  __slab_state@16 = i64 0
  __mrt_program_started@24 = i8 0
}

func @main {
  entry:
    arm64.prologue 16
    arm64.leaRdata x0, __str_rec_3  ; "region-one"
    arm64.leaRdata x1, __str_rec_4  ; "MIDDLE-PAYLOAD"
    arm64.leaRdata x2, __str_rec_5  ; "region-two"
  __il_body#7:
    arm64.loadRegBaseDisp.word64 x0, [x0 + 8]
  __il_body#8:
    arm64.loadRegBaseDisp.word64 x1, [x1 + 8]
  __il_cont#5:
    arm64.add x0, x0, x1
  __il_body#9:
    arm64.loadRegBaseDisp.word64 x1, [x2 + 8]
  __il_cont#4:
    arm64.add x0, x0, x1
    arm64.cmp x0, 0
    arm64.b.lt __rc_panic
  __rc_chk:
    arm64.cmp x0, 255
    arm64.b.gt __rc_panic
  __rc_ok:
    arm64.epilogue 16
    arm64.ret
  __rc_panic:
    arm64.leaRdata x0, __str_blob_7  ; "panic at interned-blobs-are-located-not-adjacent.test:6: Range check failed: value outside typealias 'ExitCode'\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.epilogue 16
    arm64.ret
}
```
