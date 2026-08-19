---
feature: struct-managed-field
status: experimental
keywords: [struct, field, String, ownership, consume, move, drop, destructor, cascade]
category: ownership
---

# Struct Managed Fields (P1.4a wave 2)

## Documentation

A `type` (struct) field may now be **managed** — a `String`, another `struct`, or
a payload-bearing `union`. Such a field holds an owned heap pointer, and the
struct box takes ownership of it:

- **Construct gives the field its OWN reference (⚖ user ruling, 2026-08-12).**
  `Self{name: value}` leaves the field slot holding exactly one reference, and
  which act supplies it depends on what `value` is: a borrowed `String` literal
  is promoted to an owned heap copy; an owned TEMPORARY (`Inner.create(5)`) has
  no other owner, so the slot ADOPTS the `+1` it already carries; and a bare
  reference to a live owned BINDING is CO-OWNED — the slot increfs, the binding
  stays readable, and each releases the one reference it took.

  > This bullet used to read *"transfers ownership … no incref, no copy … a bare
  > reference to an owned binding is moved-from (a later read is `E3102`)"*. That
  > was the move-only rule, retracted because two sinks for one value each need a
  > reference of their own. `consume-then-reuse-co-owns` and
  > `managed-double-store-co-owns` below are the two cases that were flipped.
- **The struct drops each managed field.** A struct with at least one managed
  field gets a synthesized `__destruct_<Struct>` that drops every managed field
  through its own type's destructor — a `String` via `__str_decref`, a nested
  managed struct via *its* `__destruct_<Struct>`, a scalar-only struct via
  `__mm_decref` — then frees the box. There is no tag switch: every field is
  always present. A moved-out field slot is null and its null-guard skips it.
- **A field write acquires the new value before releasing the old.**
  `s.name = value` takes the field's reference to `value` and only THEN decrefs
  the record the slot was holding. The order is load-bearing rather than
  cosmetic: released first, an indirect self-assignment (`dst.f = src.f` where
  both names reach one box) frees the record and then increfs freed memory. See
  `managed-field-store-order.md`.
- **A parameter stored into a durable field is CONSUMED, and the call site
  CO-OWNS.** A constructor `create(inner Inner) returns Self` whose body stores
  `inner` into a field consumes its parameter — so the CALLER hands over a
  reference. Since the 2026-08-12 ruling that reference is a fresh one the caller
  increfs rather than the one its own binding holds, so the caller's argument
  stays readable (`consume-then-reuse-co-owns`) and the struct's destructor
  releases exactly the reference the call took. A BORROWED argument at a
  consuming position is co-owned by the same rule (the transitive-consume ruling
  of 2026-08-04), not refused.

## Tests

<!-- test: string-field-construct-drop -->
A struct with a `String` field, constructed from a literal, is dropped at scope
exit through its cascade — the field is freed, no leak (a leak is exit 101).
```maxon
type Named
	export var name as String

	static function create() returns Self
		return Self{name: "hello"}
	end 'create'
end 'Named'

function main() returns ExitCode
	let n = Named.create()
	return 7
end 'main'
```
```exitcode
7
```

<!-- test: string-field-read -->
The `String` field can be read back through the box and compared, then dropped — no leak.
```maxon
type Named
	export var name as String

	static function create() returns Self
		return Self{name: "hello"}
	end 'create'
end 'Named'

function main() returns ExitCode
	let n = Named.create()
	if n.name == "hello" 'match'
		return 5
	end 'match'
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: two-string-fields-drop -->
A struct with two `String` fields drops both through the cascade — no leak.
```maxon
type Pair
	export var first as String
	export var second as String

	static function create() returns Self
		return Self{first: "ab", second: "cde"}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create()
	if p.first == "ab" and p.second == "cde" 'both'
		return 5
	end 'both'
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: nested-struct-string-field-drop -->
A struct whose field is another struct that owns a `String`: the outer cascade
drops the inner struct through *its* destructor, which drops the String — no leak.
```maxon
type Inner
	export var label as String

	static function create() returns Self
		return Self{label: "hi"}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create()
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: nested-managed-field-ranged-create-param -->
The inner struct's `create` takes a RANGED-INT-ALIAS parameter. That alias adds a name
to the project interner, shifting its ids relative to the signatures interner's. The
destructor-needs closure re-fetched the inner struct layout from `signatures` (ids in
the signatures interner) but resolved its managed field's type id against
`project.typeNames` — so it misread the field's name and panicked on this VALID program.
The closure now takes the project layout the caller already holds, like the destructor
body synthesis does, so the id and the interner that resolves it always agree.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String

	static function create(x Integer) returns Self
		return Self{label: "v{x}"}
	end 'create'
end 'BoxA'

type WrapA
	export var inner as BoxA

	static function create() returns Self
		return Self{inner: BoxA.create(1)}
	end 'create'
end 'WrapA'

function main() returns ExitCode
	let w = WrapA.create()
	return 3
end 'main'
```
```exitcode
3
```

<!-- test: param-consumed-into-field -->
A constructor stores its struct parameter into a field, consuming it. The caller
passes an owned local; the struct owns the field and drops it once — no leak, no
double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let i = Inner.create(10)
	let o = Outer.create(i)
	return o.inner.x
end 'main'
```
```exitcode
10
```

<!-- test: nested-field-write-through-loaded-box -->
A struct-typed field can be read and written through the loaded box
(`o.inner.x`), and the whole chain is dropped once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let i = Inner.create(10)
	var o = Outer.create(i)
	o.inner.x = 42
	return o.inner.x
end 'main'
```
```exitcode
42
```

<!-- test: string-field-reassign-drops-old -->
Writing a managed field drops the old value before storing the new one — the old
String is freed once, no leak.
```maxon
type Holder
	export var text as String

	static function create() returns Self
		return Self{text: "old"}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.text = "brandnew"
	if h.text == "brandnew" 'updated'
		return 8
	end 'updated'
	return 0
end 'main'
```
```exitcode
8
```

<!-- test: managed-self-field-write -->
Writing a managed field from inside an instance method (`name = n`) drops the old
value and moves the new one in — the borrowed String argument is promoted to an
owned copy, so nothing dangles and nothing leaks.
```maxon
type Box
	export var name as String

	export function setName(n String)
		name = n
	end 'setName'

	static function create() returns Self
		return Self{name: "aaa"}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create()
	b.setName("bb")
	if b.name == "bb" 'ok'
		return 2
	end 'ok'
	return 0
end 'main'
```
```exitcode
2
```

<!-- test: consume-then-reuse-co-owns -->
Reusing an argument after it was consumed into a struct field is LEGAL: a consuming position is a durable
sink, so `Outer.create(i)` gives the field its own reference (⚖ 2026-08-12) rather than stealing `i`'s.
`i` stays live and drops its own reference at scope exit, so `Inner`'s box is released exactly the two
times it was referenced. (It used to be E3102 at `i.x`.)
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let i = Inner.create(10)
	let o = Outer.create(i)
	return i.x
end 'main'
```
```exitcode
10
```

<!-- test: managed-double-store-co-owns -->
Storing one managed value into TWO owning fields of a struct literal is legal, and the two fields
CO-OWN it. `Self{a: v, b: v}` takes a reference per slot, so the String record ends at three (both
fields plus the consumed parameter `v`) and is released three times — twice by `Pair`'s destructor,
once by `v`'s scope-exit drop. Reading both fields back is what makes the aliasing observable rather
than merely tolerated.

⛔ **This was E3102, justified as *"shv2 is move-only (no incref), so a single String cannot be owned by
two fields"* — a premise the durable-store ruling (⚖ 2026-08-12) retracted.** The repeated-owning-move
guard survives only for an OPAQUE `T` field, whose shared body has no descriptor to take a reference
through; `generic-types/error.generic-double-store-managed` is that surviving case.
```maxon
type Pair
	export var a as String
	export var b as String

	static function create(v String) returns Self
		return Self{a: v, b: v}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create("{7}")
	print("{p.a}{p.b}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
77
```

<!-- test: error.borrowed-param-consumed -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function forward(i Inner) returns Outer
	return Outer.create(i)
end 'forward'

function main() returns ExitCode
	let i = Inner.create(10)
	let o = forward(i)
	return o.inner.x
end 'main'
```
```exitcode
10
```

<!-- test: boxed-union-field-construct-match -->
A struct field that is a payload-bearing (boxed) `union` is constructed by moving the
box into the field slot (P1.4b wave 2c), read back through the box, and matched — the
scalar payloads bind and the container is dropped at scope exit through its cascade,
no leak.
```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Holder
	export var pending as OuterErr

	static function create() returns Self
		return Self{pending: OuterErr.unterminatedString(7, column: 13)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	let r = match h.pending 'k'
		unterminatedString(line, column) gives (line + column)
		unexpectedEof(line, column) gives (line + column + 100)
	end 'k'
	return r
end 'main'
```
```exitcode
20
```

<!-- test: boxed-union-field-reassign-drops-old -->
Reassigning a boxed-union field drops the old box before moving the new one in — the
old union box is freed once, no leak, and the new value reads back.
```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Holder
	export var pending as OuterErr

	static function create() returns Self
		return Self{pending: OuterErr.unexpectedEof(1, column: 2)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.pending = OuterErr.unterminatedString(7, column: 13)
	let r = match h.pending 'k'
		unterminatedString(line, column) gives (line + column)
		unexpectedEof(line, column) gives (line + column + 100)
	end 'k'
	return r
end 'main'
```
```exitcode
20
```

<!-- test: boxed-union-field-drop-scope-exit -->
A container holding a boxed-union field it never touches is still dropped at scope
exit — the field's box is freed exactly once by the cascade, no leak (a leak is 101).
```maxon
typealias N = int(0 to i64.max)

union OuterErr implements Error
	unterminatedString(line N, column N)
	unexpectedEof(line N, column N)
end 'OuterErr'

type Holder
	export var pending as OuterErr
	export var flag as bool

	static function create() returns Self
		return Self{pending: OuterErr.unterminatedString(7, column: 13), flag: false}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return 4
end 'main'
```
```exitcode
4
```

<!-- test: reassign-managed-struct-field -->
Reassigning a field whose type is a struct that owns managed heap drops the OLD
struct through *its* `__destruct_<T>` (not the trivial `__mm_decref`, which would free
only the box and leak its String) before moving the new one in — the old `Named "a"`'s
String is freed exactly once, the new value reads back, no leak (a leak is 101).
```maxon
type Named
	export var label as String

	static function create(l String) returns Self
		return Self{label: l}
	end 'create'
end 'Named'

type Holder
	export var n as Named

	static function create(first Named) returns Self
		return Self{n: first}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Named.create("a"))
	h.n = Named.create("b")
	if h.n.label == "b" 'updated'
		return 9
	end 'updated'
	return 0
end 'main'
```
```exitcode
9
```

<!-- test: reassign-managed-struct-field-in-loop -->
Reassigning a managed-owning-struct field on every iteration drops each old value
through its destructor — the String of every superseded `Named` is freed once, no leak
accumulates across the loop.
```maxon
type Named
	export var label as String

	static function create(l String) returns Self
		return Self{label: l}
	end 'create'
end 'Named'

type Holder
	export var n as Named

	static function create(first Named) returns Self
		return Self{n: first}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Named.create("start"))
	var i = 0
	while i < 5 'loop'
		h.n = Named.create("iter")
		i = i + 1
	end 'loop'
	if h.n.label == "iter" 'ok'
		return 6
	end 'ok'
	return 0
end 'main'
```
```exitcode
6
```

<!-- test: reassign-nested-managed-struct-field -->
The reassigned field's struct itself owns a struct-with-String field. The old value
drops through a two-level cascade (`__destruct_Mid` → `__destruct_Inner` → the String)
on the reassignment, freeing every managed field exactly once.
```maxon
type Inner
	export var label as String

	static function create(l String) returns Self
		return Self{label: l}
	end 'create'
end 'Inner'

type Mid
	export var inner as Inner

	static function create(i Inner) returns Self
		return Self{inner: i}
	end 'create'
end 'Mid'

type Holder
	export var m as Mid

	static function create(first Mid) returns Self
		return Self{m: first}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Mid.create(Inner.create("a")))
	h.m = Mid.create(Inner.create("b"))
	if h.m.inner.label == "b" 'ok'
		return 4
	end 'ok'
	return 0
end 'main'
```
```exitcode
4
```

<!-- test: reassign-trivial-struct-field -->
Reassigning a field whose struct owns NO managed heap still drops the old box through
the trivial `__mm_decref` — the managed-struct routing must not fire for a scalar-only
struct, so the emitted drop is unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Holder
	export var p as Point

	static function create(first Point) returns Self
		return Self{p: first}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Point.create(1))
	h.p = Point.create(2)
	return h.p.x
end 'main'
```
```exitcode
2
```
