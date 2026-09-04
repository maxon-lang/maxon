---
feature: slab-scavenger
status: stable
keywords: [allocator, slab, arena, scavenger, decommit, memory, runtime, rss]
category: system
---

# The scavenger — giving a quiet heap's pages back to the OS

## Documentation

`__Builtins.scavengeMemory()` returns the number of **bytes handed back to the operating system** by
this call. It is the only door onto the allocator's reclamation path, and the number is the only thing
a Maxon program can see of it — `slab-allocator.md` explains why a program can never name a span, a
class or a chunk.

Everything below `__slab_free` recycles WITHIN the process: a slot goes back to its span's free list,
and a span whose every slot is free parks on its class's mcentral list for the next refill. That is
where reclamation stopped before this rung, and the consequence was that **a program's resident set
was its high-water mark, for the life of the process.**

The scavenger is what closes it, in three steps that happen in one call:

| Step | What moves |
|---|---|
| **1. grace** | every span parked on an mcentral list is marked `SEEN_FREE`. Nothing is released. |
| **2. release** | a span that was ALREADY `SEEN_FREE` — it stayed idle across two calls — is destroyed: its pages are unregistered from the reverse map, its chunk run goes back to the arena's bitmap, and its mspan header goes on the metadata free list. |
| **3. decommit** | every 64 KiB commit granule of every arena whose chunks are now ALL free has its physical backing dropped (`osDecommitPages`) and its commit bit cleared. |

⭐⭐ **THE TWO-EPOCH GRACE IS WHY STEP 1 AND STEP 2 ARE SEPARATE CALLS.** A span that empties and refills
between two scavenges never reaches step 2, so a workload that cycles a population does not pay a
release, a memzero and a syscall per cycle. **The first call after a population is dropped therefore
returns exactly 0** — that is the grace working, not a failure — and the second returns the bytes.

⭐ **THE DECOMMIT IS AT THE ARENA'S GRANULE, NOT AT THE SPAN.** A span's chunk run is 8 KiB-granular and
`madvise` REFUSES an address that is not page-aligned — on arm64-macOS a page is **16 KiB**, twice a
chunk — so a per-span decommit is a no-op or a corruption there, not a small win. The commit granule
(64 KiB) is a whole multiple of every page size shv2's lanes have, which is what makes one
target-neutral range legal on all of them.

⚠ **ON wasm32-wasi THE DECOMMIT IS INERT AND THE REST IS NOT.** Linear memory only grows, so
`osDecommitPages` emits no instruction there — but steps 1 and 2 run exactly as they do everywhere
else, and returning a chunk run to the arena is what lets a different size class reuse those bytes
instead of growing linear memory again. The byte count this function answers is *bytes the allocator
handed back*; what differs between lanes is whether the resident set follows.

⚠ **THE NUMBER IS NOT AN RSS READING AND CANNOT BE.** The allocator can only report what it told the OS
to drop; whether the pages left the resident set is `scripts/peak-rss.sh`'s question and no program's.
That is why the cases below assert `== 0` / `> 0` and never a byte figure: a granule count depends on
the arena geometry, and the arena is 64 MiB where a reservation is free and 512 KiB where it is not.

### What makes a case here DISCRIMINATING rather than regression-only

`slab-allocator.md` says of its own cases that every one of them would also pass against the bump
allocator they replaced. **That is not true of the cases below**, and it is worth saying which way each
one cuts, because a spec that would pass against the previous allocator proves nothing about this rung:

- **DISCRIMINATING, WITH A MEASURED BREAK** — `two-passes-before-a-byte-goes-back`,
  `released-chunks-come-back-zeroed`, `a-released-chunk-serves-a-different-class`,
  `an-idle-heap-releases-nothing`. Each one goes RED, with its own exit code, against a scavenger with one
  piece broken; the table below records which break produced which code.
- **REGRESSION-ONLY** — `an-os-direct-mapping-is-not-a-span`, which states a boundary the scavenger must
  not cross rather than a behaviour it must have; and — **MEASURED, against the reading this file first
  shipped** — `a-live-population-survives-two-scavenges`.

⛔ **THAT SECOND RECLASSIFICATION IS A MEASUREMENT, NOT A HEDGE, AND IT IS WORTH THE PARAGRAPH.** The
liveness case was written to catch a scavenger that reaches a span still holding live slots. The break that
would cause that is `__slab_free` parking a span that is NOT fully free — and with exactly that break in
place, **all six cases stayed GREEN**. The reason is the allocator's own shape: `__slab_refill` takes the
HEAD of an mcentral list, and a wrongly-parked live span is the head, so the very next allocation pulls it
straight back and `install` resets its grace state to `ACTIVE`. A scavenge never sees it. ⇒ the property
"only fully-free spans reach the scavenger" is enforced by `__slab_free`'s test and by nothing this file
can observe, and saying the case catches it would have been a claim with a measurement against it.

⚠ **All six fail against a compiler with no scavenger at all** — `E3004`, the builtin does not exist —
which is the RED this file was written against before a line of the mechanism was in the tree.

### ⚠ Those claims are SABOTAGE-MEASURED, not asserted

**A gate that cannot fail is not evidence**, and `slab-allocator.md` beside this file says the same of its
own cases. Each row below is ONE token changed on an otherwise pristine tree, rebuilt, with all six cases
run and the sources restored and re-hashed against their pristine copies between rows. **x64-windows.**

| The break | What went RED |
|---|---|
| `__slab_arena_free_chunks` fills the released run with `0x3F` instead of 0 — **INV-4 gone** | `released-chunks-come-back-zeroed` **139 (segfault)**, `a-released-chunk-serves-a-different-class` **101 (the leak gate)** |
| the grace test inverted, so a span is destroyed the FIRST time it is seen idle | `two-passes-…` **1 = `graceSkipped`**; both reuse cases 1, having nothing left to reuse |
| `__slab_arena_free_chunks` RE-CLAIMS the run instead of releasing it | `two-passes-…` **2 = `nothingReleased`**; both reuse cases 1 |
| the decommit leaves the granule's COMMIT bit SET, so a later claim never re-backs it | both reuse cases **139** — the access violation the commit bitmap exists to prevent |
| the "is every chunk of this granule free?" test inverted, so a granule is decommitted while it is IN USE | **all six 139** — the first one starts by decommitting the granule holding the arena's own bitmap |
| `__slab_free` parks a span that is NOT fully free | **nothing** — see the reclassification above; this is the row that changed what this file claims |

⭐ **The breaks are told apart by WHICH case goes red and WITH WHAT CODE**, which is the property that makes
them a net rather than one alarm: a single red case would say the scavenger is broken, and these say *where*.

⭐⭐ **AND THE GEOMETRY BOUND IS SABOTAGE-VERIFIED TOO, at COMPILE time rather than run time — which is the
whole point of it.** `checkSlabRuntimeGeometry` asserts that the two chunk runs this allocator asks for —
its state region and the widest class's span — fit an arena on BOTH lanes. Raising `SlabMaxShards` from
256 to 1024 takes the state region to 69 chunks against the non-reserving arena's 63, and the compiler
REFUSES every heap program with:

```
panic at SlabRuntime.maxon:400: slab runtime: the allocator's state region needs 69 chunk(s) and an
arena on the non-reserving lane has 63 after its metadata, so `__slab_arena_alloc_chunks` would abort at
run time
```

Before S6 that change compiled clean and aborted **at run time, on wasm only**, with
`slabChunkRunUnsatisfiable` — a true statement about the arena and a baffling one about the edit that
caused it.

## Tests

<!-- test: slab-scavenger.two-passes-before-a-byte-goes-back -->
**THE GRACE CASE.** 16,000 buffers are allocated and then dropped whole, so every span of their class
empties and parks on mcentral. The FIRST scavenge must return exactly 0 — it only opens the grace
period — and the SECOND must return a positive count, because those spans stayed idle across both.

⭐ It is the case that separates a scavenger from an eager free: an implementation that decommits the
instant a span empties returns a non-zero count from the first call, and one that never releases
returns 0 from the second.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(96)
	for i in 0 upto 96 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto 16000 'alloc'
		bufs.push(build(k as Seed))
	end 'alloc'
	bufs = Bufs.create()

	let first = __Builtins.scavengeMemory()
	if first != 0 'graceSkipped'
		return 1
	end 'graceSkipped'

	let second = __Builtins.scavengeMemory()
	if second <= 0 'nothingReleased'
		return 2
	end 'nothingReleased'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-scavenger.an-idle-heap-releases-nothing -->
**THE NOTHING-TO-DO CASE.** A program that has allocated nothing but the allocator's own state must
hand back nothing, twice. The state region, the arena metadata and the reverse-map tables are all
CLAIMED chunks, so no granule holding one of them may ever be decommitted — and a scavenger that
tested the commit bit without also testing that every chunk in the granule is free would decommit the
granule holding its own bookkeeping and fault on the next allocation.
```maxon
function main() returns ExitCode
	let first = __Builtins.scavengeMemory()
	if first != 0 'releasedSomething'
		return 1
	end 'releasedSomething'

	let second = __Builtins.scavengeMemory()
	if second != 0 'releasedSomethingLater'
		return 2
	end 'releasedSomethingLater'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-scavenger.a-live-population-survives-two-scavenges -->
**THE LIVENESS CASE.** 16,000 buffers are allocated, each holding a pattern derived from its own index,
and are STILL LIVE across two scavenge passes. Every byte is verified afterwards.

⭐ A scavenger that released a span still holding live slots would hand its chunk run back to the arena
— which memzeroes a released run — so every buffer in it would read back as zero. The verification is
what makes that visible; the byte count is not, because a wrong release raises it rather than lowering
it.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(96)
	for i in 0 upto 96 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto 16000 'alloc'
		bufs.push(build(k as Seed))
	end 'alloc'

	_ = __Builtins.scavengeMemory()
	_ = __Builtins.scavengeMemory()

	var bad = 0
	for k in 0 upto 16000 'check'
		let b = try bufs.get(k) otherwise return 1
		if b.count() != 96 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 96 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((k + i) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'

	if bad != 0 'corrupted'
		return 3
	end 'corrupted'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-scavenger.released-chunks-come-back-zeroed -->
**THE ZEROING CASE — the one this rung is most likely to break, and it breaks silently.** A population is
allocated, dropped and scavenged twice, so its chunks go back to the arena's bitmap and its granules
are decommitted. A SECOND population of the SAME size class is then allocated out of those very chunks
— the arena's scan is first-fit, so the freed low chunks are reused ahead of any virgin high one — and
every byte of it is verified.

⭐ A recycled chunk is not fresh OS memory. A span cut from one whose bytes were not zeroed starts with
a virgin bump region full of the previous occupants: their poisoned payloads and, in the box header,
their REFCOUNTS. `__mm_alloc` does not write the refcount — 0 is the born state — so a stale one is a
box that is freed at the wrong time. The live-count check at the end is what catches it: a population
that is fully dropped must return the allocator's live count to what it was before.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(96)
	for i in 0 upto 96 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var first = Bufs.create()
	for k in 0 upto 16000 'allocFirst'
		first.push(build(k as Seed))
	end 'allocFirst'
	first = Bufs.create()

	_ = __Builtins.scavengeMemory()
	let released = __Builtins.scavengeMemory()
	if released <= 0 'nothingToReuse'
		return 1
	end 'nothingToReuse'

	// The second population's HOLDER is created before the reading, so the two live counts describe the same
	// set of bindings and the only difference between them is the population itself.
	var second = Bufs.create()
	let liveBefore = __Builtins.mmAllocLive()

	for k in 0 upto 16000 'allocSecond'
		second.push(build((k + 7) as Seed))
	end 'allocSecond'

	var bad = 0
	for k in 0 upto 16000 'check'
		let b = try second.get(k) otherwise return 2
		for i in 0 upto 96 'bytes'
			let got = try b.get(i) otherwise return 3
			if got != ((k + 7 + i) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'
	if bad != 0 'corrupted'
		return 4
	end 'corrupted'

	second = Bufs.create()
	if __Builtins.mmAllocLive() != liveBefore 'refcountsDrifted'
		return 5
	end 'refcountsDrifted'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-scavenger.a-released-chunk-serves-a-different-class -->
**THE CROSS-CLASS CASE — the thing a per-span decommit can never do.** A population of one size class is
dropped and scavenged twice; a population of a DIFFERENT, larger size class is then allocated. It can
only be served out of the first population's chunks, because those are the low free ones the arena's
first-fit scan reaches first.

⭐ It is what separates returning a span's CHUNKS to the page layer from merely dropping its pages'
backing: an allocator that keeps the chunk run parked on its class's mcentral list holds those bytes
for that one class forever, and this population would have to grow the arena instead.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed, n Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto n 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var small = Bufs.create()
	for k in 0 upto 16000 'allocSmall'
		small.push(build(k as Seed, n: 96))
	end 'allocSmall'
	small = Bufs.create()

	_ = __Builtins.scavengeMemory()
	if __Builtins.scavengeMemory() <= 0 'nothingReleased'
		return 1
	end 'nothingReleased'

	var large = Bufs.create()
	for k in 0 upto 3000 'allocLarge'
		large.push(build(k as Seed, n: 500))
	end 'allocLarge'

	var bad = 0
	for k in 0 upto 3000 'check'
		let b = try large.get(k) otherwise return 2
		if b.count() != 500 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 500 'bytes'
			let got = try b.get(i) otherwise return 3
			if got != ((k + i) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'

	if bad != 0 'corrupted'
		return 4
	end 'corrupted'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-scavenger.an-os-direct-mapping-is-not-a-span -->
**THE BOUNDARY CASE — REGRESSION-ONLY.** A 300 KB buffer is past `SlabMaxSmallSize`, so it is its own
mapping and is registered in NO arena and NO reverse-map slot: a map MISS *is* the OS-direct sentinel.
The scavenger walks mcentral lists and arena bitmaps, so it must never see this mapping at all — while
it is live, and after it is freed, when its pages have gone back to the OS whole rather than to a
chunk bitmap.

This case states a boundary the scavenger must not cross rather than a behaviour it must have, so it
would also pass against an allocator that never scavenged. It is here because the routing it pins —
map hit means span, map miss means OS-direct — is exactly what a scavenger that registered its
released chunks would destroy.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var big = ByteArray.create()
	big.reserve(300000)
	for i in 0 upto 512 'fill'
		big.push((i mod 251) as Byte)
	end 'fill'

	_ = __Builtins.scavengeMemory()
	_ = __Builtins.scavengeMemory()

	var bad = 0
	for i in 0 upto 512 'check'
		let got = try big.get(i) otherwise return 1
		if got != (i mod 251) 'value'
			bad = bad + 1
		end 'value'
	end 'check'
	if bad != 0 'corrupted'
		return 2
	end 'corrupted'

	big = ByteArray.create()
	_ = __Builtins.scavengeMemory()
	_ = __Builtins.scavengeMemory()

	return 0
end 'main'
```
```exitcode
0
```
