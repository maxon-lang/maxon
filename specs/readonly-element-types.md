---
feature: readonly-element-types
status: experimental
keywords: [readonly, Array, Map, List, element, let, read-only, type argument]
category: type-system
---

# `readonly` element types

## Documentation

`Array with readonly T`, `List with readonly T` and `Map with K, readonly V` hold values that nothing writes
once they are stored. The qualifier is part of the type argument, so `Array with readonly T` and `Array with T`
are two types and neither converts to the other.

- **Storing.** A value put into a `readonly` position follows the rule a `let` field follows: it is already
  read-only, or a value type, or built for the call, or a `var` at its last use, which the call moves. A `var`
  that is read again afterwards is E3078. Nothing is copied. A function that stores its parameter into a
  `readonly` position, directly or through other functions, keeps it the same way, so its callers follow the same rule.
- **Reading.** An element read out by `get`, `first`, a `for` variable or a cursor is read-only. Writing through
  it is E3159, and binding it to a `var` is E3078.
- **The container** stays as mutable as its binding: `push`, `set`, `remove`, `insert` and `clear` all work on a
  `var`.
- **Where it may appear.** `readonly` qualifies only an element type, written `Array with readonly T`, or a
  `Map`'s value type, written `Map with K, readonly V` (or `Map with (K, readonly V)`). A `Map` key and a `Set`
  element are read-only already and take no qualifier: the key or element a `for` reads is read-only, and storing
  it follows the same rule. Written anywhere else it is E2075.
- **Generics.** A generic body over `T` works unchanged on a `readonly` instance: the instance stores read-only
  values, so a read-only argument satisfies it.

## Tests

<!-- test: values-that-nothing-writes-are-stored-and-read -->
A `let` value, a fresh value and a `var` at its last use are all stored, a `readonly` element is read through `for`,
`get` and `first`, and the container itself is pushed to, set, removed from and cleared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	let shared = Counter.create(4)
	roster.push(shared)
	roster.push(Counter.create(5))

	var built = Counter.create(1)
	built.n = 6
	roster.push(built)

	var total = 0 as Integer

	for entry in roster 'each'
		total = total + entry.n
	end 'each'

	let head = try roster.first() otherwise return 1
	let second = try roster.get(1) otherwise return 1
	total = total + head.n + second.n

	try roster.set(0, value: Counter.create(10)) otherwise return 1
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

<!-- test: a-map-stores-read-only-values-and-stays-mutable -->
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

typealias Index = Map with String, readonly Counter

function main() returns ExitCode
	var index = Index.create()
	let shared = Counter.create(5)
	index.upsert("a", value: shared)
	index.upsert("b", value: Counter.create(6))

	var late = Counter.create(1)
	late.n = 2
	try index.insert("c", value: late) otherwise return 1

	index.upsert("a", value: Counter.create(7))
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
`Map with (K, readonly V)` and `Map with K, readonly V` both qualify the value type, and a `List` qualifies its
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

typealias Index = Map with (String, readonly Counter)
typealias Spelled = Map with String, readonly Counter
typealias Queue = List with readonly Counter

function main() returns ExitCode
	let shared = Counter.create(2)
	var index = Index.create()
	index.upsert("a", value: shared)
	var spelled = Spelled.create()
	spelled.upsert("b", value: shared)
	var queue = Queue.create()
	queue.append(shared)
	let first = try queue.first() otherwise return 1

	let total = (index.count() as Integer) + (spelled.count() as Integer) + (queue.count() as Integer) + first.n + shared.n

	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-generic-body-over-t-takes-a-readonly-instance -->
`count`, `contains`, `sort` and a `for` loop are generic over the element type and work unchanged on a `readonly`
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

typealias Counters = Array with readonly Counter

function main() returns ExitCode
	var counters = Counters.create()
	counters.push(Counter.create(3))
	counters.push(Counter.create(1))
	counters.push(Counter.create(2))
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
A `var` that is still used after the `push` has another writer, so the `readonly` element refuses it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	var counter = Counter.create(1)
	roster.push(counter)
	counter.n = 2

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/readonly-element-types/error.a-var-pushed-and-written-afterwards-is-refused.maxon:17:9: cannot store the `var` 'counter', which is read again, into a `readonly` element of the container written by 'push': a `readonly` element has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.a-map-value-upserted-and-written-afterwards-is-refused -->
The same refusal for a `Map`'s `readonly` value.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with String, readonly Counter

function main() returns ExitCode
	var index = Index.create()
	var counter = Counter.create(1)
	index.upsert("a", value: counter)
	counter.n = 2

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/readonly-element-types/error.a-map-value-upserted-and-written-afterwards-is-refused.maxon:17:8: cannot pass the `var` 'counter', which is read again, to parameter 'value' of 'Map.upsert', which stores it into a `readonly` element: a `readonly` element has no other writer, so it takes a fresh or moved value or a read-only one; pass a clone()
```

<!-- test: error.an-element-read-by-get-is-not-written -->
An element read out by `get` is read-only, so a write through it is E3159.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))
	let first = try roster.get(0) otherwise return 1
	first.n = 5

	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/readonly-element-types/error.an-element-read-by-get-is-not-written.maxon:18:2: cannot write through 'first': 'first' is read-only (a `readonly` element) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-for-variable-over-readonly-elements-is-not-written -->
A `for` variable over a `readonly` array is read-only even though the array is a `var`.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))

	for entry in roster 'each'
		entry.n = 7
	end 'each'

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/readonly-element-types/error.a-for-variable-over-readonly-elements-is-not-written.maxon:19:3: cannot write through 'entry': 'entry' is read-only (a `readonly` element) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.an-element-is-not-bound-to-a-var -->
Binding an element to a `var` would give a writable name to a record the array stores read-only.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))
	var mine = try roster.get(0) otherwise return 1
	mine.n = 3

	return mine.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/readonly-element-types/error.an-element-is-not-bound-to-a-var.maxon:17:6: cannot assign a value read out of a `readonly` element to mutable binding 'mine'; use 'let' instead of 'var', or use clone()
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

typealias Roster = Array with readonly Counter
typealias Plain = Array with Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))
	var plain = Plain.create()
	let first = try roster.get(0) otherwise return 1
	plain.push(first)

	return plain.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/readonly-element-types/error.an-element-is-not-pushed-into-a-plain-array.maxon:20:8: cannot store a value read out of a `readonly` element into mutable place 'an element of the container push writes'; use clone()
```

<!-- test: error.a-readonly-array-is-not-a-plain-array -->
An `Array with readonly T` is not accepted where an `Array with T` is declared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter
typealias Plain = Array with Counter

function takesPlain(items Plain) returns Count
	return items.count()
end 'takesPlain'

function main() returns ExitCode
	var roster = Roster.create()

	return takesPlain(roster) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/readonly-element-types/error.a-readonly-array-is-not-a-plain-array.maxon:22:9: argument type mismatch for 'items': expected 'Plain', got 'Roster'
```

<!-- test: error.a-plain-array-is-not-a-readonly-array -->
And an `Array with T` is not accepted where an `Array with readonly T` is declared.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter
typealias Plain = Array with Counter

function takesRoster(items Roster) returns Count
	return items.count()
end 'takesRoster'

function main() returns ExitCode
	var plain = Plain.create()

	return takesRoster(plain) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/readonly-element-types/error.a-plain-array-is-not-a-readonly-array.maxon:22:9: argument type mismatch for 'items': expected 'Roster', got 'Plain'
```

<!-- test: error.readonly-on-a-field-type-is-refused -->
`readonly` is not a field qualifier: a field uses `let` for a read-only place.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Holder
	export var kept as readonly Counter

	static function create() returns Self
		return Self{kept: Counter.create(1)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create()

	return holder.kept.n as ExitCode
end 'main'
```
```maxoncstderr
error E2075: specs/readonly-element-types/error.readonly-on-a-field-type-is-refused.maxon:13:21: `readonly` qualifies only the element type of an `Array` or `List` and the value type of a `Map` (`Array with readonly Name`, `Map with Key, readonly Value`): it is not an argument of a container's type
```

<!-- test: error.readonly-on-a-parameter-type-is-refused -->
A parameter that is not a container argument takes no qualifier either.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function read(counter readonly Counter) returns Integer
	return counter.n
end 'read'

function main() returns ExitCode
	return read(Counter.create(1)) as ExitCode
end 'main'
```
```maxoncstderr
error E2075: specs/readonly-element-types/error.readonly-on-a-parameter-type-is-refused.maxon:12:23: `readonly` qualifies only the element type of an `Array` or `List` and the value type of a `Map` (`Array with readonly Name`, `Map with Key, readonly Value`): it is not an argument of a container's type
```

<!-- test: error.readonly-on-a-map-key-is-refused -->
A `Map` key is read-only once stored already, so it takes no qualifier.

```maxon
typealias Index = Map with readonly String, String

function main() returns ExitCode
	var index = Index.create()
	index.upsert("a", value: "b")

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2075: <fragment>:2:28: `readonly` qualifies only the element type of an `Array` or `List` and the value type of a `Map` (`Array with readonly Name`, `Map with Key, readonly Value`): a `Map` key is read-only once stored, so only the value type takes the qualifier
```

<!-- test: error.readonly-on-a-set-element-is-refused -->
A `Set` element is read-only once stored already, so it takes no qualifier.

```maxon
typealias Names = Set with readonly String

function main() returns ExitCode
	var names = Names.create()
	names.insert("a")

	return names.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2075: <fragment>:2:28: `readonly` qualifies only the element type of an `Array` or `List` and the value type of a `Map` (`Array with readonly Name`, `Map with Key, readonly Value`): a `Set` element is read-only once stored, so it takes no qualifier
```

<!-- test: error.readonly-on-a-user-generic-argument-is-refused -->
Only the built-in containers take the qualifier; a user generic's argument does not.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias CounterBox = Box with readonly Integer

type Box uses Item
	let kept as Item

	static function create(item Item) returns Self
		return Self{kept: item}
	end 'create'
end 'Box'

function main() returns ExitCode
	let box = CounterBox.create(1)

	return 0
end 'main'
```
```maxoncstderr
error E2075: <fragment>:3:33: `readonly` qualifies only the element type of an `Array` or `List` and the value type of a `Map` (`Array with readonly Name`, `Map with Key, readonly Value`): `Box` is not an `Array`, a `List` or a `Map`, so no argument of it takes the qualifier
```

<!-- test: error.a-for-variable-over-a-readonly-list-is-not-written -->
A `List` iterates through a cursor, and the cursor reads the same read-only elements.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Queue = List with readonly Counter

function main() returns ExitCode
	var queue = Queue.create()
	queue.append(Counter.create(1))

	for item in queue 'each'
		item.n = 7
	end 'each'

	return queue.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/readonly-element-types/error.a-for-variable-over-a-readonly-list-is-not-written.maxon:19:3: cannot write through 'item': 'item' is read-only (a `readonly` element) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.a-for-value-over-a-readonly-map-is-not-written -->
A `Map`'s `readonly` value stays read-only when a `for` loop destructures an entry.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Index = Map with String, readonly Counter

function main() returns ExitCode
	var index = Index.create()
	index.upsert("a", value: Counter.create(1))

	for (_, value) in index 'each'
		value.n = 7
	end 'each'

	return index.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/readonly-element-types/error.a-for-value-over-a-readonly-map-is-not-written.maxon:19:3: cannot write through 'value': 'value' is read-only (a `for` element of a read-only source) and nothing reachable through it may be written; use 'var', or write a clone()
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

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))

	for (_, item) in roster.withIterator() 'each'
		item.n = 7
	end 'each'

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: specs/readonly-element-types/error.an-element-an-iterator-pair-yields-is-not-written.maxon:19:3: cannot write through 'item': 'item' is read-only (a `readonly` element) and nothing reachable through it may be written; use 'var', or write a clone()
```

<!-- test: error.the-element-a-cursor-reads-is-not-bound-to-a-var -->
An explicit cursor over a `readonly` array hands back the stored element, which no `var` may name.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

typealias Roster = Array with readonly Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(1))
	var cursor = try roster.createIterator() otherwise return 1
	var current = cursor.current()
	current.n = 9

	return roster.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/readonly-element-types/error.the-element-a-cursor-reads-is-not-bound-to-a-var.maxon:18:6: cannot assign a value read out of a `readonly` element to mutable binding 'current'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-wrapper-storing-into-a-readonly-element-takes-a-read-only-argument -->
`add` stores its parameter into an `Array with readonly ByteArray`, directly and through `register`. Neither keeps
the value where something may write it, so the read-only `source.title` is accepted for both.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Roster = Array with readonly ByteArray

type Source
	export let title as ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Catalog
	var names as Roster

	static function create() returns Self
		return Self{names: Roster.create()}
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
	catalog.add(source.title)
	catalog.register(source.title)

	return catalog.total() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.a-wrapper-storing-into-a-plain-element-refuses-a-read-only-argument -->
The same wrappers over an `Array with ByteArray` keep the value where something may write it, so the read-only
`source.title` is refused for `register`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Roster = Array with ByteArray

type Source
	export let title as ByteArray

	static function create() returns Self
		return Self{title: b"title"}
	end 'create'
end 'Source'

type Catalog
	var names as Roster

	static function create() returns Self
		return Self{names: Roster.create()}
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
error E3078: <fragment>:36:10: cannot pass a `let` field of 'source', which is read-only, to parameter 'name' of 'Catalog.register', which keeps it where something may write it; pass a clone()
```

<!-- test: a-set-element-read-by-for-is-kept-without-a-copy -->
The elements of a `Set` are read-only once stored, so the element a `for` reads out of a `var` set goes to a
function that keeps it in a `let` field with no copy.

```maxon
typealias NameSet = Set with ByteArray
typealias CreditArray = Array with Credit

type Credit
	export let name as ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Credit'

function note(credits CreditArray, name ByteArray)
	credits.push(Credit.create(name))
end 'note'

function main() returns ExitCode
	var reached = NameSet.create()
	reached.insert(b"a")
	var credits = CreditArray.create()

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
		return Self{counts: CountMap.create(), last: ByteArray.create()}
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
error E3078: specs/readonly-element-types/error.a-map-key-read-by-for-is-not-stored-into-a-var-field.maxon:19:9: cannot store a value read out of a stored Map key into mutable place 'Tally.last'; use clone()
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
		return Self{names: NameSet.create(), last: ByteArray.create()}
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
error E3078: specs/readonly-element-types/error.a-set-element-read-by-for-is-not-stored-into-a-var-field.maxon:18:9: cannot store a value read out of a stored Set element into mutable place 'Tally.last'; use clone()
```

<!-- test: a-type-named-like-the-readonly-marker-compiles-beside-a-readonly-instance -->
`ro_Counter` is a legal type name. The compiled name of `Array with readonly Counter` is minted in the reserved
space, so it cannot be the name of `Array with ro_Counter`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Label = Array with Integer

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type ro_Counter
	export var label as Label
	export var extra as Label

	static function create() returns Self
		var label = Label.create()
		label.push(7)
		var extra = Label.create()
		extra.push(8)
		return Self{label: label, extra: extra}
	end 'create'
end 'ro_Counter'

typealias Roster = Array with readonly Counter
typealias Others = Array with ro_Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(4))
	var others = Others.create()
	others.push(ro_Counter.create())
	let a = try roster.get(0) otherwise return 1
	let b = try others.get(0) otherwise return 1
	return (a.n + (b.label.count() as Integer) + (b.extra.count() as Integer)) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: error.two-instances-that-compile-to-one-name-print-the-readonly-qualifier -->
`Array_` is a legal generic type name, and joined to `_ro_Counter` it spells what `Array with readonly Counter`
compiles to. Both instantiations are printed as written, `readonly` included.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type _ro_Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end '_ro_Counter'

type Array_ uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Array_'

typealias Roster = Array with readonly Counter
typealias Wrapped = Array_ with _ro_Counter

function main() returns ExitCode
	var roster = Roster.create()
	roster.push(Counter.create(4))
	let wrapped = Wrapped.create(_ro_Counter.create(5))
	let a = try roster.get(0) otherwise return 1
	return (a.n + wrapped.item.n) as ExitCode
end 'main'
```
```maxoncstderr
error E3006: <fragment>:29:11: duplicate definition of 'Array___ro_Counter' — the generic instantiations `Array with readonly Counter` and `Array_ with _ro_Counter` compile to that same name
```
