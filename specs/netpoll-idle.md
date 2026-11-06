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
**A DEADLINE ARMED EARLIER THAN THE ONE THE POLLER WAITS FOR ENDS ITS WAIT.** A service parks a coroutine on a
timer without end (`ForeverMs`), so the machine that runs it settles into the poller with no deadline that could
ever wake it. `main` stays busy until a processor reports idle, which is that machine giving its processor up,
and then sleeps 50 ms. Only an interrupt of the poller's wait can fire that timer, so a missed one leaves `main`
asleep for ever and the runner reports the case TIMED OUT: no elapsed time is compared to a threshold, and no
deadline of the sleeper's can run out before the nap is armed.

⚠ **THE SLEEPER IS A COROUTINE THE SERVICE HOLDS, NOT A HANDLER THAT SLEEPS.** A handler asleep without end
cannot be ended from outside, `cancel()` on its reply included: a queued message always runs. The coroutine sits
in a field of the service, `arm` yields once so the strand runs it up to its `sleep`, and `release` pops it and
drops it at its end, so the sleep ends and the program exits. It reports whether the coroutine was still parked
when it was dropped.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Held = Promise with Integer
typealias HeldArray = Array with Held

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Sleeper
	var held as HeldArray

	static function create() returns Self
		return Self{held: HeldArray.create(0)}
	end 'create'

	export function arm() returns Integer
		self.held.push(async forever())
		Scheduler.yield()
		return 1
	end 'arm'

	export function release() returns bool
		let sleeper = try self.held.pop() otherwise panic("arm ran before this message")
		let stillParked = not sleeper.isComplete()
		sleeper.cancel()
		return stillParked
	end 'release'
end 'Sleeper'

function main() returns ExitCode
	let s = spawn Sleeper.create()
	let armed = try s.arm() otherwise panic("an open mailbox with room takes the send")
	let v = await armed
	var idle = 0 as SchedulerEventCount

	// Busy, so this machine does not go idle: the other machine settles into the poller on the sleeper's
	// deadline, and only an interrupt can end that wait at 50.
	while idle == 0 'untilOtherIdles'
		idle = Scheduler.counters().idleProcessors
	end 'untilOtherIdles'

	sleep(50)
	let stillParked = try await s.release() otherwise false
	s.shutdown()
	print("napped parked={stillParked} v={v}\n")
	return 0
end 'main'
```
```stdout
napped parked=true v=1
```
```exitcode
0
```

<!-- test: netpoll-idle.a-break-reaches-the-poller-while-a-former-poller-runs -->
<!-- procs: 2 -->
**A BREAK ENDS THE POLLER'S WAIT WHILE ANOTHER MACHINE THAT ONCE POLLED IS BUSY.** `main`'s machine waits in the
poller first, then stays busy while a service's 50 ms sleep breaks the other machine's wait on a deadline that
never comes (a held coroutine asleep for `ForeverMs`). `main` spins until that service has finished its nap and
published it, so a break that never reaches the poller leaves the nap unfired and `main` spinning for ever, and
the runner reports the case TIMED OUT. No elapsed time is compared to a threshold, and the long sleeper cannot
wake first.

⚠ **THE LONG SLEEPER IS A COROUTINE THE SERVICE HOLDS**, for the reason the previous case gives: a handler asleep
without end cannot be ended, and `release` drops the coroutine out of the field that holds it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Held = Promise with Integer
typealias HeldArray = Array with Held
typealias NapCell = SharedValue with bool

function forever() returns Integer
	sleep(ForeverMs)
	return 1
end 'forever'

type Holder
	var held as HeldArray

	static function create() returns Self
		return Self{held: HeldArray.create(0)}
	end 'create'

	export function arm() returns Integer
		self.held.push(async forever())
		Scheduler.yield()
		return 1
	end 'arm'

	export function release() returns bool
		let sleeper = try self.held.pop() otherwise panic("arm ran before this message")
		let stillParked = not sleeper.isComplete()
		sleeper.cancel()
		return stillParked
	end 'release'
end 'Holder'

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
	let longer = spawn Holder.create()
	let shorter = spawn Napper.create(napped.clone())
	let armed = try longer.arm() otherwise panic("an open mailbox with room takes the send")
	let v = await armed

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
	let stillParked = try await longer.release() otherwise false
	longer.shutdown()
	print("napped={seen} took={took} parked={stillParked} v={v}\n")
	return 0
end 'main'
```
```stdout
napped=true took=1 parked=true v=1
```
```exitcode
0
```

<!-- test: netpoll-idle.a-close-under-a-parked-accept-breaks-a-poller-it-leaves-nothing-to-wait-for -->
<!-- procs: 2 -->
**A CLOSE THAT READIES THE LAST WAITER BREAKS THE POLLER, NOT ONLY THE PARK THE CLOSE BEGINS WITH.** A coroutine of
`main` is parked in `accept` on a listener nothing connects to, so the machine that ran it settles into the poller
with no deadline. `main` waits until that has happened, starts a second coroutine and closes the listener. The close
first yields (`__gt_io_park` is a timer park with a deadline of now), and that park breaks the poller; the second
coroutine then holds the strand until the poller's wait has returned (`parkWakes` moves) and a spin has given it
time to block again, so the poller is blocked on the accept again when the close goes on to ready the accept and
leaves nothing pending. Only a break from that readiness can end the wait now. The poller machine counts as
running in `__sched_checkdead`, so without it the exit drain never ends and the runner reports the case TIMED OUT:
no elapsed time is compared to a threshold. The accepting coroutine's promise is tied to the listener `main`
closes, so it lives in a local and is awaited after the close.

⚠ **THE SPIN IS NOT AN ASSERTION.** It sets the order the case needs (the poller blocked again before the
readiness), and a host fast enough to finish it early reads green rather than red.
```maxon
typealias Integer = int(i64.min to i64.max)

let settleReads = 2000000

function holdStrandUntilPollerReturns(wakesBefore SchedulerEventCount) returns Integer
	var wakes = wakesBefore

	while wakes == wakesBefore 'untilPollerReturns'
		wakes = Scheduler.counters().parkWakes
	end 'untilPollerReturns'

	var reads = 0 as Integer
	var idle = 0 as SchedulerEventCount

	while reads < settleReads 'settle'
		idle = Scheduler.counters().idleProcessors
		reads = reads + 1
	end 'settle'

	return 1
end 'holdStrandUntilPollerReturns'

function stall(listener TcpListener) returns Integer
	_ = try listener.accept() otherwise return 2

	return 0
end 'stall'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise panic("a loopback bind on an ephemeral port succeeds")
	let before = Scheduler.counters().netpollBlocks
	let accepter = async stall(listener)

	while Scheduler.counters().netpollBlocks == before 'untilParked'
		Scheduler.yield()
	end 'untilParked'

	var idle = 0 as SchedulerEventCount

	while idle == 0 'untilPollerSettles'
		idle = Scheduler.counters().idleProcessors
	end 'untilPollerSettles'

	var reads = 0 as Integer

	while reads < settleReads 'settle'
		idle = Scheduler.counters().idleProcessors
		reads = reads + 1
	end 'settle'

	let holder = async holdStrandUntilPollerReturns(Scheduler.counters().parkWakes)
	listener.close()
	_ = await holder
	_ = await accepter

	print("released\n")
	return 0
end 'main'
```
```stdout
released
```
```exitcode
0
```
