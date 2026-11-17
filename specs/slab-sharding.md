---
feature: slab-sharding
status: stable
keywords: [allocator, slab, sharding, processor, mcache, remote-free, ownership, concurrency, runtime]
category: system
---

# The sharded slab — per-processor mcache rows, span ownership, and the remote-free queue

## Documentation

The allocator is **per-P sharded**. Each processor (`SchedRuntime`'s P) owns its own mcache
ROW and is the sole writer of every span cached there, so the allocation fast path takes no lock. Three
mechanisms make that safe, and each of them is reachable — in part — from an ordinary single-processor
program, which is what the cases below drive:

| Mechanism | What it decides |
|---|---|
| **the shard row** | the P read (`emitSlabResolveShard`, emitted inline into each door of the three families, counted, tracked and scaffold, such as `__slab_alloc`/`__slab_free`; `__mm_alloc`/`__mm_free` walk no shard) walks the scheduler word to the running M and to the P it holds; that P's clamped `id` is the mcache row, and a thread that holds NO P gets the dedicated RAW row (255), never row 0 |
| **the ownership stamp** | every span carries the P that owns it; a cached span owned by somebody else is a MISS, not something to pop |
| **the remote-free queue** | a free by a P that does not hold the slot's span cached CAS-pushes it onto the SPAN's own Treiber stack; the chain is collected when a P takes that span off its class's list, when an exhausted span is relisted, and by the scavenger |

⚠ **NO CASE HERE CAN DEMAND THAT TWO PROCESSORS CONTEND, BECAUSE A COMMITTED CASE CANNOT.** A case can set
its processor count — the harness has a per-case processor marker (`specs/sched-default-procs.md` owns it), and
the default is the machine's processor count, so an unmarked case is already multi-processor on any ordinary
host; `a-sole-processors-frees-onto-an-uncached-span-are-not-remote` uses that marker to pin ONE processor.
But the multi-M properties need the OS to actually run a second machine while twelve harness workers compete
for the box, and it sometimes does not: a multi-processor version of that case fails intermittently at eight
messages, and so does a version that WAITS for the property across up to 500 waves. `pin-matrix.sh:100-137`
hits the same wall from the other side.

⚠ **THE CASES AT THE END RUN AT FOUR PROCESSORS AND ARE A SAFETY NET, NOT A GATE.** Six services churn
`String`s through six size classes, then the same six with a seventh service scavenging under them, then six
over ONE class, then six over one class with a seventh scavenging, and each checks every byte of every object
it reads. They are green whether or not the OS ran the services on different machines at the moment that
mattered, so a contention bug in the allocator's shared structures shows as a failure rate across runs, not
as a red on every run.

⚠ **`multicore-stress/alloc-torture.maxon` IS NOT THE MULTI-PROCESSOR GATE.** Its work is `async`, an
`async` frame is a coroutine of its caller, and that program is asserted to read `workers=1` at one
processor. The
multi-processor readings come from the SPAWN family (`service-torture`, `service-fanin-torture`)
driven by `multicore-stress/pin-matrix.sh`, which runs standalone and sweeps the count.

What a ONE-processor program CAN reach, and what each case below is for:

* **the P-owned row.** A green-thread program runs its main thread as P[0], so every allocation a green
  thread makes goes through row 0 with a real owner stamped on the span — the refill, the eviction of a
  drained span and the return of an emptied one all run against `owning_p`.
* **the RAW row and its lock.** Everything a program allocates *before* its first `async` is allocated
  with no P at all, so those spans are owned by nobody and live on the raw row. Freeing one of them
  AFTER the scheduler exists takes the serialised path under the raw-row lock — the one arm of that lock
  an ordinary single-threaded program reaches, and it reaches it on **every** green-thread program.
* **the uncached sentinels.** A span a processor has drained is stamped with a sentinel that is neither a
  P nor "no P" and reaches its class's mcentral list on the next free into it; the next refill re-stamps
  it. A free that observed the DEAD sentinel would be a double free — the span's last slot has already
  come back and the header is on the metadata free list — and the runtime aborts
  (`slabFreeOfParkedSpan`, exit 89) rather than writing through a recycled record.

⚠ **WHAT NO CASE HERE CAN FORCE, STATED SO NOBODY READS SILENCE AS COVERAGE**: the ownership gate
actually REJECTING a span, two threads contending for the raw row, two processors meeting on one class's
stack or on the heap lock, and whether the traffic counters are stepped ATOMICALLY. Those need a second OS
thread running at the same instant; the four-processor cases give the OS the chance, and
`multicore-stress/` sweeps for it.

⭐ **THE REMOTE-FREE TREIBER PUSH IS COUNTABLE, WHICH IS NOT THE SAME AS BEING PINNED HERE.**
`HeapStats.slab().remoteFrees` sums a per-P counter, so the road that `service-torture` and
`service-fanin-torture` drive moves a number. ⚠ **The
non-zero reading is `multicore-stress`'s and not this file's** — see the last case for why.
What this file pins is the counter's other edge: at one processor it must be exactly **0**, because a
counter that answers non-zero where no free can cross is counting the wrong frees.

⚠ **THE SHARDING IS COMPILED INTO EVERY HEAP PROGRAM, AND THE CASES HERE ARE STILL GREEN-THREAD PROGRAMS.**
A program with no scheduler runs the same allocator: the scheduler word in the slab's state head reads 0,
every thread takes the raw row as its sole writer, and no lock is taken — `emitted-runtime-body.md`'s
`allocator-shape-is-one-in-a-program-with-no-scheduler` renders that path. What a P-owned row, a parked
span's re-owning and a remote free need is a P, and a P exists only where a scheduler has been built, so
every case below spawns one. ⚠ **NO CASE CARRIES AN `unsupported-targets:` MARKER, AND NONE MAY:** on wasm a
green-thread program is refused at its own span with E3104, which the harness counts as a SKIP naming the
case; a marker would make the same fact invisible (`docs/SPECS.md`, *Which lanes a case
excludes*). `sched-processor.md` carries the identical restriction for the identical reason.

## Tests

<!-- test: slab-sharding.a-green-thread-program-allocates-through-its-processor-row -->
**THE P-OWNED ROW, HAMMERED.** A green thread's allocations run with `main` adopted as P[0], so every
span they touch is cut, stamped, cached, drained, evicted and returned against a real owner. This builds
and drops thousands of short-lived Strings inside green threads and then verifies a population built the
same way, so a span handed to the wrong row — or a refill that installed a span without stamping it —
shows up as corrupted survivors rather than as a passing program.

⚠ The survivors are built INSIDE green threads too, and read back on the main thread after every one of
them has completed: that is what makes the check see the row's spans rather than only main's own.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias StringArray = Array with String
typealias StringPromise = Promise with String
typealias PromiseArray = Array with StringPromise
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function churn(idx Integer) returns Integer
	var acc = 0 as Integer
	for j in 0 upto 60 'alloc'
		let tmp = "shard-{idx}-slot-{j}"
		if not tmp.isEmpty() 'nonEmpty'
			acc = acc + 1
		end 'nonEmpty'
	end 'alloc'
	return acc
end 'churn'

function survivor(idx Integer) returns String
	return "keep-{idx}-{idx}"
end 'survivor'

function main() returns ExitCode
	let churns = IntPromiseArray.create(0)
	for i in 0 upto 40 'spawnChurn'
		churns.push(async churn(i))
	end 'spawnChurn'

	let kept = PromiseArray.create(0)
	for k in 0 upto 24 'spawnKeep'
		kept.push(async survivor(k))
	end 'spawnKeep'

	var total = 0 as Integer
	for c in churns 'awaitChurn'
		total = total + await c
	end 'awaitChurn'

	if total != 2400 'churnLost'
		return 1
	end 'churnLost'

	let strings = StringArray.create(0)
	for p in kept 'awaitKeep'
		strings.push(await p)
	end 'awaitKeep'

	for (iter, s) in strings.withIterator() 'check'
		let idx = iter.index()
		if s != "keep-{idx}-{idx}" 'corrupted'
			return 2
		end 'corrupted'
	end 'check'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-sharding.a-slot-allocated-before-the-scheduler-is-freed-after-it -->
**THE RAW ROW, AND THE ONE ARM OF THE RAW-ROW LOCK A SINGLE-THREADED PROGRAM REACHES.** Everything allocated
before the first `async` is allocated with NO processor: the P read answers "none", the spans
land on the dedicated raw row and are stamped as owned by nobody. Once the scheduler exists those same
spans are shared, in principle, by every OS thread that owns no P — so a free of one of their slots
takes the serialised path.

⭐ **THE ORDER IS THE WHOLE CASE.** The pre-scheduler population is built FIRST, is still live when the
scheduler starts, and is only released afterwards. That is what puts a whole population of NO-OWNER spans
in front of a running scheduler, which no other case here does.

⚠ **WHAT IT CATCHES AND WHAT IT ONLY EXERCISES, because the difference is not visible from a green
result.** It CATCHES a refill that fails to establish a span's owner (with the owner stamp
removed from `__slab_refill`, this case exits 89). It only EXERCISES the serialised arm of the raw-row lock: with
one OS thread there is no second writer to race, so a build that took no lock at all would still pass
here. That half is not this case's to verify.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)
typealias Integer = int(i64.min to i64.max)

function build(seed Seed) returns ByteArray
	let b = ByteArray.create(0)
	b.reserve(48)
	for i in 0 upto 48 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function work() returns Integer
	var acc = 0 as FullRangeInt
	for j in 0 upto 200 'alloc'
		let tmp = "post-scheduler-{j}"
		if not tmp.isEmpty() 'nonEmpty'
			acc = acc + 1
		end 'nonEmpty'
	end 'alloc'
	return acc
end 'work'

function main() returns ExitCode
	// Allocated with no processor: the raw row.
	var early = Bufs.create(0)
	for k in 0 upto 3000 'preScheduler'
		early.push(build(k as Seed))
	end 'preScheduler'

	// The scheduler comes up here, and main becomes P[0].
	let p = async work()
	if await p != 200 'workLost'
		return 1
	end 'workLost'

	// Read the pre-scheduler population back, THEN drop it — every one of these frees is of a
	// slot on a span that no processor owns, with a scheduler running.
	var bad = 0 as FullRangeInt
	for (iter, b) in early.withIterator() 'check'
		let seed = iter.index()
		if b.count() != 48 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 48 'bytes'
			let got = try b.get(i) otherwise return 2
			if (got as ElementIndex) != ((seed + i) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'

	early = Bufs.create(0)

	if bad != 0 'corrupted'
		return 3
	end 'corrupted'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-sharding.a-parked-span-is-taken-back-and-re-owned -->
**THE UNCACHED SENTINELS, ROUND-TRIPPED.** A span whose last slot is handed OUT is unlinked from its
owner's mcache row and stamped with an owner that is neither a processor nor "no processor"; the first
free back into it lists it on mcentral, and the next refill takes it back and re-stamps it. This case
empties a class's spans wholesale and then re-fills the same class, twice over, so every span in it
makes that round trip several times.

⛔ **THE FAILURE IS AN ABORT, NOT A WRONG ANSWER.** A free that reached a span carrying the DEAD sentinel
would be a double free of a span whose header has already gone back to the metadata slab, and
`__slab_free` exits **89** rather than writing through it — so a refill that forgot to re-stamp the owner
cannot pass this case by accident. A refill that stamped the WRONG owner is caught by the survivors
instead.

⚠⚠ **THE `async` IS LOAD-BEARING AND IS NOT DECORATION — WITHOUT IT THIS CASE READS A DIFFERENT ARM OF
`__slab_free`.** The allocator is one shape in every heap program, but WHICH arm a free takes is decided by
the span's owner, and a program with no scheduler allocates with no P: every span it cuts is stamped
`SpanOwnerState.none`, so every free of one goes down the raw arm, which compares no owner and never reads an
uncached sentinel. A real P is what stamps a span with a real owner, and the uncached/mine/remote routing
this case is about begins there.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed) returns ByteArray
	let b = ByteArray.create(0)
	b.reserve(80)
	for i in 0 upto 80 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function cycle(rounds Seed) returns Seed
	var seen = 0 as Seed
	for r in 0 upto rounds 'round'
		var wave = Bufs.create(0)
		for k in 0 upto 4000 'alloc'
			wave.push(build(((r * 4000) + k) as Seed))
		end 'alloc'
		seen = (seen + (wave.count() as Seed)) as Seed
		// Dropping the whole wave empties every span of this class: each one parks on mcentral with
		// its owner cleared, and the next round's allocations must take them back and re-own them.
		wave = Bufs.create(0)
	end 'round'
	return seen
end 'cycle'

function main() returns ExitCode
	// The whole cycle runs inside a green thread, so this program carries the SHARDED allocator and its
	// spans are parked and re-owned against a real processor.
	let p = async cycle(3)

	if await p != 12000 'lostBuffers'
		return 1
	end 'lostBuffers'

	// The survivors are cut from spans that have each been parked and re-owned several times over.
	let keep = Bufs.create(0)
	for k in 0 upto 200 'survivors'
		keep.push(build((k + 900) as Seed))
	end 'survivors'

	var bad = 0 as FullRangeInt
	for (iter, b) in keep.withIterator() 'check'
		let seed = iter.index() + 900
		if b.count() != 80 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 80 'bytes'
			let got = try b.get(i) otherwise return 2
			if (got as ElementIndex) != ((seed + i) mod 251) 'value'
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

<!-- test: slab-sharding.a-parked-span-still-reaches-the-scavenger -->
**THE OWNER STAMP AND THE SCAVENGER, TOGETHER.** The scavenger destroys spans off the mcentral lists, and
every one of those carries an uncached sentinel in the field the mcache eviction wrote. This is the
two-pass grace case run through a GREEN THREAD, so the spans it destroys were owned by a real processor
before they were emptied — the combination the single-threaded scavenger cases cannot make.

⚠ Two calls, because the granule grace releases nothing on the first: that is `slab-scavenger`'s rule,
and it is restated here only to say that sharding did not change it.

⚠ **THIS ONE IS COVERAGE, NOT A DISCRIMINATOR, AND SAYING SO IS THE POINT.** Against a compiler
whose refill is stripped of its owner stamp, the three cases above exit 89 and this one still PASSES
— its population is large enough that almost every span it touches is freshly CUT rather than taken back
off mcentral, and a freshly cut span's header is zeroed, which reads as a valid "no owner". It earns its
place by driving the destruction over spans a processor owned; it does not stand in for the cases above.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Integer = int(i64.min to i64.max)

function fill() returns Integer
	var bufs = Bufs.create(0)
	for k in 0 upto 16000 'alloc'
		let b = ByteArray.create(0)
		b.reserve(96)
		for i in 0 upto 96 'bytes'
			b.push(((k + i) mod 251) as Byte)
		end 'bytes'
		bufs.push(b)
	end 'alloc'
	let n = bufs.count()
	bufs = Bufs.create(0)
	return n as Integer
end 'fill'

function main() returns ExitCode
	let p = async fill()
	if await p != 16000 'lost'
		return 1
	end 'lost'

	let first = HeapStats.scavenge()
	if first != 0 'graceSkipped'
		return 2
	end 'graceSkipped'

	let second = HeapStats.scavenge()
	if second <= 0 'nothingReleased'
		return 3
	end 'nothingReleased'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slab-sharding.the-traffic-counters-are-exact-across-green-threads -->
**THE COUNTERS, READ THROUGH THE SHARDED ALLOCATOR.** Every traffic column an emitted program keeps is
stepped inside the slab's doors, and `__mm_alloc`/`__mm_free` carry the tracked doors' hit path inline in a
build without `--debugstream` and otherwise reach `__slab_alloc_tracked`/`__slab_free_tracked`. A door's hit
path carries the class lookup, the processor read, the shard row, the state region and the pop in ONE leaf,
and a SHARDED build's copy is a different emission from the single-threaded one (it carries the ownership
gate and the remote push; the lock arm and the remote drain stay in the `_slow` body). This drives two waves of green-thread
allocations through it and asks the two tracked columns the only questions that are EXACT: the LIVE
count comes back to the number it started at, and two identical waves credit the CUMULATIVE column by
the identical amount.

⚠ The warm-up wave is load-bearing: the first `async` in a process brings the scheduler up, and
bring-up allocates. Measuring from after it is what makes `first == second` an equality rather than an
inequality with a fudge factor.

⚠⚠ **WHAT THIS DOES NOT PIN, BECAUSE THE HARNESS CANNOT: THE `lock` PREFIX.** The traffic columns are
per-row: a P steps its own row plainly and the shared raw row is stepped with an `atomicRmw` once a
scheduler exists (`SlabRuntime.emitSlabTrafficCredit`), and telling a plain step from an atomic one needs
a second P-less thread crediting the raw row at the same instant — which needs more than one processor. A case CAN ask for four, and the case at the end of this file does.
At one processor the plain form
is exact too, so this case passes either way and does not claim otherwise.

⛔ **`multicore-stress/alloc-torture.maxon` DOES NOT PIN IT EITHER.** An `async` task is a coroutine of its
calling green thread, so its tasks never leave that one green thread, which runs on one M at a
time — a preemption or a wake can move it to another, but never runs it on two. It proves determinism and leak-freedom; it does not
discriminate the `lock` prefix, because no column is ever credited by two machines at once. ⚠ **This does
NOT mean the raw row is stepped plainly** — its step is atomic wherever the scheduler word is non-zero, and the
system monitor steps it whatever `async` does. It means the ORACLE for that arm needs
`spawn`, which is where a second M comes from.

⚠⚠ **THAT ORACLE IS DISCHARGEABLE AND NOT DISCHARGED — SAID PLAINLY SO IT
IS NOT READ AS PAID.** A spawned
program at four processors really does put two Ms on one column. What the case at the end of this file adds
is NOT that — it observes that the remote-free ROAD IS TAKEN (`HeapStats.slab().remoteFrees > 0`), which is a
different subject from whether a contended column loses an update. ⇒ **the `lock`-prefix oracle is still
owed**, and what it needs is this case's two waves driven by `spawn` rather than `async`, with the atomic
forced off as the positive control, where the leak gate (exit 101) is the lost-update oracle.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Count = int(0 to 65536)

function churn(seed Count) returns Integer
	var acc = 0 as FullRangeInt
	for j in 0 upto 400 'alloc'
		let tmp = "count-{seed}-{j}"
		if not tmp.isEmpty() 'nonEmpty'
			acc = acc + 1
		end 'nonEmpty'
	end 'alloc'
	return acc
end 'churn'

function wave(base Count) returns Integer
	let a = async churn(base)
	let b = async churn((base + 1) as Count)
	return (await a) + (await b)
end 'wave'

function main() returns ExitCode
	// Bring the scheduler up before anything is measured.
	if wave(0) != 800 'warmupLost'
		return 1
	end 'warmupLost'

	let liveBefore = HeapStats.allocationsLive()
	let totalBefore = HeapStats.allocationsMade()

	if wave(10) != 800 'firstLost'
		return 2
	end 'firstLost'
	let totalMid = HeapStats.allocationsMade()

	if wave(20) != 800 'secondLost'
		return 3
	end 'secondLost'
	let totalAfter = HeapStats.allocationsMade()
	let liveAfter = HeapStats.allocationsLive()

	let first = totalMid - totalBefore
	let second = totalAfter - totalMid

	var score = 0 as FullRangeInt
	if liveAfter == liveBefore 'liveCameBack'
		score = score + 1
	end 'liveCameBack'
	if first == second 'wavesAgree'
		score = score + 2
	end 'wavesAgree'
	if first > 0 'itMoved'
		score = score + 4
	end 'itMoved'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: slab-sharding.a-local-free-is-never-counted-as-a-remote-one -->
<!-- procs: 1 -->
⭐ **`HeapStats.slab().remoteFrees` IS THE CROSS-P FREE'S OBSERVER.** A
box `main` allocates and a `spawn`ed service drops is released by whichever machine ran that receiver —
`SlabRuntime`'s remote-free road, a CAS push onto the SPAN's own Treiber stack, credited to the PUSHER. So
what it counts is frees PERFORMED across processors rather than frees received. `service-torture` and
`service-fanin-torture` drive thousands of those, and this counter is what observes them; it is per-P and summed like
`schedStealCount()`, so it costs no `.data` word.

⭐⭐ **WHAT THIS CASE PINS IS THE HALF THAT IS DETERMINISTIC: AT ONE PROCESSOR THE ANSWER IS EXACTLY ZERO.**
Every free in a one-P program is local by construction, so a counter that ever answers non-zero here is
counting the wrong frees — which is a real defect and the one this case exists to catch. It runs the same
spawn-and-move traffic the multi-processor reading uses, so the boxes genuinely travel between green
threads; what they cannot do at one processor is travel between MACHINES.

⛔⛔ **THE OTHER HALF — "a remote free ACTUALLY HAPPENS" — IS NOT A SPEC CASE.** A case for it is flaky
under suite load, even one that **waits** for the property, sending up to 500 waves and stopping the instant
a box crosses. The reason is structural, not a matter of scale: `main` sends and then awaits, so once `main`
parks its own machine runs the receivers unless a worker M steals one first — and whether the OS
schedules that worker while twelve harness workers are competing is not something the program decides.
`pin-matrix.sh:100-137` hits the same wall from the other side: at total CPU saturation
*"no program-side change can fix that"*.

⇒ **A committed case that needs the OS to co-operate is a flake, so the multi-M reading lives in `multicore-stress/`**,
which runs standalone rather than under a twelve-way load, sweeps the processor count, and already asserts
the same class of property (`steals > 0` at every effective N ≥ 2) for the spawn family. ⚠ **This is a
statement about where the reading belongs, not a retreat from taking it** — the counter is read there, and a
green run of this case plus a green `pin-matrix.sh` row is the pair.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SinkHandleArray = Array with Sink.handle
typealias CountPromise = Promise with Integer
typealias CountPromiseArray = Array with CountPromise

let sinkCount = 8
let waves = 40

type Sink
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(s String) returns Integer
		self.seen = self.seen + (s.count() as Integer)

		return 1
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sinks = SinkHandleArray.create(0)
	var i = 0 as Integer
	while i < sinkCount 'spawnEach'
		sinks.push(spawn Sink.create())
		i = i + 1
	end 'spawnEach'

	var delivered = 0 as Integer
	var wave = 0 as Integer
	while wave < waves 'waves'
		let replies = CountPromiseArray.create(0)
		var k = 0 as Integer
		while k < sinkCount 'sendEach'
			let s = try sinks.get(k as ElementIndex) otherwise panic("sinks.get OOB at {k}")
			replies.push(try s.take("payload-{k}-{wave}") otherwise panic("an open mailbox with room takes the send"))
			k = k + 1
		end 'sendEach'

		var n = 0 as Integer
		while n < sinkCount 'collect'
			let p = try replies.get(n as ElementIndex) otherwise panic("replies.get OOB at {n}")
			delivered = delivered + (await p)
			n = n + 1
		end 'collect'
		wave = wave + 1
	end 'waves'

	print("delivered={delivered} remote={HeapStats.slab().remoteFrees}\n")

	return 0
end 'main'
```
```stdout
delivered=320 remote=0
```
```exitcode
0
```

<!-- test: slab-sharding.a-sole-processors-frees-onto-an-uncached-span-are-not-remote -->
<!-- procs: 1 -->
Twenty thousand strings fill spans the one processor gives back to the shared list, so most of them are freed onto a span no processor caches; those frees are local too.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Counted = int(0 to 1000000)
typealias StringBox = Array with String

type Sink
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(s String) returns Integer
		self.seen = self.seen + (s.count() as Integer)

		return 1
	end 'take'
end 'Sink'

function holdThenDrop(n Counted) returns Integer
	let held = StringBox.create(0)
	var i = 0 as Integer
	while i < (n as Integer) 'fill'
		held.push("payload-{i}")
		i = i + 1
	end 'fill'

	let before = HeapStats.slab().remoteFrees as Integer
	print("held={held.count()} remote-before-drop={before}\n")

	return before
end 'holdThenDrop'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let first = try await sink.take("warm") otherwise 0
	print("first={first} remote-start={HeapStats.slab().remoteFrees}\n")

	let before = holdThenDrop(20000)
	print("remote-after-drop={HeapStats.slab().remoteFrees} before={before}\n")

	return 0
end 'main'
```
```stdout
first=1 remote-start=0
held=20000 remote-before-drop=0
remote-after-drop=0 before=0
```
```exitcode
0
```

<!-- test: slab-sharding.services-churning-different-size-classes-keep-their-sums -->
<!-- procs: 4 -->
**SIX SERVICES, SIX SIZE CLASSES, ONE ALLOCATOR, FOUR PROCESSORS.** Every refill, every return of an exhausted
span to its class's list and every scavenge shares the allocator's structures, and a change to how they are
guarded must leave this program's answers where they are. Each service builds a population of `String`s of
ONE fixed size per round, hands every second one to the next service of a ring to drop, and drops the rest at
the end of its round, so a service fills whole spans of its own class, frees slots of spans it did not allocate
from, and frees slots onto spans another processor holds.

The sizes are chosen from the class table (`SlabClasses.maxon`) to spread over it: a string's slot is its
header plus its bytes, and these pads land in six different classes, which the program ASKS THE CENSUS about
before it starts (exit 4 when a pad lands anywhere else, so the claim below cannot rot silently).

| service | pad bytes | slot class | slot bytes | slots per span | objects per round |
|---|---|---|---|---|---|
| 0 | 3 | 8 | 96 | 85 | 1600 |
| 1 | 185 | 19 | 288 | 28 | 800 |
| 2 | 1000 | 33 | 1152 | 7 | 360 |
| 3 | 3700 | 44 | 4096 | 2 | 120 |
| 4 | 9900 | 54 | 10240 | 4 (5 pages) | 48 |
| 5 | 30000 | 67 | 32768 | 1 (4 pages) | 16 |

Every class therefore fills between 12 and 60 spans per round and gives them all back, 120 rounds over, so the
refill, the relist of an exhausted span and the return of an emptied one run thousands of times per class, on
different classes at the same moment.

⭐ **EACH OBJECT CARRIES ITS OWN KEY AND FILL BYTE, AND THE CHECK READS EVERY BYTE.** A string is
`{key:04}:<pad of the service's fill letter>:{key:04}`, and both the builder and the receiver compare its length
and the sum of all its bytes with what the key and the sender's fill letter say it must be. A slot handed to two
objects, or a span's slots read after the span was recycled, changes a sum; `wrong` counts them and must be 0. The
printed totals are functions of the rounds and the sizes alone, so they are the same at every interleaving.

⚠ **THIS IS A SAFETY NET FOR A LOCKING CHANGE, NOT A DISCRIMINATOR FOR ONE.** It is green whatever guards the
class lists: what it can catch is a refill or relist that races with another on a DIFFERENT class's list, or with the scavenger's walk (the next case). Whether a lock is taken at
all is not observable here.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias StringArray = Array with String
typealias ChurnHandleArray = Array with Churn.handle
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

let ServiceCount = 6
let Rounds = 120
let FramingBytes = 10
let FramingSum = 500
let FirstFillByte = 97
let ProbeObjects = 20

function digitSum(k Tally) returns Tally
	return (k / 1000) + ((k / 100) mod 10) + ((k / 10) mod 10) + (k mod 10)
end 'digitSum'

function holds(s String, key Tally, fillByte Tally, padLength Tally) returns bool
	if (s.byteLength() as Tally) != padLength + FramingBytes 'length'
		return false
	end 'length'

	var sum = 0 as Tally

	for b in s.bytes() 'sum'
		sum = sum + (b as Tally)
	end 'sum'

	return sum == FramingSum + (2 * digitSum(key)) + (fillByte * padLength)
end 'holds'

function makePad(fill String, length Tally) returns String
	let sb = StringBuilder.create()
	var left = length

	while left > 0 'build'
		sb.append(fill)
		left = left - 1
	end 'build'

	return sb.build()
end 'makePad'

type Churn
	var pad as String
	var padLength as Tally
	var fillByte as Tally
	var population as Tally
	var built as Tally
	var absorbed as Tally
	var wrong as Tally

	static function create(fill String, fillByte Tally, padLength Tally, population Tally) returns Self
		return Self{pad: makePad(fill, length: padLength), padLength: padLength, fillByte: fillByte, population: population, built: 0, absorbed: 0, wrong: 0}
	end 'create'

	export function round(next Churn.handle) returns Tally
		let keep = StringArray.create(0)
		var made = 0 as Tally

		for k in 0 upto self.population 'make'
			let s = "{k:04}:{self.pad}:{k:04}"

			if not holds(s, key: k, fillByte: self.fillByte, padLength: self.padLength) 'badBuild'
				self.wrong = self.wrong + 1
			end 'badBuild'

			made = made + (s.byteLength() as Tally)

			if (k mod 2) == 0 'handOver'
				try next.absorb(s, key: k, senderFill: self.fillByte, senderPad: self.padLength) otherwise panic("churn: the neighbour's mailbox holds a round's hand-overs")
			end 'handOver' else 'keepIt'
				keep.push(s)
			end 'keepIt'
		end 'make'

		self.built = self.built + made
		return made
	end 'round'

	export function absorb(s String, key Tally, senderFill Tally, senderPad Tally)
		self.absorbed = self.absorbed + (s.byteLength() as Tally)

		if not holds(s, key: key, fillByte: senderFill, padLength: senderPad) 'badAbsorb'
			self.wrong = self.wrong + 1
		end 'badAbsorb'
	end 'absorb'

	export function builtBytes() returns Tally
		return self.built
	end 'builtBytes'

	export function absorbedBytes() returns Tally
		return self.absorbed
	end 'absorbedBytes'

	export function wrongCount() returns Tally
		return self.wrong
	end 'wrongCount'
end 'Churn'

function classCount(sizeClass HeapBucketIndex) returns HeapCount
	return HeapStats.census(HeapCensusMode.bySizeClass).bucketCount(sizeClass)
end 'classCount'

function landsIn(fill String, length Tally, sizeClass HeapBucketIndex) returns bool
	let pad = makePad(fill, length: length)
	let held = StringArray.create(0)
	let before = classCount(sizeClass)

	for k in 0 upto ProbeObjects 'alloc'
		held.push("{k:04}:{pad}:{k:04}")
	end 'alloc'

	return classCount(sizeClass) >= before + (held.count() as HeapCount)
end 'landsIn'

function main() returns ExitCode
	let fills = StringArray from ["a", "b", "c", "d", "e", "f"]
	let pads = TallyArray from [3, 185, 1000, 3700, 9900, 30000]
	let populations = TallyArray from [1600, 800, 360, 120, 48, 16]
	let classes = TallyArray from [8, 19, 33, 44, 54, 67]

	let handles = ChurnHandleArray.create(0)

	for i in 0 upto ServiceCount 'spawnEach'
		let fill = try fills.get(i) otherwise panic("churn: fills.get OOB at {i}")
		let pad = try pads.get(i) otherwise panic("churn: pads.get OOB at {i}")
		let population = try populations.get(i) otherwise panic("churn: populations.get OOB at {i}")
		let sizeClass = try classes.get(i) otherwise panic("churn: classes.get OOB at {i}")

		if not landsIn(fill, length: pad, sizeClass: sizeClass as HeapBucketIndex) 'misplaced'
			return 4
		end 'misplaced'

		handles.push(spawn(capacity: 2048) Churn.create(fill, fillByte: (FirstFillByte + i) as Tally, padLength: pad, population: population))
	end 'spawnEach'

	var roundBytes = 0 as Tally

	for _ in 0 upto Rounds 'eachRound'
		let replies = TallyPromiseArray.create(0)

		for i in 0 upto ServiceCount 'sendEach'
			let mine = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
			let next = try handles.get((i + 1) mod ServiceCount) otherwise panic("churn: handles.get OOB at {i}")

			replies.push(try mine.round(next) otherwise panic("churn: a service's mailbox holds a round"))
		end 'sendEach'

		for p in replies 'join'
			roundBytes = roundBytes + (await p)
		end 'join'
	end 'eachRound'

	var built = 0 as Tally
	var absorbed = 0 as Tally
	var wrong = 0 as Tally

	for i in 0 upto ServiceCount 'collect'
		let h = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
		let b = try await h.builtBytes() otherwise 0
		let a = try await h.absorbedBytes() otherwise 0
		let w = try await h.wrongCount() otherwise 1

		print("service {i}: built={b} absorbed={a} wrong={w}\n")

		built = built + b
		absorbed = absorbed + a
		wrong = wrong + w
	end 'collect'

	print("total built={built} absorbed={absorbed} wrong={wrong} rounds={roundBytes}\n")

	if wrong != 0 'corrupted'
		return 3
	end 'corrupted'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
service 0: built=2496000 absorbed=28809600 wrong=0
service 1: built=18720000 absorbed=1248000 wrong=0
service 2: built=43632000 absorbed=9360000 wrong=0
service 3: built=53424000 absorbed=21816000 wrong=0
service 4: built=57081600 absorbed=26712000 wrong=0
service 5: built=57619200 absorbed=28540800 wrong=0
total built=232972800 absorbed=116486400 wrong=0 rounds=232972800
```

<!-- test: slab-sharding.the-same-churn-keeps-its-sums-while-another-service-scavenges -->
<!-- procs: 4 -->
**THE CHURN OF THE CASE ABOVE, WITH THE SCAVENGER RUNNING UNDER IT.** The same six services, the same six
size classes (8, 19, 33, 44, 54 and 67) and the same checks, plus a seventh service that calls
`HeapStats.scavenge()` three times per round and yields between the calls, so the scavenger walks every class's
list, destroys the spans the churn has just emptied and hands their chunks back to the arena WHILE the six
refill from those lists and from the same arena. A span the scavenger destroys must not be one a refill has just
taken, and a chunk it returns must not be one a refill is cutting a span from.

What is asserted is what the churn asserts: no object is corrupted (`wrong=0`), the totals are the rounds' and
the sizes' own, every scavenger pass ran (`sweeps=360`), and the program exits 0, which is the leak gate and the
allocator's own aborts. The number of bytes a scavenge released is NOT printed or asserted: it depends on what
the churn had emptied at that instant.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias StringArray = Array with String
typealias ChurnHandleArray = Array with Churn.handle
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

let ServiceCount = 6
let Rounds = 120
let FramingBytes = 10
let FramingSum = 500
let FirstFillByte = 97
let SweepsPerRound = 3

function digitSum(k Tally) returns Tally
	return (k / 1000) + ((k / 100) mod 10) + ((k / 10) mod 10) + (k mod 10)
end 'digitSum'

function holds(s String, key Tally, fillByte Tally, padLength Tally) returns bool
	if (s.byteLength() as Tally) != padLength + FramingBytes 'length'
		return false
	end 'length'

	var sum = 0 as Tally

	for b in s.bytes() 'sum'
		sum = sum + (b as Tally)
	end 'sum'

	return sum == FramingSum + (2 * digitSum(key)) + (fillByte * padLength)
end 'holds'

function makePad(fill String, length Tally) returns String
	let sb = StringBuilder.create()
	var left = length

	while left > 0 'build'
		sb.append(fill)
		left = left - 1
	end 'build'

	return sb.build()
end 'makePad'

type Churn
	var pad as String
	var padLength as Tally
	var fillByte as Tally
	var population as Tally
	var built as Tally
	var absorbed as Tally
	var wrong as Tally

	static function create(fill String, fillByte Tally, padLength Tally, population Tally) returns Self
		return Self{pad: makePad(fill, length: padLength), padLength: padLength, fillByte: fillByte, population: population, built: 0, absorbed: 0, wrong: 0}
	end 'create'

	export function round(next Churn.handle) returns Tally
		let keep = StringArray.create(0)
		var made = 0 as Tally

		for k in 0 upto self.population 'make'
			let s = "{k:04}:{self.pad}:{k:04}"

			if not holds(s, key: k, fillByte: self.fillByte, padLength: self.padLength) 'badBuild'
				self.wrong = self.wrong + 1
			end 'badBuild'

			made = made + (s.byteLength() as Tally)

			if (k mod 2) == 0 'handOver'
				try next.absorb(s, key: k, senderFill: self.fillByte, senderPad: self.padLength) otherwise panic("churn: the neighbour's mailbox holds a round's hand-overs")
			end 'handOver' else 'keepIt'
				keep.push(s)
			end 'keepIt'
		end 'make'

		self.built = self.built + made
		return made
	end 'round'

	export function absorb(s String, key Tally, senderFill Tally, senderPad Tally)
		self.absorbed = self.absorbed + (s.byteLength() as Tally)

		if not holds(s, key: key, fillByte: senderFill, padLength: senderPad) 'badAbsorb'
			self.wrong = self.wrong + 1
		end 'badAbsorb'
	end 'absorb'

	export function builtBytes() returns Tally
		return self.built
	end 'builtBytes'

	export function absorbedBytes() returns Tally
		return self.absorbed
	end 'absorbedBytes'

	export function wrongCount() returns Tally
		return self.wrong
	end 'wrongCount'
end 'Churn'

type Scavenger
	var passes as Tally

	static function create() returns Self
		return Self{passes: 0}
	end 'create'

	export function sweep(count Tally) returns Tally
		for _ in 0 upto count 'passes'
			_ = HeapStats.scavenge()
			self.passes = self.passes + 1
			Scheduler.yield()
		end 'passes'

		return self.passes
	end 'sweep'
end 'Scavenger'

function main() returns ExitCode
	let scavenger = spawn Scavenger.create()
	let fills = StringArray from ["a", "b", "c", "d", "e", "f"]
	let pads = TallyArray from [3, 185, 1000, 3700, 9900, 30000]
	let populations = TallyArray from [1600, 800, 360, 120, 48, 16]

	let handles = ChurnHandleArray.create(0)

	for i in 0 upto ServiceCount 'spawnEach'
		let fill = try fills.get(i) otherwise panic("churn: fills.get OOB at {i}")
		let pad = try pads.get(i) otherwise panic("churn: pads.get OOB at {i}")
		let population = try populations.get(i) otherwise panic("churn: populations.get OOB at {i}")

		handles.push(spawn(capacity: 2048) Churn.create(fill, fillByte: (FirstFillByte + i) as Tally, padLength: pad, population: population))
	end 'spawnEach'

	var roundBytes = 0 as Tally
	var sweeps = 0 as Tally

	for _ in 0 upto Rounds 'eachRound'
		let replies = TallyPromiseArray.create(0)
		let sweeping = try scavenger.sweep(SweepsPerRound) otherwise panic("churn: the scavenger's mailbox holds a round")

		for i in 0 upto ServiceCount 'sendEach'
			let mine = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
			let next = try handles.get((i + 1) mod ServiceCount) otherwise panic("churn: handles.get OOB at {i}")

			replies.push(try mine.round(next) otherwise panic("churn: a service's mailbox holds a round"))
		end 'sendEach'

		for p in replies 'join'
			roundBytes = roundBytes + (await p)
		end 'join'

		sweeps = await sweeping
	end 'eachRound'

	var built = 0 as Tally
	var absorbed = 0 as Tally
	var wrong = 0 as Tally

	for i in 0 upto ServiceCount 'collect'
		let h = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
		let b = try await h.builtBytes() otherwise 0
		let a = try await h.absorbedBytes() otherwise 0
		let w = try await h.wrongCount() otherwise 1

		print("service {i}: built={b} absorbed={a} wrong={w}\n")

		built = built + b
		absorbed = absorbed + a
		wrong = wrong + w
	end 'collect'

	print("total built={built} absorbed={absorbed} wrong={wrong} rounds={roundBytes} sweeps={sweeps}\n")

	if wrong != 0 'corrupted'
		return 3
	end 'corrupted'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
service 0: built=2496000 absorbed=28809600 wrong=0
service 1: built=18720000 absorbed=1248000 wrong=0
service 2: built=43632000 absorbed=9360000 wrong=0
service 3: built=53424000 absorbed=21816000 wrong=0
service 4: built=57081600 absorbed=26712000 wrong=0
service 5: built=57619200 absorbed=28540800 wrong=0
total built=232972800 absorbed=116486400 wrong=0 rounds=232972800 sweeps=360
```

<!-- test: slab-sharding.services-churning-one-size-class-keep-their-sums -->
<!-- procs: 4 -->
**THE CONTROL FOR SAME-LIST CONTENTION: SIX SERVICES, ONE SIZE CLASS.** The ring and the checks of the first
case, with every service building 1000-byte pads: all six allocate from class 33 (1152-byte slots, 7 to a
span, so 360 objects fill 52 spans per round and each service refills over and over), all six free into the
same class's span list, and every relist and every refill of every service meets another's on one list. The
fill letters still differ per service, so a slot handed to two services is a corrupted sum.

⚠ **THE EXISTING EIGHT-SERVICES CASE OF `inline-allocation-under-preemption` IS NOT THIS CASE.** Its arrays
come from the inline hit path and a few spans last the whole run; this population is far larger than the spans a
processor holds cached, so the work is in the refill and relist roads on one class's list. The
program asks the census, before it starts, that the pads do land in class 33 (exit 4 otherwise).
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias StringArray = Array with String
typealias ChurnHandleArray = Array with Churn.handle
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

let ServiceCount = 6
let Rounds = 120
let FramingBytes = 10
let FramingSum = 500
let FirstFillByte = 97
let ProbeObjects = 20
let SharedClass = 33

function digitSum(k Tally) returns Tally
	return (k / 1000) + ((k / 100) mod 10) + ((k / 10) mod 10) + (k mod 10)
end 'digitSum'

function holds(s String, key Tally, fillByte Tally, padLength Tally) returns bool
	if (s.byteLength() as Tally) != padLength + FramingBytes 'length'
		return false
	end 'length'

	var sum = 0 as Tally

	for b in s.bytes() 'sum'
		sum = sum + (b as Tally)
	end 'sum'

	return sum == FramingSum + (2 * digitSum(key)) + (fillByte * padLength)
end 'holds'

function makePad(fill String, length Tally) returns String
	let sb = StringBuilder.create()
	var left = length

	while left > 0 'build'
		sb.append(fill)
		left = left - 1
	end 'build'

	return sb.build()
end 'makePad'

type Churn
	var pad as String
	var padLength as Tally
	var fillByte as Tally
	var population as Tally
	var built as Tally
	var absorbed as Tally
	var wrong as Tally

	static function create(fill String, fillByte Tally, padLength Tally, population Tally) returns Self
		return Self{pad: makePad(fill, length: padLength), padLength: padLength, fillByte: fillByte, population: population, built: 0, absorbed: 0, wrong: 0}
	end 'create'

	export function round(next Churn.handle) returns Tally
		let keep = StringArray.create(0)
		var made = 0 as Tally

		for k in 0 upto self.population 'make'
			let s = "{k:04}:{self.pad}:{k:04}"

			if not holds(s, key: k, fillByte: self.fillByte, padLength: self.padLength) 'badBuild'
				self.wrong = self.wrong + 1
			end 'badBuild'

			made = made + (s.byteLength() as Tally)

			if (k mod 2) == 0 'handOver'
				try next.absorb(s, key: k, senderFill: self.fillByte, senderPad: self.padLength) otherwise panic("churn: the neighbour's mailbox holds a round's hand-overs")
			end 'handOver' else 'keepIt'
				keep.push(s)
			end 'keepIt'
		end 'make'

		self.built = self.built + made
		return made
	end 'round'

	export function absorb(s String, key Tally, senderFill Tally, senderPad Tally)
		self.absorbed = self.absorbed + (s.byteLength() as Tally)

		if not holds(s, key: key, fillByte: senderFill, padLength: senderPad) 'badAbsorb'
			self.wrong = self.wrong + 1
		end 'badAbsorb'
	end 'absorb'

	export function builtBytes() returns Tally
		return self.built
	end 'builtBytes'

	export function absorbedBytes() returns Tally
		return self.absorbed
	end 'absorbedBytes'

	export function wrongCount() returns Tally
		return self.wrong
	end 'wrongCount'
end 'Churn'

function classCount(sizeClass HeapBucketIndex) returns HeapCount
	return HeapStats.census(HeapCensusMode.bySizeClass).bucketCount(sizeClass)
end 'classCount'

function landsIn(fill String, length Tally, sizeClass HeapBucketIndex) returns bool
	let pad = makePad(fill, length: length)
	let held = StringArray.create(0)
	let before = classCount(sizeClass)

	for k in 0 upto ProbeObjects 'alloc'
		held.push("{k:04}:{pad}:{k:04}")
	end 'alloc'

	return classCount(sizeClass) >= before + (held.count() as HeapCount)
end 'landsIn'

function main() returns ExitCode
	let fills = StringArray from ["a", "b", "c", "d", "e", "f"]
	let pads = TallyArray from [1000, 1000, 1000, 1000, 1000, 1000]
	let populations = TallyArray from [360, 360, 360, 360, 360, 360]

	let handles = ChurnHandleArray.create(0)

	for i in 0 upto ServiceCount 'spawnEach'
		let fill = try fills.get(i) otherwise panic("churn: fills.get OOB at {i}")
		let pad = try pads.get(i) otherwise panic("churn: pads.get OOB at {i}")
		let population = try populations.get(i) otherwise panic("churn: populations.get OOB at {i}")

		if not landsIn(fill, length: pad, sizeClass: SharedClass) 'misplaced'
			return 4
		end 'misplaced'

		handles.push(spawn(capacity: 2048) Churn.create(fill, fillByte: (FirstFillByte + i) as Tally, padLength: pad, population: population))
	end 'spawnEach'

	var roundBytes = 0 as Tally

	for _ in 0 upto Rounds 'eachRound'
		let replies = TallyPromiseArray.create(0)

		for i in 0 upto ServiceCount 'sendEach'
			let mine = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
			let next = try handles.get((i + 1) mod ServiceCount) otherwise panic("churn: handles.get OOB at {i}")

			replies.push(try mine.round(next) otherwise panic("churn: a service's mailbox holds a round"))
		end 'sendEach'

		for p in replies 'join'
			roundBytes = roundBytes + (await p)
		end 'join'
	end 'eachRound'

	var built = 0 as Tally
	var absorbed = 0 as Tally
	var wrong = 0 as Tally

	for i in 0 upto ServiceCount 'collect'
		let h = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
		let b = try await h.builtBytes() otherwise 0
		let a = try await h.absorbedBytes() otherwise 0
		let w = try await h.wrongCount() otherwise 1

		print("service {i}: built={b} absorbed={a} wrong={w}\n")

		built = built + b
		absorbed = absorbed + a
		wrong = wrong + w
	end 'collect'

	print("total built={built} absorbed={absorbed} wrong={wrong} rounds={roundBytes}\n")

	if wrong != 0 'corrupted'
		return 3
	end 'corrupted'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
service 0: built=43632000 absorbed=21816000 wrong=0
service 1: built=43632000 absorbed=21816000 wrong=0
service 2: built=43632000 absorbed=21816000 wrong=0
service 3: built=43632000 absorbed=21816000 wrong=0
service 4: built=43632000 absorbed=21816000 wrong=0
service 5: built=43632000 absorbed=21816000 wrong=0
total built=261792000 absorbed=130896000 wrong=0 rounds=261792000
```

<!-- test: slab-sharding.services-churning-one-size-class-keep-their-sums-while-another-service-scavenges -->
<!-- procs: 4 -->
**THE TWO CASES ABOVE, JOINED: SIX SERVICES ON ONE SIZE CLASS, A SEVENTH SCAVENGING UNDER THEM.** The ring, the
pads and the checks of the one-class case (all six build 1000-byte pads in class 33, each hands every second
string to the next service to drop), plus the scavenger of the case before it, which calls
`HeapStats.scavenge()` three times per round and yields between the calls. Every refill, every relist and every
scavenge of the one class's span list meets another's. It is a safety net: it is green whether or not a lock
guards that list, and a race on it shows as a failure rate: a corrupted sum (`wrong` above 0), an allocator
abort or a leak at exit (the exit code is pinned to 0). The census check that the pads land in class 33 runs
first (exit 4 otherwise), and `sweeps=360` says every scavenger pass ran.

⭐ **A LOST SPAN IS CAUGHT AFTER THE JOIN.** Once every service has answered its last message the heap is
quiescent, so the main thread scavenges twice and reads `HeapStats.slab().parked`, the bytes of spans that
hold no live slot yet sit on no processor's cache. A healthy allocator has destroyed every such span on a
class's list by then, so the figure is exactly 0 whichever processor ran which service; a span the lock-free
list dropped, with every slot free, is one no pass can reach and reads as parked (exit 5).
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias StringArray = Array with String
typealias ChurnHandleArray = Array with Churn.handle
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

let ServiceCount = 6
let Rounds = 120
let FramingBytes = 10
let FramingSum = 500
let FirstFillByte = 97
let ProbeObjects = 20
let SharedClass = 33
let SweepsPerRound = 3
let SettlingPasses = 2

function digitSum(k Tally) returns Tally
	return (k / 1000) + ((k / 100) mod 10) + ((k / 10) mod 10) + (k mod 10)
end 'digitSum'

function holds(s String, key Tally, fillByte Tally, padLength Tally) returns bool
	if (s.byteLength() as Tally) != padLength + FramingBytes 'length'
		return false
	end 'length'

	var sum = 0 as Tally

	for b in s.bytes() 'sum'
		sum = sum + (b as Tally)
	end 'sum'

	return sum == FramingSum + (2 * digitSum(key)) + (fillByte * padLength)
end 'holds'

function makePad(fill String, length Tally) returns String
	let sb = StringBuilder.create()
	var left = length

	while left > 0 'build'
		sb.append(fill)
		left = left - 1
	end 'build'

	return sb.build()
end 'makePad'

type Churn
	var pad as String
	var padLength as Tally
	var fillByte as Tally
	var population as Tally
	var built as Tally
	var absorbed as Tally
	var wrong as Tally

	static function create(fill String, fillByte Tally, padLength Tally, population Tally) returns Self
		return Self{pad: makePad(fill, length: padLength), padLength: padLength, fillByte: fillByte, population: population, built: 0, absorbed: 0, wrong: 0}
	end 'create'

	export function round(next Churn.handle) returns Tally
		let keep = StringArray.create(0)
		var made = 0 as Tally

		for k in 0 upto self.population 'make'
			let s = "{k:04}:{self.pad}:{k:04}"

			if not holds(s, key: k, fillByte: self.fillByte, padLength: self.padLength) 'badBuild'
				self.wrong = self.wrong + 1
			end 'badBuild'

			made = made + (s.byteLength() as Tally)

			if (k mod 2) == 0 'handOver'
				try next.absorb(s, key: k, senderFill: self.fillByte, senderPad: self.padLength) otherwise panic("churn: the neighbour's mailbox holds a round's hand-overs")
			end 'handOver' else 'keepIt'
				keep.push(s)
			end 'keepIt'
		end 'make'

		self.built = self.built + made
		return made
	end 'round'

	export function absorb(s String, key Tally, senderFill Tally, senderPad Tally)
		self.absorbed = self.absorbed + (s.byteLength() as Tally)

		if not holds(s, key: key, fillByte: senderFill, padLength: senderPad) 'badAbsorb'
			self.wrong = self.wrong + 1
		end 'badAbsorb'
	end 'absorb'

	export function builtBytes() returns Tally
		return self.built
	end 'builtBytes'

	export function absorbedBytes() returns Tally
		return self.absorbed
	end 'absorbedBytes'

	export function wrongCount() returns Tally
		return self.wrong
	end 'wrongCount'
end 'Churn'

type Scavenger
	var passes as Tally

	static function create() returns Self
		return Self{passes: 0}
	end 'create'

	export function sweep(count Tally) returns Tally
		for _ in 0 upto count 'passes'
			_ = HeapStats.scavenge()
			self.passes = self.passes + 1
			Scheduler.yield()
		end 'passes'

		return self.passes
	end 'sweep'
end 'Scavenger'

function parkedBytes() returns HeapBytes
	return HeapStats.slab().parked
end 'parkedBytes'

function classCount(sizeClass HeapBucketIndex) returns HeapCount
	return HeapStats.census(HeapCensusMode.bySizeClass).bucketCount(sizeClass)
end 'classCount'

function landsIn(fill String, length Tally, sizeClass HeapBucketIndex) returns bool
	let pad = makePad(fill, length: length)
	let held = StringArray.create(0)
	let before = classCount(sizeClass)

	for k in 0 upto ProbeObjects 'alloc'
		held.push("{k:04}:{pad}:{k:04}")
	end 'alloc'

	return classCount(sizeClass) >= before + (held.count() as HeapCount)
end 'landsIn'

function main() returns ExitCode
	let fills = StringArray from ["a", "b", "c", "d", "e", "f"]
	let pads = TallyArray from [1000, 1000, 1000, 1000, 1000, 1000]
	let populations = TallyArray from [360, 360, 360, 360, 360, 360]

	let handles = ChurnHandleArray.create(0)

	for i in 0 upto ServiceCount 'spawnEach'
		let fill = try fills.get(i) otherwise panic("churn: fills.get OOB at {i}")
		let pad = try pads.get(i) otherwise panic("churn: pads.get OOB at {i}")
		let population = try populations.get(i) otherwise panic("churn: populations.get OOB at {i}")

		if not landsIn(fill, length: pad, sizeClass: SharedClass) 'misplaced'
			return 4
		end 'misplaced'

		handles.push(spawn(capacity: 2048) Churn.create(fill, fillByte: (FirstFillByte + i) as Tally, padLength: pad, population: population))
	end 'spawnEach'

	let scavenger = spawn Scavenger.create()

	var roundBytes = 0 as Tally
	var sweeps = 0 as Tally

	for _ in 0 upto Rounds 'eachRound'
		let replies = TallyPromiseArray.create(0)
		let sweeping = try scavenger.sweep(SweepsPerRound) otherwise panic("churn: the scavenger's mailbox holds a round")

		for i in 0 upto ServiceCount 'sendEach'
			let mine = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
			let next = try handles.get((i + 1) mod ServiceCount) otherwise panic("churn: handles.get OOB at {i}")

			replies.push(try mine.round(next) otherwise panic("churn: a service's mailbox holds a round"))
		end 'sendEach'

		for p in replies 'join'
			roundBytes = roundBytes + (await p)
		end 'join'

		sweeps = await sweeping
	end 'eachRound'

	var built = 0 as Tally
	var absorbed = 0 as Tally
	var wrong = 0 as Tally

	for i in 0 upto ServiceCount 'collect'
		let h = try handles.get(i) otherwise panic("churn: handles.get OOB at {i}")
		let b = try await h.builtBytes() otherwise 0
		let a = try await h.absorbedBytes() otherwise 0
		let w = try await h.wrongCount() otherwise 1

		print("service {i}: built={b} absorbed={a} wrong={w}\n")

		built = built + b
		absorbed = absorbed + a
		wrong = wrong + w
	end 'collect'

	print("total built={built} absorbed={absorbed} wrong={wrong} rounds={roundBytes} sweeps={sweeps}\n")

	if wrong != 0 'corrupted'
		return 3
	end 'corrupted'

	for _ in 0 upto SettlingPasses 'settle'
		_ = HeapStats.scavenge()
	end 'settle'

	if parkedBytes() != 0 'spanLost'
		return 5
	end 'spanLost'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
service 0: built=43632000 absorbed=21816000 wrong=0
service 1: built=43632000 absorbed=21816000 wrong=0
service 2: built=43632000 absorbed=21816000 wrong=0
service 3: built=43632000 absorbed=21816000 wrong=0
service 4: built=43632000 absorbed=21816000 wrong=0
service 5: built=43632000 absorbed=21816000 wrong=0
total built=261792000 absorbed=130896000 wrong=0 rounds=261792000 sweeps=360
```
