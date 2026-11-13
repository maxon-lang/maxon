---
feature: runtime-park-waits
status: experimental
keywords: [Scheduler.quiesce, CownTorture, wait, yield, message, spawn factory, modify, acquire, E3221, E3205, message signature]
category: concurrency
---

# A runtime park that waits on another party is a wait

## Documentation

`Scheduler.quiesce()` parks the caller until every other green thread is idle, and `CownTorture.violations` spawns green threads that acquire cowns and joins them. Both wait on other parties, so a function that calls either waits, and a message or spawn factory that reaches one is refused (E3221): the caller's own service is pending while it parks, so the wait could not end. Inside a `modify` block, a one-call open or an `acquire` block they are refused (E3205), directly or through a callee. `main` and a plain coroutine hold nothing and may call them.

## Tests

<!-- test: runtime-park-waits.error.a-message-that-quiesces-is-refused -->
`settle` runs in `Settler`'s own message, so the scheduler could never go idle while it parks.

```maxon
typealias Tally = int(0 to u64.max)

type Settler
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function settle() returns Tally
		return Scheduler.quiesce() as Tally
	end 'settle'
end 'Settler'

function main() returns ExitCode
	let settler = spawn Settler.create()
	_ = settler

	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:11:18: the message 'settle' may wait (it calls 'quiesce', which parks until every other green thread is idle, at line 86) while holding its own service; call it from `main`, which holds no service
```

<!-- test: runtime-park-waits.error.a-message-that-runs-the-cown-torture-is-refused -->
`stress` joins the green threads the torture spawns while holding `Stresser`'s own service.

```maxon
typealias Tally = int(0 to u64.max)

type Stresser
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function stress() returns Tally
		return CownTorture.violations(4, workers: 2, rounds: 2) as Tally
	end 'stress'
end 'Stresser'

function main() returns ExitCode
	let stresser = spawn Stresser.create()
	_ = stresser

	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:11:18: the message 'stress' may wait (it calls 'violations', which joins the acquirers it spawns, at line 87) while holding its own service; call it from `main`, which holds no service
```

<!-- test: runtime-park-waits.error.a-spawn-factory-that-quiesces-is-refused -->
<!-- tier: stdlib -->
A spawn factory holds its own service while it runs, so it may not park either. The case calls the intrinsic itself, so the line the diagnostic cites is the case's own.

```maxon
typealias Tally = int(0 to u64.max)

type Booted
	var seen as Tally

	static function create() returns Self
		let parked = __Builtins.gtQuiesce()

		return Self{seen: parked as Tally}
	end 'create'
end 'Booted'

function main() returns ExitCode
	let booted = spawn Booted.create()
	_ = booted

	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:7:18: the spawn factory 'create' may wait (it parks until every other green thread is idle, at line 8) while holding its own service; call it from `main`, which holds no service
```

<!-- test: runtime-park-waits.error.a-message-reaches-the-park-through-a-callee -->
<!-- tier: stdlib -->
`relay` waits through `drain`, a function that calls the park.

```maxon
typealias Tally = int(0 to u64.max)

function drain() returns Tally
	return __Builtins.gtQuiesce() as Tally
end 'drain'

type Relay
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function relay() returns Tally
		return drain()
	end 'relay'
end 'Relay'

function main() returns ExitCode
	let relay = spawn Relay.create()
	_ = relay

	return 0
end 'main'
```
```maxoncstderr
error E3221: <fragment>:15:18: the message 'relay' may wait (it calls 'drain', which parks until every other green thread is idle, at line 5) while holding its own service; call it from `main`, which holds no service
```

<!-- test: runtime-park-waits.error.a-quiesce-inside-a-modify-block-is-refused -->
<!-- tier: stdlib -->
Another coroutine could run while the park waits, so `batch` may not stay open across it.

```maxon
typealias Tally = int(0 to u64.max)

type Batch
	export var done as Tally

	static function create() returns Batch
		return Self{done: 0}
	end 'create'
end 'Batch'

function main() returns ExitCode
	let batch = Batch.create() as isolated Batch

	modify batch 'edit'
		batch.done = __Builtins.gtQuiesce() as Tally
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:16:27: cannot wait inside 'edit'; another coroutine could run while 'batch' is open. Await before or after the block
```

<!-- test: runtime-park-waits.a-main-may-quiesce-and-run-the-cown-torture -->
`main` holds no service, so it may park on the scheduler and join the torture's green threads. The torture answers 0 when mutual exclusion held, and the quiesce answers how many other green threads are still owned.

```maxon
function main() returns ExitCode
	let violations = CownTorture.violations(4, workers: 2, rounds: 2)
	let outstanding = Scheduler.quiesce()

	print("violations={violations} outstanding={outstanding}\n")

	return 0
end 'main'
```
```stdout
violations=0 outstanding=0
```
```exitcode
0
```

<!-- test: runtime-park-waits.a-a-coroutine-of-main-may-run-the-cown-torture -->
A coroutine of `main` holds no service either, so it may run the torture.

```maxon
typealias Tally = int(0 to u64.max)

function stress() returns Tally
	return CownTorture.violations(4, workers: 2, rounds: 2) as Tally
end 'stress'

function main() returns ExitCode
	let job = async stress()
	let violations = await job

	print("violations={violations}\n")

	return 0
end 'main'
```
```stdout
violations=0
```
```exitcode
0
```

<!-- test: runtime-park-waits.error.a-message-returning-an-inline-ranged-int-is-refused-at-its-return-type -->
An `int(…)` spelled inline in a message's return type is the same E3005 a plain function gets, not a compiler fault: the message is refused where it is declared, so no request union is built for it.

```maxon
typealias Money = int(i64.min to i64.max)

type Account
	var balance as Money

	static function create(opening Money) returns Self
		return Self{balance: opening}
	end 'create'

	export function historySize() returns int(0 to u64.max)
		return 7
	end 'historySize'
end 'Account'

function main() returns ExitCode
	let account = spawn Account.create(1)
	_ = account

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:40: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: runtime-park-waits.error.a-message-taking-an-inline-ranged-int-is-refused-at-its-parameter -->
```maxon
typealias Money = int(i64.min to i64.max)

type Account
	var balance as Money

	static function create(opening Money) returns Self
		return Self{balance: opening}
	end 'create'

	export function deposit(by int(0 to 100))
		self.balance = self.balance + (by as Money)
	end 'deposit'
end 'Account'

function main() returns ExitCode
	let account = spawn Account.create(1)
	_ = account

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:29: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: runtime-park-waits.error.a-message-returning-a-tuple-with-an-inline-ranged-element-is-refused -->
```maxon
typealias Money = int(i64.min to i64.max)

type Account
	var balance as Money

	static function create(opening Money) returns Self
		return Self{balance: opening}
	end 'create'

	export function bounds() returns (Money, int(0 to 9))
		return (self.balance, 2)
	end 'bounds'
end 'Account'

function main() returns ExitCode
	let account = spawn Account.create(1)
	_ = account

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:43: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: runtime-park-waits.error.a-message-returning-an-inline-function-type-is-refused -->
```maxon
typealias Money = int(i64.min to i64.max)

type Account
	var balance as Money

	static function create(opening Money) returns Self
		return Self{balance: opening}
	end 'create'

	export function step() returns function(Money) returns Money
		return function(n Money) gives n
	end 'step'
end 'Account'

function main() returns ExitCode
	let account = spawn Account.create(1)
	_ = account

	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:11:33: Expected 'a type' but got 'function'
```

<!-- test: runtime-park-waits.a-messages-with-aliased-signatures-register-and-answer -->
The same shapes written through a `typealias` register as messages and answer: a ranged return, a tuple return, and a ranged and a tuple parameter.

```maxon
typealias Money = int(i64.min to i64.max)
typealias Narrow = int(0 to 9)
typealias Pair = (Money, Narrow)

type Account
	var balance as Money

	static function create(opening Money) returns Self
		return Self{balance: opening}
	end 'create'

	export function narrowed(by Narrow) returns Narrow
		return by
	end 'narrowed'

	export function bounds() returns Pair
		return (self.balance, 2)
	end 'bounds'

	export function absorb(pair Pair)
		let (amount, _) = pair
		self.balance = self.balance + amount
	end 'absorb'
end 'Account'

function main() returns ExitCode
	let account = spawn Account.create(5)
	let narrowed = try await account.narrowed(3) otherwise 0
	try account.absorb((10, 4)) otherwise ignore
	let (balance, second) = try await account.bounds() otherwise return 1

	print("{narrowed} {balance} {second}\n")

	return 0
end 'main'
```
```stdout
3 15 2
```
```exitcode
0
```
