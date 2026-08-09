---
feature: generic-type-substitution
status: experimental
keywords: [type, uses, with, generic, Self, Iterable, substitution]
category: type-system
---

# Generic Type Substitution

## Documentation

shv2 compiles ONE body per generic type, so everything written inside `type Outer uses T` is
written in the **declaration view**: in terms of `T`, of `Self`, and of instances over those.
A concrete receiver fixes every one of them. This spec covers the case where the declaration
view reaches **another generic type**.

### A bare generic type name inside a type-parameter scope

Inside the body of `type Outer uses T`, a bare reference to another generic type `Inner` means
`Inner with Outer.T`. The arguments are not absent — they are the enclosing scope's:

```text
type Inner uses T
	typealias TArr = Array with T
	var v as TArr
	export static function make(v TArr) returns Self
		return Self{v: v}
	end 'make'
end 'Inner'

type Outer uses T
	typealias OArr = Array with T
	var items as OArr
	export function wrap() returns Inner        // means: Inner with Outer.T
		return Inner.make(items)                 // parameter reads as Array with Outer.T
	end 'wrap'
end 'Outer'
```

This carries both halves of one mechanism:

- **the ARGUMENT.** `Inner.make`'s parameter is declared `Array with Inner.T`. A type parameter's
  identity is `(declaring type, parameter name)`, so `Array with Inner.T` and `Array with Outer.T`
  are genuinely two different instances — and the call is legal only because the bare `Inner`
  binds `Inner.T` to `Outer.T`, which makes the parameter read as `Array with Outer.T`.
- **the RETURN.** `wrap()` returns `Inner with Outer.T`, so at a call site where the receiver is
  an `Outer with Integer` the result must be an `Inner with Integer` — or nothing downstream can
  type a call on it.

### The binding is by parameter NAME, and it is all-or-nothing

Each of the named type's own `uses` names must be a name the enclosing scope declares. A partial
overlap binds nothing rather than binding what it can — `type Pair uses A, B` named inside
`type Outer uses A` has no `B` for the scope to stand for — and the name then resolves exactly as
it did before, with the ordinary refusals speaking for it.

### The type's own name still means `Self`

`Self.make(x)`, and the type's own name written inside its own body, are the DECLARATION view (the
base), not an instance over the enclosing parameters. The instance is supplied at the call site.

### A generic type may conform to a generic interface

A generic type's `implements … with (…)` clause may bind an associated type to one of its own type
parameters and to another generic type of the program, and `for … in` walks it through the cursor
protocol exactly as it walks a non-generic conformer.

## Tests

<!-- test: bare-generic-name-in-a-generic-body -->
### A composite parameter of ANOTHER generic type, and its return
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses T
	typealias TArr = Array with T
	var v as TArr
	export static function make(v TArr) returns Self
		return Self{v: v}
	end 'make'
	export function first() returns T throws ArrayError
		return try v.get(0)
	end 'first'
end 'Inner'

type Outer uses T
	typealias OArr = Array with T
	var items as OArr
	export static function create(items OArr) returns Self
		return Self{items: items}
	end 'create'
	export function wrap() returns Inner
		return Inner.make(items)
	end 'wrap'
end 'Outer'

typealias O = Outer with Integer
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	let o = O.create(a)
	let w = o.wrap()
	return (try w.first() otherwise 0) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: bare-generic-name-as-a-field-type -->
### A bare generic name is a field's type, at the enclosing scope's parameters
```maxon
typealias Integer = int(i64.min to i64.max)

type Cell uses T
	export var v as T
	export static function make(v T) returns Self
		return Self{v: v}
	end 'make'
	export function get() returns T
		return self.v
	end 'get'
end 'Cell'

type Holder uses T
	export var cell as Cell
	export static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
	export function value() returns T
		return self.cell.get()
	end 'value'
end 'Holder'

typealias IntCell = Cell with Integer
typealias IntHolder = Holder with Integer

function main() returns ExitCode
	let h = IntHolder.create(IntCell.make(9))
	return h.value() as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: bare-generic-name-with-two-parameters -->
### Two parameters bind by NAME, not by position
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot uses Key, Value
	export var k as Key
	export var v as Value
	export static function make(k Key, v Value) returns Self
		return Self{k: k, v: v}
	end 'make'
	export function value() returns Value
		return self.v
	end 'value'
end 'Slot'

type Table uses Key, Value
	export var one as Slot
	export static function create(one Slot) returns Self
		return Self{one: one}
	end 'create'
	export function only() returns Slot
		return self.one
	end 'only'
end 'Table'

typealias IntSlot = Slot with (Integer, Integer)
typealias IntTable = Table with (Integer, Integer)

function main() returns ExitCode
	let t = IntTable.create(IntSlot.make(1, v: 5))
	return t.only().value() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.a-name-the-scope-does-not-bind-binds-nothing -->
### A parameter name the enclosing scope does not declare binds nothing
`Inner uses U` inside `type Outer uses T` has no `U` for the scope to stand for, so the bare
`Inner` is left alone and the argument meets the declaration view it always did. The refusal is
positioned; the C# bootstrap accepts the same program silently and dies in the assembler
(`E9001: Unresolved label: Inner.first`).
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses U
	typealias UArr = Array with U
	var v as UArr
	export static function make(v UArr) returns Self
		return Self{v: v}
	end 'make'
	export function first() returns U throws ArrayError
		return try v.get(0)
	end 'first'
end 'Inner'

type Outer uses T
	typealias OArr = Array with T
	var items as OArr
	export static function create(items OArr) returns Self
		return Self{items: items}
	end 'create'
	export function wrap() returns Inner
		return Inner.make(items)
	end 'wrap'
end 'Outer'

typealias O = Outer with Integer
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	let o = O.create(a)
	let w = o.wrap()
	return (try w.first() otherwise 0) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:10: argument type mismatch for 'v': expected 'Inner.UArr', got 'Outer.OArr'
```

<!-- test: own-name-in-a-generic-body-is-still-Self -->
### The type's own name inside its own body is the declaration view
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var v as T
	export static function make(x T) returns Self
		return Self{v: x}
	end 'make'
	export static function twice(x T) returns Box
		return Box.make(x)
	end 'twice'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	return IntBox.twice(6).v as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: for-in-over-a-generic-instance -->
### `for … in` walks a generic instance through the cursor protocol
A generic instance carries its base name AND its concrete type arguments, which is strictly more
than a plain struct has — so the loop reads its `current()`/`advance()` off the base and the
element off the instance.
```maxon
typealias Integer = int(i64.min to i64.max)

type Walker uses T
	typealias TArr = Array with T
	var items as TArr
	var at = 0

	export static function create(items TArr) returns Self
		return Self{items: items}
	end 'create'

	export function current() returns T
		return try items.get(at) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		at = at + 1
		if at >= items.count() 'done'
			throw IterationError.exhausted
		end 'done'
	end 'advance'
end 'Walker'

typealias IntArray = Array with Integer
typealias IntWalker = Walker with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(3)
	a.push(4)
	var w = IntWalker.create(a)
	var total = 0
	for x in w 'walk'
		total = total + x
	end 'walk'
	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: substituted-return-of-a-built-record-is-already-owned -->
### A substituted return the shared body BUILT is already owned, and the caller must not co-own it
⭐⭐ **THE CALLER'S `+1` IS OWED FOR AN OPAQUE `T` AND FOR NOTHING ELSE.** A shared generic body cannot
classify a bare `T` — an unconditional retain there would fault on a trivial instantiation's raw scalar —
so `Walker.current() returns T` hands the caller a BORROW and the caller takes the reference itself
(`Parser.coOwnSubstitutedCallResult`). That premise is a property of the **opaque** return, not of
substitution: a return type that is a CONCRETE managed record — a tuple `(T, T)`, whose shape the body
knows whichever type `T` is — is classified by `emitOwnedValueReturn` like any other, so the body already
promotes-or-passes-through an OWNED record and the hand-off is discharged before the caller sees it.

⛔ **Co-owning one anyway leaks it, once per call.** The caller minted a SECOND reference — a trivial
tuple gets a whole fresh record (`copyTupleValue`), a managed-element one an `__mm_retain` — and then
dropped only that one, while the record the body actually built was adopted by nobody. The answer stays
correct and the leak gate is the only thing that can see it, which is why this case asserts an exit code
that is only reachable through `__mm_leak_check`.

The reaching program needs no loop, no iterator and no `Map`: one generic method whose substituted return
type is a freshly built tuple, called once. Returns `42`, and exits 101 if the record is leaked.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Holder uses T
	typealias Pair = (T, T)
	typealias Items = Array with T
	var items as Items

	export static function create(items Items) returns Self
		return Self{items: items}
	end 'create'

	export function pair() returns Pair
		let a = try items.get(0) otherwise panic("oob")
		let b = try items.get(1) otherwise panic("oob")
		return (a, b)
	end 'pair'
end 'Holder'

typealias IntHolder = Holder with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(11)
	a.push(31)
	let h = IntHolder.create(a)
	let (x, y) = h.pair()
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: substituted-return-of-a-managed-element-tuple-is-already-owned -->
### The same, through the INCREF arm rather than the record copy
A tuple with a MANAGED element is not copyable — a shallow copy would free one `String` twice — so the
promotion increfs it instead. That is the other arm of the same door, and it leaks the same way: `+1`
taken by the caller, `-1` spent by the caller, and the body's own record adopted by nobody. Returns `42`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Labeller uses T
	typealias Tagged = (String, T)
	typealias Items = Array with T
	var items as Items

	export static function create(items Items) returns Self
		return Self{items: items}
	end 'create'

	export function tagged() returns Tagged
		let v = try items.get(0) otherwise panic("oob")
		return ("ab{items.count()}", v)
	end 'tagged'
end 'Labeller'

typealias IntLabeller = Labeller with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(39)
	let l = IntLabeller.create(a)
	let (name, v) = l.tagged()
	return v + name.count()
end 'main'
```
```exitcode
42
```

<!-- test: generic-type-conforming-to-Iterable -->
### A generic type conforming to `Iterable`
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxIter uses T implements Iterator with T
	typealias TArr = Array with T
	var items as TArr
	var at = 0

	export static function create(items TArr) returns Self throws IterationError
		if items.count() == 0 'empty'
			throw IterationError.exhausted
		end 'empty'
		return Self{items: items}
	end 'create'

	export function current() returns T
		return try items.get(at) otherwise panic("oob")
	end 'current'

	export function advance() throws IterationError
		at = at + 1
		if at >= items.count() 'done'
			throw IterationError.exhausted
		end 'done'
	end 'advance'
end 'BoxIter'

type Box uses T implements Iterable with (T, BoxIter)
	typealias TArr = Array with T
	var items as TArr

	export static function create(items TArr) returns Self
		return Self{items: items}
	end 'create'

	export function createIterator() returns BoxIter throws IterationError
		return try BoxIter.create(items)
	end 'createIterator'
end 'Box'

typealias IntArray = Array with Integer
typealias B = Box with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(3)
	a.push(4)
	let b = B.create(a)
	var total = 0
	for x in b 'walk'
		total = total + x
	end 'walk'
	return total as ExitCode
end 'main'
```
```exitcode
7
```
