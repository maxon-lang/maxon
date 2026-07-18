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

- **Construct moves the value into the field.** `Self{name: value}` transfers
  ownership of `value` into the field slot — no incref, no copy. A borrowed
  `String` literal is promoted to an owned heap copy first; an owned temporary
  (`Inner.create(5)`) is consumed; a bare reference to an owned binding is
  moved-from (a later read is `E3102`).
- **The struct drops each managed field.** A struct with at least one managed
  field gets a synthesized `__destruct_<Struct>` that drops every managed field
  through its own type's destructor — a `String` via `__str_decref`, a nested
  managed struct via *its* `__destruct_<Struct>`, a scalar-only struct via
  `__mm_decref` — then frees the box. There is no tag switch: every field is
  always present. A moved-out field slot is null and its null-guard skips it.
- **A field write drops the old value and moves the new one in.**
  `s.name = value` decrefs the field's current value before storing `value`.
- **A parameter moved into a durable field is CONSUMED.** A constructor
  `create(inner Inner) returns Self` whose body stores `inner` into a field
  consumes its parameter: the caller's argument is moved-from at the call site (a
  later read is `E3102`), and the struct's destructor drops the field exactly
  once. A borrowed argument passed at a consuming position is refused (`E2015` —
  the transitive-consume case arrives with the call-graph fixpoint).

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

<!-- test: error.consume-then-reuse -->
Reusing an argument after it was consumed into a struct field is use-after-move.
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
```maxoncstderr
error E3102: specs/fragments/struct-managed-field/error.consume-then-reuse.test:23:9: use of moved value 'i': its ownership moved to another binding at an earlier bind or assignment
```

<!-- disabled-test: error.borrowed-param-consumed -->
<!-- P1.4a wave 2+ — the transitive-consume case: a borrowed parameter forwarded to a consuming callee position needs the call-graph fixpoint (`wrap` consumes `i` because `Wrapper.create` does). Refused with E2015 until then. -->
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
