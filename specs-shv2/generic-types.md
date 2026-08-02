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
	return p.getX() - 7
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
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
		let b = StrBox.create("{i}")
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
	return 0
end 'main'
```
```exitcode
0
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
		let outer = BoxBox.create(inner)
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
	return 0
end 'main'
```
```exitcode
0
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
error E3005: <fragment>:26:9: argument type mismatch for '_': expected 'Box_Leaf', got 'Box_Other'
```

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
error E3005: <fragment>:23:12: argument type mismatch for 'v': expected 'Box_Leaf', got 'Box_Other'
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
error E3005: <fragment>:24:12: argument type mismatch for 'v': expected 'Box_Leaf', got 'Box_Other'
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
error E3005: <fragment>:33:9: argument type mismatch for '_': expected 'Box_Leaf', got 'Box_Other'
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
error E3005: <fragment>:23:2: Cannot return 'Box_Other' from function declared to return 'Box_Leaf'
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
error E3005: <fragment>:24:2: cannot assign 'Box_Other' to variable 'b' of type 'Box_Leaf'
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
error E3005: <fragment>:25:15: cannot assign 'Box_Other' to variable 'Holder.b' of type 'Box_Leaf'
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
error E3005: <fragment>:19:9: argument type mismatch for '_': expected 'Box_Leaf', got 'Leaf'
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
error E3005: <fragment>:25:10: argument type mismatch for 'v': expected 'Pair_Box_Leaf_Box_Box_Leaf', got 'Pair_Box_Leaf_Leaf'
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
	return 0
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
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
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: alias-named-type-argument-cross-file-use-first -->
```maxon
// --- file: a_use.maxon
typealias N1 = Box with N0
function main() returns ExitCode
	let v0 = N0.create(S0.make("x"))
	let v1 = N1.create(v0)
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
	let b = ViaInline.create(N0.create(S0.make("b")))
	var swap = ViaAlias.create(N0.create(S0.make("c")))
	swap = ViaInline.create(N0.create(S0.make("d")))
	let q = makeIt()
	return takes(ViaInline.create(N0.create(S0.make("e"))))
end 'main'
```
```exitcode
0
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
	let e = DeepInline.create(N1.create(N0.create(S0.make("y"))))
	return takes(DeepInline.create(N1.create(N0.create(S0.make("z")))))
end 'main'
```
```exitcode
0
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
error E3005: <fragment>:19:9: argument type mismatch for '_': expected 'Box_S0', got 'Box'
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
error E3005: <fragment>:20:9: argument type mismatch for '_': expected 'Box_S0', got 'Box'
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
	return takesPlain(a.getTag())
end 'main'
```
```exitcode
0
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
error E3091: <fragment>:14:11: typealias 'A' forms a type cycle: its type arguments refer back to 'A'
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
error E3091: <fragment>:14:11: typealias 'A' forms a type cycle: its type arguments refer back to 'A'
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
error E3091: <fragment>:14:11: typealias 'A' forms a type cycle: its type arguments refer back to 'A'
```

<!-- test: error.field-access-on-builtin-array-base -->
`Array` and `Set` are BUILTIN generic bases: shv2 synthesizes their runtime records
rather than compiling `stdlib/Array.maxon`, so no `type` declaration carries a field
table and no field of an instance is reachable. The field is missing from the
COMPILER, not from the language — so this reports a not-implemented-yet construct,
never an unknown field. It used to be a `panic` with no diagnostic at all.

⚠ The field named here is deliberately NOT `managed`. That one spelling — the only
field `stdlib/Array.maxon` actually declares — is now SERVED, in both the chained
(`arr.managed.setLength(2)`) and the value (`f(arr.managed)`) forms, because it is
routed to the array dispatcher ahead of the field machinery (`arrayManagedFieldAt`,
BATCH2 slice 6). Every OTHER field of the synthesized record still lands here, which
is what this case pins.
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
error E2015: <fragment>:8:9: Unsupported: a field access on 'arr': `Array` is a BUILTIN whose runtime record shv2 synthesizes, not a `type` it compiles — shv2 reads no stdlib, so none of the fields `stdlib/Array.maxon` declares exist here yet. The field is missing from this compiler, not from the language; reach the contents through the methods
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
was printed. `Map` is the reachable spelling: it is a real stdlib generic that shv2
has not built.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntMap = Map with (Int, Int)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2055: <fragment>:3:20: Type 'Map' has no associated types
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
error E2015: <fragment>:13:2: Unsupported: a field access on 'a': `Array` is a BUILTIN whose runtime record shv2 synthesizes, not a `type` it compiles — shv2 reads no stdlib, so none of the fields `stdlib/Array.maxon` declares exist here yet. The field is missing from this compiler, not from the language; reach the contents through the methods
```

<!-- test: error.field-read-on-builtin-set-base-a-declaration-also-claims -->
The `Set` twin, and the READ direction — the two builtins are one rule, so a fix that
reached only the one it was measured on would leave the other silently handing out its
record. This one used to print the set record's live count as if it were the declared
field.
```maxon
typealias Int = int(i64.min to i64.max)

type Set uses T
	export var value as T
end 'Set'

typealias IntSet = Set with Int

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(7)
	return s.value
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:9: Unsupported: a field access on 's': `Set` is a BUILTIN whose runtime record shv2 synthesizes, not a `type` it compiles — shv2 reads no stdlib, so none of the fields `stdlib/Set.maxon` declares exist here yet. The field is missing from this compiler, not from the language; reach the contents through the methods
```

<!-- test: error.sizeof-of-undeclared-generic-base -->
The other query that assumes an instance's base exists, and it is NOT the field walks'
gate: `genericInstanceBoxSize` answers for a BUILTIN (the fixed 48-byte record) as happily
as for a declared base, and has nothing to say only when the base is neither. `sizeof`
folds at PARSE time, while the E2055 `checkGenericInstance` recorded against this `with` is
drained whole-program afterwards — so this used to die inside `ProgramSignatures.baseLayoutOf`'s
sibling with a stack trace and the real diagnostic never printed. `sizeof(Array with Int)`
still folds to 48; only an unknown base is refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntMap = Map with (Int, Int)

function main() returns ExitCode
	return sizeof(IntMap) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:9: Unsupported: sizeof of a type that instantiates 'Map', which no file declares as a generic `type`, so it has no box to size — the `with` that names it is the error
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
Both float spellings again on a BUILTIN base, where the panic was equally reachable (`Array with Real`
died in the same emitter): the rule is about the ARGUMENT, so `Array` reaches it through the same
`parseGenericArgNode` a declared generic does. Only the FIRST offending argument in a file is shown
here because each alias is its own declaration — two declarations, two diagnostics.
```maxon
typealias Real = float(f64.min to f64.max)
typealias RealArray = Array with Real
typealias FloatArray = Array with float
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2062: <fragment>:3:34: Cannot use 'Real' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
error E2062: <fragment>:4:35: Cannot use 'float' as a type argument: a float type argument is not supported yet. A type parameter is an opaque 8-byte general-purpose slot under shv2's dictionary-passing, and a float value travels in a floating-point register, so it has no way through
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
	let lb = LeafBox.create(Leaf.make("a"))
	let nested = LeafBoxBox.create(LeafBox.create(Leaf.make("b")))
	return 0
end 'main'
```
```exitcode
0
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
error E3009: <fragment>:11:10: argument 'v': cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
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
error E3005: <fragment>:10:10: argument type mismatch for 'v': expected 'String', got 'float'
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
error E3005: <fragment>:27:10: argument type mismatch for 'v': expected 'Leaf', got 'IntWrapper.Idx'
```
