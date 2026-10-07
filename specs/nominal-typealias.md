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
guard; a narrowing cast keeps its runtime range check. E3010 fires when the cast names the value's OWN
alias, and when a numeric literal is cast to exactly the type its destination already declares (an
argument's parameter, a `return`'s result, a struct-literal or stored field).

**A `return` carries the cast.** `return x` from a function declared `returns T` is `return x as T`: the
one door with an implicit conversion, and it performs exactly what the written cast would. A widening
return emits no guard, a narrowing return keeps its runtime guard, and `main` may return an alias-typed
value without spelling `ExitCode`. Nothing else converts a NAMED value implicitly — at an argument, a
rebind, a field store, an `otherwise` value and a match-arm merge an aliased value still demands the
cast. A literal names no alias and demands none (see Decay). Only a NOMINAL difference
converts: a different struct, a boxed union where a scalar is declared, or a lossy float where an int is
declared is refused at the `return` exactly as before.

**Decay.** A value with NO alias fits any alias slot of its structural type: a literal, a counted-loop
counter, a bare `var` initialised from a literal, the raw value of a payload-free `enum` case. A named
value fits an unnamed slot. Two DIFFERENT names conflict unless one `implements` the other.

**Subtypes.** `typealias BlockId = int(0 to u64.max) implements ElementIndex` makes `BlockId` a subtype
of `ElementIndex`: a `BlockId` goes wherever an `ElementIndex` (or any alias `ElementIndex` implements)
is declared, with no cast. The relation is one way — an `ElementIndex` needs a cast to become a
`BlockId`, and a cast to an ancestor is E3010 — and two subtypes of one parent are still two types. The
subtype's range must fit its parent's over the same primitive and signedness (E3178), a chain that comes
back to itself is E3091, and a parent that is not a ranged typealias is E2003.

**Arithmetic.** `a + a2` over one alias yields that alias. `a + 1` yields it too — an UNNAMED operand
adopts the named one, and so does an ancestor: `block + offset` over a `BlockId` and an `ElementIndex` is a
`BlockId`. `block + varId` over two subtypes of one parent is a MIXED expression: it lands in a destination
any operand's alias satisfies (a parameter, a field, a merge), computed into a range-checked temporary of
the destination's type, and elsewhere it is the nearest alias both implement — which is what a diagnostic
names. Two aliases with no common ancestor are an error; the same rule governs comparisons,
`min`/`max` and unary minus. The `match` and ternary arms of two subtypes join to their nearest common
ancestor the same way. A shift adopts its LEFT operand only, so `n shl w` is `n`'s type whatever
`w` is. Negation adopts a SIGNED alias's identity; negating an UNSIGNED alias yields an unnamed value,
because the result is outside the alias's own range — `-x` for an unsigned `x` renders as a signed
number.

An arithmetic result carries its alias as a NAME, not as a PROOF: `a + a2` over `Score` is `Score`-typed
but may lie outside `Score`'s range, so every range guard on it still fires.

**`ExitCode` is an alias like any other.** `x as ExitCode` is legal for any alias-typed `x` and, where the
alias provably fits, emits no guard and is not E3010; `main`'s `return` converts the same way without it.

**Cross-file.** A typealias is its DECLARATION: the same name over the same range in two files is two
types, and a value crosses from one to the other only through `as` (`specs/typealias-file-scope.md`).

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

<!-- test: error.an-aliased-index-into-array-get -->
`Array.get` declares its index `ElementIndex`, so a value of another alias is refused there like at any
other parameter.
```maxon
typealias Offset = int(0 to u64.max)
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	let o = 0 as Offset
	let r = try rows.get(o) otherwise 0
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:19: argument type mismatch for 'index': expected 'ElementIndex', got 'Offset'
```

<!-- test: error.an-aliased-index-into-array-set -->
```maxon
typealias Offset = int(0 to u64.max)
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	let o = 0 as Offset
	try rows.set(o, value: 7) otherwise panic("the row exists")
	print("{rows.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:11: argument type mismatch for 'index': expected 'ElementIndex', got 'Offset'
```

<!-- test: error.an-aliased-length-into-array-resize -->
```maxon
typealias Offset = int(0 to u64.max)
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function main() returns ExitCode
	var rows = RowArray.create()
	let o = 3 as Offset
	rows.resize(o)
	print("{rows.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:7: argument type mismatch for 'newLength': expected 'ElementIndex', got 'Offset'
```

<!-- test: error.array-count-into-another-alias -->
`count()` answers a `Count`, which is not the parameter's alias.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function takesNarrow(n Narrow) returns Narrow
	return n
end 'takesNarrow'

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)
	let a = takesNarrow(arr.count())
	print("{a}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 'n': expected 'Narrow', got 'Count'
```

<!-- test: error.array-count-cast-to-count-is-unneeded -->
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)
	let n = arr.count() as Count
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3010: <fragment>:8:22: unneeded cast: 'Count' already fits in 'Count'
```

<!-- test: error.array-count-cast-to-element-index-is-an-ancestor-cast -->
`Count` implements `ElementIndex`, so a cast to its ancestor is unneeded.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)
	let n = arr.count() as ElementIndex
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3010: <fragment>:8:22: unneeded cast: 'Count' already fits in 'ElementIndex'
```

<!-- test: every-collection-count-is-a-count -->
Every collection's `count()` is a `Count`, so each goes to a `Count` parameter with no cast.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function takesCount(n Count) returns Count
	return n
end 'takesCount'

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)
	let list = List from [10, 20]
	let map = [1: 10, 2: 20, 3: 30]
	let set = Set from [5, 6, 7, 8]
	let vec = Vector from [1, 2, 3, 4, 5]
	let text = "a😀b"
	print("{takesCount(arr.count())} {takesCount(list.count())} {takesCount(map.count())} {takesCount(set.count())} {takesCount(vec.count())} {takesCount(text.codepoints().count())} {takesCount(text.utf16().count())}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 3 4
```
```exitcode
0
```

<!-- test: a-json-array-length-is-a-count -->
`Json.arrayLength` answers a `Count`, so it goes to a `Count` parameter with no cast.
```maxon
function takesCount(n Count) returns Count
	return n
end 'takesCount'

function main() returns ExitCode
	let doc = try Json.parse("[1, 2, 3]") otherwise return 1
	let length = try doc.arrayLength(doc.root) otherwise return 2
	print("{takesCount(length)}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: error.an-element-index-is-not-a-count -->
A position is an `ElementIndex`, and an `ElementIndex` is not a `Count`.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function takesCount(n Count) returns Count
	return n
end 'takesCount'

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)

	for (position, _) in arr.withIterator() 'eachElement'
		print("{takesCount(position.index())}\n")
	end 'eachElement'

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:11: argument type mismatch for 'n': expected 'Count', got 'ElementIndex'
```

<!-- test: a-codepoint-count-compares-with-an-array-count -->
Two `Count` subtypes compare through their common ancestor with no cast.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function main() returns ExitCode
	var arr = NarrowArray.create()
	arr.push(4)
	arr.push(9)
	let text = "a😀"
	if text.codepoints().count() == arr.count() 'oneCodepointPerElement'
		print("same\n")
		return 0
	end 'oneCodepointPerElement'
	print("different\n")
	return 1
end 'main'
```
```stdout
same
```
```exitcode
0
```

<!-- test: a-processor-count-compares-with-an-array-count -->
<!-- unsupported-targets: wasm32-wasi -->
A processor count and an array count are both `Count`s, so they compare with no cast.
```maxon
typealias Narrow = int(0 to 1000)
typealias NarrowArray = Array with Narrow

function main() returns ExitCode
	let arr = NarrowArray.create()
	let procs = Scheduler.processorCount()
	if arr.count() < procs and procs > arr.count() 'moreProcessorsThanElements'
		return 7
	end 'moreProcessorsThanElements'
	return 2
end 'main'
```
```exitcode
7
```

<!-- test: an-alias-of-element-index-indexes-an-array -->
An alias that `implements` another is its subtype: a `BlockId` goes wherever an `ElementIndex` is
expected, with no cast.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	rows.push(9)
	let last = 1 as BlockId
	let r = try rows.get(last) otherwise 0
	print("{r}")
	return 0
end 'main'
```
```stdout
9
```

<!-- test: a-subtype-widens-at-a-parameter-transitively -->
`typealias LoopHead = int(0 to u64.max) implements BlockId` is a subtype of `BlockId` and of `ElementIndex` both.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias LoopHead = int(0 to u64.max) implements BlockId

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function takesIndex(i ElementIndex) returns ElementIndex
	return i
end 'takesIndex'

function main() returns ExitCode
	let h = 3 as LoopHead
	let b = takesBlock(h)
	let i = takesIndex(h)
	print("{b} {i}")
	return 0
end 'main'
```
```stdout
3 3
```

<!-- test: error.a-sibling-subtype-is-refused -->
Two subtypes of one alias do not meet: a `VarId` is not a `BlockId`.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias VarId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let v = 3 as VarId
	let b = takesBlock(v)
	print("{b}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'b': expected 'BlockId', got 'VarId'
```

<!-- test: error.a-supertype-does-not-narrow-to-its-subtype -->
Widening is one way: a plain `ElementIndex` needs a cast to become a `BlockId`.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let i = 3 as ElementIndex
	let b = takesBlock(i)
	print("{b}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:10: argument type mismatch for 'b': expected 'BlockId', got 'ElementIndex'
```

<!-- test: a-subtype-with-its-ancestor-is-the-subtype -->
Arithmetic over an alias and its ancestor, in either order, or with an unaliased operand, yields the
MORE-DERIVED alias: `block + offset`, `offset + block` and `block + 1` are all `BlockId`s.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let block = 3 as BlockId
	let offset = 4 as ElementIndex
	let a = takesBlock(block + offset)
	let b = takesBlock(offset + block)
	let c = takesBlock(block + 1)
	print("{a} {b} {c}")
	return 0
end 'main'
```
```stdout
7 7 4
```

<!-- test: an-expression-satisfies-any-operands-alias -->
An arithmetic expression satisfies a target alias when ANY of its operands does: `block + varId` passes
to a `BlockId` parameter, to a `VarId` parameter, and to an `ElementIndex` one.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias VarId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function takesVar(v VarId) returns VarId
	return v
end 'takesVar'

function takesIndex(i ElementIndex) returns ElementIndex
	return i
end 'takesIndex'

function main() returns ExitCode
	let block = 3 as BlockId
	let varId = 4 as VarId
	let a = takesBlock(block + varId)
	let b = takesVar(block + varId)
	let c = takesIndex(block + varId)
	let x = block + varId
	let d = takesIndex(x)
	print("{a} {b} {c} {d}")
	return 0
end 'main'
```
```stdout
7 7 7 7
```

<!-- test: a-mixed-expression-is-guarded-as-its-destination -->
`takesBlock(block + varId)` computes the sum into a temporary `BlockId`, so the sum must pass
`BlockId`'s range check at the door.
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let block = 8 as BlockId
	let varId = 5 as VarId
	let b = takesBlock(block + varId)
	print("{b}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-is-guarded-as-its-destination.test:13: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-mixed-expression-is-guarded-as-a-struct-literal-field -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

type Holder
	export var block as BlockId

	static function create(block BlockId, varId VarId) returns Self
		return Self{block: block + varId}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(8, varId: 5)
	print("{h.block}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-is-guarded-as-a-struct-literal-field.test:10: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in Holder.create
  in main
  in mrt_start
```
<!-- test: a-mixed-expression-is-guarded-as-an-assigned-field -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

type Holder
	export var block as BlockId

	static function create() returns Self
		return Self{block: 1}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let block = 8 as BlockId
	let varId = 5 as VarId
	var h = Holder.create()
	h.block = block + varId
	print("{h.block}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-is-guarded-as-an-assigned-field.test:18: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in main
  in mrt_start
```
<!-- test: a-mixed-expression-in-range-is-stored-in-a-field -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

type Holder
	export var block as BlockId
	export var other as VarId

	static function create(block BlockId, varId VarId) returns Self
		return Self{block: block + varId, other: block + varId}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(3, varId: 4)
	print("{h.block} {h.other}")
	return 0
end 'main'
```
```stdout
7 7
```
<!-- test: a-mixed-expression-is-guarded-as-its-otherwise-merge -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

enum Miss
	none
end 'Miss'

function find(ok bool) returns BlockId throws Miss
	if not ok 'missing'
		throw Miss.none
	end 'missing'

	return 1
end 'find'

function pick(block BlockId, varId VarId) returns BlockId
	return try find(false) otherwise block + varId
end 'pick'

function main() returns ExitCode
	let r = pick(8, varId: 5)
	print("{r}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-is-guarded-as-its-otherwise-merge.test:19: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in pick
  in main
  in mrt_start
```
<!-- test: a-mixed-expression-is-guarded-as-its-match-arm-merge -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

enum Pick
	first
	second
end 'Pick'

function choose(p Pick, block BlockId, varId VarId) returns BlockId
	let r = match p 'p'
		first gives block
		second gives block + varId
	end 'p'
	return r
end 'choose'

function main() returns ExitCode
	let r = choose(Pick.second, block: 8, varId: 5)
	print("{r}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-is-guarded-as-its-match-arm-merge.test:16: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in choose
  in main
  in mrt_start
```
<!-- test: a-mixed-expression-into-an-overload-is-guarded-at-the-call -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

function show(b BlockId) returns String
	return "block {b}"
end 'show'

function show(s String) returns String
	return s
end 'show'

function main() returns ExitCode
	let block = 8 as BlockId
	let varId = 5 as VarId
	print(show(block + varId))
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-mixed-expression-into-an-overload-is-guarded-at-the-call.test:17: Range check failed: value outside typealias 'BlockId'
Stack trace:
  in main
  in mrt_start
```

<!-- test: an-overload-is-chosen-by-an-operand-of-a-mixed-expression -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small
typealias Row = int(0 to 1000)

function describe(b BlockId) returns String
	return "block {b}"
end 'describe'

function describe(r Row) returns String
	return "row {r}"
end 'describe'

function main() returns ExitCode
	let block = 3 as BlockId
	let varId = 4 as VarId
	print(describe(block + varId))
	return 0
end 'main'
```
```stdout
block 7
```

<!-- test: error.a-mixed-expression-no-operand-of-which-fits-the-field -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

type Holder
	export var block as BlockId

	static function create(v VarId, w VarId) returns Self
		return Self{block: v + w}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(3, w: 4)
	print("{h.block}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:15: cannot assign a value of type 'VarId' to field 'block' of 'Holder', which holds 'BlockId'
```
<!-- test: error.a-mixed-expression-fits-no-overload -->
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small
typealias Row = int(0 to 1000)
typealias Col = int(0 to 1000)

function describe(r Row) returns String
	return "row {r}"
end 'describe'

function describe(c Col) returns String
	return "col {c}"
end 'describe'

function main() returns ExitCode
	let block = 3 as BlockId
	let varId = 4 as VarId
	print(describe(block + varId))
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:8: argument type mismatch for 'r': expected 'Row', got 'Small'
```

<!-- test: error.a-mixed-expression-that-fits-two-overloads-is-ambiguous -->
Each operand of `block + varId` satisfies one of the two overloads, so the call is ambiguous and is refused
at the call.
```maxon
typealias Small = int(0 to 10)
typealias BlockId = int(0 to 10) implements Small
typealias VarId = int(0 to 10) implements Small

function show(b BlockId) returns String
	return "block {b}"
end 'show'

function show(v VarId) returns String
	return "var {v}"
end 'show'

function main() returns ExitCode
	let block = 3 as BlockId
	let varId = 4 as VarId
	print(show(block + varId))
	return 0
end 'main'
```
```maxoncstderr
error E3007: <fragment>:17:8: Ambiguous overload for 'show': multiple overloads match. Candidates: (b BlockId), (v VarId)
```

<!-- test: an-implements-alias-widens-to-its-parent -->
`typealias FooId = int(0 to 1000) implements ElementIndex` keeps its own range and is a subtype of
`ElementIndex`: it passes to an `ElementIndex` parameter and indexes an Array with no cast.
```maxon
typealias FooId = int(0 to 1000) implements ElementIndex
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function takesIndex(i ElementIndex) returns ElementIndex
	return i
end 'takesIndex'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	rows.push(9)
	let foo = 1 as FooId
	let i = takesIndex(foo)
	let r = try rows.get(foo) otherwise 0
	print("{i} {r}")
	return 0
end 'main'
```
```stdout
1 9
```

<!-- test: an-implements-alias-is-transitive -->
```maxon
typealias FooId = int(0 to 1000) implements ElementIndex
typealias BarId = int(0 to 10) implements FooId

function takesIndex(i ElementIndex) returns ElementIndex
	return i
end 'takesIndex'

function takesFoo(f FooId) returns FooId
	return f
end 'takesFoo'

function main() returns ExitCode
	let bar = 7 as BarId
	print("{takesIndex(bar)} {takesFoo(bar)}")
	return 0
end 'main'
```
```stdout
7 7
```

<!-- test: error.an-implements-range-must-fit-its-parent -->
```maxon
typealias FooId = int(0 to 1000)
typealias WideId = int(0 to 5000) implements FooId

function main() returns ExitCode
	let w = 3 as WideId
	print("{w}")
	return 0
end 'main'
```
```maxoncstderr
error E3178: <fragment>:3:46: typealias 'WideId' does not fit the alias it implements: its range is not inside the range of 'FooId'
```

<!-- test: error.an-implements-alias-must-share-its-parents-primitive -->
```maxon
typealias FooId = int(0 to 1000)
typealias Ratio = float(0.0 to 1.0) implements FooId

function main() returns ExitCode
	let r = 0.5 as Ratio
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3178: <fragment>:3:48: typealias 'Ratio' does not fit the alias it implements: it is a `float` range and 'FooId' is an unsigned `int` range
```

<!-- test: a-value-narrowed-into-an-implements-alias-is-guarded -->
```maxon
typealias FooId = int(0 to 1000) implements ElementIndex

function takesFoo(f FooId) returns FooId
	return f
end 'takesFoo'

function main() returns ExitCode
	let i = 4000 as ElementIndex
	let f = takesFoo(i as FooId)
	print("{f}")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-value-narrowed-into-an-implements-alias-is-guarded.test:10: Range check failed: value outside typealias 'FooId'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-ternary-over-two-siblings-joins-to-their-parent -->
The arms of a ternary over two subtypes of one alias join to that alias.
```maxon
typealias Base = int(0 to 1000)
typealias Left = int(0 to 100) implements Base
typealias Right = int(0 to 200) implements Base

function leftOf() returns Left
	return 3
end 'leftOf'

function rightOf() returns Right
	return 7
end 'rightOf'

function pick(flag bool) returns Base
	let a = leftOf() if flag else rightOf()
	return a
end 'pick'

function main() returns ExitCode
	print("{pick(false)}")
	return 0
end 'main'
```
```stdout
7
```

<!-- test: a-match-over-two-siblings-joins-to-their-parent -->
```maxon
typealias Base = int(0 to 1000)
typealias Left = int(0 to 100) implements Base
typealias Right = int(0 to 200) implements Base

enum Side
	left
	right
end 'Side'

function leftOf() returns Left
	return 3
end 'leftOf'

function rightOf() returns Right
	return 7
end 'rightOf'

function pick(s Side) returns Base
	let a = match s 's'
		left gives leftOf()
		right gives rightOf()
	end 's'
	return a
end 'pick'

function main() returns ExitCode
	print("{pick(Side.right)}")
	return 0
end 'main'
```
```stdout
7
```

<!-- test: error.a-joined-ternary-is-its-parent-not-its-arm -->
```maxon
typealias Base = int(0 to 1000)
typealias Left = int(0 to 100) implements Base
typealias Right = int(0 to 200) implements Base

function leftOf() returns Left
	return 3
end 'leftOf'

function rightOf() returns Right
	return 7
end 'rightOf'

function takesLeft(l Left) returns Left
	return l
end 'takesLeft'

function main() returns ExitCode
	let a = leftOf() if true else rightOf()
	print("{takesLeft(a)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:10: argument type mismatch for 'l': expected 'Left', got 'Base'
```

<!-- test: error.a-ternary-over-unrelated-aliases -->
```maxon
typealias Left = int(0 to 100)
typealias Right = int(0 to 200)

function leftOf() returns Left
	return 3
end 'leftOf'

function rightOf() returns Right
	return 7
end 'rightOf'

function main() returns ExitCode
	let a = leftOf() if true else rightOf()
	print("{a}")
	return 0
end 'main'
```
```maxoncstderr
error E2028: <fragment>:14:19: ternary expression type mismatch: true branch is 'Left' but false branch is 'Right'
```

<!-- test: a-private-alias-of-one-name-implements-a-parent-per-file -->
Two files each declare a private `Id` implementing a different parent; each file's `Id` widens to its own
parent.
```maxon
// --- file: a.maxon
typealias Small = int(0 to 100)
typealias Id = int(0 to 10) implements Small

function takeSmall(s Small) returns ExitCode
	return 3 if s > 50 else 4
end 'takeSmall'

export function checkA() returns ExitCode
	let i = 7 as Id
	return takeSmall(i)
end 'checkA'

// --- file: b.maxon
typealias Big = int(0 to 1000)
typealias Id = int(0 to 10) implements Big

function takeBig(b Big) returns ExitCode
	return 5 if b > 5 else 6
end 'takeBig'

export function checkB() returns ExitCode
	let i = 7 as Id
	return takeBig(i)
end 'checkB'

// --- file: main.maxon
function main() returns ExitCode
	return checkA() + checkB()
end 'main'
```
```exitcode
9
```

<!-- test: an-implements-parent-may-be-qualified -->
```maxon
// --- file: api/base.maxon
export typealias Base = int(0 to 100)

// --- file: main.maxon
typealias Kid = int(0 to 10) implements api.Base

function takeBase(b api.Base) returns api.Base
	return b
end 'takeBase'

function main() returns ExitCode
	let k = 4 as Kid
	print("{takeBase(k)}")
	return 0
end 'main'
```
```stdout
4
```

<!-- test: a-qualified-implements-parent-beside-a-same-named-type-is-the-parameters-alias -->
The parent `stdlib.ElementIndex` shares its name with an author's enum, and the subtype edge is filed under the
identity the parameter type `stdlib.ElementIndex` carries, so a `Kid` is accepted there.
```maxon
// --- file: a/e.maxon
export enum ElementIndex
	below = -5
	above = 7
end 'ElementIndex'

// --- file: main.maxon
typealias Kid = int(0 to 10) implements stdlib.ElementIndex

function take(b stdlib.ElementIndex) returns stdlib.ElementIndex
	return b
end 'take'

function main() returns ExitCode
	let k = 4 as Kid
	print("{take(k)} {a.ElementIndex.above.rawValue}")
	return 0
end 'main'
```
```stdout
4 7
```

<!-- test: error.an-expression-no-operand-of-which-satisfies-the-target -->
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias VarId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let v = 3 as VarId
	let w = 4 as VarId
	let b = takesBlock(v + w)
	print("{b}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:10: argument type mismatch for 'b': expected 'BlockId', got 'VarId'
```

<!-- test: error.a-bound-mixed-expression-is-the-common-ancestor -->
With no target where it lands, `block + varId` takes the nearest common ancestor of its operands, so
`x` is an `ElementIndex` and no longer satisfies `BlockId`.
```maxon
typealias BlockId = int(0 to u64.max) implements ElementIndex
typealias VarId = int(0 to u64.max) implements ElementIndex

function takesBlock(b BlockId) returns BlockId
	return b
end 'takesBlock'

function main() returns ExitCode
	let block = 3 as BlockId
	let varId = 4 as VarId
	let x = block + varId
	let y = takesBlock(x)
	print("{y}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:10: argument type mismatch for 'b': expected 'BlockId', got 'ElementIndex'
```

<!-- test: error.an-alias-of-an-alias-is-refused -->
A subtype is declared with its range and `implements`; `typealias X = Y` naming another alias is not a
typealias form.
```maxon
typealias BlockId = ElementIndex

function main() returns ExitCode
	let b = 3 as BlockId
	print("{b}")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:21: Unsupported: a typealias over 'identifier' (a typealias names `int(low to high)`, `float(low to high)`, `bits(n)`, `function(...)`, a tuple `(A, B)` or a generic instance `Base with …`)
```

<!-- test: error.an-alias-over-the-same-range-is-still-distinct -->
Only an alias that `implements` another is compatible with it. One spelled over the same range is its own
type.
```maxon
typealias Position = int(0 to u64.max)
typealias Row = int(0 to 1000)
typealias RowArray = Array with Row

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(4)
	let p = 0 as Position
	let r = try rows.get(p) otherwise 0
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:19: argument type mismatch for 'index': expected 'ElementIndex', got 'Position'
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

	static function create(v T) returns Self
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
`stdlib/Builtins.maxon` declares `public typealias ParsedInt`, and a user `type ParsedInt` stands beside it
(`stdlib-user-shadows.md`). The struct's identity is the bare name and the alias's is a compiler mint, so
the struct does not decay: an `int` at a `Box with ParsedInt`'s `T` is refused rather than stored in the
box and freed as a record.
```maxon
type ParsedInt
	export var s as String

	static function make(t String) returns Self
		return Self{s: t}
	end 'make'
end 'ParsedInt'

type Box uses T
	export var value as T

	static function create(v T) returns Self
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

<!-- test: error.same-name-and-range-across-three-files-is-three-types -->
Declarations of `MyInt = int(0 to 1000)` in different files are different types however many there are.
`alpha/` and `beta/` each export one and name their own through their directory, because each sees the
other's export too; `main.maxon` declares none, since a third of its own would leave its bare `MyInt`
ambiguous. `alpha.doubleIt`'s result does not pass to `beta.tripleIt`: the crossing is refused.
```maxon
// --- file: alpha/a.maxon
export typealias MyInt = int(0 to 1000)

export function doubleIt(x alpha.MyInt) returns alpha.MyInt
	return x + x
end 'doubleIt'

// --- file: beta/b.maxon
export typealias MyInt = int(0 to 1000)

export function tripleIt(x beta.MyInt) returns beta.MyInt
	return x + x + x
end 'tripleIt'

// --- file: main.maxon
function main() returns ExitCode
	print("{tripleIt(doubleIt(4))}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:10: argument type mismatch for 'x': expected 'beta.MyInt', got 'alpha.MyInt'
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
emitted code holds ONE body and exactly ONE range cascade — the narrowing at `main`'s `as ExitCode`,
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
else — the emitted code shows no range cascade in `widen`, exactly as `a-widening-cast-emits-no-guard` shows for
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

<!-- test: error.an-implements-parent-another-file-keeps-private-is-refused -->
A file-private parent is nameable from its own file alone, so another file's `implements` clause naming it is
refused at the parent's name, exactly as a cast to it is.
```maxon
// --- file: base.maxon
typealias Base = int(0 to 100)

export function widest() returns ExitCode
	let b = 9 as Base
	return b
end 'widest'

// --- file: main.maxon
typealias Kid = int(0 to 10) implements Base

function main() returns ExitCode
	let k = 4 as Kid
	print("{k}")
	return widest()
end 'main'
```
```maxoncstderr
error E3008: <fragment>:11:41: typealias 'Base' is not exported
```

<!-- test: error.an-implements-parent-two-directories-export-is-ambiguous -->
Two directories export `Base`, so a bare `implements Base` from a third file could mean either and must be
qualified.
```maxon
// --- file: api/base.maxon
export typealias Base = int(0 to 100)

// --- file: legacy/base.maxon
export typealias Base = int(0 to 200)

// --- file: main.maxon
typealias Kid = int(0 to 10) implements Base

function main() returns ExitCode
	let k = 4 as Kid
	return k
end 'main'
```
```maxoncstderr
error E3063: <fragment>:9:41: Ambiguous type name 'Base': more than one visible declaration matches it. Qualify it as one of: api.Base, legacy.Base
```

<!-- test: error.a-qualified-implements-parent-the-reader-cannot-see-is-refused -->
A qualified parent is held to the declaration's tier like every other qualified type name: a `module` alias is
not nameable from outside its directory.
```maxon
// --- file: feature/types.maxon
module typealias Base = int(0 to 100)

// --- file: other/main.maxon
typealias Kid = int(0 to 10) implements feature.Base

function main() returns ExitCode
	let k = 4 as Kid
	return k
end 'main'
```
```maxoncstderr
error E3088: other/<fragment>:6:41: typealias 'feature.Base' is module-scoped and not visible from this directory
```
