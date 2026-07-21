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

### This slice: trivial type arguments only

This is the trivial base of the dictionary-passing design. A type argument must be a TRIVIAL
type — a scalar (`int`/`bool`/`float`/`ExitCode`, a ranged alias) or a struct whose fields are
all trivial. A MANAGED argument (a `String`, or a struct with a managed field) is rejected
until the layout descriptors of a later slice, because the shared trivial destructor drops
nothing and a managed argument would leak. A non-generic base, or the wrong number of
arguments, is likewise rejected.

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

<!-- test: error.managed-string-arg -->
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
	return 0
end 'main'
```
```maxoncstderr
error E2057: <fragment>:9:20: generic type argument 'String' of 'Box' is a managed type (a String, or a struct with a managed field), which is not yet supported (deferred to P1.6-B)
```

<!-- test: error.managed-struct-field-arg -->
```maxon
typealias Integer = int(i64.min to i64.max)
type Holder
	export var label as String
	export var n as Integer
end 'Holder'
type Box uses T
	export var value as T
	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'
typealias HolderBox = Box with Holder
function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2057: <fragment>:13:23: generic type argument 'Holder' of 'Box' is a managed type (a String, or a struct with a managed field), which is not yet supported (deferred to P1.6-B)
```

<!-- disabled-test: generic-nested-trivial -->
<!-- P1.6-B: a nested generic instance `Box with (Box with Integer)` is a managed struct box the trivial destructor does not drop; the parser rejects it (E2015) until the layout descriptors of P1.6-B. -->
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
