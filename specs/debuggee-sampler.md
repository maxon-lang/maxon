---
feature: debuggee-sampler
status: experimental
keywords: [debuggee, DebugPort, ProcessTrace, RegisterBlock, Sampler, SampledProcess, SampleTimer, HeapStats, SlabCensus, bounds]
category: system
---

# The debugger and profiler surface of the standard library

## Documentation

`DebugPort` (Windows) and `ProcessTrace` (Linux) drive a debuggee, and `SampledProcess` and `SampleTimer`
(every native lane) sample a process. Each owns a scratch region that `close()` releases; a call that needs the region
after `close()` panics. Every figure a debuggee hands back is a `RegisterBlock`, a record of words read at a
word-aligned byte offset:

- `word(offset)` throws `DebuggeeError.outOfBounds` for an offset that is not a multiple of 8 and for one at or past
  the end of the block;
- `with(offset, word:)` answers a new block and leaves the receiver as it was, under the same two conditions;
- on a Windows CONTEXT, `programCounter()`, `stackPointer()`, `flags()`, `gpr(index)` and `xmmLane(index)` read the
  named register, and `withProgramCounter` and `withFlags` replace it; a block of any other size throws
  `outOfBounds`;
- a transfer through `readMemory` or `writeCode` moves at most `DebugTransferBytes` (512) bytes, and a longer one
  throws `unreadable` or `unwritable` before the target is touched;
- an index typed `Win64GprIndex` or `HeapBucketIndex` is range-checked where the value is made, so one past the last
  is a panic at the cast.

A program that names these types compiles whether or not it reaches every method.

## Tests

<!-- test: a-register-block-reads-the-registers-of-a-context -->
<!-- tier: stdlib -->
A block of `0x4D0` bytes is a Win64 CONTEXT, and each accessor reads its register at a fixed offset. The block is
built from a scratch region the case fills, since only a debuggee hands one out otherwise.

```maxon
function context() returns RegisterBlock
	var pages = ScratchPages.reserve(0x1000)
	pages.setWord(0xF8, value: 0x1000)
	pages.setWord(0x98, value: 0x2000)
	pages.setWord(0x40, value: 0x2B + (0x246 shl 32))
	pages.setWord(0x80, value: 77)
	pages.setWord(0x1C0, value: 55)
	let block = RegisterBlock.readFrom(pages, at: 0, length: 0x4D0)
	pages.release()

	return block
end 'context'

function main() returns ExitCode
	let block = context()
	let pc = try block.programCounter() otherwise return 1
	let sp = try block.stackPointer() otherwise return 2
	let flags = try block.flags() otherwise return 3
	let gpr = try block.gpr(1) otherwise return 4
	let lane = try block.xmmLane(2) otherwise return 5
	print("{pc} {sp} {flags} {gpr} {lane}\n")

	return 0
end 'main'
```
```stdout
4096 8192 582 77 55
```
```exitcode
0
```

<!-- test: a-register-block-with-answers-a-new-block-and-keeps-the-receiver -->
<!-- tier: stdlib -->
`with` and its two named forms return a new block. The receiver still reads the old words, and `withFlags` keeps the
segment bits of the word it rewrites.

```maxon
function context() returns RegisterBlock
	var pages = ScratchPages.reserve(0x1000)
	pages.setWord(0xF8, value: 0x1000)
	pages.setWord(0x98, value: 0x2000)
	pages.setWord(0x40, value: 0x2B + (0x246 shl 32))
	let block = RegisterBlock.readFrom(pages, at: 0, length: 0x4D0)
	pages.release()

	return block
end 'context'

function main() returns ExitCode
	let block = context()
	let moved = try block.withProgramCounter(0x5000) otherwise return 1
	let flagged = try block.withFlags(0x202) otherwise return 2
	let replaced = try block.with(0x98, word: 9) otherwise return 3

	let movedPc = try moved.programCounter() otherwise return 4
	let keptPc = try block.programCounter() otherwise return 5
	let newFlags = try flagged.flags() otherwise return 6
	let segments = (try flagged.word(0x40) otherwise return 7) and 0xFFFFFFFF
	let newSp = try replaced.stackPointer() otherwise return 8
	let keptSp = try block.stackPointer() otherwise return 9

	print("{movedPc} {keptPc}\n")
	print("{newFlags} {segments}\n")
	print("{newSp} {keptSp}\n")

	return 0
end 'main'
```
```stdout
20480 4096
514 43
9 8192
```
```exitcode
0
```

<!-- test: a-register-block-offset-outside-the-block-throws-out-of-bounds -->
<!-- tier: stdlib -->
`word` and `with` throw `outOfBounds` for an offset that is not a multiple of 8 and for one at or past the end. A block
that is not a CONTEXT throws it from the named accessors too.

```maxon
function block(length ScratchBytes) returns RegisterBlock
	var pages = ScratchPages.reserve(0x1000)
	pages.setWord(0, value: 5)
	let made = RegisterBlock.readFrom(pages, at: 0, length: length)
	pages.release()

	return made
end 'block'

function wordOutcome(held RegisterBlock, offset RegisterOffset) returns String
	if let word = try held.word(offset) 'read'
		return "{word}"
	end 'read' else (e) 'refused'
		return e.name
	end 'refused'
end 'wordOutcome'

function withOutcome(held RegisterBlock, offset RegisterOffset) returns String
	if let changed = try held.with(offset, word: 1) 'written'
		return "{try changed.word(offset) otherwise 0}"
	end 'written' else (e) 'refused'
		return e.name
	end 'refused'
end 'withOutcome'

function programCounterOutcome(held RegisterBlock) returns String
	if let word = try held.programCounter() 'read'
		return "{word}"
	end 'read' else (e) 'refused'
		return e.name
	end 'refused'
end 'programCounterOutcome'

function main() returns ExitCode
	let context = block(0x4D0)
	print("{wordOutcome(context, offset: 0)} {wordOutcome(context, offset: 3)} {wordOutcome(context, offset: 0x4C8)} {wordOutcome(context, offset: 0x4D0)}\n")
	print("{withOutcome(context, offset: 8)} {withOutcome(context, offset: 12)} {withOutcome(context, offset: 0x4D0)}\n")

	let short = block(64)
	print("{wordOutcome(short, offset: 56)} {wordOutcome(short, offset: 64)} {programCounterOutcome(short)}\n")

	return 0
end 'main'
```
```stdout
5 outOfBounds 0 outOfBounds
1 outOfBounds outOfBounds
0 outOfBounds outOfBounds
```
```exitcode
0
```

<!-- test: a-gpr-index-past-the-last-register-panics-at-the-cast -->
<!-- tier: stdlib -->
`Win64GprIndex` is `int(0 to 15)`, so an index one past the last register never reaches `gpr`: the cast that makes it
panics.

```maxon
function main() returns ExitCode
	var pages = ScratchPages.reserve(0x1000)
	let block = RegisterBlock.readFrom(pages, at: 0, length: 0x4D0)
	pages.release()
	let past = b"0123456789abcdef".count() as Win64GprIndex

	return (try block.gpr(past) otherwise 7) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-gpr-index-past-the-last-register-panics-at-the-cast.test:6: Range check failed: value outside typealias 'Win64GprIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-process-trace-moves-at-most-the-transfer-window -->
A transfer larger than the 512-byte window throws `unreadable` or `unwritable` before the target is touched, and one
of no bytes answers at once. `close` releases the region and a second `close` changes nothing.

```maxon
function readOutcome(trace ProcessTrace, count DebuggeeByteCount) returns String
	if let bytes = try trace.readMemory(1, address: 4096, count: count) 'read'
		return "read {bytes.count()}"
	end 'read' else (e) 'refused'
		return e.name
	end 'refused'
end 'readOutcome'

function writeOutcome(trace ProcessTrace, content ByteArray) returns String
	try trace.writeCode(1, address: 4096, content: content) otherwise (e) 'refused'
		return e.name
	end 'refused'

	return "wrote"
end 'writeOutcome'

function main() returns ExitCode
	var trace = ProcessTrace.create()
	print("ready {trace.isReady()}\n")
	print("{readOutcome(trace, count: 0)} {readOutcome(trace, count: 513)} {readOutcome(trace, count: 1000000)}\n")

	var big = ByteArray.create()

	for _ in 0 upto 513 'fill'
		big.push(0)
	end 'fill'

	print("{writeOutcome(trace, content: ByteArray.create())} {writeOutcome(trace, content: big)}\n")
	trace.close()
	trace.close()
	print("ready {trace.isReady()}\n")

	return 0
end 'main'
```
```stdout
ready true
read 0 unreadable unreadable
wrote unwritable
ready false
```
```exitcode
0
```

<!-- test: a-closed-process-trace-panics-when-it-needs-its-region -->
`wait` writes the status word of the scratch region first, so a closed trace panics there instead of reading pages it
no longer holds. The status word sits after the register block and the vector block (`TraceStatusSlot`), which are
216 and 512 bytes on x64 and 272 and 528 on arm64, so the offset the panic names is 744 on every lane but arm64 and
816 on both arm64 lanes.

```maxon
function main() returns ExitCode
	var trace = ProcessTrace.create()
	trace.close()
	let report = trace.wait(0, process: 1)

	return 0 if not report.arrived() else 1
end 'main'
```
```exitcode
1
```
```stderr
panic at Debuggee.maxon:98: ScratchPages: offset 744 was asked of pages that are not held — a caller used them before reserving them or after releasing them
Stack trace:
  in ScratchPages.requireWordInside
  in ScratchPages.heldBase
  in ScratchPages.setWord
  in ProcessTrace.wait
  in main
  in mrt_start
```
```Stderr:arm64-linux
panic at Debuggee.maxon:98: ScratchPages: offset 816 was asked of pages that are not held — a caller used them before reserving them or after releasing them
Stack trace:
  in ScratchPages.requireWordInside
  in ScratchPages.heldBase
  in ScratchPages.setWord
  in ProcessTrace.wait
  in main
  in mrt_start
```
```Stderr:arm64-macos
panic at Debuggee.maxon:98: ScratchPages: offset 816 was asked of pages that are not held — a caller used them before reserving them or after releasing them
Stack trace:
  in ScratchPages.requireWordInside
  in ScratchPages.heldBase
  in ScratchPages.setWord
  in ProcessTrace.wait
  in main
  in mrt_start
```

<!-- test: a-debug-port-owns-a-region-until-it-is-closed -->
A `DebugPort` reports whether it holds its scratch region, names no event thread before an event, and releases the
region on `close`.

```maxon
function main() returns ExitCode
	var port = DebugPort.create()
	print("ready {port.isReady()} thread {port.eventThread()}\n")
	port.close()
	port.close()
	print("ready {port.isReady()}\n")

	return 0
end 'main'
```
```stdout
ready true thread 0
ready false
```
```exitcode
0
```

<!-- test: a-debug-port-with-no-debuggee-times-out -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
On wasm32-wasi the debug-port call panics (windowsDebugCall asks a host with no Win32 debug port), so the run exits 1 where the case pins 0.

On Windows a port with nothing attached has no event to wait for.
`DebugPort` drives the Win32 debug API, which only Windows provides, so the other lanes do not run the program.

```maxon
function main() returns ExitCode
	var port = DebugPort.create()
	let event = port.waitEvent(0)

	match event 'which'
		timedOut then print("timed out\n")
		default panic("an event arrived at a port with no debuggee")
	end 'which'

	port.close()

	return 0
end 'main'
```
```stdout
timed out
```
```exitcode
0
```

<!-- test: a-census-bucket-past-the-last-panics-at-the-cast -->
`HeapBucketIndex` is `int(0 to 2047)`. The last bucket reads, and an index one past it panics where it is made.

```maxon
function main() returns ExitCode
	let census = HeapStats.census(HeapCensusMode.bySizeClass)
	let last = (b"x".count() + 2046) as HeapBucketIndex
	print("last {census.bucketCount(last) >= 0}\n")
	let past = (b"x".count() + 2047) as HeapBucketIndex

	return (census.bucketCount(past) mod 2) as ExitCode
end 'main'
```
```stdout
last true
```
```exitcode
1
```
```stderr
panic at a-census-bucket-past-the-last-panics-at-the-cast.test:6: Range check failed: value outside typealias 'HeapBucketIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-sampled-process-that-does-not-exist-throws-no-such-process -->
<!-- unsupported-targets: wasm32-wasi -->
There is no process 4000000000, so opening it throws.

```maxon
function main() returns ExitCode
	try SampledProcess.open(4000000000) otherwise (e) 'refused'
		print("{e.name}\n")

		return 0
	end 'refused'

	return 1
end 'main'
```
```stdout
noSuchProcess
```
```exitcode
0
```

<!-- test: a-sampled-process-lists-its-threads-and-modules -->
<!-- unsupported-targets: x64-linux, arm64-linux, wasm32-wasi -->
The program samples itself: it has threads and modules, a thread it releases is no longer known to the sample, and
`close` releases the rest. Linux refuses a process the right to trace its own threads, so that lane samples a child
instead, in the cases below.

```maxon
function main() returns ExitCode
	var sampled = try SampledProcess.open(Process.id()) otherwise return 1
	let threads = sampled.threads()
	let modules = try sampled.modules() otherwise return 2
	print("threads {threads.count() > 0} modules {modules.count() > 0}\n")

	let first = try threads.get(0) otherwise return 3
	sampled.release(first)

	if let cycles = try sampled.cycles(first) 'known'
		print("known {cycles}\n")
	end 'known' else (e) 'unknown'
		print("{e.name}\n")
	end 'unknown'

	sampled.close()

	return 0
end 'main'
```
```stdout
threads true modules true
unknownThread
```
```exitcode
0
```

<!-- test: a-sample-timer-ticks-and-closes -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi declares no SampleTimer, so the call is refused with E3004 (call to undefined function 'SampleTimer.create'), not E3104.

A timer waits for its next deadline, and `close` releases its handle once. A timer reports through `ready` whether
the host gave it the handle it waits on.

```maxon
function main() returns ExitCode
	var timer = SampleTimer.create(1000000)

	if not timer.ready() 'notReady'
		return 1
	end 'notReady'

	timer.wait()
	timer.wait()
	timer.close()
	timer.close()
	print("closed\n")

	return 0
end 'main'
```
```stdout
closed
```
```exitcode
0
```

<!-- test: a-closed-sample-timer-panics-when-it-waits -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi declares no SampleTimer, so the call is refused with E3004 (call to undefined function 'SampleTimer.create'), not E3104.

A wait on a closed timer is a bug in the caller, so it panics.

```maxon
function main() returns ExitCode
	var timer = SampleTimer.create(1000000)
	timer.close()
	timer.wait()

	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Sampler.maxon:1386: SampleTimer.wait: the timer was closed
Stack trace:
  in SampleTimer.wait
  in main
  in mrt_start
```

<!-- test: a-sampled-child-yields-its-threads-and-a-stack -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- process: own -->
The program runs itself as a child that waits, opens the child, and lists its threads; a capture of the first
thread answers the program counter and the stack bytes it copied, and `close` releases the sample.
```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'asTheChild'
		sleep(60000)

		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 1
	var childArguments = StringArray.create()
	childArguments.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: childArguments) otherwise return 2
	sleep(300)
	let pid = try child.processId() otherwise return 3
	var sampled = try SampledProcess.open(pid) otherwise return 4
	let threads = sampled.threads()
	print("threads {threads.count() > 0}\n")

	let first = try threads.get(0) otherwise return 5
	let captured = try sampled.capture(first) otherwise return 6

	match captured 'result'
		threadGone then print("gone\n")
		unreadable then print("unreadable\n")
		captured(stack) then print("captured {stack.programCounter != 0} {stack.window.byteCount() > 0}\n")
	end 'result'

	sampled.close()
	_ = try child.waitWithTimeout(1) otherwise 0
	child.release()

	return 0
end 'main'
```
```stdout
threads true
captured true true
```
```exitcode
0
```

<!-- test: a-process-cannot-sample-its-own-threads-on-linux -->
<!-- unsupported-targets: x64-windows, arm64-macos, wasm32-wasi -->
Linux refuses a process the right to trace a thread of its own group, so opening the program's own id throws
`accessDenied`.

```maxon
function main() returns ExitCode
	try SampledProcess.open(Process.id()) otherwise (e) 'refused'
		print("{e.name}\n")

		return 0
	end 'refused'

	return 1
end 'main'
```
```stdout
accessDenied
```
```exitcode
0
```

<!-- test: a-dropped-seized-set-detaches-the-threads-it-holds -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, arm64-macos, wasm32-wasi -->
<!-- process: own -->
The program runs itself as a child that waits, seizes the child's thread under a pin, records it in a
`SeizedThreads` that takes the pin over, and drops the set without releasing it. The drop detaches the thread and
then unpins: the child has a tracer while the set holds it and none after, and the caller is left unpinned.

```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'asTheChild'
		sleep(60000)

		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 1
	var childArguments = StringArray.create()
	childArguments.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: childArguments) otherwise return 2
	sleep(300)
	let pid = try child.processId() otherwise return 3
	let status = seizeAndDrop(pid)
	print("traced after the drop {isTraced(pid)}\n")
	print("pin depth after the drop {Scheduler.pinDepth()}\n")
	_ = try child.waitWithTimeout(1) otherwise 0
	child.release()

	return status
end 'main'

function seizeAndDrop(pid Pid) returns ExitCode
	var pin = MachinePin.acquire()
	var trace = ProcessTrace.create()
	var seized = SeizedThreads.open(pid, pin: pin)
	let thread = pid as DebuggeeThreadId

	if not trace.seize(thread) 'refused'
		return 5
	end 'refused'

	seized.add(thread)
	print("traced while held {isTraced(pid)}\n")

	return 0
end 'seizeAndDrop'

function isTraced(pid Pid) returns bool
	let path = try FilePath.from("/proc/{pid}/status") otherwise panic("a process id spells a path")
	let text = try File.readText(path) otherwise panic("the child's status file is readable while the child lives")

	for line in text.split("\n") 'eachLine'
		if line.startsWith("TracerPid:") 'thisOne'
			let value = try line.split("\t").get(1) otherwise panic("a tracer line carries a value")

			return value.trim() != "0"
		end 'thisOne'
	end 'eachLine'

	panic("a status file names its tracer")
end 'isTraced'
```
```stdout
traced while held true
traced after the drop false
pin depth after the drop 0
```
```exitcode
0
```

<!-- test: a-dropped-seized-set-kills-the-process-whose-thread-it-cannot-detach -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, arm64-macos, wasm32-wasi -->
<!-- process: own -->
A thread the set holds that was never seized cannot be detached when the set is dropped, and a thread must not be
left held: the drop kills the process it belongs to, which ends with the kill signal's status, and says on stderr
which thread of which process it could not detach, once. The program runs the sampler as a child of its own so the
line can be pinned exactly.

```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 2 'asTheSampler'
		let target = try int.fromString(try arguments.get(2) otherwise return 1) otherwise return 2
		holdAndDrop(target as Pid)

		return 0
	end 'asTheSampler'

	if arguments.count() > 1 'asTheTarget'
		sleep(60000)

		return 0
	end 'asTheTarget'

	let me = try Process.executablePath() otherwise return 1
	var targetArguments = StringArray.create()
	targetArguments.push("target")
	var target = try StreamingSubprocess.spawn(Executable.path(me), arguments: targetArguments) otherwise return 2
	sleep(300)
	let pid = try target.processId() otherwise return 3
	var samplerArguments = StringArray.create()
	samplerArguments.push("sampler")
	samplerArguments.push("{pid}")
	let sampler = try Subprocess.run(Executable.path(me), arguments: samplerArguments) otherwise return 4
	print("stderr is the one line {sampler.stderr == "sampler: thread {pid} of process {pid} could not be detached within 10000 ms, so the process was killed rather than left stopped\n"}\n")

	if let code = try target.waitWithTimeout(3000) 'ended'
		print("ended {code}\n")
	end 'ended' else 'survived'
		print("survived\n")
	end 'survived'

	target.release()

	return 0
end 'main'

function holdAndDrop(pid Pid)
	var pin = MachinePin.acquire()
	var seized = SeizedThreads.open(pid, pin: pin)
	seized.add(pid as DebuggeeThreadId)
end 'holdAndDrop'
```
```stdout
stderr is the one line true
ended 137
```
```exitcode
0
```

<!-- test: a-dropped-machine-pin-unpins-its-green-thread -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
A `MachinePin` that is never released is dropped like any record, and the drop unpins the green thread that took
it: the pin depth is one while the pin lives and zero after.

```maxon
function main() returns ExitCode
	pinAndDrop()
	print("depth after the drop {Scheduler.pinDepth()}\n")

	return 0
end 'main'

function pinAndDrop()
	let pin = MachinePin.acquire()
	print("held {pin.held()} depth {Scheduler.pinDepth()}\n")
end 'pinAndDrop'
```
```stdout
held true depth 1
depth after the drop 0
```
```exitcode
0
```

<!-- test: a-process-sampling-its-own-threads-never-suspends-its-caller -->
<!-- unsupported-targets: x64-linux, arm64-linux, wasm32-wasi -->
<!-- procs: 4 -->
<!-- process: own -->
The program opens itself on one green thread and hands the sampler to another, which the scheduler runs on a
different machine while the first keeps its machine busy. The OS thread that captures is not the one that opened,
and a capture of the thread it is running on is refused with `callingThread`; one that suspended it would never
return.

```maxon
typealias ExitCode = int(0 to 255)
typealias Round = int(0 to 1000)

let HeldNanos = 200000000

function captureEverything(sampled SampledProcess) returns Round
	let threads = sampled.threads()
	var refusals = 0 as Round

	for thread in threads 'eachThread'
		if let _ = try sampled.capture(thread) 'sampled'
			refusals = refusals + 0
		end 'sampled' else (e) 'refused'
			if e != SamplerError.callingThread 'unexpected'
				panic("an own-process capture failed with {e.name}")
			end 'unexpected'

			refusals = refusals + 1
		end 'refused'
	end 'eachThread'

	sampled.close()

	return refusals
end 'captureEverything'

function main() returns ExitCode
	var sampled = try SampledProcess.open(Process.id()) otherwise return 1
	let work = async captureEverything(sampled)
	let started = Clock.nowNanos()

	while Clock.elapsedNanos(started) < HeldNanos 'holdTheMachine'
		_ = started
	end 'holdTheMachine'

	let refusals = await work
	print("survived {refusals > 0}\n")

	return 0
end 'main'
```
```stdout
survived true
```
```exitcode
0
```

<!-- test: a-dropped-page-reservation-releases-its-pages -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
The box a `__ManagedPages` reservation lives in frees its pages when the last reference drops, with no `release`
call: the base address read back after the drop is unmapped, so touching it faults.
wasm32-wasi has no unmapped addresses, so a read of freed linear memory does not fault there and the run exits 0.

```maxon
function reserveAndDrop() returns RegisterWord
	let pages = __ManagedPages.reserve(0x1000)

	return pages.base() as RegisterWord
end 'reserveAndDrop'

function main() returns ExitCode
	let base = reserveAndDrop()
	print("{__Builtins.profileReadWord(base, 0)}\n")

	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic: nil pointer or invalid memory access
Stack trace:
  in __prof_read_word
  in main
  in mrt_start
```

<!-- test: a-released-page-reservation-reads-as-empty-and-releases-once -->
<!-- tier: stdlib -->
`release` frees the pages early and is idempotent: the base reads back `0` afterwards, a second `release` does
nothing, and the drop that follows frees only the box.

```maxon
function main() returns ExitCode
	let pages = __ManagedPages.reserve(0x1000)
	print("{pages.base() != 0} {pages.bytes()}\n")
	pages.release()
	pages.release()
	print("{pages.base()}\n")

	return 0
end 'main'
```
```stdout
true 4096
0
```
```exitcode
0
```

<!-- test: a-function-reading-a-page-reservations-base-accepts-a-let-holder -->
<!-- tier: stdlib -->
Reading the base of a reservation writes nothing, so a function that does it may be handed a `let` holder.
wasm32-wasi has no page reservations.

```maxon
type Holder
	export var pages as __ManagedPages

	static function make() returns Self
		return Self{pages: __ManagedPages.reserve(0x1000)}
	end 'make'
end 'Holder'

function baseOf(h Holder) returns RegisterWord
	return h.pages.base() as RegisterWord
end 'baseOf'

function main() returns ExitCode
	let h = Holder.make()
	print("{baseOf(h) != 0}\n")

	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.a-function-lending-a-page-reservations-address-is-refused-a-let-holder -->
<!-- tier: stdlib -->
Lending the address of a reservation hands foreign code a way to write the pages, so the function that lends it writes
its holder, and a `let` holder is refused at the call exactly as it is for a function that writes a word.
wasm32-wasi has no page reservations.

```maxon
type Holder
	export var pages as __ManagedPages

	static function make() returns Self
		return Self{pages: __ManagedPages.reserve(0x1000)}
	end 'make'
end 'Holder'

function addressOf(h Holder) returns RegisterWord
	return h.pages.lend() as RegisterWord
end 'addressOf'

function main() returns ExitCode
	let h = Holder.make()
	print("{addressOf(h) != 0}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:16:10: cannot pass 'h', which is read-only (a `let` binding), to a function that writes parameter 'h' (in main); pass a clone()
```

<!-- test: a-debug-port-dropped-without-close-leaks-nothing -->
A `DebugPort` that is never closed is dropped like any record: its reservation is released with it and the leak
gate finds nothing.

```maxon
function main() returns ExitCode
	let port = DebugPort.create()
	print("ready {port.isReady()}\n")

	return 0
end 'main'
```
```stdout
ready true
```
```exitcode
0
```

<!-- test: a-process-trace-dropped-without-close-leaks-nothing -->
A `ProcessTrace` that is never closed is dropped like any record: its reservation is released with it and the leak
gate finds nothing.

```maxon
function main() returns ExitCode
	let trace = ProcessTrace.create()
	print("ready {trace.isReady()}\n")

	return 0
end 'main'
```
```stdout
ready true
```
```exitcode
0
```

<!-- test: a-sample-timer-dropped-without-close-leaks-nothing -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi declares no SampleTimer, so the call is refused with E3004 (call to undefined function 'SampleTimer.create'), not E3104.

A `SampleTimer` that is never closed is dropped like any record: its timer handle is closed with it and the leak
gate finds nothing.

```maxon
function main() returns ExitCode
	_ = SampleTimer.create(1000000)

	print("created\n")

	return 0
end 'main'
```
```stdout
created
```
```exitcode
0
```

<!-- test: a-sampled-process-dropped-without-close-leaks-nothing -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- process: own -->
A `SampledProcess` that is never closed is dropped like any record: its process handle, every thread handle it
opened and both scratch reservations are released with it, and the leak gate finds nothing. On Linux the drop
also detaches every thread the sampler still holds seized and unbinds the sampling thread from its machine.

```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'asTheChild'
		sleep(60000)

		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 1
	var childArguments = StringArray.create()
	childArguments.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: childArguments) otherwise return 2
	sleep(300)
	let pid = try child.processId() otherwise return 3
	let status = openAndDrop(pid)
	print("caller pin depth {Scheduler.pinDepth()}\n")
	_ = try child.waitWithTimeout(1) otherwise 0
	child.release()

	return status
end 'main'

function openAndDrop(pid Pid) returns ExitCode
	var sampled = try SampledProcess.open(pid) otherwise return 4
	let threads = sampled.threads()
	print("opened {threads.count() > 0}\n")

	return 0
end 'openAndDrop'
```
```stdout
opened true
caller pin depth 0
```
```exitcode
0
```

<!-- test: a-sampled-process-with-a-released-thread-dropped-leaks-nothing -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- process: own -->
A thread released early closes its handle once; the drop that follows releases only the handles still open.

```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'asTheChild'
		sleep(60000)

		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 1
	var childArguments = StringArray.create()
	childArguments.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: childArguments) otherwise return 2
	sleep(300)
	let pid = try child.processId() otherwise return 3
	let status = releaseAndDrop(pid)
	_ = try child.waitWithTimeout(1) otherwise 0
	child.release()

	return status
end 'main'

function releaseAndDrop(pid Pid) returns ExitCode
	var sampled = try SampledProcess.open(pid) otherwise return 4
	let threads = sampled.threads()
	let first = try threads.get(0) otherwise return 5
	sampled.release(first)
	sampled.release(first)

	print("released {threads.count() > 0}\n")

	return 0
end 'releaseAndDrop'
```
```stdout
released true
```
```exitcode
0
```

<!-- test: a-closed-sampled-process-dropped-leaks-nothing -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- process: own -->
`close` is an early release, so the drop after it has only the boxes left to free.

```maxon
function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'asTheChild'
		sleep(60000)

		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 1
	var childArguments = StringArray.create()
	childArguments.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: childArguments) otherwise return 2
	sleep(300)
	let pid = try child.processId() otherwise return 3
	let status = closeAndDrop(pid)
	_ = try child.waitWithTimeout(1) otherwise 0
	child.release()

	return status
end 'main'

function closeAndDrop(pid Pid) returns ExitCode
	var sampled = try SampledProcess.open(pid) otherwise return 4
	_ = sampled.threads()
	sampled.close()
	sampled.close()
	print("closed\n")

	return 0
end 'closeAndDrop'
```
```stdout
closed
```
```exitcode
0
```

<!-- test: an-adopted-handle-reads-as-empty-once-released -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
`adopting` puts an existing handle under the box's ownership: it reads back as given, `release` closes it early and
is idempotent, and the drop that follows frees only the box.
`__ManagedHandle.adopting` closes a Win32 handle, which only x64-windows provides, so the other lanes do not run the program.

```maxon
function main() returns ExitCode
	let held = __ManagedHandle.adopting(__Builtins.profileOpenTarget(Process.id()))
	print("{held.handle() != 0}\n")
	held.release()
	held.release()
	print("{held.handle()}\n")

	return 0
end 'main'
```
```stdout
true
0
```
```exitcode
0
```

<!-- test: a-dropped-adopted-handle-closes-the-handle-it-held -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
The box an adopted process handle lives in closes the handle when the last reference drops, with no `release` call:
the process answers a thread walk while the box is alive and answers none through the handle word after the drop.
`__ManagedHandle.adopting` closes a Win32 handle, which only x64-windows provides, so the other lanes do not run the program.

```maxon
function walksThreads(word RegisterWord) returns bool
	let thread = __Builtins.profileNextThread(word, 0)
	__Builtins.profileClose(thread)

	return thread != 0
end 'walksThreads'

function openAndDrop() returns RegisterWord
	let held = __ManagedHandle.adopting(__Builtins.profileOpenTarget(Process.id()))
	let word = held.handle() as RegisterWord
	print("alive {walksThreads(word)}\n")

	return word
end 'openAndDrop'

function main() returns ExitCode
	let word = openAndDrop()
	print("dropped {walksThreads(word)}\n")

	return 0
end 'main'
```
```stdout
alive true
dropped false
```
```exitcode
0
```

<!-- test: a-released-timer-reads-as-empty-and-releases-once -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
`release` closes the timer early and is idempotent: the handle reads back `0` afterwards and the drop that follows
frees only the box.
`__ManagedHandle.timer()` opens a Win32 timer, which only x64-windows provides, so the other lanes do not run the program.

```maxon
function main() returns ExitCode
	let timer = __ManagedHandle.timer()
	print("{timer.handle() != 0}\n")
	timer.release()
	timer.release()
	print("{timer.handle()}\n")

	return 0
end 'main'
```
```stdout
true
0
```
```exitcode
0
```

<!-- test: error.a-debug-port-cannot-be-a-registry-value -->
A `DebugPort` owns an OS reservation that exactly one holder releases, so the share walk refuses it: every thread
would read a registry value at once, and a close on one would unmap the pages under another.

```maxon
default DebugPort = DebugPort.create()

function main() returns ExitCode
	print("{DebugPort.current().isReady()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:2:9: `DebugPort` cannot be a registry value or a `SharedValue`: it is a `DebugPort` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: error.a-process-trace-cannot-be-shared-between-threads -->
A `ProcessTrace` is refused as a `SharedValue` payload for the same reason.

```maxon
typealias SharedTrace = SharedValue with ProcessTrace

function main() returns ExitCode
	let shared = SharedTrace.create(ProcessTrace.create())
	print("{shared.current().isReady()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:2:25: `ProcessTrace` cannot be a registry value or a `SharedValue`: it is a `ProcessTrace` whose graph reaches a type this compiler synthesizes no per-type walk for — an OS handle or a base-struct-less generic instance — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```
