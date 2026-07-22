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
