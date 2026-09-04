---
feature: nominal-typealias
status: stable
keywords: [typealias, ranged-typealias, nominal-types, type-safety, cast, as, ExitCode, operators]
category: type-system
---

# A `typealias` Is a Nominally Distinct Type

## Documentation

Every `typealias` names its own type. Two ranged aliases over the SAME range are two types, and a value of
one never flows into a slot of the other — a parameter, a rebind, an `otherwise`, a `match` arm, a struct
field, a union payload, a generic type argument — unless the author writes the cast:

```text
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

let a = 30 as Age
takesYear(a)             // E3005: expected 'Year', got 'Age'
takesYear(a as Year)     // the one door between two aliases
```

`as` crosses in BOTH directions. A widening cast (the source range provably fits the target) emits no
guard; a narrowing cast keeps its runtime range check. E3010 fires only when the cast names the value's
OWN alias.

**A `return` carries the cast.** `return x` from a function declared `returns T` is `return x as T`: the
one door with an implicit conversion, and it performs exactly what the written cast would. A widening
return emits no guard, a narrowing return keeps its runtime guard, and `main` may return an alias-typed
value without spelling `ExitCode`. Nothing else converts implicitly — an argument, a rebind, a field
store, an `otherwise` value and a match-arm merge still demand the cast. Only a NOMINAL difference
converts: a different struct, a boxed union where a scalar is declared, or a lossy float where an int is
declared is refused at the `return` exactly as before.

**Decay.** A value with NO alias fits any alias slot of its structural type: a literal, a counted-loop
counter, a bare `var` initialised from a literal, the raw value of a payload-free `enum` case. A named
value fits an unnamed slot. Only two DIFFERENT names conflict.

**Arithmetic.** `a + a2` over one alias yields that alias. `a + 1` yields it too — an UNNAMED operand
adopts the named one. `a + y` over two different aliases is an error; the same rule governs comparisons,
`min`/`max` and unary minus. A shift adopts its LEFT operand only, so `n shl w` is `n`'s type whatever
`w` is. Negation adopts a SIGNED alias's identity; negating an UNSIGNED alias yields an unnamed value,
because the result is outside the alias's own range (the bootstrap renders `-x` for an unsigned `x` as a
signed number, and shv2 agrees).

An arithmetic result carries its alias as a NAME, not as a PROOF: `a + a2` over `Score` is `Score`-typed
but may lie outside `Score`'s range, so every range guard that fires today still fires.

**`ExitCode` is an alias like any other.** `x as ExitCode` is legal for any alias-typed `x` and, where the
alias provably fits, emits no guard and is not E3010; `main`'s `return` converts the same way without it.

**Cross-file.** The same alias name over the same range in two files is ONE type.

## Tests

### The doors — one refusal each

<!-- test: error.wide-into-narrow-parameter -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function takesNarrow(n Narrow) returns Narrow
	return n
end 'takesNarrow'

function main() returns ExitCode
	let w = 5 as Wide
	let r = takesNarrow(w)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'n': expected 'Narrow', got 'Wide'
```

<!-- test: error.narrow-into-wide-parameter -->
The rejection is symmetric: a range that FITS is not a type that matches.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function main() returns ExitCode
	let n = 5 as Narrow
	let r = takesWide(n)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'w': expected 'Wide', got 'Narrow'
```

<!-- test: a-narrow-value-converts-at-a-wide-return -->
A `return` is the one door that converts by itself: `return n` from a `returns Wide` function is
`return n as Wide`.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function widen(n Narrow) returns Wide
	return n
end 'widen'

function main() returns ExitCode
	let w = widen(5)
	print("{w}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: error.a-different-struct-returned-where-a-struct-is-declared -->
The line that does not move: only a nominal difference converts. Two structs share one tag and are two
records, and the wrong one handed back would be dropped under the declared type's destructor.
```maxon
typealias Coord = int(0 to 1000)

type Point
	export var x as Coord

	static function create(x Coord) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Span
	export var len as Coord

	static function create(len Coord) returns Self
		return Self{len: len}
	end 'create'
end 'Span'

function makeSpan() returns Span
	let p = Point.create(4)
	return p
end 'makeSpan'

function main() returns ExitCode
	let s = makeSpan()
	print("{s.len}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:2: Cannot return 'Point' from function declared to return 'Span'
```

<!-- test: error.a-boxed-union-returned-where-a-scalar-is-declared -->
A boxed union and a ranged alias share the `named` tag; the union is a record, not a name over a scalar,
so it is refused at the `return`.
```maxon
typealias Wide = int(0 to u64.max)

union Slot
	held(v Wide)
	empty
end 'Slot'

function unwrap() returns Wide
	let s = Slot.held(5)
	return s
end 'unwrap'

function main() returns ExitCode
	print("{unwrap()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: Cannot return 'Slot' from function declared to return 'Wide'
```

<!-- test: error.rebind-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function main() returns ExitCode
	let n = 5 as Narrow
	var w = 9 as Wide
	w = n
	print("{w}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:2: cannot assign a value of type 'Narrow' to variable 'w', which holds 'Wide'
```

<!-- test: error.otherwise-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

enum Fault implements Error
	failed
end 'Fault'

function mayFail() returns Wide throws Fault
	throw Fault.failed
end 'mayFail'

function main() returns ExitCode
	let n = 5 as Narrow
	let v = try mayFail() otherwise n
	print("{v}")
	return 0
end 'main'
```
```maxoncstderr
error E3059: <fragment>:15:10: type mismatch: 'otherwise type 'Narrow' does not match expected type 'Wide''
```

<!-- test: error.match-arms-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function pick(k Narrow, n Narrow, w Wide) returns Wide
	let r = match k 'm'
		0 gives n
		default gives w
	end 'm'
	return r
end 'pick'

function main() returns ExitCode
	let r = pick(0, n: 5, w: 9)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:10: match arms give incompatible types: 'Wide' vs 'Narrow'
```

<!-- test: error.struct-literal-field-store-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

type Holder
	export var w as Wide

	static function create(n Narrow) returns Self
		return Self{w: n}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(5)
	print("{h.w}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:15: cannot assign a value of type 'Narrow' to field 'w' of 'Holder', which holds 'Wide'
```

<!-- test: error.struct-field-assignment-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

type Holder
	export var w as Wide

	static function create() returns Self
		return Self{w: 9}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let n = 5 as Narrow
	var h = Holder.create()
	h.w = n
	print("{h.w}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:4: cannot assign a value of type 'Narrow' to field 'w' of 'Holder', which holds 'Wide'
```

<!-- test: error.union-payload-across-aliases -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

union Slot
	held(v Wide)
	empty
end 'Slot'

function main() returns ExitCode
	let n = 5 as Narrow
	let s = Slot.held(n)
	match s 'go'
		held(v) then return v as ExitCode
		empty then return 1
	end 'go'
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:21: type mismatch: 'expected Wide, got Narrow'
```

<!-- test: error.type-argument-fed-a-different-alias -->
A generic instance's type argument is a slot like any other: `Box with Integer` takes an `Integer`, and a
`Narrow` is not one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Narrow = int(0 to 16)

type Box uses T
	export var item as T

	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'
end 'Box'

typealias IntBox = Box with Integer

function main() returns ExitCode
	let n = 5 as Narrow
	let b = IntBox.create(n)
	print("{b.item}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:17: argument type mismatch for 'v': expected 'Integer', got 'Narrow'
```

<!-- test: error.a-user-struct-sharing-a-stdlib-alias-name-is-still-a-record -->
`stdlib/Builtins.maxon` declares `typealias ParsedInt`, and a user `type ParsedInt` stands beside it
(`stdlib-user-shadows.md`). The struct's identity is the bare name and the alias's is a compiler mint, so
the struct does not decay: an `int` at a `Box with ParsedInt`'s `T` is refused rather than stored in the
box and freed as a record.
```maxon
type ParsedInt
	export var s as String

	export static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'ParsedInt'

type Box uses T
	export var value as T

	export static function create(v T) returns Self
		return Self{value: v}
	end 'create'
end 'Box'

typealias PBox = Box with ParsedInt

function main() returns ExitCode
	let bad = PBox.create(5)
	print("{bad.value.s}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:17: argument type mismatch for 'v': expected 'ParsedInt', got 'int'
```

<!-- test: error.two-names-over-one-range-are-two-types -->
The name is the type. Same base, same bounds, different name: refused.
```maxon
typealias Small = int(0 to 100)
typealias AlsoSmall = int(0 to 100)

function takesSmall(s Small) returns Small
	return s
end 'takesSmall'

function main() returns ExitCode
	let a = 42 as AlsoSmall
	let r = takesSmall(a)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 's': expected 'Small', got 'AlsoSmall'
```

<!-- test: main-returns-an-alias-typed-value -->
`ExitCode` is an alias like any other, and `main`'s `return` converts to it like any other `return`.
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	let s = 7 as Score
	return s
end 'main'
```
```exitcode
7
```

<!-- test: error.interface-method-implemented-over-a-different-alias -->
A conformance compares the SPELLED alias, so an interface method over `Integer` is not implemented by a
method over `Int`, even though the two ranges are identical.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Int = int(i64.min to i64.max)

interface Sink
	function process(v Integer) returns Integer
end 'Sink'

type Adder implements Sink
	let base as Integer

	function process(v Int) returns Integer
		print("{v}")
		return base
	end 'process'

	static function create() returns Self
		return Self{base: 40}
	end 'create'
end 'Adder'

function main() returns ExitCode
	let a = Adder.create()
	print("{a.process(2)}")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:9:6: Partial interface implementation: type 'Adder' has 1 method(s) with wrong signature:
  - process(v Int) returns Integer (expected process(v Integer) returns Integer)
```

### Operators — two names never meet without a cast

<!-- test: error.mixed-alias-addition -->
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function main() returns ExitCode
	let a = 30 as Age
	let y = 1990 as Year
	let s = a + y
	print("{s}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:12: operator '+' requires both operands to be the same type: 'Age' and 'Year' are different typealiases — cast one side with 'as'
```

<!-- test: error.mixed-alias-comparison -->
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function main() returns ExitCode
	let a = 30 as Age
	let y = 1990 as Year
	let lt = a < y
	print("{lt}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:13: operator '<' requires both operands to be the same type: 'Age' and 'Year' are different typealiases — cast one side with 'as'
```

<!-- test: error.mixed-alias-min -->
`min` and `max` are binary operators spelled as builtins, and they obey the same rule under their own name.
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function main() returns ExitCode
	let a = 30 as Age
	let y = 1990 as Year
	let m = min(a, y)
	print("{m}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:10: operator 'min' requires both operands to be the same type: 'Age' and 'Year' are different typealiases — cast one side with 'as'
```

<!-- test: error.arithmetic-result-carries-the-alias -->
`a + a2` over `Age` IS an `Age`, so it is refused at a `Year` parameter exactly as `a` alone would be.
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function main() returns ExitCode
	let a = 30 as Age
	let a2 = 12 as Age
	let r = takesYear(a + a2)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 'y': expected 'Year', got 'Age'
```

<!-- test: error.an-adopted-literal-operand-carries-the-alias -->
An unnamed operand ADOPTS the named one, so `a + 1` is an `Age` too — not a bare int that would decay
into any slot.
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function main() returns ExitCode
	let a = 30 as Age
	let r = takesYear(a + 1)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'y': expected 'Year', got 'Age'
```

<!-- test: error.a-shift-adopts-only-its-left-operand -->
A shift's right operand is a bit count, not a value of the result's type: `n shl w` is a `Narrow`
whatever `w` is, and no agreement between the two is asked. So it is refused at a `Wide` parameter.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function main() returns ExitCode
	let n = 3 as Narrow
	let w = 2 as Wide
	let r = takesWide(n shl w)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 'w': expected 'Wide', got 'Narrow'
```

<!-- test: error.negation-carries-a-signed-alias -->
`-d` over a SIGNED alias is that alias.
```maxon
typealias Delta = int(-100 to 100)
typealias Wide = int(0 to u64.max)

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function main() returns ExitCode
	let d = 5 as Delta
	let r = takesWide(-d)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'w': expected 'Wide', got 'Delta'
```

<!-- test: error.a-loop-variable-carries-the-element-alias -->
`for x in col` over an `Array with Narrow` binds a `Narrow`.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)
typealias NarrowCol = Array with Narrow

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function main() returns ExitCode
	var col = NarrowCol.create()
	col.push(4)
	var acc = 0
	for x in col 'each'
		acc = acc + takesWide(x)
	end 'each'
	print("{acc}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:15: argument type mismatch for 'w': expected 'Wide', got 'Narrow'
```

### What still flows — the legal side of every door above

<!-- test: same-alias-arithmetic-keeps-the-alias -->
```maxon
typealias Age = int(0 to 150)

function takesAge(a Age) returns Age
	return a
end 'takesAge'

function main() returns ExitCode
	let a = 30 as Age
	let a2 = 12 as Age
	let sum = takesAge(a + a2)
	let diff = takesAge(a - a2)
	let older = a2 < a
	print("{sum} {diff} {older}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 18 true
```

<!-- test: an-unnamed-operand-adopts-the-named-one -->
A literal, a counted-loop counter and a bare `var` are all unnamed, and each adopts `Age` beside an `Age`
— on either side of the operator.
```maxon
typealias Age = int(0 to 150)

function takesAge(a Age) returns Age
	return a
end 'takesAge'

function main() returns ExitCode
	let a = 30 as Age
	var bare = 10
	bare = bare + 1
	var acc = 0 as Age
	for i in 0 upto 3 'count'
		acc = acc + takesAge(a + i)
	end 'count'
	let viaLiteral = takesAge(a + 1)
	let literalFirst = takesAge(2 + a)
	let viaBare = takesAge(a + bare)
	print("{acc} {viaLiteral} {literalFirst} {viaBare}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
93 31 32 41
```

<!-- test: a-literal-decays-at-every-door -->
Argument, `return`, struct-literal field, rebind, `otherwise` and both `match` arms: a literal has no
alias and fits each.
```maxon
typealias Wide = int(0 to u64.max)

enum Fault implements Error
	failed
end 'Fault'

type Holder
	export var w as Wide

	static function create() returns Self
		return Self{w: 1}
	end 'create'
end 'Holder'

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function mayFail() returns Wide throws Fault
	throw Fault.failed
end 'mayFail'

function pick(k Wide) returns Wide
	return match k 'm'
		0 gives 8
		default gives 16
	end 'm'
end 'pick'

function main() returns ExitCode
	let h = Holder.create()
	var w = takesWide(2)
	w = 32
	let fallback = try mayFail() otherwise 64
	let total = h.w + w + fallback + pick(0) + pick(1)
	print("{total}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
121
```

<!-- test: a-named-value-decays-into-an-unnamed-slot -->
A `var` initialised from a literal has no alias, and an `Age` may be stored into it. What the binding then
holds is its own unnamed value: `acc` reaches a `Year` slot, where `a` itself could not.
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function takesYear(y Year) returns Year
	return y
end 'takesYear'

function main() returns ExitCode
	let a = 30 as Age
	var acc = 0
	acc = a
	let doubled = acc * 2
	let y = takesYear(acc)
	print("{acc} {doubled} {y}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
30 60 30
```

<!-- test: a-payload-free-enum-decays-into-an-alias-slot -->
```maxon
typealias Narrow = int(0 to 16)

enum Level
	low = 3
	high = 9
end 'Level'

function takesNarrow(n Narrow) returns Narrow
	return n
end 'takesNarrow'

function main() returns ExitCode
	let n = takesNarrow(Level.high)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: a-shift-adopts-its-left-operand -->
The legal twin of `error.a-shift-adopts-only-its-left-operand`: the same `n shl w` is accepted where a
`Narrow` is expected.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function takesNarrow(n Narrow) returns Narrow
	return n
end 'takesNarrow'

function main() returns ExitCode
	let n = 3 as Narrow
	let w = 2 as Wide
	let r = takesNarrow(n shl w)
	print("{r}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```

<!-- test: negating-an-unsigned-alias-yields-an-unnamed-value -->
`-d` over the signed `Delta` is a `Delta`; `-c` over the unsigned `Count` is unnamed — its value cannot
be a `Count` — and so decays into the `Delta` slot beside it.
```maxon
typealias Delta = int(-100 to 100)
typealias Count = int(0 to u64.max)

function takesDelta(d Delta) returns Delta
	return d
end 'takesDelta'

function main() returns ExitCode
	let d = 5 as Delta
	let c = 7 as Count
	print("{takesDelta(-d)} {takesDelta(-c)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-5 -7
```

<!-- test: an-overload-set-called-with-a-literal-still-resolves -->
Decay reaches overload scoring too: a literal argument fits the `Age` member, so it is neither refused nor
ambiguous against the `String` one.
```maxon
typealias Age = int(0 to 150)

function describe(a Age) returns Age
	return a + 1
end 'describe'

function describe(s String) returns Age
	return s.count() as Age
end 'describe'

function main() returns ExitCode
	let a = 30 as Age
	print("{describe(a)} {describe(5)} {describe("four")}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
31 6 4
```

<!-- test: an-overload-set-resolves-by-alias-name -->
Two members over two aliases of one base are two DIFFERENT parameter types, so an `Age` argument selects
the `Age` member and a `Year` the `Year` one.
```maxon
typealias Age = int(0 to 150)
typealias Year = int(0 to 3000)

function describe(a Age) returns Age
	return a + 1
end 'describe'

function describe(y Year) returns Year
	return y + 2
end 'describe'

function main() returns ExitCode
	let a = 30 as Age
	let y = 1990 as Year
	print("{describe(a)} {describe(y)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
31 1992
```

<!-- test: same-name-and-range-across-three-files-is-one-type -->
Three files each declare `MyInt = int(0 to 1000)`; a value made under one declaration passes through the
other two with no cast.
```maxon
// --- file: a.maxon
typealias MyInt = int(0 to 1000)

export function doubleIt(x MyInt) returns MyInt
	return x + x
end 'doubleIt'

// --- file: b.maxon
typealias MyInt = int(0 to 1000)

export function tripleIt(x MyInt) returns MyInt
	return x + x + x
end 'tripleIt'

// --- file: main.maxon
typealias MyInt = int(0 to 1000)

function halveIt(x MyInt) returns MyInt
	return x / 2
end 'halveIt'

function main() returns ExitCode
	let r = halveIt(tripleIt(doubleIt(4)))
	print("{r}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```

### `as` — the one door, both ways, and what it costs

<!-- test: as-crosses-both-ways -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function takesWide(w Wide) returns Wide
	return w
end 'takesWide'

function takesNarrow(n Narrow) returns Narrow
	return n
end 'takesNarrow'

function main() returns ExitCode
	let n = 5 as Narrow
	let w = n as Wide
	let back = w as Narrow
	print("{takesWide(w)} {takesNarrow(back)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 5
```

<!-- test: a-widening-cast-emits-no-guard -->
`Narrow` provably fits `Wide`, so `n as Wide` is a retag and nothing else. `widen` is inlined, so the
golden holds ONE emitted body and exactly ONE range cascade — the narrowing at `main`'s `as ExitCode`,
on a lane where `ExitCode` is narrower than `Wide`. The widening cast contributes none, which is what a
second cascade would betray.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function widen(n Narrow) returns Wide
	return n as Wide
end 'widen'

function main() returns ExitCode
	return widen(9) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: a-narrowing-cast-keeps-its-guard -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function widen(n Narrow) returns Wide
	return n as Wide
end 'widen'

function main() returns ExitCode
	let w = widen(9) * 40
	let n = w as Narrow
	print("{n}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-narrowing-cast-keeps-its-guard.test:11: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: an-arithmetic-result-is-unproven-and-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`a + a2` is `Score`-typed, but the name is not a proof: 130 is outside `Score`, and the guard at the
callee's entry still fires.
```maxon
typealias Score = int(0 to 100)

function takesScore(s Score) returns Score
	return s
end 'takesScore'

function main() returns ExitCode
	let a = 60 as Score
	let a2 = a + 10
	let r = takesScore(a + a2)
	print("{r}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-arithmetic-result-is-unproven-and-still-guarded.test:4: Range check failed: value outside typealias 'Score'
Stack trace:
  in takesScore
  in main
  in mrt_start
```

<!-- test: an-adopted-result-in-a-packed-array-literal-is-still-guarded -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`[s + 1, s + 2]` is an `Array with Small`, because its first element wears `Small` — and `Small` packs at
one byte. The element wears the name without a proof, so it is converted into the slot through the same
door a written cast uses, and the guard fires on 256 rather than storing it as 0.
```maxon
typealias Small = int(0 to 255)

function main() returns ExitCode
	let s = 255 as Small
	let xs = [s + 1, s + 2]
	let first = try xs.get(0) otherwise 99
	print("{first}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-adopted-result-in-a-packed-array-literal-is-still-guarded.test:6: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-widening-return-emits-no-guard -->
`Narrow` provably fits `Wide`, so the implicit conversion at `widen`'s `return` is a retag and nothing
else — the golden shows no range cascade in `widen`, exactly as `a-widening-cast-emits-no-guard` shows for
the written cast.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function widen(n Narrow) returns Wide
	return n
end 'widen'

function main() returns ExitCode
	return widen(9) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: a-narrowing-return-keeps-its-guard -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`return w` from a `returns Narrow` function is `return w as Narrow`, and a narrowing cast keeps its guard:
360 is outside `Narrow`, and the `return` panics.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 16)

function shrink(w Wide) returns Narrow
	return w
end 'shrink'

function main() returns ExitCode
	let n = shrink(360)
	print("{n}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-narrowing-return-keeps-its-guard.test:6: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in shrink
  in main
  in mrt_start
```

<!-- test: an-int-alias-converts-at-a-float-alias-return -->
The tag check licenses an int-to-float widening and `as` performs it, so a `return` performs it too.
```maxon
typealias Tally = int(0 to 100)
typealias Ratio = float(0.0 to 1000.0)

function toRatio(t Tally) returns Ratio
	return t
end 'toRatio'

function main() returns ExitCode
	print("{toRatio(7) / 2.0}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.5
```

<!-- test: as-exitcode-is-legal-and-guard-free-where-it-fits -->
A `Byte` fits `ExitCode` on every target, so the cast `main` owes is not E3010 and emits no guard.
```maxon
typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 7 as Byte
	return b as ExitCode
end 'main'
```
```exitcode
7
```
