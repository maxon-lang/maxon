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

⭐⭐ **THE PER-CALL SCRATCH GOES BACK TO THE ALLOCATOR; THE GT STRUCT DOES NOT.** The subprocess
runner's command line, `STARTUPINFOA` and `PROCESS_INFORMATION`, and the read probe's ~4.2 KB, each
belong to one call, and each goes back through the slab's free path (`slab-allocator.md`) the moment
that call is done with it — so a program that runs N children or reads N pipes holds none of it
afterwards. A GT struct is the one region that is never given back: a reclaimed struct goes onto
its processor's free list and serves the next spawn, so GT records are type-stable.
`spawn-await-loop-is-bounded` below carries that case.

**What these cases can actually observe.** Nothing in the language names a slab slot, so each case
below reads the RAW traffic columns (`builtins-mm-counters.md`) around a window of runtime work. The
two scratch cases ask two questions that only a reclaiming runtime answers together:

| Question | Column | A runtime that never frees |
|---|---|---|
| did the window take scratch from the allocator? | `mmRawAllocTotal` grew | yes — or it hid the population |
| did it give the scratch back? | `mmRawAllocLive` did NOT grow | **no** — live tracks total exactly |

Both halves are needed. `live` alone would pass against a runtime that allocated nothing in the
window (the pre-S3 subprocess path, whose scratch was allocated once at init), and `total` alone
would pass against the pre-S3 read probe, which allocated freely and released nothing.

The GT-struct case asks the recycling runtime's pair instead: the window took nothing from the
allocator at all, and left nothing live.

⚠ **EACH CASE WARMS THE SCHEDULER FIRST.** `__gt_init` and `__io_init` run before `main` does, so the timer
store, the process store and the completion port are in place before any window opens; what a first call
can still create for the life of the process — the GT struct its processor's free list keeps for the next
spawn is one — belongs outside the window too. Measuring across it would credit the window with
allocations that are *supposed* to still be live. The warm-up call is what makes the window contain
only per-call work.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the
one statement of it.** The two scratch cases park a green thread on a child process or a pipe, so
they are x64-windows only; the GT-struct case runs on every lane with green threads.

## Tests

<!-- test: runtime-scratch-reclaim.read-probe-scratch-returns -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
**THE THREE REUSED BUFFERS.** `__gt_process_run`'s scratch used to be three fixed regions taken at
scheduler init and reused by every call, plus a grow-on-demand command-line buffer that abandoned
its predecessor whenever a longer command appeared. The reuse was safe only because of an argument
about the code — that a call writes and consumes its scratch inside a window with no yield in it —
which is exactly the argument the read probe could not make and so did not use. With a free path
each call takes its own and hands it back, and no two calls can share anything.

The two spawns in the window take their GT structs from the free list the warm-up filled, so every
region the window counts is the runner's own scratch.
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
**THE GT STRUCT ITSELF, AND IT IS RECYCLED RATHER THAN RETURNED.** `__gt_reclaim` puts a finished
thread's struct on its processor's free list (Go's `gfput`), and `__gt_spawn` takes the next one
from there, zeroed, before it asks the allocator (Go's `gfget`). The records are TYPE-STABLE, as Go's
`g` records are: a read through a handle whose thread is gone — `awaitAny` over a promise already
awaited (`await-any.md`) — lands in a GT record rather than in whatever the slab gave that memory to
next.

⭐ **THE MEMORY IS STILL BOUNDED.** A processor's list holds fewer than 64: a put that reaches 64
moves the surplus to one global list until 32 remain, and an empty list refills from the global one
before a spawn cuts a fresh struct. Every struct on a list was live at some moment, and a fresh one
is cut only when the spawning processor's list and the global list are both empty, so the lists hold
at most the program's peak concurrent population plus fewer than 64 per processor — Go's bound for
its `gFree` lists. A loop that spawns and awaits one thread at a time cycles ONE struct.

Eight spawn/await pairs after a warm-up whose struct is already on the list: every spawn is served
by the free list (`schedGtRecycleCount()` grows by exactly 8), none asks the allocator
(`mmRawAllocTotal` does not move — a green thread's stack is `osAllocPages`, which the raw columns do
not count), and nothing is left live (`mmRawAllocLive` does not move).
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
	let recycledBefore = __Builtins.schedGtRecycleCount()
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
	let recycled = __Builtins.schedGtRecycleCount() - recycledBefore
	var score = 0
	if acc == 9 'everyThreadRanExactlyOnce'
		score = score + 1
	end 'everyThreadRanExactlyOnce'
	if totalGrew == 0 and recycled == 8 'everySpawnReusedAStruct'
		score = score + 2
	end 'everySpawnReusedAStruct'
	if liveGrew == 0 'andNothingWasLeftLive'
		score = score + 4
	end 'andNothingWasLeftLive'
	return score as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```
