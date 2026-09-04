---
feature: memory-safety
status: experimental
keywords: [clone, cloneable, deep-copy, alias, equatable, union, array, shared-record, field]
category: core
---

# Memory Safety

## Documentation

### `.clone()` is the only copy

Everything managed in Maxon is a reference. Binding one name to another never
duplicates the value it names — see `specs-shv2/moves.md` for what a bare-reference
bind does with the ownership instead. `.clone()` is the one construct that makes a
second, independent value:

```text
let a = Point.create(1, y: 2)
var b = a.clone()   // b is a new, independent copy
b.x = 99            // a.x is still 1
```

Conformance to `Cloneable` is auto-generated: a struct conforms once every field
does, and a union conforms once every case payload does. Resolution is a fixpoint
over members that already conform, so a type reachable from its own members — a
struct or a union that holds itself — never enters the set.

### A clone must reach every heap record the value owns

The copy is only independent if it stops sharing at every level, and the shapes an
aggregate can take are what make that non-trivial:

- an **Array of structs** must copy each element record, or a write through the
  clone's element lands on the original's — and a growth then frees a buffer of
  pointers nobody owns twice;
- an **Array of arrays** must reach one level further down, so growing the clone's
  inner array leaves the original's at its old length;
- a **clone of a clone** must take its own buffer rather than share the buffer the
  first clone just filled;
- an **Array of strings** must copy the string records, because `append` writes
  the record the element slot points at;
- an **Array of unions** must rebuild each live case from independent payloads, so
  a payload-carrying arm copies its payload and a payload-less arm rebuilds as
  itself;
- a **struct with a union field** carries the conformance in through that field,
  so its own cloner must reach through it.

### Equality

`==` compares contents and requires the operand type to support it; `is` compares
reference identity — whether two names denote the same heap record. `is` is what
these cases ask when the question is about the COPY rather than about the value,
because it cannot be satisfied by two records that merely agree.

### A shared immortal record may never be written through

A never-mutated literal and an empty container are each emitted once, as a shared
immortal record. That is safe only while nothing can write through them, and a
value stored into a heap PLACE — a struct field, a container slot, a union payload
— can always be fetched back out and written through. Such a value gets its own
record: the write would otherwise grow the shared one in place, so an untouched
occurrence of the same value elsewhere would read the mutated bytes, and the grown
buffer would leak because an immortal record's destructor is 0.

### A field's record is reachable by more than the field

A struct field holding a managed value hands the SAME record to everything that
reads it, and that record keeps answering after a write through the field, whoever
performed the write. This bounds what an optimizer may do with such a field:
swapping the field's record at a write would require proving no other handle is
live, and a handle need not even be bound to a name — it can be an argument.

## Tests

<!-- test: assignment-creates-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	a = b
	print("{a.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: auto-cloneable -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(10, y: 20)
	var b = a.clone()
	b.x = 99
	if a is not b 'diff'
		return a.x + a.y
	end 'diff'
	return 0
end 'main'
```
```exitcode
30
```

<!-- test: string-clone -->
```maxon
function main() returns ExitCode
	let a = "hello"
	let b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: array-clone -->
```maxon
function main() returns ExitCode
	let a = [1, 2, 3]
	let b = a.clone()
	if a is not b 'diff'
		return 1
	end 'diff'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: clone-of-struct-array-survives-a-write-and-a-growth -->
### A cloned Array of STRUCTS is independent under both mutation and growth
Growth is the half that a shared element makes fatal rather than merely wrong: pushing past the
clone's capacity copies its buffer, and a buffer of element pointers nobody owns twice is freed
twice. Measured before the fix, this program printed the wrong first line and then DIED tearing
the array down — `mm_decref: refcount underflow (already zero)`, through
`mm_decref_managed_elements`, exit 1.
```maxon
typealias Integer = int(i64.min to i64.max)

type Leaf
	export var label as String
	export var value as Integer

	static function create(label String, value Integer) returns Self
		return Self{label: label, value: value}
	end 'create'
end 'Leaf'

typealias Leaves = Array with Leaf

function main() returns ExitCode
	var a = Leaves.create()
	a.push(Leaf.create("original label long enough for a heap record", value: 10))

	var b = a.clone()
	var cloned = try b.get(0) otherwise panic("no leaf")
	cloned.label = "mutated"

	// `cloned`'s last use is above: the borrow it holds on `b` has to end before `push`
	// may grow `b` (E3070), which is why the clone is read back through a fresh `get`.
	b.push(Leaf.create("second label long enough for a heap record", value: 20))

	let kept = try a.get(0) otherwise panic("no leaf")
	let reread = try b.get(0) otherwise panic("no leaf")
	print("{a.count()}:{kept.label}\n")
	print("{b.count()}:{reread.label}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1:original label long enough for a heap record
2:mutated
```

<!-- test: clone-of-nested-array-copies-the-inner-array -->
### Cloning an Array of ARRAYS copies the inner arrays
The element is itself a container, so the copy has to reach one level further down: growing the
clone's inner array must leave the original's inner array at the length it had.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Row = Array with Integer
typealias Grid = Array with Row

function main() returns ExitCode
	var a = Grid.create()
	var row = Row.create()
	row.push(1)
	a.push(row)

	let b = a.clone()
	var clonedRow = try b.get(0) otherwise panic("no row")
	clonedRow.push(2)

	let keptRow = try a.get(0) otherwise panic("no row")
	print("{keptRow.count()} {clonedRow.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: clone-of-a-clone-is-independent-of-both -->
### Cloning a CLONE copies the elements again
`Array.clone` hands back a view onto the buffer it just filled, so the second clone's source is a
view rather than an owned buffer — and a view has no buffer of its own to put element copies in.
The second clone has to take its own buffer for exactly that reason; taking the view arm made
`a.clone().clone()` share the first clone's elements.
```maxon
typealias Integer = int(i64.min to i64.max)

type Leaf
	export var label as String
	export var value as Integer

	static function create(label String, value Integer) returns Self
		return Self{label: label, value: value}
	end 'create'
end 'Leaf'

typealias Leaves = Array with Leaf

function main() returns ExitCode
	var a = Leaves.create()
	a.push(Leaf.create("original label long enough for a heap record", value: 10))

	let b = a.clone()
	let c = b.clone()
	var cloned = try c.get(0) otherwise panic("no leaf")
	cloned.label = "mutated"

	let fromA = try a.get(0) otherwise panic("no leaf")
	let fromB = try b.get(0) otherwise panic("no leaf")
	print("{fromA.label}\n{fromB.label}\n{cloned.label}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original label long enough for a heap record
original label long enough for a heap record
mutated
```

<!-- test: clone-of-string-array-copies-its-strings -->
### Cloning an Array of STRINGS copies the strings
A `String` element is as writable as a struct one — `append` mutates the record the element slot
points at — so sharing it makes the clone and the original one string.
```maxon
typealias Words = Array with String

function main() returns ExitCode
	var a = Words.create()
	a.push("original word long enough for a heap record")

	let b = a.clone()
	var cloned = try b.get(0) otherwise panic("no word")
	cloned.append(" and a tail")

	let kept = try a.get(0) otherwise panic("no word")
	print("{kept}\n{cloned}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original word long enough for a heap record
original word long enough for a heap record and a tail
```

<!-- test: clone-of-union-array-copies-the-payload -->
### Cloning an Array of UNIONS copies each live case's payload
A union with a payload is a heap box holding heap pointers, so a copied element slot is a second
name for the SAME box and the same payload record. Writing through the clone's payload was a
write to the original's.
```maxon
type Item
	export var label as String

	static function create(label String) returns Item
		return Self{label: label}
	end 'create'
end 'Item'

union Op
	holds(item Item)
	none
end 'Op'

typealias Ops = Array with Op

function mutate(item Item)
	item.label = "MUTATED"
end 'mutate'

function main() returns ExitCode
	var a = Ops.create()
	a.push(Op.holds(Item.create("original label long enough for a heap record")))

	let b = a.clone()
	let bOp = try b.get(0) otherwise Op.none
	match bOp 'mut'
		holds(it) then mutate(it)
		none then return 3
	end 'mut'

	let aOp = try a.get(0) otherwise Op.none
	match aOp 'read'
		holds(it) then print("a holds: {it.label}\n")
		none then return 4
	end 'read'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a holds: original label long enough for a heap record
```

<!-- test: clone-of-union-array-with-string-payload -->
### A union payload that is a String is copied too
`String` is the payload a union carries most often. Reference identity is what the copy is
ABOUT, so it is what the case asks: a retained element leaves both arrays' payloads as one heap
record, and `is` says so without mutating anything.
```maxon
union Word
	spelled(text String)
	blank
end 'Word'

typealias Words = Array with Word

function payloadOf(w Word) returns String
	match w 'p'
		spelled(text) then return text
		blank then return "blank"
	end 'p'
end 'payloadOf'

function main() returns ExitCode
	var a = Words.create()
	a.push(Word.spelled("original word long enough for a heap record"))

	let b = a.clone()
	let wa = try a.get(0) otherwise Word.blank
	let wb = try b.get(0) otherwise Word.blank
	if payloadOf(wa) is payloadOf(wb) 'shared'
		print("shared\n")
		return 0
	end 'shared'
	print("independent: {payloadOf(wb)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
independent: original word long enough for a heap record
```

<!-- test: clone-of-union-array-mixes-payload-and-payload-less-arms -->
### A payload-less arm survives the copy as itself
The cloner dispatches on the tag, so an arm carrying nothing has to rebuild as that arm rather
than fall into a neighbour's payload rebuild. Both arms sit in one array here, so a single copy
walks both paths — and the filled one still has to come out independent.
```maxon
union Slot
	filled(text String)
	empty
end 'Slot'

typealias Slots = Array with Slot

function textOf(s Slot) returns String
	match s 'kind'
		filled(text) then return text
		empty then return "empty"
	end 'kind'
end 'textOf'

function main() returns ExitCode
	var a = Slots.create()
	a.push(Slot.filled("filled with a heap record"))
	a.push(Slot.empty)

	let b = a.clone()
	let a0 = try a.get(0) otherwise Slot.empty
	let b0 = try b.get(0) otherwise Slot.empty
	let b1 = try b.get(1) otherwise Slot.filled("wrong")
	if textOf(a0) is textOf(b0) 'shared'
		print("shared|{textOf(b1)}\n")
		return 0
	end 'shared'
	print("{textOf(b0)}|{textOf(b1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled with a heap record|empty
```

<!-- test: clone-of-a-union-value-is-independent -->
### `.clone()` on a union value hands back an independent box
Auto-conformance is what makes `u.clone()` resolve at all, so the method it resolves TO has to
exist and has to deep-copy: a clone sharing the payload would make the two boxes one value.
```maxon
type Item
	export var label as String

	static function create(label String) returns Item
		return Self{label: label}
	end 'create'
end 'Item'

union Op
	holds(item Item)
	none
end 'Op'

function mutate(item Item)
	item.label = "MUTATED"
end 'mutate'

function main() returns ExitCode
	let a = Op.holds(Item.create("original label long enough for a heap record"))
	let b = a.clone()

	match b 'mut'
		holds(it) then mutate(it)
		none then return 3
	end 'mut'

	match a 'read'
		holds(it) then print("a holds: {it.label}\n")
		none then return 4
	end 'read'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a holds: original label long enough for a heap record
```

<!-- test: clone-of-struct-with-union-field-copies-the-payload -->
### Cloning a struct whose FIELD is a union copies that union
A struct auto-conforms once every field does, so a union field is what carries the conformance
into the struct — and the struct's own cloner has to reach through it. Copying the field by
pointer leaves the clone and the original sharing one box.
```maxon
type Item
	export var label as String

	static function create(label String) returns Item
		return Self{label: label}
	end 'create'
end 'Item'

union Op
	holds(item Item)
	none
end 'Op'

type Decl
	export var op as Op

	static function create(op Op) returns Decl
		return Self{op: op}
	end 'create'
end 'Decl'

function mutate(item Item)
	item.label = "MUTATED"
end 'mutate'

function main() returns ExitCode
	let a = Decl.create(Op.holds(Item.create("original label long enough for a heap record")))
	let b = a.clone()

	match b.op 'mut'
		holds(it) then mutate(it)
		none then return 3
	end 'mut'

	match a.op 'read'
		holds(it) then print("a holds: {it.label}\n")
		none then return 4
	end 'read'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a holds: original label long enough for a heap record
```

<!-- test: clone-of-a-many-case-union-dispatches-every-tag -->
### Every arm of a many-case union rebuilds as itself
Enough cases to take the JUMP TABLE rather than a compare chain — the dispatch strategy is chosen by
interval count (`MaxonToStandardConversion.SwitchDispatch`), so a union with two arms never reaches
the one a union with five does. The arms here are deliberately unalike: a nested UNION payload, an
ARRAY payload, a TWO-payload arm, a payload-less arm and a scalar arm, so a tag landing in the wrong
block shows up as the wrong text rather than as a crash.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Words = Array with String

union Inner
	text(s String)
	none
end 'Inner'

union Outer
	wraps(i Inner)
	holds(w Words)
	pair(flag bool, n Integer)
	blank
	plain(n Integer)
end 'Outer'

typealias Outers = Array with Outer

function innerTextOf(i Inner) returns String
	match i 'j'
		text(s) then return s
		none then return "inner-none"
	end 'j'
end 'innerTextOf'

function describe(o Outer) returns String
	match o 'k'
		wraps(i) then return "wraps:{innerTextOf(i)}"
		holds(w) then return "holds:{w.count()}"
		pair(flag, n) then return "pair:{flag}:{n}"
		blank then return "blank"
		plain(n) then return "plain:{n}"
	end 'k'
end 'describe'

// Hands back the `wraps` payload's String ITSELF, so `is` asks about the copy rather than about a
// freshly interpolated description.
function nestedTextOf(o Outer) returns String
	match o 'k'
		wraps(i) then return innerTextOf(i)
		holds(w) then return "holds:{w.count()}"
		pair(flag, n) then return "pair:{flag}:{n}"
		blank then return "blank"
		plain(n) then return "plain:{n}"
	end 'k'
end 'nestedTextOf'

function main() returns ExitCode
	var w = Words.create()
	w.push("inner element long enough for a heap record")

	var a = Outers.create()
	a.push(Outer.wraps(Inner.text("nested payload long enough for a heap record")))
	a.push(Outer.holds(w))
	a.push(Outer.pair(true, n: 3))
	a.push(Outer.blank)
	a.push(Outer.plain(7))

	let b = a.clone()
	var out = ""
	for o in b 'each'
		out.append(describe(o))
		out.append("|")
	end 'each'
	print("{out}\n")

	let a0 = try a.get(0) otherwise Outer.blank
	let b0 = try b.get(0) otherwise Outer.blank
	if nestedTextOf(a0) is nestedTextOf(b0) 'shared'
		print("nested payload SHARED\n")
		return 8
	end 'shared'
	print("nested payload independent\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
wraps:nested payload long enough for a heap record|holds:1|pair:true:3|blank|plain:7|
nested payload independent
```

<!-- test: eq-requires-equatable -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias FnTypeAlias1 = function() returns Integer

type Callback
	export var fn as FnTypeAlias1

	static function create(fn FnTypeAlias1) returns Self
		return Self{fn: fn}
	end 'create'
end 'Callback'

function main() returns ExitCode
	let a = Callback.create(main)
	let b = Callback.create(main)
	if a == b 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/memory-safety/eq-requires-equatable.test:16:7: type mismatch: 'cannot compare struct with struct'
```

<!-- test: shared-record-not-written-through-a-field-assign -->
`StringBuilder.build()` hands its buffer to the finished `String` and resets itself with an empty one.
If that empty buffer were the shared record, two builders would share it, and appending to either
would publish the other's length.
```maxon
function main() returns ExitCode
	var a = StringBuilder.create()
	var b = StringBuilder.create()
	let fromA = a.build()
	let fromB = b.build()
	a.append("AAA")
	print("a={a.byteLength()} b={b.byteLength()} fromA=[{fromA}] fromB=[{fromB}]")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=3 b=0 fromA=[] fromB=[]
```

<!-- test: shared-record-not-written-through-a-union-payload -->
A union payload is a place too. The `String` reading of it: appending to a payload matched out of one
union must not reach an untouched literal of the same text.
```maxon
union Tagged
	named(name String)
end 'Tagged'

function main() returns ExitCode
	var boxed = Tagged.named("tag")
	match boxed 'grow'
		named(n) then n.append("!")
	end 'grow'
	let untouched = "tag"
	print("untouched={untouched}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
untouched=tag
```

<!-- test: mutated-payload-bound-inside-the-arm-frees-its-record -->
The same accounting one binding-shape over, which is the shape the defect was first seen in: the
payload is appended to INSIDE the arm, through a `var` union, and never escapes. It exited 101 too,
so both routes to a grown payload record are pinned rather than the one that happened to be
reported.
```maxon
union Word
	spelled(text String)
	blank
end 'Word'

function main() returns ExitCode
	var a = Word.spelled("head, long enough to be a real heap allocation")
	match a 'm'
		spelled(text) then text.append(" tail")
		blank then return 1
	end 'm'
	match a 'read'
		spelled(text) then print(text)
		blank then return 1
	end 'read'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
head, long enough to be a real heap allocation tail
```

### A field's record is reachable by more than the field

A struct field holding a managed value hands out the SAME record to everything that reads it, and that
record keeps answering after a write through the field, whoever performed the write. Both cases below
say so, from the two places a second handle can come from: a binding taken before the write, and a
parameter of a function that performs the write by another route to the same object.

They are language facts, not compiler bookkeeping, and they are pinned because they bound what an
optimizer may do with such a field. A compiler that swapped the field's record at a write — the way a
copy-on-write anchor does by hand — would have to prove neither handle exists, which means proving a
handle is DEAD at the write, not merely that it is the only one visible from where the write is
written. In the second case the handle is not even bound to a name: it is an argument.

<!-- test: field-record-is-seen-through-a-binding-taken-before-the-write -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	export var sites as IntArray

	static function create() returns Self
		return Self{sites: IntArray.create()}
	end 'create'

	public function add(x Integer)
		sites.push(x)
	end 'add'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	var borrowed = h.sites
	h.add(1)
	print("borrowed={borrowed.count()} field={h.sites.count()} same={borrowed is h.sites}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
borrowed=1 field=1 same=true
```

<!-- test: field-record-is-seen-through-a-parameter-across-the-write -->
`through` is handed the column and the holder separately, and writes through the holder. The column it
was given is the very record that write lands on, so it reads the new count — and nothing in `through`
or in `add` names the column and the field together.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder
	export var sites as IntArray

	static function create() returns Self
		return Self{sites: IntArray.create()}
	end 'create'

	public function add(x Integer)
		sites.push(x)
	end 'add'
end 'Holder'

function through(col IntArray, holder Holder) returns Integer
	holder.add(1)
	return col.count()
end 'through'

function main() returns ExitCode
	var h = Holder.create()
	let seen = through(h.sites, holder: h)
	print("param saw={seen} field={h.sites.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
param saw=1 field=1
```
