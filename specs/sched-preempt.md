---
feature: sched-preempt
status: stable
keywords: [scheduler, green-threads, preemption, sysmon, stack-guard, fairness, GMP]
category: system
---

# A green thread that runs too long gives up its processor

## Documentation

Go does not let one goroutine keep a processor indefinitely. Its monitor thread notices a goroutine that
has run for 10 ms without passing through the scheduler and asks it to stop: it poisons the goroutine's
stack guard, so the next function prologue's overflow check fails, and `morestack` sees the poison and
yields instead of growing (`vendor/go/src/runtime/proc.go`, `retake` and `preemptone`;
`vendor/go/src/runtime/stack.go`, `newstack`). A CPU-bound green thread here is asked the same way, through
the prologue check its stack growth already uses. A spawn, an `async` call, a service send, an `await` or
`awaitAny`, and a service taking its next message check the request on entry as well, so two services that
keep readying each other give up the processor too (`specs/sched-runqueue.md`).

`Scheduler.counters().preempts` counts the preemption requests green threads have honoured.

A case that measures a bystander starts its CPU-bound work, then puts `main` to sleep for 20 ms before
sending the bystander its request. With every processor busy, `main`'s own timer can only fire once a
spinner gives a processor back, so the measurement cannot pass by a queue order that happens to run the
bystander first.

⛔⛔ **A SPINNER ENDS WHEN `main` RELEASES IT, AND `main` RELEASES IT ONLY AFTER THE BYSTANDER HAS ANSWERED.**
No clock and no amount of work bounds a spinner: its loop reads a `SharedValue` flag that `main` publishes once
the reply has reached it, so the bystander was answered while every spinner was still running by construction,
and a handover that never comes leaves the spinner holding its processor and the runner reports the case TIMED
OUT. A budget cannot be written down instead: a handover costs the retake threshold, a monitor lap and a thread
stop, and on a host whose own scheduler holds the process off a core those stretch without bound while nothing
about preemption has changed.

## Tests

<!-- test: sched-preempt.a-cpu-bound-service-yields-to-a-bystander -->
<!-- procs: 1 -->
**A SERVICE THAT NEVER WAITS DOES NOT HOLD THE ONLY PROCESSOR FOR EVER.** Every iteration of the spinner passes a
function prologue, so a preemption request reaches it within one sysmon period. The spinner stops only when
`main` publishes the release, which it does after the bystander's reply, so the reply is an event inside the
spinner's run and no wall-clock budget the host's own load can exhaust decides it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReleaseCell = SharedValue with bool

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Spinner
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	export function spin() returns Integer
		var acc = 0 as Integer
		while not self.released.current() 'spin'
			var i = 0 as Integer
			while i < 1000 'burst'
				acc = step(acc, depth: 4)
				i = i + 1
			end 'burst'
		end 'spin'
		return acc mod 2 + 1
	end 'spin'
end 'Spinner'

type Bystander
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Bystander'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let s = spawn Spinner.create(released.clone())
	let b = spawn Bystander.create()
	let spun = try s.spin() otherwise panic("an open mailbox with room takes the send")
	sleep(20)
	let p = try await b.ping() otherwise 0
	released.publish(true)
	let q = await spun
	print("ping={p} spun={q > 0} preempted={Scheduler.counters().preempts > 0}\n")
	return 0
end 'main'
```
```stdout
ping=1 spun=true preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.preemption-inside-deep-recursion-is-sound -->
<!-- procs: 1 -->
**A PREEMPTION CAN LAND ANYWHERE IN A 20,000-FRAME RECURSION**, including at a prologue whose stack must also
grow. Every pass recomputes a value that flows through every frame and compares it with the first pass, so
a frame a preemption corrupted shows up as `wrong` above zero. The recurser runs at least `churnRounds` passes
and then until `main` releases it, which `main` does only after the bystander's reply, so the reply is an event
inside the churn and no wall-clock budget the host's own load can exhaust decides it. The floor is PASSES and not
the clock, so a busy host does not quietly buy the corruption check fewer of them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReleaseCell = SharedValue with bool

let churnRounds = 3000

function down(n Integer, acc Integer) returns Integer
	if n == 0 'bottom'
		return acc
	end 'bottom'
	return down(n - 1, acc: (acc * 31 + n) mod 1000003)
end 'down'

type Recurser
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	export function churn(rounds Integer) returns Integer
		let expected = down(20000, acc: 1)
		var wrong = 0 as Integer
		var r = 0 as Integer
		while r < rounds or not self.released.current() 'churn'
			if down(20000, acc: 1) != expected 'differs'
				wrong = wrong + 1
			end 'differs'
			r = r + 1
		end 'churn'
		return wrong
	end 'churn'
end 'Recurser'

type Bystander
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Bystander'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let r = spawn Recurser.create(released.clone())
	let b = spawn Bystander.create()
	let churned = try r.churn(churnRounds) otherwise panic("an open mailbox with room takes the send")
	sleep(20)
	let p = try await b.ping() otherwise 0
	released.publish(true)
	let wrong = await churned
	print("ping={p} wrong={wrong}\n")
	return 0
end 'main'
```
```stdout
ping=1 wrong=0
```
```exitcode
0
```

<!-- test: sched-preempt.as-many-spinners-as-processors-cannot-starve-another -->
<!-- procs: 4 -->
**EVERY PROCESSOR BUSY WITH A SPINNER, AND `main` AND ONE MORE SERVICE STILL GET A TURN.** No processor is ever
idle, so only a preemption hands one over. Every spinner stops only when `main` publishes the release, which it
does after the bystander's reply, so the bystander was answered while every processor was still busy by
construction, and a handover that never comes is a TIMEOUT.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SpinnerHandleArray = Array with Spinner.handle
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias ReleaseCell = SharedValue with bool

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Spinner
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	export function spin() returns Integer
		var acc = 0 as Integer
		while not self.released.current() 'spin'
			var i = 0 as Integer
			while i < 1000 'burst'
				acc = step(acc, depth: 4)
				i = i + 1
			end 'burst'
		end 'spin'
		return acc mod 2 + 1
	end 'spin'
end 'Spinner'

type Bystander
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Bystander'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	var spinners = SpinnerHandleArray.create()
	var i = 0 as Count
	while i < Scheduler.processorCount() 'spawnEach'
		spinners.push(spawn Spinner.create(released.clone()))
		i = i + 1
	end 'spawnEach'
	let b = spawn Bystander.create()
	var spun = ReplyPromiseArray.create()
	var k = 0 as Count
	while k < spinners.count() 'sendEach'
		view s = try spinners.get(k) otherwise panic("spinners.get out of range at {k}: bounded by the pushes above")
		spun.push(try s.spin() otherwise panic("an open mailbox with room takes the send"))
		k = k + 1
	end 'sendEach'
	sleep(20)
	let p = try await b.ping() otherwise 0
	released.publish(true)
	var ran = 0 as Count
	while spun.count() > 0 'collect'
		let r = try spun.pop() otherwise panic("spun.pop on a non-empty array")
		let q = await r
		if q > 0 'spun'
			ran = ran + 1
		end 'spun'
	end 'collect'
	print("spinners={ran == spinners.count()} ping={p}\n")
	return 0
end 'main'
```
```stdout
spinners=true ping=1
```
```exitcode
0
```

<!-- test: sched-preempt.a-preempted-coroutine-resumes-before-its-strand-does-anything-else -->
<!-- procs: 1 -->
**A PREEMPTION TAKES THE PROCESSOR, NEVER THE TURN.** A coroutine gives up its green thread only where it
waits (`async` is a coroutine, not a thread), so its owner and its siblings may observe what it wrote only
at those points. `busy` writes `phase = 1`, computes without waiting, then writes `phase = 2`. `main` and
`nap` both become runnable 30 ms in, and `busy` keeps computing until at least 150 ms have passed AND a
preemption has been honoured after both deadlines were due; neither may run until `busy` has finished, so
both read 2. The preemption is waited for rather than assumed on a clock: the monitor asks on WALL time,
so a host that gives the monitor thread no core for most of an interval delays the first request by an
unbounded amount while nothing about preemption has changed (MEASURED: 24 CPU-bound processes on 12
cores put the first request 330 to 965 ms after the spin began, and a fixed 150 ms spin saw no preemption
at all in 57 of 60 runs). The wait has no bound, so a preemption that never comes leaves `busy` spinning and the
runner reports the case TIMED OUT. One ATTEMPT owns its disturbance: a host that holds the
process off a core for most of those 30 ms leaves `busy` still at its own `sleep(1)` when `main`'s
deadline comes due, `__gt_timer_check` fires what is due in `(deadline, seq)` order, and
`__gt_ready_locked` puts a woken OWNER at the FRONT of its strand queue and a woken coroutine at the
back — so `main` runs ahead of a `busy` that has not started and reads 0, having exercised nothing.
`seenByOwner == 0 and seenBySibling == 2` is that trace and nothing else, so an attempt matching it is
discarded and retried; a 1 in either reading is the regression this case exists for, reaches the print
and reddens the pinned line, so the retry is not a tolerance. `seenBySibling` is never the 0: `nap` is
enqueued behind `busy` at its `async`, holds the later of the two deadlines so pop-min wakes `busy`
first, and `busy` never waits once started. `attemptCap` is a time budget, every attempt paying the
full spin, and exhausting it prints its own non-matching line and exits 3. `preempted` is set by `busy`
itself from `Scheduler.counters().preempts` read against a baseline taken once the deadlines are due: the
counter is process-global and monotonic, so a baseline taken any earlier would count a preemption that
came before `main` and `nap` were runnable, which exercises nothing.
```maxon
typealias Integer = int(i64.min to i64.max)

let attemptCap = 8
let spinFloorMs = 150
let deadlinesDueMs = 35

var phase = 0 as Integer
var honoured = false

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

function busy() returns Integer
	sleep(1)
	phase = 1
	let start = Clock.nowMs()
	var acc = 0 as Integer
	var elapsed = 0 as Integer
	var baseline = Scheduler.counters().preempts
	var baselined = false
	while elapsed < spinFloorMs or not honoured 'spin'
		var i = 0 as Integer
		while i < 1000 'burst'
			acc = step(acc, depth: 4)
			i = i + 1
		end 'burst'
		elapsed = Clock.elapsedMs(start) as Integer
		if elapsed >= deadlinesDueMs 'deadlinesDue'
			let now = Scheduler.counters().preempts
			if not baselined 'firstLook'
				baseline = now
				baselined = true
			end 'firstLook' else if now > baseline 'honouredAfterDue'
				honoured = true
			end 'honouredAfterDue'
		end 'deadlinesDue'
	end 'spin'
	phase = 2
	return acc mod 2 + 1
end 'busy'

function nap() returns Integer
	sleep(30)
	return phase
end 'nap'

function main() returns ExitCode
	var tries = 0 as Integer
	while tries < attemptCap 'attempts'
		tries = tries + 1
		phase = 0
		honoured = false
		let a = async busy()
		let b = async nap()
		sleep(30)
		let seenByOwner = phase
		let q = await a
		let seenBySibling = await b
		if seenByOwner == 0 and seenBySibling == 2 'neverStarted'
			continue
		end 'neverStarted'

		print("owner={seenByOwner} sibling={seenBySibling} busy={q > 0} preempted={honoured}\n")
		return 0
	end 'attempts'

	print("busy had not started in {attemptCap} attempts\n")
	return 3
end 'main'
```
```stdout
owner=2 sibling=2 busy=true preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.every-argument-register-survives-a-preemption -->
<!-- procs: 1 -->
**A PREEMPTION STOPS A FUNCTION AT ITS PROLOGUE, WHERE EVERY ARGUMENT IS STILL IN ITS REGISTER.** `mix` takes
eight integer and eight float arguments — more than either ISA passes in registers — and rotates them
through seven levels of recursion, so a register a preemption failed to preserve changes the checksum. Each
pass is compared with one computed before the churn began. Two mixers share the one processor with
different values, so whenever one is preempted the other overwrites every argument register. Each mixer churns for
at least `ms` and until the second preemption has been honoured, so the stop the case is about is certain and the
`preempted` reading is not a race against how soon a loaded host's monitor asks.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
typealias Checksums = Array with Integer

let variants = 16

function bit(x Real, y Real, weight Integer) returns Integer
	if x > y 'greater'
		return weight
	end 'greater'
	return 0
end 'bit'

function mix(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, p Real, q Real, r Real, s Real, t Real, u Real, v Real, w Real, depth Integer) returns Integer
	if depth == 0 'leaf'
		let ints = a + b * 3 + c * 5 + d * 7 + e * 11 + f * 13 + g * 17 + h * 19
		let floats = bit(p, y: q, weight: 1) + bit(q, y: r, weight: 2) + bit(r, y: s, weight: 4) + bit(s, y: t, weight: 8) + bit(t, y: u, weight: 16) + bit(u, y: v, weight: 32) + bit(v, y: w, weight: 64) + bit(w, y: p, weight: 128)
		return ints * 256 + floats
	end 'leaf'
	return mix(h, b: a, c: b, d: c, e: d, f: e, g: f, h: g, p: w, q: p, r: q, s: r, t: s, u: t, v: u, w: v, depth: depth - 1)
end 'mix'

type Mixer
	var shift as Integer
	var scale as Real

	static function create(shift Integer, scale Real) returns Self
		return Self{shift: shift, scale: scale}
	end 'create'

	export function churn(ms Integer) returns Integer
		var expected = Checksums.create()
		var k = 0 as Integer
		while k < variants 'reference'
			expected.push(mix(k, b: 2 + self.shift, c: 3, d: 4 + self.shift, e: 5, f: 6 + self.shift, g: 7, h: 8 + self.shift, p: 1.5 * self.scale, q: 2.25, r: 3.125 * self.scale, s: 4.0625, t: 5.5 * self.scale, u: 6.75, v: 7.875 * self.scale, w: 8.5, depth: 7))
			k = k + 1
		end 'reference'
		let start = Clock.nowMs()
		var wrong = 0 as Integer
		var i = 0 as Integer
		while (Clock.elapsedMs(start) as Integer) < ms or Scheduler.counters().preempts <= 1 'churn'
			let k = i mod variants
			let want = try expected.get(k as ElementIndex) otherwise panic("expected.get out of range at {k}: bounded by variants")
			if mix(k, b: 2 + self.shift, c: 3, d: 4 + self.shift, e: 5, f: 6 + self.shift, g: 7, h: 8 + self.shift, p: 1.5 * self.scale, q: 2.25, r: 3.125 * self.scale, s: 4.0625, t: 5.5 * self.scale, u: 6.75, v: 7.875 * self.scale, w: 8.5, depth: 7) != want 'differs'
				wrong = wrong + 1
			end 'differs'
			i = i + 1
		end 'churn'
		return wrong
	end 'churn'
end 'Mixer'

function main() returns ExitCode
	let m1 = spawn Mixer.create(0, scale: 1.0)
	let m2 = spawn Mixer.create(1000, scale: 9.75)
	let churned1 = try m1.churn(200) otherwise panic("an open mailbox with room takes the send")
	let churned2 = try m2.churn(200) otherwise panic("an open mailbox with room takes the send")
	sleep(20)
	let wrong1 = await churned1
	let wrong2 = await churned2
	print("wrong={wrong1 + wrong2} preempted={Scheduler.counters().preempts > 1}\n")
	return 0
end 'main'
```
```stdout
wrong=0 preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.a-call-free-loop-is-preempted-anyway -->
<!-- procs: 1 -->
**A LOOP THAT CALLS NOTHING STILL GIVES UP ITS PROCESSOR.** A request written into the stack guard is read
only by a function that reserves a frame, so this spinner never reads one; the thread has to be stopped where
it is instead. `main` sleeps before sending the bystander its request, so the bystander cannot answer early by
some queue order, and the spinner stops only when `main` publishes the release after the bystander's reply, so
the reply is an event inside the spin rather than a race against a wall-clock budget. The one call the spinner
makes is the release read, once per `chunkSteps` steps: it is `__shv_current`, a runtime function that reserves
no frame a stack guard is read in, so it is no prologue a request could be taken at.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReleaseCell = SharedValue with bool

let chunkSteps = 1000000

type Spinner
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	// ⛔ NO CALL BUT THE RELEASE READ IN THE LOOP, which is the whole point: a poisoned stack guard is read by
	// a function that reserves a frame, and this body never enters one.
	export function spin() returns Integer
		var acc = 0 as Integer
		var i = 0 as Integer
		while not self.released.current() 'spin'
			var k = 0 as Integer
			while k < chunkSteps 'chunk'
				acc = (acc * 31 + i) mod 1000003
				i = i + 1
				k = k + 1
			end 'chunk'
		end 'spin'
		return acc mod 2 + 1
	end 'spin'
end 'Spinner'

type Bystander
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Bystander'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	let s = spawn Spinner.create(released.clone())
	let b = spawn Bystander.create()
	let spun = try s.spin() otherwise panic("an open mailbox with room takes the send")
	sleep(20)
	let p = try await b.ping() otherwise 0
	released.publish(true)
	let q = await spun
	print("ping={p} spun={q > 0} preempted={Scheduler.counters().preempts > 0}\n")
	return 0
end 'main'
```
```stdout
ping=1 spun=true preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.every-processor-in-a-call-free-loop-still-yields -->
<!-- procs: 4 -->
**ONE CALL-FREE SPINNER PER PROCESSOR, AND A FIFTH SERVICE STILL GETS A TURN.** No processor is ever idle and
no spinner ever reaches a prologue, so only stopping a thread where it runs hands one over. Every spinner stops
only when `main` publishes the release, which it does after the bystander's reply, so the bystander was answered
while every processor was still busy by construction, and a handover that never comes is a TIMEOUT. The one call
a spinner makes is the release read, as in the previous case.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SpinnerHandleArray = Array with Spinner.handle
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise
typealias ReleaseCell = SharedValue with bool

let chunkSteps = 1000000

type Spinner
	let released as ReleaseCell

	static function create(released ReleaseCell) returns Self
		return Self{released: released}
	end 'create'

	// ⛔ NO CALL BUT THE RELEASE READ IN THE LOOP, which is the whole point: a poisoned stack guard is read by
	// a function that reserves a frame, and this body never enters one.
	export function spin() returns Integer
		var acc = 0 as Integer
		var i = 0 as Integer
		while not self.released.current() 'spin'
			var k = 0 as Integer
			while k < chunkSteps 'chunk'
				acc = (acc * 31 + i) mod 1000003
				i = i + 1
				k = k + 1
			end 'chunk'
		end 'spin'
		return acc mod 2 + 1
	end 'spin'
end 'Spinner'

type Bystander
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Bystander'

function main() returns ExitCode
	let released = ReleaseCell.create(false)
	var spinners = SpinnerHandleArray.create()
	var i = 0 as Count
	while i < Scheduler.processorCount() 'spawnEach'
		spinners.push(spawn Spinner.create(released.clone()))
		i = i + 1
	end 'spawnEach'
	let b = spawn Bystander.create()
	var spun = ReplyPromiseArray.create()
	var k = 0 as Count
	while k < spinners.count() 'sendEach'
		view s = try spinners.get(k) otherwise panic("spinners.get out of range at {k}: bounded by the pushes above")
		spun.push(try s.spin() otherwise panic("an open mailbox with room takes the send"))
		k = k + 1
	end 'sendEach'
	sleep(20)
	let p = try await b.ping() otherwise 0
	released.publish(true)
	var ran = 0 as Count
	while spun.count() > 0 'collect'
		let r = try spun.pop() otherwise panic("spun.pop on a non-empty array")
		let q = await r
		if q > 0 'spun'
			ran = ran + 1
		end 'spun'
	end 'collect'
	print("spinners={ran == spinners.count()} ping={p}\n")
	return 0
end 'main'
```
```stdout
spinners=true ping=1
```
```exitcode
0
```

<!-- test: sched-preempt.every-register-survives-a-stop-anywhere -->
<!-- procs: 1 -->
**A THREAD STOPPED MID-LOOP RESUMES WITH EVERY REGISTER IT HAD.** Eight integers and eight floats are live
across a loop with no call in it, so a stop lands among them rather than at a boundary where fewer are live.
Every round recomputes the same checksum and is compared with the first. The churn runs at least `rounds` rounds
and until a preemption has been honoured, so a stop is certain and the `preempted` reading is not a race against
how soon a loaded host's monitor asks.
```maxon
typealias Integer = int(i64.min to i64.max)

let rounds = 40
let stepsPerRound = 2000000

type Churner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	// Sixteen values live across a loop with no call in it: a preemption that restores one of them wrongly
	// changes the round's checksum, and every round after the first is compared with the first.
	export function churn() returns Integer
		var wrong = 0 as Integer
		var expected = 0 as Integer
		var r = 0 as Integer
		while r < rounds or Scheduler.counters().preempts == 0 'rounds'
			var a = 1 as Integer
			var b = 2 as Integer
			var c = 3 as Integer
			var d = 4 as Integer
			var e = 5 as Integer
			var f = 6 as Integer
			var g = 7 as Integer
			var h = 8 as Integer
			var p = 1.5 as FullRangeFloat
			var q = 2.25 as FullRangeFloat
			var u = 3.125 as FullRangeFloat
			var v = 4.0625 as FullRangeFloat
			var w = 5.5 as FullRangeFloat
			var x = 6.75 as FullRangeFloat
			var y = 7.875 as FullRangeFloat
			var z = 8.5 as FullRangeFloat
			var i = 0 as Integer
			while i < stepsPerRound 'spin'
				a = (a * 31 + i) mod 1000003
				b = (b * 17 + a) mod 1000003
				c = (c * 13 + b) mod 1000003
				d = (d * 11 + c) mod 1000003
				e = (e * 7 + d) mod 1000003
				f = (f * 5 + e) mod 1000003
				g = (g * 3 + f) mod 1000003
				h = (h * 2 + g) mod 1000003
				p = p * 0.5 + q
				q = q * 0.5 + u
				u = u * 0.5 + v
				v = v * 0.5 + w
				w = w * 0.5 + x
				x = x * 0.5 + y
				y = y * 0.5 + z
				z = z * 0.5 + p
				i = i + 1
			end 'spin'
			var mix = a + b * 3 + c * 5 + d * 7 + e * 11 + f * 13 + g * 17 + h * 19
			if p > q 'pq'
				mix = mix + 1
			end 'pq'
			if q > u 'qu'
				mix = mix + 2
			end 'qu'
			if u > v 'uv'
				mix = mix + 4
			end 'uv'
			if v > w 'vw'
				mix = mix + 8
			end 'vw'
			if w > x 'wx'
				mix = mix + 16
			end 'wx'
			if x > y 'xy'
				mix = mix + 32
			end 'xy'
			if y > z 'yz'
				mix = mix + 64
			end 'yz'
			if z > p 'zp'
				mix = mix + 128
			end 'zp'
			if r == 0 'reference'
				expected = mix
			end 'reference' else 'compare'
				if mix != expected 'differs'
					wrong = wrong + 1
				end 'differs'
			end 'compare'
			r = r + 1
		end 'rounds'
		return wrong
	end 'churn'
end 'Churner'

function main() returns ExitCode
	let c = spawn Churner.create()
	let churned = try c.churn() otherwise panic("an open mailbox with room takes the send")
	sleep(20)
	let wrong = await churned
	print("wrong={wrong} preempted={Scheduler.counters().preempts > 0}\n")
	return 0
end 'main'
```
```stdout
wrong=0 preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.the-switch-keeps-the-processor-with-main -->
<!-- procs: 1 -->
<!-- preempt: off -->
**WITH `MAXON_PREEMPT=off` THE MONITOR TAKES NO PROCESSOR FROM THE THREAD HOLDING IT.** Neither the 10 ms
yield request nor the syscall retake is issued, so `main` keeps the only processor across 30 ms of work and
the service it spawned first runs only when `main` parks. The hold is a LOWER bound of wall time — a loop
until 30 ms have elapsed, three monitor thresholds — and never a budget, so host load can only lengthen it.
The witness rides the reply: `ping()` answers `Scheduler.counters().preempts` as the service saw it, so a
service that ran while `main` was off its processor answers the count that put it there, and `main`'s own
reading after the hold pins that nothing was asked and nothing retaken. `held` is always `true`: it consumes
the hold's accumulator so the loop's work is not dead.
```maxon
typealias Integer = int(i64.min to i64.max)

let holdMs = 30

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Echo
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return Scheduler.counters().preempts as Integer
	end 'ping'
end 'Echo'

function main() returns ExitCode
	let e = spawn Echo.create()
	let reply = try e.ping() otherwise panic("an open mailbox with room takes the send")
	let start = Clock.nowMs()
	var acc = 0 as Integer
	while (Clock.elapsedMs(start) as Integer) < holdMs 'hold'
		acc = step(acc, depth: 4)
	end 'hold'
	let p = await reply
	print("ping={p} held={acc mod 2 + 1 > 0} preempted={Scheduler.counters().preempts} retaken={Scheduler.counters().retakes}\n")
	return 0
end 'main'
```
```stdout
ping=0 held=true preempted=0 retaken=0
```
```exitcode
0
```

<!-- test: sched-preempt.a-main-preempted-between-two-spawns-runs-the-earlier-one-first -->
<!-- procs: 1 -->
**A `main` PREEMPTED BETWEEN TWO `spawn`s SEES THE EARLIER SERVICE ANSWER FIRST.** This is
`sched-runqueue.the-last-spawned-thread-runs-first-at-one-processor` with the preemption made deterministic:
that case pins `first=2` for this same program under `preempt: off`, and on a loaded host holding the process
off a core for the monitor's 10 ms, `main` is asked to yield between its second and third `spawn`. The case
runs WITH preemption on, so it cannot choose where the monitor's request lands: a slow runner spends the
10 ms on process start and the first `spawn`, and a request that lands before the window gives `first=0`,
while one that lands after the hold gives
`first=2`. So one ATTEMPT reads `Scheduler.counters().preempts` around its window and is discarded — its handles
dropped, its services shut down unasked — whenever the count moved before the hold or moved twice by the
end; `main` retries up to `attemptCap` times and prints only from an attempt whose single preemption fell
inside the window, so the line is produced only by the trace it describes. A preempted thread goes to the
GLOBAL queue's tail and the machine takes `runnext` next, so `b` runs, sends nothing, and parks on its
empty mailbox; `a` runs from the ring and parks; `main` resumes from the global queue and spawns `c` into
`runnext`; `a.rank()` readies `a` into `runnext`, displacing `c` to the ring, and `b.rank()` readies `b`,
displacing `a`; `awaitAny()` parks `main`, `b` replies first, and the index is 1. Exactly ONE preemption per
attempt: one request, one park — `main`'s schedule tick moves when it resumes, so the next monitor lap
re-notes it rather than asking again. `burstCap` is a hang guard, not a budget: a monitor that never asks
is not a disturbed attempt but its own printed line and exit 3, as is running out of attempts — each a
distinct failure rather than a timeout.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise

let burstCap = 100000
let attemptCap = 20

enum Attempt implements Error
	preemptedBeforeTheWindow
	preemptedAfterTheWindow
	neverAsked
end 'Attempt'

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Marker
	var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'

	export function rank() returns Integer
		return self.id
	end 'rank'
end 'Marker'

// The monitor chooses when it asks, so an attempt is accepted only when its one preemption fell between
// the second and third spawn; a throw drops the handles it spawned.
function attempt() returns (Integer, Integer) throws Attempt
	let c0 = Scheduler.counters().preempts
	let a = spawn Marker.create(1)
	let b = spawn Marker.create(2)
	if Scheduler.counters().preempts != c0 'early'
		throw Attempt.preemptedBeforeTheWindow
	end 'early'

	var acc = 0 as Integer
	var bursts = 0 as Integer
	while Scheduler.counters().preempts == c0 and bursts < burstCap 'hold'
		var i = 0 as Integer
		while i < 1000 'burst'
			acc = step(acc, depth: 4)
			i = i + 1
		end 'burst'
		bursts = bursts + 1
	end 'hold'
	if Scheduler.counters().preempts == c0 'silent'
		throw Attempt.neverAsked
	end 'silent'

	let c = spawn Marker.create(3)
	var replies = ReplyPromiseArray.create()
	replies.push(try a.rank() otherwise panic("an open mailbox with room takes the send"))
	replies.push(try b.rank() otherwise panic("an open mailbox with room takes the send"))
	replies.push(try c.rank() otherwise panic("an open mailbox with room takes the send"))
	let first = replies.awaitAny()
	var sum = 0 as Integer
	for p in replies 'drainthemall'
		sum = sum + (await p)
	end 'drainthemall'
	if Scheduler.counters().preempts != c0 + 1 'late'
		throw Attempt.preemptedAfterTheWindow
	end 'late'

	return (first as Integer, sum)
end 'attempt'

function monitorNeverAsked() returns ExitCode
	print("the monitor never asked in {burstCap} bursts\n")
	return 3
end 'monitorNeverAsked'

function main() returns ExitCode
	var tries = 0 as Integer
	while tries < attemptCap 'attempts'
		tries = tries + 1
		let outcome = try attempt() otherwise (e) 'disturbed'
			match e 'why'
				neverAsked then return monitorNeverAsked()
				preemptedBeforeTheWindow or
					preemptedAfterTheWindow then continue
			end 'why'
		end 'disturbed'
		print("first={outcome.0} sum={outcome.1}\n")
		return 0
	end 'attempts'
	print("no undisturbed attempt in {attemptCap}\n")
	return 3
end 'main'
```
```stdout
first=1 sum=6
```
```exitcode
0
```

<!-- test: sched-preempt.a-loop-that-only-spawns-is-preempted-at-a-spawn -->
<!-- procs: 1 -->
**A `main` THAT ONLY SPAWNS AND SENDS IS ASKED TO YIELD AT A `spawn`.** A spawn is a preemption point, as
Go's `newproc` is: it is not `nosplit`, so a pending request lands at the next `go` statement. The loop below
spends its time in the spawn and the send, so without that point no spawned service runs at one processor
until `main` parks in its first `await`. 100,000 spawns take far longer than the monitor's 10 ms on any host,
so the counters are read once, right after the loop and before the first `await`: `preempted` is a request
`main` honoured inside the loop, and `ran` is a record given back — a service whose handle `main` dropped
answers its one message and exits. Each is a single boolean, never a count, so a fast or a slow host prints
the same line; `sum` checks every reply was produced.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise

let spawnCount = 100000

type Doubler
	var index as Integer

	static function create(index Integer) returns Self
		return Self{index: index}
	end 'create'

	export function doubled() returns Integer
		return self.index * 2
	end 'doubled'
end 'Doubler'

function main() returns ExitCode
	var replies = ReplyPromiseArray.create()
	replies.reserve(spawnCount)
	for index in 0 upto spawnCount 'spawnDoublers'
		let doubler = spawn Doubler.create(index as Integer)
		replies.push(try doubler.doubled() otherwise panic("a fresh doubler takes its only message"))
	end 'spawnDoublers'

	let afterSpawns = Scheduler.counters()
	var sum = 0 as Integer
	for reply in replies 'awaitDoublers'
		sum = sum + (await reply)
	end 'awaitDoublers'

	print("preempted={afterSpawns.preempts > 0} ran={afterSpawns.recycledThreads > 0} sum={sum}\n")
	return 0
end 'main'
```
```stdout
preempted=true ran=true sum=9999900000
```
```exitcode
0
```

<!-- test: sched-preempt.a-switch-value-that-is-neither-off-nor-on-is-refused-at-start -->
<!-- unsupported-targets: wasm32-wasi -->
**A VALUE THAT IS NEITHER `off` NOR `on` ABORTS THE PROGRAM AT START WITH `RuntimeAbort.schedulerPreemptSwitchUnreadable`, EXIT 116.**
`MAXON_MAX_PROCS` has a numeric floor to fall under and falls through to the machine's count; this switch
has none, and a value silently read as its default would run a program pinned to preemption-off under
preemption-on, so the scheduler refuses it before `main`. The marker cannot express it — the harness admits
`off` and nothing else — so the program re-executes itself with `MAXON_PREEMPT=maybe` in the child's
environment and prints the child's exit code. The child spawns one service, so it carries the scheduler
whose monitor start reads the switch on the main thread; it prints nothing, because the abort lands before
its `main` runs. Not on `wasm32-wasi`, where the subprocess band is refused at compile
time (`subprocess-unsupported.md`).
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ping() returns Integer
		return 1
	end 'ping'
end 'Echo'

function child() returns ExitCode
	let e = spawn Echo.create()
	let p = try await e.ping() otherwise 0
	if p != 1 'unanswered'
		return 3
	end 'unanswered'

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
	config.environment = Environment.inheritUpdating(["MAXON_PREEMPT": "maybe"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```stdout
child exit=116
```
```exitcode
0
```
