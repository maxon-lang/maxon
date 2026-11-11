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

`Scheduler.counters().parkWakes` counts how many times a parked machine's wait has returned, for any
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
	let before = Scheduler.counters().parkWakes
	sleep(1000)
	let wakes = Scheduler.counters().parkWakes - before
	print("quiet={wakes <= quietWakes}\n")
	return 0
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
	let before = Scheduler.counters().parkWakes
	sleep(1000)
	let wakes = Scheduler.counters().parkWakes - before
	print("quiet={wakes <= quietWakes}\n")
	return 0
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
**A DEADLINE ARMED EARLIER THAN THE ONE THE POLLER WAITS FOR ENDS ITS WAIT.** `main` parks a coroutine on a
timer without end (`ForeverMs`), so the machine that runs it settles into the poller with no deadline that could
ever wake it. `main` stays busy until a processor reports idle, which is that machine giving its processor up,
and then sleeps 50 ms. Only an interrupt of the poller's wait can fire that timer, so a missed one leaves `main`
asleep for ever and the runner reports the case TIMED OUT: no elapsed time is compared to a threshold, and no
deadline of the sleeper's can run out before the nap is armed.

⚠ **THE SLEEPER IS A COROUTINE `main` HOLDS, NOT A HANDLER THAT SLEEPS.** A handler asleep without end cannot be
ended from outside, `cancel()` on its reply included: a queued message always runs, and a service's state may not
hold a promise. `main` yields once so the coroutine runs up to its `sleep`, and cancels it at the end, so the sleep
ends and the program exits. It reports whether the coroutine was still parked when it was cancelled.
```maxon
typealias Integer = int(i64.min to i64.max)

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

function main() returns ExitCode
	let sleeper = async forever()
	Scheduler.yield()
	var idle = 0 as SchedulerEventCount

	// Busy, so this machine does not go idle: the other machine settles into the poller on the sleeper's
	// deadline, and only an interrupt can end that wait at 50.
	while idle == 0 'untilOtherIdles'
		idle = Scheduler.counters().idleProcessors
	end 'untilOtherIdles'

	sleep(50)
	let stillParked = not sleeper.isComplete()
	sleeper.cancel()
	print("napped parked={stillParked}\n")
	return 0
end 'main'
```
```stdout
napped parked=true
```
```exitcode
0
```

<!-- test: netpoll-idle.a-break-reaches-the-poller-while-a-former-poller-runs -->
<!-- procs: 2 -->
**A BREAK ENDS THE POLLER'S WAIT WHILE ANOTHER MACHINE THAT ONCE POLLED IS BUSY.** `main`'s machine waits in the
poller first, then stays busy while a service's 50 ms sleep breaks the other machine's wait on a deadline that
never comes (a coroutine `main` holds, asleep for `ForeverMs`). `main` spins until that service has finished its nap and
published it, so a break that never reaches the poller leaves the nap unfired and `main` spinning for ever, and
the runner reports the case TIMED OUT. No elapsed time is compared to a threshold, and the long sleeper cannot
wake first.

⚠ **THE LONG SLEEPER IS A COROUTINE `main` HOLDS**, for the reason the previous case gives: a handler asleep
without end cannot be ended, and a service's state may not hold a promise. `main` cancels it at the end.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NapCell = SharedValue with bool

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Napper
	let napped as NapCell

	static function create(napped NapCell) returns Self
		return Self{napped: napped}
	end 'create'

	export function nap() returns Integer
		sleep(50)
		self.napped.publish(true)
		return 1
	end 'nap'
end 'Napper'

function main() returns ExitCode
	let napped = NapCell.create(false)
	let shorter = spawn Napper.create(napped.clone())
	let longer = async forever()
	Scheduler.yield()

	// `main`'s machine is the poller for this one, and then the other machine polls on the sleeper's deadline.
	sleep(20)

	var idle = 0 as SchedulerEventCount

	while idle == 0 'untilOtherIdles'
		idle = Scheduler.counters().idleProcessors
	end 'untilOtherIdles'

	// Armed by a third machine while `main`'s keeps running: only the break can end the poller's wait.
	let r2 = try shorter.nap() otherwise panic("an open mailbox with room takes the send")
	var seen = false

	while not seen 'untilNapped'
		seen = napped.current()
	end 'untilNapped'

	let took = await r2
	let stillParked = not longer.isComplete()
	longer.cancel()
	print("napped={seen} took={took} parked={stillParked}\n")
	return 0
end 'main'
```
```stdout
napped=true took=1 parked=true
```
```exitcode
0
```

<!-- test: netpoll-idle.a-close-under-a-parked-accept-from-another-owner-is-refused -->
**A LISTENER HAS ONE OWNER, SO NO OTHER COROUTINE CAN CLOSE IT UNDER A PARKED ACCEPT.** `async stall(listener)`
lends the listener to the coroutine that accepts until the promise is awaited or cancelled, so `main` closing it in
between (which is what ended a parked accept from outside) is a use of a lent value (E3102). The accepting coroutine
closes its own listener when it returns, and it cannot do that while it is parked in `accept`.
```maxon
typealias Integer = int(i64.min to i64.max)

function stall(listener TcpListener) returns Integer
	_ = try listener.accept() otherwise return 2

	return 0
end 'stall'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("a loopback bind on an ephemeral port succeeds")
	let accepter = async stall(listener)

	Scheduler.yield()
	listener.close()
	_ = await accepter

	print("released\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:2: use of moved value 'listener': it is lent to the coroutine of the `async` call at 12:23 until that promise is awaited or cancelled
```

