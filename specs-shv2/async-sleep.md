---
feature: async-sleep
status: stable
keywords: [sleep, async, await, green-threads, scheduler, timer, yield, concurrency]
category: concurrency
---

# Sleep — the mid-body yield and netpoller (P1.5)

## Documentation

`sleep(ms)` suspends the **current green thread** for `ms` milliseconds and yields to the scheduler, so
other green threads run while it waits. It is the first **mid-body yield**: unlike a B1a async body, which
runs to completion in one shot, a sleeping thread parks on a timer, hands control back, and RESUMES where it
left off once its deadline has passed.

```text
function main() returns ExitCode
	sleep(10)
	return 0
end 'main'
```

`sleep` works from the main thread (`GT0`) and from a spawned `async` thread alike. When the run queue
empties, the scheduler **netpolls** — it waits on the earliest timer deadline with a real OS sleep (never a
busy-spin) — and re-enqueues each parked thread once its deadline arrives. Because the wait is on the
EARLIEST deadline, threads resume in deadline order: a shorter sleep resumes before a longer one regardless
of spawn order.

The argument is an integer count of milliseconds; a `float`/`String`/`bool` is refused. `sleep` returns
nothing, so its result may not be used in value position.

## Tests

<!-- test: async-sleep.basic -->
<!-- targets: x64-windows -->
The main thread (GT0) sleeps, then returns a value: GT0 parks on the timer, the netpoll waits, and GT0
resumes with its state intact.
```maxon
function main() returns ExitCode
	sleep(50)
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: async-sleep.resume-state -->
<!-- targets: x64-windows -->
A spawned green thread's frame survives the mid-body yield: a value live across the `sleep` (the parameter
`base`) is intact after the context switch back into the thread, so `base + 2` is correct.
```maxon

function worker(base int) returns int
	sleep(30)
	return base + 2
end 'worker'

function main() returns ExitCode
	let p = async worker(40)
	let r = await p
	return r as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: async-sleep.interleave -->
<!-- targets: x64-windows -->
Two async threads sleep for different durations; the shorter-sleep one resumes and observes FIRST. Each
records its completion order into a global (`order = order * 10 + tag`), so `21` proves the fast thread
(tag 2) completed before the slow thread (tag 1) — deadline order, not spawn order.
```maxon
var order = 0

function slow() returns int
	sleep(100)
	order = order * 10 + 1
	return 1
end 'slow'

function fast() returns int
	sleep(10)
	order = order * 10 + 2
	return 2
end 'fast'

function main() returns ExitCode
	let p1 = async slow()
	let p2 = async fast()
	let r1 = await p1
	let r2 = await p2
	return order as ExitCode
end 'main'
```
```exitcode
21
```

<!-- test: async-sleep.zero -->
<!-- targets: x64-windows -->
`sleep(0)` parks on a deadline of "now" and resumes promptly (the netpoll fires it on the first poll).
```maxon
function main() returns ExitCode
	sleep(0)
	return 5
end 'main'
```
```exitcode
5
```

<!-- test: async-sleep.spawn-loop -->
<!-- targets: x64-windows -->
Robustness: fifty spawned threads each sleep then complete, awaited in turn. Each parks (yielded, NOT
completed — its stack must NOT be recycled while parked) then resumes and completes (stack recycled onto the
free-list). The sum proves all fifty ran to completion with no crash, no use-after-free, and no leak.
```maxon
function sleeper() returns int
	sleep(2)
	return 1
end 'sleeper'

function main() returns ExitCode
	var i = 0
	var sum = 0
	while i < 50 'l'
		let p = async sleeper()
		let r = await p
		sum = sum + r
		i = i + 1
	end 'l'
	return sum as ExitCode
end 'main'
```
```exitcode
50
```

<!-- test: async-sleep.float-arg-rejected -->
<!-- targets: x64-windows -->
`sleep` requires an integer millisecond count; a float is refused at compile time.
```maxon
function main() returns ExitCode
	sleep(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:2: 'sleep' requires a integer, but its argument is float
```
