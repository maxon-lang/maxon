---
feature: process-descendants
status: stable
keywords: [process, Process.endDescendants, descendants, detached, orphan, leak]
category: system
---

# `Process.endDescendants()` — end every process this one started and left running

## Documentation

| Function | Meaning |
|---|---|
| `Process.endDescendants() returns DescendantProcessArray` | Ends every live process descended from this one, children, detached children and the grandchildren whose parents have already ended included, and answers what it found as a `DescendantProcess` (`pid`, `name`) each |

A descendant's `name` is the process's executable name as the host records it, with each byte that is not
printable ASCII (a control byte, a newline among them, or a byte of a multi-byte character) reported as `?`, so
one name is always one line of the report.

A process that has ended is not listed, whether or not its parent has waited for it: only a process still
running when the call finds it is a descendant to end. A program that calls
`Process.endDescendants()` tracks its descendants from startup, so its first call also finds the
processes it started before that call.

Windows keeps every descendant in a job object and Linux makes the process the subreaper of its orphans, so
neither can lose one. macOS has neither: a process that ended leaves its children to `launchd`. The tracking
there records the kernel's unique id of every process this one starts afterwards and marks this process's
environment with `MAXON_TRACK_DESCENDANTS=D<pid>`, which every process started afterwards inherits. The call
finds the live descendants as the union of three sets: the process tree below this process; every process
whose original parent is a recorded child, because the kernel keeps a process's original parent across its
reparenting to `launchd`, for Apple's own programs too; and every process whose environment carries the
mark, which covers a descendant of any depth that is not an Apple program, because the kernel hides the
environment of those. The one descendant it can miss is an Apple program whose parent ended without ever
being recorded: two generations of ended processes between this process and it.

## Tests

<!-- test: process-descendants.a-detached-sleeper-is-listed-ended-and-named -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
let EndsWithinMs = 30000
let PollMs = 50

function main() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("30")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("30")
	#endif

	let armed = Process.endDescendants()
	let pid = try config.runDetached() otherwise return 3
	let alive = Process.isAlive(pid)
	let found = Process.endDescendants()
	var listed = false
	var named = false

	for entry in found 'eachEntry'
		if entry.pid == pid 'ours'
			listed = true
			named = entry.name.toLower().startsWith("ping") or entry.name.equals("sleep")
		end 'ours'
	end 'eachEntry'

	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	print("armed={armed.count()} alive={alive} listed={listed} named={named} onlyOne={found.count() == 1} ended={not Process.isAlive(pid)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 alive=true listed=true named=true onlyOne=true ended=true
```

<!-- test: process-descendants.a-name-holding-a-newline-is-one-listed-descendant -->
<!-- process: own -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A POSIX executable name may hold a newline, and the host reports that name as the process's name. The program
copies itself to a file named so, runs the copy detached and lists its descendants: the report has one entry,
and the newline is a `?` in its name.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let CopyName = "nl\nself"
let ListedName = "nl?self"
let CopyScript = "cp \"$0\" \"$(printf 'nl\\nself')\""
let RemoveScript = "rm -f \"$(printf 'nl\\nself')\""
let ChildMode = "child"

function runShell(script String, exePath FilePath) returns ExitCode
	var argv = StringArray.create(0)

	argv.push("-c")
	argv.push(script)
	argv.push(exePath.toString())

	let result = try Subprocess.run(Executable.path(try FilePath.from("/bin/sh") otherwise return 2), arguments: argv) otherwise return 3

	return result.status.code() as ExitCode
end 'runShell'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'asTheChild'
		sleep(EndsWithinMs)
		return 0
	end 'asTheChild'

	let me = try Process.executablePath() otherwise return 2
	let armed = Process.endDescendants()
	let copied = runShell(CopyScript, exePath: me)

	if copied != 0 'copyFailed'
		return 4
	end 'copyFailed'

	var config = Configuration.create(Executable.path(try FilePath.from("./{CopyName}") otherwise return 5))

	config.arguments.push(ChildMode)

	let pid = try config.runDetached() otherwise return 6
	let found = Process.endDescendants()
	let removed = runShell(RemoveScript, exePath: me)
	var named = false

	for entry in found 'eachEntry'
		if entry.pid == pid 'ours'
			named = entry.name.equals(ListedName)
		end 'ours'
	end 'eachEntry'

	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	print("armed={armed.count()} removed={removed} named={named} onlyOne={found.count() == 1} ended={not Process.isAlive(pid)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 removed=0 named=true onlyOne=true ended=true
```

<!-- test: process-descendants.a-waited-child-is-not-listed -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create(0)
	argv.push("/c")
	argv.push("exit")
	argv.push("0")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create(0)
	argv.push("-c")
	argv.push("exit 0")
	#endif

	let armed = Process.endDescendants()
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 3
	let found = Process.endDescendants()

	print("armed={armed.count()} ran={result.pid != 0} found={found.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 ran=true found=0
```

<!-- test: process-descendants.an-ended-detached-child-is-not-listed -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
A detached child nobody waits for stays an entry of the process table once it ends, so it is a zombie under
POSIX. It has ended all the same, and it is not listed.
```maxon
let EndsWithinMs = 30000
let PollMs = 50

function main() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("cmd"))
	config.arguments.push("/c")
	config.arguments.push("exit")
	config.arguments.push("0")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sh") otherwise return 2))
	config.arguments.push("-c")
	config.arguments.push("exit 0")
	#endif

	let armed = Process.endDescendants()
	let pid = try config.runDetached() otherwise return 3
	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	let found = Process.endDescendants()

	print("armed={armed.count()} ended={not Process.isAlive(pid)} found={found.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 ended=true found=0
```

<!-- test: process-descendants.an-orphaned-detached-grandchild-is-found -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
The program runs itself as a child. The child detaches a long sleeper, names its id on stdout and exits, so
the sleeper outlives the only process that was its parent. Under Linux the sleeper is reparented to the
nearest subreaper, which is the process that asked to track its descendants; under Windows it stays in the
job the tracking created. Either way it is found, named and ended.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let SleeperLabel = "sleeper="

function detachSleeper() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("30")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("30")
	#endif

	let pid = try config.runDetached() otherwise return 3

	print("{SleeperLabel}{pid}\n")
	return 0
end 'detachSleeper'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'asTheChild'
		return detachSleeper()
	end 'asTheChild'

	let armed = Process.endDescendants()
	var argv = StringArray.create(0)

	argv.push("child")

	let me = try Process.executablePath() otherwise return 2
	let child = try Subprocess.run(Executable.path(me), arguments: argv) otherwise return 3
	let text = child.stdout.trim().replace(SleeperLabel, with: "")
	let pid = try int.fromString(text) otherwise return 4
	let alive = Process.isAlive(pid as Pid)
	let found = Process.endDescendants()
	var listed = false
	var named = false

	for entry in found 'eachEntry'
		if entry.pid == (pid as Pid) 'ours'
			listed = true
			named = entry.name.toLower().startsWith("ping") or entry.name.equals("sleep")
		end 'ours'
	end 'eachEntry'

	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid as Pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	print("armed={armed.count()} alive={alive} listed={listed} named={named} onlyOne={found.count() == 1} ended={not Process.isAlive(pid as Pid)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 alive=true listed=true named=true onlyOne=true ended=true
```

<!-- test: process-descendants.a-first-call-lists-everything-the-program-started-before-it -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
The program runs itself as a probe whose environment carries no armed token, the shape of a program run outside
the harness. The probe starts a detached sleeper, then runs itself as an orphaner that detaches a second sleeper
and exits. Only then does the probe call `Process.endDescendants()` for the first time: it lists the first
sleeper, which was started before the call, and the second, whose parent had ended before the call.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let SleeperLabel = "sleeper="
let TrackVariable = "MAXON_TRACK_DESCENDANTS"
let UntrackedValue = "X"
let ProbeMode = "probe"
let OrphanerMode = "orphaner"

function startSleeper() returns Pid
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("30")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise panic("/bin/sleep is a valid path")))
	config.arguments.push("30")
	#endif

	return try config.runDetached() otherwise panic("the sleeper did not start")
end 'startSleeper'

function runAsOrphaner() returns ExitCode
	print("{SleeperLabel}{startSleeper()}\n")
	return 0
end 'runAsOrphaner'

function runAsProbe() returns ExitCode
	let direct = startSleeper()
	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create(0)

	argv.push(OrphanerMode)

	let orphaner = try Subprocess.run(Executable.path(me), arguments: argv) otherwise return 3
	let orphan = try int.fromString(orphaner.stdout.trim().replace(SleeperLabel, with: "")) otherwise return 4
	let found = Process.endDescendants()
	var listedDirect = false
	var listedOrphan = false

	for entry in found 'eachEntry'
		if entry.pid == direct 'theDirectOne'
			listedDirect = true
		end 'theDirectOne'

		if entry.pid == (orphan as Pid) 'theOrphan'
			listedOrphan = true
		end 'theOrphan'
	end 'eachEntry'

	var waitedMs = 0 as DurationMs

	while (Process.isAlive(direct) or Process.isAlive(orphan as Pid)) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	let ended = not Process.isAlive(direct) and not Process.isAlive(orphan as Pid)

	print("found={found.count()} direct={listedDirect} orphan={listedOrphan} ended={ended}\n")
	return 0
end 'runAsProbe'

function main() returns ExitCode
	let args = CommandLine.args()

	if args.count() > 1 'asAChild'
		let mode = try args.get(1) otherwise return 2

		return runAsOrphaner() if mode == OrphanerMode else runAsProbe()
	end 'asAChild'

	var overrides = EnvMap.create()

	overrides.upsert(TrackVariable, value: UntrackedValue)

	let me = try Process.executablePath() otherwise return 2
	var config = Configuration.create(Executable.path(me))

	config.arguments.push(ProbeMode)
	config.environment = Environment.inheritUpdating(overrides)

	let probe = try config.run() otherwise return 3

	print("exit={probe.status.code()} {probe.stdout.trim()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
exit=0 found=2 direct=true orphan=true ended=true
```

<!-- test: process-descendants.an-armed-case-that-leaves-a-sleeper-reports-it-in-a-framed-line -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
The program runs itself as a child with `MAXON_TRACK_DESCENDANTS=A<token>`. The child detaches a long sleeper
and returns 0. Its exit gate finds the sleeper, ends it, and writes one framed line to its stderr naming it;
the exit code stays 0.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let SleeperLabel = "sleeper="
let TrackVariable = "MAXON_TRACK_DESCENDANTS"
let ArmedValue = "A4242"
let FramePrefix = "@@maxon-spec@@!"

typealias PidArray = Array with Pid

function detachSleeper() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("30")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("30")
	#endif

	let pid = try config.runDetached() otherwise return 3

	print("{SleeperLabel}{pid}\n")
	return 0
end 'detachSleeper'

function framedPids(text String) returns PidArray
	var found = PidArray.create(0)

	for line in text.split("\n") 'eachLine'
		if line.startsWith(FramePrefix) 'framed'
			let fields = line.replace(FramePrefix, with: "").split(" ")
			let digits = try fields.get(0) otherwise continue
			let pid = try int.fromString(digits) otherwise continue

			found.push(pid as Pid)
		end 'framed'
	end 'eachLine'

	return found
end 'framedPids'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'asTheChild'
		return detachSleeper()
	end 'asTheChild'

	var overrides = EnvMap.create()

	overrides.upsert(TrackVariable, value: ArmedValue)

	let me = try Process.executablePath() otherwise return 2
	var config = Configuration.create(Executable.path(me))

	config.arguments.push("child")
	config.environment = Environment.inheritUpdating(overrides)

	let child = try config.run() otherwise return 3
	let sleeper = try int.fromString(child.stdout.trim().replace(SleeperLabel, with: "")) otherwise return 4
	let framed = framedPids(child.stderr)
	var listed = false

	for pid in framed 'eachFrame'
		if pid == (sleeper as Pid) 'ours'
			listed = true
		end 'ours'
	end 'eachFrame'

	var waitedMs = 0 as DurationMs

	while Process.isAlive(sleeper as Pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	print("exit={child.status.code()} framed={framed.count()} listed={listed} ended={not Process.isAlive(sleeper as Pid)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
exit=0 framed=1 listed=true ended=true
```

<!-- test: process-descendants.an-armed-case-that-waits-for-its-child-reports-nothing -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
The armed child starts a short detached process and polls it until it has ended, so nothing is left to find at
its exit gate. It also reports the token it sees in its own environment: arming turned the first byte from `A`
into `D`.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let TrackVariable = "MAXON_TRACK_DESCENDANTS"
let ArmedValue = "A4242"
let FramePrefix = "@@maxon-spec@@!"

function waitForShortChild() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("2")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("1")
	#endif

	let pid = try config.runDetached() otherwise return 3
	var waitedMs = 0 as DurationMs

	while Process.isAlive(pid) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	let token = try Process.environmentVariable(TrackVariable) otherwise "unset"

	print("sawD={token.startsWith("D")} ended={not Process.isAlive(pid)}\n")
	return 0
end 'waitForShortChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'asTheChild'
		return waitForShortChild()
	end 'asTheChild'

	var overrides = EnvMap.create()

	overrides.upsert(TrackVariable, value: ArmedValue)

	let me = try Process.executablePath() otherwise return 2
	var config = Configuration.create(Executable.path(me))

	config.arguments.push("child")
	config.environment = Environment.inheritUpdating(overrides)

	let child = try config.run() otherwise return 3
	let framed = child.stderr.contains(FramePrefix)

	print("exit={child.status.code()} childSaid={child.stdout.trim()} framed={framed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
exit=0 childSaid=sawD=true ended=true framed=false
```

<!-- test: process-descendants.the-children-of-an-armed-case-do-not-arm -->
<!-- process: own -->
<!-- unsupported-targets: wasm32-wasi -->
The armed child runs the program a second time, as a grandchild that inherits the token. The grandchild leaves a
sleeper and returns; its first byte reads `D`, so it does not arm and reports nothing. Only the child's exit
gate finds the sleeper, so exactly one framed line reaches the parent.
```maxon
let EndsWithinMs = 30000
let PollMs = 50
let SleeperLabel = "sleeper="
let TrackVariable = "MAXON_TRACK_DESCENDANTS"
let ArmedValue = "A4242"
let FramePrefix = "@@maxon-spec@@!"
let ChildMode = "child"
let GrandchildMode = "grandchild"

typealias PidArray = Array with Pid

function detachSleeper() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("30")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("30")
	#endif

	let pid = try config.runDetached() otherwise return 3
	let token = try Process.environmentVariable(TrackVariable) otherwise "unset"

	print("sawD={token.startsWith("D")}\n")
	print("{SleeperLabel}{pid}\n")
	return 0
end 'detachSleeper'

function framedPids(text String) returns PidArray
	var found = PidArray.create(0)

	for line in text.split("\n") 'eachLine'
		if line.startsWith(FramePrefix) 'framed'
			let fields = line.replace(FramePrefix, with: "").split(" ")
			let digits = try fields.get(0) otherwise continue
			let pid = try int.fromString(digits) otherwise continue

			found.push(pid as Pid)
		end 'framed'
	end 'eachLine'

	return found
end 'framedPids'

function runAsChild() returns ExitCode
	let me = try Process.executablePath() otherwise return 2
	var config = Configuration.create(Executable.path(me))

	config.arguments.push(GrandchildMode)

	let grandchild = try config.run() otherwise return 3

	print("{grandchild.stdout.trim()}\n")
	print("grandchildFrames={framedPids(grandchild.stderr).count()}\n")
	return 0
end 'runAsChild'

function main() returns ExitCode
	let args = CommandLine.args()

	if args.count() > 1 'asAChild'
		let mode = try args.get(1) otherwise return 2

		return detachSleeper() if mode == GrandchildMode else runAsChild()
	end 'asAChild'

	var overrides = EnvMap.create()

	overrides.upsert(TrackVariable, value: ArmedValue)

	let me = try Process.executablePath() otherwise return 2
	var config = Configuration.create(Executable.path(me))

	config.arguments.push(ChildMode)
	config.environment = Environment.inheritUpdating(overrides)

	let child = try config.run() otherwise return 3
	var sleeper = 0 as Pid

	for line in child.stdout.split("\n") 'eachLine'
		if line.startsWith(SleeperLabel) 'theSleeper'
			let parsed = try int.fromString(line.trim().replace(SleeperLabel, with: "")) otherwise return 4

			sleeper = parsed as Pid
		end 'theSleeper'
	end 'eachLine'

	let framed = framedPids(child.stderr)
	var listed = false

	for pid in framed 'eachFrame'
		if pid == sleeper 'ours'
			listed = true
		end 'ours'
	end 'eachFrame'

	var waitedMs = 0 as DurationMs

	while Process.isAlive(sleeper) and waitedMs < EndsWithinMs 'untilEnded'
		sleep(PollMs)
		waitedMs = waitedMs + PollMs
	end 'untilEnded'

	let grandchildInert = child.stdout.contains("sawD=true") and child.stdout.contains("grandchildFrames=0")

	print("exit={child.status.code()} framed={framed.count()} listed={listed} grandchildInert={grandchildInert} ended={not Process.isAlive(sleeper)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
exit=0 framed=1 listed=true grandchildInert=true ended=true
```

<!-- test: process-descendants.a-shell-that-is-forking-when-the-audit-finds-it-leaves-nothing-behind -->
<!-- process: own -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A detached shell runs `sleep` as a command of its own, so the shell forks while the audit stops it. The
audit ends the shell and whatever the shell forked, whichever side of the stop the fork fell on: a second
audit right after each first one finds nothing, over a hundred shells started and audited back to back.
```maxon
let Laps = 100

function main() returns ExitCode
	let armed = Process.endDescendants()
	var foundShells = 0 as Count
	var missed = 0 as Count
	var lap = 0 as Count

	while lap < Laps 'laps'
		var config = Configuration.create(Executable.path(try FilePath.from("/bin/sh") otherwise return 2))

		config.arguments.push("-c")
		config.arguments.push("sleep 60; true")

		_ = try config.runDetached() otherwise return 3

		foundShells = foundShells + Process.endDescendants().count()
		missed = missed + Process.endDescendants().count()
		lap = lap + 1
	end 'laps'

	print("armed={armed.count()} laps={lap} foundEveryShell={foundShells >= Laps} missed={missed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
armed=0 laps=100 foundEveryShell=true missed=0
```

<!-- test: process-descendants.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	let found = Process.endDescendants()

	return 0 if found.count() == 0 else 1
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:22: 'Process.endDescendants' lowers to the runtime entry '__proc_end_descendants_text', which has no wasm32-wasi implementation
```
