---
feature: generic-type-substitution
status: experimental
keywords: [type, uses, with, generic, Self, Iterable, substitution]
category: type-system
---

# Generic Type Substitution

## Documentation

the compiler compiles ONE body per generic type, so everything written inside `type Outer uses T` is
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

### A base the scope does not bind may not be fed the scope's parameters, nor laid out

A bare name that binds nothing — `Box uses Element` inside `type Outer uses T` — is the BASE, and no
instance stands behind a value of it. Two calls on such a subject are refused (**E3162**), whether the base
is named statically (`Box.create(…)`) or is the type of the receiver (`self.tag.width()`):

- a call that hands a slot written over one of the base's own type parameters — `Element` itself, or an
  instance such as `Array with Element` — a value typed at the enclosing type's parameters: `Box.create(first)`
  with `first as T`. The value would be stored where no type argument describes it, so nothing releases it.
  An overloaded callee is judged by the member its arguments pick;
- a call whose callee needs a layout descriptor — `Holder.create()` whose body sizes an `Element`. The base
  has no descriptor of its own, and the enclosing frame's describes `Outer`'s parameters, not `Holder`'s.

A static call whose arguments fix every one of the base's parameters to a concrete type is not a bare
subject at all: it builds that instance. Name the instance with a `typealias` — `typealias Inner = Box with T`
— and both calls are ordinary; a field typed `Box with T` inline does not parse.

Outside a generic type body there is no scope to feed, but a layout-needing call on a bare base has no
descriptor either unless the calling function carries one of its own: a static whose arguments do not fix every
one of the base's parameters (`Holder.create()` in `main`), and a method called through a receiver of the bare
base type (`t.width()` in `main`), are refused the same way.

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
Both bodies spell the composite as `TArr`: an alias is a brand, and the value `Outer` hands to
`Inner.make` must carry the name `make` declares (`nominal-generic-alias.md`).
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
	typealias TArr = Array with T
	var items as TArr
	export static function create(items TArr) returns Self
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
is named anywhere the module scan can see, so `noteCascadeUsage`'s closure has to reach them through the
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
`Inner` is left alone and stays the base. `make`'s slot is `Array with U`, written over the base's own
parameter, and `items` is an `Array with T`, so the call hands the unbound base a value typed at `Outer`'s
parameter: E3162, positioned at the call rather than accepted silently and left to die in the assembler
(`E9001: Unresolved label: Inner.first`).

⚠ `main` hands the unbound `Inner` on without calling a method on it. `first()` needs a layout descriptor and
the value is the bare base, so calling it would be a second refusal of the same code.
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

function takes(_ Inner) returns ExitCode
	return 7
end 'takes'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	let o = O.create(a)
	return takes(o.wrap())
end 'main'
```
```maxoncstderr
error E3162: <fragment>:22:16: 'Inner' is named without type arguments inside 'type Outer uses T', where its parameter 'U' binds to nothing, so 'Inner.make' may not be handed a value typed at a parameter of 'Outer': no instance of 'Inner' describes what it would hold. Name the instance with a typealias, e.g. 'typealias InnerInstance = Inner with T'
```

<!-- test: error.a-bare-generic-base-may-not-be-fed-the-enclosing-types-parameter -->
### A base the scope does not bind may not be handed a value of the scope's parameter
`Box uses Element` binds nothing inside `type Outer uses T`, so `inner` holds the base and `Box.create(first)`
fixes no instance. `Outer with String`'s drop reaches `inner` through the base, whose `Element` owns nothing,
and the `String` is never released.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{saved: first}
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'

type Outer uses T
	export var inner as Box
	export static function create(first T) returns Self
		return Self{inner: Box.create(first)}
	end 'create'
end 'Outer'

typealias OuterStr = Outer with String

function main() returns ExitCode
	var o = OuterStr.create("alpha")
	o.inner.replace("beta")
	return 0
end 'main'
```
```maxoncstderr
error E3162: <fragment>:15:26: 'Box' is named without type arguments inside 'type Outer uses T', where its parameter 'Element' binds to nothing, so 'Box.create' may not be handed a value typed at a parameter of 'Outer': no instance of 'Box' describes what it would hold. Name the instance with a typealias, e.g. 'typealias BoxInstance = Box with T'
```

<!-- test: error.a-bare-generic-base-may-not-take-a-layout-needing-call -->
### A base the scope does not bind may not take a layout-needing call
`Holder.create` sizes a `Vector with 4 Element`, so it is handed a layout descriptor. The bare `Holder` has
none, and the enclosing frame's describes `Outer`'s parameters rather than `Holder`'s.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

type Outer uses T
	var holder as Holder

	export static function create() returns Self
		return Self{holder: Holder.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create()
	return o.size()
end 'main'
```
```maxoncstderr
error E3162: <fragment>:22:30: 'Holder' is named without type arguments inside 'type Outer uses T', where its parameter 'Element' binds to nothing, so 'Holder.create' has no layout descriptor to be handed: the base has none, and this frame's describes the parameters of 'Outer'. Name the instance with a typealias, e.g. 'typealias HolderInstance = Holder with T'
```

<!-- test: error.a-receiver-of-a-bare-generic-base-may-not-be-fed-the-enclosing-types-parameter -->
### The receiver spelling of the same feed
`Tag.create()` is neither fed nor laid out, so it stays legal and `tag` holds the base. Handing that receiver a
`T` is the feed the static spelling above refuses, reached one call later.
```maxon
typealias Int = int(i64.min to i64.max)

type Tag uses Element
	var n as Int

	export static function create() returns Self
		return Self{n: 3}
	end 'create'

	export function accepts(_ Element) returns Int
		return n
	end 'accepts'
end 'Tag'

type Outer uses T
	var tag as Tag

	export static function create() returns Self
		return Self{tag: Tag.create()}
	end 'create'

	export function probe(x T) returns Int
		return tag.accepts(x)
	end 'probe'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create()
	return o.probe("a")
end 'main'
```
```maxoncstderr
error E3162: <fragment>:24:14: 'Tag' is named without type arguments inside 'type Outer uses T', where its parameter 'Element' binds to nothing, so 'Tag.accepts' may not be handed a value typed at a parameter of 'Outer': no instance of 'Tag' describes what it would hold. Name the instance with a typealias, e.g. 'typealias TagInstance = Tag with T'
```

<!-- test: error.a-receiver-of-a-bare-generic-base-may-not-take-a-layout-needing-call -->
### The receiver spelling of the layout-needing call
`Tag.width` reads `sizeof(Element)` out of the descriptor it is handed. Through the bare receiver there is no
`Element` to size, and the only descriptor in reach is `Outer with String`'s.
```maxon
typealias Int = int(i64.min to i64.max)

type Tag uses Element
	var n as Int

	export static function create() returns Self
		return Self{n: 3}
	end 'create'

	export function width() returns Int
		return sizeof(Element)
	end 'width'
end 'Tag'

type Outer uses T
	var tag as Tag

	export static function create() returns Self
		return Self{tag: Tag.create()}
	end 'create'

	export function width() returns Int
		return tag.width()
	end 'width'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create()
	return o.width()
end 'main'
```
```maxoncstderr
error E3162: <fragment>:24:14: 'Tag' is named without type arguments inside 'type Outer uses T', where its parameter 'Element' binds to nothing, so 'Tag.width' has no layout descriptor to be handed: the base has none, and this frame's describes the parameters of 'Outer'. Name the instance with a typealias, e.g. 'typealias TagInstance = Tag with T'
```

<!-- test: error.a-bare-generic-base-outside-a-generic-body-may-not-take-a-layout-needing-call -->
### Outside a generic body a bare base's layout-needing static has no descriptor either
`main` has no type parameters to bind `Holder` over and `create` takes no argument that could fix
`Element`, so the call names the base, and a frame that is no generic body has no descriptor to hand on.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.size()
end 'main'
```
```maxoncstderr
error E3162: <fragment>:19:17: 'Holder' is named without type arguments and nothing binds its parameter 'Element', so 'Holder.create' has no layout descriptor to be handed: the base has none, and neither does this function. Name the instance with a typealias, giving each of its parameters a type argument
```

<!-- test: error.a-receiver-of-a-bare-generic-base-outside-a-generic-body-may-not-take-a-layout-needing-call -->
### Outside a generic body a bare receiver's layout-needing method has no descriptor either
`Tag.create()` needs no descriptor, so `t` holds the bare base legally. `width` sizes an `Element`, and neither
the base nor `main` has a descriptor to hand it.
```maxon
typealias Int = int(i64.min to i64.max)

type Tag uses Element
	var n as Int

	export static function create() returns Self
		return Self{n: 3}
	end 'create'

	export function width() returns Int
		return sizeof(Element)
	end 'width'
end 'Tag'

function main() returns ExitCode
	let t = Tag.create()
	return t.width() as ExitCode
end 'main'
```
```maxoncstderr
error E3162: <fragment>:18:11: 'Tag' is named without type arguments and nothing binds its parameter 'Element', so 'Tag.width' has no layout descriptor to be handed: the base has none, and neither does this function. Name the instance with a typealias, giving each of its parameters a type argument
```

<!-- test: error.a-bare-generic-base-overload-is-judged-by-the-member-the-arguments-fit -->
### An overloaded factory is judged by the member its arguments fit
`items` is an `Array with T`, which the `IntArray` overload cannot take, so the call resolves to `create(_ Element)`
and hands the bare base's `Element` slot a value typed at `Outer`'s parameter. The `IntArray` member takes no such
value, and it is not the member the call resolves to.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type Box uses Element
	export var n as Int

	export static function create(_ Element) returns Self
		return Self{n: 1}
	end 'create'

	export static function create(first IntArray) returns Self
		return Self{n: first.count()}
	end 'create'
end 'Box'

type Outer uses T
	typealias TArr = Array with T
	export var inner as Box

	export static function create(first T) returns Self
		var items = TArr.create()
		items.push(first)
		return Self{inner: Box.create(items)}
	end 'create'
end 'Outer'

typealias OuterStr = Outer with String

function main() returns ExitCode
	let o = OuterStr.create("alpha {1}")
	return o.inner.n as ExitCode
end 'main'
```
```maxoncstderr
error E3162: <fragment>:24:26: 'Box' is named without type arguments inside 'type Outer uses T', where its parameter 'Element' binds to nothing, so 'Box.create' may not be handed a value typed at a parameter of 'Outer': no instance of 'Box' describes what it would hold. Name the instance with a typealias, e.g. 'typealias BoxInstance = Box with T'
```

<!-- test: a-base-written-with-the-enclosing-parameter-is-fed-and-released -->
### The control: the same feed through `typealias Inner = Box with T`
The instance is spelled, so `inner` holds a `Box with String` at `Outer with String` and its `String` is
released with it — exit 0 rather than 101.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{saved: first}
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'

type Outer uses T
	typealias Inner = Box with T
	export var inner as Inner
	export static function create(first T) returns Self
		return Self{inner: Inner.create(first)}
	end 'create'
end 'Outer'

typealias OuterStr = Outer with String

function main() returns ExitCode
	var o = OuterStr.create("alpha")
	o.inner.replace("beta")
	print("{o.inner.saved}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
beta

```

<!-- test: a-base-written-with-the-enclosing-parameter-takes-a-layout-needing-call -->
### The control: the same layout-needing call through `typealias Held = Holder with T`
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

type Outer uses T
	typealias Held = Holder with T
	var holder as Held

	export static function create() returns Self
		return Self{holder: Held.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'

typealias IntOuter = Outer with Int

function main() returns ExitCode
	let o = IntOuter.create()
	print("{o.size()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4

```

<!-- test: error.a-managed-instantiation-a-later-body-mints-refuses-the-vector-body-first -->
### …and at a MANAGED argument the vector is refused, though the instance is minted below the body that asks
`Holder with String` is written nowhere: `Outer`'s body mints it, below `Holder`'s. A parse asks whether some
instantiation makes the vector's element managed while it emits `Slot.create()`, and the answer it acts on must
be the whole program's rather than the rows the file had minted by then — admitted, this program compiles and a
`for … in` read of a published slot dereferences a null.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'

type Outer uses T
	typealias Held = Holder with T
	var holder as Held

	export static function create() returns Self
		return Self{holder: Held.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create()
	print("{o.size()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:21: Unsupported: `Vector with <N> <type parameter>` — a vector PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
```

<!-- test: error.a-managed-instantiation-a-later-body-mints-refuses-the-vector-use-first -->
The same program with the minting body ABOVE the vector's, which the file's own rows answered all along.
```maxon
typealias Int = int(i64.min to i64.max)

type Outer uses T
	typealias Held = Holder with T
	var holder as Held

	export static function create() returns Self
		return Self{holder: Held.create()}
	end 'create'

	export function size() returns Int
		return holder.size()
	end 'size'
end 'Outer'

typealias StrOuter = Outer with String

function main() returns ExitCode
	let o = StrOuter.create()
	print("{o.size()}\n")
	return 0
end 'main'

type Holder uses Element
	typealias Slot = Vector with 4 Element

	var slot as Slot

	export static function create() returns Self
		return Self{slot: Slot.create()}
	end 'create'

	export function size() returns Int
		return slot.count()
	end 'size'
end 'Holder'
```
```maxoncstderr
error E2015: <fragment>:31:21: Unsupported: `Vector with <N> <type parameter>` — a vector PUBLISHES all N of its slots at `create` by zeroing them, and this generic type is instantiated with a type whose slot is a heap POINTER: a zeroed slot is an element for a trivial instantiation and a NULL for a managed one, so `count()` would answer N while every `get` reports an empty slot and a `for … in` read dereferences the null. Instantiate this type at integer or bool elements only — a `float` TYPE ARGUMENT is refused separately today, for its own reason — or hold the elements in an `Array with <type parameter>`, which publishes nothing and grows by `push`
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

⚠ **THAT `+1` IS THE CALLEE'S.** It is emitted in `emitOwnedValueReturn` through `coOwnBorrowedOpaque` →
`__retain_type_param`. The discrimination this case is about therefore lives at that one door:
`emitOwnedValueReturn` promotes only a BORROWED value, and the tuple the body just built is already owned. Measured
on this program: `Holder.pair` `__mm_alloc`s the pair record and `__retain_type_param`s each opaque ELEMENT into
it, and `main` spends exactly one `__mm_decref` on the result and takes no reference of its own.

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
