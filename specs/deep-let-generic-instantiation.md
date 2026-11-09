---
feature: deep-let-generic-instantiation
status: experimental
keywords: [generic, instantiation, let, immutable, Set, type parameter]
category: type-system
---

# Immutable places and a generic body's type parameter

## Documentation

A generic body is compiled once against its opaque type parameter, so the rules cannot see whether a value of
that type may be written. They are decided where the program instantiates the body. A `let` field keeps any value
of its type parameter, and a `Set`'s elements are `immutable` slots, so a body that inserts a value of its type
parameter into a `Set` is refused at the call that instantiates it unless the argument is data nothing writes: a
number, a `String`, a record whose fields are all `let` values, or frozen data. A call through an interface has no
type argument to judge, so the instance is judged where it becomes an interface value: as an argument, a return,
a field store or a message payload.

## Tests

<!-- test: a-set-of-strings-is-built-from-a-literal -->
A `Set` instantiates bodies that keep a value of its element type in an `immutable` slot; a `String` element
satisfies them all.

```maxon
typealias NameSet = Set with String

function main() returns ExitCode
	let names = NameSet from ["a", "b", "a"]

	if names.contains("a") 'has'
		return names.count() as ExitCode
	end 'has'

	return 0
end 'main'
```
```exitcode
2
```

<!-- test: a-generic-body-keeping-its-type-parameter-takes-a-value-type -->
A user type whose method stores its elements into a `Set` is judged where it is instantiated: with `String`
it is accepted.

```maxon
typealias StringArray = Array with String
typealias StringRegistry = Registry with String

type Registry uses Key where Key is Hashable and Equatable
	typealias KeySet = Set with Key
	typealias KeyArray = Array with Key

	var seen as KeySet

	static function create() returns Self
		return Self{seen: KeySet.create()}
	end 'create'

	function remember(keys KeyArray)
		for key in keys 'eachKey'
			self.seen.insert(key)
		end 'eachKey'
	end 'remember'

	function size() returns Count
		return self.seen.count()
	end 'size'
end 'Registry'

function main() returns ExitCode
	var names = StringArray.create(0)
	names.push("a")
	names.push("b")
	var registry = StringRegistry.create()
	registry.remember(names)
	return registry.size() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-generic-body-keeping-its-type-parameter-refuses-a-writable-record -->
The same type instantiated with a record that has a `var` field is refused at the call that instantiates the body, naming `Registry.remember`: a `Set` keeps `immutable` elements, and `counters` is mutable.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterArray = Array with Counter
typealias CounterRegistry = Registry with Counter

type Counter implements Hashable, Equatable
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function hash() returns HashValue
		return self.n as HashValue
	end 'hash'

	function equals(other Counter) returns bool
		return self.n == other.n
	end 'equals'
end 'Counter'

type Registry uses Key where Key is Hashable and Equatable
	typealias KeySet = Set with Key
	typealias KeyArray = Array with Key

	var seen as KeySet

	static function create() returns Self
		return Self{seen: KeySet.create()}
	end 'create'

	function remember(keys KeyArray)
		for key in keys 'eachKey'
			self.seen.insert(key)
		end 'eachKey'
	end 'remember'

	function size() returns Count
		return self.seen.count()
	end 'size'
end 'Registry'

function main() returns ExitCode
	var counters = CounterArray.create(0)
	counters.push(Counter.create(1))
	var registry = CounterRegistry.create()
	registry.remember(counters)
	return registry.size() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:47:11: cannot call 'Registry.remember' with a type whose values something may write: its body keeps a value of its type parameter, and a kept value must have no other writer, which only a type nothing writes guarantees (a number, a String, or a record whose fields are all `let`); use such a type
error E3207: <fragment>:47:20: 'remember' needs 'keys' frozen (it calls 'insert', which takes `immutable` 'element'); 'counters' is mutable. Freeze it first
```

<!-- test: error.a-set-of-writable-records-is-refused-for-an-element-another-array-still-holds -->
A record read out of an array the program still holds is mutable, so it is not handed to a `Set`, whose elements are `immutable`; the refusal is at the `insert` in the program's own loop.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterArray = Array with Counter
typealias CounterSet = Set with Counter

type Counter implements Hashable, Equatable
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function hash() returns HashValue
		return self.n as HashValue
	end 'hash'

	function equals(other Counter) returns bool
		return self.n == other.n
	end 'equals'
end 'Counter'

function main() returns ExitCode
	var counters = CounterArray.create(0)
	counters.push(Counter.create(1))
	var members = CounterSet.create()

	for counter in counters 'eachCounter'
		members.insert(counter)
	end 'eachCounter'

	return members.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:28:18: 'insert' takes `immutable` 'element'; 'counter' is mutable. Freeze it first
```

<!-- test: a-set-of-writable-records-takes-frozen-members -->
The twin: members frozen where they are inserted are the only reference to their records.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterSet = Set with Counter

type Counter implements Hashable, Equatable
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function hash() returns HashValue
		return self.n as HashValue
	end 'hash'

	function equals(other Counter) returns bool
		return self.n == other.n
	end 'equals'
end 'Counter'

function main() returns ExitCode
	var members = CounterSet.create()
	members.insert(freeze Counter.create(1))
	members.insert(freeze Counter.create(2))

	return members.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-generic-create-keeps-its-type-parameter-argument-in-a-let-field -->
A generic type whose `create` stores its type-parameter argument in a `let` field is judged where it is
instantiated: a `String` is a value, so the box takes it.

```maxon
typealias StringBox = Box with String

type Box uses Item
	let kept as Item

	static function create(item Item) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	let box = StringBox.create("a")

	return box.peek().byteLength() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-generic-create-keeps-an-argument-another-name-still-holds -->
The same `create` instantiated with a record that has a `var` field takes the argument at the user's call, though the array still holds the element: the box keeps it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterBox = Box with Counter
typealias CounterArray = Array with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box uses Item
	let kept as Item

	static function create(item Item) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counters = CounterArray.create(0)
	counters.push(Counter.create(1))
	var box = CounterBox.create(try counters.get(0) otherwise panic("empty"))

	return box.peek().n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-generic-store-of-a-value-read-out-of-an-array-is-judged-at-the-call -->
A static function that builds the box from an element it reads out of an array is judged at the call that
instantiates it, like a `create` that takes the value as an argument: a `String` element is accepted.

```maxon
typealias StringBox = Box with String
typealias StringArray = Array with String

type Box uses Item
	typealias ItemArray = Array with Item

	let kept as Item

	static function first(items ItemArray) returns Self
		return Self{kept: try items.get(0) otherwise panic("empty")}
	end 'first'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	var items = StringArray.create(0)
	items.push("abc")
	let box = StringBox.first(items)

	return box.peek().byteLength() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-generic-store-of-a-value-read-out-of-an-array-takes-a-writable-record -->
The same `first` instantiated with a record that has a `var` field is accepted at the call that instantiates it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterBox = Box with Counter
typealias CounterArray = Array with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box uses Item
	typealias ItemArray = Array with Item

	let kept as Item

	static function first(items ItemArray) returns Self
		return Self{kept: try items.get(0) otherwise panic("empty")}
	end 'first'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	var items = CounterArray.create(0)
	items.push(Counter.create(1))
	var box = CounterBox.first(items)

	return box.peek().n as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-type-parameter-value-read-out-of-a-let-field-may-be-stored-in-a-var-field-of-a-value-type -->
A value of the type parameter read out of a `let` field and stored into a `var` field is judged where the
method is instantiated, and a `String` satisfies it.

```maxon
typealias StringPair = Pair with String

type Pair uses Item
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror(extra Item) returns Item
		self.second = self.first

		return extra
	end 'mirror'
end 'Pair'

function main() returns ExitCode
	var pair = StringPair.create("a", two: "b")
	let extra = pair.mirror("c")

	return extra.byteLength() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-type-parameter-value-read-out-of-a-let-field-is-stored-in-a-var-field-of-a-writable-record -->
The same method instantiated with a record that has a `var` field is accepted at the call: both fields then name one record.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterPair = Pair with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Pair uses Item
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror(extra Item) returns Item
		self.second = self.first

		return extra
	end 'mirror'
end 'Pair'

function main() returns ExitCode
	var pair = CounterPair.create(Counter.create(1), two: Counter.create(2))
	let extra = pair.mirror(Counter.create(3))

	return extra.n as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-method-with-only-its-receiver-is-judged-by-the-receivers-type-argument -->
A method with no parameter of its own is judged by the type argument of the receiver it is called on.

```maxon
typealias StringPair = Pair with String

type Pair uses Item
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'
end 'Pair'

function main() returns ExitCode
	var pair = StringPair.create("a", two: "b")
	pair.mirror()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-method-with-only-its-receiver-takes-a-receiver-of-writable-records -->
The same method called on a pair of records that have a `var` field is accepted at the call.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterPair = Pair with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Pair uses Item
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'
end 'Pair'

function main() returns ExitCode
	var pair = CounterPair.create(Counter.create(1), two: Counter.create(2))
	pair.mirror()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-generic-body-reached-through-an-interface-takes-a-value-type -->
A call through an interface hands the body an erased receiver, so the instantiation is judged where the instance
becomes an interface value: at that conversion the type argument is known. `Pair with String` keeps a `String`,
a value type, in a place something may write, so the conversion is accepted and the call through the interface
runs.

```maxon
typealias StringPair = Pair with String

interface Mirrored
	function mirror(mutable self)
	function agrees() returns bool
end 'Mirrored'

type Pair uses Item implements Mirrored where Item is Equatable
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'

	function agrees() returns bool
		return self.first.equals(self.second)
	end 'agrees'
end 'Pair'

type Holder
	var held as Mirrored

	static function create(held Mirrored) returns Holder
		return Holder{held: held}
	end 'create'

	function run()
		self.held.mirror()
	end 'run'

	function agrees() returns bool
		return self.held.agrees()
	end 'agrees'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create(StringPair.create("a", two: "b"))
	print("{holder.agrees()}\n")
	holder.run()
	print("{holder.agrees()}\n")

	return 0
end 'main'
```
```stdout
false
true
```
```exitcode
0
```

<!-- test: a-generic-body-reached-through-an-interface-takes-a-writable-record-at-the-conversion -->
The same body instantiated with a record that has a `var` field is accepted where the instance becomes a `Mirrored`, and every call through the interface reaches it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterPair = Pair with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

interface Mirrored
	function mirror(mutable self)
end 'Mirrored'

type Pair uses Item implements Mirrored
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'
end 'Pair'

type Holder
	var held as Mirrored

	static function create(held Mirrored) returns Holder
		return Holder{held: held}
	end 'create'

	function run()
		self.held.mirror()
	end 'run'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create(CounterPair.create(Counter.create(1), two: Counter.create(2)))
	holder.run()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-generic-body-returned-at-an-interface-type-is-accepted-at-the-conversion -->
A return at an interface type is a conversion too: the returned pair of records that have a `var` field becomes a `Mirrored` at the `return`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterPair = Pair with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

interface Mirrored
	function mirror(mutable self)
end 'Mirrored'

type Pair uses Item implements Mirrored
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'
end 'Pair'

function make() returns Mirrored
	return CounterPair.create(Counter.create(1), two: Counter.create(2))
end 'make'

function main() returns ExitCode
	let made = make()
	made.mirror()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-generic-body-stored-in-an-interface-field-is-accepted-at-the-conversion -->
A store into a field declared at an interface type is a conversion as well: the pair becomes a `Mirrored` where `Holder` is built.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterPair = Pair with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

interface Mirrored
	function mirror(mutable self)
end 'Mirrored'

type Pair uses Item implements Mirrored
	let first as Item
	var second as Item

	static function create(one Item, two Item) returns Self
		return Self{first: one, second: two}
	end 'create'

	function mirror()
		self.second = self.first
	end 'mirror'
end 'Pair'

type Holder
	export var held as Mirrored

	static function create() returns Holder
		return Holder{held: CounterPair.create(Counter.create(5), two: Counter.create(6))}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()
	holder.held.mirror()

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-var-at-its-last-use-moves-into-a-static-factorys-let-field -->
A `var` handed to a static factory that keeps it in a `let` field is accepted at its last use: the call moves it
into the box, which is then the only name that reaches the record.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box
	let kept as Counter

	static function create(item Counter) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counter = Counter.create(1)
	counter.n = 3
	let box = Box.create(counter)

	return box.peek() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-var-at-its-last-use-moves-into-a-generic-factorys-let-field -->
The generic spelling of the same keeper, reached through an alias of the instance, takes a `var` at its last use.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterBox = Box with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box uses Item
	let kept as Item

	static function create(item Item) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counter = Counter.create(1)
	counter.n = 4
	let box = CounterBox.create(counter)

	return box.peek().n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-var-read-after-a-static-factory-keeps-it-is-accepted -->
The `var` is read again after the call, so the factory's `let` field shares the record with a name that still reads it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box
	let kept as Counter

	static function create(item Counter) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counter = Counter.create(1)
	let box = Box.create(counter)
	let seen = counter.n

	return (box.peek() + seen) as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-var-written-after-a-static-factory-keeps-it-is-accepted -->
A write through the `var` after the call reaches the record the factory's `let` field holds, and `peek` reads the write.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box
	let kept as Counter

	static function create(item Counter) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counter = Counter.create(1)
	let box = Box.create(counter)
	counter.n = 2

	return box.peek() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-var-written-after-a-generic-factory-keeps-it-is-accepted -->
The generic spelling accepts the same write, and `peek` reads it.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterBox = Box with Counter

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box uses Item
	let kept as Item

	static function create(item Item) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Item
		return self.kept
	end 'peek'
end 'Box'

function main() returns ExitCode
	var counter = Counter.create(1)
	let box = CounterBox.create(counter)
	counter.n = 2

	return box.peek().n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-let-view-of-a-vars-field-is-kept-by-a-let-field-keeper -->
A `view` of the record a `var`'s field holds is kept by a `let` field, and the write to the `var`'s field is read through it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Holder
	export var field as Counter

	static function create(n Integer) returns Self
		return Self{field: Counter.create(n)}
	end 'create'
end 'Holder'

type Box
	let kept as Counter

	static function create(item Counter) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	var holder = Holder.create(1)
	view view = holder.field
	let box = Box.create(view)
	let seen = view.n
	holder.field.n = 2

	return (box.peek() + seen) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-let-moved-from-a-var-is-sole-so-a-let-field-keeps-it -->
`let b = a` moves the `var` `a` into `b`, which is then the record's only name, so a `let` field may keep it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box
	let kept as Counter

	static function create(item Counter) returns Self
		return Self{kept: item}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	var a = Counter.create(1)
	a.n = 2
	let b = a
	let box = Box.create(b)
	let seen = b.n

	return (box.peek() + seen) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-let-view-of-a-vars-field-is-kept-by-a-factorys-struct-literal -->
`x` views the record `holder.field` holds. The struct literal's `let` field keeps it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Holder
	export var field as Counter

	static function create(n Integer) returns Self
		return Self{field: Counter.create(n)}
	end 'create'
end 'Holder'

type Box
	let kept as Counter

	static function create(n Integer) returns Self
		var holder = Holder.create(n)
		holder.field.n = n + 1
		view x = holder.field
		return Self{kept: x}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	let box = Box.create(1)

	return box.peek() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: a-let-bound-to-a-fresh-record-is-kept-by-a-factorys-struct-literal -->
A `let` bound to a freshly built record is its only name, so the struct literal's `let` field keeps it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "x"}
	end 'create'
end 'Counter'

type Box
	let kept as Counter

	static function create(n Integer) returns Self
		let x = Counter.create(n)
		return Self{kept: x}
	end 'create'

	function peek() returns Integer
		return self.kept.n
	end 'peek'
end 'Box'

function main() returns ExitCode
	let box = Box.create(3)

	return box.peek() as ExitCode
end 'main'
```
```exitcode
3
```
