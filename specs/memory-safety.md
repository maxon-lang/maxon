---
feature: memory-safety
status: experimental
keywords: [reference, alias, clone, cloneable, equatable, ownership, region, lifetime]
category: core
---

# Memory Safety

## Documentation

### Reference-by-Default Assignment

In Maxon, assigning a struct variable to another variable copies the **heap pointer**, creating an alias (reference) to the same object:

```text
var a = Point{x: 1, y: 2}
var b = a        // b is an alias — same object as a
b.x = 99        // a.x is also 99 (shared mutation)
```

Rebinding a variable to a new struct does not affect the other:

```text
var a = Point{x: 1, y: 2}
var b = a
b = Point{x: 5, y: 6}  // rebinds b; a is unchanged
```

Primitives are unaffected — `var b = a` copies the value for int, float, bool, and byte.

### Explicit Clone with `.clone()`

To create an independent deep copy, use `.clone()`:

```text
var a = Point{x: 1, y: 2}
var b = a.clone()   // b is a new, independent copy
b.x = 99           // a.x is still 1
```

This requires the type to implement the `Cloneable` interface. The compiler auto-generates `Cloneable` conformance for any struct whose fields are all Cloneable, and for any union whose case payloads are all Cloneable (all primitives, String, Array, Cloneable structs, and Cloneable unions qualify). A union's clone rebuilds the live case from independent copies of its payloads, so a case carrying nothing clones as itself.

Conformance is resolved to a fixpoint over members that already conform, so a type reachable from its own members — a struct or a union that holds itself — never enters the set and is not auto-Cloneable.

### Equality

- `==` compares contents and requires `Equatable` conformance
- `is` compares reference identity (same heap object)
- The compiler auto-generates `Equatable` conformance for structs whose fields all implement `Equatable`

### Parameter Passing

All function parameters are passed by reference. The compiler infers parameter immutability: parameters that are not assigned to inside the function body are semantically immutable (`let`).

### Ownership and Regions

Every object is owned by a region (stack frame, struct, or array). When a region ends, everything it owns is freed. Return values transfer ownership to the caller's region. References must not outlive the objects they refer to.

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

<!-- test: rebind-does-not-mutate -->
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
	a.x = 1
	var b = a
	b = Point.create(99, y: 99)
	print("{a.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: non-cloneable-assignment-ok -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

type Item
	export var color as Color
	export var value as Integer

	static function create(color Color, value Integer) returns Self
		return Self{color: color, value: value}
	end 'create'
end 'Item'

function main() returns ExitCode
	var a = Item.create(Color.red, value: 42)
	a.value = 42
	var b = a
	b.value = 99
	return a.value
end 'main'
```
```exitcode
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

<!-- test: nested-struct-clone -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var a as Inner
	export var b as Integer

	static function create(a Inner, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let x = Outer.create(Inner.create(42), b: 10)
	var y = x.clone()
	y.a.value = 99
	y.b = 0
	print("{x.a.value}\n")
	print("{x.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
10
```

<!-- test: clone-of-array-field-copies-its-struct-elements -->
### Cloning a struct whose field is an Array of STRUCTS copies the elements
`nested-struct-clone` proves the cascade reaches a struct FIELD. It stops there unless the
Array's own clone reaches its ELEMENTS: an element copied by pointer leaves the clone and the
original holding one `Leaf`, so a write through the clone is a write to the original.
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

type Holder
	export var leaves as Leaves

	static function create(leaves Leaves) returns Self
		return Self{leaves: leaves}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var a = Holder.create(Leaves.create())
	a.leaves.push(Leaf.create("original label long enough for a heap record", value: 10))

	let b = a.clone()
	var cloned = try b.leaves.get(0) otherwise panic("no leaf")
	cloned.label = "mutated"
	cloned.value = 99

	let kept = try a.leaves.get(0) otherwise panic("no leaf")
	print("{kept.label} {kept.value}\n")
	print("{cloned.label} {cloned.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original label long enough for a heap record 10
mutated 99
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

<!-- test: recursive-union-is-not-cloneable -->
### A union that holds ITSELF does not auto-conform
Auto-conformance closes over the payload types to a fixpoint, so a union reachable from its own
payload never enters the set — exactly as a self-referential struct never does. That is the
answer rather than a special case: nothing has to bound the recursion because no cyclic type is
admitted in the first place, and the element copy of one stays a retain.
```maxon
union Chain
	link(next Chain)
	tip
end 'Chain'

function main() returns ExitCode
	let a = Chain.tip
	let b = a.clone()
	match b 'read'
		link(next) then return 1
		tip then return 0
	end 'read'
end 'main'
```
```maxoncstderr
error E4006: specs/fragments/memory-safety/recursive-union-is-not-cloneable.test:9:12: Union type 'Chain' has no property or method named 'clone'
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
error E3069: specs/fragments/memory-safety/eq-requires-equatable.test:16:7: '==' requires type 'Callback' to implement 'Equatable'
```

<!-- test: auto-equatable -->
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
	let a = Point.create(1, y: 2)
	let b = Point.create(1, y: 2)
	let c = Point.create(3, y: 4)
	var result = 0
	if a == b 'eq1'
		result = result + 1
	end 'eq1'
	if a == c 'eq2'
		result = result + 10
	end 'eq2'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: is-compares-refs -->
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
	let a = Point.create(1, y: 2)
	let b = a
	if a is b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: is-after-clone -->
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
	let a = Point.create(1, y: 2)
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

<!-- test: scope-cleanup -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Resource
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Resource'

function createAndDrop() returns Integer
	@heap let r = Resource.create(42)
	return r.id
end 'createAndDrop'

function main() returns ExitCode
	return createAndDrop()
end 'main'
```
```exitcode
42
```

<!-- test: return-ownership-transfer -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makeRef() returns Point
	let local = Point.create(1, y: 2)
	return local
end 'makeRef'

function main() returns ExitCode
	let p = makeRef()
	print("{p.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: block-scope-struct-release -->
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
	var result = 0
	if true 'block'
		@heap let p = Point.create(10, y: 20)
		result = p.x
	end 'block'
	return result
end 'main'
```
```exitcode
10
```
```RequiredIR:x64-windows
=== maxon
module {
  func @Point.create(x: i64, y: i64) -> Point {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %2 = maxon.struct_literal @Point
    maxon.scope_end [x, y]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 1 : i1}
    maxon.cond_br %18 [then: block_0, else: block_0.merge]
  block_0:
    %19 = maxon.literal {value = 10 : i64}
    %20 = maxon.literal {value = 20 : i64}
    %21 = maxon.call @Point.create %19, %20
    maxon.assign %21 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %21 {var = p} {decl = 1 : i1}
    %22 = maxon.struct_var_ref p
    %23 = maxon.field_access .x %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [p]
    maxon.br block_0.merge
  block_0.merge:
    %24 = maxon.var_ref {var = result} {type = i64}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 4294967295 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at block-scope-struct-release.test:19: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result]
    maxon.return %24
  }
}
=== standard
module {
  func @Point.create(x: i64, y: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = func.param y : StdI64
    %2 = arith.constant {value = 16 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 1 : i64}
    %5 = std.call_runtime @mm_alloc %2, %3, %4
    memref.store %5, __struct_0
    %6 = memref.load __struct_0 : i64
    memref.store_indirect %0, %6+0
    %7 = memref.load __struct_0 : i64
    memref.store_indirect %1, %7+8
    %8 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %8
    %9 = memref.load __struct_0 : i64
    func.return %9
  }
  func @main() -> u32 {
  entry:
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, result
    %11 = arith.constant {value = 1 : i1}
    cf.cond_br %11 [then: block_0, else: block_0.merge]
  block_0:
    %12 = arith.constant {value = 10 : i64}
    %13 = arith.constant {value = 20 : i64}
    %14 = func.call @Point.create %12, %13
    memref.store %14, p
    %17 = memref.load p : i64
    %18 = memref.load_indirect %17+0
    memref.store %18, result
    %19 = memref.load p : i64
    std.call_runtime_if_nonnull @mm_decref %19
    cf.br block_0.merge
  block_0.merge:
    %21 = memref.load result : i64
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    %30 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @Point.create(x: i64, y: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov [rbp-16], rcx
    x64.mov [rbp-24], rdx
    x64.mov rcx, 16
    x64.xor edx, edx
    x64.mov r8, 1
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.mov [rax+0], rcx
    x64.mov rdx, [rbp-8]
    x64.mov rbx, [rbp-24]
    x64.mov [rdx+8], rbx
    x64.mov rsi, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.mov rcx, 1
    x64.test rcx, rcx
    x64.je main.block_0.merge
  block_0:
    x64.mov rcx, 10
    x64.mov rdx, 20
    x64.call Point.create
    x64.mov [rbp-16], rax
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-8], rcx
    x64.mov rdx, [rbp-16]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.jmp main.block_0.merge
  block_0.merge:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    x64.jmp __destruct_Point.done
  done:
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @Point.create(x: i64, y: i64) -> Point {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = x} {type = i64}
    %1 = maxon.param {index = 1 : i32} {name = y} {type = i64}
    %2 = maxon.struct_literal @Point
    maxon.scope_end [x, y]
    maxon.return %2
  }
  func @main() -> i64 {
  entry:
    %17 = maxon.literal {value = 0 : i64}
    maxon.assign %17 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %18 = maxon.literal {value = 1 : i1}
    maxon.cond_br %18 [then: block_0, else: block_0.merge]
  block_0:
    %19 = maxon.literal {value = 10 : i64}
    %20 = maxon.literal {value = 20 : i64}
    %21 = maxon.call @Point.create %19, %20
    maxon.assign %21 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %21 {var = p} {decl = 1 : i1}
    %22 = maxon.struct_var_ref p
    %23 = maxon.field_access .x %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [p]
    maxon.br block_0.merge
  block_0.merge:
    %24 = maxon.var_ref {var = result} {type = i64}
    %25 = maxon.literal {value = 0 : i64}
    %26 = maxon.binop %24, %25 {op = lt}
    %27 = maxon.literal {value = 255 : i64}
    %28 = maxon.binop %24, %27 {op = gt}
    %29 = maxon.binop %26, %28 {op = or}
    maxon.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at block-scope-struct-release.test:19: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result]
    maxon.return %24
  }
}
=== standard
module {
  func @Point.create(x: i64, y: i64) -> i64 {
  entry:
    %0 = func.param x : StdI64
    %1 = func.param y : StdI64
    %2 = arith.constant {value = 16 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 1 : i64}
    %5 = std.call_runtime @mm_alloc %2, %3, %4
    memref.store %5, __struct_0
    %6 = memref.load __struct_0 : i64
    memref.store_indirect %0, %6+0
    %7 = memref.load __struct_0 : i64
    memref.store_indirect %1, %7+8
    %8 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %8
    %9 = memref.load __struct_0 : i64
    func.return %9
  }
  func @main() -> u8 {
  entry:
    %10 = arith.constant {value = 0 : i64}
    memref.store %10, result
    %11 = arith.constant {value = 1 : i1}
    cf.cond_br %11 [then: block_0, else: block_0.merge]
  block_0:
    %12 = arith.constant {value = 10 : i64}
    %13 = arith.constant {value = 20 : i64}
    %14 = func.call @Point.create %12, %13
    memref.store %14, p
    %17 = memref.load p : i64
    %18 = memref.load_indirect %17+0
    memref.store %18, result
    %19 = memref.load p : i64
    std.call_runtime_if_nonnull @mm_decref %19
    cf.br block_0.merge
  block_0.merge:
    %21 = memref.load result : i64
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 255 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_0
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    %30 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @Point.create(x: i64, y: i64) -> i64 {
  entry:
    arm64.prologue stack_size=64
    arm64.str x0, [x29, #-16]
    arm64.str x1, [x29, #-24]
    arm64.mov x0, #16
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.str x1, [x0, #0]
    arm64.ldr x2, [x29, #-8]
    arm64.ldr x3, [x29, #-24]
    arm64.str x3, [x2, #8]
    arm64.ldr x4, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x5, [x29, #-8]
    arm64.mov x0, x5
    arm64.epilogue stack_size=64
    arm64.ret
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #1
    arm64.cmp x1, #0
    arm64.b.ne main.block_0
    arm64.b main.block_0.merge
  block_0:
    arm64.mov x0, #10
    arm64.mov x1, #20
    arm64.bl Point.create
    arm64.str x0, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-8]
    arm64.ldr x2, [x29, #-16]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_12
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_12
    arm64.b main.block_0.merge
  block_0.merge:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #255
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @__destruct_Point(ptr: i64) {
  entry:
    arm64.b __destruct_Point.done
  done:
    arm64.ret
  }
}
```

### Push increfs a struct element — REGRESSION PIN, read this before regenerating

The `RequiredIR` blocks below are a **regression pin**, not a shape this spec set out
to document. What they pin is an **absence**: there must be no release-of-old-value on
the `try ... otherwise` result slot `__try_result_0`. Both arms of an `otherwise` store
that slot as a **declaration**, and a declaration never releases a previous value —
emitting the ordinary reassignment sequence there released whatever the uninitialized
slot happened to hold. Commit `4572c988b` fixed that, and measured why it can have no
bespoke minimal test: what the stale slot holds depends on frame layout, so a smaller
program reads 0 there, the null guard inside `mm_decref`'s emission hides it, and the
small program passes on the *broken* compiler too. This block is the durable guard
instead.

Before committing a regenerated block, diff it for a `memref.load __try_result_0`
feeding an `mm_decref` **ahead of** the store in `otherwise_default_success_0`. If that
pair is back, the fix has regressed — do not commit the regeneration.

The entry-block zero-init of `__try_result_0` travels with that load and is **not** an
independent signal. `DeadStoreEliminationPass` keeps a zero-init only while some path
can reach a load of the slot without storing it first, so removing the last such load
makes the zero-init dead and the two vanish together. Reading the zero-init's departure
as a second, separate loss is how this diff looks alarming when it is not.

<!-- test: array-push-struct-incref -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Item'

typealias ItemArray = Array with Item

function main() returns ExitCode
	var arr = ItemArray.create()
	let item = Item.create(7)
	arr.push(item)
	let got = try arr.get(0) otherwise Item.create(0)
	return got.value
end 'main'
```
```exitcode
7
```
```RequiredIR:x64-windows
=== maxon
module {
  func @Item.create(value: i64) -> Item {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = value} {type = i64}
    %1 = maxon.struct_literal @Item
    maxon.scope_end [value]
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %11 = maxon.call @ItemArray.create
    maxon.assign %11 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %11 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 7 : i64}
    %13 = maxon.call @Item.create %12
    maxon.assign %13 {var = __call_tmp_1} {decl = 1 : i1}
    maxon.assign %13 {var = item} {decl = 1 : i1}
    %14 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %11, %14
    %15 = maxon.struct_var_ref arr
    %16 = maxon.literal {value = 0 : i64}
    %19, %18 = maxon.try_call @ItemArray.get %15, %16
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %18, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_0, else: otherwise_default_success_0]
  otherwise_default_error_0:
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.call @Item.create %22
    maxon.assign %23 {var = __call_tmp_2} {decl = 1 : i1}
    maxon.assign %23 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_success_0:
    maxon.assign %19 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %24 = maxon.struct_var_ref __try_result_0
    maxon.assign %24 {var = got} {decl = 1 : i1}
    %25 = maxon.struct_var_ref got
    %26 = maxon.field_access .value %25
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 4294967295 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at array-push-struct-incref.test:19: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [arr, item, got, __call_tmp_2, __try_result_0]
    maxon.return %26
  }
}
=== standard
module {
  func @Item.create(value: i64) -> i64 {
  entry:
    %0 = func.param value : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @main() -> u32 {
  entry:
    %53 = arith.constant {value = 0 : i64}
    memref.store %53, __call_tmp_0
    %8 = func.call @ItemArray.create
    memref.store %8, arr
    %11 = arith.constant {value = 7 : i64}
    %12 = func.call @Item.create %11
    memref.store %12, item
    %15 = memref.load arr : i64
    %16 = memref.load item : i64
    func.call @ItemArray.push %15, %16
    %17 = arith.constant {value = 0 : i64}
    %18 = memref.load arr : i64
    %19, %20 = func.try_call @ItemArray.get %18, %17
    memref.store %19, __callret_0
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.cmpi ne %20, %21
    cf.cond_br %22 [then: otherwise_default_error_0, else: otherwise_default_success_0]
  otherwise_default_error_0:
    %23 = arith.constant {value = 0 : i64}
    %24 = func.call @Item.create %23
    memref.store %24, __call_tmp_0
    memref.store %24, __try_result_0
    %27 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %27
    cf.br otherwise_default_continue_0
  otherwise_default_success_0:
    %28 = memref.load __callret_0 : i64
    memref.store %28, __try_result_0
    cf.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %29 = memref.load __try_result_0 : i64
    memref.store %29, got
    %30 = memref.load got : i64
    std.call_runtime @mm_incref %30
    %31 = memref.load got : i64
    %32 = memref.load_indirect %31+0
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_0
    %39 = std.ptr_to_i64 %38
    std.call_runtime @mrt_panic %39
  __range_ok_0:
    %40 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %40
    %42 = memref.load __call_tmp_0 : i64
    std.call_runtime_if_nonnull @mm_decref %42
    %44 = memref.load got : i64
    std.call_runtime_if_nonnull @mm_decref %44
    %46 = memref.load item : i64
    std.call_runtime_if_nonnull @mm_decref %46
    %48 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %48
    func.return %32
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    %55 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    %56 = func.param ptr : StdI64
    memref.store %56, __destr_ptr
    %59 = memref.load __destr_ptr : i64
    %60 = memref.load_indirect %59+16
    %61 = arith.constant {value = -1 : i64}
    %62 = arith.cmpi eq %60, %61
    cf.cond_br %62 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %63 = memref.load __destr_ptr : i64
    %64 = memref.load_indirect %63+32
    std.call_runtime_if_nonnull @mm_decref %64
    cf.br skip_buf_0
  check_owned_0:
    %65 = memref.load __destr_ptr : i64
    %66 = memref.load_indirect %65+16
    %67 = arith.constant {value = -2 : i64}
    %68 = arith.cmpi ne %66, %67
    cf.cond_br %68 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %69 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %69
    %70 = memref.load __destr_ptr : i64
    %71 = memref.load_indirect %70+32
    %72 = arith.constant {value = -3 : i64}
    %73 = arith.cmpi ne %71, %72
    cf.cond_br %73 [then: raw_free_0, else: skip_buf_0]
  raw_free_0:
    %74 = memref.load __destr_ptr : i64
    %75 = memref.load_indirect %74+0
    std.call_runtime @mm_raw_free %75
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @Item.create(value: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-16], rcx
    x64.mov rcx, 8
    x64.xor edx, edx
    x64.mov r8, 1
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.mov [rax+0], rcx
    x64.mov rdx, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=64
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.call ItemArray.create
    x64.mov [rbp-16], rax
    x64.mov rcx, 7
    x64.call Item.create
    x64.mov [rbp-24], rax
    x64.mov rcx, [rbp-16]
    x64.mov rdx, [rbp-24]
    x64.call ItemArray.push
    x64.mov rbx, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.xor edx, edx
    x64.call ItemArray.get
    x64.mov [rbp-32], rax
    x64.xor esi, esi
    x64.cmp rdx, rsi
    x64.je main.otherwise_default_success_0
  otherwise_default_error_0:
    x64.xor ecx, ecx
    x64.call Item.create
    x64.mov [rbp-8], rax
    x64.mov [rbp-40], rax
    x64.mov rax, [rbp-40]
    x64.mov rcx, [rbp-40]
    x64.call mm_incref
    x64.jmp main.otherwise_default_continue_0
  otherwise_default_success_0:
    x64.mov rax, [rbp-32]
    x64.mov [rbp-40], rax
    x64.jmp main.otherwise_default_continue_0
  otherwise_default_continue_0:
    x64.mov rax, [rbp-40]
    x64.mov [rbp-48], rax
    x64.mov rcx, [rbp-48]
    x64.call mm_incref
    x64.mov rdx, [rbp-48]
    x64.mov rbx, [rdx+0]
    x64.xor esi, esi
    x64.mov edi, 4294967295
    x64.cmp rbx, rdi
    x64.jg main.__range_panic_0
    x64.cmp rbx, rsi
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rax, [rbp-40]
    x64.mov [rbp-56], rbx
    x64.test rax, rax
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-40]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rcx, [rbp-8]
    x64.test rcx, rcx
    x64.jz __nonnull_skip_1
    x64.call mm_decref
    x64.label __nonnull_skip_1
    x64.mov rdx, [rbp-48]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_2
    x64.mov rcx, [rbp-48]
    x64.call mm_decref
    x64.label __nonnull_skip_2
    x64.mov rbx, [rbp-24]
    x64.test rbx, rbx
    x64.jz __nonnull_skip_3
    x64.mov rcx, [rbp-24]
    x64.call mm_decref
    x64.label __nonnull_skip_3
    x64.mov rsi, [rbp-16]
    x64.test rsi, rsi
    x64.jz __nonnull_skip_4
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_4
    x64.mov rax, [rbp-56]
    x64.epilogue
    x64.ret
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    x64.jmp __destruct_Item.done
  done:
    x64.ret
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -1
    x64.cmp rcx, rdx
    x64.jne __destruct_ItemArray.check_owned_0
  slice_cleanup_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+32]
    x64.mov [rbp-16], rcx
    x64.test rcx, rcx
    x64.jz __nonnull_skip_0
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.jmp __destruct_ItemArray.skip_buf_0
  check_owned_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+16]
    x64.mov rdx, -2
    x64.cmp rcx, rdx
    x64.je __destruct_ItemArray.skip_buf_0
  free_buf_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_decref_managed_elements
    x64.mov rcx, [rbp-8]
    x64.mov rdx, [rcx+32]
    x64.mov rbx, -3
    x64.cmp rdx, rbx
    x64.je __destruct_ItemArray.skip_buf_0
  raw_free_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rax+0]
    x64.call mm_raw_free
    x64.jmp __destruct_ItemArray.skip_buf_0
  skip_buf_0:
    x64.jmp __destruct_ItemArray.done
  done:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @Item.create(value: i64) -> Item {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = value} {type = i64}
    %1 = maxon.struct_literal @Item
    maxon.scope_end [value]
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %11 = maxon.call @ItemArray.create
    maxon.assign %11 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %11 {var = arr} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 7 : i64}
    %13 = maxon.call @Item.create %12
    maxon.assign %13 {var = __call_tmp_1} {decl = 1 : i1}
    maxon.assign %13 {var = item} {decl = 1 : i1}
    %14 = maxon.struct_var_ref item
    maxon.call @ItemArray.push %11, %14
    %15 = maxon.struct_var_ref arr
    %16 = maxon.literal {value = 0 : i64}
    %19, %18 = maxon.try_call @ItemArray.get %15, %16
    %20 = maxon.literal {value = 0 : i64}
    %21 = maxon.binop %18, %20 {op = ne}
    maxon.cond_br %21 [then: otherwise_default_error_0, else: otherwise_default_success_0]
  otherwise_default_error_0:
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.call @Item.create %22
    maxon.assign %23 {var = __call_tmp_2} {decl = 1 : i1}
    maxon.assign %23 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_success_0:
    maxon.assign %19 {var = __try_result_0} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %24 = maxon.struct_var_ref __try_result_0
    maxon.assign %24 {var = got} {decl = 1 : i1}
    %25 = maxon.struct_var_ref got
    %26 = maxon.field_access .value %25
    %27 = maxon.literal {value = 0 : i64}
    %28 = maxon.binop %26, %27 {op = lt}
    %29 = maxon.literal {value = 255 : i64}
    %30 = maxon.binop %26, %29 {op = gt}
    %31 = maxon.binop %28, %30 {op = or}
    maxon.cond_br %31 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at array-push-struct-incref.test:19: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [arr, item, got, __call_tmp_2, __try_result_0]
    maxon.return %26
  }
}
=== standard
module {
  func @Item.create(value: i64) -> i64 {
  entry:
    %0 = func.param value : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @main() -> u8 {
  entry:
    %53 = arith.constant {value = 0 : i64}
    memref.store %53, __call_tmp_0
    %8 = func.call @ItemArray.create
    memref.store %8, arr
    %11 = arith.constant {value = 7 : i64}
    %12 = func.call @Item.create %11
    memref.store %12, item
    %15 = memref.load arr : i64
    %16 = memref.load item : i64
    func.call @ItemArray.push %15, %16
    %17 = arith.constant {value = 0 : i64}
    %18 = memref.load arr : i64
    %19, %20 = func.try_call @ItemArray.get %18, %17
    memref.store %19, __callret_0
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.cmpi ne %20, %21
    cf.cond_br %22 [then: otherwise_default_error_0, else: otherwise_default_success_0]
  otherwise_default_error_0:
    %23 = arith.constant {value = 0 : i64}
    %24 = func.call @Item.create %23
    memref.store %24, __call_tmp_0
    memref.store %24, __try_result_0
    %27 = memref.load __try_result_0 : i64
    std.call_runtime @mm_incref %27
    cf.br otherwise_default_continue_0
  otherwise_default_success_0:
    %28 = memref.load __callret_0 : i64
    memref.store %28, __try_result_0
    cf.br otherwise_default_continue_0
  otherwise_default_continue_0:
    %29 = memref.load __try_result_0 : i64
    memref.store %29, got
    %30 = memref.load got : i64
    std.call_runtime @mm_incref %30
    %31 = memref.load got : i64
    %32 = memref.load_indirect %31+0
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 255 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_0
    %39 = std.ptr_to_i64 %38
    std.call_runtime @mrt_panic %39
  __range_ok_0:
    %40 = memref.load __try_result_0 : i64
    std.call_runtime_if_nonnull @mm_decref %40
    %42 = memref.load __call_tmp_0 : i64
    std.call_runtime_if_nonnull @mm_decref %42
    %44 = memref.load got : i64
    std.call_runtime_if_nonnull @mm_decref %44
    %46 = memref.load item : i64
    std.call_runtime_if_nonnull @mm_decref %46
    %48 = memref.load arr : i64
    std.call_runtime_if_nonnull @mm_decref %48
    func.return %32
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    %55 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    %56 = func.param ptr : StdI64
    memref.store %56, __destr_ptr
    %59 = memref.load __destr_ptr : i64
    %60 = memref.load_indirect %59+16
    %61 = arith.constant {value = -1 : i64}
    %62 = arith.cmpi eq %60, %61
    cf.cond_br %62 [then: slice_cleanup_0, else: check_owned_0]
  slice_cleanup_0:
    %63 = memref.load __destr_ptr : i64
    %64 = memref.load_indirect %63+32
    std.call_runtime_if_nonnull @mm_decref %64
    cf.br skip_buf_0
  check_owned_0:
    %65 = memref.load __destr_ptr : i64
    %66 = memref.load_indirect %65+16
    %67 = arith.constant {value = -2 : i64}
    %68 = arith.cmpi ne %66, %67
    cf.cond_br %68 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %69 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %69
    %70 = memref.load __destr_ptr : i64
    %71 = memref.load_indirect %70+32
    %72 = arith.constant {value = -3 : i64}
    %73 = arith.cmpi ne %71, %72
    cf.cond_br %73 [then: raw_free_0, else: skip_buf_0]
  raw_free_0:
    %74 = memref.load __destr_ptr : i64
    %75 = memref.load_indirect %74+0
    std.call_runtime @mm_raw_free %75
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @Item.create(value: i64) -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.str x1, [x0, #0]
    arm64.ldr x2, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x3, [x29, #-8]
    arm64.mov x0, x3
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=128
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.bl ItemArray.create
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #7
    arm64.bl Item.create
    arm64.str x0, [x29, #-24]
    arm64.ldr x1, [x29, #-16]
    arm64.ldr x2, [x29, #-24]
    arm64.ldr x0, [x29, #-16]
    arm64.ldr x1, [x29, #-24]
    arm64.bl ItemArray.push
    arm64.ldr x3, [x29, #-16]
    arm64.ldr x0, [x29, #-16]
    arm64.mov x1, #0
    arm64.bl ItemArray.get
    arm64.str x0, [x29, #-32]
    arm64.mov x4, #0
    arm64.cmp x1, x4
    arm64.cset x5, ne
    arm64.cmp x5, #0
    arm64.b.ne main.otherwise_default_error_0
    arm64.b main.otherwise_default_success_0
  otherwise_default_error_0:
    arm64.mov x0, #0
    arm64.bl Item.create
    arm64.str x0, [x29, #-8]
    arm64.str x0, [x29, #-40]
    arm64.ldr x0, [x29, #-40]
    arm64.bl mm_incref
    arm64.b main.otherwise_default_continue_0
  otherwise_default_success_0:
    arm64.ldr x0, [x29, #-32]
    arm64.str x0, [x29, #-40]
    arm64.b main.otherwise_default_continue_0
  otherwise_default_continue_0:
    arm64.ldr x0, [x29, #-40]
    arm64.str x0, [x29, #-48]
    arm64.ldr x1, [x29, #-48]
    arm64.ldr x0, [x29, #-48]
    arm64.bl mm_incref
    arm64.ldr x2, [x29, #-48]
    arm64.ldr x3, [x2, #0]
    arm64.mov x4, #0
    arm64.cmp x3, x4
    arm64.cset x5, lt
    arm64.mov x6, #255
    arm64.cmp x3, x6
    arm64.cset x7, gt
    arm64.orr x8, x5, x7
    arm64.cmp x8, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-40]
    arm64.str x3, [x29, #-56]
    arm64.cmp x0, #0
    arm64.b.eq main.__skip_guarded_43
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_43
    arm64.ldr x1, [x29, #-8]
    arm64.cmp x1, #0
    arm64.b.eq main.__skip_guarded_45
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_45
    arm64.ldr x2, [x29, #-48]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_47
    arm64.ldr x0, [x29, #-48]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_47
    arm64.ldr x3, [x29, #-24]
    arm64.cmp x3, #0
    arm64.b.eq main.__skip_guarded_49
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_49
    arm64.ldr x4, [x29, #-16]
    arm64.cmp x4, #0
    arm64.b.eq main.__skip_guarded_51
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_51
    arm64.ldr x0, [x29, #-56]
    arm64.epilogue stack_size=128
    arm64.ret
  }
  func @__destruct_Item(ptr: i64) {
  entry:
    arm64.b __destruct_Item.done
  done:
    arm64.ret
  }
  func @__destruct_ItemArray(ptr: i64) {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-1
    arm64.cmp x1, x2
    arm64.cset x3, eq
    arm64.cmp x3, #0
    arm64.b.ne __destruct_ItemArray.slice_cleanup_0
    arm64.b __destruct_ItemArray.check_owned_0
  slice_cleanup_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #32]
    arm64.str x1, [x29, #-16]
    arm64.cmp x1, #0
    arm64.b.eq __destruct_ItemArray.__skip_guarded_9
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label __destruct_ItemArray.__skip_guarded_9
    arm64.b __destruct_ItemArray.skip_buf_0
  check_owned_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #16]
    arm64.mov x2, #-2
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne __destruct_ItemArray.free_buf_0
    arm64.b __destruct_ItemArray.skip_buf_0
  free_buf_0:
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_decref_managed_elements
    arm64.ldr x1, [x29, #-8]
    arm64.ldr x2, [x1, #32]
    arm64.mov x3, #-3
    arm64.cmp x2, x3
    arm64.cset x4, ne
    arm64.cmp x4, #0
    arm64.b.ne __destruct_ItemArray.raw_free_0
    arm64.b __destruct_ItemArray.skip_buf_0
  raw_free_0:
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x0, #0]
    arm64.mov x0, x1
    arm64.bl mm_raw_free
    arm64.b __destruct_ItemArray.skip_buf_0
  skip_buf_0:
    arm64.b __destruct_ItemArray.done
  done:
    arm64.epilogue stack_size=48
    arm64.ret
  }
}
```

<!-- test: release-before-break -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'loop'
		let c = Counter.create(i)
		if c.n == 1 'check'
			result = c.n
			break
		end 'check'
		i = i + 1
	end 'loop'
	return result
end 'main'
```
```exitcode
1
```
```RequiredIR:x64-windows
=== maxon
module {
  func @Counter.create(n: i64) -> Counter {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.struct_literal @Counter
    maxon.scope_end [n]
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    maxon.assign %12 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %13 = maxon.literal {value = 3 : i64}
    %14 = maxon.var_ref {var = i} {type = i64}
    %15 = maxon.binop %14, %13 {op = lt}
    maxon.cond_br %15 [then: loop_0, else: loop_0.exit]
  loop_0:
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.call @Counter.create %16
    maxon.assign %17 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %17 {var = c} {decl = 1 : i1}
    %18 = maxon.struct_var_ref c
    %19 = maxon.field_access .n %18
    %20 = maxon.literal {value = 1 : i64}
    %21 = maxon.binop %19, %20 {op = eq}
    maxon.cond_br %21 [then: check_0, else: check_0.after]
  check_0:
    %22 = maxon.struct_var_ref c
    %23 = maxon.field_access .n %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.exit
  check_0.after:
    %24 = maxon.literal {value = 1 : i64}
    %25 = maxon.var_ref {var = i} {type = i64}
    %26 = maxon.binop %25, %24 {op = add}
    maxon.assign %26 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.header
  loop_0.exit:
    %27 = maxon.var_ref {var = result} {type = i64}
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 4294967295 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-break.test:23: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result, i]
    maxon.return %27
  }
}
=== standard
module {
  func @Counter.create(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @main() -> u32 {
  entry:
    %8 = arith.constant {value = 0 : i64}
    memref.store %8, result
    %9 = arith.constant {value = 0 : i64}
    memref.store %9, i
    cf.br loop_0.header
  loop_0.header:
    %10 = arith.constant {value = 3 : i64}
    %11 = memref.load i : i64
    %12 = arith.cmpi lt %11, %10
    cf.cond_br %12 [then: loop_0, else: loop_0.exit]
  loop_0:
    %13 = memref.load i : i64
    %14 = func.call @Counter.create %13
    memref.store %14, c
    %17 = memref.load c : i64
    %18 = memref.load_indirect %17+0
    %19 = arith.constant {value = 1 : i64}
    %20 = arith.cmpi eq %18, %19
    cf.cond_br %20 [then: check_0, else: check_0.after]
  check_0:
    %21 = memref.load c : i64
    %22 = memref.load_indirect %21+0
    memref.store %22, result
    %23 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %23
    cf.br loop_0.exit
  check_0.after:
    %25 = arith.constant {value = 1 : i64}
    %26 = memref.load i : i64
    %27 = arith.addi %26, %25
    memref.store %27, i
    %28 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %28
    cf.br loop_0.header
  loop_0.exit:
    %30 = memref.load result : i64
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 4294967295 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %36 = memref.lea_symdata __panic_msg_0
    %37 = std.ptr_to_i64 %36
    std.call_runtime @mrt_panic %37
  __range_ok_0:
    func.return %30
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    %39 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @Counter.create(n: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-16], rcx
    x64.mov rcx, 8
    x64.xor edx, edx
    x64.mov r8, 1
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.mov [rax+0], rcx
    x64.mov rdx, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=32
    x64.xor eax, eax
    x64.mov [rbp-8], rax
    x64.xor ecx, ecx
    x64.mov [rbp-16], rcx
    x64.jmp main.loop_0.header
  loop_0.header:
    x64.mov rax, 3
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.jge main.loop_0.exit
  loop_0:
    x64.mov rax, [rbp-16]
    x64.mov rcx, [rbp-16]
    x64.call Counter.create
    x64.mov [rbp-24], rax
    x64.mov rcx, [rbp-24]
    x64.mov rdx, [rcx+0]
    x64.mov rbx, 1
    x64.cmp rdx, rbx
    x64.jne main.check_0.after
  check_0:
    x64.mov rax, [rbp-24]
    x64.mov rcx, [rax+0]
    x64.mov [rbp-8], rcx
    x64.mov rdx, [rbp-24]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-24]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.jmp main.loop_0.exit
  check_0.after:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.add rcx, rax
    x64.mov [rbp-16], rcx
    x64.mov rdx, [rbp-24]
    x64.test rdx, rdx
    x64.jz __nonnull_skip_1
    x64.mov rcx, [rbp-24]
    x64.call mm_decref
    x64.label __nonnull_skip_1
    x64.jmp main.loop_0.header
  loop_0.exit:
    x64.mov rax, [rbp-8]
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    x64.jmp __destruct_Counter.done
  done:
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @Counter.create(n: i64) -> Counter {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.struct_literal @Counter
    maxon.scope_end [n]
    maxon.return %1
  }
  func @main() -> i64 {
  entry:
    %11 = maxon.literal {value = 0 : i64}
    maxon.assign %11 {var = result} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %12 = maxon.literal {value = 0 : i64}
    maxon.assign %12 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %13 = maxon.literal {value = 3 : i64}
    %14 = maxon.var_ref {var = i} {type = i64}
    %15 = maxon.binop %14, %13 {op = lt}
    maxon.cond_br %15 [then: loop_0, else: loop_0.exit]
  loop_0:
    %16 = maxon.var_ref {var = i} {type = i64}
    %17 = maxon.call @Counter.create %16
    maxon.assign %17 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %17 {var = c} {decl = 1 : i1}
    %18 = maxon.struct_var_ref c
    %19 = maxon.field_access .n %18
    %20 = maxon.literal {value = 1 : i64}
    %21 = maxon.binop %19, %20 {op = eq}
    maxon.cond_br %21 [then: check_0, else: check_0.after]
  check_0:
    %22 = maxon.struct_var_ref c
    %23 = maxon.field_access .n %22
    maxon.assign %23 {var = result} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.exit
  check_0.after:
    %24 = maxon.literal {value = 1 : i64}
    %25 = maxon.var_ref {var = i} {type = i64}
    %26 = maxon.binop %25, %24 {op = add}
    maxon.assign %26 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.scope_end [c]
    maxon.br loop_0.header
  loop_0.exit:
    %27 = maxon.var_ref {var = result} {type = i64}
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 255 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-break.test:23: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end [result, i]
    maxon.return %27
  }
}
=== standard
module {
  func @Counter.create(n: i64) -> i64 {
  entry:
    %0 = func.param n : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @main() -> u8 {
  entry:
    %8 = arith.constant {value = 0 : i64}
    memref.store %8, result
    %9 = arith.constant {value = 0 : i64}
    memref.store %9, i
    cf.br loop_0.header
  loop_0.header:
    %10 = arith.constant {value = 3 : i64}
    %11 = memref.load i : i64
    %12 = arith.cmpi lt %11, %10
    cf.cond_br %12 [then: loop_0, else: loop_0.exit]
  loop_0:
    %13 = memref.load i : i64
    %14 = func.call @Counter.create %13
    memref.store %14, c
    %17 = memref.load c : i64
    %18 = memref.load_indirect %17+0
    %19 = arith.constant {value = 1 : i64}
    %20 = arith.cmpi eq %18, %19
    cf.cond_br %20 [then: check_0, else: check_0.after]
  check_0:
    %21 = memref.load c : i64
    %22 = memref.load_indirect %21+0
    memref.store %22, result
    %23 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %23
    cf.br loop_0.exit
  check_0.after:
    %25 = arith.constant {value = 1 : i64}
    %26 = memref.load i : i64
    %27 = arith.addi %26, %25
    memref.store %27, i
    %28 = memref.load c : i64
    std.call_runtime_if_nonnull @mm_decref %28
    cf.br loop_0.header
  loop_0.exit:
    %30 = memref.load result : i64
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 255 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %36 = memref.lea_symdata __panic_msg_0
    %37 = std.ptr_to_i64 %36
    std.call_runtime @mrt_panic %37
  __range_ok_0:
    func.return %30
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    %39 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @Counter.create(n: i64) -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.str x1, [x0, #0]
    arm64.ldr x2, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x3, [x29, #-8]
    arm64.mov x0, x3
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=80
    arm64.mov x0, #0
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.str x1, [x29, #-16]
    arm64.b main.loop_0.header
  loop_0.header:
    arm64.mov x0, #3
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, lt
    arm64.cmp x2, #0
    arm64.b.ne main.loop_0
    arm64.b main.loop_0.exit
  loop_0:
    arm64.ldr x0, [x29, #-16]
    arm64.bl Counter.create
    arm64.str x0, [x29, #-24]
    arm64.ldr x1, [x29, #-24]
    arm64.ldr x2, [x1, #0]
    arm64.mov x3, #1
    arm64.cmp x2, x3
    arm64.cset x4, eq
    arm64.cmp x4, #0
    arm64.b.ne main.check_0
    arm64.b main.check_0.after
  check_0:
    arm64.ldr x0, [x29, #-24]
    arm64.ldr x1, [x0, #0]
    arm64.str x1, [x29, #-8]
    arm64.ldr x2, [x29, #-24]
    arm64.cmp x2, #0
    arm64.b.eq main.__skip_guarded_21
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_21
    arm64.b main.loop_0.exit
  check_0.after:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.add x2, x1, x0
    arm64.str x2, [x29, #-16]
    arm64.ldr x3, [x29, #-24]
    arm64.cmp x3, #0
    arm64.b.eq main.__skip_guarded_28
    arm64.ldr x0, [x29, #-24]
    arm64.bl mm_decref
    arm64.label main.__skip_guarded_28
    arm64.b main.loop_0.header
  loop_0.exit:
    arm64.ldr x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x3, #255
    arm64.cmp x0, x3
    arm64.cset x4, gt
    arm64.orr x5, x2, x4
    arm64.cmp x5, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=80
    arm64.ret
  }
  func @__destruct_Counter(ptr: i64) {
  entry:
    arm64.b __destruct_Counter.done
  done:
    arm64.ret
  }
}
```

<!-- test: release-before-return-in-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Wrapper'

function compute(flag Integer) returns Integer
	if flag > 0 'check'
		@heap let w = Wrapper.create(flag)
		return w.val + 1
	end 'check'
	return 0
end 'compute'

function main() returns ExitCode
	return compute(5)
end 'main'
```
```exitcode
6
```
```RequiredIR:x64-windows
=== maxon
module {
  func @Wrapper.create(val: i64) -> Wrapper {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = val} {type = i64}
    %1 = maxon.struct_literal @Wrapper
    maxon.scope_end [val]
    maxon.return %1
  }
  func @compute(flag: i64) -> i64 {
  entry:
    %11 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = gt}
    maxon.cond_br %13 [then: check_0, else: check_0.after]
  check_0:
    %14 = maxon.var_ref {var = flag} {type = i64}
    %15 = maxon.call @Wrapper.create %14
    maxon.assign %15 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %15 {var = w} {decl = 1 : i1}
    %16 = maxon.struct_var_ref w
    %17 = maxon.field_access .val %16
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.binop %17, %18 {op = add}
    maxon.scope_end [flag, w]
    maxon.return %19
  check_0.after:
    %20 = maxon.literal {value = 0 : i64}
    maxon.scope_end [flag]
    maxon.return %20
  }
  func @main() -> i64 {
  entry:
    %21 = maxon.literal {value = 5 : i64}
    %22 = maxon.call @compute %21
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %22, %23 {op = lt}
    %25 = maxon.literal {value = 4294967295 : i64}
    %26 = maxon.binop %22, %25 {op = gt}
    %27 = maxon.binop %24, %26 {op = or}
    maxon.cond_br %27 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:21: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %22
  }
}
=== standard
module {
  func @Wrapper.create(val: i64) -> i64 {
  entry:
    %0 = func.param val : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @compute(flag: i64) -> i64 {
  entry:
    %8 = func.param flag : StdI64
    memref.store %8, flag
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi gt %8, %9
    cf.cond_br %10 [then: check_0, else: check_0.after]
  check_0:
    %11 = memref.load flag : i64
    %12 = func.call @Wrapper.create %11
    memref.store %12, w
    %15 = memref.load w : i64
    %16 = memref.load_indirect %15+0
    %17 = arith.constant {value = 1 : i64}
    %18 = arith.addi %16, %17
    %19 = memref.load w : i64
    std.call_runtime_if_nonnull @mm_decref %19
    func.return %18
  check_0.after:
    %21 = arith.constant {value = 0 : i64}
    func.return %21
  }
  func @main() -> u32 {
  entry:
    %23 = arith.constant {value = 5 : i64}
    %24 = func.call @compute %23
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 4294967295 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %30 = memref.lea_symdata __panic_msg_0
    %31 = std.ptr_to_i64 %30
    std.call_runtime @mrt_panic %31
  __range_ok_0:
    func.return %24
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    %32 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== x86
module {
  func @Wrapper.create(val: i64) -> i64 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-16], rcx
    x64.mov rcx, 8
    x64.xor edx, edx
    x64.mov r8, 1
    x64.call mm_alloc
    x64.mov [rbp-8], rax
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-16]
    x64.mov [rax+0], rcx
    x64.mov rdx, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call mm_incref
    x64.mov rax, [rbp-8]
    x64.epilogue
    x64.ret
  }
  func @compute(flag: i64) -> i64 {
  entry:
    x64.prologue stack_size=32
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.cmp rcx, rax
    x64.jle compute.check_0.after
  check_0:
    x64.mov rax, [rbp-8]
    x64.mov rcx, [rbp-8]
    x64.call Wrapper.create
    x64.mov [rbp-16], rax
    x64.mov rcx, [rbp-16]
    x64.mov rdx, [rcx+0]
    x64.mov rbx, 1
    x64.add rdx, rbx
    x64.mov rsi, [rbp-16]
    x64.mov [rbp-24], rdx
    x64.test rsi, rsi
    x64.jz __nonnull_skip_0
    x64.mov rcx, [rbp-16]
    x64.call mm_decref
    x64.label __nonnull_skip_0
    x64.mov rax, [rbp-24]
    x64.epilogue
    x64.ret
  check_0.after:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 5
    x64.call compute
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    x64.jmp __destruct_Wrapper.done
  done:
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @Wrapper.create(val: i64) -> Wrapper {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = val} {type = i64}
    %1 = maxon.struct_literal @Wrapper
    maxon.scope_end [val]
    maxon.return %1
  }
  func @compute(flag: i64) -> i64 {
  entry:
    %11 = maxon.param {index = 0 : i32} {name = flag} {type = i64}
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.binop %11, %12 {op = gt}
    maxon.cond_br %13 [then: check_0, else: check_0.after]
  check_0:
    %14 = maxon.var_ref {var = flag} {type = i64}
    %15 = maxon.call @Wrapper.create %14
    maxon.assign %15 {var = __call_tmp_0} {decl = 1 : i1}
    maxon.assign %15 {var = w} {decl = 1 : i1}
    %16 = maxon.struct_var_ref w
    %17 = maxon.field_access .val %16
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.binop %17, %18 {op = add}
    maxon.scope_end [flag, w]
    maxon.return %19
  check_0.after:
    %20 = maxon.literal {value = 0 : i64}
    maxon.scope_end [flag]
    maxon.return %20
  }
  func @main() -> i64 {
  entry:
    %21 = maxon.literal {value = 5 : i64}
    %22 = maxon.call @compute %21
    %23 = maxon.literal {value = 0 : i64}
    %24 = maxon.binop %22, %23 {op = lt}
    %25 = maxon.literal {value = 255 : i64}
    %26 = maxon.binop %22, %25 {op = gt}
    %27 = maxon.binop %24, %26 {op = or}
    maxon.cond_br %27 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at release-before-return-in-block.test:21: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %22
  }
}
=== standard
module {
  func @Wrapper.create(val: i64) -> i64 {
  entry:
    %0 = func.param val : StdI64
    %1 = arith.constant {value = 8 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 1 : i64}
    %4 = std.call_runtime @mm_alloc %1, %2, %3
    memref.store %4, __struct_0
    %5 = memref.load __struct_0 : i64
    memref.store_indirect %0, %5+0
    %6 = memref.load __struct_0 : i64
    std.call_runtime @mm_incref %6
    %7 = memref.load __struct_0 : i64
    func.return %7
  }
  func @compute(flag: i64) -> i64 {
  entry:
    %8 = func.param flag : StdI64
    memref.store %8, flag
    %9 = arith.constant {value = 0 : i64}
    %10 = arith.cmpi gt %8, %9
    cf.cond_br %10 [then: check_0, else: check_0.after]
  check_0:
    %11 = memref.load flag : i64
    %12 = func.call @Wrapper.create %11
    memref.store %12, w
    %15 = memref.load w : i64
    %16 = memref.load_indirect %15+0
    %17 = arith.constant {value = 1 : i64}
    %18 = arith.addi %16, %17
    %19 = memref.load w : i64
    std.call_runtime_if_nonnull @mm_decref %19
    func.return %18
  check_0.after:
    %21 = arith.constant {value = 0 : i64}
    func.return %21
  }
  func @main() -> u8 {
  entry:
    %23 = arith.constant {value = 5 : i64}
    %24 = func.call @compute %23
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 255 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %30 = memref.lea_symdata __panic_msg_0
    %31 = std.ptr_to_i64 %30
    std.call_runtime @mrt_panic %31
  __range_ok_0:
    func.return %24
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    %32 = func.param ptr : StdI64
    cf.br done
  done:
    func.return
  }
}
=== arm64
module {
  func @Wrapper.create(val: i64) -> i64 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-16]
    arm64.mov x0, #8
    arm64.mov x1, #0
    arm64.mov x2, #1
    arm64.bl mm_alloc
    arm64.str x0, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.ldr x1, [x29, #-16]
    arm64.str x1, [x0, #0]
    arm64.ldr x2, [x29, #-8]
    arm64.ldr x0, [x29, #-8]
    arm64.bl mm_incref
    arm64.ldr x3, [x29, #-8]
    arm64.mov x0, x3
    arm64.epilogue stack_size=48
    arm64.ret
  }
  func @compute(flag: i64) -> i64 {
  entry:
    arm64.prologue stack_size=64
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, gt
    arm64.cmp x2, #0
    arm64.b.ne compute.check_0
    arm64.b compute.check_0.after
  check_0:
    arm64.ldr x0, [x29, #-8]
    arm64.bl Wrapper.create
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.ldr x2, [x1, #0]
    arm64.mov x3, #1
    arm64.add x4, x2, x3
    arm64.ldr x5, [x29, #-16]
    arm64.str x4, [x29, #-24]
    arm64.cmp x5, #0
    arm64.b.eq compute.__skip_guarded_13
    arm64.ldr x0, [x29, #-16]
    arm64.bl mm_decref
    arm64.label compute.__skip_guarded_13
    arm64.ldr x0, [x29, #-24]
    arm64.epilogue stack_size=64
    arm64.ret
  check_0.after:
    arm64.mov x0, #0
    arm64.epilogue stack_size=64
    arm64.ret
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #5
    arm64.bl compute
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
  func @__destruct_Wrapper(ptr: i64) {
  entry:
    arm64.b __destruct_Wrapper.done
  done:
    arm64.ret
  }
}
```

### Continue cleans up loop body scope

When `continue` is used inside a loop that allocates structs, the loop body scope
must be exited before jumping back to the header. Otherwise the struct allocated in
that iteration leaks.

<!-- test: release-before-continue -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'loop'
		i = i + 1
		let c = Counter.create(i)
		if c.n == 3 'skip'
			continue
		end 'skip'
		total = total + c.n
	end 'loop'
	return total
end 'main'
```
```exitcode
12
```

### Labeled break from nested loop cleans up both scopes

When breaking out of an outer loop from inside an inner loop, both the inner
loop body scope and the outer loop body scope must be cleaned up.

<!-- test: release-labeled-break-nested -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Pair
	export var a as Integer
	export var b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'outer'
		let p = Pair.create(i, b: i * 10)
		var j = 0
		while j < 3 'inner'
			let q = Pair.create(j, b: j * 10)
			if p.a == 1 'check'
				if q.a == 2 'found'
					result = p.b + q.b
					break 'outer'
				end 'found'
			end 'check'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return result
end 'main'
```
```exitcode
30
```

### Break from for-in loop cleans up loop scope

For-in loops use the same scope mechanism as while loops. Breaking out of a
for-in loop with struct allocations must clean up the loop body scope.

<!-- test: release-break-for-in -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Item
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Item'

function main() returns ExitCode
	let items = [10, 20, 30, 40, 50]
	var result = 0
	for item in items 'search'
		let wrapped = Item.create(item)
		if wrapped.val == 30 'found'
			result = wrapped.val
			break
		end 'found'
	end 'search'
	return result
end 'main'
```
```exitcode
30
```

### Error propagation cleans up function scope

When a `try` call propagates an error to the caller, the function's scope must
be exited so that any allocations made before the try call are freed.

<!-- test: release-on-error-propagation -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Resource
	export var id as Integer

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Resource'

enum ResourceError
	case notFound
end 'ResourceError'

function loadResource() returns Resource throws ResourceError
	throw ResourceError.notFound
end 'loadResource'

function process() returns Integer throws ResourceError
	@heap let marker = Resource.create(42)
	let res = try loadResource()
	return res.id + marker.id
end 'process'

function main() returns ExitCode
	let result = try process() otherwise 'err'
		return 99
	end 'err'
	return result
end 'main'
```
```exitcode
99
```

### Error propagation from inside block scope

When error propagation happens inside a nested block scope (e.g., inside an if),
all intermediate scopes plus the function scope must be cleaned up.

<!-- test: release-on-error-propagation-in-block -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var val as Integer

	static function create(val Integer) returns Self
		return Self{val: val}
	end 'create'
end 'Wrapper'

enum LookupError
	case missing
end 'LookupError'

function failingLookup() returns Integer throws LookupError
	throw LookupError.missing
end 'failingLookup'

function compute(flag Integer) returns Integer throws LookupError
	let w = Wrapper.create(flag)
	if w.val > 0 'positive'
		let inner = Wrapper.create(w.val * 2)
		let result = try failingLookup()
		return result + inner.val
	end 'positive'
	return 0
end 'compute'

function main() returns ExitCode
	let result = try compute(5) otherwise 'err'
		return 77
	end 'err'
	return result
end 'main'
```
```exitcode
77
```

### Generic function with scope ops (monomorphization)

When a generic function (via interface alias / typealias with) contains scope
management ops (scope_enter, scope_exit, move), the monomorphization pass must
clone these ops correctly. Missing handlers would crash the compiler.

<!-- test: generic-function-with-scope-ops -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Wrapper'

typealias WrapperArray = Array with Wrapper

function firstOrDefault(arr WrapperArray) returns Wrapper
	let fallback = Wrapper.create(0)
	let result = try arr.get(0) otherwise fallback
	return result
end 'firstOrDefault'

function main() returns ExitCode
	var arr = WrapperArray.create()
	let w = Wrapper.create(42)
	arr.push(w)
	let got = firstOrDefault(arr)
	return got.value
end 'main'
```
```exitcode
42
```

### Reference identity in generic context

The `is` operator (MaxonRefEqOp) must be handled by function cloner and
monomorphization passes when it appears in generic or cloned functions.

<!-- test: ref-identity-in-cloned-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function isSame(a Box, b Box) returns Integer
	if a is b 'same'
		return 1
	end 'same'
	return 0
end 'isSame'

function main() returns ExitCode
	let x = Box.create(10)
	let y = x
	let z = Box.create(10)
	let same = isSame(x, b: y)
	let diff = isSame(x, b: z)
	return same + diff
end 'main'
```
```exitcode
1
```

### A shared record is never written through

A never-mutated `String` literal, and an empty container nothing writes to, are each emitted ONCE as a
shared immortal record. That is only safe while nothing can write through them, and a value stored
into a heap PLACE — a struct field, a container slot, a union payload — can always be fetched back out
and written through from somewhere the compiler cannot follow. Such a value gets its own record.

The two cases below are the doors that are not an array slot; the array ones live in `arrays.md`. Each
was a real corruption: the write grew the SHARED record in place, so an untouched occurrence of the
same value elsewhere in the program read the mutated bytes, and the grown buffer leaked because an
immortal record's destructor is 0.

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

<!-- test: retained-payload-that-escapes-shares-the-owner-s-record -->
⭐ **THE LEAK GATE FOR THAT DOOR, AND IT IS THE EXIT CODE THAT CARRIES IT.** The case above keeps
the payload inside the match; this one lets it ESCAPE and be appended through from outside. The bind
is an INCREF rather than a copy, so the escaped `String` is a SECOND owner of the record the union
still holds, and the append is visible through both — which is what the `stdout` block says.

⚠ **THE `exitcode` BLOCK IS THE HALF THAT CARRIES IT, AND THE CASE ABOVE CANNOT SUBSTITUTE FOR IT.**
Until `4a69525121` this program printed the RIGHT line and then exited **101** — `MM raw leak:
1 allocation(s) remain` — because the literal was emitted as a shared immortal record, the append
grew it in place, and an immortal record's destructor is 0 so the grown buffer was never freed.
Measured on `maxon-sharp` at `4a69525121~1`: this program, exit 101, stdout correct. The sibling
above goes red on that same compiler, but by its `stdout` (`untouched=tag!`) — it is a WRONG ANSWER
case that happens to leak, and it was written at `035fae8fa9`, AFTER the fix, so it has never been
red. A leak whose answer is right is a shape it cannot express, and this one is exactly that.
`specs-shv2/union-managed-payload.md` carries this same program and used to settle the bootstrap's
behaviour in prose — "answers identically (measured — same two lines of stdout)" — which is a
measurement of `stdout` standing in for a question only the exit code could answer. This case is
that measurement written down where it can fail.
```maxon
union M
	silent
	text(body String)
end 'M'

function grab(m M) returns String
	return match m 'k'
		silent gives "a fallback literal long enough to be a real heap string"
		text(s) gives s
	end 'k'
end 'grab'

function main() returns ExitCode
	let m = M.text("original payload, long enough to be a real heap allocation")
	var escaped = grab(m)
	escaped.append(" MUTATED")
	print(grab(m))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
original payload, long enough to be a real heap allocation MUTATED
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
