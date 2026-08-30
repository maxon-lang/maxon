---
feature: runtime-scratch-reclaim
status: stable
keywords: [runtime, scratch, slab, free, green-threads, subprocess, spawnReadLine, memory, reclaim]
category: system
---

# The runtime's own scratch is RETURNED to the allocator

## Documentation

The green-thread scheduler, the one-shot read probe and the subprocess runner all need small
regions the language cannot name: a GT struct, a mutable command line, a `STARTUPINFOA`, a
`PROCESS_INFORMATION`, an overlapped read buffer. They take them from `__slab_alloc` directly —
they carry no box header and no refcount, so `__mm_alloc_count` cannot see them.

⭐⭐ **UNTIL S3 THEY WERE NEVER GIVEN BACK, AND EACH SITE HAD INVENTED ITS OWN WAY TO COPE.** The
GT struct had a private `.data` LIFO it was pushed onto and popped off; the subprocess scratch was
three fixed buffers allocated once at scheduler init and reused under a "no two calls' scratch can
coexist" argument; the read probe simply allocated ~4.2 KB per call and abandoned it. All three
existed for ONE reason, and every one of them said so in its own comment: `__slab_alloc` was a bump
cursor with no free path. It has one now (`slab-allocator.md`), so all three are the same call.

**What these cases can actually observe.** Nothing in the language names a slab slot, so each case
below reads the RAW traffic columns (`builtins-mm-counters.md`) around a window of runtime work and
asks two questions that only a reclaiming runtime answers together:

| Question | Column | A runtime that never frees |
|---|---|---|
| did the window take scratch from the allocator? | `mmRawAllocTotal` grew | yes — or it hid the population |
| did it give the scratch back? | `mmRawAllocLive` did NOT grow | **no** — live tracks total exactly |

Both halves are needed. `live` alone would pass against a runtime that allocated nothing in the
window (the pre-S3 subprocess path, whose scratch was allocated once at init), and `total` alone
would pass against the pre-S3 read probe, which allocated freely and released nothing.

⚠ **EACH CASE WARMS THE SCHEDULER FIRST.** `__gt_init` and `__io_init` take GT0, the timer store,
the process store and the completion port's lock the first time any of this is touched, and those
regions are genuinely process-lifetime. Measuring across them would credit the window with
allocations that are *supposed* to still be live. The warm-up call is what makes the window contain
only per-call work.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the
one statement of it.** Every case here parks a green thread, so all of them are x64-windows only.

## Tests

<!-- test: runtime-scratch-reclaim.read-probe-scratch-returns -->
<!-- targets: x64-windows -->
**THE ~4.2 KB-PER-CALL DEBT.** `spawnReadLine` builds a pipe name, a `SECURITY_ATTRIBUTES`, a
two-handle out-param block, a mutable command line, a `STARTUPINFOA`, a `PROCESS_INFORMATION` and a
4 KiB read region — seven allocations, per call. Before S3 there were eight (the byte-count slot was
its own) and not one of them was released, so a program that read from N children held N × ~4.2 KB it
could never use again. Here three reads follow a warm-up: the allocator sees the traffic (`total`
moves by at least three regions per read) and gets all of it back (`live` does not move by more than
one region per read).

⚠ The read buffer is the one region that outlives the PARK — the completion thread is writing into
it while the green thread is suspended — so it is released only after the yielding read has
returned, and the drop-in-flight path releases it through the GT's own scratch slot instead
(`spawn-read-line.drop-in-flight`).
```maxon
function main() returns ExitCode
	let warm = spawnReadLine("cmd /c echo hello")
	let totalBefore = __Builtins.mmRawAllocTotal()
	let liveBefore = __Builtins.mmRawAllocLive()
	let a = spawnReadLine("cmd /c echo hello")
	let b = spawnReadLine("cmd /c echo hello")
	let c = spawnReadLine("cmd /c echo hello")
	let totalGrew = __Builtins.mmRawAllocTotal() - totalBefore
	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	var score = 0
	if warm + a + b + c == 28 'everyReadReturnedSevenBytes'
		score = score + 1
	end 'everyReadReturnedSevenBytes'
	if totalGrew >= 9 'theReadsTookScratchFromTheAllocator'
		score = score + 2
	end 'theReadsTookScratchFromTheAllocator'
	if liveGrew <= 3 'andGaveItBack'
		score = score + 4
	end 'andGaveItBack'
	return score as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: runtime-scratch-reclaim.subprocess-scratch-returns -->
<!-- targets: x64-windows -->
**THE THREE REUSED BUFFERS.** `__gt_process_run`'s scratch used to be three fixed regions taken at
scheduler init and reused by every call, plus a grow-on-demand command-line buffer that abandoned
its predecessor whenever a longer command appeared. The reuse was safe only because of an argument
about the code — that a call writes and consumes its scratch inside a window with no yield in it —
which is exactly the argument the read probe could not make and so did not use. With a free path
each call takes its own and hands it back, and no two calls can share anything.

The spawn is measured too: a GT struct is one of the regions the window takes and returns.
```maxon
function child() returns Integer
	return try __Builtins.runProcess("cmd /c exit 3") otherwise 99
end 'child'

function main() returns ExitCode
	let p0 = async child()
	let w = await p0
	let totalBefore = __Builtins.mmRawAllocTotal()
	let liveBefore = __Builtins.mmRawAllocLive()
	let p1 = async child()
	let a = await p1
	let p2 = async child()
	let b = await p2
	let totalGrew = __Builtins.mmRawAllocTotal() - totalBefore
	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	var score = 0
	if w + a + b == 9 'everyChildExitedThree'
		score = score + 1
	end 'everyChildExitedThree'
	if totalGrew >= 6 'eachRunTookItsScratchFromTheAllocator'
		score = score + 2
	end 'eachRunTookItsScratchFromTheAllocator'
	if liveGrew <= 2 'andGaveItBack'
		score = score + 4
	end 'andGaveItBack'
	return score as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: runtime-scratch-reclaim.spawn-await-loop-is-bounded -->
<!-- targets: x64-windows -->
**THE GT STRUCT ITSELF.** A spawn takes a `GtStructSize` region and an await gives it back. Before
S3 the giving-back was a push onto a private `.data` LIFO, which bounded a spawn/await loop's
memory but bounded it in a pool nothing else in the program could ever draw on: 40 finished green
threads held 40 structs' worth of address space against the day a 41st spawn wanted one. Now they
go back to the span they came from, where any allocation of any size class can have them.

Eight spawn/await pairs after a warm-up: `total` moves by at least one region per spawn, `live`
does not move at all beyond the slack a single allocation would take.
```maxon
function work(n Integer) returns Integer
	__Builtins.parallelBoundary()
	return n + 1
end 'work'

function spawnAndAwait(seed Integer) returns Integer
	let p = async work(seed)
	return await p
end 'spawnAndAwait'

function main() returns ExitCode
	var acc = spawnAndAwait(0)
	let totalBefore = __Builtins.mmRawAllocTotal()
	let liveBefore = __Builtins.mmRawAllocLive()
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	acc = spawnAndAwait(acc)
	let totalGrew = __Builtins.mmRawAllocTotal() - totalBefore
	let liveGrew = __Builtins.mmRawAllocLive() - liveBefore
	var score = 0
	if acc == 9 'everyThreadRanExactlyOnce'
		score = score + 1
	end 'everyThreadRanExactlyOnce'
	if totalGrew >= 8 'eachSpawnTookAStruct'
		score = score + 2
	end 'eachSpawnTookAStruct'
	if liveGrew <= 1 'andEachAwaitGaveItBack'
		score = score + 4
	end 'andEachAwaitGaveItBack'
	return score as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
