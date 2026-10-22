---
feature: generic-opaque-value-store
status: stable
keywords: [generics, type-parameter, ownership, layout-descriptor, retain, dictionary]
category: type-system
---

# Storing a borrowed opaque `T` into a record

## Documentation

A shared generic body compiles ONCE for every instantiation, so when it stores a value of its own type
parameter into a durable slot — a tuple it builds, a `Self{…}` field, an array element — it cannot name
the reference protocol that slot owes. The concrete twin of the same body can: a `String` element read
out of a container and stored into a tuple takes a COPY, and a struct element takes an INCREF.

The record's destructor is CONCRETE either way: the caller's `(String, Integer)` is a tuple whose first
slot is a `String`, and its `__destruct_` decrefs that slot whoever filled it. So a shared body that
stored the raw borrow made the record a second OWNER of a reference nobody took — a double free.

The reference is therefore taken at run time, through the enclosing instance's layout descriptor: the
`retainFunc` word holds `__str_clone` for a byte-record argument, `__mm_retain` for a managed aggregate,
and 0 for an argument that owns nothing. It is the same three-way protocol a witness table's
`retainFunc@16` carries, because it answers the same question about a type the code cannot name.

**A managed aggregate SHARES, and that is the observable half.** A struct has reference identity a
program can see, so the slot becomes a second owner of the ONE record — a write through the record read
back out of the container shows through the container. A deep copy would be a different struct, which is
a wrong answer and not merely a slower one.

## Tests

### A managed aggregate argument is SHARED by the record the shared body builds

<!-- test: aggregate-argument-is-shared-not-copied -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max) implements ElementIndex

type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function bump(by Integer)
		n = n + by
	end 'bump'
end 'Cell'

type Holder uses Element
	typealias EArray = Array with Element
	export typealias Entry = (Element, Integer)

	var items = EArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(item Element)
		items.push(item)
	end 'add'

	function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias CellHolder = Holder with Cell

function main() returns ExitCode
	var h = CellHolder.create()
	h.add(Cell.create(40))

	var e = h.entryAt(0)
	e.0.bump(2)

	view again = h.entryAt(0)
	return again.0.n
end 'main'
```
```exitcode
42
```

### A `String` argument outlives the container the shared body read it from

<!-- test: a-parameter-held-in-a-map-field-is-shared -->
A record stored under the type parameter in a `Map with (String, Value)` field is shared: writes through the record read back out show in the map.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Held uses Value
	typealias ValueMap = Map with (String, Value)
	var values as ValueMap

	static function create() returns Self
		return Self{values: ValueMap.create()}
	end 'create'

	function hold(key String, value Value)
		self.values.upsert(key, value: value)
	end 'hold'

	function get(key String) returns Value throws MapError
		return try self.values.get(key)
	end 'get'
end 'Held'

typealias HeldBoxes = Held with Box

function main() returns ExitCode
	var held = HeldBoxes.create()
	held.hold("a", value: Box.create(1))
	for _ in 0 upto 3 'eachRound'
		var b = try held.get("a") otherwise panic("absent")
		b.bump()
	end 'eachRound'
	view c = try held.get("a") otherwise panic("absent")
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3
```

<!-- test: a-parameter-held-in-a-map-field-behind-a-sibling-call-is-shared -->
The same store beside a sibling method that reads the same map.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Held uses Value
	typealias ValueMap = Map with (String, Value)
	var values as ValueMap

	static function create() returns Self
		return Self{values: ValueMap.create()}
	end 'create'

	function hold(key String, value Value)
		self.values.upsert(key, value: value)
		self.touch(key)
	end 'hold'

	function touch(key String)
		if not self.values.contains(key) 'absent'
			panic("touch: the key was just stored")
		end 'absent'
	end 'touch'

	function get(key String) returns Value throws MapError
		return try self.values.get(key)
	end 'get'
end 'Held'

typealias HeldBoxes = Held with Box

function main() returns ExitCode
	var held = HeldBoxes.create()
	held.hold("a", value: Box.create(1))
	for _ in 0 upto 3 'eachRound'
		var b = try held.get("a") otherwise panic("absent")
		b.bump()
	end 'eachRound'
	view c = try held.get("a") otherwise panic("absent")
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3
```

<!-- test: a-parameter-replaced-in-a-map-field-releases-the-old-record -->
Replacing the stored record releases the old one exactly once, and the new one is the record read back out.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Held uses Value
	typealias ValueMap = Map with (String, Value)
	var values as ValueMap

	static function create() returns Self
		return Self{values: ValueMap.create()}
	end 'create'

	function hold(key String, value Value)
		self.values.upsert(key, value: value)
	end 'hold'

	function get(key String) returns Value throws MapError
		return try self.values.get(key)
	end 'get'
end 'Held'

typealias HeldBoxes = Held with Box

function main() returns ExitCode
	var held = HeldBoxes.create()
	let first = Box.create(1)
	held.hold("a", value: first)
	held.hold("a", value: Box.create(7))
	var b = try held.get("a") otherwise panic("absent")
	b.bump()
	view c = try held.get("a") otherwise panic("absent")
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 1
```

<!-- test: a-parameter-read-out-of-a-map-field-by-a-generic-accessor-is-shared -->
The same sharing through a second generic layer that holds the first and forwards to it.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Held uses Value
	typealias ValueMap = Map with (String, Value)
	var values as ValueMap

	static function create() returns Self
		return Self{values: ValueMap.create()}
	end 'create'

	function hold(key String, value Value)
		self.values.upsert(key, value: value)
	end 'hold'

	function get(key String) returns Value throws MapError
		return try self.values.get(key)
	end 'get'
end 'Held'

type Registry uses Item
	typealias ItemHeld = Held with Item
	var held as ItemHeld

	static function create() returns Self
		return Self{held: ItemHeld.create()}
	end 'create'

	function put(key String, item Item)
		self.held.hold(key, value: item)
	end 'put'

	function get(key String) returns Item throws MapError
		return try self.held.get(key)
	end 'get'
end 'Registry'

typealias BoxRegistry = Registry with Box

function main() returns ExitCode
	var registry = BoxRegistry.create()
	registry.put("a", item: Box.create(1))
	for _ in 0 upto 3 'eachRound'
		var b = try registry.get("a") otherwise panic("absent")
		b.bump()
	end 'eachRound'
	view c = try registry.get("a") otherwise panic("absent")
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3
```

<!-- test: a-mixed-instance-snapshot-inside-a-generic-copies-like-the-concrete-one -->
A snapshot cloned inside a generic body copies its records exactly as the same snapshot of a concrete instance does.
```maxon
typealias Count = int(0 to 1000)
typealias Index = int(0 to u64.max) implements ElementIndex
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Columns uses X, Y
	typealias XArray = Array with X
	typealias YArray = Array with Y
	var xs as XArray
	var ys as YArray

	static function create() returns Self
		return Self{xs: XArray.create(), ys: YArray.create()}
	end 'create'

	function add(x X, y Y)
		self.xs.push(x)
		self.ys.push(y)
	end 'add'

	function snapshot() returns Self
		return Self{xs: self.xs.clone(), ys: self.ys.clone()}
	end 'snapshot'

	function yAt(i Index) returns Y
		return try self.ys.get(i) otherwise panic("yAt")
	end 'yAt'
end 'Columns'

type Holder uses T
	typealias Mixed = Columns with (String, T)
	var mixed as Mixed

	static function create() returns Self
		return Self{mixed: Mixed.create()}
	end 'create'

	function put(t T)
		self.mixed.add("k", y: t)
	end 'put'

	function original() returns T
		return self.mixed.yAt(0)
	end 'original'

	function copied() returns T
		return self.mixed.snapshot().yAt(0)
	end 'copied'
end 'Holder'

typealias BoxHolder = Holder with Box
typealias BoxColumns = Columns with (String, Box)

function main() returns ExitCode
	var concrete = BoxColumns.create()
	concrete.add("k", y: Box.create(1))
	var concreteCopy = concrete.snapshot().yAt(0)
	concreteCopy.bump()
	print("concrete {concrete.yAt(0).n}\n")

	var h = BoxHolder.create()
	h.put(Box.create(1))
	var c = h.copied()
	c.bump()
	print("generic {h.original().n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
concrete 1
generic 1
```

<!-- test: error.an-aliased-array-argument-of-a-mixed-instance-snapshot-is-refused -->
An array type argument spelled through an alias is refused at a snapshot exactly as the inline spelling is.
```maxon
typealias Count = int(0 to 1000)
typealias Index = int(0 to u64.max) implements ElementIndex
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Columns uses X, Y
	typealias XArray = Array with X
	typealias YArray = Array with Y
	var xs as XArray
	var ys as YArray

	static function create() returns Self
		return Self{xs: XArray.create(), ys: YArray.create()}
	end 'create'

	function add(x X, y Y)
		self.xs.push(x)
		self.ys.push(y)
	end 'add'

	function snapshot() returns Self
		return Self{xs: self.xs.clone(), ys: self.ys.clone()}
	end 'snapshot'

	function yAt(i Index) returns Y
		return try self.ys.get(i) otherwise panic("yAt")
	end 'yAt'
end 'Columns'

type Holder uses T
	typealias TArray = Array with T
	typealias Mixed = Columns with (String, TArray)
	var mixed as Mixed

	static function create() returns Self
		return Self{mixed: Mixed.create()}
	end 'create'

	function put(t T)
		var column = TArray.create()
		column.push(t)
		self.mixed.add("k", y: column)
	end 'put'

	function original() returns T
		return try self.mixed.yAt(0).get(0) otherwise panic("original")
	end 'original'

	function copied() returns T
		return try self.mixed.snapshot().yAt(0).get(0) otherwise panic("copied")
	end 'copied'
end 'Holder'

typealias BoxHolder = Holder with Box
typealias BoxColumns = Columns with (String, Box)

function main() returns ExitCode
	var concrete = BoxColumns.create()
	concrete.add("k", y: Box.create(1))
	var concreteCopy = concrete.snapshot().yAt(0)
	concreteCopy.bump()
	print("concrete {concrete.yAt(0).n}\n")

	var h = BoxHolder.create()
	h.put(Box.create(1))
	var c = h.copied()
	c.bump()
	print("generic {h.original().n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:46:12: Unsupported: `slice` COPIES each element of an `Array with <type parameter>` field, but this generic type is instantiated with a type whose managed element cannot be deep-cloned — a compiler-owned aggregate (`__ManagedFile`), a base-struct-less generic instance with no runtime copy of its own, an ELEMENT held at an interface type (an element slot is one machine word and a fat pointer is two), or a generic instance that owns one of those. String / struct / boxed-union / container (`Array with int`, `List with String`, `Array with (Array with String)`) / trivial instantiations, a record holding an interface-typed FIELD, and a declared generic's instance whose own substituted fields are all deep-cloneable (`Box with String`), ARE supported.
note: stdlib/Array.maxon:83:32: raised inside the library, on behalf of the construct above
```

<!-- test: a-closure-in-a-generic-method-stores-the-parameter-through-its-descriptor -->
A closure inside a generic method stores a type-parameter value through the enclosing method's descriptor, so the stored record is the shared one.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Held uses Value
	typealias ValueMap = Map with (String, Value)
	var values as ValueMap

	static function create() returns Self
		return Self{values: ValueMap.create()}
	end 'create'

	function hold(key String, value Value) returns bool
		self.values.upsert(key, value: value)
		return true
	end 'hold'

	function get(key String) returns Value throws MapError
		return try self.values.get(key)
	end 'get'
end 'Held'

typealias Action = function() returns bool

function perform(action Action) returns bool
	return action()
end 'perform'

type Registry uses Item
	typealias ItemHeld = Held with Item
	var held as ItemHeld

	static function create() returns Self
		return Self{held: ItemHeld.create()}
	end 'create'

	function put(key String, item Item)
		if not self.held.hold(key, value: item) 'unstored'
			panic("put: hold always stores")
		end 'unstored'
	end 'put'

	function copyLater(source String, target String)
		if not perform(function() gives held.hold(target, value: try held.get(source) otherwise panic("absent"))) 'uncopied'
			panic("copyLater: hold always stores")
		end 'uncopied'
	end 'copyLater'

	function get(key String) returns Item throws MapError
		return try self.held.get(key)
	end 'get'
end 'Registry'

typealias BoxRegistry = Registry with Box

function main() returns ExitCode
	var registry = BoxRegistry.create()
	registry.put("a", item: Box.create(1))
	registry.copyLater("a", target: "b")
	for _ in 0 upto 3 'eachRound'
		var b = try registry.get("b") otherwise panic("absent")
		b.bump()
	end 'eachRound'
	view c = try registry.get("a") otherwise panic("absent")
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 3
```

<!-- test: a-closure-reading-a-parameter-out-of-a-generic-cell-keeps-it-alive -->
A closure inside a generic method that reads a type-parameter field hands back the shared record, which stays alive and balanced across every call.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'

	function bump()
		self.n = self.n + 1
		self.names.push("x{self.n}")
	end 'bump'
end 'Box'

type Cell uses T
	typealias Source = function() returns T
	var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	function get() returns T
		return self.value
	end 'get'

	function run(source Source) returns T
		return source()
	end 'run'

	function later() returns T
		return self.run(function() gives value)
	end 'later'
end 'Cell'

typealias BoxCell = Cell with Box

function main() returns ExitCode
	var cell = BoxCell.create(Box.create(5))
	for _ in 0 upto 3 'eachRound'
		var b = cell.later()
		b.bump()
	end 'eachRound'
	view c = cell.get()
	print("{c.n} {c.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 3
```

<!-- test: error.a-generic-type-reaching-itself-through-a-growing-argument-is-refused -->
A generic type whose methods reach an instance of itself with a strictly growing type argument has no finite set of instances, and is refused.
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair uses A, B
	typealias AArray = Array with A
	typealias BArray = Array with B
	typealias Deeper = Pair with (A, BArray)
	var firsts as AArray
	var seconds as BArray

	static function create() returns Self
		return Self{firsts: AArray.create(), seconds: BArray.create()}
	end 'create'

	function depth() returns Integer
		let d = Deeper.create()
		return d.depth() + 1
	end 'depth'
end 'Pair'

typealias IntStrPair = Pair with (Integer, String)

function main() returns ExitCode
	let p = IntStrPair.create()
	return p.depth() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:11: Unsupported: 'Pair.create' is called on 'Pair.Deeper', whose type arguments build on the type parameters of 'Pair', and whose methods reach 'Pair' again — so composing its layout descriptor at a concrete instantiation would build an unbounded chain of ever-deeper instances. Give that type argument a concrete type, or pass the value it wraps as a type parameter of its own
```

<!-- test: mapping-a-map-of-records-releases-the-entry-tuples-it-built -->
Each `map` over a `Map with (String, Box)` builds entry tuples that share the records, keeps them alive after the map replaces its values, and releases them when the mapped array goes.
```maxon
typealias Count = int(0 to 1000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'
end 'Box'

typealias BoxMap = Map with (String, Box)

function main() returns ExitCode
	var m = BoxMap.create()
	m.upsert("a", value: Box.create(1))
	m.upsert("bb", value: Box.create(2))
	var total = 0 as Count
	for _ in 0 upto 3 'eachRound'
		let mapped = m.map(function(p) gives p)
		m.upsert("a", value: Box.create(50))
		m.upsert("bb", value: Box.create(60))
		for pair in mapped 'sum'
			total = total + pair.1.n + (pair.1.names.count() as Count)
		end 'sum'
	end 'eachRound'
	view a = try m.get("a") otherwise panic("absent")
	view bb = try m.get("bb") otherwise panic("absent")
	print("{total} {a.n} {a.names.count()} {bb.n} {bb.names.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
223 50 0 60 0
```

<!-- test: a-record-over-the-enclosing-parameter-in-a-container-is-created-and-cloned -->
A record built over the enclosing type's own parameter is pushed into a container, and a clone of that container keeps its own copy.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	export typealias EBoxArray = Array with EBox

	var items as EBoxArray

	static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 4)
	let c = b.copy()
	b.add(heap(2), tag: 5)
	let e = try c.get(0) otherwise return 92
	print("{e.v} {c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough to allocate 1 1
```

<!-- test: a-record-over-the-enclosing-parameter-in-a-list-is-created-and-read -->
The same record kept in a `List` node reads back the value it was built with.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = List with EBox
	var items as Store

	function add(x Element, tag Integer)
		items.append(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element
		let slot = try items.first() otherwise panic("empty")
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store.create()}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 7)
	b.add(heap(2), tag: 8)
	let f = b.first()
	print("{f}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough to allocate 1
```

<!-- test: a-record-over-the-enclosing-parameter-pushed-into-its-container-is-shared -->
A record read out of one container and pushed into another is shared by both, and the first container is unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	export typealias EBoxArray = Array with EBox

	var items as EBoxArray

	static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'

	function addBox(b EBox)
		self.items.push(b)
	end 'addBox'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 4)
	var c = b.copy()
	var one = try c.get(0) otherwise return 90
	b.addBox(one)
	b.add(heap(2), tag: 5)
	view e = try c.get(0) otherwise return 92
	print("{e.v} {c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough to allocate 1 1
```

<!-- test: a-record-over-the-enclosing-parameter-stashed-by-a-static-reached-from-an-instance-method-is-shared -->
A static reached from an instance method pushes the record into the instance's container, which grows to two.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	export typealias EBoxArray = Array with EBox

	var items as EBoxArray

	static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'

	function viaStash(b EBox) returns Integer
		return Self.stash(b, into: self.items)
	end 'viaStash'

	static function stash(b EBox, into EBoxArray) returns Integer
		var target = into
		target.push(b)
		return target.count()
	end 'stash'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 4)
	var c = b.copy()
	var one = try c.get(0) otherwise return 90
	let n = b.viaStash(one)
	print("{n}\n")
	b.add(heap(2), tag: 5)
	view e = try c.get(0) otherwise return 92
	print("{e.v} {c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
a heap string long enough to allocate 1 1
```

<!-- test: a-record-over-the-enclosing-parameter-wrapped-in-a-fresh-container-is-shared -->
A record wrapped in a fresh container built inside the generic body is the same record the original container holds.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	export typealias EBoxArray = Array with EBox

	var items as EBoxArray

	static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'

	function wrap(b EBox) returns EBoxArray
		var xs = EBoxArray.create()
		xs.push(b)
		return xs
	end 'wrap'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 4)
	let c = b.copy()
	let one = try c.get(0) otherwise return 90
	let w = b.wrap(one)
	let w0 = try w.get(0) otherwise return 91
	print("{w0.v} {w.count()}\n")
	b.add(heap(2), tag: 5)
	let e = try c.get(0) otherwise return 92
	print("{e.v} {c.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough to allocate 1 1
a heap string long enough to allocate 1 1
```

<!-- test: a-container-of-tuples-over-the-parameter-clones-its-records -->
A clone of an array of `(String, Value)` tuples is its own array: an entry added after the clone is not in it, and every record it holds is released.
```maxon
typealias Count = int(0 to 100000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'
end 'Box'

type Holder uses Value
	typealias Pair = (String, Value)
	typealias Pairs = Array with Pair
	typealias Copier = function() returns Pairs
	var pairs as Pairs

	static function create() returns Self
		return Self{pairs: Pairs.create()}
	end 'create'

	function put(key String, value Value)
		self.pairs.push((key, value))
	end 'put'

	function snapshot() returns Pairs
		return self.pairs.clone()
	end 'snapshot'

	function apply(copier Copier) returns Pairs
		return copier()
	end 'apply'
end 'Holder'

typealias BoxHolder = Holder with Box

function main() returns ExitCode
	var h = BoxHolder.create()
	h.put("x", value: Box.create(5))
	h.put("y", value: Box.create(6))
	let snap = h.snapshot()
	h.put("z", value: Box.create(7))
	var total = 0 as Count

	for pair in snap 'eachPair'
		total = total + pair.1.n
	end 'eachPair'

	print("{total} {snap.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 2
```

<!-- test: a-container-of-tuples-over-the-parameter-cloned-inside-a-closure-clones-its-records -->
The same clone written inside a closure in the method.
```maxon
typealias Count = int(0 to 100000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'
end 'Box'

type Holder uses Value
	typealias Pair = (String, Value)
	typealias Pairs = Array with Pair
	typealias Copier = function() returns Pairs
	var pairs as Pairs

	static function create() returns Self
		return Self{pairs: Pairs.create()}
	end 'create'

	function put(key String, value Value)
		self.pairs.push((key, value))
	end 'put'

	function snapshot() returns Pairs
		return self.apply(function() gives self.pairs.clone())
	end 'snapshot'

	function apply(copier Copier) returns Pairs
		return copier()
	end 'apply'
end 'Holder'

typealias BoxHolder = Holder with Box

function main() returns ExitCode
	var h = BoxHolder.create()
	h.put("x", value: Box.create(5))
	h.put("y", value: Box.create(6))
	view snap = h.snapshot()
	h.put("z", value: Box.create(7))
	var total = 0 as Count

	for pair in snap 'eachPair'
		total = total + pair.1.n
	end 'eachPair'

	print("{total} {snap.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 2
```

<!-- test: a-container-of-tuples-over-the-parameter-returned-releases-its-records -->
Returning the array field hands back the array itself, so an entry added afterwards is in it, and every record it holds is released.
```maxon
typealias Count = int(0 to 100000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'
end 'Box'

type Holder uses Value
	typealias Pair = (String, Value)
	typealias Pairs = Array with Pair
	typealias Copier = function() returns Pairs
	var pairs as Pairs

	static function create() returns Self
		return Self{pairs: Pairs.create()}
	end 'create'

	function put(key String, value Value)
		self.pairs.push((key, value))
	end 'put'

	function snapshot() returns Pairs
		return self.pairs
	end 'snapshot'

	function apply(copier Copier) returns Pairs
		return copier()
	end 'apply'
end 'Holder'

typealias BoxHolder = Holder with Box

function main() returns ExitCode
	var h = BoxHolder.create()
	h.put("x", value: Box.create(5))
	h.put("y", value: Box.create(6))
	view snap = h.snapshot()
	h.put("z", value: Box.create(7))
	var total = 0 as Count

	for pair in snap 'eachPair'
		total = total + pair.1.n
	end 'eachPair'

	print("{total} {snap.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18 3
```

<!-- test: a-container-literal-over-the-parameter-in-an-instance-method-is-served -->
A container literal of records over the enclosing parameter is built in an instance method, which takes the descriptor from `self`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export var tag as Integer

	static function create(x T, tag Integer) returns Self
		return Self{v: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	export typealias EBoxArray = Array with EBox

	var items as EBoxArray

	static function create() returns Self
		return Self{items: EBoxArray.create()}
	end 'create'

	function add(x Element, tag Integer)
		self.items.push(EBox.create(x, tag: tag))
	end 'add'

	function copy() returns EBoxArray
		return self.items.clone()
	end 'copy'

	function wrap(b EBox) returns EBoxArray
		return [b]
	end 'wrap'
end 'Bag'

typealias StrBag = Bag with String

function heap(n Integer) returns String
	var sb = StringBuilder.create()
	sb.append("a heap string long enough to allocate {n}")
	return sb.build()
end 'heap'

function main() returns ExitCode
	var b = StrBag.create()
	b.add(heap(1), tag: 4)
	let c = b.copy()
	let one = try c.get(0) otherwise return 90
	let w = b.wrap(one)
	b.add(heap(2), tag: 5)
	let e = try c.get(0) otherwise return 92
	print("{e.v} {c.count()} {w.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap string long enough to allocate 1 1 1
```

<!-- test: a-closure-in-a-static-storing-a-borrowed-parameter-takes-the-call-sites-descriptor -->
A closure written in a static stores a borrowed type-parameter value. The closure carries the descriptor of the body it is written in, which `StrCell.keep` receives from its call, so the record the closure builds holds a reference of its own.
```maxon
typealias Int = int(i64.min to i64.max)

type Cell uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function marker() returns Int
		return 1
	end 'marker'

	static function firstOf(cells CellArray) returns Int
		let f = function() gives cells.count()
		return f()
	end 'firstOf'

	static function keep(v T) returns Int
		let f = function(x T) gives Self{v: x}
		let c = f(v)
		return c.marker()
	end 'keep'
end 'Cell'

typealias StrCell = Cell with String
typealias CellArray = Array with StrCell

function main() returns ExitCode
	let c = StrCell.create("a {40 + 2} value long enough for a heap record")
	print("{StrCell.keep("b {40 + 2} value long enough for a heap record")} {c.marker()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: a-static-reassigning-its-parameter-from-a-borrow-takes-the-call-sites-descriptor -->
A static that does not return `Self` reassigns its type-parameter parameter from a field of another instance. The reference it takes is read through the descriptor `StrCell.adopt` hands the shared body, and the record the field names is still live afterwards.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function adopt(v T, other Self) returns bool
		v = other.v
		return true
	end 'adopt'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	let src = StrCell.make("source {40 + 2} value long enough for a heap record")
	let adopted = StrCell.adopt("dropped {40 + 2} value long enough for a heap record", other: src)
	print("{src.v} {adopted}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source 42 value long enough for a heap record true
```

<!-- test: a-static-handing-a-local-to-a-by-reference-parameter-takes-the-call-sites-descriptor -->
A static that does not return `Self` hands a local holding a type-parameter value to an instance method of another instance that takes it by reference. The static takes the descriptor from its call site and hands it on to that method.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'

	static function viaLocal(other Self) returns T
		var x = other.v
		other.setOnce(x)
		return x
	end 'viaLocal'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	let c = StrCell.make("payload {40 + 2} value long enough for a heap record")
	print("{StrCell.viaLocal(c)} {c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
payload 42 value long enough for a heap record payload 42 value long enough for a heap record
```

<!-- test: a-static-handing-a-field-to-a-by-reference-parameter-takes-the-call-sites-descriptor -->
A static that does not return `Self` hands a field of another instance to an instance method that takes it by reference. The static takes the descriptor from its call site and hands it on to that method.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'

	static function poke(into Self, donor Self)
		into.setOnce(donor.v)
	end 'poke'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	var src = StrCell.make("source {40 + 2} value long enough for a heap record")
	var dst = StrCell.make("target {40 + 2} value long enough for a heap record")
	StrCell.poke(dst, donor: src)
	print("{src.v} {dst.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
target 42 value long enough for a heap record target 42 value long enough for a heap record
```

<!-- test: a-static-handing-a-record-field-to-a-by-reference-parameter-takes-the-call-sites-descriptor -->
The same hand-off at a record instantiation: the record's `String` field is retained through the call site's descriptor and released once when the cell dies.
```maxon
typealias Small = int(0 to 1000)

type Entry
	export var name as String
	export var n as Small

	static function create(name String, n Small) returns Self
		return Self{name: name, n: n}
	end 'create'
end 'Entry'

type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'

	static function viaLocal(other Self) returns T
		var x = other.v
		other.setOnce(x)
		return x
	end 'viaLocal'
end 'Cell'

typealias EntryCell = Cell with Entry

function main() returns ExitCode
	let c = EntryCell.make(Entry.create("entry {40 + 2} value long enough for a heap record", n: 7))
	let got = EntryCell.viaLocal(c)
	print("{got.name} {got.n} {c.v.name}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
entry 42 value long enough for a heap record 7 entry 42 value long enough for a heap record
```

<!-- test: a-static-reading-sizeof-that-also-passes-an-int-by-reference-reads-the-call-sites-descriptor -->
A static that does not return `Self` reads `sizeof` of the type parameter and also hands an integer local to an unrelated by-reference parameter. The size comes from the descriptor the call site hands the static.
```maxon
typealias Count = int(0 to 1000)

function bump(n Count)
	n = n + 1
end 'bump'

type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function width(seed Count) returns Count
		var k = seed
		bump(k)
		return (sizeof(T) as Count) + k
	end 'width'
end 'Cell'

typealias CountCell = Cell with Count

function main() returns ExitCode
	print("{CountCell.width(1) == (sizeof(Count) as Count) + 2} {CountCell.make(2).v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true 2
```

<!-- test: a-call-reaching-a-generic-and-a-concrete-declaration-binds-each-by-its-own-convention -->
A name reaches both a generic function that reassigns its parameter and a concrete function of the same name; each call binds the declaration its arguments select and passes each argument by that declaration's convention.
```maxon
typealias Count = int(0 to 1000)

function settle(dest T, src T) uses T
	dest = src
end 'settle'

function settle(dest Count, src Count) returns Count
	return dest + src
end 'settle'

function seed() returns Count
	return 2
end 'seed'

function main() returns ExitCode
	var target = "target"
	let source = "source"
	settle(target, src: source)
	var left = 1 as Count
	let right = seed()
	print("{target} {settle(left, src: right)}\n")
	left = 3
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source 3
```

<!-- test: a-call-reaching-a-concrete-declaration-that-reassigns-and-a-generic-one-that-does-not -->
A name reaches both a concrete function that reassigns its parameter and a generic function of the same name that does not; the concrete call is handed the caller's storage and the generic call its argument's value.
```maxon
typealias Count = int(0 to 1000)

function bump(dest Count)
	dest = dest + 1
end 'bump'

function bump(dest T) uses T returns T
	return dest
end 'bump'

function main() returns ExitCode
	var n = 5 as Count
	bump(n)
	var s = "word"
	print("{bump(s)} {n}\n")
	s = "done"
	return 0
end 'main'
```
```exitcode
0
```
```stdout
word 6
```

<!-- test: a-static-dropping-an-opaque-value-it-reads-releases-it-through-the-call-sites-descriptor -->
A closure in a static reads an opaque value and drops it. The static receives the descriptor from its call, and the closure releases the value through it.
```maxon
typealias Int = int(i64.min to i64.max)

type Cell uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function get() returns T
		return v
	end 'get'
end 'Cell'

type Holder uses T
	typealias Inner = Cell with T
	typealias Pair = (Inner, Int)
	var one as Inner

	static function create(v T) returns Self
		return Self{one: Inner.create(v)}
	end 'create'

	static function peek(h Self) returns Int
		let f = function() gives h.one.get()
		_ = f()
		return 1
	end 'peek'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	let h = StrHolder.create("a {40 + 2} value long enough for a heap record")
	print("{StrHolder.peek(h)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-composed-container-cloned-in-a-closure-in-a-static-copies-through-the-call-sites-descriptor -->
A container of tuples over the parameter cloned in a closure written in a static copies its records through the descriptor the static receives from its call.
```maxon
typealias Count = int(0 to 100000)
typealias NameArray = Array with String

type Box
	export var n as Count
	export var names as NameArray

	static function create(n Count) returns Box
		return Self{n: n, names: NameArray.create()}
	end 'create'
end 'Box'

type Holder uses Value
	typealias Pair = (String, Value)
	typealias Pairs = Array with Pair
	typealias Copier = function() returns Pairs
	var pairs as Pairs

	static function create() returns Self
		return Self{pairs: Pairs.create()}
	end 'create'

	function put(key String, value Value)
		self.pairs.push((key, value))
	end 'put'

	function snapshot() returns Pairs
		return self.apply(function() gives self.pairs.clone())
	end 'snapshot'

	function apply(copier Copier) returns Pairs
		return copier()
	end 'apply'

	static function countOf(ps Pairs) returns Count
		let f = function() gives ps.clone().count()
		return f()
	end 'countOf'
end 'Holder'

typealias BoxHolder = Holder with Box

function main() returns ExitCode
	var h = BoxHolder.create()
	h.put("x", value: Box.create(5))
	h.put("y", value: Box.create(6))
	var snap = h.snapshot()
	let k = BoxHolder.countOf(snap)
	h.put("z", value: Box.create(7))
	var total = 0 as Count

	for pair in snap 'eachPair'
		total = total + pair.1.n
	end 'eachPair'

	print("{total} {snap.count()} {k}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 2 2
```

<!-- test: a-generic-types-own-name-literal-in-a-static-builds-like-self -->
A generic type's own name, written as a literal in its `Self`-returning static, builds the record exactly as `Self{}` does, field defaults included.
```maxon
type Bag uses T
	typealias TArray = Array with T
	export var items = TArray.create()

	static function make() returns Self
		return Bag{}
	end 'make'

	function add(x T)
		self.items.push(x)
	end 'add'
end 'Bag'

typealias StrBag = Bag with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var b = StrBag.make()
	b.add(heapString("first payload ", b: "long enough to allocate"))
	print("{b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: a-self-returning-static-copying-a-borrowed-parameter-field-is-served -->
A `Self`-returning static that copies a type-parameter field out of a borrowed `Self` takes its reference through the descriptor its result sources.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function copyOf(other Self) returns Self
		return Self{v: other.v}
	end 'copyOf'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let s = heapString("first payload ", b: "long enough to allocate")
	let a = StrCell.create(s)
	let b = StrCell.copyOf(a)
	print("{b.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-generic-types-own-name-static-call-in-its-body-is-served -->
A static called by the generic type's own name inside its body is the same call as `Self.make()`.
```maxon
type Bag uses T
	typealias TArray = Array with T
	export var items as TArray

	static function make() returns Self
		return Self{items: TArray.create()}
	end 'make'

	function add(x T)
		self.items.push(x)
	end 'add'

	function fresh() returns Self
		return Bag.make()
	end 'fresh'
end 'Bag'

typealias StrBag = Bag with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var b = StrBag.make()
	b.add(heapString("first payload ", b: "long enough to allocate"))
	var c = b.fresh()
	c.add(heapString("second payload ", b: "long enough to allocate"))
	c.add(heapString("third payload ", b: "long enough to allocate"))
	print("{b.items.count()} {c.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-generic-types-own-name-local-literal-is-served -->
A local bound to a literal spelled with the generic type's own name builds the record as `Self{…}` does.
```maxon
type Bag uses T
	typealias TArray = Array with T
	export var items as TArray

	static function make() returns Self
		return Self{items: TArray.create()}
	end 'make'

	function add(x T)
		self.items.push(x)
	end 'add'

	function withOne(y T) returns Self
		var x = Bag{items: TArray.create()}
		x.items.push(y)
		return x
	end 'withOne'
end 'Bag'

typealias StrBag = Bag with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var b = StrBag.make()
	let c = b.withOne(heapString("second payload ", b: "long enough to allocate"))
	print("{c.items.count()} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 0
```

<!-- test: a-parameter-typed-by-the-generic-types-own-name-is-served -->
A parameter typed by the generic type's own name is the same instance as `Self`, so its type-parameter elements can be read and stored.
```maxon
type Bag uses T
	typealias TArray = Array with T
	export var items as TArray

	static function make() returns Self
		return Self{items: TArray.create()}
	end 'make'

	function add(x T)
		self.items.push(x)
	end 'add'

	function merge(other Bag)
		for x in other.items 'each'
			self.items.push(x)
		end 'each'
	end 'merge'
end 'Bag'

typealias StrBag = Bag with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrBag.make()
	a.add(heapString("first payload ", b: "long enough to allocate"))
	var b = StrBag.make()
	b.add(heapString("second payload ", b: "long enough to allocate"))
	a.merge(b)
	print("{a.items.count()} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
```

<!-- test: an-opaque-field-assigned-from-another-instances-field-is-shared -->
A type-parameter field assigned from another instance's field holds that instance's value: a record would be shared, and a `String` holds the same text.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function takeFrom(other Self)
		self.v = other.v
	end 'takeFrom'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrCell.create(heapString("first payload ", b: "long enough to allocate"))
	let b = StrCell.create(heapString("second payload ", b: "long enough to allocate"))
	a.takeFrom(b)
	print("{a.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-an-array-element-is-served -->
A `Self`-returning static builds its own record from an element read out of an array of the type parameter.
```maxon
type Cell uses T
	typealias TArray = Array with T
	export var v as T

	static function firstOf(xs TArray) returns Self
		return Self{v: try xs.get(0) otherwise panic("empty")}
	end 'firstOf'
end 'Cell'

typealias StrCell = Cell with String
typealias Strings = Array with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var xs = Strings.create()
	xs.push(heapString("first payload ", b: "long enough to allocate"))
	xs.push(heapString("second payload ", b: "long enough to allocate"))
	view c = StrCell.firstOf(xs)
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-a-for-element-is-served -->
The same record built from a `for` element.
```maxon
type Cell uses T
	typealias TArray = Array with T
	export var v as T

	static function firstOf(xs TArray) returns Self
		for e in xs 'each'
			return Self{v: e}
		end 'each'
		panic("empty")
	end 'firstOf'
end 'Cell'

typealias StrCell = Cell with String
typealias Strings = Array with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var xs = Strings.create()
	xs.push(heapString("first payload ", b: "long enough to allocate"))
	xs.push(heapString("second payload ", b: "long enough to allocate"))
	view c = StrCell.firstOf(xs)
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-a-local-bound-to-a-read-is-served -->
The same record built from a local bound to the element read.
```maxon
type Cell uses T
	typealias TArray = Array with T
	export var v as T

	static function firstOf(xs TArray) returns Self
		let e = try xs.get(0) otherwise panic("empty")
		return Self{v: e}
	end 'firstOf'
end 'Cell'

typealias StrCell = Cell with String
typealias Strings = Array with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var xs = Strings.create()
	xs.push(heapString("first payload ", b: "long enough to allocate"))
	xs.push(heapString("second payload ", b: "long enough to allocate"))
	view c = StrCell.firstOf(xs)
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-a-call-results-field-is-served -->
The same record built from a field of another `Self`-returning static's result.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function copyVia(v T) returns Self
		return Self{v: Self.make(v).v}
	end 'copyVia'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let c = StrCell.copyVia(heapString("first payload ", b: "long enough to allocate"))
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-a-local-bound-to-its-parameter-is-served -->
The same record built from a local that holds the parameter.
```maxon
type Cell uses T
	export var v as T

	static function wrap(v T) returns Self
		let x = v
		return Self{v: x}
	end 'wrap'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let c = StrCell.wrap(heapString("first payload ", b: "long enough to allocate"))
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first payload long enough to allocate
```

<!-- test: a-self-returning-static-built-from-a-reassigned-parameter-is-served -->
The same record built from a parameter that the body reassigns from a field of another instance.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function adopt(v T, other Self) returns Self
		v = other.v
		return Self{v: v}
	end 'adopt'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function build() returns StrCell
	let src = StrCell.make(heapString("source payload ", b: "long enough to allocate"))
	return StrCell.adopt(heapString("dropped payload ", b: "long enough to allocate"), other: src)
end 'build'

function main() returns ExitCode
	let c = build()
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{c.v}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source payload long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-self-returning-static-built-from-a-try-with-an-owned-local-handler-is-served -->
A `Self`-returning static fills a field from a `try` whose handler value is a local holding an owned value, on both the path where the call succeeds and the path where the handler is used.
```maxon
enum PickError implements Error
	empty
end 'PickError'

type Cell uses T
	export var v as T
	export var label as String
	export var labelled as bool

	static function make(v T, label String, labelled bool) returns Self
		return Self{v: v, label: label, labelled: labelled}
	end 'make'

	function pickLabel() returns String throws PickError
		if not labelled 'unlabelled'
			throw PickError.empty
		end 'unlabelled'

		return label
	end 'pickLabel'

	static function relabel(v T, other Self, fallback String) returns Self
		let d = fallback.clone()
		return Self{v: v, label: try other.pickLabel() otherwise d, labelled: true}
	end 'relabel'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let labelled = StrCell.make(heapString("first value ", b: "long enough to allocate"), label: heapString("picked label ", b: "long enough to allocate"), labelled: true)
	let unlabelled = StrCell.make(heapString("second value ", b: "long enough to allocate"), label: heapString("unused label ", b: "long enough to allocate"), labelled: false)
	let fallback = heapString("fallback label ", b: "long enough to allocate")
	let picked = StrCell.relabel(heapString("third value ", b: "long enough to allocate"), other: labelled, fallback: fallback)
	let fellBack = StrCell.relabel(heapString("fourth value ", b: "long enough to allocate"), other: unlabelled, fallback: fallback)
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{picked.v} / {picked.label}\n")
	print("{fellBack.v} / {fellBack.label}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
third value long enough to allocate / picked label long enough to allocate
fourth value long enough to allocate / fallback label long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-generic-functions-by-reference-parameter-takes-the-callers-cell -->
A generic function that reassigns its type-parameter parameter is handed the caller's storage, whether the argument is a local, a value computed at the call, or a ranged integer.
```maxon
typealias Count = int(0 to 1000)

function overwrite(dest T, with T) uses T
	dest = with
end 'overwrite'

function swapIn(x T, y T) uses T returns T
	var held = x
	overwrite(held, with: y)
	return held
end 'swapIn'

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let first = heapString("first payload ", b: "long enough to allocate")
	let second = heapString("second payload ", b: "long enough to allocate")
	print("{swapIn(first, y: second)}\n")
	print("{swapIn(3 as Count, y: 7 as Count)}\n")
	var s = heapString("literal target ", b: "long enough to allocate")
	overwrite(s, with: heapString("rvalue payload ", b: "long enough to allocate"))
	print("{s}\n")
	print("{first} {second}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate
7
rvalue payload long enough to allocate
first payload long enough to allocate second payload long enough to allocate
```

<!-- test: error.a-let-handed-to-a-reassigned-parameter-inside-a-generic-function-is-reported-once-under-its-own-name -->
A generic function instantiated twice hands a `let` to a generic function that reassigns its parameter, and the refusal is reported once, naming the generic function.
```maxon
typealias Count = int(0 to 1000)

function overwrite(dest T, with T) uses T
	dest = with
end 'overwrite'

function swapIn(v T) uses T returns T
	let x = v
	overwrite(x, with: v)
	return x
end 'swapIn'

function main() returns ExitCode
	let t = "text"
	print("{swapIn(t)} {swapIn(4 as Count)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:2: cannot pass 'x', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in swapIn); pass a clone()
```

<!-- test: two-files-private-generic-functions-of-one-name-each-keep-their-own-by-reference-parameter -->
Two files each declare a private generic function of the same name, and each reassigns a different parameter; each call hands over the parameter its own file's function reassigns.
```maxon
// --- file: a.maxon
function settle(dest T, src T) uses T
	dest = src
end 'settle'

export function fromA() returns String
	var s = "a before"
	var w = "a after"
	settle(s, src: w)
	return s
end 'fromA'

// --- file: main.maxon
function settle(dest T, src T) uses T
	src = dest
end 'settle'

function main() returns ExitCode
	var s = "b dest"
	var w = "b src"
	settle(s, src: w)
	print("{w} / {fromA()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
b dest / a after
```

<!-- test: a-private-generic-function-and-another-files-private-concrete-one-of-one-name-each-bind-their-own-calls -->
One file declares a private generic function that reassigns a parameter, and another file declares a private non-generic function of the same name; each call reaches only the declaration its own file can see.
```maxon
// --- file: b.maxon
function swap(a T, b T) uses T
	a = b
end 'swap'

export function fromB() returns String
	var s = "b1"
	swap(s, b: "b2")
	return s
end 'fromB'

// --- file: main.maxon
typealias Integer = int(0 to 100)

function swap(a Integer) returns Integer
	return a + 1
end 'swap'

function main() returns ExitCode
	var x = 5 as Integer
	print("{swap(x)} {fromB()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6 b2
```

<!-- test: a-reassigned-parameter-stored-by-a-static-that-also-stores-a-field-is-released -->
A static that reassigns its parameter from a field of another instance, on a path that does not return the other field, releases what it dropped.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function adopt(v T, other Self, flag bool) returns Self
		v = other.v

		if flag 'direct'
			return Self{v: other.v}
		end 'direct'

		return Self{v: v}
	end 'adopt'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function build() returns StrCell
	let src = StrCell.make(heapString("source payload ", b: "long enough to allocate"))
	return StrCell.adopt(heapString("dropped payload ", b: "long enough to allocate"), other: src, flag: false)
end 'build'

function main() returns ExitCode
	let c = build()
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{c.v}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source payload long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-closure-parameter-named-like-the-methods-parameter-is-its-own-value -->
A closure inside a static that names its own parameter like the method's parameter builds the record from the closure's argument.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function rewrap(v T, other Self) returns Self
		let f = function(v T) gives Self{v: v}
		_ = Self{v: v}
		return f(other.v)
	end 'rewrap'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function build() returns StrCell
	let src = StrCell.make(heapString("source payload ", b: "long enough to allocate"))
	return StrCell.rewrap(heapString("unused payload ", b: "long enough to allocate"), other: src)
end 'build'

function main() returns ExitCode
	let c = build()
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{c.v}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source payload long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-closure-parameter-named-like-a-local-is-its-own-value -->
A closure parameter named like a local of the static is the closure's argument, not the local.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	static function rewrap(v T, other Self, keepLocal bool) returns Self
		let chosen = v
		let f = function(chosen T) gives Self{v: chosen}

		if keepLocal 'local'
			return Self{v: chosen}
		end 'local'

		return f(other.v)
	end 'rewrap'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function build(keepLocal bool) returns StrCell
	let src = StrCell.make(heapString("source payload ", b: "long enough to allocate"))
	return StrCell.rewrap(heapString("chosen payload ", b: "long enough to allocate"), other: src, keepLocal: keepLocal)
end 'build'

function main() returns ExitCode
	let local = build(true)
	let c = build(false)
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{local.v}\n")
	print("{c.v}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
chosen payload long enough to allocate
source payload long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-local-rebound-through-a-by-reference-argument-is-stored-as-rebound -->
A local passed to a method whose parameter is reassigned holds the reassigned value when the static stores it.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function overwrite(dest T)
		dest = self.v
	end 'overwrite'

	static function adopt(v T, other Self) returns Self
		var x = v
		other.overwrite(x)
		return Self{v: x}
	end 'adopt'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function build() returns StrCell
	let src = StrCell.make(heapString("source payload ", b: "long enough to allocate"))
	return StrCell.adopt(heapString("dropped payload ", b: "long enough to allocate"), other: src)
end 'build'

function main() returns ExitCode
	let c = build()
	let noise = heapString("overwrite payload ", b: "long enough to allocate xxxxxxxx")
	print("{c.v}\n")
	print("{noise}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
source payload long enough to allocate
overwrite payload long enough to allocate xxxxxxxx
```

<!-- test: a-long-chain-of-reassigned-locals-compiles-in-linear-time -->
Deciding what a record literal's field holds costs one visit per local, however each local is rebound.
```maxon
type Cell uses T
	export var v as T

	static function chain(v T) returns Self
		var a0 = v
		a0 = v
		var a1 = a0
		a1 = a0
		var a2 = a1
		a2 = a1
		var a3 = a2
		a3 = a2
		var a4 = a3
		a4 = a3
		var a5 = a4
		a5 = a4
		var a6 = a5
		a6 = a5
		var a7 = a6
		a7 = a6
		var a8 = a7
		a8 = a7
		var a9 = a8
		a9 = a8
		var a10 = a9
		a10 = a9
		var a11 = a10
		a11 = a10
		var a12 = a11
		a12 = a11
		var a13 = a12
		a13 = a12
		var a14 = a13
		a14 = a13
		var a15 = a14
		a15 = a14
		var a16 = a15
		a16 = a15
		var a17 = a16
		a17 = a16
		var a18 = a17
		a18 = a17
		var a19 = a18
		a19 = a18
		var a20 = a19
		a20 = a19
		var a21 = a20
		a21 = a20
		var a22 = a21
		a22 = a21
		var a23 = a22
		a23 = a22
		var a24 = a23
		a24 = a23
		return Self{v: a24}
	end 'chain'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	let c = StrCell.chain("payload")
	print("{c.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
payload
```

<!-- test: a-by-reference-parameter-at-a-ranged-int-instantiation-takes-a-local-and-a-literal -->
A method that reassigns its type-parameter parameter, instantiated at a ranged `int`, is handed a local and a literal: the local receives the reassigned value and the literal's temporary is range-checked as a value.
```maxon
typealias Count = int(0 to 1000)

type Box uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'
end 'Box'

typealias CountBox = Box with Count

function main() returns ExitCode
	let b = CountBox.make(10)
	var s = 20 as Count
	b.setOnce(s)
	b.setOnce(160)
	print("once {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
once 10
```

<!-- test: error.an-out-of-range-literal-at-a-generic-byref-param-is-a-compile-error -->
A method that reassigns its type-parameter parameter, instantiated at a ranged `int`, is handed a literal outside that range, and the literal is refused where it is written, as at a by-value parameter.
```maxon
typealias Count = int(0 to 1000)

type Box uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'
end 'Box'

typealias CountBox = Box with Count

function main() returns ExitCode
	let b = CountBox.make(10)
	b.setOnce(2000)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:4: Value 2000 is outside the range of 'Count' (int(0 to 1000))
```

<!-- test: a-generic-type-that-is-never-instantiated-may-reassign-its-parameter -->
A generic type the program never instantiates declares a method that reassigns its type-parameter parameter; the program still compiles and runs.
```maxon
type Lonely uses T
	export var v as T

	function setOnce(dest T)
		dest = self.v
	end 'setOnce'
end 'Lonely'

function main() returns ExitCode
	print("ok\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: a-local-named-like-the-generic-type-is-not-the-type -->
A parameter named like the generic type is a value, so `Bag.make()` inside the body is that value's method, not the type's static.
```maxon
typealias Count = int(0 to 1000)

type Other
	export var n as Count

	static function create() returns Self
		return Self{n: 5}
	end 'create'

	function make() returns Count
		return self.n
	end 'make'
end 'Other'

type Bag uses T
	typealias TArray = Array with T
	export var items as TArray

	static function make() returns Self
		return Self{items: TArray.create()}
	end 'make'

	function viaOther(Bag Other) returns Count
		return Bag.make()
	end 'viaOther'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	let b = StrBag.make()
	let o = Other.create()
	print("{b.viaOther(o)} {b.items.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 0
```

<!-- test: string-argument-outlives-its-source -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max) implements ElementIndex

type Holder uses Element
	typealias EArray = Array with Element
	export typealias Entry = (Element, Integer)

	var items = EArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(item Element)
		items.push(item)
	end 'add'

	function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias StringHolder = Holder with String

function pluck() returns (String, Integer)
	var h = StringHolder.create()
	h.add("the source is gone")
	return h.entryAt(0)
end 'pluck'

function main() returns ExitCode
	let e = pluck()
	if e.0.equals("the source is gone") 'kept'
		return 42
	end 'kept'
	return 1
end 'main'
```
```exitcode
42
```

### A trivial argument's retain word is 0, so the same body stores it raw

<!-- test: trivial-argument-takes-no-reference -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Index = int(0 to u64.max) implements ElementIndex
typealias SmallInt = int(0 to 100)

type Holder uses Element
	typealias EArray = Array with Element
	export typealias Entry = (Element, Integer)

	var items = EArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(item Element)
		items.push(item)
	end 'add'

	function entryAt(i Index) returns Entry
		let v = try items.get(i) otherwise panic("Holder.entryAt: out of range")
		return (v, 1)
	end 'entryAt'
end 'Holder'

typealias SmallHolder = Holder with SmallInt

function main() returns ExitCode
	var h = SmallHolder.create()
	h.add(40)
	h.add(2)

	let a = h.entryAt(0)
	let b = h.entryAt(1)
	return a.0 + b.0
end 'main'
```
```exitcode
42
```

### A generic with NO `T`-typed field still releases the reference its record took

The retain word is a fact about the type ARGUMENT and the release must be the same fact. A base that
declares no `Array with T` and no bare `T` — only an `Integer` — can still take a borrowed `T` into a tuple
it builds, and if that tuple is dropped HERE the reference has to go with it. Were `destroyFunc@40`
gated on the base's FIELD LIST instead, this exact shape would retain through a live `retainFunc@64` and
release through a zero: exit 101 with the right answer printed.

<!-- test: a-record-in-a-fieldless-generic-releases-what-it-took -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pack uses T
	var n as Integer

	static function create() returns Self
		return Self{n: 40}
	end 'create'

	function tag(t T) returns Integer
		let pair = (t, n)
		return pair.1
	end 'tag'
end 'Pack'

typealias StrPack = Pack with String

function main() returns ExitCode
	var p = StrPack.create()
	let s = "hello"
	return p.tag(s) + p.tag("literal")
end 'main'
```
```exitcode
80
```

### An interface type argument is REFUSED, not crashed on

An existential is a two-word fat pointer whose retain and release live in its witness, and a descriptor
word carries no witness — so `Box with Named` has no ownership protocol to name. The refusal is the
container-element rule's, already recorded by the time the destructor walk runs; the walk must therefore
contribute NOTHING for such an argument rather than route it into a one-argument drop router, which
replaced the diagnostic with a compiler stack trace.

<!-- test: error.an-interface-type-argument-is-refused -->
```maxon
typealias Integer = int(i64.min to i64.max)

interface Named
	function label() returns String
end 'Named'

type Thing implements Named
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function label() returns String
		return "thing"
	end 'label'
end 'Thing'

type Box uses T
	var value as T

	static function create(value T) returns Self
		return Self{value: value}
	end 'create'

	function get() returns T
		return value
	end 'get'
end 'Box'

typealias NamedBox = Box with Named

function main() returns ExitCode
	let b = NamedBox.create(Thing.create(3))
	return b.get().label().length()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:32:31: Unsupported: a container's element type declared at the interface type 'Named' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and an element slot is one machine word. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

### A container of records written over the enclosing parameter

A record built by a sibling generic's constructor over the enclosing type's own parameter —
`EBox.create(x, tag: tag)`, where `EBox = Box with Element` — can be put into a container and read back.
The container releases each element through the enclosing instance's layout descriptor, which knows the
element's per-instantiation destructor, so the stored `String` stays alive while the container holds it and
is released with it.

<!-- test: a-container-of-records-over-the-enclosing-parameter-is-created -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9
	end 'e'
	print(got)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and the CONCRETE instantiation of the same constructor, which always worked

The byte-identical program with `Box with String` written at top level and a NON-generic `Bag` holding
`Array with StrBox`. Same `Box uses T`, same `Box.create` body; only the INSTANTIATION differs. Here
`argIsConsumedAt` sees a concrete `String` argument, consumes it and hands `Box.create` a `+1` outright.
It answers what the generic spelling above answers.

<!-- test: the-concrete-spelling-of-the-same-constructor-feed -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

typealias StrBox = Box with String

type Bag
	typealias BoxArray = Array with StrBox
	var items = BoxArray.create()

	static function create() returns Self
		return Self{}
	end 'create'

	function add(x String, tag Integer)
		items.push(StrBox.create(x, tag: tag))
	end 'add'

	function first() returns String throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'
end 'Bag'

function fill(b Bag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = Bag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9
	end 'e'
	print(got)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and a record two levels over the parameter, stored in a field

Here `Box` holds an `Inner with T`, and `Inner` holds the bare parameter, while `Bag` spells `Box` over its own
`Element`. `Box`'s record is freed by a shared body, and the descriptor that body is handed carries the nested
view's per-instantiation destructor, so the stored `inner` field is released at every instantiation.

<!-- test: a-nested-instance-two-levels-over-the-enclosing-parameter-is-stored-and-read -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses U
	let x as U

	function value() returns U
		return x
	end 'value'

	static function create(x U) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Box uses T
	typealias TInner = Inner with T
	let inner as TInner
	let tag as Integer

	function value() returns T
		return inner.value()
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{inner: TInner.create(x), tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	b.add(s, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9
	end 'e'
	print(got)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and the TRIVIAL instantiation of that two-level shape still runs

Byte-identical to the program above with `String` replaced by a ranged `int`: `Inner`'s bare `U` owns nothing at run time under every `with`
the program writes, so the column's `__mm_decref` IS the correct element destructor and the program is whole.

<!-- test: a-trivial-instantiation-of-the-two-level-shape-still-runs -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)

type Inner uses U
	let x as U

	function value() returns U
		return x
	end 'value'

	static function create(x U) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Box uses T
	typealias TInner = Inner with T
	let inner as TInner
	let tag as Integer

	function value() returns T
		return inner.value()
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{inner: TInner.create(x), tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias SmallBag = Bag with Small

function fill(b SmallBag)
	b.add(41, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = SmallBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9
	end 'e'
	return got
end 'main'
```
```exitcode
41
```

### A hundred borrowed stores into the PARAMETER'S OWN container balance

The refusal above tells the author to hold the values in `Array with <type parameter>` instead, and a
refusal's message is a claim the corpus has to check. This is that program: a hundred trips storing the same
borrowed `String` into the enclosing parameter's own container, whose element destructor IS carried by the
enclosing instance's layout descriptor (`destroyFunc@40`) — so a hundred references are taken through
`retainFunc@64` and a hundred are released.

⚠ **THE SOURCE DIES BEFORE THE READ, WHICH IS THE WHOLE DISCRIMINATOR.** `load`'s heap `String` is released
at its scope exit while the bag lives on in `main`; a store that kept a raw borrow would fault on the read.
And the direction no exit code can see is the other one: an over-retain leaks and the gate reports **101**,
which a single store could not tell from a rounding error and a hundred can.

<!-- test: a-hundred-borrowed-stores-into-the-parameters-own-container-balance -->
```maxon
typealias Idx = int(0 to u64.max) implements ElementIndex

type Bag uses Element
	typealias Items = Array with Element
	var items as Items

	function fill(x Element, times Idx)
		var n = 0 as Idx
		while n < times 'fill'
			items.push(x)
			n = n + 1
		end 'fill'
	end 'fill'

	function at(i Idx) returns Element throws ArrayError
		return try self.items.get(i)
	end 'at'

	static function create() returns Self
		return Self{items: Items{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function load(b StrBag)
	var sb = StringBuilder.create()
	sb.append("a repeated borrowed string long enough to force a heap allocation")
	let s = sb.build()
	b.fill(s, times: 100)
end 'load'

function main() returns ExitCode
	var b = StrBag.create()
	load(b)
	let middle = try b.at(50) otherwise return 1
	print("{middle}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a repeated borrowed string long enough to force a heap allocation
```

### A TRIVIAL instantiation forwards through the same path and takes nothing

**The false-reject control for the refusal above, and it is load-bearing.** This is the byte-identical
program with `String` replaced by a ranged `int`: the same `EBox.create(x, tag: tag)` forward into the same
`Array with EBox`. Every instantiation of `Bag` here makes `Element` a scalar, so the box's bare `T` field
owns nothing at run time, the column's `__mm_decref` IS the correct element destructor, and the program is
whole. The refusal therefore asks the INSTANTIATION and not merely the shape — a rule that refused every
container of records over the enclosing parameter would reject this one, which owes nothing to anybody.

<!-- test: a-trivial-instantiation-forwards-and-takes-nothing -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias BoxArray = Array with EBox
	var items as BoxArray

	function add(x Element, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element throws ArrayError
		let slot = try self.items.first() otherwise 'e'
			throw ArrayError.indexOutOfBounds
		end 'e'
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: BoxArray{}}
	end 'create'
end 'Bag'

typealias IntBag = Bag with Integer

function fill(b IntBag)
	b.add(42, tag: 7)
end 'fill'

function main() returns ExitCode
	var b = IntBag.create()
	fill(b)
	let got = try b.first() otherwise 'e'
		return 9
	end 'e'
	return got as ExitCode
end 'main'
```
```exitcode
42
```

### The same record kept in a `List` node

`List` is a declared generic, so its chain is built inside `List`'s own shared body, and `Bag` feeds each record
to it through an ordinary `append` call. The record built over the enclosing parameter is kept by the list and
read back with the value it was built with.

<!-- test: a-record-over-the-enclosing-parameter-fed-to-a-list-is-kept -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = List with EBox
	var items as Store

	function add(x Element, tag Integer)
		items.append(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element
		let slot = try items.first() otherwise panic("empty")
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store.create()}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	b.add(sb.build(), tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	print("{b.first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and the same `List` shape at a TRIVIAL instantiation still runs

Byte-identical to the `List` program above but for `String` → a ranged `int`: the box's
bare `T` slot owns nothing at run time, so the chain's `element_drop@24` is right as it stands and the
program is whole.

<!-- test: a-trivial-instantiation-of-the-list-shape-still-runs -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 100)

type Box uses T
	export let x as T
	export let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = List with EBox
	var items as Store

	function add(x Element, tag Integer)
		items.append(EBox.create(x, tag: tag))
	end 'add'

	function first() returns Element
		let slot = try items.first() otherwise panic("empty")
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store.create()}
	end 'create'
end 'Bag'

typealias SmallBag = Bag with Small

function main() returns ExitCode
	var b = SmallBag.create()
	b.add(41, tag: 7)
	return b.first()
end 'main'
```
```exitcode
41
```

### The same record with NO CONTAINER ANYWHERE — a plain field, and it ROUND-TRIPS

⭐⭐ **A RECORD AT REST IN A PLAIN FIELD.** `var one as EBox` filled by
`Self{one: EBox.create(x, tag: tag)}`: no container is created, no element is pushed, and the record comes
to rest in a slot that outlives the borrow. It needs two halves, and they are different halves
in different places:

* the **reference** is taken at the constructor feed, because the `x` handed to `EBox.create` is a borrowed
  opaque `T` and a body compiled once takes its reference through the descriptor's `retainFunc@64`;
* the **release** is `__destruct_Bag_String`'s substituted cascade reaching `__destruct_Box_String`, which
  it can do — the enclosing instantiation is CONCRETE wherever the bag is freed, so
  nothing here needs the dictionary destructor at all.

The `String` is built in `fill`, whose `StringBuilder` result dies at that frame's exit,
so a missing reference is a read of freed memory and a surplus one is a leak the gate exits 101 on.

<!-- test: a-record-over-the-enclosing-parameter-in-a-plain-field-round-trips -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T
	export let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	let one as EBox

	function first() returns Element
		return one.value()
	end 'first'

	static function create(x Element, tag Integer) returns Self
		return Self{one: EBox.create(x, tag: tag)}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill() returns StrBag
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	let s = sb.build()
	return StrBag.create(s, tag: 7)
end 'fill'

function main() returns ExitCode
	let b = fill()
	print("{b.first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

### …and a record this frame built dies in the frame, which is no arrival at all

The other side of PROVENANCE, and the case that keeps the refusal from being a rule about the TYPE. The
identical `EBox.create(<a borrowed opaque T>)` builds a record whose slot holds a borrow nobody referenced —
and it is a LOCAL that dies before the borrow's owner does, so nothing releases the slot and nothing reads it
afterwards, and the program exits 0. A refusal that fired here would be refusing a program that is whole.

<!-- test: a-record-built-from-a-borrow-may-die-in-the-frame -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max) implements ElementIndex

type Box uses T
	export let x as T
	export let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Items = Array with Element
	var items as Items

	function add(x Element)
		items.push(x)
	end 'add'

	function tagOfFirst() returns Integer
		let w = EBox.create(try items.get(0 as Idx) otherwise panic("oob"), tag: 7)
		return w.tag
	end 'tagOfFirst'

	static function create() returns Self
		return Self{items: Items{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function main() returns ExitCode
	var b = StrBag.create()
	var sb = StringBuilder.create()
	sb.append("a heap payload the record only borrows")
	b.add(sb.build())
	return b.tagOfFirst() as ExitCode
end 'main'
```
```exitcode
7
```

### …and the third cure the message names: give the inner type a CONCRETE field

A refusal's advice is a claim the corpus has to check, and this file already checks the other two — the
parameter's own container (`a-hundred-borrowed-stores-into-the-parameters-own-container-balance`) and a
concrete instantiation (`the-concrete-spelling-of-the-same-constructor-feed`). This is the third: the same
`Bag with String` holding the same `Array with EBox`, where `Box`'s payload slot is declared `String` rather
than at `Box`'s own parameter. The column's element destructor is then a fact the shared body CAN name, the
store is an ordinary concrete move-in, and the heap payload outlives the helper that made it.

⚠ **THE `Box` IS GENERIC AND `EBox` IS AN INSTANCE OVER THE ENCLOSING PARAMETER HERE TOO** — what differs
is only that no slot of it stands at a parameter this body cannot name. That is exactly the boundary the
refusal reads, so this case is what shows the boundary is the SLOT and not the instantiation.

<!-- test: an-inner-alias-whose-slots-are-all-concrete-is-admitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as String
	export let tag as Integer

	function value() returns String
		return x
	end 'value'

	static function create(x String, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	typealias EBox = Box with Element
	typealias Store = Array with EBox
	var items as Store

	function add(x String, tag Integer)
		items.push(EBox.create(x, tag: tag))
	end 'add'

	function first() returns String throws ArrayError
		let slot = try self.items.first()
		return slot.value()
	end 'first'

	static function create() returns Self
		return Self{items: Store{}}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String

function fill(b StrBag)
	var sb = StringBuilder.create()
	sb.append("hello ")
	sb.append("heap world")
	b.add(sb.build(), tag: 7)
end 'fill'

function main() returns ExitCode
	var b = StrBag.create()
	fill(b)
	let got = try b.first() otherwise return 9
	print("{got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello heap world
```

## A shared body RETURNS the record it built out of a borrow

⭐⭐⭐ **THE SHAPE THE WHOLE MECHANISM EXISTS FOR.**
`Bag.wrap(x Element) returns EBox` builds a `Box with Element` out of a borrowed opaque `Element` and hands
it back. Every other arrival in this file is a value coming to REST; this one leaves the frame entirely, and
the caller that receives it is CONCRETE — `makeOne` holds a `Box with String`, drops it through
`__destruct_Box_String`, and that destructor releases the payload.

⛔⛔ **THE CONCRETE DESTRUCTOR RELEASES THE PAYLOAD, SO THE CONSTRUCTOR FEED MUST TAKE THE REFERENCE.**
`makeOne`'s `StringBuilder` result dies at that frame's exit, so without that reference the box would hold a
pointer into freed memory, `main`'s read of it would fault, and the concrete destructor would release a
record nobody had referenced. The constructor feed takes the reference through the descriptor's `retainFunc@64`
(`Parser.referenceOrMarkOpaqueFeed`), and the pair balances.

⚠ **A `static` SPELLING OF `wrap` IS ADMITTED TOO.** One that returns the enclosing type takes the descriptor
off the instance it builds (see `a-record-over-the-enclosing-parameter-in-a-plain-field-round-trips`, whose
feeding `create` is exactly that), and any other static takes it from the instance its call names.

<!-- test: a-returned-record-outlives-the-borrows-source -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	let x as T
	let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	var seed as Integer

	function wrap(x Element) returns EBox
		return EBox.create(x, tag: seed)
	end 'wrap'

	static function create() returns Self
		return Self{seed: 3}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String
typealias StrBox = Box with String

function makeOne(b StrBag) returns StrBox
	var sb = StringBuilder.create()
	sb.append("a heap payload ")
	sb.append("long enough to allocate")
	let s = sb.build()
	return b.wrap(s)
end 'makeOne'

function main() returns ExitCode
	var b = StrBag.create()
	let boxed = makeOne(b)
	print("{boxed.value()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a heap payload long enough to allocate
```

### A hundred of them, kept in a concrete column, and the arithmetic balances

The exit code says a program did not FAULT; only the leak gate says it took as many references as it gave
back. A hundred trips, each building a fresh heap `String` that dies inside the loop body, each wrapped and
pushed into an `Array with StrBox` whose element destructor is the concrete `__destruct_Box_String` — then
the whole column is destroyed at scope exit. One retain per trip against one release per trip: a missing
release is **exit 101** and a surplus one frees a `String` a live box still holds.

⚠ The column's element is the CONCRETE `Box with String`, not the declaration view, so its destructor is the
symbol `__destruct_Box_String`, stamped directly as the element destructor.

<!-- test: a-hundred-returned-records-balance -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Count = int(0 to u32.max)

type Box uses T
	let x as T
	let tag as Integer

	function value() returns T
		return x
	end 'value'

	static function create(x T, tag Integer) returns Self
		return Self{x: x, tag: tag}
	end 'create'
end 'Box'

type Bag uses Element
	export typealias EBox = Box with Element
	var seed as Integer

	function wrap(x Element) returns EBox
		return EBox.create(x, tag: seed)
	end 'wrap'

	static function create() returns Self
		return Self{seed: 3}
	end 'create'
end 'Bag'

typealias StrBag = Bag with String
typealias StrBox = Box with String
typealias BoxArray = Array with StrBox

function main() returns ExitCode
	var b = StrBag.create()
	var kept = BoxArray.create()
	var i = 0 as Count
	while i < 100 'fill'
		var sb = StringBuilder.create()
		sb.append("payload number ")
		sb.append("{i} long enough to be a heap record")
		kept.push(b.wrap(sb.build()))
		i = i + 1
	end 'fill'
	print("{kept.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
100
```

<!-- test: a-nested-view-over-the-outer-parameters-takes-and-releases-its-reference -->
### A nested declaration view over the outer type's parameters takes and releases its reference
`Mid` holds an `Inner2 with (Y, Z)` and is itself spelled by `Outer` as `Mid with (T, U)`. `Outer with (String,
String)` makes those parameters managed, so the constructor feed inside `Mid.create` takes a reference through
the descriptor, and the dictionary destructor that frees the record releases it: the program balances.

⚠ `m` is read for its scalar `tag` so the binding is used (`E3012`). The read is a plain field load: it
neither retains nor releases the opaque slot, so the record is still BUILT in a shared body that carries a
descriptor and DROPPED in that frame, which routes the drop through `instanceBoxDropCallee`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner2 uses P, Q
	let p as P
	let q as Q

	static function create(p P, q Q) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Inner2'

type Mid uses Y, Z
	typealias In = Inner2 with (Y, Z)
	let i as In
	export let tag as Integer

	static function create(y Y, z Z) returns Self
		return Self{i: In.create(y, q: z), tag: 2}
	end 'create'
end 'Mid'

type Outer uses T, U
	typealias M = Mid with (T, U)
	var n as Integer

	function build(t T, u U) returns Integer
		let m = M.create(t, z: u)
		return n + m.tag
	end 'build'

	static function create() returns Self
		return Self{n: 11}
	end 'create'
end 'Outer'

typealias O = Outer with (String, String)

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var o = O.create()
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	print("{o.build(s1, u: s2)}\n")
	print(s1)
	print("\n")
	print(s2)
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
13
first payload long enough to allocate
second payload long enough to allocate
```

<!-- test: a-nested-view-fed-a-referenced-borrow-into-a-field-releases-it -->
### …and the same shape WITH a managed instantiation of the middle type
Adding `Mid with (String, String)` makes `Mid`'s own parameter managed as well, so the constructor feed inside
`Mid.create` takes a descriptor-mediated reference and the record that holds it rests in a field of `Mid`, a
declaration view. The shared body that frees it is handed a descriptor that carries the nested view's
per-instantiation destructor, so the field is released at each instantiation.

⚠ `m` is read for its scalar `tag` so the binding is used (`E3012`). The read is a plain field load: it
neither retains nor releases the opaque slot, so the record is still BUILT in a shared body that carries a
descriptor and DROPPED in that frame, which routes the drop through `instanceBoxDropCallee`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner2 uses P, Q
	let p as P
	let q as Q

	static function create(p P, q Q) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Inner2'

type Mid uses Y, Z
	typealias In = Inner2 with (Y, Z)
	let i as In
	export let tag as Integer

	static function create(y Y, z Z) returns Self
		return Self{i: In.create(y, q: z), tag: 2}
	end 'create'
end 'Mid'

type Outer uses T, U
	typealias M = Mid with (T, U)
	var n as Integer

	function build(t T, u U) returns Integer
		let m = M.create(t, z: u)
		return n + m.tag
	end 'build'

	static function create() returns Self
		return Self{n: 11}
	end 'create'
end 'Outer'

typealias O = Outer with (String, String)
typealias MS = Mid with (String, String)

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var o = O.create()
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	print("{o.build(s1, u: s2)}\n")
	let extra = MS.create(heapString("x ", b: "yyyyyyyyyyyyyyyyyyyyyy"), z: heapString("z ", b: "wwwwwwwwwwwwwwwwwwwwww"))
	print("extra {extra.tag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
13
extra 2
```

<!-- test: a-closure-between-the-feed-and-the-store-still-stores-correctly -->
The same store when a closure literal sits between the constructor feed and the store. The closure is parsed
as a function of its own; the store after it is judged on the record's real provenance, and the program
balances.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner2 uses P, Q
	let p as P
	let q as Q

	static function create(p P, q Q) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'Inner2'

type Mid uses Y, Z
	typealias In = Inner2 with (Y, Z)
	let i as In
	export let tag as Integer

	static function create(y Y, z Z) returns Self
		let inner = In.create(y, q: z)
		let bump = function(n Integer) gives n + 1
		return Self{i: inner, tag: bump(1)}
	end 'create'
end 'Mid'

type Outer uses T, U
	typealias M = Mid with (T, U)
	var n as Integer

	function build(t T, u U) returns Integer
		let m = M.create(t, z: u)
		return n + m.tag
	end 'build'

	static function create() returns Self
		return Self{n: 11}
	end 'create'
end 'Outer'

typealias O = Outer with (String, String)
typealias MS = Mid with (String, String)

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var o = O.create()
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	print("{o.build(s1, u: s2)}\n")
	let extra = MS.create(heapString("x ", b: "yyyyyyyyyyyyyyyyyyyyyy"), z: heapString("z ", b: "wwwwwwwwwwwwwwwwwwwwww"))
	print("extra {extra.tag}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
13
extra 2
```

<!-- test: a-nested-view-another-file-reaches-stores-holder-first -->
The same store when the nested declaration view is reached through ANOTHER file: `Holder` stores a `Cell`
over its own parameter, and `Outer` holds a `Holder` over `Outer`'s. The program is the same whichever file is
parsed first.
```maxon
// --- file: a_holder.maxon
export typealias Int = int(0 to 1000)

type Cell uses T
	export var value as T
	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Cell'

type Holder uses T
	var cell as Cell
	static function create(value T) returns Self
		return Self{cell: Cell.create(value)}
	end 'create'
	function replace(next Cell)
		self.cell = next
	end 'replace'
end 'Holder'

export type Outer uses T
	var holder as Holder
	export static function create(value T) returns Self
		return Self{holder: Holder.create(value)}
	end 'create'
	export function tag() returns Int
		return 3
	end 'tag'
end 'Outer'


// --- file: b_use.maxon
typealias TextOuter = Outer with String

function main() returns ExitCode
	let o = TextOuter.create("x")
	return o.tag()
end 'main'
```
```exitcode
3
```

<!-- test: a-nested-view-another-file-reaches-stores-use-first -->
```maxon
// --- file: a_use.maxon
typealias TextOuter = Outer with String

function main() returns ExitCode
	let o = TextOuter.create("x")
	return o.tag()
end 'main'


// --- file: b_holder.maxon
export typealias Int = int(0 to 1000)

type Cell uses T
	export var value as T
	static function create(value T) returns Self
		return Self{value: value}
	end 'create'
end 'Cell'

type Holder uses T
	var cell as Cell
	static function create(value T) returns Self
		return Self{cell: Cell.create(value)}
	end 'create'
	function replace(next Cell)
		self.cell = next
	end 'replace'
end 'Holder'

export type Outer uses T
	var holder as Holder
	export static function create(value T) returns Self
		return Self{holder: Holder.create(value)}
	end 'create'
	export function tag() returns Int
		return 3
	end 'tag'
end 'Outer'
```
```exitcode
3
```

<!-- test: a-tuple-field-resting-a-nested-view-is-released-on-reassignment -->
A tuple field that rests a nested declaration view is released when the record holding it is replaced.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T

	static function create(x T) returns Self
		return Self{x: x}
	end 'create'
end 'Box'

type Bag uses E
	typealias EBox = Box with E
	typealias EPair = (EBox, Integer)
	export let pair as EPair

	static function create(x E) returns Self
		return Self{pair: (EBox.create(x), 1)}
	end 'create'
end 'Bag'

type Outer uses T
	typealias TBag = Bag with T
	var bag as TBag

	static function create(x T) returns Self
		return Self{bag: TBag.create(x)}
	end 'create'

	function reset(x T)
		self.bag = TBag.create(x)
	end 'reset'

	function first() returns T
		return self.bag.pair.0.x
	end 'first'
end 'Outer'

typealias StrOuter = Outer with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	var o = StrOuter.create(s1)
	o.reset(s2)
	print("{o.first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate
```

<!-- test: a-tuple-field-over-the-parameter-in-a-nested-view-is-released-on-reassignment -->
A tuple field over the type parameter itself, in a nested declaration view, is released when the record holding it is replaced.
```maxon
typealias Integer = int(i64.min to i64.max)

type Bag uses E
	export let pair as (E, Integer)

	static function create(x E) returns Self
		return Self{pair: (x, 1)}
	end 'create'
end 'Bag'

type Outer uses T
	typealias TBag = Bag with T
	var bag as TBag

	static function create(x T) returns Self
		return Self{bag: TBag.create(x)}
	end 'create'

	function reset(x T)
		self.bag = TBag.create(x)
	end 'reset'

	function first() returns T
		return self.bag.pair.0
	end 'first'
end 'Outer'

typealias StrOuter = Outer with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	var o = StrOuter.create(s1)
	o.reset(s2)
	print("{o.first()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate
```

<!-- test: a-tuple-field-resting-a-nested-view-reassigned-in-its-own-type-is-released -->
The same tuple field reassigned inside its own type releases the tuple it displaces.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export let x as T

	static function create(x T) returns Self
		return Self{x: x}
	end 'create'
end 'Box'

type Bag uses E
	typealias EBox = Box with E
	typealias EPair = (EBox, Integer)
	export var pair as EPair

	static function create(x E) returns Self
		return Self{pair: (EBox.create(x), 1)}
	end 'create'

	function swap(p EPair)
		self.pair = p
	end 'swap'

	function first() returns E
		return self.pair.0.x
	end 'first'
end 'Bag'

typealias StrBag = Bag with String
typealias StrBox = Box with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let s1 = heapString("first payload ", b: "long enough to allocate")
	let s2 = heapString("second payload ", b: "long enough to allocate")
	var b = StrBag.create(s1)
	b.swap((StrBox.create(s2), 2))
	print("{b.first()} {b.pair.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate 2
```

### The record's RELEASE, in a body that is itself shared — a tuple of parameters

Every case above hands its record to a CONCRETE frame, and there the release is a symbol: the caller's
`(String, Integer)` has a `__destruct___Tuple2.String.Integer` that decrefs the first slot whoever filled
it. A frame that is ITSELF shared has no such symbol — the tuple it holds is typed `(K, V)`, both slots
stand at bare parameters, and `typeIsManaged` answers `false` for a parameter, so the drop router hands
back the trivial `__mm_decref`. That frees the box and STRANDS the reference `retainFunc@64` took when the
record was built.

The release is therefore the same dictionary cascade a declaration view takes —
`__destruct_dict_<tuple>(descriptor, box)` — reading `destroyFunc@40` out of the block of each slot's own
parameter, and running only at refcount zero, because a shared frame is not always the record's last owner.

⚠ **THE TWO ENDS MUST READ ONE DESCRIPTOR AND THE MINT READS NONE.** `__mm_alloc(size, 0)` says the box
owns nothing while the stores that follow it take two references; the disagreement is invisible until a
shared frame is the party that frees the box.

<!-- test: a-tuple-of-parameters-dropped-in-a-shared-body-releases-what-it-took -->
```maxon
typealias Small = int(0 to 1000)

type Holder uses K, V
	export typealias Pair = (K, V)
	let k as K
	let v as V

	static function create(k K, v V) returns Self
		return Self{k: k, v: v}
	end 'create'

	function pair() returns Pair
		return (k, v)
	end 'pair'

	// The drop under test: this body is shared, so the tuple it takes back is typed
	// at the parameters and the reference its first slot holds is a descriptor read.
	function second() returns V
		let p = pair()
		return p.1
	end 'second'
end 'Holder'

typealias StrHolder = Holder with String, Small

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let h = StrHolder.create(heapString("a heap key ", b: "long enough to allocate"), v: 7)
	print("second {h.second()}\n")
	return 4
end 'main'
```
```stdout
second 7
```
```exitcode
4
```

### …and the same shape with a SECOND instantiation of the same generic

The number of instantiations is the variable, because it decides whether the enclosing type is
`genericTypeIsInstantiated` — which decides whether the shared body reserves a layout descriptor at all,
and therefore whether the record's stores read a REAL protocol or the all-zero one a parameterised blob
carries. A cure that balances one of the two modes is silent about the other, so both are pinned.

<!-- test: a-tuple-of-parameters-dropped-in-a-shared-body-with-two-instantiations -->
```maxon
typealias Small = int(0 to 1000)

type Holder uses K, V
	export typealias Pair = (K, V)
	let k as K
	let v as V

	static function create(k K, v V) returns Self
		return Self{k: k, v: v}
	end 'create'

	function pair() returns Pair
		return (k, v)
	end 'pair'

	function second() returns V
		let p = pair()
		return p.1
	end 'second'
end 'Holder'

typealias StrHolder = Holder with String, Small
typealias SmallHolder = Holder with Small, Small

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let h = StrHolder.create(heapString("a heap key ", b: "long enough to allocate"), v: 7)
	let t = SmallHolder.create(5, v: 6)
	print("second {h.second()} {t.second()}\n")
	return 4
end 'main'
```
```stdout
second 7 6
```
```exitcode
4
```

### …and the CONCRETE caller of the same `pair()`, which always worked

The control. `main` is not generic, so the tuple it takes back is typed `(String, Small)` and its release
is the ordinary substituted cascade. The retain in `pair()` is the SAME retain in all three cases — one
shared body, one `__retain_type_param` — so a cure that removed it would redden this case rather than the
two above.

<!-- test: a-tuple-of-parameters-dropped-by-a-concrete-caller -->
```maxon
typealias Small = int(0 to 1000)

type Holder uses K, V
	export typealias Pair = (K, V)
	let k as K
	let v as V

	static function create(k K, v V) returns Self
		return Self{k: k, v: v}
	end 'create'

	function pair() returns Pair
		return (k, v)
	end 'pair'
end 'Holder'

typealias StrHolder = Holder with String, Small

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let h = StrHolder.create(heapString("a heap key ", b: "long enough to allocate"), v: 7)
	let p = h.pair()
	print("pair {p.0} {p.1}\n")
	return 4
end 'main'
```
```stdout
pair a heap key long enough to allocate 7
```
```exitcode
4
```

### The stdlib chain the same drop runs through — `Map`'s `Entry` tuple

`stdlib/Interfaces.maxon`'s `extension Iterable` gives `Map` a `map` whose per-trip element is
`MapIterator.current()`'s `(Key, Value)` — the tuple of parameters above, built by one shared body and
freed by another. The keys here are HEAP Strings (a `Map` copies a literal key into an owned record), so a
stranded reference is a real leak and the runner's exit code says so.

⚠ **A SECOND DECLARED INSTANTIATION IS PART OF THE CASE.** `typealias EnvMap = Map with String, String`
instantiates `Map` by a declaration beside the literal. `Map.map` carries a descriptor under both, because
its `ElementArray{}` is composed over `Map`'s parameters, and forwards it into the iterator chain.

<!-- test: mapping-a-map-releases-the-entry-tuples-it-built -->
```maxon
typealias EnvMap = Map with String, String
typealias Cnt = int(0 to 1000)

function envSize(e EnvMap) returns Cnt
	return e.count()
end 'envSize'

function main() returns ExitCode
	let m = ["a": 1, "b": 2, "c": 3]
	let mapped = m.map(function(p) gives p)
	var e = EnvMap.create()
	try e.insert("k", value: "v") otherwise return 9
	print("mapped {mapped.count()}\n")
	return ((mapped.count() as Cnt) + envSize(e)) as ExitCode
end 'main'
```
```stdout
mapped 3
```
```exitcode
4
```

<!-- test: a-closure-in-a-generic-method-handing-back-its-parameter-reserves-the-descriptor -->
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function touch() returns ExitCode
		let e = function(x T) gives x
		_ = e(self.v)
		return 7
	end 'touch'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	var seed = 0 as Count

	while seed < 3 'grow'
		seed = seed + 1
	end 'grow'

	let c = StrCell.make("kept through a closure number {seed} long enough to live on the heap")
	return c.touch()
end 'main'
```
```exitcode
7
```

<!-- test: an-unqualified-sibling-static-fed-a-borrowed-parameter-stores-it-through-the-call-sites-descriptor -->
A static calls its sibling static by its bare name and hands it the borrowed type-parameter parameter twice. The bare call is the same call as `DPair.create(v, b: v)`: the sibling stores both arguments, so the caller takes its references through the call site's descriptor.
```maxon
type DPair uses T
	export var a as T
	export var b as T

	static function create(a T, b T) returns Self
		return Self{a: a, b: b}
	end 'create'

	static function twice(v T) returns Self
		return create(v, b: v)
	end 'twice'
end 'DPair'

typealias StrPair = DPair with String

function main() returns ExitCode
	let p = StrPair.twice("dup {40 + 2} value long enough for a heap record")
	print("{p.a} {p.b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
dup 42 value long enough for a heap record dup 42 value long enough for a heap record
```

<!-- test: an-unqualified-sibling-static-fed-a-borrowed-record-stores-it-through-the-call-sites-descriptor -->
The same bare sibling call at a record instantiation, whose `String` field the shared body retains once per stored reference.
```maxon
typealias Small = int(0 to 1000)

type Entry
	export var name as String
	export var n as Small

	static function create(name String, n Small) returns Self
		return Self{name: name, n: n}
	end 'create'
end 'Entry'

type DPair uses T
	export var a as T
	export var b as T

	static function create(a T, b T) returns Self
		return Self{a: a, b: b}
	end 'create'

	static function twice(v T) returns Self
		return create(v, b: v)
	end 'twice'
end 'DPair'

typealias EntryPair = DPair with Entry

function main() returns ExitCode
	let p = EntryPair.twice(Entry.create("entry {40 + 2} value long enough for a heap record", n: 9))
	print("{p.a.name} {p.b.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
entry 42 value long enough for a heap record 9
```

<!-- test: an-unqualified-sibling-static-called-in-a-closure-of-a-static-stores-through-the-call-sites-descriptor -->
A closure written in a static calls the sibling static by its bare name with its own type-parameter parameter.
```maxon
type DPair uses T
	export var a as T
	export var b as T

	static function create(a T, b T) returns Self
		return Self{a: a, b: b}
	end 'create'

	static function viaClosure(v T) returns Self
		let build = function(x T) gives create(x, b: x)
		return build(v)
	end 'viaClosure'
end 'DPair'

typealias StrPair = DPair with String

function main() returns ExitCode
	let p = StrPair.viaClosure("dup {40 + 2} value long enough for a heap record")
	print("{p.a} {p.b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
dup 42 value long enough for a heap record dup 42 value long enough for a heap record
```

<!-- test: a-generic-type-hands-its-value-parameter-to-a-map-upsert -->
A generic method that hands its type-parameter value to a map's `upsert` hands the map ownership of it, so the caller's heap string is released once.
```maxon
typealias Count = int(0 to u64.max)

type Registry uses Value
	typealias ValueMap = Map with (String, Value)

	var entries as ValueMap

	static function create() returns Self
		return Self{entries: ValueMap.create()}
	end 'create'

	function put(key String, item Value)
		self.entries.upsert(key, value: item)
	end 'put'

	function count() returns Count
		return self.entries.count()
	end 'count'
end 'Registry'

typealias StringRegistry = Registry with String

function main() returns ExitCode
	var registry = StringRegistry.create()
	registry.put("a", item: "hello {1 + 1}")
	registry.put("b", item: "world {2 + 2}")
	registry.put("a", item: "again {3 + 3}")

	if registry.count() == 2 'check'
		return 0
	end 'check'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-non-opaque-field-of-a-nested-instance-is-reassigned-and-the-displaced-value-released -->
A field typed as an instance over the type parameter (`Cell`), written through `self.inner` with the new value a parameter. The displaced cell and the string it holds must be released exactly once.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Cell'

type Slot uses T
	export typealias C = Cell with T
	export var c as C

	static function create(c C) returns Self
		return Self{c: c}
	end 'create'
end 'Slot'

type Wrapper uses T
	export typealias Inner = Slot with T
	export typealias WrapperCell = Cell with T
	var inner as Inner

	static function create(c WrapperCell) returns Self
		return Self{inner: Inner.create(c)}
	end 'create'

	function put(next WrapperCell)
		self.inner.c = next
	end 'put'
end 'Wrapper'

typealias StrCell = Cell with String
typealias StrWrapper = Wrapper with String

function main() returns ExitCode
	var w = StrWrapper.create(StrCell.make("first string long enough to force a heap allocation"))
	w.put(StrCell.make("second string, longer still, and also heap allocated"))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-opaque-field-assigned-a-borrowed-value-by-its-bare-name-is-served -->
The same assignment spelled by the bare field name: it reserves the layout descriptor the borrowed value's reference is taken through.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function takeFrom(item T)
		v = item
	end 'takeFrom'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrCell.create(heapString("first payload ", b: "long enough to allocate"))
	let s = heapString("second payload ", b: "long enough to allocate")
	a.takeFrom(s)
	print("{a.v} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate second payload long enough to allocate
```

<!-- test: an-opaque-field-assigned-by-its-bare-name-from-another-instances-field-is-shared -->
The same assignment spelled by the bare field name: the reassignment reserves the layout descriptor it drops the displaced value and takes the new reference through.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function takeFrom(other Self)
		v = other.v
	end 'takeFrom'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrCell.create(heapString("first payload ", b: "long enough to allocate"))
	let b = StrCell.create(heapString("second payload ", b: "long enough to allocate"))
	a.takeFrom(b)
	print("{a.v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate
```

<!-- test: an-opaque-field-of-a-nested-instance-is-assigned-a-borrowed-value -->
A type-parameter field of an instance held over the enclosing type's own parameter, written through `self.inner`.
```maxon
type Slot uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Slot'

type Wrapper uses T
	export typealias Inner = Slot with T
	var inner as Inner

	static function create(v T) returns Self
		return Self{inner: Inner.create(v)}
	end 'create'

	function put(item T)
		self.inner.v = item
	end 'put'

	function show() returns T
		return self.inner.v
	end 'show'
end 'Wrapper'

typealias StrWrapper = Wrapper with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var w = StrWrapper.create(heapString("first payload ", b: "long enough to allocate"))
	let s = heapString("second payload ", b: "long enough to allocate")
	w.put(s)
	print("{w.show()} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate second payload long enough to allocate
```

<!-- test: an-opaque-field-of-a-self-typed-parameter-is-assigned-a-borrowed-value -->
A type-parameter field written through a `Self`-typed parameter drops the displaced value and takes its own reference to the borrowed one, so the method owes a layout descriptor.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function overwrite(other Self, item T)
		other.v = item
	end 'overwrite'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrCell.create(heapString("first payload ", b: "long enough to allocate"))
	var b = StrCell.create(heapString("second payload ", b: "long enough to allocate"))
	let s = heapString("third payload ", b: "long enough to allocate")
	a.overwrite(b, item: s)
	print("{b.v} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
third payload long enough to allocate third payload long enough to allocate
```

<!-- test: an-opaque-field-of-a-self-typed-parameters-nested-instance-is-assigned -->
The same write through the nested field of a `Self`-typed parameter.
```maxon
type Slot uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Slot'

type Wrapper uses T
	export typealias Inner = Slot with T
	var inner as Inner

	static function create(v T) returns Self
		return Self{inner: Inner.create(v)}
	end 'create'

	function putInto(other Self, item T)
		other.inner.v = item
	end 'putInto'

	function show() returns T
		return self.inner.v
	end 'show'
end 'Wrapper'

typealias StrWrapper = Wrapper with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var w = StrWrapper.create(heapString("first payload ", b: "long enough to allocate"))
	var o = StrWrapper.create(heapString("second payload ", b: "long enough to allocate"))
	let s = heapString("third payload ", b: "long enough to allocate")
	w.putInto(o, item: s)
	print("{o.show()} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
third payload long enough to allocate third payload long enough to allocate
```

<!-- test: an-opaque-field-of-an-instance-over-the-second-of-two-parameters-is-assigned-a-borrowed-value -->
`Slot with U` inside `Wrapper uses T, U` stores through the block of `U`, so the displaced and the new `String` are released and retained through `String`'s protocol, and the first parameter is a trivial type.
```maxon
typealias Small = int(0 to 255)

type Slot uses V
	export var v as V

	static function create(first V) returns Self
		return Self{v: first}
	end 'create'
end 'Slot'

type Wrapper uses T, U
	export typealias Inner = Slot with U
	var tag as T
	var inner as Inner

	static function create(t T, u U) returns Self
		return Self{tag: t, inner: Inner.create(u)}
	end 'create'

	function peekValue() returns U
		return self.inner.v
	end 'peekValue'

	function put(item U)
		self.inner.v = item
	end 'put'

	function putInto(other Self, item U)
		other.inner.v = item
	end 'putInto'
end 'Wrapper'

typealias SmallStrWrapper = Wrapper with (Small, String)

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var w = SmallStrWrapper.create(3, u: heapString("first payload ", b: "long enough to allocate"))
	var o = SmallStrWrapper.create(4, u: heapString("other payload ", b: "long enough to allocate"))
	let s = heapString("second payload ", b: "long enough to allocate")
	let t = heapString("third payload ", b: "long enough to allocate")
	w.put(s)
	w.putInto(o, item: t)
	print("{w.peekValue()} {o.peekValue()} {s} {t}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate third payload long enough to allocate second payload long enough to allocate third payload long enough to allocate
```

<!-- test: an-opaque-field-two-nested-instances-deep-is-assigned-a-borrowed-value -->
The same write through a field of a field, both holding an instance over the enclosing type's own parameter.
```maxon
type Slot uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Slot'

type Middle uses T
	export typealias Inner = Slot with T
	export var b as Inner

	static function create(v T) returns Self
		return Self{b: Inner.create(v)}
	end 'create'
end 'Middle'

type Outer uses T
	export typealias Mid = Middle with T
	var a as Mid

	static function create(v T) returns Self
		return Self{a: Mid.create(v)}
	end 'create'

	function put(item T)
		self.a.b.v = item
	end 'put'

	function show() returns T
		return self.a.b.v
	end 'show'
end 'Outer'

typealias StrOuter = Outer with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var o = StrOuter.create(heapString("first payload ", b: "long enough to allocate"))
	let s = heapString("second payload ", b: "long enough to allocate")
	o.put(s)
	print("{o.show()} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate second payload long enough to allocate
```

<!-- test: an-instance-field-of-a-nested-instance-is-reassigned-in-a-static-through-the-call-sites-descriptor -->
The same write inside a `static function`, which releases the displaced cell through the descriptor its call hands it.
```maxon
type Cell uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Cell'

type Slot uses T
	export var cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Slot'

type Wrapper uses T
	export typealias Inner = Slot with T
	export var inner as Inner

	static function create(cell Cell) returns Self
		return Self{inner: Inner.create(cell)}
	end 'create'

	static function put(target Self, next Cell)
		target.inner.cell = next
	end 'put'
end 'Wrapper'

typealias StrCell = Cell with String
typealias StrWrapper = Wrapper with String

function main() returns ExitCode
	var w = StrWrapper.create(StrCell.make("first string long enough to force a heap allocation"))
	StrWrapper.put(w, next: StrCell.make("second string, longer still, and also heap allocated"))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-opaque-field-assigned-a-borrowed-value-in-a-static-takes-the-call-sites-descriptor -->
The same assignment through a `Self`-typed parameter of a `static function`, which takes a reference through the descriptor its call hands it.
```maxon
type Cell uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function overwrite(target Self, item T)
		target.v = item
	end 'overwrite'
end 'Cell'

typealias StrCell = Cell with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var a = StrCell.create(heapString("first payload ", b: "long enough to allocate"))
	let s = heapString("second payload ", b: "long enough to allocate")
	StrCell.overwrite(a, item: s)
	print("{a.v} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second payload long enough to allocate second payload long enough to allocate
```

<!-- test: an-owned-parameter-value-is-released-in-a-method-reached-through-a-call-on-a-result -->
`self.make().stash()` calls a method that builds an `Array with T` of borrowed `T` values and releases them at its scope exit. The receiver of `stash` is a call's result, so the release goes through the descriptor the caller forwards from its own.
```maxon
type Inner uses T
	export typealias Items = Array with T
	var v as T

	static function create(x T) returns Self
		return Self{v: x}
	end 'create'

	function stash() returns Count
		var items = Items.create()
		items.push(self.v)
		return items.count()
	end 'stash'
end 'Inner'

type Outer uses T
	typealias Held = Inner with T
	var held as Held

	static function create(x T) returns Self
		return Self{held: Held.create(x)}
	end 'create'

	function make() returns Held
		return held
	end 'make'

	function probe() returns Count
		return self.make().stash()
	end 'probe'
end 'Outer'

typealias StrOuter = Outer with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let o = StrOuter.create(heapString("first payload ", b: "long enough to allocate"))
	print("{o.probe()} {o.probe()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: a-static-not-returning-self-pushes-a-borrowed-parameter-through-a-typealias-instance -->
A `static function` that does not return `Self` pushes a borrowed `T` into an `Array with T`. It takes the descriptor from the call that names the instance through its typealias, so the array holds a reference of its own.
```maxon
type Pile uses T
	export typealias Items = Array with T
	var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	static function stash(items Items, item T)
		items.push(item)
	end 'stash'
end 'Pile'

typealias StrPile = Pile with String
typealias StrItems = Array with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	var items = StrItems.create()
	let s = heapString("pushed payload ", b: "long enough to allocate")
	StrPile.stash(items, item: s)
	StrPile.stash(items, item: s)
	let first = try items.get(0) otherwise return 90
	print("{items.count()} {first} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 pushed payload long enough to allocate pushed payload long enough to allocate
```

<!-- test: a-static-pushing-a-self-typed-parameters-field-into-a-local-array-takes-the-call-sites-descriptor -->
A `static function` that does not return `Self` reads a type-parameter field of a `Self`-typed parameter and pushes it into a local `Array with T`. The local array releases the reference through the descriptor the call hands the static.
```maxon
type Pile uses T
	export typealias Items = Array with T
	export var top as T

	static function create(top T) returns Self
		return Self{top: top}
	end 'create'

	static function tally(owner Self) returns Count
		var items = Items.create()
		items.push(owner.top)
		items.push(owner.top)
		return items.count()
	end 'tally'
end 'Pile'

typealias StrPile = Pile with String

function heapString(a String, b String) returns String
	var sb = StringBuilder.create()
	sb.append(a)
	sb.append(b)
	return sb.build()
end 'heapString'

function main() returns ExitCode
	let p = StrPile.create(heapString("top payload ", b: "long enough to allocate"))
	print("{StrPile.tally(p)} {p.top}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 top payload long enough to allocate
```

<!-- test: a-closure-in-main-returns-the-parameter-a-generic-static-picks-over-a-scalar-instance -->
A closure written in a plain function calls an equality-guarded static of a generic type through its instance alias, and the static hands back one of its parameters.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let xs = [1, 2]
	let ys = xs.map(function(n) gives IntHolder.pick(n, b: 1))
	print("{try ys.get(0) otherwise 0} {try ys.get(1) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-closure-in-main-returns-the-parameter-a-generic-static-picks-over-a-string-instance -->
The same closure over a managed type argument hands back a heap string, and every string is released exactly once.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	let xs = ["first {40 + 2} value long enough for a heap record", "second {40 + 3} value long enough for a heap record"]
	let ys = xs.map(function(s) gives StrHolder.pick(s, b: "first {40 + 2} value long enough for a heap record"))
	print("{try ys.get(0) otherwise ""}|{try ys.get(1) otherwise ""}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first 42 value long enough for a heap record|second 43 value long enough for a heap record
```

<!-- test: a-plain-function-returns-the-parameter-a-generic-static-picks-over-a-scalar-instance -->
A function with scalar parameters and a scalar result returns what an equality-guarded static of a generic type picks.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function viaFree(n Integer) returns Integer
	return IntHolder.pick(n, b: 1)
end 'viaFree'

function main() returns ExitCode
	print("{viaFree(1)} {viaFree(2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-static-returning-the-type-parameter-through-an-instance-alias-is-a-value-of-the-argument -->
The result of such a call outside any closure is a `String` where the alias fixes `T` to one: it interpolates, and its heap string is released once.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	let r = StrHolder.pick("pick {40 + 2} value long enough for a heap record", b: "other")
	print("{r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pick 42 value long enough for a heap record
```

<!-- test: a-scalar-result-of-a-static-through-an-instance-alias-is-read-bound-interpolated-and-dropped -->
A scalar result of a static through an instance alias owns nothing, so a bound one, one interpolated into a string and one that is only a temporary of the statement all release nothing.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let r = IntHolder.pick(2, b: 1)
	print("{r} {IntHolder.pick(1, b: 2)}\n")
	print("{IntHolder.pick(1, b: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1
1
```

<!-- test: a-heap-result-of-a-static-through-an-instance-alias-is-read-bound-interpolated-and-dropped -->
A heap result of the same static is released once however it is used.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	let first = "first {40 + 2} value long enough for a heap record"
	let second = "second {40 + 3} value long enough for a heap record"
	let r = StrHolder.pick(first, b: second)
	print("{r} {StrHolder.pick(second, b: first)}\n")
	print("{StrHolder.pick(first, b: first)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first 42 value long enough for a heap record second 43 value long enough for a heap record
first 42 value long enough for a heap record
```

<!-- test: an-overloaded-bare-static-infers-its-instance-from-the-declaration-the-call-binds -->
Two statics of one name, and the call binds the one whose parameters its arguments fit: the instance is inferred from that declaration, so the result is an `Integer` in a closure and outside it.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'

	static function pick(a T) returns T
		return a
	end 'pick'
end 'Holder'

function main() returns ExitCode
	let xs = [1, 2]
	let ys = xs.map(function(n) gives Holder.pick(n, b: 1))
	print("{try ys.get(0) otherwise 0} {try ys.get(1) otherwise 0}\n")
	print("{Holder.pick(1, b: 1)} {Holder.pick(2, b: 1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
1 2
```

<!-- test: an-overloaded-static-through-an-alias-whose-overloads-return-different-types-is-typed-by-the-bound-one -->
The two overloads disagree about what they return, and the call binds one of them: its result is typed by that declaration read through the alias.
```maxon
type Holder uses T where T is Equatable
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'

	static function pick(c Integer) returns Integer
		return c
	end 'pick'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let xs = [1, 2]
	let ys = xs.map(function(n) gives IntHolder.pick(n, b: 1))
	let h = IntHolder.pick(9)
	print("{try ys.get(0) otherwise 0} {try ys.get(1) otherwise 0} {h}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 9
```

<!-- test: a-static-returning-a-bare-generic-type-is-read-in-the-callees-scope-through-the-alias -->
A static returning `Inner` inside `type Holder uses T` returns `Inner with Holder.T`, which the alias fixes to `Inner with String`.
```maxon
type Inner uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function get() returns T
		return self.v
	end 'get'
end 'Inner'

type Holder uses T
	static function wrap(v T) returns Inner
		return Inner.make(v)
	end 'wrap'
end 'Holder'

typealias StrHolder = Holder with String

function main() returns ExitCode
	let w = StrHolder.wrap("x {40 + 2} value long enough for a heap record")
	print("{w.get()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x 42 value long enough for a heap record
```

<!-- test: a-bare-generic-static-call-in-a-closure-infers-its-instance-from-the-arguments -->
`Holder.pick(n, b: 1)` names the generic type outright, so the arguments fix `T` and the closure's result is an `Integer`.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

function main() returns ExitCode
	let xs = [1, 2]
	let ys = xs.map(function(n) gives Holder.pick(n, b: 1))
	print("{try ys.get(0) otherwise 0} {try ys.get(1) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: error.a-bare-generic-static-whose-arguments-fix-no-type-parameter-of-its-result-is-refused -->
`Holder.make()` returns `T`, and neither the arguments nor an alias fix it, so the call has no type and is refused where it is written.
```maxon
enum HolderError implements Error
	empty
end 'HolderError'

type Holder uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function make() returns T throws HolderError
		throw HolderError.empty
	end 'make'
end 'Holder'

function main() returns ExitCode
	let m = try Holder.make() otherwise 0
	print("{m}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3162: <fragment>:19:21: 'Holder' is named without type arguments and nothing binds its parameter 'T', so 'Holder.make' has no type: its result is written over a parameter of 'Holder' that no argument fixes. Name the instance with a typealias, giving each of its parameters a type argument
```

<!-- test: error.a-bare-generic-static-whose-arguments-fix-no-type-parameter-of-its-result-is-refused-without-an-otherwise -->
The same call bound with no `otherwise` has no type either, and is refused at the call rather than at a later use of the binding.
```maxon
type Holder uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function make() returns T
		panic("nothing to make")
	end 'make'
end 'Holder'

function main() returns ExitCode
	let m = Holder.make()
	print("{m}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3162: <fragment>:15:17: 'Holder' is named without type arguments and nothing binds its parameter 'T', so 'Holder.make' has no type: its result is written over a parameter of 'Holder' that no argument fixes. Name the instance with a typealias, giving each of its parameters a type argument
```

<!-- test: a-bare-generic-static-called-inside-its-own-type-is-typed-by-the-enclosing-instance -->
Inside `Holder`'s own body the base names the enclosing instance, so `Holder.make()` is typed at the enclosing `T` and is not refused.
```maxon
type Holder uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function make() returns T
		panic("nothing to make")
	end 'make'

	static function orMake(flag bool, v T) returns T
		if flag 'wantsOne'
			return Holder.make()
		end 'wantsOne'

		return v
	end 'orMake'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let kept = IntHolder.orMake(false, v: 7)
	print("{kept}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.a-static-of-a-generic-type-is-not-a-function-value -->
A static method in value position, through an instance alias, is refused.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let f = IntHolder.pick
	print("{f(1, b: 2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:22:20: Expected 'min or max' but got 'pick'
```

<!-- test: a-static-takes-its-instance-from-its-alias-and-not-from-an-argument-of-the-same-base -->
An argument that is an instance of the static's own base fixes nothing about the static's `T`: the alias does.
```maxon
type Holder uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function keep(other StrHolder, fallback T) returns T
		print("{other.v}\n")
		return fallback
	end 'keep'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
typealias IntHolder = Holder with Integer
typealias StrHolder = Holder with String

function main() returns ExitCode
	let n = IntHolder.keep(StrHolder.create("seen"), fallback: 7)
	print("{n + 1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seen
8
```

<!-- test: a-narrow-signed-result-of-a-static-through-an-instance-alias-keeps-its-sign -->
A `returns T` the alias fixes to a narrow signed range keeps its sign when the call site reads it.
```maxon
type Holder uses T where T is Equatable
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function pick(a T, b T) returns T
		if a == b 'same'
			return b
		end 'same'

		return a
	end 'pick'
end 'Holder'

typealias Small = int(-100 to 100)
typealias SmallHolder = Holder with Small

function main() returns ExitCode
	let s = SmallHolder.pick(-3, b: 4)
	print("{s} {SmallHolder.pick(-5, b: -5) + 1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-3 -4
```

<!-- test: a-void-static-through-an-instance-alias-has-no-result-to-type -->
A static with no result, called through an instance alias, types nothing and leaves nothing to drop.
```maxon
type Holder uses T
	var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	static function note(n Count)
		print("noted {n}\n")
	end 'note'
end 'Holder'

typealias IntHolder = Holder with Count

function main() returns ExitCode
	IntHolder.note(5)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
noted 5
```

<!-- test: method-overloads-that-disagree-about-their-return-type-are-typed-by-the-bound-one -->
Two `get` methods differ in arity and in what they return. Each call is typed, and its heap result released, by the declaration it binds and not by the by-name return type.
```maxon
type Box uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function get(_ Count) returns T
		return self.v
	end 'get'

	function get(index Count, by Count) returns Count
		return index + by
	end 'get'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let b = StrBox.create("held {40 + 2} long enough for a heap record")
	let s = b.get(0)
	let n = b.get(1, by: 2)
	print("{s} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
held 42 long enough for a heap record 3
```

<!-- test: error.an-instance-method-named-statically-without-its-receiver-is-an-arity-error -->
An instance method called as `Adder.bump()` carries no receiver argument, and the call is refused as a wrong argument count.
```maxon
type Adder
	export var total as Count

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function bump(by Count)
		self.total = self.total + by
	end 'bump'
end 'Adder'

function main() returns ExitCode
	Adder.bump()
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:15:8: 'Adder.bump' expects 2 argument(s) but 0 were provided
```

<!-- test: error.an-instance-method-named-through-an-instance-alias-without-its-receiver-is-an-arity-error -->
The same call through an instance alias, where the call already names an instance, is refused as a wrong argument count and not typed through it.
```maxon
type Box uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function widen(by Count) returns Count
		return by + 1
	end 'widen'
end 'Box'

typealias CountBox = Box with Count

function main() returns ExitCode
	let n = CountBox.widen()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:17:19: 'Box.widen' expects 2 argument(s) but 0 were provided
```

<!-- test: an-implicit-self-helper-returning-a-bare-generic-name-is-typed-like-a-call-on-self -->
`helper()` called without a receiver inside `type Outer uses T` returns a bare `Inner`, which is `Inner with Outer.T`, and the result is used through that instance.
```maxon
type Inner uses T
	export var v as T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function get() returns T
		return self.v
	end 'get'
end 'Inner'

type Outer uses T
	export var seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function helper() returns Inner
		return Inner.make(self.seed)
	end 'helper'

	function viaImplicit() returns T
		let w = helper()
		return w.get()
	end 'viaImplicit'

	function viaSelf() returns T
		let w = self.helper()
		return w.get()
	end 'viaSelf'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create("outer {40 + 2} value long enough for a heap record")
	print("{o.viaImplicit()}\n{o.viaSelf()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
outer 42 value long enough for a heap record
outer 42 value long enough for a heap record
```

<!-- test: a-static-storing-a-copied-parameter-into-self-through-an-inferred-instance-retains-it -->
A `static function` of a generic type copies its `T` parameter into a `var`, builds `Self{v: copy}` and is called on the bare base, so the instance is inferred from the argument. The record owns its own reference to the string.
```maxon
type Box uses T
	export var v as T

	function touch() returns Count
		return 1
	end 'touch'

	static function keep(x T) returns Count
		var copy = x
		copy = x
		let pair = Self{v: copy}
		return pair.touch()
	end 'keep'
end 'Box'

function main() returns ExitCode
	let s = "alpha {40 + 2} value long enough for a heap record"
	let k = Box.keep(s)
	print("{k} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 alpha 42 value long enough for a heap record
```

<!-- test: a-static-storing-its-parameter-into-self-through-an-inferred-instance-retains-it -->
The same static without the local copy: `Self{v: x}` stores the borrowed parameter directly.
```maxon
type Box uses T
	export var v as T

	function touch() returns Count
		return 1
	end 'touch'

	static function keep(x T) returns Count
		let pair = Self{v: x}
		return pair.touch()
	end 'keep'
end 'Box'

function main() returns ExitCode
	let s = "alpha {40 + 2} value long enough for a heap record"
	let k = Box.keep(s)
	print("{k} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 alpha 42 value long enough for a heap record
```

<!-- test: a-static-storing-a-copied-parameter-into-self-through-a-typealias-instance-retains-it -->
The alias-qualified call of the same static is the control: both spellings of the instance take the reference.
```maxon
type Box uses T
	export var v as T

	function touch() returns Count
		return 1
	end 'touch'

	static function keep(x T) returns Count
		var copy = x
		copy = x
		let pair = Self{v: copy}
		return pair.touch()
	end 'keep'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let s = "alpha {40 + 2} value long enough for a heap record"
	let k = StrBox.keep(s)
	print("{k} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 alpha 42 value long enough for a heap record
```
