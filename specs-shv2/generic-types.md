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
typealias Integer = int(i64.min to i64.max)
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
typealias Integer = int(i64.min to i64.max)
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
typealias Integer = int(i64.min to i64.max)
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
typealias Integer = int(i64.min to i64.max)
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
typealias Integer = int(i64.min to i64.max)
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
<!-- targets: x64-windows, x64-linux -->
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
