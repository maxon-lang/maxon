---
feature: ranged-element-invariance
status: stable
keywords: [generics, typealias, ranged-typealias, element-type, invariance, type-safety, array]
category: types
---

# A Generic's RANGED Element Type Is Part of Its Type

## Documentation

An `Array`'s element must be a typealias, so two array types are always two named aliases:

```
typealias Narrow    = int(0 to 16)
typealias NarrowCol = Array with Narrow

typealias Wide      = int(0 to u64.max)
typealias WideCol   = Array with Wide
```

`NarrowCol` and `WideCol` are **different types**, and passing one where the other is expected must be
rejected. Two things distinguish them, and both matter:

1. **Their invariants.** A `Narrow` promises `0 to 16`. That promise is what a ranged type is *for*.
2. **Their storage width.** `Narrow` occupies ONE byte per element; `Wide` occupies EIGHT.

Compatibility used to be decided by normalizing a ranged element to its BASE type before comparing —
and the base is the *declared* one, so `int(0 to 16)` and `int(0 to u64.max)` **both declare base
`int`** and compared EQUAL. A `NarrowCol` could therefore be passed to a `WideCol` parameter with no
diagnostic.

The consequence was silent corruption of the range invariant. A store through the wide parameter is
range-checked against `Wide` — which permits anything — and then truncated into the one-byte element:
writing `300` read back as **44** (300 mod 256), leaving a value *outside* the range its own type
declares. Memory stays safe (element width travels with the value, so the stride is right and
neighbours are untouched); what breaks is the type's guarantee.

This is the same root cause as `array-clone-element-size`, where the range was thrown away while
resolving a `Self` return and `Array.clone()` read 8 bytes at a 1-byte stride. That was fixed on the
`Self`-returning-call path; this is the same class on the ARGUMENT-PASSING path.

A ranged alias is a nominal type: two aliases spelling the *same* range are two types, and a value
of one reaches the other only through `as` (`nominal-typealias.md`). Two containers over two such
aliases are therefore two instances as well, whatever their widths — the container half of that rule
is `nominal-generic-alias.md`'s.

## Tests

<!-- test: narrow-element-rejected-where-wide-expected -->
Passing an `Array` whose element is a NARROW ranged alias to a parameter expecting a WIDE one must be
a compile error. Before, this compiled and silently truncated.
```maxon
typealias Narrow = int(0 to 16)
typealias NarrowCol = Array with Narrow
typealias Wide = int(0 to u64.max)
typealias WideCol = Array with Wide

function writeThrough(col WideCol, value Wide)
	try col.set(0, value: value) otherwise panic("set")
end 'writeThrough'

function main() returns ExitCode
	var narrow = NarrowCol.create()
	narrow.push(1)
	writeThrough(narrow, value: 300)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-element-invariance/narrow-element-rejected-where-wide-expected.test:14:2: argument type mismatch for 'col': expected 'WideCol', got 'NarrowCol'
```

<!-- test: wide-element-rejected-where-narrow-expected -->
The rejection is symmetric — neither direction is a subtype of the other.
```maxon
typealias Narrow = int(0 to 16)
typealias NarrowCol = Array with Narrow
typealias Wide = int(0 to u64.max)
typealias WideCol = Array with Wide

function readNarrow(col NarrowCol) returns Narrow
	return try col.get(0) otherwise panic("get")
end 'readNarrow'

function main() returns ExitCode
	var wide = WideCol.create()
	wide.push(7)
	return readNarrow(wide)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-element-invariance/wide-element-rejected-where-narrow-expected.test:14:9: argument type mismatch for 'col': expected 'NarrowCol', got 'WideCol'
```

<!-- test: matching-ranged-element-still-works -->
The ordinary case — element aliases that match exactly — is unaffected.
```maxon
typealias Small = int(0 to 100)
typealias SmallCol = Array with Small

function bump(col SmallCol, by Small)
	let old = try col.get(0) otherwise panic("get")
	try col.set(0, value: old + by) otherwise panic("set")
end 'bump'

function main() returns ExitCode
	var a = SmallCol.create()
	a.push(40)
	bump(a, by: 2)
	return try a.get(0) otherwise panic("get")
end 'main'
```
```exitcode
42
```
