---
feature: debuggee-sampler
status: experimental
keywords: [debuggee, DebugPort, ProcessTrace, RegisterBlock, Sampler, SampledProcess, SampleTimer, HeapStats, SlabCensus, bounds]
category: system
---

# The debugger and profiler surface of the standard library

## Documentation

`DebugPort` (Windows) and `ProcessTrace` (Linux) drive a debuggee, and `SampledProcess` and `SampleTimer`
(Windows, x64) sample a process. Each owns a scratch region that `close()` releases; a call that needs the region
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
There is no process 4000000000, so opening it throws.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
The program samples itself: it has threads and modules, a thread it releases is no longer known to the sample, and
`close` releases the rest.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A timer waits for its next deadline, and `close` releases its handle once.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	var timer = try SampleTimer.create(1000000) otherwise return 1
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A wait on a closed timer is a bug in the caller, so it panics.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	var timer = try SampleTimer.create(1000000) otherwise return 1
	timer.close()
	timer.wait()

	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at Sampler.maxon:378: SampleTimer.wait: the timer was closed
Stack trace:
  in SampleTimer.wait
  in main
  in mrt_start
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- unsupported-targets: wasm32-wasi -->
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A `SampleTimer` that is never closed is dropped like any record: its timer handle is closed with it and the leak
gate finds nothing.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	_ = try SampleTimer.create(1000000) otherwise return 1
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A `SampledProcess` that is never closed is dropped like any record: its process handle, every thread handle it
opened and both scratch reservations are released with it, and the leak gate finds nothing.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	var sampled = try SampledProcess.open(Process.id()) otherwise return 1
	let threads = sampled.threads()
	print("opened {threads.count() > 0}\n")

	return 0
end 'main'
```
```stdout
opened true
```
```exitcode
0
```

<!-- test: a-sampled-process-with-a-released-thread-dropped-leaks-nothing -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A thread released early closes its handle once; the drop that follows releases only the handles still open.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	var sampled = try SampledProcess.open(Process.id()) otherwise return 1
	let threads = sampled.threads()
	let first = try threads.get(0) otherwise return 2
	sampled.release(first)
	sampled.release(first)

	print("released {threads.count() > 0}\n")

	return 0
end 'main'
```
```stdout
released true
```
```exitcode
0
```

<!-- test: a-closed-sampled-process-dropped-leaks-nothing -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
`close` is an early release, so the drop after it has only the boxes left to free.
`SampledProcess` and `SampleTimer` exist only on x64-windows, so the other lanes do not compile the program.

```maxon
function main() returns ExitCode
	var sampled = try SampledProcess.open(Process.id()) otherwise return 1
	_ = sampled.threads()
	sampled.close()
	sampled.close()
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
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
