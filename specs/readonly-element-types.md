---
feature: immutable-element-types
status: experimental
keywords: [immutable, Array, Map, List, element, freeze, read-only, type argument]
category: type-system
---

# `immutable` element types

## Documentation

`Array with immutable T`, `List with immutable T` and `Map with K, immutable V` hold frozen values that nothing
writes once they are stored. The qualifier is part of the type argument, so `Array with immutable T` and
`Array with T` are two types and neither converts to the other.

- **Storing.** A value put into an `immutable` position is frozen: a `freeze` expression, a value read from
  another `immutable` slot, or a value type. A mutable record is E3196. Nothing is copied.
- **Reading.** An element read out by `get`, `first`, a `for` variable or a cursor is immutable. Writing through
  it is E3197.
- **The container** is as mutable as its binding: `push`, `set`, `remove`, `insert` and `clear` need no
  reassignment of the container.
- **Where it may appear.** `immutable` qualifies an element type, written `Array with immutable T`, or a
  `Map`'s value type, written `Map with K, immutable V` (or `Map with (K, immutable V)`). A `Map` key and a `Set`
  element are read-only already and take no qualifier: the key or element a `for` reads is read-only, and storing
  it follows the same rule.
- **Generics.** A generic body over `T` works unchanged on an `immutable` instance: the instance stores frozen
  values, so a frozen argument satisfies it.

## Tests

<!-- test: values-that-nothing-writes-are-stored-and-read -->
A `let` value and a fresh value are frozen and stored, an `immutable` element is read through `for`,
`get` and `first`, and the container itself is pushed to, set, removed from and cleared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	let roster = Roster.create(0)
	let shared = freeze Counter.create(4)
	roster.push(shared)
	roster.push(freeze Counter.create(5))

	let built = freeze Counter.create(6)
	roster.push(built)

	var total = 0 as Integer

	for entry in roster 'each'
		total = total + entry.n
	end 'each'

	let head = try roster.first() otherwise return 1
	let second = try roster.get(1) otherwise return 1
	total = total + head.n + second.n

	try roster.set(0, value: freeze Counter.create(10)) otherwise return 1
	let removed = try roster.remove(2) otherwise return 1
	total = total + removed.n
	roster.insert(0, value: shared)
	total = total + (roster.count() as Integer)
	roster.clear()

	return (total + (roster.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
33
```

<!-- test: a-map-stores-frozen-values-and-stays-mutable -->
A `Map`'s value type takes the qualifier: values are stored by `upsert` and `insert`, read back by `get`, and the
map is still `upsert`ed over and removed from.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with String, immutable Counter

function main() returns ExitCode
	let index = Index.create()
	let shared = freeze Counter.create(5)
	index.upsert("a", value: shared)
	index.upsert("b", value: freeze Counter.create(6))

	let late = freeze Counter.create(2)
	try index.insert("c", value: late) otherwise return 1

	index.upsert("a", value: freeze Counter.create(7))
	let seen = try index.get("a") otherwise return 1
	let gone = index.remove("b")
	var total = seen.n + shared.n

	if gone 'removed'
		total = total + 1
	end 'removed'

	return (total + (index.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
15
```

<!-- test: the-parenthesized-spelling-of-a-map-and-a-list-take-the-qualifier -->
`Map with (K, immutable V)` and `Map with K, immutable V` both qualify the value type, and a `List` qualifies its
element. Each takes a `let` record that other names can still read, which a container of writable records would
refuse; two aliases are two types, so the spellings are told apart by what they accept and not by passing one
where the other is declared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with (String, immutable Counter)
typealias Spelled = Map with String, immutable Counter
typealias Queue = List with immutable Counter

function main() returns ExitCode
	let shared = freeze Counter.create(2)
	let index = Index.create()
	index.upsert("a", value: shared)
	let spelled = Spelled.create()
	spelled.upsert("b", value: shared)
	let queue = Queue.create()
	queue.append(shared)
	let first = try queue.first() otherwise return 1

	let total = (index.count() as Integer) + (spelled.count() as Integer) + (queue.count() as Integer) + first.n + shared.n

	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-generic-body-over-t-takes-an-immutable-instance -->
`count`, `contains`, `sort` and a `for` loop are generic over the element type and work unchanged on an `immutable`
instance of records that can be written elsewhere.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter implements Comparable, Equatable
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function compare(other Counter) returns Ordering
		return self.n.compare(other.n)
	end 'compare'

	function equals(other Counter) returns bool
		return self.n == other.n
	end 'equals'
end 'Counter'

typealias Counters = Array with immutable Counter

function main() returns ExitCode
	let counters = Counters.create(0)
	counters.push(freeze Counter.create(3))
	counters.push(freeze Counter.create(1))
	counters.push(freeze Counter.create(2))
	counters.sort()
	var total = 0 as Integer

	for counter in counters 'each'
		total = total + counter.n
	end 'each'

	if counters.contains(Counter.create(2)) 'found'
		total = total + 1
	end 'found'

	return (total + (counters.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: error.a-var-pushed-and-written-afterwards-is-refused -->
A mutable record has another writer, so an `immutable` element refuses it until it is frozen: E3196.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	var counter = Counter.create(1)
	roster.push(counter)
	counter.n = 2

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3196: specs/readonly-element-types/error.a-var-pushed-and-written-afterwards-is-refused.maxon:17:14: cannot store a mutable 'Counter' into 'an element of the container push writes', which holds an immutable 'Counter'; freeze it
```

<!-- test: error.a-map-value-upserted-and-written-afterwards-is-refused -->
The same refusal for a `Map`'s `immutable` value.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with String, immutable Counter

function main() returns ExitCode
	var index = Index.create()
	var counter = Counter.create(1)
	index.upsert("a", value: counter)
	counter.n = 2

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3196: specs/readonly-element-types/error.a-map-value-upserted-and-written-afterwards-is-refused.maxon:17:27: cannot store a mutable 'Counter' into 'Map.upsert.value', which holds an immutable 'Counter'; freeze it
```

<!-- test: error.an-element-read-by-get-is-not-written -->
An element read out by `get` is immutable, so a write through it is E3197.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))
	let first = try roster.get(0) otherwise return 1
	first.n = 5

	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.an-element-read-by-get-is-not-written.maxon:18:2: cannot write 'n' through 'first', which is immutable
```

<!-- test: error.a-for-variable-over-immutable-elements-is-not-written -->
A `for` variable over an `immutable` array is immutable even though the array itself is mutable.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))

	for entry in roster 'each'
		entry.n = 7
	end 'each'

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.a-for-variable-over-immutable-elements-is-not-written.maxon:19:3: cannot write 'n' through 'entry', which is immutable
```

<!-- test: error.an-element-is-not-bound-to-a-var -->
An element bound to a `var` is still immutable: a write through the `var` is E3197.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))
	var mine = try roster.get(0) otherwise return 1
	mine.n = 3

	return mine.n as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.an-element-is-not-bound-to-a-var.maxon:18:2: cannot write 'n' through 'mine', which is immutable
```

<!-- test: error.an-element-is-not-pushed-into-a-plain-array -->
Storing an element into an array of writable elements would give it a second, writable holder.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter
typealias Plain = Array with Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))
	var plain = Plain.create(0)
	let first = try roster.get(0) otherwise return 1
	plain.push(first)

	return plain.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3196: specs/readonly-element-types/error.an-element-is-not-pushed-into-a-plain-array.maxon:20:13: cannot store an immutable 'Counter' into 'an element of the container push writes', which holds a mutable 'Counter'; declare the element `immutable Counter`
```

<!-- test: error.an-immutable-array-is-not-a-plain-array -->
An `Array with immutable T` is not accepted where an `Array with T` is declared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter
typealias Plain = Array with Counter

function takesPlain(items Plain) returns Count
	return items.count()
end 'takesPlain'

function main() returns ExitCode
	var roster = Roster.create(0)

	return takesPlain(roster) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/readonly-element-types/error.an-immutable-array-is-not-a-plain-array.maxon:22:9: argument type mismatch for 'items': expected 'Plain', got 'Roster'
```

<!-- test: error.a-plain-array-is-not-an-immutable-array -->
And an `Array with T` is not accepted where an `Array with immutable T` is declared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter
typealias Plain = Array with Counter

function takesRoster(items Roster) returns Count
	return items.count()
end 'takesRoster'

function main() returns ExitCode
	var plain = Plain.create(0)

	return takesRoster(plain) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/readonly-element-types/error.a-plain-array-is-not-an-immutable-array.maxon:22:9: argument type mismatch for 'items': expected 'Roster', got 'Plain'
```

<!-- test: error.immutable-on-a-map-key-is-refused -->
A `Map` key is read-only once stored already, so it takes no qualifier.

```maxon
typealias Index = Map with immutable String, String

function main() returns ExitCode
	var index = Index.create()
	index.upsert("a", value: "b")

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:2:28: Expected 'a type' but got 'immutable'
```

<!-- test: error.immutable-on-a-set-element-is-refused -->
A `Set` element is read-only once stored already, so it takes no qualifier.

```maxon
typealias Names = Set with immutable String

function main() returns ExitCode
	var names = Names.create()
	names.insert("a")

	return names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:2:28: Expected 'a type' but got 'immutable'
```

<!-- test: error.a-for-variable-over-an-immutable-list-is-not-written -->
A `List` iterates through a cursor, and the cursor reads the same read-only elements.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Queue = List with immutable Counter

function main() returns ExitCode
	var queue = Queue.create()
	queue.append(freeze Counter.create(1))

	for item in queue 'each'
		item.n = 7
	end 'each'

	return queue.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.a-for-variable-over-an-immutable-list-is-not-written.maxon:19:3: cannot write 'n' through 'item', which is immutable
```

<!-- test: error.a-for-value-over-an-immutable-map-is-not-written -->
A `Map`'s `immutable` value stays immutable when a `for` loop destructures an entry.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with String, immutable Counter

function main() returns ExitCode
	var index = Index.create()
	index.upsert("a", value: freeze Counter.create(1))

	for (_, value) in index 'each'
		value.n = 7
	end 'each'

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.a-for-value-over-an-immutable-map-is-not-written.maxon:19:3: cannot write 'n' through 'value', which is immutable
```

<!-- test: error.an-element-an-iterator-pair-yields-is-not-written -->
The element of a `withIterator()` pair is read out of the same array.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))

	for (_, item) in roster.withIterator() 'each'
		item.n = 7
	end 'each'

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.an-element-an-iterator-pair-yields-is-not-written.maxon:19:3: cannot write 'n' through 'item', which is immutable
```

<!-- test: error.the-element-a-cursor-reads-is-not-bound-to-a-var -->
An explicit cursor over an `immutable` array hands back the stored element, which nothing may write through.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	var roster = Roster.create(0)
	roster.push(freeze Counter.create(1))
	var cursor = try roster.createIterator() otherwise return 1
	var current = cursor.current()
	current.n = 9

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3197: specs/readonly-element-types/error.the-element-a-cursor-reads-is-not-bound-to-a-var.maxon:19:2: cannot write 'n' through 'current', which is immutable
```

<!-- test: a-wrapper-storing-into-an-immutable-element-takes-a-frozen-argument -->
`add` stores its parameter into an `Array with immutable ByteArray`, directly and through `register`. Neither keeps
the value where something may write it, so the immutable `source.title` is accepted for both.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Roster = Array with immutable ByteArray

type Source
	export let title as immutable ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Catalog
	var names as Roster

	static function create() returns Self
		return Self{names: Roster.create(0)}
	end 'create'

	function add(name ByteArray)
		self.names.push(name)
	end 'add'

	function register(name ByteArray)
		self.add(name)
	end 'register'

	function total() returns Integer
		return self.names.count() as Integer
	end 'total'
end 'Catalog'

function main() returns ExitCode
	let catalog = Catalog.create()
	let source = Source.create()
	catalog.add(source.title)
	catalog.register(source.title)

	return catalog.total() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-wrapper-storing-into-a-plain-element-refuses-an-immutable-argument -->
The same wrappers over an `Array with ByteArray` keep the value where something may write it, so the read-only
`source.title` is refused for `register`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Roster = Array with ByteArray

type Source
	export let title as immutable ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Catalog
	var names as Roster

	static function create() returns Self
		return Self{names: Roster.create(0)}
	end 'create'

	function add(name ByteArray)
		self.names.push(name)
	end 'add'

	function register(name ByteArray)
		self.add(name)
	end 'register'

	function total() returns Integer
		return self.names.count() as Integer
	end 'total'
end 'Catalog'

function main() returns ExitCode
	var catalog = Catalog.create()
	let source = Source.create()
	catalog.register(source.title)

	return catalog.total() as ExitCode
end 'main'
```
```maxoncstderr
error E3207: <fragment>:36:19: 'register' writes 'name' (it calls 'add', which writes 'name'); 'source' is immutable
```

<!-- test: a-set-element-read-by-for-is-kept-without-a-copy -->
The elements of a `Set` are read-only once stored, so the element a `for` reads out of a set goes to a
function that keeps it in a `let` field with no copy.

```maxon
typealias NameSet = Set with ByteArray
typealias CreditArray = Array with Credit

type Credit
	export let name as immutable ByteArray

	static function create(name immutable ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Credit'

function note(credits CreditArray, name immutable ByteArray)
	credits.push(Credit.create(name))
end 'note'

function main() returns ExitCode
	let reached = NameSet.create()
	reached.insert(b"a")
	let credits = CreditArray.create(0)

	for name in reached 'eachReached'
		note(credits, name: name)
	end 'eachReached'

	return credits.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-map-key-read-by-for-is-not-stored-into-a-var-field -->
A key read back out of a `Map` is read-only, so it cannot be stored into a `var` field, where it would have a
second writer.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CountMap = Map with (ByteArray, Integer)

type Tally
	var counts as CountMap
	export var last as ByteArray

	static function create() returns Self
		return Self{counts: CountMap.create(), last: ByteArray.create(0)}
	end 'create'

	function record(name ByteArray)
		self.counts.upsert(name, value: 1)
	end 'record'

	function rememberKeys()
		for (name, _) in self.counts 'eachName'
			self.last = name
		end 'eachName'
	end 'rememberKeys'
end 'Tally'

function main() returns ExitCode
	var tally = Tally.create()
	tally.record(b"a")
	tally.rememberKeys()

	return tally.last.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3196: specs/readonly-element-types/error.a-map-key-read-by-for-is-not-stored-into-a-var-field.maxon:19:16: cannot store an immutable 'ByteArray' into 'last', which holds a mutable 'ByteArray'; declare the field `immutable ByteArray`
```

<!-- test: error.a-set-element-read-by-for-is-not-stored-into-a-var-field -->
An element read back out of a `Set` is read-only, so it cannot be stored into a `var` field, where it would have a
second writer.

```maxon
typealias NameSet = Set with ByteArray

type Tally
	var names as NameSet
	export var last as ByteArray

	static function create() returns Self
		return Self{names: NameSet.create(), last: ByteArray.create(0)}
	end 'create'

	function record(name ByteArray)
		self.names.insert(name)
	end 'record'

	function rememberElements()
		for name in self.names 'eachName'
			self.last = name
		end 'eachName'
	end 'rememberElements'
end 'Tally'

function main() returns ExitCode
	var tally = Tally.create()
	tally.record(b"a")
	tally.rememberElements()

	return tally.last.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3196: specs/readonly-element-types/error.a-set-element-read-by-for-is-not-stored-into-a-var-field.maxon:18:16: cannot store an immutable 'ByteArray' into 'last', which holds a mutable 'ByteArray'; declare the field `immutable ByteArray`
```

<!-- test: an-immutable-element-is-carried-to-the-ninth-argument-of-a-nested-instance -->
An `extension Array` member returns a nine-argument instance whose last argument is the array's element type. On an
`Array with immutable Counter` that argument is immutable, and the position it occupies is the ninth, past the eight a
one-byte mask could hold.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Wide uses A, B, C, D, E, F, G, H, I
	let last as I

	static function create(last I) returns Self
		return Self{last: last}
	end 'create'

	function item() returns I
		return self.last
	end 'item'
end 'Wide'

extension Array
	typealias NineWide = Wide with (Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Element)

	function wide() returns NineWide throws ArrayError
		let first = try self.get(0)
		return NineWide.create(first)
	end 'wide'
end 'Array'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	let roster = Roster.create(0)
	roster.push(freeze Counter.create(7))
	let nine = try roster.wide() otherwise return 1
	return nine.item().n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: an-immutable-element-is-carried-to-the-sixty-fourth-argument-of-a-nested-instance -->
The sixty-fourth argument is the last position an instance records, and an `Array with immutable Counter`'s element
reaches it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Widest uses P0, P1, P2, P3, P4, P5, P6, P7, P8, P9, P10, P11, P12, P13, P14, P15, P16, P17, P18, P19, P20, P21, P22, P23, P24, P25, P26, P27, P28, P29, P30, P31, P32, P33, P34, P35, P36, P37, P38, P39, P40, P41, P42, P43, P44, P45, P46, P47, P48, P49, P50, P51, P52, P53, P54, P55, P56, P57, P58, P59, P60, P61, P62, P63
	let last as P63

	static function create(last P63) returns Self
		return Self{last: last}
	end 'create'

	function item() returns P63
		return self.last
	end 'item'
end 'Widest'

extension Array
	typealias SixtyFourWide = Widest with (Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Element)

	function widest() returns SixtyFourWide throws ArrayError
		let first = try self.get(0)
		return SixtyFourWide.create(first)
	end 'widest'
end 'Array'

typealias Roster = Array with immutable Counter

function main() returns ExitCode
	let roster = Roster.create(0)
	roster.push(freeze Counter.create(9))
	let wide = try roster.widest() otherwise return 1
	return wide.item().n as ExitCode
end 'main'
```
```exitcode
9
```
