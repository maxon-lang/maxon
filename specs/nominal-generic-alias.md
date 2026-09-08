---
feature: nominal-generic-alias
status: stable
keywords: [typealias, generics, nominal-types, brand, type-safety, cast, as, array]
category: type-system
---

# A Generic-Instance `typealias` Is a Brand

## Documentation

`typealias Xs = Array with Integer` and `typealias Ys = Array with Integer` name ONE generic instance —
one layout, one method set, one element type — under two BRANDS. A brand is a name that rides beside
the instance identity and is compared only after the identities agree: an `Xs` never flows into a `Ys`
slot — a parameter, a rebind, an `otherwise`, a `match` arm, a field, a payload, a global — unless the
author writes `xs as Ys`. A `return` is the one door that carries the cast itself: `return xs` from a
`returns Ys` function is `return xs as Ys`, a re-brand at no cost. A DIFFERENT instance is still refused
at the `return`.

```text
typealias Xs = Array with Integer
typealias Ys = Array with Integer

sumYs(xs)          // E3005: expected 'Ys', got 'Xs'
sumYs(xs as Ys)    // a re-brand: no operation survives to codegen
sumYs([1, 2, 3])   // a literal carries no brand and fits any
```

A cast between two brands of one instance is a pure re-brand — the golden for `as-rebrands-both-ways`
shows the retag folded away. A cast to a DIFFERENT instance is still E3131, for the storage reason
`type-casting.md` states.

**What carries a brand:** a call result (the callee's declared return alias), a `Self` result (the
receiver's brand), a field read (the field's declared alias), a parameter, a loop variable bound from a
branded element type, a global. **What carries none:** a `[...]` literal, and a merge whose arms are all
unbranded.

**Brands are shallow.** `Array with Xs` spelled through the alias `Xs` and `Array with (Array with
Integer)` spelled inline are one instance; a nested brand is enforced at the ELEMENT door, through the
leaf, so a row read out of the first is an `Xs` and a row read out of the second is unbranded.

## Tests

### The doors — one refusal each, and the literal that fits them all

<!-- test: error.xs-into-ys-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function sumYs(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	let r = sumYs(xs)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:10: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: an-xs-converts-at-a-ys-return -->
`return xs` from a `returns Ys` function is `return xs as Ys`: the same record under the declared brand.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function makeYs() returns Ys
	var xs = Xs.create()
	xs.push(1)
	return xs
end 'makeYs'

function main() returns ExitCode
	let ys = makeYs()
	print("{ys.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
```

<!-- test: error.a-different-instance-returned-is-still-refused -->
The line that does not move: `WideCol` and `NarrowCol` are two INSTANCES (eight-byte and one-byte
elements), not two brands of one, so the `return` refuses exactly as an argument would.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 63)
typealias WideCol = Array with Wide
typealias NarrowCol = Array with Narrow

function makeNarrow() returns NarrowCol
	var w = WideCol.create()
	w.push(5)
	return w
end 'makeNarrow'

function main() returns ExitCode
	print("{makeNarrow().count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:2: Cannot return 'WideCol' from function declared to return 'NarrowCol'
```

<!-- test: error.rebind-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	ys = xs
	print("{ys.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'Xs' to variable 'ys', which holds 'Ys'
```

<!-- test: error.otherwise-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

enum Fault implements Error
	failed
end 'Fault'

function mayFail() returns Ys throws Fault
	throw Fault.failed
end 'mayFail'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	let ys = try mayFail() otherwise xs
	print("{ys.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3059: <fragment>:17:11: type mismatch: 'otherwise type 'Xs' does not match expected type 'Ys''
```

<!-- test: error.match-arms-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function pick(k Integer, xs Xs, ys Ys) returns Ys
	let r = match k 'm'
		0 gives ys
		default gives xs
	end 'm'
	return r
end 'pick'

function main() returns ExitCode
	let r = pick(0, xs: [1], ys: [2])
	print("{r.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:10: match arms give incompatible types: 'Xs' vs 'Ys'
```

<!-- test: error.struct-literal-field-store-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

type Holder
	export var xs as Xs

	static function create(ys Ys) returns Self
		return Self{xs: ys}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create([1])
	print("{h.xs.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:15: cannot assign a value of type 'Ys' to field 'xs' of 'Holder', which holds 'Xs'
```

<!-- test: error.union-payload-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

union Slot
	held(v Ys)
	empty
end 'Slot'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	let s = Slot.held(xs)
	match s 'go'
		held(v) then return v.count() as ExitCode
		empty then return 1
	end 'go'
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:22: type mismatch: 'expected Ys, got Xs'
```

<!-- test: error.global-across-brands -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

var shared = Xs.create()

function main() returns ExitCode
	var ys = Ys.create()
	ys.push(1)
	shared = ys
	print("{shared.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'Ys' to global 'shared', which holds 'Xs'
```

<!-- test: a-literal-decays-into-any-brand -->
The decay control for every door above: a `[...]` literal carries no brand and is accepted at an
argument, a `return`, a struct-literal field, a rebind, a global store, an `otherwise`, a union payload
and both `match` arms.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

enum Fault implements Error
	failed
end 'Fault'

union Slot
	held(v Ys)
	empty
end 'Slot'

type Holder
	export var xs as Xs

	static function create() returns Self
		return Self{xs: [1]}
	end 'create'
end 'Holder'

var shared = Xs.create()

function sumXs(xs Xs) returns Integer
	var t = 0
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function makeXs() returns Xs
	return [2]
end 'makeXs'

function mayFail() returns Ys throws Fault
	throw Fault.failed
end 'mayFail'

function pick(k Integer) returns Xs
	return match k 'm'
		0 gives [4]
		default gives [8]
	end 'm'
end 'pick'

function main() returns ExitCode
	let h = Holder.create()
	var xs = makeXs()
	xs = [16]
	shared = [32]
	let fallback = try mayFail() otherwise [64]
	let s = Slot.held([128])
	let payload = match s 'go'
		held(v) gives sumYs(v)
		empty gives 0
	end 'go'
	let sum = sumXs(h.xs) + sumXs(xs) + sumXs(shared) + sumYs(fallback) + sumXs(pick(0)) + sumXs(pick(1)) + payload
	print("{sum}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
253
```

### Where a brand comes from

<!-- test: error.a-loop-over-rows-carries-the-element-brand -->
`Rows = Array with Row` is spelled through the alias `Row`, so `for row in rows` binds a `Row` — and a
`Row` is not a `Col`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Row = Array with Integer
typealias Rows = Array with Row
typealias Col = Array with Integer

function sumCol(c Col) returns Integer
	var t = 0
	for v in c 'each'
		t = t + v
	end 'each'
	return t
end 'sumCol'

function main() returns ExitCode
	var rows = Rows.create()
	rows.push([1, 2])
	var total = 0
	for row in rows 'each'
		total = total + sumCol(row)
	end 'each'
	print("{total}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:19: argument type mismatch for 'c': expected 'Col', got 'Row'
```

<!-- test: an-inline-spelled-element-carries-no-brand -->
The shallow half: `Array with (Array with Integer)` names its element INLINE, so a row read out of it is
unbranded and fits both `Xs` and `Ys`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer
typealias Inline = Array with (Array with Integer)

function first(xs Xs) returns Integer
	return try xs.get(0) otherwise 0
end 'first'

function total(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	var rows = Inline.create()
	rows.push([1, 2])
	rows.push([3])
	var acc = 0
	for row in rows 'each'
		acc = acc + first(row) + total(row)
	end 'each'
	print("{acc}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: error.a-self-result-keeps-the-receivers-brand -->
`bump()` is declared `returns Self`; called on an `A`, its result is an `A`.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box uses T
	export var item as T

	export static function create(v T) returns Self
		return Self{item: v}
	end 'create'

	export function bump() returns Self
		return Self{item: self.item}
	end 'bump'
end 'Box'

typealias A = Box with Integer
typealias B = Box with Integer

function takesB(b B) returns Integer
	return b.item
end 'takesB'

function main() returns ExitCode
	let a = A.create(4)
	let r = takesB(a.bump())
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:25:10: argument type mismatch for 'b': expected 'B', got 'A'
```

<!-- test: error.a-field-read-carries-the-fields-brand -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

type Holder
	export var xs as Xs

	static function create() returns Self
		return Self{xs: [5, 6]}
	end 'create'
end 'Holder'

function sumYs(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function main() returns ExitCode
	let h = Holder.create()
	let r = sumYs(h.xs)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:24:10: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: an-unbranded-merge-adopts-nothing -->
A conditional over two literals merges two unbranded arms; the result is unbranded and fits either slot.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function sumXs(xs Xs) returns Integer
	var t = 0
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function main() returns ExitCode
	let k = 3 as Integer
	let picked = [7] if k > 2 else [8, 9]
	print("{sumXs(picked)} {sumYs(picked)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 7
```

<!-- test: error.interface-impl-brand-must-match -->
A conformance compares the SPELLED alias — `take(xs Ys)` does not implement `take(xs Xs)` — and the
diagnostic prints the spellings, not the instance mint.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

interface Bag
	function take(xs Xs) returns Integer
end 'Bag'

type Sack implements Bag
	let n as Integer

	function take(xs Ys) returns Integer
		print("{xs.count()}")
		return n
	end 'take'

	static function create() returns Self
		return Self{n: 41}
	end 'create'
end 'Sack'

function main() returns ExitCode
	let s = Sack.create()
	var f = Ys.create()
	f.push(1)
	print("{s.take(f)}")
	return 0
end 'main'
```
```maxoncstderr
error E3016: <fragment>:10:6: Partial interface implementation: type 'Sack' has 1 method(s) with wrong signature:
  - take(xs Ys) returns Integer (expected take(xs Xs) returns Integer)
```

### `as` — a re-brand costs nothing, and a different instance is still not a cast target

<!-- test: as-rebrands-both-ways -->
The cast changes the brand and nothing else — the golden shows no operation surviving for either `as`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function sumXs(xs Xs) returns Integer
	var t = 0
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(20)
	xs.push(22)
	let ys = xs as Ys
	let back = ys as Xs
	print("{sumYs(ys)} {sumXs(back)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 42
```

<!-- test: error.as-to-a-different-instance-is-still-refused -->
The boundary of the re-brand: `WideCol` and `NarrowCol` are two INSTANCES (eight-byte and one-byte
elements), not two brands of one, and `type-casting.md`'s refusal stands unchanged.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 63)
typealias WideCol = Array with Wide
typealias NarrowCol = Array with Narrow

function widthOf(c NarrowCol) returns ExitCode
	return c.count() as ExitCode
end 'widthOf'

function main() returns ExitCode
	var w = WideCol.create()
	w.push(5)
	let n = w as NarrowCol
	return widthOf(n)
end 'main'
```
```maxoncstderr
error E3131: <fragment>:14:12: Cannot cast to 'NarrowCol': a container's elements have a storage layout of their own, so 'WideCol' cannot be retagged as one — build the container with the element type you need, or convert it element by element
```

<!-- test: a-literal-decays-into-a-narrow-ranged-brand -->
⭐ **THE DECAY CONTROL ABOVE USES A FULL-RANGE ELEMENT, AND THAT IS THE ONE CASE THE RULE CANNOT FAIL IN.**
`a-literal-decays-into-any-brand` declares `Integer = int(i64.min to i64.max)`, so its literal's own inferred
element type and the brand's element type are the same type and no adoption is needed to make them agree.
A brand over a NARROWER element is where "a literal carries no brand and fits any" has to do work.

⚠ **AND THE LANGUAGE ALREADY ANSWERS THIS EVERYWHERE ELSE.** A bare `3` adopts `Count` at a scalar
parameter, and `xs.push(1)` adopts it at an element store — both compile. An array literal is the only
position where the elements are typed by their own spelling instead of by the slot receiving them, which
makes a `[1, 2, 3]` an `Array with <full-range int>` that no `Count` brand will take. The four doors below
are the same adoption asked four ways, and a rule that answers two of them differently is not a rule about
ranges.

⚠ **WHERE THE ADOPTION STOPS, AND IT IS WORTH KNOWING BEFORE YOU MEET IT.** The slot must be one the compiler
can name before the argument is parsed, which excludes two positions: an OVERLOADED callee, because which
declaration a call resolves to is settled a whole pass later — so adding an unrelated overload of `totalXs`
turns the first line of `main` below into `E3005`, a refusal rather than a wrong answer — and a MODULE-SCOPE
initializer, whose literal is built by the constant folder from its own first element and is never offered a
slot at all.

⭐⭐ **"FITS ANY" IS GATED WITH *TWO* BRANDS, BECAUSE ONE BRAND CANNOT TELL IT FROM "FITS THE ONE".** `Xs` and
`Ys` are distinct brands over the identical narrow element, and the SAME literal reaches both — which is the
half of the rule a single-brand program leaves unmeasured. The refusal that bounds it is next door and
unchanged: a BUILT `Xs` at a `Ys` parameter is still `E3005`
(`error.xs-into-ys-parameter`), because a value that carries a brand keeps it.
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count
typealias Ys = Array with Count

function totalXs(xs Xs) returns Count
	var t = 0 as Count
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'totalXs'

function totalYs(ys Ys) returns Count
	var t = 0 as Count
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'totalYs'

function one(n Count) returns Count
	return n
end 'one'

function main() returns ExitCode
	let viaLiteral = totalXs([1, 2, 3])
	let viaOtherBrand = totalYs([1, 2, 3])
	let viaScalar = one(3)

	var built = Xs.create()
	built.push(1)
	built.push(2)
	let viaPush = totalXs(built)

	return (viaLiteral + viaOtherBrand + viaScalar + viaPush) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: an-eight-byte-ranged-element-is-adopted-too -->
### The element does not have to be NARROWER — it has to be a different TYPE
⛔⛔ **`int(0 to i64.max)` AND `int(0 to u64.max)` OCCUPY A MACHINE WORD, EXACTLY AS A BARE `int` DOES, AND
THAT IS THE CLASS A STRIDE COMPARISON CANNOT SEE.** They are stdlib's own shape — `Array.ElementIndex`,
`File.FileSize`, `Clock.DurationMs`, `Character.Utf8ByteCount` — and `int(0 to u64.max)` is written 318
times in this suite, so nothing here was measuring it. A literal at such a slot is adopted for the same
reason a narrow one is: the element is a different TYPE, whatever number of bytes it happens to take.

⚠ Both doors are gated, because they were both wrong: the ARGUMENT below, and the `from` head, which had
never accepted this class either.
```maxon
typealias NonNeg = int(0 to i64.max)
typealias NonNegArray = Array with NonNeg
typealias Unsigned = int(0 to u64.max)
typealias UnsignedArray = Array with Unsigned

function total(xs NonNegArray) returns NonNeg
	var t = 0 as NonNeg
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'total'

function size(ys UnsignedArray) returns Unsigned
	return ys.count() as Unsigned
end 'size'

function main() returns ExitCode
	let viaArgument = total([1, 2, 3]) as ExitCode
	let viaHead = size(UnsignedArray from [1, 2, 3]) as ExitCode

	return viaArgument + viaHead
end 'main'
```
```exitcode
9
```

<!-- test: error.an-adopted-element-is-still-range-checked -->
### Adoption CHECKS the elements; it does not assume them
⚠ **THE SAME-STRIDE CLASS IS WHERE THIS COULD HAVE BEEN LOST SILENTLY.** An element is sent through the
conversion door — the one that carries the range guard — and a slot whose element merely occupies the same
number of bytes must not skip it. `-1` is a machine word exactly as `1` is, and `int(0 to u64.max)` holds
neither sign bit nor any negative value.
```maxon
typealias Unsigned = int(0 to u64.max)
typealias UnsignedArray = Array with Unsigned

function total(xs UnsignedArray) returns Unsigned
	var t = 0 as Unsigned
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	return total([-1]) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:16: Value -1 is outside the range of 'Unsigned' (int(0 to 18446744073709551615))
```

<!-- test: a-conditional-hands-the-slot-to-both-arms -->
### A `… if … else …` is ONE value in the slot, so both arms are built at the slot's element type
⛔ **THE ARM THAT PARSES FIRST MUST NOT TAKE THE ANSWER AWAY FROM THE OTHER.** The true arm is parsed before
the `if` reveals the form, so a slot's answer keyed on the conditional's own first token is spent by the
time the false arm is read — and the two arms then merge as *"true branch is 'Xs' but false branch is
'Array_int'"*, a refusal stating that two textually identical literals are different types.
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count

function total(xs Xs) returns Count
	var t = 0 as Count
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	let taken = total([1, 2] if true else [30, 40])
	let skipped = total([1, 2] if false else [30, 40])

	return (taken + skipped) as ExitCode
end 'main'
```
```exitcode
73
```
