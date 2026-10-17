---
feature: readonly-element-returns
status: experimental
keywords: [readonly, element, return, method, read-only, var, generic]
category: type-system
---

# A function that returns a `readonly` element returns a read-only value

## Documentation

An element read out of an `Array with readonly T`, a `List with readonly T` or the value side of a
`Map with K, readonly V` is read-only, and so is the value a function returns when the element is what it
returns. The caller cannot bind it to a `var` or write through it, whether the function is generic, whether it
throws, and whether it returns the element on every path or on only some of them. A copy made with `clone()` is
a new value and is writable.

## Tests

<!-- test: error.a-method-returning-a-readonly-element-hands-back-a-read-only-value -->
`at` returns `try self.items.get(i)`, an element of the `readonly` array `items`. The caller's `var first` would
write the record the shelf holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with readonly Counter

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = Counter.create(4)
	var shelf = Shelf.create()
	shelf.add(shared)
	var first = try shelf.at(0) otherwise return 1
	first.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:6: cannot assign a value, which may share storage with a `readonly` element, to mutable binding 'first'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-generic-method-returning-a-readonly-element-hands-back-a-read-only-value -->
The same through a generic shelf: `Shelf with Counter` returns its item type.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf uses Item
	typealias Items = Array with readonly Item

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item Item)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Item throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

typealias CounterShelf = Shelf with Counter

function main() returns ExitCode
	let shared = Counter.create(4)
	var shelf = CounterShelf.create()
	shelf.add(shared)
	var first = try shelf.at(0) otherwise return 1
	first.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:36:6: cannot assign a value, which may share storage with a `readonly` element, to mutable binding 'first'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-method-returning-a-readonly-element-or-a-fresh-record-hands-back-a-read-only-value -->
`at` returns the element on one path and a fresh record on the other. The caller cannot tell which, so the result
is read-only.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with readonly Counter

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter
		return try self.items.get(i) otherwise Counter.create(0)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = Counter.create(4)
	var shelf = Shelf.create()
	shelf.add(shared)
	var first = shelf.at(0)
	first.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:6: cannot assign a value, which may share storage with a `readonly` element, to mutable binding 'first'; use 'let' instead of 'var', or use clone()
```

<!-- test: error.a-method-returning-a-readonly-map-value-hands-back-a-read-only-value -->
The value side of `Map with String, readonly Counter` is read-only when a method returns it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Index
	typealias Entries = Map with String, readonly Counter

	var entries as Entries

	static function create() returns Self
		return Self{entries: Entries.create()}
	end 'create'

	function add(name String, counter Counter)
		self.entries.upsert(name, value: counter)
	end 'add'

	function find(name String) returns Counter throws MapError
		return try self.entries.get(name)
	end 'find'
end 'Index'

function main() returns ExitCode
	let shared = Counter.create(4)
	var index = Index.create()
	index.add("one", counter: shared)
	var found = try index.find("one") otherwise return 1
	found.n = 99
	return shared.n as ExitCode
end 'main'
```
```maxoncstderr
error E3078: <fragment>:34:6: cannot assign a value, which may share storage with a `readonly` element, to mutable binding 'found'; use 'let' instead of 'var', or use clone()
```

<!-- test: a-method-returning-a-readonly-element-is-read-through-a-let -->
The accepted twin: the caller binds the returned element with `let` and reads it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

type Shelf
	typealias Items = Array with readonly Counter

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = Counter.create(4)
	var shelf = Shelf.create()
	shelf.add(shared)
	view first = try shelf.at(0) otherwise return 1
	return first.n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-method-returning-a-readonly-element-is-copied-with-clone -->
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
	typealias Items = Array with readonly Counter

	var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'

	function add(item Counter)
		self.items.push(item)
	end 'add'

	function at(i ElementIndex) returns Counter throws ArrayError
		return try self.items.get(i)
	end 'at'
end 'Shelf'

function main() returns ExitCode
	let shared = Counter.create(4)
	var shelf = Shelf.create()
	shelf.add(shared)
	var mine = (try shelf.at(0) otherwise return 1).clone()
	mine.n = 99
	return shared.n as ExitCode
end 'main'
```
```exitcode
4
```
