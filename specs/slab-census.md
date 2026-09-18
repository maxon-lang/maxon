---
feature: slab-census
status: stable
keywords: [allocator, slab, census, residency, live, committed, memory, runtime]
category: system
---

# The census — what the heap is HOLDING, by the state each byte is in

## Documentation

Five builtins report the allocator's **levels**. Every other memory figure this language exposes is a
CUMULATIVE counter — `__Builtins.mmAllocBytes()` and its family say what a program has ASKED FOR since
it started, and they move exactly as far for a program that allocates a gigabyte and frees it as for one
that allocates a gigabyte and keeps it. None of them can answer *how much is out right now*, and an
operating system's peak-memory figure answers that and attributes none of it.

| Builtin | What it counts |
|---|---|
| `__Builtins.slabLiveBytes()` | slots currently handed out |
| `__Builtins.slabFreeBytes()` | slots sitting on some span's free list |
| `__Builtins.slabCachedFreeBytes()` | the SUBSET of those a processor's mcache can still reach |
| `__Builtins.slabParkedBytes()` | whole spans parked on an mcentral list, holding no live slot |
| `__Builtins.slabCommittedBytes()` | arena granules backed by physical memory, plus live OS-direct mappings |

⭐⭐ **`live + free` IS EVERY SLOT OF EVERY LIVE SPAN, WHICH IS WHAT MAKES THE PAIR CHECKABLE RATHER THAN
MERELY PLAUSIBLE.** Nothing but a scavenge destroys a span, so dropping a population moves bytes from
`live` to `free` and changes their sum by nothing at all. A census that has started skipping spans fails
that identity; a census that double-counts them fails it the other way.

⭐⭐ **`free - cachedFree` IS THE NUMBER THE OTHER FOUR EXIST TO EXPOSE.** A span that runs out of slots
is dropped from the mcache, and it is taken back only when it becomes ENTIRELY free — so one surviving
object in a span makes every other slot in it unreachable by any processor, however many of them are
free. That quantity is invisible to every cumulative counter and to the OS's peak alike.

⚠ **`slabLiveBytes` COUNTS A SLOT SITTING ON A REMOTE-FREE QUEUE.** A free performed on a machine that
does not own the slot's span is queued for the owner, and `free_count` does not credit it until the
owner drains the queue. That is the honest reading rather than a defect in the census: such a slot is
unavailable to every processor, exactly as a live one is. `slab-sharding.md` owns the road.

⚠ **THESE ARE BYTES OF SLOT, NOT BYTES REQUESTED.** A 40-byte request occupies a 48-byte slot, so `live`
exceeds what a program asked for by the size-class rounding. `slab-allocator.md` owns the ladder.

⛔ **THE WALK IS COLD AND THE HOT PATHS PAY NOTHING FOR IT.** Nothing is stepped at an allocation or a
free; each figure is derived when it is asked for, under `__slab_lock`, from state the allocator was
keeping anyway. The cost is proportional to the HEAP rather than to the call, which is why the
compiler's own residency table is sampled only under `--metrics` or `--log=compiler:debug`.

⚠ **NO CASE BELOW ASSERTS A BYTE FIGURE**, for `slab-scavenger.md`'s reason: a size class's slot size,
an arena's granule count and the allocator's own state region are geometry, and a case pinned to one of
them would be re-minted by every ladder regeneration. The cases assert RELATIONS between the five —
which is what they are for.

### ⚠ These cases are SABOTAGE-MEASURED, not asserted

**A gate that cannot fail is not evidence**, and `slab-scavenger.md` beside this file says the same of its
own cases. Each row is ONE token changed on an otherwise pristine tree, rebuilt, with every case run and
the source restored between rows. **x64-windows.**

| The break | What went RED |
|---|---|
| `__slab_meta_free` does not stamp `MspanOwningPFreed`, so a destroyed header is read as a live span | `a-scavenge-lowers-committed-and-leaves-live-alone` **2 = `parkedSpansSurvived`** — a dead header keeps the parked owner its span had, for ever |
| the metadata chunk walk never follows the link, so only the chunk being filled is seen | `the-walk-reaches-every-metadata-chunk` **1 = `theWalkMissedSpans`** — and **nothing else**, which is why that case exists |

⛔ **THE SECOND ROW IS WHY THERE IS A MAGNITUDE CASE AT ALL.** Five relations between the figures all
survive a census that reports a fraction of the heap, because the fraction is consistent. Only a bound
the PROGRAM can assert catches it.

## Tests

<!-- test: slab-census.dropping-a-population-moves-bytes-from-live-to-free -->
**THE IDENTITY CASE.** A population is built, measured, dropped and measured again. Dropping it must
LOWER `live`, RAISE `free`, and leave `live + free` exactly where it was — nothing but a scavenge
destroys a span, so no slot can leave the accounting in between.

⭐ It is the case that separates a census from a guess: a walk that skips a metadata chunk, or counts a
destroyed header as a live span, breaks the sum in one direction or the other.
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

	let liveFull = __Builtins.slabLiveBytes()
	let freeFull = __Builtins.slabFreeBytes()

	bufs = Bufs.create()

	let liveEmpty = __Builtins.slabLiveBytes()
	let freeEmpty = __Builtins.slabFreeBytes()

	if liveFull <= 0 'nothingLive'
		return 1
	end 'nothingLive'

	if liveEmpty >= liveFull 'dropDidNotLowerLive'
		return 2
	end 'dropDidNotLowerLive'

	if freeEmpty <= freeFull 'dropDidNotRaiseFree'
		return 3
	end 'dropDidNotRaiseFree'

	if (liveEmpty + freeEmpty) != (liveFull + freeFull) 'slotsLeftTheAccounting'
		return 4
	end 'slotsLeftTheAccounting'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.the-unreachable-free-set-is-what-a-dropped-population-becomes -->
**THE SUBSET CASE, AND THE ONE THAT NAMES THE DEFECT.** `cachedFree` and `parked` are each a subset of
`free`, so neither may exceed it. And after a population of one class is dropped whole, the free bytes
must vastly exceed what any mcache row still points at — a processor caches AT MOST ONE SPAN PER CLASS,
so everything else those spans hold is beyond reach until the span becomes entirely free.
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

	let free = __Builtins.slabFreeBytes()
	let cached = __Builtins.slabCachedFreeBytes()
	let parked = __Builtins.slabParkedBytes()

	if cached > free 'cachedIsNotASubset'
		return 1
	end 'cachedIsNotASubset'

	if parked > free 'parkedIsNotASubset'
		return 2
	end 'parkedIsNotASubset'

	if free <= cached 'everythingFreeWasReachable'
		return 3
	end 'everythingFreeWasReachable'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.committed-covers-every-slot-the-allocator-holds -->
**THE CONTAINMENT CASE.** Every slot the census reports lives in a chunk the arena handed out, and every
such chunk lies in a granule that is backed — so `committed` can never be below `live + free`. A
committed figure that fell short of the slots it must contain would be a commit bitmap that had lost
track of a granule, which is an access violation waiting for the next claim.
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

	if __Builtins.slabCommittedBytes() <= 0 'nothingCommittedAtStart'
		return 1
	end 'nothingCommittedAtStart'

	for k in 0 upto 8000 'alloc'
		bufs.push(build(k as Seed))

		if __Builtins.slabCommittedBytes() < (__Builtins.slabLiveBytes() + __Builtins.slabFreeBytes()) 'slotsOutsideTheCommittedSet'
			return 2
		end 'slotsOutsideTheCommittedSet'
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.an-os-direct-mapping-is-committed-and-is-not-a-slot -->
**THE OTHER ROAD.** A request above the largest size class is its own mapping rather than a slot in a
span, so no arena bitmap describes it and no span holds it. It must still raise `committed` — by at
least its own size — and must move neither `live` nor `free`. Freeing it must give the bytes back.

⭐ Without the census's own OS-direct word, a program whose whole working set is large buffers reports
`committed` as though it had allocated nothing.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

let Big = 400000

function main() returns ExitCode
	let committedBefore = __Builtins.slabCommittedBytes()
	let liveBefore = __Builtins.slabLiveBytes()

	var buf = ByteArray.create()
	buf.reserve(Big)
	for i in 0 upto Big 'fill'
		buf.push((i mod 251) as Byte)
	end 'fill'

	let committedHolding = __Builtins.slabCommittedBytes()

	if (committedHolding - committedBefore) < Big 'mappingNotCounted'
		return 1
	end 'mappingNotCounted'

	if (__Builtins.slabLiveBytes() - liveBefore) >= Big 'mappingCountedAsASlot'
		return 2
	end 'mappingCountedAsASlot'

	buf = ByteArray.create()

	if __Builtins.slabCommittedBytes() >= committedHolding 'mappingNotGivenBack'
		return 3
	end 'mappingNotGivenBack'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.a-scavenge-lowers-committed-and-leaves-live-alone -->
**THE RECLAMATION CASE, READ THROUGH THE CENSUS.** `slab-scavenger.md` asserts the byte count the
scavenger REPORTS; this asserts what the heap looks like afterwards. Two passes over a dropped
population must lower `committed` and `parked` and must not touch `live` — a scavenger that reached a
span still holding live slots would move the one number it may never move.
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
	var kept = Bufs.create()
	for k in 0 upto 200 'keep'
		kept.push(build(k as Seed))
	end 'keep'

	var bufs = Bufs.create()
	for k in 0 upto 16000 'alloc'
		bufs.push(build(k as Seed))
	end 'alloc'
	bufs = Bufs.create()

	let committedBefore = __Builtins.slabCommittedBytes()
	let parkedBefore = __Builtins.slabParkedBytes()
	let liveBefore = __Builtins.slabLiveBytes()

	_ = __Builtins.scavengeMemory()
	_ = __Builtins.scavengeMemory()

	if __Builtins.slabCommittedBytes() >= committedBefore 'nothingDecommitted'
		return 1
	end 'nothingDecommitted'

	if __Builtins.slabParkedBytes() >= parkedBefore 'parkedSpansSurvived'
		return 2
	end 'parkedSpansSurvived'

	if __Builtins.slabLiveBytes() != liveBefore 'theLiveSetMoved'
		return 3
	end 'theLiveSetMoved'

	if kept.count() != 200 'populationLost'
		return 4
	end 'populationLost'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.the-walk-reaches-every-metadata-chunk -->
**THE MAGNITUDE CASE, AND IT IS THE ONLY ONE THAT IS NOT A RELATION.** Every other case here compares
the five figures with each other, and a census that walks only the NEWEST metadata chunk keeps every
one of those relations intact while under-reporting the whole heap several-fold. **MEASURED: with the
chunk link dropped, the other five cases stayed GREEN.**

⭐ The bound is derived from the PROGRAM rather than from the allocator's geometry — 60,000 buffers of
96 bytes are at least 5,760,000 bytes of slot whatever ladder the classes are cut from, because a slot
is never smaller than the request it serves. A walk that reaches one chunk's worth of span headers
cannot reach it.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

let Population = 60000
let PayloadBytes = 96

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(PayloadBytes)
	for i in 0 upto PayloadBytes 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function main() returns ExitCode
	var bufs = Bufs.create()
	for k in 0 upto Population 'alloc'
		bufs.push(build((k mod 65536) as Seed))
	end 'alloc'

	let live = __Builtins.slabLiveBytes()

	if live < (Population * PayloadBytes) 'theWalkMissedSpans'
		return 1
	end 'theWalkMissedSpans'

	if bufs.count() != Population 'populationLost'
		return 2
	end 'populationLost'

	return 0
end 'main'
```
```exitcode
0
```
