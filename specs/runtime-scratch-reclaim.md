---
feature: runtime-scratch-reclaim
status: stable
keywords: [runtime, scratch, slab, free, green-threads, subprocess, spawnReadLine, memory, reclaim]
category: system
---

# The runtime's own scratch is RETURNED to the allocator

## Documentation

The green-thread scheduler, the one-shot read probe and the subprocess runner all need small
regions the language cannot name: a GT record, a mutable command line, a `STARTUPINFOA`, a
`PROCESS_INFORMATION`, an overlapped read buffer. They carry no box header and no refcount, so the
allocator's TRACKED live column — the one the leak gate reads — cannot see them. The per-call scratch
comes from `__slab_alloc` directly; a GT record comes from the scheduler's own record arena, carved from
`osAllocPages` chunks like a green thread's stack, so it moves no raw column either.

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

The GT-record case asks the recycling runtime's count instead: every spawn in the window was served
from a free list rather than carving a fresh record, and the window took nothing from the allocator
and left nothing live.

⚠ **EACH CASE WARMS THE SCHEDULER FIRST.** `__gt_init` runs before `main` does, so the timer
store and the poller are in place before any window opens; what a first call
can still create for the life of the process — the GT struct its processor's free list keeps for the next
spawn is one — belongs outside the window too. Measuring across it would credit the window with
allocations that are *supposed* to still be live. The warm-up call is what makes the window contain
only per-call work.

**Targets — the green-thread substrate gate; see `async-scheduler.md`'s *Targets* section for the
one statement of it.** The two probe scratch cases park a green thread on a child process or a pipe
through probes only x64-windows builds; the GT-struct case runs on every lane with green threads, and
`every-spawn-door-returns-its-scratch` on every lane that can spawn a child.

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

⚠ The read buffer is the one region that outlives the PARK — the kernel is writing into it while the green
thread is suspended — so it is released only after the yielding read has returned. A DROPPED reader takes the
same road: it is renounced rather than abandoned, so it comes back and frees its own buffer
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

<!-- test: runtime-scratch-reclaim.every-spawn-door-returns-its-scratch -->
<!-- unsupported-targets: wasm32-wasi -->
**THE PUBLIC SPAWN DOORS, AND EVERY ROAD OUT OF THEM.** A `Subprocess.run` takes scratch in three
places the program cannot see: the stdlib's `PATH` walk (its state, the variables it reads and the
candidate path), the request the spawn entry fills (the request record, the argument vector or command
line, and the environment vector a caller-built environment becomes on POSIX), and the spawn core
(the inheritable `SECURITY_ATTRIBUTES`, the child-handle scratch, the null-device name, a name and an
out-param block per pipe, the `STARTUPINFOA` and the `PROCESS_INFORMATION`); the collect adds its own
loop state. Each belongs to one call, so each goes back on the road that call leaves by — a child that
ran, an executable that is not there, a working directory that is not there — and on the streaming
door, whose handle outlives the spawn but whose request does not.

Each door runs once to warm, then `Rounds` more times inside the window: the allocator must have seen
the traffic (`total` grew by at least one region per round) and must have it all back (`live` grew by
less than one region per round, so a single region kept per spawn fails the door). Each door reports
its three facts on a line of its own.
```maxon
let Rounds = 16

enum Door
	childRuns
	executableMissing
	workingDirectoryMissing
	streamingWithEnvironment
end 'Door'

function shell() returns Executable
	#if os(Windows)
	return Executable.name("cmd")
	#else
	return Executable.name("sh")
	#endif
end 'shell'

function exitZero() returns StringArray
	var argv = StringArray.create()
	#if os(Windows)
	argv.push("/c")
	#else
	argv.push("-c")
	#endif
	argv.push("exit 0")
	return argv
end 'exitZero'

function childRuns() returns bool
	let result = try Subprocess.run(shell(), arguments: exitZero()) otherwise return false
	return result.exitCode() == 0
end 'childRuns'

function executableMissing() returns bool
	_ = try Subprocess.run(Executable.path(Directory.currentPath().join("maxon-spec-no-such-executable-xyzzy")), arguments: StringArray.create()) otherwise return true
	return false
end 'executableMissing'

function workingDirectoryMissing() returns bool
	_ = try Subprocess.run(shell(), arguments: exitZero(), workingDirectory: Directory.currentPath().join("maxon-spec-no-such-working-directory-xyzzy")) otherwise return true
	return false
end 'workingDirectoryMissing'

function streamingWithEnvironment() returns bool
	var overrides = EnvMap.create()
	overrides.upsert("MAXON_SPEC_SCRATCH", value: "1")
	let inheritCwd = try FilePath.from("") otherwise return false
	var child = try StreamingSubprocess.spawnWithEnvironment(shell(), arguments: exitZero(), workingDirectory: inheritCwd, environment: Environment.inheritUpdating(overrides)) otherwise return false
	let code = try child.wait() otherwise 1
	child.release()
	return code == 0
end 'streamingWithEnvironment'

function attempt(door Door) returns bool
	return match door 'door'
		childRuns gives childRuns()
		executableMissing gives executableMissing()
		workingDirectoryMissing gives workingDirectoryMissing()
		streamingWithEnvironment gives streamingWithEnvironment()
	end 'door'
end 'attempt'

function reportScratch(door Door)
	var answered = attempt(door)
	let totalBefore = __Builtins.mmRawAllocTotal()
	let liveBefore = __Builtins.mmRawAllocLive()

	for _ in 0 upto Rounds 'round'
		if not attempt(door) 'wrong'
			answered = false
		end 'wrong'
	end 'round'

	let tookScratch = __Builtins.mmRawAllocTotal() - totalBefore >= Rounds
	let gaveItBack = __Builtins.mmRawAllocLive() - liveBefore < Rounds
	print("{door.name}: answered={answered} took={tookScratch} gaveBack={gaveItBack}\n")
end 'reportScratch'

function main() returns ExitCode
	for door in Door.allCases 'door'
		reportScratch(door)
	end 'door'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
childRuns: answered=true took=true gaveBack=true
executableMissing: answered=true took=true gaveBack=true
workingDirectoryMissing: answered=true took=true gaveBack=true
streamingWithEnvironment: answered=true took=true gaveBack=true
```

<!-- test: runtime-scratch-reclaim.spawn-await-loop-is-bounded -->
**THE GT RECORD ITSELF, AND IT IS RECYCLED RATHER THAN RETURNED.** `__gt_reclaim` puts a finished
thread's record on its processor's free list (Go's `gfput`), and `__gt_spawn` takes the next one from
there, zeroed, before it carves a fresh one from the scheduler's record arena (Go's `gfget`). The records
are TYPE-STABLE, as Go's `g` records are: a read through a handle whose thread is gone — `awaitAny` over
a promise already awaited (`await-any.md`) — lands in a GT record rather than in memory that has been
given to something else.

⭐ **THE MEMORY IS STILL BOUNDED.** Every record on a list was live at some moment, and a fresh one is
carved only when the spawning processor's list and the global list are both empty. So the records a
program ever carves number at most its peak concurrent population plus what other processors' lists
hold, and a processor's list holds fewer than 64 — Go's bound for its `gFree` lists. A loop that spawns
and awaits one thread at a time cycles ONE record.

Eight spawn/await pairs after a warm-up whose struct is already on the list: every spawn is served
by the free list (`schedGtRecycleCount()` grows by exactly 8), none asks the allocator
(`mmRawAllocTotal` does not move — a green thread's record comes from the scheduler's arena and its
stack from `osAllocPages`, and the raw columns count neither), and nothing is left live
(`mmRawAllocLive` does not move).
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
