---
feature: slab-sharding
status: stable
keywords: [allocator, slab, sharding, processor, mcache, remote-free, ownership, concurrency, runtime]
category: system
---

# The sharded slab — per-processor mcache rows, span ownership, and the remote-free queue

## Documentation

Since S5 the allocator is **per-P sharded**. Each processor (`SchedRuntime`'s P) owns its own mcache
ROW and is the sole writer of every span cached there, so the allocation fast path takes no lock. Three
mechanisms make that safe, and each of them is reachable — in part — from an ordinary single-processor
program, which is what the cases below drive:

| Mechanism | What it decides |
|---|---|
| **the shard row** | the P read (`emitSlabCurrentP`, emitted inline into `__slab_alloc`/`__slab_free` since EC8) answers the running P; its clamped `id` is the mcache row, and an OS thread that owns NO P gets the dedicated RAW row (255), never row 0 |
| **the ownership stamp** | every span carries the P that owns it; a cached span owned by somebody else is a MISS, not something to pop |
| **the remote-free queue** | a free by a P that does not own the slot's span CAS-pushes it onto the OWNER's Treiber stack, and the owner replays the chain on its next allocation slow path |

⛔⛔ **THIS PARAGRAPH SAID A SPEC CASE COULD NOT SET `MAXON_MAX_PROCS`, AND THAT HAS EXPIRED** — the harness
gained a per-case processor marker (`specs-shv2/sched-default-procs.md` owns it), and the default is now the
machine's processor count rather than 1, so an unmarked case is already multi-processor on any ordinary
host. The last case in this file uses that marker to pin ONE processor, which is a deliberate choice and not
the old limitation.

⚠ **BUT THE SENTENCE'S CONCLUSION SURVIVES ITS REASON, AND THAT IS THE USEFUL PART.** Nothing here tests two
processors, because a committed case cannot: the multi-M properties need the OS to actually run a second
machine while twelve harness workers compete for the box, and it sometimes does not. MEASURED, twice, on
this tree — a multi-processor version of the last case failed **1 run in 6** at eight messages, and failed
1 in 6 again in a version that WAITED for the property across up to 500 waves. `pin-matrix.sh:100-137` had
already hit the same wall from the other side.

⚠ **AND THE OLD SENTENCE NAMED THE WRONG GATE.** It called `track0/alloc-torture.maxon` "the
multi-processor gate", which EC10 ended: its work is `async`, an `async` frame is a coroutine of its caller,
and that program now reads `workers=1` at every count — a fact this file's own later paragraphs already
record. The multi-processor readings come from the SPAWN family (`service-torture`, `service-fanin-torture`)
driven by `track0/pin-matrix.sh`, which runs standalone and sweeps the count.

What a ONE-processor program CAN reach, and what each case below is for:

* **the P-owned row.** A green-thread program runs its main thread as P[0], so every allocation a green
  thread makes goes through row 0 with a real owner stamped on the span — the refill, the eviction of a
  drained span and the return of an emptied one all run against `owning_p` rather than against the
  degenerate constant they used before S5.
* **the RAW row and its lock.** Everything a program allocates *before* its first `async` is allocated
  with no P at all, so those spans are owned by nobody and live on the raw row. Freeing one of them
  AFTER the scheduler exists takes the serialised path — the one arm of the lock an ordinary
  single-threaded program reaches, and it reaches it on **every** green-thread program.
* **the parked sentinel.** A span whose last slot comes back is parked on mcentral with its owner
  cleared to a sentinel that is neither a P nor "no P"; the next refill re-stamps it. A free that
  observed that sentinel would be a double free, and the runtime aborts (`slabFreeOfParkedSpan`, exit
  89) rather than pushing onto a span that is already fully free.

⚠ **WHAT IS NOT REACHABLE FROM HERE, STATED SO NOBODY READS SILENCE AS COVERAGE**: the ownership gate
actually REJECTING a span, two threads contending for the raw row, and — since EC8 — whether the traffic
counters are stepped ATOMICALLY. Those need a second OS thread crediting the same word, and were verified
by measurement instead — see `track0/`.

⭐ **THE REMOTE-FREE TREIBER PUSH IS NOW COUNTABLE, WHICH IS NOT THE SAME AS BEING PINNED HERE.**
`__Builtins.slabRemoteFreeCount()` sums a per-P counter, so the road that `service-torture` and
`service-fanin-torture` have driven since SV1 finally moves a number instead of being believed. ⚠ **The
non-zero reading is `track0`'s and not this file's** — see the last case for the measurement that says why.
What this file pins is the counter's other edge: at one processor it must be exactly **0**, because a
counter that answers non-zero where no free can cross is counting the wrong frees.

⚠ **EVERY CASE HERE CARRIES A `targets:` MARKER, AND THAT IS A PROPERTY OF THE SUBJECT RATHER THAN A
CONVENIENCE.** All are green-thread programs, because sharding is only compiled into a program that has a
scheduler (`usesGt`) — and a P exists only where one has been built. ⛔ This paragraph said *"on exactly one
lane"* and named the tier below as agreeing: *"`StdToWasm` REFUSES `tlsSlotLoad` and the lock trio at
emission, and `StdToArm64Conversion` has no case for either"*. The second half stopped being true when the
arm64-macOS scheduler landed: that isel now lowers `tlsSlotLoad` as a thread-pointer read with no call at
all, and the lock trio as `pthread_mutex_*`. wasm still refuses all four, and both Linux lanes have no libc
to build any of it on. `sched-processor.md` carries the identical restriction for the identical reason.

## Tests

<!-- test: slab-sharding.a-green-thread-program-allocates-through-its-processor-row -->
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
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
	var acc = 0
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
	var churns = IntPromiseArray.create()
	for i in 0 upto 40 'spawnChurn'
		churns.push(async churn(i))
	end 'spawnChurn'

	var kept = PromiseArray.create()
	for k in 0 upto 24 'spawnKeep'
		kept.push(async survivor(k))
	end 'spawnKeep'

	var total = 0
	for c in churns 'awaitChurn'
		total = total + await c
	end 'awaitChurn'

	if total != 2400 'churnLost'
		return 1
	end 'churnLost'

	var strings = StringArray.create()
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
**THE RAW ROW, AND THE ONE ARM OF THE LOCK A SINGLE-THREADED PROGRAM REACHES.** Everything allocated
before the first `async` is allocated with NO processor: the P read answers "none", the spans
land on the dedicated raw row and are stamped as owned by nobody. Once the scheduler exists those same
spans are shared, in principle, by every OS thread that owns no P — so a free of one of their slots
takes the serialised path.

⭐ **THE ORDER IS THE WHOLE CASE.** The pre-scheduler population is built FIRST, is still live when the
scheduler starts, and is only released afterwards. That is what puts a whole population of NO-OWNER spans
in front of a running scheduler, which no other case here does.

⚠ **WHAT IT CATCHES AND WHAT IT ONLY EXERCISES, because the difference is not visible from a green
result.** It CATCHES a refill that fails to establish a span's owner (MEASURED: with the owner stamp
removed from `__slab_refill`, this case exits 89). It only EXERCISES the serialised arm of the lock: with
one OS thread there is no second writer to race, so a build that took no lock at all would still pass
here. That half is verified by measurement, not by this case.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)
typealias Integer = int(i64.min to i64.max)

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(48)
	for i in 0 upto 48 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function work() returns Integer
	var acc = 0
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
	var early = Bufs.create()
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
	var bad = 0
	for (iter, b) in early.withIterator() 'check'
		let seed = iter.index()
		if b.count() != 48 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 48 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((seed + i) mod 251) 'value'
				bad = bad + 1
			end 'value'
		end 'bytes'
	end 'check'

	early = Bufs.create()

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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
**THE PARKED SENTINEL, ROUND-TRIPPED.** A span whose last slot comes back is unlinked from its owner's
mcache row, parked on mcentral and stamped with an owner that is neither a processor nor "no
processor". The next refill takes it back and re-stamps it. This case empties a class's spans wholesale
and then re-fills the same class, twice over, so every span in it makes that round trip several times.

⛔ **THE FAILURE IS AN ABORT, NOT A WRONG ANSWER.** A free that reached a span still carrying the parked
sentinel would be a double free of a span already fully returned, and `__slab_free` exits **89** rather
than pushing onto it — so a refill that forgot to re-stamp the owner cannot pass this case by accident.
A refill that stamped the WRONG owner is caught by the survivors instead.

⚠⚠ **THE `async` IS LOAD-BEARING AND IS NOT DECORATION — WITHOUT IT THIS CASE TESTS NOTHING IT CLAIMS
TO.** Sharding is a BUILD-TIME argument keyed on `usesGt` (`SlabRuntime`'s header), so a program with no
green thread carries the UNSHARDED allocator, which never reads `owning_p` at all. MEASURED: written
without the `async`, this case passed against a compiler whose refill had been stripped of its owner
stamp entirely; with it, the same sabotage makes it exit 89.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Seed = int(0 to 65536)

function build(seed Seed) returns ByteArray
	var b = ByteArray.create()
	b.reserve(80)
	for i in 0 upto 80 'fill'
		b.push(((seed + i) mod 251) as Byte)
	end 'fill'
	return b
end 'build'

function cycle(rounds Seed) returns Seed
	var seen = 0 as Seed
	for r in 0 upto rounds 'round'
		var wave = Bufs.create()
		for k in 0 upto 4000 'alloc'
			wave.push(build(((r * 4000) + k) as Seed))
		end 'alloc'
		seen = (seen + wave.count()) as Seed
		// Dropping the whole wave empties every span of this class: each one parks on mcentral with
		// its owner cleared, and the next round's allocations must take them back and re-own them.
		wave = Bufs.create()
	end 'round'
	return seen
end 'cycle'

function main() returns ExitCode
	// The whole cycle runs inside a green thread, so this program carries the SHARDED allocator and its
	// spans are parked and re-owned against a real processor.
	let p = async cycle(3 as Seed)

	if await p != 12000 'lostBuffers'
		return 1
	end 'lostBuffers'

	// The survivors are cut from spans that have each been parked and re-owned several times over.
	var keep = Bufs.create()
	for k in 0 upto 200 'survivors'
		keep.push(build((k + 900) as Seed))
	end 'survivors'

	var bad = 0
	for (iter, b) in keep.withIterator() 'check'
		let seed = iter.index() + 900
		if b.count() != 80 'length'
			bad = bad + 1
		end 'length'
		for i in 0 upto 80 'bytes'
			let got = try b.get(i) otherwise return 2
			if got != ((seed + i) mod 251) 'value'
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
**THE OWNER STAMP AND THE SCAVENGER, TOGETHER.** The scavenger destroys spans off the mcentral lists,
and since S5 every one of those carries the parked sentinel in the field the mcache eviction is derived
from. This is the two-pass grace case run through a GREEN THREAD, so the spans it empties were owned by
a real processor before they parked — the combination the single-threaded scavenger cases cannot make.

⚠ Two calls, because the grace guard releases nothing on the first: that is `slab-scavenger`'s rule, and
it is restated here only to say that sharding did not change it.

⚠ **THIS ONE IS COVERAGE, NOT A DISCRIMINATOR, AND SAYING SO IS THE POINT.** MEASURED against a compiler
whose refill had been stripped of its owner stamp, the three cases above exit 89 and this one still PASSES
— its population is large enough that almost every span it touches is freshly CUT rather than taken back
off mcentral, and a freshly cut span's header is zeroed, which reads as a valid "no owner". It earns its
place by driving the scavenger over spans a processor owned; it does not stand in for the cases above.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Bufs = Array with ByteArray
typealias Integer = int(i64.min to i64.max)

function fill() returns Integer
	var bufs = Bufs.create()
	for k in 0 upto 16000 'alloc'
		var b = ByteArray.create()
		b.reserve(96)
		for i in 0 upto 96 'bytes'
			b.push(((k + i) mod 251) as Byte)
		end 'bytes'
		bufs.push(b)
	end 'alloc'
	let n = bufs.count()
	bufs = Bufs.create()
	return n as Integer
end 'fill'

function main() returns ExitCode
	let p = async fill()
	if await p != 16000 'lost'
		return 1
	end 'lost'

	let first = __Builtins.scavengeMemory()
	if first != 0 'graceSkipped'
		return 2
	end 'graceSkipped'

	let second = __Builtins.scavengeMemory()
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
**THE COUNTERS, READ THROUGH THE SHARDED ALLOCATOR.** Every traffic column an emitted program keeps is
stepped inside `__mm_alloc`/`__mm_free`, which since EC8 reach their slot through a `__slab_alloc` that
carries the class lookup, the processor read, the shard row, the state region and the pop in ONE body
— and a SHARDED build's copy of that body is a different emission from the single-threaded one (it
carries the ownership gate, the remote drain and the lock arm). This drives two waves of green-thread
allocations through it and asks the two tracked columns the only questions that are EXACT: the LIVE
count comes back to the number it started at, and two identical waves credit the CUMULATIVE column by
the identical amount.

⚠ The warm-up wave is load-bearing: the first `async` in a process brings the scheduler up, and
bring-up allocates. Measuring from after it is what makes `first == second` an equality rather than an
inequality with a fudge factor.

⚠⚠ **WHAT THIS DOES NOT PIN, BECAUSE THE HARNESS CANNOT: THE `lock` PREFIX.** `emitGlobalAccumulate`
emits an `atomicRmw` when the program has green threads and a plain load/add/store when it does not, and
telling those two apart needs a second M crediting the same column at the same instant — which needs
more than one processor. ⛔ This used to add *"which a spec case cannot set"*, and that expired when the
per-case processor marker landed: a case CAN ask for four now, and the case at the end of this file does.
At one processor the plain form
is exact too, so this case passes either way and does not claim otherwise. That half is measured with
`track0/alloc-torture.maxon` across `MAXON_MAX_PROCS ∈ {1, 2, 4, 12}`, where the leak gate (exit 101) IS
the lost-update oracle — EC8 measured it clean with the atomic and exit 101 at 2, 4 and 12 with the
atomic forced off, which is the positive control this case cannot be.

⛔ **THAT MEASUREMENT STANDS AS HISTORY AND CANNOT BE RE-TAKEN ON THIS TREE (EC10).** `alloc-torture`
reached a second M by spawning `async` tasks the scheduler handed to worker Ms; since `async` became a
coroutine of its calling green thread there is no worker M to hand them to, so the program runs entirely
on one M and `workers=1` at every `MAXON_MAX_PROCS`. It still proves determinism and leak-freedom; it no
longer discriminates the `lock` prefix. ⚠ **This does NOT mean the counters went plain** — `emitGlobalAccumulate`
keeps its `multiM` arm, and a `.data` word is reachable from the IOCP completion thread whatever `async`
does. It means the ORACLE for that arm is waiting on `spawn`, which is where a second M comes back.

⚠⚠ **`spawn` HAS LANDED, AND THIS DEBT IS THEREFORE DISCHARGEABLE AND NOT DISCHARGED — SAID PLAINLY SO IT
IS NOT READ AS PAID.** The condition the paragraph above names as missing is available today: a spawned
program at four processors really does put two Ms on one column. What the case at the end of this file adds
is NOT that — it observes that the remote-free ROAD IS TAKEN (`slabRemoteFreeCount() > 0`), which is a
different subject from whether a contended column loses an update. ⇒ **the `lock`-prefix oracle is still
owed**, and what it needs is this case's two waves driven by `spawn` rather than `async`, with the atomic
forced off as the positive control that `alloc-torture` used to be. Whoever writes it should read EC8's
measurement first: exit 101 at 2, 4 and 12 with the atomic off, clean with it on.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Count = int(0 to 65536)

function churn(seed Count) returns Integer
	var acc = 0
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

	let liveBefore = __Builtins.mmAllocLive()
	let totalBefore = __Builtins.mmAllocTotal()

	if wave(10) != 800 'firstLost'
		return 2
	end 'firstLost'
	let totalMid = __Builtins.mmAllocTotal()

	if wave(20) != 800 'secondLost'
		return 3
	end 'secondLost'
	let totalAfter = __Builtins.mmAllocTotal()
	let liveAfter = __Builtins.mmAllocLive()

	let first = totalMid - totalBefore
	let second = totalAfter - totalMid

	var score = 0
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
<!-- targets: x64-windows, arm64-macos, arm64-linux, x64-linux -->
<!-- procs: 1 -->
⭐ **`__Builtins.slabRemoteFreeCount()` EXISTS BECAUSE THE CROSS-P FREE HAD PRODUCERS AND NO OBSERVER.** A
box `main` allocates and a `spawn`ed service drops is released by whichever machine ran that receiver —
`SlabRuntime`'s remote-free road, a CAS push onto the owning P's Treiber stack. `service-torture` and
`service-fanin-torture` drive thousands of those, and `track0/README.md` said what that was worth: the road
was **exercised but not observed**. This counter is the observation; it is per-P and summed like
`schedStealCount()`, so it costs no `.data` word and no golden churn.

⭐⭐ **WHAT THIS CASE PINS IS THE HALF THAT IS DETERMINISTIC: AT ONE PROCESSOR THE ANSWER IS EXACTLY ZERO.**
Every free in a one-P program is local by construction, so a counter that ever answers non-zero here is
counting the wrong frees — which is a real defect and the one this case exists to catch. It runs the same
spawn-and-move traffic the multi-processor reading uses, so the boxes genuinely travel between green
threads; what they cannot do at one processor is travel between MACHINES.

⛔⛔ **THE OTHER HALF — "a remote free ACTUALLY HAPPENS" — IS NOT A SPEC CASE, AND THAT WAS MEASURED RATHER
THAN ASSUMED.** Two versions were written and both were flaky under suite load: eight messages failed 1 run
in 6, and so did a version that **waited** for the property, sending up to 500 waves and stopping the instant
a box crossed. The reason is structural, not a matter of scale: `main` sends and then awaits, so main's own
driver runs the receivers INLINE on main's machine unless a worker M steals one first — and whether the OS
schedules that worker while twelve harness workers are competing is not something the program decides.
`pin-matrix.sh:100-137` had already measured the same wall from the other side: at total CPU saturation
*both* 400 and 4,000 rounds failed 40 of 40, and *"no program-side change can fix that"*.

⇒ **A committed case that needs the OS to co-operate is a flake, so the multi-M reading lives in `track0/`**,
which runs standalone rather than under a twelve-way load, sweeps the processor count, and already asserts
the same class of property (`steals > 0` at every effective N ≥ 2) for the spawn family. ⚠ **This is a
statement about where the reading belongs, not a retreat from taking it** — the counter is read there, and a
green run of this case plus a green `pin-matrix.sh` row is the pair.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SinkHandleArray = Array with Sink.handle
typealias CountPromise = Promise with (Integer, ServiceError)
typealias CountPromiseArray = Array with CountPromise

let sinkCount = 8
let waves = 40

type Sink
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(s String) returns Integer
		self.seen = self.seen + s.count()

		return 1
	end 'take'
end 'Sink'

function main() returns ExitCode
	var sinks = SinkHandleArray.create()
	var i = 0
	while i < sinkCount 'spawnEach'
		sinks.push(spawn Sink.create())
		i = i + 1
	end 'spawnEach'

	var delivered = 0
	var wave = 0
	while wave < waves 'waves'
		var replies = CountPromiseArray.create()
		var k = 0
		while k < sinkCount 'sendEach'
			let s = try sinks.get(k) otherwise panic("sinks.get OOB at {k}")
			replies.push(s.take("payload-{k}-{wave}"))
			k = k + 1
		end 'sendEach'

		var n = 0
		while n < sinkCount 'collect'
			let p = try replies.get(n) otherwise panic("replies.get OOB at {n}")
			delivered = delivered + (try await p otherwise 0)
			n = n + 1
		end 'collect'
		wave = wave + 1
	end 'waves'

	print("delivered={delivered} remote={__Builtins.slabRemoteFreeCount()}\n")

	return 0 as ExitCode
end 'main'
```
```stdout
delivered=320 remote=0
```
```exitcode
0
```
