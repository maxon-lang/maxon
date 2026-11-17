---
feature: sendable-bounds
status: experimental
keywords: [region, bound, sendable, where, generic, instantiation, function type, interface, promise, immutable, share]
category: semantics
---

# What `where T is sendable` admits

## Documentation

A type argument meets `where T is sendable` when two things hold of it.

It must STAND as sendable: it is a value (a scalar, `String`, `Character`, a C string, or any aggregate that is not
`unique` and owns no counted box, such as a union with no payload or a record packed in a word), a service handle
or a `SharedValue` handle; or it is written `immutable` and is not `unique`. A `unique` type never meets `sendable`,
whatever its size. A record that is a counted box is a plain record, not a value: one whose fields do not fit one
word is, even when every field is a `let` of a value. A container instance, a tuple, a union whose cases carry a
payload and an instance of a generic record are plain records too, whether written directly or through an alias;
each is refused bare and admitted as `immutable`. A function type, an interface type and a promise are not values, and
neither is an alias of one; writing `immutable` before one does not make it one.

And a value of it must be SHAREABLE with another thread: the share walk that `freeze` and `SharedValue` use must
be able to mark everything its graph reaches. An `immutable` record whose graph reaches a function value stands
as sendable but cannot be shared, so the bound refuses it too and names what the graph reaches. A type that owns
no counted record needs no walk. The bound judges the type's own graph and never the conformers of an interface
the graph holds: an `immutable` record with a field of an interface type meets the bound in every program. What
such a field may hold is judged where a value is made shared — at a `freeze`, a `publish` or a registration —
in a program that spawns a service.

```text
typealias StepPool   = Pool with Step               // refused: a function type
typealias HolderPool = Pool with immutable Holder   // refused when Holder's graph reaches a function value
typealias JobPool    = Pool with immutable Job      // accepted: Job's graph is strings and numbers
```

Each refusal is reported at the instantiation the program wrote, naming the argument as the program wrote it.
A type parameter passed on as the argument is judged by the bound of the declaration it belongs to, at the
instantiation that fixes it. A `spawn` that infers a generic service's argument from its factory's arguments is
an instantiation too, reported at the factory the `spawn` calls.

An argument written inside a generic type is judged there, with the type's parameters unknown. When it is refused
whatever the parameter is (a function type; under `sendable` also a tuple, a plain record or a container
instance), it is reported once, where the generic type writes it, and no instantiation of the type reports it
again. When the verdict depends on the
parameter (a bare `T`; an `immutable` argument whose graph reaches `T`; under `isolated`, an aggregate built over
`T`, which is `unique` when `T` is), nothing is reported in the generic type and each instantiation is judged where
it is written.

A generic service's factory parameter or reply whose declared type is the bare type parameter of a `sendable`
bound is judged by the bound alone, so a refused argument is reported once, by the bound. A type built over the
parameter (`Array with T`) crosses to the service as any other type does and needs `isolated` or `immutable`.

## Tests

<!-- test: a-sendable-bound-refuses-a-function-type -->
A function type is not a value: a function value is a closure record that owns what it captures.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias StepPool = Pool with Step

function main() returns ExitCode
	let pool = StepPool.create(function(n) gives n + 1)
	let step = pool.item
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-an-immutable-function-type -->
`immutable` does not make a function type a value, so `immutable Step` is refused as `Step` is.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias FrozenStepPool = Pool with immutable Step
typealias FrozenStepPools = Array with FrozenStepPool

function main() returns ExitCode
	let pools = FrozenStepPools.create(0)
	print("{pools.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-an-interface-type -->
An interface type is not a value: what it holds is whichever conformer was converted to it. A generic type's
argument cannot be an interface type at all, which is reported as well.

```maxon
typealias Tally = int(0 to 1024)

interface Sink
	function total() returns Tally
end 'Sink'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias SinkPool = Pool with Sink
typealias SinkPools = Array with SinkPool

function main() returns ExitCode
	let pools = SinkPools.create(0)
	print("{pools.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:16:11: 'Sink' is an interface type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
error E2015: <fragment>:16:32: Unsupported: a container's element type declared at the interface type 'Sink' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and an element slot is one machine word. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

<!-- test: a-sendable-bound-refuses-a-promise-alias -->
A promise is not a value: it owns a green thread that exactly one owner may reclaim. An alias of `Promise with
Integer` is a promise.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Pending = Promise with Integer

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias PendingPool = Pool with Pending

function seven() returns Integer
	Scheduler.yield()
	return 7
end 'seven'

function main() returns ExitCode
	let pool = PendingPool.create(async seven())
	print("{pool.item.isComplete()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Pending' is a promise, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-an-immutable-promise-alias -->
`immutable` does not make a promise a value, so `immutable Pending` is refused as `Pending` is.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Pending = Promise with Integer

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias PendingPool = Pool with immutable Pending

function seven() returns Integer
	Scheduler.yield()
	return 7
end 'seven'

function main() returns ExitCode
	let pool = PendingPool.create(async seven())
	print("{pool.item.isComplete()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Pending' is a promise, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-an-immutable-record-whose-graph-reaches-a-function-value -->
`immutable Holder` stands as sendable, but a `Holder` holds a function value, which no share walk can mark, so
the bound refuses it and names what the graph reaches.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Holder
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder
typealias HolderPools = Array with HolderPool

function main() returns ExitCode
	let pools = HolderPools.create(0)
	let holder = Holder.create(function(n) gives n + 1)
	print("{pools.count()} {holder.step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:21:11: 'Holder' cannot be shared with another thread, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: it is a `Holder` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread
```

<!-- test: a-sendable-bound-refuses-an-immutable-record-whose-graph-reaches-a-promise -->
`immutable Holder` stands as sendable, but a `Holder` holds a promise, which no share walk can mark, so the bound
refuses it and names what the graph reaches, in the words a `freeze` of a `Holder` uses.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Pending = Promise with Integer

type Holder
	export let pending as Pending

	static function create(pending Pending) returns Self
		return Self{pending: pending}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder
typealias HolderPools = Array with HolderPool

function seven() returns Integer
	Scheduler.yield()
	return 7
end 'seven'

function main() returns ExitCode
	let pools = HolderPools.create(0)
	let holder = Holder.create(async seven())
	print("{pools.count()} {holder.pending.isComplete()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:21:11: 'Holder' cannot be shared with another thread, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: it is a `Holder` whose graph reaches a promise, which owns a green thread that exactly one owner may reclaim
```

<!-- test: a-sendable-bound-accepts-an-immutable-record-whose-interface-field-holds-a-function-value -->
<!-- process: own -->
A `Holder` holds a `Shape`, and the program converts a `Stepper`, which holds a function value, to `Shape`.
The bound judges `Holder`'s own graph, not the conformers of `Shape`, so `immutable Holder` meets it, and in a
program that spawns no service the `freeze` shares nothing with another thread.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper implements Shape
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	function area() returns Tally
		return self.step(1)
	end 'area'
end 'Stepper'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder

function wrap() returns Shape
	return Stepper.create(function(n) gives n + 1)
end 'wrap'

function main() returns ExitCode
	let pool = HolderPool.create(freeze Holder.create(wrap()))
	print("{pool.item.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: freezing-a-record-whose-interface-field-reaches-a-function-value-is-refused-at-the-freeze -->
<!-- unsupported-targets: wasm32-wasi -->
In a program that spawns a service, the `freeze` that makes a `Holder` shared judges what its `Shape` field may
hold, and a `Stepper` holds a function value, so the `freeze` is refused. `Pool with immutable Holder` still
meets the bound: the refusal is the `freeze`'s, and the bound reports nothing.
wasm32-wasi answers `E3104` for its `spawn` before the `freeze` is judged, so the case is pinned on the native lanes.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper implements Shape
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	function area() returns Tally
		return self.step(1)
	end 'area'
end 'Stepper'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = n
	end 'look'
end 'Door'

function wrap() returns Shape
	return Stepper.create(function(n) gives n + 1)
end 'wrap'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let pool = HolderPool.create(freeze Holder.create(wrap()))
	print("{pool.item.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:58:31: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Stepper` stands at `Shape` but no share walk can mark it: its `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:52:9: `Stepper` is converted to `Shape` here
```

<!-- test: a-sendable-bound-accepts-an-immutable-record-whose-interface-field-holds-an-os-handle -->
<!-- process: own -->
A `Holder` holds a `Shape`, and the program converts a `Port`, which holds a listening socket, to `Shape`. The
bound judges `Holder`'s own graph, not the conformers of `Shape`, so `immutable Holder` meets it, and in a program
that spawns no service the `freeze` shares nothing with another thread.

```maxon
typealias Tally = int(0 to 1024)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	export let listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'Port'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder

function open(listener TcpListener) returns Shape
	return Port.create(listener)
end 'open'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let pool = HolderPool.create(freeze Holder.create(open(listener)))
	print("{pool.item.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: freezing-a-record-whose-interface-field-reaches-an-os-handle-is-refused-at-the-freeze -->
<!-- unsupported-targets: wasm32-wasi -->
In a program that spawns a service, the `freeze` that makes a `Holder` shared judges what its `Shape` field may
hold, and a `Port` holds an OS handle, so the `freeze` is refused. `Pool with immutable Holder` still meets the
bound: the refusal is the `freeze`'s, and the bound reports nothing.
wasm32-wasi answers `E3104` for its `spawn` and `TcpListener.bind` before the `freeze` is judged, so the case is pinned on the native lanes.

```maxon
typealias Tally = int(0 to 1024)

interface Shape
	function area() returns Tally
end 'Shape'

type Port implements Shape
	export let listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'Port'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = n
	end 'look'
end 'Door'

function open(listener TcpListener) returns Shape
	return Port.create(listener)
end 'open'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let pool = HolderPool.create(freeze Holder.create(open(listener)))
	print("{pool.item.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:58:31: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Port` stands at `Shape` but no share walk can mark it: its `listener` is a `TcpListener`, which holds an OS handle — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:51:9: `Port` is converted to `Shape` here
```

<!-- test: a-sendable-bound-refuses-a-container-instance-through-an-alias -->
`Tallies` names `Array with Tally`, a container the caller keeps sharing, so it is a plain record and the bound
refuses it, naming it as the program wrote it.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TalliesPool = Pool with Tallies

function main() returns ExitCode
	let tallies = Tallies.create(0)
	tallies.push(3)
	tallies.push(4)
	let pool = TalliesPool.create(tallies)
	print("{pool.item.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Tallies' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Tallies`, or a value or a service handle
```

<!-- test: a-sendable-bound-accepts-an-immutable-container-instance-through-an-alias -->
`immutable Tallies` is shared read-only, so the bound holds.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TalliesPool = Pool with immutable Tallies

function tallies() returns Tallies
	let made = Tallies.create(0)
	made.push(3)
	made.push(4)
	return made
end 'tallies'

function main() returns ExitCode
	let pool = TalliesPool.create(freeze tallies())
	print("{pool.item.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: a-sendable-bound-refuses-a-tuple-alias -->
A tuple is a record the caller keeps sharing, so `Span`, an alias of one, is refused, named by the tuple it
denotes.

```maxon
typealias Tally = int(0 to 1024)
typealias Span = (Tally, Tally)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias SpanPool = Pool with Span

function main() returns ExitCode
	let span = (3, 4) as Span
	let pool = SpanPool.create(span)
	print("{pool.item.0} {pool.item.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: '(Tally, Tally)' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable (Tally, Tally)`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-union-with-a-payload -->
A union whose cases carry a payload is a record the caller keeps sharing, so `Reading` is refused.

```maxon
typealias Tally = int(0 to 1024)

union Reading
	exact(value Tally)
	missing
end 'Reading'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias ReadingPool = Pool with Reading

function main() returns ExitCode
	let pool = ReadingPool.create(Reading.exact(5))
	let shown = match pool.item 'reading'
		exact(value) gives value
		missing gives 0
	end 'reading'
	print("{shown}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:17:11: 'Reading' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Reading`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-an-instance-of-a-generic-record -->
`TallyBox` names `Box with Tally`, an instance of a generic record, which the caller keeps sharing, so the
bound refuses it, naming it as the program wrote it.

```maxon
typealias Tally = int(0 to 1024)

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias TallyBox = Box with Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TallyBoxPool = Pool with TallyBox

function main() returns ExitCode
	let pool = TallyBoxPool.create(TallyBox.create(5))
	print("{pool.item.item}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:22:11: 'TallyBox' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable TallyBox`, or a value or a service handle
```

<!-- test: a-sendable-bound-accepts-an-immutable-tuple-alias -->
`immutable Span` is shared read-only, so the bound holds for the tuple it denotes.

```maxon
typealias Tally = int(0 to 1024)
typealias Span = (Tally, Tally)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias SpanPool = Pool with immutable Span

function span() returns Span
	return (3, 4)
end 'span'

function main() returns ExitCode
	let pool = SpanPool.create(freeze span())
	print("{pool.item.0} {pool.item.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4
```

<!-- test: a-sendable-bound-accepts-an-immutable-union-with-a-payload -->
`immutable Reading` is shared read-only, so the bound holds.

```maxon
typealias Tally = int(0 to 1024)

union Reading
	exact(value Tally)
	missing
end 'Reading'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias ReadingPool = Pool with immutable Reading

function main() returns ExitCode
	let pool = ReadingPool.create(Reading.exact(5))
	let shown = match pool.item 'reading'
		exact(value) gives value
		missing gives 0
	end 'reading'
	print("{shown}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-sendable-bound-accepts-an-immutable-instance-of-a-generic-record -->
`immutable TallyBox` is shared read-only, so the bound holds.

```maxon
typealias Tally = int(0 to 1024)

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias TallyBox = Box with Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TallyBoxPool = Pool with immutable TallyBox

function main() returns ExitCode
	let pool = TallyBoxPool.create(TallyBox.create(5))
	print("{pool.item.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-sendable-bound-refuses-an-immutable-record-holding-an-os-handle-as-unique -->
A record that holds an OS handle in a field of its own is `unique`, so `immutable Holder` does not stand as
sendable and the bound refuses it as a `unique` type.

```maxon
type Holder
	export let listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'
end 'Holder'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias HolderPool = Pool with immutable Holder
typealias HolderPools = Array with HolderPool

function main() returns ExitCode
	let pools = HolderPools.create(0)
	print("{pools.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:18:11: 'Holder' is `unique`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a unique value moves between owners instead of being shared; bound 'T' with `isolated` instead
```

<!-- test: a-sendable-bound-reaches-a-function-type-made-through-a-type-parameter -->
`Outer uses T` holds `Pool with T`; `StepOuter` fills `T` with a function type, so the `Pool with Step` it makes
breaks the `sendable` bound even though no typealias spells it.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(item T) returns Self
		return Self{pool: Inner.create(item)}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step

function main() returns ExitCode
	let outer = StepOuter.create(function(n) gives n + 1)
	let step = outer.pool.item
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:23:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-judges-an-argument-holding-a-type-parameter-where-it-is-fixed -->
`Pool with immutable Held` inside `Outer uses T` still holds `T`, so the template is not judged; `TextOuter` fixes
`T` to `String`, and `Pool with immutable (Box with String)` meets the bound.

```maxon
type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Held = Box with T
	typealias Inner = Pool with immutable Held

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextBox = Box with String
typealias TextPool = Pool with immutable TextBox

function main() returns ExitCode
	let outer = TextOuter.create(TextPool.create(TextBox.create("hello")))
	print("{outer.pool.item.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: a-sendable-bound-refuses-an-argument-holding-a-type-parameter-where-it-is-fixed -->
`Outer with Step` makes `Outer`'s `Pool with immutable Held` a pool of `Box with Step`, whose graph reaches a
function value. The refusal is reported at the instantiation that fixes the parameter.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Held = Box with T
	typealias Inner = Pool with immutable Held

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step
typealias StepOuters = Array with StepOuter

function main() returns ExitCode
	let outers = StepOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:32:11: 'Box with Step' cannot be shared with another thread, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: it is a `Box with Step` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread
```

<!-- test: a-sendable-bound-judges-an-immutable-container-over-a-type-parameter-where-it-is-fixed -->
`Pool with immutable Items` inside `Wrapper uses T` is an `immutable` container over `T`, so whether its graph can be
shared depends on `T`. It is not judged in `Wrapper`; `Wrapper with Tally` fixes `T` to a number, and the pool of
`immutable Array with Tally` meets the bound.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Items = Array with T
	typealias ItemsPool = Pool with immutable Items

	export var pool as ItemsPool

	static function create(pool ItemsPool) returns Self
		return Self{pool: pool}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias TallyWrappers = Array with TallyWrapper

function main() returns ExitCode
	let tallies = TallyWrappers.create(0)
	print("{tallies.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-sendable-bound-refuses-an-immutable-container-over-a-type-parameter-where-it-is-fixed -->
`Wrapper with Step` makes `Wrapper`'s `Pool with immutable Items` a pool of `Array with Step`, whose graph reaches a
function value. The refusal is reported at the instantiation that fixes the parameter.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Items = Array with T
	typealias ItemsPool = Pool with immutable Items

	export var pool as ItemsPool

	static function create(pool ItemsPool) returns Self
		return Self{pool: pool}
	end 'create'
end 'Wrapper'

typealias StepWrapper = Wrapper with Step
typealias StepWrappers = Array with StepWrapper

function main() returns ExitCode
	let steps = StepWrappers.create(0)
	print("{steps.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:24:11: 'Array with Step' cannot be shared with another thread, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: it is a `Array with Step` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread
```

<!-- test: a-sendable-bound-refuses-a-function-type-through-a-nested-alias -->
`Step` is declared inside `Outer`, and an alias of a function type is a function type wherever it is declared.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer
	typealias Step = function(Tally) returns Tally
	typealias StepPool = Pool with Step

	export var pool as StepPool

	static function create() returns Self
		return Self{pool: StepPool.create(function(n) gives n + 1)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let outer = Outer.create()
	let step = outer.pool.item
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Outer.Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-a-tuple-through-a-nested-alias -->
`Span` is declared inside `Outer`, and an alias of a tuple is a plain record wherever it is declared.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer
	typealias Span = (Tally, Tally)
	typealias SpanPool = Pool with Span

	export var pool as SpanPool

	static function create() returns Self
		return Self{pool: SpanPool.create((3, 4) as Span)}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let outer = Outer.create()
	print("{outer.pool.item.0} {outer.pool.item.1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: '(Tally, Tally)' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable (Tally, Tally)`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-an-instance-of-a-generic-record-over-a-nested-alias -->
`Grid.Cell` is a ranged alias declared inside `Grid`, and `CellBox` names `Box with Grid.Cell`, an instance of a generic record the caller keeps sharing, so the bound refuses it, naming it as the program wrote it.

```maxon
type Grid
	export typealias Cell = int(0 to 9)

	export var at as Cell

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Grid'

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias CellBox = Box with Grid.Cell
typealias CellBoxPool = Pool with CellBox

function main() returns ExitCode
	let pool = CellBoxPool.create(CellBox.create(5))
	print("{pool.item.item}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:29:11: 'CellBox' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable CellBox`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-tuple-holding-a-nested-alias -->
A tuple is a record the caller keeps sharing whatever its elements are named by, so one with a nested ranged
alias as an element is refused.

```maxon
typealias Tally = int(0 to 1024)
type Grid
	export typealias Cell = int(0 to 9)

	export var at as Cell

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Grid'

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias Mixed = (Grid.Cell, Tally)
typealias MixedPool = Pool with Mixed

function main() returns ExitCode
	let pool = MixedPool.create((3, 4))
	print("{pool.item.0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:30:11: '(Grid.Cell, Tally)' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable (Grid.Cell, Tally)`, or a value or a service handle
```

<!-- test: a-sendable-bound-accepts-a-nested-ranged-alias -->
`Grid.Cell` is a ranged alias declared inside `Grid`, a scalar, so it is sendable as it stands.

```maxon
type Grid
	export typealias Cell = int(0 to 9)

	export var at as Cell

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Grid'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias GridPool = Pool with Grid.Cell

function main() returns ExitCode
	let pool = GridPool.create(5)
	print("{pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-sendable-bound-accepts-an-immutable-instance-of-a-generic-record-over-a-nested-alias -->
`immutable CellBox` is shared read-only, so the bound holds for a `Box with Grid.Cell`.

```maxon
type Grid
	export typealias Cell = int(0 to 9)

	export var at as Cell

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Grid'

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias CellBox = Box with Grid.Cell
typealias FrozenPool = Pool with immutable CellBox

function main() returns ExitCode
	let pool = FrozenPool.create(CellBox.create(5))
	print("{pool.item.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-sendable-bound-refuses-a-container-instance-through-a-nested-alias-named-from-outside -->
`Outer.Items` names, from outside `Outer`, an alias of `Array with Tally` declared inside it: a container
instance, which the caller keeps sharing.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer
	typealias Items = Array with Tally

	export var at as Tally

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Outer'

typealias ItemsPool = Pool with Outer.Items
typealias ItemsPools = Array with ItemsPool

function main() returns ExitCode
	let pools = ItemsPools.create(0)
	print("{pools.count()} {Outer.create().at}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:22:11: 'Outer.Items' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Outer.Items`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-container-instance-through-an-extension-alias-named-through-a-conformer -->
`Digits.Items` is the `Array with Tally` that `extension Seq` declares for `Digits`, a container instance.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Digits implements Seq with Tally
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Tally
		return self.n
	end 'first'
end 'Digits'

extension Seq
	typealias Items = Array with Item

	function all() returns Items
		return Items.create(0)
	end 'all'
end 'Seq'

typealias ItemsPool = Pool with Digits.Items

function main() returns ExitCode
	let pool = ItemsPool.create(Digits.create(3).all())
	print("{pool.item.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:36:11: 'Digits.Items' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Digits.Items`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-function-type-through-a-nested-alias-of-a-generic-type -->
`Step` is declared inside `Wrapper uses T` as `function(T) returns T`, a function type whatever `T` is. So `Pool
with Step` is refused where `Wrapper` writes it, once, and `Wrapper with Tally` adds no second report.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Step = function(T) returns T
	typealias StepPool = Pool with Step

	export var pool as StepPool

	static function create(step Step) returns Self
		return Self{pool: StepPool.create(step)}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias TallyWrappers = Array with TallyWrapper

function main() returns ExitCode
	let wrappers = TallyWrappers.create(0)
	print("{wrappers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Wrapper.Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-refuses-a-tuple-through-a-nested-alias-of-a-generic-type -->
`Pair` is declared inside `Wrapper uses T` as `(T, T)`, a tuple whatever `T` is, and a tuple is a plain record.
So `Pool with Pair` is refused where `Wrapper` writes it, once, and `Wrapper with Tally` adds no second report.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Pair = (T, T)
	typealias PairPool = Pool with Pair

	export var pool as PairPool

	static function create(pair Pair) returns Self
		return Self{pool: PairPool.create(pair)}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias TallyWrappers = Array with TallyWrapper

function main() returns ExitCode
	let wrappers = TallyWrappers.create(0)
	print("{wrappers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: '(T, T)' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable (T, T)`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-container-through-a-nested-alias-of-a-generic-type -->
`Items` is declared inside `Wrapper uses T` as `Array with T`, a container instance whatever `T` is, and a container
instance is a plain record. So `Pool with Items` is refused where `Wrapper` writes it, once, and `Wrapper with Tally`
adds no second report.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Items = Array with T
	typealias ItemsPool = Pool with Items

	export var pool as ItemsPool

	static function create(items Items) returns Self
		return Self{pool: ItemsPool.create(items)}
	end 'create'
end 'Wrapper'

typealias Tallies = Array with Tally
typealias TallyWrapper = Wrapper with Tally
typealias TallyWrappers = Array with TallyWrapper

function main() returns ExitCode
	let wrappers = TallyWrappers.create(0)
	print("{wrappers.count()} {Tallies.create(0).count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Wrapper.Items' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Wrapper.Items`, or a value or a service handle
```

<!-- test: a-sendable-bound-refuses-a-function-type-in-a-generic-type-nothing-instantiates -->
A generic type is checked where it is declared: `Pool with Step` is refused in `Wrapper` although no program
names a `Wrapper with` anything.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Step = function(T) returns T
	typealias Held = Pool with Step

	export var held as Held

	static function create(step Step) returns Self
		return Self{held: Held.create(step)}
	end 'create'
end 'Wrapper'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:12:12: 'Wrapper.Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-reports-a-fixed-and-an-open-argument-of-one-generic-type-each-where-it-is-decided -->
`Outer` writes `Pool with Step`, refused there, and `Pool with T`, left to each instantiation. `Outer with Step` makes
both the same pool, and each is still reported where it is decided: one in `Outer`, one at `Outer with Step`,
whichever field is declared first.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Fixed = Pool with Step
	typealias Open = Pool with T

	export var fixed as Fixed
	export var open as Open

	static function create(fixed Fixed, open Open) returns Self
		return Self{fixed: fixed, open: open}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step
typealias StepOuters = Array with StepOuter

function main() returns ExitCode
	let outers = StepOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
error E3215: <fragment>:25:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-reaches-an-instance-held-in-a-container-of-a-generic-type -->
An instance a generic type holds only as the element of a container is an instantiation too: `Outer with Step`
makes `Outer`'s `Array with (Pool with T)` an array of pools of `Step`, refused where `T` is fixed.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T
	typealias Inners = Array with Inner

	export var pools as Inners

	static function create() returns Self
		return Self{pools: Inners.create(0)}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step

function main() returns ExitCode
	let outer = StepOuter.create()
	print("{outer.pools.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:24:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-reaches-an-instance-held-in-a-tuple-of-a-generic-type -->
An instance a generic type holds only as the element of a tuple is reached the same way.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T
	typealias Pair = (Inner, Tally)

	export var pair as Pair

	static function create(item T) returns Self
		return Self{pair: (Inner.create(item), 0)}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step

function main() returns ExitCode
	let outer = StepOuter.create(function(n) gives n + 1)
	let (pool, _) = outer.pair
	let step = pool.item
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:24:11: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-reports-an-instance-a-generic-type-holds-in-a-container-once -->
`Pool with Step` is refused where `Outer` writes it. Holding that pool in an array and in a tuple adds no
report, in `Outer` or at `Outer with Step`.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Fixed = Pool with Step
	typealias Fixeds = Array with Fixed
	typealias FixedPair = (Fixed, Tally)

	export var pools as Fixeds
	export var pair as FixedPair

	static function create(pair FixedPair) returns Self
		return Self{pools: Fixeds.create(0), pair: pair}
	end 'create'
end 'Outer'

typealias StepOuter = Outer with Step
typealias StepOuters = Array with StepOuter

function main() returns ExitCode
	let outers = StepOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Step' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-names-a-function-alias-through-an-instance-of-a-generic-type-as-unknown -->
Only a ranged alias declared inside a generic type is named through one of its instances. `TallyWrapper.Step`,
an alias of a function type reached through `Wrapper with Tally`, names no type: it is reported once, as an
unknown type.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Step = function(T) returns T

	export var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias StepPool = Pool with TallyWrapper.Step

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:23:32: Unknown type 'TallyWrapper.Step'
```

<!-- test: a-sendable-bound-names-a-container-alias-through-an-instance-of-a-generic-type-as-unknown -->
`TallyWrapper.Items`, an alias of `Array with T` reached through `Wrapper with Tally`, names no type: it is
reported once, as an unknown type.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Wrapper uses T
	typealias Items = Array with T

	export var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias ItemsPool = Pool with TallyWrapper.Items

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:23:33: Unknown type 'TallyWrapper.Items'
```

<!-- test: a-sendable-bound-names-an-interface-function-alias-as-unknown -->
An interface body declares no alias of a function type, and `Shape.Step` names no type: each is reported once.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Shape
	typealias Step = function(Tally) returns Tally
	function area() returns Tally
end 'Shape'

typealias StepPool = Pool with Shape.Step

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:2: Unsupported: a function typealias inside an `interface` body is not supported — the declaration sweep does not read an interface's members, so the alias would name a type only its own file could resolve; declare it at file scope
error E3011: <fragment>:17:32: Unknown type 'Shape.Step'
```

<!-- test: a-sendable-bound-names-an-interface-container-alias-as-unknown -->
An alias an interface declares is not named through the interface from outside it: `Shape.Items` names no type,
and is reported once, as an unknown type.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Shape
	typealias Items = Array with Tally
	function area() returns Tally
end 'Shape'

typealias ItemsPool = Pool with Shape.Items

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:17:33: Unknown type 'Shape.Items'
```

<!-- test: a-sendable-bound-accepts-a-string -->
A `String` is a value, so it is sendable as it stands.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TextPool = Pool with String

function main() returns ExitCode
	let pool = TextPool.create("hello")
	print("{pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: a-sendable-bound-accepts-a-character -->
A `Character` is a value, so it is sendable as it stands.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias LetterPool = Pool with Character

function main() returns ExitCode
	let pool = LetterPool.create('q')
	print("{pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
q
```

<!-- test: a-sendable-bound-accepts-a-c-string -->
<!-- tier: stdlib -->
A C string is an uncounted word, so it is a value and is sendable as it stands.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TextPool = Pool with cstring

function main() returns ExitCode
	let pool = TextPool.create("hi".cstr())
	print("{pool.item == pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: a-sendable-bound-accepts-a-signed-scalar-alias -->
An alias of a ranged integer is a scalar, so it is sendable as it stands.

```maxon
typealias Offset = int(-50 to 50)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias OffsetPool = Pool with Offset

function main() returns ExitCode
	let pool = OffsetPool.create(-7)
	print("{pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-7
```

<!-- test: a-sendable-bound-accepts-a-payload-free-enum -->
An enum whose cases carry no payload is a value, so it is sendable as it stands.

```maxon
enum Light
	red
	green
end 'Light'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias LightPool = Pool with Light

function main() returns ExitCode
	let pool = LightPool.create(Light.green)
	let shown = match pool.item 'light'
		red gives "red"
		green gives "green"
	end 'light'
	print("{shown}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
green
```

<!-- test: a-sendable-bound-accepts-a-service-handle -->
A service handle reaches its service by message, so it is sendable as it stands.

```maxon
typealias Tally = int(0 to 1024)

type Counter
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Counter'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias CounterPool = Pool with Counter.handle

function main() returns ExitCode
	let pool = CounterPool.create(spawn Counter.create())
	let seen = try await pool.item.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-sendable-bound-accepts-a-shared-value-handle -->
A `SharedValue` handle is read by every thread at once by design, so it is sendable as it stands.

```maxon
typealias Label = SharedValue with String

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias LabelPool = Pool with Label

function main() returns ExitCode
	let pool = LabelPool.create(Label.create("hello"))
	print("{pool.item.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: a-sendable-bound-accepts-a-shared-value-handle-written-directly -->
`SharedValue with String` written as the argument, with no alias, is a `SharedValue` handle as `Label` is.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias LabelPool = Pool with SharedValue with String
typealias Label = SharedValue with String

function main() returns ExitCode
	let pool = LabelPool.create(Label.create("hello"))
	print("{pool.item.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: a-sendable-bound-accepts-the-handle-of-a-service-whose-state-holds-a-function-value -->
A handle reaches its service by message, so what the service's state holds, a function value here, never crosses
with it.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Stepper
	var step as Step

	static function create() returns Self
		return Self{step: function(n) gives n + 1}
	end 'create'

	export function apply(n Tally) returns Tally
		return self.step(n)
	end 'apply'
end 'Stepper'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias StepperPool = Pool with Stepper.handle

function main() returns ExitCode
	let pool = StepperPool.create(spawn Stepper.create())
	let seen = try await pool.item.apply(4) otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: a-sendable-bound-accepts-the-handle-of-a-generic-service-instance -->
`Keeper.handle with String` is the handle of a service instance, so it is sendable as it stands.

```maxon
typealias Tally = int(0 to 1024)

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias KeeperPool = Pool with Keeper.handle with String

function main() returns ExitCode
	let pool = KeeperPool.create(spawn Keeper.create("text"))
	let seen = try await pool.item.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-sendable-bound-accepts-an-immutable-record-of-text-and-numbers -->
`immutable Job` stands as sendable, and a `Job`'s graph is a `String` and an array of numbers, which a share walk
marks, so the bound holds.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Job
	export let name as String
	export let steps as Tallies

	static function create(name String, steps Tallies) returns Self
		return Self{name: name, steps: steps}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias JobPool = Pool with immutable Job

function main() returns ExitCode
	let pool = JobPool.create(freeze Job.create("build", steps: [3, 4]))
	print("{pool.item.name} {pool.item.steps.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
build 2
```

<!-- test: a-sendable-bound-accepts-a-record-packed-in-a-word -->
A record packed in a word is a value: copying it copies the word, so it is sendable as it stands, without
`immutable`.

```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias PairPool = Pool with Pair

function main() returns ExitCode
	let pool = PairPool.create(Pair.create(1, hi: 2))
	print("{pool.item.lo} {pool.item.hi}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-sendable-bound-refuses-a-record-too-wide-to-pack-in-a-word -->
Three 32-bit fields sum past the 64 bits of a word, so `Triple` is a heap record, not a value, although every
field is a `let` of a value: the bound refuses it bare as a plain record.

```maxon
typealias Half = int(0 to u32.max)

type Triple
	export let lo as Half
	export let mid as Half
	export let hi as Half

	static function create(lo Half, mid Half, hi Half) returns Self
		return Self{lo: lo, mid: mid, hi: hi}
	end 'create'
end 'Triple'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TriplePool = Pool with Triple

function main() returns ExitCode
	let pool = TriplePool.create(Triple.create(1, mid: 2, hi: 3))
	print("{pool.item.lo} {pool.item.mid} {pool.item.hi}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:22:11: 'Triple' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Triple`, or a value or a service handle
```

<!-- test: a-sendable-bound-accepts-an-immutable-record-too-wide-to-pack-in-a-word -->
`immutable Triple` is shared read-only, so the bound holds. A `Triple` is a three-slot heap record, as its size
shows.

```maxon
typealias Half = int(0 to u32.max)

type Triple
	export let lo as Half
	export let mid as Half
	export let hi as Half

	static function create(lo Half, mid Half, hi Half) returns Self
		return Self{lo: lo, mid: mid, hi: hi}
	end 'create'
end 'Triple'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TriplePool = Pool with immutable Triple

function main() returns ExitCode
	let pool = TriplePool.create(Triple.create(1, mid: 2, hi: 3))
	print("{pool.item.lo} {pool.item.mid} {pool.item.hi} {sizeof(Triple)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 24
```

<!-- test: a-sendable-bound-refuses-a-unique-record-packed-in-a-word -->
A `unique` type is never a value at a bound, whatever its size: a `Token` whose two fields pack into a word still
moves between owners instead of being shared, so the bound refuses it.

```maxon
typealias Half = int(0 to u32.max)

type Token is unique
	export let lo as Half
	export let hi as Half

	static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Token'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias TokenPool = Pool with Token

function main() returns ExitCode
	let pool = TokenPool.create(Token.create(1, hi: 2))
	print("{pool.item.lo} {pool.item.hi}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:21:11: 'Token' is `unique`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a unique value moves between owners instead of being shared; bound 'T' with `isolated` instead
```

<!-- test: a-sendable-service-bound-reports-a-plain-record-at-a-bare-type-parameter-once -->
`spawn Keeper.create(Job.create(1))` infers `Keeper with Job`. The factory parameter is declared as the bare
`T` of a `sendable` bound, so the bound judges the argument, and a `Job` is a plain record: the bound's refusal
is the one report.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create(Job.create(1))
	let seen = try await keeper.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:27:28: 'Job' is not sendable, so it cannot be 'T' of 'Keeper', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```

<!-- test: a-sendable-service-bound-reports-a-record-holding-a-function-value-once -->
`spawn Keeper.create(Holder.create(bumpOne))` infers `Keeper with Holder`, and a bare `Holder` is a plain record,
so the bound refuses it, and that refusal is the one report.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Holder
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Holder'

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function bumpOne(n Tally) returns Tally
	return n + 1
end 'bumpOne'

function main() returns ExitCode
	let keeper = spawn Keeper.create(Holder.create(bumpOne))
	let seen = try await keeper.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:32:28: 'Holder' is not sendable, so it cannot be 'T' of 'Keeper', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Holder`, or a value or a service handle
```

<!-- test: a-sendable-service-bound-reports-a-plain-record-spawned-by-a-generic-function-once -->
`startKeeper` spawns `Keeper.create(item)` with its own `U`, and the call `startKeeper(Job.create(1))` fixes `U`
to `Job`, which infers `Keeper with Job`: the bound's refusal is the one report.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function startKeeper(item U) uses U returns Tally
	let keeper = spawn Keeper.create(item)
	return try await keeper.bump() otherwise 0
end 'startKeeper'

function main() returns ExitCode
	let seen = startKeeper(Job.create(1))
	print("{seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:27:28: 'Job' is not sendable, so it cannot be 'T' of 'Keeper', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```

<!-- test: a-sendable-service-bound-spawns-with-a-string -->
A `String` is a value, so `spawn Keeper.create("text")` infers `Keeper with String`, which meets the bound,
and the service runs.

```maxon
typealias Tally = int(0 to 1024)

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function main() returns ExitCode
	let keeper = spawn Keeper.create("text")
	let seen = try await keeper.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-sendable-service-bound-spawns-with-a-service-handle -->
A service handle reaches its service by message, so `Keeper with Counter.handle` meets the bound, and the
service runs.

```maxon
typealias Tally = int(0 to 1024)

type Counter
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Counter'

type Keeper uses T where T is sendable
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function main() returns ExitCode
	let counter = spawn Counter.create()
	let first = try await counter.bump() otherwise 0
	let keeper = spawn Keeper.create(counter)
	let seen = try await keeper.bump() otherwise 0
	print("{first} {seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: a-sendable-service-bound-leaves-a-type-built-over-the-parameter-to-the-crossing-rule -->
CONTROL: `Items` is `Array with T`, a type built over the parameter, not the bare parameter, so the factory
parameter crosses to the service as any array does and needs a capability word, even though `Keeper with
String` meets the bound.

```maxon
typealias Tally = int(0 to 1024)
typealias Names = Array with String

type Keeper uses T where T is sendable
	typealias Items = Array with T

	var items as Items
	var seen as Tally

	static function create(items Items) returns Self
		return Self{items: items, seen: 0}
	end 'create'

	export function bump() returns Tally
		self.seen = self.seen + 1
		return self.seen
	end 'bump'
end 'Keeper'

function names() returns Names
	let made = Names.create(0)
	made.push("text")
	return made
end 'names'

function main() returns ExitCode
	let keeper = spawn Keeper.create(names())
	let seen = try await keeper.bump() otherwise 0
	print("{seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3217: <fragment>:28:15: parameter `items` of the factory `Keeper.create` is a `Keeper.Items` with no capability word, and this `spawn` moves its arguments into the state of `Keeper`, a service — whose state is run by whichever green thread takes its next message, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `create(items isolated …)` or `create(items immutable …)`
```

### The `isolated` bound

The `isolated` bound reads the same list of values and handles as `sendable`, and otherwise asks whether the argument is `unique`. A function type and a plain array are refused.

<!-- test: an-isolated-bound-refuses-a-function-type -->
A function type is not a value and is not `unique`, so an `isolated` bound refuses it.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias StepCell = Cell with Step

function main() returns ExitCode
	let cell = StepCell.create(function(n) gives n + 1)
	let step = cell.item
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Step' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-refuses-an-array-of-values -->
An array of plain values is a mutable collection the caller can keep, and it is not `unique`, so an `isolated` bound refuses it.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias TallyCell = Cell with Tallies

function main() returns ExitCode
	let cell = TallyCell.create(Tallies.create(0))
	print("{cell.item.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:11: 'Tallies' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-refuses-an-instance-of-a-generic-record-over-a-nested-alias -->
`CellBox` names `Box with Grid.Cell`, which is not `unique` and not a value, so an `isolated` bound refuses it.

```maxon
type Grid
	export typealias Cell = int(0 to 9)

	export var at as Cell

	static function create() returns Self
		return Self{at: 0}
	end 'create'
end 'Grid'

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias CellBox = Box with Grid.Cell
typealias CellBoxCell = Cell with CellBox

function main() returns ExitCode
	let cell = CellBoxCell.create(CellBox.create(5))
	print("{cell.item.item}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:29:11: 'CellBox' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-refuses-a-function-type-through-a-nested-alias-of-a-generic-type -->
`Step` is declared inside `Wrapper uses T` as `function(T) returns T`, a function type whatever `T` is, and no function
type is isolated. So `Cell with Step` is refused where `Wrapper` writes it, once, and `Wrapper with Tally` adds no
second report.

```maxon
typealias Tally = int(0 to 1024)

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Wrapper uses T
	typealias Step = function(T) returns T
	typealias StepCell = Cell with Step

	export var cell as StepCell

	static function create(step Step) returns Self
		return Self{cell: StepCell.create(step)}
	end 'create'
end 'Wrapper'

typealias TallyWrapper = Wrapper with Tally
typealias TallyWrappers = Array with TallyWrapper

function main() returns ExitCode
	let wrappers = TallyWrappers.create(0)
	print("{wrappers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Wrapper.Step' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-judges-an-aggregate-over-a-type-parameter-where-it-is-fixed -->
`Cell with Held` inside `Outer uses T` holds a `Box with T`, which is `unique` when `T` is, so the verdict depends on
`T` and `Outer` is not judged. `Outer with Token` fixes `T` to a `unique` type, and the `Box with Token` meets the
bound.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T
	typealias Held = Box with T
	typealias HeldCell = Cell with Held

	export var cell as HeldCell

	static function create(cell HeldCell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Outer'

typealias TokenOuter = Outer with Token
typealias TokenOuters = Array with TokenOuter

function main() returns ExitCode
	let tokens = TokenOuters.create(0)
	print("{tokens.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: an-isolated-bound-refuses-an-aggregate-over-a-type-parameter-where-it-is-fixed -->
`Outer with Tally` makes `Outer`'s `Cell with Held` a cell of `Box with Tally`, which is neither a value nor `unique`.
The refusal is reported at the instantiation that fixes the parameter.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T
	typealias Held = Box with T
	typealias HeldCell = Cell with Held

	export var cell as HeldCell

	static function create(cell HeldCell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Outer'

typealias TallyBox = Box with Tally
typealias TallyOuter = Outer with Tally
typealias TallyOuters = Array with TallyOuter

function main() returns ExitCode
	let tallies = TallyOuters.create(0)
	let box = TallyBox.create(0)
	print("{tallies.count()} {box.item}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:40:11: 'TallyBox' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-accepts-a-record-packed-in-a-word -->
A record packed in a word is a value, so an `isolated` bound takes it as `sendable` does.

```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias PairCell = Cell with Pair

function main() returns ExitCode
	let cell = PairCell.create(Pair.create(1, hi: 2))
	print("{cell.item.lo} {cell.item.hi}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: an-isolated-bound-accepts-a-unique-record-packed-in-a-word -->
A `unique` record is what an `isolated` bound takes, whatever its size, so `Cell with Token` holds for a `Token`
whose two fields pack into a word.

```maxon
typealias Half = int(0 to u32.max)

type Token is unique
	export let lo as Half
	export let hi as Half

	static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Token'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias TokenCell = Cell with Token

function main() returns ExitCode
	let cell = TokenCell.create(Token.create(1, hi: 2))
	print("{cell.item.lo} {cell.item.hi}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: an-isolated-bound-accepts-a-c-string -->
<!-- tier: stdlib -->
A C string is an uncounted word, so it is a value and an `isolated` bound takes it as `sendable` does.

```maxon
type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias TextCell = Cell with cstring

function main() returns ExitCode
	let cell = TextCell.create("hi".cstr())
	print("{cell.item == cell.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true
```

<!-- test: an-isolated-bound-accepts-a-shared-value-handle-written-directly -->
A `SharedValue` handle is on the list both bounds read, so an `isolated` bound takes `SharedValue with String`.

```maxon
type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

typealias LabelCell = Cell with SharedValue with String
typealias Label = SharedValue with String

function main() returns ExitCode
	let cell = LabelCell.create(Label.create("hello"))
	print("{cell.item.current()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: an-isolated-bound-refuses-a-container-instance-through-an-extension-alias-named-through-a-conformer -->
`Digits.Items` is the `Array with Tally` that `extension Seq` declares for `Digits`, which is not `unique`.

```maxon
typealias Tally = int(0 to 1024)

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

interface Seq uses Item
	function first() returns Item
end 'Seq'

type Digits implements Seq with Tally
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Tally
		return self.n
	end 'first'
end 'Digits'

extension Seq
	typealias Items = Array with Item

	function all() returns Items
		return Items.create(0)
	end 'all'
end 'Seq'

typealias ItemsCell = Cell with Digits.Items

function main() returns ExitCode
	let cell = ItemsCell.create(Digits.create(3).all())
	print("{cell.item.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:36:11: 'Digits.Items' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: a-sendable-bound-names-an-unknown-argument-once -->
An argument naming no type is reported once, as an unknown type, and the bound adds no second error.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias BogusPool = Pool with Bogus

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:10:33: Unknown type 'Bogus'
```

<!-- test: a-sendable-bound-names-an-unspellable-void-argument-once -->
`void` is not a type an argument can name, so it is reported once, as an unknown type, and the bound adds no
second error.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias VoidPool = Pool with void

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:10:32: Unknown type 'void'
```

<!-- test: a-sendable-bound-names-an-unknown-type-inside-its-argument-once -->
`BogusBox` is `Box with Bogus`, and `Bogus` names no type: that is the one report, and the bound judges nothing
about an argument it cannot know.

```maxon
type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias BogusBox = Box with Bogus
typealias BogusPool = Pool with BogusBox
typealias BogusPools = Array with BogusPool

function main() returns ExitCode
	let pools = BogusPools.create(0)
	print("{pools.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:18:31: Unknown type 'Bogus'
```

<!-- test: a-sendable-bound-names-an-unknown-qualified-argument-once -->
A qualified argument naming no type is reported once, as an unknown type.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

typealias BogusPool = Pool with Bogus.Inner

function main() returns ExitCode
	print("unreachable\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:10:33: Unknown type 'Bogus.Inner'
```

<!-- test: an-isolated-bound-reaches-an-instance-held-in-a-container-of-a-generic-type -->
An `isolated`-bounded instance a generic type holds only as the element of a container is judged where the
parameter is fixed.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T
	typealias Inner = Cell with T
	typealias Inners = Array with Inner

	export var cells as Inners

	static function create() returns Self
		return Self{cells: Inners.create(0)}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tallies

function main() returns ExitCode
	let outer = TallyOuter.create()
	print("{outer.cells.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:24:11: 'Tallies' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

