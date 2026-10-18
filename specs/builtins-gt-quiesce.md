---
feature: builtins-gt-quiesce
status: experimental
keywords: [__Builtins, gtQuiesce, scheduler, green-threads, services, drain]
category: system
---

# `__Builtins.gtQuiesce()`

## Documentation

`__Builtins.gtQuiesce()` parks the calling green thread until every other green thread that owns a slot in
the scheduler's live count has finished, and answers how many have not. It takes no arguments and answers an
`int`.

Every `__Builtins` member is library-tier: only a source file under `stdlib/` or `runtime/` may call one,
and a call from anywhere else is E3185. The cases below call the intrinsics themselves, so they compile in
the library tier (`<!-- tier: stdlib -->`). A program calls `Scheduler.quiesce()`, which answers a
`GreenThreadCount`.

It is not the exit drain. It releases no registry slot, runs no global cleanup and stops no worker, so the
program carries on afterwards exactly as it was.

The wait ends in one of two ways. The live count reaches zero, and the answer is `0`. Or every machine is idle
with nothing pending, so nothing that still owns a green thread can ever run again, and the answer is the count
that is left: a program that asks is told, where one that does not is a scheduler deadlock. A coroutine
whose promise has been dropped answers at once, without waiting, with the live count at that moment.

## Targets

wasm32-wasi has no green threads, so a program naming it is refused at the call (E3104).

## Tests

<!-- test: builtins-gt-quiesce.waits-for-a-service-that-was-shut-down -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
The service prints from its own green thread after `main` has asked for the shutdown, and the print precedes the
line that follows the wait.
```maxon
typealias Count = int(0 to 1000)

type Greeter
	var greeted as Count

	static function create() returns Self
		return Self{greeted: 0}
	end 'create'

	export function greet(times Count)
		self.greeted = self.greeted + times
		sleep(20)
		print("greeted {self.greeted}\n")
	end 'greet'
end 'Greeter'

function main() returns ExitCode
	var greeter = spawn Greeter.create()
	try greeter.greet(2) otherwise panic("an open mailbox with room takes the send")
	greeter.shutdown()

	let outstanding = __Builtins.gtQuiesce()
	print("quiesced {outstanding}\n")
	return 0
end 'main'
```
```stdout
greeted 2
quiesced 0
```
```exitcode
0
```

<!-- test: builtins-gt-quiesce.with-nothing-outstanding-answers-zero-at-once -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let outstanding = __Builtins.gtQuiesce()
	print("outstanding {outstanding}\n")
	return 0
end 'main'
```
```stdout
outstanding 0
```
```exitcode
0
```

<!-- test: builtins-gt-quiesce.answers-what-nothing-can-finish -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
The service waits on its mailbox for a message that only `main` could send, so nothing can ever finish it. The wait answers the one green thread that is left instead of ending the program, and a second wait after the
shutdown finds none.
```maxon
typealias Count = int(0 to 1000)

type Greeter
	var greeted as Count

	static function create() returns Self
		return Self{greeted: 0}
	end 'create'

	export function greet(times Count)
		self.greeted = self.greeted + times
		print("greeted {self.greeted}\n")
	end 'greet'
end 'Greeter'

function main() returns ExitCode
	var greeter = spawn Greeter.create()
	try greeter.greet(1) otherwise panic("an open mailbox with room takes the send")

	let stranded = __Builtins.gtQuiesce()
	print("stranded {stranded}\n")

	greeter.shutdown()
	let outstanding = __Builtins.gtQuiesce()
	print("outstanding {outstanding}\n")
	return 0
end 'main'
```
```stdout
greeted 1
stranded 1
outstanding 0
```
```exitcode
0
```

<!-- test: builtins-gt-quiesce.a-second-waiter-aborts -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
The service waits for every green thread while `main` waits too. One waiter at a time is all the scheduler holds,
so whichever arrives second ends the program with runtime abort 123.
```maxon
type Waiter
	static function create() returns Self
		return Self{}
	end 'create'

	export function wait()
		let outstanding = __Builtins.gtQuiesce()
		print("the service saw {outstanding}\n")
	end 'wait'
end 'Waiter'

function main() returns ExitCode
	var waiter = spawn Waiter.create()
	try waiter.wait() otherwise panic("an open mailbox with room takes the send")

	let outstanding = __Builtins.gtQuiesce()
	print("main saw {outstanding}\n")
	return 0
end 'main'
```
```stderr
fatal error: runtime abort 123 (schedulerQuiesceWaiterTaken)
```
```exitcode
123
```

<!-- test: builtins-gt-quiesce.races-coroutine-completion-across-many-rounds -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 4 -->
⭐⭐ **A WAIT THAT REGISTERS WHILE THE LAST GREEN THREADS' COROUTINES ARE STILL COMPLETING.** Each round spawns
three services, sends each one `work` and then `linger`, and then shuts it down. Both messages run three
coroutines on the worker's strand, two that yield and one that sleeps for 1 ms, and sum their answers.
`main` awaits the three `work` replies and then waits at once. So at four processors the `linger`
coroutines complete and the workers exit on other machines while `main` is checking the live count,
registering as the waiter, or already parked. The worker whose exit takes the live count to zero claims the
waiter word and readies `main`. Exactly one party may claim it, whichever order the registration and the
last exit come in. A wake lost there leaves `main` parked until the deadlock check answers a count that is
not zero, or hangs the round. A claim made twice readies `main` twice. A `linger` that lost a coroutine
prints its round.

The replies are awaited before the wait because a reply nobody has awaited holds its slot in the live count.
A wait made over one would end only at the deadlock check, which answers the count of unawaited replies.

✅ **IT IS A CONTROL: ANY SOUND WAKE PROTOCOL PASSES IT, AND IT HOLDS THIS ONE TO ITS ANSWERS.** The waker
claims the waiter word by a compare-and-swap. That and the registration's own atomic steps on the same word
decide between the two without a lock.
`races-coroutine-completion-at-one-processor` is its twin at one processor, where the registration and the
last exit never overlap.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise

let rounds = 100
let workersPerRound = 3

function yieldingPart(v Integer) returns Integer
	Scheduler.yield()
	return v
end 'yieldingPart'

function sleepingPart(v Integer) returns Integer
	sleep(1)
	return v
end 'sleepingPart'

function coroutineSum(v Integer) returns Integer
	let a = async yieldingPart(v)
	let b = async sleepingPart(v)
	let c = async yieldingPart(1)
	return (await a) + (await b) + (await c)
end 'coroutineSum'

type Worker
	var answered as Integer

	static function create() returns Self
		return Self{answered: 0}
	end 'create'

	export function work(v Integer) returns Integer
		self.answered = self.answered + 1
		return coroutineSum(v)
	end 'work'

	export function linger(v Integer)
		if coroutineSum(v) != 2 * v + 1 'lostACoroutine'
			print("round {v} lost a coroutine\n")
		end 'lostACoroutine'
	end 'linger'
end 'Worker'

function main() returns ExitCode
	var outstanding = 0 as Integer
	var sum = 0 as Integer

	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()

		for _ in 0 upto workersPerRound 'spawnEach'
			var worker = spawn Worker.create()
			replies.push(try worker.work(r) otherwise panic("an open mailbox with room takes the send"))
			try worker.linger(r) otherwise panic("an open mailbox with room takes the send")
			worker.shutdown()
		end 'spawnEach'

		for p in replies 'collect'
			sum = sum + (await p)
		end 'collect'

		outstanding = outstanding + (__Builtins.gtQuiesce() as Integer)
	end 'eachRound'

	print("outstanding={outstanding} sum={sum}\n")
	return 0
end 'main'
```
```stdout
outstanding=0 sum=30000
```
```exitcode
0
```

<!-- test: builtins-gt-quiesce.races-coroutine-completion-at-one-processor -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
**THE ONE-PROCESSOR TWIN OF `races-coroutine-completion-across-many-rounds`.** With one processor nothing
runs beside `main`, so the wait's registration and the last worker's exit never overlap. Each round either
finds the live count already at zero, or registers first and is readied by the last exit. Those orders must
give the same answers as the racing ones. Like its twin, it is a control.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias ReplyPromise = Promise with Integer
typealias ReplyPromiseArray = Array with ReplyPromise

let rounds = 100
let workersPerRound = 3

function yieldingPart(v Integer) returns Integer
	Scheduler.yield()
	return v
end 'yieldingPart'

function sleepingPart(v Integer) returns Integer
	sleep(1)
	return v
end 'sleepingPart'

function coroutineSum(v Integer) returns Integer
	let a = async yieldingPart(v)
	let b = async sleepingPart(v)
	let c = async yieldingPart(1)
	return (await a) + (await b) + (await c)
end 'coroutineSum'

type Worker
	var answered as Integer

	static function create() returns Self
		return Self{answered: 0}
	end 'create'

	export function work(v Integer) returns Integer
		self.answered = self.answered + 1
		return coroutineSum(v)
	end 'work'

	export function linger(v Integer)
		if coroutineSum(v) != 2 * v + 1 'lostACoroutine'
			print("round {v} lost a coroutine\n")
		end 'lostACoroutine'
	end 'linger'
end 'Worker'

function main() returns ExitCode
	var outstanding = 0 as Integer
	var sum = 0 as Integer

	for r in 0 upto rounds 'eachRound'
		var replies = ReplyPromiseArray.create()

		for _ in 0 upto workersPerRound 'spawnEach'
			var worker = spawn Worker.create()
			replies.push(try worker.work(r) otherwise panic("an open mailbox with room takes the send"))
			try worker.linger(r) otherwise panic("an open mailbox with room takes the send")
			worker.shutdown()
		end 'spawnEach'

		for p in replies 'collect'
			sum = sum + (await p)
		end 'collect'

		outstanding = outstanding + (__Builtins.gtQuiesce() as Integer)
	end 'eachRound'

	print("outstanding={outstanding} sum={sum}\n")
	return 0
end 'main'
```
```stdout
outstanding=0 sum=30000
```
```exitcode
0
```

<!-- test: builtins-gt-quiesce.a-dropped-coroutine-parked-in-the-wait-is-not-the-waiter-afterwards -->
<!-- unsupported-targets: wasm32-wasi -->
A coroutine that calls `Scheduler.quiesce()` parks as the scheduler's one waiter while `main` awaits a sibling. `main` then
returns with the quiescing coroutine un-awaited, so scope exit drops its promise while it is parked in the wait. The
drop must withdraw the waiter registration before the coroutine can be reclaimed: the last live thread's exit
claims that registration, and one naming a reclaimed thread would be readied as though it were parked.
```maxon
typealias Integer = int(i64.min to i64.max)

function quiescer() returns Integer
	return Scheduler.quiesce() as Integer
end 'quiescer'

function fast() returns Integer
	Scheduler.yield()
	return 42
end 'fast'

function main() returns ExitCode
	let waiter = async quiescer()
	let sibling = async fast()
	let answer = await sibling
	let finished = 1 if waiter.isComplete() else 0
	return (answer + finished) as ExitCode
end 'main'
```
```exitcode
42
```
