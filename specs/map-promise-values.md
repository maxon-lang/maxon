---
feature: map-promise-values
status: stable
keywords: [map, value, column, promise, drop, ownership, leak, literal, upsert]
category: memory
---

# A Map's value column owns what it holds, promises included

## Documentation

A `Map`'s value column is an `Array` of the value type inside `stdlib/Map.maxon`, so a value that owes a drop
without being a refcounted box (a `Promise`, whose green thread only `__gt_promise_drop` reclaims) is dropped
by the map exactly as an array drops it, and a record or union that holds a promise is dropped through its own
destructor. The `[key: value]` literal desugars to `Map.create()` plus one `upsert` per pair, so a pair is
stored by the same call a written `upsert` makes, and the map owns its value however the pair was written.

Every case below stores one promise, directly or inside a record or union, in a map that is never awaited
from, and expects exit 0: a missed drop of a promise is the green-thread leak abort (75), and a missed drop of
a heap value is the heap leak gate (101). The top-level cases remove the entry while `main` still runs, which
is where the promises' own strand drops them.

## Tests

<!-- test: a-map-upserted-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = PromiseMap.create()
	held.upsert("a", value: async work(1))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-map-literal-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	let held = ["a": async work(1)]

	if held.count() == 1 'one'
		return 0
	end 'one'

	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-map-literal-bound-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	let pending = async work(1)
	let held = ["a": pending]

	if held.count() == 1 'one'
		return 0
	end 'one'

	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-map-upserted-record-holding-a-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Job
	var running as IntPromise

	static function start(running IntPromise) returns Self
		return Self{running: running}
	end 'start'
end 'Job'

typealias JobMap = Map with (String, Job)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = JobMap.create()
	held.upsert("a", value: Job.start(async work(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-map-upserted-union-carrying-a-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Slot
	running(job IntPromise)
	idle
end 'Slot'

typealias SlotMap = Map with (String, Slot)

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = SlotMap.create()
	held.upsert("a", value: Slot.running(async work(1)))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-map-literal-record-holding-a-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Job
	var running as IntPromise

	static function start(running IntPromise) returns Self
		return Self{running: running}
	end 'start'
end 'Job'

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	let held = ["a": Job.start(async work(1))]

	if held.count() == 1 'one'
		return 0
	end 'one'

	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-map-literal-union-carrying-a-promise-is-reclaimed-at-scope-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Slot
	running(job IntPromise)
	idle
end 'Slot'

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	let held = ["a": Slot.running(async work(1))]

	if held.count() == 1 'one'
		return 0
	end 'one'

	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-map-of-promises-reclaims-a-removed-entry -->
<!-- process: own -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias PromiseMap = Map with (String, IntPromise)

var shelf = PromiseMap.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	shelf.upsert("a", value: async work(1))

	if shelf.remove("a") 'removed'
		return 0
	end 'removed'

	return 2
end 'main'
```
```exitcode
0
```
