---
feature: array-element-drop-stamp
status: stable
keywords: [array, element, destructor, drop, promise, global, top-level, leak, ownership]
category: memory
---

# An array's element destructor is the same whether the array is local or top-level

## Documentation

An `Array`'s record is born holding the destructor its teardown walk calls on each live element. The same
element type gets the same destructor wherever the array is created: in a function body, or as a
top-level `var` whose record `__module_init` builds before `main`. An element that owes a drop without being
a refcounted box (a `Promise`, whose green thread only `__gt_promise_drop` reclaims) is dropped by both, so
`clear()` on a top-level array of promises reclaims every un-awaited element exactly as it does on a local
array.

Every case below holds one element that is never consumed, in a top-level array and in a local array of the
same element type, and expects exit 0: a missed drop of a promise is the green-thread leak abort (75), and a
missed drop of a managed element is the heap leak gate (101). The promise cases clear the array while `main`
still runs, which is where the promises' own strand drops them.

## Tests

<!-- test: a-top-level-array-of-promises-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

var held = IntPromiseArray.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	held.push(async work(1))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-promises-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = IntPromiseArray.create()
	held.push(async work(1))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-throwing-promises-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias CheckedPromise = Promise with (Integer, WorkError)
typealias CheckedPromiseArray = Array with CheckedPromise

enum WorkError implements Error
	failed
end 'WorkError'

var held = CheckedPromiseArray.create()

function work(n Integer) returns Integer throws WorkError
	Scheduler.yield()

	if n < 0 'negative'
		throw WorkError.failed
	end 'negative'

	return n
end 'work'

function main() returns ExitCode
	held.push(async work(1))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-throwing-promises-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias CheckedPromise = Promise with (Integer, WorkError)
typealias CheckedPromiseArray = Array with CheckedPromise

enum WorkError implements Error
	failed
end 'WorkError'

function work(n Integer) returns Integer throws WorkError
	Scheduler.yield()

	if n < 0 'negative'
		throw WorkError.failed
	end 'negative'

	return n
end 'work'

function main() returns ExitCode
	var held = CheckedPromiseArray.create()
	held.push(async work(1))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-records-holding-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Job
	var running as IntPromise

	static function start(running IntPromise) returns Self
		return Self{running: running}
	end 'start'
end 'Job'

typealias JobArray = Array with Job

var held = JobArray.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	held.push(Job.start(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-records-holding-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Job
	var running as IntPromise

	static function start(running IntPromise) returns Self
		return Self{running: running}
	end 'start'
end 'Job'

typealias JobArray = Array with Job

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = JobArray.create()
	held.push(Job.start(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-generic-instances-over-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Box uses Item
	var item as Item

	static function wrap(item Item) returns Self
		return Self{item: item}
	end 'wrap'
end 'Box'

typealias PromiseBox = Box with IntPromise
typealias PromiseBoxArray = Array with PromiseBox

var held = PromiseBoxArray.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	held.push(PromiseBox.wrap(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-generic-instances-over-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

type Box uses Item
	var item as Item

	static function wrap(item Item) returns Self
		return Self{item: item}
	end 'wrap'
end 'Box'

typealias PromiseBox = Box with IntPromise
typealias PromiseBoxArray = Array with PromiseBox

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = PromiseBoxArray.create()
	held.push(PromiseBox.wrap(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-unions-carrying-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Slot
	running(job IntPromise)
	idle
end 'Slot'

typealias SlotArray = Array with Slot

var held = SlotArray.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	held.push(Slot.running(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-unions-carrying-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

union Slot
	running(job IntPromise)
	idle
end 'Slot'

typealias SlotArray = Array with Slot

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = SlotArray.create()
	held.push(Slot.running(async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-tuples-holding-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias Entry = (Integer, IntPromise)
typealias EntryArray = Array with Entry

var held = EntryArray.create()

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	held.push((1, async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-tuples-holding-a-promise-reclaims-an-unawaited-element -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias Entry = (Integer, IntPromise)
typealias EntryArray = Array with Entry

function work(n Integer) returns Integer
	Scheduler.yield()
	return n
end 'work'

function main() returns ExitCode
	var held = EntryArray.create()
	held.push((1, async work(1)))
	held.clear()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-records-holding-a-string-releases-its-element -->
```maxon
type Label
	var text as String

	static function named(text String) returns Self
		return Self{text: text}
	end 'named'
end 'Label'

typealias LabelArray = Array with Label

var held = LabelArray.create()

function main() returns ExitCode
	held.push(Label.named("held {1 + 1}"))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-local-array-of-records-holding-a-string-releases-its-element -->
```maxon
type Label
	var text as String

	static function named(text String) returns Self
		return Self{text: text}
	end 'named'
end 'Label'

typealias LabelArray = Array with Label

function main() returns ExitCode
	var held = LabelArray.create()
	held.push(Label.named("held {1 + 1}"))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-top-level-array-of-inline-records-owns-nothing-to-release -->
```maxon
typealias Small = int(0 to 255)

type Cell
	let low as Small
	let high as Small

	static function of(low Small, high Small) returns Self
		return Self{low: low, high: high}
	end 'of'

	function total() returns Small
		return (self.low + self.high) as Small
	end 'total'
end 'Cell'

typealias CellArray = Array with Cell

var held = CellArray.create()

function main() returns ExitCode
	held.push(Cell.of(3, high: 4))

	let cell = try held.get(0) otherwise panic("one was pushed")
	return cell.total() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-local-array-of-inline-records-owns-nothing-to-release -->
```maxon
typealias Small = int(0 to 255)

type Cell
	let low as Small
	let high as Small

	static function of(low Small, high Small) returns Self
		return Self{low: low, high: high}
	end 'of'

	function total() returns Small
		return (self.low + self.high) as Small
	end 'total'
end 'Cell'

typealias CellArray = Array with Cell

function main() returns ExitCode
	var held = CellArray.create()
	held.push(Cell.of(3, high: 4))

	let cell = try held.get(0) otherwise panic("one was pushed")
	return cell.total() as ExitCode
end 'main'
```
```exitcode
7
```
