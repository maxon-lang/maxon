---
feature: netpoll-idle
status: stable
keywords: [scheduler, netpoll, idle, park, timers, wakeNetPoller, sysmon, GMP]
category: system
---

# An idle scheduler sleeps until there is something to do

## Documentation

Go's idle machines do not poll. A machine that finds nothing to run gives its processor back and, if no
other machine is already waiting in the network poller, blocks there until the earliest timer's deadline or
an I/O event; every other idle machine parks on its own note with no timeout
(`vendor/go/src/runtime/proc.go`, `findRunnable`'s tail and `stopm`). A timer armed earlier than the
poller's deadline interrupts the poller (`wakeNetPoller`), so waking on time costs nothing while nothing is
due.

`__Builtins.schedParkWakeCount()` counts how many times a parked machine's wait has returned, for any
reason — a timeout, a hand-off, a stray signal. It is the number an idle program should keep near zero.

## Tests

<!-- test: netpoll-idle.an-idle-sleep-wakes-no-machine -->
<!-- procs: 4 -->
**A SECOND OF SLEEP WAKES AT MOST TWO PARKED WAITS.** Four processors, one sleeping `main`, nothing else.
```maxon
// A second of sleep at four processors. One machine waiting in the poller wakes once, at the deadline; the
// others stay parked. A scheduler that polls on a timeout wakes several times per machine per second.
let quietWakes = 2

function main() returns ExitCode
	sleep(1)
	let before = __Builtins.schedParkWakeCount()
	sleep(1000)
	let wakes = __Builtins.schedParkWakeCount() - before
	print("quiet={wakes <= quietWakes}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
quiet=true
```
```exitcode
0
```

<!-- test: netpoll-idle.an-idle-sleep-wakes-no-machine-at-one-processor -->
<!-- procs: 1 -->
The same second at one processor.
```maxon
// A second of sleep at one processor. The one machine waits in the poller and wakes once, at the deadline.
let quietWakes = 2

function main() returns ExitCode
	sleep(1)
	let before = __Builtins.schedParkWakeCount()
	sleep(1000)
	let wakes = __Builtins.schedParkWakeCount() - before
	print("quiet={wakes <= quietWakes}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
quiet=true
```
```exitcode
0
```

<!-- test: netpoll-idle.an-earlier-timer-interrupts-a-later-wait -->
<!-- procs: 2 -->
**A DEADLINE ARMED EARLIER THAN THE ONE THE POLLER WAITS FOR ENDS ITS WAIT.** A service sleeps 600 ms and
`main` stays busy long enough for an idle machine to block in the poller on that deadline; `main` then sleeps
50 ms and must wake near 50, not at 600.
```maxon
typealias Integer = int(i64.min to i64.max)

// Far below the long sleep and far above the short one: a short sleep that waited for the long deadline
// reads 600.
let lateMs = 300

type Sleeper
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function nap() returns Integer
		sleep(600)
		return 1
	end 'nap'
end 'Sleeper'

function main() returns ExitCode
	let s = spawn Sleeper.create()
	let r = s.nap()
	sleep(20)
	// Busy, so this machine does not go idle: the other machine settles into the poller on the 600 ms
	// deadline first, and only an interrupt can end that wait at 50.
	let spinFrom = Clock.nowMs()
	var spins = 0

	while Clock.elapsedMs(spinFrom) < 10 'spin'
		spins = spins + 1
	end 'spin'

	let start = Clock.nowMs()
	sleep(50)
	let tookMs = Clock.elapsedMs(start) as Integer
	let v = try await r otherwise 0
	print("late={tookMs >= lateMs} v={v}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
late=false v=1
```
```exitcode
0
```

<!-- test: netpoll-idle.a-break-reaches-the-poller-while-a-former-poller-runs -->
<!-- procs: 2 -->
**A BREAK ENDS THE POLLER'S WAIT WHILE ANOTHER MACHINE THAT ONCE POLLED IS BUSY.** `main`'s machine waits in the
poller first, then stays busy while a service's 50 ms sleep breaks the other machine's wait on a 900 ms
deadline. The service must wake near 50, not when `main` stops running at 600.
```maxon
typealias Integer = int(i64.min to i64.max)

// Far below the busy stretch and the long sleep, far above the short one.
let lateMs = 300

type Sleeper
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function nap(ms Integer) returns Integer
		let start = Clock.nowMs()
		sleep(ms as Milliseconds)
		return Clock.elapsedMs(start) as Integer
	end 'nap'
end 'Sleeper'

function spinMs(ms Integer) returns Integer
	let began = Clock.nowMs()
	var spins = 0

	while (Clock.elapsedMs(began) as Integer) < ms 'spin'
		spins = spins + 1
	end 'spin'

	return spins
end 'spinMs'

function main() returns ExitCode
	let longer = spawn Sleeper.create()
	let shorter = spawn Sleeper.create()
	let r1 = longer.nap(900)

	// `main`'s machine is the poller for this one, and then the other machine polls on the 900 ms deadline.
	sleep(20)
	let settled = spinMs(10)

	// Armed by a third machine while `main`'s keeps running: only the break can end the poller's wait at 50.
	let r2 = shorter.nap(50)
	let busy = spinMs(600)
	let took = try await r2 otherwise 0
	let slept = try await r1 otherwise 0
	print("late={took >= lateMs} spun={settled > 0} {busy > 0} slept={slept >= 900}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
late=false spun=true true slept=true
```
```exitcode
0
```
