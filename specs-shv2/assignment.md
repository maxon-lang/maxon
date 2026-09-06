---
feature: assignment
status: selfhosted
keywords: [assignment, equals, mutation, reassignment, var]
category: statements
milestone: M4b
---

# Assignment Statement

## Documentation

The assignment operator `=` updates the value of a mutable (`var`) binding:

```maxon
variable = expression
```

M4b adds reassignment on top of M2's `let`/`var` bindings. A `var` may be
reassigned any number of times; each write rebinds the variable's current value,
so a later reference sees the new value (`x = x + 2` reads the old `x`, then
rebinds it to the sum). A `let` binding is immutable — assigning to it is E2013.

Lowering: the value model is **on-the-fly SSA**. A reassignment is a rebinding of
the variable's current ValueId (no stack slot, no store op), so straight-line code
needs no phis. Where a `var` is reassigned across a control-flow merge — the header
and exit of a `while` loop — the parser inserts block-arg **phis**, which the
Std-tier phi-elimination pass resolves into coalesced values plus register moves
before the backend (see `specs-shv2/while-loops.md`).

## Tests

The M4b slice of `specs/assignment.md`: straight-line reassignment, chained
reassignment across two variables, and the canonical accumulator loop. All three
fit the placeholder register allocator's pool.

<!-- test: basic-assignment -->
```maxon
function main() returns ExitCode
	var x = 3
	x = x + 2
	return x
end 'main'
```
```exitcode
5
```

<!-- test: multiple-assignments -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = 20
	x = y
	y = 30
	return x + y
end 'main'
```
```exitcode
50
```

<!-- test: assignment-in-loop -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 5 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
15
```

<!-- test: assign-to-let-error -->
Assigning to a `let` binding is rejected — only `var` bindings are mutable.
```maxon
function main() returns ExitCode
	let x = 3
	x = 5
	return x
end 'main'
```
```maxoncstderr
error E2013: <fragment>:4:2: cannot assign to immutable variable: 'x'
```

<!-- test: error.retype-struct-to-other-struct-errors -->
Two different structs are two different types, even though both are "a struct". A kind alone cannot
tell them apart, so a bare `structRef == structRef` tag check passes this retype and the overwritten
box is later dropped under the binding's declared destructor — a wrong answer in the bootstrap
(E9001 in lowering) and a memory-safety hole once the field is managed (OPEN #54). Struct identity is
the interned name, exact. (Ported from `specs/assignment.md`; shv2's `assignTypeMismatch` wording.)
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Other
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Other'

function main() returns ExitCode
	var p = Point.create(1)
	p = Other.create(2)
	print("{p.x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-to-other-struct-errors.test:23:2: cannot assign a value of type 'Other' to variable 'p', which holds 'Point'
```

<!-- test: error.reassign-wrong-struct -->
The same retype with MANAGED (String-field) structs — the case OPEN #54 was filed for. The scalar tag
check accepts it; the overwrite drops the old `BoxA` and the scope exit drops `a` under `BoxA`'s
destructor while it holds a `BoxB` box. Rejected at the reassignment, before either drop.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

type BoxB
	export var t as String

	static function create(x Integer) returns Self
		return Self{t: "w{x}"}
	end 'create'
end 'BoxB'

function main() returns ExitCode
	var a = BoxA.create(1)
	a = BoxB.create(2)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.reassign-wrong-struct.test:22:2: cannot assign a value of type 'BoxB' to variable 'a', which holds 'BoxA'
```

<!-- test: error.reassign-union-as-scalar -->
Reassigning a scalar-typed var to a boxed union (OPEN 59): the var declares Integer (a scalar) but the
value is a boxed Holder union. The tag check accepts named-vs-named (the ValueTypeTag.named overload is
a boxed union value AND a ranged-int alias), so the union box would be dropped under the scalar var's
absent destructor at scope exit, a leak. The aggregate-name check runs after the tag check and rejects
a real aggregate assigned where a scalar is declared.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

union Holder
	holds(inner BoxA)
end 'Holder'

function main() returns ExitCode
	var n = 5
	n = Holder.holds(BoxA.create(9))
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.reassign-union-as-scalar.test:18:2: cannot assign a value of type 'Holder' to variable 'n', which holds 'int'
```

<!-- test: error.wrong-enum-into-an-enum-field -->
The SCALAR field store was the one coercion door of the eight that never asked `aggregatesConflict`, so a
field declared `Color` accepted a `Shade` — MEASURED 2026-08-06 (BATCH32): this program COMPILED and printed
`stored`, where the bootstrap oracle refuses it, and `h.c.ordinal` would then read `Shade.light`'s tag under
`Color`'s name. It is `error.retype-enum-to-other-enum-errors` one slot over, which is exactly the pairing
`error.retype-enum-field-errors` states in `specs/assignment.md` — that spec's own cases stay disabled here
only because canonical spells the sentence differently, and a wording gap is not a reason to leave the hole
open. shv2-authored, in shv2's `assignTypeMismatch` wording.
```maxon

enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

type Holder
	export var c as Color

	static function create() returns Self
		return Self{c: Color.red}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.c = Shade.light
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.wrong-enum-into-an-enum-field.test:23:4: cannot assign a value of type 'Shade' to field 'c' of 'Holder', which holds 'Color'
```

<!-- test: error.wrong-enum-in-a-struct-literal-field -->
The STRUCT-LITERAL door reaches the same slot under the same declared type through `requireFieldType`, so
it shared the hole and is fixed by the same check rather than by a second copy of it. MEASURED 2026-08-06
(BATCH32): before the fix this compiled and stored a `Shade` in a `Color` slot with no diagnostic.
shv2-authored, the literal twin of the case above.
```maxon

enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

type Holder
	export var c as Color

	static function create() returns Self
		return Self{c: Shade.light}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.wrong-enum-in-a-struct-literal-field.test:17:15: cannot assign a value of type 'Shade' to field 'c' of 'Holder', which holds 'Color'
```

### The type rule — rejections

<!-- test: error.retype-local-errors -->
A `var`'s type is its declared type. This program compiled CLEAN and printed `hello` — the
assignment was silently accepted, because a local's readers forward the assigned value and so
never consult the declared type at all. The variable appeared to re-infer; nothing re-inferred it.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var x = 0 as Integer
	x = "hello"
	print("{x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-local-errors.test:7:2: cannot assign a value of type 'String' to variable 'x', which holds 'int'
```

<!-- test: error.retype-global-errors -->
The SAME store to a GLOBAL, and the same error — this test and the local one above are the pair
that pins the two paths together. They used to disagree: a global cannot forward the assigned
value, it must go through a typed load using the declared kind, so this program compiled clean
and printed a raw heap pointer (`140696866942976`) with exit 0. One rule, one answer, either way.
```maxon

typealias Integer = int(i64.min to i64.max)

var g = 0 as Integer

function main() returns ExitCode
	g = "hello"
	print("{g}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-global-errors.test:8:2: cannot assign a value of type 'String' to global 'g', which holds 'int'
```

<!-- test: error.retype-conditional-errors -->
A retype the program never executes is still a type error: the rule is about the ASSIGNMENT, not
about whether control reaches it. This branch is dead (`c` is `false`), and the program still
panicked with a nil pointer — `z` was typed String lexically while holding the int `0`.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let c = false
	var z = 0 as Integer
	if c 'maybe'
		z = "hello"
	end 'maybe'
	print("{z}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-conditional-errors.test:9:3: cannot assign a value of type 'String' to variable 'z', which holds 'int'
```

<!-- test: error.retype-in-loop-errors -->
The same inside a loop body, where the variable is legitimately reassigned on every iteration:
being reassignable is what `var` grants, and it is not permission to change type.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var acc = 0 as Integer
	var i = 0 as Integer
	while i < 3 'loop'
		acc = "hello"
		i = i + 1
	end 'loop'
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-in-loop-errors.test:9:3: cannot assign a value of type 'String' to variable 'acc', which holds 'int'
```

<!-- test: error.retype-struct-to-int-errors -->
A struct into an `int`. Unchecked, this reached the arithmetic below and died as
`E9001: Unhandled cast combination: Struct -> Integer` — an INTERNAL error with a .NET stack
trace, naming no source position, for a plain type error. The check fires at the assignment,
which is both where the defect is and where the user can see it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var n = 0 as Integer
	n = Point.create(1)
	let sum = n + 1
	print("{sum}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-to-int-errors.test:15:2: cannot assign a value of type 'struct' to variable 'n', which holds 'int'
```

<!-- test: error.retype-struct-field-errors -->
A FIELD is a place with a declared type too, and the rule does not change because the store lands
in a struct instead of a frame slot. This site carried only the WIDENING half of the rule — a
mismatch it could not widen was stored anyway — so this program compiled clean and printed a raw
heap pointer (`1827332661328`) with exit 0, exactly as the global did.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(1)
	p.x = "hello"
	print("{p.x}")
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-struct-field-errors.test:15:4: cannot assign a value of type 'String' to field 'x' of 'Point', which holds 'int'
```

### The type rule — what stays legal

<!-- test: assign-ranged-typealias -->
A literal into a ranged typealias — the most common assignment there is. The declared type is the
alias; a literal in range is the same type, not a retype.

⭐ **AND A REBIND IS NOT A RANGE-CHECK DOOR.** `s = s + 20` stores a computed value into a narrowly
ranged binding and is guarded by nothing: a binding NAMES a value, where a door is a declared SLOT a
value is stored INTO, and `completeLocalRebind` records no site. The result stays in range here, so
this case cannot tell the two apart on its own — `a-rebind-out-of-range-is-caught-at-the-return-door`
below is the half that can, and the pair is what pins the exemption as a decision.
```maxon

typealias Score = int(0 to 100)

function main() returns ExitCode
	var s = 0 as Score
	s = 5
	s = s + 20
	return s
end 'main'
```
```exitcode
25
```

<!-- test: a-rebind-out-of-range-is-caught-at-the-return-door -->
⭐ The other half. `s` leaves `Score` inside `bump` and NOTHING fires — the print proves the binding is
holding 200 — and the check lands at the `return`, the first door the value reaches. A rebind that
guarded would have panicked one line earlier and printed nothing.
```maxon

typealias Score = int(0 to 100)

function bump(start Score) returns Score
	var s = start
	s = s + 200
	print("s={s}\n")
	return s
end 'bump'

function main() returns ExitCode
	let r = bump(0)
	print("unreachable r={r}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
s=200
```
```exitcode
1
```
```stderr
panic at a-rebind-out-of-range-is-caught-at-the-return-door.test:9: Range check failed: value outside typealias 'Score'
Stack trace:
  in bump
  in main
  in mrt_start
```

<!-- test: assign-widening-byte-to-int -->
A `Byte` reaches an `Integer` variable through `as`, and the cast MINTS an `Integer`: after the rebind
`i` denotes `Integer`, not `b`'s `Byte`, so `i as ExitCode` is a cast between two types and not E3010.
```maxon

typealias Byte = int(0 to u8.max)
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var i = 0 as Integer
	let b = 7 as Byte
	i = b as Integer
	return i as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: assign-widening-int-to-float-promotes -->
An `int` assigned to a `float` is PROMOTED, not merely permitted. The stored value carries the
declared type, so this prints `5.0`. Before the declared type was made the single source of
truth this printed `5`: the widening was legal, went unapplied, and the reader saw the raw int.
```maxon
function main() returns ExitCode
	var f = 1.5
	f = 5
	print("{f}")
	return 0 as ExitCode
end 'main'
```
```stdout
5.0
```

<!-- test: assign-struct-to-same-struct -->
A struct value into a variable of that struct type, from a factory, from a function, and from
another variable.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function makePoint(v Integer) returns Point
	return Point.create(v, y: v)
end 'makePoint'

function main() returns ExitCode
	var p = Point.create(1, y: 2)
	p = makePoint(7)
	p.x = 9
	let q = Point.create(5, y: 5)
	p = q
	return p.x as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: assign-constants-enum-to-backing -->
A constants-enum where its numeric backing type is declared coerces to the raw backing value.
```maxon

typealias Byte = int(0 to u8.max)

enum JsonByte as Byte
	lBracket = 91
	rBracket = 93
end 'JsonByte'

function main() returns ExitCode
	var b = 0 as Byte
	b = JsonByte.lBracket
	return b
end 'main'
```
```exitcode
91
```

<!-- test: assign-self-field-and-iterator -->
A `self` field assignment and a `for`-loop iterator binding are assignments too, and are governed
by the same rule.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type Counter
	export var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function addAll(values IntArray) returns Integer
		for v in values 'each'
			self.total = self.total + v
		end 'each'
		return self.total
	end 'addAll'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	let nums = [1, 2, 3]
	return c.addAll(nums) as ExitCode
end 'main'
```
```exitcode
6
```


### The type rule for an ENUM place — the same rule, the same hole

<!-- test: error.retype-enum-to-other-enum-errors -->
Two different enums are two different types, exactly as two different structs are. This is the
struct case above one type over, and it had the identical defect for the identical reason: the
assignment door compared the declared NAME against the value only when the value was a struct, so
an enum place accepted any enum at all. `c` is declared `Color` and holds `Shade.light`; the
program compiled clean and `c.ordinal` reported `Shade.light`'s ordinal under `Color`'s name.
```maxon

enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

function main() returns ExitCode
	var c = Color.red
	c = Shade.light
	return c.ordinal
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-enum-to-other-enum-errors.test:15:2: cannot assign a value of type 'Shade' to variable 'c', which holds 'Color'
```

<!-- test: error.retype-enum-field-errors -->
The FIELD store reaches the same place by the same road — it shares the door with the local
assignment, so it shared the hole.
```maxon

enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

type Holder
	export var c as Color

	static function create() returns Self
		return Self{c: Color.red}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.c = Shade.light
	return h.c.ordinal
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.retype-enum-field-errors.test:23:4: cannot assign a value of type 'Shade' to field 'c' of 'Holder', which holds 'Color'
```

<!-- test: error.wrong-enum-in-struct-literal-field-errors -->
And the STRUCT-LITERAL field initializer, which put the same value in the same slot under the same
declared type — but reached it through a door that skipped the check entirely unless the field was
a numeric primitive.
```maxon

enum Color
	red
	green
end 'Color'

enum Shade
	dark
	light
end 'Shade'

type Holder
	export var c as Color

	static function create() returns Self
		return Self{c: Shade.light}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.c.ordinal
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/assignment/error.wrong-enum-in-struct-literal-field-errors.test:17:15: cannot assign a value of type 'Shade' to field 'c' of 'Holder', which holds 'Color'
```

<!-- test: assign-enum-to-same-enum -->
The control: the same shape with the enum it declared. A rule that refused this would be worse than
no rule.
```maxon

enum Color
	red
	green
end 'Color'

type Holder
	export var c as Color

	static function create() returns Self
		return Self{c: Color.red}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.c = Color.green
	var c = Color.red
	c = Color.green
	return h.c.ordinal + c.ordinal
end 'main'
```
```exitcode
2
```
