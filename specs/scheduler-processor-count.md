---
feature: scheduler-processor-count
status: stable
keywords: [runtime, processorCount, scheduler, parallelism, MAXON_MAX_PROCS, services, green-threads]
category: concurrency
---

# `Scheduler.processorCount()` — how many processors services run on

## Documentation

`Scheduler.processorCount()` answers the number of processors the green-thread scheduler runs services on:
the machine's logical processor count, or the count `MAXON_MAX_PROCS` sets. Like Go's `GOMAXPROCS`, a set
count is taken exactly whether it is below the machine's count or above it. A value that is not a decimal
fitting a positive 32-bit signed integer — `0`, a non-number, or a number above 2147483647 — is ignored, and
the machine's count applies, as Go's `schedinit` ignores a `GOMAXPROCS` it cannot parse as a positive
`int32`. It is the number a program sizes a pool of services by.

### The answer never depends on what the program has already started

The scheduler resolves its processor count once, before `main` runs. Asking is itself a use of the
scheduler, so a program that asks has it installed and resolved whether or not it ever spawns anything —
the answer is never 0, and a count read before the first `spawn` is the count every later service runs on.

### Targets

It is a scheduler query, and `wasm32-wasi` has no green-thread scheduler, so the call is refused there with
`E3104`.

## Tests

<!-- test: scheduler-processor-count.the-machine-count-by-default -->
With no `MAXON_MAX_PROCS` in the environment the scheduler builds one processor per logical CPU, and a
program that spawns nothing is told exactly that.
```maxon
function main() returns ExitCode
	if Scheduler.processorCount() == __Builtins.cpuCount() 'everyProcessor'
		return 3
	end 'everyProcessor'
	return 1
end 'main'
```
```exitcode
3
```

<!-- test: scheduler-processor-count.pinned-to-one -->
<!-- procs: 1 -->
`MAXON_MAX_PROCS=1` resolves the scheduler to one processor, and the answer is that resolution rather than
the machine's count.
```maxon
function main() returns ExitCode
	print("procs={Scheduler.processorCount()}\n")
	return 0
end 'main'
```
```stdout
procs=1
```

<!-- test: scheduler-processor-count.a-request-above-the-machine-is-honoured -->
<!-- procs: 64 -->
A requested count above the machine's is taken exactly, as Go takes a `GOMAXPROCS` above `NumCPU`. 64 is
above the processor count of every host a lane runs on, so the answer can only be 64 if the request was
honoured rather than lowered to the machine's count.
```maxon
function main() returns ExitCode
	print("procs={Scheduler.processorCount()}\n")
	return 0
end 'main'
```
```stdout
procs=64
```

<!-- test: scheduler-processor-count.a-request-beyond-32-bits-is-ignored -->
<!-- unsupported-targets: wasm32-wasi -->
`18446744073709551621` is `2^64 + 5`: a decimal walk that wraps in 64 bits reads it as 5, and a count is only
a count when it fits a positive 32-bit signed integer, so the value is ignored and the machine's count
applies. The `procs:` marker takes only a count the harness can read as one, so the program re-executes
itself with that text as `MAXON_MAX_PROCS` in the child's environment and prints what the child reports.
The child compares against `__Builtins.cpuCount()`, which keeps the answer the same on every host. Not on
`wasm32-wasi`, where the subprocess band is refused at compile time (`subprocess-unsupported.md`).
```maxon
function child() returns ExitCode
	let machine = Scheduler.processorCount() == __Builtins.cpuCount()
	print("machine={machine}\n")
	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_MAX_PROCS": "18446744073709551621"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child {run.stdout}")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```stdout
child machine=true
child exit=0
```
```exitcode
0
```

<!-- test: scheduler-processor-count.a-count-with-trailing-text-is-ignored -->
<!-- unsupported-targets: wasm32-wasi -->
`12abc` starts with a count and is not one: the whole value must be a decimal, so a trailing suffix makes it
ignored rather than read as 12, and the machine's count applies. The same re-execution as its sibling
above, with this text as `MAXON_MAX_PROCS`.
```maxon
function child() returns ExitCode
	let machine = Scheduler.processorCount() == __Builtins.cpuCount()
	print("machine={machine}\n")
	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_MAX_PROCS": "12abc"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child {run.stdout}")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```stdout
child machine=true
child exit=0
```
```exitcode
0
```

<!-- test: scheduler-processor-count.a-count-with-a-leading-plus-is-taken -->
<!-- unsupported-targets: wasm32-wasi -->
`+4` is a decimal, as Go's `strconv.ParseInt` reads one: a leading `+` is part of the number, so the count
is 4. The same re-execution as the siblings above, with this text as `MAXON_MAX_PROCS`.
```maxon
function child() returns ExitCode
	print("procs={Scheduler.processorCount()}\n")
	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_MAX_PROCS": "+4"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child {run.stdout}")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```stdout
child procs=4
child exit=0
```
```exitcode
0
```

<!-- test: scheduler-processor-count.a-count-longer-than-thirty-two-bytes-is-read-whole -->
<!-- unsupported-targets: wasm32-wasi -->
Forty `0`s and a `4` are 41 bytes of one decimal whose value is 4. The length of the text is not what makes a
count; its value is, so a value longer than any first read's buffer is read whole and the count is 4. The
same re-execution as the siblings above, with this text as `MAXON_MAX_PROCS`.
```maxon
function child() returns ExitCode
	print("procs={Scheduler.processorCount()}\n")
	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	var argv = StringArray.create()
	argv.push("child")

	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_MAX_PROCS": "00000000000000000000000000000000000000004"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child {run.stdout}")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```stdout
child procs=4
child exit=0
```
```exitcode
0
```

<!-- test: scheduler-processor-count.asked-before-the-first-spawn -->
A pool is sized BEFORE its services exist, so the count read ahead of the first `spawn` must be the one the
services then run on.
```maxon
typealias Tally = int(0 to 1000000)

type Worker
	var done as Tally

	static function create() returns Self
		return Self{done: 0}
	end 'create'

	export function work(units Tally) returns Tally
		self.done = self.done + units
		return self.done
	end 'work'
end 'Worker'

function main() returns ExitCode
	let before = Scheduler.processorCount()
	let h = spawn Worker.create()
	let units = try await h.work(4) otherwise 0
	let after = Scheduler.processorCount()

	if before == after and before >= 1 'stable'
		return units as ExitCode
	end 'stable'
	return 1
end 'main'
```
```exitcode
4
```

<!-- test: scheduler-processor-count.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A WASI component has no green-thread scheduler, so the call is refused at the span the user wrote, naming
the stdlib function rather than a line inside `stdlib/`.
```maxon
function main() returns ExitCode
	let n = Scheduler.processorCount()
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:20: 'Scheduler.processorCount' lowers to the runtime entry '__sched_processor_count', which has no wasm32-wasi implementation
```
