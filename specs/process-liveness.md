---
feature: process-liveness
status: stable
keywords: [process, processIsAlive, pid, liveness, __Builtins, intrinsics]
category: system
---

# `__Builtins.processIsAlive(pid)` — whether a process with that id exists

## Documentation

| Intrinsic | Meaning |
|---|---|
| `__Builtins.processIsAlive(pid)` | `true` while a process with id `pid` exists, `false` once it has ended and been reaped |

## Tests

<!-- test: process-liveness.this-process-is-alive -->
```maxon
function main() returns ExitCode
	if __Builtins.processIsAlive(__Builtins.currentProcessId()) 'alive'
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
	if __Builtins.processIsAlive(0) 'claimed'
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
	if __Builtins.processIsAlive(4294967296 + __Builtins.currentProcessId()) 'claimed'
		return 1
	end 'claimed'
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-negative-id-is-not-alive -->
```maxon
function main() returns ExitCode
	if __Builtins.processIsAlive(0 - 1) 'claimed'
		return 1
	end 'claimed'
	if __Builtins.processIsAlive(0 - __Builtins.currentProcessId()) 'mirrored'
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
	let pid = __Builtins.currentProcessId()
	for offset in 1 to 3 'offGranule'
		if __Builtins.processIsAlive(pid + offset) 'claimed'
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
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-Command")
	argv.push("$PID")
	var child = try StreamingSubprocess.spawn(Executable.name("powershell"), arguments: argv) otherwise return 2
	let line = try child.readStdoutLine() otherwise return 4
	let pid = try int.fromString(line.trim()) otherwise return 5
	let code = try child.wait() otherwise return 6
	var answer = 3 as ExitCode
	if code != 0 'failed'
		answer = 8
	end 'failed' else if __Builtins.processIsAlive(pid) 'stillThere'
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
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit")
	argv.push("0")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exit 0")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if result.pid == 0 'noId'
		return 4
	end 'noId'
	if __Builtins.processIsAlive(result.pid) 'stillThere'
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
function main() returns ExitCode
	#if os(Windows)
	var config = Configuration.create(Executable.name("ping"))
	config.arguments.push("-n")
	config.arguments.push("3")
	config.arguments.push("127.0.0.1")
	#else
	var config = Configuration.create(Executable.path(try FilePath.from("/bin/sleep") otherwise return 2))
	config.arguments.push("2")
	#endif
	let pid = try config.runDetached() otherwise return 2
	if __Builtins.processIsAlive(pid) 'alive'
		return 3
	end 'alive'
	return 4
end 'main'
```
```exitcode
3
```

<!-- test: process-liveness.a-program-alias-named-like-the-runtimes-parameter-changes-nothing -->
The id is converted to the runtime entry's parameter type as the runtime file declares it, range included. A
program that declares a narrower alias of the same name does not narrow the check on its pid.
```maxon
typealias ProbeWord = int(0 to 10)

function main() returns ExitCode
	let small = 7 as ProbeWord
	if __Builtins.processIsAlive(__Builtins.currentProcessId()) 'alive'
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
	if __Builtins.processIsAlive(__Builtins.currentProcessId()) 'alive'
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
typealias WidePid = int(0 to u64.max)

function main() returns ExitCode
	if __Builtins.processIsAlive(u64.max as WidePid) 'claimed'
		return 1
	end 'claimed'

	let pids = [u64.max as WidePid, 9223372036854775807 as WidePid]

	for pid in pids 'each'
		if __Builtins.processIsAlive(pid) 'alive'
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
	if __Builtins.processIsAlive(1) 'alive'
		return 1
	end 'alive'
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:16: this construct lowers to the runtime entry '__proc_alive', which has no wasm32-wasi implementation
```
