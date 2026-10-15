---
feature: witness-by-reference
keywords: [witness, interface, dispatch, byref, param, reassign, cell, adapter, existential, where]
category: memory-safety
---

# Witness Dispatch Passes a Parameter by Reference Where Any Conformer Writes It

## Documentation

A parameter a function REASSIGNS is passed as the address of a cell rather than as a value
(`pass-by-reference.md`), and a call through a function value follows the same rule position by position
(`byref-param-widening-and-guard.md`). A call through an interface follows it too. The callee is not known
at the call — the shared body of a type constrained with `where T is Bumper`, or a value held at the
interface `Bumper`, reaches its method through a witness table — so the convention belongs to the
REQUIREMENT: a position of `Bumper.bump` is passed as the argument's own storage when any conformer's
`bump` writes that position, and every conformer's table entry takes the position the same way.

A conformer that only reads such a position is reached through an adapter that loads the value out of the
cell and calls the conformer with it, as a function value's thunk does. A direct call to a conformer, which
names its callee, keeps passing exactly what that callee takes.

The caller's `var` is updated by the call. A `let` handed to such a position is refused with the same
diagnostic a direct call earns, and a narrow variable handed to a position whose declared alias is wider is
range-checked when the call returns.

## Tests

<!-- test: witness-by-reference.a-writing-conformer-updates-the-callers-var -->
`self.item.bump(x)` dispatches through the witness of `Doubler`, whose `bump` reassigns its parameter, so the
shared body of `Holder.run` hands over `x`'s cell and the write reaches `x`.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-reading-conformer-is-reached-through-an-adapter -->
One requirement, two conformers: `Doubler.bump` writes its parameter and `Reader.bump` only reads it. The
position is passed as a cell for both, so the reader's table entry loads the value before it runs, and the
caller's variable is unchanged by it.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Reader implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		print("read {n}\n")
	end 'bump'
end 'Reader'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler
typealias ReaderHolder = Holder with Reader

function main() returns ExitCode
	let d = DoublerHolder.create(Doubler.create(1))
	let r = ReaderHolder.create(Reader.create(1))
	r.run()
	d.run()
	return 0
end 'main'
```
```stdout
read 3
3
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-direct-call-to-the-conformer-is-unchanged -->
The same conformers called by name, beside the dispatch through the witness.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Reader implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		print("read {n}\n")
	end 'bump'
end 'Reader'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run(x Wide)
		var y = x
		self.item.bump(y)
		print("{y}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	var a = 3 as Wide
	Doubler.create(1).bump(a)
	print("{a}\n")
	var b = 4 as Wide
	Reader.create(1).bump(b)
	print("{b}\n")
	DoublerHolder.create(Doubler.create(1)).run(5)
	return 0
end 'main'
```
```stdout
50
read 4
4
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-position-forwarded-to-a-writer-is-a-cell -->
A conformer that only hands its parameter on to a function that reassigns it writes the position too.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

function reset(n Wide)
	n = 7
end 'reset'

type Forwarder implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		reset(n)
	end 'bump'
end 'Forwarder'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias ForwarderHolder = Holder with Forwarder

function main() returns ExitCode
	ForwarderHolder.create(Forwarder.create(1)).run()
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: witness-by-reference.a-throwing-requirement-passes-the-cell -->
The throwing form of the dispatch passes the cell exactly as the plain one does.
```maxon
typealias Wide = int(0 to 100)

enum BumpError implements Error
	refused
end 'BumpError'

interface Bumper
	function bump(n Wide) throws BumpError
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide) throws BumpError
		n = 50

		if n > 60 'never'
			throw BumpError.refused
		end 'never'
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run() throws BumpError
		var x = 3 as Wide
		try self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	try h.run() otherwise return 1
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.an-existential-receiver-passes-the-cell -->
A value held at the interface dispatches through the witness its fat pointer carries, and follows the same rule.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

function bumpThrough(b Bumper)
	var x = 3 as Wide
	b.bump(x)
	print("{x}\n")
end 'bumpThrough'

function main() returns ExitCode
	bumpThrough(Doubler.create(1))
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-field-at-a-written-position-is-updated -->
A field of the shared body's own receiver is storage with an address, and the dispatch writes it.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T
	export var level as Wide

	static function create(item T) returns Self
		return Self{item: item, level: 3}
	end 'create'

	function run()
		self.item.bump(self.level)
		print("{self.level}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	var h = DoublerHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-narrow-var-written-wider-through-a-witness-is-caught-at-the-call -->
The declared alias of the requirement's position is `Wide`; the variable is `Narrow`, so the value it holds
when the call returns is range-checked.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Narrow
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at witness-by-reference.a-narrow-var-written-wider-through-a-witness-is-caught-at-the-call.test:30: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in Holder.run
  in main
  in mrt_start
```

<!-- test: witness-by-reference.a-narrow-var-written-within-its-range-needs-no-adapter-check -->
A conformer whose parameter is the variable's own alias writes only values that alias admits.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

interface Bumper
	function bump(n Narrow)
end 'Bumper'

type Settler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Narrow)
		n = 7
	end 'bump'
end 'Settler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Narrow
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias SettlerHolder = Holder with Settler

function main() returns ExitCode
	let h = SettlerHolder.create(Settler.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: witness-by-reference.error.a-let-at-a-written-position-is-refused -->
A `let` handed to a position some conformer writes is refused, as it is at a direct call.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		let x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:29:13: 'Doubler.bump' writes its parameter 'n', but 'bump' passes it the immutable 'x'
```

<!-- test: witness-by-reference.error.a-let-handed-to-an-existential-at-a-written-position-is-refused -->
The same refusal through a value held at the interface.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

function bumpThrough(b Bumper)
	let x = 3 as Wide
	b.bump(x)
end 'bumpThrough'

function main() returns ExitCode
	bumpThrough(Doubler.create(1))
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:22:4: 'Doubler.bump' writes its parameter 'n', but 'bump' passes it the immutable 'x'
```

<!-- test: witness-by-reference.a-let-is-accepted-where-no-conformer-writes-the-position -->
Control: the position is written by no conformer, so a `let` is a legal argument and the adapter-free dispatch is unchanged.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Reader implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		print("read {n}\n")
	end 'bump'
end 'Reader'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		let x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias ReaderHolder = Holder with Reader

function main() returns ExitCode
	let h = ReaderHolder.create(Reader.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
read 3
3
```
```exitcode
0
```

<!-- test: witness-by-reference.a-parameter-handed-on-through-a-witness-is-written-in-the-caller -->
A parameter the shared body hands to the dispatch is by reference itself, so the write reaches the variable
the body's own caller passed.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run(n Wide)
		self.item.bump(n)
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	var x = 3 as Wide
	h.run(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-module-level-var-at-a-written-position-is-updated -->
A module-level `var` is storage with an address, and the dispatch writes it.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

var level = 3 as Wide

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		self.item.bump(level)
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	h.run()
	print("{level}\n")
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.a-requirement-inherited-through-extends-follows-the-same-rule -->
The requirement is declared by `Base` and reached through `Derived`; its convention belongs to the
requirement, not to the interface the constraint names.
```maxon
typealias Wide = int(0 to 100)

interface Base
	function bump(n Wide)
end 'Base'

interface Derived extends Base
	function label() returns Wide
end 'Derived'

type Doubler implements Derived
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'

	function label() returns Wide
		return self.seed
	end 'label'
end 'Doubler'

type Holder uses T where T is Derived
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Wide
		self.item.bump(x)
		print("{x} {self.item.label()}\n")
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler

function main() returns ExitCode
	let h = DoublerHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
50 1
```
```exitcode
0
```

<!-- test: witness-by-reference.error.a-witness-hand-over-adopts-the-requirements-brand -->
An unbranded `var` handed to a position whose requirement declares a brand adopts it, so a later use at another brand is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ws = Array with Integer
typealias Vs = Array with Integer

interface Filler
	function bump(n Ws)
end 'Filler'

type Pusher implements Filler
	export let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Ws)
		n = Ws.create()
		n.push(self.seed)
	end 'bump'
end 'Pusher'

function takeVs(vs Vs)
	print("{vs.count()}\n")
end 'takeVs'

type Holder uses T where T is Filler
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var a = [1 as Integer]
		self.item.bump(a)
		takeVs(a)
	end 'run'
end 'Holder'

typealias PusherHolder = Holder with Pusher

function main() returns ExitCode
	let h = PusherHolder.create(Pusher.create(7))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:37:3: argument type mismatch for 'vs': expected 'Vs', got 'Ws'
```

<!-- test: witness-by-reference.a-witness-hand-over-adopts-without-a-conflict -->
Control: the later use is at the brand the requirement declared.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ws = Array with Integer

interface Filler
	function bump(n Ws)
end 'Filler'

type Pusher implements Filler
	export let seed as Integer

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Ws)
		n = Ws.create()
		n.push(self.seed)
	end 'bump'
end 'Pusher'

function takeWs(ws Ws)
	print("{ws.count()}\n")
end 'takeWs'

type Holder uses T where T is Filler
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var a = [1 as Integer]
		self.item.bump(a)
		takeWs(a)
	end 'run'
end 'Holder'

typealias PusherHolder = Holder with Pusher

function main() returns ExitCode
	let h = PusherHolder.create(Pusher.create(7))
	h.run()
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: witness-by-reference.error.each-writer-of-a-requirement-is-named-with-its-own-parameter -->
Two conformers write the position under different parameter names, so the refusal names each of them.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Doubler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(n Wide)
		n = 50
	end 'bump'
end 'Doubler'

type Tripler implements Bumper
	export let seed as Wide

	static function create(seed Wide) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(amount Wide)
		amount = 75
	end 'bump'
end 'Tripler'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		let x = 3 as Wide
		self.item.bump(x)
	end 'run'
end 'Holder'

typealias DoublerHolder = Holder with Doubler
typealias TriplerHolder = Holder with Tripler

function main() returns ExitCode
	let a = DoublerHolder.create(Doubler.create(1))
	let b = TriplerHolder.create(Tripler.create(1))
	a.run()
	b.run()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:41:13: 'Doubler.bump' writes its parameter 'n', but 'bump' passes it the immutable 'x'
error E3159: <fragment>:41:13: 'Tripler.bump' writes its parameter 'amount', but 'bump' passes it the immutable 'x'
```

<!-- test: witness-by-reference.a-narrow-var-written-wider-through-an-associated-type-formal-is-caught-at-the-call -->
A requirement formal that names an associated type declares no alias of its own, so it may be wider than the variable handed to it and the variable is range-checked when the call returns.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

interface Sink uses Item
	function put(n Item)
end 'Sink'

type WideSink implements Sink with Wide
	export var last as Wide

	static function create(last Wide) returns Self
		return Self{last: last}
	end 'create'

	function put(n Wide)
		n = 5000
	end 'put'
end 'WideSink'

type Holder uses T where T is Sink
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Narrow
		self.item.put(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias SinkHolder = Holder with WideSink

function main() returns ExitCode
	let h = SinkHolder.create(WideSink.create(1))
	h.run()
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at witness-by-reference.a-narrow-var-written-wider-through-an-associated-type-formal-is-caught-at-the-call.test:30: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in Holder.run
  in main
  in mrt_start
```

<!-- test: witness-by-reference.error.an-impl-taking-a-wider-alias-than-its-requirement-is-refused -->
A conformer's parameter names the requirement's alias exactly, so no conformer can take a wider one than the requirement states.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

interface Bumper
	function bump(n Narrow)
end 'Bumper'

type Impl implements Bumper
	export var last as Wide

	static function create(last Wide) returns Self
		return Self{last: last}
	end 'create'

	function bump(n Wide)
		n = 5000
	end 'bump'
end 'Impl'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Narrow
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias ImplHolder = Holder with Impl

function main() returns ExitCode
	let h = ImplHolder.create(Impl.create(1))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Impl' has 1 method(s) with wrong signature:
  - bump(n Wide) returns void (expected bump(n Narrow) returns void)
```

<!-- test: witness-by-reference.a-conformance-declared-by-an-extension-of-a-generic-base-passes-the-cell -->
A conformance an `extension` of a generic base declares is read for the cell convention like one the type declares, so the caller's variable is the one the conformer writes.
```maxon
typealias Wide = int(0 to 100)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

extension Box implements Bumper
	function bump(n Wide)
		n = 50
	end 'bump'
end 'Box'

typealias WideBox = Box with Wide

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Wide
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias BoxHolder = Holder with WideBox

function main() returns ExitCode
	let h = BoxHolder.create(WideBox.create(1))
	h.run()
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: witness-by-reference.error.a-conformance-declared-on-a-generic-instance-alias-is-refused -->
An alias of a generic instance cannot declare a conformance, so no conformer is ever filed under an alias name.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias WideBox = Box with Wide

extension WideBox implements Bumper
	function bump(n Wide)
		n = 5000
	end 'bump'
end 'WideBox'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var x = 3 as Narrow
		self.item.bump(x)
		print("{x}\n")
	end 'run'
end 'Holder'

typealias BoxHolder = Holder with WideBox

function main() returns ExitCode
	let h = BoxHolder.create(WideBox.create(1))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:39:11: Type 'WideBox' does not satisfy constraint 'Bumper' required by type parameter 'T' of 'Holder'
```

<!-- test: witness-by-reference.error.a-float-var-handed-to-an-integer-requirement-formal-is-refused-before-any-cell-is-shared -->
```maxon
typealias Count = int(0 to 100)
typealias Real = float(0.0 to 100.0)

interface Bumper
	function bump(k Count)
end 'Bumper'

type Impl implements Bumper
	export let seed as Count

	static function create(seed Count) returns Self
		return Self{seed: seed}
	end 'create'

	function bump(k Count)
		k = 7
	end 'bump'
end 'Impl'

type Holder uses T where T is Bumper
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var r = 2.5 as Real
		self.item.bump(r)
		print("{r}\n")
	end 'run'
end 'Holder'

typealias ImplHolder = Holder with Impl

function main() returns ExitCode
	let h = ImplHolder.create(Impl.create(1))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:30:13: argument type mismatch for 'k': expected 'Count', got 'Real'
```

<!-- test: witness-by-reference.error.an-integer-var-handed-to-a-float-requirement-formal-is-refused-before-any-cell-is-shared -->
```maxon
typealias Count = int(0 to 100)
typealias Real = float(0.0 to 100.0)

interface Scaler
	function scale(k Real)
end 'Scaler'

type Doubler implements Scaler
	export let seed as Count

	static function create(seed Count) returns Self
		return Self{seed: seed}
	end 'create'

	function scale(k Real)
		k = 2.5
	end 'scale'
end 'Doubler'

type Holder uses T where T is Scaler
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function run()
		var n = 3 as Count
		self.item.scale(n)
		print("{n}\n")
	end 'run'
end 'Holder'

typealias DHolder = Holder with Doubler

function main() returns ExitCode
	let h = DHolder.create(Doubler.create(1))
	h.run()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:30:13: argument type mismatch for 'k': expected 'Real', got 'Count'
```

<!-- test: witness-by-reference.error.a-static-function-is-not-an-impl-of-an-instance-requirement -->
Only an instance method, whose receiver is its first parameter, can satisfy a requirement, so no impl is ever filed without a receiver.
```maxon
typealias Wide = int(0 to 10000)

interface Bumper
	function bump(n Wide)
end 'Bumper'

type Impl implements Bumper
	export var last as Wide

	static function create(last Wide) returns Self
		return Self{last: last}
	end 'create'

	static function bump(n Wide)
		n = 5000
	end 'bump'
end 'Impl'

function main() returns ExitCode
	let i = Impl.create(1)
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:8:6: Partial interface implementation: type 'Impl' has 1 method(s) with wrong signature:
  - static bump(n Wide) returns void (expected bump(n Wide) returns void)
```
