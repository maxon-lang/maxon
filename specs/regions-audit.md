---
feature: regions-audit
status: experimental
keywords: [modify, window, interface, function value, send, immutable, spawn, capture, indirect call]
category: semantics
---

# Stage B soundness audit

## Documentation

Each case below is a program the stage-B audit found accepted where the rulings refuse it. Nested `modify`
windows keep every enclosing window's root read-only; a result through an interface requirement or a function
value is read-only; a direct send that waits without end is refused inside a window; an `immutable T` parameter
refuses a mutable argument; a call through a function value feeds the caller's may-yield bit; a `spawn`
factory's arguments are checked against its parameters; and a closure that captures a parameter pins it
mutable-only.

## Tests

<!-- test: freezing-an-enclosing-windows-root-inside-a-nested-window-is-refused -->
`lines` is open in `'outer'`, so inside `'inner'` it is outside the open block and read-only, and `freeze` needs a writable holder.

```maxon
typealias LineArray = Array with String

function f(lines isolated LineArray, other isolated LineArray)
	modify lines 'outer'
		modify other 'inner'
			_ = freeze lines
		end 'inner'
	end 'outer'
end 'f'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:7:15: 'lines' is used as mutable; freeze a copy: `freeze lines.clone()`
```

<!-- test: writing-an-enclosing-windows-root-inside-a-nested-window-is-refused -->
`lines` is open in `'outer'`, so a push through it inside `'inner'` writes data outside the open block.

```maxon
typealias LineArray = Array with String

function f(lines isolated LineArray, other isolated LineArray)
	modify lines 'outer'
		modify other 'inner'
			lines.push("x")
		end 'inner'
	end 'outer'
end 'f'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:7:10: cannot write through a read-only value while 'inner' is open
```

<!-- test: a-result-through-an-interface-requirement-is-read-only -->
`h.part()` answers the interface value's own data, and `h` is inferred read-only for a frozen `Shelf`, so writing through the result is refused.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

interface Holder
	function part() returns Part
end 'Holder'

type Shelf implements Holder
	var held as Part

	static function create() returns Self
		return Self{held: Part.create()}
	end 'create'

	function part() returns Part
		return self.held
	end 'part'
end 'Shelf'

function poke(h Holder)
	var p = h.part()
	p.n = 1
end 'poke'

function main() returns ExitCode
	var s = freeze Shelf.create()
	poke(s)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:35:7: 'poke' writes 'h' (line 30: `p.n = 1`); 's' is immutable
```

<!-- test: a-direct-send-that-waits-without-end-inside-a-window-is-refused -->
`h.fill(sendTimeout: ForeverMs)` waits for room in a full mailbox, so it may wait while `batch` is open.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

type Store
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function fill()
		self.n = self.n + 1
	end 'fill'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'edit'
		try h.fill(sendTimeout: ForeverMs) otherwise panic("a send that waits without end returns only when it is delivered or the service stops")
		batch.push("sent")
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:22:7: cannot wait inside 'edit'; another coroutine could run while 'batch' is open. Await before or after the block
```

<!-- test: a-mutable-argument-to-an-immutable-type-parameter-is-refused -->
`look` declares `immutable T`, so a record another name may still write cannot be handed to it.

```maxon
typealias Tally = int(0 to u64.max)

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function look(_ immutable T) uses T returns Tally
	return 1
end 'look'

function main() returns ExitCode
	var row = Row.create(1)
	print("{look(row)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:18:15: 'look' takes `immutable` '_'; 'row' is mutable. Freeze it first
```

<!-- test: a-spawn-argument-that-is-mutable-to-an-immutable-factory-parameter-is-refused -->
`Sorter.create` declares its parameter `immutable`, so `spawn` refuses a mutable record, as a call does.

```maxon
typealias Tally = int(0 to u64.max)

type Settings
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Settings'

type Sorter
	var settings as immutable Settings

	static function create(settings immutable Settings) returns Self
		return Self{settings: settings}
	end 'create'

	export function ping() returns Tally
		return self.settings.n
	end 'ping'
end 'Sorter'

function main() returns ExitCode
	var s = Settings.create(1)
	let h = spawn Sorter.create(s)
	h.shutdown()
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:30: 'create' takes `immutable` 'settings'; 's' is mutable. Freeze it first
```

<!-- test: a-closure-that-captures-a-parameter-pins-it-mutable-only -->
`wrap` captures its parameter `r` in a closure, so `r` is mutable only and the frozen row is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)
typealias Reader = function() returns Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function wrap(r Row) returns Reader
	return function() gives r.n
end 'wrap'

function main() returns ExitCode
	var frozen = freeze Row.create(1)
	let read = wrap(frozen)
	print("{read()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:19:18: 'wrap' writes 'r' (line 14: `return function() gives r.n`); 'frozen' is immutable
```

<!-- test: a-direct-send-with-a-finite-timeout-inside-a-window-is-refused -->
Any nonzero `sendTimeout:` waits on another party for room in the mailbox, so a direct send with a finite one is refused inside the window too.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

type Store
	var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function fill()
		self.n = self.n + 1
	end 'fill'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	var batch = LineArray.create(0) as isolated LineArray

	modify batch 'edit'
		try h.fill(sendTimeout: 50) otherwise panic("a send that waits for room returns when it is delivered or the timeout passes")
		batch.push("sent")
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:22:7: cannot wait inside 'edit'; another coroutine could run while 'batch' is open. Await before or after the block
```

<!-- test: a-call-through-a-function-value-that-yields-inside-a-window-is-refused -->
`run` is a closure whose body calls a function that sleeps, so calling it inside the block lets a sibling coroutine run while `batch` is open.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

function slow() returns Tally
	sleep(1)
	return 1
end 'slow'

function pause() returns Tally
	let later = async slow()
	return await later
end 'pause'

function main() returns ExitCode
	var batch = LineArray.create(0) as isolated LineArray
	let run = function() gives pause()

	modify batch 'edit'
		let n = run()
		batch.push("{n}")
	end 'edit'

	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:20:14: cannot call the closure at <fragment>:17:12 inside 'edit'; it may yield (it calls 'pause', which it awaits a coroutine at line 12). Call it before or after the block
```

<!-- test: a-result-through-a-function-value-is-read-only -->
`getPart()` answers through a function value, so its result is never fresh and cannot be written when the function value was built over frozen data.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartGetter = function() returns Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

function poke(getPart PartGetter)
	var p = getPart()
	p.n = 1
end 'poke'

function main() returns ExitCode
	let frozen = freeze Part.create()
	poke(function() gives frozen)
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:20:7: 'poke' writes 'getPart' (line 15: `p.n = 1`); 'function() gives frozen' is immutable
```


<!-- test: a-result-through-a-function-value-over-mutable-captures-is-writable -->
A closure over mutable data hands its result back mutable, so the function-typed parameter that is written through is mutable-only and the mutable closure meets it.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartGetter = function() returns Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

function poke(getPart PartGetter) returns Tally
	var p = getPart()
	p.n = 5

	return p.n
end 'poke'

function main() returns ExitCode
	var held = Part.create()
	print("{poke(function() gives held)}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-result-through-a-function-value-that-captures-nothing-is-writable -->
A closure that captures nothing is a clean value, so it meets a function-typed parameter that is written through.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartMaker = function() returns Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

function poke(make PartMaker) returns Tally
	var p = make()
	p.n = 7

	return p.n
end 'poke'

function main() returns ExitCode
	print("{poke(function() gives Part.create())}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-frozen-function-value-is-read-through-a-function-typed-parameter -->
A function-typed parameter that is only read through accepts a closure over frozen data.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartGetter = function() returns Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 3}
	end 'create'
end 'Part'

function look(getPart PartGetter) returns Tally
	let p = getPart()

	return p.n
end 'look'

function main() returns ExitCode
	let frozen = freeze Part.create()
	print("{look(function() gives frozen)}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-result-stored-through-a-function-typed-parameter-pins-it-mutable-only -->
`collect` stores what the function value hands back into a mutable array, so a closure over frozen data is refused at the call.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartGetter = function() returns Part
typealias PartArray = Array with Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

function collect(getPart PartGetter) returns PartArray
	var parts = PartArray.create(0)
	parts.push(getPart())

	return parts
end 'collect'

function main() returns ExitCode
	let frozen = freeze Part.create()
	let parts = collect(function() gives frozen)
	print("{parts.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:22: 'collect' writes 'getPart' (line 16: `parts.push(getPart())`); 'function() gives frozen' is immutable
```

<!-- test: a-by-reference-argument-is-not-a-clean-input-to-freeze -->
`pin` takes `part` by reference, so `Wrapper.around(part)` is not built from clean inputs and cannot be frozen.

```maxon
typealias Tally = int(0 to u64.max)

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

type Wrapper
	export var part as Part

	static function around(part Part) returns Self
		return Self{part: part}
	end 'around'
end 'Wrapper'

function pin(part mutable Part) returns Tally
	let frozen = freeze Wrapper.around(part)
	part.n = 9

	return frozen.part.n
end 'pin'

function main() returns ExitCode
	var part = Part.create()
	print("{pin(part)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:21:22: 'Wrapper.around(part)' is used as mutable; freeze a copy: `freeze Wrapper.around(part).clone()`
```

<!-- test: a-promise-whose-payload-is-not-sendable-is-not-a-clean-input -->
`Holder` holds a promise of a function value, which cannot cross a thread, so a record built from it cannot be frozen.

```maxon
typealias Tally = int(0 to u64.max)
typealias Maker = function() returns Tally
typealias MakerPromise = Promise with Maker

type Holder
	export var pending as MakerPromise

	static function around(pending MakerPromise) returns Self
		return Self{pending: pending}
	end 'around'
end 'Holder'

function make() returns Maker
	sleep(1)
	return function() gives 4
end 'make'

function main() returns ExitCode
	let pending = async make()
	let held = freeze Holder.around(pending)
	print("{held.pending}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3138: <fragment>:21:13: a `freeze` is a `Holder` whose graph reaches a promise, which owns a green thread that exactly one owner may reclaim. A send hands the record over WHOLE, and this compiler walks the graph below it at run time, immediately before the send — but it can only walk a graph whose every type has a per-type cascade, and this one does not. Send the scalars the value is built from, or keep it on this side and send what the service needs of it
```
