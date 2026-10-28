---
feature: sched-park
status: stable
keywords: [scheduler, green-threads, park, ready, await, coroutine, strand, gopark, GMP]
category: system
---

# Every wait parks, and a completion readies the waiter

## Documentation

Go never runs one goroutine on top of another. A goroutine that waits — for a channel, a timer, a
network read, another goroutine — `gopark`s: it switches to its machine's scheduler context, which runs
whatever is runnable next, and whoever completes the wait `ready`s it back onto a run queue
(`vendor/go/src/runtime/proc.go`, `gopark` and `ready`). No goroutine's progress depends on the stack of
another.

A green thread that awaits parks the same way here, and so does one that sleeps, reads a pipe, waits for a
child or receives from its mailbox. The cases below are the shapes a scheduler that runs a waiter's work ON
the waiter's stack cannot finish: a chain of services that each await the next, a nested await whose answer
needs `main` to move, `main` woken behind a service that is still waiting, and a service's own coroutine
that must run while its handler sleeps.

⚖ **An `async` coroutine still belongs to the green thread that started it, and runs only where that green
thread's work runs** — one coroutine of a green thread at a time, never on a second machine at once. Its
green thread's WAIT does not pin it: a coroutine started before a `sleep` runs during the sleep.

## Tests
<!-- test: sched-park.nested-awaits-through-three-services-complete -->
<!-- procs: 1 -->
**THREE SERVICES, EACH AWAITING THE NEXT, AND `main` AWAITING TWO OF THEM.** `w.run` awaits `y.ask`, which
awaits `z.get`; `main` sends `y.ask` directly too. Every reply is answered, so the sum is 3 + 3.
```maxon
typealias Integer = int(i64.min to i64.max)

type Z
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function get() returns Integer
		return 3
	end 'get'
end 'Z'

type Y
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask(zh Z.handle) returns Integer
		return try await zh.get() otherwise 0
	end 'ask'
end 'Y'

type W
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function run(yh Y.handle, zh Z.handle) returns Integer
		return try await yh.ask(zh.clone()) otherwise 0
	end 'run'
end 'W'

function main() returns ExitCode
	let z = spawn Z.create()
	let y = spawn Y.create()
	let w = spawn W.create()
	let py = try y.ask(z.clone()) otherwise panic("an open mailbox with room takes the send")
	let pw = try w.run(y.clone(), zh: z.clone()) otherwise panic("an open mailbox with room takes the send")
	let a = await py
	let b = await pw
	return (a + b) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: sched-park.nested-awaits-through-three-services-complete-on-four-processors -->
<!-- procs: 4 -->
The same chain at four processors.
```maxon
typealias Integer = int(i64.min to i64.max)

type Z
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function get() returns Integer
		return 3
	end 'get'
end 'Z'

type Y
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask(zh Z.handle) returns Integer
		return try await zh.get() otherwise 0
	end 'ask'
end 'Y'

type W
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function run(yh Y.handle, zh Z.handle) returns Integer
		return try await yh.ask(zh.clone()) otherwise 0
	end 'run'
end 'W'

function main() returns ExitCode
	let z = spawn Z.create()
	let y = spawn Y.create()
	let w = spawn W.create()
	let py = try y.ask(z.clone()) otherwise panic("an open mailbox with room takes the send")
	let pw = try w.run(y.clone(), zh: z.clone()) otherwise panic("an open mailbox with room takes the send")
	let a = await py
	let b = await pw
	return (a + b) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: sched-park.a-nested-await-that-needs-main-completes -->
<!-- procs: 1 -->
**A SERVICE'S AWAIT THAT ONLY `main` CAN ANSWER.** `Middle.go` awaits `Waiter.waitForFile`, which polls for a
file `main` writes after its own `sleep(5)`. `main` must wake from that sleep while `Middle` is still waiting.
```maxon
typealias Integer = int(i64.min to i64.max)

type Waiter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function waitForFile() returns Integer
		while not File.exists(FilePath from "sched-park-flag-one.txt") 'poll'
			sleep(1)
		end 'poll'

		return 1
	end 'waitForFile'
end 'Waiter'

type Middle
	var w as Waiter.handle

	static function create() returns Self
		return Self{w: spawn Waiter.create()}
	end 'create'

	export function go() returns Integer
		let v = try await self.w.waitForFile() otherwise 0
		return v
	end 'go'
end 'Middle'

function main() returns ExitCode
	try File.delete(FilePath from "sched-park-flag-one.txt") otherwise ignore
	let m = spawn Middle.create()
	let r = try m.go() otherwise panic("an open mailbox with room takes the send")
	sleep(5)
	print("main woke\n")
	try File.writeText(FilePath from "sched-park-flag-one.txt", content: "x") otherwise ignore
	let v = await r
	print("v={v}\n")
	try File.delete(FilePath from "sched-park-flag-one.txt") otherwise ignore
	return 0
end 'main'
```
```stdout
main woke
v=1
```
```exitcode
0
```

<!-- test: sched-park.a-nested-await-that-needs-main-completes-on-four-processors -->
<!-- procs: 4 -->
The same shape at four processors.
```maxon
typealias Integer = int(i64.min to i64.max)

type Waiter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function waitForFile() returns Integer
		while not File.exists(FilePath from "sched-park-flag-four.txt") 'poll'
			sleep(1)
		end 'poll'

		return 1
	end 'waitForFile'
end 'Waiter'

type Middle
	var w as Waiter.handle

	static function create() returns Self
		return Self{w: spawn Waiter.create()}
	end 'create'

	export function go() returns Integer
		let v = try await self.w.waitForFile() otherwise 0
		return v
	end 'go'
end 'Middle'

function main() returns ExitCode
	try File.delete(FilePath from "sched-park-flag-four.txt") otherwise ignore
	let m = spawn Middle.create()
	let r = try m.go() otherwise panic("an open mailbox with room takes the send")
	sleep(5)
	print("main woke\n")
	try File.writeText(FilePath from "sched-park-flag-four.txt", content: "x") otherwise ignore
	let v = await r
	print("v={v}\n")
	try File.delete(FilePath from "sched-park-flag-four.txt") otherwise ignore
	return 0
end 'main'
```
```stdout
main woke
v=1
```
```exitcode
0
```

<!-- test: sched-park.main-wakes-on-time-behind-a-long-service-await -->
<!-- procs: 1 -->
**`main`'s TEN-MILLISECOND SLEEP ENDS WHILE A SERVICE IT MESSAGED IS STILL AWAITING A REPLY** that only `main`
can end. The service polls a flag `main` publishes after its sleep, so a wake that waited for the service would
wait for ever and the runner would report the case TIMED OUT; no elapsed time is compared to a threshold, so a
slow host cannot turn the order into a race. Nothing the service waits on lies under `main`'s wake.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReleaseCell = SharedValue with bool

type Slow
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	export function get() returns Integer
		while not self.released.current() 'untilReleased'
			sleep(1)
		end 'untilReleased'

		return 7
	end 'get'
end 'Slow'

type Middle
	var s as Slow.handle

	static function create(released ReleaseCell) returns Self
		return Self{s: spawn Slow.create(released.clone())}
	end 'create'

	export function go() returns Integer
		let v = try await self.s.get() otherwise 0
		return v
	end 'go'
end 'Middle'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let m = spawn Middle.create(released.clone())
	let r = try m.go() otherwise panic("an open mailbox with room takes the send")
	sleep(10)
	print("woke\n")
	released.publish(true)
	let v = await r
	print("v={v}\n")
	return 0
end 'main'
```
```stdout
woke
v=7
```
```exitcode
0
```

<!-- test: sched-park.a-service-coroutine-runs-while-its-handler-sleeps -->
<!-- procs: 1 -->
**A HANDLER'S COROUTINE OVERLAPS THE HANDLER'S OWN WAIT.** `work` starts `flip`, then sleeps until the
non-blocking peek reads that `flip` has finished, so the reply is 42 × 10 + 1. The sleep has no bound of its own:
a coroutine that could not run while its handler slept would leave the handler sleeping for ever, and the runner
would report the case TIMED OUT, so no sleep has to outlast the host.
```maxon
typealias Integer = int(i64.min to i64.max)

function flip(v Integer) returns Integer
	Scheduler.yield()
	return v + 1
end 'flip'

type Handler
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function work() returns Integer
		let p = async flip(41)
		var naps = 0 as Integer

		while not p.isComplete() 'untilFlipped'
			sleep(1)
			naps = naps + 1
		end 'untilFlipped'

		let doneDuringSleep = 1 if naps > 0 else 0
		let v = await p
		return v * 10 + doneDuringSleep
	end 'work'
end 'Handler'

function main() returns ExitCode
	let h = spawn Handler.create()
	let r = try await h.work() otherwise 0
	print("r={r}\n")
	return 0
end 'main'
```
```stdout
r=421
```
```exitcode
0
```

<!-- test: sched-park.a-renounced-sleeper-due-at-shutdown-does-not-abort-the-exit -->
<!-- procs: 4 -->
**A RENOUNCED COROUTINE THAT PARKS AFTER ITS DROP IS ABANDONED AT EXIT, NOT RUN AND NOT AN ABORT.** `child`
has started when `p.cancel()` renounces it, so it runs its body out — and parks on `sleep(0)` after `main` has
returned. No promise is outstanding, so the exit drain ends at once and the workers are stopped; the service
spawned first leaves a worker parked, which the teardown's signal wakes with `child`'s deadline already due. The
worker may fire that deadline onto its own processor, and the exit it takes next must still leave cleanly.
```maxon
typealias Integer = int(i64.min to i64.max)

function child() returns Integer
	Scheduler.yield()
	sleep(0)
	return 1
end 'child'

type S
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function get() returns Integer
		return 2
	end 'get'
end 'S'

function startAndStopAService()
	let s = spawn S.create()
	let t = s.clone()
	_ = t
end 'startAndStopAService'

function main() returns ExitCode
	startAndStopAService()
	var i = 0 as Integer

	while i < 2000 'settle'
		Scheduler.yield()
		i = i + 1
	end 'settle'

	let p = async child()
	Scheduler.yield()
	p.cancel()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: sched-park.a-timer-lap-that-hands-the-processor-to-a-pinned-thread-wakes-a-machine-for-the-rest -->
<!-- procs: 2 -->
<!-- preempt: off -->
**A LAP THAT FIRES TWO TIMERS AND HANDS ITS PROCESSOR TO A PINNED THREAD STILL PAYS THE WAKE THE SECOND TIMER OWES.**
Both timers are armed while every processor is busy, so no machine is started to poll for them, and both are due
by the time `main` — the only machine not waiting for a pinned thread — parks. Its loop fires them in deadline
order: the signaller's thread, then the waiter's. The waiter's token is the one the search finds, and it belongs
to another machine, so the lap hands the processor and the token to that machine; the signaller's token is left on
the ring of the processor it handed over, with a machine busy on it and the other processor idle. Go's timer fire
readies each thread through `goready`, which wakes a machine for it; here that wake is paid on the hand-off, and
a new machine takes the signaller's token and gives its processor to its owner. Without the wake the signaller
waits behind the waiter, which is waiting for the signaller: with preemption off nothing ever runs it.

⚖ **THE OBSERVABLE IS THE WAKE, NOT A RACE AGAINST PREEMPTION.** `preempt: off` withdraws the one rescue a
stranded signaller has, so a runtime that pays the wake delivers the signal on every schedule and one that does not
pays it only in a round the hand-off never reached. The waiter holds its machine and counts turns until the signal
arrives; the count `handOffTurnCap` ends the wait so a stranded signaller prints the failing round instead of
hanging. The cap is a number of turns, never a wall-clock budget, and it is far above the turns any
wake takes.

⚠ **SIX ROUNDS, BECAUSE ONE LAP MAY NOT BE THE ONE THAT HANDS OFF.** The monitor starting a machine for an overdue
timer can fire the two timers one lap apart, and a round spoiled that way passes without reaching the hand-off, on a
runtime that pays the wake and on one that does not. Each round is on fresh services, the case holds only when every
round's signaller ran, and the rounds end at the first one that did not.
```maxon
typealias HandOffFlag = SharedValue with bool
typealias HandOffTurns = int(0 to u64.max)

let handOffLeadMs = 50
let handOffRounds = 6
let handOffTurnCap = 1500000000

function handOffRemaining(target InstantMs) returns Milliseconds
	let now = Clock.nowMs()

	if now >= target 'past'
		return 0
	end 'past'

	return (target - now) as Milliseconds
end 'handOffRemaining'

function handOffSpinUntil(deadline InstantMs) returns HandOffTurns
	var turns = 0 as HandOffTurns

	while Clock.nowMs() < deadline 'spin'
		turns = turns + 1
	end 'spin'

	return turns
end 'handOffSpinUntil'

type HandOffThread
	var held as HandOffTurns

	static function create() returns Self
		return Self{held: 0}
	end 'create'

	export function waitForTheSignal(wakeAt InstantMs, signalled HandOffFlag, armed HandOffFlag) returns bool
		if not Scheduler.pinToMachine() 'refused'
			return false
		end 'refused'

		armed.publish(true)
		sleep(handOffRemaining(wakeAt))

		while not signalled.current() and self.held < handOffTurnCap 'wait'
			self.held = self.held + 1
		end 'wait'

		let arrived = signalled.current()
		Scheduler.unpinFromMachine()
		return arrived
	end 'waitForTheSignal'

	export function signalAt(wakeAt InstantMs, signalled HandOffFlag) returns bool
		if not Scheduler.pinToMachine() 'refused'
			return false
		end 'refused'

		sleep(handOffRemaining(wakeAt))
		signalled.publish(true)
		Scheduler.unpinFromMachine()
		return true
	end 'signalAt'
end 'HandOffThread'

function handOffRound() returns bool
	let signalled = HandOffFlag.create(false)
	let armed = HandOffFlag.create(false)
	let dueAt = Clock.nowMs() + handOffLeadMs
	let waiter = spawn HandOffThread.create()
	let signaller = spawn HandOffThread.create()
	let waiting = try waiter.waitForTheSignal(dueAt + 1, signalled: signalled.clone(), armed: armed.clone()) otherwise panic("an open mailbox with room takes the send")
	var looks = 0 as HandOffTurns

	while not armed.current() 'armWaiter'
		looks = looks + 1
	end 'armWaiter'

	_ = handOffSpinUntil(Clock.nowMs() + 2)
	let signalling = try signaller.signalAt(dueAt, signalled: signalled.clone()) otherwise panic("an open mailbox with room takes the send")
	_ = handOffSpinUntil(dueAt + 2)

	let arrived = await waiting
	let sent = await signalling
	return arrived and sent
end 'handOffRound'

function main() returns ExitCode
	var ran = 0 as HandOffTurns
	var allRan = true

	while allRan and ran < handOffRounds 'rounds'
		allRan = handOffRound()

		if allRan 'roundRan'
			ran = ran + 1
		end 'roundRan'
	end 'rounds'

	print("rounds in which the signaller ran while the waiter held its machine: {ran} of {handOffRounds}\n")
	return 0
end 'main'
```
```stdout
rounds in which the signaller ran while the waiter held its machine: 6 of 6
```
```exitcode
0
```
