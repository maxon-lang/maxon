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
error E3207: <fragment>:35:10: 'keep' writes 'p'; 'frozen' is immutable
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
error E3196: <fragment>:32:14: cannot store a mutable 'Box' into 'box', which holds an immutable 'Box'; freeze it, or store a copy
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
error E3196: <fragment>:20:15: cannot store a mutable 'Box' into 'held', which holds an immutable 'Box'; freeze it, or store a copy
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
	var ticket = Token.create(1)
	var box = TokenArray.create(0)
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

<!-- test: a-constructor-field-fed-a-call-result-pins-the-parameter-it-may-hand-back -->
`wrap` builds a `Wrapper` over what `pick` may hand back from `p`, so the constructor's mutable field makes `p` mutable only and the frozen argument is refused.

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
	print("{wrapper.row.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:34:21: 'wrap' writes 'p' (it calls 'create', which writes 'row'); 'frozen' is immutable
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
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'pick' is read-only
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
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'pick' is read-only
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
error E3207: <fragment>:33:16: 'relay' writes 'p'; 'frozen' is immutable
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
error E3196: <fragment>:14:15: cannot store a mutable 'Box' into 'an element of the container push writes', which holds an immutable 'Box'; freeze it, or store a copy
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
error E3207: <fragment>:33:15: 'adopt' needs 'getPart' frozen; 'function' is mutable. Freeze it first
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
	var items = TallyArray.create(0)
	items.push(1)

	var promises = TallyPromiseArray.create(0)
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
	var promises = FlagPromiseArray.create(0)
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
error E3207: <fragment>:26:14: 'bump' needs a writable receiver; the argument is read-only while 'window' is open
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
	var outer = Outer.create()
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
error E3207: <fragment>:23:7: 'keep' writes 'box'; 'freeze' is immutable
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
error E3207: <fragment>:21:14: 'take' takes `immutable` 'item'; 'same' is read-only
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
error E3207: <fragment>:34:20: 'bump' needs a writable receiver; the argument is read-only while 'right' is open
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
	var a = Node.create(3)
	var log = Log.create()
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
	var batch = LineArray.create(0) as isolated LineArray
	var other = Box.create(1)

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
