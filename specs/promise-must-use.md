---
feature: promise-must-use
status: experimental
keywords: [async, promise, await, cancel, must-use, dropped, E3213]
category: semantics
---

# A promise held in a local is awaited, cancelled, stored or returned

## Documentation

A promise held in a local variable is consumed on every path out of its scope: it is awaited, cancelled with
`.cancel()`, stored, returned or passed to a parameter that takes it. Dropping it at the end of its scope,
overwriting it, discarding it with `_ =`, or leaving the function on a path that does none of these is a
compile error. A promise inside a container or a field is cancelled when its holder is dropped.

## Tests

<!-- test: a-promise-dropped-at-the-end-of-its-scope-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
`later` is never awaited, cancelled, stored or returned, so the end of `main` drops it.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function main() returns ExitCode
	let later = async slow()
	_ = later.isComplete()
	print("started\n")
	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:13:2: this drops 'later', which was never awaited, cancelled, stored or returned: `await later`, call `later.cancel()`, store it or return it
```

<!-- test: a-promise-overwritten-before-it-is-consumed-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Re-arming `later` drops the first promise before anything consumed it.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function main() returns ExitCode
	var later = async slow()
	later = async slow()
	_ = await later
	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:11:2: this drops a promise that was never awaited, cancelled, stored or returned: `await` it, call `.cancel()` on it, store it or return it
```

<!-- test: a-discarded-promise-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
`_ =` discards the promise at once, which is a drop.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function main() returns ExitCode
	_ = async slow()
	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:10:2: this drops a promise that was never awaited, cancelled, stored or returned: `await` it, call `.cancel()` on it, store it or return it
```

<!-- test: a-promise-awaited-on-one-path-only-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The `await` sits in one branch, so the other path reaches the end of `main` with the promise still held.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function choose() returns bool
	return false
end 'choose'

function main() returns ExitCode
	let later = async slow()

	if choose() 'maybe'
		_ = await later
	end 'maybe'

	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:16:2: this drops a promise that was never awaited, cancelled, stored or returned: `await` it, call `.cancel()` on it, store it or return it
```

<!-- test: an-early-return-before-the-await-of-a-promise-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The early `return 1` leaves with the promise held.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function leave(early bool) returns Tally
	let later = async slow()

	if early 'leavingEarly'
		return 1
	end 'leavingEarly'

	return await later
end 'leave'

function main() returns ExitCode
	print("{leave(true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:13:3: this drops 'later', which was never awaited, cancelled, stored or returned: `await later`, call `later.cancel()`, store it or return it
```

<!-- test: a-promise-stored-in-a-field-is-accepted -->
Storing the promise in a record hands it to the record, which cancels it when it is dropped.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Pending
	var later as TallyPromise

	static function of(later TallyPromise) returns Self
		return Self{later: later}
	end 'of'
end 'Pending'

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function main() returns ExitCode
	let held = Pending.of(async slow())
	_ = held
	print("stored\n")
	return 0
end 'main'
```
```stdout
stored
```
```exitcode
0
```

<!-- test: a-promise-returned-is-accepted -->
Returning the promise hands it to the caller, which awaits it.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function start() returns TallyPromise
	return async slow()
end 'start'

function main() returns ExitCode
	let later = start()
	print("{await later}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-promise-cancelled-is-accepted -->
`.cancel()` consumes the promise, so the end of `main` drops nothing.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function main() returns ExitCode
	let later = async slow()
	later.cancel()
	print("cancelled\n")
	return 0
end 'main'
```
```stdout
cancelled
```
```exitcode
0
```

<!-- test: a-promise-awaited-on-every-path-is-accepted -->
Both branches consume the promise, so no path reaches an exit with it held.

```maxon
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	Scheduler.yield()
	return 1
end 'slow'

function choose() returns bool
	return false
end 'choose'

function main() returns ExitCode
	let later = async slow()

	if choose() 'maybe'
		_ = await later
	end 'maybe' else 'otherwise'
		later.cancel()
	end 'otherwise'

	print("done\n")
	return 0
end 'main'
```
```stdout
done
```
```exitcode
0
```

<!-- test: a-service-reply-discarded-with-an-underscore-is-accepted -->
Sending a message and discarding its reply with `_ =` is an explicit fire-and-forget.

```maxon
type Echo
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	_ = try h.say("a") otherwise panic("a running service with room takes the send")
	print("sent\n")
	return 0
end 'main'
```
```stdout
sent
```
```exitcode
0
```

<!-- test: a-service-reply-held-in-a-local-and-never-consumed-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
Binding the reply makes it a promise in a local, so the end of `main` drops it.

```maxon
type Echo
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function say(s String) returns String
		return "[{s}]"
	end 'say'
end 'Echo'

function main() returns ExitCode
	let h = spawn Echo.create()
	let reply = try h.say("a") otherwise panic("a running service with room takes the send")
	_ = reply.isComplete()
	print("sent\n")
	return 0
end 'main'
```
```maxoncstderr
error E3213: <fragment>:19:2: this drops 'reply', which was never awaited, cancelled, stored or returned: `await reply`, call `reply.cancel()`, store it or return it
```
