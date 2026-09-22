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

**Attaching changes nothing else about how the program ends.** A fault is still terminal, with the same
panic line and the same exit code it has with nothing attached — the agent's trap handler owns the trap
codes it installed and no others.

**A parked agent watches the driver PROCESS and ends itself when that process is gone.** A driver that was
killed leaves a program stopped before `main` with nobody to resume it, so the agent opens a handle to the
pid the page names at attach and polls it while parked; a parked child whose driver has exited exits **97**.
The guard is liveness rather than a clock, because a driver at a prompt may think for as long as its user
does, and a driver that cannot open the named process at all stays dark rather than attaching.

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
### An attached child that faults still panics, prints its backtrace and exits 1

⭐⭐ **THE AGENT INSTALLS A TRAP HANDLER, AND THIS IS WHAT SAYS IT OWNS ONLY THE CODES IT ASKED FOR.** A
handler that claimed an access violation would swallow the one fault the language reports, and the program
would hang parked, or continue past a wild store, instead of stopping. The child below calls
`__Builtins.forceSegfault()` the way `specs/safety.md`'s `force-segfault` case spells it, with the agent
attached and `stopAtEntry` 0, and the parent reads back the same exit code and the same first stderr line
that case pins with nothing attached.
```maxon
typealias StringArray = Array with String

let MagicText = "MXDBGCTL"
let SegmentName = "maxon-spec-dbg-fault"
let SegmentBytes = 8192
let DebugVariableName = "MAXON_DEBUG"
let ChildArgument = "child"
let PanicLine = "panic: nil pointer or invalid memory access"

let MagicOffset = 0 as SegmentOffset
let DriverVersionOffset = 8 as SegmentOffset
let AgentVersionOffset = 16 as SegmentOffset
let DriverPidOffset = 40 as SegmentOffset
let StopAtEntryOffset = 48 as SegmentOffset

let DriverVersion = 1
let RunToCompletion = 0

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
		__Builtins.forceSegfault()
		return 0
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
	segment.close()

	print("attached={version} child={run.exitCode()} panicked={run.stderr.contains(PanicLine)}\n")

	return 0
end 'main'
```
```stdout
attached=1 child=1 panicked=true
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
