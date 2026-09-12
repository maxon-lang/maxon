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
the prologue check its stack growth already uses.

`__Builtins.schedPreemptCount()` counts the preemption requests green threads have honoured.

A case that measures a bystander starts its CPU-bound work, then puts `main` to sleep for 20 ms before
sending the bystander its request. With every processor busy, `main`'s own timer can only fire once a
spinner gives a processor back, so the measurement cannot pass by a queue order that happens to run the
bystander first.

## Tests

<!-- test: sched-preempt.a-cpu-bound-service-yields-to-a-bystander -->
<!-- procs: 1 -->
**A SERVICE THAT COMPUTES FOR 300 MS DOES NOT HOLD THE ONLY PROCESSOR FOR 300 MS.** Every iteration of the
spinner passes a function prologue, so a preemption request reaches it within one sysmon period.
```maxon
typealias Integer = int(i64.min to i64.max)

// Far below a spinner's 300 ms and far above a preemption request's 10 ms.
let promptMs = 150

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Spinner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function spin(ms Integer) returns Integer
		let start = Clock.nowMs()
		var acc = 0
		while (Clock.elapsedMs(start) as Integer) < ms 'spin'
			var i = 0
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
	let s = spawn Spinner.create()
	let b = spawn Bystander.create()
	let start = Clock.nowMs()
	let spun = s.spin(300)
	sleep(20)
	let p = try await b.ping() otherwise 0
	let tookMs = Clock.elapsedMs(start) as Integer
	let q = try await spun otherwise 0
	print("prompt={tookMs < promptMs} ping={p} spun={q > 0} preempted={__Builtins.schedPreemptCount() > 0}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
prompt=true ping=1 spun=true preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.preemption-inside-deep-recursion-is-sound -->
<!-- procs: 1 -->
**A PREEMPTION CAN LAND ANYWHERE IN A 20,000-FRAME RECURSION**, including at a prologue whose stack must also
grow. Every pass recomputes a value that flows through every frame and compares it with the first pass, so
a frame a preemption corrupted shows up as `wrong` above zero.
```maxon
typealias Integer = int(i64.min to i64.max)

// Far below a spinner's 300 ms and far above a preemption request's 10 ms.
let promptMs = 150

function down(n Integer, acc Integer) returns Integer
	if n == 0 'bottom'
		return acc
	end 'bottom'
	return down(n - 1, acc: (acc * 31 + n) mod 1000003)
end 'down'

type Recurser
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function churn(ms Integer) returns Integer
		let expected = down(20000, acc: 1)
		let start = Clock.nowMs()
		var wrong = 0
		while (Clock.elapsedMs(start) as Integer) < ms 'churn'
			if down(20000, acc: 1) != expected 'differs'
				wrong = wrong + 1
			end 'differs'
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
	let r = spawn Recurser.create()
	let b = spawn Bystander.create()
	let start = Clock.nowMs()
	let churned = r.churn(300)
	sleep(20)
	let p = try await b.ping() otherwise 0
	let tookMs = Clock.elapsedMs(start) as Integer
	let wrong = try await churned otherwise 0 - 1
	print("prompt={tookMs < promptMs} ping={p} wrong={wrong}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
prompt=true ping=1 wrong=0
```
```exitcode
0
```

<!-- test: sched-preempt.as-many-spinners-as-processors-cannot-starve-another -->
<!-- procs: 4 -->
**EVERY PROCESSOR BUSY WITH A SPINNER, AND `main` AND ONE MORE SERVICE STILL GET A TURN.** No processor is ever
idle, so only a preemption hands one over.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SpinnerHandleArray = Array with Spinner.handle
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

// Far below a spinner's 300 ms and far above a preemption request's 10 ms.
let promptMs = 150

// Recursive, so it is never inlined and every iteration passes a function prologue.
function step(acc Integer, depth Integer) returns Integer
	if depth == 0 'leaf'
		return (acc * 31 + 7) mod 1000003
	end 'leaf'
	return step((acc * 17 + depth) mod 1000003, depth: depth - 1)
end 'step'

type Spinner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function spin(ms Integer) returns Integer
		let start = Clock.nowMs()
		var acc = 0
		while (Clock.elapsedMs(start) as Integer) < ms 'spin'
			var i = 0
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
	var spinners = SpinnerHandleArray.create()
	var i = 0
	while i < __Builtins.schedProcessorCount() 'spawnEach'
		spinners.push(spawn Spinner.create())
		i = i + 1
	end 'spawnEach'
	let b = spawn Bystander.create()
	let start = Clock.nowMs()
	var spun = ReplyPromiseArray.create()
	var k = 0
	while k < spinners.count() 'sendEach'
		let s = try spinners.get(k) otherwise panic("spinners.get out of range at {k}: bounded by the pushes above")
		spun.push(s.spin(300))
		k = k + 1
	end 'sendEach'
	sleep(20)
	let p = try await b.ping() otherwise 0
	let tookMs = Clock.elapsedMs(start) as Integer
	var ran = 0
	while spun.count() > 0 'collect'
		let r = try spun.pop() otherwise panic("spun.pop on a non-empty array")
		let q = try await r otherwise 0
		if q > 0 'spun'
			ran = ran + 1
		end 'spun'
	end 'collect'
	print("prompt={tookMs < promptMs} spinners={ran == spinners.count()} ping={p}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
prompt=true spinners=true ping=1
```
```exitcode
0
```

<!-- test: sched-preempt.a-preempted-coroutine-resumes-before-its-strand-does-anything-else -->
<!-- procs: 1 -->
**A PREEMPTION TAKES THE PROCESSOR, NEVER THE TURN.** A coroutine gives up its green thread only where it
waits (`async` is a coroutine, not a thread), so its owner and its siblings may observe what it wrote only
at those points. `busy` writes `phase = 1`, computes for 150 ms without waiting, then writes `phase = 2`.
`main` and `nap` both become runnable 30 ms in, while `busy` is being preempted every 10 ms; neither may run
until `busy` has finished, so both read 2.
```maxon
typealias Integer = int(i64.min to i64.max)

var phase = 0

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
	var acc = 0
	while (Clock.elapsedMs(start) as Integer) < 150 'spin'
		var i = 0
		while i < 1000 'burst'
			acc = step(acc, depth: 4)
			i = i + 1
		end 'burst'
	end 'spin'
	phase = 2
	return acc mod 2 + 1
end 'busy'

function nap() returns Integer
	sleep(30)
	return phase
end 'nap'

function main() returns ExitCode
	let a = async busy()
	let b = async nap()
	sleep(30)
	let seenByOwner = phase
	let q = await a
	let seenBySibling = await b
	print("owner={seenByOwner} sibling={seenBySibling} busy={q > 0} preempted={__Builtins.schedPreemptCount() > 0}\n")
	return 0 as ExitCode
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
different values, so whenever one is preempted the other overwrites every argument register.
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
		var k = 0
		while k < variants 'reference'
			expected.push(mix(k, b: 2 + self.shift, c: 3, d: 4 + self.shift, e: 5, f: 6 + self.shift, g: 7, h: 8 + self.shift, p: 1.5 * self.scale, q: 2.25, r: 3.125 * self.scale, s: 4.0625, t: 5.5 * self.scale, u: 6.75, v: 7.875 * self.scale, w: 8.5, depth: 7))
			k = k + 1
		end 'reference'
		let start = Clock.nowMs()
		var wrong = 0
		var i = 0
		while (Clock.elapsedMs(start) as Integer) < ms 'churn'
			let k = i mod variants
			let want = try expected.get(k) otherwise panic("expected.get out of range at {k}: bounded by variants")
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
	let churned1 = m1.churn(200)
	let churned2 = m2.churn(200)
	sleep(20)
	let wrong1 = try await churned1 otherwise 0 - 1
	let wrong2 = try await churned2 otherwise 0 - 1
	print("wrong={wrong1 + wrong2} preempted={__Builtins.schedPreemptCount() > 1}\n")
	return 0 as ExitCode
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
some queue order.
```maxon
typealias Integer = int(i64.min to i64.max)

// Far below the spinner's run and far above a preemption request's 10 ms.
let promptMs = 150
// Calibrated to about 300 ms: a loop this long cannot be mistaken for a thread that simply finished.
let spinSteps = 90000000

type Spinner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	// ⛔ NOT ONE CALL IN THE LOOP, which is the whole point: a poisoned stack guard is read by a function
	// that reserves a frame, and this body never enters one.
	export function spin(steps Integer) returns Integer
		var acc = 0
		var i = 0
		while i < steps 'spin'
			acc = (acc * 31 + i) mod 1000003
			i = i + 1
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
	let s = spawn Spinner.create()
	let b = spawn Bystander.create()
	let start = Clock.nowMs()
	let spun = s.spin(spinSteps)
	sleep(20)
	let p = try await b.ping() otherwise 0
	let tookMs = Clock.elapsedMs(start) as Integer
	let q = try await spun otherwise 0
	print("prompt={tookMs < promptMs} ping={p} spun={q > 0} preempted={__Builtins.schedPreemptCount() > 0}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
prompt=true ping=1 spun=true preempted=true
```
```exitcode
0
```

<!-- test: sched-preempt.every-processor-in-a-call-free-loop-still-yields -->
<!-- procs: 4 -->
**ONE CALL-FREE SPINNER PER PROCESSOR, AND A FIFTH SERVICE STILL GETS A TURN.** No processor is ever idle and
no spinner ever reaches a prologue, so only stopping a thread where it runs hands one over.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias SpinnerHandleArray = Array with Spinner.handle
typealias ReplyPromise = Promise with (Integer, ServiceError)
typealias ReplyPromiseArray = Array with ReplyPromise

let promptMs = 150
let spinSteps = 90000000

type Spinner
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	// ⛔ NOT ONE CALL IN THE LOOP, which is the whole point: a poisoned stack guard is read by a function
	// that reserves a frame, and this body never enters one.
	export function spin(steps Integer) returns Integer
		var acc = 0
		var i = 0
		while i < steps 'spin'
			acc = (acc * 31 + i) mod 1000003
			i = i + 1
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
	var spinners = SpinnerHandleArray.create()
	var i = 0
	while i < __Builtins.schedProcessorCount() 'spawnEach'
		spinners.push(spawn Spinner.create())
		i = i + 1
	end 'spawnEach'
	let b = spawn Bystander.create()
	let start = Clock.nowMs()
	var spun = ReplyPromiseArray.create()
	var k = 0
	while k < spinners.count() 'sendEach'
		let s = try spinners.get(k) otherwise panic("spinners.get out of range at {k}: bounded by the pushes above")
		spun.push(s.spin(spinSteps))
		k = k + 1
	end 'sendEach'
	sleep(20)
	let p = try await b.ping() otherwise 0
	let tookMs = Clock.elapsedMs(start) as Integer
	var ran = 0
	while spun.count() > 0 'collect'
		let r = try spun.pop() otherwise panic("spun.pop on a non-empty array")
		let q = try await r otherwise 0
		if q > 0 'spun'
			ran = ran + 1
		end 'spun'
	end 'collect'
	print("prompt={tookMs < promptMs} spinners={ran == spinners.count()} ping={p}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
prompt=true spinners=true ping=1
```
```exitcode
0
```

<!-- test: sched-preempt.every-register-survives-a-stop-anywhere -->
<!-- procs: 1 -->
**A THREAD STOPPED MID-LOOP RESUMES WITH EVERY REGISTER IT HAD.** Eight integers and eight floats are live
across a loop with no call in it, so a stop lands among them rather than at a boundary where fewer are live.
Every round recomputes the same checksum and is compared with the first.
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
		var wrong = 0
		var expected = 0
		var r = 0
		while r < rounds 'rounds'
			var a = 1
			var b = 2
			var c = 3
			var d = 4
			var e = 5
			var f = 6
			var g = 7
			var h = 8
			var p = 1.5
			var q = 2.25
			var u = 3.125
			var v = 4.0625
			var w = 5.5
			var x = 6.75
			var y = 7.875
			var z = 8.5
			var i = 0
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
	let churned = c.churn()
	sleep(20)
	let wrong = try await churned otherwise 0 - 1
	print("wrong={wrong} preempted={__Builtins.schedPreemptCount() > 0}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
wrong=0 preempted=true
```
```exitcode
0
```
