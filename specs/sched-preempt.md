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
