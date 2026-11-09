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

- it has at most seven cases that carry a payload and at most 32767 cases in all. A union of at most seven
  cases tags each by declaration position from one (tag zero is the empty word). A larger one gives its
  payload cases the tags one to seven in declaration order and its payload-free cases the keys after them,
  in declaration order; a payload-free case whose key is past seven is stored as that key shifted left by 48,
  a word no heap address can equal, so only the payload cases count against the seven tags;
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

<!-- test: an-eighth-payload-case-keeps-the-box-and-the-behaviour -->
Eight cases that carry a payload exceed the seven tags a word can hold, so this union is boxed; it behaves as the word form does.
```maxon
union Wide
	p0(value String)
	p1(value String)
	p2(value String)
	p3(value String)
	p4(value String)
	p5(value String)
	p6(value String)
	p7(value String)
end 'Wide'

function main() returns ExitCode
	let w = Wide.p7("boxed")

	return match w 'kind'
		p7(value) gives value.byteLength() as ExitCode
		p0 or
		p1 or
		p2 or
		p3 or
		p4 or
		p5 or
		p6 gives 0
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
		let taken = self.comments
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

	export function count(empty bool) returns isolated FileCount
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
Every payload of `Shape` is a `let` and `Point` is value-like (every field is a `let` of a value type), so `Shape` counts as a value: it needs no `freeze` (a `freeze` of it is E3200), and the send hands the word to the service by move, as it hands over a `String`, so `s` is read before the send and not after. The struct payload's header is met by one share walk, and the service reads the payload.
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

	export function keep(s immutable Shape) returns Coord
		self.seen = self.seen + 1

		return match s 'shape'
			dot(at) gives at.x + at.y + at.label.byteLength() as Coord
			nothing gives 0
		end 'shape'
	end 'keep'
end 'Store'

function weigh(s Shape) returns Coord
	return match s 'shape'
		dot(at) gives at.x + at.y + at.label.byteLength() as Coord
		nothing gives 0
	end 'shape'
end 'weigh'

function main() returns ExitCode
	let h = spawn Store.create()
	let s = Shape.dot(Point.create(10, y: 20, label: "cde{2}"))
	let mine = weigh(s)
	let r = try await h.keep(s) otherwise 0

	print("{r} {mine}\n")

	return 0
end 'main'
```
```stdout
34 34
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

	export function keep(s immutable Shape) returns Coord
		self.seen = self.seen + 1

		return match s 'shape'
			cell(c) gives c.n + c.label.byteLength() as Coord
			nothing gives 0
		end 'shape'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let s = freeze Shape.cell(Cell.create(7, label: "cde{2}"))
	let w = s
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

	export function keep(s immutable Shape) returns Coord
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
	let s = freeze Shape.cell(Cell.create(5, label: "ab{1}"))
	let a = s
	let b = s
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

	export function keep(n immutable Note) returns Count
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
	let n = Note.text("abc{1}")
	view a = n
	view b = n
	let wa = weigh(a)
	let wb = weigh(b)
	let wn = weigh(n)
	let r = try await h.keep(n) otherwise 0

	print("{r} {wa} {wb} {wn}\n")

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

	export function keep(b immutable Blob) returns Count
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
	var blob = freeze Blob.nothing
	blob = freeze Blob.bytes(nineBytes())
	var other = freeze Blob.nothing
	other = freeze Blob.words(twoWords())
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
error E2013: specs/union-unboxed/error.payload-assignment-to-a-union-nothing-holds.maxon:13:20: 'name' is a let and cannot be reassigned
```

<!-- test: error.payload-assignment-to-a-boxed-union-nothing-holds -->
The refusal does not depend on the union's representation: a union too wide for a word is refused the same way.
```maxon
union Wide
	p0(value String)
	p1(value String)
	p2(value String)
	p3(value String)
	p4(value String)
	p5(value String)
	p6(value String)
	p7(value String)
end 'Wide'

function make() returns Wide
	return Wide.p7("hello")
end 'make'

function main() returns ExitCode
	match make() 'step'
		p7(value) then value = "world"
		p0 or
		p1 or
		p2 or
		p3 or
		p4 or
		p5 or
		p6 then return 1
	end 'step'

	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/union-unboxed/error.payload-assignment-to-a-boxed-union-nothing-holds.maxon:19:18: 'value' is a let and cannot be reassigned
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

<!-- test: payload-free-cases-past-the-seventh-key-are-built-and-matched-without-a-box -->
<!-- MmTrace -->
`Wide` has seven cases that carry a payload and four that carry none. Only the payload cases count against the seven tags, so it is a word: building the four payload-free cases through a call and matching them allocates nothing, and the empty trace below is what says so. A box would show a `Wide` allocation per call.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Wide
	idle
	text(value String)
	point(at Point)
	bytes(data ByteArray)
	done
	label(value String)
	spot(at Point)
	blob(data ByteArray)
	failed
	other(at Point)
	skipped
end 'Wide'

function pick(n Weight) returns Wide
	return match n 'pick'
		0 gives Wide.idle
		1 gives Wide.done
		2 gives Wide.failed
		default gives Wide.skipped
	end 'pick'
end 'pick'

function weigh(w Wide) returns Weight
	return match w 'wide'
		idle gives 1
		done gives 20
		failed gives 30
		skipped gives 40
		text(value) gives value.byteLength() as Weight + 100
		point(at) gives at.x
		bytes(data) gives data.count() as Weight + 200
		label(value) gives value.byteLength() as Weight + 300
		spot(at) gives at.x * 2
		blob(data) gives data.count() as Weight + 400
		other(at) gives at.x + 500
	end 'wide'
end 'weigh'

function main() returns ExitCode
	return (weigh(pick(2)) + weigh(pick(0)) + weigh(pick(3)) + weigh(pick(1))) as ExitCode
end 'main'
```
```exitcode
91
```
```mm-trace

```

<!-- test: every-case-of-a-wide-union-binds-its-own-payload -->
<!-- MmTrace -->
The values are built out of declaration order, so a binding that read the wrong key's payload, or a payload-free case that read as a payload one, would hand the arm a misaligned pointer or the wrong weight. Each of the eleven cases is matched, and `main` counts the ones that answered wrongly.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Wide
	idle
	text(value String)
	point(at Point)
	bytes(data ByteArray)
	done
	label(value String)
	spot(at Point)
	blob(data ByteArray)
	failed
	other(at Point)
	skipped
end 'Wide'

function weigh(w Wide) returns Weight
	return match w 'wide'
		idle gives 1
		done gives 20
		failed gives 30
		skipped gives 40
		text(value) gives value.byteLength() as Weight + 100
		point(at) gives at.x
		bytes(data) gives data.count() as Weight + 200
		label(value) gives value.byteLength() as Weight + 300
		spot(at) gives at.x * 2
		blob(data) gives data.count() as Weight + 400
		other(at) gives at.x + 500
	end 'wide'
end 'weigh'

function expect(actual Weight, wanted Weight, failures ExitCode) returns ExitCode
	return failures if actual == wanted else failures + 1
end 'expect'

function main() returns ExitCode
	let spot = Wide.spot(Point.create(7, label: "p"))
	let blob = Wide.blob(ByteArray.create(11))
	let failed = Wide.failed
	let text = Wide.text("ab")
	let idle = Wide.idle
	let label = Wide.label("hello")
	let skipped = Wide.skipped
	let point = Wide.point(Point.create(4, label: "p"))
	let bytes = Wide.bytes(ByteArray.create(6))
	let done = Wide.done
	let other = Wide.other(Point.create(2, label: "p"))

	var failures = 0 as ExitCode
	failures = expect(weigh(spot), wanted: 14, failures: failures)
	failures = expect(weigh(blob), wanted: 411, failures: failures)
	failures = expect(weigh(failed), wanted: 30, failures: failures)
	failures = expect(weigh(text), wanted: 102, failures: failures)
	failures = expect(weigh(idle), wanted: 1, failures: failures)
	failures = expect(weigh(label), wanted: 305, failures: failures)
	failures = expect(weigh(skipped), wanted: 40, failures: failures)
	failures = expect(weigh(point), wanted: 4, failures: failures)
	failures = expect(weigh(bytes), wanted: 206, failures: failures)
	failures = expect(weigh(done), wanted: 20, failures: failures)
	failures = expect(weigh(other), wanted: 502, failures: failures)

	return failures
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc StringRecord #1 size=58
mm_alloc Point #2 size=16
mm_incref StringRecord #1 rc=2
mm_alloc StringRecord #3 size=58
mm_decref StringRecord #1 rc=1
mm_decref StringRecord #1 rc=0
mm_free StringRecord #1
mm_alloc ArrayRecord #4 size=75
mm_alloc StringRecord #5 size=59
mm_alloc StringRecord #6 size=62
mm_alloc StringRecord #7 size=58
mm_alloc Point #8 size=16
mm_incref StringRecord #7 rc=2
mm_alloc StringRecord #9 size=58
mm_decref StringRecord #7 rc=1
mm_decref StringRecord #7 rc=0
mm_free StringRecord #7
mm_alloc ArrayRecord #10 size=70
mm_alloc StringRecord #11 size=58
mm_alloc Point #12 size=16
mm_incref StringRecord #11 rc=2
mm_alloc StringRecord #13 size=58
mm_decref StringRecord #11 rc=1
mm_decref StringRecord #11 rc=0
mm_free StringRecord #11
mm_incref Point #2 rc=2
mm_decref Point #2 rc=1
mm_incref ArrayRecord #4 rc=2
mm_decref ArrayRecord #4 rc=1
mm_incref StringRecord #5 rc=2
mm_decref StringRecord #5 rc=1
mm_incref StringRecord #6 rc=2
mm_decref StringRecord #6 rc=1
mm_incref Point #8 rc=2
mm_decref Point #8 rc=1
mm_incref ArrayRecord #10 rc=2
mm_decref ArrayRecord #10 rc=1
mm_incref Point #12 rc=2
mm_decref Point #12 rc=1
mm_decref Point #12 rc=0
mm_decref StringRecord #13 rc=0
mm_free StringRecord #13
mm_free Point #12
mm_decref ArrayRecord #10 rc=0
mm_free ArrayRecord #10
mm_decref Point #8 rc=0
mm_decref StringRecord #9 rc=0
mm_free StringRecord #9
mm_free Point #8
mm_decref StringRecord #6 rc=0
mm_free StringRecord #6
mm_decref StringRecord #5 rc=0
mm_free StringRecord #5
mm_decref ArrayRecord #4 rc=0
mm_free ArrayRecord #4
mm_decref Point #2 rc=0
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_free Point #2
```

<!-- test: ordinal-and-name-read-every-case-of-a-union-with-payload-free-keys -->
<!-- MmTrace -->
`Mixed` declares its payload-free cases around its two payload cases, so the keys differ from the declaration order and the payload-free cases past the seventh key are stored apart from the tag bits. `.ordinal` and `.name` answer the declaration position and the declared name for every case.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Mixed
	first
	second
	held(value String)
	third
	fourth
	fifth
	sixth
	kept(at Point)
	seventh
	eighth
end 'Mixed'

function show(m Mixed)
	print("{m.ordinal} {m.name}\n")
end 'show'

function main() returns ExitCode
	show(Mixed.eighth)
	show(Mixed.kept(Point.create(1, label: "p")))
	show(Mixed.first)
	show(Mixed.sixth)
	show(Mixed.third)
	show(Mixed.held("x"))
	show(Mixed.seventh)
	show(Mixed.second)
	show(Mixed.fifth)
	show(Mixed.fourth)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
9 eighth
7 kept
0 first
6 sixth
3 third
2 held
8 seventh
1 second
5 fifth
4 fourth
```
```mm-trace
mm_alloc InterpolationScratch #1 size=20
mm_alloc StringRecord #2 size=66
mm_decref InterpolationScratch #1 rc=0
mm_free InterpolationScratch #1
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
mm_alloc StringRecord #3 size=58
mm_alloc Point #4 size=16
mm_incref StringRecord #3 rc=2
mm_alloc StringRecord #5 size=58
mm_decref StringRecord #3 rc=1
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_alloc InterpolationScratch #6 size=20
mm_alloc StringRecord #7 size=64
mm_decref InterpolationScratch #6 rc=0
mm_free InterpolationScratch #6
mm_decref StringRecord #7 rc=0
mm_free StringRecord #7
mm_decref Point #4 rc=0
mm_decref StringRecord #5 rc=0
mm_free StringRecord #5
mm_free Point #4
mm_alloc InterpolationScratch #8 size=20
mm_alloc StringRecord #9 size=65
mm_decref InterpolationScratch #8 rc=0
mm_free InterpolationScratch #8
mm_decref StringRecord #9 rc=0
mm_free StringRecord #9
mm_alloc InterpolationScratch #10 size=20
mm_alloc StringRecord #11 size=65
mm_decref InterpolationScratch #10 rc=0
mm_free InterpolationScratch #10
mm_decref StringRecord #11 rc=0
mm_free StringRecord #11
mm_alloc InterpolationScratch #12 size=20
mm_alloc StringRecord #13 size=65
mm_decref InterpolationScratch #12 rc=0
mm_free InterpolationScratch #12
mm_decref StringRecord #13 rc=0
mm_free StringRecord #13
mm_alloc StringRecord #14 size=58
mm_alloc InterpolationScratch #15 size=20
mm_alloc StringRecord #16 size=64
mm_decref InterpolationScratch #15 rc=0
mm_free InterpolationScratch #15
mm_decref StringRecord #16 rc=0
mm_free StringRecord #16
mm_decref StringRecord #14 rc=0
mm_free StringRecord #14
mm_alloc InterpolationScratch #17 size=20
mm_alloc StringRecord #18 size=67
mm_decref InterpolationScratch #17 rc=0
mm_free InterpolationScratch #17
mm_decref StringRecord #18 rc=0
mm_free StringRecord #18
mm_alloc InterpolationScratch #19 size=20
mm_alloc StringRecord #20 size=66
mm_decref InterpolationScratch #19 rc=0
mm_free InterpolationScratch #19
mm_decref StringRecord #20 rc=0
mm_free StringRecord #20
mm_alloc InterpolationScratch #21 size=20
mm_alloc StringRecord #22 size=65
mm_decref InterpolationScratch #21 rc=0
mm_free InterpolationScratch #21
mm_decref StringRecord #22 rc=0
mm_free StringRecord #22
mm_alloc InterpolationScratch #23 size=20
mm_alloc StringRecord #24 size=66
mm_decref InterpolationScratch #23 rc=0
mm_free InterpolationScratch #23
mm_decref StringRecord #24 rc=0
mm_free StringRecord #24
```

<!-- test: a-wide-union-var-is-reassigned-and-written-back -->
<!-- MmTrace -->
A `var` moves between payload cases, payload-free cases of both storages, and back, each assignment releasing what the old value held; then a payload is written back through a `match`, which rebuilds the word with the stored tag.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Wide
	idle
	text(value String)
	point(at Point)
	bytes(data ByteArray)
	done
	label(value String)
	spot(at Point)
	blob(data ByteArray)
	failed
	other(at Point)
	skipped
end 'Wide'

function weigh(w Wide) returns Weight
	return match w 'wide'
		idle or
		done or
		failed or
		skipped gives 1
		text(value) gives value.byteLength() as Weight
		label(value) gives value.byteLength() as Weight
		point(at) gives at.x
		spot(at) gives at.x
		other(at) gives at.x
		bytes(data) gives data.count() as Weight
		blob(data) gives data.count() as Weight
	end 'wide'
end 'weigh'

function main() returns ExitCode
	var w = Wide.text("one")
	w = Wide.point(Point.create(2, label: "p"))
	w = Wide.idle
	w = Wide.bytes(ByteArray.create(5))
	w = Wide.skipped
	w = Wide.failed
	w = Wide.bytes(ByteArray.create(5))

	match w 'grow'
		bytes(data) then data = ByteArray.create(9)
		idle or
		text or
		point or
		done or
		label or
		spot or
		blob or
		failed or
		other or
		skipped then return 100
	end 'grow'

	let grown = weigh(w)
	w = Wide.done

	return (grown + weigh(w)) as ExitCode
end 'main'
```
```exitcode
10
```
```mm-trace
mm_alloc StringRecord #1 size=60
mm_alloc StringRecord #2 size=58
mm_alloc Point #3 size=16
mm_incref StringRecord #2 rc=2
mm_alloc StringRecord #4 size=58
mm_decref StringRecord #2 rc=1
mm_decref StringRecord #2 rc=0
mm_free StringRecord #2
mm_decref StringRecord #1 rc=0
mm_free StringRecord #1
mm_decref Point #3 rc=0
mm_decref StringRecord #4 rc=0
mm_free StringRecord #4
mm_free Point #3
mm_alloc ArrayRecord #5 size=69
mm_decref ArrayRecord #5 rc=0
mm_free ArrayRecord #5
mm_alloc ArrayRecord #6 size=69
mm_incref ArrayRecord #6 rc=2
mm_alloc ArrayRecord #7 size=73
mm_decref ArrayRecord #6 rc=1
mm_incref ArrayRecord #7 rc=2
mm_decref ArrayRecord #6 rc=0
mm_free ArrayRecord #6
mm_decref ArrayRecord #7 rc=1
mm_incref ArrayRecord #7 rc=2
mm_decref ArrayRecord #7 rc=1
mm_decref ArrayRecord #7 rc=0
mm_free ArrayRecord #7
```

<!-- test: or-arms-and-default-over-the-payload-free-cases-of-a-wide-union -->
<!-- MmTrace -->
An `or` arm that names payload-free cases of both storages folds into compares of their keys, and a `default throws` keeps the test of the last case it lists: `skipped` and `text` reach the default, `done` and `failed` their arms.
```maxon
typealias Weight = int(-1 to 100000)

union Wide
	idle
	text(value String)
	done
	label(value String)
	failed
	other(value String)
	skipped
	eight
	nine
	ten
	eleven
end 'Wide'

enum Unmatched
	unhandled
end 'Unmatched'

function bucket(w Wide) returns Weight
	return match w 'bucket'
		idle or
		done or
		failed gives 1
		skipped or
		eight or
		nine gives 2
		ten or
		eleven gives 3
		text or
		label or
		other gives 4
	end 'bucket'
end 'bucket'

function partial(w Wide) returns Weight throws Unmatched
	return match w 'partial'
		done gives 20
		failed gives 30
		default throws Unmatched.unhandled
	end 'partial'
end 'partial'

function main() returns ExitCode
	let a = bucket(Wide.eleven) * 1000 + bucket(Wide.idle) * 100 + bucket(Wide.label("x")) * 10 + bucket(Wide.nine)

	print("{a}\n")

	let b = try partial(Wide.done) otherwise -1
	let c = try partial(Wide.failed) otherwise -1
	let d = try partial(Wide.skipped) otherwise -1
	let e = try partial(Wide.text("t")) otherwise -1

	print("{b} {c} {d} {e}\n")

	return 0
end 'main'
```
```stdout
3142
20 30 -1 -1
```
```exitcode
0
```
```mm-trace
mm_alloc StringRecord #1 size=58
mm_decref StringRecord #1 rc=0
mm_free StringRecord #1
mm_alloc InterpolationScratch #2 size=21
mm_alloc StringRecord #3 size=62
mm_decref InterpolationScratch #2 rc=0
mm_free InterpolationScratch #2
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_alloc StringRecord #4 size=58
mm_decref StringRecord #4 rc=0
mm_free StringRecord #4
mm_alloc InterpolationScratch #5 size=21
mm_alloc InterpolationScratch #6 size=21
mm_alloc InterpolationScratch #7 size=21
mm_alloc InterpolationScratch #8 size=21
mm_alloc StringRecord #9 size=69
mm_decref InterpolationScratch #5 rc=0
mm_free InterpolationScratch #5
mm_decref InterpolationScratch #6 rc=0
mm_free InterpolationScratch #6
mm_decref InterpolationScratch #7 rc=0
mm_free InterpolationScratch #7
mm_decref InterpolationScratch #8 rc=0
mm_free InterpolationScratch #8
mm_decref StringRecord #9 rc=0
mm_free StringRecord #9
```

<!-- test: from-name-builds-the-payload-free-cases-of-a-wide-union -->
<!-- MmTrace -->
`fromName` builds a payload-free case of either storage and refuses the name of a payload case.
```maxon
union Wide
	idle
	text(value String)
	done
	label(value String)
	failed
	other(value String)
	skipped
	eight
	nine
	ten
end 'Wide'

function code(w Wide) returns ExitCode
	return match w 'code'
		idle gives 1
		done gives 2
		failed gives 3
		skipped gives 4
		eight gives 5
		nine gives 6
		ten gives 7
		text or
		label or
		other gives 99
	end 'code'
end 'code'

function lookup(name String) returns ExitCode
	let made = try Wide.fromName(name) otherwise return 0

	return code(made)
end 'lookup'

function main() returns ExitCode
	let sum = lookup("idle") + lookup("done") + lookup("failed") + lookup("skipped") + lookup("eight") + lookup("nine") + lookup("ten") + lookup("text") + lookup("nothing")

	return sum
end 'main'
```
```exitcode
28
```
```mm-trace

```

<!-- test: a-wide-union-in-an-array-is-cloned-and-dropped -->
<!-- MmTrace -->
Elements of every storage sit in one array, which is walked, then a payload case and a payload-free case are cloned. The clone of a payload case owns a copy of its payload, and the clone of a payload-free case is the same word.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Wide
	idle
	text(value String)
	point(at Point)
	done
	label(value String)
	failed
	other(at Point)
	skipped
	eight
	nine
end 'Wide'

typealias WideArray = Array with Wide

function weigh(w Wide) returns Weight
	return match w 'wide'
		idle or
		done or
		failed or
		skipped or
		eight or
		nine gives 1
		text(value) gives value.byteLength() as Weight
		label(value) gives value.byteLength() as Weight
		point(at) gives at.x
		other(at) gives at.x
	end 'wide'
end 'weigh'

function main() returns ExitCode
	var all = WideArray.create(0)
	all.push(Wide.nine)
	all.push(Wide.point(Point.create(7, label: "p")))
	all.push(Wide.idle)
	all.push(Wide.label("four"))
	all.push(Wide.skipped)
	all.push(Wide.other(Point.create(30, label: "p")))

	var total = 0 as Weight

	for w in all 'eachWide'
		total = total + weigh(w)
	end 'eachWide'

	let payload = Wide.point(Point.create(11, label: "p"))
	let copy = payload.clone()
	let bare = Wide.eight
	let again = bare.clone()

	return (total + weigh(copy) + weigh(again)) as ExitCode
end 'main'
```
```exitcode
56
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc ElementBuffer #2 size=32
mm_alloc StringRecord #3 size=58
mm_alloc Point #4 size=16
mm_incref StringRecord #3 rc=2
mm_alloc StringRecord #5 size=58
mm_decref StringRecord #3 rc=1
mm_decref StringRecord #3 rc=0
mm_free StringRecord #3
mm_alloc StringRecord #6 size=61
mm_alloc ElementBuffer #7 size=64
mm_decref ElementBuffer #2 rc=0
mm_free ElementBuffer #2
mm_alloc StringRecord #8 size=58
mm_alloc Point #9 size=16
mm_incref StringRecord #8 rc=2
mm_alloc StringRecord #10 size=58
mm_decref StringRecord #8 rc=1
mm_decref StringRecord #8 rc=0
mm_free StringRecord #8
mm_incref Point #4 rc=2
mm_decref Point #4 rc=1
mm_incref StringRecord #6 rc=2
mm_decref StringRecord #6 rc=1
mm_incref Point #9 rc=2
mm_decref Point #9 rc=1
mm_alloc StringRecord #11 size=58
mm_alloc Point #12 size=16
mm_incref StringRecord #11 rc=2
mm_alloc StringRecord #13 size=58
mm_decref StringRecord #11 rc=1
mm_decref StringRecord #11 rc=0
mm_free StringRecord #11
mm_alloc Point #14 size=16
mm_alloc StringRecord #15 size=58
mm_incref Point #14 rc=2
mm_decref Point #14 rc=1
mm_decref Point #14 rc=0
mm_decref StringRecord #15 rc=0
mm_free StringRecord #15
mm_free Point #14
mm_decref Point #12 rc=0
mm_decref StringRecord #13 rc=0
mm_free StringRecord #13
mm_free Point #12
mm_decref ArrayRecord #1 rc=0
mm_decref Point #4 rc=0
mm_decref StringRecord #5 rc=0
mm_free StringRecord #5
mm_free Point #4
mm_decref StringRecord #6 rc=0
mm_free StringRecord #6
mm_decref Point #9 rc=0
mm_decref StringRecord #10 rc=0
mm_free StringRecord #10
mm_free Point #9
mm_decref ElementBuffer #7 rc=0
mm_free ElementBuffer #7
mm_free ArrayRecord #1
```

<!-- test: a-wide-union-with-payload-free-cases-is-shared-with-a-service -->
<!-- MmTrace -->
Words of every storage travel to a service: a payload-free case stored past the tag bits owns nothing and is shared as it is, and a payload case is shared and then read by both sides.
```maxon
typealias Weight = int(0 to 1000)

type Point
	export let x as Weight
	export let label as String

	static function create(x Weight, label String) returns Self
		return Self{x: x, label: label}
	end 'create'
end 'Point'

union Wide
	idle
	spot(let at Point)
	done
	text(let value String)
	failed
	skipped
	eight
	nine
	ten
end 'Wide'

type Store
	var seen = 0 as Weight

	static function create() returns Self
		return Self{}
	end 'create'

	export function keep(w immutable Wide) returns Weight
		self.seen = self.seen + 1

		return match w 'wide'
			spot(at) gives at.x + at.label.byteLength() as Weight
			text(value) gives value.byteLength() as Weight
			idle or
			done or
			failed or
			skipped or
			eight or
			nine or
			ten gives 1
		end 'wide'
	end 'keep'
end 'Store'

function main() returns ExitCode
	let h = spawn Store.create()
	let spotted = Wide.spot(Point.create(5, label: "ab{1}"))
	let texted = Wide.text("xyz{2}")
	var last = Wide.idle
	last = Wide.ten
	var first = Wide.eight
	first = Wide.idle
	var broken = Wide.nine
	broken = Wide.failed
	let a = try await h.keep(last) otherwise 0
	let b = try await h.keep(spotted) otherwise 0
	let c = try await h.keep(first) otherwise 0
	let d = try await h.keep(texted) otherwise 0
	let e = try await h.keep(broken) otherwise 0

	print("{a} {b} {c} {d} {e}\n")

	return 0
end 'main'
```
```stdout
1 8 1 4 1
```
```exitcode
0
```
```mm-trace
mm_alloc ServiceStartRecord #1 size=8
mm_alloc Store.handle #2 size=8
mm_alloc InterpolationScratch #3 size=21
mm_alloc StringRecord #4 size=60
mm_decref InterpolationScratch #3 rc=0
mm_free InterpolationScratch #3
mm_alloc Point #5 size=16
mm_incref StringRecord #4 rc=2
mm_alloc StringRecord #6 size=60
mm_decref StringRecord #4 rc=1
mm_decref StringRecord #4 rc=0
mm_free StringRecord #4
mm_alloc InterpolationScratch #7 size=21
mm_alloc StringRecord #8 size=61
mm_decref InterpolationScratch #7 rc=0
mm_free InterpolationScratch #7
mm_alloc Store #9 size=8
mm_decref ServiceStartRecord #1 rc=0
mm_free ServiceStartRecord #1
mm_alloc ElementBuffer #10 size=3
mm_decref Point #5 rc=0
mm_decref StringRecord #6 rc=0
mm_decref ElementBuffer #10 rc=0
mm_free ElementBuffer #10
mm_free StringRecord #6
mm_free Point #5
mm_alloc ElementBuffer #11 size=4
mm_decref StringRecord #8 rc=0
mm_decref ElementBuffer #11 rc=0
mm_free ElementBuffer #11
mm_free StringRecord #8
mm_alloc InterpolationScratch #12 size=20
mm_alloc InterpolationScratch #13 size=20
mm_alloc InterpolationScratch #14 size=20
mm_alloc InterpolationScratch #15 size=20
mm_alloc InterpolationScratch #16 size=20
mm_alloc StringRecord #17 size=67
mm_decref InterpolationScratch #12 rc=0
mm_free InterpolationScratch #12
mm_decref InterpolationScratch #13 rc=0
mm_free InterpolationScratch #13
mm_decref InterpolationScratch #14 rc=0
mm_free InterpolationScratch #14
mm_decref InterpolationScratch #15 rc=0
mm_free InterpolationScratch #15
mm_decref InterpolationScratch #16 rc=0
mm_free InterpolationScratch #16
mm_decref StringRecord #17 rc=0
mm_free StringRecord #17
mm_decref Store.handle #2 rc=0
mm_free Store.handle #2
mm_decref Store #9 rc=0
mm_free Store #9
```
