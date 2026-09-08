---
feature: sleep
status: experimental
keywords: [sleep, async, concurrency, timer]
category: concurrency
---

# Sleep

## Documentation

The `sleep` function suspends the current green thread for a specified number of milliseconds. It yields to the scheduler, allowing other green threads to run during the sleep period.

```text
sleep(500)  // sleep for 500 milliseconds
```

`sleep` works in both async green threads and the main thread. It is a cooperative yield point — other green threads can execute while the current one sleeps.

## Tests

<!-- test: sleep.basic -->
```maxon
function main() returns ExitCode
		sleep(10)
		print("done\n")
		return 0
end 'main'
```
```stdout
done
```

<!-- test: sleep.async-interleave -->
```maxon
typealias Integer = int(i64.min to i64.max)

function slowTask() returns Integer
		sleep(200)
		print("slow\n")
		return 1
end 'slowTask'

function fastTask() returns Integer
		sleep(10)
		print("fast\n")
		return 2
end 'fastTask'

function main() returns ExitCode
		let p1 = async slowTask()
		let p2 = async fastTask()
		let r1 = await p1
		let r2 = await p2
		print("r1={r1} r2={r2}\n")
		return 0
end 'main'
```
```stdout
fast
slow
r1=1 r2=2
```

<!-- test: sleep.zero -->
```maxon
function main() returns ExitCode
		sleep(0)
		print("ok\n")
		return 0
end 'main'
```
```stdout
ok
```

<!-- test: sleep.main-thread-with-concurrent-async-io -->
A `sleep()` on the MAIN thread lasts its full duration even while an `async`
worker is outstanding. The worker is expected to run *during* the sleep — that is
what a cooperative timer is for — but parking it on I/O must not end the sleep.

Every `async` worker reaches this: a function that never yields cannot be spawned
at all (`async-await.error.no-yield`), so an outstanding worker parked on I/O is
the normal case rather than an unlucky interleaving.

The assertion is a WIDE BAND, never an exact duration. The defect it pins returned
in **0 ms** against a 300 ms request, so a lower bound of 250 ms separates "slept"
from "did not sleep" with 250 ms of margin, and the upper bound is loose enough to
survive any scheduling delay a loaded host can add.

```maxon
typealias Probe = int(0 to 1000000)

function probe() returns Probe
		for _ in 0 upto 20 'each'
				_ = File.exists(FilePath from "noyield.txt")
		end 'each'
		return 7
end 'probe'

function main() returns ExitCode
		let worker = async probe()
		let start = Clock.nowMs()
		sleep(300)
		let elapsed = Clock.elapsedMs(start)
		let probed = await worker
		var score = 0
		if elapsed >= 250 'slept'
				score = score + 1
		end 'slept'
		if elapsed < 10000 'bounded'
				score = score + 1
		end 'bounded'
		print("score={score} probed={probed}\n")
		return 0
end 'main'
```
```stdout
score=2 probed=7
```

<!-- test: sleep.main-thread-alone -->
The control for the case above: the same 300 ms main-thread sleep, measured the
same way, with the `async` worker removed. It is deliberately close to a duplicate
— that is its whole value. A failure of the pair's first case with this one green
is attributable to the concurrent worker, and not to the clock, the timer heap or
the host.

```maxon
function main() returns ExitCode
		let start = Clock.nowMs()
		sleep(300)
		let elapsed = Clock.elapsedMs(start)
		var score = 0
		if elapsed >= 250 'slept'
				score = score + 1
		end 'slept'
		if elapsed < 10000 'bounded'
				score = score + 1
		end 'bounded'
		print("score={score}\n")
		return 0
end 'main'
```
```stdout
score=2
```

<!-- test: sleep.trace-sleep-yields-the-green-thread -->
<!-- AsyncTrace -->
Verify that a `sleep` inside a green thread yields and resumes it, and that the trace names the SLEEP
rather than an I/O operation.

This is the pin for the `sleep_yield`/`sleep_resume` tags. Without it the tags are emitted by the runtime
and asserted by nothing: `/specs/http-client.md`'s `async-trace-interleave` is the only canonical case that
names them, and it is `disabled-test:` there and here — it additionally needs a sync-worker I/O pool and
runnext scheduling, neither of which this compiler has.

The shape is `async-await.trace-yield`'s, one operation over: a spawn, the yield, the scheduler taking the
worker, the resume, and the await observing an already-yielded promise.
```maxon
typealias Integer = int(i64.min to i64.max)

function napper() returns Integer
	sleep(1)
	return 7
end 'napper'

function main() returns ExitCode
	let p = async napper()
	let r = await p
	return r
end 'main'
```
```exitcode
7
```
```stderr
spawn #1
sleep_yield #1
worker_start #1
sleep_resume #1
await #1 [yield]
worker_exit #1
worker_start #2
worker_exit #2
```
