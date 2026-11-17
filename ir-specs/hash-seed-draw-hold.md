---
feature: hash-seed-draw-hold
status: experimental
keywords: [hash, seed, random, hold, retain, borrow, codegen]
category: memory
---

# A hash seed draw ends no hold

## Documentation

`HashSeed.random()` draws its seed from the runtime. The draw is an effect, so it is never folded away or
moved, but it releases no managed value of the program: an element read out of an array before the draw stays
borrowed across it, and the reading body takes no reference to the element.

## Tests

<!-- test: a-counted-read-held-across-a-hash-seed-draw-is-not-retained -->
`agrees` reads `word` out of the array its caller still holds, draws a seed, then hashes `word` under that
seed. `main`, which inlines `agrees`, takes no reference to `word` around the draw. Both folds hash the bytes
`beta` under one seed, so they agree whatever the seed is. Prints `same 3`.

```maxon
typealias ByteArrays = Array with ByteArray

function agrees(words ByteArrays) returns bool
	let word = try words.get(1) otherwise panic("the array holds three words")
	let seed = HashSeed.random()
	let left = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: word)
	let right = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: b"beta")

	return left == right
end 'agrees'

function main() returns ExitCode
	let words = ["alpha".toByteArray(), "beta".toByteArray(), "gamma".toByteArray()]

	if agrees(words) 'same'
		print("same {words.count()}\n")
	end 'same'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
same 3
```
```TargetIr:x64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_cpu_aes_usable@16 = i64 0
  __mrt_cpu_sha_usable@24 = i64 0
  __mrt_hash_seed@32 = i64 0
  __mrt_hash_tier@40 = i64 0
  __mrt_hash_secret@48 = i64 0
  __mrt_hash_pinned@56 = i64 0
  __mrt_hash_library_draws@64 = i64 0
  __slab_arena_list@72 = i64 0
  __slab_arena_map_l1@80 = i64 0
  __slab_state@88 = i64 0
  __mrt_program_started@96 = i8 0
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
    x64.leaRegRdata rcx, [rip + __str_rec_9]  ; "alpha"
    x64.callDirect String.toByteArray
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __managed_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_10]  ; "beta"
    x64.callDirect String.toByteArray
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_11]  ; "gamma"
    x64.callDirect String.toByteArray
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
  __il_body#7:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 40]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit#24
  __im_empty:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __im_slow
  critsplit#25:
    x64.movRegReg r10, rax
    x64.jmp __im_cont
  critsplit#24:
    x64.movRegReg r10, rax
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  __il_body#17:
    x64.callDirect __hash_seed_draw
  __il_body#19:
    x64.leaRegRdata rcx, [rip + __barr_rec_14]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegReg r9, r8
    x64.callDirect __hash_bytes
    x64.movRegReg r13, r8
  __il_cont#21:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect KeyedHasher.combinedBytes
    x64.movRegReg rbx, r8
  __il_cont#20:
    x64.leaRegRdata rdx, [rip + __barr_rec_7]
    x64.movRegReg rcx, r13
    x64.callDirect KeyedHasher.combinedBytes
    x64.cmpRegReg rbx, r8
    x64.setccReg equal, rax
  __il_cont#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  same:
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "same "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __uint_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_13]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, r12
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#4:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
  ifcont:
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
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.movRegReg rbx, r8
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "panic at a-counted-read-held-across-a-hash-seed-draw-is-not-retained.test:5: the array holds three words\x0a"
    x64.callDirect mrt_panic
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
```
```TargetIr:x64-windows
data {
  __mrt_cpu_aes_usable@24 = i64 0
  __mrt_cpu_sha_usable@32 = i64 0
  __mrt_hash_seed@40 = i64 0
  __mrt_hash_tier@48 = i64 0
  __mrt_hash_secret@56 = i64 0
  __mrt_hash_pinned@64 = i64 0
  __mrt_hash_library_draws@72 = i64 0
  __slab_arena_list@80 = i64 0
  __slab_arena_map_l1@88 = i64 0
  __slab_state@96 = i64 0
  __mrt_console_probe_stdin@104 = i8 0
  __mrt_console_probe_stdout@105 = i8 0
  __mrt_console_probe_stderr@106 = i8 0
  __mrt_program_started@107 = i8 0
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
    x64.leaRegRdata rcx, [rip + __str_rec_9]  ; "alpha"
    x64.callDirect String.toByteArray
    x64.movRegReg rbx, r8
    x64.movRegImm32 rcx, 8
    x64.leaRegFunc rdx, [rip + __managed_decref]
    x64.callDirect __managed_create
    x64.movRegReg r12, r8
    x64.storeSlotReg slot0, r12
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, rbx
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_10]  ; "beta"
    x64.callDirect String.toByteArray
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
    x64.leaRegRdata rcx, [rip + __str_rec_11]  ; "gamma"
    x64.callDirect String.toByteArray
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r8
    x64.callDirect __managed_push
  __il_body#7:
    x64.movRegImm32 rdx, 1
    x64.loadRegBaseDisp.word64 rax, [r12 + 8]
    x64.cmpRegImm32 rax, 1
    x64.jcc belowEqual, __im_slow
  __im_load:
    x64.loadRegBaseDisp.word64 rax, [r12 + 0]
    x64.loadRegBaseDisp.word64 rbx, [rax + 8]
    x64.movRegImm32 rax, 0
    x64.loadRegBaseDisp.word64 rcx, [r12 + 40]
    x64.cmpRegImm32 rcx, 0
    x64.jcc equal, critsplit#24
  __im_empty:
    x64.cmpRegImm32 rbx, 0
    x64.jcc equal, __im_slow
  critsplit#25:
    x64.movRegReg r10, rax
    x64.jmp __im_cont
  critsplit#24:
    x64.movRegReg r10, rax
  __im_cont:
    x64.cmpRegImm32 r10, 0
    x64.jcc notEqual, tryerr
  __il_body#17:
    x64.callDirect __hash_seed_draw
  __il_body#19:
    x64.leaRegRdata rcx, [rip + __barr_rec_14]
    x64.movRegImm32 rdx, 0
    x64.movRegImm32 rax, 0
    x64.movRegReg r9, r8
    x64.callDirect __hash_bytes
    x64.movRegReg r13, r8
  __il_cont#21:
    x64.movRegReg rcx, r13
    x64.movRegReg rdx, rbx
    x64.callDirect KeyedHasher.combinedBytes
    x64.movRegReg rbx, r8
  __il_cont#20:
    x64.leaRegRdata rdx, [rip + __barr_rec_7]
    x64.movRegReg rcx, r13
    x64.callDirect KeyedHasher.combinedBytes
    x64.cmpRegReg rbx, r8
    x64.setccReg equal, rax
  __il_cont#6:
    x64.cmpRegImm32 rax, 0
    x64.jcc equal, ifcont
  same:
    x64.leaRegRdata rax, [rip + __str_rec_12]  ; "same "
    x64.loadRegBaseDisp.word64 rbx, [rax + 0]
    x64.loadRegBaseDisp.word64 r13, [rax + 8]
    x64.loadRegBaseDisp.word64 r12, [r12 + 8]
    x64.movRegImm32 rcx, 20
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.movRegReg r14, r8
    x64.movRegReg rcx, r12
    x64.movRegReg rdx, r14
    x64.callDirect __uint_to_string
    x64.movRegReg r12, r8
    x64.leaRegRdata rax, [rip + __str_rec_13]  ; "\x0a"
    x64.loadRegBaseDisp.word64 rcx, [rax + 0]
    x64.storeSlotReg slot4, rcx
    x64.loadRegBaseDisp.word64 rax, [rax + 8]
    x64.storeSlotReg slot3, rax
    x64.leaRegRegReg rcx, r13, r12
    x64.leaRegRegReg rax, rcx, rax
    x64.storeSlotReg slot1, rax
    x64.leaRegRegImm32 rcx, rax, 57
    x64.movRegImm32 rdx, 0
    x64.callDirect __mm_alloc
    x64.storeSlotReg slot2, r8
    x64.leaRegRegImm32 r15, r8, 56
    x64.movRegReg rcx, r15
    x64.movRegReg rdx, rbx
    x64.movRegReg rax, r13
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, r15, r13
    x64.movRegReg rcx, rbx
    x64.movRegReg rdx, r14
    x64.movRegReg rax, r12
    x64.callDirect __str_copy
    x64.leaRegRegReg rbx, rbx, r12
    x64.loadRegSlot rax, slot3
    x64.loadRegSlot rdx, slot4
    x64.movRegReg rcx, rbx
    x64.callDirect __str_copy
    x64.loadRegSlot rax, slot3
    x64.leaRegRegReg rax, rbx, rax
    x64.loadRegSlot rax, slot2
    x64.storeBaseDispReg.word64 [rax + 0], r15
    x64.loadRegSlot rcx, slot1
    x64.storeBaseDispReg.word64 [rax + 8], rcx
    x64.movRegImm rcx, 18446744073709551613
    x64.storeBaseDispReg.word64 [rax + 16], rcx
    x64.movRegImm32 rcx, 1
    x64.storeBaseDispReg.word64 [rax + 24], rcx
    x64.movRegReg rcx, r14
    x64.callDirect __mm_decref
  __il_body#5:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __write_stdout
  __il_cont#4:
    x64.loadRegSlot rcx, slot2
    x64.callDirect __str_decref
  ifcont:
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
  __im_slow:
    x64.movRegReg rcx, r12
    x64.callDirect __managed_get
    x64.movRegReg rbx, r8
    x64.jmp __im_cont
  tryerr:
    x64.leaRegRdata rcx, [rip + __str_blob_8]  ; "panic at a-counted-read-held-across-a-hash-seed-draw-is-not-retained.test:5: the array holds three words\x0a"
    x64.callDirect mrt_panic
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
```
```TargetIr:arm64-macos
data {
  __mrt_cpu_aes_usable@0 = i64 1
  __mrt_hash_seed@8 = i64 0
  __mrt_hash_tier@16 = i64 0
  __mrt_hash_secret@24 = i64 0
  __mrt_hash_pinned@32 = i64 0
  __mrt_hash_library_draws@40 = i64 0
  __slab_arena_list@48 = i64 0
  __slab_arena_map_l1@56 = i64 0
  __slab_state@64 = i64 0
  __mrt_program_started@72 = i8 0
}

func @main {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_9  ; "alpha"
    arm64.bl String.toByteArray
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __managed_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_10  ; "beta"
    arm64.bl String.toByteArray
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_11  ; "gamma"
    arm64.bl String.toByteArray
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __il_body#7:
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.movImm x0, 0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 40]
    arm64.cmp x2, 0
    arm64.b.eq critsplit#24
  __im_empty:
    arm64.cmp x19, 0
    arm64.b.eq __im_slow
  critsplit#25:
    arm64.movRegReg x9, x0
    arm64.b __im_cont
  critsplit#24:
    arm64.movRegReg x9, x0
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  __il_body#17:
    arm64.bl __hash_seed_draw
    arm64.movRegReg x3, x0
  __il_body#19:
    arm64.leaRdata x0, __barr_rec_14
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.bl __hash_bytes
    arm64.movRegReg x21, x0
  __il_cont#21:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl KeyedHasher.combinedBytes
    arm64.movRegReg x19, x0
  __il_cont#20:
    arm64.leaRdata x1, __barr_rec_7
    arm64.movRegReg x0, x21
    arm64.bl KeyedHasher.combinedBytes
    arm64.cmp x19, x0
    arm64.cset x0, eq
  __il_cont#6:
    arm64.cbz x0, ifcont
  same:
    arm64.leaRdata x0, __str_rec_12  ; "same "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_13  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x21, x22
    arm64.add x26, x0, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#5:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#4:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  ifcont:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x19, x0
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_8  ; "panic at a-counted-read-held-across-a-hash-seed-draw-is-not-retained.test:5: the array holds three words\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
}
```
```TargetIr:arm64-linux
data {
  __mrt_envp@0 = i64 0
  __mrt_signal_stack_bytes@8 = i64 0
  __mrt_hwcap_atomics@16 = i64 0
  __mrt_cpu_aes_usable@24 = i64 0
  __mrt_cpu_sha_usable@32 = i64 0
  __mrt_hash_seed@40 = i64 0
  __mrt_hash_tier@48 = i64 0
  __mrt_hash_secret@56 = i64 0
  __mrt_hash_pinned@64 = i64 0
  __mrt_hash_library_draws@72 = i64 0
  __slab_arena_list@80 = i64 0
  __slab_arena_map_l1@88 = i64 0
  __slab_state@96 = i64 0
  __mrt_program_started@104 = i8 0
}

func @main {
  entry:
    arm64.prologue 96
    arm64.storeSlotReg slot9, x28
    arm64.storeSlotReg slot8, x27
    arm64.storeSlotReg slot7, x26
    arm64.storeSlotReg slot6, x25
    arm64.storeSlotReg slot5, x24
    arm64.storeSlotReg slot4, x23
    arm64.storeSlotReg slot3, x22
    arm64.storeSlotReg slot2, x21
    arm64.storeSlotReg slot1, x20
    arm64.storeSlotReg slot0, x19
    arm64.leaRdata x0, __str_rec_9  ; "alpha"
    arm64.bl String.toByteArray
    arm64.movRegReg x19, x0
    arm64.movImm x0, 8
    arm64.leaFuncAddr x1, __managed_decref
    arm64.bl __managed_create
    arm64.movRegReg x20, x0
    arm64.movRegReg x0, x20
    arm64.movRegReg x1, x19
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_10  ; "beta"
    arm64.bl String.toByteArray
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
    arm64.leaRdata x0, __str_rec_11  ; "gamma"
    arm64.bl String.toByteArray
    arm64.movRegReg x1, x0
    arm64.movRegReg x0, x20
    arm64.bl __managed_push
  __il_body#7:
    arm64.movImm x1, 1
    arm64.loadRegBaseDisp.word64 x0, [x20 + 8]
    arm64.cmp x0, 1
    arm64.b.ls __im_slow
  __im_load:
    arm64.loadRegBaseDisp.word64 x0, [x20 + 0]
    arm64.loadRegBaseDisp.word64 x19, [x0 + 8]
    arm64.movImm x0, 0
    arm64.loadRegBaseDisp.word64 x2, [x20 + 40]
    arm64.cmp x2, 0
    arm64.b.eq critsplit#24
  __im_empty:
    arm64.cmp x19, 0
    arm64.b.eq __im_slow
  critsplit#25:
    arm64.movRegReg x9, x0
    arm64.b __im_cont
  critsplit#24:
    arm64.movRegReg x9, x0
  __im_cont:
    arm64.cmp x9, 0
    arm64.b.ne tryerr
  __il_body#17:
    arm64.bl __hash_seed_draw
    arm64.movRegReg x3, x0
  __il_body#19:
    arm64.leaRdata x0, __barr_rec_14
    arm64.movImm x1, 0
    arm64.movImm x2, 0
    arm64.bl __hash_bytes
    arm64.movRegReg x21, x0
  __il_cont#21:
    arm64.movRegReg x0, x21
    arm64.movRegReg x1, x19
    arm64.bl KeyedHasher.combinedBytes
    arm64.movRegReg x19, x0
  __il_cont#20:
    arm64.leaRdata x1, __barr_rec_7
    arm64.movRegReg x0, x21
    arm64.bl KeyedHasher.combinedBytes
    arm64.cmp x19, x0
    arm64.cset x0, eq
  __il_cont#6:
    arm64.cbz x0, ifcont
  same:
    arm64.leaRdata x0, __str_rec_12  ; "same "
    arm64.loadRegBaseDisp.word64 x19, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x21, [x0 + 8]
    arm64.loadRegBaseDisp.word64 x22, [x20 + 8]
    arm64.movImm x0, 20
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x23, x0
    arm64.movRegReg x0, x22
    arm64.movRegReg x1, x23
    arm64.bl __uint_to_string
    arm64.movRegReg x22, x0
    arm64.leaRdata x0, __str_rec_13  ; "\x0a"
    arm64.loadRegBaseDisp.word64 x24, [x0 + 0]
    arm64.loadRegBaseDisp.word64 x25, [x0 + 8]
    arm64.add x0, x21, x22
    arm64.add x26, x0, x25
    arm64.add x0, x26, 57
    arm64.movImm x1, 0
    arm64.bl __mm_alloc
    arm64.movRegReg x27, x0
    arm64.add x28, x27, 56
    arm64.movRegReg x0, x28
    arm64.movRegReg x1, x19
    arm64.movRegReg x2, x21
    arm64.bl __str_copy
    arm64.add x19, x28, x21
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x23
    arm64.movRegReg x2, x22
    arm64.bl __str_copy
    arm64.add x19, x19, x22
    arm64.movRegReg x0, x19
    arm64.movRegReg x1, x24
    arm64.movRegReg x2, x25
    arm64.bl __str_copy
    arm64.add x0, x19, x25
    arm64.storeBaseDispReg.word64 [x27 + 0], x28
    arm64.storeBaseDispReg.word64 [x27 + 8], x26
    arm64.movImm x0, 18446744073709551613
    arm64.storeBaseDispReg.word64 [x27 + 16], x0
    arm64.movImm x0, 1
    arm64.storeBaseDispReg.word64 [x27 + 24], x0
    arm64.movRegReg x0, x23
    arm64.bl __mm_decref
  __il_body#5:
    arm64.movRegReg x0, x27
    arm64.bl __write_stdout
  __il_cont#4:
    arm64.movRegReg x0, x27
    arm64.bl __str_decref
  ifcont:
    arm64.movImm x19, 0
    arm64.movRegReg x0, x20
    arm64.bl __managed_decref
    arm64.movRegReg x0, x19
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
  __im_slow:
    arm64.movRegReg x0, x20
    arm64.bl __managed_get
    arm64.movRegReg x19, x0
    arm64.b __im_cont
  tryerr:
    arm64.leaRdata x0, __str_blob_8  ; "panic at a-counted-read-held-across-a-hash-seed-draw-is-not-retained.test:5: the array holds three words\x0a"
    arm64.bl mrt_panic
    arm64.movImm x0, 0
    arm64.loadRegSlot x19, slot0
    arm64.loadRegSlot x20, slot1
    arm64.loadRegSlot x21, slot2
    arm64.loadRegSlot x22, slot3
    arm64.loadRegSlot x23, slot4
    arm64.loadRegSlot x24, slot5
    arm64.loadRegSlot x25, slot6
    arm64.loadRegSlot x26, slot7
    arm64.loadRegSlot x27, slot8
    arm64.loadRegSlot x28, slot9
    arm64.epilogue 96
    arm64.ret
}
```
