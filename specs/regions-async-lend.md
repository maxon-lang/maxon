---
feature: regions-async-lend
status: experimental
keywords: [async, by-reference, lent, promise, cancel, coroutine, mutable parameter]
category: semantics
---

# A variable passed by reference to `async` is lent until its promise is drained

## Documentation

A variable handed to a `mutable` parameter of an `async` callee is the coroutine's to write: the coroutine receives the
caller's own cell, and the write is visible once the promise is awaited or cancelled. Until then the variable is lent to
the coroutine like a move that ends at the drain, and the caller may neither read, write nor pass it, nor name it in the
statement that starts the coroutine. A tied promise is awaited or cancelled before its function returns, and
`.cancel()` waits for the coroutine to finish.

## Tests

<!-- test: the-coroutine-writes-the-callers-variable-and-the-await-shows-it -->
<!-- unsupported-targets: wasm32-wasi -->
`bump` writes its `mutable` parameter, which is the caller's own `n`: after the await `n` is 6.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	let k = await promise
	print("{n} {k}\n")
	return 0
end 'main'
```
```stdout
6 1
```
```exitcode
0
```

<!-- test: the-coroutine-writes-a-string-variable-and-the-await-shows-it -->
<!-- unsupported-targets: wasm32-wasi -->
A `String` variable is the coroutine's cell as well.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var label = "original"
	let promise = async shout(label)
	let k = await promise
	print("{label} {k}\n")
	return 0
end 'main'
```
```stdout
replaced 1
```
```exitcode
0
```

<!-- test: cancelling-the-promise-waits-and-the-write-is-visible -->
<!-- unsupported-targets: wasm32-wasi -->
`.cancel()` of a tied promise waits for the coroutine to stop, so the caller's variable holds the write.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	Scheduler.yield()
	promise.cancel()
	print("{n}\n")
	return 0
end 'main'
```
```stdout
6
```
```exitcode
0
```

<!-- test: cancelling-a-coroutine-that-never-started-leaves-the-variable-alone -->
<!-- unsupported-targets: wasm32-wasi -->
A coroutine cancelled before it starts never runs, so the variable keeps its value.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	promise.cancel()
	print("{n}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-field-of-a-local-handed-by-reference-is-written-by-the-coroutine -->
<!-- unsupported-targets: wasm32-wasi -->
The by-reference argument is the field cell `holder.f`.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	let k = await promise
	print("{holder.f} {k}\n")
	return 0
end 'main'
```
```stdout
replaced 1
```
```exitcode
0
```

<!-- test: each-lap-lends-the-variable-until-its-own-await -->
<!-- unsupported-targets: wasm32-wasi -->
The variable is lent for one lap and free again after its await, so three laps count to eight.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	var lap = 0 as Tally

	while lap < 3 'laps'
		let promise = async bump(n, k: 1)
		let k = await promise
		lap = lap + k
	end 'laps'

	print("{n}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: a-read-between-the-async-and-the-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The variable is lent to the coroutine until `promise` is awaited, so reading it before is refused.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	print("{n}\n")
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:10: use of moved value 'n': it is lent to the coroutine of the `async` call at 13:22 until that promise is awaited or cancelled
```

<!-- test: a-write-between-the-async-and-the-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Assigning the lent variable before the await is refused.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	n = 7
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:2: use of moved value 'n': it is lent to the coroutine of the `async` call at 13:22 until that promise is awaited or cancelled
```

<!-- test: passing-the-lent-variable-before-the-await-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Handing the lent variable to another call before the await is refused.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function twice(n Tally) returns Tally
	return n * 2
end 'twice'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	let doubled = twice(n)
	let k = await promise
	print("{k} {doubled}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:22: use of moved value 'n': it is lent to the coroutine of the `async` call at 17:22 until that promise is awaited or cancelled
```

<!-- test: a-sibling-field-of-the-lent-local-is-refused-before-the-await -->
<!-- unsupported-targets: wasm32-wasi -->
The lend is per variable: reading `holder.g` while `holder.f` is lent is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String
	export var g as String

	static function create() returns Self
		return Self{f: "original", g: "other"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	print("{holder.g}\n")
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:10: use of moved value 'holder': it is lent to the coroutine of the `async` call at 22:22 until that promise is awaited or cancelled
```

<!-- test: a-read-after-the-cancel-is-accepted -->
<!-- unsupported-targets: wasm32-wasi -->
The lend ends at the cancel.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)
	Scheduler.yield()
	promise.cancel()
	n = n + 10
	print("{n}\n")
	return 0
end 'main'
```
```stdout
16
```
```exitcode
0
```

<!-- test: a-read-in-the-statement-that-starts-the-coroutine-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Every read in the statement that starts the coroutine counts: the coroutine's write is not ordered against them.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: n + 1)
	let k = await promise
	print("{n} {k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3186: <fragment>:13:27: argument 'n' is read by another operand of this statement and passed by reference to 'bump' (parameter 'n') — a hold cannot protect a value its by-reference cell overwrites. Copy 'n' into a `let` in an earlier statement (`.clone()` a record, array or string) and use the copy for the other operand
```

<!-- test: a-literal-at-a-by-reference-position-of-an-async-call-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A by-reference position takes a variable, not a literal.

```maxon
typealias Tally = int(0 to u64.max)

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	let promise = async bump(5, k: 1)
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:12:22: cannot pass a literal to a function that writes parameter 'n' (in main); pass a `var`
```

<!-- test: a-loop-that-reassigns-the-lent-holder-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The lend belongs to the variable, not to the value it holds at the moment: a loop that assigns `holder` would free the record whose field cell the coroutine still holds, so the assignment is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	var lap = 0 as Tally

	while lap < 2 'laps'
		holder = Holder.create()
		lap = lap + 1
	end 'laps'

	let k = await promise
	print("{holder.f} {k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:3: use of moved value 'holder': it is lent to the coroutine of the `async` call at 21:22 until that promise is awaited or cancelled
```

<!-- test: a-read-in-a-loop-that-reassigns-the-lent-holder-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Inside a loop that assigns the lent variable, a read before the assignment is refused as well.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	var lap = 0 as Tally

	while lap < 2 'laps'
		print("{holder.f}\n")
		holder = Holder.create()
		lap = lap + 1
	end 'laps'

	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:11: use of moved value 'holder': it is lent to the coroutine of the `async` call at 21:22 until that promise is awaited or cancelled
```

<!-- test: a-holder-reassigned-after-its-await-in-a-loop-is-accepted -->
<!-- unsupported-targets: wasm32-wasi -->
Each lap awaits its own promise, which ends the lend, so the holder may be replaced afterwards.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	var lap = 0 as Tally

	while lap < 2 'laps'
		let promise = async shout(holder.f)
		let k = await promise
		print("{holder.f}\n")
		holder = Holder.create()
		lap = lap + k
	end 'laps'

	print("{holder.f}\n")
	return 0
end 'main'
```
```stdout
replaced
replaced
original
```
```exitcode
0
```

<!-- test: a-shadow-of-the-lent-holder-in-an-inner-scope-is-accepted -->
<!-- unsupported-targets: wasm32-wasi -->
An inner `holder` is another variable: only the outer one is lent.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	var lap = 0 as Tally

	while lap < 1 'laps'
		let holder = Holder.create()
		print("{holder.f}\n")
		lap = lap + 1
	end 'laps'

	let k = await promise
	print("{holder.f} {k}\n")
	return 0
end 'main'
```
```stdout
original
replaced 1
```
```exitcode
0
```

<!-- test: the-lent-holder-is-refused-again-when-its-shadow-ends -->
<!-- unsupported-targets: wasm32-wasi -->
The outer `holder` comes back at the end of the inner scope, still lent.

```maxon
typealias Tally = int(0 to u64.max)

type Holder
	export var f as String

	static function create() returns Self
		return Self{f: "original"}
	end 'create'
end 'Holder'

function shout(text mutable String) returns Tally
	sleep(1)
	text = "replaced"

	return 1
end 'shout'

function main() returns ExitCode
	var holder = Holder.create()
	let promise = async shout(holder.f)
	var lap = 0 as Tally

	while lap < 1 'laps'
		let holder = Holder.create()
		print("{holder.f}\n")
		lap = lap + 1
	end 'laps'

	print("{holder.f}\n")
	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:10: use of moved value 'holder': it is lent to the coroutine of the `async` call at 21:22 until that promise is awaited or cancelled
```

<!-- test: a-lent-variable-reassigned-in-a-try-body-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A `try` body is no exception to the lend: the assignment is refused before the await.

```maxon
typealias Tally = int(0 to u64.max)

enum Failure implements Error
	broke
end 'Failure'

function check(k Tally) throws Failure
	if k > 100 'tooBig'
		throw Failure.broke
	end 'tooBig'
end 'check'

function bump(n mutable Tally, k Tally) returns Tally
	sleep(1)
	n = n + 1

	return k
end 'bump'

function main() returns ExitCode
	var n = 5 as Tally
	let promise = async bump(n, k: 1)

	try 'work'
		check(1)
		n = 7
	end 'work'
	otherwise (e) 'handler'
		match e 'which'
			broke then print("broke\n")
		end 'which'
	end 'handler'

	let k = await promise
	print("{k}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:3: use of moved value 'n': it is lent to the coroutine of the `async` call at 23:22 until that promise is awaited or cancelled
```
