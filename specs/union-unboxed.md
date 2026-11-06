---
feature: union-unboxed
status: experimental
keywords: [union, payload, unboxed, tagged pointer, ownership, drop, write-back]
category: ownership
---

# Unboxed Single-Pointer Unions

## Documentation

A `union` whose every case carries no payload or exactly one managed record is stored without a heap box:
one machine word holding the payload pointer and, in its low three bits, the case tag. Creating one
allocates nothing but the payload; dropping one releases the payload it holds.

A union is stored this way when all of these hold:

- it has at most seven cases, tagged by declaration position from one (tag zero is the empty word);
- it declares no `implements` clause;
- every case has no payload, or one whose type is a `String`, a `Character`, a struct, or an array, map or
  other generic-instance record;
- it is declared in source (the compiler's own request and reply unions are boxed).

Any other union keeps the heap box. The two forms behave alike: the same ownership, the same `match`, the same
diagnostics.

Assigning a match binding replaces the payload held by the matched union, so it writes the union itself.
The write must be able to reach the union: the scrutinee is a local `var`, a field of a `var` or of a record
parameter, or a parameter marked `mutable`. A scrutinee that is an unmarked parameter is E2013, which names
the parameter and the fix. Writing through the payload record in place (`c.n = c.n + 1`) is not an
assignment of the binding and stays legal on a parameter's payload.

## Tests

<!-- test: construct-and-match-a-string-payload -->
A single-`String` union is constructed, passed to a function and matched.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function describe(n Named) returns String
	return match n 'show'
		named(name) gives name
		anonymous gives "none"
	end 'show'
end 'describe'

function main() returns ExitCode
	let a = Named.named("hello world this is a long enough string to be heap")
	let b = Named.anonymous
	print("{describe(a)}\n")
	print("{describe(b)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello world this is a long enough string to be heap
none
```

<!-- test: a-copy-of-the-union-shares-the-payload-and-both-drop -->
Two names for one union each own a reference, and the payload is released once both are gone.
```maxon
typealias Mass = int(0 to 100)

type Body
	export var mass as Mass

	static function create(mass Mass) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Holder
	empty
	holding(body Body)
end 'Holder'

function mass(h Holder) returns Mass
	return match h 'kind'
		holding(b) gives b.mass
		empty gives 0
	end 'kind'
end 'mass'

function main() returns ExitCode
	let a = Holder.holding(Body.create(7))
	let b = a
	return (mass(a) + mass(b)) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: a-var-union-assigned-to-another-case-drops-the-old-payload -->
```maxon
typealias Mass = int(0 to 100)

type Body
	export var mass as Mass

	static function create(mass Mass) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Holder
	empty
	holding(body Body)
end 'Holder'

function main() returns ExitCode
	var h = Holder.holding(Body.create(3))
	h = Holder.holding(Body.create(5))
	h = Holder.empty
	h = Holder.holding(Body.create(9))
	return match h 'kind'
		holding(b) gives b.mass as ExitCode
		empty gives 0
	end 'kind'
end 'main'
```
```exitcode
9
```

<!-- test: write-back-replaces-the-payload-of-a-local-var -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	var v = Named.named("x{1}")

	match v 'update'
		named(name) then name = "yy{2}"
		anonymous then return 1
	end 'update'

	match v 'read'
		named(name) then return name.byteLength() as ExitCode
		anonymous then return 0
	end 'read'
end 'main'
```
```exitcode
3
```

<!-- test: write-back-through-a-field-of-a-var-record -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

type Card
	export var label as Named

	static function create() returns Self
		return Self{label: Named.named("abc")}
	end 'create'
end 'Card'

function lengthen(card Card)
	match card.label 'update'
		named(name) then name = "abcdef"
		anonymous then return
	end 'update'
end 'lengthen'

function main() returns ExitCode
	var card = Card.create()
	lengthen(card)

	match card.label 'read'
		named(name) then return name.byteLength() as ExitCode
		anonymous then return 0
	end 'read'
end 'main'
```
```exitcode
6
```

<!-- test: write-back-through-a-mutable-parameter -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function rename(n mutable Named)
	match n 'step'
		named(name) then name = "world!"
		anonymous then return
	end 'step'
end 'rename'

function main() returns ExitCode
	var n = Named.named("hi")
	rename(n)

	match n 'read'
		named(name) then return name.byteLength() as ExitCode
		anonymous then return 0
	end 'read'
end 'main'
```
```exitcode
6
```

<!-- test: error.payload-assignment-through-a-by-value-parameter -->
The parameter is passed by value, so the assignment could not reach the caller; the diagnostic names the
parameter and both fixes.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function rename(n Named)
	match n 'step'
		named(name) then name = "world"
		anonymous then return
	end 'step'
end 'rename'

function main() returns ExitCode
	var n = Named.named("hello")
	rename(n)
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/union-unboxed/error.payload-assignment-through-a-by-value-parameter.maxon:9:20: 'name' is a payload of the by-value parameter 'n', so assigning it cannot reach the caller's union; mark 'n' mutable or copy it into a local var
```

<!-- test: a-union-in-an-array-and-in-a-map -->
```maxon
typealias Mass = int(0 to 100)

type Body
	export var mass as Mass

	static function create(mass Mass) returns Self
		return Self{mass: mass}
	end 'create'
end 'Body'

union Holder
	empty
	holding(body Body)
end 'Holder'

typealias HolderArray = Array with Holder
typealias HolderMap = Map with (String, Holder)

function mass(h Holder) returns Mass
	return match h 'kind'
		holding(b) gives b.mass
		empty gives 0
	end 'kind'
end 'mass'

function main() returns ExitCode
	var all = HolderArray.create(0)
	all.push(Holder.holding(Body.create(4)))
	all.push(Holder.empty)
	all.push(Holder.holding(Body.create(6)))

	var byName = HolderMap.create()
	byName.upsert("a", value: Holder.holding(Body.create(10)))

	var total = 0 as Mass

	for h in all 'eachHolder'
		total = total + mass(h)
	end 'eachHolder'

	let found = try byName.get("a") otherwise return 99
	return (total + mass(found)) as ExitCode
end 'main'
```
```exitcode
20
```

<!-- test: a-generic-record-holds-a-union -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias NamedBox = Box with Named

function main() returns ExitCode
	let boxed = NamedBox.create(Named.named("generic"))

	return match boxed.item 'kind'
		named(name) gives name.byteLength() as ExitCode
		anonymous gives 0
	end 'kind'
end 'main'
```
```exitcode
7
```

<!-- test: an-eighth-case-keeps-the-box-and-the-behaviour -->
Eight cases exceed the seven tags a word can hold, so this union is boxed; it behaves as the word form does.
```maxon
union Wide
	c0
	c1
	c2
	c3
	c4
	c5
	c6
	c7
	text(value String)
end 'Wide'

function main() returns ExitCode
	let w = Wide.text("boxed")

	return match w 'kind'
		text(value) gives value.byteLength() as ExitCode
		c0 or
		c1 or
		c2 or
		c3 or
		c4 or
		c5 or
		c6 or
		c7 gives 0
	end 'kind'
end 'main'
```
```exitcode
5
```

<!-- test: a-seven-case-union-is-a-word -->
```maxon
union Narrow
	c0
	c1
	c2
	c3
	c4
	c5
	text(value String)
end 'Narrow'

function main() returns ExitCode
	let w = Narrow.text("word")

	return match w 'kind'
		text(value) gives value.byteLength() as ExitCode
		c0 or
		c1 or
		c2 or
		c3 or
		c4 or
		c5 gives 0
	end 'kind'
end 'main'
```
```exitcode
4
```

<!-- test: ordinal-and-name-read-the-tag -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	let a = Named.named("x")
	let b = Named.anonymous
	print("{a.name} {a.ordinal} {b.name} {b.ordinal}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
named 1 anonymous 0
```

<!-- test: clone-copies-the-payload -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	let a = Named.named("clone me")
	let b = a.clone()

	return match b 'kind'
		named(name) gives name.byteLength() as ExitCode
		anonymous gives 0
	end 'kind'
end 'main'
```
```exitcode
8
```

<!-- test: from-name-builds-a-payload-free-case -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function main() returns ExitCode
	let made = try Named.fromName("anonymous") otherwise return 9

	return match made 'kind'
		anonymous gives 0
		named gives 1
	end 'kind'
end 'main'
```
```exitcode
0
```

<!-- test: an-error-union-keeps-its-box -->
A union that implements `Error` is thrown by its box pointer, so it stays boxed and still carries its payload.
```maxon
union Failure implements Error
	none
	because(reason String)
end 'Failure'

function risky() throws Failure
	throw Failure.because("broken")
end 'risky'

function main() returns ExitCode
	try risky() otherwise (e) 'caught'
		return match e 'kind'
			because(reason) gives reason.byteLength() as ExitCode
			none gives 0
		end 'kind'
	end 'caught'

	return 1
end 'main'
```
```exitcode
6
```

<!-- test: a-payload-free-word-moved-out-of-a-field-is-fresh -->
A field filled only with payload-free words (here through a ternary of two cases) keeps its slot exclusive, so a
reader that swaps the word out hands the caller a value it may store into a `let` field.
```maxon
typealias Bit = int(0 to 1)

union Log
	discarded
	recorded(text String)
end 'Log'

function peek(l Log) returns Bit
	return match l 'kind'
		recorded gives 1
		discarded gives 0
	end 'kind'
end 'peek'

type Scanner
	var comments as Log

	static function create(flag bool) returns Self
		return Self{comments: Log.discarded if flag else Log.discarded}
	end 'create'

	function note() returns Bit
		return peek(self.comments)
	end 'note'

	function takeComments() returns Log
		var taken = self.comments
		self.comments = Log.discarded

		return taken
	end 'takeComments'
end 'Scanner'

type Lexed
	export let comments as Log

	static function create(comments Log) returns Self
		return Self{comments: comments}
	end 'create'
end 'Lexed'

function main() returns ExitCode
	var scanner = Scanner.create(true)
	let n = scanner.note()
	print("{n}")
	view log = scanner.takeComments()
	let lexed = Lexed.create(log)

	return match lexed.comments 'kind'
		recorded gives 1
		discarded gives 0
	end 'kind'
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: a-service-replies-with-a-payload-free-word-and-a-constructed-one -->
A service method that returns a payload-free case on one path and a constructed case on another replies with a value it solely owns on both.
```maxon
typealias Tally = int(0 to 1000)

type Counts
	export var lines = 0 as Tally

	static function of(content ByteArray) returns Self
		var counts = Self{}
		counts.lines = content.count() as Tally

		return counts
	end 'of'
end 'Counts'

union FileCount
	counted(counts Counts)
	unreadable
end 'FileCount'

type Counter
	static function create() returns Self
		return Self{}
	end 'create'

	export function count(empty bool) returns FileCount
		if empty 'nothingToCount'
			return FileCount.unreadable
		end 'nothingToCount'

		return FileCount.counted(Counts.of(ByteArray.create(3)))
	end 'count'
end 'Counter'

function main() returns ExitCode
	let h = spawn Counter.create()
	let gone = try await h.count(true) otherwise FileCount.unreadable
	let there = try await h.count(false) otherwise FileCount.unreadable

	let first = match gone 'kind'
		counted(c) gives c.lines as ExitCode
		unreadable gives 7
	end 'kind'

	let second = match there 'kind'
		counted(c) gives c.lines as ExitCode
		unreadable gives 7
	end 'kind'

	print("{first} {second}\n")

	return 0
end 'main'
```
```stdout
7 3
```
```exitcode
0
```

<!-- test: an-all-let-union-with-a-struct-payload-is-shared-with-a-service -->
Every payload of `Shape` is a `let`, so the send shares the word with the service while `w` still views it, and the struct payload's header is met by one share walk. Both sides read the payload afterwards.
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord
	export let y as Coord
	export let label as String

	static function create(x Coord, y Coord, label String) returns Self
		return Self{x: x, y: y, label: label}
	end 'create'
end 'Point'

union Shape
	dot(let at Point)
	nothing
end 'Shape'

type Store
	var seen = 0 as Coord

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(s Shape) returns Coord
		self.seen = self.seen + 1

		return match s 'shape'
			dot(at) gives at.x + at.y + at.label.byteLength() as Coord
			nothing gives 0
		end 'shape'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var s = Shape.dot(Point.create(1, y: 2, label: "ab{1}"))
	s = Shape.dot(Point.create(10, y: 20, label: "cde{2}"))
	view w = s
	let r = try await h.keep(s) otherwise 0

	let mine = match w 'shape'
		dot(at) gives at.x + at.y + at.label.byteLength() as Coord
		nothing gives 0
	end 'shape'

	let again = try await h.keep(s) otherwise 0

	print("{r} {mine} {again}\n")

	return 0
end 'main'
```
```stdout
34 34 34
```
```exitcode
0
```

<!-- test: an-all-let-union-with-a-mutable-struct-payload-is-shared-with-a-service -->
`Shape`'s payloads are `let`, but the struct they hold has a `var` field, so the payload walk meets its record as owned state while the word is shared with the service and viewed by `w`.
```maxon
typealias Coord = int(0 to 1000)

type Cell
	export var n as Coord
	export let label as String

	static function create(n Coord, label String) returns Self
		return Self{n: n, label: label}
	end 'create'
end 'Cell'

union Shape
	cell(let c Cell)
	nothing
end 'Shape'

type Store
	var seen = 0 as Coord

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(s Shape) returns Coord
		self.seen = self.seen + 1

		return match s 'shape'
			cell(c) gives c.n + c.label.byteLength() as Coord
			nothing gives 0
		end 'shape'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	var s = Shape.cell(Cell.create(5, label: "ab{1}"))
	s = Shape.cell(Cell.create(7, label: "cde{2}"))
	view w = s
	let r = try await h.keep(s) otherwise 0

	let mine = match w 'shape'
		cell(c) gives c.n + c.label.byteLength() as Coord
		nothing gives 0
	end 'shape'

	print("{r} {mine}\n")

	return 0
end 'main'
```
```stdout
11 11
```
```exitcode
0
```

<!-- test: an-all-let-union-is-shared-while-several-words-hold-a-mutable-struct-payload -->
Three words hold one `Shape` payload while a fourth reference travels with the send. The share walk meets the payload record once, as a view of an immutable union, and the owners outside the graph are tolerated exactly as they were when the payload sat behind a box.
```maxon
typealias Coord = int(0 to 1000)

type Cell
	export var n as Coord
	export let label as String

	static function create(n Coord, label String) returns Self
		return Self{n: n, label: label}
	end 'create'
end 'Cell'

union Shape
	cell(let c Cell)
	nothing
end 'Shape'

type Store
	var seen = 0 as Coord

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(s Shape) returns Coord
		self.seen = self.seen + 1

		return match s 'shape'
			cell(c) gives c.n + c.label.byteLength() as Coord
			nothing gives 0
		end 'shape'
	end 'keep'
end 'Store'

function weigh(s Shape) returns Coord
	return match s 'shape'
		cell(c) gives c.n + c.label.byteLength() as Coord
		nothing gives 0
	end 'shape'
end 'weigh'

function main() returns ExitCode
	let h = spawn Store.create()
	var s = Shape.cell(Cell.create(1, label: "x{0}"))
	s = Shape.cell(Cell.create(5, label: "ab{1}"))
	view a = s
	view b = s
	let r = try await h.keep(s) otherwise 0

	print("{r} {weigh(a)} {weigh(b)} {weigh(s)}\n")

	return 0
end 'main'
```
```stdout
8 8 8 8
```
```exitcode
0
```

<!-- test: an-all-let-union-is-shared-while-several-words-hold-a-string-payload -->
```maxon
typealias Count = int(0 to 1000)

union Note
	text(let body String)
	blank
end 'Note'

type Store
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(n Note) returns Count
		self.seen = self.seen + 1

		return match n 'note'
			text(body) gives body.byteLength() as Count
			blank gives 0
		end 'note'
	end 'keep'
end 'Store'

function weigh(n Note) returns Count
	return match n 'note'
		text(body) gives body.byteLength() as Count
		blank gives 0
	end 'note'
end 'weigh'

function main() returns ExitCode
	let h = spawn Store.create()
	var n = Note.text("x{0}")
	n = Note.text("abc{1}")
	view a = n
	view b = n
	let r = try await h.keep(n) otherwise 0

	print("{r} {weigh(a)} {weigh(b)} {weigh(n)}\n")

	return 0
end 'main'
```
```stdout
4 4 4 4
```
```exitcode
0
```

<!-- test: an-all-let-union-with-an-array-payload-is-shared-with-a-service -->
The payloads are a `ByteArray` and an `Array` of `String`, each sent to a service.
```maxon
typealias Count = int(0 to 10000)
typealias WordArray = Array with String

union Blob
	bytes(let data ByteArray)
	words(let list WordArray)
	nothing
end 'Blob'

type Store
	var seen = 0 as Count

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(b Blob) returns Count
		self.seen = self.seen + 1

		return match b 'blob'
			bytes(data) gives data.count() as Count
			words(list) gives list.count() as Count
			nothing gives 0
		end 'blob'
	end 'keep'
end 'Store'

function weigh(b Blob) returns Count
	return match b 'blob'
		bytes(data) gives data.count() as Count
		words(list) gives list.count() as Count
		nothing gives 0
	end 'blob'
end 'weigh'

function nineBytes() returns ByteArray
	var data = ByteArray.create(0)
	data.resize(9)

	return data
end 'nineBytes'

function twoWords() returns WordArray
	var list = WordArray.create(0)
	list.push("one{1}")
	list.push("two{2}")

	return list
end 'twoWords'

function main() returns ExitCode
	let h = spawn Store.create()
	var blob = Blob.nothing
	blob = Blob.bytes(nineBytes())
	var other = Blob.nothing
	other = Blob.words(twoWords())
	let before = weigh(blob)
	let r = try await h.keep(blob) otherwise 0
	let q = try await h.keep(other) otherwise 0

	print("{before} {r} {q}\n")

	return 0
end 'main'
```
```stdout
9 9 2
```
```exitcode
0
```

<!-- test: error.payload-assignment-to-a-union-nothing-holds -->
The scrutinee is a temporary, so the assignment could not reach any union; it is refused.
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function make() returns Named
	return Named.named("hello")
end 'make'

function main() returns ExitCode
	match make() 'step'
		named(name) then name = "world"
		anonymous then return 1
	end 'step'

	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/union-unboxed/error.payload-assignment-to-a-union-nothing-holds.maxon:13:20: cannot assign to immutable variable: 'name'
```

<!-- test: error.payload-assignment-to-a-boxed-union-nothing-holds -->
The refusal does not depend on the union's representation: a union too wide for a word is refused the same way.
```maxon
union Wide
	c0
	c1
	c2
	c3
	c4
	c5
	c6
	c7
	text(value String)
end 'Wide'

function make() returns Wide
	return Wide.text("hello")
end 'make'

function main() returns ExitCode
	match make() 'step'
		text(value) then value = "world"
		c0 or
		c1 or
		c2 or
		c3 or
		c4 or
		c5 or
		c6 or
		c7 then return 1
	end 'step'

	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/union-unboxed/error.payload-assignment-to-a-boxed-union-nothing-holds.maxon:20:20: cannot assign to immutable variable: 'value'
```

<!-- test: a-match-on-a-temporary-word-gives-its-payload -->
```maxon
union Named
	anonymous
	named(name String)
end 'Named'

function make(long bool) returns Named
	return Named.named("hello") if long else Named.anonymous
end 'make'

function main() returns ExitCode
	let a = match make(true) 'read'
		named(name) gives name.byteLength()
		anonymous gives 0
	end 'read'
	let b = match make(false) 'read'
		named(name) gives name.byteLength()
		anonymous gives 0
	end 'read'

	return (a + b) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: every-stored-tag-binds-its-own-payload-through-both-dispatch-shapes -->
`Wide` fills all seven stored tags, so its seven-interval `match` dispatches through a search or a table, and the payload cases at stored tags three to six bind a struct, a `ByteArray`, a `String` and a struct. `Trio` has three intervals and dispatches through a compare chain. The values are built out of declaration order, so a binding that read the wrong tag's payload would hand the arm a misaligned pointer.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export let x as Integer
	export let y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

union Wide
	empty
	text(value String)
	point(at Point)
	bytes(data ByteArray)
	label(value String)
	spot(at Point)
	blob(data ByteArray)
end 'Wide'

union Trio
	first(value String)
	second(at Point)
	third(data ByteArray)
end 'Trio'

function bytesOf(count Integer) returns ByteArray
	var data = ByteArray.create(0)
	data.resize(count as ElementIndex)

	return data
end 'bytesOf'

function weighWide(w Wide) returns Integer
	return match w 'wide'
		empty gives 0
		text(value) gives value.byteLength() as Integer
		point(at) gives at.x + at.y
		bytes(data) gives data.count() as Integer
		label(value) gives value.byteLength() as Integer + 100
		spot(at) gives at.x * at.y
		blob(data) gives data.count() as Integer + 200
	end 'wide'
end 'weighWide'

function weighTrio(t Trio) returns Integer
	return match t 'trio'
		first(value) gives value.byteLength() as Integer
		second(at) gives at.x * 10 + at.y
		third(data) gives data.count() as Integer
	end 'trio'
end 'weighTrio'

function main() returns ExitCode
	let spot = Wide.spot(Point.create(7, y: 8))
	let blob = Wide.blob(bytesOf(11))
	let text = Wide.text("ab{1}")
	let empty = Wide.empty
	let label = Wide.label("hello{2}")
	let point = Wide.point(Point.create(4, y: 5))
	let bytes = Wide.bytes(bytesOf(6))

	print("{weighWide(spot)} {weighWide(blob)} {weighWide(text)} {weighWide(empty)} {weighWide(label)} {weighWide(point)} {weighWide(bytes)}\n")

	let third = Trio.third(bytesOf(4))
	let first = Trio.first("xy{3}")
	let second = Trio.second(Point.create(2, y: 3))

	print("{weighTrio(third)} {weighTrio(first)} {weighTrio(second)}\n")

	return 0
end 'main'
```
```stdout
56 211 3 0 106 9 6
4 3 23
```
```exitcode
0
```

<!-- test: a-default-throws-over-a-word-union-still-tests-its-last-listed-case -->
A `default throws` leaves the miss reachable, so the match keeps the test of its last listed case: `third`, which no arm names, reaches the default and throws instead of running `second`'s arm on its payload.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export let x as Integer
	export let y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

union Trio
	first(value String)
	second(at Point)
	third(data ByteArray)
end 'Trio'

enum Unmatched
	unhandled
end 'Unmatched'

function weigh(t Trio) returns Integer throws Unmatched
	return match t 'trio'
		first(value) gives value.byteLength() as Integer
		second(at) gives at.x * 10 + at.y
		default throws Unmatched.unhandled
	end 'trio'
end 'weigh'

function main() returns ExitCode
	let a = try weigh(Trio.first("xy{3}")) otherwise -1
	let b = try weigh(Trio.second(Point.create(2, y: 3))) otherwise -1
	let c = try weigh(Trio.third(ByteArray.create(0))) otherwise -1

	print("{a} {b} {c}\n")

	return 0
end 'main'
```
```stdout
3 23 -1
```
```exitcode
0
```

<!-- test: an-exhaustive-plain-enum-match-names-every-case-once -->
The twin of the word-union chain over a plain `enum`: the last case's arm is reached without a test of its own, and each of the three values still reaches its own arm.
```maxon
enum Level
	low
	mid
	high
end 'Level'

function rank(level Level) returns ExitCode
	return match level 'level'
		low gives 1
		mid gives 2
		high gives 3
	end 'level'
end 'rank'

function main() returns ExitCode
	print("{rank(Level.high)} {rank(Level.low)} {rank(Level.mid)}\n")

	return 0
end 'main'
```
```stdout
3 1 2
```
```exitcode
0
```
