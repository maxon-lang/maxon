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
A `spawn` that infers a generic service's argument from its factory's arguments is an instantiation too, reported
at the factory the `spawn` calls.

An argument written inside a generic type `D` that is or mentions a parameter `P` of `D` is judged there, from
`D`'s own `where` clause alone, and no instantiation of `D` judges it again. `D with X` is judged against `D`'s own
bounds and nothing else.

- A bare `P` meets `sendable` only when `D` declares `where P is sendable`, and meets `isolated` only when `D`
  declares `where P is isolated`. Neither bound proves the other; a type that fills both kinds of position declares
  both (`where P is sendable and isolated`).
- An `immutable` aggregate over `P` meets `sendable` when every parameter it mentions is bounded `sendable` in `D`,
  it is not `unique`, and the rest of its graph can be shared. A bare aggregate over `P` (a tuple, a container
  instance, an instance of a generic record, a union) never meets `sendable`.
- Under `isolated`, an aggregate over `P` is met only when it is `unique` whatever `P` is: a generic record
  declared `is unique`, or one holding a `unique` part that does not come from `P`.
- A function type, an interface type and a promise are refused in `D` whatever `P` is.

Each refusal is reported once, where `D` writes the argument, however many instantiations `D` has, none included, and
names the parameter, `D` and the bound that would prove it.

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

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-once-whatever-it-is-instantiated-with -->
`Outer uses T` holds `Pool with T` and declares no bound on `T`. The instance is refused once, where `Outer` writes
it; `StepOuter` fills `T` with a function type and adds no report.

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
error E3215: <fragment>:14:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-in-a-generic-type-nothing-instantiates -->
`Outer uses T` declares no bound, so the `Pool with T` it writes is refused where `Outer` writes it although no
program names an `Outer with` anything.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:11:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-once-for-two-instantiations -->
`Outer with Tally` and `Outer with String` are two instantiations of the same unbounded `Outer`. The `Pool with T`
it writes is refused once, where `Outer` writes it, and neither instantiation adds a report.

```maxon
typealias Tally = int(0 to 1024)

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

typealias TallyOuter = Outer with Tally
typealias TextOuter = Outer with String

function main() returns ExitCode
	let tally = TallyOuter.create(1)
	let text = TextOuter.create("hello")
	print("{tally.pool.item} {text.pool.item}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:13:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-accepts-a-type-parameter-its-owner-bounds-sendable -->
`Outer` declares `where T is sendable`, which proves the `sendable` bound of the `Pool with T` it writes, so
`Outer with Tally` is built and used.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(item T) returns Self
		return Self{pool: Inner.create(item)}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally

function main() returns ExitCode
	let outer = TallyOuter.create(7)
	print("{outer.pool.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-sendable-bound-on-the-owner-refuses-a-plain-record-at-its-own-instantiation-only -->
`Outer` bounds `T` `sendable`, so `Outer with Job` is judged against that bound alone: the plain record is refused
once, at `JobOuter`, naming `Outer`, and the `Pool with Job` it makes is not judged again.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(item T) returns Self
		return Self{pool: Inner.create(item)}
	end 'create'
end 'Outer'

typealias JobOuter = Outer with Job

function main() returns ExitCode
	let outer = JobOuter.create(Job.create(1))
	print("{outer.pool.item.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:30:11: 'Job' is not sendable, so it cannot be 'T' of 'Outer', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```

<!-- test: a-sendable-bound-on-the-owner-refuses-a-function-type-at-its-own-instantiation-only -->
`Outer` bounds `T` `sendable`, so `Outer with Step` is refused once, at `StepOuter`, naming `Outer`, and the
`Pool with Step` it makes is not judged again.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
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
error E3215: <fragment>:23:11: 'Step' is a function type, so it cannot be 'T' of 'Outer', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: an-isolated-bound-on-the-owner-does-not-prove-a-sendable-position -->
`Outer` declares `where T is isolated`, which admits `unique` types that `sendable` refuses, so it does not prove
the `sendable` bound of the `Pool with T` it writes.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is isolated
	typealias Inner = Pool with T

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:11:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: an-isolated-bound-on-the-owner-does-not-prove-an-immutable-sendable-argument-over-it -->
`Outer` bounds `T` `isolated`, which admits a `unique` `Token`, but `Pool with immutable Rows`, `Rows` being
`Array with (T, Tally)`, is a `sendable` position and an `isolated` bound does not prove it. It is refused where `Outer`
writes it, once, and `Outer with Token` adds no report.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is isolated
	typealias Pair = (T, Tally)
	typealias Rows = Array with Pair
	typealias Inner = Pool with immutable Rows

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TokenOuter = Outer with Token
typealias TokenOuters = Array with TokenOuter

function main() returns ExitCode
	let outers = TokenOuters.create(0)
	print("{outers.count()} {Token.create(1).n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:23:12: 'Outer.Rows' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-on-the-owner-does-not-prove-an-isolated-position -->
`Outer` declares `where T is sendable`, which admits `immutable` aggregates that `isolated` refuses, so it does not
prove the `isolated` bound of the `Cell with T` it writes.

```maxon
type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T where T is sendable
	typealias Inner = Cell with T

	export var cell as Inner

	static function create(cell Inner) returns Self
		return Self{cell: cell}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:11:12: 'T' of 'Outer' is not bounded `isolated`, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: declare `where T is isolated` on 'Outer'
```

<!-- test: a-generic-type-bounding-its-parameter-both-ways-fills-a-sendable-and-an-isolated-position -->
`Outer` declares `where T is sendable and isolated`, so it proves both bounds: the `Pool with T` and the `Cell with T`
it writes are met, and `Outer with Tally` runs.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T where T is sendable and isolated
	typealias Sent = Pool with T
	typealias Held = Cell with T

	export var sent as Sent
	export var held as Held

	static function create(item T) returns Self
		return Self{sent: Sent.create(item), held: Held.create(item)}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally

function main() returns ExitCode
	let outer = TallyOuter.create(5)
	print("{outer.sent.item} {outer.held.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5
```

<!-- test: a-sendable-bound-refuses-an-immutable-record-over-a-type-parameter-its-owner-does-not-bound -->
`Pool with immutable Held`, `Held` being `Box with T`, inside `Outer uses T` is an `immutable` record over a `T` that `Outer` does not
bound `sendable`. It is refused where `Outer` writes it, once, and `TextOuter` adds no report.

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
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:20:12: 'Outer.Held' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-accepts-an-immutable-record-over-a-type-parameter-its-owner-bounds -->
`Outer` bounds `T` `sendable`, so `Pool with immutable Held`, `Held` being `Box with T`, meets the bound in `Outer`: every parameter it
mentions is bounded, the `Box` is not `unique`, and the rest of its graph can be shared. `TextOuter` is judged
against `Outer`'s own bound only.

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

type Outer uses T where T is sendable
	typealias Held = Box with T
	typealias Inner = Pool with immutable Held

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-sendable-bound-refuses-an-immutable-record-over-a-bounded-type-parameter-whose-graph-reaches-a-function-value -->
`Outer` bounds `T` `sendable`, but `Box` also holds a function value, so `Pool with immutable Held`, `Held` being `Box with T`, cannot be
shared whatever `T` is. It is refused where `Outer` writes it, once, and `Outer with String` adds no report.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Box uses T
	export var item as T
	export var step as Step

	static function create(item T, step Step) returns Self
		return Self{item: item, step: step}
	end 'create'
end 'Box'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
	typealias Held = Box with T
	typealias Inner = Pool with immutable Held

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:24:12: 'Outer.Held' cannot be shared with another thread, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: it is a `Outer.Held` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread
```

<!-- test: a-sendable-bound-refuses-an-immutable-container-over-a-type-parameter-its-owner-does-not-bound -->
`Pool with immutable Held`, `Held` being `Array with T`, inside `Outer uses T` is an `immutable` container over a `T` that `Outer` does
not bound `sendable`. It is refused where `Outer` writes it, once.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Held = Array with T
	typealias Items = Pool with immutable Held

	export var items as Items

	static function create(items Items) returns Self
		return Self{items: items}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:12:12: 'Outer.Held' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-accepts-an-immutable-container-over-a-type-parameter-its-owner-bounds -->
`Outer` bounds `T` `sendable`, so `Pool with immutable Held`, `Held` being `Array with T`, meets the bound in `Outer`, and
`Outer with String` is judged against `Outer`'s own bound only.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
	typealias Held = Array with T
	typealias Items = Pool with immutable Held

	export var items as Items

	static function create(items Items) returns Self
		return Self{items: items}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-sendable-bound-refuses-an-immutable-tuple-over-a-type-parameter-its-owner-does-not-bound -->
`Pool with immutable Pair`, `Pair` being `(T, Tally)`, inside `Outer uses T` is an `immutable` tuple over a `T` that
`Outer` does not bound `sendable`. It is refused where `Outer` writes it, once, and `Outer with Step` adds no report.

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
	typealias Pair = (T, Tally)
	typealias PairPool = Pool with immutable Pair

	export var pool as PairPool

	static function create(pool PairPool) returns Self
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
error E3215: <fragment>:15:12: '(T, Tally)' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-names-the-first-declared-of-two-unbounded-parameters-an-argument-is-built-over -->
`Pair` is `(U, T)` inside `Outer uses T, U`, so `Pool with immutable Pair` is built over two parameters `Outer` does not bound
`sendable`. The refusal names `T`, the one `Outer` declares first, although `Pair` writes `U` first.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T, U
	typealias Pair = (U, T)
	typealias PairPool = Pool with immutable Pair

	export var pool as PairPool

	static function create(pool PairPool) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:12:12: '(U, T)' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-accepts-an-immutable-tuple-over-a-type-parameter-its-owner-bounds -->
`Outer` bounds `T` `sendable`, so `Pool with immutable Pair`, `Pair` being `(T, Tally)`, meets the bound in `Outer`: every
parameter the tuple mentions is bounded and the rest of its graph can be shared. `Outer with String` is judged against
`Outer`'s own bound only.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
	typealias Pair = (T, Tally)
	typealias PairPool = Pool with immutable Pair

	export var pool as PairPool

	static function create(pool PairPool) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-sendable-bound-refuses-an-immutable-phantom-record-over-a-type-parameter-its-owner-does-not-bound -->
`Tag uses T` holds only a `String`, so its graph never reaches `T`, but `Tagged` is `Tag with T` and so mentions `T`.
`Pool with immutable Tagged` inside `Outer uses T` is refused where `Outer` writes it, once, and `TextOuter` adds no report.

```maxon
type Tag uses T
	export let s as String

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'Tag'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Tagged = Tag with T
	typealias Inner = Pool with immutable Tagged

	export var pool as Inner

	static function create(pool Inner) returns Self
		return Self{pool: pool}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	print("{outers.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:20:12: 'Outer.Tagged' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-an-immutable-phantom-record-over-a-function-type-over-a-type-parameter-its-owner-does-not-bound -->
`Tag uses T` holds only a `String`, so its graph never reaches `T`, but `Tagged` is `Tag with Maker` and `Maker` is a function type over `T`, so `Tagged` mentions `T`.
`Pool with immutable Tagged` inside `Outer uses T` is refused where `Outer` writes it, once.

```maxon
typealias Tally = int(0 to 1024)

type Tag uses T
	export let s as String

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'Tag'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Maker = function(T) returns Tally
	typealias Tagged = Tag with Maker
	typealias Inner = Pool with immutable Tagged

	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function f(a Inner) returns Tally
		_ = a
		return self.n
	end 'f'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:23:12: 'Outer.Tagged' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
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

<!-- test: a-recursive-function-alias-held-in-nested-containers-compiles -->
A function alias that names itself in its own parameter list is a finite type. Finding which type parameters an
argument mentions looks inside function types, and meets `Loop` again inside `Loop`: it answers once per alias
and stops there, so the program compiles and runs.

```maxon
typealias Tally = int(0 to 1024)
typealias Loop = function(Loop) returns Tally
typealias Loops = Array with (Array with Loop)

function main() returns ExitCode
	let l = Loops.create(0)
	print("{l.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: a-sendable-bound-refuses-each-of-two-mutually-recursive-function-aliases-over-a-type-parameter -->
`Loop2` and `Loop` name each other and only `Loop` names `T`, so both mention `T`, whichever of them the walk meets first.
`First` is `Tag with Loop` and `Second` is `Tag with Loop2`; each `Pool with immutable` of them is refused where `Outer` writes it.

```maxon
typealias Tally = int(0 to 1024)

type Tag uses P
	export let s as String

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'Tag'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Loop2 = function(Loop) returns Tally
	typealias Loop = function(Loop2, T) returns Tally
	typealias First = Tag with Loop
	typealias Second = Tag with Loop2
	typealias A = Pool with immutable First
	typealias B = Pool with immutable Second

	export var a as A
	export var b as B

	static function create(a A, b B) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:25:12: 'Outer.First' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
error E3215: <fragment>:26:12: 'Outer.Second' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-names-the-first-declared-parameter-for-each-member-of-a-cycle -->
`Loop2` and `Loop` name each other, and between them they mention both `T` and `U`, neither of which `Outer` bounds
`sendable`. Each `Pool with immutable` of `First` or `Second` is refused where `Outer` writes it, and both name `T`, the
one `Outer` declares first, whichever of the two aliases is written first.

```maxon
typealias Tally = int(0 to 1024)

type Tag uses P
	export let s as String

	static function create(s String) returns Self
		return Self{s: s}
	end 'create'
end 'Tag'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T, U
	typealias Loop2 = function(U, Loop) returns Tally
	typealias Loop = function(T, Loop2) returns Tally
	typealias First = Tag with Loop
	typealias Second = Tag with Loop2
	typealias A = Pool with immutable Second
	typealias B = Pool with immutable First

	export var a as A
	export var b as B

	static function create(a A, b B) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:25:12: 'Outer.Second' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
error E3215: <fragment>:26:12: 'Outer.First' is built over 'T' of 'Outer', which is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-function-type-over-a-type-parameter-nested-in-a-container-in-a-generic-type -->
`Maker` is a function type over `T`, so `Pool with Maker` mentions `T` and is judged where `Outer` writes it, although it sits inside `Array`.
It is refused once, where `Outer` writes it, whatever `Outer` is instantiated with.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Maker = function(T) returns Tally
	typealias Pools = Array with (Pool with Maker)

	export var pools as Pools

	static function create(pools Pools) returns Self
		return Self{pools: pools}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally
typealias TallyOuters = Array with TallyOuter

function main() returns ExitCode
	let o = TallyOuters.create(0)
	print("{o.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'Outer.Maker' is a function type, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-reports-a-fixed-and-an-open-argument-of-one-generic-type-in-the-generic-type -->
`Outer` writes `Pool with Step`, refused because a function type is refused whatever `T` is, and `Pool with T`,
refused because `Outer` declares no bound on `T`. Both are reported where `Outer` writes them, in source order, and
`Outer with Step` adds no report.

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
error E3215: <fragment>:15:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-on-the-owner-refuses-a-function-type-whose-pool-it-holds-in-a-container -->
`Outer` bounds `T` `sendable`, so the `Pool with T` it holds as the element of a container is met in `Outer`.
`Outer with Step` is refused at its own instantiation, against `Outer`'s own bound, and no line names `Pool`.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
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
error E3215: <fragment>:24:11: 'Step' is a function type, so it cannot be 'T' of 'Outer', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
```

<!-- test: a-sendable-bound-on-the-owner-refuses-a-function-type-whose-pool-it-holds-in-a-tuple -->
`Outer` bounds `T` `sendable`, so the `Pool with T` it holds as the element of a tuple is met in `Outer` too, and
`Outer with Step` is refused once, at its own instantiation.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T where T is sendable
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
error E3215: <fragment>:24:11: 'Step' is a function type, so it cannot be 'T' of 'Outer', which declares `where T is sendable`: the bound takes a value, a service handle, or an `immutable` type that is not `unique`
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

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-in-a-method-body -->
`Outer uses T` writes `Pool with T` and uses it in a method body. `Outer` declares no bound, so the instance is
refused once, where `Outer` writes it, and `StepOuter` adds no report.

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

	export var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	function wrap(item T) returns T
		let pool = Inner.create(item)
		return pool.item
	end 'wrap'
end 'Outer'

typealias StepOuter = Outer with Step

function main() returns ExitCode
	let outer = StepOuter.create()
	let step = outer.wrap(function(n) gives n + 1)
	print("{step(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:14:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-held-only-by-a-function-type-field -->
The only holder of `Pool with T` is the return type of a function-typed field. The instance is refused once, where
`Outer` writes it.

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
	typealias Maker = function(Tally) returns Inner

	export var make as Maker

	static function create(make Maker) returns Self
		return Self{make: make}
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
error E3215: <fragment>:14:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-held-only-as-a-promise-payload -->
The only holder of `Pool with T` is the payload of a promise a field holds. The instance is refused once, where
`Outer` writes it, although nothing instantiates `Outer`.

```maxon
type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Outer uses T
	typealias Inner = Pool with T
	typealias Pending = Promise with Inner

	export var pending as Pending

	static function create(pending Pending) returns Self
		return Self{pending: pending}
	end 'create'
end 'Outer'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:11:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-under-a-phantom-parameter -->
`Phantom uses P` holds no value of `P`, so `Phantom with (Pool with T)` is a record of a number. The `Pool with T`
inside it is still written in `Outer`, which declares no bound, so it is refused there, once.

```maxon
typealias Tally = int(0 to 1024)

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

type Phantom uses P
	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Phantom'

type Outer uses T
	typealias Held = Phantom with (Pool with T)

	export var held as Held

	static function create() returns Self
		return Self{held: Held.create()}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally

function main() returns ExitCode
	let outer = TallyOuter.create()
	print("{outer.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:21:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
```

<!-- test: a-sendable-bound-refuses-a-type-parameter-its-owner-does-not-bound-under-an-interface-base -->
The only holder of `Pool with T` is the argument of an interface instance, `Seq with (Pool with T)`, that `Outer`
writes. The `Pool with T` is refused once, where `Outer` writes it, and `Outer with Step` adds no report. No type
conforms to `Seq`: a use of an interface states the binding its conformers settled, so a conformer could not fill
`Element` with a `Pool`.

```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Seq uses Element
	function first() returns Element
end 'Seq'

type Outer uses T
	typealias Items = Seq with (Pool with T)

	export var n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function inspect(items Items) returns Tally
		_ = items
		return self.n
	end 'inspect'
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
error E3215: <fragment>:18:12: 'T' of 'Outer' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Outer'
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

<!-- test: a-sendable-bound-accepts-an-associated-type-in-an-extension-of-an-interface-bound-to-a-value -->
`extension Seq` writes `Pool with Item`, and `Jobs` binds `Item` to a `Tally`, which is a value. The alias is judged as `Pool with Tally` for that conformer, and it meets the bound.

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

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Jobs implements Seq with Tally
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Tally
		return self.n
	end 'first'
end 'Jobs'

function main() returns ExitCode
	print("{Jobs.create(3).pooled()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```


<!-- test: a-sendable-bound-refuses-an-associated-type-in-an-extension-of-an-interface-bound-to-a-plain-record -->
An interface's associated type is not a type parameter: `extension Seq` declares `SP` once per conformer, with `Item` replaced by that conformer's binding. `Tasks` binds `Item` to a plain record `Job`, so its `SP` is `Pool with Job`, refused where the extension writes the alias.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Seq uses Item
	function first() returns Item
end 'Seq'

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Tasks implements Seq with Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Job
		return Job.create(self.n)
	end 'first'
end 'Tasks'

function main() returns ExitCode
	print("{Tasks.create(4).pooled()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:25:12: 'Job' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```


<!-- test: a-sendable-bound-refuses-an-associated-type-in-an-extension-of-an-interface-once-for-the-conformer-that-fails -->
The same `extension Seq` with two conformers: `Jobs` binds `Item` to a `Tally` and meets the bound, `Tasks` binds it to a plain record `Job` and does not. The refusal is reported once, for the conformer that fails.

```maxon
typealias Tally = int(0 to 1024)

type Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Job'

type Pool uses T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Pool'

interface Seq uses Item
	function first() returns Item
end 'Seq'

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Jobs implements Seq with Tally
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Tally
		return self.n
	end 'first'
end 'Jobs'

type Tasks implements Seq with Job
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function first() returns Job
		return Job.create(self.n)
	end 'first'
end 'Tasks'

function main() returns ExitCode
	print("{Jobs.create(3).pooled()} {Tasks.create(4).pooled()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:25:12: 'Job' is not sendable, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: a plain record is shared with the caller; use `immutable Job`, or a value or a service handle
```


<!-- test: a-sendable-bound-refuses-an-associated-type-bound-to-a-generic-conformers-unbounded-parameter -->
`Box` binds `Item` to its own parameter `T` and declares no `where` clause, so the alias in `extension Seq` is judged from `Box`'s clause alone, and `T` is not bounded `sendable`. It is reported once, although `Box` is never instantiated.

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

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Box uses T implements Seq with T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function first() returns T
		return self.item
	end 'first'
end 'Box'

function main() returns ExitCode
	print("none\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:17:12: 'T' of 'Box' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Box'
```


<!-- test: a-sendable-bound-refuses-an-associated-type-bound-to-a-generic-conformers-unbounded-parameter-once-for-two-instantiations -->
The same unbounded `Box`, instantiated at `Tally` and at `String` and used. The refusal comes from `Box`'s own clause, so it is reported once, whatever the instantiations.

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

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Box uses T implements Seq with T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function first() returns T
		return self.item
	end 'first'
end 'Box'

typealias TallyBox = Box with Tally
typealias TextBox = Box with String

function main() returns ExitCode
	print("{TallyBox.create(3).pooled()} {TextBox.create("a").pooled()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:17:12: 'T' of 'Box' is not bounded `sendable`, so it cannot be 'T' of 'Pool', which declares `where T is sendable`: declare `where T is sendable` on 'Box'
```


<!-- test: a-sendable-bound-accepts-an-associated-type-bound-to-a-generic-conformers-bounded-parameter -->
`Box` binds `Item` to its parameter `T` and declares `where T is sendable`, so the alias in `extension Seq` is judged from that clause and meets the bound; `Box with Tally` runs.

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

extension Seq
	typealias SP = Pool with Item

	function pooled() returns Tally
		let p = SP.create(self.first())
		_ = p
		return 1
	end 'pooled'
end 'Seq'

type Box uses T implements Seq with T where T is sendable
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function first() returns T
		return self.item
	end 'first'
end 'Box'

typealias TallyBox = Box with Tally

function main() returns ExitCode
	print("{TallyBox.create(3).pooled()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
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

<!-- test: a-sendable-bound-accepts-the-handle-of-a-generic-service-over-a-type-parameter-its-owner-does-not-bound -->
`Counter.handle with T` is the handle of a service instance, so it is sendable as it stands, whatever `T` is. `Outer`
bounds nothing and `Pool with Counter.handle with T` meets the bound there.

```maxon
typealias Tally = int(0 to 1024)

type Counter uses T
	var item as T
	var seen as Tally

	static function create(item T) returns Self
		return Self{item: item, seen: 0}
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

type Outer uses T
	typealias Handles = Pool with Counter.handle with T

	export var handles as Handles

	static function create(handles Handles) returns Self
		return Self{handles: handles}
	end 'create'
end 'Outer'

typealias TextOuter = Outer with String
typealias TextOuters = Array with TextOuter

function main() returns ExitCode
	let outers = TextOuters.create(0)
	let counter = spawn Counter.create("text")
	let seen = try await counter.bump() otherwise 0
	print("{outers.count()} {seen}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 1
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

<!-- test: a-generic-function-called-with-its-callers-type-parameter-is-refused-at-the-call-before-any-bound -->
A method of `Outer uses T` calls `startKeeper(self.item)`, which would infer `U` as `Outer`'s own `T`. A generic
function's type parameter is never inferred as another declaration's type parameter, so the call is refused in
`Outer`, where the author must change it, and no copy of `startKeeper` exists for the bound of `Keeper` to judge.

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

function startKeeper(item U) uses U returns Tally
	let keeper = spawn Keeper.create(item)
	return try await keeper.bump() otherwise 0
end 'startKeeper'

type Outer uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function start() returns Tally
		let seen = startKeeper(self.item)
		return seen
	end 'start'
end 'Outer'

typealias TallyOuter = Outer with Tally

function main() returns ExitCode
	let outer = TallyOuter.create(1)
	print("{outer.start()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3181: <fragment>:31:14: type parameter 'U' of 'startKeeper' cannot be 'type parameter': a generic function's type parameter is inferred as a number (a ranged typealias included), `String`, `bool`, `Character`, a `type` declaration or an enum; a generic instance, whichever typealias names it, is none of these
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

<!-- test: an-isolated-bound-refuses-an-aggregate-over-a-type-parameter-its-owner-does-not-bound -->
`Cell with (Box with T)` inside `Outer uses T` holds a plain `Box` over `T`, which is not `unique` whatever `T` is.
It is refused where `Outer` writes it, once, and `Outer with Token` adds no report even though that `Box` would be
`unique`.

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
	typealias HeldCell = Cell with (Box with T)

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
```maxoncstderr
error E3215: <fragment>:29:12: 'Box with T' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-refuses-an-aggregate-over-a-type-parameter-its-owner-bounds-isolated -->
`Outer` bounds `T` `isolated`, but a plain `Box` over `T` is neither a value nor `unique`, so `Cell with (Box with T)`
is refused where `Outer` writes it, once. `Outer with Tally` is judged against `Outer`'s own bound only.

```maxon
typealias Tally = int(0 to 1024)

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

type Outer uses T where T is isolated
	typealias Held = Cell with (Box with T)

	export var cell as Held

	static function create(cell Held) returns Self
		return Self{cell: cell}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally
typealias TallyOuters = Array with TallyOuter

function main() returns ExitCode
	let tallies = TallyOuters.create(0)
	print("{tallies.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3215: <fragment>:21:12: 'Box with T' is not isolated, so it cannot be 'T' of 'Cell', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

<!-- test: an-isolated-bound-accepts-a-unique-aggregate-over-a-type-parameter-its-owner-does-not-bound -->
A generic `Box` declared `is unique` is `unique` whatever `T` is, so `Cell with (Box with T)` meets the `isolated` bound
inside an unbounded `Outer`, and `Outer with Tally` adds nothing.

```maxon
typealias Tally = int(0 to 1024)

type Box is unique uses T
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
	typealias Held = Cell with (Box with T)

	export var cell as Held

	static function create(cell Held) returns Self
		return Self{cell: cell}
	end 'create'
end 'Outer'

typealias TallyOuter = Outer with Tally
typealias TallyOuters = Array with TallyOuter

function main() returns ExitCode
	let tallies = TallyOuters.create(0)
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

<!-- test: an-isolated-bound-on-the-owner-refuses-a-plain-container-whose-cell-it-holds-in-a-container -->
`Outer` bounds `T` `isolated`, so the `Cell with T` it holds in a container is met in `Outer`. `Outer with Tallies`
is refused at its own instantiation, against `Outer`'s own bound, and no line names `Cell`.

```maxon
typealias Tally = int(0 to 1024)
typealias Tallies = Array with Tally

type Cell uses T where T is isolated
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Outer uses T where T is isolated
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
error E3215: <fragment>:24:11: 'Tallies' is not isolated, so it cannot be 'T' of 'Outer', which declares `where T is isolated`: the bound takes a value, a service handle or a `unique` type
```

