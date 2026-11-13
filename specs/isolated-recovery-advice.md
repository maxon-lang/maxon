---
feature: isolated-recovery-advice
status: stable
keywords: [isolated, swap, return, advice, E3200]
category: semantics
---
# E3200 names what its position wants

## Documentation

A value wanted isolated that is not gets advice for its position: a plain local is asked to declare itself `as isolated T`, an isolated binding is asked for a clean initializer, and a call result or another expression is asked for a fresh value, a clean call, a `swap` or an `isolated` local.

## Tests

<!-- test: error.a-call-result-returned-as-isolated-names-the-clean-ways -->
`source.view()` hands back the source's own cell, so a `returns isolated` return cannot be fixed by declaring anything: the advice names the values that are isolated.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Source
	var cell as Cell

	static function create() returns Self
		return Self{cell: Cell.create(1)}
	end 'create'

	function view() returns Cell
		return self.cell
	end 'view'
end 'Source'

type Slot
	export var held as isolated Cell

	static function create() returns Self
		return Self{held: Cell.create(0)}
	end 'create'
end 'Slot'
function viaCall(source Source) returns isolated Cell
	return source.view()
end 'viaCall'

function main() returns ExitCode
	let source = Source.create()
	let got = viaCall(source)
	print("{got.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:32:9: 'source.view()' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: error.a-plain-local-returned-as-isolated-is-asked-to-declare-itself -->
`held` is a local that a declaration can make isolated, so the advice names the declaration.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Source
	var cell as Cell

	static function create() returns Self
		return Self{cell: Cell.create(1)}
	end 'create'

	function view() returns Cell
		return self.cell
	end 'view'
end 'Source'

type Slot
	export var held as isolated Cell

	static function create() returns Self
		return Self{held: Cell.create(0)}
	end 'create'
end 'Slot'
function viaLocal(source Source) returns isolated Cell
	let held = source.view()
	held.n = 3

	return held
end 'viaLocal'

function main() returns ExitCode
	let source = Source.create()
	let got = viaLocal(source)
	print("{got.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:35:9: 'held' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Cell`, or pass a copy
```

<!-- test: error.an-isolated-binding-initialised-from-a-view-is-asked-for-a-clean-value -->
`kept` is already declared isolated, so the advice is about what initializes it.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Source
	var cell as Cell

	static function create() returns Self
		return Self{cell: Cell.create(1)}
	end 'create'

	function view() returns Cell
		return self.cell
	end 'view'
end 'Source'

type Slot
	export var held as isolated Cell

	static function create() returns Self
		return Self{held: Cell.create(0)}
	end 'create'
end 'Slot'
function viaBinding(source Source) returns Tally
	let kept = source.view() as isolated Cell

	return kept.n
end 'viaBinding'

function main() returns ExitCode
	let source = Source.create()
	print("{viaBinding(source)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:32:13: 'source.view() as isolated Cell' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; initialize it with a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: error.a-swap-of-a-call-result-names-the-clean-ways -->
The replacement of a `swap` is wanted isolated: a call result that views another record is not.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Source
	var cell as Cell

	static function create() returns Self
		return Self{cell: Cell.create(1)}
	end 'create'

	function view() returns Cell
		return self.cell
	end 'view'
end 'Source'

type Slot
	export var held as isolated Cell

	static function create() returns Self
		return Self{held: Cell.create(0)}
	end 'create'
end 'Slot'
function viaSwapOfACall(slot Slot, source Source) returns isolated Cell
	return swap slot.held with source.view()
end 'viaSwapOfACall'

function main() returns ExitCode
	let slot = Slot.create()
	let source = Source.create()
	let old = viaSwapOfACall(slot, source: source)
	print("{old.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:32:29: 'source.view()' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted; use a fresh value, a clean call, a `swap` or an `isolated` local, or pass a copy
```

<!-- test: error.a-swap-of-a-plain-local-is-asked-to-declare-it -->
`spare` is a local the swap wants isolated, so the advice names the declaration.

```maxon
typealias Tally = int(0 to u64.max)

type Cell
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Cell'

type Source
	var cell as Cell

	static function create() returns Self
		return Self{cell: Cell.create(1)}
	end 'create'

	function view() returns Cell
		return self.cell
	end 'view'
end 'Source'

type Slot
	export var held as isolated Cell

	static function create() returns Self
		return Self{held: Cell.create(0)}
	end 'create'
end 'Slot'
function viaSwapOfALocal(slot Slot, source Source) returns isolated Cell
	let spare = source.view()
	spare.n = 9

	return swap slot.held with spare
end 'viaSwapOfALocal'

function main() returns ExitCode
	let slot = Slot.create()
	let source = Source.create()
	let old = viaSwapOfALocal(slot, source: source)
	print("{old.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:35:29: 'spare' is not isolated: only a clean call, a `swap` or an `isolated` local goes where an isolated value is wanted. Declare it `as isolated Cell`, or pass a copy
```
