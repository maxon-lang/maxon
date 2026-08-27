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

### The DROP CASCADE reads the same instance view every other door does

A bare generic name in a field position is not a scalar and not the base struct — it is an INSTANCE,
and the instance is what owns the heap. So `Holder with String`'s destructor must reach the
`Cell with String` its `cell` field holds, and through it that cell's `String`. Reaching only the base
`Cell` instead drops the field through the base's own classification — `Cell`'s single field is the
opaque `T`, which owns nothing — so the cell's BOX is reclaimed and everything the type argument
brought with it is stranded.

That is a leak with no diagnostic (exit **101**, the leak gate), and it is invisible to a test whose
type argument is trivial: `Holder with Integer` has nothing for the missing drop to strand, which is
exactly why the case above passes and the one below did not.

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

<!-- test: bare-generic-name-as-a-managed-field-type -->
### A bare generic name holding a MANAGED type argument is dropped through its instance
The `Integer` case above cannot see this: the outer cascade reached the BASE `Cell`, whose only field is
the opaque `T`, and so released the cell's box and nothing else. With a `String` argument that is a
stranded heap record — exit 101, the leak gate — while the program prints the right answer.
```maxon
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

typealias StrCell = Cell with String
typealias StrHolder = Holder with String

function main() returns ExitCode
	let h = StrHolder.create(StrCell.make("a string long enough to force a heap allocation"))
	return h.value().count() as ExitCode
end 'main'
```
```exitcode
47
```

<!-- test: inner-alias-nested-instance-is-dropped-through-its-substituted-instance -->
### The same nesting reached through an INNER TYPEALIAS
`typealias Inner = Cell with T` inside `type Holder uses T` is the other spelling of the same field, and it
is the spelling `stdlib/Array.maxon` uses for its own buffer (`typealias ElementMemory = __ManagedMemory
with Element`). The sweep records it as a bare `named("Holder.Inner")` because the alias registry is filled
after the file is swept, so the cascade used to classify the field by its ALIAS NAME and resolve that to the
UNSUBSTITUTED `Cell with Holder.T` — the trivial box drop, and a stranded string. Measured at exit 101.
```maxon
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
	typealias Inner = Cell with T
	export var cell as Inner
	export static function create(cell Inner) returns Self
		return Self{cell: cell}
	end 'create'
	export function value() returns T
		return self.cell.get()
	end 'value'
end 'Holder'

typealias StrCell = Cell with String
typealias StrHolder = Holder with String

function main() returns ExitCode
	let h = StrHolder.create(StrCell.make("a string long enough to force a heap allocation"))
	return h.value().count() as ExitCode
end 'main'
```
```exitcode
47
```

<!-- test: bare-generic-name-nested-three-levels -->
### THREE levels of bare generic nesting all cascade
Each level's field is a bare generic name at the level above's parameters, so the drop has to descend
`Top with String` → `Mid with String` → `Cell with String` → the `String`.
```maxon
type Cell uses T
	export var v as T
	export static function make(v T) returns Self
		return Self{v: v}
	end 'make'
	export function get() returns T
		return self.v
	end 'get'
end 'Cell'

type Mid uses T
	export var cell as Cell
	export static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
	export function value() returns T
		return self.cell.get()
	end 'value'
end 'Mid'

type Top uses T
	export var mid as Mid
	export static function create(mid Mid) returns Self
		return Self{mid: mid}
	end 'create'
	export function value() returns T
		return self.mid.value()
	end 'value'
end 'Top'

typealias StrCell = Cell with String
typealias StrMid = Mid with String
typealias StrTop = Top with String

function main() returns ExitCode
	let t = StrTop.create(StrMid.create(StrCell.make("a string long enough to force a heap allocation")))
	return t.value().count() as ExitCode
end 'main'
```
```exitcode
47
```

<!-- test: bare-generic-name-managed-field-reassigned -->
### Reassigning the field through a CONCRETE receiver releases the OLD instance exactly once
The receiver fixes the argument, so the write drops the displaced value through the concrete
`__destruct_Cell_String`: a missing release leaks the first string and a doubled one drives the allocation
count negative, and both are exit 101. This is the shape that stays legal — see the refusal below for the
one that cannot.
```maxon
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

typealias StrCell = Cell with String
typealias StrHolder = Holder with String

function main() returns ExitCode
	var h = StrHolder.create(StrCell.make("first string long enough to force a heap allocation"))
	h.cell = StrCell.make("second string, longer still, and also heap allocated")
	return h.value().count() as ExitCode
end 'main'
```
```exitcode
52
```

<!-- test: error.bare-generic-name-field-reassigned-in-the-shared-body -->
### The SHARED body cannot reassign such a field, because it cannot name the drop
`__drop_type_param` releases an opaque `T` field by reading `T`'s destructor out of the enclosing instance's
layout descriptor — but a descriptor describes the PARAMETERS, not the instances built over them, so it
holds `String`'s `__str_decref` and nothing that names `__destruct_Cell_String`. The one callee the shared
body can pick is the non-concrete instance's own `__mm_decref`, which frees the cell's box and strands the
string: measured at exit **101** before this refusal existed. The refusal is on DIVERGENCE, so an
all-trivial program — where `__mm_decref` really is every instantiation's drop — is untouched.
```maxon
type Cell uses T
	export var v as T
	export static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Cell'

type Holder uses T
	export var cell as Cell
	export static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
	export function replace(next Cell)
		self.cell = next
	end 'replace'
end 'Holder'

typealias StrCell = Cell with String
typealias StrHolder = Holder with String

function main() returns ExitCode
	var h = StrHolder.create(StrCell.make("first string long enough to force a heap allocation"))
	h.replace(StrCell.make("second string, longer still, and also heap allocated"))
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:8: Unsupported: reassigning 'cell' of 'Holder', whose type is a generic instance over this type's OWN parameters — the shared generic body compiles once for every instantiation, so the drop for the value being displaced is not one callee: it is `__mm_decref` here and something else at some instantiation. The box's own destructor releases the field correctly, so reassign it through a CONCRETE receiver instead; a descriptor slot carrying a nested instance's per-instantiation destructor is a later slice
```

<!-- test: bare-generic-name-managed-field-as-array-element -->
### The same cascade reached through an array element's descriptor
An `Array with (Holder with String)` releases each element through the layout descriptor's
`destroyFunc`, which is the very `__destruct_Holder_String` the direct scope-exit drop names — so the
element walk strands the same string when the cascade stops at the base.
```maxon
type Cell uses T
	export var v as T
	export static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Cell'

type Holder uses T
	export var cell as Cell
	export static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Holder'

typealias StrCell = Cell with String
typealias StrHolder = Holder with String
typealias HolderArray = Array with StrHolder

function main() returns ExitCode
	var a = HolderArray.create()
	a.push(StrHolder.create(StrCell.make("a string long enough to force a heap allocation")))
	a.push(StrHolder.create(StrCell.make("another string long enough to force a heap allocation")))
	return a.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: bare-generic-name-nesting-is-deep-cloneable -->
### The CLONE direction CASCADES two instances deep, and is not silently shallow
⭐ **THIS CASE WAS A REFUSAL UNTIL W162, AND ITS SUBJECT IS WHAT THAT RUNG BUILT.** The drop side of a
nested bare generic name has always cascaded; the clone side had no cascade to reach, because a non-`Array`
generic instance had no `__clone_<instance>` at all — so the gate and the strategy agreed to refuse, which
is what kept the two directions from disagreeing (a gate that admitted the copy with no cloner behind it
would byte-blit the inner box's pointer and free it twice).

The cloner now exists and is the exact dual of the drop cascade it mirrors: `__clone_Holder_String` clones
its `cell` field through `__clone_Cell_String`, which clones its `v` through `__str_clone` — the same three
levels `__destruct_Holder_String` → `__destruct_Cell_String` → `__str_decref` releases. Neither inner cloner
is named anywhere the module scan can see, so `noteClonerUsage`'s closure has to reach them through the
instance nodes it registers.

The source array is built and dropped inside the helper, so what `main` reads is the clone alone: a shallow
copy at ANY of the three levels would leave it pointing at a freed record, and the leak gate would see the
other half of the same mistake.
```maxon
type Cell uses T
	export var v as T
	export static function make(v T) returns Self
		return Self{v: v}
	end 'make'
end 'Cell'

type Holder uses T
	export var cell as Cell
	export static function create(cell Cell) returns Self
		return Self{cell: cell}
	end 'create'
end 'Holder'

typealias StrCell = Cell with String
typealias StrHolder = Holder with String
typealias HolderArray = Array with StrHolder

function detached() returns HolderArray
	var a = HolderArray.create()
	a.push(StrHolder.create(StrCell.make("a string long enough to force a heap allocation")))
	return a.clone()
	// a, its Holder box, its Cell box and their String are freed when this function returns
end 'detached'

function main() returns ExitCode
	let b = detached()

	let h = try b.get(0) otherwise return 91
	if not h.cell.v.equals("a string long enough to force a heap allocation") 'lostTheString'
		return 92
	end 'lostTheString'

	return b.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: bare-generic-name-trivial-argument-stays-inert -->
### A trivial type argument gets NO release, with the leak gate live
The control for the four cases above: `Cell with Integer` owns nothing but its box, so its cascade must
stay the trivial box drop. A spurious release here would decref an `Integer` as if it were a record —
a wild free, or an over-release the same gate reports. The `String` local is what keeps that gate
meaningful rather than vacuous.
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
	let s = "a string long enough to force a heap allocation"
	if s.count() != 47 'unexpectedLength'
		return 1
	end 'unexpectedLength'
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

⚠ The `otherwise` is DIVERGING, and it has to be for this case to keep testing its own subject. A VALUE
fallback merges with the try's success value through one owned phi, and since P1.7 slice 3b-vi-a a
`returns U` hand-off is an owned `+1` even when `U` is unbound — so `otherwise 0` is a second, perfectly
correct refusal (`E3059`, an `int` fallback against a `type parameter` result) that fires during the parse
and hides the semantic one this case is about.
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
	return (try w.first() otherwise panic("Inner binds nothing")) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:16: argument type mismatch for 'v': expected 'Inner.UArr', got 'Outer.OArr'
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
	let w = IntWalker.create(a)
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
⭐⭐ **THE EXTRA `+1` IS OWED FOR AN OPAQUE `T` AND FOR NOTHING ELSE.** A shared generic body cannot classify
a bare `T` — an unconditional retain there would fault on a trivial instantiation's raw scalar — so
`Walker.current() returns T` needs a descriptor-gated one. That obligation is a property of the **opaque**
return, not of substitution: a return type that is a CONCRETE managed record — a tuple `(T, T)`, whose shape
the body knows whichever type `T` is — is classified by `emitOwnedValueReturn` like any other, so the body
already promotes-or-passes-through an OWNED record and the hand-off is discharged before the caller sees it.

⚠ **WHO TAKES THAT `+1` MOVED, AND THIS PARAGRAPH NAMED THE OLD PARTY.** It said the CALLER took it
(`Parser.coOwnSubstitutedCallResult`); `582d9c45b9` (P1.7 slice 3b-vi-a) deleted that function and made the
`+1` the CALLEE's, emitted in `emitOwnedValueReturn` through `coOwnBorrowedOpaque` → `__retain_type_param`.
The discrimination this case is about therefore now lives at that one door: `emitOwnedValueReturn` promotes
only a BORROWED value, and the tuple the body just built is already owned. Measured on this program:
`Holder.pair` `__mm_alloc`s the pair record and `__retain_type_param`s each opaque ELEMENT into it, and
`main` spends exactly one `__mm_decref` on the result and takes no reference of its own.

⛔ **Co-owning the record anyway leaks it, once per call, and that is what the exit code is for.** The caller
minted a SECOND reference — a trivial tuple got a whole fresh record (`copyTupleValue`), a managed-element
one an `__mm_retain` — and then dropped only that one, while the record the body actually built was adopted
by nobody. The answer stays correct either way and the leak gate is the only thing that can see it, which is
why this case asserts an exit code that is only reachable through `__mm_leak_check`.

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
