---
feature: array-conditional-conformance-withheld
status: experimental
keywords: [array, hashable, equatable, conditional, extension, where, map, set, diagnostics]
category: type-system
---

# `Array`'s `Hashable`/`Equatable` is CONDITIONAL — what a withheld one is refused with

## Documentation

`Array` conforms to `Hashable` and `Equatable` only `where Element is Hashable and Equatable`
(`stdlib/Array.maxon`, and `specs-shv2/array-hashable.md` for what the conformance DOES). An array whose
element does not satisfy that clause has no `hash`, no `equals` and no `==`/`!=`, and cannot be a `Map` or
`Set` key.

This file is the WITHHELD half. It lives here rather than beside the positive cases because
`array-hashable.md` is ported byte-identical from `/specs` and may not gain an shv2-authored case — the
same reason `builtin-member-rosters.md` gives for `Character`'s roster.

**One clause, one walk, three doors.** The element's conformance is decided by a single predicate
(`ProgramSignatures.arrayElementConstraintCheck`) under a single list (`Array`'s own intrinsic conformance
row), and three doors consume its verdict:

- the METHOD surface — `arr.hash()` / `a.equals(b)` — refused with **E4006**, the sentence a user's own
  conditional extension is withheld with, naming the unmet interface and the element that failed it;
- the `==` / `!=` OPERATOR, which IS `Array.equals`, so it is refused with the same sentence at the
  operator's own span;
- the hash-table KEY gate, which admits `Array` as a key type and must therefore not report a withheld
  array as a key type it does not serve yet. Its refusal names the clause and the element instead.

A copy of the walk at any one of those doors is a silent wrong answer in either direction: `a == b` refused
while the same array is still stamped with `__witness_Array.*` and admitted as a key, or the reverse.
Neither is a compile error and neither shows up as a failing test, which is why the cases below hold all
three doors to one clause.

## Tests

<!-- test: error.hash-is-withheld-for-a-non-conforming-element -->
### `hash()` on an array of a non-conforming element names the unmet interface
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque

function main() returns ExitCode
	var a = OpaqueArr.create()
	a.push(Opaque.create(1))
	let h = a.hash()
	return h
end 'main'
```
```maxoncstderr
error E4006: <fragment>:17:12: Type 'Array' has no field named 'hash' ('hash' is available as a conditional extension where Element is Hashable and Equatable, but 'Opaque' does not implement 'Hashable')
```

<!-- test: error.equality-operator-is-withheld-for-a-non-conforming-element -->
### `==` on two such arrays is refused as the `equals` it dispatches to
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque

function main() returns ExitCode
	var a = OpaqueArr.create()
	a.push(Opaque.create(1))
	var b = OpaqueArr.create()
	b.push(Opaque.create(1))
	if a == b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E4006: <fragment>:19:7: Type 'Array' has no field named 'equals' ('equals' is available as a conditional extension where Element is Hashable and Equatable, but 'Opaque' does not implement 'Hashable')
```

<!-- test: error.a-map-key-array-is-refused-for-its-element -->
### A `Map` key array is refused for its ELEMENT, not as an unserved key type
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque
typealias OpaqueArrMap = Map with (OpaqueArr, Val)

function main() returns ExitCode
	var m = OpaqueArrMap.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:16:10: Unsupported: `Map` hashes and compares its keys through their `Hashable`/`Equatable` witnesses, and `Array` supplies those only as a conditional extension (where Element is Hashable and Equatable) — this key's element 'Opaque' does not implement 'Hashable'
```

<!-- test: error.a-set-key-array-spelled-inline-is-refused-the-same-way -->
### The same refusal reaches a `Set` and an inline `Array with E` spelling
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArrSet = Set with (Array with Opaque)

function main() returns ExitCode
	var s = OpaqueArrSet.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: `Set` hashes and compares its keys through their `Hashable`/`Equatable` witnesses, and `Array` supplies those only as a conditional extension (where Element is Hashable and Equatable) — this key's element 'Opaque' does not implement 'Hashable'
```

<!-- test: error.a-key-type-nothing-conforms-for-still-reads-as-a-later-slice -->
### A key that is not an array at all keeps the roster sentence
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueMap = Map with (Opaque, Val)

function main() returns ExitCode
	var m = OpaqueMap.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:10: Unsupported: `Map` hashes and compares its keys through their `Hashable`/`Equatable` witnesses, so a key must be one of `int`, `String`, `Character`, `Array` — a 'Opaque' key is a later slice
```

<!-- test: error.an-array-of-non-conforming-arrays-names-the-inner-array -->
### A nested array whose INNER array is withheld reports the inner spelling
```maxon
typealias Val = int(i64.min to i64.max)

type Opaque
	export var x as Val

	static function create(x Val) returns Self
		return Self{x: x}
	end 'create'
end 'Opaque'

typealias OpaqueArr = Array with Opaque
typealias OpaqueArrArr = Array with OpaqueArr

function main() returns ExitCode
	var outer = OpaqueArrArr.create()
	let h = outer.hash()
	return h
end 'main'
```
```maxoncstderr
error E4006: <fragment>:17:16: Type 'Array' has no field named 'hash' ('hash' is available as a conditional extension where Element is Hashable and Equatable, but 'OpaqueArr' does not implement 'Hashable')
```

<!-- test: an-array-of-arrays-decides-the-inner-conformance-first -->
### A nested array's conformance is decided element-first
```maxon
typealias Val = int(i64.min to i64.max)
typealias IntArr = Array with Val
typealias IntArrArr = Array with IntArr

function main() returns ExitCode
	var outer = IntArrArr.create()
	var inner = IntArr.create()
	inner.push(3)
	outer.push(inner)
	var other = IntArrArr.create()
	var innerOther = IntArr.create()
	innerOther.push(3)
	other.push(innerOther)
	let h = outer.hash()
	if h == 0 'hashIsZero'
		return 1
	end 'hashIsZero'
	return 42
end 'main'
```
```exitcode
42
```
