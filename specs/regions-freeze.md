---
feature: regions-freeze
status: experimental
keywords: [freeze, merge, isolated, immutable, region, use after move]
category: semantics
---

# `freeze` and `merge`

## Documentation

`freeze x` turns an `isolated` region into frozen data: the result is `immutable`, shared freely, and never
written again. It consumes a local, so reading the local afterwards is E3102. An expression whose inputs are
all values, `immutable` data or moved `isolated` data cannot point back into the current region, so
`freeze Point.create(2, y: 3)` needs no `isolated` declaration. A local that was written as ordinary mutable
data is part of its region and cannot be frozen: freeze a `clone()` of it instead.

`merge x` is the reverse: it moves an `isolated` region into the current one and the result is ordinary
mutable data.

## Tests

<!-- test: freeze-of-a-fresh-call-runs -->
The call's inputs are plain numbers, so its result is a region of its own and `freeze` accepts it; `plot` only reads its parameter and takes the frozen point and the mutable one alike.

```maxon
typealias Coord = int(0 to 1024)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function plot(p Point) returns String
	return "({p.x}, {p.y})"
end 'plot'

function main() returns ExitCode
	let cursor = Point.create(0, y: 0)
	cursor.x = 7
	let pinned = freeze Point.create(2, y: 3)
	print("{plot(pinned)} {plot(cursor)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
(2, 3) (7, 0)
```

<!-- test: freeze-of-a-mutable-local-refused -->
`cursor` was written as ordinary mutable data, so it belongs to the current region and cannot be frozen in place.

```maxon
typealias Coord = int(0 to 1024)

type Point
	export var x as Coord
	export var y as Coord

	static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var cursor = Point.create(0, y: 0)
	cursor.x = 1
	let pinned = freeze cursor
	print("{pinned.x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:16:22: 'cursor' is used as mutable at line 15; freeze a copy: `freeze cursor.clone()`
```

<!-- test: use-after-freeze -->
`freeze` consumes the isolated local, so the read of `bag` after it is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function main() returns ExitCode
	var bag = Bag.create() as isolated Bag

	modify bag 'fill'
		bag.add(3)
	end 'fill'

	let done = freeze bag
	let after = bag.size()
	print("{done.size()} {after}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:14: use of moved value 'bag': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-of-an-isolated-local-moves-it -->
`let b = a` hands the isolated region from `a` to `b`, so the read of `a` afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(1) as isolated Box
	let b = a
	let again = a.n
	print("{b.n} {again}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:14: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-of-an-isolated-local-runs-through-the-new-name -->
After `let b = a` only `b` names the region, and it is read through a one-statement open and released once at the end of the function.

```maxon
typealias Tally = int(0 to u64.max)

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Box.create(4) as isolated Box
	let b = a
	print("{b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: merge-isolated-into-region-runs -->
`merge chunk` moves the filled region into the current one, where the result is ordinary mutable data that `add` writes.

```maxon
typealias Tally = int(0 to u64.max)

type Bag
	var total as Tally

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function add(n Tally)
		self.total = self.total + n
	end 'add'

	function size() returns Tally
		return self.total
	end 'size'
end 'Bag'

function main() returns ExitCode
	let chunk = Bag.create() as isolated Bag

	modify chunk 'fill'
		chunk.add(5)
	end 'fill'

	let mine = merge chunk
	mine.add(7)
	print("{mine.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```

<!-- test: freeze-of-an-interface-value-whose-conformers-all-walk-runs -->
`Plain` and every `Gen` the program builds are converted to `Sink`, and each has a share walk, so a frozen `Holder` of either is marked shared and read back; the spawn makes the share walk run at the `freeze`.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Plain implements Sink
	var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function total() returns Tally
		return self.n
	end 'total'
end 'Plain'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	static function sink(seed T, seen Tally) returns Sink
		return Gen.create(seed, seen: seen)
	end 'sink'
end 'Wrap'

typealias TallyWrap = Wrap with Tally

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()

	let plain = freeze Holder.create(Plain.create(3))
	let generic = freeze Holder.create(TallyWrap.sink(4, seen: 5))
	print("{plain.sink.total()} {generic.sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 5
```

<!-- test: error.freeze-of-an-interface-whose-converted-conformer-holds-an-os-handle -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` and `TcpListener.bind` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

The `Holder` frozen here holds a `Plain`, but `open` converts a `Port` to `Shape`, so a frozen `Holder` may reach a listening socket that no share walk can mark.

```maxon
typealias Tally = int(0 to u64.max)

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

interface Shape
	function area() returns Tally
end 'Shape'

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

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

function open(listener TcpListener) returns Shape
	return Port.create(listener)
end 'open'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let held = freeze Holder.create(Plain.create())
	print("{held.shape.area()} {open(listener).area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:58:13: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Port` stands at `Shape` but no share walk can mark it: its `listener` is a `TcpListener`, which holds an OS handle — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:51:9: `Port` is converted to `Shape` here
```

<!-- test: error.freeze-reaching-an-interface-through-a-conformers-own-interface-field -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` and `TcpListener.bind` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`Wrapper` stands at `Outer` and holds an `Inner`, and `Port` stands at `Inner`, so a frozen `Holder` may reach a listening socket two interfaces down.

```maxon
typealias Tally = int(0 to u64.max)

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

interface Inner
	function area() returns Tally
end 'Inner'

interface Outer
	function measure() returns Tally
end 'Outer'

type Calm implements Inner
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Calm'

type Port implements Inner
	export let listener as TcpListener

	static function create(listener TcpListener) returns Self
		return Self{listener: listener}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'Port'

type Wrapper implements Outer
	export let inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'

	function measure() returns Tally
		return self.inner.area()
	end 'measure'
end 'Wrapper'

type Holder
	export let outer as Outer

	static function create(outer Outer) returns Self
		return Self{outer: outer}
	end 'create'
end 'Holder'

function open(listener TcpListener) returns Inner
	return Port.create(listener)
end 'open'

function wrapped() returns Outer
	return Wrapper.create(Calm.create())
end 'wrapped'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let held = freeze Holder.create(wrapped())
	print("{held.outer.measure()} {open(listener).area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:78:13: `Holder` cannot be frozen: its `outer` is an `Outer`, and `Wrapper` stands at `Outer`, and its `inner` is an `Inner`, and `Port` stands at `Inner` but no share walk can mark it: its `listener` is a `TcpListener`, which holds an OS handle — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:71:9: `Wrapper` is converted to `Outer` here
note: <fragment>:67:9: `Port` is converted to `Inner` here
```

<!-- test: error.freeze-of-an-interface-whose-generic-conformer-is-built-at-a-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`Wrap` converts a `Gen with T` to `Sink` in its generic body, and the program builds `Wrap with Step`, so a `Gen with Step` holding a function value stands at `Sink`.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

interface Sink
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	static function sink(seed T, seen Tally) returns Sink
		return Gen.create(seed, seen: seen)
	end 'sink'
end 'Wrap'

typealias StepWrap = Wrap with Step

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let held = freeze Holder.create(StepWrap.sink(function(n Tally) gives n + 1, seen: 5))
	print("{held.sink.total()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:53:13: `Holder` cannot be frozen: its `sink` is a `Sink`, and `Gen with Step` stands at `Sink` but no share walk can mark it: its `held` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:36:10: `Gen with Step` is converted to `Sink` here
```

<!-- test: freeze-of-an-interface-whose-generic-conformer-nests-its-own-instance-runs -->
`Gen with T` stands at `Sink` through `Wrap`'s generic body, and `Gen`'s own `nest` builds a `Gen with (Gen with T)`, an instance one level deeper than its receiver; the conformers a `Sink` may hold are still finite, so the `freeze` compiles and runs.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function nest(inner T, seen Tally) returns Tally
		let deeper = Gen.create(Gen.create(inner, seen: seen), seen: seen + 1)
		return deeper.total()
	end 'nest'
end 'Gen'

type Wrap uses T
	static function sink(seed T, seen Tally) returns Sink
		return Gen.create(seed, seen: seen)
	end 'sink'
end 'Wrap'

typealias TallyGen = Gen with Tally
typealias TallyWrap = Wrap with Tally

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let held = freeze Holder.create(TallyWrap.sink(4, seen: 5))
	let nested = TallyGen.nest(4, seen: 6)
	print("{held.sink.total()} {nested}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 7
```

<!-- test: freeze-of-an-interface-whose-generic-conformers-name-ever-deeper-instances-runs -->
`Gen with T` stands at `Sink` through `Wrap`'s generic body, and `Gen` and `Pair` name each other over ever-deeper arguments (`Gen with Tally` builds a `Pair with Tally`, whose `keep` names a `Gen with (Box with Tally)`, and so on); the conformers a `Sink` may hold are still decided in finite time, so the `freeze` compiles and runs.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	typealias TPair = Pair with T
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function pair(inner T, seen Tally) returns Tally
		let paired = TPair.create(inner, seen: seen)
		return paired.count()
	end 'pair'
end 'Gen'

type Pair uses U
	typealias UBox = Box with U
	typealias BoxGen = Gen with UBox
	var first as U
	var seen as Tally

	static function create(first U, seen Tally) returns Self
		return Self{first: first, seen: seen}
	end 'create'

	function count() returns Tally
		return self.seen
	end 'count'

	static function keep(boxed BoxGen) returns BoxGen
		return boxed
	end 'keep'
end 'Pair'

type Box uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Wrap uses T
	static function sink(seed T, seen Tally) returns Sink
		return Gen.create(seed, seen: seen)
	end 'sink'
end 'Wrap'

typealias TallyGen = Gen with Tally
typealias TallyPair = Pair with Tally
typealias TallyWrap = Wrap with Tally
typealias TallyBox = Box with Tally
typealias BoxedGen = Gen with TallyBox

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let held = freeze Holder.create(TallyWrap.sink(4, seen: 5))
	print("{held.sink.total()} {TallyGen.pair(4, seen: 6)} {TallyPair.keep(BoxedGen.create(TallyBox.create(4), seen: 8)).total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 6 8
```

<!-- test: generic-conformers-naming-ever-deeper-instances-without-a-freeze-runs -->
The program above with its `freeze` taken away: `Gen` and `Pair` still name each other over ever-deeper arguments, and with no share door to judge, nothing asks which conformers a `Sink` may hold.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	typealias TPair = Pair with T
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function pair(inner T, seen Tally) returns Tally
		let paired = TPair.create(inner, seen: seen)
		return paired.count()
	end 'pair'
end 'Gen'

type Pair uses U
	typealias UBox = Box with U
	typealias BoxGen = Gen with UBox
	var first as U
	var seen as Tally

	static function create(first U, seen Tally) returns Self
		return Self{first: first, seen: seen}
	end 'create'

	function count() returns Tally
		return self.seen
	end 'count'

	static function keep(boxed BoxGen) returns BoxGen
		return boxed
	end 'keep'
end 'Pair'

type Box uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

type Wrap uses T
	static function sink(seed T, seen Tally) returns Sink
		return Gen.create(seed, seen: seen)
	end 'sink'
end 'Wrap'

typealias TallyGen = Gen with Tally
typealias TallyPair = Pair with Tally
typealias TallyWrap = Wrap with Tally
typealias TallyBox = Box with Tally
typealias BoxedGen = Gen with TallyBox

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let held = Holder.create(TallyWrap.sink(4, seen: 5))
	print("{held.sink.total()} {TallyGen.pair(4, seen: 6)} {TallyPair.keep(BoxedGen.create(TallyBox.create(4), seen: 8)).total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 6 8
```

<!-- test: error.freeze-in-a-generic-type-body-whose-instance-reaches-an-unwalkable-conformer -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`Froze`'s body is compiled once, and its `freeze` is judged at every instance the program builds: at `Froze with Holder` it freezes a `Cell with Holder`, whose `Shape` is a `Stepper` holding a function value.

```maxon
typealias Tally = int(0 to u64.max)
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

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Froze uses T
	typealias TCell = Cell with T

	static function run(x T) returns Tally
		let f = freeze TCell.create(x).clone()
		_ = f
		return 1
	end 'run'
end 'Froze'

typealias HolderFroze = Froze with Holder

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()
	print("{HolderFroze.run(Holder.create(Stepper.create(function(n Tally) gives n + 1)))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:53:11: `Cell with Holder` cannot be frozen: its `item.shape` is a `Shape`, and `Stepper` stands at `Shape` but no share walk can mark it: its `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:64:40: `Stepper` is converted to `Shape` here
```

<!-- test: error.freeze-in-a-generic-type-body-whose-instance-holds-a-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

At `Froze with Stepper` the compiled-once `freeze` reaches `Stepper`'s function value with no interface in between.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Froze uses T
	typealias TCell = Cell with T

	static function run(x T) returns Tally
		let f = freeze TCell.create(x).clone()
		_ = f
		return 1
	end 'run'
end 'Froze'

typealias StepperFroze = Froze with Stepper

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()
	print("{StepperFroze.run(Stepper.create(function(n Tally) gives n + 1))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:37:11: `Cell with Stepper` cannot be frozen: it is a `Cell with Stepper` whose graph reaches a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches
```

<!-- test: freeze-in-a-generic-type-body-at-two-instances-whose-graphs-walk-runs -->
One compiled-once body freezes a `Cell with Holder` and a `Cell with Tally` in a program that spawns a service; each instance's clone and share walk are its own.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Froze uses T
	typealias TCell = Cell with T

	static function run(x T) returns immutable TCell
		return freeze TCell.create(x).clone()
	end 'run'
end 'Froze'

typealias HolderFroze = Froze with Holder
typealias TallyFroze = Froze with Tally

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()
	let got = HolderFroze.run(Holder.create(Plain.create()))
	let t = TallyFroze.run(4)
	print("{got.item.shape.area()} {t.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 4
```

<!-- test: clone-in-a-generic-type-body-at-two-instances-one-holding-an-interface-runs -->
One compiled-once body clones a `Cell with Holder`, whose `Holder` holds a `Shape`, and a `Cell with Plain`; each instance's clone walks its own graph.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Froze uses T
	typealias TCell = Cell with T

	static function run(x T) returns TCell
		return TCell.create(x).clone()
	end 'run'
end 'Froze'

typealias HolderFroze = Froze with Holder
typealias PlainFroze = Froze with Plain

function main() returns ExitCode
	let got = HolderFroze.run(Holder.create(Plain.create()))
	let t = PlainFroze.run(Plain.create())
	print("{got.item.shape.area()} {t.item.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: error.freeze-through-a-generic-type-that-calls-another-generic-type-reaches-a-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`Outer` builds `Froze` over its own parameter, and `Froze`'s generic body converts a `Gen with T` to `Shape`; the program builds `Outer with Stepper`, so the instance that stands at `Shape` is a `Gen with Stepper` that no `Outer` body names directly.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Gen uses T implements Shape
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function area() returns Tally
		return self.seen
	end 'area'
end 'Gen'

type Froze uses T
	typealias TGen = Gen with T

	static function run(x T, seen Tally) returns Holder
		return Holder.create(TGen.create(x, seen: seen))
	end 'run'
end 'Froze'

type Outer uses T
	typealias TFroze = Froze with T

	static function go(x T) returns Holder
		return TFroze.run(x, seen: 3)
	end 'go'
end 'Outer'

typealias StepOuter = Outer with Stepper

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()
	let f = freeze StepOuter.go(Stepper.create(function(n Tally) gives n + 1))
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:71:10: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Gen with Stepper` stands at `Shape` but no share walk can mark it: its `held.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:54:24: `Gen with Stepper` is converted to `Shape` here
```

<!-- test: error.freeze-through-a-generic-type-that-names-another-by-its-bare-name-reaches-a-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

The same chain with `Outer` calling `Froze.run` by its bare name, so the instance is inferred from the argument rather than spelled.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Gen uses T implements Shape
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function area() returns Tally
		return self.seen
	end 'area'
end 'Gen'

type Froze uses T
	typealias TGen = Gen with T

	static function run(x T, seen Tally) returns Holder
		return Holder.create(TGen.create(x, seen: seen))
	end 'run'
end 'Froze'

type Outer uses T

	static function go(x T) returns Holder
		return Froze.run(x, seen: 3)
	end 'go'
end 'Outer'

typealias StepOuter = Outer with Stepper

function main() returns ExitCode
	let h = spawn Door.create()
	h.shutdown()
	let f = freeze StepOuter.go(Stepper.create(function(n Tally) gives n + 1))
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:70:10: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Gen with Stepper` stands at `Shape` but no share walk can mark it: its `held.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:54:24: `Gen with Stepper` is converted to `Shape` here
```

<!-- test: error.freeze-door-in-a-generic-type-reached-through-another-generic-types-body -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

The `freeze` sits in `Froze`'s generic body and the program reaches `Froze` only through `Outer with Holder`, so the frozen `Cell with Holder` is an instance no entry of the program names.

```maxon
typealias Tally = int(0 to u64.max)
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

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Froze uses T
	typealias TCell = Cell with T

	static function run(x T) returns immutable TCell
		return freeze TCell.create(x).clone()
	end 'run'
end 'Froze'

type Outer uses T
	typealias TFroze = Froze with T
	typealias TCell = Cell with T

	static function go(x T) returns immutable TCell
		return TFroze.run(x)
	end 'go'
end 'Outer'

typealias HolderOuter = Outer with Holder

function main() returns ExitCode
	let d = spawn Door.create()
	d.shutdown()
	let got = HolderOuter.go(Holder.create(Stepper.create(function(n Tally) gives n + 1)))
	print("{got.item.shape.area()} {Plain.create().area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:63:10: `Cell with Holder` cannot be frozen: its `item.shape` is a `Shape`, and `Stepper` stands at `Shape` but no share walk can mark it: its `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:81:41: `Stepper` is converted to `Shape` here
```

<!-- test: freeze-of-a-generic-conformer-with-swapped-arguments-over-a-finite-cycle-runs -->
`Gen`'s own body widens `Gen with (B, A)` to `Sink`, so the instances a `Sink` may hold cycle between `Gen with (Tally, bool)` and `Gen with (bool, Tally)`; the cycle is finite, so the `freeze` compiles and runs.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Gen uses A, B implements Sink
	typealias Flip = Gen with (B, A)
	var first as A
	var second as B
	var seen as Tally

	static function create(first A, second B, seen Tally) returns Self
		return Self{first: first, second: second, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function flipped(first A, second B, seen Tally) returns Sink
		return Flip.create(second, second: first, seen: seen)
	end 'flipped'
end 'Gen'

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

typealias Pairing = Gen with (Tally, bool)

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let held = freeze Holder.create(Pairing.flipped(3, second: true, seen: 7))
	print("{held.sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: freeze-of-a-deeper-instance-whose-fields-do-not-depend-on-the-parameter-runs -->
`Box` freezes a `Box with (Cell with T)`, an instance one level deeper than its receiver, but no field of `Box` holds `T`, so every depth is judged alike; the chain is not refused and the program runs.

```maxon
typealias Tally = int(0 to u64.max)
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

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Box uses T
	typealias Deeper = Box with (Cell with T)
	export let seen as Tally
	export var name as String

	static function create(seen Tally) returns Self
		return Self{seen: seen, name: "box"}
	end 'create'

	function deeper() returns immutable Deeper
		return freeze Deeper.create(self.seen + 1)
	end 'deeper'
end 'Box'

typealias TallyBox = Box with Tally

function main() returns ExitCode
	let d = spawn Door.create()
	d.shutdown()
	let f = TallyBox.create(1).deeper()
	print("{f.seen} {Plain.create().area()} {Stepper.create(function(n Tally) gives n).area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 1
```

<!-- test: error.freeze-door-of-ever-deeper-instances-whose-fields-hold-the-growing-parameter -->
`Box` freezes a `Box with (Cell with T)` whose field holds the growing argument, so the instances a share door must judge are an unbounded chain.

```maxon
typealias Tally = int(0 to u64.max)

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Box uses T
	typealias TCell = Cell with T
	typealias Deeper = Box with (Cell with T)
	export let held as T
	export let seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function deeper() returns immutable Deeper
		return freeze Deeper.create(TCell.create(self.held), seen: self.seen + 1).clone()
	end 'deeper'
end 'Box'

typealias TextBox = Box with String

function main() returns ExitCode
	let grown = TextBox.create("held text", seen: 1).deeper()
	print("{grown.seen}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:23:10: Unsupported: sharing is judged at every instance the program builds of 'Box', and 'Box' names 'Box.Deeper', an instance whose type arguments nest its own type parameters deeper — so the instances a share door must judge are an unbounded chain of ever-deeper instances. Give that type argument a concrete type, or pass the value it wraps as a type parameter of its own
```

<!-- test: a-generic-body-widening-its-own-instance-releases-what-it-holds-runs -->
`Gen.sink` widens its own instance to `Sink` inside its generic body; the instance's table destroys what `Gen with (Tally, String)` holds, so the program leaks nothing.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Gen uses A, B implements Sink
	var first as A
	var second as B
	var seen as Tally

	static function create(first A, second B, seen Tally) returns Self
		return Self{first: first, second: second, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function sink(first A, second B, seen Tally) returns Sink
		return Gen.create(first, second: second, seen: seen)
	end 'sink'
end 'Gen'

typealias TextGen = Gen with (Tally, String)

function main() returns ExitCode
	let sink = TextGen.sink(1, second: "a string long enough to live on the heap for sure", seen: 5)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: freeze-of-a-generic-bodys-own-instance-widening-runs -->
`Gen.sink` widens its own instance to `Sink`, and the program freezes a `Holder` holding a `Gen with Tally`, whose graph every thread can walk.

```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function total() returns Tally
end 'Sink'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function sink(held T, seen Tally) returns Sink
		return Gen.create(held, seen: seen)
	end 'sink'
end 'Gen'

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

typealias TallyGen = Gen with Tally

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let held = freeze Holder.create(TallyGen.sink(4, seen: 5))
	print("{held.sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: error.freeze-of-a-generic-bodys-own-instance-holding-a-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`Gen`'s own body widens its instance to `Sink`, and the program builds it at a `Stepper`, which holds a function value; `Gen with Tally` beside it stays walkable.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Sink
	function total() returns Tally
end 'Sink'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T, seen Tally) returns Self
		return Self{held: held, seen: seen}
	end 'create'

	function total() returns Tally
		return self.seen
	end 'total'

	static function sink(held T, seen Tally) returns Sink
		return Gen.create(held, seen: seen)
	end 'sink'
end 'Gen'

type Holder
	export let sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'
end 'Holder'

typealias StepGen = Gen with Stepper
typealias TallyGen = Gen with Tally

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let held = freeze Holder.create(StepGen.sink(Stepper.create(function(n Tally) gives n + 1), seen: 5))
	print("{held.sink.total()} {TallyGen.sink(4, seen: 6).total()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:60:13: `Holder` cannot be frozen: its `sink` is a `Sink`, and `StepGen` stands at `Sink` but no share walk can mark it: its `held.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:42:10: `StepGen` is converted to `Sink` here
```

<!-- test: error.freeze-names-the-path-through-a-nested-conformer-to-the-function-value -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`W` holds a `Stepper` in a field, so the function value is `s.step`: the reason names the path to the part no walk can mark, not the field that holds a record.

```maxon
typealias Tally = int(0 to u64.max)
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

type Plain implements Shape
	static function create() returns Self
		return Self{}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type W implements Shape
	let s as Stepper

	static function create(s Stepper) returns Self
		return Self{s: s}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'W'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let d = spawn Door.create()
	d.shutdown()
	let f = freeze Holder.create(W.create(Stepper.create(function(n Tally) gives n + 1)))
	print("{f.shape.area()} {Plain.create().area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:66:10: `Holder` cannot be frozen: its `shape` is a `Shape`, and `W` stands at `Shape` but no share walk can mark it: its `s.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:66:31: `W` is converted to `Shape` here
```

<!-- test: clone-in-a-generic-body-of-a-self-referential-record-compiles-and-runs -->
`Node` holds a `Node with T` in a field, so its instance reaches itself through its own fields; cloning one inside `Run`'s generic body walks each record the clone copies once and compiles.

```maxon
typealias Tally = int(0 to u64.max)

type Node uses T
	typealias TNode = Node with T
	export let item as T
	export let next as TNode

	static function create(item T, next TNode) returns Self
		return Self{item: item, next: next}
	end 'create'
end 'Node'

type Run uses T
	typealias TNode = Node with T

	static function copy(n TNode) returns TNode
		return n.clone()
	end 'copy'

	static function marker() returns Tally
		return 7
	end 'marker'
end 'Run'

typealias TextRun = Run with String

function main() returns ExitCode
	print("{TextRun.marker()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: clone-in-a-generic-body-of-a-record-that-holds-itself-through-an-array-runs -->
`Node` holds its children as an array of `Node with T`; `Run.copy` clones a tree in a generic body and the program frees both trees.

```maxon
type Node uses T
	typealias TNode = Node with T
	typealias TNodes = Array with TNode
	export let item as T
	export let kids as TNodes

	static function create(item T, kids TNodes) returns Self
		return Self{item: item, kids: kids}
	end 'create'
end 'Node'

type Run uses T
	typealias TNode = Node with T
	typealias TNodes = Array with TNode

	static function leaf(item T) returns TNode
		return TNode.create(item, kids: TNodes.create(0))
	end 'leaf'

	static function tree(root T, child T) returns TNode
		let kids = TNodes.create(0)
		kids.push(TNode.create(child, kids: TNodes.create(0)))
		return TNode.create(root, kids: kids)
	end 'tree'

	static function copy(n TNode) returns TNode
		return n.clone()
	end 'copy'
end 'Run'

typealias TextRun = Run with String

function main() returns ExitCode
	let original = TextRun.tree("a root string that lives on the heap", child: "a child string that lives on the heap too")
	let copied = TextRun.copy(original)
	print("{copied.item} {copied.kids.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a root string that lives on the heap 1
```


<!-- test: error.freeze-names-an-arrays-element-slot-through-the-array-field -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

A conformer's `Array with Stepper` field is a library container, so the refusal names the field that holds it and what it holds — the elements' own `step` — and not the container's private storage.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

typealias StepperArray = Array with Stepper

type ArrShape implements Shape
	export let all as StepperArray

	static function create(all StepperArray) returns Self
		return Self{all: all}
	end 'create'

	function area() returns Tally
		return 3
	end 'area'
end 'ArrShape'

function pick(which Tally) returns Shape
	_ = which
	let all = StepperArray.create(0)
	return ArrShape.create(all)
end 'pick'

function main() returns ExitCode
	spawnDoor()
	let a = Holder.create(pick(2))
	let b = Holder.create(pick(1))
	let c = freeze Holder.create(pick(2))
	print("{a.shape.area()} {b.shape.area()} {c.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:91:10: `Holder` cannot be frozen: its `shape` is a `Shape`, and `ArrShape` stands at `Shape` but no share walk can mark it: its `all` holds `Stepper` elements, whose `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:84:9: `ArrShape` is converted to `Shape` here
```

<!-- test: error.freeze-names-a-maps-values-through-the-map-field -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

A conformer's `Map with (String, Stepper)` field is named by the field and by what it holds: the `Stepper` values, whose `step` is the function value. The map's private `values` storage is not part of the path.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

typealias StepperMap = Map with (String, Stepper)

type MapShape implements Shape
	export let byName as StepperMap

	static function create(byName StepperMap) returns Self
		return Self{byName: byName}
	end 'create'

	function area() returns Tally
		return 4
	end 'area'
end 'MapShape'

function pick() returns Shape
	return MapShape.create(StepperMap.create())
end 'pick'

function main() returns ExitCode
	spawnDoor()
	let held = freeze Holder.create(pick())
	print("{held.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:87:13: `Holder` cannot be frozen: its `shape` is a `Shape`, and `MapShape` stands at `Shape` but no share walk can mark it: its `byName` holds `Stepper` values, whose `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:82:9: `MapShape` is converted to `Shape` here
```

<!-- test: error.freeze-names-a-lists-elements-through-the-list-field -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

A `List with Stepper` field reads as an `Array` and a `Map` do: the field holds `Stepper` elements, whose `step` is the function value, and the list's private chain is not part of the path.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

typealias StepperList = List with Stepper

type ListShape implements Shape
	export let steps as StepperList

	static function create(steps StepperList) returns Self
		return Self{steps: steps}
	end 'create'

	function area() returns Tally
		return 5
	end 'area'
end 'ListShape'

function pick() returns Shape
	return ListShape.create(StepperList.create())
end 'pick'

function main() returns ExitCode
	spawnDoor()
	let held = freeze Holder.create(pick())
	print("{held.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:87:13: `Holder` cannot be frozen: its `shape` is a `Shape`, and `ListShape` stands at `Shape` but no share walk can mark it: its `steps` holds `Stepper` elements, whose `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:82:9: `ListShape` is converted to `Shape` here
```

<!-- test: error.freeze-names-a-tuple-element-as-an-author-writes-it -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

A tuple element is named by its position as an author writes it, `pair.0`, and not by the field name the compiler gives the tuple's storage.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

typealias StepperPair = (Stepper, Plain)

type PairShape implements Shape
	export let pair as StepperPair

	static function create(pair StepperPair) returns Self
		return Self{pair: pair}
	end 'create'

	function area() returns Tally
		return 6
	end 'area'
end 'PairShape'

function pick() returns Shape
	return PairShape.create((stepper(), Plain.create()))
end 'pick'

function main() returns ExitCode
	spawnDoor()
	let held = freeze Holder.create(pick())
	print("{held.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:87:13: `Holder` cannot be frozen: its `shape` is a `Shape`, and `PairShape` stands at `Shape` but no share walk can mark it: its `pair.0.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:82:9: `PairShape` is converted to `Shape` here
```

<!-- test: a-generic-permuting-its-parameters-at-an-entry-naming-one-of-them-runs -->
`Swap` hands its arguments to itself in the other order, so it denotes only two instances at any entry; sharing is judged in finite time and the `freeze` runs.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Plain.create().n + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Swap uses X, Y
	typealias Flipped = Swap with (Y, X)
	typealias WX = Wrap with X

	static function go(x X, y Y, depth Tally) returns Holder
		if depth == 0 'done'
			return Holder.create(WX.create(x))
		end 'done'

		return Flipped.go(y, y: x, depth: depth - 1)
	end 'go'
end 'Swap'

typealias Entry = Swap with (Plain, Cell with Plain)

function main() returns ExitCode
	spawnDoor()
	let f = freeze Entry.go(Plain.create(), y: Cell.create(Plain.create()), depth: 3)
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-generic-duplicating-a-parameter-at-an-entry-runs -->
`Dup` passes its second argument in both positions; the instances it denotes stay finite, so the `freeze` runs.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Dup uses X, Y
	typealias Same = Dup with (Y, Y)
	typealias WX = Wrap with X

	static function go(x X, y Y, depth Tally) returns Holder
		if depth == 0 'done'
			return Holder.create(WX.create(x))
		end 'done'

		return Same.go(y, y: y, depth: depth - 1)
	end 'go'
end 'Dup'

typealias Entry = Dup with (Plain, Cell with Plain)

function main() returns ExitCode
	spawnDoor()
	let f = freeze Entry.go(Plain.create(), y: Cell.create(Plain.create()), depth: 3)
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-generic-fixing-one-parameter-and-wrapping-the-other-runs -->
`Fix` keeps its first argument and wraps its second in a `Cell` once; the wrapped argument feeds no parameter the first one depends on, so the instances it denotes are finite and the `freeze` runs.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

function stepper() returns Stepper
	return Stepper.create(function(n Tally) gives n + 1)
end 'stepper'

function spawnDoor()
	let h = spawn Door.create()
	try h.look(Holder.create(Wrap.create(Plain.create())).shape.area() + stepper().step(1)) otherwise panic("look")
	h.shutdown()
end 'spawnDoor'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Fix uses X, Y
	typealias Settled = Fix with (X, Cell with X)
	typealias CX = Cell with X
	typealias WY = Wrap with Y

	static function go(x X, y Y, depth Tally) returns Holder
		if depth == 0 'done'
			return Holder.create(WY.create(y))
		end 'done'

		return Settled.go(x, y: CX.create(x), depth: depth - 1)
	end 'go'
end 'Fix'

typealias Entry = Fix with (Plain, Plain)

function main() returns ExitCode
	spawnDoor()
	let f = freeze Entry.go(Plain.create(), y: Plain.create(), depth: 3)
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-string-conformer-widened-to-a-share-reached-interface-runs -->
A `String` stands at `Shape` through an extension, and the interface is share-reached; the widening is judged like any conformer's and the program runs.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

extension String implements Shape
	function area() returns Tally
		return 3
	end 'area'
end 'String'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	let held = freeze Holder.create("a string long enough to be heap allocated, not inline")
	print("{held.shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: descriptor-chain-over-a-parameter-nothing-reads-runs -->
`Tagged` recurses through `Deeper`, which nests `Cell` around its second parameter, and widens `Wrap with T` to `Shape` through its descriptor; nothing reads that second parameter, so the descriptors the recursion needs are one chain and not an unbounded one.

```maxon
typealias Tally = int(0 to u64.max)

interface Shape
	function area() returns Tally
end 'Shape'

type Plain implements Shape
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Wrap uses T implements Shape
	export let held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function area() returns Tally
		return 7
	end 'area'
end 'Wrap'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Tagged uses T, U
	typealias Deeper = Tagged with (T, Cell with U)
	typealias WT = Wrap with T

	static function go(x T, depth Tally) returns Holder
		if depth == 0 'done'
			return Holder.create(WT.create(x))
		end 'done'

		return Deeper.go(x, depth: depth - 1)
	end 'go'
end 'Tagged'

typealias Entry = Tagged with (Plain, Tally)

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function main() returns ExitCode
	let h = spawn Door.create()
	let f = freeze Holder.create(Plain.create())
	let n = f.shape.area() + Entry.go(Plain.create(), depth: 3).shape.area()
	try h.look(n) otherwise panic("look")
	h.shutdown()
	print("{n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: freeze-door-in-a-generic-body-naming-an-instance-over-an-unread-parameter-runs -->
`Froze` names `Froze with (Phantom with T)`, which nests its own parameter under a type that holds nothing, so the owner instances a freeze door is judged at end after one level.

```maxon
typealias Tally = int(0 to u64.max)

type Plain
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Plain'

type Cell uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Phantom uses U
	export let n as Tally

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Phantom'

type Froze uses T
	typealias TCell = Cell with T
	typealias Wider = Froze with (Phantom with T)

	static function run(x T) returns Tally
		let f = freeze TCell.create(x).clone()
		_ = f
		return 1 + Wider.touch()
	end 'run'

	static function touch() returns Tally
		return 5
	end 'touch'
end 'Froze'

typealias PlainFroze = Froze with Plain

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function main() returns ExitCode
	let door = spawn Door.create()
	door.shutdown()
	print("{PlainFroze.run(Plain.create())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

<!-- test: error.freeze-through-interfaces-names-tuple-and-instance-conformers -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

A conformer holding a tuple, an instance and nested arrays of tuples is named in the spelling the program wrote, each interface with its own article.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface I1
	function a1() returns Tally
end 'I1'

interface I2
	function a2() returns Tally
end 'I2'

interface I3
	function a3() returns Tally
end 'I3'

type Stepper
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'
end 'Stepper'

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias StepperBox = Box with Stepper
typealias Pair = (Tally, Stepper)
typealias Pairs = Array with Pair
typealias PairsArray = Array with Pairs

type TupBag implements I1
	export let p as Pair

	static function create() returns Self
		return Self{p: (1, Stepper.create(function(n Tally) gives n))}
	end 'create'

	function a1() returns Tally
		return self.p.0
	end 'a1'
end 'TupBag'

type BoxBag implements I2
	export let b as StepperBox

	static function create() returns Self
		return Self{b: StepperBox.create(Stepper.create(function(n Tally) gives n))}
	end 'create'

	function a2() returns Tally
		return 2
	end 'a2'
end 'BoxBag'

type DeepBag implements I3
	export let d as PairsArray

	static function create() returns Self
		return Self{d: PairsArray.create(0)}
	end 'create'

	function a3() returns Tally
		return self.d.count() as Tally
	end 'a3'
end 'DeepBag'

type H1
	export let s as I1

	static function create(s I1) returns Self
		return Self{s: s}
	end 'create'
end 'H1'

type H2
	export let s as I2

	static function create(s I2) returns Self
		return Self{s: s}
	end 'create'
end 'H2'

type H3
	export let s as I3

	static function create(s I3) returns Self
		return Self{s: s}
	end 'create'
end 'H3'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function main() returns ExitCode
	let d = spawn Door.create()
	d.shutdown()
	let f1 = freeze H1.create(TupBag.create())
	let f2 = freeze H2.create(BoxBag.create())
	let f3 = freeze H3.create(DeepBag.create())
	print("{f1.s.a1()} {f2.s.a2()} {f3.s.a3()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:113:11: `H1` cannot be frozen: its `s` is an `I1`, and `TupBag` stands at `I1` but no share walk can mark it: its `p.1.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:113:28: `TupBag` is converted to `I1` here
error E3171: <fragment>:114:11: `H2` cannot be frozen: its `s` is an `I2`, and `BoxBag` stands at `I2` but no share walk can mark it: its `b.item.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:114:28: `BoxBag` is converted to `I2` here
error E3171: <fragment>:115:11: `H3` cannot be frozen: its `s` is an `I3`, and `DeepBag` stands at `I3` but no share walk can mark it: its `d` holds `(Tally, Stepper)` elements, whose `1.step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:115:28: `DeepBag` is converted to `I3` here
```

<!-- test: error.a-conversion-note-points-at-the-arm-that-returned-the-conformer -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi answers E3104 for the `spawn` before any share door is judged, and the door is judged for this cause only in a program that spawns a service.

`pick` returns `plain` from one arm and `stepper` from the other, so the note naming where `Stepper` becomes a `Shape` points at the second arm.

```maxon
typealias Tally = int(0 to u64.max)
typealias Step = function(Tally) returns Tally

interface Shape
	function area() returns Tally
end 'Shape'

type Plain implements Shape
	export let n as Tally

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function area() returns Tally
		return 1
	end 'area'
end 'Plain'

type Stepper implements Shape
	export let step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	function area() returns Tally
		return 2
	end 'area'
end 'Stepper'

enum Which
	first
	second
end 'Which'

type Holder
	export let shape as Shape

	static function create(shape Shape) returns Self
		return Self{shape: shape}
	end 'create'
end 'Holder'

type Door
	var seen as Tally

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function look(n Tally)
		self.seen = self.seen + n
	end 'look'
end 'Door'

function pick(plain Plain, stepper Stepper, which Which) returns Shape
	return match which 'pickOne'
		first gives plain
		second gives stepper
	end 'pickOne'
end 'pick'

function main() returns ExitCode
	let d = spawn Door.create()
	d.shutdown()
	let f = freeze Holder.create(pick(Plain.create(), stepper: Stepper.create(function(n Tally) gives n), which: Which.second))
	print("{f.shape.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3171: <fragment>:68:10: `Holder` cannot be frozen: its `shape` is a `Shape`, and `Stepper` stands at `Shape` but no share walk can mark it: its `step` is a function value, a closure record that owns what it captures and does not cross to another thread — a shared value is read by every thread at once, so a share walk must be able to mark everything it reaches. Hold that part in a service, or use a separate interface
note: <fragment>:61:16: `Stepper` is converted to `Shape` here
```
