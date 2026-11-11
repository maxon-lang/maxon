---
feature: deep-let-door-agreement
status: experimental
keywords: [let, field, call argument, store, owner, shared record, key column]
category: type-system
---

# Every door a `let` field is filled through takes the record itself

## Documentation

A `let` field is a name that is never reassigned; the record it holds is written like any other. Two doors fill
one: a call to a function that stores its parameter into the field, and a store written in the function that owns
the field's type. Both hand the field the record itself, so a record that is also named elsewhere stays shared
with those names: one placed inside another container, one read again afterwards, one handed out in the result,
one read out of a record this function made, or an element read out of an array whose slot was rewritten. A
write through any of those names is read through the field, and `clone()` gives the field a record of its own.
The key column of a `Map` or a `Set` holds `immutable` keys only.

## Tests

<!-- test: an-element-of-an-array-another-field-also-names-is-kept-by-a-let-field -->
`orphan` swaps `a` out and reads an element of the old array. `seed` made `b` another name for the same array, so the element stays reachable through `b`, and the `let` field keeps it all the same.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

typealias CounterArray = Array with Counter

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

type Hub
	var a as CounterArray
	var b as CounterArray

	static function create() returns Self
		return Self{a: CounterArray.create(0), b: CounterArray.create(0)}
	end 'create'

	function seed(counter Counter)
		self.a.push(counter)
		self.b = self.a
	end 'seed'

	function orphan() returns Keeper
		let old = self.a
		self.a = CounterArray.create(0)
		return Keeper.create(try old.get(0) otherwise panic("seeded before"))
	end 'orphan'
end 'Hub'

function main() returns ExitCode
	let hub = Hub.create()
	hub.seed(Counter.create(4))
	let kept = hub.orphan()
	return kept.inner.v as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: a-value-made-here-and-stored-once-is-stored-into-a-let-field -->
The accepted twin: the record is made here, named once and stored, so nothing else can write it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Keeper
	export let inner as Counter

	static function made() returns Self
		let counter = Counter.create(1)
		return Self{inner: counter}
	end 'made'
end 'Keeper'

function main() returns ExitCode
	let kept = Keeper.made()
	return kept.inner.v as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: a-record-read-out-of-an-owner-that-is-handed-out-afterwards-is-kept-by-a-let-field -->
`owner` is made in `build`, its `inner` goes into the `let` field, and `owner` itself is handed out in the result. The caller can write `owner.inner`, the record the `let` field holds.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Box
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create(0)}
	end 'create'
end 'Box'

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

function build() returns (Keeper, Box)
	let owner = Box.create()
	let keeper = Keeper.create(owner.inner)
	return (keeper, owner)
end 'build'

function main() returns ExitCode
	let built = build()
	return built.0.inner.v as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-record-read-out-of-an-owner-nothing-names-afterwards-is-stored-into-a-let-field -->
The accepted twin: `owner` is not named after the read, so the `let` field holds the record alone.

```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Counter'

type Box
	export var inner as Counter

	static function create() returns Self
		return Self{inner: Counter.create(0)}
	end 'create'
end 'Box'

type Keeper
	export let inner as Counter

	static function create(inner Counter) returns Self
		return Self{inner: inner}
	end 'create'
end 'Keeper'

function build() returns Keeper
	let owner = Box.create()
	return Keeper.create(owner.inner)
end 'build'

function main() returns ExitCode
	let built = build()
	return built.inner.v as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-map-that-grows-keeps-every-key -->
A `Map` rehashes into larger arrays when it fills, moving each key out of the old key column into the new one. The key column holds only `immutable` keys, so the move is accepted and no key is lost.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias NameMap = Map with (String, Integer)

function main() returns ExitCode
	let names = NameMap.create()

	for i in 0 upto 200 'fill'
		names.upsert("key number {i} padded out long enough to heap allocate", value: i as Integer)
	end 'fill'

	var total = 0 as Integer

	for i in 0 upto 200 'sum'
		total = total + (try names.get("key number {i} padded out long enough to heap allocate") otherwise 0)
	end 'sum'

	print("{names.count()} {total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
200 19900
```

<!-- test: a-let-kept-by-a-let-field-is-moved-into-a-var -->
`wrap` keeps the record `cell` names in a `let` field, and `cell` is moved into a `var` that writes it: `wrap` reads the write.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function main() returns ExitCode
	let cell = Cell.create()
	let wrap = Wrap.create(cell)
	let mine = cell
	mine.n = 7
	return wrap.cell.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-let-kept-by-a-let-field-and-also-returned-is-accepted -->
The function hands the record to the caller beside the `Wrap` that keeps it: both names reach one record.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function register() returns (Wrap, Cell)
	let cell = Cell.create()
	let wrap = Wrap.create(cell)
	return (wrap, cell)
end 'register'

function main() returns ExitCode
	let (wrap, cell) = register()
	return (wrap.cell.n + cell.n) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-array-kept-by-a-let-field-and-also-returned-is-accepted -->
A `ByteArray` is an array like any other: the name a `Layout` keeps in its `let` field may also be returned.

```maxon
type Layout
	export let name as ByteArray

	static function create(name ByteArray) returns Self
		return Self{name: name}
	end 'create'
end 'Layout'

function register() returns (Layout, ByteArray)
	let name = ByteArray.create(0)
	let layout = Layout.create(name)
	return (layout, name)
end 'register'

function main() returns ExitCode
	let (layout, name) = register()
	return (layout.name.count() + name.count()) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: a-let-kept-by-a-let-field-is-read-through-the-field -->
The accepted twin: the record is kept by the field, the caller reads it through the field, and nothing else names it.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function register() returns Wrap
	let cell = Cell.create(7)
	return Wrap.create(cell)
end 'register'

function main() returns ExitCode
	let wrap = register()
	let cell = wrap.cell
	return cell.n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-var-moved-into-a-let-field-at-its-last-use-is-accepted -->
The other accepted twin: the `var` is the record's only name, and the call that keeps it is its last use.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cell
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Wrap
	export let cell as Cell

	static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Wrap'

function main() returns ExitCode
	let cell = Cell.create(1)
	cell.n = 9
	let wrap = Wrap.create(cell)
	return wrap.cell.n as ExitCode
end 'main'
```
```exitcode
9
```
