---
title: Memory Allocator
description: The runtime heap every Maxon program carries, the zeroing contract it keeps, and the fill ladders each back end emits.
sidebar:
  order: 4
---

The heap is part of the runtime the compiler emits into every program that allocates
(`maxon-bin/Compiler/Runtime/` and `runtime/`). How a program's memory is owned and freed is in
[Memory Management](../MEMORY_MANAGEMENT.md); this page is about the allocator underneath.

## Structure

- The arena (`runtime/SlabArena.maxon`, with its geometry in `SlabArena.maxon`) supplies 8 KiB chunks from
  memory it reserves from the operating system.
- `SlabRuntime.maxon` carves them into spans of one size class each. A request above 32 KiB bypasses the size
  classes and goes to the operating system directly, and is returned to it whole when freed.
- `MmRuntime.maxon` is the box layer on top: `__mm_alloc` asks `__slab_alloc` for the box, header and payload
  together, and `__mm_free` hands the same pointer back to `__slab_free`.

Each processor (P) of the scheduler owns a cache of spans and is their only writer, so allocating from it
takes no lock. A slot freed by another P is pushed onto the owning P's queue with a compare-and-swap, and the
owner reclaims it on its slow path. A thread holding no P allocates from a shared row under a global lock.

A span serves slots from two places: its free list of slots that were allocated and freed, and a bump region
of slots never handed out. The bump region is never threaded into the free list up front, because writing a
link into every slot of a fresh span would dirty every page of it. A span whose slots are all free is
destroyed by the scavenger and its chunks returned to the arena, which decommits memory a 64 KiB granule at
a time.

## The zeroing contract

**Every box a caller receives reads zero.** Two invariants keep it without clearing memory on the common
path:

- **Every chunk the arena hands out reads zero** (INV-4). `__slab_arena_free_chunks` zeroes a chunk run as it
  is released, before the run is marked free and before the scavenger can decommit it. The zero is written
  rather than recovered from the operating system because a recommitted page differs by platform: Windows
  and Linux return zeroes, macOS's `MADV_FREE` may return the same dirty page, and wasm32 keeps the old
  bytes. A fresh reservation is zero already.
- **Every byte of a span's bump region is zero** (INV-3), because the region is carved from chunks and is
  never written until a slot is handed out. A bump slot costs nothing to hand out.

A recycled slot is re-zeroed as it is popped from the free list: `__slab_alloc` fills the whole slot with 0
(`memFill`). `__slab_alloc_raw` is built without that fill, for a caller that provably writes every byte
before anything reads it and never keeps managed pointers in the region; it has no caller, so dead function
elimination removes it from every program.

`__mm_free` poisons the freed payload with `0x3F` before returning it (`PoisonByte`): a poisoned integer
reads as a conspicuous 63, and a poisoned pointer is a non-canonical x64 address that faults when
dereferenced. A recycled slot is therefore written twice, poisoned on free and zeroed on reuse.

Code relies on the contract: `__mm_alloc` does not store an initial refcount, because the slot already holds
0, and a C string's terminator is the zero already there. A layout that needs particular zero bytes for its
own correctness, such as an enum box's unused payload slots, stores them explicitly instead.

## memFill and the fill ladders

`StdOp.memFill(dest, byteCount, value)` fills `byteCount` bytes with the low byte of `value`; a count of 0
writes nothing. It is an op, not a Std-IR loop, because the right code depends on the size and Std IR cannot
express a size dispatch. A byte loop costs a store, an add, a compare and a branch per byte, on the
allocator's hottest paths: zeroing a recycled slot or a released chunk run, and poisoning a freed payload.
`value` is a compile-time constant, so the 64-bit pattern (`byte × 0x0101010101010101`) is computed at
compile time.

It declares `clobbersFlags`, because its ladder compares, so a compare before a `memFill` is never fused with
a branch after it. It is not a call, so a function that fills memory needs no call frame for it; instead it
clobbers a fixed set of registers, which the register allocator checks against the encoder's.

The instruction selectors cannot create blocks, so each native ladder is one macro-op whose encoder emits the
size dispatch with branches patched inside it.

### x64

`encodeX64MemFill` (`Targets/X64/X64Backend.maxon`) uses `rdi` as the cursor, `rcx` as the count, `rdx` as the
end and `rax` as the pattern — the registers `rep stosq` fixes, plus one. It computes the end first, then
dispatches on the count:

| Bytes | Code |
| --- | --- |
| 0–7 | a byte loop |
| 8–15 | one qword from the start and one ending at the end, overlapping |
| 16–31 | two qwords from each end |
| 32–63 | four qwords from each end |
| 64–255 | a loop of eight qwords, then one 64-byte block ending at the end |
| 256 and up | `rep stosq`, then one qword ending at the end |

The overlapping stores cover any length in the band without a remainder loop. `rep stosq` has a start-up cost
that only pays off at large sizes, hence its threshold.

### arm64

`arm64EmitMemFill` (`Targets/Arm64/Arm64Backend.maxon`) uses `x0` as the cursor, `x1` as the count, `x2` as the
end and `x3` as the pattern:

| Bytes | Code |
| --- | --- |
| 0–7 | a byte loop |
| 8–15 | `str` from the start and `stur` ending at the end |
| 16–31 | one `stp` pair from each end |
| 32–63 | two `stp` pairs from each end |
| 64 and up | a loop of four `stp` pairs, then one 64-byte block ending at the end |

### wasm32

`emitMemFill` (`Targets/Wasm/StdToWasm.maxon`) emits one `memory.fill`. Every wasm store is bounds-checked on
its own, while `memory.fill` is one check and an engine-tuned fill that already handles short lengths, so a
ladder of stores would be slower here.
