---
feature: debug-agent
status: experimental
keywords: [debugger, debug-agent, MAXON_DEBUG, control-segment, breakpoint, stop-at-entry, orphan-guard, attach]
category: runtime
---

## Documentation

# The in-process debug agent

Every program the compiler emits carries a **debug agent**: a small body the entry stub calls before
`main`, which looks for a shared control segment named in the environment variable **`MAXON_DEBUG`** and,
finding one, attaches to it. The driver — `maxon debug` — is the other half: it creates the segment, fills
in the fields the agent reads, spawns the program with that variable set, and from then on speaks to the
agent through the same two pages. There is no OS debug API on either side, which is what lets the same
design serve four native lanes.

**A program with no `MAXON_DEBUG` in its environment is an ordinary program.** The agent reads one
environment variable, finds nothing, and returns; nothing else in the process differs. `--no-debug-agent`
is the build-time opt-out, and it is the only one — `--no-debug-info` withholds the `.mxdbg` sidecar and
changes no code byte.

**The segment's layout is version 1** and is two pages (8192 bytes) of 8-byte words. The driver writes
`magic` (ASCII `MXDBGCTL`), `driverVersion`, `stopAtEntry` and its own `driverPid` BEFORE the spawn; the
agent answers with `agentVersion`, `flags` (bit 0 `agentAlive`, written LAST), `agentPid`, and — once it
parks — `stopSeq`, `stopReason`, `textBase` and `textSize`. A command is `cmdArg*`, then `cmd`, then
`cmdSeq` LAST; the agent answers `ackSeq`, `cmdResult` (1 done, 0 refused) and, when refused, `cmdRefusal`.

**An attach that does not recognise the segment leaves the program running undebugged.** A wrong magic or
an unknown `driverVersion` is not an error the program reports: a debugger that cannot read the page has
no business stopping a program that was going to run correctly without it.

**A fault is a stop, and it is still terminal.** While a driver is attached, the four hardware faults the
runtime's own handler converts — an access violation, a stack overflow, an integer divide by zero and an
integer overflow — park the program exactly as a trap does: stop reason **6 `fault`**, the whole register
file, the faulting pc, and the exception code in `stopFault`. A step from it is refused **faultIsTerminal**
(13), because the instruction would only fault again; a continue hands the fault on to the runtime's
handler, which ends the program with the same panic line, backtrace and exit code it has with nothing
attached. Every other exception code is declined untouched.

**A parked agent watches the driver PROCESS and ends itself when that process is gone.** A driver that was
killed leaves a program stopped before `main` with nobody to resume it, so the agent opens a handle to the
pid the page names at attach and polls it while parked; a parked child whose driver has exited exits **97**.
The guard is liveness rather than a clock, because a driver at a prompt may think for as long as its user
does, and a driver that cannot open the named process at all stays dark rather than attaching.

**A stop is a stop of the whole program.** Before a word of it is published the agent takes the preemption
window (`__sched_preempt_ext_lock`), then `__sched_lock`, then suspends every machine on `__sched_allm`
but its own and reads each suspended thread's registers — a suspend on this host is a request, and the
read is what forces it to complete. Both locks are held across the park and released in the reverse order
after every resume, so no machine runs user code while a driver reads the page, no machine can be born
into a stop, and no thread can begin `ExitProcess` underneath one. A program with no scheduler has one
machine and one thread to stop.

**The mailbox is live while the program runs.** The agent starts one OS thread of its own at attach; it is
not a machine, holds no thread-local slot and calls no scheduler door, and it services the mailbox under
the same lock a parked stop holds, so a command is answered by the service thread or by the park loop and
never by both. Serviced while running: setting and clearing a breakpoint or a condition, reading memory,
listing, holding and releasing a green thread, and `pause`. A `continue`, a step or a backtrace posted
then is refused **notRunning** (11) — the program is running and nothing is stopped — which is a different
fact from **notStopped** (7), where a stop is in progress but no thread's registers were captured at it:
the entry park, and a `pause` of a program whose threads were all idle.

**`pause` asks rather than stops.** The agent samples every machine's current green thread; if none is
running user code the program is idle and the service thread takes the stop itself, reporting no pc, sp or
fp. Otherwise it reuses the preemption monitor's protocol per machine — claim the machine's signal word,
take the window, suspend, write the cooperative request into the green thread's guard word *while the
thread is suspended*, read its registers, ask `__gt_preempt_safe`, and on a safe instant inject the same
way asynchronous preemption does, onto an `int3; ret` trampoline the agent owns. That trap is stop reason
**4 `pause`**, reporting the interrupted pc. A program that reaches no safe point within the attempt
budget is refused **notPausable** (12).

**A condition is evaluated in the agent, before anything is stopped.** The driver writes an eight-word
record — kind, operand, width, signedness, operator, literal — into the segment and names the breakpoint
by its text offset; the agent copies it into the entry with the kind last, so a trap taken on another
machine reads a whole record or none of it. A hit whose condition is false is resumed out of line and
costs one trap, and a record the agent cannot evaluate admits the stop: a dropped hit is invisible where
an extra stop is not.

**Green threads are visible by address.** The scheduler's carve arm registers every green-thread record it
mints with the agent, which is the only moment one comes into existence; records are never freed, so an
address outlives the threads that occupy it and every read derives the life afresh. `gtList` (9) fills the
page with the live records — `running` is derived from the machines, a record with no stack or a completed
one is omitted — and `gtBacktrace` (10) walks a parked thread from its own saved frame pointer, refusing
**threadIsRunning** (10) for one whose registers are in another machine.

**A hold moves nothing.** `gtHold` (11) records the thread's address with its entry function and stack
base as witnesses; the scheduler then asks the agent at every hand-over and puts a refused candidate back
on the tail of the queue it came from, pausing before it looks again. So a held thread stays inside the
scheduler's own structures, `__sched_checkdead` keeps seeing a machine running, `gtRelease` (12) needs no
wake, and a hold on a life that has ended lapses instead of catching the thread that now occupies the
address. Holding the thread a machine is running is refused, as is an address no live record answers to
(**noSuchThread**, 8) and a seventeenth hold (**holdTableFull**, 9).

**An `int3` the agent does not own is a stop, never an exit.** A trap matching no live or retired
breakpoint, no out-of-line slot and not the pause trampoline is published as stop reason **5**, at the
faulting pc, and resumes at the byte after it. Declining it would hand the exception to a chain that does
not want it either and kill the program with the address printed nowhere.

**The agent reads the program's own bytes, never the planted ones.** A slot fill and a memory read inside
the image both answer through the breakpoint table, so a driver disassembling what came back sees the
program it built.

**Targets — x64-windows only, until the lane stage.** The agent's trap thunk, its installer and its
out-of-line slot pool are hand-assembled per architecture, and on every other native lane the entry stub
mints no call into the agent at all, so there is nothing to attach; `wasm32-wasi` can host no child
process, which is what the cases below are built out of. The other four lanes therefore carry an
`unsupported-targets` marker rather than a skip.

## Tests

<!-- test: debug-agent.a-child-named-a-segment-stamps-its-version-and-pid -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A child handed a well-formed segment stamps its version, its liveness bit and its own process id

⭐⭐ **THE PAGE IS THE ONLY CHANNEL, AND THIS IS THE FLOOR UNDER EVERY OTHER CASE.** The program below is
both halves: run with no argument it creates the segment, fills in the four driver words, and re-spawns
ITSELF with `MAXON_DEBUG` naming that segment; run with one it is the debuggee. `stopAtEntry` is 0, so the
child never parks — it attaches, stamps, and runs to its own exit code, which the parent reads back beside
the words the agent left.

⛔ **THE PID IS COMPARED, NOT MERELY ASKED TO BE NONZERO.** `CollectedOutput.pid` is the process the parent
actually spawned, and an `agentPid` that is any other number is an agent that stamped a page belonging to
somebody else — which is exactly the failure a nonzero test cannot see.

⚠ **`flags` IS ASKED FOR BIT 0 RATHER THAN FOR EQUALITY**, because the bit is written LAST at attach and
the rest of the word is the agent's to use.
```maxon
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-stamp"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let AgentVersionOffset = 16 as SegmentOffset
let FlagsOffset = 24 as SegmentOffset
let AgentPidOffset = 32 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset

let DriverVersion = 1
let AgentAliveBit = 1
let RunToCompletion = 0
let ChildExitCode = 5

let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating([DebugVariableName: segment.segmentName()])
	let run = try Subprocess.runConfiguration(config) otherwise return 9

	let version = try segment.readWord(AgentVersionOffset) otherwise return 10
	let flags = try segment.readWord(FlagsOffset) otherwise return 11
	let pid = try segment.readWord(AgentPidOffset) otherwise return 12
	segment.close()

	print("version={version} alive={flags and AgentAliveBit} samePid={pid == run.pid as SegmentWord} child={run.exitCode()}\n")

	return 0
end 'main'
```
```stdout
version=1 alive=1 samePid=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.an-unset-variable-leaves-the-page-untouched -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A child spawned without the variable leaves every agent word at zero

⭐⭐ **THE CONTROL.** The page above is written by the agent, and a page written by ANYTHING ELSE — the
runtime, a previous run, the allocator reaching a mapping it does not own — would satisfy that case
without an agent existing. Here the very same segment is created and the very same child is spawned, with
only `MAXON_DEBUG` withheld: every agent word must still be zero.
```maxon
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-unset"
let SegmentBytes = 8192
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let AgentVersionOffset = 16 as SegmentOffset
let FlagsOffset = 24 as SegmentOffset
let AgentPidOffset = 32 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0
let ChildExitCode = 5

let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	let run = try Subprocess.runConfiguration(config) otherwise return 9

	let version = try segment.readWord(AgentVersionOffset) otherwise return 10
	let flags = try segment.readWord(FlagsOffset) otherwise return 11
	let pid = try segment.readWord(AgentPidOffset) otherwise return 12
	segment.close()

	print("version={version} flags={flags} pid={pid} child={run.exitCode()}\n")

	return 0
end 'main'
```
```stdout
version=0 flags=0 pid=0 child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-bad-magic-leaves-the-program-running-undebugged -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A segment whose magic is wrong leaves the program running, and stamps nothing

⛔ **A DEBUGGER THAT CANNOT READ THE PAGE MUST NOT STOP A PROGRAM THAT WOULD HAVE RUN CORRECTLY.** The
variable names a real, mapped segment here — so the agent gets as far as reading the first word — and that
word is not the magic. The child must run to its own exit code and leave the page alone, which is the only
answer that keeps a stale segment name in somebody's environment from bricking an unrelated program.
```maxon
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-badmagic"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let AgentVersionOffset = 16 as SegmentOffset
let FlagsOffset = 24 as SegmentOffset
let AgentPidOffset = 32 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let ChildExitCode = 5
let MagicSpoiler = 1

let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic() + MagicSpoiler) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating([DebugVariableName: segment.segmentName()])
	let run = try Subprocess.runConfiguration(config) otherwise return 9

	let version = try segment.readWord(AgentVersionOffset) otherwise return 10
	let flags = try segment.readWord(FlagsOffset) otherwise return 11
	let pid = try segment.readWord(AgentPidOffset) otherwise return 12
	segment.close()

	print("version={version} flags={flags} pid={pid} child={run.exitCode()}\n")

	return 0
end 'main'
```
```stdout
version=0 flags=0 pid=0 child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-fault-is-still-terminal-while-attached -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### An attached child that faults stops, cannot be stepped past the fault, and after a continue still panics, prints its backtrace and exits 1

⭐⭐ **A FAULT IS A STOP, AND IT IS STILL TERMINAL.** The agent parks on the fault so a debugger can see
where it happened, but it cannot make the faulting instruction succeed: a one-instruction step would
execute it again. So a `stepInstruction` at a fault stop is refused by reason (**faultIsTerminal**, 13)
rather than obeyed, and a continue hands the fault on to the runtime's own handler, which reports it
exactly as it does with nothing attached. The child below calls `__Builtins.forceSegfault()` the way
`specs/safety.md`'s `force-segfault` case spells it, with the agent attached and `stopAtEntry` 0; the parent
reads the stop, is refused the step, continues, and then reads back that case's exit code, its first stderr
line and the frame its backtrace opens with.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-fault"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"
let PanicLine = "panic: nil pointer or invalid memory access"
let FaultingFrame = "in maxon_force_segfault"
let LineBreak = "\n"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0
let StepInstructionCommandCode = 6
let ContinueCommandCode = 3
let FaultStopReason = 6
let CommandRefused = 0
let CommandDone = 1
let FaultIsTerminalRefusal = 13
let FirstStop = 1
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let NoArgument = 0

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function drainedStderr(child StreamingSubprocess) returns String
	var text = ""
	var reading = true

	while reading 'eachLine'
		let line = try child.readStderrLine() otherwise ""

		if line.isEmpty() 'endOfStream'
			reading = false
			continue
		end 'endOfStream'

		text.append("{line}{LineBreak}")
	end 'eachLine'

	return text
end 'drainedStderr'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		__Builtins.forceSegfault()
		return 0
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 11

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 12
	try segment.writeWord(CmdOffset, value: StepInstructionCommandCode) otherwise return 13
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 14

	let sawTheStep = awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let stepResult = try segment.readWord(CmdResultOffset) otherwise return 15
	let stepRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 16

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 17
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 18

	let sawTheContinue = awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)
	let continueResult = try segment.readWord(CmdResultOffset) otherwise return 19

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	let said = drainedStderr(child)
	child.release()
	segment.close()

	print("stopped={stopped and reason == FaultStopReason} stepRefused={sawTheStep and stepResult == CommandRefused and stepRefusal == FaultIsTerminalRefusal} continued={sawTheContinue and continueResult == CommandDone} child={code} panicked={said.contains(PanicLine)} backtrace={said.contains(FaultingFrame)}\n")

	return 0
end 'main'
```
```stdout
stopped=true stepRefused=true continued=true child=1 panicked=true backtrace=true
```
```exitcode
0
```

<!-- test: debug-agent.a-fault-stops-a-debugged-program -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A debugged program that faults stops at the faulting instruction, names the fault, and ends as it would undebugged once continued

⭐⭐ **EVERY DEBUGGER STOPS ON AN UNHANDLED FAULT, BECAUSE IT IS THE ONE MOMENT THE STACK IS WORTH MOST.** An
agent that let the runtime's handler take the fault would end the program with its backtrace printed to a
stream and nothing left to inspect. Here nothing is parked and no breakpoint is armed; the child faults on
its own, and the parent sees a stop with reason **6 `fault`**, the exception code the host raised in
`stopFault` (0xC0000005, an access violation), and a pc inside the program's own code. A continue is then
acknowledged, and the child exits with the runtime's panic exit code and opens its stderr with the same
panic line `specs/safety.md`'s `force-segfault` case pins with nothing attached.

⚠ **THE PC IS ASKED TO BE INSIDE `[textBase, textBase + textSize)` AND NOTHING MORE.** Which instruction
of the probe faults is a fact about a build; that the stop reports the program's own code rather than
the agent's, the host's or zero is the claim.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-faultstop"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"
let PanicLine = "panic: nil pointer or invalid memory access"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset
let StopPcOffset = 176 as SegmentOffset
let StopFaultOffset = 200 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0
let ContinueCommandCode = 3
let CommandDone = 1
let AccessViolationCode = 0xC0000005
let FirstStop = 1
let FirstCommandSequence = 1
let NoArgument = 0

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		__Builtins.forceSegfault()
		return 0
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 11
	let fault = try segment.readWord(StopFaultOffset) otherwise return 12
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 13
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 14
	let stopPc = try segment.readWord(StopPcOffset) otherwise return 15

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 16
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 17
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 18

	let resumed = awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let resumeResult = try segment.readWord(CmdResultOffset) otherwise return 19

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	let firstLine = try child.readStderrLine() otherwise ""
	child.release()
	segment.close()

	print("stopped={stopped} reason={reason} accessViolation={fault == AccessViolationCode} inText={textBase != 0 and stopPc >= textBase and stopPc < textBase + textSize} resumed={resumed and resumeResult == CommandDone} child={code} panicked={firstLine.startsWith(PanicLine)}\n")

	return 0
end 'main'
```
```stdout
stopped=true reason=6 accessViolation=true inText=true resumed=true child=1 panicked=true
```
```exitcode
0
```

<!-- test: debug-agent.a-parked-child-obeys-continue -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A child parked at entry publishes its stop, takes a continue, and runs to its own exit code

⭐⭐ **THIS IS THE WHOLE MAILBOX, IN ONE PASS.** `stopAtEntry` is 1, so the child parks before `main` and
publishes `stopSeq` LAST, after `stopReason`, `textBase` and `textSize` — which is why the parent may read
those three the moment the sequence moves. The parent then writes the command's arguments, then `cmd`,
then `cmdSeq` LAST, and waits for `ackSeq` to reach it. Both orders are the same rule: **the sequence word
is the publication, and everything it describes is written before it.**

⛔ **THE PARENT IS THE DRIVER THE AGENT WATCHES.** It writes its own pid into the page before the spawn and
outlives the child, so the orphan guard below never fires here however long the parent takes to answer.

⚠ **`textBase` AND `textSize` ARE ASKED TO BE NONZERO AND NOTHING MORE.** Where an image lands and how big
its code is are facts about a build, and a case that pinned either would be re-blessed on every change to
anything.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-park"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let EntryStopReason = 1
let ContinueCommandCode = 3
let CommandDone = 1
let FirstCommandSequence = 1
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: FirstCommandSequence)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 11
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 12
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 13

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 14
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 15
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 16

	awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let acked = try segment.readWord(AckSeqOffset) otherwise return 17
	let result = try segment.readWord(CmdResultOffset) otherwise return 18

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("parked={reason == EntryStopReason} text={textBase != 0 and textSize != 0} acked={acked} result={result == CommandDone} child={code}\n")

	return 0
end 'main'
```
```stdout
parked=true text=true acked=1 result=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.an-orphaned-parked-child-exits-97 -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A parked child whose driver process has exited ends itself with 97

⭐⭐ **THIS IS THE ONLY ORPHAN GUARD THAT SURVIVES A KILLED DRIVER.** A child parked before `main` runs no
code of its own and answers nothing; without this it is a process that waits forever, holding its image,
its console and whatever it had opened.

⭐⭐ **THREE PROCESSES, BECAUSE THE GUARD WATCHES A PROCESS.** The top level creates the page and spawns a
DRIVER, which stamps its OWN pid into word 40 and then waits for the child's entry stop; the top level
spawns the CHILD against that same page, and the driver exits as soon as the child is parked — WITHOUT
continuing it. The top level is the one that holds the child's handle, which is what makes the exit code
an observation rather than a guess: it cannot be the driver itself, because the process the agent watches
has to die while somebody is still alive to read what the child did about it.

⚠ **THE DRIVER EXITS ONLY ONCE THE CHILD HAS PUBLISHED ITS STOP**, so the attach — one `OpenProcess` of the
stamped pid — happens while the driver is unmistakably alive. A driver that vanished FIRST would leave the
child dark and running to its own exit code, which is a different answer and is why both are printed.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-orphan"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let DriverArgument = "driver"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let FirstStop = 1
let SomePid = 1
let NoDriverYet = 0
let ChildExitCode = 5
let OrphanedExitCode = 97

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let DeadlineMs = 60000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function runDriver() returns ExitCode
	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 21
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 22
	let parked = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	segment.close()

	return 0 if parked else 23
end 'runDriver'

function main() returns ExitCode
	let arguments = CommandLine.args()

	if arguments.count() > 1 'aSpawnedRole'
		let role = try arguments.get(1) otherwise return 2

		if role == DriverArgument 'iAmTheDriver'
			return runDriver()
		end 'iAmTheDriver'

		return ChildExitCode
	end 'aSpawnedRole'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: NoDriverYet) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var driverArgv = StringArray.create()
	driverArgv.push(DriverArgument)
	var driver = try StreamingSubprocess.spawn(Executable.path(me), arguments: driverArgv) otherwise return 10

	if not awaitWord(segment, offset: DriverPidOffset, atLeast: SomePid) 'theDriverNeverStamped'
		return 11
	end 'theDriverNeverStamped'

	var childArgv = StringArray.create()
	childArgv.push(ChildArgument)
	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: childArgv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 12

	let driverCode = try driver.waitWithTimeout(DeadlineMs) otherwise 98
	driver.release()

	let code = try child.waitWithTimeout(DeadlineMs) otherwise 99
	child.release()
	segment.close()

	print("driver={driverCode} child={code} ownExit={code == ChildExitCode} guarded={code == OrphanedExitCode}\n")

	return 0
end 'main'
```
```stdout
driver=0 child=97 ownExit=false guarded=true
```
```exitcode
0
```

<!-- test: debug-agent.an-unknown-command-is-refused-by-reason -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A command code the agent does not know is refused with a reason, and the session goes on

⛔ **A REFUSAL IS AN ANSWER AND MUST BE ACKNOWLEDGED LIKE ANY OTHER.** An agent that ignored a command it
did not recognise would leave `ackSeq` behind `cmdSeq` forever, and the driver — which cannot tell a
refusal from a hang — would time the session out and kill a program that is perfectly healthy. So the
unknown command below is acknowledged with `cmdResult` 0 and `cmdRefusal` 6, and the continue that
follows it is obeyed.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-refuse"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let ContinueCommandCode = 3
let UnknownCommandCode = 99
let UnknownCommandRefusal = 6
let CommandRefused = 0
let CommandDone = 1
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: FirstCommandSequence)

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 11
	try segment.writeWord(CmdOffset, value: UnknownCommandCode) otherwise return 12
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 13

	awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let refusedResult = try segment.readWord(CmdResultOffset) otherwise return 14
	let refusal = try segment.readWord(CmdRefusalOffset) otherwise return 15

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 16
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 17

	awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)
	let resumedResult = try segment.readWord(CmdResultOffset) otherwise return 18

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("refused={refusedResult == CommandRefused} unknownCommand={refusal == UnknownCommandRefusal} resumed={resumedResult == CommandDone} child={code}\n")

	return 0
end 'main'
```
```stdout
refused=true unknownCommand=true resumed=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-read-of-the-programs-own-code-comes-back -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `readMemory` of the program's own code answers with the bytes and a readable status

⭐⭐ **THE AGENT READS THE DEBUGGEE'S MEMORY FOR THE DRIVER, AND `textBase` IS THE ONE ADDRESS EVERY BUILD
HAS.** The child parks at entry and publishes where its code begins; the parent asks for the first bytes of
it and gets `readStatus` 1 and exactly the length it asked for. Asking for the CODE rather than for a
number the test planted is what keeps the case from passing against an agent that echoes its own argument.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-read"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let CmdArg1Offset = 80 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let ReadLenOffset = 1936 as SegmentOffset
let ReadStatusOffset = 1944 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let ReadMemoryCommandCode = 5
let ContinueCommandCode = 3
let CommandDone = 1
let ReadableStatus = 1
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let RequestedBytes = 16
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: FirstCommandSequence)
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 11

	try segment.writeWord(CmdArg0Offset, value: textBase) otherwise return 12
	try segment.writeWord(CmdArg1Offset, value: RequestedBytes) otherwise return 13
	try segment.writeWord(CmdOffset, value: ReadMemoryCommandCode) otherwise return 14
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 15

	awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let result = try segment.readWord(CmdResultOffset) otherwise return 16
	let status = try segment.readWord(ReadStatusOffset) otherwise return 17
	let length = try segment.readWord(ReadLenOffset) otherwise return 18

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 19
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 20
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 21

	awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("acked={result == CommandDone} readable={status == ReadableStatus} length={length} child={code}\n")

	return 0
end 'main'
```
```stdout
acked=true readable=true length=16 child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-read-of-an-unmapped-address-is-answered-not-readable -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `readMemory` of an address nothing maps is answered, not faulted

⛔ **AN AGENT THAT FAULTED ON A BAD ADDRESS WOULD KILL THE PROGRAM ITS DRIVER WAS INSPECTING.** A debugger
is asked for the memory a pointer names constantly, and a pointer under inspection is exactly the one most
likely to be wrong — so the read is a REFUSAL OF THE RANGE, not a refusal of the command: `cmdResult` is
still 1, `readStatus` is 0 and `readLen` is 0. The address below is inside the first 64 KiB, which no
Windows process may map.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-unread"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let CmdArg1Offset = 80 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let ReadLenOffset = 1936 as SegmentOffset
let ReadStatusOffset = 1944 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let ReadMemoryCommandCode = 5
let ContinueCommandCode = 3
let CommandDone = 1
let UnreadableStatus = 0
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let RequestedBytes = 16
let NullPageAddress = 16
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: FirstCommandSequence)

	try segment.writeWord(CmdArg0Offset, value: NullPageAddress) otherwise return 11
	try segment.writeWord(CmdArg1Offset, value: RequestedBytes) otherwise return 12
	try segment.writeWord(CmdOffset, value: ReadMemoryCommandCode) otherwise return 13
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 14

	awaitWord(segment, offset: AckSeqOffset, atLeast: FirstCommandSequence)
	let result = try segment.readWord(CmdResultOffset) otherwise return 15
	let status = try segment.readWord(ReadStatusOffset) otherwise return 16
	let length = try segment.readWord(ReadLenOffset) otherwise return 17

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 18
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 19
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 20

	awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("acked={result == CommandDone} readable={status != UnreadableStatus} length={length} child={code}\n")

	return 0
end 'main'
```
```stdout
acked=true readable=false length=0 child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-breakpoint-the-agent-will-not-place-is-refused-by-reason -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A breakpoint outside the code, inside the runtime's own code, or unclassified is refused by reason

⛔⛔ **A BREAKPOINT IS A WRITE INTO EXECUTABLE MEMORY AND EVERY REFUSAL HERE IS A WRITE THE AGENT DECLINED
TO MAKE.** An offset past the code writes into whatever follows `.text`; an offset inside a runtime symbol
plants a trap in the very code the trap handler runs on; a class of 0 means the driver could not classify
the instruction, so the agent has no way to resume past it out of line. The three are asked in one session
because each must be answered and acknowledged like any other command.

⚠ **OFFSET 0 IS `mrt_start` ON EVERY BUILD**, which is what makes the runtime-symbol refusal askable without
knowing anything about this program.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-refusebp"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let CmdArg1Offset = 80 as SegmentOffset
let CmdArg2Offset = 88 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let SetBreakpointCommandCode = 1
let ContinueCommandCode = 3
let CommandRefused = 0
let OutOfTextRefusal = 1
let InsideRuntimeSymbolRefusal = 2
let UnclassifiedRefusal = 3
let PlainInstructionClass = 1 shl 8
let OneByteInstruction = 1
let UnclassifiedClassWord = 0
let RuntimeSymbolOffset = 0
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: 1)
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 11

	try segment.writeWord(CmdArg0Offset, value: textSize) otherwise return 12
	try segment.writeWord(CmdArg1Offset, value: PlainInstructionClass + OneByteInstruction) otherwise return 13
	try segment.writeWord(CmdArg2Offset, value: NoArgument) otherwise return 14
	try segment.writeWord(CmdOffset, value: SetBreakpointCommandCode) otherwise return 15
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 16

	awaitWord(segment, offset: AckSeqOffset, atLeast: 1)
	let pastTheCode = try segment.readWord(CmdRefusalOffset) otherwise return 17
	let pastResult = try segment.readWord(CmdResultOffset) otherwise return 18

	try segment.writeWord(CmdArg0Offset, value: RuntimeSymbolOffset) otherwise return 19
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 20

	awaitWord(segment, offset: AckSeqOffset, atLeast: 2)
	let inTheRuntime = try segment.readWord(CmdRefusalOffset) otherwise return 21

	try segment.writeWord(CmdArg1Offset, value: UnclassifiedClassWord) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 23

	awaitWord(segment, offset: AckSeqOffset, atLeast: 3)
	let unclassified = try segment.readWord(CmdRefusalOffset) otherwise return 24

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 25
	try segment.writeWord(CmdSeqOffset, value: 4) otherwise return 26

	awaitWord(segment, offset: AckSeqOffset, atLeast: 4)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("refused={pastResult == CommandRefused} outOfText={pastTheCode == OutOfTextRefusal} runtimeSymbol={inTheRuntime == InsideRuntimeSymbolRefusal} unclassified={unclassified == UnclassifiedRefusal} child={code}\n")

	return 0
end 'main'
```
```stdout
refused=true outOfText=true runtimeSymbol=true unclassified=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-backtrace-asked-before-main-is-refused -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A backtrace asked at the entry stop is refused, because no thread was stopped where it stood

⛔ **THE ENTRY STOP IS A PARK, NOT A TRAP, AND IT HAS NO REGISTER FILE.** The agent reaches it by calling its
own park loop before `main`, so there is no interrupted frame to walk and no saved frame pointer to start
from — and answering with the agent's OWN frames would hand the driver a backtrace of the debugger. The
refusal is `notStopped`, and the session goes on: the continue after it is obeyed.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-btentry"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let BacktraceCommandCode = 4
let ContinueCommandCode = 3
let CommandRefused = 0
let CommandDone = 1
let NotStoppedRefusal = 7
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: 1)

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 11
	try segment.writeWord(CmdOffset, value: BacktraceCommandCode) otherwise return 12
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 13

	awaitWord(segment, offset: AckSeqOffset, atLeast: 1)
	let walked = try segment.readWord(CmdResultOffset) otherwise return 14
	let refusal = try segment.readWord(CmdRefusalOffset) otherwise return 15

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 16
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 17

	awaitWord(segment, offset: AckSeqOffset, atLeast: 2)
	let resumed = try segment.readWord(CmdResultOffset) otherwise return 18

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("refused={walked == CommandRefused} notStopped={refusal == NotStoppedRefusal} resumed={resumed == CommandDone} child={code}\n")

	return 0
end 'main'
```
```stdout
refused=true notStopped=true resumed=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-stop-suspends-every-other-machine -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A stop suspends every other machine, so the program makes no progress while it is parked

⭐⭐ **STOP-THE-WORLD IS THE ONE PROPERTY A DEBUGGER CANNOT DEMONSTRATE BY STOPPING.** A stop that suspended
only the trapping machine looks identical from the driver's side — a stop arrives, the page is readable,
the commands are answered — while the other machines keep running user code underneath. The only thing
that tells the two apart is PROGRESS: the child below runs two green threads on two processors, each
bumping a counter in a loop, and the parent reads that counter twice over the same span of wall time
BEFORE the stop and twice again DURING it. Without stop-the-world the second pair moves exactly like the
first.

⛔ **THE FIRST PAIR IS THE CONTROL AND IT IS NOT OPTIONAL.** A counter that is frozen because both workers
happened to be asleep, or had already finished, would satisfy the claim while saying nothing. `moving`
is the statement that this window is long enough to see the program run.

⚠ **THE COUNTER LIVES IN THE CONTROL PAGE, AT THE WORD AT 0x9F0** — two words the layout reserves for a
case's own use, past the condition record and the green-thread tallies and short of the record array at
0xA00. A spec program cannot take the ADDRESS of one of its own variables, and `SharedSegment`'s mapped
base is private to the stdlib, so a counter the parent could ask `readMemory` for cannot be published
from Maxon at all; the shared page is the one place both processes can name the same word.

⚠ **A PAUSE MAY LAND ON EITHER ROAD, SO WHAT IS ASKED IS THAT THE TWO WORDS AGREE.** A machine caught
between two green threads is idle by definition, and a pause that samples it there is answered by the
agent's own service thread with no machine trapped and no pc — so `machine` asks that `stopMachine` and
`stopPc` are zero together or nonzero together, which is true of both roads and false of a stop that named
a thread it did not trap. Freezing is what this case is about, and stop-the-world freezes on either road.

⚠ **`MAXON_MAX_PROCS` REACHES THE CHILD THROUGH ITS ENVIRONMENT, NOT THROUGH THE `procs:` MARKER**, which
applies to the case's own program. The child is spawned with both variables set.
```maxon
typealias Millis = int(0 to i64.max)
typealias Progress = int(0 to 1000000)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-stw"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let TwoProcessors = "2"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset
let StopMachineOffset = 136 as SegmentOffset
let StopPcOffset = 176 as SegmentOffset
let CaseScratchOffset = 2544 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0
let PauseCommandCode = 13
let ContinueCommandCode = 3
let PauseStopReason = 4
let FirstStop = 1
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let NoArgument = 0
let NoProgressYet = 0
let ChildExitCode = 5

let WorkerMs = 7000 as Millis
let WorkerStepMs = 1 as Millis
let SettleMs = 400 as Millis
let ObserveMs = 200 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function bump() returns Progress
	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 0
	var turns = 0
	let deadline = (Clock.nowMs() as Millis) + WorkerMs

	while (Clock.nowMs() as Millis) < deadline 'grind'
		let seen = try segment.readWord(CaseScratchOffset) otherwise 0
		try segment.writeWord(CaseScratchOffset, value: seen + 1) otherwise ignore
		sleep(WorkerStepMs)
		turns = turns + 1
	end 'grind'

	segment.close()

	return 1 if turns > 0 else 0
end 'bump'

function runChild() returns ExitCode
	let first = async bump()
	let second = async bump()
	let a = await first
	let b = await second

	return ChildExitCode if a + b > 0 else 0
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return runChild()
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7
	try segment.writeWord(CaseScratchOffset, value: NoProgressYet) otherwise return 8

	let me = try Process.executablePath() otherwise return 9
	let here = try FilePath.from("") otherwise return 10
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: TwoProcessors])) otherwise return 11

	sleep(SettleMs)
	let before = try segment.readWord(CaseScratchOffset) otherwise return 12
	sleep(ObserveMs)
	let after = try segment.readWord(CaseScratchOffset) otherwise return 13

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 14
	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 15
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 16

	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 17
	let machine = try segment.readWord(StopMachineOffset) otherwise return 18
	let stopPc = try segment.readWord(StopPcOffset) otherwise return 23

	let atTheStop = try segment.readWord(CaseScratchOffset) otherwise return 19
	sleep(ObserveMs)
	let stillAtTheStop = try segment.readWord(CaseScratchOffset) otherwise return 20

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 21
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 22
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("moving={after > before} paused={stopped and reason == PauseStopReason} machine={machine != 0 and stopPc != 0 or machine == 0 and stopPc == 0} frozen={stillAtTheStop == atTheStop} child={code}\n")

	return 0
end 'main'
```
```stdout
moving=true paused=true machine=true frozen=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-pause-stops-a-running-program -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `pause` interrupts a program that plants no trap of its own, inside its own code

⭐⭐ **A PROGRAM THAT NEVER STOPS IS THE ONE A DEBUGGER IS MOST OFTEN POINTED AT, AND STAGE 1 COULD NOT
REACH IT.** Nothing is parked here — `stopAtEntry` is 0 — so the mailbox is serviced by the agent's own
service thread while the child spins. The pause is a request the machine grants at a point of its
choosing, so the case asks only that the stop ARRIVED, that it is reported as a `pause`, and that the pc
it publishes is inside the program's own code.

⚠ **THE PC IS ASKED TO BE INSIDE `[textBase, textBase + textSize)` AND NOTHING MORE.** Which instruction a
spinning loop was on is a fact about a build and a moment; that the interruption landed in the program
rather than in a runtime body or in the agent is the claim.

⚠ **THE SPIN CALLS NOTHING, WHICH IS WHAT MAKES THAT ANSWER DETERMINISTIC.** A pause lands only where
`__gt_preempt_safe` admits the pc, and an attempt it refuses leaves a yield request behind. A loop that
spends its time inside a clock call is refused poll after poll, and a poll that samples the machine
between green threads is answered by the service thread with no pc at all. So the inner loop is bare
arithmetic, and the scratch word is read once every `TurnsPerLook` turns: the parent writes `Released`
there once its `continue` is acknowledged, and the child exits 5 only on seeing it. `LookLimit` bounds a
child whose parent never writes it.

⚠ **THE PAUSE IS POSTED ONLY ONCE THE CHILD SAYS IT IS SPINNING.** The child writes `Spinning` into the
case's scratch word at 0x9F0 on the instruction before it enters its loop, and the parent waits for that
word rather than for a fixed settling time: a pause that arrives while the child is still starting up is
asking a question about startup, not about a running program, and the answer it gets is a fact about the
machine's load rather than about the agent.
```maxon
typealias Millis = int(0 to i64.max)
typealias Spin = int(0 to 1)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-pause"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset
let StopPcOffset = 176 as SegmentOffset
let CaseScratchOffset = 2544 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0
let PauseCommandCode = 13
let ContinueCommandCode = 3
let PauseStopReason = 4
let CommandDone = 1
let FirstStop = 1
let FirstCommandSequence = 1
let SecondCommandSequence = 2
let NoArgument = 0
let ChildExitCode = 5
let NotSpinningYet = 0
let Spinning = 1
let Released = 2

let TurnsPerLook = 1000000
let LookLimit = 100000
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function spin(segment SharedSegment) returns Spin
	var looks = 0

	while looks < LookLimit 'look'
		let word = try segment.readWord(CaseScratchOffset) otherwise return 0

		if word == Released 'letGo'
			return 1
		end 'letGo'

		var turns = 0

		while turns < TurnsPerLook 'grind'
			turns = turns + 1
		end 'grind'

		looks = looks + 1
	end 'look'

	return 0
end 'spin'

function runChild() returns Spin
	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 0
	try segment.writeWord(CaseScratchOffset, value: Spinning) otherwise ignore
	let released = spin(segment)
	segment.close()

	return released
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode if runChild() > 0 else 0
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: RunToCompletion) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7
	try segment.writeWord(CaseScratchOffset, value: NotSpinningYet) otherwise return 21

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 10

	let spinning = awaitWord(segment, offset: CaseScratchOffset, atLeast: Spinning)

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 11
	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 12
	try segment.writeWord(CmdSeqOffset, value: FirstCommandSequence) otherwise return 13

	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 14
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 15
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 16
	let stopPc = try segment.readWord(StopPcOffset) otherwise return 17

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 18
	try segment.writeWord(CmdSeqOffset, value: SecondCommandSequence) otherwise return 19

	let resumed = awaitWord(segment, offset: AckSeqOffset, atLeast: SecondCommandSequence)
	let resumeResult = try segment.readWord(CmdResultOffset) otherwise return 20
	try segment.writeWord(CaseScratchOffset, value: Released) otherwise return 22

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("paused={spinning and stopped and reason == PauseStopReason} inText={textBase != 0 and stopPc >= textBase and stopPc < textBase + textSize} resumed={resumed and resumeResult == CommandDone} child={code}\n")

	return 0
end 'main'
```
```stdout
paused=true inText=true resumed=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-pause-of-an-idle-program-is-answered-without-a-thread -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `pause` of a program whose machines are all idle is answered with a stop that names no thread

⭐⭐ **A PROGRAM WITH NOTHING TO INTERRUPT IS STILL A PROGRAM A DEBUGGER MAY STOP.** Every green thread
below is parked — three workers asleep and `main` awaiting them — so no machine is executing an
instruction of the program and there is no pc to report. The agent's own service thread declares the stop
instead, and the case pins what such a stop says: reason `pause`, a stop pc of 0, and a stop machine of 0.

⛔ **A PC OF 0 IS THE CONTRACT, NOT A FAILURE TO FIND ONE.** A driver that reads `stopPc` must be able to
tell "the program was nowhere" from "the program was somewhere I could not name", so the no-thread stop
publishes an explicit zero for the pc and for the machine — the servicer's own thread trapped nothing and
naming it would read as a machine the driver could ask about — and clears the register file behind it; `backtrace` is refused rather than
answered with a walk of whatever the agent's own thread was doing.

⚠ **WHAT SUCH A STOP CAN STILL ANSWER IS THE ROSTER.** `gtList` is built by the runtime as records are
carved, not from a register file, so it is correct at a stop that has no thread — and it is the whole of
what a debugger has to work with here.
```maxon
typealias Millis = int(0 to i64.max)
typealias Nap = int(0 to 1)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-idlepause"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let StopReasonOffset = 128 as SegmentOffset
let StopMachineOffset = 136 as SegmentOffset
let StopPcOffset = 176 as SegmentOffset
let GtCountOffset = 2528 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let ContinueCommandCode = 3
let BacktraceCommandCode = 4
let GtListCommandCode = 9
let PauseCommandCode = 13
let CommandRefused = 0
let CommandDone = 1
let NotStoppedRefusal = 7
let PauseStopReason = 4
let NoPc = 0
let NoMachine = 0
let FirstStop = 1
let SecondStop = 2
let ContinueSequence = 1
let PauseSequence = 2
let BacktraceSequence = 3
let ListSequence = 4
let ResumeSequence = 5
let NoArgument = 0
let ChildExitCode = 5
let WorkerCount = 3

let NapMs = 3000 as Millis
let SettleMs = 400 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function napper() returns Nap
	sleep(NapMs)

	return 1
end 'napper'

function runChild() returns ExitCode
	let a = async napper()
	let b = async napper()
	let c = async napper()
	let x = await a
	let y = await b
	let z = await c

	return ChildExitCode if x + y + z == WorkerCount else 0
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return runChild()
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 10

	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 11
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 12
	try segment.writeWord(CmdSeqOffset, value: ContinueSequence) otherwise return 13
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: ContinueSequence)

	sleep(SettleMs)

	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 14
	try segment.writeWord(CmdSeqOffset, value: PauseSequence) otherwise return 15

	let acked = awaitWord(segment, offset: AckSeqOffset, atLeast: PauseSequence)
	let pauseResult = try segment.readWord(CmdResultOffset) otherwise return 16
	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: SecondStop)
	let reason = try segment.readWord(StopReasonOffset) otherwise return 17
	let stopPc = try segment.readWord(StopPcOffset) otherwise return 18
	let machine = try segment.readWord(StopMachineOffset) otherwise return 19

	try segment.writeWord(CmdOffset, value: BacktraceCommandCode) otherwise return 20
	try segment.writeWord(CmdSeqOffset, value: BacktraceSequence) otherwise return 21

	let walked = awaitWord(segment, offset: AckSeqOffset, atLeast: BacktraceSequence)
	let walkResult = try segment.readWord(CmdResultOffset) otherwise return 22
	let walkRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 23

	try segment.writeWord(CmdOffset, value: GtListCommandCode) otherwise return 24
	try segment.writeWord(CmdSeqOffset, value: ListSequence) otherwise return 25

	let listed = awaitWord(segment, offset: AckSeqOffset, atLeast: ListSequence)
	let listResult = try segment.readWord(CmdResultOffset) otherwise return 26
	let gtCount = try segment.readWord(GtCountOffset) otherwise return 27

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 28
	try segment.writeWord(CmdSeqOffset, value: ResumeSequence) otherwise return 29
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: ResumeSequence)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("answered={acked and pauseResult == CommandDone} noThread={stopped and reason == PauseStopReason and stopPc == NoPc and machine == NoMachine} refused={walked and walkResult == CommandRefused and walkRefusal == NotStoppedRefusal} listed={listed and listResult == CommandDone and gtCount >= WorkerCount} child={code}\n")

	return 0
end 'main'
```
```stdout
answered=true noThread=true refused=true listed=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-breakpoint-set-while-running-is-acknowledged -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A command posted while the child is RUNNING is acknowledged, and the ones that need a stop are refused

⭐⭐ **THE MAILBOX IS NO LONGER SERVICED ONLY OUT OF A PARK LOOP.** The child below parks once, so the
parent can learn `textSize`, and is then continued — after which nothing in it is parked and the only
thing that can answer a command is the agent's own service thread. The `setBreakpoint` the parent posts
is one it KNOWS will be refused (`outOfText`), because what is being asked is not whether the write
happened but whether the command was SEEN: `ackSeq` reaching `cmdSeq` while the program runs is the whole
claim, and a refusal proves it as well as a success while touching no executable byte.

⛔ **A STOP IS STILL REQUIRED FOR THE COMMANDS THAT NEED ONE.** `backtrace` has no register file to walk
while every machine is running, so it is answered `notRunning` rather than with a walk of whatever the
agent's own thread was doing — which is the failure a driver could not tell from a real backtrace.

⚠ **THE REASON IS `notRunning` AND NOT `notStopped`, AND THE TWO ARE DIFFERENT FACTS.** Nothing is stopped
here at all, which is what 11 says; 7 is for a stop that HAS happened and captured no thread — the entry
park, and a pause the agent declared over machines that were all idle.
`a-backtrace-asked-before-main-is-refused` is the case that pins 7, and it is the other half of this pair.
```maxon
typealias Millis = int(0 to i64.max)
typealias Spin = int(0 to 1)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-live"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let CmdArg1Offset = 80 as SegmentOffset
let CmdArg2Offset = 88 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let SetBreakpointCommandCode = 1
let ContinueCommandCode = 3
let BacktraceCommandCode = 4
let CommandRefused = 0
let OutOfTextRefusal = 1
let NotRunningRefusal = 11
let PlainInstructionClass = 1 shl 8
let OneByteInstruction = 1
let FirstStop = 1
let NoArgument = 0
let ChildExitCode = 5

let SpinMs = 5000 as Millis
let SettleMs = 300 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function spin() returns Spin
	var total = 0
	let deadline = (Clock.nowMs() as Millis) + SpinMs

	while (Clock.nowMs() as Millis) < deadline 'grind'
		total = total + 1
	end 'grind'

	return 1 if total > 0 else 0
end 'spin'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode if spin() > 0 else 0
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 10

	let parked = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 11

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 12
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 13
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 14
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 1)

	sleep(SettleMs)

	try segment.writeWord(CmdArg0Offset, value: textSize) otherwise return 15
	try segment.writeWord(CmdArg1Offset, value: PlainInstructionClass + OneByteInstruction) otherwise return 16
	try segment.writeWord(CmdArg2Offset, value: NoArgument) otherwise return 17
	try segment.writeWord(CmdOffset, value: SetBreakpointCommandCode) otherwise return 18
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 19

	let sawTheWrite = awaitWord(segment, offset: AckSeqOffset, atLeast: 2)
	let writeResult = try segment.readWord(CmdResultOffset) otherwise return 20
	let writeRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 21

	try segment.writeWord(CmdOffset, value: BacktraceCommandCode) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 23

	let sawTheWalk = awaitWord(segment, offset: AckSeqOffset, atLeast: 3)
	let walkResult = try segment.readWord(CmdResultOffset) otherwise return 24
	let walkRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 25

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("parked={parked} ackedWhileRunning={sawTheWrite and sawTheWalk} outOfText={writeResult == CommandRefused and writeRefusal == OutOfTextRefusal} notRunning={walkResult == CommandRefused and walkRefusal == NotRunningRefusal} child={code}\n")

	return 0
end 'main'
```
```stdout
parked=true ackedWhileRunning=true outOfText=true notRunning=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-green-thread-roster-lists-the-parked-workers -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `gtList` at a stop lists the green threads that are parked, each with a handle and an entry inside the code

⭐⭐ **A GREEN THREAD IS NOT AN OS THREAD AND NOTHING THE HOST OFFERS CAN SEE ONE.** The roster is built by
the runtime itself, at the moment a record is carved, so it is the only account of what a program is
running. The child below spawns three workers that all sleep at once; the parent pauses, asks for the
roster, and requires at least those three to be present, WAITING, with a nonzero handle and an entry pc
inside the program's own code.

⚠ **AT LEAST THREE, NEVER EXACTLY THREE.** `main` is a green thread too, and the scheduler's own records
are the runtime's business; a case that pinned the count would be re-blessed every time the scheduler
carved one more.
```maxon
typealias Millis = int(0 to i64.max)
typealias Nap = int(0 to 1)
typealias RecordIndex = int(0 to 80)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-gtlist"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset
let GtCountOffset = 2528 as SegmentOffset

let GtRecordsOffset = 2560
let GtRecordStride = 64
let GtRecordCapacity = 80
let GtHandleField = 0
let GtStatusField = 8
let GtEntryPcField = 16

let DriverVersion = 1
let ParkBeforeMain = 1
let ContinueCommandCode = 3
let PauseCommandCode = 13
let GtListCommandCode = 9
let CommandDone = 1
let WaitingStatus = 3
let FirstStop = 1
let SecondStop = 2
let NoArgument = 0
let ChildExitCode = 5
let WorkerCount = 3

let NapMs = 3000 as Millis
let SettleMs = 400 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function recordWord(segment SharedSegment, index RecordIndex, field SegmentOffset) returns SegmentWord
	let at = (GtRecordsOffset + index * GtRecordStride) as SegmentOffset

	return try segment.readWord(at + field) otherwise 0
end 'recordWord'

function napper() returns Nap
	sleep(NapMs)

	return 1
end 'napper'

function runChild() returns ExitCode
	let a = async napper()
	let b = async napper()
	let c = async napper()
	let x = await a
	let y = await b
	let z = await c

	return ChildExitCode if x + y + z == WorkerCount else 0
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return runChild()
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 10

	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 11
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 12

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 13
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 14
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 15
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 1)

	sleep(SettleMs)

	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 16
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 17
	let stopped = awaitWord(segment, offset: StopSeqOffset, atLeast: SecondStop)

	try segment.writeWord(CmdOffset, value: GtListCommandCode) otherwise return 18
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 19

	let listed = awaitWord(segment, offset: AckSeqOffset, atLeast: 3)
	let listResult = try segment.readWord(CmdResultOffset) otherwise return 20
	let gtCount = try segment.readWord(GtCountOffset) otherwise return 21

	var waiting = 0
	var i = 0

	while i < gtCount and i < GtRecordCapacity 'eachRecord'
		let at = i as RecordIndex
		let handle = recordWord(segment, index: at, field: GtHandleField as SegmentOffset)
		let status = recordWord(segment, index: at, field: GtStatusField as SegmentOffset)
		let entryPc = recordWord(segment, index: at, field: GtEntryPcField as SegmentOffset)

		if status == WaitingStatus and handle != 0 and entryPc >= textBase and entryPc < textBase + textSize 'aParkedWorker'
			waiting = waiting + 1
		end 'aParkedWorker'

		i = i + 1
	end 'eachRecord'

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 4) otherwise return 23
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 4)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("stopped={stopped} listed={listed and listResult == CommandDone} roster={gtCount >= WorkerCount} parked={waiting >= WorkerCount} child={code}\n")

	return 0
end 'main'
```
```stdout
stopped=true listed=true roster=true parked=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-parked-workers-backtrace-is-answered -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A `gtBacktrace` of a parked green thread answers frames, all of them inside the program's code

⭐⭐ **A PARKED GREEN THREAD'S REGISTERS ARE IN ITS OWN RECORD AND NOWHERE ELSE.** No machine is running
it, so the stopping machine's frame pointer says nothing about it; the walk has to be seeded from the
record the roster just handed over. Every frame pc is required to be inside `.text`, which is what tells
a real walk from a chain that ran off the end of a borrowed stack and answered whatever it found.
```maxon
typealias Millis = int(0 to i64.max)
typealias Nap = int(0 to 1)
typealias RecordIndex = int(0 to 80)
typealias FrameIndex = int(0 to 64)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-gtbt"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let TextBaseOffset = 160 as SegmentOffset
let TextSizeOffset = 168 as SegmentOffset
let BtCountOffset = 896 as SegmentOffset
let GtCountOffset = 2528 as SegmentOffset

let BtFramesOffset = 912
let BtFrameStride = 16
let GtRecordsOffset = 2560
let GtRecordStride = 64
let GtRecordCapacity = 80
let GtHandleField = 0
let GtStatusField = 8

let DriverVersion = 1
let ParkBeforeMain = 1
let ContinueCommandCode = 3
let PauseCommandCode = 13
let GtListCommandCode = 9
let GtBacktraceCommandCode = 10
let CommandDone = 1
let WaitingStatus = 3
let FirstStop = 1
let SecondStop = 2
let NoHandle = 0
let NoArgument = 0
let ChildExitCode = 5
let WorkerCount = 3

let NapMs = 3000 as Millis
let SettleMs = 400 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function recordWord(segment SharedSegment, index RecordIndex, field SegmentOffset) returns SegmentWord
	let at = (GtRecordsOffset + index * GtRecordStride) as SegmentOffset

	return try segment.readWord(at + field) otherwise 0
end 'recordWord'

function framePc(segment SharedSegment, index FrameIndex) returns SegmentWord
	let at = (BtFramesOffset + index * BtFrameStride) as SegmentOffset

	return try segment.readWord(at) otherwise 0
end 'framePc'

function napper() returns Nap
	sleep(NapMs)

	return 1
end 'napper'

function runChild() returns ExitCode
	let a = async napper()
	let b = async napper()
	let c = async napper()
	let x = await a
	let y = await b
	let z = await c

	return ChildExitCode if x + y + z == WorkerCount else 0
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return runChild()
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 10

	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)
	let textBase = try segment.readWord(TextBaseOffset) otherwise return 11
	let textSize = try segment.readWord(TextSizeOffset) otherwise return 12

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 13
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 14
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 15
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 1)

	sleep(SettleMs)

	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 16
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 17
	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: SecondStop)

	try segment.writeWord(CmdOffset, value: GtListCommandCode) otherwise return 18
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 19
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 3)

	let gtCount = try segment.readWord(GtCountOffset) otherwise return 20
	var chosen = NoHandle
	var i = 0

	while i < gtCount and i < GtRecordCapacity 'eachRecord'
		let at = i as RecordIndex
		let handle = recordWord(segment, index: at, field: GtHandleField as SegmentOffset)
		let status = recordWord(segment, index: at, field: GtStatusField as SegmentOffset)

		if chosen == NoHandle and status == WaitingStatus and handle != 0 'aParkedWorker'
			chosen = handle
		end 'aParkedWorker'

		i = i + 1
	end 'eachRecord'

	try segment.writeWord(CmdArg0Offset, value: chosen) otherwise return 21
	try segment.writeWord(CmdOffset, value: GtBacktraceCommandCode) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 4) otherwise return 23

	let walked = awaitWord(segment, offset: AckSeqOffset, atLeast: 4)
	let walkResult = try segment.readWord(CmdResultOffset) otherwise return 24
	let btCount = try segment.readWord(BtCountOffset) otherwise return 25

	var inText = btCount > 0
	var f = 0

	while f < btCount 'eachFrame'
		let pc = framePc(segment, index: f as FrameIndex)
		inText = false if pc < textBase or pc >= textBase + textSize else inText
		f = f + 1
	end 'eachFrame'

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 26
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 27
	try segment.writeWord(CmdSeqOffset, value: 5) otherwise return 28
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 5)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("found={chosen != NoHandle} walked={walked and walkResult == CommandDone} frames={btCount >= 1} inText={inText} child={code}\n")

	return 0
end 'main'
```
```stdout
found=true walked=true frames=true inText=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-held-thread-does-not-run-until-released -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A held green thread runs nothing while it is held, and finishes its work once it is released

⭐⭐ **A HOLD IS APPLIED BY THE SCHEDULER, NOT BY THE AGENT, AND ONLY THE PROGRAM CAN SAY WHETHER IT TOOK.**
The worker below sleeps and then writes 1 into the case's scratch word. The parent holds it while it is
still asleep, waits well past the moment its sleep expires — so the timer has put it on a run queue and a
machine has looked for work — and requires the word to be STILL ZERO. Then it releases the thread and
requires the word to become 1 and the program to reach its own exit code.

⛔ **BOTH HALVES OR NEITHER.** A thread that is never released is a program that never ends, because `main`
awaits it; a thread that was never really held writes its word immediately. Only the pair — still zero
under the hold, one after the release — separates a hold that worked from a hold that did nothing.

⚠ **`main`'S OWN HANDLE IS LEARNED FROM A ROSTER TAKEN BEFORE THE WORKER EXISTS.** The roster hands over
handles and no names, so the only honest way to say "hold the worker and not `main`" is to look once
while the child is still spinning ahead of its spawn, and hold the handle that was not there then. A hold
placed on `main` would wedge the program, which is the failure this ordering exists to avoid.
```maxon
typealias Millis = int(0 to i64.max)
typealias Mark = int(0 to 1)
typealias RecordIndex = int(0 to 80)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-hold"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let MaxProcsVariableName = "MAXON_MAX_PROCS"
let OneProcessor = "1"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset
let GtCountOffset = 2528 as SegmentOffset
let CaseScratchOffset = 2544 as SegmentOffset

let GtRecordsOffset = 2560
let GtRecordStride = 64
let GtRecordCapacity = 80
let GtHandleField = 0

let DriverVersion = 1
let ParkBeforeMain = 1
let ContinueCommandCode = 3
let PauseCommandCode = 13
let GtListCommandCode = 9
let GtHoldCommandCode = 11
let GtReleaseCommandCode = 12
let CommandDone = 1
let FirstStop = 1
let SecondStop = 2
let ThirdStop = 3
let NoHandle = 0
let NoArgument = 0
let NotYetRun = 0
let HasRun = 1
let ChildExitCode = 5

let SpinBeforeSpawnMs = 2000 as Millis
let WorkerNapMs = 4000 as Millis
let BeforeTheSpawnMs = 300 as Millis
let AfterTheSpawnMs = 2500 as Millis
let PastTheNapMs = 3000 as Millis
let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 3000 as Millis
let ChildDeadlineMs = 30000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function recordHandle(segment SharedSegment, index RecordIndex) returns SegmentWord
	let at = (GtRecordsOffset + index * GtRecordStride + GtHandleField) as SegmentOffset

	return try segment.readWord(at) otherwise 0
end 'recordHandle'

function marker() returns Mark
	sleep(WorkerNapMs)

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 0
	try segment.writeWord(CaseScratchOffset, value: HasRun) otherwise ignore
	segment.close()

	return 1
end 'marker'

function runChild() returns ExitCode
	let deadline = (Clock.nowMs() as Millis) + SpinBeforeSpawnMs
	var turns = 0

	while (Clock.nowMs() as Millis) < deadline 'beforeTheSpawn'
		turns = turns + 1
	end 'beforeTheSpawn'

	let pending = async marker()
	let done = await pending

	return ChildExitCode if done + turns > 0 else 0
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return runChild()
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7
	try segment.writeWord(CaseScratchOffset, value: NotYetRun) otherwise return 8

	let me = try Process.executablePath() otherwise return 9
	let here = try FilePath.from("") otherwise return 10
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName(), MaxProcsVariableName: OneProcessor])) otherwise return 11

	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 12
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 13
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 14
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 1)

	sleep(BeforeTheSpawnMs)

	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 15
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 16
	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: SecondStop)

	try segment.writeWord(CmdOffset, value: GtListCommandCode) otherwise return 17
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 18
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 3)

	let beforeCount = try segment.readWord(GtCountOffset) otherwise return 19
	var known = StringArray.create()
	var i = 0

	while i < beforeCount and i < GtRecordCapacity 'eachEarlyRecord'
		known.push("{recordHandle(segment, index: i as RecordIndex)}")
		i = i + 1
	end 'eachEarlyRecord'

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 20
	try segment.writeWord(CmdSeqOffset, value: 4) otherwise return 21
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 4)

	sleep(AfterTheSpawnMs)

	try segment.writeWord(CmdOffset, value: PauseCommandCode) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 5) otherwise return 23
	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: ThirdStop)

	try segment.writeWord(CmdOffset, value: GtListCommandCode) otherwise return 24
	try segment.writeWord(CmdSeqOffset, value: 6) otherwise return 25
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 6)

	let afterCount = try segment.readWord(GtCountOffset) otherwise return 26
	var chosen = NoHandle
	var j = 0

	while j < afterCount and j < GtRecordCapacity 'eachLaterRecord'
		let handle = recordHandle(segment, index: j as RecordIndex)

		if chosen == NoHandle and handle != 0 and not known.contains("{handle}") 'theNewOne'
			chosen = handle
		end 'theNewOne'

		j = j + 1
	end 'eachLaterRecord'

	try segment.writeWord(CmdArg0Offset, value: chosen) otherwise return 27
	try segment.writeWord(CmdOffset, value: GtHoldCommandCode) otherwise return 28
	try segment.writeWord(CmdSeqOffset, value: 7) otherwise return 29

	let heldAck = awaitWord(segment, offset: AckSeqOffset, atLeast: 7)
	let heldResult = try segment.readWord(CmdResultOffset) otherwise return 30

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 31
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 32
	try segment.writeWord(CmdSeqOffset, value: 8) otherwise return 33
	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 8)

	sleep(PastTheNapMs)
	let whileHeld = try segment.readWord(CaseScratchOffset) otherwise return 34

	try segment.writeWord(CmdArg0Offset, value: chosen) otherwise return 35
	try segment.writeWord(CmdOffset, value: GtReleaseCommandCode) otherwise return 36
	try segment.writeWord(CmdSeqOffset, value: 9) otherwise return 37

	let releasedAck = awaitWord(segment, offset: AckSeqOffset, atLeast: 9)
	let releaseResult = try segment.readWord(CmdResultOffset) otherwise return 38

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	let afterRelease = try segment.readWord(CaseScratchOffset) otherwise return 39
	child.release()
	segment.close()

	print("found={chosen != NoHandle} held={heldAck and heldResult == CommandDone} idle={whileHeld == NotYetRun} released={releasedAck and releaseResult == CommandDone} ranAfter={afterRelease == HasRun} child={code}\n")

	return 0
end 'main'
```
```stdout
found=true held=true idle=true released=true ranAfter=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.a-condition-refusal-names-its-reason -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A condition attached to a breakpoint index nothing armed is refused by reason, both ways

⛔ **A CONDITION IS A SECOND WRITE TO A BREAKPOINT ENTRY, AND AN ENTRY NOBODY ARMED IS NOT ONE.** The
scratch record the parent fills below is well formed — a register compare against a literal, the shape
the agent evaluates — so the refusal is about the TEXT OFFSET and nothing else. Both the set and the
clear are asked, because an agent that validated one and not the other would write a condition into a
free entry and then refuse to take it back out.

⚠ **A CONDITION NAMES ITS BREAKPOINT BY TEXT OFFSET, THE WAY `setBreakpoint` AND `clearBreakpoint` DO.**
The agent's table index is private bookkeeping it publishes nowhere, so naming one by index would oblige
a driver to read that table out of the debuggee to learn something it already knows.
```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-cond"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdResultOffset = 104 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset

let ConditionKindOffset = 2464 as SegmentOffset
let ConditionOperandOffset = 2472 as SegmentOffset
let ConditionWidthOffset = 2480 as SegmentOffset
let ConditionSignedOffset = 2488 as SegmentOffset
let ConditionOpOffset = 2496 as SegmentOffset
let ConditionLiteralOffset = 2504 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let SetConditionCommandCode = 7
let ClearConditionCommandCode = 8
let ContinueCommandCode = 3
let CommandRefused = 0
let CommandDone = 1
let NoSuchBreakpointRefusal = 5
let RegisterCondition = 1
let FirstRegister = 0
let WordWidth = 8
let UnsignedCompare = 0
let EqualOp = 1
let ZeroLiteral = 0
let UnarmedTextOffset = 99
let FirstStop = 1
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 5000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord) returns bool
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return false

		if seen >= atLeast 'arrived'
			return true
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitWord'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	_ = awaitWord(segment, offset: StopSeqOffset, atLeast: FirstStop)

	try segment.writeWord(ConditionKindOffset, value: RegisterCondition) otherwise return 11
	try segment.writeWord(ConditionOperandOffset, value: FirstRegister) otherwise return 12
	try segment.writeWord(ConditionWidthOffset, value: WordWidth) otherwise return 13
	try segment.writeWord(ConditionSignedOffset, value: UnsignedCompare) otherwise return 14
	try segment.writeWord(ConditionOpOffset, value: EqualOp) otherwise return 15
	try segment.writeWord(ConditionLiteralOffset, value: ZeroLiteral) otherwise return 16

	try segment.writeWord(CmdArg0Offset, value: UnarmedTextOffset) otherwise return 17
	try segment.writeWord(CmdOffset, value: SetConditionCommandCode) otherwise return 18
	try segment.writeWord(CmdSeqOffset, value: 1) otherwise return 19

	let setAck = awaitWord(segment, offset: AckSeqOffset, atLeast: 1)
	let setResult = try segment.readWord(CmdResultOffset) otherwise return 20
	let setRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 21

	try segment.writeWord(CmdOffset, value: ClearConditionCommandCode) otherwise return 22
	try segment.writeWord(CmdSeqOffset, value: 2) otherwise return 23

	let clearAck = awaitWord(segment, offset: AckSeqOffset, atLeast: 2)
	let clearResult = try segment.readWord(CmdResultOffset) otherwise return 24
	let clearRefusal = try segment.readWord(CmdRefusalOffset) otherwise return 25

	try segment.writeWord(CmdArg0Offset, value: NoArgument) otherwise return 26
	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 27
	try segment.writeWord(CmdSeqOffset, value: 3) otherwise return 28

	_ = awaitWord(segment, offset: AckSeqOffset, atLeast: 3)
	let resumed = try segment.readWord(CmdResultOffset) otherwise return 29

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("setRefused={setAck and setResult == CommandRefused and setRefusal == NoSuchBreakpointRefusal} clearRefused={clearAck and clearResult == CommandRefused and clearRefusal == NoSuchBreakpointRefusal} resumed={resumed == CommandDone} child={code}\n")

	return 0
end 'main'
```
```stdout
setRefused=true clearRefused=true resumed=true child=5
```
```exitcode
0
```

<!-- test: debug-agent.an-operand-the-class-word-places-past-the-instruction-is-refused -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
### A breakpoint whose class word places an operand past its instruction is refused as unclassified

```maxon
typealias Millis = int(0 to i64.max)
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-operandpast"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset
let CmdSeqOffset = 56 as SegmentOffset
let CmdOffset = 64 as SegmentOffset
let CmdArg0Offset = 72 as SegmentOffset
let CmdArg1Offset = 80 as SegmentOffset
let CmdArg2Offset = 88 as SegmentOffset
let AckSeqOffset = 96 as SegmentOffset
let CmdRefusalOffset = 112 as SegmentOffset
let StopSeqOffset = 120 as SegmentOffset

let DriverVersion = 1
let ParkBeforeMain = 1
let SetBreakpointCommandCode = 1
let ClearBreakpointCommandCode = 2
let ContinueCommandCode = 3
let UnclassifiedRefusal = 3
let ClassShift = 8
let OperandPositionShift = 16
let ModRmPositionShift = 32
let PcRelativeDataClass = 2
let ReturnClass = 6
let IndirectCallClass = 7
let ReturnReleasingBytes = 3
let ReleaseImmediateBytes = 2
let PcRelativeLoadBytes = 7
let IndirectCallBytes = 3
let PastEveryInstruction = 255
let FirstCodeOffset = 0
let NoArgument = 0
let ChildExitCode = 5

let PollIntervalMs = 5 as Millis
let PollDeadlineMs = 20000 as Millis
let ChildDeadlineMs = 20000 as Millis
let AsciiBits = 8

function controlMagic() returns SegmentWord
	var magic = 0
	var shift = 0

	for b in MagicText.bytes() 'eachByte'
		magic = magic + ((b as SegmentWord) shl shift)
		shift = shift + AsciiBits
	end 'eachByte'

	return magic
end 'controlMagic'

function awaitWord(segment SharedSegment, offset SegmentOffset, atLeast SegmentWord)
	let deadline = (Clock.nowMs() as Millis) + PollDeadlineMs

	while (Clock.nowMs() as Millis) < deadline 'poll'
		let seen = try segment.readWord(offset) otherwise return

		if seen >= atLeast 'arrived'
			return
		end 'arrived'

		sleep(PollIntervalMs)
	end 'poll'
end 'awaitWord'

function classWord(instructionClass SegmentWord, length SegmentWord, operandAt SegmentWord, shift SegmentWord) returns SegmentWord
	return (instructionClass shl ClassShift) + length + (operandAt shl shift)
end 'classWord'

function refusalOf(segment SharedSegment, word SegmentWord, sequence SegmentWord) returns SegmentWord
	try segment.writeWord(CmdArg1Offset, value: word) otherwise return NoArgument
	try segment.writeWord(CmdSeqOffset, value: sequence) otherwise return NoArgument

	awaitWord(segment, offset: AckSeqOffset, atLeast: sequence)

	return try segment.readWord(CmdRefusalOffset) otherwise NoArgument
end 'refusalOf'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return ChildExitCode
	end 'iAmTheChild'

	var segment = try SharedSegment.create(SegmentName, bytes: SegmentBytes) otherwise return 3
	try segment.writeWord(MagicOffset, value: controlMagic()) otherwise return 4
	try segment.writeWord(DriverVersionOffset, value: DriverVersion) otherwise return 5
	try segment.writeWord(StopAtEntryOffset, value: ParkBeforeMain) otherwise return 6
	try segment.writeWord(DriverPidOffset, value: __Builtins.currentProcessId() as SegmentWord) otherwise return 7

	let me = try Process.executablePath() otherwise return 8
	let here = try FilePath.from("") otherwise return 9
	var argv = StringArray.create()
	argv.push(ChildArgument)

	var child = try StreamingSubprocess.spawnWithEnvironment(Executable.path(me), arguments: argv, workingDirectory: here, environment: Environment.inheritUpdating([DebugVariableName: segment.segmentName()])) otherwise return 10

	awaitWord(segment, offset: StopSeqOffset, atLeast: 1)

	try segment.writeWord(CmdArg0Offset, value: FirstCodeOffset) otherwise return 11
	try segment.writeWord(CmdArg2Offset, value: NoArgument) otherwise return 12
	try segment.writeWord(CmdOffset, value: SetBreakpointCommandCode) otherwise return 13

	let release = refusalOf(segment, word: classWord(ReturnClass, length: ReturnReleasingBytes, operandAt: PastEveryInstruction, shift: OperandPositionShift), sequence: 1)
	let displacement = refusalOf(segment, word: classWord(PcRelativeDataClass, length: PcRelativeLoadBytes, operandAt: PastEveryInstruction, shift: OperandPositionShift), sequence: 2)
	let modRm = refusalOf(segment, word: classWord(IndirectCallClass, length: IndirectCallBytes, operandAt: PastEveryInstruction, shift: ModRmPositionShift), sequence: 3)
	let lastByteOver = refusalOf(segment, word: classWord(ReturnClass, length: ReturnReleasingBytes, operandAt: ReturnReleasingBytes - ReleaseImmediateBytes + 1, shift: OperandPositionShift), sequence: 4)
	let lastFit = refusalOf(segment, word: classWord(ReturnClass, length: ReturnReleasingBytes, operandAt: ReturnReleasingBytes - ReleaseImmediateBytes, shift: OperandPositionShift), sequence: 5)

	try segment.writeWord(CmdOffset, value: ClearBreakpointCommandCode) otherwise return 14
	try segment.writeWord(CmdSeqOffset, value: 6) otherwise return 15

	awaitWord(segment, offset: AckSeqOffset, atLeast: 6)

	try segment.writeWord(CmdOffset, value: ContinueCommandCode) otherwise return 16
	try segment.writeWord(CmdSeqOffset, value: 7) otherwise return 17

	awaitWord(segment, offset: AckSeqOffset, atLeast: 7)

	let code = try child.waitWithTimeout(ChildDeadlineMs) otherwise 97
	child.release()
	segment.close()

	print("release={release == UnclassifiedRefusal} displacement={displacement == UnclassifiedRefusal} modRm={modRm == UnclassifiedRefusal} lastByteOver={lastByteOver == UnclassifiedRefusal} lastFit={lastFit == UnclassifiedRefusal} child={code}\n")

	return 0
end 'main'
```
```stdout
release=true displacement=true modRm=true lastByteOver=true lastFit=false child=5
```
```exitcode
0
```
