---
feature: generic-instance-existential-ownership
status: stable
keywords: [generics, interface, existential, witness, ownership, leak, dictionary-passing]
category: memory-safety
---

# A Generic Instance Widened To An Interface Releases What It Holds

## Documentation

An instance of a generic type (`Gen with String`) that conforms to an interface and is widened to it, at a
`return`, a call argument or a store into an interface-typed field, is released through the witness table
the widening names. The type argument's ownership decides how that release goes: `Gen with Tally` owns one
box and `Gen with String` owns a box and a string, so one table keyed by the generic's name cannot release
both.

A widening of a concrete instance names that instance's own table whenever the instance releases or retains
differently from the base type. A widening written inside a generic body, over the enclosing type's own
type parameter (`Gen with T` inside `type Wrap uses T`), reads its table out of the layout descriptor the
enclosing instantiation hands down, so `Wrap with String` releases a `Gen with String` and `Wrap with
Tally` a `Gen with Tally`.

## Tests

<!-- test: string-argument-widened-at-a-return -->
A `Gen with String` returned as a `Sink` releases the string it holds when the interface value drops.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias StringGen = Gen with String

function widen(label String) returns Sink
	return StringGen.create(label)
end 'widen'

function main() returns ExitCode
	let sink = widen("alpha beta gamma delta epsilon zeta")
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: record-argument-widened-at-a-return -->
A `Gen with Pad`, `Pad` a record that owns a string, returned as a `Sink` releases the record.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias PadGen = Gen with Pad

function widen(label String) returns Sink
	return PadGen.create(Pad.create(label))
end 'widen'

function main() returns ExitCode
	let sink = widen("alpha beta gamma delta epsilon zeta")
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: array-argument-widened-at-a-return -->
A `Gen with NameArray` returned as a `Sink` releases the array and the strings in it.
```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias NamesGen = Gen with NameArray

function widen(label String) returns Sink
	let names = NameArray.create(0)
	names.push(label)
	return NamesGen.create(names)
end 'widen'

function main() returns ExitCode
	let sink = widen("alpha beta gamma delta epsilon zeta")
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: string-argument-widened-at-an-argument -->
A `Gen with String` passed to an interface-typed parameter that the callee keeps in a field releases the string when that record drops.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias StringGen = Gen with String

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create(StringGen.create("alpha beta gamma delta epsilon zeta"))
	print("{shelf.poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: record-argument-widened-at-an-argument -->
A `Gen with Pad` passed to an interface-typed parameter that the callee keeps releases the record.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias PadGen = Gen with Pad

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create(PadGen.create(Pad.create("alpha beta gamma delta epsilon zeta")))
	print("{shelf.poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: array-argument-widened-at-an-argument -->
A `Gen with NameArray` passed to an interface-typed parameter that the callee keeps releases the array and its strings.
```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias NamesGen = Gen with NameArray

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

function main() returns ExitCode
	let names = NameArray.create(0)
	names.push("alpha beta gamma delta epsilon zeta")
	let shelf = Shelf.create(NamesGen.create(names))
	print("{shelf.poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: string-argument-widened-at-a-field-store -->
A `Gen with String` stored into an interface-typed field is released with the record that holds the field.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

typealias StringGen = Gen with String

type Shelf
	var sink as Sink

	static function create(label String) returns Self
		return Self{sink: StringGen.create(label)}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

function main() returns ExitCode
	let shelf = Shelf.create("alpha beta gamma delta epsilon zeta")
	print("{shelf.poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-a-string-instance-at-a-return -->
`Wrap with String` builds a `Gen with T` at its own type parameter and returns it as a `Sink`: the table comes out of the descriptor `Wrap with String` carries, and it releases the string.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function asSink() returns Sink
		return Gen.create(self.seed)
	end 'asSink'
end 'Wrap'

typealias StringWrap = Wrap with String

function main() returns ExitCode
	let wrapped = StringWrap.create("alpha beta gamma delta epsilon zeta")
	let sink = wrapped.asSink()
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-a-record-instance-at-a-return -->
The same widening over a record type argument releases the record.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function asSink() returns Sink
		return Gen.create(self.seed)
	end 'asSink'
end 'Wrap'

typealias PadWrap = Wrap with Pad

function main() returns ExitCode
	let wrapped = PadWrap.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	let sink = wrapped.asSink()
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-an-array-instance-at-a-return -->
The same widening over an array type argument releases the array and its strings.
```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function asSink() returns Sink
		return Gen.create(self.seed)
	end 'asSink'
end 'Wrap'

typealias NamesWrap = Wrap with NameArray

function main() returns ExitCode
	let names = NameArray.create(0)
	names.push("alpha beta gamma delta epsilon zeta")
	let wrapped = NamesWrap.create(names)
	let sink = wrapped.asSink()
	sink.take(2)
	print("{sink.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-a-string-instance-at-an-argument -->
`Wrap with String` hands a `Gen with T` to an interface-typed parameter the callee keeps.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function feed() returns Tally
		let shelf = Shelf.create(Gen.create(self.seed))
		return shelf.poke()
	end 'feed'
end 'Wrap'

typealias StringWrap = Wrap with String

function main() returns ExitCode
	let wrapped = StringWrap.create("alpha beta gamma delta epsilon zeta")
	print("{wrapped.feed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-a-record-instance-at-an-argument -->
The same argument widening over a record type argument.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function feed() returns Tally
		let shelf = Shelf.create(Gen.create(self.seed))
		return shelf.poke()
	end 'feed'
end 'Wrap'

typealias PadWrap = Wrap with Pad

function main() returns ExitCode
	let wrapped = PadWrap.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{wrapped.feed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-an-array-instance-at-an-argument -->
The same argument widening over an array type argument.
```maxon
typealias Tally = int(0 to u64.max)
typealias NameArray = Array with String

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Shelf
	var sink as Sink

	static function create(sink Sink) returns Self
		return Self{sink: sink}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Shelf'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function feed() returns Tally
		let shelf = Shelf.create(Gen.create(self.seed))
		return shelf.poke()
	end 'feed'
end 'Wrap'

typealias NamesWrap = Wrap with NameArray

function main() returns ExitCode
	let names = NameArray.create(0)
	names.push("alpha beta gamma delta epsilon zeta")
	let wrapped = NamesWrap.create(names)
	print("{wrapped.feed()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widens-a-string-instance-at-a-field-store -->
`Slot with String` stores a `Gen with T` into an interface-typed field of its own record.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Slot uses T
	var sink as Sink

	static function create(seed T) returns Self
		return Self{sink: Gen.create(seed)}
	end 'create'

	function poke() returns Tally
		self.sink.take(2)
		return self.sink.total()
	end 'poke'
end 'Slot'

typealias StringSlot = Slot with String

function main() returns ExitCode
	let slot = StringSlot.create("alpha beta gamma delta epsilon zeta")
	print("{slot.poke()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: generic-body-widening-serves-a-string-and-a-tally-instantiation-of-one-type -->
One `Wrap` instantiated at `String` and at `Tally` widens at a return for both: the `Tally` instance's table is the shared one, the `String` instance's table its own, and each descriptor hands its own to the one compiled `asSink`.
```maxon
typealias Tally = int(0 to u64.max)

interface Sink
	function take(mutable self, by Tally)
	function total() returns Tally
end 'Sink'

type Gen uses T implements Sink
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function take(mutable self, by Tally)
		self.seen = self.seen + by
	end 'take'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses T
	var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function asSink() returns Sink
		return Gen.create(self.seed)
	end 'asSink'
end 'Wrap'

typealias StringWrap = Wrap with String
typealias TallyWrap = Wrap with Tally

function main() returns ExitCode
	let named = StringWrap.create("alpha beta gamma delta epsilon zeta")
	let counted = TallyWrap.create(7)
	let first = named.asSink()
	let second = counted.asSink()
	first.take(2)
	second.take(3)
	print("{first.total()} {second.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 3
```
