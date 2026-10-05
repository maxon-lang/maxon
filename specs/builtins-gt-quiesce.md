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

It is not the exit drain. It releases no registry slot, runs no global cleanup and stops no worker, so the
program carries on afterwards exactly as it was.

The wait ends in one of two ways. The live count reaches zero, and the answer is `0`. Or every machine is idle
with nothing pending, so nothing that still owns a green thread can ever run again, and the answer is the count
that is left: a program that asks is told, where one that does not is a scheduler deadlock.

## Targets

wasm32-wasi has no green threads, so a program naming it is refused at the call (E3104).

## Tests

<!-- test: builtins-gt-quiesce.waits-for-a-service-that-was-shut-down -->
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
	greeter.greet(2)
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
	greeter.greet(1)

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
	waiter.wait()

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
