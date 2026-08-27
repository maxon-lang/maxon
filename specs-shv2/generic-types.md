---
feature: generic-types
status: experimental
keywords: [type, uses, with, generic, typealias, Self]
category: type-system
---

# Generic Types

## Documentation

A `type` (or `interface`) may declare TYPE PARAMETERS with a `uses` clause, and be
INSTANTIATED with concrete type arguments through a `with` clause on a `typealias`.

### Declaring a generic type

`uses` names one or more type parameters (comma-separated, no parentheses). A parameter
is an opaque type usable anywhere a type is: a field's type, a parameter's type, a return
type.

```text
type Box uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	export function get() returns T
		return self.value
	end 'get'
end 'Box'
```

A multi-parameter type names each parameter, in order:

```text
type Pair uses A, B
	export var first as A
	export var second as B
end 'Pair'
```

### Instantiating a generic type

A `typealias` binds a name to a `Base with Args` instantiation. A single argument needs no
parentheses; two or more are a parenthesized, comma-separated list. The alias name is then a
static-call base for the base type's methods, which are SHARED across every instantiation
(one compiled body, keyed on the opaque type parameter):

```text
typealias IntBox = Box with Integer
typealias IntPair = Pair with (Integer, Integer)

let b = IntBox.create(42)
let v = b.get()
```

### Type arguments: trivial (borrowed) and managed (owned)

A **trivial** type argument — a scalar (`int`/`bool`/`float`/`ExitCode`, a ranged alias) or a
struct/instance whose fields are all trivial — is passed opaque and **borrowed**: the box aliases
it and the caller keeps and drops it (`PointBox.create(p)` leaves `p` usable). A **managed** type
argument — a `String`, a struct with a managed field, a boxed union, or a nested instance that owns
managed heap — is **owned**: the concrete constructor call MOVES it into the box, and the box drops
it exactly once through its synthesized `__destruct_<instance>` cascade (P1.6-B2). So a returned or
escaping managed instance carries VALID content, and there is no leak or double-free. A non-generic
base, or the wrong number of arguments, is rejected.

## Tests

<!-- test: generic-create-get -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	let b = IntBox.create(42)
	let v = b.get()
	if v == 42 'chk'
		return 0
	end 'chk'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: generic-field-read -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	let b = IntBox.create(7)
	if b.value == 7 'chk'
		return 0
	end 'chk'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: generic-field-write -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	var b = IntBox.create(7)
	b.value = 99
	if b.value == 99 'chk'
		return 0
	end 'chk'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: generic-multi-param -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Pair uses A, B
	export var first as A
	export var second as B
	export static function create(a A, b B) returns Self
		return Self{first: a, second: b}
	end 'create'
	export function firstVal() returns A
		return self.first
	end 'firstVal'
	export function secondVal() returns B
		return self.second
	end 'secondVal'
end 'Pair'
typealias IntPair = Pair with (Integer, Integer)
function main() returns ExitCode
	let p = IntPair.create(3, b: 4)
	if p.firstVal() == 3 'c1'
		if p.secondVal() == 4 'c2'
			return 0
		end 'c2'
	end 'c1'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: generic-trivial-struct-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
	export function getX() returns Integer
		return self.x
	end 'getX'
end 'Point'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let p = Point.create(7)
	let pb = PointBox.create(p)
	print("{pb.value.x}")
	return p.getX() - 7
end 'main'
```
```exitcode
0
```
```stdout
7

```

<!-- test: error.non-generic-with -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Plain
	export var x as Integer
end 'Plain'
typealias Bad = Plain with Integer
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2055: <fragment>:6:17: Type 'Plain' has no associated types
```

<!-- test: error.wrong-arity -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Pair uses A, B
	export var first as A
	export var second as B
end 'Pair'
typealias Bad = Pair with Integer
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2056: <fragment>:7:17: generic type 'Pair' expects 2 type argument(s), but 1 were supplied
```

<!-- test: managed-string-arg -->
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	let s = "{42}"
	let b = StrBox.create(s)
	print("{b.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: managed-struct-field-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Holder
	export var label as String
	export var n as Integer
	export static function create(l String, n Integer) returns Self
		return Self{label: l, n: n}
	end 'create'
end 'Holder'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias HolderBox = Box with Holder
function main() returns ExitCode
	let h = Holder.create("{9}", n: 3)
	let b = HolderBox.create(h)
	print("{b.value.label}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9

```

<!-- test: generic-nested-trivial -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias IntBox = Box with Integer
typealias BoxBox = Box with (Box with Integer)
function main() returns ExitCode
	let inner = IntBox.create(5)
	let outer = BoxBox.create(inner)
	print("{outer.value.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5

```

<!-- test: managed-string-arg-loop -->
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	var i = 0
	while i < 100 'loop'
		_ = StrBox.create("{i}")
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: managed-string-arg-moved-not-double-freed -->
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	let s = "{42}"
	let a = StrBox.create(s)
	let b = a
	print("{b.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42

```

<!-- test: managed-instance-escape-owns-content -->
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
function make() returns StrBox
	let s = "{7}"
	return StrBox.create(s)
end 'make'
function main() returns ExitCode
	let b = make()
	let sv = b.value
	return sv.byteLength() - 1
end 'main'
```
```exitcode
0
```

<!-- test: nested-managed-cascade -->
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
typealias BoxBox = Box with (Box with String)
function main() returns ExitCode
	var i = 0
	while i < 50 'loop'
		let inner = StrBox.create("{i}")
		_ = BoxBox.create(inner)
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: generic-string-member-via-generic -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Tagged uses T
	export var value as T
	export var tag as String
	export static function create(v T, tag String) returns Self
		return Self{value: v, tag: tag}
	end 'create'
end 'Tagged'
typealias IntTagged = Tagged with Integer
function main() returns ExitCode
	let t = IntTagged.create(5, tag: "{3}")
	print("{t.value}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5

```

<!-- test: error.generic-double-store-managed -->
```maxon
typealias Integer = int(i64.min to i64.max)
type DPair uses T
	export var a as T
	export var b as T
	export static function create(v T) returns Self
		return Self{a: v, b: v}
	end 'create'
end 'DPair'
typealias StrDPair = DPair with String
function main() returns ExitCode
	let p = StrDPair.create("{7}")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:7:24: use of moved value 'v': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: scalar-double-store -->
```maxon
typealias Integer = int(i64.min to i64.max)
type IntPair
	export var a as Integer
	export var b as Integer
	export static function create(n Integer) returns Self
		return Self{a: n, b: n}
	end 'create'
end 'IntPair'
function main() returns ExitCode
	let p = IntPair.create(7)
	return p.a - 7
end 'main'
```
```exitcode
0
```

<!-- test: opaque-field-reassign-trivial-instantiation-inert -->
Reassigning a bare opaque `T` field inside a shared generic method body is INERT when every instantiation
is trivial (`Box with SmallInt`): the opaque word owns no heap, so the write is a sound scalar plain store
with no drop — unchanged from before P1.7 slice 3b-vii, and exercised on all targets.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias SmallInt = int(0 to 100)
typealias IntBox = Box with SmallInt
function main() returns ExitCode
	var b = IntBox.create(7)
	b.replace(9)
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: reassign-managed-opaque-field-in-generic-method -->
Reassigning a bare opaque `T` field INSIDE a shared generic method body (`self.saved = next`) WORKS when an
instantiation makes it MANAGED (`Box with String`): the field's old opaque value is dropped through the
descriptor-gated single-value drop (`__drop_type_param`), whose install is now decoupled from the array floor,
and the new value transfers in. Old `"alpha"` is freed exactly once — leak-free under `__mm_free` poisoning.
This was a reachable `0xC0000005` fault before P1.7 Finding A.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias StringBox = Box with String
function main() returns ExitCode
	var b = StringBox.create("alpha")
	b.replace("beta")
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: reassign-managed-opaque-field-concrete-instance -->
The same reassignment on a CONCRETE instance (`b.saved = "beta"` where `b` is `Box with String`) WORKS too:
the field retypes to the instance's substituted `String`, so the old String drops through `__str_decref`
(Finding B's concrete-field fix) and the new one moves in — no descriptor needed, the field is concrete here.
Leak-free under `__mm_free` poisoning.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
end 'Box'
typealias StringBox = Box with String
function main() returns ExitCode
	var b = StringBox.create("alpha")
	b.saved = "beta"
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: reassign-opaque-managed-field-in-loop -->
Reassigning a managed opaque `T` field REPEATEDLY (a loop of `b.replace(...)`) drops each previous value
before storing the next: every iteration frees the old String exactly once and stores a fresh one, and the
final value drops at the container's own scope exit — a balanced drop-per-store, leak-free under `__mm_free`
poisoning.
```maxon
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias StringBox = Box with String
function main() returns ExitCode
	var b = StringBox.create("start")
	var i = 0
	while i < 3 'loop'
		b.replace("iter")
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: reassign-opaque-field-struct-element -->
The opaque `T` need not be a String: a `Box with Holder` whose `Holder` owns a `String` field drops the old
struct through the descriptor's `destroyFunc@40` (`__destruct_<Holder>`, which frees the Holder's String),
then moves the new struct in. Both the shared-body reassign (`self.saved = next`) and the container's own
scope-exit drop free each Holder exactly once — leak-free under `__mm_free` poisoning.
```maxon
type Holder
	export var text as String
	export static function create(t String) returns Self
		return Self{ text: t }
	end 'create'
end 'Holder'
type Box uses Element
	export var saved as Element
	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'
	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'
typealias HolderBox = Box with Holder
function main() returns ExitCode
	var b = HolderBox.create(Holder.create("alpha"))
	b.replace(Holder.create("beta"))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: error.instance-arg-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias OtherBox = Box with Other
function takes(_ LeafBox) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	return takes(OtherBox.create(Other.make("x")))
end 'main'
```
```maxoncstderr
error E3005: <fragment>:26:9: argument type mismatch for '_': expected 'LeafBox', got 'OtherBox'
```

### ⚠ `Box_Leaf` BELOW IS THE FALLBACK, NOT AN UNCONVERTED DOOR — and the `typealias` line is the tell

A diagnostic names a type by the `typealias` the author wrote (user ruling, 2026-08-04), and every case in
this file that HAS such a line reads it back: `expected 'LeafBox'` above, `Cannot return 'OtherBox' …` below.
The three cases that still print a mint are the three whose type has **no declaration to quote** —
`typealias LeafBoxBox = Box with (Box with Leaf)` declares `LeafBoxBox`, and the `Box with Leaf` inside its
argument list is interned without any name of its own. There is nothing else the message could say, so the
canonical mint is the answer rather than the absence of one (`ProgramSignatures.instanceDisplayName`).

The discriminator is mechanical: add `typealias LeafBox = Box with Leaf` to one of these programs and the
`expected` side becomes `LeafBox`, measured. **A mint here beside a declaration that names the same type
would be a bug; a mint here with no such declaration is the rule working.**

<!-- test: error.type-parameter-arg-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias OtherBox = Box with Other
typealias LeafBoxBox = Box with (Box with Leaf)
function main() returns ExitCode
	let bad = LeafBoxBox.create(OtherBox.create(Other.make("x")))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:23: argument type mismatch for 'v': expected 'Box_Leaf', got 'OtherBox'
```

<!-- test: error.type-parameter-arg-wrong-instance-bound -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias OtherBox = Box with Other
typealias LeafBoxBox = Box with (Box with Leaf)
function main() returns ExitCode
	let wrong = OtherBox.create(Other.make("x"))
	let bad = LeafBoxBox.create(wrong)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:23: argument type mismatch for 'v': expected 'Box_Leaf', got 'OtherBox'
```

<!-- test: error.method-arg-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias OtherBox = Box with Other
type Holder
	export var b as LeafBox
	export static function create(b LeafBox) returns Self
		return Self{b: b}
	end 'create'
	export function replace(_ LeafBox) returns ExitCode
		return 0
	end 'replace'
end 'Holder'
function main() returns ExitCode
	let h = Holder.create(LeafBox.create(Leaf.make("a")))
	return h.replace(OtherBox.create(Other.make("x")))
end 'main'
```
```maxoncstderr
error E3005: <fragment>:33:11: argument type mismatch for '_': expected 'LeafBox', got 'OtherBox'
```

<!-- test: error.return-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias OtherBox = Box with Other
function wrong() returns LeafBox
	return OtherBox.create(Other.make("x"))
end 'wrong'
function main() returns ExitCode
	let v = wrong()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:2: Cannot return 'OtherBox' from function declared to return 'LeafBox'
```

<!-- test: error.reassign-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias OtherBox = Box with Other
function main() returns ExitCode
	var b = LeafBox.create(Leaf.make("a"))
	b = OtherBox.create(Other.make("x"))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:2: cannot assign a value of type 'OtherBox' to variable 'b', which holds 'LeafBox'
```

<!-- test: error.struct-field-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Other
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias OtherBox = Box with Other
type Holder
	export var b as LeafBox
	export static function create(v OtherBox) returns Self
		return Self{b: v}
	end 'create'
end 'Holder'
function main() returns ExitCode
	let h = Holder.create(OtherBox.create(Other.make("x")))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:15: cannot assign a value of type 'OtherBox' to field 'b' of 'Holder', which holds 'LeafBox'
```

<!-- test: error.plain-struct-into-instance-arg -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
function takes(_ LeafBox) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	return takes(Leaf.make("x"))
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:9: argument type mismatch for '_': expected 'LeafBox', got 'Leaf'
```

<!-- test: error.nested-pair-arg-wrong-instance -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
type Pair uses A, B
	export var first as A
	export var second as B
	export static function create(first A, second B) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'
typealias LeafBox = Box with Leaf
typealias WrongPair = Pair with (Box with Leaf, Leaf)
typealias Deep = Box with (Pair with (Box with Leaf, Box with (Box with Leaf)))
function main() returns ExitCode
	let d = Deep.create(WrongPair.create(LeafBox.create(Leaf.make("q")), second: Leaf.make("r")))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:15: argument type mismatch for 'v': expected 'Pair_Box_Leaf_Box_Box_Leaf', got 'WrongPair'
```

<!-- test: two-aliases-one-instance-agree -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias LeafBoxAgain = Box with Leaf
function takes(_ LeafBox) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	return takes(LeafBoxAgain.create(Leaf.make("a")))
end 'main'
```
```exitcode
0
```

<!-- test: nested-instance-arg-matching-instance-agrees -->
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
typealias LeafBoxBox = Box with (Box with Leaf)
function main() returns ExitCode
	let inner = LeafBox.create(Leaf.make("a"))
	let outer = LeafBoxBox.create(inner)
	print("{outer.value.value.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a

```

<!-- test: alias-named-type-argument-agrees -->
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with N0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x

```

<!-- test: alias-named-type-argument-forward-declared-agrees -->
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N1 = Box with N0
typealias N0 = Box with S0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x

```

<!-- test: alias-named-type-argument-cross-file-types-first -->
```maxon
// --- file: a_types.maxon
export type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
export typealias N0 = Box with S0
// --- file: b_use.maxon
typealias N1 = Box with N0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x

```

<!-- test: alias-named-type-argument-cross-file-use-first -->
```maxon
// --- file: a_use.maxon
typealias N1 = Box with N0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	return 0
end 'main'
// --- file: b_types.maxon
export type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
export type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
export typealias N0 = Box with S0
```
```exitcode
0
```
```stdout
x

```

<!-- test: both-spellings-agree-at-the-comparison -->
`Box with N0` and `Box with (Box with S0)` are the SAME type spelled two ways. They intern as two
instances with two compiled names, so every site that decides type IDENTITY must canonicalize — and
must agree with every other site. This exercises all four: the type-ARGUMENT read, a call ARGUMENT,
a `var` REASSIGNMENT, and a `return`.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
typealias ViaAlias = Box with N0
typealias ViaInline = Box with (Box with S0)
function takes(_ ViaAlias) returns ExitCode
	return 0
end 'takes'
function makeIt() returns ViaAlias
	let v0 = N0.create(S0.make("r"))
	let r = ViaInline.create(v0)
	return r
end 'makeIt'
function main() returns ExitCode
	let a = ViaAlias.create(N0.create(S0.make("a")))
	print("{a.value.value.s}")
	let b = ViaInline.create(N0.create(S0.make("b")))
	print("{b.value.value.s}")
	var swap = ViaAlias.create(N0.create(S0.make("c")))
	swap = ViaInline.create(N0.create(S0.make("d")))
	print("{swap.value.value.s}")
	let q = makeIt()
	print("{q.value.value.s}")
	return takes(ViaInline.create(N0.create(S0.make("e"))))
end 'main'
```
```exitcode
0
```
```stdout
abdr

```

<!-- test: both-spellings-agree-at-a-nested-type-argument -->
The same agreement one level DEEPER, where the instance's own type argument is a nested instance
spelled through an alias. This is a separate identity read from the one above — the type-ARGUMENT
extractor rather than the value extractor — and the two diverge at depth 2 if only one canonicalizes.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with N0
typealias DeepAlias = Box with (Box with N0)
typealias DeepInline = Box with (Box with (Box with S0))
function takes(_ DeepAlias) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	let d = DeepAlias.create(N1.create(N0.create(S0.make("x"))))
	print("{d.value.value.value.s}")
	let e = DeepInline.create(N1.create(N0.create(S0.make("y"))))
	print("{e.value.value.value.s}")
	return takes(DeepInline.create(N1.create(N0.create(S0.make("z")))))
end 'main'
```
```exitcode
0
```
```stdout
xy

```

<!-- test: alias-named-type-argument-owning-heap-is-not-co-owned-trivial -->
⭐⭐ **A `Box with N0` whose `N0` owns a String is an OWNING argument, and the CONSUME boundary has to
say so as loudly as the DROP boundary already did (A3k).** `typeArgIsOwned` looked the alias name up in
`structTypes` and `enumTypes`, which a generic-instance typealias is in neither of, and answered "not
owned" — while its twin `typeIsManaged` has always ended in `isGenericAlias(name)` and answered
"managed". The pair is read as `typeArgIsCoOwnedTrivial = typeIsManaged and not typeArgIsOwned`, so the
box classified as CO-OWNED TRIVIAL: every construction paid an `__mm_incref` plus a destructor call
where a move was owed, and this program — a shared-body reassign of `Box.value`, which is refused
whenever ANY instantiation of `Box` is co-owned trivial — was refused **E2015** "a trivial-struct
instantiation co-owns the field", said of a box that owns a String. Order-INDEPENDENTLY, unlike the
tuple-alias half A3e's review closed.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function swap(v T)
		self.value = v
	end 'swap'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with N0
function main() returns ExitCode
	var v0 = N0.create(S0.make("x"))
	var v1 = N1.create(v0)
	v1.swap(N0.create(S0.make("y")))
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: alias-named-type-argument-reassigned-in-a-loop-is-leak-free -->
The same reassignment 200 times over, so the balance is a COUNT and not a coincidence: each round moves
a fresh `N0` into the box and drops the one it replaces. A retain that no longer has a matching decref
— or a decref for a reference the move never took — is a leak or a double free rather than an exit 0.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function swap(v T)
		self.value = v
	end 'swap'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with N0
function main() returns ExitCode
	var v1 = N1.create(N0.create(S0.make("start")))
	for _ in 0 upto 200 'rounds'
		v1.swap(N0.create(S0.make("round")))
	end 'rounds'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: alias-named-type-argument-is-consumed-and-co-owned -->
⭐ **The narrowing that comes with it, and the half that proves the CONSUME is real.** An owning argument
is CONSUMED into the box, which since the durable-sink ruling (⚖ 2026-08-12) means the box takes its OWN
reference rather than stealing the caller's — so `v0` stays readable and releases its reference at scope
exit. What the rung fixed is still pinned here: the alias spelling and the inline spelling must agree
about the argument being OWNING at all, and they now do. (Both spellings were E3102 at `v0.value` while a
consume was a MOVE; the classification they pin is unchanged, only its refcount consequence is.)
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with N0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	let again = v0.value
	print("{again.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xx

```

<!-- test: inline-nested-type-argument-is-consumed-and-co-owned -->
The inline spelling of the case above, accepted identically. It is the CONTROL that makes the pair a
statement about agreement rather than about one spelling: its argument arrives tagged
`genericInstance`, which `typeArgIsOwned` has always classified through the instance, so this half was
already correct and did not move.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
typealias N1 = Box with (Box with S0)
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
	print("{v1.value.value.s}")
	let again = v0.value
	print("{again.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xx

```

<!-- test: error.bare-generic-constructor-unbound-t -->
⭐ **A generic type's constructor called on the BASE rather than on an instance alias is REFUSED, and
that closes a LEAK.** `Box.create(…)` binds no type argument, so `T` is never bound: there is no
concrete instance, therefore no layout descriptor, therefore no synthesized `__destruct_<instance>` —
and the value it builds is never dropped. Before this rung the program compiled and **exited 101**
(both this form and the `let a = Box.create(…)` / `takes(a)` form). The reference compiler agrees the
construct is ill-formed, and says why more directly: it resolves `a.value` as bare `int`
(`E4006: Primitive type 'int' has no method named 's'`) — `T` unbound.

The diagnostic it lands on is the ordinary argument-identity mismatch, `got 'Box'` being the base
rather than an instance. That is defensible and is recorded here as the code this construct gets; it
is deliberately NOT a code of its own.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
function takes(_ N0) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	return takes(Box.create(S0.make("x")))
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:9: argument type mismatch for '_': expected 'N0', got 'Box'
```

<!-- test: error.bare-generic-constructor-unbound-t-bound-first -->
The same unbound-`T` construct reached through a binding rather than inline — it leaked identically
(exit 101) and is refused identically, so the rejection does not depend on the argument being a
temporary.
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias N0 = Box with S0
function takes(_ N0) returns ExitCode
	return 0
end 'takes'
function main() returns ExitCode
	let a = Box.create(S0.make("x"))
	return takes(a)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:9: argument type mismatch for '_': expected 'N0', got 'Box'
```

<!-- test: per-instance-alias-decays-at-a-type-parameter-argument -->
⭐ **A PER-INSTANCE alias argument DECAYS at a bare `T` parameter, exactly as it does everywhere else.**
`WA.Idx` is a nominal identity only against another per-instance alias (P1.6-C); met by a target that is
not one — here `T` bound to the ranged alias `Integer`, whose type-argument identity is deliberately
empty — it carries no claim and decays to its underlying scalar.

The type-parameter check must therefore ask the SAME door the parser's coercion sites ask
(`aggregatesConflict`, which owns that decay) and not the bare rule beneath it. Asked the bare way it read
the decaying argument as "an aggregate meeting a scalar" and REJECTED this program, while
`takesPlain(t)` — the same value into the same `Integer` — was accepted one site over. Both forms are
pinned here so the two can never again answer differently.
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapper uses T
	export typealias Idx = int(0 to u64.max)

	export var value as T
	export var tag as Idx

	export static function create(value T, tag Idx) returns Self
		return Self{value: value, tag: tag}
	end 'create'

	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'

type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'

typealias WA = Wrapper with Integer
typealias IntBox = Box with Integer

function takesPlain(_ Integer) returns ExitCode
	return 0
end 'takesPlain'

function main() returns ExitCode
	let a = WA.create(1, tag: 5)
	let t = a.getTag()
	let b = IntBox.create(t)
	print("{b.value}")
	return takesPlain(a.getTag())
end 'main'
```
```exitcode
0
```
```stdout
5

```

<!-- test: error.self-cycle-alias -->
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias A = Box with A
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:14:24: Circular typealias dependency: A
```

<!-- test: error.mutual-cycle-alias -->
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias A = Box with B
typealias B = Box with A
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:14:11: Circular typealias dependency: A -> B -> A
```

<!-- test: error.self-cycle-alias-used -->
```maxon
type S0
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'S0'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias A = Box with A
function main() returns ExitCode
	let x = A.create(S0.make("q"))
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:14:24: Circular typealias dependency: A
```

<!-- test: error.field-access-on-builtin-array-base -->
⭐⭐ **THIS CASE'S SUBJECT SURVIVED THE LISTING; ITS REASON DID NOT, AND THE NEW ANSWER IS
STRICTLY BETTER.** It used to read: *"`Array` and `Set` are BUILTIN generic bases: shv2
synthesizes their runtime records rather than compiling `stdlib/Array.maxon`, so no
`type` declaration carries a field table and no field of an instance is reachable. The
field is missing from the COMPILER, not from the language — so this reports a
not-implemented-yet construct, never an unknown field."* Every clause of that is now
false for `Array`: `stdlib/Array.maxon` is LISTED, it IS compiled, and its `type Array`
declaration carries a field table with exactly one entry in it — `managed`.

⇒ The answer is no longer `E2015 Unsupported` but `E3018 type 'Array' has no field named
'value'`, which is what the program's mistake actually is. Three things improve at once:
the diagnostic stops apologising for the compiler, it points at the FIELD token (`:8:13`)
rather than at the receiver (`:8:9`), and it is the same sentence any user `type` gets for
the same mistake instead of a container-specific copy of one. The oracle's own answer to
this program is `E4006 Type 'IntArray' has no field named 'value'` — the same fact under
that compiler's numbering.

⚠ **`Set` IS STILL SYNTHESIZED AND STILL TAKES THE OLD DOOR**, so the sentence above is
about `Array` alone; the E2015 arm is live and is not dead code. Its own coverage moves the
day `stdlib/Set.maxon` is listed.

⚠ The field named here is still deliberately NOT `managed`: that spelling is served —
in both the chained (`arr.managed.setLength(2)`) and the value (`f(arr.managed)`) forms —
because it is routed to the array dispatcher ahead of the field machinery
(`arrayManagedFieldAt`, BATCH2 slice 6). It is now ALSO the one field the corpus
declaration genuinely has, so the two doors agree on the roster for the first time; `value`
is refused by both.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	return arr.value
end 'main'
```
```maxoncstderr
error E3018: <fragment>:8:13: type 'Array' has no field named 'value'
```

<!-- test: error.field-access-on-undeclared-generic-base -->
The other way a generic base has no `StructLayout`: nothing declares it. A parameter
typed by such an alias reaches the same field-access door, and must say what is
actually wrong — the `with` — rather than crash. (`checkGenericInstance` records E2055
against that `with`, but a thrown parse error discards this file's artifact
diagnostics, so the message carries the cause itself.)
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntThing = Nonexistent with Int

function readIt(t IntThing) returns Int
	return t.foo
end 'readIt'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:9: Unsupported: a field access on 't': its type instantiates 'Nonexistent', which no file declares as a generic `type`, so it has no fields — the `with` that names it is the error
```

<!-- test: error.undeclared-generic-base-reaches-no-layout-query -->
An instance is interned for whatever base a `with` names, declared or not, so the
whole-program instance walks (`noteDestructorUsage`'s managed-opaque-element rooting
among them) meet an undeclared base with no fields to read. They must answer "no
fields" and let the E2055 already recorded at the `with` be the verdict — this
program used to die inside `ProgramSignatures.baseLayoutOf` before any diagnostic
was printed. ⚠ This case and its `sizeof` sibling below used to spell the undeclared
base `Map`, on the stated ground that it was "a real stdlib generic that shv2 has not
built" — **a premise the compiler falsified** when `Map` became a builtin generic. A
base that is undeclared only until someone builds it dates the test to the day it was
written; `Nonexistent` is undeclared by construction, and is the same spelling the
field-access case above already uses.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntMap = Nonexistent with (Int, Int)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2055: <fragment>:3:20: Type 'Nonexistent' has no associated types
```

<!-- test: error.field-write-on-builtin-base-a-declaration-also-claims -->
The third way a generic instance's base has no layout OF ITS OWN, and the one that is not
about a layout being ABSENT: a declaration claims a BUILTIN base name. `type Array uses T`
registers a field table, but every door that decides what the VALUE is — `create`, the
method router, the box size, the drop callee — is routed to the synthesized runtime record
by `isArrayInstance`, so the declaration's field offsets address that record. The field
access must ask the same walk gate the destructor walks do, not `structOf`, which answers
the different question "is a type of this NAME declared". Before it did, this program
compiled with no diagnostic at all: the write landed on the array record's element buffer
POINTER and the teardown took an access violation (0xC0000005). Reading was as bad and
quieter — `a.value` handed the raw heap address back as an Integer.
```maxon
typealias Int = int(i64.min to i64.max)

type Array uses T
	export var value as T
end 'Array'

typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.value = 4242
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:2: Unsupported: a field access on 'a': `Array` is a BUILTIN whose runtime record shv2 SYNTHESIZES rather than compiling a declaration for, so a value of it carries that record's own words and not the fields `stdlib/Array.maxon` declares. The field is missing from this compiler, not from the language; reach the contents through the methods
```

<!-- test: error.field-read-on-the-second-builtin-base-a-declaration-also-claims -->
The SECOND-NAME twin, and the READ direction — the two builtins are one rule, so a fix that
reached only the one it was measured on would leave the other silently handing out its
record. This one used to hand the record's own words back as if they were the declared
field.

⚠ **THE SUBJECT HAS BEEN FALSIFIED TWICE BY THE RETIREMENT CHAIN, WHICH IS WHY THE CASE NAME
NO LONGER CARRIES A CONTAINER.** It was `Set` until `W90` listed `stdlib/Set.maxon`, and
`List` until `W153` listed `stdlib/List.maxon`: a listed module makes a user `type Set` /
`type List` CONTEST a declared type rather than a synthesized record, so it takes A1s
wave 2's "a declaration wins" road instead of this gate. **Measured at `W153` on the same
binary, the two answer identically** — `a member access 'add'/'append' on a 'unknown' value`
— which is that other road, not this one.

⇒ **`Vector` is the subject, and `W189`/`W190` did NOT take it away — which is worth stating,
because the paragraph that stood here predicted they would.** It read *"`stdlib/Vector.maxon`
exists and is NOT whitelisted … when `Vector` is listed in its turn, this case moves to whatever
base is still single-regime or it goes"*. The module IS listed now and every member of a `Vector`
is its declaration's, and this case is UNMOVED — because what it turns on is not who serves the
members but who owns the RECORD, and that is still shv2 (`isBuiltinGenericBaseName`, which decides
what `Vector with 3 Int` MEANS; taking the name off it is `W114`'s rung). The refusal's own sentence
is re-derived to say exactly that and no longer claims this compiler reads no stdlib, which had
stopped being true for both bases. The `Array` write-direction twin directly above does not cover
this direction, and `Array` would not carry the second-name half of the question at all — the
roster's other members (`__ManagedList`, `__ManagedListNode`, `__ManagedMemoryCursor`) cannot
serve, because `E2051` refuses a declaration whose name starts with `__` before this gate is ever
reached.
```maxon
typealias Int = int(i64.min to i64.max)

type Vector uses T
	export var value as T
end 'Vector'

typealias IntVec = Vector with 3 Int

function main() returns ExitCode
	var v = IntVec.create()
	return v.value
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:9: Unsupported: a field access on 'v': `Vector` is a BUILTIN whose runtime record shv2 SYNTHESIZES rather than compiling a declaration for, so a value of it carries that record's own words and not the fields `stdlib/Vector.maxon` declares. The field is missing from this compiler, not from the language; reach the contents through the methods
```

<!-- test: error.sizeof-of-undeclared-generic-base -->
The other query that assumes an instance's base exists, and it is NOT the field walks'
gate: `genericInstanceBoxSize` answers for a BUILTIN (the fixed 48-byte record) as happily
as for a declared base, and has nothing to say only when the base is neither. `sizeof`
folds at PARSE time, while the E2055 `checkGenericInstance` recorded against this `with` is
drained whole-program afterwards — so this used to die inside `ProgramSignatures.baseLayoutOf`'s
sibling with a stack trace and the real diagnostic never printed. `sizeof(Array with Int)`
still folds to 48; only an unknown base is refused. (`Nonexistent` rather than `Map` for
the reason the case above records.)
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntMap = Nonexistent with (Int, Int)

function main() returns ExitCode
	return sizeof(IntMap) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:9: Unsupported: sizeof of a type that instantiates 'Nonexistent', which no file declares as a generic `type`, so it has no box to size — the `with` that names it is the error
```

<!-- test: error.bare-int-type-arg -->
⭐ **A BARE `int` IS NOT A TYPE ARGUMENT (E2061).** Everywhere else in the language a numeric
domain has to be DECLARED — that is the whole of the ranged-typealias rule — and a `with` clause
was the one type position that let the keyword through. It was silently ACCEPTED: `parseTypeReference`
mints a bare keyword as a CONCRETE tag, so it never entered the name cascade that validates every
other spelling, and the check that guards that cascade returned before seeing it. The oracle refuses
it (`RejectBarePrimitiveTypeArgs`), and the fix the message names is the declaration the rule wanted
all along.
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with int
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2061: <fragment>:8:29: Cannot use bare type 'int' as a type argument; use a ranged typealias instead (e.g. typealias MyType = int(...))
```

<!-- test: error.float-type-arg -->
⭐ **A FLOAT TYPE ARGUMENT IS REFUSED HOWEVER IT IS SPELLED (E2062), AND THAT IS A DELIBERATE
DIVERGENCE FROM BOTH REFERENCE COMPILERS.** They accept it because they MONOMORPHIZE — the
instantiation gets a genuine f64 slot and the question never arises. shv2 DICTIONARY-PASSES: a type
parameter is one opaque 8-byte GENERAL-PURPOSE slot, so a float value, which is born in a
floating-point register, has no way to travel through it. Reaching the backend it PANICKED —
*"a register-to-register move from xmm0 to rcx crosses register files"* — with no `where` constraint,
no comparison and no method call needed; the instantiation and one call are the whole reproducer.
This is a compiler limitation, not a language rule, so the message names no workaround: at this
milestone there is none.
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias FloatBox = Box with float
function main() returns ExitCode
	let b = FloatBox.create(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:8:31: Cannot use 'float' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
```

<!-- test: error.float-alias-type-arg -->
⭐ **THE CASE THAT CLOSES THE SECOND SPELLING, AND THE REASON E2062 IS ASKED BEFORE E2061.**
A ranged FLOAT typealias reaches the identical backend assertion — the alias is not the problem, the
float is — so refusing only the bare keyword would have left the crash reachable through the very
declaration E2061 recommends. Which is also why a bare `float`, which satisfies BOTH rules, is
claimed by this one: "use a ranged typealias instead" is true for `int` and a trap for `float`, and a
compiler must not route a reader into a panic with its own diagnostic. The message names the ALIAS
the source wrote, not `float`, so a reader of the line `Box with Real` is told about `Real`.

⚠ **IT CLOSES THE TYPE-ARGUMENT DOOR, NOT THE VALUE DOOR — and the difference is two lines of
source.** Every spelling of a float TYPE ARGUMENT is refused above, but a float VALUE handed to a
`T`-typed formal reached `X64Backend.emitRegRegMove` with no float in the type arguments at all, and
PANICKED the compiler: `typealias SBox = Box with String` then `SBox.create(1.5)`, and the same call
on a `Box with Integer`. That door is the OPPOSITE side of one thesis E2062's own message states — a
float cannot travel through a type parameter's general-purpose slot — but it is a different
mechanism (`tagIsIntegral` answers `false` for `typeParameter` precisely so that a float into a `T`
does NOT read as a lossy conversion, which is right INSIDE a generic body), so this rule strictly
shrank its reach rather than closing it.

⭐ **IT IS CLOSED NOW, AND THE RULING IS THAT A GENERIC CALL SITE SUBSTITUTES ITS INSTANCE'S TYPE
ARGUMENTS BEFORE THE ACTUAL-VS-FORMAL CHECK.** `IntBox.create(1.5)` is therefore the ordinary E3009
the non-generic `takeInt(1.5)` already reports, and `StrBox.create(1.5)` the ordinary E3005 —
`error.float-actual-at-opaque-formal` and `error.float-actual-at-opaque-formal-string` below pin
both, byte-for-byte against the non-generic wording, and `int-actual-at-opaque-formal-still-works`
pins that an ordinary generic call is untouched. ONE RULE, BOTH PATHS. The rejected alternative was
refusing a float actual at an opaque formal outright: it decides from the FORMAL's shape rather than
from the instance's real type, and answers wrongly the moment a float type argument becomes legal —
which E2062's own message says is temporary. The defect is described here rather than deleted
because the sentence that used to stand in this place said the panic was unreachable, and a reader
who believed it would never look for the value door at all.
```maxon
typealias Real = float(f64.min to f64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias FloatBox = Box with Real
function main() returns ExitCode
	let b = FloatBox.create(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:9:31: Cannot use 'Real' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
```

<!-- test: error.float-type-arg-builtin-generic -->
Both float spellings again on a BUILTIN base: the rule is about the ARGUMENT, so a synthesized base
reaches it through the same `parseGenericArgNode` a declared generic does. Only the FIRST offending
argument in a file is shown here because each alias is its own declaration — two declarations, two
diagnostics.

`Set` and `List` are the bases still under the rule, and `Array` and `Vector` are the two that are not
(A4d / the `vector` port): a buffer STRIDE is written and read at the element's own type, where a
dictionary-passed type parameter is one opaque general-purpose slot. `Set` hashes its key and `List`
boxes its payload, and each refuses a float in its own terms at its own door — so the refusal that
belongs at the DECLARATION is still theirs.
```maxon
typealias Real = float(f64.min to f64.max)
typealias RealSet = Set with Real
typealias FloatList = List with float
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:3:30: Cannot use 'Real' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
error E2062: <fragment>:4:33: Cannot use 'float' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
```

<!-- test: a-float-element-is-admitted-on-a-buffer-base -->
### The exemption, stated as an acceptance
The refusal above is about a dictionary-passed type-parameter slot, so it lifts exactly where there is
no such slot. Both buffer bases take both float spellings, and this is the case that turns RED if the
exemption is ever narrowed back to `Vector` alone.
```maxon
typealias Real = float(f64.min to f64.max)
typealias Reals = Array with Real
typealias Vec2 = Vector with 2 Real

function main() returns ExitCode
	var a = Reals.create()
	a.push(1.5)
	var v = Vec2.create()
	try v.set(0, value: 3.5) otherwise panic("test invariant: set OOB")
	return trunc((try a.get(0) otherwise 0.0) + (try v.get(0) otherwise 0.0))
end 'main'
```
```exitcode
5
```

<!-- test: error.bare-float-type-arg-on-a-buffer-base -->
### A BARE `float` on a buffer base is E2061, and only there is its advice true
The rules overlap on the bare keyword, and the order between them decides which sentence a reader
gets. Off a buffer base E2062 claims it, because *"declare a ranged typealias"* would send the reader
into the very refusal they are already in. ON one the exemption lifts E2062, E2061 takes the keyword
back, and its advice is now a working program — `error.float-type-arg-builtin-generic`'s `Array` half
became this case.
```maxon
typealias FloatArray = Array with float
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2061: <fragment>:2:35: Cannot use bare type 'float' as a type argument; use a ranged typealias instead (e.g. typealias MyType = float(...))
```

<!-- test: error.bare-type-arg-builtin-generic -->
The rule is about the ARGUMENT, so it does not care what the base is: a BUILTIN generic (`Array`,
whose runtime record shv2 synthesizes rather than declaring) reaches the identical check, because
every `Base with Args` — builtin base or declared base — parses its arguments through the one
`parseGenericArgNode`. `Array with Int` over a declared `typealias Int = int(...)` is the spelling
every array spec in this suite already uses.
```maxon
typealias IntArray = Array with int
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2061: <fragment>:2:33: Cannot use bare type 'int' as a type argument; use a ranged typealias instead (e.g. typealias MyType = int(...))
```

<!-- test: error.bare-type-arg-nested -->
A NESTED argument is checked by the same call, which is what makes one edit cover every reachable
position: `parseGenericArgNode` recurses into an inner `with` and every LEAF it reaches — at any
depth — goes through `checkGenericArgType`. The diagnostic points at the offending `int` rather
than at the alias name, so a reader of a deeply nested instantiation is told WHICH argument is
wrong and not merely that one of them is.
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias BadNest = Box with (Box with int)
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2061: <fragment>:8:40: Cannot use bare type 'int' as a type argument; use a ranged typealias instead (e.g. typealias MyType = int(...))
```

<!-- test: bool-type-arg-admitted -->
⭐ **`bool` IS DELIBERATELY NOT A BARE PRIMITIVE**, and this is the case that keeps the rule narrow.
There is no range to declare for it — it is already a constrained type, its domain is its two
values — so demanding a `typealias` over it would demand a declaration the grammar cannot even
spell. The oracle excludes it for exactly this reason (`MlirType.IsBarePrimitive` names only the
numerics). It must stay admitted through BOTH kinds of base: a declared generic `type`, and the
builtin `Array`.
```maxon
type Sizer uses T
	export var v as T
	export static function create(x T) returns Self
		return Self{v: x}
	end 'create'
	export function get() returns T
		return self.v
	end 'get'
end 'Sizer'
typealias BoolSizer = Sizer with bool
typealias BoolArray = Array with bool
function main() returns ExitCode
	let s = BoolSizer.create(true)
	var a = BoolArray.create()
	a.push(s.get())
	return a.count() - 1
end 'main'
```
```exitcode
0
```

<!-- test: string-and-user-type-args-admitted -->
The other admitted shapes, and the reason the rule tests the TAG rather than the token: a `String`,
a user `type` and a NESTED instance each reach `checkGenericArgType` with a concrete tag that is not
`integer` or `float`, so the bare-primitive arm passes them straight to the name cascade that was
always there. These are the 900-odd `with String` / `with <UserType>` arguments the rest of the
suite is built on; the rule may not touch one of them.
```maxon
type Leaf
	export var s as String
	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StringBox = Box with String
typealias LeafBox = Box with Leaf
typealias LeafBoxBox = Box with (Box with Leaf)
function main() returns ExitCode
	let sb = StringBox.create("hello")
	print("{sb.value}")
	let lb = LeafBox.create(Leaf.make("a"))
	print("{lb.value.s}")
	let nested = LeafBoxBox.create(LeafBox.create(Leaf.make("b")))
	print("{nested.value.value.s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
helloab

```

<!-- test: ranged-alias-type-arg-admitted -->
The workaround the E2061 message NAMES, demonstrated: a ranged `typealias` over `int` is an
ordinary `named` type argument, and it works — so the diagnostic tells the reader something true
and one edit away. It reaches the check as `named`, falls past the bare-primitive arm, and is
resolved by the same `denotedNamedType` cascade every other name goes through.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias IntBox = Box with Integer
typealias IntArray = Array with Integer
function main() returns ExitCode
	let b = IntBox.create(42)
	var a = IntArray.create()
	a.push(b.get())
	return (try a.get(0) otherwise 0) - 42
end 'main'
```
```exitcode
0
```

<!-- test: error.float-actual-at-opaque-formal -->
A `float` actual meeting a `T`-typed formal is refused by the type the INSTANCE substitutes for `T`,
not by the formal's own spelling. `IntBox`'s `T` is `Integer`, so this is the identical E3009 the
non-generic `takeInt(1.5)` gives -- one rule, both paths. Before the substitution existed the front
end had nothing to check against and the float reached the x64 emitter, which died on a
register-to-register move across register files (`X64Backend.maxon:751`) -- a compiler panic on a
program whose only defect was an ordinary lossy conversion.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	let b = IntBox.create(1.5)
	return b.value
end 'main'
```
```maxoncstderr
error E3009: <fragment>:11:17: argument 'v': cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.float-actual-at-opaque-formal-string -->
The same rule where the substituted type is not numeric at all: `StrBox`'s `T` is `String`, so a
float actual is a plain type mismatch (E3005), matching the non-generic `takeStr(1.5)`. This case
exists because the panic was NOT specific to `Integer` -- it reproduced identically here, so a fix
that only taught the numeric arm would leave half the defect live.
```maxon
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	let b = StrBox.create(1.5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:17: argument type mismatch for 'v': expected 'String', got 'float'
```

<!-- test: int-actual-at-opaque-formal-still-works -->
The regression guard the two refusals need: an `int` actual at a `T`-typed formal whose instance
substitutes an integer type is ordinary, legal, and must stay that way. A substitution rule that
refuses this would break every generic call in the corpus.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	let b = IntBox.create(7)
	return b.value
end 'main'
```
```exitcode
7
```

<!-- test: error.per-instance-alias-actual-at-opaque-formal -->
The OTHER value door out of the same check, and the one the two float tests above do not reach: a
per-instance alias actual (`IntWrapper.Idx` — a nominal wrapper over a plain `int`) meeting a
`T`-typed formal whose instance substitutes a STRUCT. Both halves of the argument rule used to
decline to judge it. The nominal half abstained because `aggregatesConflict` decayed any
per-instance `got` against any target that was not itself per-instance, `Leaf` included; the scalar
half never ran, because it runs only where the type argument carries no nominal identity and `Leaf`
carries one. So an `int` was stored into the box's `T` field and freed by `__destruct_Box_Leaf` as a
`Leaf`: a wild free, exit `0xC0000005`, from a program that compiled clean — the same failure mode
the type-parameter argument check was opened on.

The fix is in the decay rule itself, not in a new arm here: a per-instance alias decays only where
the target carries NO nominal identity of its own, because there is nothing for it to decay into
otherwise. The decay's old safety argument leaned on "a tag check runs before every one of these
sites", which is a second fact held at six call sites — and false at this one, where the tag check
compares `substitutedInstanceArg`'s AS-INTERNED `named` leaf against a `named` value and agrees.
`per-instance-alias-decays-at-a-type-parameter-argument` above pins the decay that must survive.
```maxon
typealias Integer = int(i64.min to i64.max)
type Leaf
	export var name as String
end 'Leaf'
type Wrapper uses T
	export typealias Idx = int(0 to u64.max)
	export var value as T
	export var tag as Idx
	export static function create(v T, tag Idx) returns Self
		return Self{value: v, tag: tag}
	end 'create'
	export function getTag() returns Idx
		return self.tag
	end 'getTag'
end 'Wrapper'
type Box uses T
	export var item as T
	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'
typealias IntWrapper = Wrapper with Integer
typealias LeafBox = Box with Leaf
function main() returns ExitCode
	let w = IntWrapper.create(1, tag: 5)
	let b = LeafBox.create(w.getTag())
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:27:18: argument type mismatch for 'v': expected 'Leaf', got 'IntWrapper.Idx'
```

<!-- test: opaque-field-read-of-a-struct-argument-is-that-struct -->
⭐ **A `T`-TYPED FIELD READ FROM OUTSIDE THE GENERIC BODY IS THE INSTANCE'S ARGUMENT, AND FOR A
STRUCT ARGUMENT IT WAS AN INTEGER (A4i).** The retype-of-`T` at a concrete field read has been here
since P1.6-B2, but it handed the value the instance's argument exactly as the registry stored it —
and a struct argument is stored as a bare `named`, which reads as an INTEGER everywhere. So
`pb.value` on a `Box with Point` bound a value the front end typed `int` and the machine typed a
pointer. `generic-trivial-struct-arg` above builds the same box and never reads its field, which is
why the suite could be green over it.
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let pb = PointBox.create(Point.create(7))
	let q = pb.value
	print("{q.x}\n")
	return q.x - 7
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: error.opaque-field-read-of-a-struct-argument-is-not-an-integer -->
⚠ **THE SAME READ, AND THE WORST OF ITS FACES: ARITHMETIC WAS ACCEPTED AGAINST A POINTER.** Typed
`int`, `q + 0` compiled and PRINTED THE RAW HEAP ADDRESS — a silent wrong answer where the two
refusing faces at least stopped. The correct verdict is the one a plain struct already gets from
this compiler and from the bootstrap alike, at the same code with the same words: a struct is not
an operand of `+`.
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let pb = PointBox.create(Point.create(7))
	let q = pb.value
	print("{q + 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:19:12: Cannot operate on struct and int
```

<!-- test: opaque-field-read-of-a-generic-alias-argument-is-that-instance -->
The third face, and the one that proves the cause is the ARGUMENT's storage and not struct-hood: a
GENERIC-ALIAS argument (`Holder with IntBox`) is stored as a bare `named` too, so the read used to
report `E3011 Unknown type 'IntBox'` — a name the program declares one line above. Resolved through
the same door, it is the instance, and `q.value` reaches `Box`'s field.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
type Holder uses U
	export var item as U
	export static function hold(v U) returns Self
		return Self{item: v}
	end 'hold'
end 'Holder'
typealias IntBox = Box with Integer
typealias BoxHolder = Holder with IntBox
function main() returns ExitCode
	let h = BoxHolder.hold(IntBox.create(7))
	let q = h.item
	print("{q.value}\n")
	return q.value - 7
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: opaque-field-read-of-a-managed-struct-argument-borrows -->
⚠ **THE OWNERSHIP DIRECTION OF THE SAME RETYPE.** The argument here OWNS heap (a `String` field), so
naming the read's type correctly is exactly where a drop could be invented that the box already
owes: `q` is a BORROW of the box's content, the box drops it once at ITS scope exit, and a leak or a
double free would both show here — the runner treats either as a failure.
```maxon
typealias Integer = int(i64.min to i64.max)
type Label
	export var text as String
	export var n as Integer
	export static function create(t String, n Integer) returns Self
		return Self{text: t, n: n}
	end 'create'
end 'Label'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LabelBox = Box with Label
function main() returns ExitCode
	let b = LabelBox.create(Label.create("{9}", n: 3))
	let q = b.value
	print("{q.text} {q.n}\n")
	return q.n - 3
end 'main'
```
```exitcode
0
```
```stdout
9 3
```

<!-- test: opaque-field-write-on-a-concrete-instance-drops-a-generic-alias-argument -->
⭐⭐ **THE WRITE TWIN OF THE READ ABOVE, AND IT WAS A READ-AFTER-FREE (A4i review).** `emitFieldWrite`
re-spelled the read's `adoptType(substitutedInstanceArg(…))` instead of calling it, so fixing the READ
door left its declared twin handing a bare `named` to `classifyUnionPayload` — which resolves a `named`
through its OWN cascade, and that cascade has a struct arm but NO generic-alias arm. So this write fell
through to `undeclaredName`, took the SCALAR store, and neither dropped the old box nor moved the new one
in: the temporary was freed at the statement and the field kept pointing at it, so the read printed
`4557430888798830399` — `0x3F3F3F3F3F3F3F3F`, the free poison. Reading the field back was `E3011` before
A4i, so A4i is what turned a clean refusal into this.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
type Holder uses U
	export var item as U
	export static function hold(v U) returns Self
		return Self{item: v}
	end 'hold'
end 'Holder'
typealias IntBox = Box with Integer
typealias BoxHolder = Holder with IntBox
function main() returns ExitCode
	var h = BoxHolder.hold(IntBox.create(7))
	h.item = IntBox.create(8)
	let q = h.item
	print("{q.value}\n")
	return q.value - 8
end 'main'
```
```exitcode
0
```
```stdout
8
```

<!-- test: opaque-field-write-on-a-concrete-instance-drops-a-managed-struct-argument -->
The OTHER arm of that same door, and the reason the split above stayed hidden: a STRUCT argument was
already answered correctly — not by the shared door, but by `classifyUnionPayload`'s own struct arm
resolving the bare `named` a second time. Both arms come out of one call now, so this case and the one
above cannot part again. The old `Label` owns a `String`, so a write that failed to drop it would leak
and a write that dropped it twice would fault — the runner treats either as a failure.
```maxon
typealias Integer = int(i64.min to i64.max)
type Label
	export var text as String
	export var n as Integer
	export static function create(t String, n Integer) returns Self
		return Self{text: t, n: n}
	end 'create'
end 'Label'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LabelBox = Box with Label
function main() returns ExitCode
	var b = LabelBox.create(Label.create("{9}", n: 3))
	b.value = Label.create("{8}", n: 4)
	let q = b.value
	print("{q.text} {q.n}\n")
	return q.n - 4
end 'main'
```
```exitcode
0
```
```stdout
8 4
```

<!-- test: error.opaque-field-write-on-a-concrete-instance-refuses-another-struct -->
⭐⭐ **THE REFUSAL THE TWO CASES ABOVE WERE MISSING, AND WITHOUT IT THE WRITE THEY PIN WAS A WILD ONE
(BATCH32 review).** `b.value = <a Label>` is legal on a `Box with Label` because the instance binds `T`;
`b.value = <an Other>` is the same door with the identity that binds it VIOLATED, and nothing was asking.
The store already resolved `T` → `Label` (`emitFieldWrite`), while the type CHECK one line earlier was
handed the unsubstituted `T` — which admits everything — so an `Other` box was written into a `Label` slot
and reading it back exited **0xC0000005**. The bootstrap refuses the same program. Both readers now take
the field type from one derivation (`storedFieldType`), so the verdict and the store cannot disagree
about what the slot holds.
```maxon
typealias Integer = int(i64.min to i64.max)
type Label
	export var text as String
	export var n as Integer
	export static function create(t String, n Integer) returns Self
		return Self{text: t, n: n}
	end 'create'
end 'Label'
type Other
	export var k as Integer
	export static function create(k Integer) returns Self
		return Self{k: k}
	end 'create'
end 'Other'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LabelBox = Box with Label
function main() returns ExitCode
	var b = LabelBox.create(Label.create("hi", n: 3))
	b.value = Other.create(99)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:4: cannot assign a value of type 'Other' to field 'value' of 'Box', which holds 'Label'
```

<!-- test: error.opaque-field-write-in-the-shared-body-refuses-a-concrete-value -->
⭐⭐ **THE OTHER HALF OF THAT DOOR: A SHARED BODY CANNOT NAME A CONCRETE TYPE FOR `T`.** `Box uses T`'s
body is compiled ONCE for every instantiation, so `self.value = Label.create(77)` claims that every
`Box with X` holds a `Label` — true of at most one instantiation and unjustifiable for the rest. Here the
slot legitimately stays opaque (a `structRef` receiver binds nothing), so the cure is not substitution but
the ordinary identity comparison: an empty aggregate name meeting `Label` is a conflict, exactly as
`namedAggregatesConflict` has always said. It was reachable only because a blanket "an opaque slot has no
identity, so return" stood in front of that comparison. **MEASURED:** unguarded, this program compiled and
exited **101 — a leak** — after printing the `Label` box's ADDRESS as the integer the field is declared to
hold. The bootstrap does not adjudicate it: it internal-errors (`E9001 Unknown value kind: TypeParameter`),
which is its own defect and not a verdict.
```maxon
typealias Integer = int(i64.min to i64.max)
type Label
	export var n as Integer
	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Label'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function clobber()
		self.value = Label.create(77)
	end 'clobber'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	var b = IntBox.create(5)
	b.clobber()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:8: cannot assign a value of type 'Label' to field 'value' of 'Box', which holds 'type parameter'
```

<!-- test: generic-alias-union-payload-round-trips -->
⭐⭐ **A UNION PAYLOAD IS A SLOT TOO, AND ITS CASCADE ANSWERED THE SLOT QUESTION DIFFERENTLY (A4k).**
`ProgramSignatures.declaredSlotType` re-tags a bare `named` that names a generic alias to the instance
it denotes; `classifyUnionPayload` resolved a `named` through its OWN cascade, which had a struct arm
and NO generic-alias arm — so a payload declared `b PB` fell through to `undeclaredName`, the binding
kept the bare `named`, and reading it was `E3011 Unknown type 'PB'` on a program the oracle compiles
and runs. Both now come out of one door (`denotedSlotType`), so a name cannot denote one thing to a
struct field and another to a payload.
```maxon
typealias Num = int(0 to 1000)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias NumBox = Box with Num
union Payload
	boxed(b NumBox)
	empty
end 'Payload'
function main() returns ExitCode
	let p = Payload.boxed(NumBox.create(7))
	match p 'k'
		empty then print("none\n")
		boxed(b) then print("v={b.value}\n")
	end 'k'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=7
```

<!-- test: array-alias-union-payload-round-trips -->
The same gap under the alias form the corpus actually writes: an `Array` typealias is a generic alias,
so `list(xs Nums)` was `E3011 Unknown type 'Nums'` at the binding — and with it every container-typed
payload. The payload is the instance now, so `xs.count()` dispatches.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Nums = Array with Integer
union Payload
	list(xs Nums)
	empty
end 'Payload'
function main() returns ExitCode
	var a = Nums.create()
	a.push(4)
	a.push(5)
	let p = Payload.list(a)
	match p 'k'
		empty then print("none\n")
		list(xs) then print("n={xs.count()} first={try xs.get(0) otherwise 0}\n")
	end 'k'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=2 first=4
```

<!-- test: managed-instance-union-payload-moves-out-and-drops-once -->
⚠ **THE OWNERSHIP DIRECTION, AND IT IS WHY THE REFUSAL WAS NOT MERELY AN INCONVENIENCE.** Classified
`undeclaredName`, the payload was NOT managed: `moveInPayload` stored the pointer without consuming the
temporary, so the box was freed at the end of the construct statement while the union kept pointing at
it — the same dangling store `emitFieldWrite` was measured printing the free poison from. Classified as
the instance it is, the construct MOVES it in and the match binding moves it out and drops it once. The
instance owns a `String`, so a missed drop leaks (exit 101) and a double drop faults — the runner treats
either as a failure.
```maxon
type Label
	export var text as String
	export static function create(t String) returns Self
		return Self{text: t}
	end 'create'
end 'Label'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LabelBox = Box with Label
union Payload
	boxed(b LabelBox)
	empty
end 'Payload'
function show(b LabelBox)
	let q = b.value
	print("t={q.text}\n")
end 'show'
function main() returns ExitCode
	let p = Payload.boxed(LabelBox.create(Label.create("{9}")))
	match p 'k'
		empty then print("none\n")
		boxed(b) then show(b)
	end 'k'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
t=9
```

<!-- test: generic-alias-union-payload-drops-through-the-cascade -->
The drop half, with NOTHING bound: an unmatched managed payload is freed by the union's own
`__destruct_<U>` cascade, which is synthesized only when `caseHasManagedField` says the case carries
one — a whole-program walk that reads the payload column with no reader file and so asked the same
broken cascade. It compiled before this rung too, and that is the point: the payload was silently
freed at the construct statement instead, so the box outlived its own content. Now the box owns it and
the cascade frees it exactly once.
```maxon
type Label
	export var text as String
	export static function create(t String) returns Self
		return Self{text: t}
	end 'create'
end 'Label'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LabelBox = Box with Label
union Payload
	boxed(b LabelBox)
	empty
end 'Payload'
function main() returns ExitCode
	let p = Payload.boxed(LabelBox.create(Label.create("{9}")))
	match p 'k'
		empty then print("none\n")
		boxed then print("boxed\n")
	end 'k'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
boxed
```

<!-- test: function-alias-union-payload-calls-through -->
The THIRD arm the payload cascade was missing, found by deriving the difference rather than by probing
for it: a FUNCTION typealias is re-tagged by the shared door exactly as a generic one is, and was
equally absent here. Bound out of the payload it was a bare `named` — an int — so interpolating it was
`E2015 … a value of type 'unknown'` and calling it had no signature to check. It is a code pointer, a
scalar payload, and it calls.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
function twice(v Integer) returns Integer
	return v * 2
end 'twice'
union Payload
	op(f UnaryOp)
	empty
end 'Payload'
function main() returns ExitCode
	let p = Payload.op(twice)
	match p 'k'
		empty then print("none\n")
		op(f) then print("f={f(3)}\n")
	end 'k'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
f=6
```

<!-- test: a-field-chain-through-a-generic-instance-field -->
⭐⭐ **A FIELD CHAIN THROUGH A GENERIC-INSTANCE-TYPED FIELD (A4j).** `structLayoutOfType` — the door a
chain's BASE resolves through — has known `genericInstance` since P1.6-B1; `structLayoutOfField`, the door
each later HOP resolves through, did not. So `o.b.value` on a plain `type Outer` with `export var b as
IntBox` was refused by a message that contradicted itself, because `typeTagName(genericInstance)` prints
`"struct"`: *"a field access through 'Outer.b', which is declared 'struct' and not a struct"*. No type
parameter appears anywhere in this program's chain — the receiver `o` is an ordinary struct. MEASURED on the
runnable oracle, which prints 7.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias IntBox = Box with Integer
type Outer
	export var b as IntBox
	export static function create(v IntBox) returns Self
		return Self{b: v}
	end 'create'
end 'Outer'
function main() returns ExitCode
	let o = Outer.create(IntBox.create(7))
	print("{o.b.value}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-field-chain-through-a-type-parameter-field -->
⭐ **THE SECOND SPELLING, AND IT IS THE SAME DOOR.** `pb.value.x` hops through a field the shared generic
body typed `T`, so `structLayoutOfField` saw a `typeParameter` where the first case gave it a
`genericInstance` — one function, two tags, one refusal. Continuing the walk therefore needs the hop's type
SUBSTITUTED through the instance in hand, which is the same substitution `emitFieldLoad` already applies to
the loaded VALUE (`instanceSubstitutedType`); left unsubstituted a `typeParameter` reads as an INTEGER
(`tagIsIntegral`). `opaque-field-read-of-a-struct-argument-is-that-struct` above is this program with the
read SPLIT INTO TWO STATEMENTS, which is precisely the hop that never reached the walk. MEASURED on the
oracle: 7.
```maxon
typealias Integer = int(i64.min to i64.max)
type Point
	export var x as Integer
	export static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias PointBox = Box with Point
function main() returns ExitCode
	let pb = PointBox.create(Point.create(7))
	print("{pb.value.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: a-three-hop-chain-through-an-instance-survives-both-substitutions -->
Both hops in one chain: `o.b` is a generic-instance field, `.value` is that instance's `T`-typed field, and
`.n` is a plain field of the struct `T` turned out to be. The substitution has to survive more than one hop
or the third one reads an integer. MEASURED on the oracle: 9.
```maxon
typealias Integer = int(i64.min to i64.max)
type Leaf
	export var n as Integer
	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Leaf'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias LeafBox = Box with Leaf
type Outer
	export var b as LeafBox
	export static function create(v LeafBox) returns Self
		return Self{b: v}
	end 'create'
end 'Outer'
function main() returns ExitCode
	let o = Outer.create(LeafBox.create(Leaf.create(9)))
	print("{o.b.value.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: error.a-field-chain-through-a-builtin-instance-field-is-still-refused -->
⛔⛔ **THE MEMORY-SAFETY GATE, CARRIED ACROSS TO THE HOP DOOR.** `structLayoutOfType` does not ask `structOf`
for an instance — it asks `genericInstanceHasBaseLayout` first, and its header records what happens without
that, MEASURED, with no diagnostic anywhere and a compile that exits 0: `a.value` on an `Array with Integer`
printed **7077984**, the element buffer POINTER read as an Integer, and `a.value = 4242` overwrote that
pointer and took an access violation (0xC0000005) in the teardown. `structOf` answering "a type of this name
is declared" is NOT the same fact as "this instance's fields live at that layout": for a BUILTIN base the
runtime record is a synthesized struct unrelated to any user `type` of the same name.

So teaching the HOP door about `genericInstance` and forgetting the gate would reintroduce that miscompile
one door over. This case is what says it did not.

⭐⭐ **THE GATE IS UNCHANGED BY THE `stdlib/Array.maxon` LISTING, AND THAT IS THE POINT WORTH PINNING —
BECAUSE THE LISTING IS EXACTLY THE CHANGE THAT COULD HAVE DISSOLVED IT.** The old closing sentence read
*"shv2's answer is the one it can honestly give — it reads no stdlib, so the field is missing from this
compiler rather than from the language"*, and that premise is gone: there IS a `type Array` declaration now,
it DOES carry a `StructLayout`, and so `structOf` finally answers YES for this name. That is precisely the
condition `genericInstanceHasBaseLayout` exists to distinguish from — *"a type of this name is declared"* is
still NOT the fact *"this instance's fields live at that layout"*, because the buffer an `Array with Integer`
carries is the synthesized runtime record and not the corpus struct. The measurement in the paragraph above
(`a.value` reading the element buffer POINTER as **7077984**, `a.value = 4242` overwriting it and taking a
0xC0000005) is what the gate prevents, and it is now prevented against a REAL declaration rather than
against a missing one.

⇒ The refusal survives with the same force and a better sentence: `E3018 type 'Array' has no field named
'value'`, which is also what the oracle says (`E4006 Type 'IntArray' has no field named 'value'`) — the two
compilers now give one answer to this program where they used to give two.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
type Holder
	export var a as IntArray
	export static function create(a IntArray) returns Self
		return Self{a: a}
	end 'create'
end 'Holder'
function main() returns ExitCode
	let h = Holder.create([1, 2, 3])
	print("{h.a.value}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3018: <fragment>:12:14: type 'Array' has no field named 'value'
```

<!-- test: generic-managed-return-round-trips -->
⭐⭐ **A `T`-RETURNING METHOD ON A MANAGED CONCRETE INSTANCE HANDS BACK THE CONCRETE TYPE, AND THE ROUND TRIP
IS WHAT PROVES IT (A5o).** `bx.echo(Alpha.create())` was accepted — its argument is the opaque `T`, which
agrees with everything — and then FEEDING THE RESULT BACK was refused: `E3005 … expected 'Alpha', got 'type
parameter'` at the second call, and `back.a` was `E2015 … declared 'type parameter' and not a struct type`.
One instantiation, one method, and the two directions disagreed.

The cause was an ORDER, not a rule: the drop enrolment (`valueIsManagedHeap` → `trackOwnedTemp`) ran inside
the MINT, while the tag was still the opaque `T`, so it always answered "owns nothing"; the retype then
DECLINED to fire for a managed argument precisely because that decision had already been made. Enrolling
AFTER the retype makes both halves true at once. MEASURED on the oracle: `a=1`.
```maxon
typealias Integer = int(i64.min to i64.max)
type Alpha
	export var a as Integer
	export static function create() returns Self
		return Self{a: 1}
	end 'create'
end 'Alpha'
type Box uses T
	export var tag as Integer
	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
	export function echo(v T) returns T
		return v
	end 'echo'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	var bx = AlphaBox.create()
	let got = bx.echo(Alpha.create())
	let back = bx.echo(got)
	print("a={back.a}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=1
```

<!-- test: generic-managed-return-drops-once -->
⛔⛔ **THE CASE THE DELETED `managedConcrete` GUARD EXISTED FOR — a managed `T` return bound to a `let` and
left to fall out of scope.** Before A5o the enrolment could never fire for such a result (the tag was still
opaque when it was asked), so the guard's job was to stop the retype from making the value LOOK managed
after the drop decision had been taken. With the enrolment moved after the retype the two agree, and this
is the case that says so: a hundred boxes allocated, each dropped exactly once. A missed enrolment is a
leak (the runner reports exit 101); a doubled one faults on the poisoned box.
```maxon
typealias Integer = int(i64.min to i64.max)
type Alpha
	export var a as Integer
	export static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'Alpha'
type Box uses T
	export var tag as Integer
	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
	export function echo(v T) returns T
		return v
	end 'echo'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	var bx = AlphaBox.create()
	var i = 0
	var total = 0
	while i < 100 'loop'
		let back = bx.echo(Alpha.create(i))
		total = total + back.a
		i = i + 1
	end 'loop'
	return total - 4950
end 'main'
```
```exitcode
0
```

<!-- test: generic-managed-string-return-drops-once -->
The OTHER half of `valueIsManagedHeap` — a BYTE RECORD rather than an aggregate. `Box with String` reaches
the enrolment through `tagIsByteRecord`, not `valueIsNonTextAggregate`, so a fix that moved only the
aggregate half would pass the case above and leak here. A hundred fused String records, each returned out
of the shared body and dropped once; the byte lengths of `0`…`99` sum to 190.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var tag as Integer
	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
	export function echo(v T) returns T
		return v
	end 'echo'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	var bx = StrBox.create()
	var i = 0
	var total = 0
	while i < 100 'loop'
		let s = bx.echo("{i}")
		total = total + s.byteLength()
		i = i + 1
	end 'loop'
	return total - 190
end 'main'
```
```exitcode
0
```

<!-- test: generic-managed-return-passed-on -->
⚠ **THE ENROLMENT AND THE CALLER-SIDE CONSUME MEET HERE, AND THIS IS THE PATH THAT DOUBLE-FREES IF THEY
DISAGREE.** The `T` result is enrolled as an owned temporary and then MOVED into `Wrap`'s field by
`applyCallerConsume`, which poisons the source and takes it back off the pending list. Enrol without the
transfer and the box is freed at the statement while the field still points at it; transfer without the
enrolment and nobody ever owed the drop. The oracle cannot compile this program — it types a `T`-returning
method's result as `int` at a concretely-typed parameter (`E3005 … expected 'Alpha', got 'int'`, a bootstrap
defect) — so the answer is pinned by the non-generic control it DOES run, `Wrap.create(Alpha.create(5))`,
which reads back 5.
```maxon
typealias Integer = int(i64.min to i64.max)
type Alpha
	export var a as Integer
	export static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'Alpha'
type Wrap
	export var held as Alpha
	export static function create(v Alpha) returns Self
		return Self{held: v}
	end 'create'
end 'Wrap'
type Box uses T
	export var tag as Integer
	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
	export function echo(v T) returns T
		return v
	end 'echo'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	var bx = AlphaBox.create()
	let w = Wrap.create(bx.echo(Alpha.create(5)))
	print("{w.held.a}\n")
	return w.held.a - 5
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: generic-managed-return-relayed-inside-the-generic-body -->
⭐⭐ **THE CO-OWN MUST NOT COMPOUND, AND THIS IS THE SHAPE THAT WOULD MAKE IT.** `relay()` returns what
`self.get()` returned — a `T` produced by a call made INSIDE the shared body. If the caller-side co-own
fired at both call sites the box would take two references and be dropped once: a LEAK, not a double free,
and therefore invisible to a crash and visible only to the leak gate. It does not fire twice, and the
reason is structural rather than lucky: `retypeOpaqueMethodResult` substitutes only when the receiver's tag
is `genericInstance`, and inside the shared body `self` is the generic BASE, so no retype happens there and
no co-own is spent. The instantiation's single co-own at `bx.relay()` is the only one, which is exactly the
claim that a `T` return is a BORROW every callee refuses to retain. Pinned because a second co-own here
would pass every other case in this file.
```maxon
typealias Integer = int(i64.min to i64.max)
type Alpha
	export var a as Integer
	export static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
	export function relay() returns T
		return self.get()
	end 'relay'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	let bx = AlphaBox.create(Alpha.create(3))
	let r = bx.relay()
	print("r={r.a}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=3
```

<!-- test: generic-managed-return-overload-selection -->
⭐ **THE REFUSAL THAT GOES AWAY.** While the result kept the opaque tag, an overloaded callee handed one had
nothing to choose by, and shv2 said so rather than guessing (`resolving an overload of 'over' against an
argument of opaque generic type`). Retyped, the argument is an `Alpha` and there is nothing left to
disambiguate. The oracle cannot run the generic spelling (same bootstrap defect as the case above: it types
`b.get()` as `int` and reports `E3005 … expected 'Alpha', got 'int'`); the value 7 is the oracle's own answer
to the non-generic control `over(Alpha.create(3))`, which this program must agree with.
```maxon
typealias Integer = int(i64.min to i64.max)
type Alpha
	export var a as Integer
	export static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias AlphaBox = Box with Alpha
function over(x Alpha) returns Integer
	return x.a + 4
end 'over'
function over(x Integer) returns Integer
	return x + 22
end 'over'
function main() returns ExitCode
	let b = AlphaBox.create(Alpha.create(3))
	print("{over(b.get())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: generic-managed-union-return-overload-selection -->
⛔⛔ **THE EXACT PROGRAM THE DELETED GUARD'S HEADER CITED AS ITS EVIDENCE — `Box with Shape` for a BOXED
UNION.** That header recorded a MEASURED wrong answer of exit 25 where 7 was correct: the retype fired, the
substituted type stayed a bare `named` (which reads as an INTEGER), and `over(b.get())` chose
`over(x Integer)` and did arithmetic on the payload word. A4i/A4k routed the substitution through
`declaredSlotType` in the meantime, so the union name is now re-interned as itself and picks its own
overload — but nothing in either corpus said so, which is how a fix could have silently un-fixed it. The
oracle runs the non-generic control (`over(Shape.circle(3))` with the `Shape` overload alone) and answers 7;
with both overloads present it reports its own `E3007 Ambiguous overload … (x Shape), (x i64)`, so the
generic spelling is beyond it.
```maxon
typealias Integer = int(i64.min to i64.max)
union Shape
	circle(r Integer)
	square(s Integer)
end 'Shape'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias ShapeBox = Box with Shape
function over(x Shape) returns Integer
	match x 'k'
		circle(r) then return r + 4
		square(s) then return s + 5
	end 'k'
end 'over'
function over(x Integer) returns Integer
	return x + 22
end 'over'
function main() returns ExitCode
	let b = ShapeBox.create(Shape.circle(3))
	print("{over(b.get())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```

<!-- test: generic-managed-return-of-an-rdata-literal-copies -->
⛔ **THE REASON THE CO-OWNERSHIP GOES THROUGH `promoteBorrowedToOwned` AND NOT THROUGH A BARE INCREF.** The
reference a shared generic body could not take is taken by the CALLER (`coOwnSubstitutedCallResult`), and
for a `String` that must be a COPY: `"hi"` is an immortal `.rdata` literal (capacity == -2) whose header
lives in read-only memory, so `__mm_incref` on it writes a page the loader mapped read-only. The
promotion's byte-record arm copies instead, exactly as a concrete `returns String` callee's own hand-off
does. The oracle cannot compile this program either — it types the generic result as `int`
(`E4006 Variable 's' is not a struct or enum type` at `s.byteLength()`) — so the answer is pinned by the
non-generic control `let s = "hi"`, whose `byteLength()` is 2.
```maxon
typealias Integer = int(i64.min to i64.max)
type Box uses T
	export var tag as Integer
	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
	export function echo(v T) returns T
		return v
	end 'echo'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	var bx = StrBox.create()
	let s = bx.echo("hi")
	print("{s}\n")
	return s.byteLength() - 2
end 'main'
```
```exitcode
0
```
```stdout
hi
```

<!-- test: generic-managed-return-routed-through-a-try-block -->
⚠ **THE SUBSTITUTED RESULT MEETS THE BLOCK-FORM `try`'s ROUTING, WHICH IS THE ONE ORDERING A5o COULD NOT
MOVE.** `routeBareThrowingCallToTryBlock` rewrites the call to a `tryCall` and builds the throw edge, and
it depends on the result's drop obligation being settled before the op is appended — on the error edge the
result register was never written, so releasing it there faults. A SUBSTITUTED result owes nothing on that
edge (the callee took no reference for it) and is co-owned only on the OK continuation, after the routing
has forked. Both paths run here: `run(0)` returns through the body, `run(1)` throws out of the same call.
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'Alpha'
type Box uses T
	export var tag as Integer
	export static function create(t Integer) returns Self
		return Self{tag: t}
	end 'create'
	export function echo(v T) returns T throws Boom
		if self.tag > 0 'blows'
			throw Boom.bad
		end 'blows'
		return v
	end 'echo'
end 'Box'
typealias AlphaBox = Box with Alpha
function run(t Integer) returns Integer
	var bx = AlphaBox.create(t)
	try 'work'
		let back = bx.echo(Alpha.create(9))
		print("ok={back.a}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			bad then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'
function main() returns ExitCode
	let a = run(0)
	let b = run(1)
	return a + b
end 'main'
```
```exitcode
0
```
```stdout
ok=9
caught
```

<!-- test: expression-form-try-over-a-substituted-return -->
⭐⭐ **THE EXPRESSION FORM OF THE CASE THE BLOCK FORM ABOVE HANDLES, AND IT REACHES THE SAME SHAPE BY THE
SAME RULE: THE CALLER'S `+1` RIDES THE OK EDGE.** A shared generic body cannot classify its `T` return, so
it hands one back as a BORROW and the caller takes its own reference (`coOwnSubstitutedCallResult`) —
necessarily after the call, which is where the value exists. An expression `try` splits on the error flag
at exactly that point, and on the error edge the result register was never written, so the promotion may
not sit in the block both edges flow from. It does not: its ops are lifted off the fork's entry block and
re-attached as the FIRST ops of `tryok`, the one edge the `tryCall` wrote the result on. That is the shape
`generic-managed-return-routed-through-a-try-block` already emits — reached here by MOVING the ops the
call left behind rather than by building a second mechanism to emit them elsewhere.

⚠ **THE ORACLE MISCOMPILES THIS PROGRAM, so it is NOT the arbiter for the capability.** MEASURED on the
bootstrap: it compiles, prints `v=9`, and then dies with `mm_decref: refcount underflow (already zero)`
in `__destruct_AlphaBox`, exit 1 — it takes no reference and then over-releases. shv2's answer is derived
from the rule and from the block form's golden shape, never from what the reference emits.
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(v Integer) returns Self
		return Self{a: v}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function fetch() returns T throws Boom
		return self.value
	end 'fetch'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	let bx = AlphaBox.create(Alpha.create(9))
	let got = try bx.fetch() otherwise Alpha.create(0)
	print("v={got.a}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=9
```

<!-- test: expression-form-try-over-a-substituted-string-return -->
The other managed spelling, which used to reach the refusal by a different wrong road — and every
clause of that road is now HISTORY, so read the next two sentences in the past tense. A `String`
substitution promoted by COPYING (`promoteToOwnedString`), so the op the try-rewrite found trailing the
target was a string-interpolation rather than the aggregate's `__mm_retain` call, and the program was
told *"the expression after `try` is not a call"* about a program whose `try` is applied to exactly one.
Both spellings are ONE construct, so the cure keyed the move onto the ok edge on the promotion's op RANGE
and not on which op the promotion happened to end with — a cure that only understood the aggregate arm's
single `call` would have left the interpolation's whole op chain on the fork's entry block. TODAY neither
half is emitted at this call: the `+1` for a substituted `T` return is taken by the CALLEE
(`emitOwnedValueReturn`, P1.7 slice 3b-vi-a), so nothing trails the call to lift, and a borrowed `String`
handed off is RETAINED rather than copied (`retainBorrowedByteRecord` → `__str_retain`, `ca5169e231`).
What the case pins is unchanged and is the ANSWER: `v=hi` off the ok edge of an expression-form `try`
over a substituted managed return.
```maxon
enum Boom implements Error
	bad
end 'Boom'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function fetch() returns T throws Boom
		return self.value
	end 'fetch'
end 'Box'
typealias StrBox = Box with String
function main() returns ExitCode
	let bx = StrBox.create("hi")
	let got = try bx.fetch() otherwise "no"
	print("v={got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=hi
```

<!-- test: expression-form-try-over-a-trivial-substituted-return -->
⭐ **THE NEGATIVE CONTROL, AND THE DISCRIMINATOR THAT PROVED THE DIAGNOSIS.** A TRIVIAL instantiation
owns no heap, so `coOwnSubstitutedCallResult` returns early and emits nothing at all — the call op is
still the last op the target emitted, the rewrite claims it, and the program compiles and runs. That
asymmetry between `Box with Integer` and `Box with Alpha` is what identified the co-own's promotion,
rather than anything about `try` or about generics, as what the rewrite was grabbing. It stays a control
after the cure: there is no promotion here to move onto the ok edge, so **not one op may change** and the
golden must not budge. Widen the move to every substituted return and this case starts relocating the
CALL itself — the sabotage this control is here to fail.
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function fetch() returns T throws Boom
		return self.value
	end 'fetch'
end 'Box'
typealias IntBox = Box with Integer
function main() returns ExitCode
	let bx = IntBox.create(9)
	let got = try bx.fetch() otherwise 0
	print("v={got}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=9
```

<!-- test: condition-form-try-over-a-substituted-return -->
⚠ **THE SIBLING DOOR, WHICH HAD THE SAME WRONG NOUN AND TAKES THE SAME CURE.** `if let x = try …` does not
go through `parseTry` at all — `parseCatchingCondition` runs the same three steps and lets the `if` own the
fork — but it takes the target's VALUE through the identical `parseTryCallTarget`, so it was broken
identically and told the author about `__mm_retain` identically. The promotion is lifted off the condition's
entry block at that ONE funnel, and each door re-attaches it at the head of its own ok edge: `tryok` for the
expression form, the `if`'s THEN block here (the condition tests `flag == 0`, so then IS the ok edge).
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(v Integer) returns Self
		return Self{a: v}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function fetch() returns T throws Boom
		return self.value
	end 'fetch'
end 'Box'
typealias AlphaBox = Box with Alpha
function main() returns ExitCode
	let bx = AlphaBox.create(Alpha.create(9))
	if let got = try bx.fetch() 'ok'
		print("v={got.a}\n")
	end 'ok' else (e) 'bad'
		print("boom\n")
	end 'bad'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=9
```

<!-- test: try-over-a-call-whose-argument-is-a-substituted-return -->
⭐ **THE SECOND NEGATIVE CONTROL, AND THE ONE THAT PINS THE OTHER HALF OF THE MOVE'S CONDITION.** A
substituted `T` result is co-owned here too — `bx.get()` returns `Alpha` — but it is an ARGUMENT of the
try target, so its promotion is emitted BEFORE `consume`'s call and `consume`'s call is still the last op
the target left behind. The rewrite claims the right op and the program is legal WITHOUT anything moving:
the promotion belongs on the unconditional path here, because the value it takes a reference to is one
`consume` is handed and not one the fork's ok edge produces. That is why the move tests the co-own's own
VALUE against the target rather than merely asking whether a co-own happened inside the target's extent:
the looser question is true here, and a deferral keyed on it would carry this retain past `consume`'s own
fork — a `+1` on the wrong side of a split, and an argument handed the borrow it was taken to replace.
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(v Integer) returns Self
		return Self{a: v}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
	export function get() returns T
		return self.value
	end 'get'
end 'Box'
typealias AlphaBox = Box with Alpha
function consume(a Alpha) returns Integer throws Boom
	if a.a > 100 'big'
		throw Boom.bad
	end 'big'
	return a.a
end 'consume'
function main() returns ExitCode
	let bx = AlphaBox.create(Alpha.create(9))
	let n = try consume(bx.get()) otherwise 0
	print("n={n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n=9
```

<!-- test: try-over-a-substituted-return-whose-receiver-is-a-fork-temporary -->
⭐⭐ **THE CO-OWN LANDS *FIRST* ON THE OK EDGE, AHEAD OF THE FORK'S OWN DROPS — AND THIS IS THE PROGRAM
THAT CAN TELL.** `try make(f).fetch()` builds its receiver as a TEMPORARY, and `desugarTry` releases the
fork's temporaries at the top of BOTH edges (they are live on each and each must drop them once). The
box's release runs `__destruct_Box_Alpha`, which decrefs the very `Alpha` the promotion is about to take a
reference to — so a co-own placed after those drops retains a freed record and `got.a` reads the `0x3F3F…`
poison. Placed first, it holds the field alive across the box's own death and the answer is **11**.

Every other case in this family binds its receiver, so the box outlives the statement and the ordering is
unobservable. This one is why `attachSubstitutedCoOwnToOkEdge` is called on a block nothing has emitted
into yet, rather than merely "somewhere on the ok edge".
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(v Integer) returns Self
		return Self{a: v}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export var fail as bool
	export static function create(v T, f bool) returns Self
		return Self{value: v, fail: f}
	end 'create'
	export function fetch() returns T throws Boom
		if self.fail 'boom'
			throw Boom.bad
		end 'boom'
		return self.value
	end 'fetch'
end 'Box'
typealias AlphaBox = Box with Alpha
function make(f bool) returns AlphaBox
	return AlphaBox.create(Alpha.create(11), f: f)
end 'make'
function forkTemp(f bool) returns Integer
	let got = try make(f).fetch() otherwise Alpha.create(1)
	return got.a
end 'forkTemp'
function main() returns ExitCode
	print("ok={forkTemp(false)} err={forkTemp(true)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok=11 err=1
```

<!-- test: condition-form-try-over-a-substituted-return-whose-receiver-is-a-fork-temporary -->
⛔⛔ **THE SIBLING DOOR'S FORK TEMPORARY, WHICH THE REVIEW FOUND MISCOMPILING AND WHICH THE ORDERING
ARGUMENT ABOVE DOES NOT REACH ON ITS OWN.** `desugarTry` releases the fork's temporaries at the top of
each EDGE, so the co-own it puts at the head of `tryok` is ahead of them for free. A catching `if` used to
release its condition's temporaries in the ENTRY block instead — one release dominating both edges, which
is right for every condition whose temporaries nothing past the fork still reads, and wrong for exactly
this one: `__destruct_Box_Alpha` ran before the branch, so the retain on the then edge took a reference to
a freed record and `got.a` read the poison. MEASURED, before the fix: `ok=4557430888798830399`.

⭐ **THE CURE IS THAT THE TWO `try` FORKS NOW SAY IT ONCE** — a catching `if` takes `desugarTry`'s per-edge
rule (`takeCatchingForkTemps`), so the head of its then block is the co-own and then the drops, and the
head of its else block is the drops, in the order the expression form has always had. The pair of cases —
this one and its `otherwise` twin above — is what keeps the two doors from drifting apart again.
```maxon
typealias Integer = int(0 to 125)
enum Boom implements Error
	bad
end 'Boom'
type Alpha
	export var a as Integer
	export static function create(v Integer) returns Self
		return Self{a: v}
	end 'create'
end 'Alpha'
type Box uses T
	export var value as T
	export var fail as bool
	export static function create(v T, f bool) returns Self
		return Self{value: v, fail: f}
	end 'create'
	export function fetch() returns T throws Boom
		if self.fail 'boom'
			throw Boom.bad
		end 'boom'
		return self.value
	end 'fetch'
end 'Box'
typealias AlphaBox = Box with Alpha
function make(f bool) returns AlphaBox
	return AlphaBox.create(Alpha.create(11), f: f)
end 'make'
function forkTemp(f bool) returns Integer
	if let got = try make(f).fetch() 'ok'
		return got.a
	end 'ok' else (e) 'bad'
		return 1
	end 'bad'
end 'forkTemp'
function main() returns ExitCode
	print("ok={forkTemp(false)} err={forkTemp(true)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok=11 err=1
```

<!-- test: error.a-type-parameter-belongs-to-the-type-that-declares-it -->
⭐⭐ **`Array with U` IN ONE GENERIC TYPE AND `Array with V` IN ANOTHER ARE TWO TYPES, AND A DIAGNOSTIC
MUST SAY WHICH (W14).** A `typeParameter`'s identity used to be its POSITION in its declaring type's
`uses` list and nothing else, so `HoldA`'s first parameter and `HoldB`'s first parameter were ONE
value — and `GenericInstanceRegistry.instanceKey`, which mixes each argument's `(tag, id)` pair,
therefore gave `Array with U` and `Array with V` ONE key. Not two names for two types: one interned
type. `deriveInstanceDisplayNames` then settled the two spellings by FIRST DECLARATION, which is the
right rule for two names that really are true of one type and the wrong one for a type the key had
merged by accident.

⛔ **MEASURED on this program and its twin below — byte-identical apart from which type is declared
first.** Against the compiler as it stood, this order reported `got 'HoldA.Items'` for a value whose
type is `HoldB`'s, and the swapped order reported `got 'HoldB.Items'`: one program, two answers,
decided by declaration order. That is the build-order artifact the 2026-07-24 user ruling recorded at
`StdlibLoader.maxon:39-63` bars from a diagnostic, and it is the same defect `W14b` closed for a
nested tuple's name one rung earlier.

⭐ **THE PAIR IS THE GATE, AND NO ORDER-DEPENDENT CITATION CAN PASS IT**: the two cases pin ONE
byte-identical sentence, so a compiler that lets declaration order pick the name must fail exactly one
of them WHATEVER text is pinned. The cure is in the KEY — a type parameter is now identified by a
digest of `(declaring type, parameter name)` (`ProgramSignatures.typeParamTokenFor`) — and NOT in the
display rule, which needed no change once the two types stopped sharing an instance.

The refusal itself is correct in both orders: an `Array` of `Num` and an `Array` of `String` are
different types. Only the NAME was wrong.

⚠ **THE `got` SIDE MOVED AT W25, FROM THE DECLARATION VIEW TO THE INSTANCE VIEW, AND THE PAIR STILL
MEASURES WHAT IT WAS BUILT TO MEASURE.** `b.items` on a `HoldB with String` no longer keeps the shared
body's `Array with V` — it is substituted to the type the receiver actually fixes, `Array with String` — so
the sentence names that type by the `typealias` the program declares for it (`Strs`) instead of by the
inner alias of the declaration it was read through (`HoldB.Items`). The property the pair exists for is
untouched: the two orders must still print ONE byte-identical sentence, and a compiler that lets
declaration order pick the name fails exactly one of them.

⚠ `Strs` is declared here ON PURPOSE, in both cases identically. Without it the program names
`Array with String` nowhere, and `instanceDisplayName`'s first-declaration fallback then reaches into
STDLIB and quotes `BuildConfig.StringArray` — a non-exported inner alias of a type this program has never
heard of. That fallback is not W25's: it reproduces on the merge base for a bare `total(["a", "b"])`, and
it is reported as its own finding rather than pinned here, because pinning it would couple this case to a
stdlib private name.
```maxon
typealias Num = int(0 to 1000)
typealias Nums = Array with Num
typealias Strs = Array with String

type HoldA uses U
	typealias Items = Array with U
	export var items as Items

	static function of(first U) returns HoldA
		var xs = Items.create()
		xs.push(first)
		return HoldA{items: xs}
	end 'of'

	export function total(other Nums) returns Num
		return other.count()
	end 'total'
end 'HoldA'

type HoldB uses V
	typealias Items = Array with V
	export var items as Items

	static function of(first V) returns HoldB
		var xs = Items.create()
		xs.push(first)
		return HoldB{items: xs}
	end 'of'
end 'HoldB'

typealias HoldANum = HoldA with Num
typealias HoldBStr = HoldB with String

function main() returns ExitCode
	let a = HoldANum.of(42)
	let b = HoldBStr.of("hello")
	var s = Strs.create()
	s.push("names the type the refusal quotes")
	return a.total(b.items)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:40:11: argument type mismatch for 'other': expected 'Nums', got 'Strs'
```

<!-- test: error.a-type-parameter-belongs-to-the-type-that-declares-it-swapped -->
The RED-GATE CONTROL of the case above: the same program with the two type declarations exchanged, and
the same pinned sentence, to the byte. `HoldA` is 14 lines and `HoldB` is 10 either way, so
`a.total(b.items)` sits at the same line in both — which is what leaves declaration order as the only
difference between the two cases, and therefore the only thing the pair can be measuring.
```maxon
typealias Num = int(0 to 1000)
typealias Nums = Array with Num
typealias Strs = Array with String

type HoldB uses V
	typealias Items = Array with V
	export var items as Items

	static function of(first V) returns HoldB
		var xs = Items.create()
		xs.push(first)
		return HoldB{items: xs}
	end 'of'
end 'HoldB'

type HoldA uses U
	typealias Items = Array with U
	export var items as Items

	static function of(first U) returns HoldA
		var xs = Items.create()
		xs.push(first)
		return HoldA{items: xs}
	end 'of'

	export function total(other Nums) returns Num
		return other.count()
	end 'total'
end 'HoldA'

typealias HoldANum = HoldA with Num
typealias HoldBStr = HoldB with String

function main() returns ExitCode
	let a = HoldANum.of(42)
	let b = HoldBStr.of("hello")
	var s = Strs.create()
	s.push("names the type the refusal quotes")
	return a.total(b.items)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:40:11: argument type mismatch for 'other': expected 'Nums', got 'Strs'
```

<!-- test: an-inner-alias-field-crosses-into-a-parameter-of-its-own-instance -->
⭐⭐ **THE LEGAL COUNTERPART OF THE PAIR ABOVE — AND IT WAS REFUSED (W25).** The two cases above pin that
`HoldA`'s `Items` and `HoldB`'s `Items` are DIFFERENT types and a diagnostic must say which. This one pins
the other half of that sentence: **`HoldA`'s `Items` and `HoldA`'s `Items` are the SAME type**, so a value
read out of one `HoldA with Num` may be handed to a parameter of another one. It was
**`E3005: argument type mismatch for 'other': expected 'HoldANum.Items', got 'HoldA.Items'`** — a program
handing a value to a parameter of its own type, refused, with a sentence naming one type twice. The oracle
builds it, runs it and prints `ok`.

⚠ **THE MECHANISM IS THE SAME DECLARATION-VIEW/INSTANCE-VIEW SPLIT the extension crash is made of**, one
surface over: substitution stopped at a bare `typeParameter`, so a field whose declared type is a generic
INSTANCE (`Array with T`, reached through the inner `typealias Items`) was never substituted at all and
`b.items` kept the shared body's opaque `Array with T` where the receiver had already fixed it to
`Array with Num`. The NON-generic control is the case below: the identical store on a plain `type` compiled
and ran throughout, which is what says the store is ownership-legal and this was purely a substitution
failure.
```maxon
typealias Num = int(i64.min to i64.max)

type HoldA uses T
	typealias Items = Array with T
	export var items as Items
	export static function create(seed T) returns Self
		var arr = Items.create()
		arr.resize(1)
		try arr.set(0, value: seed) otherwise panic("seeded slot 0 must exist after resize(1)")
		return Self{items: arr}
	end 'create'
	export function adopt(other Items)
		self.items = other
	end 'adopt'
	export function isSingle() returns bool
		return self.items.count() == 1
	end 'isSingle'
end 'HoldA'

typealias HoldANum = HoldA with Num

function main() returns ExitCode
	let b = HoldANum.create(7)
	var a = HoldANum.create(1)
	a.adopt(b.items)
	if a.isSingle() 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

<!-- test: the-same-field-store-on-a-non-generic-type -->
The one-variable CONTROL of the case above: the identical program with the type parameter removed and the
array alias hoisted to file scope, so nothing is substituted and nothing can fail to be. It compiled and
ran on the merge base, which is what attributes the refusal above to the substitution and not to the store,
to the array's ownership, or to `adopt`'s signature.
```maxon
typealias Num = int(i64.min to i64.max)
typealias NumArray = Array with Num

type HoldConcrete
	export var items as NumArray
	export static function create(seed Num) returns Self
		var arr = NumArray.create()
		arr.resize(1)
		try arr.set(0, value: seed) otherwise panic("seeded slot 0 must exist after resize(1)")
		return Self{items: arr}
	end 'create'
	export function adopt(other NumArray)
		self.items = other
	end 'adopt'
	export function isSingle() returns bool
		return self.items.count() == 1
	end 'isSingle'
end 'HoldConcrete'

function main() returns ExitCode
	let b = HoldConcrete.create(7)
	var a = HoldConcrete.create(1)
	a.adopt(b.items)
	if a.isSingle() 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ok
```

### ⭐⭐ An OPAQUE type parameter may not stand where a CONCRETE AGGREGATE is declared — W179

A generic body compiles ONCE, for the declaration view, and a `T` there is one opaque machine word whose
representation no instantiation has fixed. A CONCRETE AGGREGATE place — a `String`, a struct, a generic
instance, a function value, a `Character` — is a promise that the word is a POINTER to a particular record
with a particular destructor, and the shared body cannot keep that promise: it is dereferenced, stored and
DROPPED under the declared type. Passing a `T` there was accepted with no diagnostic at all and the program
faulted (`0xC0000005`), because the deferral that admits a type parameter lives in the TAG domain and a
primitive formal has no other domain to be judged in.

**The refusal does NOT depend on what the instantiations bind.** It is refused at `Outer with String` — where
`T` really is `String` — exactly as it is refused at `Outer with Integer`, and
`error.opaque-type-parameter-at-a-concrete-aggregate-argument-is-refused-at-a-matching-instantiation` below
pins that.
Two things in the tree already answer it that way and this is now the third: the MANAGED FIELD door has always
compared the tags EXACTLY (`Parser.requireManagedValueMatches`), so `self.s = t` is refused at every
instantiation; and the bootstrap oracle refuses both spellings at the same position with the same E3005.
Deciding it from the instantiation set instead would need an ALL-fold over every `with` in the program, and
would make a body's validity change when an unrelated file adds a second instantiation.

⚠ **The SCALAR quadrant is deliberately NOT refused, and that is a BOUND on this rule rather than a claim
that it is sound.** A `T` meeting a declared `int`/`bool` place is a word moving to a word-shaped slot: it
dereferences nothing and drops nothing, so the worst it can produce is a wrong NUMBER, never the fault above —
and `array-declared-record`'s `the-self-spelling-of-a-corpus-served-member-reaches-the-same-body` is a
committed program that returns an opaque element as its ranged-int alias and depends on being allowed to.
Refusing that quadrant as well was MEASURED against the whole suite and costs three committed cases, two of
them `error.` cases whose own subject it masks; separating the sound instances from the unsound ones needs the
instantiation fold this rule deliberately does without.

<!-- test: error.opaque-type-parameter-at-a-concrete-aggregate-argument -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses P
	let p as P
	let s as String

	export function tag() returns Integer
		return s.count() as Integer
	end 'tag'

	static function create(p P, s String) returns Self
		return Self{p: p, s: s}
	end 'create'
end 'Inner'

type Outer uses T
	typealias I = Inner with T
	var n as Integer

	export function build(t T) returns Integer
		let i = I.create(t, s: t)
		return n + i.tag()
	end 'build'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with Integer

function main() returns ExitCode
	var o = O.create()
	print("{o.build(42)}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:13: argument type mismatch for 's': expected 'String', got 'type parameter': a generic body is compiled ONCE, against the declaration view, so a type parameter is an opaque machine word here and nothing has fixed what it points at — this is refused even where every 'with' in the program binds it to that very type. Declare the place at the type parameter too, or make the call from a concrete instantiation, where the type argument is known
```

<!-- test: error.opaque-type-parameter-at-a-concrete-aggregate-argument-of-a-plain-function -->
The same rule with no generic in the CALLEE at all — a plain `takeText(s String)` reached from inside a
generic body. It is the wider shape and the one that shows the hole was never about the callee's own
instance: the argument door alone decides it.
```maxon
typealias Integer = int(i64.min to i64.max)

function takeText(s String) returns Integer
	return s.count() as Integer
end 'takeText'

type Outer uses T
	var n as Integer

	export function build(t T) returns Integer
		return takeText(t) + n
	end 'build'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with Integer

function main() returns ExitCode
	var o = O.create()
	print("{o.build(42)}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 's': expected 'String', got 'type parameter': a generic body is compiled ONCE, against the declaration view, so a type parameter is an opaque machine word here and nothing has fixed what it points at — this is refused even where every 'with' in the program binds it to that very type. Declare the place at the type parameter too, or make the call from a concrete instantiation, where the type argument is known
```

<!-- test: error.opaque-type-parameter-into-a-concrete-aggregate-global -->
The ASSIGNMENT half of the same rule. The verdict is `TypeRules.checkDeclaredType`'s, so the global's own
door reports it without restating anything.
```maxon
typealias Integer = int(i64.min to i64.max)

var slot = "x"

type Outer uses T
	var n as Integer

	export function put(t T) returns Integer
		slot = t
		return n
	end 'put'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with Integer

function main() returns ExitCode
	var o = O.create()
	print("{o.put(42)}")
	print("{slot}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:3: cannot assign a value of type 'type parameter' to global 'slot', which holds 'String'
```

<!-- test: error.opaque-type-parameter-at-a-concrete-aggregate-argument-is-refused-at-a-matching-instantiation -->
⭐ **THE CONTROL, AND IT IS A REFUSAL.** The first case's program with its ONE instantiation changed to
`Outer with String`, so `T` is bound to exactly the type the formal declares. It COMPILED and printed `3`
before this rule existed. It is refused now, for the reason the section above gives: the body is checked once,
against a declaration view no `with` has reached, and the answer cannot be allowed to depend on which
instantiations the rest of the program happens to contain.
```maxon
typealias Integer = int(i64.min to i64.max)

type Inner uses P
	let p as P
	let s as String

	export function tag() returns Integer
		return s.count() as Integer
	end 'tag'

	static function create(p P, s String) returns Self
		return Self{p: p, s: s}
	end 'create'
end 'Inner'

type Outer uses T
	typealias I = Inner with T
	var n as Integer

	export function build(t T) returns Integer
		let i = I.create(t, s: t)
		return n + i.tag()
	end 'build'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with String

function main() returns ExitCode
	var o = O.create()
	print("{o.build("ab")}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:13: argument type mismatch for 's': expected 'String', got 'type parameter': a generic body is compiled ONCE, against the declaration view, so a type parameter is an opaque machine word here and nothing has fixed what it points at — this is refused even where every 'with' in the program binds it to that very type. Declare the place at the type parameter too, or make the call from a concrete instantiation, where the type argument is known
```

<!-- test: error.opaque-type-parameter-returned-at-a-concrete-aggregate-return -->
⚠ **THE RETURN QUADRANT WAS NEVER A HOLE, AND THIS CASE RECORDS WHICH REFUSAL SPEAKS — measured both ways.**
On the merge base this program is refused too, by the DESCRIPTOR door (`E2015`, "takes a reference to a
borrowed type-parameter value"): returning a `T` means taking a reference to it, and a `static function` has
no `self` to read the instantiation's descriptor from. The coercion rule now answers first, because the type
fault is the more fundamental one — following E2015's advice and moving the body to an instance method lands
on this same refusal — and because its cure ("declare the place at the type parameter too", i.e. `returns T`)
is the one that makes the program compile. It is pinned so that the ordering is a decision on the record
rather than a side effect nothing observes.
```maxon
typealias Integer = int(i64.min to i64.max)

type Outer uses T
	var n as Integer

	export static function pick(t T) returns String
		return t
	end 'pick'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with Integer

function main() returns ExitCode
	var o = O.create()
	print("{O.pick(42)}{o.n}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:3: Cannot return 'type parameter' from function declared to return 'String'
```

<!-- test: error.opaque-type-parameter-at-a-boxed-union-argument-explains-itself -->
A boxed UNION formal carries a NOMINAL identity, so this pairing is refused by the argument door's identity
arm and never reaches the tag rule at all — it was never part of the hole. It is pinned because the READER is
the same reader: one fault gets one explanation, so the identity arm quotes the same tail the tag arm does
whenever the ARGUMENT is the opaque one.
```maxon
typealias Integer = int(i64.min to i64.max)

union Message
	silent
	text(body String)
end 'Message'

function takeMsg(m Message) returns Integer
	return match m 'm'
		silent gives 1
		text(b) gives b.count() as Integer
	end 'm'
end 'takeMsg'

type Outer uses T
	var n as Integer

	export function build(t T) returns Integer
		return takeMsg(t) + n
	end 'build'

	static function create() returns Self
		return Self{n: 1}
	end 'create'
end 'Outer'

typealias O = Outer with Integer

function main() returns ExitCode
	var o = O.create()
	print("{o.build(42)}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:10: argument type mismatch for 'm': expected 'Message', got 'type parameter': a generic body is compiled ONCE, against the declaration view, so a type parameter is an opaque machine word here and nothing has fixed what it points at — this is refused even where every 'with' in the program binds it to that very type. Declare the place at the type parameter too, or make the call from a concrete instantiation, where the type argument is known
```
