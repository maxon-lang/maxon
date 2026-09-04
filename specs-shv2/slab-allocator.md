---
feature: slab-allocator
status: stable
keywords: [allocator, slab, arena, size-class, span, free-list, memory, runtime]
category: system
---

# The slab allocator — size classes, spans, and the OS-direct tier

## Documentation

`__slab_alloc` is the layer every heap byte in a Maxon program comes from: `__mm_alloc` asks it for
each managed box WHOLE (header + payload, one request), and the green-thread scheduler, the subprocess
scratch buffers and the DebugStream ring call it directly. Since S2 it is a **size-classed span
allocator** over the chunk arena (`Compiler/Runtime/SlabRuntime.maxon` on
`Compiler/Runtime/SlabArena.maxon`), not the bump cursor it used to be.

⭐⭐ **SINCE S4 IT IS THE ONLY ALLOCATOR.** Between S2 and S4 `__mm_alloc` carried a second one —
16-byte-granular free lists of its own — which served a repeat box out of its own buckets and asked
`__slab_alloc` only on a miss. It was a stopgap from the days when the layer beneath it reclaimed
nothing. It is gone, and the consequence for THIS file is that every case below now drives the span
free list, the recycled-slot zeroing and (past 32 KiB) the OS-direct unmap, where before S4 most of
them stopped at a bucket.

The pieces, and what each of the cases below can actually see of them:

| Layer | What it does |
|---|---|
| **arena** | 8 KiB chunks out of a reservation committed 64 KiB at a time, tracked by a bitmap, first-fit |
| **reverse map** | a three-level radix map from a page to the span that owns it; a MISS means OS-direct |
| **size classes** | Go's 68-class ladder, reached in O(1) through two derived reverse tables |
| **spans** | one chunk run cut into equal slots, with a free list AND a virgin bump region |
| **mcache / mcentral** | one cached span per (shard, class); a fully-emptied span parks for reuse |
| **OS-direct** | above 32 KiB a request is its own mapping, its length in a 16-byte prefix |

### ⭐⭐ Why these cases are STRESS programs and not assertions about the allocator

**Nothing in the language can name a span, a class or a chunk.** A Maxon program sees only the
addresses it is handed and the bytes it reads back out of them, so every case here works the same
way: allocate a population whose members would COLLIDE if the allocator got a size, a class or a
routing decision wrong, write a distinct pattern into each, and read every byte back.

That is not a weaker test than an assertion would be — it is the test the allocator actually owes.
A class lookup one index low hands out a slot too small for the request, so the tail of one object
lands in the next one; a free that pushes the wrong pointer hands one slot to two objects; a bump
cursor that runs past its span's end writes into whatever the arena put in the next chunk. **Every
one of those is a WRONG ANSWER in a program that verifies what it wrote**, and none of them is
visible any other way.

### ⚠⚠ GREEN HERE IS A CORRECTNESS CLAIM, NOT A LIVENESS ONE — and that has to be said out loud

**Every case in this file would also pass against the BUMP ALLOCATOR these layers replaced.** A
cursor that never reuses a byte cannot hand one slot to two objects, so it satisfies the whole file
trivially. ⇒ **A green run of this file does not prove the slab is what served the program.**

What these cases are is a REGRESSION net: they are the thing that goes red when the class lookup, the
span geometry, the bump bound or the free routing is wrong, and before this file existed ~890 lines
of arena and object-layer code had no committed test at all. Two other instruments carry the half
this one cannot:

- **the emitted-bytes delta** — the allocator is in the image, and a layer nothing calls is dropped
  by `DeadFunctionElimination` and shows up as a byte-identical binary;
- **SABOTAGE** — breaking one index in the class lookup, or the bump cursor's bound, or the free
  routing, must turn cases here RED. A gate that cannot fail is not evidence, and for this file the
  demonstration that it CAN is a deliberate act rather than a property of the corpus.

### What these cases CANNOT observe, stated rather than skipped

Four of the layer's behaviours have no Maxon-visible consequence today, and saying so is the point:

- ~~The arena's chunk FREE path (`__slab_arena_free_chunks`).~~ **OBSERVABLE SINCE S6, AND IT HAS ITS
  OWN FILE.** This entry said the function had no reachable path and that its first caller would be
  the scavenger, which was true and is not any more: `__slab_scavenge` calls it for every span that
  stays idle across two passes, and `__Builtins.scavengeMemory()` returns the bytes that produced.
  The cases that drive it — including the one that proves a recycled chunk is handed back ZEROED,
  which is what the release path now owes — are in `slab-scavenger.md`. Nothing in THIS file calls
  it, so every case below still describes an allocator that only ever grows.
- **INV-1's trap** (`RuntimeAbort.slabSpanExhaustedPastItsEnd`). It fires only when the span's three
  accounts of its own free slots disagree, which no legal sequence of allocations can produce. It is
  verified by SABOTAGE — breaking the bump cursor's bound turns these cases red — not by a case.
- ~~The span FREE list's recycling.~~ **OBSERVABLE — AND THE HISTORY IS WORTH KEEPING, BECAUSE ONLY
  MEASURING IT EVER SAID SO.** While `__mm_free` still had buckets of its own (S2–S4) the obvious
  reading was that a box always went to a bucket and never back to its span. It was wrong even then:
  a box the buckets REFUSED went to `__slab_free`, and they refused two kinds — one past 256 KiB (an
  OS-direct mapping) and one with a **ZERO PAYLOAD**, which is what an EMPTY element buffer is.
  ⭐ **MEASURED at S2: with every `__slab_free` routed to the OS-direct arm, 12 corpus cases abort on
  the magic word** — `arrays/slice-of-empty-array-then-push`,
  `byte-string-literal.empty-literal-detaches`, `managed-memory-methods/empty-bstring-push`,
  `string-type-2/string-append-empty` and eight more, every one of them a program that builds an array
  or a String from empty. **S4 removed the buckets, so what was true of two shapes is now true of
  every box in the language**: every free reaches `__slab_free` and every reuse comes off a span's
  free list or its bump region.
- **The size a request is ROUNDED to.** `__slab_rounded_size` reports it, and nothing in shv2 asks:
  v1's caller is an inline-small-array capacity reclaim that shv2's array records have no equivalent
  of. The rounding is still what these cases depend on — a class too small corrupts a neighbour —
  they just cannot read the number.

### The one thing a case here must never do

**Allocate a population that fits in one span of one class.** Then the class lookup is never
exercised past one entry, a refill never happens, and the case passes against an allocator that
knows one size. Every case below either sweeps a range of sizes or allocates past a span's capacity,
and says which.

## Tests

<!-- test: slab-allocator.every-size-class-keeps-its-own-bytes -->
**THE CLASS-LOOKUP CASE.** 260 buffers whose capacities sweep every byte length from 1 to 260, each
holding a pattern derived from its own length, all live at once and all verified afterwards. The
sweep is dense on purpose: consecutive lengths land in the same class for a while and then step to
the next one, so it crosses roughly twenty class boundaries — and a lookup that answers one index
LOW hands a buffer a slot too short, whose tail then lands in a neighbour and reads back wrong.

Buffers are verified only after ALL of them exist, which is what makes an overlap visible: a case
that checked each buffer immediately would read the right bytes back before the neighbour that
overwrites them had been allocated.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 4096)

function build(n Len) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto n 'fill'
		b.push(((i + n) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for n in 1 upto 261 'alloc'
		bufs.push(build(n as Len))
	end 'alloc'

	var bad = 0
	for n in 1 upto 261 'check'
		let b = try bufs.get(n - 1) otherwise return 1
		if b.count() != n 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto n 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((i + n) mod 251) 'value'
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

<!-- test: slab-allocator.a-span-fills-and-the-next-one-refills -->
**THE SPAN-EXHAUSTION CASE.** 3000 buffers of ONE small size, live at once. A span of the class that
serves them holds far fewer than 3000 slots, so the mcache entry drains, is evicted, and is refilled
from a freshly cut span — dozens of times, each refill taking a new chunk run out of the arena and
registering it in the reverse map.

It is the case a one-span allocator passes and a mis-sized one does not: if `objects_per_span` were
read too HIGH the bump cursor would run off the end of the span (INV-1's trap, or silent corruption
of the next chunk), and if the refill installed a span without resetting its cursor the second span
would hand out the first one's slots.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Index = int(0 to 65536)

function build(seed Index) returns ByteArray
	var b = ByteArray.create()
	b.reserve(24)
	for i in 0 upto 24 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto 3000 'alloc'
		bufs.push(build(k as Index))
	end 'alloc'

	var bad = 0
	for k in 0 upto 3000 'check'
		let b = try bufs.get(k) otherwise return 1
		for i in 0 upto 24 'bytes'
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

<!-- test: slab-allocator.an-os-direct-buffer-round-trips -->
**THE OS-DIRECT ROUND TRIP.** A 300 KB buffer is far past `SlabMaxSmallSize`, so `__slab_alloc` gives
it its own mapping; when the box dies `__mm_free` hands it to `__slab_free`, whose reverse-map lookup
MISSES — and a miss IS the OS-direct sentinel — so the mapping goes back to the OS whole, its length
read out of the 16-byte prefix the allocation stamped.

Sixty-four round trips, each writing and verifying its own pattern.

⚠ **IT FAILS TWO WAYS ON THIS LANE, NOT THE THREE THE OBVIOUS READING GIVES — MEASURED AT S4.** A prefix
the free does not recognise aborts on the magic word, and a mapping that is never released leaves 19 MB
committed where the live set is 300 KB (MEASURED post-cutover: **peak RSS 1.06 MB**, so the release both
runs and works). The third — *"a length recovered wrongly unmaps a region that is still in use"* — is NOT
a failure mode on **x64-windows**, because `osFreePages` lowers to `VirtualFree(addr, 0, MEM_RELEASE)`,
whose `dwSize` MUST be 0 and is ignored: the length word in the prefix is DEAD on this lane. (SABOTAGE:
reading it out of the MAGIC word's offset instead leaves this file at 10 passed / 0 failed.) It is live on
a `munmap` lane, and inert again on wasm, where `osFreePages` emits nothing at all.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Round = int(0 to 255)

function roundTrip(r Round) returns Integer
	var big = ByteArray.create()
	big.reserve(300000)
	for i in 0 upto 512 'fill'
		big.push(((r + i) mod 251) as Byte)
	end 'fill'

	var wrong = 0
	for i in 0 upto 512 'check'
		let got = try big.get(i) otherwise return 1
		if (got as Round) != ((r + i) mod 251) 'value'
			wrong = wrong + 1
		end 'value'
	end 'check'
	return wrong
end 'roundTrip'

function main() returns ExitCode
	var bad = 0
	for r in 0 upto 64 'rounds'
		bad = bad + roundTrip(r as Round)
	end 'rounds'

	if bad != 0 'corrupted'
		return 3
	end 'corrupted'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: slab-allocator.os-direct-mappings-coexist -->
**MANY OS-DIRECT MAPPINGS LIVE AT ONCE**, which is the half the round-trip case above cannot see: it
holds one at a time, so a free that released the wrong mapping would still leave the next allocation
working. Here 40 mappings of DIFFERENT sizes are live together, each stamped with its own pattern,
and every one is read back after all 40 exist.

⭐ It is also the case that would have caught v1's ceiling. Its OS-direct tier tracks live mappings
in a 512-entry array and ABORTS the program on the 513th; shv2 carries each length in the mapping's
own prefix, so there is no table to fill and no number of live mappings that is too many.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Index = int(0 to 255)

function build(k Index) returns ByteArray
	var b = ByteArray.create()
	b.reserve((280000 + k * 4096) as ElementIndex)
	for i in 0 upto 64 'fill'
		b.push(((k * 7 + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto 40 'alloc'
		bufs.push(build(k as Index))
	end 'alloc'

	var bad = 0
	for k in 0 upto 40 'check'
		let b = try bufs.get(k) otherwise return 1
		for i in 0 upto 64 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((k * 7 + i) mod 251) 'value'
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

<!-- test: slab-allocator.a-fresh-slot-reads-zero -->
**THE ZEROING CONTRACT.** The allocator hands back memory that is always zero, and the bump region is
what makes that free: a slot that has never been handed out still holds the zeroes the OS gave the
arena. This allocates a 4 KB buffer, exposes every byte of it without writing any, and requires all
4096 to read 0 — after a preceding round has filled a same-sized buffer with `0xFF` and dropped it,
so a slot that came back dirty from anywhere would show.

It is what breaks first if a future free path threads a link through a fresh span (the intrusive
free list this design deliberately does not build), because that writes a pointer into the first word
of every slot.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function dirty()
	let mm = try __ManagedMemory.create(4096, elementSize: 1) otherwise return
	try mm.setLength(4096) otherwise return
	var arr = ByteArray.init(mm)
	for i in 0 upto 4096 'fill'
		try arr.set(i, value: 255) otherwise return
	end 'fill'
end 'dirty'

function countNonZero() returns Integer
	let mm = try __ManagedMemory.create(4096, elementSize: 1) otherwise return 1
	try mm.setLength(4096) otherwise return 2
	let arr = ByteArray.init(mm)
	var seen = 0
	for i in 0 upto 4096 'scan'
		let got = try arr.get(i) otherwise return 3
		if got != 0 'nonZero'
			seen = seen + 1
		end 'nonZero'
	end 'scan'
	return seen
end 'countNonZero'

function main() returns ExitCode
	for _ in 0 upto 8 'rounds'
		dirty()
	end 'rounds'

	if countNonZero() != 0 'dirtySlot'
		return 3
	end 'dirtySlot'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: slab-allocator.allocation-and-release-interleave -->
**ALLOCATE, RELEASE, RE-ALLOCATE — and the answers stay right.** Half the population is dropped and
rebuilt at a different size four times over, so slots and mappings are handed back and taken again
while the other half stays live and keeps its bytes. A pointer returned to the wrong free list, or a
slot handed out twice, corrupts a survivor rather than the object being reallocated — which is
exactly why the survivors are what this case reads.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 4096)

function build(n Len, seed Len) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto n 'fill'
		b.push(((i + seed) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function verify(b ByteArray, n Len, seed Len) returns Integer
	if b.count() != n 'length'
		return 1
	end 'length'
	var wrong = 0
	for i in 0 upto n 'bytes'
		let got = try b.get(i) otherwise return 1
		if (got as Len) != ((i + seed) mod 251) 'value'
			wrong = wrong + 1
		end 'value'
	end 'bytes'
	return wrong
end 'verify'

function main() returns ExitCode
	var keep = Bufs.create()
	for k in 0 upto 120 'survivors'
		keep.push(build((k + 8) as Len, seed: k as Len))
	end 'survivors'

	// Four waves of churn. Each wave builds and drops its own population, so the slots it takes come
	// from — and go back to — the same spans and buckets the survivors were cut from.
	for wave in 0 upto 4 'waves'
		var churn = Bufs.create()
		for k in 0 upto 200 'build'
			churn.push(build(((k mod 97) + wave * 13 + 1) as Len, seed: (k + wave) as Len))
		end 'build'
	end 'waves'

	var bad = 0
	for k in 0 upto 120 'check'
		let b = try keep.get(k) otherwise return 1
		bad = bad + verify(b, n: (k + 8) as Len, seed: k as Len)
	end 'check'

	if bad != 0 'corrupted'
		return 3
	end 'corrupted'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: slab-allocator.empty-buffers-go-back-to-their-span -->
**THE SPAN FREE LIST, AT THE SMALLEST BOX THERE IS.** An EMPTY element buffer is a box with a ZERO
payload — 24 bytes of header and nothing else — so it lands in the ladder's smallest class, and its
death pushes that slot back onto its span's free list. Two thousand of them are built and dropped
here, so one class's slots are pushed and popped thousands of times over, and a run of survivors built
the same way is verified afterwards.

⚠ It hammers the RECYCLED arm of `__slab_alloc`'s pop — the arm that must zero a dirty slot, where the
virgin arm gets its zeroes free. A pop that failed to unlink, or a push of the wrong pointer, hands one
slot to two buffers, and the survivors are what shows it. (Before S4 this was the ONE case here that
reached that arm at all, because `__mm_free`'s own buckets served every other shape. Now every case
does, and this one keeps its place as the extreme: the shortest slot, recycled the most times.)

⛔ **THE SHAPE IS `b""` AND A GROW, AND THE OBVIOUS SPELLING DOES NOT WORK.** `ByteArray.create()`
followed by a push allocates nothing to free — an array that never had a buffer owes nothing — so a
case written that way passes against a `__slab_free` whose span arm has been sabotaged away, which is
to say it tested nothing here. MEASURED at S2, both spellings, against that sabotage: the `create()`
version PASSED and this one aborted. The zero-payload box is the buffer an EMPTY LITERAL detaches
into, freed when the first push reallocates it — which is exactly what the twelve corpus cases named
above do.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 256)

function emptyThenGrow(n Len) returns ByteArray
	var b = b""
	for i in 0 upto n 'fill'
		b.push(((i + n) mod 251) as Byte)
	end 'fill'
	return b
end 'emptyThenGrow'

function main() returns ExitCode
	var keep = Bufs.create()
	for k in 0 upto 40 'survivors'
		keep.push(emptyThenGrow((k + 1) as Len))
	end 'survivors'

	for _ in 0 upto 2000 'churn'
		var scratch = b""
		scratch.push(1)
	end 'churn'

	var bad = 0
	for k in 0 upto 40 'check'
		let b = try keep.get(k) otherwise return 1
		let n = k + 1
		if b.count() != n 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto n 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((i + n) mod 251) 'value'
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

<!-- test: slab-allocator.the-header-decides-which-side-of-the-os-direct-boundary-a-box-lands -->
**THE ROUTING BOUNDARY, AND THE 24 BYTES THAT DECIDE IT (S4).** `__slab_alloc` serves a request of at
most `SlabMaxSmallSize` (32,768) out of a span and gives anything larger its own mapping. Since S4
`__mm_alloc` asks for the WHOLE box, so the boundary in USER terms sits 24 bytes lower: a 32,744-byte
buffer is the largest a span can serve, and 32,745 is the first that needs a mapping of its own.

This sweeps 26 buffers across that line, four bytes apart, all live at once and every byte verified —
so the population contains both routes and each one's neighbours are on the other side. Then it drops
them all and rebuilds the identical sweep, which runs both FREE routes (a slot pushed back onto its
span, a mapping handed back to the OS) before the second round asks for the same sizes again.

⚠ **IT IS THE OFF-BY-ONE THE BOX LAYER INTRODUCED, AND NOTHING ELSE IN THIS FILE STRADDLES IT.** Before
S4 `__mm_alloc` never routed on this boundary at all — its own buckets ran to 256 KB, and the slab only
ever saw the boxes those buckets refused. A total computed WITHOUT the header sends a 32,750-byte
request down the span path, where the widest class is 32,768 bytes and the box needs 32,774: the tail
of that buffer lands in the following slot, which is its neighbour in this sweep. Verifying every byte
of every buffer only after all 26 exist is what makes that visible.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 65536)

let FirstLen = 32700
let LenStep = 4
let SweepCount = 26

function build(n Len) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto n 'fill'
		b.push(((i + n) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function sweep() returns Integer
	var bufs = Bufs.create()
	for k in 0 upto SweepCount 'alloc'
		bufs.push(build((FirstLen + k * LenStep) as Len))
	end 'alloc'

	var bad = 0
	for k in 0 upto SweepCount 'check'
		let n = FirstLen + k * LenStep
		let b = try bufs.get(k) otherwise return 1
		if b.count() != n 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto n 'bytes'
			let got = try b.get(i) otherwise return 1
			if got != ((i + n) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'
	return bad
end 'sweep'

function main() returns ExitCode
	if sweep() != 0 'firstRound'
		return 3
	end 'firstRound'

	// The first sweep's buffers are all dead by now, so this one re-asks for the same sizes with both
	// free routes already run — a span slot recycled, a mapping unmapped and re-mapped.
	if sweep() != 0 'secondRound'
		return 4
	end 'secondRound'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: slab-allocator.multi-page-spans-place-every-slot -->
**THE MULTI-PAGE SPAN CASE, WHICH IS ALSO THE REVERSE MAP'S INTERIOR-HIT CASE.** The big classes are
cut from runs of several 8 KiB chunks and hold only a handful of slots each, so most of their slots
start on a page that is not the span's first — and the reverse map has to name the owning span for
every one of those pages, not just for the base. A map that registered only the run's first page
would answer 0 for an interior slot, which reads as the OS-direct sentinel and sends a slab slot to
`osFreePages`.

The sizes sweep the top of the ladder (roughly 9 KB to 30 KB, still below the 32 KiB OS-direct
boundary), so between them they take spans of one, two, three, five, seven and ten chunks.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 65536)

function build(n Len, seed Len) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto 32 'fill'
		b.push(((i + seed) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto 96 'alloc'
		bufs.push(build((9000 + k * 220) as Len, seed: k as Len))
	end 'alloc'

	var bad = 0
	for k in 0 upto 96 'check'
		let b = try bufs.get(k) otherwise return 1
		for i in 0 upto 32 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((i + k) mod 251) 'value'
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

<!-- test: slab-allocator.the-arena-hands-out-hundreds-of-chunk-runs -->
**THE ARENA'S BITMAP UNDER LOAD — first-fit across many words, and runs that cross one.** Enough
distinct classes are exercised, and enough spans of each, that the arena hands out several hundred
chunk runs of one to ten chunks. A bitmap word covers 64 chunks, so a multi-chunk run lands astride a
word boundary long before the population is exhausted — the case where the allocate side's
per-bit word recomputation and the release side's have to agree about which bit belongs to which
chunk.

⚠ It exercises the arena's ALLOCATE side only. Nothing in this rung returns a chunk, so first-fit
REUSE has no producer to drive it — see *What these cases cannot observe* above.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Len = int(0 to 65536)

function build(n Len, seed Len) returns ByteArray
	var b = ByteArray.create()
	b.reserve(n as ElementIndex)
	for i in 0 upto 16 'fill'
		b.push(((i + seed) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	// Sixteen sizes spread across the ladder, forty spans' worth of each: the arena serves a run per
	// span, and the runs are of sixteen different lengths.
	for k in 0 upto 640 'alloc'
		let size = 1000 + (k mod 16) * 1800
		bufs.push(build(size as Len, seed: k as Len))
	end 'alloc'

	var bad = 0
	for k in 0 upto 640 'check'
		let b = try bufs.get(k) otherwise return 1
		for i in 0 upto 16 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((i + k) mod 251) 'value'
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
