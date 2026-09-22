---
feature: scheduler-processor-count
status: stable
keywords: [runtime, processorCount, scheduler, parallelism, MAXON_MAX_PROCS, services, green-threads]
category: concurrency
---

# `Scheduler.processorCount()` — how many processors services run on

## Documentation

`Scheduler.processorCount()` answers the number of processors the green-thread scheduler runs services on:
the machine's logical processor count, or the count `MAXON_MAX_PROCS` sets, clamped to between 1 and the
machine's count. It is the number a program sizes a pool of services by.

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

<!-- test: scheduler-processor-count.never-above-the-machine -->
<!-- procs: 2 -->
A requested count above the machine's is capped at the machine's, so on a one-processor host the answer is
1 and everywhere else it is the 2 requested.
```maxon
function main() returns ExitCode
	let requested = 2
	let cpus = __Builtins.cpuCount()
	let expected = requested if cpus > requested else cpus

	if Scheduler.processorCount() == expected 'clamped'
		return 5
	end 'clamped'
	return 1
end 'main'
```
```exitcode
5
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
