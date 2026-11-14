---
feature: process-liveness
status: stable
keywords: [process, Process.isAlive, processIsAlive, pid, liveness]
category: system
---

# `Process.isAlive(pid)` — whether a process with that id exists

## Documentation

| Function | Meaning |
|---|---|
| `Process.isAlive(pid Pid) returns bool` | `true` while a process with id `pid` is running, `false` once it has ended, whether or not its parent has reaped it |

An id no host can hand out — zero, one past the 32-bit range, any `Pid` up to `u64.max` — answers `false`,
never a range panic. Its body is the library-tier intrinsic `__Builtins.processIsAlive(pid)`, which takes a
signed word; the cases pinning that intrinsic's negative words and arity compile as library-tier
sources (`<!-- tier: stdlib -->`).

## Tests

<!-- test: process-liveness.this-process-is-alive -->
```maxon
function main() returns ExitCode
	if Process.isAlive(Process.id()) 'alive'
		return 3
	end 'alive'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.no-process-has-id-zero -->
```maxon
function main() returns ExitCode
	if Process.isAlive(0) 'claimed'
		return 1
	end 'claimed'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.an-id-past-every-host-range-is-not-alive -->
```maxon
function main() returns ExitCode
	if Process.isAlive(4294967296 + Process.id()) 'claimed'
		return 1
	end 'claimed'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-negative-id-is-not-alive -->
<!-- tier: stdlib -->
```maxon
typealias SignedWord = int(i64.min to i64.max)

function main() returns ExitCode
	let own = __Builtins.currentProcessId() as SignedWord
	if __Builtins.processIsAlive(0 - 1) 'claimed'
		return 1
	end 'claimed'
	if __Builtins.processIsAlive(0 - own) 'mirrored'
		return 4
	end 'mirrored'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-windows-id-off-the-granule-is-not-alive -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
```maxon
function main() returns ExitCode
	let pid = Process.id()
	for offset in 1 to 3 'offGranule'
		if Process.isAlive(pid + offset) 'claimed'
			return offset
		end 'claimed'
	end 'offGranule'
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: process-liveness.an-exited-child-still-held-is-not-alive -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
```maxon
function main() returns ExitCode
	let argv = StringArray.create(0)
	argv.push("-NoProfile")
	argv.push("-Command")
	argv.push("$PID")
	let child = try StreamingSubprocess.spawn(Executable.name("powershell"), arguments: argv) otherwise return 2
	let output = try child.takeStdout() otherwise return 4
	let read = try output.readLine() otherwise return 4

	let printed = match read 'read'
		line(text) gives text
		ended gives ""
	end 'read'

	let pid = try int.fromString(printed.trim()) otherwise return 5
	let code = try child.wait() otherwise return 6
	var answer = 3 as ExitCode
	if code != 0 'failed'
		answer = 8
	end 'failed' else if Process.isAlive(pid as Pid) 'stillThere'
		answer = 9
	end 'stillThere'
	child.release()
	return answer
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-reaped-child-is-gone -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	let argv = StringArray.create(0)
	argv.push("/c")
	argv.push("exit")
	argv.push("0")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	let argv = StringArray.create(0)
	argv.push("-c")
	argv.push("exit 0")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if result.pid == 0 'noId'
		return 4
	end 'noId'
	if Process.isAlive(result.pid) 'stillThere'
		return 5
	end 'stillThere'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-running-child-is-alive -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
let ChildEndsWithinMs = 30000
let ChildPollMs = 50

function main() returns ExitCode
	#if os(Windows)
	let config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("3")
	config.arguments.push("127.0.0.1")
	#else
	let config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("2")
	#endif
	let pid = try config.runDetached() otherwise return 2
	let aliveAtStart = Process.isAlive(pid)
	var waitedMs = 0 as DurationMs
	while Process.isAlive(pid) and waitedMs < ChildEndsWithinMs 'untilItEnds'
		sleep(ChildPollMs)
		waitedMs = waitedMs + ChildPollMs
	end 'untilItEnds'
	if Process.isAlive(pid) 'neverEnded'
		return 5
	end 'neverEnded'
	if aliveAtStart 'alive'
		return 3
	end 'alive'
	return 4
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-detached-child-that-ended-reads-ended-though-unreaped -->
<!-- unsupported-targets: wasm32-wasi -->
A detached child is one nobody waits for, so under POSIX it stays an unreaped zombie after it ends. It reads as ended all the same: the id still names an entry in the process table, and `isAlive` answers about the process.
```maxon
let ChildEndsWithinMs = 30000
let ChildPollMs = 50

function main() returns ExitCode
	#if os(Windows)
	let config = Configuration.create(Executable.name("cmd"))
	config.arguments.push("/c")
	config.arguments.push("exit")
	config.arguments.push("0")
	#else
	let config = Configuration.create(Executable.path(try FilePath.from("/bin/sh") otherwise return 2))
	config.arguments.push("-c")
	config.arguments.push("exit 0")
	#endif
	let pid = try config.runDetached() otherwise return 2
	var waitedMs = 0 as DurationMs
	var everReadEnded = false
	while waitedMs < ChildEndsWithinMs and not everReadEnded 'untilItEnds'
		if not Process.isAlive(pid) 'ended'
			everReadEnded = true
		end 'ended' else 'running'
			sleep(ChildPollMs)
			waitedMs = waitedMs + ChildPollMs
		end 'running'
	end 'untilItEnds'
	print("ended={everReadEnded}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ended=true
```

<!-- test: process-liveness.a-program-alias-named-like-the-runtimes-parameter-changes-nothing -->
The id is converted to the runtime entry's parameter type as the runtime file declares it, range included. A
program that declares a narrower alias of the same name does not narrow the check on its pid.
```maxon
typealias ProbeWord = int(0 to 10)

function main() returns ExitCode
	let small = 7 as ProbeWord
	if Process.isAlive(Process.id()) 'alive'
		return (small - 4) as ExitCode
	end 'alive'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-program-type-named-like-the-runtimes-parameter-changes-nothing -->
The same for a program TYPE of that name: the call site writes no cast, so it earns no cast diagnostic.
```maxon
typealias Tally = int(0 to 100)

type ProbeWord
	export let count as Tally

	static function create() returns ProbeWord
		return ProbeWord{count: 3}
	end 'create'
end 'ProbeWord'

function main() returns ExitCode
	let probe = ProbeWord.create()
	if Process.isAlive(Process.id()) 'alive'
		return probe.count as ExitCode
	end 'alive'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.an-unsigned-id-past-every-host-range-is-not-alive -->
An unsigned id no signed word can hold names no process, and asking about one answers `false`, never a range
panic.
```maxon
function main() returns ExitCode
	if Process.isAlive(u64.max as Pid) 'claimed'
		return 1
	end 'claimed'

	let pids = [u64.max as Pid, 9223372036854775807 as Pid]

	for pid in pids 'each'
		if Process.isAlive(pid) 'alive'
			return 2
		end 'alive'
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: process-liveness.arity-checked -->
<!-- tier: stdlib -->
```maxon
function main() returns ExitCode
	if __Builtins.processIsAlive() 'alive'
		return 1
	end 'alive'
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:16: '__Builtins.processIsAlive' takes exactly 1 argument, but 0 were given
```

<!-- test: process-liveness.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	if Process.isAlive(1) 'alive'
		return 1
	end 'alive'
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:13: 'Process.isAlive' lowers to the runtime entry '__proc_alive', which has no wasm32-wasi implementation
```
