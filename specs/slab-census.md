---
feature: slab-census
status: stable
keywords: [allocator, slab, census, residency, live, committed, memory, runtime, tag, attribution, by-tag, size-class]
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
| `__Builtins.slabParkedBytes()` | whole spans on a class's list holding no live slot — the backlog `scavengeMemory()` returns |
| `__Builtins.slabCommittedBytes()` | arena granules backed by physical memory, plus live OS-direct mappings |

⭐⭐ **THREE MORE SAY WHAT THE LIVE BYTES ARE, NOT MERELY HOW MANY.** A level answers *how much is out*
and attributes none of it. `__Builtins.slabCensusTally(mode)` walks every live slot once and fills a table
of 2048 buckets, read back one at a time by `__Builtins.slabCensusBucketCount(i)` and
`__Builtins.slabCensusBucketBytes(i)`.

| `mode` | Bucket `i` holds |
|---|---|
| `0` | the slots whose ALLOCATION TAG is `i` — a tag exists only in a `--debugstream` build, which is what prepends the `(alloc_id << 16 \| tag_index)` word below each box's header |
| `1` | the slots of SIZE CLASS `i` |

Bucket 2046 is `(unattributable)` — a slot whose first word is not a plausible packed id — and 2047 is
`(overflow)`, a tag past the end of the table. Neither is ranked with the rest; each is its own row.

⭐⭐ **THE HEADER-LESS POPULATION IS `__Builtins.mmRawAllocLive()`, AND THE CENSUS READS IT RATHER THAN
COUNTING IT AGAIN.** `builtins-mm-counters.md` owns that figure — the raw layer's live count less the
tracked layer's. It is what the tally's table cannot name: an element buffer, a string's bytes, a closure's
environment, each of which the tally attributes by whatever its first word happens to hold. The case below
keeps it honest for that use, which is the only reason a counter case lives in this file.

⭐⭐ **`live + free` IS EVERY SLOT OF EVERY LIVE SPAN, AND IT FALLS ONLY WHEN A SPAN DIES.** Nothing but
`scavengeMemory()` destroys a span, so in a program that never calls one, dropping a population moves bytes
from `live` to `free` and changes their sum by nothing at all. A scavenge takes the emptied spans out of
the accounting entirely and the sum falls. It can never RISE: a census that has started skipping spans
fails that in one direction, and one that double-counts them fails it in the other.

⭐⭐ **`free - cachedFree` IS THE NUMBER THE OTHER FOUR EXIST TO EXPOSE.** A span that runs out of slots is
dropped from the mcache and is reachable again only through its class's partial list, so one surviving
object in a span makes every other slot in it unreachable by the processor that was using it, however many
of them are free. That quantity is invisible to every cumulative counter and to the OS's peak alike.

⚠ **`slabParkedBytes` IS A LEVEL AND NOT A DEFECT CHANNEL.** A span with no live slot stays on its class's
list until somebody scavenges, because a refill hands one back to its own class rather than destroying it —
so this figure is the backlog a program could still reclaim, and it is bounded rather than growing without
limit.

⚠ **`slabFreeBytes` COUNTS A SLOT SITTING ON A SPAN'S REMOTE-FREE STACK.** A free performed on a machine
that does not hold the span cached is queued against the span, and `free_count` does not credit it until a
collector chains it onto the free list — so the census reads the queue's own count beside `free_count`.
Counting it live instead would report a span whose queue nobody has collected as fully in use.
`slab-sharding.md` owns the road.

⚠ **THESE ARE BYTES OF SLOT, NOT BYTES REQUESTED.** A 40-byte request occupies a 48-byte slot, so `live`
exceeds what a program asked for by the size-class rounding. `slab-allocator.md` owns the ladder.

⛔ **THE WALK IS COLD AND THE HOT PATHS PAY NOTHING FOR IT.** Nothing is stepped at an allocation or a
free; each figure is derived when it is asked for, under `__slab_lock`, from state the allocator was
keeping anyway. The cost is proportional to the HEAP rather than to the call, which is why the
compiler's own residency table is sampled only under `--metrics` or `--log=compiler:debug`.

⭐⭐ **`__Builtins.slabCensusTally(mode)` ANSWERS THE LIVE BYTES IT WALKED, WHICH IS AN INDEPENDENT
CROSS-CHECK OF THE TABLE IT FILLED.** The buckets are read back one at a time through
`slabCensusBucketBytes`, so their sum travels through the table; the walk's own return travels through
nothing. A tally whose bucket arithmetic is wrong moves the sum and leaves the return alone, and one whose
enumeration is wrong moves both — so the two figures beside `slabLiveBytes()` tell a table fault from a
walk fault. It is not a status word: every mode it accepts is checked before the walk starts, and a mode it
does not accept ends the process with `slabCensusTallyUnsound` rather than returning a code.

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
| the class tally credits a span's WHOLE slot count to the live histogram, free slots included | **four at once**: `the-tally-totals-the-live-heap` **2 = `theCensusDisagreesWithTheLevel`** and `the-tally-totals-the-live-heap-on-several-processors` **2** — the bucket total overshoots the level the same walk reports — and `an-untraced-build-tallies-by-size-class` **2 = `noBucketRoseByThePopulation`** and `dropping-the-population-returns-the-bucket` **2 = `noBucketFellByThePopulation`**, because a bucket now moves by its spans' whole slot count rather than by the population |
| the tag walk steps one slot PAST the bump cursor, so a slot that was never handed out is read as live | `a-tagged-population-moves-one-bucket-by-N` **119 = `slabCensusTallyUnsound`** — the tally's own live count no longer matches what the span's free counts say, and the walk ends the process rather than publishing a table it cannot stand behind |
| a size-class bucket is keyed by the span's FREE COUNT rather than by its class index | `an-untraced-build-tallies-by-size-class` **2 = `noBucketRoseByThePopulation`** and `dropping-the-population-returns-the-bucket` **2 = `noBucketFellByThePopulation`** — one class's slots scatter over as many buckets as its spans have free counts |
| a freed slot is left in the bucket the tally last found it in (the table is not cleared) | `dropping-the-population-returns-the-bucket` **2 = `noBucketFellByThePopulation`** — nothing falls when the population goes, and **nothing else**, which is why that case exists |
| `__Builtins.mmRawAllocLive()` answers the RAW layer's own live count rather than that count less the tracked layer's | `raw-live-slots-do-not-count-boxes` **2 = `theBoxesWereCountedAsHeaderLessSlots`** — every box is a slab request too, so the figure becomes the whole live table's slot count and a thousand boxes move it by a thousand — and **nothing else in this file**, which is why that case exists; the figure is shared, so `builtins-mm-counters` reports the same break in its own cases |
| the tally ADDS a span's free set to its live count instead of subtracting it (`-` → `+`) | **six at once**: `dropping-a-population-moves-bytes-from-live-to-free` **2**, `committed-covers-every-slot-the-allocator-holds` **2**, `a-scavenge-lowers-committed-and-leaves-live-alone` **3**, `an-untraced-build-tallies-by-size-class` **2**, `dropping-the-population-returns-the-bucket` **2**, and `a-tagged-population-moves-one-bucket-by-N` **119 = `slabCensusTallyUnsound`**, because the tag walk's own live count then contradicts the figure it is checked against. ⚠ **THE TWO `the-tally-totals-the-live-heap` CASES STAY GREEN HERE**, and the paragraph below the table is why: `liveSlots` is SHARED by the level and the tally, so both sides of their equality move together — what catches this break is a bound a program asserts, not a relation between two derivations of one number |

⭐⭐ **NO ONE-TOKEN CHANGE CAN MAKE THE TWO WALKS DISAGREE ONLY ON A MULTI-PROCESSOR HEAP, AND THAT IS WHAT
`the-tally-totals-the-live-heap-on-several-processors` EXISTS TO KEEP TRUE.** The tally and the level share
their enumeration (`slabWalkSpans`), their destroyed-header test, their geometry read (`slabSpanGeom`) and
their free set (`slabSpanFreeSlots`) — so a break in any of them moves BOTH figures and the equality
survives — which is what the table's FREE-SET-ADDED row measures, and why the two equality cases stay
green under it. A break in what the tally does with what those four hand it moves every tally case at once,
which is what the table's WHOLE-SLOT-COUNT row measures.

The case is a gate against the sharing being undone: a tally that re-spelled the free set as
`free_count` alone would agree with the level on every single-threaded heap in this file and disagree by
exactly the queued slots the moment another processor frees into a span it does not hold cached.

⛔⛔ **TWO PREDICTIONS WERE FALSIFIED HERE AND THE ROWS ABOVE ARE WHAT REPLACED THEM.** *"mode 0 reads a
box's first word without asking whether it is a plausible packed id"* was measured — the plausibility test
removed — and **every case stayed GREEN**: in a single-processor traced program every live slot IS a box, so
the guard fires on nothing the suite runs. Mis-extracting the tag was measured too (`word and 16` in place
of `word and 0xFFFF`) and **stayed GREEN as well**, because the case asserts only that SOME bucket rose by
exactly N and 64 widgets that all land in one wrong bucket satisfy it as well as 64 in the right one. ⇒
**`a-tagged-population-moves-one-bucket-by-N` GATES THE WALK, NOT THE ATTRIBUTION**: what it catches is a
tally that finds the wrong number of live slots, which is the row above it. What pins the attribution is
this case's ```mm-trace golden, which names every box the program allocated.

⛔ *"a size-class bucket is keyed by the REQUESTED size rather than the slot size"* is not a defect this
tier can have: the walk never sees a request, only a span's class index and the ladder's packed geometry.
The row above it is the nearest break that is expressible — a bucket key that is not the class.

⚠ **THE FIRST ROW'S SIGHTING SURVIVES THE OWNER STATES BEING RENAMED.** An unstamped destroyed header
reads `Partial` with every slot free, which is exactly what `parked` counts — so it is reported against the
same exit code the row names.

⛔ **THE SECOND ROW IS WHY THERE IS A MAGNITUDE CASE AT ALL.** Five relations between the figures all
survive a census that reports a fraction of the heap, because the fraction is consistent. Only a bound
the PROGRAM can assert catches it.

## Tests

<!-- test: slab-census.dropping-a-population-moves-bytes-from-live-to-free -->
**THE IDENTITY CASE.** A population is built, measured, dropped and measured again. Dropping it must
LOWER `live`, RAISE `free`, and may not RAISE `live + free` — a dropped slot moves from one column to the
other, and the only thing that can take it out of the accounting altogether is a span being destroyed,
which can only ever lower the sum.

⭐ It is the case that separates a census from a guess: a walk that skips a metadata chunk, or counts a
destroyed header as a live span, breaks the relation in one direction or the other.

⚠ **THE SUM IS BOUNDED RATHER THAN PINNED, AND THE SLACK IS ONE-SIDED ON PURPOSE.** The only thing that
takes slots out of the accounting is a span being destroyed, and only `scavengeMemory()` does that — which
this program never calls, so today the sum is level. Asserting the bound rather than the equality is what
keeps the case measuring the census instead of the reclamation road.
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

	if (liveEmpty + freeEmpty) > (liveFull + freeFull) 'slotsAppearedFromNowhere'
		return 4
	end 'slotsAppearedFromNowhere'

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
so everything else those spans hold is beyond reach until something refills the class or scavenges.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

let Population = 16000

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
	for k in 0 upto Population 'alloc'
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

<!-- test: slab-census.a-dropped-class-serves-a-different-one-after-a-scavenge -->
**THE CLASS-AGNOSTIC RETURN.** A burst of one size class is allocated and dropped WHOLE, one
`scavengeMemory()` destroys the spans it emptied, and a burst of a DIFFERENT, larger class is then cut
straight out of the chunks that came back. `committed` must not rise.

⭐ It is the case that separates chunks returned to the PAGE LAYER from chunks parked on one class's
list. An allocator that holds a fully free span for its own class has nothing to offer the larger class
and must claim virgin chunks for it — in granules nothing has committed yet — so `committed` rises by
the whole of that population.

⚠ **ONE CALL, NOT TWO, AND THAT IS WHAT MAKES `committed` THE RIGHT FIGURE TO WATCH.** A span destroy
puts chunks back in the arena's bitmap without decommitting anything, and the granule grace means the
FIRST call can only ever mark. A second call would decommit the very granules the larger class is about
to claim, and `committed` would then fall and rise again for reasons this case is not about.

⚠ **RECLAMATION IS REACHED THROUGH THIS DOOR AND NO OTHER.** A refill hands a fully free span to its own
class rather than destroying it, so a program that never calls `scavengeMemory()` keeps every emptied
span on its class's list — which is what keeps the free path lock-free and costs the allocator nothing
per free.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

let SmallPayload = 96
let LargePayload = 500

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
		small.push(build(k as Seed, n: SmallPayload as Seed))
	end 'allocSmall'
	small = Bufs.create()

	// The one door reclamation is reached through: it destroys the spans the drop emptied and puts their
	// chunks back in the arena's bitmap, where any class can reach them.
	_ = __Builtins.scavengeMemory()

	let committedAfterDrop = __Builtins.slabCommittedBytes()

	if committedAfterDrop <= 0 'nothingCommitted'
		return 1
	end 'nothingCommitted'

	var large = Bufs.create()
	for k in 0 upto 600 'allocLarge'
		large.push(build(k as Seed, n: LargePayload as Seed))
	end 'allocLarge'

	if __Builtins.slabCommittedBytes() > committedAfterDrop 'theSecondClassGrewTheHeap'
		return 2
	end 'theSecondClassGrewTheHeap'

	var bad = 0
	for k in 0 upto 600 'check'
		let b = try large.get(k) otherwise return 3
		if b.count() != LargePayload 'length'
			bad = bad + 1
		end 'length'
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

<!-- test: slab-census.the-tally-totals-the-live-heap -->
**THE TOTAL IS THE LEVEL, AND THE TWO WALKS MUST AGREE.** A census by bucket and the `live` level are
two derivations from one lock-held walk, so summing every bucket must land exactly on the figure
`slabLiveBytes()` reports — no byte of slot may be attributed twice and none may be dropped on the
floor. The case allocates nothing between the two readings, which is what makes an EQUALITY legal here
where every other case in this file asserts a bound.

⭐ It is the case a census can fail while every relation between the five levels stays intact: a walk
that stops at the first metadata chunk, or one that files a free slot as live, moves the total off the
level it is supposed to be a decomposition of.

⚠ **IT USES SIZE-CLASS MODE, SO IT CARRIES NO MARKER AND RUNS IN BOTH BUILD MODES UNCHANGED.**
Attribution by tag needs the prefix word only a `--debugstream` build lays down; a decomposition by
class needs nothing but the geometry every build has.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

let Population = 16000
let PayloadBytes = 96
let SizeClassMode = 1
let BucketCount = 2048

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

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theTallysOwnWalkMissedTheLevel'
		return 1
	end 'theTallysOwnWalkMissedTheLevel'

	var total = 0
	for i in 0 upto BucketCount 'sum'
		total = total + __Builtins.slabCensusBucketBytes(i)
	end 'sum'

	if total != __Builtins.slabLiveBytes() 'theCensusDisagreesWithTheLevel'
		return 2
	end 'theCensusDisagreesWithTheLevel'

	if __Builtins.mmRawAllocLive() < 0 'rawSlotsWentNegative'
		return 3
	end 'rawSlotsWentNegative'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.the-tally-totals-the-live-heap-on-several-processors -->
**THE SAME EQUALITY, ON A HEAP SEVERAL PROCESSORS BUILT.** `the-tally-totals-the-live-heap` above asks it
of a heap one thread cut. This asks it of one where green threads on other Ps own spans, hold mcache rows,
and carry remote-free chains the main thread pushed onto them — the three states a single-threaded program
never puts a span in, and the three a walk can tell apart from the state the level's walk sees.

⭐ **THE POPULATIONS CROSS PROCESSORS IN BOTH DIRECTIONS, WHICH IS WHAT MAKES THE THREE STATES REAL.**
Every survivor is cut inside a green thread and moved to the main thread, so another P's spans are holding
live slots at the tally; half of them are then dropped ON MAIN, which is a free by a thread that does not
hold their span cached and so CAS-pushes each slot onto the owner's remote queue. A tally that credited a
queued slot to the live histogram, or that skipped a span whose owner is a processor rather than this one,
moves off the level by exactly those slots.

⚠ **THE WORKERS ARE ALL AWAITED BEFORE THE TALLY, AND THE TWO READINGS ARE ADJACENT STATEMENTS.** The
allocator's fast path does NOT take `__slab_lock` (`slab-sharding.md` owns why), so a P still running would
be stepping a cached span's `free_count` between the two walks and the equality would be asserting the
scheduler's timing rather than the census. Quiescing first is what leaves only the question this case asks:
given ONE heap, do the two derivations agree.
<!-- procs: 4 -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias BufPromise = Promise with ByteArray
typealias PromiseArray = Array with BufPromise
typealias Seed = int(0 to 65536)

let Workers = 32
let Churn = 200
let PayloadBytes = 96
let SizeClassMode = 1
let BucketCount = 2048

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(PayloadBytes)
	for i in 0 upto PayloadBytes 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function churn(idx Integer) returns ByteArray
	let survivor = build((idx mod 65536) as Seed)

	for j in 0 upto Churn 'churn'
		let tmp = build(((idx + j) mod 65536) as Seed)

		if tmp.count() != PayloadBytes 'short'
			return ByteArray.create()
		end 'short'
	end 'churn'

	return survivor
end 'churn'

function main() returns ExitCode
	var spawned = PromiseArray.create()
	for i in 0 upto Workers 'spawn'
		spawned.push(async churn(i))
	end 'spawn'

	var kept = Bufs.create()
	for p in spawned 'awaitAll'
		kept.push(await p)
	end 'awaitAll'

	if kept.count() != Workers 'populationLost'
		return 1
	end 'populationLost'

	for k in 0 upto Workers 'dropHalfOnMain'
		if (k mod 2) == 0 'even'
			try kept.set(k, value: ByteArray.create()) otherwise return 1
		end 'even'
	end 'dropHalfOnMain'

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theTallysOwnWalkMissedTheLevel'
		return 1
	end 'theTallysOwnWalkMissedTheLevel'

	var total = 0
	for i in 0 upto BucketCount 'sum'
		total = total + __Builtins.slabCensusBucketBytes(i)
	end 'sum'

	if total != __Builtins.slabLiveBytes() 'theCensusDisagreesWithTheLevel'
		return 2
	end 'theCensusDisagreesWithTheLevel'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.a-tagged-population-moves-one-bucket-by-N -->
**THE ATTRIBUTION CASE.** In a `--debugstream` build every box carries a packed `(alloc_id << 16 | tag)`
word ahead of its payload, so the census can say WHOSE the bytes are rather than only how big they are.
A population of N widgets is built between two tallies, and exactly one bucket must be N slots heavier.

⭐ **THE PROGRAM CANNOT NAME ITS OWN TAG INDEX**, which is why the assertion is the LARGEST RISER
rather than a named bucket: a tag is minted by the compiler and a case pinned to a number would be
re-minted by every type the tree adds ahead of `Widget`. The largest riser is the same fact stated in
something the program can observe.

⚠ **THE HOLDER IS RESERVED BEFORE THE FIRST TALLY AND THE SNAPSHOT IS SIZED BEFORE IT TOO.** An element
buffer that grew between the readings would free its old buffer, and a bucket that FELL would be a
defect this case reports against a growth it caused itself.

⚠ **THE POPULATION IS SMALL BECAUSE THE GOLDEN IS THE WHOLE TRACE.** A capture case commits every
`mm_` line its program produced, so the count here is the smallest one that still states the relation —
the largest riser rises by exactly N for any N above zero.
<!-- MmTrace -->
```maxon
typealias WidgetField = int(0 to 65536)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Population = 64
let TagMode = 0
let BucketCount = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	var before = TallyArray.create()
	before.reserve(BucketCount)
	for _ in 0 upto BucketCount 'sizeTheSnapshot'
		before.push(0)
	end 'sizeTheSnapshot'

	if __Builtins.slabCensusTally(TagMode) != __Builtins.slabLiveBytes() 'theFirstTallysOwnWalkMissedTheLevel'
		return 1
	end 'theFirstTallysOwnWalkMissedTheLevel'

	for i in 0 upto BucketCount 'snapshot'
		try before.set(i, value: __Builtins.slabCensusBucketCount(i)) otherwise panic("the snapshot is sized to every bucket")
	end 'snapshot'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	if __Builtins.slabCensusTally(TagMode) != __Builtins.slabLiveBytes() 'theSecondTallysOwnWalkMissedTheLevel'
		return 1
	end 'theSecondTallysOwnWalkMissedTheLevel'

	var biggestRise = 0
	for i in 0 upto BucketCount 'compare'
		let was = try before.get(i) otherwise panic("the snapshot is sized to every bucket")
		let now = __Builtins.slabCensusBucketCount(i)

		if now < was 'aBucketFell'
			return 3
		end 'aBucketFell'

		if (now - was) > biggestRise 'rose'
			biggestRise = now - was
		end 'rose'
	end 'compare'

	if biggestRise != Population 'noBucketRoseByThePopulation'
		return 2
	end 'noBucketRoseByThePopulation'

	return 0
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc ElementBuffer #2 size=512
mm_alloc ArrayRecord #3 size=48
mm_alloc ElementBuffer #4 size=16384
mm_alloc Widget #5 size=16
mm_alloc Widget #6 size=16
mm_alloc Widget #7 size=16
mm_alloc Widget #8 size=16
mm_alloc Widget #9 size=16
mm_alloc Widget #10 size=16
mm_alloc Widget #11 size=16
mm_alloc Widget #12 size=16
mm_alloc Widget #13 size=16
mm_alloc Widget #14 size=16
mm_alloc Widget #15 size=16
mm_alloc Widget #16 size=16
mm_alloc Widget #17 size=16
mm_alloc Widget #18 size=16
mm_alloc Widget #19 size=16
mm_alloc Widget #20 size=16
mm_alloc Widget #21 size=16
mm_alloc Widget #22 size=16
mm_alloc Widget #23 size=16
mm_alloc Widget #24 size=16
mm_alloc Widget #25 size=16
mm_alloc Widget #26 size=16
mm_alloc Widget #27 size=16
mm_alloc Widget #28 size=16
mm_alloc Widget #29 size=16
mm_alloc Widget #30 size=16
mm_alloc Widget #31 size=16
mm_alloc Widget #32 size=16
mm_alloc Widget #33 size=16
mm_alloc Widget #34 size=16
mm_alloc Widget #35 size=16
mm_alloc Widget #36 size=16
mm_alloc Widget #37 size=16
mm_alloc Widget #38 size=16
mm_alloc Widget #39 size=16
mm_alloc Widget #40 size=16
mm_alloc Widget #41 size=16
mm_alloc Widget #42 size=16
mm_alloc Widget #43 size=16
mm_alloc Widget #44 size=16
mm_alloc Widget #45 size=16
mm_alloc Widget #46 size=16
mm_alloc Widget #47 size=16
mm_alloc Widget #48 size=16
mm_alloc Widget #49 size=16
mm_alloc Widget #50 size=16
mm_alloc Widget #51 size=16
mm_alloc Widget #52 size=16
mm_alloc Widget #53 size=16
mm_alloc Widget #54 size=16
mm_alloc Widget #55 size=16
mm_alloc Widget #56 size=16
mm_alloc Widget #57 size=16
mm_alloc Widget #58 size=16
mm_alloc Widget #59 size=16
mm_alloc Widget #60 size=16
mm_alloc Widget #61 size=16
mm_alloc Widget #62 size=16
mm_alloc Widget #63 size=16
mm_alloc Widget #64 size=16
mm_alloc Widget #65 size=16
mm_alloc Widget #66 size=16
mm_alloc Widget #67 size=16
mm_alloc Widget #68 size=16
mm_decref ArrayRecord #3 rc=0
mm_decref ElementBuffer #4 rc=0
mm_free ElementBuffer #4
mm_free ArrayRecord #3
mm_decref ArrayRecord #1 rc=0
mm_decref Widget #5 rc=0
mm_free Widget #5
mm_decref Widget #6 rc=0
mm_free Widget #6
mm_decref Widget #7 rc=0
mm_free Widget #7
mm_decref Widget #8 rc=0
mm_free Widget #8
mm_decref Widget #9 rc=0
mm_free Widget #9
mm_decref Widget #10 rc=0
mm_free Widget #10
mm_decref Widget #11 rc=0
mm_free Widget #11
mm_decref Widget #12 rc=0
mm_free Widget #12
mm_decref Widget #13 rc=0
mm_free Widget #13
mm_decref Widget #14 rc=0
mm_free Widget #14
mm_decref Widget #15 rc=0
mm_free Widget #15
mm_decref Widget #16 rc=0
mm_free Widget #16
mm_decref Widget #17 rc=0
mm_free Widget #17
mm_decref Widget #18 rc=0
mm_free Widget #18
mm_decref Widget #19 rc=0
mm_free Widget #19
mm_decref Widget #20 rc=0
mm_free Widget #20
mm_decref Widget #21 rc=0
mm_free Widget #21
mm_decref Widget #22 rc=0
mm_free Widget #22
mm_decref Widget #23 rc=0
mm_free Widget #23
mm_decref Widget #24 rc=0
mm_free Widget #24
mm_decref Widget #25 rc=0
mm_free Widget #25
mm_decref Widget #26 rc=0
mm_free Widget #26
mm_decref Widget #27 rc=0
mm_free Widget #27
mm_decref Widget #28 rc=0
mm_free Widget #28
mm_decref Widget #29 rc=0
mm_free Widget #29
mm_decref Widget #30 rc=0
mm_free Widget #30
mm_decref Widget #31 rc=0
mm_free Widget #31
mm_decref Widget #32 rc=0
mm_free Widget #32
mm_decref Widget #33 rc=0
mm_free Widget #33
mm_decref Widget #34 rc=0
mm_free Widget #34
mm_decref Widget #35 rc=0
mm_free Widget #35
mm_decref Widget #36 rc=0
mm_free Widget #36
mm_decref Widget #37 rc=0
mm_free Widget #37
mm_decref Widget #38 rc=0
mm_free Widget #38
mm_decref Widget #39 rc=0
mm_free Widget #39
mm_decref Widget #40 rc=0
mm_free Widget #40
mm_decref Widget #41 rc=0
mm_free Widget #41
mm_decref Widget #42 rc=0
mm_free Widget #42
mm_decref Widget #43 rc=0
mm_free Widget #43
mm_decref Widget #44 rc=0
mm_free Widget #44
mm_decref Widget #45 rc=0
mm_free Widget #45
mm_decref Widget #46 rc=0
mm_free Widget #46
mm_decref Widget #47 rc=0
mm_free Widget #47
mm_decref Widget #48 rc=0
mm_free Widget #48
mm_decref Widget #49 rc=0
mm_free Widget #49
mm_decref Widget #50 rc=0
mm_free Widget #50
mm_decref Widget #51 rc=0
mm_free Widget #51
mm_decref Widget #52 rc=0
mm_free Widget #52
mm_decref Widget #53 rc=0
mm_free Widget #53
mm_decref Widget #54 rc=0
mm_free Widget #54
mm_decref Widget #55 rc=0
mm_free Widget #55
mm_decref Widget #56 rc=0
mm_free Widget #56
mm_decref Widget #57 rc=0
mm_free Widget #57
mm_decref Widget #58 rc=0
mm_free Widget #58
mm_decref Widget #59 rc=0
mm_free Widget #59
mm_decref Widget #60 rc=0
mm_free Widget #60
mm_decref Widget #61 rc=0
mm_free Widget #61
mm_decref Widget #62 rc=0
mm_free Widget #62
mm_decref Widget #63 rc=0
mm_free Widget #63
mm_decref Widget #64 rc=0
mm_free Widget #64
mm_decref Widget #65 rc=0
mm_free Widget #65
mm_decref Widget #66 rc=0
mm_free Widget #66
mm_decref Widget #67 rc=0
mm_free Widget #67
mm_decref Widget #68 rc=0
mm_free Widget #68
mm_decref ElementBuffer #2 rc=0
mm_free ElementBuffer #2
mm_free ArrayRecord #1
```

<!-- test: slab-census.an-untraced-build-tallies-by-size-class -->
**THE SAME POPULATION, READ BY GEOMETRY INSTEAD OF BY TAG.** Mode 1 needs no prefix word, so it answers
in every build — and it answers with a bucket per size class, which is a fact the allocator's own ladder
already holds. The same N widgets move one bucket by N, and that bucket's bytes divided by its count is
the class's slot size.

⭐ **THE QUOTIENT IS WHAT MAKES THIS A CLASS BUCKET RATHER THAN A HEAP OF EVERYTHING.** A bucket holding
two different slot sizes has a quotient that is neither of them, and one keyed by the REQUESTED size
answers below the box it must actually hold. The bound is derived from the PROGRAM — a widget's fields
plus the box header — so it is not the geometry pinned against itself.
```maxon
typealias WidgetField = int(0 to 65536)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Population = 1000
let SizeClassMode = 1
let BucketCount = 2048
let HeaderBytes = 24

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	var before = TallyArray.create()
	before.reserve(BucketCount)
	for _ in 0 upto BucketCount 'sizeTheSnapshot'
		before.push(0)
	end 'sizeTheSnapshot'

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theFirstTallysOwnWalkMissedTheLevel'
		return 1
	end 'theFirstTallysOwnWalkMissedTheLevel'

	for i in 0 upto BucketCount 'snapshot'
		try before.set(i, value: __Builtins.slabCensusBucketCount(i)) otherwise panic("the snapshot is sized to every bucket")
	end 'snapshot'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theSecondTallysOwnWalkMissedTheLevel'
		return 1
	end 'theSecondTallysOwnWalkMissedTheLevel'

	var biggestRise = 0
	var biggestIndex = 0
	for i in 0 upto BucketCount 'compare'
		let was = try before.get(i) otherwise panic("the snapshot is sized to every bucket")
		let now = __Builtins.slabCensusBucketCount(i)

		if now < was 'aBucketFell'
			return 3
		end 'aBucketFell'

		if (now - was) > biggestRise 'rose'
			biggestRise = now - was
			biggestIndex = i
		end 'rose'
	end 'compare'

	if biggestRise != Population 'noBucketRoseByThePopulation'
		return 2
	end 'noBucketRoseByThePopulation'

	let count = __Builtins.slabCensusBucketCount(biggestIndex)
	let bytes = __Builtins.slabCensusBucketBytes(biggestIndex)
	let slot = trunc(try (bytes / count) otherwise panic("the bucket that rose by the population holds slots"))

	if (slot * count) != bytes 'theBucketMixesSlotSizes'
		return 4
	end 'theBucketMixesSlotSizes'

	if slot < (sizeof(Widget) + HeaderBytes) 'slotTooSmallForAWidget'
		return 4
	end 'slotTooSmallForAWidget'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.dropping-the-population-returns-the-bucket -->
**THE IDENTITY CASE, ASKED OF ONE BUCKET.** `dropping-a-population-moves-bytes-from-live-to-free` says a
drop moves bytes from `live` to `free` and may not raise their sum; this says WHICH bucket the slots
left. One bucket must fall by exactly the population, and the sum of the two levels must not rise across
the drop — the same one-sided slack, for the same reason: only a span being destroyed takes slots out of
the accounting, and nothing here destroys one.

⭐ A census that never retires a slot keeps every level relation intact and reports a bucket that only
ever grows, which is a residency table that cannot see a leak being fixed.
```maxon
typealias WidgetField = int(0 to 65536)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Population = 1000
let SizeClassMode = 1
let BucketCount = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	var before = TallyArray.create()
	before.reserve(BucketCount)
	for _ in 0 upto BucketCount 'sizeTheSnapshot'
		before.push(0)
	end 'sizeTheSnapshot'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theFirstTallysOwnWalkMissedTheLevel'
		return 1
	end 'theFirstTallysOwnWalkMissedTheLevel'

	for i in 0 upto BucketCount 'snapshot'
		try before.set(i, value: __Builtins.slabCensusBucketCount(i)) otherwise panic("the snapshot is sized to every bucket")
	end 'snapshot'

	let liveHolding = __Builtins.slabLiveBytes()
	let freeHolding = __Builtins.slabFreeBytes()

	holder = Widgets.create()

	if __Builtins.slabCensusTally(SizeClassMode) != __Builtins.slabLiveBytes() 'theSecondTallysOwnWalkMissedTheLevel'
		return 1
	end 'theSecondTallysOwnWalkMissedTheLevel'

	var biggestFall = 0
	for i in 0 upto BucketCount 'compare'
		let was = try before.get(i) otherwise panic("the snapshot is sized to every bucket")
		let now = __Builtins.slabCensusBucketCount(i)

		if (was - now) > biggestFall 'fell'
			biggestFall = was - now
		end 'fell'
	end 'compare'

	if biggestFall != Population 'noBucketFellByThePopulation'
		return 2
	end 'noBucketFellByThePopulation'

	if (__Builtins.slabLiveBytes() + __Builtins.slabFreeBytes()) > (liveHolding + freeHolding) 'slotsAppearedFromNowhere'
		return 3
	end 'slotsAppearedFromNowhere'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-census.raw-live-slots-do-not-count-boxes -->
**THE HEADER-LESS POPULATION IS WHAT THE CENSUS PRINTS BESIDE ITS TABLE, AND A BOX IS NOT ONE.** Every box
the memory manager hands out is also a slab request, so the raw layer's own live count includes every box in
the program — a figure equal to the whole live table, which attributes nothing. What the census needs
beside its table is the slots that carry NO box header: an element buffer, a string's bytes, a closure's
environment. `__Builtins.mmRawAllocLive()` is already that figure — the raw layer's live count less the
tracked layer's, taken per traffic row — so the census reads it rather than carrying a second builtin
answering the same walk. `builtins-mm-counters.md` owns what it counts; this case owns that it is still the
right figure for a census to print.

⭐ A population of N boxes must therefore move this figure by far less than N. It moves it at all only
through the buffers the holder grows on the way, which is a handful of slots for a thousand boxes.
```maxon
typealias WidgetField = int(0 to 65536)
typealias Delta = int(i64.min to i64.max)

let Population = 1000

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	let before = __Builtins.mmRawAllocLive() as Delta

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	let rose = (__Builtins.mmRawAllocLive() as Delta) - before

	if holder.count() != Population 'theWidgetsWereNotHeld'
		return 1
	end 'theWidgetsWereNotHeld'

	if rose >= (Population / 2) 'theBoxesWereCountedAsHeaderLessSlots'
		return 2
	end 'theBoxesWereCountedAsHeaderLessSlots'

	return 0
end 'main'
```
```exitcode
0
```
