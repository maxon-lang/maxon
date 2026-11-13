---
feature: cooperative-cancel
status: experimental
keywords: [cancel, promise, drop, sleep, coroutine, linear, finalizer, async]
category: concurrency
---

# Cooperative cancellation

## Documentation

Cancelling a coroutine, with `.cancel()` or by dropping its promise, never tears it down mid-frame. It marks the
coroutine cancelled, and the coroutine runs to completion through its own code. A `sleep` the coroutine is parked in
ends early, and every `sleep` it makes afterwards yields once, so its siblings run, and returns at once. A linear value the coroutine holds is consumed by its
own code, on the path the early return of the wait leads it down, so a finalizer runs and nothing leaks.

`.cancel()` on a promise that is not tied returns at once and the coroutine finishes on its own, its result discarded.
On a tied promise it waits for the coroutine to finish.

## Tests

<!-- test: a-cancelled-coroutine-parked-in-sleep-consumes-its-linear-value -->
<!-- procs: 1 -->
The coroutine builds a `Resp`, parks in `sleep(20000)`, and is cancelled. The sleep ends early, the second sleep returns at once, and the coroutine's own code consumes the value: the finalizer prints and the exit code is 0.
```maxon
typealias Code = int(0 to 999)

type Resp
	var code as Code

	static function create(code Code) returns Self
		return Self{code: code}
	end 'create'

	function peek() returns Code
		return self.code
	end 'peek'

	function fin(consuming self)
		print("fin {self.code}\n")
		destruct self
	end 'fin'
end 'Resp'

function worker() returns Code
	let r = Resp.create(1)
	print("parking\n")
	sleep(20000)
	print("woke\n")
	sleep(20000)
	print("woke again\n")
	let n = r.peek()
	r.fin()
	return n
end 'worker'

function main() returns ExitCode
	let p = async worker()
	sleep(200)
	p.cancel()
	print("cancelled\n")
	sleep(500)
	print("done\n")
	return 0
end 'main'
```
```stdout
parking
cancelled
woke
woke again
fin 1
done
```
```exitcode
0
```

<!-- test: every-sleep-of-a-cancelled-coroutine-returns-at-once -->
<!-- procs: 1 -->
A loop of long sleeps in a cancelled coroutine runs through every iteration without waiting.
```maxon
typealias Tick = int(0 to 99)

function ticker() returns Tick
	var ticks = 0 as Tick

	for _ in 0 upto 3 'each'
		sleep(20000)
		ticks = ticks + 1
	end 'each'

	print("ticks {ticks}\n")

	return ticks
end 'ticker'

function main() returns ExitCode
	let p = async ticker()
	sleep(200)
	p.cancel()
	sleep(500)
	print("done\n")
	return 0
end 'main'
```
```stdout
ticks 3
done
```
```exitcode
0
```

<!-- test: a-sleep-of-a-cancelled-coroutine-lets-a-sibling-run-before-it-returns -->
<!-- procs: 1 -->
<!-- preempt: off -->
One processor and no preemption leave the cancelled coroutine's own sleeps as the only point where a sibling can run. The first sleep ends early when the coroutine is cancelled, and the second returns at once but yields once first, so the sibling spawned behind the cancelled coroutine prints before the coroutine finishes its loop.
```maxon
typealias Tick = int(0 to 99)

function ticker() returns Tick
	var ticks = 0 as Tick

	for _ in 0 upto 3 'each'
		sleep(20000)
		ticks = ticks + 1
	end 'each'

	print("ticks {ticks}\n")

	return ticks
end 'ticker'

function sibling() returns Tick
	print("sibling\n")

	return 1
end 'sibling'

function main() returns ExitCode
	let p = async ticker()
	sleep(200)
	p.cancel()
	let q = async sibling()
	_ = await q
	sleep(300)
	print("done\n")
	return 0
end 'main'
```
```stdout
sibling
ticks 3
done
```
```exitcode
0
```
