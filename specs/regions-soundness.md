---
feature: regions-soundness
status: experimental
keywords: [region, soundness, result, settled, join, immutable, viewpoint, async, tied, modify, unique]
category: semantics
---

# Soundness of the inferred capability checker

## Documentation

A call result sourced from an unmarked parameter is judged by what the callee's masks settle, at every consumer: a
store, a constructor field, a send, a capture and a `returns` clause, as well as a call argument. The value the parser
holds before the masks are known is never more permissive than the settled one. Data of an `immutable` slot stays
immutable whatever the receiver. An `async` call whose argument carries the caller's mutable data is tied whatever the
argument's spelling. A nested `modify` over another field demotes every enclosing window's root.

A runtime function is a clean input only once it is audited as handing back memory nothing else reaches:
`__ManagedMemory.create` and the intrinsics that answer an owned buffer are, so `ByteArray.init` over one is
returned as `isolated` without a copy; `slice`, which may view the buffer it is given, is not, and a buffer
bound by a plain `let` is ordinary mutable data. These cases compile in the library tier
(`<!-- tier: stdlib -->`); `specs/subprocess-builtins.md` pins the same for `subprocessReadAvailable`.

## Tests

<!-- test: a-field-store-of-a-call-result-sourced-from-a-parameter-pins-the-parameter -->
`pick` may hand back `p`, so storing its result into a mutable field makes `p` mutable only and the frozen argument is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder
	export var row as Box

	static function create() returns Self
		return Self{row: Box.create(0)}
	end 'create'
end 'Holder'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function keep(h Holder, p Box)
	h.row = pick(p, fresh: false)
end 'keep'

function main() returns ExitCode
	var frozen = freeze Box.create(4)
	var h = Holder.create()
	keep(h, p: frozen)
	h.row.n = 9
	print("{frozen.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:35:10: 'keep' writes 'p' (line 29: a result read from it is stored in a writable slot (row)); 'frozen' is immutable
```

<!-- test: a-returns-mutable-clause-refuses-a-call-result-sourced-from-a-parameter -->
`relay` declares a mutable result but returns what `pick` may hand back from its parameter, so the return is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function relay(p Box) returns mutable Box
	return pick(p, fresh: false)
end 'relay'

function viaParam(q Box)
	var r = relay(q)
	r.n = 5
end 'viaParam'

function main() returns ExitCode
	var frozen = freeze Box.create(7)
	viaParam(frozen)
	print("{frozen.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:21:2: a return does not fit the declared result: the returned value is a parameter's data, the result is mutable
error E3207: <fragment>:31:11: 'viaParam' writes 'q'; 'frozen' is immutable
```

<!-- test: a-mixed-result-given-a-frozen-argument-is-not-immutable-for-an-immutable-field -->
`choose` hands back `p` or a box it also pushed into `spare`, so given a frozen `p` its result is read-only, not immutable, and cannot enter an `immutable` field.

```maxon
typealias Tally = int(0 to 1024)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function choose(p Box, spare BoxArray, useP bool) returns Box
	if useP 'direct'
		return p
	end 'direct'

	var extra = Box.create(0)
	extra.n = 2
	spare.push(extra)
	return extra
end 'choose'

type Pinned
	var box as immutable Box

	static function build(first immutable Box) returns Self
		return Self{box: first}
	end 'build'

	function adopt(frozen immutable Box, spare BoxArray)
		self.box = choose(frozen, spare: spare, useP: false)
	end 'adopt'

	function value() returns Tally
		return self.box.n
	end 'value'
end 'Pinned'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	var spare = BoxArray.create(0)
	var pinned = Pinned.build(frozen)
	pinned.adopt(frozen, spare: spare)
	var alias = try spare.get(0) otherwise panic("main: choose pushed one box")
	alias.n = 7
	print("{pinned.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:32:14: cannot store a mutable 'Box' into 'box', which holds an immutable 'Box'; freeze it
```

<!-- test: a-mixed-join-stored-into-an-immutable-field-is-refused -->
A join of a parameter with fresh data is mutable data, so it cannot enter an `immutable` field, and the caller's mutable box stays unaliased by the field.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	var held as immutable Box

	static function build(item immutable Box) returns Self
		return Self{held: item}
	end 'build'

	function put(p Box, flag bool)
		self.held = p if flag else Box.create(1)
	end 'put'

	function value() returns Tally
		return self.held.n
	end 'value'
end 'Shelf'

function main() returns ExitCode
	var loose = Box.create(1)
	var frozen = freeze Box.create(0)
	var s = Shelf.build(frozen)
	s.put(loose, flag: true)
	loose.n = 9
	print("{s.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:20:15: cannot store a mutable 'Box' into 'held', which holds an immutable 'Box'; freeze it
```

<!-- test: data-of-an-immutable-slot-returned-by-a-plain-result-stays-immutable -->
`part` returns data of an `immutable` slot; the result stays immutable whatever the receiver, so writing through it is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Shelf
	var held as immutable Box

	static function build(item immutable Box) returns Self
		return Self{held: item}
	end 'build'

	function part() returns Box
		return self.held
	end 'part'
end 'Shelf'

function main() returns ExitCode
	var pinned = freeze Box.create(1)
	var s = Shelf.build(pinned)
	var p = s.part()
	p.n = 9
	print("{pinned.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3209: <fragment>:20:10: a return does not fit the declared result: 'self.held' is immutable, the result is not `immutable`; declare the result `immutable`
```

<!-- test: an-async-over-an-expression-carrying-mutable-data-is-tied -->
`same(row)` is the caller's own record, so the promise is tied and cannot be returned.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyPromise = Promise with Tally

type Row
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function bump(row Row) returns Tally
	sleep(1)
	row.n = row.n + 1
	return row.n
end 'bump'

function same(row Row) returns Row
	return row
end 'same'

function start(row Row) returns TallyPromise
	return async bump(same(row))
end 'start'

function main() returns ExitCode
	var row = Row.create(1)
	let later = start(row)
	let n = await later
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:24:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-nested-modify-over-another-field-demotes-the-enclosing-field-window -->
Inside `'inner'` the enclosing `self.left` is read-only.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	var left as isolated Box
	var right as isolated Box

	static function create() returns Self
		return Self{left: Box.create(0), right: Box.create(0)}
	end 'create'

	function both()
		let spare = Box.create(0) as isolated Box
		modify self.left 'outer'
			modify spare 'inner'
				self.left.n = 5
			end 'inner'
		end 'outer'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:24:5: cannot write 'n' through 'self.left', which is read-only while 'inner' is open
```

<!-- test: a-unique-method-that-keeps-its-receiver-moves-the-receiver -->
`stash` stores `self`, so the call moves `ticket` and the read after it is refused.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function stash(into TokenArray)
		into.push(self)
	end 'stash'
end 'Token'

typealias TokenArray = Array with Token

function main() returns ExitCode
	let ticket = Token.create(1)
	let box = TokenArray.create(0)
	ticket.stash(box)
	print("{ticket.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:22:10: use of moved value 'ticket': its ownership moved to the parameter 'self' of 'Token.stash', which keeps it
```

<!-- test: starting-an-async-call-inside-a-modify-window-is-refused -->
An `async` start inside `'window'` is refused where it is written, whatever the callee does: a coroutine started there could run while `box` is open.

```maxon
typealias Tally = int(0 to 1024)
typealias TallyPromise = Promise with Tally

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function spin() returns Tally
	Scheduler.yield()
	return 1
end 'spin'

function main() returns ExitCode
	var box = Box.create(0) as isolated Box

	modify box 'window'
		let later = async spin()
		box.n = 2
		_ = await later
	end 'window'

	print("{box.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3205: <fragment>:22:15: cannot wait inside 'window'; another coroutine could run while 'box' is open. Await before or after the block
```

<!-- test: a-constructor-field-fed-a-call-result-holds-the-parameter-it-may-hand-back -->
`wrap` returns a `Wrapper` over what `pick` may hand back from `p`, so the result follows `p`: the frozen argument gives a read-only `Wrapper` and `main` reads it.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrapper
	export var row as Box

	static function create(row Box) returns Self
		return Self{row: row}
	end 'create'
end 'Wrapper'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function wrap(p Box) returns Wrapper
	return Wrapper.create(pick(p, fresh: false))
end 'wrap'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	let wrapper = wrap(frozen)
	print("{wrapper.row.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-write-through-a-wrapper-built-over-a-call-result-of-a-frozen-argument-is-refused -->
The `Wrapper` holds what `pick` may hand back from the frozen `p`, so writing through it writes frozen data.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrapper
	export var row as Box

	static function create(row Box) returns Self
		return Self{row: row}
	end 'create'
end 'Wrapper'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function wrap(p Box) returns Wrapper
	return Wrapper.create(pick(p, fresh: false))
end 'wrap'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	let wrapper = wrap(frozen)
	wrapper.row.n = 5
	print("{wrapper.row.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:35:2: cannot write 'n' through 'wrapper.row', which is read-only
```

<!-- test: an-immutable-parameter-fed-a-call-result-pins-the-parameter-it-may-hand-back -->
`take` declares `immutable`, and `pick` may hand back `p`, a parameter `relay` has not decided is frozen, so the result is read-only and `take` refuses it.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box) returns Box
	return p
end 'pick'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function relay(p Box) returns Tally
	return take(pick(p))
end 'relay'

function main() returns ExitCode
	var box = Box.create(1)
	print("{relay(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'pick(p)' is read-only
error E3207: <fragment>:26:16: 'relay' needs 'p' frozen (it calls 'take', which takes `immutable` 'item'); 'box' is mutable. Freeze it first
```

<!-- test: an-immutable-parameter-fed-a-call-result-is-read-only-even-for-a-frozen-argument -->
`pick` may hand back `p`, which `relay` has not decided is frozen, so its result is read-only to `take`'s `immutable` parameter whatever `main` passes.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(p Box) returns Box
	return p
end 'pick'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function relay(p Box) returns Tally
	return take(pick(p))
end 'relay'

function main() returns ExitCode
	let box = freeze Box.create(1)
	print("{relay(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'pick(p)' is read-only
```

<!-- test: a-closure-that-writes-a-captured-call-result-pins-the-parameter-it-may-hand-back -->
`step` writes `r`, which `pick` may take from `p`, so `relay` needs `p` writable and the frozen argument is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump() returns Tally
		self.n = self.n + 1
		return self.n
	end 'bump'
end 'Box'

function pick(p Box, fresh bool) returns Box
	if fresh 'makeOne'
		return Box.create(0)
	end 'makeOne'

	return p
end 'pick'

function relay(p Box) returns Tally
	var r = pick(p, fresh: false)
	let step = function() gives r.bump()
	return step()
end 'relay'

function main() returns ExitCode
	var frozen = freeze Box.create(1)
	print("{relay(frozen)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:33:16: 'relay' writes 'p' (line 27: a result read from it is stored in a writable slot (r)); 'frozen' is immutable
```

<!-- test: a-mixed-join-pushed-into-an-array-of-immutable-elements-is-refused -->
`FrozenBoxes` holds `immutable Box`, and `p if flag else Box.create(1)` may be the caller's mutable `p`, so the push is refused.

```maxon
typealias Tally = int(0 to 1024)
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function stash(frozens FrozenBoxes, p Box, flag bool)
	frozens.push(p if flag else Box.create(1))
end 'stash'

function main() returns ExitCode
	var frozens = FrozenBoxes.create(0)
	var loose = Box.create(1)
	stash(frozens, p: loose, flag: true)
	loose.n = 9
	print("{frozens.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3196: <fragment>:14:15: cannot store a mutable 'Box' into 'an element of the container push writes', which holds an immutable 'Box'; freeze it
```

<!-- test: a-function-value-result-stored-into-an-immutable-field-is-refused -->
`getPart()` may hand back a record a mutable closure holds, so it cannot enter `Pinned`'s `immutable` field, and the closure over mutable `held` is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias PartGetter = function() returns Part

type Part
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Part'

type Pinned
	var part as immutable Part

	static function create(part immutable Part) returns Self
		return Self{part: part}
	end 'create'

	function adopt(getPart PartGetter)
		self.part = getPart()
	end 'adopt'

	function value() returns Tally
		return self.part.n
	end 'value'
end 'Pinned'

function main() returns ExitCode
	let frozen = freeze Part.create()
	var pinned = Pinned.create(frozen)
	var held = Part.create()
	pinned.adopt(function() gives held)
	print("{pinned.value()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:33:15: 'adopt' needs 'getPart' frozen (line 21: `self.part = getPart()`); the closure at <fragment>:33:15 is mutable. Freeze it first
```

<!-- test: an-async-over-a-clone-of-mutable-data-is-not-tied -->
`items.clone()` is a deep copy that shares nothing with `items`, so the promise keeps no hold on the caller's data and may be stored in an array while the caller goes on writing `items`.

```maxon
typealias Tally = int(0 to 1024)
typealias TallyArray = Array with Tally
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

function countAfterYield(items TallyArray) returns Tally
	sleep(1)

	return items.count() as Tally
end 'countAfterYield'

function main() returns ExitCode
	let items = TallyArray.create(0)
	items.push(1)

	let promises = TallyPromiseArray.create(0)
	promises.push(async countAfterYield(items.clone()))
	items.push(2)
	items.push(3)

	for promise in promises 'each'
		print("{await promise}\n")
	end 'each'

	print("{items.count()}\n")
	return 0
end 'main'
```
```stdout
1
3
```
```exitcode
0
```

<!-- test: an-async-over-a-shared-value-clone-is-not-tied -->
A `SharedValue` is a handle every thread reads without a lock, so the coroutine holds no mutable data of the caller and its promise may be stored.

```maxon
typealias Flag = SharedValue with bool
typealias FlagPromise = Promise with bool
typealias FlagPromiseArray = Array with FlagPromise

function waitForFlag(flag Flag) returns bool
	while not flag.current() 'polling'
		sleep(1)
	end 'polling'

	return true
end 'waitForFlag'

function main() returns ExitCode
	let flag = Flag.create(false)
	let promises = FlagPromiseArray.create(0)
	promises.push(async waitForFlag(flag.clone()))
	flag.publish(true)

	for promise in promises 'each'
		print("{await promise}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: a-declared-clone-that-shares-data-is-not-a-fresh-copy -->
`Ledger.clone` is the author's own and hands back the same array, so its result is not a deep copy: freezing it would freeze data `live` still writes.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Ledger
	export var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'

	function clone() returns Self
		return Self{items: self.items}
	end 'clone'
end 'Ledger'

function main() returns ExitCode
	var live = Ledger.create()
	let sealed = freeze live.clone()
	live.items.push(1)
	print("{sealed.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:19:22: 'live.clone()' is used as mutable; freeze a copy: `freeze live.clone().clone()`
```

<!-- test: a-function-value-clone-of-a-capturing-closure-is-not-a-fresh-copy -->
<!-- unsupported-targets: wasm32-wasi -->
`measure.clone()` holds the same closure, which captured the caller's `counter`, so the coroutine started over it still holds the caller's data and its promise is tied.

```maxon
typealias Tally = int(0 to u64.max)
typealias Measure = function() returns Tally
typealias TallyPromise = Promise with Tally

type Counter
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function later(measure Measure) returns Tally
	sleep(1)

	return measure()
end 'later'

function start(counter Counter) returns TallyPromise
	let measure = function() gives counter.n

	return async later(measure.clone())
end 'start'

function main() returns ExitCode
	var counter = Counter.create(1)
	let pending = start(counter)
	counter.n = 5
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:23:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-closure-over-the-callers-data-handed-to-a-coroutine-ties-its-promise -->
<!-- unsupported-targets: wasm32-wasi -->
The closure reads the caller's `counter`, which is not a clean input, so the promise of the `async` call is tied and may not be returned.

```maxon
typealias Tally = int(0 to u64.max)
typealias Measure = function() returns Tally
typealias TallyPromise = Promise with Tally

type Counter
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function later(measure Measure) returns Tally
	sleep(1)

	return measure()
end 'later'

function start(counter Counter) returns TallyPromise
	return async later(function() gives counter.n)
end 'start'

function main() returns ExitCode
	var counter = Counter.create(1)
	let pending = start(counter)
	counter.n = 5
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:21:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-closure-over-a-value-handed-to-a-coroutine-stays-untied -->
<!-- unsupported-targets: wasm32-wasi -->
The closure captures only a value, so nothing the caller can write reaches the coroutine, and the promise may be returned.

```maxon
typealias Tally = int(0 to u64.max)
typealias Measure = function() returns Tally
typealias TallyPromise = Promise with Tally

function later(measure Measure) returns Tally
	sleep(1)

	return measure()
end 'later'

function start(base Tally) returns TallyPromise
	return async later(function() gives base + 1)
end 'start'

function main() returns ExitCode
	let pending = start(4)
	let n = await pending
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

<!-- test: an-isolated-sibling-of-an-open-field-window-is-read-only-inside-it -->
`modify self.left` opens one isolated field; `self.right` is another region the window does not hold, so writing it inside the window is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Pair
	var left as isolated Box
	var right as isolated Box

	static function create() returns Self
		return Self{left: Box.create(0), right: Box.create(0)}
	end 'create'

	function both()
		modify self.left 'window'
			self.right.n = 5
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:22:4: 'right' cannot be opened: the value that holds it is read-only while 'window' is open
```

<!-- test: a-call-open-of-a-sibling-isolated-field-inside-a-window-is-refused -->
`modify self.left` opens one isolated field; calling a mutating method on `self.right` would open a second region the window does not hold, so it is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Box'

type Pair
	var left as isolated Box
	var right as isolated Box

	static function create() returns Self
		return Self{left: Box.create(0), right: Box.create(0)}
	end 'create'

	function both()
		modify self.left 'window'
			self.right.bump()
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:26:14: 'bump' needs a writable receiver at line 12; the argument is read-only while 'window' is open
```

<!-- test: a-field-path-window-of-two-levels-is-written-through-its-own-path -->
`modify self.holder.part` opens a path two fields deep; the write inside names the same path again, so it is the opened region and is accepted.

```maxon
typealias Tally = int(0 to 1024)

type Part
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Part'

type Holder
	export var part as isolated Part

	static function create() returns Self
		return Self{part: Part.create(0)}
	end 'create'
end 'Holder'

type Outer
	var holder as Holder

	static function create() returns Self
		return Self{holder: Holder.create()}
	end 'create'

	function bump() returns Tally
		modify self.holder.part 'window'
			self.holder.part.n = 5
		end 'window'

		return self.holder.part.n
	end 'bump'
end 'Outer'

function main() returns ExitCode
	let outer = Outer.create()
	print("{outer.bump()}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-call-result-of-an-undecided-source-pushed-into-a-plain-array-is-judged -->
`same(box)` hands `box` back, and `items.push` stores it into an array whose elements are plain, so `keep` needs `box` mutable and the frozen argument is refused.

```maxon
typealias Tally = int(0 to u64.max)
typealias BoxArray = Array with Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function same(box Box) returns Box
	return box
end 'same'

function keep(box Box, items BoxArray)
	items.push(same(box))
end 'keep'

function main() returns ExitCode
	var items = BoxArray.create(0)
	keep(freeze Box.create(1), items: items)
	var first = try items.pop() otherwise panic("one pushed")
	first.n = 9
	print("{first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:23:7: 'keep' writes 'box' (line 18: a result read from it is stored in a writable slot (an element of the container push writes)); 'freeze Box.create(1)' is immutable
```

<!-- test: an-immutable-only-parameter-as-the-sole-source-gives-a-read-only-result -->
`same(p)` returns only `p`, and `relay` needs `p` frozen for `take`, but the result of a call is read-only while its source is the caller's own undecided parameter, so `take`'s `immutable` parameter refuses it inside `relay`.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function same(box Box) returns Box
	return box
end 'same'

function take(item immutable Box) returns Tally
	return item.n
end 'take'

function viaParam(p Box) returns Tally
	return take(same(p))
end 'viaParam'

function main() returns ExitCode
	let box = freeze Box.create(1)
	print("{viaParam(box)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'same(p)' is read-only
```

<!-- test: a-fresh-argument-and-a-frozen-argument-mixed-in-a-return-are-refused -->
`pick` may hand back either argument, `Box.create(0)` is mutable data and `frozen` is not, so the two sources disagree.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box, first bool) returns Box
	return a if first else b
end 'pick'

function main() returns ExitCode
	let frozen = freeze Box.create(2)
	let chosen = pick(Box.create(0), b: frozen, first: false)
	print("{chosen.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3208: <fragment>:18:15: 'pick' returns 'a' or 'b'; they must both be mutable or both immutable here
```

<!-- test: a-declared-clone-result-given-to-a-coroutine-ties-its-promise -->
<!-- unsupported-targets: wasm32-wasi -->
`Ledger.clone` is declared by the author and shares `items`, so a coroutine started over its result still holds the data `live` writes: the promise is tied and may not be stored.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias TallyPromise = Promise with Tally
typealias TallyPromiseArray = Array with TallyPromise

type Ledger
	export var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'

	function clone() returns Self
		return Self{items: self.items}
	end 'clone'
end 'Ledger'

function countAfterYield(ledger Ledger) returns Tally
	sleep(1)

	return ledger.items.count() as Tally
end 'countAfterYield'

function main() returns ExitCode
	var live = Ledger.create()
	var promises = TallyPromiseArray.create(0)
	promises.push(async countAfterYield(live.clone()))
	live.items.push(1)

	for promise in promises 'each'
		print("{await promise}\n")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:28:11: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-function-parameter-forwarded-to-a-coroutine-ties-its-promise -->
<!-- unsupported-targets: wasm32-wasi -->
`start` cannot tell what `measure` captured, so a function value that is not a closure literal or a named function is not a clean input and the promise is tied.

```maxon
typealias Tally = int(0 to u64.max)
typealias Measure = function() returns Tally
typealias TallyPromise = Promise with Tally

type Counter
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function later(measure Measure) returns Tally
	sleep(1)

	return measure()
end 'later'

function start(measure Measure) returns TallyPromise
	return async later(measure)
end 'start'

function main() returns ExitCode
	var counter = Counter.create(1)
	let pending = start(function() gives counter.n)
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:21:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-named-function-handed-to-a-coroutine-stays-untied -->
<!-- unsupported-targets: wasm32-wasi -->
`seven` captures nothing, so the coroutine holds nothing the caller can write and the promise may be returned.

```maxon
typealias Tally = int(0 to u64.max)
typealias Measure = function() returns Tally
typealias TallyPromise = Promise with Tally

function later(measure Measure) returns Tally
	sleep(1)

	return measure()
end 'later'

function seven() returns Tally
	return 7
end 'seven'

function start() returns TallyPromise
	return async later(seven)
end 'start'

function main() returns ExitCode
	let pending = start()
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-write-two-fields-deep-below-a-sibling-isolated-field-is-refused-inside-a-window -->
`self.right.inner.n` reaches through the isolated `right`, which the window on `left` does not hold, so the write is refused although `inner` is a plain record.

```maxon
typealias Tally = int(0 to 1024)

type Inner
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Inner'

type Box
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(0)}
	end 'create'
end 'Box'

type Pair
	var left as isolated Box
	var right as isolated Box

	static function create() returns Self
		return Self{left: Box.create(), right: Box.create()}
	end 'create'

	function both()
		modify self.left 'window'
			self.right.inner.n = 5
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:34:4: 'right' cannot be opened: the value that holds it is read-only while 'right' is open
```

<!-- test: a-call-two-fields-deep-below-a-sibling-isolated-field-is-refused-inside-a-window -->
`self.right.inner.bump()` writes through the isolated `right`, which the window on `left` does not hold, so the call is refused.

```maxon
typealias Tally = int(0 to 1024)

type Inner
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function bump()
		self.n = self.n + 1
	end 'bump'
end 'Inner'

type Box
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create(0)}
	end 'create'
end 'Box'

type Pair
	var left as isolated Box
	var right as isolated Box

	static function create() returns Self
		return Self{left: Box.create(), right: Box.create()}
	end 'create'

	function both()
		modify self.left 'window'
			self.right.inner.bump()
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:34:20: 'bump' needs a writable receiver at line 12; the argument is read-only while 'right' is open
```

<!-- test: the-clone-of-a-type-parameter-value-is-not-a-clean-input -->
<!-- unsupported-targets: wasm32-wasi -->
`relay` is checked once for every `T`, and what the `clone` of a `T` hands back is not known to be a copy, so the promise of the `async` call over it is tied.

```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias TallyPromise = Promise with Tally

type Ledger implements Cloneable
	export var items as TallyArray

	static function create() returns Self
		return Self{items: TallyArray.create(0)}
	end 'create'

	function clone() returns Self
		return Self{items: self.items.clone()}
	end 'clone'
end 'Ledger'

function later(_ T) uses T returns Tally
	sleep(1)

	return 1
end 'later'

function relay(item T) uses T returns TallyPromise where T is Cloneable
	return async later(item.clone())
end 'relay'

function main() returns ExitCode
	var live = Ledger.create()
	let pending = relay(live)
	live.items.push(1)
	let n = await pending
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:25:2: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```

<!-- test: a-method-that-notes-its-receiver-leaves-the-local-readable -->
`note` reads its receiver and copies a value out of it, so `a` is still readable after the call.

```maxon
typealias Tally = int(0 to 1024)

type Log
	export var last as Tally

	static function create() returns Self
		return Self{last: 0}
	end 'create'
end 'Log'

type Node
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function note(into mutable Log)
		into.last = self.n
	end 'note'
end 'Node'

function main() returns ExitCode
	let a = Node.create(3)
	let log = Log.create()
	a.note(log)
	print("{a.n} {log.last}\n")
	return 0
end 'main'
```
```stdout
3 3
```
```exitcode
0
```

<!-- test: a-swap-two-fields-deep-below-a-sibling-isolated-field-is-refused-inside-a-window -->
`swap` takes `self.right.inner.slot` out of a record reached through the isolated `right`, which the window on `left` does not hold, so the swap is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Inner
	export var slot as isolated Box

	static function create() returns Self
		return Self{slot: Box.create(0)}
	end 'create'
end 'Inner'

type Mid
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Mid'

type Pair
	var left as isolated Box
	var right as isolated Mid

	static function create() returns Self
		return Self{left: Box.create(0), right: Mid.create()}
	end 'create'

	function both()
		modify self.left 'window'
			let old = swap self.right.inner.slot with Box.create(1)
			print("{old.n}\n")
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:38:14: 'right' cannot be opened: the value that holds it is read-only while 'right' is open
```

<!-- test: a-modify-two-fields-deep-below-a-sibling-isolated-field-is-refused-inside-a-window -->
`modify self.right.inner.slot` opens a region through the isolated `right`, which the window on `left` does not hold, so the inner window is refused.

```maxon
typealias Tally = int(0 to 1024)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Inner
	export var slot as isolated Box

	static function create() returns Self
		return Self{slot: Box.create(0)}
	end 'create'
end 'Inner'

type Mid
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Mid'

type Pair
	var left as isolated Box
	var right as isolated Mid

	static function create() returns Self
		return Self{left: Box.create(0), right: Mid.create()}
	end 'create'

	function both()
		modify self.left 'window'
			modify self.right.inner.slot 'inner'
				print("opened\n")
			end 'inner'
		end 'window'
	end 'both'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.both()
	return 0
end 'main'
```
```maxoncstderr
error E3199: <fragment>:38:11: 'right' cannot be opened: the value that holds it is read-only while 'right' is open
```

<!-- test: a-parameter-that-must-be-frozen-and-a-fresh-argument-give-a-read-only-result -->
`relay` must have `p` frozen because `reads` takes it `immutable`, so `pick(p, b: Box.create(0))` joins read-only data with a fresh value and answers read-only data, which is read and not refused.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box) returns Box
	return a if a.n > b.n else b
end 'pick'

function reads(item immutable Box) returns Tally
	return item.n
end 'reads'

function relay(p Box) returns Tally
	let first = reads(p)
	let chosen = pick(p, b: Box.create(0))
	return first + chosen.n
end 'relay'

function main() returns ExitCode
	let frozen = freeze Box.create(4)
	print("{relay(frozen)}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: paused-data-and-a-fresh-argument-give-a-read-only-result -->
Inside the window `other` is read-only, so `pick(other, b: Box.create(0))` joins read-only data with a fresh value and answers read-only data, which the block reads.

```maxon
typealias LineArray = Array with String
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a Box, b Box) returns Box
	return a if a.n > b.n else b
end 'pick'

function main() returns ExitCode
	let batch = LineArray.create(0) as isolated LineArray
	let other = Box.create(1)

	modify batch 'fill'
		let chosen = pick(other, b: Box.create(0))
		batch.push("{chosen.n}")
	end 'fill'

	print("{batch.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-capture-less-closure-is-a-clean-input-to-an-isolated-result -->
`function() gives 1` captures nothing, so it is a clean input and a record built around it may be returned as `isolated`.

```maxon
typealias Tally = int(0 to u64.max)
typealias Callback = function() returns Tally

type Holder
	export var n as Tally
	export var callback as Callback

	static function create() returns isolated Holder
		return Self{n: 7, callback: function() gives 1}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	print("{holder.n}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-result-joining-a-merged-isolated-parameter-with-a-frozen-one-is-read-only -->
`pick` may hand back the merged region of `a` or the frozen `b`, so its result is read-only and reading it is accepted.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, b Box, flag bool) returns Box
	return merge a if flag else b
end 'pick'

function main() returns ExitCode
	let iso = Box.create(1) as isolated Box
	let frozen = freeze Box.create(2)
	let r = pick(iso, b: frozen, flag: true)
	print("{r.n}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-result-joining-a-merged-isolated-parameter-with-a-frozen-one-refuses-a-write -->
The same result is not written through: the frozen `b` may be what came back.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(a isolated Box, b Box, flag bool) returns Box
	return merge a if flag else b
end 'pick'

function main() returns ExitCode
	let iso = Box.create(1) as isolated Box
	let frozen = freeze Box.create(2)
	var r = pick(iso, b: frozen, flag: true)
	r.n = 9
	print("{r.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:20:2: cannot write 'n' through 'r', which is read-only
```

<!-- test: error.a-refused-constructor-body-does-not-refuse-its-callers-writes -->
The literal in `Tracer.create` stores a mutable `Cols` into an `immutable` slot, which refuses the body. `build` writes through the record it gets back; that write is judged against a body that never finished, so it is not refused as well. Only the store is reported.

```maxon
type Cols
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cols'

type Deps
	export var rows as Count

	static function create() returns Self
		return Self{rows: 0}
	end 'create'

	function record()
		self.rows = self.rows + 1
	end 'record'
end 'Deps'

type Tracer
	export var deps as Deps
	export var cols as immutable Cols

	static function create(deps Deps) returns Self
		return Self{deps: deps, cols: Cols.create()}
	end 'create'
end 'Tracer'

function settle(deps Deps)
	deps.record()
end 'settle'

function build() returns Tracer
	var tracer = Tracer.create(Deps.create())
	settle(tracer.deps)
	return tracer
end 'build'

function main() returns ExitCode
	let tracer = build()
	return tracer.deps.rows as ExitCode
end 'main'
```
```maxoncstderr
error E3196: <fragment>:27:33: cannot store a mutable 'Cols' into 'Tracer.cols', which holds an immutable 'Cols'; freeze it
```

<!-- test: a-fresh-runtime-buffer-recovers-as-isolated -->
<!-- tier: stdlib -->
`__ManagedMemory.create` hands back a buffer nothing else reaches, so `ByteArray.init` over it is built from clean inputs and is returned as `isolated`: it fills an `isolated` field and is passed to an `isolated` parameter.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function reserved(n Count) returns isolated ByteArray
	return ByteArray.init(try __ManagedMemory.create(n, 1) otherwise panic("reserved: create"))
end 'reserved'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function main() returns ExitCode
	let shelf = Shelf.holding(reserved(4))

	modify shelf.bytes 'fill'
		shelf.bytes.push(7)
		shelf.bytes.push(9)
	end 'fill'

	let weighed = weigh(reserved(3))
	let first = try shelf.bytes.get(0) otherwise panic("main: get")
	print("{first} {weighed}\n")
	return 0
end 'main'
```
```stdout
7 0
```
```exitcode
0
```

<!-- test: a-runtime-buffer-bound-as-isolated-moves-into-a-fresh-array -->
<!-- tier: stdlib -->
A fresh buffer bound `as isolated` is written through one-call opens and then moved into `ByteArray.init`, so the array is returned as `isolated` without a copy.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function stamped(n Count) returns isolated ByteArray
	let raw = (try __ManagedMemory.create(n, 1) otherwise panic("stamped: create")) as isolated __ManagedMemory
	try raw.setLength(n) otherwise panic("stamped: setLength")
	try raw.setByte(0, value: 5) otherwise panic("stamped: setByte")
	return ByteArray.init(raw)
end 'stamped'

function main() returns ExitCode
	let bytes = stamped(3)
	let first = try bytes.get(0) otherwise panic("main: get")
	print("{bytes.count()} {first}\n")
	return 0
end 'main'
```
```stdout
3 5
```
```exitcode
0
```

<!-- test: error.a-managed-slice-is-not-fresh -->
<!-- tier: stdlib -->
`slice` may view the buffer it is given rather than copy it, so an array over a slice is not a fresh region even when the sliced buffer is an `isolated` parameter, and it is refused as an `isolated` result.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function sliced(raw isolated __ManagedMemory, n Count) returns isolated ByteArray
	return ByteArray.init(try raw.slice(0, n) otherwise panic("sliced: slice"))
end 'sliced'

function main() returns ExitCode
	print("0\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:6:9: 'ByteArray.init(try raw.slice(0, n) otherwise panic("sliced: slice"))' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated ByteArray`, or pass a copy
```

<!-- test: error.a-named-mutable-runtime-buffer-is-not-isolated -->
<!-- tier: stdlib -->
A fresh buffer bound by a plain `let` is ordinary mutable data of the function's region, so `ByteArray.init` over it is refused as an `isolated` result; binding it `as isolated __ManagedMemory` keeps it fresh.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function reserved(n Count) returns isolated ByteArray
	let managed = try __ManagedMemory.create(n, 1) otherwise panic("reserved: create")
	return ByteArray.init(managed)
end 'reserved'

function main() returns ExitCode
	let bytes = reserved(4)
	print("{bytes.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:7:9: 'ByteArray.init(managed)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated ByteArray`, or pass a copy
```

<!-- test: error.array-init-over-an-isolated-local-moves-it -->
`ByteArray.init(src)` builds the array over `src`'s storage, so `src` is moved into the clean call and the array it answers fills the `isolated` field. A later use of `src` is refused exactly as after `Shelf.holding(src)`: otherwise `src.push` would write into the shelf's isolated region from outside it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let src = ByteArray.create(0) as isolated ByteArray
	src.push(1)
	let shelf = Shelf.holding(ByteArray.init(src))
	src.push(2)
	src.push(3)
	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:2: use of moved value 'src': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: array-init-over-an-isolated-local-is-then-pushed -->
The move leaves the array `ByteArray.init(src)` answers as the one owner of the storage, so the new name is an ordinary array that takes its own `push` and counts the bytes that were in `src`.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	let src = ByteArray.create(0) as isolated ByteArray
	src.push(7)
	src.push(8)
	let grown = ByteArray.init(src)
	grown.push(9)
	let first = try grown.get(0) otherwise panic("main: get")
	print("{grown.count()} {first}\n")
	return 0
end 'main'
```
```stdout
3 7
```
```exitcode
0
```

<!-- test: array-init-over-an-isolated-local-then-pushed-inside-modify -->
The same array handed to an `isolated` field is pushed from inside `modify`, the one place the shelf's region may be written, and the bytes that were in `src` are still there.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let src = ByteArray.create(0) as isolated ByteArray
	src.push(7)
	src.push(8)
	let shelf = Shelf.holding(ByteArray.init(src))
	var total = 0 as Count
	var first = 0 as Byte

	modify shelf.bytes 'grow'
		shelf.bytes.push(9)
		total = shelf.bytes.count()
		first = try shelf.bytes.get(0) otherwise panic("main: get")
	end 'grow'

	print("{total} {first}\n")
	return 0
end 'main'
```
```stdout
3 7
```
```exitcode
0
```

<!-- test: error.array-init-over-an-isolated-parameter-moves-it -->
A parameter declared `isolated` is moved by `ByteArray.init` exactly as an `isolated` local is, so the function's own later `push` through the parameter is refused: otherwise it would write into the shelf's region from outside it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function shelve(src isolated ByteArray) returns Count
	let shelf = Shelf.holding(ByteArray.init(src))
	src.push(2)
	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	return total
end 'shelve'

function main() returns ExitCode
	print("{shelve(ByteArray.create(0))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:2: use of moved value 'src': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.array-init-over-an-isolated-local-sent-to-a-service-moves-it -->
The same array sent as an `isolated` message argument crosses to the service's thread, so `src` is moved by the send and a later push from the sender is refused rather than racing the handler.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Keeper
	var held as Count

	static function create() returns Self
		return Self{held: 0}
	end 'create'

	export function keep(bytes isolated ByteArray) returns Count
		self.held = self.held + bytes.count()
		return self.held
	end 'keep'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let src = ByteArray.create(0) as isolated ByteArray
	src.push(1)
	let kept = try await keeper.keep(ByteArray.init(src)) otherwise 0
	src.push(2)
	print("{kept} {src.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:23:2: use of moved value 'src': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.array-init-over-an-isolated-runtime-buffer-moves-it -->
<!-- tier: stdlib -->
A fresh buffer bound `as isolated` is moved into `ByteArray.init`, so after the array fills the shelf's `isolated` field the buffer cannot be written again: `raw.setByte` would reach the shelf's region from outside it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let raw = (try __ManagedMemory.create(4, 1) otherwise panic("main: create")) as isolated __ManagedMemory
	try raw.setLength(4) otherwise panic("main: setLength")
	try raw.setByte(0, value: 5) otherwise panic("main: setByte")
	let shelf = Shelf.holding(ByteArray.init(raw))
	try raw.setByte(0, value: 99) otherwise panic("main: setByte again")
	var first = 0 as Byte

	modify shelf.bytes 'peek'
		first = try shelf.bytes.get(0) otherwise panic("main: get")
	end 'peek'

	print("{first}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:6: use of moved value 'raw': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.array-init-cannot-read-an-isolated-field-outside-a-block -->
An argument to `ByteArray.init` is a read like any other, so an `isolated` field handed to it outside a `modify` or `swap` is refused as the bare read `shelf.bytes` is, inside a method as well as from the field's owner.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'

	function copied() returns ByteArray
		return ByteArray.init(self.bytes)
	end 'copied'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.holding(ByteArray.create(0))
	let outside = ByteArray.init(shelf.bytes)
	outside.push(4)
	print("{outside.count()} {shelf.copied().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:13:25: 'self.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:19:31: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.array-init-over-a-string-view-is-not-fresh -->
<!-- tier: stdlib -->
`addressableBytes` hands back the string's own buffer, not a copy, so an array over it is not a fresh region and is refused as an `isolated` argument: otherwise a write through `shelf.bytes` would change the immutable `text`.

```maxon
type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let seven = 7 as Count
	let text = "{seven}bcd"
	let shelf = Shelf.holding(ByteArray.init(text.addressableBytes()))

	modify shelf.bytes 'scribble'
		try shelf.bytes.set(0, value: 90) otherwise panic("main: set")
	end 'scribble'

	print("{text}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:13:28: 'ByteArray.init(text.addressableBytes())' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.a-string-view-is-not-fresh -->
<!-- tier: stdlib -->
The buffer `addressableBytes` answers is the string's own, so it is not a clean call's fresh result even though its receiver is a value, and it is refused where an `isolated` value is wanted.

```maxon
function weigh(raw isolated __ManagedMemory) returns Count
	return raw.length()
end 'weigh'

function main() returns ExitCode
	let seven = 7 as Count
	let text = "{seven}bcd"
	let weighed = weigh(text.addressableBytes())
	print("{weighed}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:9:22: 'text.addressableBytes()' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.array-init-cannot-read-an-isolated-payload-through-a-field -->
A match over `holder.slot` does not move the union out of the field, so its `bytes` binding is a read of the field's `isolated` payload and `ByteArray.init(bytes)` is refused as the bare read is: otherwise the shelf and the holder would own one region, and the push through the second match would grow the shelf's array.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()
	var shelf = Shelf.holding(ByteArray.create(0))

	match holder.slot 'take'
		full(bytes) then shelf = Shelf.holding(ByteArray.init(bytes))
		empty then print("empty\n")
	end 'take'

	match holder.slot 'grow'
		full(bytes) then bytes.push(7)
		empty then print("empty\n")
	end 'grow'

	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:57: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-payload-read-through-a-field-is-not-a-second-owner -->
The same binding passed straight to an `isolated` parameter is the same read of the field's payload, and is refused for the same reason.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()
	var shelf = Shelf.holding(ByteArray.create(0))

	match holder.slot 'take'
		full(bytes) then shelf = Shelf.holding(bytes)
		empty then print("empty\n")
	end 'take'

	match holder.slot 'grow'
		full(bytes) then bytes.push(7)
		empty then print("empty\n")
	end 'grow'

	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:42: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-local-union-bound-from-an-awaited-call-moves-its-isolated-payload -->
A union whose case carries an `isolated` payload comes back from an awaited service message into a local, and the match arm that binds the payload merges it into the caller's result. The local holds the union the call produced, so the payload it binds is the call's own isolated value and `merge` moves it.

```maxon
union Drain
	lines(lines isolated StringArray)
	unreadable(reason String)
end 'Drain'

type Collector
	var served = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	export function drain() returns isolated Drain
		let collected = StringArray.create(0) as isolated StringArray

		modify collected 'fill'
			collected.push("alpha")
			collected.push("beta")
			collected.push("gamma")
		end 'fill'

		self.served = self.served + 1

		return Drain.lines(collected)
	end 'drain'
end 'Collector'

function gather(collector Collector.handle) returns StringArray
	let drained = try await collector.drain() otherwise panic("gather: the collector stopped while the caller held its handle")

	match drained 'drained'
		lines(collected) then return merge collected
		unreadable(reason) then panic("unreadable: {reason}")
	end 'drained'
end 'gather'

function main() returns ExitCode
	let collector = spawn Collector.create()
	let collected = gather(collector)

	print("{collected.count()}\n")
	collector.shutdown()

	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-local-union-bound-from-a-plain-call-moves-its-isolated-payload -->
The same union bound from a plain call, with no await between the call and the match, merges its payload the same way.

```maxon
union Drain
	lines(lines isolated StringArray)
	unreadable(reason String)
end 'Drain'

function drain() returns isolated Drain
	let collected = StringArray.create(0) as isolated StringArray

	modify collected 'fill'
		collected.push("alpha")
		collected.push("beta")
		collected.push("gamma")
		collected.push("delta")
	end 'fill'

	return Drain.lines(collected)
end 'drain'

function gather() returns StringArray
	let drained = drain()

	match drained 'drained'
		lines(collected) then return merge collected
		unreadable(reason) then panic("unreadable: {reason}")
	end 'drained'
end 'gather'

function main() returns ExitCode
	let collected = gather()

	print("{collected.count()}\n")

	return 0
end 'main'
```
```stdout
4
```
```exitcode
0
```

<!-- test: error.a-local-union-matched-twice-cannot-give-its-isolated-payload-two-owners -->
Matching the isolated payload out of a local union moves the payload and partially moves the local, so a second `match` over the same local would bind a second owner of one payload and is refused.

```maxon
union Drain
	lines(lines isolated StringArray)
	unreadable(reason String)
end 'Drain'

type Shelf
	export var lines as isolated StringArray

	static function holding(lines isolated StringArray) returns Self
		return Self{lines: lines}
	end 'holding'
end 'Shelf'

function drain() returns isolated Drain
	let collected = StringArray.create(0) as isolated StringArray

	modify collected 'fill'
		collected.push("alpha")
		collected.push("beta")
	end 'fill'

	return Drain.lines(collected)
end 'drain'

function main() returns ExitCode
	let drained = drain()
	var shelf = Shelf.holding(StringArray.create(0))

	match drained 'first'
		lines(collected) then shelf = Shelf.holding(collected)
		unreadable(reason) then panic("unreadable: {reason}")
	end 'first'

	match drained 'second'
		lines(again) then shelf = Shelf.holding(again)
		unreadable(reason) then panic("unreadable: {reason}")
	end 'second'

	var total = 0 as Count

	modify shelf.lines 'weigh'
		total = shelf.lines.count()
	end 'weigh'

	print("{total}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:35:8: use of moved value 'drained': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.an-awaited-local-union-matched-twice-cannot-give-its-isolated-payload-two-owners -->
The same union bound from an awaited service message is refused the same way: the second `match` over the local would give the payload a second owner.

```maxon
union Drain
	lines(lines isolated StringArray)
	unreadable(reason String)
end 'Drain'

type Shelf
	export var lines as isolated StringArray

	static function holding(lines isolated StringArray) returns Self
		return Self{lines: lines}
	end 'holding'
end 'Shelf'

type Collector
	var served = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	export function drain() returns isolated Drain
		let collected = StringArray.create(0) as isolated StringArray

		modify collected 'fill'
			collected.push("alpha")
			collected.push("beta")
		end 'fill'

		self.served = self.served + 1

		return Drain.lines(collected)
	end 'drain'
end 'Collector'

function gather(collector Collector.handle) returns Count
	let drained = try await collector.drain() otherwise panic("gather: the collector stopped while the caller held its handle")
	var shelf = Shelf.holding(StringArray.create(0))

	match drained 'first'
		lines(collected) then shelf = Shelf.holding(collected)
		unreadable(reason) then panic("unreadable: {reason}")
	end 'first'

	match drained 'second'
		lines(again) then shelf = Shelf.holding(again)
		unreadable(reason) then panic("unreadable: {reason}")
	end 'second'

	var total = 0 as Count

	modify shelf.lines 'weigh'
		total = shelf.lines.count()
	end 'weigh'

	return total
end 'gather'

function main() returns ExitCode
	let collector = spawn Collector.create()
	let total = gather(collector)

	print("{total}\n")
	collector.shutdown()

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:8: use of moved value 'drained': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.a-local-alias-of-a-field-union-cannot-give-its-isolated-payload-a-second-owner -->
A `let` bound from `holder.slot` holds the same union the field does, so the payload its match binds is still a part of the holder's field and handing it to an `isolated` parameter is refused as the read through the field is: otherwise the shelf and the holder would own one region, and the push through the shelf would grow the array the holder still reaches.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()
	var shelf = Shelf.holding(ByteArray.create(0))
	let alias = holder.slot

	match alias 'take'
		full(bytes) then shelf = Shelf.holding(bytes)
		empty then print("empty\n")
	end 'take'

	var total = 0 as Count

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
		total = shelf.bytes.count()
	end 'grow'

	match holder.slot 'look'
		full(bytes) then print("{total} {bytes.count()}\n")
		empty then print("empty\n")
	end 'look'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:42: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-parenthesized-field-union-cannot-give-its-isolated-payload-a-second-owner -->
Parentheses around `holder.slot` read the same field, so the payload the match binds is the field's and is refused where an `isolated` value is wanted exactly as it is without them.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()
	var shelf = Shelf.holding(ByteArray.create(0))

	match (holder.slot) 'take'
		full(bytes) then shelf = Shelf.holding(bytes)
		empty then print("empty\n")
	end 'take'

	var total = 0 as Count

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
		total = shelf.bytes.count()
	end 'grow'

	match holder.slot 'look'
		full(bytes) then print("{total} {bytes.count()}\n")
		empty then print("empty\n")
	end 'look'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:42: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-field-union-returned-by-a-helper-cannot-give-its-isolated-payload-a-second-owner -->
A function that returns `holder.slot` hands back the union the field still holds, not a fresh one, so the payload a match over its result binds is the field's and is refused where an `isolated` value is wanted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function slotOf(holder Holder) returns Slot
	return holder.slot
end 'slotOf'

function main() returns ExitCode
	let holder = Holder.create()
	var shelf = Shelf.holding(ByteArray.create(0))

	match slotOf(holder) 'take'
		full(bytes) then shelf = Shelf.holding(bytes)
		empty then print("empty\n")
	end 'take'

	var total = 0 as Count

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
		total = shelf.bytes.count()
	end 'grow'

	match holder.slot 'look'
		full(bytes) then print("{total} {bytes.count()}\n")
		empty then print("empty\n")
	end 'look'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:42: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-nested-field-union-bound-to-a-local-cannot-give-its-isolated-payload-a-second-owner -->
A union bound out of a field's union payload is still part of that field, so a `let` holding it does not own the `isolated` payload its match binds, and handing that payload to an `isolated` parameter is refused: otherwise the shelf and the nest would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

union Outer
	inner(slot Slot)
	none
end 'Outer'

type Nest
	export var outer as Outer

	static function create() returns Self
		return Self{outer: Outer.inner(Slot.full(ByteArray.create(0)))}
	end 'create'
end 'Nest'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let nest = Nest.create()
	var shelf = Shelf.holding(ByteArray.create(0))

	let slot = match nest.outer 'outer'
		inner(slot) gives slot
		none gives Slot.empty
	end 'outer'

	match slot 'take'
		full(bytes) then shelf = Shelf.holding(bytes)
		empty then print("empty\n")
	end 'take'

	var total = 0 as Count

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
		total = shelf.bytes.count()
	end 'grow'

	let again = match nest.outer 'again'
		inner(s) gives s
		none gives Slot.empty
	end 'again'

	match again 'look'
		full(b) then print("{total} {b.count()}\n")
		empty then print("empty\n")
	end 'look'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:41:42: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-local-union-matched-in-a-loop-cannot-give-its-isolated-payload-two-owners -->
A `match` over a local union inside a loop runs once per iteration, so the payload the first iteration moved out would be bound again by the second, and the second trip is refused as a second `match` after the first is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(0))
end 'make'

function main() returns ExitCode
	let made = make()
	var shelf = Shelf.holding(ByteArray.create(0))
	var lap = 0 as Count

	while lap < 2 'laps'
		match made 'take'
			full(bytes) then shelf = Shelf.holding(bytes)
			empty then print("empty\n")
		end 'take'

		lap = lap + 1
	end 'laps'

	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:9: use of moved value 'made': it was moved in an earlier iteration of this loop
```

<!-- test: error.an-isolated-payload-given-out-of-a-match-over-a-field-is-not-a-second-owner -->
A `gives` arm that hands back the payload a `match` over `holder.slot` binds yields the field's own region, so the value the match expression produces is a read of an isolated part the holder still reaches and is refused where an `isolated` value is wanted, as the payload is when passed directly: otherwise the shelf and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()

	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	let shelf = Shelf.holding(got)
	var total = 0 as Count

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
		total = shelf.bytes.count()
	end 'grow'

	match holder.slot 'look'
		full(bytes) then print("{total} {bytes.count()}\n")
		empty then print("empty\n")
	end 'look'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:30:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-payload-given-out-of-a-match-over-a-parameter-field-is-not-returned-isolated -->
A function that returns `isolated` the payload a `match` over its parameter's field gives does not hand back a fresh region: the caller's holder still reaches it, so the read is refused as it is outside a block.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function grab(holder Holder) returns isolated ByteArray
	return match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'
end 'grab'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(grab(holder))

	modify shelf.bytes 'grow'
		shelf.bytes.push(7)
	end 'grow'

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.array-init-over-a-named-buffer-leaves-no-live-alias -->
<!-- tier: stdlib -->
`ByteArray.init(buf)` builds the array over `buf`'s storage, so a buffer bound by a plain `let` is still named after the array exists: two names reach one region. The array is therefore not fresh, and handing it to an `isolated` parameter is refused rather than letting the `setByte` through `buf` write into a region the callee owns.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function main() returns ExitCode
	let buf = try __ManagedMemory.create(4, 1) otherwise panic("main: create")
	try buf.setLength(4) otherwise panic("main: setLength")
	let a = ByteArray.init(buf)
	let weighed = weigh(a)
	try buf.setByte(0, value: 9) otherwise panic("main: setByte")
	print("{weighed}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:13:22: 'a' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.a-send-error-union-matched-twice-cannot-give-its-isolated-payload-two-owners -->
A refused send hands its `isolated` argument back as the payload of the send error, and the first `match e` moves that payload into `adopt`. The union still names it, so a second `match e` that reads the payload would give one region two owners and is refused as a use of a moved value.

```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Payload)
		self.seen = self.seen + 1
		print("kept {p.n}\n")
	end 'keep'
end 'Store'

function adopt(p isolated Payload) returns Integer
	return p.n
end 'adopt'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()

	let accepted = Payload.create(1) as isolated Payload

	try h.keep(accepted) otherwise panic("a capacity of one takes the first send")

	let second = Payload.create(2) as isolated Payload
	var total = 0 as Integer

	try h.keep(second) otherwise (e) 'refused'
		match e 'first'
			mailboxFull(back) then total = adopt(back)
			serviceStopped(back) then total = adopt(back)
		end 'first'

		match e 'second'
			mailboxFull(again) then total = total + again.n
			serviceStopped(again) then total = total + again.n
		end 'second'
	end 'refused'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:45:9: use of moved value 'e': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-send-error-union-hands-its-isolated-payload-back-once -->
<!-- procs: 1 -->
<!-- preempt: off -->
With one processor the service does not run between the two sends, so a mailbox of capacity one refuses the second and the send error hands its `isolated` payload back. One `match e` moves that payload into an `isolated` field, which then holds the very record the sender built.

```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Shelf
	export var held as isolated Payload

	static function holding(held isolated Payload) returns Self
		return Self{held: held}
	end 'holding'
end 'Shelf'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Payload)
		self.seen = self.seen + 1
		print("kept {p.n}\n")
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	var shelf = Shelf.holding(Payload.create(0) as isolated Payload)

	let accepted = Payload.create(1) as isolated Payload

	try h.keep(accepted) otherwise panic("a capacity of one takes the first send")

	let second = Payload.create(2) as isolated Payload

	try h.keep(second) otherwise (e) 'refused'
		match e 'first'
			mailboxFull(back) then shelf = Shelf.holding(back)
			serviceStopped(back) then shelf = Shelf.holding(back)
		end 'first'
	end 'refused'

	modify shelf.held 'read'
		print("{shelf.held.n}\n")
	end 'read'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
kept 1
```

<!-- test: error.a-local-alias-of-a-send-error-union-cannot-give-its-isolated-payload-a-second-owner -->
A `let` bound from the send error `e` takes the union and the `isolated` payload it owns, as a `let` bound from any owned local union does. A later `match e` that moves the payload into `adopt` would leave `other` naming the same payload for a second `adopt`, so the read of `e` is refused as a use of a moved value.

```maxon
typealias Integer = int(i64.min to i64.max)

type Payload
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Payload'

type Store
	var seen as Integer

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function keep(p isolated Payload)
		self.seen = self.seen + 1
		print("kept {p.n}\n")
	end 'keep'
end 'Store'

function adopt(p isolated Payload) returns Integer
	return p.n
end 'adopt'

function main() returns ExitCode
	let h = spawn(capacity: 1) Store.create()
	let accepted = Payload.create(1) as isolated Payload

	try h.keep(accepted) otherwise panic("a capacity of one takes the first send")

	let second = Payload.create(2) as isolated Payload
	var total = 0 as Integer

	try h.keep(second) otherwise (e) 'refused'
		let other = e

		match e 'first'
			mailboxFull(back) then total = adopt(back)
			serviceStopped(back) then total = adopt(back)
		end 'first'

		match other 'second'
			mailboxFull(again) then total = total + adopt(again)
			serviceStopped(again) then total = total + adopt(again)
		end 'second'
	end 'refused'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:41:9: use of moved value 'e': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-a-constructor-field -->
An `isolated` part of a holder that a join hands to a field declared `isolated` is a read of a region the holder still reaches, however the read arrives: through a conditional expression, through a `match` expression's `gives` arms, as the payload bound directly, or as the holder's own `isolated` field. Each reaches the constructor as the bare read does, so each is refused: otherwise the new shelf and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'

	static function viaTernary(holder Holder, flag bool) returns Self
		match holder.slot 'take'
			full(bytes) then return Self{bytes: bytes if flag else ByteArray.create(0)}
			empty then return Self{bytes: ByteArray.create(0)}
		end 'take'
	end 'viaTernary'

	static function viaGives(holder Holder) returns Self
		let got = match holder.slot 'take'
			full(bytes) gives bytes
			empty gives ByteArray.create(0)
		end 'take'

		return Self{bytes: got}
	end 'viaGives'

	static function direct(holder Holder) returns Self
		match holder.slot 'take'
			full(bytes) then return Self{bytes: bytes}
			empty then return Self{bytes: ByteArray.create(0)}
		end 'take'
	end 'direct'

	static function fieldTernary(shelf Shelf, flag bool) returns Self
		return Self{bytes: shelf.bytes if flag else ByteArray.create(0)}
	end 'fieldTernary'

	static function fieldDirect(shelf Shelf) returns Self
		return Self{bytes: shelf.bytes}
	end 'fieldDirect'
end 'Shelf'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))
	let a = Shelf.viaTernary(holder, flag: true)
	let b = Shelf.viaGives(holder)
	let c = Shelf.direct(holder)
	let d = Shelf.fieldTernary(shelf, flag: true)
	let e = Shelf.fieldDirect(shelf)
	var total = 0 as Count

	modify a.bytes 'weigh'
		total = a.bytes.count()
	end 'weigh'

	print("{total} {b.bytes.count()} {c.bytes.count()} {d.bytes.count()} {e.bytes.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:27:40: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:34:22: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:43:40: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:49:22: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:53:22: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-reaches-a-constructor-field -->
The payload a `match` moves out of a local `isolated` union is that call's own region, so the same three joins hand it to an `isolated` field without a refusal, and the shelf owns it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(4))
end 'make'

type Shelf
	export var bytes as isolated ByteArray

	static function viaTernary(flag bool) returns Self
		let made = make()

		match made 'take'
			full(bytes) then return Self{bytes: bytes if flag else ByteArray.create(2)}
			empty then return Self{bytes: ByteArray.create(0)}
		end 'take'
	end 'viaTernary'

	static function viaGives() returns Self
		let made = make()

		let got = match made 'take'
			full(bytes) gives bytes
			empty gives ByteArray.create(0)
		end 'take'

		return Self{bytes: got}
	end 'viaGives'

	static function direct() returns Self
		let made = make()

		match made 'take'
			full(bytes) then return Self{bytes: bytes}
			empty then return Self{bytes: ByteArray.create(0)}
		end 'take'
	end 'direct'
end 'Shelf'

function weighShelf(shelf Shelf) returns Count
	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	return total
end 'weighShelf'

function main() returns ExitCode
	print("{weighShelf(Shelf.viaTernary(true))} {weighShelf(Shelf.viaGives())} {weighShelf(Shelf.direct())}\n")
	return 0
end 'main'
```
```stdout
4 4 4
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-an-isolated-field-store -->
A store into an `isolated` field takes an `isolated` value, so an `isolated` part of a holder that reaches it through a conditional expression, a `match` expression's `gives` arms, the payload bound directly or the holder's own `isolated` field is refused as the bare read is, outside a `modify` block.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function storeTernary(holder Holder, shelf Shelf, flag bool)
	match holder.slot 'take'
		full(bytes) then shelf.bytes = bytes if flag else ByteArray.create(0)
		empty then print("empty\n")
	end 'take'
end 'storeTernary'

function storeGives(holder Holder, shelf Shelf)
	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	shelf.bytes = got
end 'storeGives'

function storeDirect(holder Holder, shelf Shelf)
	match holder.slot 'take'
		full(bytes) then shelf.bytes = bytes
		empty then print("empty\n")
	end 'take'
end 'storeDirect'

function storeFieldTernary(donor Shelf, shelf Shelf, flag bool)
	shelf.bytes = donor.bytes if flag else ByteArray.create(0)
end 'storeFieldTernary'

function storeFieldDirect(donor Shelf, shelf Shelf)
	shelf.bytes = donor.bytes
end 'storeFieldDirect'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))
	let source = Shelf.holding(ByteArray.create(0))

	storeTernary(holder, shelf: shelf, flag: true)
	storeGives(holder, shelf: shelf)
	storeDirect(holder, shelf: shelf)
	storeFieldTernary(source, shelf: shelf, flag: true)
	storeFieldDirect(source, shelf: shelf)

	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:34: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:35:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:44:34: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:50:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:54:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-an-isolated-field-store-inside-modify -->
Opening the destination with `modify` does not make the source a value: the `isolated` part the store reads still belongs to the holder, so the same five reads are refused where the destination is a field of a `modify` root.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function insideTernary(holder Holder, boxed isolated Shelf, flag bool)
	modify boxed 'open'
		match holder.slot 'take'
			full(bytes) then boxed.bytes = bytes if flag else ByteArray.create(0)
			empty then print("empty\n")
		end 'take'
	end 'open'
end 'insideTernary'

function insideGives(holder Holder, boxed isolated Shelf)
	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	modify boxed 'open'
		boxed.bytes = got
	end 'open'
end 'insideGives'

function insideDirect(holder Holder, boxed isolated Shelf)
	modify boxed 'open'
		match holder.slot 'take'
			full(bytes) then boxed.bytes = bytes
			empty then print("empty\n")
		end 'take'
	end 'open'
end 'insideDirect'

function insideFieldTernary(donor Shelf, boxed isolated Shelf, flag bool)
	modify boxed 'open'
		boxed.bytes = donor.bytes if flag else ByteArray.create(0)
	end 'open'
end 'insideFieldTernary'

function insideFieldDirect(donor Shelf, boxed isolated Shelf)
	modify boxed 'open'
		boxed.bytes = donor.bytes
	end 'open'
end 'insideFieldDirect'

function main() returns ExitCode
	let holder = Holder.create()
	let source = Shelf.holding(ByteArray.create(0))

	insideTernary(holder, boxed: Shelf.holding(ByteArray.create(0)), flag: true)
	insideGives(holder, boxed: Shelf.holding(ByteArray.create(0)))
	insideDirect(holder, boxed: Shelf.holding(ByteArray.create(0)))
	insideFieldTernary(source, boxed: Shelf.holding(ByteArray.create(0)), flag: true)
	insideFieldDirect(source, boxed: Shelf.holding(ByteArray.create(0)))

	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:29:35: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:37:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:49:35: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:57:17: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:63:17: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-reaches-an-isolated-field-store -->
The payload a `match` moves out of a local `isolated` union is a fresh region, so the three joins store it into an `isolated` field outside a block and inside a `modify` root alike.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(4))
end 'make'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weighShelf(shelf Shelf) returns Count
	var total = 0 as Count

	modify shelf.bytes 'weigh'
		total = shelf.bytes.count()
	end 'weigh'

	return total
end 'weighShelf'

function storeTernary(flag bool) returns Count
	let made = make()
	let shelf = Shelf.holding(ByteArray.create(0))

	match made 'take'
		full(bytes) then shelf.bytes = bytes if flag else ByteArray.create(2)
		empty then print("empty\n")
	end 'take'

	return weighShelf(shelf)
end 'storeTernary'

function storeGives() returns Count
	let made = make()
	let shelf = Shelf.holding(ByteArray.create(0))

	let got = match made 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	shelf.bytes = got

	return weighShelf(shelf)
end 'storeGives'

function storeDirect() returns Count
	let made = make()
	let shelf = Shelf.holding(ByteArray.create(0))

	match made 'take'
		full(bytes) then shelf.bytes = bytes
		empty then print("empty\n")
	end 'take'

	return weighShelf(shelf)
end 'storeDirect'

function insideTernary(flag bool) returns Count
	let made = make()
	let boxed = Shelf.holding(ByteArray.create(0)) as isolated Shelf

	modify boxed 'open'
		match made 'take'
			full(bytes) then boxed.bytes = bytes if flag else ByteArray.create(2)
			empty then print("empty\n")
		end 'take'
	end 'open'

	return weighShelf(boxed)
end 'insideTernary'

function main() returns ExitCode
	print("{storeTernary(true)} {storeGives()} {storeDirect()} {insideTernary(true)}\n")
	return 0
end 'main'
```
```stdout
4 4 4 4
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-an-isolated-argument -->
An argument to an `isolated` parameter moves its value into the callee, so an `isolated` part of a holder that a join hands to one is refused as the bare read is: otherwise the callee and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function argTernary(holder Holder, flag bool) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(bytes if flag else ByteArray.create(0))
		empty then return 0
	end 'take'
end 'argTernary'

function argGives(holder Holder) returns Count
	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return weigh(got)
end 'argGives'

function argDirect(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(bytes)
		empty then return 0
	end 'take'
end 'argDirect'

function argFieldTernary(shelf Shelf, flag bool) returns Count
	return weigh(shelf.bytes if flag else ByteArray.create(0))
end 'argFieldTernary'

function argFieldDirect(shelf Shelf) returns Count
	return weigh(shelf.bytes)
end 'argFieldDirect'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))

	print("{argTernary(holder, flag: true)} {argGives(holder)} {argDirect(holder)}\n")
	print("{argFieldTernary(shelf, flag: true)} {argFieldDirect(shelf)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:33: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:39:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:48:33: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:54:15: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:58:15: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-reaches-an-isolated-argument -->
The payload a `match` moves out of a local `isolated` union is a fresh region, so the three joins hand it to an `isolated` parameter without a refusal.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(4))
end 'make'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function argTernary(flag bool) returns Count
	let made = make()

	match made 'take'
		full(bytes) then return weigh(bytes if flag else ByteArray.create(2))
		empty then return 0
	end 'take'
end 'argTernary'

function argGives() returns Count
	let made = make()

	let got = match made 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return weigh(got)
end 'argGives'

function argDirect() returns Count
	let made = make()

	match made 'take'
		full(bytes) then return weigh(bytes)
		empty then return 0
	end 'take'
end 'argDirect'

function main() returns ExitCode
	print("{argTernary(true)} {argGives()} {argDirect()}\n")
	return 0
end 'main'
```
```stdout
4 4 4
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-merge -->
`merge` moves an `isolated` region into the current one, so an `isolated` part of a holder that a join hands to it is refused as the bare read is: otherwise the result and the holder would share one region as ordinary mutable data.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function mergeTernary(holder Holder, flag bool) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return merge (bytes if flag else ByteArray.create(0))
		empty then return ByteArray.create(0)
	end 'take'
end 'mergeTernary'

function mergeGives(holder Holder) returns ByteArray
	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return merge got
end 'mergeGives'

function mergeDirect(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return merge bytes
		empty then return ByteArray.create(0)
	end 'take'
end 'mergeDirect'

function mergeFieldTernary(shelf Shelf, flag bool) returns ByteArray
	return merge (shelf.bytes if flag else ByteArray.create(0))
end 'mergeFieldTernary'

function mergeFieldDirect(shelf Shelf) returns ByteArray
	return merge shelf.bytes
end 'mergeFieldDirect'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))

	print("{mergeTernary(holder, flag: true).count()} {mergeGives(holder).count()} {mergeDirect(holder).count()}\n")
	print("{mergeFieldTernary(shelf, flag: true).count()} {mergeFieldDirect(shelf).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:34: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:35:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:44:27: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:50:16: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:54:9: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-reaches-merge -->
The payload a `match` moves out of a local `isolated` union is a fresh region, so `merge` of it through the three joins is ordinary mutable data the caller owns.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(4))
end 'make'

function mergeTernary(flag bool) returns ByteArray
	let made = make()

	match made 'take'
		full(bytes) then return merge (bytes if flag else ByteArray.create(2))
		empty then return ByteArray.create(0)
	end 'take'
end 'mergeTernary'

function mergeGives() returns ByteArray
	let made = make()

	let got = match made 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return merge got
end 'mergeGives'

function mergeDirect() returns ByteArray
	let made = make()

	match made 'take'
		full(bytes) then return merge bytes
		empty then return ByteArray.create(0)
	end 'take'
end 'mergeDirect'

function main() returns ExitCode
	print("{mergeTernary(true).count()} {mergeGives().count()} {mergeDirect().count()}\n")
	return 0
end 'main'
```
```stdout
4 4 4
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-through-a-join-cannot-reach-an-isolated-return -->
A function that returns `isolated` hands its caller a region of its own, so an `isolated` part of a holder that a join returns is refused as the bare read is: otherwise the caller and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function returnTernary(holder Holder, flag bool) returns isolated ByteArray
	match holder.slot 'take'
		full(bytes) then return bytes if flag else ByteArray.create(0)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnTernary'

function returnGives(holder Holder) returns isolated ByteArray
	let got = match holder.slot 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return got
end 'returnGives'

function returnDirect(holder Holder) returns isolated ByteArray
	match holder.slot 'take'
		full(bytes) then return bytes
		empty then return ByteArray.create(0)
	end 'take'
end 'returnDirect'

function returnFieldTernary(shelf Shelf, flag bool) returns isolated ByteArray
	return shelf.bytes if flag else ByteArray.create(0)
end 'returnFieldTernary'

function returnFieldDirect(shelf Shelf) returns isolated ByteArray
	return shelf.bytes
end 'returnFieldDirect'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))

	print("{weigh(returnTernary(holder, flag: true))} {weigh(returnGives(holder))} {weigh(returnDirect(holder))}\n")
	print("{weigh(returnFieldTernary(shelf, flag: true))} {weigh(returnFieldDirect(shelf))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:27: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:39:21: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:48:27: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:54:9: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:58:9: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-reaches-an-isolated-return -->
The payload a `match` moves out of a local `isolated` union is a fresh region, so a function that returns `isolated` hands it back through the three joins.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

function make() returns isolated Slot
	return Slot.full(ByteArray.create(4))
end 'make'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function returnTernary(flag bool) returns isolated ByteArray
	let made = make()

	match made 'take'
		full(bytes) then return bytes if flag else ByteArray.create(2)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnTernary'

function returnGives() returns isolated ByteArray
	let made = make()

	let got = match made 'take'
		full(bytes) gives bytes
		empty gives ByteArray.create(0)
	end 'take'

	return got
end 'returnGives'

function returnDirect() returns isolated ByteArray
	let made = make()

	match made 'take'
		full(bytes) then return bytes
		empty then return ByteArray.create(0)
	end 'take'
end 'returnDirect'

function main() returns ExitCode
	print("{weigh(returnTernary(true))} {weigh(returnGives())} {weigh(returnDirect())}\n")
	return 0
end 'main'
```
```stdout
4 4 4
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-cannot-reach-an-async-isolated-argument -->
An `async` call to an `isolated` parameter moves its argument into the coroutine as a plain call does, so an `isolated` part of a holder, bare or through a join, is refused there as it is at a plain call: otherwise the coroutine and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function asyncPayload(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weigh(bytes)
		empty then return 0
	end 'take'
end 'asyncPayload'

function asyncPayloadTernary(holder Holder, flag bool) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weigh(bytes if flag else ByteArray.create(0))
		empty then return 0
	end 'take'
end 'asyncPayloadTernary'

function asyncField(shelf Shelf) returns Count
	return await async weigh(shelf.bytes)
end 'asyncField'

function asyncFieldTernary(shelf Shelf, flag bool) returns Count
	return await async weigh(shelf.bytes if flag else ByteArray.create(0))
end 'asyncFieldTernary'

function main() returns ExitCode
	let holder = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))

	print("{asyncPayload(holder)} {asyncPayloadTernary(holder, flag: true)}\n")
	print("{asyncField(shelf)} {asyncFieldTernary(shelf, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:45: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:39:45: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:45:27: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:49:27: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-async-call-cannot-move-a-callers-mutable-data-into-an-isolated-parameter -->
An `isolated` parameter takes only a fresh region, and an `async` call is no exception: the caller's mutable data handed to one would be kept by the callee while the caller still reaches it, so it is refused as at a plain call.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function adopt(bytes isolated ByteArray) returns Shelf
	return Shelf.holding(bytes)
end 'adopt'

function grow(shelf Shelf)
	modify shelf.bytes 'write'
		shelf.bytes.push(9)
	end 'write'
end 'grow'

function fromParameter(p ByteArray) returns Shelf
	return await async adopt(p)
end 'fromParameter'

function main() returns ExitCode
	let mine = ByteArray.create(1)
	let other = fromParameter(mine)
	grow(other)
	print("{mine.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:24:27: 'p' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
```

<!-- test: error.an-isolated-part-read-captured-by-a-closure-is-not-a-clean-input -->
A closure that captures an `isolated` part of a holder hands that region back from every call, so it is no clean input: the result of a call over it cannot go where an isolated value is wanted, bare or through a join.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function captured(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(apply(function() gives bytes))
		empty then return 0
	end 'take'
end 'captured'

function capturedTernary(holder Holder, flag bool) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(apply(function() gives bytes if flag else ByteArray.create(0)))
		empty then return 0
	end 'take'
end 'capturedTernary'

function main() returns ExitCode
	let holder = Holder.create()

	print("{captured(holder)} {capturedTernary(holder, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:30:56: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:37:56: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-through-a-join-with-a-parameter-arm-cannot-reach-an-isolated-field-store -->
A join that has an `isolated` parameter as one arm, or a call result whose capability is settled by its arguments, is still a join: an `isolated` part of a holder on its other arm is refused at a store into an `isolated` field, whichever arm it is on.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Box
	export var part as isolated ByteArray

	static function holding(part isolated ByteArray) returns Self
		return Self{part: part}
	end 'holding'
end 'Box'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function storeParameterSecond(a Box, b Box, n isolated ByteArray, c bool)
	b.part = a.part if c else n
end 'storeParameterSecond'

function storeParameterFirst(a Box, b Box, n isolated ByteArray, c bool)
	b.part = n if c else a.part
end 'storeParameterFirst'

function storeSettledSecond(a Box, b Box, c bool)
	b.part = a.part if c else echo(ByteArray.create(0))
end 'storeSettledSecond'

function storeSettledFirst(a Box, b Box, c bool)
	b.part = echo(ByteArray.create(0)) if c else a.part
end 'storeSettledFirst'

function main() returns ExitCode
	let a = Box.holding(ByteArray.create(0))
	let b = Box.holding(ByteArray.create(0))

	storeParameterSecond(a, b: b, n: ByteArray.create(0), c: true)
	storeParameterFirst(a, b: b, n: ByteArray.create(0), c: true)
	storeSettledSecond(a, b: b, c: true)
	storeSettledFirst(a, b: b, c: true)
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:18:11: 'a.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:22:23: 'a.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:26:11: 'a.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:30:47: 'a.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-fresh-isolated-value-through-a-join-with-a-parameter-arm-reaches-an-isolated-field-store -->
With a fresh `isolated` value on the other arm, the same joins reach an `isolated` field: a parameter arm and a settled call result add no holder to the region the field receives.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Box
	export var part as isolated ByteArray

	static function holding(part isolated ByteArray) returns Self
		return Self{part: part}
	end 'holding'
end 'Box'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function storeParameterSecond(b Box, n isolated ByteArray, c bool)
	b.part = ByteArray.create(3) if c else n
end 'storeParameterSecond'

function storeParameterFirst(b Box, n isolated ByteArray, c bool)
	b.part = n if c else ByteArray.create(3)
end 'storeParameterFirst'

function storeSettledSecond(b Box, c bool)
	b.part = ByteArray.create(1) if c else echo(ByteArray.create(3))
end 'storeSettledSecond'

function storeSettledFirst(b Box, c bool)
	b.part = echo(ByteArray.create(3)) if c else ByteArray.create(1)
end 'storeSettledFirst'

function weighBox(box Box) returns Count
	var total = 0 as Count

	modify box.part 'weigh'
		total = box.part.count()
	end 'weigh'

	return total
end 'weighBox'

function main() returns ExitCode
	let b = Box.holding(ByteArray.create(0))

	storeParameterSecond(b, n: ByteArray.create(5), c: true)
	let one = weighBox(b)
	storeParameterSecond(b, n: ByteArray.create(5), c: false)
	let two = weighBox(b)
	storeParameterFirst(b, n: ByteArray.create(5), c: true)
	let three = weighBox(b)
	storeParameterFirst(b, n: ByteArray.create(5), c: false)
	let four = weighBox(b)
	storeSettledSecond(b, c: true)
	let five = weighBox(b)
	storeSettledSecond(b, c: false)
	let six = weighBox(b)
	storeSettledFirst(b, c: true)
	let seven = weighBox(b)
	storeSettledFirst(b, c: false)
	let eight = weighBox(b)

	print("{one} {two} {three} {four} {five} {six} {seven} {eight}\n")
	return 0
end 'main'
```
```stdout
3 5 5 3 1 3 3 1
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-cannot-reach-a-union-constructors-isolated-payload -->
A union case whose payload is `isolated` takes an `isolated` value, so an `isolated` part of a holder handed to its constructor, bare, as a field or through a join, is refused as the bare read is: otherwise the new union and the holder would own one region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function rewrap(holder Holder, other Holder)
	match holder.slot 'take'
		full(bytes) then other.slot = Slot.full(bytes)
		empty then print("empty\n")
	end 'take'
end 'rewrap'

function wrapField(shelf Shelf) returns Slot
	return Slot.full(shelf.bytes)
end 'wrapField'

function wrapTernary(shelf Shelf, flag bool) returns Slot
	return Slot.full(shelf.bytes if flag else ByteArray.create(0))
end 'wrapTernary'

function main() returns ExitCode
	let holder = Holder.create()
	let other = Holder.create()
	let shelf = Shelf.holding(ByteArray.create(0))

	rewrap(holder, other: other)
	_ = wrapField(shelf)
	_ = wrapTernary(shelf, flag: true)
	print("done\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:43: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:34:19: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:19: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-through-an-otherwise-arm-cannot-reach-an-isolated-slot -->
The fallback of `try … otherwise` is a second arm of the value, so an `isolated` part of a holder given as the fallback is refused at an `isolated` field store and an `isolated` parameter as the bare read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

enum MakeError implements Error
	refused
end 'MakeError'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function makeOrFail(flag bool) returns ByteArray throws MakeError
	if flag 'refuse'
		throw MakeError.refused
	end 'refuse'

	return ByteArray.create(1)
end 'makeOrFail'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function storeOtherwise(holder Holder, shelf Shelf, flag bool)
	match holder.slot 'take'
		full(bytes) then shelf.bytes = try makeOrFail(flag) otherwise bytes
		empty then print("empty\n")
	end 'take'
end 'storeOtherwise'

function storeOtherwiseField(donor Shelf, shelf Shelf, flag bool)
	shelf.bytes = try makeOrFail(flag) otherwise donor.bytes
end 'storeOtherwiseField'

function argOtherwiseField(donor Shelf, flag bool) returns Count
	return weigh(try makeOrFail(flag) otherwise donor.bytes)
end 'argOtherwiseField'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))
	let shelf = Shelf.holding(ByteArray.create(0))

	storeOtherwise(holder, shelf: shelf, flag: true)
	storeOtherwiseField(donor, shelf: shelf, flag: true)
	print("{argOtherwiseField(donor, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:44:65: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:50:47: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:54:46: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-in-the-middle-arm-of-a-match-expression-cannot-reach-an-isolated-slot -->
A `match` expression joins all its arms, so an `isolated` part of a holder on its middle arm alone, between two fresh arms, is refused at an `isolated` field store, an `isolated` parameter and an `isolated` return.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum Mode
	first
	second
	third
end 'Mode'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function storeMiddle(donor Shelf, shelf Shelf, mode Mode)
	shelf.bytes = match mode 'pick'
		first gives ByteArray.create(0)
		second gives donor.bytes
		third gives ByteArray.create(1)
	end 'pick'
end 'storeMiddle'

function argMiddle(donor Shelf, mode Mode) returns Count
	return weigh(match mode 'pick'
		first gives ByteArray.create(0)
		second gives donor.bytes
		third gives ByteArray.create(1)
	end 'pick')
end 'argMiddle'

function returnMiddle(donor Shelf, mode Mode) returns isolated ByteArray
	return match mode 'pick'
		first gives ByteArray.create(0)
		second gives donor.bytes
		third gives ByteArray.create(1)
	end 'pick'
end 'returnMiddle'

function main() returns ExitCode
	let donor = Shelf.holding(ByteArray.create(0))
	let shelf = Shelf.holding(ByteArray.create(0))

	storeMiddle(donor, shelf: shelf, mode: Mode.second)
	print("{argMiddle(donor, mode: Mode.second)} {weigh(returnMiddle(donor, mode: Mode.second))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:34:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:42:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-captured-by-a-nested-closure-is-not-a-clean-input -->
A closure that builds another closure over an `isolated` part of a holder captures that part too, so the result of a call over the outer closure is no clean input to an `isolated` parameter.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function nested(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(apply(function() gives apply(function() gives bytes)))
		empty then return 0
	end 'take'
end 'nested'

function main() returns ExitCode
	let holder = Holder.create()

	print("{nested(holder)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:30:79: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-awaited-result-over-an-isolated-part-read-is-not-a-clean-input -->
A coroutine is never given an `isolated` part of a holder through a plain parameter, directly or captured by a closure: the spawn is refused (E3207) before its awaited result could reach an `isolated` parameter.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function awaitClosure(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(await async apply(function() gives bytes))
		empty then return 0
	end 'take'
end 'awaitClosure'

function awaitEcho(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(await async echo(bytes))
		empty then return 0
	end 'take'
end 'awaitEcho'

function main() returns ExitCode
	let holder = Holder.create()

	print("{awaitClosure(holder)} {awaitEcho(holder)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:34:51: 'apply' is spawned with `async`, and 'function' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
error E3207: <fragment>:41:50: 'echo' is spawned with `async`, and 'bytes' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: error.an-isolated-local-moved-by-an-async-call-in-a-loop-is-used-after-move -->
An `async` call moves its `isolated` argument into the coroutine, so the same `isolated` local handed to it again on a later lap, or read after the call, is a use of a moved value.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function inLoop(rounds Count) returns Count
	let mine = ByteArray.create(2) as isolated ByteArray
	var total = 0 as Count
	var lap = 0 as Count

	while lap < rounds 'each'
		total = total + await async weigh(mine)
		lap = lap + 1
	end 'each'

	return total
end 'inLoop'

function afterCall() returns Count
	let mine = ByteArray.create(2) as isolated ByteArray
	let total = await async weigh(mine)
	return total + mine.count()
end 'afterCall'

function main() returns ExitCode
	print("{inLoop(2)} {afterCall()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:37: use of moved value 'mine': it was moved in an earlier iteration of this loop
error E3102: <fragment>:25:17: use of moved value 'mine': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: error.an-isolated-part-read-cannot-reach-a-service-message-argument -->
A service message's `isolated` argument crosses to the service's thread, so an `isolated` part of a holder sent as one, bare, parenthesized or through a join, is refused as the bare read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

type Keeper
	var held as Count

	static function create() returns Self
		return Self{held: 0}
	end 'create'

	export function keep(bytes isolated ByteArray) returns Count
		self.held = self.held + bytes.count()
		return self.held
	end 'keep'
end 'Keeper'

function sendPayload(keeper Keeper.handle, holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return try await keeper.keep((bytes)) otherwise 0
		empty then return 0
	end 'take'
end 'sendPayload'

function sendField(keeper Keeper.handle, donor Shelf) returns Count
	return try await keeper.keep(donor.bytes) otherwise 0
end 'sendField'

function sendTernary(keeper Keeper.handle, donor Shelf, flag bool) returns Count
	return try await keeper.keep(donor.bytes if flag else ByteArray.create(0)) otherwise 0
end 'sendTernary'

function main() returns ExitCode
	let keeper = spawn Keeper.create()
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))

	print("{sendPayload(keeper, holder: holder)} {sendField(keeper, donor: donor)} {sendTernary(keeper, donor: donor, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:41:49: '(bytes)' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:47:31: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:51:31: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-cannot-replace-an-isolated-field-by-swap -->
The replacement `swap` stores into an `isolated` field takes an `isolated` value, so an `isolated` part of a holder given as the replacement, bare, as a field or through a join, is refused as the bare read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function swapPayload(holder Holder, shelf Shelf) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(swap shelf.bytes with bytes)
		empty then return 0
	end 'take'
end 'swapPayload'

function swapField(donor Shelf, shelf Shelf) returns Count
	return weigh(swap shelf.bytes with donor.bytes)
end 'swapField'

function swapTernary(donor Shelf, shelf Shelf, flag bool) returns Count
	return weigh(swap shelf.bytes with (donor.bytes if flag else ByteArray.create(0)))
end 'swapTernary'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))
	let shelf = Shelf.holding(ByteArray.create(0))

	print("{swapPayload(holder, shelf: shelf)} {swapField(donor, shelf: shelf)} {swapTernary(donor, shelf: shelf, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:55: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:37: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:42:38: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-cannot-be-frozen -->
`freeze` makes its operand's region immutable for good, so an `isolated` part of a holder, bare, as a field or through a join, cannot be frozen while the holder still writes it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function freezePayload(holder Holder) returns immutable ByteArray
	match holder.slot 'take'
		full(bytes) then return freeze bytes
		empty then return freeze ByteArray.create(0)
	end 'take'
end 'freezePayload'

function freezeField(donor Shelf) returns immutable ByteArray
	return freeze donor.bytes
end 'freezeField'

function freezeTernary(donor Shelf, flag bool) returns immutable ByteArray
	return freeze (donor.bytes if flag else ByteArray.create(0))
end 'freezeTernary'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))

	print("{freezePayload(holder).count()} {freezeField(donor).count()} {freezeTernary(donor, flag: true).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:28:34: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:34:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:17: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-cannot-enter-a-tuple -->
A tuple element is a second path to what it holds, so an `isolated` part of a holder placed in a tuple literal, kept whole or destructured, does not become a clean input to an `isolated` parameter.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function viaTuple(donor Shelf) returns Count
	let pair = (donor.bytes, true)
	return weigh(pair.0)
end 'viaTuple'

function viaDestructure(donor Shelf) returns Count
	let (got, _) = (donor.bytes, true)
	return weigh(got)
end 'viaDestructure'

function main() returns ExitCode
	let donor = Shelf.holding(ByteArray.create(0))

	print("{viaTuple(donor)} {viaDestructure(donor)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:18:14: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:23:18: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-cannot-be-cast-as-isolated -->
`as isolated` claims a fresh region, so an `isolated` part of a holder, bare or through a join, cast to one is refused as the bare read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function castPayload(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(bytes as isolated ByteArray)
		empty then return 0
	end 'take'
end 'castPayload'

function castTernary(donor Shelf, flag bool) returns Count
	return weigh((donor.bytes if flag else ByteArray.create(0)) as isolated ByteArray)
end 'castTernary'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))

	print("{castPayload(holder)} {castTernary(donor, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:33: 'bytes as isolated ByteArray' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:16: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-assigned-to-an-isolated-var-in-a-loop-is-refused -->
An `isolated` local reassigned on a loop's back edge carries every value assigned to it into the next lap, so an `isolated` part of a holder assigned to it, bare or through a join, is refused where it is assigned.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function loopBare(donor Shelf, rounds Count) returns Count
	var cur = ByteArray.create(0) as isolated ByteArray
	var total = 0 as Count
	var lap = 0 as Count

	while lap < rounds 'each'
		total = total + weigh(cur)
		cur = donor.bytes
		lap = lap + 1
	end 'each'

	return total
end 'loopBare'

function loopTernary(donor Shelf, rounds Count) returns Count
	var cur = ByteArray.create(0) as isolated ByteArray
	var total = 0 as Count
	var lap = 0 as Count

	while lap < rounds 'each'
		total = total + weigh(cur)
		cur = donor.bytes if lap > 0 else ByteArray.create(1)
		lap = lap + 1
	end 'each'

	return total
end 'loopTernary'

function main() returns ExitCode
	let donor = Shelf.holding(ByteArray.create(0))

	print("{loopBare(donor, rounds: 2)} {loopTernary(donor, rounds: 2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:24:9: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:9: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-cannot-reach-a-plain-parameter-that-keeps-it -->
A plain parameter whose callee keeps it in a record or hands it back gives the caller a writable path into the region the holder still owns, so an `isolated` part of a holder passed to one is refused as the bare read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

type Pair
	export var a as ByteArray

	static function holding(a ByteArray) returns Self
		return Self{a: a}
	end 'holding'
end 'Pair'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function keepPayload(holder Holder) returns Pair
	match holder.slot 'take'
		full(bytes) then return Pair.holding(bytes)
		empty then return Pair.holding(ByteArray.create(0))
	end 'take'
end 'keepPayload'

function keepField(donor Shelf) returns Pair
	return Pair.holding(donor.bytes)
end 'keepField'

function echoField(donor Shelf) returns ByteArray
	return echo(donor.bytes)
end 'echoField'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))

	keepPayload(holder).a.push(1)
	keepField(donor).a.push(1)
	echoField(donor).push(1)
	print("done\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:40:32: 'holding' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:46:14: 'holding' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:50:9: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.an-isolated-part-read-through-a-generic-identity-cannot-reach-an-isolated-slot -->
A generic function that hands back its argument hands back the region it was given, so an `isolated` part of a holder passed through one is still that part: it is refused at an `isolated` parameter and at an `isolated` field store.

```maxon
typealias Tally = int(0 to 1024)

type Blob
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Blob'

union Crate
	full(blob isolated Blob)
	empty
end 'Crate'

type Holder
	export var crate as Crate

	static function create() returns Self
		return Self{crate: Crate.full(Blob.create(1))}
	end 'create'
end 'Holder'

type Shelf
	export var blob as isolated Blob

	static function holding(blob isolated Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Shelf'

function ident(x T) uses T returns T
	return x
end 'ident'

function weigh(blob isolated Blob) returns Tally
	return blob.n
end 'weigh'

function viaGeneric(holder Holder) returns Tally
	match holder.crate 'take'
		full(blob) then return weigh(ident(blob))
		empty then return 0
	end 'take'
end 'viaGeneric'

function viaGenericField(donor Shelf, shelf Shelf)
	shelf.blob = ident(donor.blob)
end 'viaGenericField'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(Blob.create(2))
	let shelf = Shelf.holding(Blob.create(3))

	print("{viaGeneric(holder)}\n")
	viaGenericField(donor, shelf: shelf)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:43:32: 'ident' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:49:15: 'ident' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-function-value-or-interface-result-is-not-a-clean-input -->
A function value can hand back what it captured and a `mutable self` requirement can hand back its receiver's data, so a call through a function-typed parameter or an interface value is no clean call: its result is refused at an `isolated` field store.

```maxon
typealias Tally = int(0 to 1024)

type Blob
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Blob'

typealias Maker = function() returns Blob

interface Source
	function make(mutable self) returns Blob
end 'Source'

type Pantry implements Source
	var stock as Blob

	static function create() returns Self
		return Self{stock: Blob.create(2)}
	end 'create'

	function make() returns Blob
		return self.stock
	end 'make'
end 'Pantry'

type Shelf
	export var blob as isolated Blob

	static function holding(blob isolated Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Shelf'

function storeFromMaker(make Maker, shelf Shelf)
	shelf.blob = make()
end 'storeFromMaker'

function storeFromSource(source Source, shelf Shelf)
	shelf.blob = source.make()
end 'storeFromSource'

function main() returns ExitCode
	let mine = Blob.create(1)
	let pantry = Pantry.create()
	let shelf = Shelf.holding(Blob.create(0))

	storeFromMaker(function() gives mine, shelf: shelf)
	storeFromSource(pantry, shelf: shelf)
	print("done\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:39:15: 'make()' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Blob`, or store a copy
error E3200: <fragment>:43:15: 'source.make()' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated Blob`, or store a copy
```

<!-- test: error.an-isolated-part-read-reaches-a-callee-declared-later-as-one-declared-earlier -->
Whether a callee is declared before or after its caller does not change what its `isolated` parameter takes: an `isolated` part of a holder given to one declared later, by a plain or an `async` call, directly or through a helper that hands its argument back, is refused exactly as for one declared earlier.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function echoEarly(bytes ByteArray) returns ByteArray
	return bytes
end 'echoEarly'

function weighEarly(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weighEarly'

function asyncEarly(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weighEarly(bytes)
		empty then return 0
	end 'take'
end 'asyncEarly'

function asyncLate(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weighLate(bytes)
		empty then return 0
	end 'take'
end 'asyncLate'

function plainEarlyTernary(donor Shelf, flag bool) returns Count
	return weighEarly(donor.bytes if flag else ByteArray.create(0))
end 'plainEarlyTernary'

function plainLateTernary(donor Shelf, flag bool) returns Count
	return weighLate(donor.bytes if flag else ByteArray.create(0))
end 'plainLateTernary'

function asyncEarlyEcho(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weighEarly(echoEarly(bytes))
		empty then return 0
	end 'take'
end 'asyncEarlyEcho'

function asyncLateEcho(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return await async weighLate(echoLate(bytes))
		empty then return 0
	end 'take'
end 'asyncLateEcho'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(ByteArray.create(0))

	print("{asyncEarly(holder)} {asyncLate(holder)} {plainEarlyTernary(donor, flag: true)} {plainLateTernary(donor, flag: true)}\n")
	print("{asyncEarlyEcho(holder)} {asyncLateEcho(holder)}\n")
	return 0
end 'main'

function echoLate(bytes ByteArray) returns ByteArray
	return bytes
end 'echoLate'

function weighLate(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weighLate'
```
```maxoncstderr
error E3198: <fragment>:36:50: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:43:49: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:49:20: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:53:19: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3201: <fragment>:58:50: 'echoEarly' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:65:49: 'echoLate' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-field-read-through-an-isolated-field-is-a-read-of-the-part -->
A field of the record an `isolated` field holds lives in that field's region, so reading one through the holder outside a `modify` block, to bind it, store it in a plain field or return it, lets it outlive the statement that opens the part, and each is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Crate
	export var blob as isolated Blob

	static function holding(blob isolated Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Crate'

type Pair
	export var a as ByteArray

	static function holding(a ByteArray) returns Self
		return Self{a: a}
	end 'holding'
end 'Pair'

function bindDeep(crate Crate) returns Count
	let d = crate.blob.data
	d.push(1)
	return d.count()
end 'bindDeep'

function storeDeep(crate Crate, pair Pair)
	pair.a = crate.blob.data
end 'storeDeep'

function returnDeep(crate Crate) returns ByteArray
	return crate.blob.data
end 'returnDeep'

function main() returns ExitCode
	let crate = Crate.holding(Blob.create())
	let pair = Pair.holding(ByteArray.create(0))

	storeDeep(crate, pair: pair)
	print("{bindDeep(crate)} {returnDeep(crate).count()} {pair.a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3202: <fragment>:30:10: 'crate.blob.data' is mutable and cannot leave the statement that opens 'blob'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
error E3197: <fragment>:36:2: cannot write 'a' through 'pair', which is read-only while 'blob' is open
error E3202: <fragment>:40:9: 'crate.blob.data' is mutable and cannot leave the statement that opens 'blob'; a value leaving a one-statement open must be a value, immutable or a fresh isolated
```

<!-- test: error.an-element-got-from-an-isolated-array-field-cannot-be-returned-as-plain-data -->
An element of an array an `isolated` field holds lives in that field's region, so returning one that `get` read through the field outside a `modify` block as plain data is a read of the `isolated` part.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

function returnByGet(bin Bin) returns Blob
	return try bin.blobs.get(0) otherwise Blob.create()
end 'returnByGet'

function main() returns ExitCode
	let bin = Bin.holding(BlobArray.create(0))

	print("{returnByGet(bin).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:9: 'try bin.blobs.get(0) otherwise Blob.create()' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-handed-back-by-a-closure-is-a-read-of-the-part -->
A closure that hands back an `isolated` field of a holder makes the part readable wherever the closure is called, so the closure's body reading the field outside a `modify` block is refused as a direct read is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

typealias Maker = function() returns ByteArray

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function leakField(donor Shelf) returns ByteArray
	return apply(function() gives donor.bytes)
end 'leakField'

function bindField(donor Shelf) returns Count
	let got = apply(function() gives donor.bytes)
	got.push(1)
	return got.count()
end 'bindField'

function main() returns ExitCode
	let donor = Shelf.holding(ByteArray.create(0))

	print("{leakField(donor).count()} {bindField(donor)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:20:32: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:24:35: 'donor.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-nested-isolated-field-read-inside-an-outer-window-is-a-part-read -->
Opening an `isolated` local with `modify` opens its own region, not the `isolated` regions its records hold: a nested `isolated` field read inside the window and handed to an `isolated` parameter, to `merge` or to a binding is still a part read.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var bytes as isolated ByteArray

	static function holding(bytes isolated ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Shelf'

type Rack
	export var shelf as Shelf

	static function create() returns Self
		return Self{shelf: Shelf.holding(ByteArray.create(0))}
	end 'create'
end 'Rack'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function moveNested() returns Count
	let rack = Rack.create() as isolated Rack
	var total = 0 as Count

	modify rack 'open'
		total = weigh(rack.shelf.bytes)
	end 'open'

	return total
end 'moveNested'

function mergeNested() returns Count
	let rack = Rack.create() as isolated Rack
	var total = 0 as Count

	modify rack 'open'
		total = (merge rack.shelf.bytes).count()
	end 'open'

	return total
end 'mergeNested'

function bindNested() returns Count
	let rack = Rack.create() as isolated Rack
	var total = 0 as Count

	modify rack 'open'
		let got = rack.shelf.bytes
		total = got.count()
	end 'open'

	return total
end 'bindNested'

function main() returns ExitCode
	print("{moveNested()} {mergeNested()} {bindNested()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:30:17: 'rack.shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:41:12: 'rack.shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:52:13: 'rack.shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-call-result-of-a-parameters-data-is-judged-alike-whichever-order-the-callee-is-declared -->
A call result whose callee hands back its parameter's plain field is that parameter's data, so it is refused at an `isolated` field store, an `isolated` parameter and an `isolated` return, whether the callee is declared before its caller or after.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Box
	export var plain as ByteArray
	export var part as isolated ByteArray

	static function create() returns Self
		return Self{plain: ByteArray.create(0), part: ByteArray.create(0)}
	end 'create'
end 'Box'

function takeEarly(b Box) returns ByteArray
	return b.plain
end 'takeEarly'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function storeEarly(src Box, dst Box)
	dst.part = takeEarly(src)
end 'storeEarly'

function storeLate(src Box, dst Box)
	dst.part = takeLate(src)
end 'storeLate'

function argEarly(src Box) returns Count
	return weigh(takeEarly(src))
end 'argEarly'

function argLate(src Box) returns Count
	return weigh(takeLate(src))
end 'argLate'

function giveEarly(src Box) returns isolated ByteArray
	return takeEarly(src)
end 'giveEarly'

function giveLate(src Box) returns isolated ByteArray
	return takeLate(src)
end 'giveLate'

function main() returns ExitCode
	let src = Box.create()
	let dst = Box.create()

	storeEarly(src, dst: dst)
	storeLate(src, dst: dst)
	print("{argEarly(src)} {argLate(src)} {weigh(giveEarly(src))} {weigh(giveLate(src))}\n")
	return 0
end 'main'

function takeLate(b Box) returns ByteArray
	return b.plain
end 'takeLate'
```
```maxoncstderr
error E3200: <fragment>:23:13: 'takeEarly(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated ByteArray`, or store a copy
error E3200: <fragment>:27:13: 'takeLate(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; declare it `as isolated ByteArray`, or store a copy
error E3200: <fragment>:31:15: 'takeEarly(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
error E3200: <fragment>:35:15: 'takeLate(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
error E3200: <fragment>:39:9: 'takeEarly(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated ByteArray`, or pass a copy
error E3200: <fragment>:43:9: 'takeLate(src)' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated ByteArray`, or pass a copy
```

<!-- test: error.an-element-of-an-isolated-array-field-cannot-leave-a-for-loop-into-outside-data -->
A `for` loop over an `isolated` array field outside a `modify` block reads the field's region, so an element it hands to data outside the region, a plain array or a parameter's plain field, is a second path into that region and is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var blob as Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Pocket'

function keepByFor(bin Bin, keep BlobArray)
	for blob in bin.blobs 'each'
		keep.push(blob)
	end 'each'
end 'keepByFor'

function storeByFor(bin Bin, pocket Pocket)
	for blob in bin.blobs 'each'
		pocket.blob = blob
	end 'each'
end 'storeByFor'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = BlobArray.create(0)
	let pocket = Pocket.create()

	keepByFor(bin, keep: keep)
	storeByFor(bin, pocket: pocket)
	print("{keep.count()} {pocket.blob.data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:31:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:37:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-for-loop-over-an-isolated-array-field-hands-out-copies-and-values -->
The same loops compile when what leaves the region is a copy or a value: a `.clone()` of each element pushed into a plain array or stored into a plain field, and a count summed into a local.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var blob as Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Pocket'

function keepByFor(bin Bin, keep BlobArray)
	for blob in bin.blobs 'each'
		keep.push(blob.clone())
	end 'each'
end 'keepByFor'

function storeByFor(bin Bin, pocket Pocket)
	for blob in bin.blobs 'each'
		pocket.blob = blob.clone()
	end 'each'
end 'storeByFor'

function weighByFor(bin Bin) returns Count
	var total = 0 as Count

	for blob in bin.blobs 'each'
		total = total + 1 + blob.data.count()
	end 'each'

	return total
end 'weighByFor'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = BlobArray.create(0)
	let pocket = Pocket.create()

	keepByFor(bin, keep: keep)
	storeByFor(bin, pocket: pocket)
	print("{keep.count()} {pocket.blob.data.count()} {weighByFor(bin)}\n")
	return 0
end 'main'
```
```stdout
1 0 1
```
```exitcode
0
```

<!-- test: error.a-closure-over-an-isolated-payload-cannot-hand-it-out-as-plain-data -->
A closure that captures an `isolated` payload of a holder's union hands that region back from every call, so returning the call's result as plain data, storing it into a plain field, or returning or storing the closure itself gives a second path into the region the holder still owns, and each is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Pair
	export var a as ByteArray
	export var make as Maker

	static function create() returns Self
		return Self{a: ByteArray.create(0), make: function() gives ByteArray.create(0)}
	end 'create'
end 'Pair'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function returnResult(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return apply(function() gives bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnResult'

function storeResult(holder Holder, pair Pair)
	match holder.slot 'take'
		full(bytes) then pair.a = apply(function() gives bytes)
		empty then print("empty\n")
	end 'take'
end 'storeResult'

function returnClosure(holder Holder) returns Maker
	match holder.slot 'take'
		full(bytes) then return function() gives bytes
		empty then return function() gives ByteArray.create(0)
	end 'take'
end 'returnClosure'

function storeClosure(holder Holder, pair Pair)
	match holder.slot 'take'
		full(bytes) then pair.make = function() gives bytes
		empty then print("empty\n")
	end 'take'
end 'storeClosure'

function main() returns ExitCode
	let holder = Holder.create()
	let pair = Pair.create()

	returnResult(holder).push(1)
	storeResult(holder, pair: pair)
	pair.a.push(2)
	returnClosure(holder)().push(3)
	storeClosure(holder, pair: pair)
	pair.make().push(4)
	print("done\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:35:50: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:42:52: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:49:44: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:56:49: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-closure-over-a-fresh-isolated-payload-hands-it-out-as-plain-data -->
The payload a `match` moves out of a local `isolated` union is that call's own region, so the same closures over it hand it out as plain data without a refusal.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Pair
	export var a as ByteArray
	export var make as Maker

	static function create() returns Self
		return Self{a: ByteArray.create(0), make: function() gives ByteArray.create(0)}
	end 'create'
end 'Pair'

function makeSlot() returns isolated Slot
	return Slot.full(ByteArray.create(2))
end 'makeSlot'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function returnResult() returns ByteArray
	let made = makeSlot()

	match made 'take'
		full(bytes) then return apply(function() gives bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnResult'

function storeResult(pair Pair)
	let made = makeSlot()

	match made 'take'
		full(bytes) then pair.a = apply(function() gives bytes)
		empty then print("empty\n")
	end 'take'
end 'storeResult'

function returnClosure() returns Maker
	let made = makeSlot()

	match made 'take'
		full(bytes) then return function() gives bytes
		empty then return function() gives ByteArray.create(0)
	end 'take'
end 'returnClosure'

function storeClosure(pair Pair)
	let made = makeSlot()

	match made 'take'
		full(bytes) then pair.make = function() gives bytes
		empty then print("empty\n")
	end 'take'
end 'storeClosure'

function main() returns ExitCode
	let pair = Pair.create()
	let one = returnResult()
	one.push(1)
	storeResult(pair)
	pair.a.push(2)
	let two = returnClosure()()
	two.push(3)
	storeClosure(pair)
	let three = pair.make()
	three.push(4)
	print("{one.count()} {pair.a.count()} {two.count()} {three.count()}\n")
	return 0
end 'main'
```
```stdout
3 3 3 3
```
```exitcode
0
```

<!-- test: error.an-isolated-part-read-cannot-be-stored-into-a-plain-field-or-a-plain-variable -->
A part of a holder read outside a `modify` block is a second path into the region the holder still owns, so storing it into a plain field or reassigning a plain variable with it is refused as a part read, as a store into an `isolated` slot is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var part as isolated ByteArray
	export var plain as ByteArray

	static function create() returns Self
		return Self{part: ByteArray.create(0), plain: ByteArray.create(0)}
	end 'create'
end 'Shelf'

function storeIntoField(shelf Shelf)
	shelf.plain = shelf.part
end 'storeIntoField'

function reassignVariable(shelf Shelf) returns Count
	var held = ByteArray.create(0)
	held = shelf.part
	return held.count()
end 'reassignVariable'

function main() returns ExitCode
	let shelf = Shelf.create()

	storeIntoField(shelf)
	print("{reassignVariable(shelf)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:15:16: 'shelf.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:20:9: 'shelf.part' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-part-of-a-holder-taken-out-by-swap-reaches-a-plain-field-and-a-plain-variable -->
The same two doors take the region once it is taken out of the holder by `swap` and merged.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Shelf
	export var part as isolated ByteArray
	export var plain as ByteArray

	static function create() returns Self
		return Self{part: ByteArray.create(2), plain: ByteArray.create(0)}
	end 'create'
end 'Shelf'

function storeIntoField(shelf Shelf)
	let taken = swap shelf.part with ByteArray.create(0)
	shelf.plain = merge taken
end 'storeIntoField'

function reassignVariable(shelf Shelf) returns Count
	var held = ByteArray.create(0)
	let taken = swap shelf.part with ByteArray.create(0)
	held = merge taken
	return held.count()
end 'reassignVariable'

function main() returns ExitCode
	let shelf = Shelf.create()

	storeIntoField(shelf)
	print("{shelf.plain.count()} {reassignVariable(shelf)}\n")
	return 0
end 'main'
```
```stdout
2 0
```
```exitcode
0
```

<!-- test: a-payload-part-read-inside-an-open-window-is-used-in-place -->
A payload of a union held by an `isolated` field, matched inside the `modify` block that opens the field, is read and written in place without leaving the block.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as isolated Slot

	static function holding(slot isolated Slot) returns Self
		return Self{slot: slot}
	end 'holding'
end 'Holder'

function weigh(holder Holder) returns Count
	var total = 0 as Count

	modify holder.slot 'open'
		match holder.slot 'take'
			full(bytes) then total = bytes.count()
			empty then total = 0
		end 'take'
	end 'open'

	return total
end 'weigh'

function grow(holder Holder)
	modify holder.slot 'open'
		match holder.slot 'take'
			full(bytes) then bytes.push(7)
			empty then print("empty\n")
		end 'take'
	end 'open'
end 'grow'

function main() returns ExitCode
	let holder = Holder.holding(Slot.full(ByteArray.create(2)))

	print("{weigh(holder)}\n")
	grow(holder)
	print("{weigh(holder)}\n")
	return 0
end 'main'
```
```stdout
2
3
```
```exitcode
0
```

<!-- test: error.an-element-reached-by-an-iterator-over-an-isolated-array-field-cannot-leave-into-outside-data -->
An iterator made over an `isolated` array field outside a `modify` block reads the field's region, so an element it hands out, by `current` or by a `withIterator` loop, pushed into an array outside the region is a second path into it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

function keepByIterator(bin Bin, keep BlobArray)
	let it = try bin.blobs.createIterator() otherwise return
	keep.push(it.current())
end 'keepByIterator'

function keepByPairs(bin Bin, keep BlobArray)
	for (_, blob) in bin.blobs.withIterator() 'each'
		keep.push(blob)
	end 'each'
end 'keepByPairs'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = BlobArray.create(0)

	keepByIterator(bin, keep: keep)
	keepByPairs(bin, keep: keep)
	print("{keep.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:23:25: 'createIterator' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:28:29: 'withIterator' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.an-element-of-an-isolated-list-or-map-field-cannot-leave-into-outside-data -->
A `for` loop over an `isolated` `List` or `Map` field, and a `get` from the map, read the field's region outside a `modify` block, so an element pushed into an outside array or returned as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias BlobList = List with Blob
typealias BlobMap = Map with (String, Blob)

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var list as isolated BlobList
	export var index as isolated BlobMap

	static function create() returns Self
		return Self{list: BlobList.create(), index: BlobMap.create()}
	end 'create'
end 'Bin'

function keepFromList(bin Bin, keep BlobArray)
	for blob in bin.list 'each'
		keep.push(blob)
	end 'each'
end 'keepFromList'

function getFromMap(bin Bin) returns Blob
	return try bin.index.get("a") otherwise Blob.create()
end 'getFromMap'

function main() returns ExitCode
	let bin = Bin.create()
	let keep = BlobArray.create(0)

	keepFromList(bin, keep: keep)
	print("{keep.count()} {getFromMap(bin).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:14: 'bin.list' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3201: <fragment>:32:23: 'get' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-value-of-an-isolated-map-field-cannot-leave-a-for-loop-into-outside-data -->
A `for` loop over an `isolated` `Map` field outside a `modify` block reads the field's region as a loop over an `isolated` array or `List` field does, so a value it hands out pushed into an outside array, stored into a plain field or returned from inside the loop is a second path into that region and is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias BlobMap = Map with (String, Blob)

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var index as isolated BlobMap

	static function create() returns Self
		return Self{index: BlobMap.create()}
	end 'create'
end 'Bin'

type Pocket
	export var blob as Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Pocket'

function keepFromMap(bin Bin, keep BlobArray)
	for (_, blob) in bin.index 'each'
		keep.push(blob)
	end 'each'
end 'keepFromMap'

function storeFromMap(bin Bin, pocket Pocket)
	for (_, blob) in bin.index 'each'
		pocket.blob = blob
	end 'each'
end 'storeFromMap'

function firstFromMap(bin Bin) returns Blob
	for (_, blob) in bin.index 'each'
		return blob
	end 'each'

	return Blob.create()
end 'firstFromMap'

function main() returns ExitCode
	let bin = Bin.create()
	let keep = BlobArray.create(0)
	let pocket = Pocket.create()

	keepFromMap(bin, keep: keep)
	storeFromMap(bin, pocket: pocket)
	print("{keep.count()} {pocket.blob.data.count()} {firstFromMap(bin).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:19: 'bin.index' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:19: 'bin.index' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:44:19: 'bin.index' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: an-isolated-list-or-map-field-hands-out-copies-and-values -->
The same loops compile when what leaves the region is a copy or a value.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias BlobList = List with Blob
typealias BlobMap = Map with (String, Blob)

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var list as isolated BlobList
	export var index as isolated BlobMap

	static function create() returns Self
		return Self{list: BlobList.create(), index: BlobMap.create()}
	end 'create'
end 'Bin'

function keepFromList(bin Bin, keep BlobArray)
	for blob in bin.list 'each'
		keep.push(blob.clone())
	end 'each'
end 'keepFromList'

function weighMap(bin Bin) returns Count
	var total = 0 as Count

	for (_, blob) in bin.index 'each'
		total = total + blob.data.count()
	end 'each'

	return total
end 'weighMap'

function main() returns ExitCode
	let bin = Bin.create()
	let keep = BlobArray.create(0)

	keepFromList(bin, keep: keep)
	print("{keep.count()} {weighMap(bin)}\n")
	return 0
end 'main'
```
```stdout
0 0
```
```exitcode
0
```

<!-- test: error.a-method-of-an-isolated-part-that-hands-back-its-own-data-is-a-read-of-the-part -->
A method that hands back data of its receiver, called on an `isolated` field or payload of a holder outside a `modify` block, hands back that part's region, so returning its result as plain data or storing it into a plain field is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

union Crate
	full(blob isolated Blob)
	empty
end 'Crate'

type Holder
	export var crate as Crate
	export var blob as isolated Blob

	static function create() returns Self
		return Self{crate: Crate.full(Blob.create()), blob: Blob.create()}
	end 'create'
end 'Holder'

type Pair
	export var a as ByteArray

	static function create() returns Self
		return Self{a: ByteArray.create(0)}
	end 'create'
end 'Pair'

function returnFieldInner(holder Holder) returns ByteArray
	return holder.blob.inner()
end 'returnFieldInner'

function storeFieldInner(holder Holder, pair Pair)
	pair.a = holder.blob.inner()
end 'storeFieldInner'

function returnPayloadInner(holder Holder) returns ByteArray
	match holder.crate 'take'
		full(blob) then return blob.inner()
		empty then return ByteArray.create(0)
	end 'take'
end 'returnPayloadInner'

function storePayloadInner(holder Holder, pair Pair)
	match holder.crate 'take'
		full(blob) then pair.a = blob.inner()
		empty then print("empty\n")
	end 'take'
end 'storePayloadInner'

function main() returns ExitCode
	let holder = Holder.create()
	let pair = Pair.create()

	storeFieldInner(holder, pair: pair)
	storePayloadInner(holder, pair: pair)
	print("{returnFieldInner(holder).count()} {returnPayloadInner(holder).count()} {pair.a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:40:21: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:44:23: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:49:31: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:56:33: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: values-read-from-an-isolated-part-leave-freely -->
What leaves an `isolated` field or payload read outside a `modify` block as a value is no second path into its region: a scalar field, a count, a read-only helper's count, a comparison and a printed value are each accepted, bound, assigned to a plain variable or returned.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray
	export var n as Count

	static function create(n Count) returns Self
		return Self{data: ByteArray.create(2), n: n}
	end 'create'
end 'Blob'

union Crate
	full(blob isolated Blob)
	empty
end 'Crate'

type Holder
	export var crate as Crate
	export var blob as isolated Blob

	static function create() returns Self
		return Self{crate: Crate.full(Blob.create(3)), blob: Blob.create(4)}
	end 'create'
end 'Holder'

function countOf(bytes ByteArray) returns Count
	return bytes.count()
end 'countOf'

function fieldValues(holder Holder) returns Count
	let n = holder.blob.n
	var total = 0 as Count
	total = holder.blob.data.count()
	let viaHelper = countOf(holder.blob.data)

	if holder.blob.data.count() == viaHelper 'same'
		print("{holder.blob.n} {holder.blob.data.count()}\n")
	end 'same'

	return n + total + viaHelper
end 'fieldValues'

function payloadValue(holder Holder) returns Count
	match holder.crate 'take'
		full(blob) then return blob.n + blob.data.count() + countOf(blob.data)
		empty then return 0
	end 'take'
end 'payloadValue'

function main() returns ExitCode
	let holder = Holder.create()

	print("{fieldValues(holder)} {payloadValue(holder)}\n")
	return 0
end 'main'
```
```stdout
4 2
8 7
```
```exitcode
0
```

<!-- test: a-count-of-what-a-method-of-an-isolated-part-hands-back-is-a-value -->
A method that hands back its receiver's data, called on an `isolated` field or payload outside a `modify` block, hands back mutable data of the part, but a count of that data taken in the same statement is a value and leaves no path into the region: assigning it to a plain variable, binding a read-only helper's count of it, and returning it are each accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray
	export var n as Count

	static function create(n Count) returns Self
		return Self{data: ByteArray.create(2), n: n}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

union Crate
	full(blob isolated Blob)
	empty
end 'Crate'

type Holder
	export var crate as Crate
	export var blob as isolated Blob

	static function create() returns Self
		return Self{crate: Crate.full(Blob.create(3)), blob: Blob.create(4)}
	end 'create'
end 'Holder'

function countOf(bytes ByteArray) returns Count
	return bytes.count()
end 'countOf'

function assignedCount(holder Holder) returns Count
	var total = 0 as Count
	total = holder.blob.inner().count()
	return total
end 'assignedCount'

function helperCount(holder Holder) returns Count
	let viaMethod = countOf(holder.blob.inner())
	return viaMethod
end 'helperCount'

function payloadCount(holder Holder) returns Count
	match holder.crate 'take'
		full(blob) then return blob.n + countOf(blob.inner())
		empty then return 0
	end 'take'
end 'payloadCount'

function main() returns ExitCode
	let holder = Holder.create()

	print("{assignedCount(holder)} {helperCount(holder)} {payloadCount(holder)}\n")
	return 0
end 'main'
```
```stdout
2 2 5
```
```exitcode
0
```

<!-- test: error.an-isolated-payload-read-through-two-nested-helpers-is-a-read-of-the-part -->
A helper that may hand back its argument hands back the part it was given however many times it is applied, so an `isolated` payload passed through two nested ones is refused at a plain return, a plain field store and an `isolated` parameter.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Pair
	export var a as ByteArray

	static function create() returns Self
		return Self{a: ByteArray.create(0)}
	end 'create'
end 'Pair'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function returnTwice(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return echo(echo(bytes))
		empty then return ByteArray.create(0)
	end 'take'
end 'returnTwice'

function storeTwice(holder Holder, pair Pair)
	match holder.slot 'take'
		full(bytes) then pair.a = echo(echo(bytes))
		empty then print("empty\n")
	end 'take'
end 'storeTwice'

function weighTwice(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(echo(echo(bytes)))
		empty then return 0
	end 'take'
end 'weighTwice'

function main() returns ExitCode
	let holder = Holder.create()
	let pair = Pair.create()

	storeTwice(holder, pair: pair)
	print("{returnTwice(holder).count()} {weighTwice(holder)} {pair.a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:36:32: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:43:34: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:50:38: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: a-clone-of-an-isolated-part-read-is-a-clean-value -->
A `.clone()` of an `isolated` field or payload read outside a `modify` block, or of a value `get` reads from an `isolated` map field, hands back a region of its own, so the copy reaches an `isolated` parameter, an `isolated` field, a plain field and a plain return.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BytesMap = Map with (String, ByteArray)

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot
	export var bytes as isolated ByteArray
	export var plain as ByteArray
	export var index as isolated BytesMap

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(2)), bytes: ByteArray.create(3), plain: ByteArray.create(0), index: BytesMap.create()}
	end 'create'
end 'Holder'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function weighPayload(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(bytes.clone())
		empty then return 0
	end 'take'
end 'weighPayload'

function storeIsolated(holder Holder, other Holder)
	other.bytes = holder.bytes.clone()
end 'storeIsolated'

function storePlain(holder Holder, other Holder)
	other.plain = holder.bytes.clone()
end 'storePlain'

function returnField(holder Holder) returns ByteArray
	return holder.bytes.clone()
end 'returnField'

function returnMapValue(holder Holder) returns ByteArray
	return (try holder.index.get("a") otherwise ByteArray.create(0)).clone()
end 'returnMapValue'

function main() returns ExitCode
	let holder = Holder.create()
	let other = Holder.create()

	storeIsolated(holder, other: other)
	storePlain(holder, other: other)
	print("{weighPayload(holder)} {returnField(holder).count()} {other.bytes.count()} {other.plain.count()} {returnMapValue(holder).count()}\n")
	return 0
end 'main'
```
```stdout
2 3 3 3 0
```
```exitcode
0
```

<!-- test: a-returns-isolated-call-over-an-isolated-part-read-is-a-clean-value -->
A function declared `returns isolated` hands back a region of its own whatever it reads, so its result over an `isolated` field or payload reaches an `isolated` parameter, an `isolated` field, a plain field once merged, and a plain return once merged.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot
	export var bytes as isolated ByteArray
	export var plain as ByteArray

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(2)), bytes: ByteArray.create(3), plain: ByteArray.create(0)}
	end 'create'
end 'Holder'

function copyOf(bytes ByteArray) returns isolated ByteArray
	return bytes.clone()
end 'copyOf'

function weigh(bytes isolated ByteArray) returns Count
	return bytes.count()
end 'weigh'

function weighPayload(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return weigh(copyOf(bytes))
		empty then return 0
	end 'take'
end 'weighPayload'

function returnPayload(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return merge copyOf(bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnPayload'

function storeField(holder Holder, other Holder)
	other.bytes = copyOf(holder.bytes)
	other.plain = merge copyOf(holder.bytes)
end 'storeField'

function main() returns ExitCode
	let holder = Holder.create()
	let other = Holder.create()

	storeField(holder, other: other)
	print("{weighPayload(holder)} {returnPayload(holder).count()} {other.bytes.count()} {other.plain.count()}\n")
	return 0
end 'main'
```
```stdout
2 2 3 3
```
```exitcode
0
```

<!-- test: error.a-closure-over-an-isolated-payload-given-by-a-match-expression-cannot-be-bound -->
A closure that a `match` expression's arm builds over an `isolated` payload of a holder is that payload at every exit, so binding it to a local, for another closure to capture and a call to hand on, is refused at the payload's read.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Pair
	export var a as ByteArray

	static function create() returns Self
		return Self{a: ByteArray.create(0)}
	end 'create'
end 'Pair'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function storeNested(holder Holder, pair Pair)
	let inner = match holder.slot 'pick'
		full(bytes) gives function() gives bytes
		empty gives function() gives ByteArray.create(0)
	end 'pick'
	let outer = function() gives apply(inner)
	pair.a = apply(outer)
end 'storeNested'

function returnNested(holder Holder) returns ByteArray
	let inner = match holder.slot 'pick'
		full(bytes) gives function() gives bytes
		empty gives function() gives ByteArray.create(0)
	end 'pick'
	let outer = function() gives apply(inner)
	return apply(outer)
end 'returnNested'

function main() returns ExitCode
	let holder = Holder.create()
	let pair = Pair.create()

	storeNested(holder, pair: pair)
	print("{returnNested(holder).count()} {pair.a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:34:38: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:43:38: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-closure-over-an-isolated-part-that-hands-back-a-count-is-accepted -->
A closure that captures an `isolated` field or payload but hands back only a count of it gives no path into the region, so a call over it is accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Counter = function() returns Count

type Holder
	export var slot as Slot
	export var bytes as isolated ByteArray

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(2)), bytes: ByteArray.create(3)}
	end 'create'
end 'Holder'

function tally(count Counter) returns Count
	return count()
end 'tally'

function payloadTally(holder Holder) returns Count
	match holder.slot 'take'
		full(bytes) then return tally(function() gives bytes.count())
		empty then return 0
	end 'take'
end 'payloadTally'

function fieldTally(holder Holder) returns Count
	let n = tally(function() gives holder.bytes.count())
	return n
end 'fieldTally'

function main() returns ExitCode
	let holder = Holder.create()

	print("{payloadTally(holder)} {fieldTally(holder)}\n")
	return 0
end 'main'
```
```stdout
2 3
```
```exitcode
0
```

<!-- test: error.an-isolated-payload-given-by-a-match-expression-in-a-loop-cannot-reach-a-plain-variable -->
A `match` expression whose arm gives an `isolated` payload of a holder, assigned to a plain variable on each lap of a loop and returned after it, hands the part out as plain data.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function lastPayload(holder Holder, rounds Count) returns ByteArray
	var got = ByteArray.create(0)

	for _ in 0 upto rounds 'each'
		got = match holder.slot 'take'
			full(bytes) gives bytes
			empty gives ByteArray.create(0)
		end 'take'
	end 'each'

	return got
end 'lastPayload'

function main() returns ExitCode
	let holder = Holder.create()

	print("{lastPayload(holder, rounds: 2).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:22: 'bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-unclean-argument-is-quoted-whole-across-nested-parens-strings-and-named-arguments -->
A refused argument is quoted from its first token to the comma or parenthesis that ends it at its own depth, so nested calls, string literals holding commas and parentheses, a closure with two parameters and named arguments neither cut it short nor run it on.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

typealias Adder = function(Count, Count) returns Count

type Shelf
	export var bytes as isolated ByteArray

	static function create() returns Self
		return Self{bytes: ByteArray.create(0)}
	end 'create'
end 'Shelf'

function pick(bytes ByteArray, label String) returns ByteArray
	if label == "c)" 'mine'
		return bytes
	end 'mine'

	return ByteArray.create(0)
end 'pick'

function sumWith(add Adder) returns Count
	return add(1, 2)
end 'sumWith'

function weighTagged(tag String, bytes isolated ByteArray, extra Count) returns Count
	if tag == "" 'none'
		return extra
	end 'none'

	return bytes.count() + extra
end 'weighTagged'

function viaParam(plain ByteArray) returns Count
	return weighTagged("a, (b", bytes: pick(plain, label: "c)"), extra: sumWith(function(a, b) gives a + b))
end 'viaParam'

function viaPart(shelf Shelf, flag bool) returns Count
	return weighTagged("(", bytes: shelf.bytes if sumWith(function(a, b) gives a + b) > 0 or flag else ByteArray.create(0), extra: 1)
end 'viaPart'

function main() returns ExitCode
	let shelf = Shelf.create()

	print("{viaParam(ByteArray.create(0))} {viaPart(shelf, flag: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:36:37: 'pick(plain, label: "c)")' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; pass a copy
error E3198: <fragment>:40:33: 'shelf.bytes' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-part-read-through-a-generic-helpers-second-parameter-is-a-read-of-the-part -->
A generic helper that may hand back either argument hands back the part given at its second parameter, so an `isolated` payload or field given there is refused at an `isolated` parameter and an `isolated` field store.

```maxon
typealias Tally = int(0 to 1024)

type Blob
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Blob'

union Crate
	full(blob isolated Blob)
	empty
end 'Crate'

type Holder
	export var crate as Crate

	static function create() returns Self
		return Self{crate: Crate.full(Blob.create(1))}
	end 'create'
end 'Holder'

type Shelf
	export var blob as isolated Blob

	static function holding(blob isolated Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Shelf'

function either(a T, b T, first bool) uses T returns T
	return a if first else b
end 'either'

function weigh(blob isolated Blob) returns Tally
	return blob.n
end 'weigh'

function viaPayload(holder Holder) returns Tally
	match holder.crate 'take'
		full(blob) then return weigh(either(Blob.create(0), b: blob, first: false))
		empty then return 0
	end 'take'
end 'viaPayload'

function viaField(donor Shelf, shelf Shelf)
	shelf.blob = either(Blob.create(0), b: donor.blob, first: false)
end 'viaField'

function main() returns ExitCode
	let holder = Holder.create()
	let donor = Shelf.holding(Blob.create(2))
	let shelf = Shelf.holding(Blob.create(3))

	print("{viaPayload(holder)}\n")
	viaField(donor, shelf: shelf)
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:43:32: 'either' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:49:15: 'either' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.an-isolated-payload-read-through-a-try-otherwise-call-is-a-read-of-the-part -->
A throwing helper that may hand back its argument, called under `try … otherwise` with an `isolated` payload of a holder, hands back the part, so returning the result as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum EchoError implements Error
	empty
end 'EchoError'

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function echoOrFail(bytes ByteArray, fail bool) returns ByteArray throws EchoError
	if fail 'refuse'
		throw EchoError.empty
	end 'refuse'

	return bytes
end 'echoOrFail'

function returnEcho(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return try echoOrFail(bytes, fail: false) otherwise ByteArray.create(0)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnEcho'

function main() returns ExitCode
	let holder = Holder.create()

	print("{returnEcho(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:32:31: 'echoOrFail' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```


<!-- test: error.a-field-of-an-element-a-for-loop-hands-out-over-an-isolated-array-field-cannot-leave -->
An element a `for` loop hands out over an `isolated` array field outside a `modify` block is in the field's region, and so is a field of that element, so pushing the field into an outside array, storing it into a plain field or returning it from inside the loop is a second path into the region and is refused, as the element itself is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias DataArray = Array with ByteArray

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var bytes as ByteArray

	static function create() returns Self
		return Self{bytes: ByteArray.create(0)}
	end 'create'
end 'Pocket'

function keepData(bin Bin, keep DataArray)
	for blob in bin.blobs 'each'
		keep.push(blob.data)
	end 'each'
end 'keepData'

function storeData(bin Bin, pocket Pocket)
	for blob in bin.blobs 'each'
		pocket.bytes = blob.data
	end 'each'
end 'storeData'

function firstData(bin Bin) returns ByteArray
	for blob in bin.blobs 'each'
		return blob.data
	end 'each'

	return ByteArray.create(0)
end 'firstData'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = DataArray.create(0)
	let pocket = Pocket.create()

	keepData(bin, keep: keep)
	storeData(bin, pocket: pocket)
	print("{keep.count()} {pocket.bytes.count()} {firstData(bin).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:32:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:38:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:44:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: a-for-loop-over-an-isolated-array-field-hands-out-copies-of-element-fields -->
The same loops compile when what leaves the region is a copy of the element's field or its count.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias DataArray = Array with ByteArray

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var bytes as ByteArray

	static function create() returns Self
		return Self{bytes: ByteArray.create(0)}
	end 'create'
end 'Pocket'

function keepData(bin Bin, keep DataArray)
	for blob in bin.blobs 'each'
		keep.push(blob.data.clone())
	end 'each'
end 'keepData'

function storeData(bin Bin, pocket Pocket)
	for blob in bin.blobs 'each'
		pocket.bytes = blob.data.clone()
	end 'each'
end 'storeData'

function firstCount(bin Bin) returns Count
	for blob in bin.blobs 'each'
		return blob.data.count()
	end 'each'

	return 0
end 'firstCount'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = DataArray.create(0)
	let pocket = Pocket.create()

	keepData(bin, keep: keep)
	storeData(bin, pocket: pocket)
	print("{keep.count()} {pocket.bytes.count()} {firstCount(bin)}\n")
	return 0
end 'main'
```
```stdout
1 1 1
```
```exitcode
0
```

<!-- test: error.an-element-returned-from-inside-a-for-loop-over-an-isolated-array-field-is-a-read-of-the-part -->
Returning an element from inside a `for` loop over an `isolated` array field outside a `modify` block hands the field's region out as plain data.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

function firstBlob(bin Bin) returns Blob
	for blob in bin.blobs 'each'
		return blob
	end 'each'

	return Blob.create()
end 'firstBlob'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)

	print("{firstBlob(bin).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-field-read-in-parentheses-cannot-be-iterated-into-outside-data -->
A `for` loop over an `isolated` array field written in parentheses reads the field as the bare spelling does, so an element it pushes into an outside array is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

function keepByFor(bin Bin, keep BlobArray)
	for blob in (bin.blobs) 'each'
		keep.push(blob)
	end 'each'
end 'keepByFor'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = BlobArray.create(0)

	keepByFor(bin, keep: keep)
	print("{keep.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:14: '(bin.blobs)' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-element-of-an-isolated-array-field-captured-by-a-closure-in-a-for-loop-cannot-leave -->
A closure built inside a `for` loop over an `isolated` array field captures the element the loop hands out, so the closure stored into a plain field, or what a call over it hands back pushed into an outside array, is a second path into the region.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

typealias Maker = function() returns Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(0)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var make as Maker

	static function create() returns Self
		return Self{make: function() gives Blob.create()}
	end 'create'
end 'Pocket'

function apply(make Maker) returns Blob
	return make()
end 'apply'

function keepByClosure(bin Bin, keep BlobArray)
	for blob in bin.blobs 'each'
		keep.push(apply(function() gives blob))
	end 'each'
end 'keepByClosure'

function storeClosure(bin Bin, pocket Pocket)
	for blob in bin.blobs 'each'
		pocket.make = function() gives blob
	end 'each'
end 'storeClosure'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let keep = BlobArray.create(0)
	let pocket = Pocket.create()

	keepByClosure(bin, keep: keep)
	storeClosure(bin, pocket: pocket)
	print("{keep.count()} {pocket.make().data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:38:36: 'blob' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3198: <fragment>:44:34: 'blob' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-isolated-payload-read-through-a-helper-cannot-enter-a-tuple-or-a-union -->
A helper that may hand back its argument hands back an `isolated` payload it is given, so its result placed in a tuple bound to a local or in a plain union payload returned is a read of the part.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

union Loose
	some(bytes ByteArray)
	none
end 'Loose'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function wrapLoose(holder Holder) returns Loose
	match holder.slot 'take'
		full(bytes) then return Loose.some(echo(bytes))
		empty then return Loose.none
	end 'take'
end 'wrapLoose'

function pairUp(holder Holder) returns (ByteArray, Count)
	match holder.slot 'take'
		full(bytes) then return (echo(bytes), 1)
		empty then return (ByteArray.create(0), 0)
	end 'take'
end 'pairUp'

function main() returns ExitCode
	let holder = Holder.create()

	match wrapLoose(holder) 'show'
		some(bytes) then print("{bytes.count()}\n")
		none then print("none\n")
	end 'show'

	let (got, n) = pairUp(holder)
	print("{got.count()} {n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:29:38: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:36:28: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-closure-over-an-isolated-payload-given-to-an-async-call-is-a-read-of-the-part -->
An `async` call is no door out of the part rule: a closure over an `isolated` payload of a holder, the payload itself and an `isolated` field are each refused at the spawn (E3207) when they reach a plain parameter, because an `async` call takes an isolated value only in an `isolated` parameter, which moves it.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

type Holder
	export var slot as Slot
	export var bytes as isolated ByteArray

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0)), bytes: ByteArray.create(0)}
	end 'create'
end 'Holder'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function returnFromCoroutine(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return await async apply(function() gives bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnFromCoroutine'

function returnEchoedPayload(holder Holder) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return await async echo(bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnEchoedPayload'

function returnEchoedField(holder Holder) returns ByteArray
	return await async echo(holder.bytes)
end 'returnEchoedField'

function main() returns ExitCode
	let holder = Holder.create()

	print("{returnFromCoroutine(holder).count()} {returnEchoedPayload(holder).count()} {returnEchoedField(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:45: 'apply' is spawned with `async`, and 'function' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
error E3207: <fragment>:38:44: 'echo' is spawned with `async`, and 'bytes' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
error E3207: <fragment>:44:26: 'echo' is spawned with `async`, and 'holder' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: a-coroutine-over-a-fresh-isolated-payload-hands-it-out-as-plain-data -->
The payload a `match` moves out of a local `isolated` union is that call's own region, so what a coroutine hands back from a closure over it is returned as plain data without a refusal.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

typealias Maker = function() returns ByteArray

function makeSlot() returns isolated Slot
	return Slot.full(ByteArray.create(2))
end 'makeSlot'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function returnFromCoroutine() returns ByteArray
	let made = makeSlot()

	match made 'take'
		full(bytes) then return await async apply(function() gives bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnFromCoroutine'

function main() returns ExitCode
	print("{returnFromCoroutine().count()}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: error.a-mutable-receiver-beside-an-isolated-payload-argument-is-named-whole-when-chained -->
A method call given an `isolated` payload of a holder opens that payload, so a mutable outside record as its receiver is refused beside it, and the refusal names the receiver as written, a chained call included.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

type Relay
	export var last as ByteArray

	static function create() returns Self
		return Self{last: ByteArray.create(0)}
	end 'create'

	function pass(bytes ByteArray) returns ByteArray
		return bytes
	end 'pass'

	function me() returns Relay
		return self
	end 'me'
end 'Relay'

function returnViaMethod(holder Holder, relay Relay) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return relay.me().pass(bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnViaMethod'

function storeViaMethod(holder Holder, relay Relay)
	match holder.slot 'take'
		full(bytes) then relay.last = relay.pass(bytes)
		empty then print("empty\n")
	end 'take'
end 'storeViaMethod'

function main() returns ExitCode
	let holder = Holder.create()
	let relay = Relay.create()

	storeViaMethod(holder, relay: relay)
	print("{returnViaMethod(holder, relay: relay).count()} {relay.last.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:36:27: 'relay.me()' is mutable outside 'bytes'; a call into an isolated region takes values, immutable or isolated
error E3201: <fragment>:43:33: 'relay' is mutable outside 'bytes'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: error.an-isolated-part-read-through-a-join-of-two-helper-calls-is-a-read-of-the-part -->
A conditional whose arms are two helpers that may hand back their arguments, one given an `isolated` payload and one given fresh data, is a read of the part at a plain return.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Slot
	full(bytes isolated ByteArray)
	empty
end 'Slot'

type Holder
	export var slot as Slot

	static function create() returns Self
		return Self{slot: Slot.full(ByteArray.create(0))}
	end 'create'
end 'Holder'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function returnJoin(holder Holder, flag bool) returns ByteArray
	match holder.slot 'take'
		full(bytes) then return echo(ByteArray.create(0)) if flag else echo(bytes)
		empty then return ByteArray.create(0)
	end 'take'
end 'returnJoin'

function main() returns ExitCode
	let holder = Holder.create()

	print("{returnJoin(holder, flag: false).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:24:66: 'echo' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.an-element-a-for-loop-hands-out-over-an-isolated-array-field-cannot-be-kept-by-a-call -->
An element a `for` loop hands out over an `isolated` array field outside a `modify` block is data of the field's region, so it is no more an ordinary argument than the field itself: given to a parameter that keeps it, to a constructor that stores it in the record it builds, or to an `async` call, it is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias PocketArray = Array with Pocket

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var blob as Blob

	static function holding(blob Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Pocket'

function keep(blob Blob, into PocketArray)
	into.push(Pocket.holding(blob))
end 'keep'

function weigh(blob Blob) returns Count
	return blob.data.count()
end 'weigh'

function keepEach(bin Bin, pockets PocketArray)
	for blob in bin.blobs 'each'
		keep(blob, into: pockets)
	end 'each'
end 'keepEach'

function holdEach(bin Bin, pockets PocketArray)
	for blob in bin.blobs 'each'
		pockets.push(Pocket.holding(blob))
	end 'each'
end 'holdEach'

function weighFirstAsync(bin Bin) returns Count
	for blob in bin.blobs 'each'
		return await async weigh(blob)
	end 'each'

	return 0
end 'weighFirstAsync'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let pockets = PocketArray.create(0)

	keepEach(bin, pockets: pockets)
	holdEach(bin, pockets: pockets)
	print("{pockets.count()} {weighFirstAsync(bin)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:41:20: 'pockets' is mutable outside 'blob'; a call into an isolated region takes values, immutable or isolated
error E3198: <fragment>:46:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
error E3207: <fragment>:53:28: 'weigh' is spawned with `async`, and 'blob' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: a-copy-of-an-element-a-for-loop-hands-out-over-an-isolated-array-field-is-an-ordinary-argument -->
The same calls compile when what they are given is a copy of the element, and a count of the element read in place leaves the loop as a value.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob
typealias PocketArray = Array with Pocket

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

type Pocket
	export var blob as Blob

	static function holding(blob Blob) returns Self
		return Self{blob: blob}
	end 'holding'
end 'Pocket'

function keep(blob Blob, into PocketArray)
	into.push(Pocket.holding(blob))
end 'keep'

function weigh(blob Blob) returns Count
	return blob.data.count()
end 'weigh'

function keepCopies(bin Bin, pockets PocketArray)
	for blob in bin.blobs 'each'
		keep(blob.clone(), into: pockets)
		pockets.push(Pocket.holding(blob.clone()))
	end 'each'
end 'keepCopies'

function weighEach(bin Bin) returns Count
	var total = 0 as Count

	for blob in bin.blobs 'each'
		total = total + weigh(blob)
	end 'each'

	return total
end 'weighEach'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)
	let pockets = PocketArray.create(0)

	keepCopies(bin, pockets: pockets)
	print("{pockets.count()} {weighEach(bin)}\n")
	return 0
end 'main'
```
```stdout
2 1
```
```exitcode
0
```

<!-- test: error.a-call-given-two-handed-back-results-cannot-store-one-region-into-the-other -->
A call whose result is a clean count, given what methods of two `isolated` fields hand back so that it may store the first field's data into the second field's region, reaches through two isolated values in one statement and is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Shelf = Array with ByteArray

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Rack
	export var shelf as Shelf

	static function create() returns Self
		return Self{shelf: Shelf.create(0)}
	end 'create'

	function inner() returns Shelf
		return self.shelf
	end 'inner'
end 'Rack'

type Holder
	export var blob as isolated Blob
	export var rack as isolated Rack

	static function create() returns Self
		return Self{blob: Blob.create(), rack: Rack.create()}
	end 'create'
end 'Holder'

function glue(bytes ByteArray, into Shelf) returns Count
	into.push(bytes)
	return into.count()
end 'glue'

function main() returns ExitCode
	let holder = Holder.create()
	let n = glue(holder.blob.inner(), into: holder.rack.inner())

	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:46:42: one statement reaches through two isolated values, 'holder' and 'holder'; a statement opens one isolated value, so open one with `modify` or split the statement
```

<!-- test: error.a-handed-back-result-given-to-an-async-call-is-a-read-of-the-part -->
An `async` call whose argument is what a method of an `isolated` field hands back would run beside the caller with the field's data, so it is refused although its promise answers a count.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function countOf(bytes ByteArray) returns Count
	return bytes.count()
end 'countOf'

function main() returns ExitCode
	let holder = Holder.create()
	let pending = async countOf(holder.blob.inner())

	print("{await pending}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:31:30: 'countOf' is spawned with `async`, and 'holder' is isolated; an `async` call takes an isolated value only in an `isolated` parameter, which moves it
```

<!-- test: error.a-handed-back-result-joined-with-fresh-data-is-a-read-of-the-part -->
A conditional whose one arm is what a method of an `isolated` field hands back and whose other arm is fresh data is the field's data in the arm that hands it back, so binding the join is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function pick(holder Holder, flag bool) returns ByteArray
	let got = ByteArray.create(0) if flag else holder.blob.inner()
	return got
end 'pick'

function main() returns ExitCode
	let holder = Holder.create()

	print("{pick(holder, flag: false).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:26:57: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-handed-back-result-given-by-a-closure-is-a-read-of-the-part -->
A closure that gives what a method of an `isolated` field hands back carries the field's data out of the statement it is read in, so its body is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

typealias Maker = function() returns ByteArray

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function apply(make Maker) returns ByteArray
	return make()
end 'apply'

function leak(holder Holder) returns ByteArray
	return apply(function() gives holder.blob.inner())
end 'leak'

function main() returns ExitCode
	let holder = Holder.create()

	print("{leak(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:32:44: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: values-of-a-handed-back-result-taken-in-the-same-statement-leave-freely -->
What a method of an `isolated` field hands back is consumed in its own statement by a sum of two counts and by an interpolation of its count.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function sum(holder Holder) returns Count
	return holder.blob.inner().count() + holder.blob.inner().count()
end 'sum'

function shown(holder Holder) returns String
	return "{holder.blob.inner().count()}"
end 'shown'

function main() returns ExitCode
	let holder = Holder.create()

	print("{sum(holder)} {shown(holder)}\n")
	return 0
end 'main'
```
```stdout
4 2
```
```exitcode
0
```

<!-- test: a-comparison-of-a-handed-back-result-is-a-value -->
What a method of an `isolated` field hands back, compared with `==` in its own statement against fresh data or a parameter, leaves only a `bool`, so the comparison is accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function sameAsFresh(holder Holder) returns bool
	return holder.blob.inner() == ByteArray.create(2)
end 'sameAsFresh'

function sameAs(holder Holder, other ByteArray) returns bool
	return holder.blob.inner() == other
end 'sameAs'

function main() returns ExitCode
	let holder = Holder.create()

	print("{sameAsFresh(holder)} {sameAs(holder, other: ByteArray.create(1))}\n")
	return 0
end 'main'
```
```stdout
true false
```
```exitcode
0
```

<!-- test: error.a-slice-of-a-handed-back-result-bound-by-try-otherwise-is-a-read-of-the-part -->
A slice of what a method of an `isolated` field hands back may view the field's data, so binding it through `try … otherwise` is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function head(holder Holder) returns ByteArray
	let got = try holder.blob.inner().slice(0, endIndex: 1) otherwise ByteArray.create(0)
	return got
end 'head'

function main() returns ExitCode
	let holder = Holder.create()

	print("{head(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:26:28: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-handed-back-result-through-three-nested-helpers-is-a-read-of-the-part -->
What a method of an `isolated` field hands back, passed through three helpers that may each hand back their argument, is still the field's data at a plain return.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function leak(holder Holder) returns ByteArray
	return echo(echo(echo(holder.blob.inner())))
end 'leak'

function main() returns ExitCode
	let holder = Holder.create()

	print("{leak(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:30:36: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: a-count-of-a-handed-back-result-through-three-nested-helpers-is-a-value -->
A count taken in the same statement of what a method of an `isolated` field hands back, passed through three helpers that may each hand back their argument, is a value and is accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function echo(bytes ByteArray) returns ByteArray
	return bytes
end 'echo'

function countOf(bytes ByteArray) returns Count
	return bytes.count()
end 'countOf'

function viaHelper(holder Holder) returns Count
	return countOf(echo(echo(echo(holder.blob.inner()))))
end 'viaHelper'

function viaMethod(holder Holder) returns Count
	return echo(echo(echo(holder.blob.inner()))).count()
end 'viaMethod'

function main() returns ExitCode
	let holder = Holder.create()

	print("{viaHelper(holder)} {viaMethod(holder)}\n")
	return 0
end 'main'
```
```stdout
2 2
```
```exitcode
0
```

<!-- test: error.an-element-of-an-elements-own-array-field-cannot-leave-a-nested-for-loop -->
A nested `for` loop over the array field of an element an outer loop hands out over an `isolated` array field iterates data of that field's region, so its element returned as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias ChunkArray = Array with ByteArray
typealias BlobArray = Array with Blob

type Blob
	export var chunks as ChunkArray

	static function create() returns Self
		let chunks = ChunkArray.create(0)
		chunks.push(ByteArray.create(1))
		return Self{chunks: chunks}
	end 'create'
end 'Blob'

type Bin
	export var blobs as isolated BlobArray

	static function holding(blobs isolated BlobArray) returns Self
		return Self{blobs: blobs}
	end 'holding'
end 'Bin'

function firstChunk(bin Bin) returns ByteArray
	for blob in bin.blobs 'outer'
		for chunk in blob.chunks 'inner'
			return chunk
		end 'inner'
	end 'outer'

	return ByteArray.create(0)
end 'firstChunk'

function main() returns ExitCode
	let blobs = BlobArray.create(0) as isolated BlobArray
	blobs.push(Blob.create())
	let bin = Bin.holding(blobs)

	print("{firstChunk(bin).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:26:14: 'bin.blobs' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: the-key-of-a-pair-a-for-loop-hands-out-over-an-isolated-map-field-leaves-freely -->
A `for` loop destructuring the pairs of an `isolated` map field outside a `modify` block hands out keys that are values, so a key returned or stored in a plain variable leaves freely while the value is read in place.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Index = Map with Count, ByteArray

type Holder
	export var index as isolated Index

	static function holding(index isolated Index) returns Self
		return Self{index: index}
	end 'holding'
end 'Holder'

function largestKey(holder Holder) returns Count
	var best = 0 as Count

	for (key, bytes) in holder.index 'each'
		if bytes.count() > 0 and key > best 'larger'
			best = key
		end 'larger'
	end 'each'

	return best
end 'largestKey'

function main() returns ExitCode
	let index = Index.create() as isolated Index
	index.upsert(3, value: ByteArray.create(1))
	index.upsert(5, value: ByteArray.create(2))
	let holder = Holder.holding(index)

	print("{largestKey(holder)}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-fresh-constructor-given-a-copy-or-a-value-field-of-an-isolated-part-is-clean -->
A fresh constructor given a copy of an `isolated` field's data, or a value-type field of the part, builds a record that holds nothing of the region, so the record leaves freely.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray
	export var n as Count

	static function create() returns Self
		return Self{data: ByteArray.create(2), n: 4}
	end 'create'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

type Pocket
	export var bytes as ByteArray
	export var n as Count

	static function holding(bytes ByteArray, n Count) returns Self
		return Self{bytes: bytes, n: n}
	end 'holding'
end 'Pocket'

function copied(holder Holder) returns Pocket
	return Pocket.holding(holder.blob.data.clone(), n: holder.blob.n)
end 'copied'

function main() returns ExitCode
	let holder = Holder.create()
	let pocket = copied(holder)

	print("{pocket.bytes.count()} {pocket.n}\n")
	return 0
end 'main'
```
```stdout
2 4
```
```exitcode
0
```

<!-- test: error.a-method-of-an-isolated-part-reached-by-an-index-or-parentheses-is-a-read-of-the-part -->
A method that hands back its receiver's data, called on an `isolated` field of a holder reached by an index into an array of holders or by a parenthesised holder, hands back the part's data, so returning it as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias HolderArray = Array with Holder

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function byIndex(holders HolderArray, at ElementIndex) returns ByteArray
	return (try holders.get(at) otherwise panic("one holder")).blob.inner()
end 'byIndex'

function byParens(holder Holder) returns ByteArray
	return (holder).blob.inner()
end 'byParens'

function main() returns ExitCode
	let holder = Holder.create()
	let holders = HolderArray.create(0)
	holders.push(Holder.create())

	print("{byIndex(holders, at: 0).count()} {byParens(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:27:66: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:31:23: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: a-count-of-a-slice-of-an-isolated-part-is-a-value -->
A slice of an `isolated` field's data, or of what a method of the field hands back, counted in the same statement leaves only a value, so the count is accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function slicedField(holder Holder) returns Count
	return (try holder.blob.data.slice(0, endIndex: 1) otherwise panic("two bytes")).count()
end 'slicedField'

function slicedInner(holder Holder) returns Count
	return (try holder.blob.inner().slice(0, endIndex: 1) otherwise panic("two bytes")).count()
end 'slicedInner'

function main() returns ExitCode
	let holder = Holder.create()

	print("{slicedField(holder)} {slicedInner(holder)}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: error.an-element-a-user-defined-cursor-hands-out-over-an-isolated-field-cannot-leave-a-for-loop -->
An element a `for` loop hands out through a user-defined `Iterable` held by an `isolated` field outside a `modify` block is data of the field's region, so returning it as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BlobArray = Array with Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'
end 'Blob'

type RackCursor implements Iterator with Blob
	var blobs as BlobArray
	var at as ElementIndex

	static function over(blobs BlobArray) returns Self
		return Self{blobs: blobs, at: 0}
	end 'over'

	function current() returns Blob
		return try self.blobs.get(self.at) otherwise panic("a cursor stops at the last blob")
	end 'current'

	function advance(mutable self) throws IterationError
		if self.at + 1 >= self.blobs.count() 'last'
			throw IterationError.exhausted
		end 'last'

		self.at = self.at + 1
	end 'advance'
end 'RackCursor'

type Rack implements Iterable with (Blob, RackCursor)
	var blobs as BlobArray

	static function create() returns Self
		let blobs = BlobArray.create(0)
		blobs.push(Blob.create())
		return Self{blobs: blobs}
	end 'create'

	function createIterator() returns RackCursor throws IterationError
		return RackCursor.over(self.blobs)
	end 'createIterator'
end 'Rack'

type Holder
	export var rack as isolated Rack

	static function create() returns Self
		return Self{rack: Rack.create()}
	end 'create'
end 'Holder'

function firstBlob(holder Holder) returns Blob
	for blob in holder.rack 'each'
		return blob
	end 'each'

	return Blob.create()
end 'firstBlob'

function main() returns ExitCode
	let holder = Holder.create()

	print("{firstBlob(holder).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:58:14: 'holder.rack' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.an-element-of-an-isolated-fixed-array-field-cannot-leave-a-for-loop -->
An element a `for` loop hands out over an `isolated` `FixedArray` field outside a `modify` block is data of the field's region, so returning it as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias Trio = FixedArray with 3 Blob

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(1)}
	end 'create'
end 'Blob'

type Holder
	export var trio as isolated Trio

	static function create() returns Self
		return Self{trio: Trio.create(value: Blob.create())}
	end 'create'
end 'Holder'

function firstBlob(holder Holder) returns Blob
	for blob in holder.trio 'each'
		return blob
	end 'each'

	return Blob.create()
end 'firstBlob'

function main() returns ExitCode
	let holder = Holder.create()

	print("{firstBlob(holder).data.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3198: <fragment>:23:14: 'holder.trio' is isolated and cannot be read outside a block; open it with `modify`, or take it out with `swap`
```

<!-- test: error.a-handed-back-result-kept-by-a-fresh-local-is-a-read-of-the-part -->
A method of a fresh local that keeps its argument and answers a count, given what a method of an `isolated` field hands back, would leave the local holding the field's data: the local is mutable data outside the region the call opens, so the call is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

type Keeper
	export var kept as ByteArray

	static function create() returns Self
		return Self{kept: ByteArray.create(0)}
	end 'create'

	function keep(bytes ByteArray) returns Count
		self.kept = bytes
		return bytes.count()
	end 'keep'
end 'Keeper'

function keepInLocal(holder Holder) returns Keeper
	let keeper = Keeper.create()
	_ = keeper.keep(holder.blob.inner())
	return keeper
end 'keepInLocal'

function main() returns ExitCode
	let holder = Holder.create()

	print("{keepInLocal(holder).kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:40:6: 'keeper' is mutable outside 'holder'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: error.a-handed-back-result-kept-by-a-helper-into-a-fresh-local-is-a-read-of-the-part -->
A helper that answers a count and stores what a method of an `isolated` field hands back into a fresh local it is also given would leave the local holding the field's data: the local is mutable data outside the region the call opens, so the call is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

type Keeper
	export var kept as ByteArray

	static function create() returns Self
		return Self{kept: ByteArray.create(0)}
	end 'create'
end 'Keeper'

function keepInto(keeper Keeper, bytes ByteArray) returns Count
	keeper.kept = bytes
	return bytes.count()
end 'keepInto'

function keepInLocal(holder Holder) returns Keeper
	let keeper = Keeper.create()
	_ = keepInto(keeper, bytes: holder.blob.inner())
	return keeper
end 'keepInLocal'

function main() returns ExitCode
	let holder = Holder.create()

	print("{keepInLocal(holder).kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:40:15: 'keeper' is mutable outside 'holder'; a call into an isolated region takes values, immutable or isolated
```

<!-- test: error.a-handed-back-result-given-by-a-match-expression-arm-is-a-read-of-the-part -->
A `match` expression whose arm gives what a method of an `isolated` field hands back is the field's data, so binding it is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

enum Pick
	held
	fresh
end 'Pick'

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function chosen(holder Holder, pick Pick) returns ByteArray
	let got = match pick 'which'
		held gives holder.blob.inner()
		fresh gives ByteArray.create(0)
	end 'which'
	return got
end 'chosen'

function main() returns ExitCode
	let holder = Holder.create()

	print("{chosen(holder, pick: Pick.held).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:32:26: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: a-for-loop-over-a-handed-back-result-reads-its-bytes-in-place -->
A `for` loop whose source is what a method of an `isolated` field hands back, and whose body reads only the bytes it hands out, leaves only values, so it is accepted as a loop over the field's data is.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function total(holder Holder) returns Count
	var acc = 0 as Count

	for byte in holder.blob.inner() 'each'
		acc = acc + (byte as Count) + 1
	end 'each'

	return acc
end 'total'

function main() returns ExitCode
	let holder = Holder.create()

	print("{total(holder)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: error.a-handed-back-result-in-a-tuple-or-a-union-a-helper-builds-is-a-read-of-the-part -->
A helper that puts its argument into a tuple or a union payload hands back what a method of an `isolated` field hands back inside it, so binding either result is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Maybe
	some(bytes ByteArray)
	none
end 'Maybe'

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function tupled(bytes ByteArray) returns (ByteArray, Count)
	return (bytes, bytes.count())
end 'tupled'

function wrapped(bytes ByteArray) returns Maybe
	return Maybe.some(bytes)
end 'wrapped'

function viaTuple(holder Holder) returns ByteArray
	let (bytes, n) = tupled(holder.blob.inner())
	bytes.push(n as Byte)
	return bytes
end 'viaTuple'

function viaUnion(holder Holder) returns Maybe
	let got = wrapped(holder.blob.inner())
	return got
end 'viaUnion'

function main() returns ExitCode
	let holder = Holder.create()

	match viaUnion(holder) 'got'
		some(bytes) then print("{viaTuple(holder).count()} {bytes.count()}\n")
		none then print("none\n")
	end 'got'

	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:39:38: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:45:32: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-handed-back-result-is-refused-alike-when-its-types-are-declared-after-their-use -->
The handed-back refusal does not depend on declaration order: with the holder, the part and its method declared after the functions that call it, a plain return and a binding of what the method hands back are refused at the same sites.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function leak(holder Holder) returns ByteArray
	return holder.blob.inner()
end 'leak'

function bind(holder Holder) returns Count
	let got = holder.blob.inner()
	got.push(1)
	return got.count()
end 'bind'

function counted(holder Holder) returns Count
	return holder.blob.inner().count()
end 'counted'

function main() returns ExitCode
	let holder = Holder.create()

	print("{leak(holder).count()} {bind(holder)} {counted(holder)}\n")
	return 0
end 'main'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'
```
```maxoncstderr
error E3201: <fragment>:6:21: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
error E3201: <fragment>:10:24: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: a-handed-back-result-is-clean-when-copied-counted-in-a-ternary-or-used-inside-a-window -->
What a method of an `isolated` field hands back leaves freely as a copy, as a count chosen by a conditional, as a count a closure takes when called, and as a count of a fresh record built over a copy; inside a `modify` block that opens the field it is bound and written in place.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function inner() returns ByteArray
		return self.data
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

type Pocket
	export var bytes as ByteArray

	static function holding(bytes ByteArray) returns Self
		return Self{bytes: bytes}
	end 'holding'
end 'Pocket'

function copied(holder Holder) returns ByteArray
	let got = holder.blob.inner().clone()
	got.push(9)
	return got
end 'copied'

function chosen(holder Holder, flag bool) returns Count
	let n = holder.blob.inner().count() if flag else 0
	return n
end 'chosen'

function deferred(holder Holder) returns Count
	let measure = function() gives holder.blob.inner().count()
	return measure()
end 'deferred'

function pocketed(holder Holder) returns Pocket
	return Pocket.holding(holder.blob.inner().clone())
end 'pocketed'

function grown(holder Holder) returns Count
	var n = 0 as Count

	modify holder.blob 'open'
		let got = holder.blob.inner()
		got.push(5)
		n = got.count()
	end 'open'

	return n
end 'grown'

function main() returns ExitCode
	let holder = Holder.create()

	print("{copied(holder).count()} {chosen(holder, flag: true)} {deferred(holder)} {pocketed(holder).bytes.count()} {grown(holder)}\n")
	return 0
end 'main'
```
```stdout
3 2 2 2 3
```
```exitcode
0
```

<!-- test: error.an-element-a-for-loop-hands-out-over-a-handed-back-result-cannot-leave -->
A `for` loop whose source is what a method of an `isolated` field hands back hands out elements of the field's data, so returning one as plain data is refused.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias ChunkArray = Array with ByteArray

type Blob
	export var chunks as ChunkArray

	static function create() returns Self
		let chunks = ChunkArray.create(0)
		chunks.push(ByteArray.create(1))
		return Self{chunks: chunks}
	end 'create'

	function inner() returns ChunkArray
		return self.chunks
	end 'inner'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function firstChunk(holder Holder) returns ByteArray
	for chunk in holder.blob.inner() 'each'
		return chunk
	end 'each'

	return ByteArray.create(0)
end 'firstChunk'

function main() returns ExitCode
	let holder = Holder.create()

	print("{firstChunk(holder).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:29:27: 'inner' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-part-handed-back-through-a-chain-of-three-receiver-methods-is-a-read-of-the-part -->
Three chained methods that each hand back their receiver, called on an `isolated` field, hand back the part itself, so returning the chain's result as plain data is refused while a count read through the same chain leaves as a value.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function me() returns Blob
		return self
	end 'me'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function leak(holder Holder) returns Blob
	return holder.blob.me().me().me()
end 'leak'

function counted(holder Holder) returns Count
	return holder.blob.me().me().me().data.count()
end 'counted'

function main() returns ExitCode
	let holder = Holder.create()

	print("{leak(holder).data.count()} {counted(holder)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3201: <fragment>:26:21: 'me' hands back mutable data of the isolated region it opens; use it inside a `modify` block
```

<!-- test: error.a-user-equals-reached-by-an-operator-cannot-keep-what-it-is-given -->
A `==` that dispatches a type's own `equals` hands it the handed-back result by value, and the only way the method could keep it is to write its receiver through a field, which the comparison-method rule refuses.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Sink
	export var held as ByteArray

	static function create() returns Self
		return Self{held: ByteArray.create(0)}
	end 'create'
end 'Sink'

type Blob
	export var data as ByteArray
	export var sink as Sink

	static function create() returns Self
		return Self{data: ByteArray.create(2), sink: Sink.create()}
	end 'create'

	function me() returns Blob
		return self
	end 'me'

	function equals(other Blob) returns bool
		self.sink.held = other.data
		return true
	end 'equals'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function sameAs(holder Holder) returns bool
	let local = Blob.create()
	return local == holder.blob.me()
end 'sameAs'

function main() returns ExitCode
	let holder = Holder.create()

	print("{sameAs(holder)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3187: <fragment>:25:11: 'Blob.equals' writes its receiver, but a method behind the comparison operators may not modify its receiver — `==`, `!=`, `<`, `<=`, `>` and `>=` pass it by value
```

<!-- test: a-user-equals-reached-by-an-operator-takes-a-handed-back-result-beside-clean-operands -->
What a method of an `isolated` field hands back, compared with `==` against fresh data through the type's own `equals`, leaves only a `bool`: nothing in the comparison can keep the region, so the comparison is accepted.

```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Blob
	export var data as ByteArray

	static function create() returns Self
		return Self{data: ByteArray.create(2)}
	end 'create'

	function me() returns Blob
		return self
	end 'me'

	function equals(other Blob) returns bool
		return self.data.count() == other.data.count()
	end 'equals'
end 'Blob'

type Holder
	export var blob as isolated Blob

	static function create() returns Self
		return Self{blob: Blob.create()}
	end 'create'
end 'Holder'

function sameAsFresh(holder Holder) returns bool
	return holder.blob.me() == Blob.create()
end 'sameAsFresh'

function differsFromFresh(holder Holder) returns bool
	return holder.blob.me() != Blob.create()
end 'differsFromFresh'

function main() returns ExitCode
	let holder = Holder.create()

	print("{sameAsFresh(holder)} {differsFromFresh(holder)}\n")
	return 0
end 'main'
```
```stdout
true false
```
```exitcode
0
```
