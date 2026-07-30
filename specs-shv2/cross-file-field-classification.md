---
feature: cross-file-field-classification
status: stable
keywords: [cross-file, typealias, struct, field, managed, temporary, interner, ownership]
category: ownership
---

# Cross-File Field Classification

## Documentation

"Is this struct field MANAGED?" is one question with one answer, and the file a program spells the
field's type in may not change it. A field typed by a `typealias` declared in a **sibling file** is
the same scalar it would be in one file; a `String`, struct, `Array` or payload-bearing `union`
field is the same managed heap the drop cascade frees.

Two mechanisms read that answer and they must agree:

- the struct's `__destruct_<T>` **drop cascade**, which frees every managed field, and
- `postfix-member-walk.md`'s refusal of a **managed field read out of a TEMPORARY**, whose whole
  soundness argument is that "the box frees it at drop" and "the read borrows it" are one answer.

### Why the file boundary could change the answer at all

A struct layout's field types carry **interned NAME ids**, and the id is meaningless without the
interner it was minted against — a type crossing into the signature index must carry the interner
named alongside it (`classifyUnionPayload` takes one; `foreignNameOf` panics without one). A field
type handed back to the parser has been **re-interned into the parser's own file-local table**, so
its id is a *different number* from the one the whole-program index files that name under.

In a **single-file** program the two tables are folded in the same order, so every id coincides and
a query that resolved a file-local id against the program-wide table happened to be right. Across
files they diverge, and the resolution then answers about **whatever unrelated type name happens to
hold that number** — so the classification followed the interning order rather than the type.

Both directions were measured, and both are wrong answers rather than mere over-rejection:

- a **scalar** cross-file alias field (and a payload-free `enum` field) classified MANAGED, so a
  legal read off a temporary was refused with `E2015`;
- a **boxed-union** field classified SCALAR, so the read was **admitted** — the exact use-after-free
  `postfix-member-walk.md` exists to refuse. The accepted program hung.

The cases below pin both directions, and pin the controls that prove the fix reaches the
classification rather than the guard.

## Tests

<!-- test: crossfile-alias-scalar-field-off-a-temporary -->
⭐ **THE HEADLINE.** `n` is `int(0 to 1000)`, declared in the sibling file that declares `Gate`. A
scalar field is COPIED, not borrowed, so reading it off a temporary is legal — and the same program
in one file (below) has always run.
```maxon
// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Gate{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Gate.make(v + v)
	end 'twice'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: same-file-alias-scalar-field-off-a-temporary -->
⚠ **CONTROL (b).** The identical program in ONE file. It already ran, which is what localised the
defect to the file boundary rather than to the alias or to the temporary.
```maxon
typealias Num = int(0 to 1000)

type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Gate{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Gate.make(v + v)
	end 'twice'
end 'Gate'

function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-builtin-typed-field-off-a-temporary -->
⚠ **CONTROL (c).** The same cross-file shape with the field typed by the BUILTIN name `ExitCode`
instead of an alias. A builtin carries its own type tag and never needs a name resolved, so it was
never affected — which is what proves the alias RESOLUTION is the part that broke.
```maxon
// --- file: gate.maxon
export type Gate
	export var n as ExitCode

	export static function make(v ExitCode) returns Gate
		return Gate{n: v}
	end 'make'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	return Gate.make(42).n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-alias-scalar-field-off-a-bound-name -->
⚠ **CONTROL (d).** The same cross-file alias field read off a BOUND NAME. A name's box outlives the
statement, so the guard never asks the classifier at all — which is what proves the temporary is the
half that makes the misclassification visible, not the half that is wrong.
```maxon
// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Gate{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Gate.make(v + v)
	end 'twice'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	let g = Gate.twice(21)
	return g.n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-alias-scalar-field-off-a-temporary-with-shifted-interner-ids -->
⭐ **THE ANSWER MAY NOT DEPEND ON INTERNING ORDER.** The same program as the headline, with three
unrelated aliases declared ahead of the read so the reading file's own name table hands `Num` a
different id. Under the defect this one COMPILED while the headline did not — the classification
followed the id, not the type. Both must run.
```maxon
// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function make(v Num) returns Gate
		return Gate{n: v}
	end 'make'

	export static function twice(v Num) returns Gate
		return Gate.make(v + v)
	end 'twice'
end 'Gate'

// --- file: main.maxon
typealias P1 = int(0 to 5)
typealias P2 = int(0 to 5)
typealias P3 = int(0 to 5)

function pad(a P1, b P2, c P3) returns P1
	return a
end 'pad'

function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-payload-free-enum-field-off-a-temporary -->
A payload-free `enum` field is a SCALAR — its value IS its i64 tag, it owns no heap, and the drop
cascade skips it. Declared in a sibling file it was refused exactly as the alias was; the same
program in one file (below) ran.
```maxon
// --- file: color.maxon
export enum Color
	red
	green
end 'Color'

export type Gate
	export var c as Color

	export static function make() returns Gate
		return Gate{c: Color.green}
	end 'make'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	let c = Gate.make().c
	match c 'which'
		red then return 1
		green then return 42
	end 'which'
end 'main'
```
```exitcode
42
```

<!-- test: same-file-payload-free-enum-field-off-a-temporary -->
⚠ The enum control, in ONE file.
```maxon
enum Color
	red
	green
end 'Color'

type Gate
	export var c as Color

	export static function make() returns Gate
		return Gate{c: Color.green}
	end 'make'
end 'Gate'

function main() returns ExitCode
	let c = Gate.make().c
	match c 'which'
		red then return 1
		green then return 42
	end 'which'
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-alias-scalar-field-three-files-deep -->
The alias, the struct and the read in THREE different files. The name still crosses by NAME, so the
answer is the same one the headline gives.
```maxon
// --- file: num.maxon
typealias Num = int(0 to 1000)

export function double(v Num) returns Num
	return v + v
end 'double'

// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function twice(v Num) returns Gate
		return Gate{n: double(v)}
	end 'twice'
end 'Gate'

// --- file: main.maxon
function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-same-alias-name-declared-in-two-files -->
A `typealias` is FILE-SCOPED, so two files may each declare `Num`. The field's type is the one
visible where the STRUCT was declared, and a temporary read still copies it.
```maxon
// --- file: gate.maxon
typealias Num = int(0 to 1000)

export type Gate
	export var n as Num

	export static function twice(v Num) returns Gate
		return Gate{n: v + v}
	end 'twice'
end 'Gate'

// --- file: main.maxon
typealias Num = int(0 to 100)

function half(v Num) returns Num
	return v / 2
end 'half'

function main() returns ExitCode
	return Gate.twice(21).n
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-float-alias-field-off-a-temporary -->
A ranged FLOAT alias field. It is the second `named` kind whose VALUE is a scalar, and the raw
layout column the classifier reads still holds the bare name — so it travels the same cascade the
int alias does and must reach the same `false`.
```maxon
// --- file: scale.maxon
typealias Weight = float(0.0 to 1000.0)

export type Scale
	export var w as Weight

	export static function make() returns Scale
		return Scale{w: 42.0}
	end 'make'
end 'Scale'

// --- file: main.maxon
function main() returns ExitCode
	return Scale.make().w as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: crossfile-function-alias-field-off-a-temporary -->
A FUNCTION-typed field is a code pointer — an i64 stored inline that owns no heap, so the cascade
skips it and the read copies it. Declared in a sibling file it is the same scalar.
```maxon
// --- file: holder.maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

export function inc(v Integer) returns Integer
	return v + 1
end 'inc'

export type Holder
	export var op as UnaryOp

	export static function make() returns Holder
		return Holder{op: inc}
	end 'make'
end 'Holder'

// --- file: main.maxon
function main() returns ExitCode
	let f = Holder.make().op
	return f(41)
end 'main'
```
```exitcode
42
```

<!-- test: error.crossfile-string-field-read-out-of-a-temporary -->
⭐ **THE GUARD MUST STILL FIRE.** A `String` field declared in a sibling file, read off a call
result, is the use-after-free `postfix-member-walk.md` refuses — and a fix that reached the guard
rather than the classifier would let it through.
```maxon
// --- file: box.maxon
export type Box
	export var name as String

	export static function make(n String) returns Box
		return Box{name: n}
	end 'make'
end 'Box'

// --- file: main.maxon
function main() returns ExitCode
	let s = Box.make("hello").name
	return s.byteLength()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:28: Unsupported: reading the managed field 'name' out of a TEMPORARY `Box` — a field read is a BORROW and the value the `.` is applied to is owned by this statement alone, so the heap this would hand back is freed at the statement's end (measured: a use-after-free, silent). Bind the receiver to a name first and read the field off THAT, which borrows from a box outliving the read; keeping a temporary alive for a borrow taken out of it is the ownership rung's. A SCALAR field is copied rather than borrowed and needs none of this
```

<!-- test: error.crossfile-struct-field-read-out-of-a-temporary -->
⭐ **THE GUARD MUST STILL FIRE**, for the shape that returned the freed-memory fill byte
`0x3F3F3F3F` as the program's answer when it was let through in one file.
```maxon
// --- file: inner.maxon
typealias Wide = int(i64.min to i64.max)

export type Inner
	export var v as Wide

	export static function make(v Wide) returns Inner
		return Inner{v: v}
	end 'make'

	export function get() returns Wide
		return self.v
	end 'get'
end 'Inner'

// --- file: outer.maxon
export type Outer
	export var inner as Inner

	export static function make(i Inner) returns Outer
		return Outer{inner: i}
	end 'make'
end 'Outer'

// --- file: main.maxon
function main() returns ExitCode
	let i = Outer.make(Inner.make(3)).inner
	return i.get()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:28:36: Unsupported: reading the managed field 'inner' out of a TEMPORARY `Outer` — a field read is a BORROW and the value the `.` is applied to is owned by this statement alone, so the heap this would hand back is freed at the statement's end (measured: a use-after-free, silent). Bind the receiver to a name first and read the field off THAT, which borrows from a box outliving the read; keeping a temporary alive for a borrow taken out of it is the ownership rung's. A SCALAR field is copied rather than borrowed and needs none of this
```

<!-- test: error.crossfile-boxed-union-field-read-out-of-a-temporary -->
⭐⭐ **THE CASE THAT WAS ADMITTED, and the reason this is a use-after-free rung and not a
false-rejection one.** A payload-bearing `union` field is a managed heap box, and across files it
classified SCALAR — so this program COMPILED and then hung on the freed box. The four padding
aliases are what land the reading file's `Shape` id on a scalar name in the program-wide table:
measured, the refusal held at 0–3 padding aliases and the program was ADMITTED from 4 upwards. A
classification that changes at the fourth unrelated `typealias` is the defect stated as a test.
```maxon
// --- file: shape.maxon
typealias Integer = int(i64.min to i64.max)

export type Body
	export var mass as Integer

	export static function create(mass Integer) returns Body
		return Body{mass: mass}
	end 'create'
end 'Body'

export union Shape
	empty
	solid(body Body)
end 'Shape'

export type Holder
	export var shape as Shape

	export static function make() returns Holder
		return Holder{shape: Shape.solid(Body.create(5))}
	end 'make'
end 'Holder'

// --- file: main.maxon
typealias P1 = int(0 to 5)
typealias P2 = int(0 to 5)
typealias P3 = int(0 to 5)
typealias P4 = int(0 to 5)

function pad(a P1, b P2, c P3, d P4) returns P1
	return a
end 'pad'

function main() returns ExitCode
	let s = Holder.make().shape
	match s 'check'
		empty then return 0
		solid(b) then return b.mass
	end 'check'
end 'main'
```
```maxoncstderr
error E2015: <fragment>:37:24: Unsupported: reading the managed field 'shape' out of a TEMPORARY `Holder` — a field read is a BORROW and the value the `.` is applied to is owned by this statement alone, so the heap this would hand back is freed at the statement's end (measured: a use-after-free, silent). Bind the receiver to a name first and read the field off THAT, which borrows from a box outliving the read; keeping a temporary alive for a borrow taken out of it is the ownership rung's. A SCALAR field is copied rather than borrowed and needs none of this
```

<!-- test: error.crossfile-array-field-read-out-of-a-temporary -->
⭐ **THE GUARD MUST STILL FIRE** for an `Array` field — a generic-instance alias declared in a
sibling file, whose record and buffer the box owns.
```maxon
// --- file: bag.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Bag
	export var items as IntArray

	export static function make() returns Bag
		var xs = IntArray.create()
		xs.push(1)
		return Bag{items: xs}
	end 'make'
end 'Bag'

// --- file: main.maxon
function main() returns ExitCode
	let xs = Bag.make().items
	return try xs.get(0) otherwise 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:18:22: Unsupported: reading the managed field 'items' out of a TEMPORARY `Bag` — a field read is a BORROW and the value the `.` is applied to is owned by this statement alone, so the heap this would hand back is freed at the statement's end (measured: a use-after-free, silent). Bind the receiver to a name first and read the field off THAT, which borrows from a box outliving the read; keeping a temporary alive for a borrow taken out of it is the ownership rung's. A SCALAR field is copied rather than borrowed and needs none of this
```

<!-- test: error.crossfile-array-element-alias-in-a-third-file -->
⭐ **THE GUARD MUST STILL FIRE** when the `Array` instance alias and its ELEMENT alias are declared
in different files again — three files between the element's range and the read.
```maxon
// --- file: elem.maxon
typealias Integer = int(i64.min to i64.max)

export function seed() returns Integer
	return 7
end 'seed'

// --- file: bag.maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

export type Bag
	export var items as IntArray

	export static function make() returns Bag
		var xs = IntArray.create()
		xs.push(seed())
		return Bag{items: xs}
	end 'make'
end 'Bag'

// --- file: main.maxon
function main() returns ExitCode
	let xs = Bag.make().items
	return try xs.get(0) otherwise 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:25:22: Unsupported: reading the managed field 'items' out of a TEMPORARY `Bag` — a field read is a BORROW and the value the `.` is applied to is owned by this statement alone, so the heap this would hand back is freed at the statement's end (measured: a use-after-free, silent). Bind the receiver to a name first and read the field off THAT, which borrows from a box outliving the read; keeping a temporary alive for a borrow taken out of it is the ownership rung's. A SCALAR field is copied rather than borrowed and needs none of this
```
