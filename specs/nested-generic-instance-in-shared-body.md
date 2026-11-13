---
feature: nested-generic-instance-in-shared-body
status: stable
keywords: [generics, ownership, leak, dictionary-passing, nested-instance, type-alias]
category: memory-safety
---

# A Nested Generic Instance Built Over A Type Parameter Releases What It Holds

## Documentation

An instance of a generic type written over another instance, `Gen with (Cell with A)` inside `type Wrap
uses A`, is built and dropped by a body compiled once for every instantiation of `A`. The inner instance
stands at the enclosing type's own parameter, so the release of what it holds is read out of the layout
descriptor the enclosing instantiation hands down: `Wrap with String` releases a `Cell with String`, `Wrap
with Pad` a `Cell with Pad`, and `Wrap with NameArray` a `Cell with NameArray`, at every depth of nesting.
The instance is released the same way whether it is a local, a field of the enclosing type or a value the
body returns, and whether the nested instance is written inline or through a type alias.

## Tests

<!-- test: local-string-at-depth-2 -->
A nested instance built as a local over a `String` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellA.create(self.left))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-record-at-depth-2 -->
A nested instance built as a local over a `Pad` record at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellA.create(self.left))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-array-at-depth-2 -->
A nested instance built as a local over a `NameArray` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellA.create(self.left))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-string-at-depth-3 -->
A nested instance built as a local over a `String` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellCell.create(CellA.create(self.left)))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-record-at-depth-3 -->
A nested instance built as a local over a `Pad` record at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellCell.create(CellA.create(self.left)))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: local-array-at-depth-3 -->
A nested instance built as a local over a `NameArray` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function run() returns Tally
		let g = Nested.create(CellCell.create(CellA.create(self.left)))
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-string-at-depth-2 -->
A nested instance received from a call as a local over a `String` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-record-at-depth-2 -->
A nested instance received from a call as a local over a `Pad` record at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-array-at-depth-2 -->
A nested instance received from a call as a local over a `NameArray` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-string-at-depth-3 -->
A nested instance received from a call as a local over a `String` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-record-at-depth-3 -->
A nested instance received from a call as a local over a `Pad` record at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: callresult-array-at-depth-3 -->
A nested instance received from a call as a local over a `NameArray` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'

	function run() returns Tally
		let g = self.build()
		g.bump(1)
		return g.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-string-at-depth-2 -->
A nested instance held in a field over a `String` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-record-at-depth-2 -->
A nested instance held in a field over a `Pad` record at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-array-at-depth-2 -->
A nested instance held in a field over a `NameArray` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-string-at-depth-3 -->
A nested instance held in a field over a `String` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-record-at-depth-3 -->
A nested instance held in a field over a `Pad` record at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: field-array-at-depth-3 -->
A nested instance held in a field over a `NameArray` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var slot as Nested

	static function create(left A) returns Self
		return Self{slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-string-at-depth-2 -->
A nested instance replaced in a field over a `String` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellA.create(self.left))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-record-at-depth-2 -->
A nested instance replaced in a field over a `Pad` record at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellA.create(self.left))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-array-at-depth-2 -->
A nested instance replaced in a field over a `NameArray` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellA.create(left))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellA.create(self.left))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-string-at-depth-3 -->
A nested instance replaced in a field over a `String` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellCell.create(CellA.create(self.left)))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-record-at-depth-3 -->
A nested instance replaced in a field over a `Pad` record at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellCell.create(CellA.create(self.left)))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: replaced-array-at-depth-3 -->
A nested instance replaced in a field over a `NameArray` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A
	var slot as Nested

	static function create(left A) returns Self
		return Self{left: left, slot: Nested.create(CellCell.create(CellA.create(left)))}
	end 'create'

	function run() returns Tally
		self.slot = Nested.create(CellCell.create(CellA.create(self.left)))
		self.slot.bump(1)
		return self.slot.total()
	end 'run'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	print("{w.run()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-string-at-depth-2 -->
A nested instance returned over a `String` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-record-at-depth-2 -->
A nested instance returned over a `Pad` record at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-array-at-depth-2 -->
A nested instance returned over a `NameArray` at depth 2 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias Nested = Gen with CellA
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellA.create(self.left))
	end 'build'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-string-at-depth-3 -->
A nested instance returned over a `String` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'
end 'Wrap'

typealias Held = Wrap with String

function main() returns ExitCode
	let w = Held.create("alpha beta gamma delta epsilon zeta")
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-record-at-depth-3 -->
A nested instance returned over a `Pad` record at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

type Pad
	export var label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'
end 'Pad'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'
end 'Wrap'

typealias Held = Wrap with Pad

function main() returns ExitCode
	let w = Held.create(Pad.create("alpha beta gamma delta epsilon zeta"))
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: return-array-at-depth-3 -->
A nested instance returned over a `NameArray` at depth 3 is released with what it holds.
```maxon
typealias Tally = int(0 to u64.max)

typealias NameArray = Array with String

function names() returns NameArray
	let result = NameArray.create(0)
	result.push("alpha beta gamma delta epsilon zeta")
	result.push("alpha beta gamma delta epsilon zeta two")
	return result
end 'names'

type Cell uses V
	var item as V

	static function create(item V) returns Self
		return Self{item: item}
	end 'create'
end 'Cell'

type Gen uses T
	var held as T
	var seen as Tally

	static function create(held T) returns Self
		return Self{held: held, seen: 0}
	end 'create'

	function bump(by Tally)
		self.seen = self.seen + by
	end 'bump'

	function total() returns Tally
		return self.seen
	end 'total'
end 'Gen'

type Wrap uses A
	typealias CellA = Cell with A
	typealias CellCell = Cell with CellA
	typealias Nested = Gen with CellCell
	var left as A

	static function create(left A) returns Self
		return Self{left: left}
	end 'create'

	function build() returns Nested
		return Nested.create(CellCell.create(CellA.create(self.left)))
	end 'build'
end 'Wrap'

typealias Held = Wrap with NameArray

function main() returns ExitCode
	let w = Held.create(names())
	let g = w.build()
	g.bump(1)
	print("{g.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```
