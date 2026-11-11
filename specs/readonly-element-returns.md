---
feature: immutable-element-returns
status: experimental
keywords: [immutable, element, return, method, read-only, generic]
category: type-system
---

# A function that returns an `immutable` element declares an `immutable` result

## Documentation

An element read out of an `Array with immutable T`, a `List with immutable T` or the value side of a
`Map with K, immutable V` is `immutable`, and so is the value a function returns when the element is what it
returns. The function declares that result `immutable T`; a plain `T` result is E3209, whether the function is
generic, whether it throws, and whether it returns the element on every path or on only some of them. The caller
cannot write through the returned value (E3197). A copy made with `clone()` is a new value and is writable.

## Tests

<!-- test: error.a-method-returning-an-immutable-element-declares-its-result-immutable -->
`at` returns `try self.items.get(i)`, an element of the `immutable` array `items`. A plain `Counter` result would hand
the caller a writable name for the record the shelf holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with immutable Counter

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item immutable Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = Shelf.create()
	shelf.add(shared)
	let first = try shelf.at(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```maxoncstderr
error E3209: <fragment>:26:10: a return does not fit the declared result: 'self.items.get(i)' is immutable, the result is not `immutable`; declare the result `immutable`
```


<!-- test: a-generic-method-returning-its-item-type-over-an-immutable-instance-runs -->
The same through a generic shelf: `Shelf with immutable Counter` stores and returns frozen counters, and the item
type it returns is `immutable` by the instance.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf uses Item
	typealias Items = Array with Item

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item Item)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Item throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

typealias CounterShelf = Shelf with immutable Counter

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = CounterShelf.create()
	shelf.add(shared)
	let first = try shelf.at(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```exitcode
4
```


<!-- test: error.a-method-returning-an-immutable-element-or-a-fresh-record-hands-back-a-read-only-value -->
`at` returns the element on one path and a fresh record on the other. The caller cannot tell which, so the result
is read-only and a write through the caller's `var` is E3197.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with immutable Counter

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item immutable Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter
		return try self.items.get(i) otherwise Counter.create(0)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = Shelf.create()
	shelf.add(shared)
	var first = shelf.at(0)
	first.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3197: <fragment>:35:2: cannot write 'n' through 'first', which is read-only
```


<!-- test: error.a-method-returning-an-immutable-map-value-declares-its-result-immutable -->
The value side of `Map with String, immutable Counter` is `immutable` when a method returns it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Index
	typealias Entries = Map with String, immutable Counter

	let entries as Entries

	static function create() returns Self
		return Self{entries: Entries.create()}
	end 'create'

	function add(name String, counter immutable Counter)
		self.entries.upsert(name, value: counter)
	end 'add'

	function find(name String) returns Counter throws MapError
		return try self.entries.get(name)
	end 'find'
end 'Index'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let index = Index.create()
	index.add("one", counter: shared)
	let found = try index.find("one") otherwise return 1
	return found.n as ExitCode
end 'main'
```
```maxoncstderr
error E3209: <fragment>:26:10: a return does not fit the declared result: 'self.entries.get(name)' is immutable, the result is not `immutable`; declare the result `immutable`
```


<!-- test: a-method-returning-an-immutable-element-is-read-through-a-let -->
The accepted twin: the result is declared `immutable` and the caller binds the returned element with `let` and
reads it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with immutable Counter

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item immutable Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns immutable Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = Shelf.create()
	shelf.add(shared)
	let first = try shelf.at(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```exitcode
4
```


<!-- test: error.an-immutable-result-is-not-written-by-its-caller -->
The result is declared `immutable`, so a write through the caller's binding is E3197.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with immutable Counter

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item immutable Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns immutable Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = Shelf.create()
	shelf.add(shared)
	var first = try shelf.at(0) otherwise return 1
	first.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3197: <fragment>:35:2: cannot write 'n' through 'first', which is immutable
```


<!-- test: a-method-returning-an-immutable-element-is-copied-with-clone -->
The accepted twin: a `clone()` of the returned element is a new record, so the caller's `var` writes the copy and
the shelf's record keeps its value.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with immutable Counter

	let items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(item immutable Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns immutable Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = freeze Counter.create(4)
	let shelf = Shelf.create()
	shelf.add(shared)
	let mine = (try shelf.at(0) otherwise return 1).clone()
	mine.n = 99
	return shared.n as ExitCode
end 'main'
```
```exitcode
4
```

