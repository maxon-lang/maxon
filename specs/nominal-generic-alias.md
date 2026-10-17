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

A cast between two brands of one instance is a pure re-brand — the emitted code of `as-rebrands-both-ways`
shows the retag folded away. A cast to a DIFFERENT instance is a re-brand too when the two instances'
elements differ only by declaration and share one layout (underlying type, range, domain). Any other cast to
a different instance is E3131, for the storage reason `type-casting.md` states.

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
	var t = 0 as Integer
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

<!-- test: error.an-unbranded-var-adopts-the-brand-of-its-first-branded-store -->
A `var` initialized from a literal carries no brand until the first branded value is stored into it; from that store it is a variable of that brand for its whole life, and a store of another brand is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	var a = [3 as Integer]
	a = xs
	a = ys
	print("{a.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:2: cannot assign a value of type 'Ys' to variable 'a', which holds 'Xs'
```

<!-- test: an-unbranded-var-that-adopted-a-brand-reaches-that-brands-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer

function sumXs(xs Xs) returns Integer
	var t = 0 as Integer

	for x in xs 'each'
		t = t + x
	end 'each'

	return t
end 'sumXs'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(4)
	var a = [3 as Integer]
	a = xs
	print("{sumXs(a)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: error.an-adopted-brand-is-the-brand-every-later-read-has -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function sumYs(ys Ys) returns Integer
	var t = 0 as Integer

	for y in ys 'each'
		t = t + y
	end 'each'

	return t
end 'sumYs'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(4)
	var a = [3 as Integer]
	a = xs
	print("{sumYs(a)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:10: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-cell-resident-var-adopts-its-first-branded-store -->
A variable a callee writes through lives in a cell, and the cell is as branded as the variable.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function reset(target Xs)
	target = Xs.create()
end 'reset'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	var a = [3 as Integer]
	a = xs
	reset(a)
	a = ys
	print("{a.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:2: cannot assign a value of type 'Ys' to variable 'a', which holds 'Xs'
```

<!-- test: error.branches-storing-different-brands-are-refused-at-the-second -->
The first branded store in source order fixes the brand, whichever branch it sits in.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function main() returns ExitCode
	let k = 3 as Integer
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	var a = [3 as Integer]

	if k > 2 'first'
		a = xs
	end 'first' else 'second'
		a = ys
	end 'second'

	print("{a.count()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:3: cannot assign a value of type 'Ys' to variable 'a', which holds 'Xs'
```

<!-- test: error.an-unbranded-global-adopts-the-brand-of-its-first-branded-store -->
A module `var` initialized from a literal carries no brand until the first branded value is stored into it; a store of another brand is refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["a"]

function main() returns ExitCode
	shared = Xs.create()
	shared = Ys.create()
	print("{shared.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:2: cannot assign a value of type 'Ys' to global 'shared', which holds 'Xs'
```

<!-- test: error.two-files-storing-different-brands-into-one-global-are-judged-in-file-order -->
The first store in the program's canonical file order fixes the brand, so the store in the later file is the one refused.
```maxon
// --- file: a.maxon
export function storeFirst()
	shared = Xs.create()
end 'storeFirst'

// --- file: b.maxon
export function storeSecond()
	shared = Ys.create()
end 'storeSecond'

// --- file: main.maxon
export typealias Xs = Array with String
export typealias Ys = Array with String
export var shared = ["a"]

function main() returns ExitCode
	storeFirst()
	storeSecond()
	print("{shared.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:2: cannot assign a value of type 'Ys' to global 'shared', which holds 'Xs'
```

<!-- test: error.two-files-storing-different-brands-into-one-global-are-judged-in-file-order-either-way -->
Naming the files the other way round moves the refusal to the other store: the order is the files', not the order they were parsed in.
```maxon
// --- file: a.maxon
export function storeSecond()
	shared = Ys.create()
end 'storeSecond'

// --- file: b.maxon
export function storeFirst()
	shared = Xs.create()
end 'storeFirst'

// --- file: main.maxon
export typealias Xs = Array with String
export typealias Ys = Array with String
export var shared = ["a"]

function main() returns ExitCode
	storeFirst()
	storeSecond()
	print("{shared.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:2: cannot assign a value of type 'Xs' to global 'shared', which holds 'Ys'
```

<!-- test: error.a-read-of-an-adopted-global-reaches-only-its-own-brands-parameter -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["a"]

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	shared = Xs.create()
	takeXs(shared)
	takeYs(shared)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: an-adopted-global-reaches-its-own-brands-parameter -->
```maxon
typealias Xs = Array with String

var shared = ["a"]

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function main() returns ExitCode
	shared = Xs.create()
	takeXs(shared)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: error.a-read-of-a-global-before-its-first-store-sees-the-adopted-brand -->
The brand is the variable's for its whole life, so a read in a function that runs before the adopting store is judged against it too.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["a"]

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function early()
	takeYs(shared)
end 'early'

function main() returns ExitCode
	early()
	shared = Xs.create()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-read-of-an-adopted-global-into-another-brands-local-is-refused -->
A read of the adopted global is a value of its brand wherever it lands, so a local of the other brand refuses it.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["a"]

function main() returns ExitCode
	shared = Xs.create()
	var ys = Ys.create()
	ys = shared
	print("{ys.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:2: cannot assign a value of type 'Xs' to variable 'ys', which holds 'Ys'
```

<!-- test: error.handing-an-unbranded-var-to-a-branded-by-reference-parameter-adopts-that-brand -->
The callee writes a value of the parameter's brand into the variable, so the call is the variable's first branded store.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	var a = ["a"]
	reset(a)
	takeXs(a)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:2: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: a-var-handed-to-a-branded-by-reference-parameter-reaches-that-brands-parameter -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	var a = ["a"]
	reset(a)
	takeYs(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: error.an-assignment-of-another-brand-after-a-hand-over-is-refused-at-the-assignment -->
The hand-over comes first in the source, so it fixes the brand and the later assignment is the store that disagrees.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	var a = ["a"]
	reset(a)
	a = Xs.create()
	print("{a.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:2: cannot assign a value of type 'Xs' to variable 'a', which holds 'Ys'
```

<!-- test: error.a-read-before-the-adopting-assignment-is-judged-against-the-adopted-brand -->
The brand is the variable's for its whole life, so a read that comes earlier in the source is judged against it too.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function main() returns ExitCode
	var a = ["a"]
	takeYs(a)
	a = Xs.create()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-module-var-handed-to-a-branded-by-reference-parameter-adopts-that-brand -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

var shared = ["a"]

function main() returns ExitCode
	reset(shared)
	takeXs(shared)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:2: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: error.comparing-two-brands-of-one-instance-is-refused -->
An equality passes its right operand to a `Self`-typed parameter, and that parameter takes the receiver's brand.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	let xs = Xs.create()
	let ys = Ys.create()

	if xs == ys 'same'
		print("same\n")
	end 'same'

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:8: argument type mismatch for 'other': expected 'Xs', got 'Ys'
```

<!-- test: error.equals-with-another-brand-is-refused -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	let xs = Xs.create()
	let ys = Ys.create()
	print("{xs.equals(ys)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:13: argument type mismatch for 'other': expected 'Xs', got 'Ys'
```

<!-- test: error.a-self-parameter-of-a-declared-generic-takes-the-receivers-brand -->
```maxon
typealias Count = int(0 to 100)

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function itemOf(other Self) returns T
		return other.item
	end 'itemOf'
end 'Box'

typealias CountsA = Box with Count
typealias CountsB = Box with Count

function main() returns ExitCode
	let a = CountsA.create(1)
	let b = CountsB.create(1)
	print("{a.itemOf(b)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:12: argument type mismatch for 'other': expected 'CountsA', got 'CountsB'
```

<!-- test: an-unbranded-argument-fits-a-self-parameter -->
```maxon
typealias Xs = Array with String

function main() returns ExitCode
	let xs = Xs.create()
	print("{xs.equals(["b"])}\n")
	print("{xs == xs}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
true
```

<!-- test: error.a-merge-of-unadopted-reads-carries-the-adopted-brand -->
A value merged from reads of an adopted global is a value of the adopted brand, so the door it reaches judges that brand.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["a"]

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function resetYs(ys Ys)
	ys = Ys.create()
end 'resetYs'

function pick(flag bool)
	takeYs(shared if flag else shared)
end 'pick'

function main() returns ExitCode
	shared = Xs.create()
	pick(true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-function-value-call-handing-a-var-to-a-branded-parameter-adopts-that-brand -->
The function value lands on `resetYs`, whose parameter is a `Ys`, so the call is the variable's first branded store.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

function resetYs(ys Ys)
	ys = Ys.create()
end 'resetYs'

function main() returns ExitCode
	var a = ["a"]
	let f = resetYs
	f(a)
	takeXs(a)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:2: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: error.a-function-value-call-whose-candidates-disagree-on-the-brand-is-refused -->
A function value of one type may land on a body declaring either brand, so no one brand can be adopted for the argument.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

typealias Reset = function(Xs)

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function resetYs(ys Ys)
	ys = Ys.create()
end 'resetYs'

function run(f Reset)
	var a = ["a"]
	f(a)
	print("{a.count()}\n")
end 'run'

function main() returns ExitCode
	run(resetXs)
	run(resetYs)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:3: argument type mismatch for 1: the function value may land on bodies that declare 'Xs' and 'Ys' for this parameter, so no one brand fits it
```

<!-- test: a-hand-over-to-an-overloaded-callee-adopts-the-brand-of-the-overload-the-call-resolves-to -->
The overload that takes the call's arguments decides the brand, not the first declaration of the name.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function reset(xs Xs, extra Integer)
	xs = Xs.create()
	print("{extra}\n")
end 'reset'

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'

typealias Integer = int(0 to 100)

function main() returns ExitCode
	var a = ["a"]
	reset(a)
	takeYs(a)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: error.a-field-of-an-inner-alias-holds-that-alias-as-its-brand -->
A field whose type is a generic instance is always spelled through an alias, so it holds that alias as its brand from its declaration and has no first store to adopt from.
```maxon
typealias Xs = Array with String

type Holder
	typealias Items = Array with String
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create()}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create()
	h.items = Xs.create()
	print("{h.items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:4: cannot assign a value of type 'Xs' to field 'items' of 'Holder', which holds 'Items'
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

<!-- test: error.a-trailing-doc-comment-keeps-a-fields-brand -->
A trailing doc comment on a field's line does not change the field's declared type.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

type Holder
	export var xs as Xs /// note

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

<!-- test: error.a-bare-generic-alias-parameter-refuses-another-alias-of-its-instance -->
`alpha/` names `Array with String` as `Rows`; `main.maxon` names the same instance `Mine`. Two aliases are two
types wherever they are declared, so a `Mine` does not reach a parameter declared `Rows`.
```maxon
// --- file: alpha/a.maxon
export typealias Rows = Array with String

// --- file: main.maxon
typealias Mine = Array with String

function total(rows Rows) returns ExitCode
	return rows.count() as ExitCode
end 'total'

function main() returns ExitCode
	var m = Mine.create()
	m.push("a")
	return total(m)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:9: argument type mismatch for 'rows': expected 'Rows', got 'Mine'
```

<!-- test: error.a-qualified-generic-alias-parameter-refuses-another-alias-of-its-instance -->
The same parameter spelled through its directory, `alpha.Rows`. A qualified spelling names the same alias as the
bare one, so it is refused the same way.
```maxon
// --- file: alpha/a.maxon
export typealias Rows = Array with String

// --- file: main.maxon
typealias Mine = Array with String

function total(rows alpha.Rows) returns ExitCode
	return rows.count() as ExitCode
end 'total'

function main() returns ExitCode
	var m = Mine.create()
	m.push("a")
	return total(m)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:9: argument type mismatch for 'rows': expected 'Rows', got 'Mine'
```

<!-- test: error.a-two-segment-qualified-generic-alias-parameter-refuses-another-alias -->
The same parameter spelled through two directories, `lib.inner.Xs`. It names the same alias as the bare
spelling, so a `Ys` is refused the same way.
```maxon
// --- file: lib/inner/rows.maxon
export typealias Xs = Array with String

// --- file: main.maxon
typealias Ys = Array with String

function total(xs lib.inner.Xs) returns ExitCode
	return xs.count() as ExitCode
end 'total'

function main() returns ExitCode
	var ys = Ys.create()
	ys.push("a")
	return total(ys)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:9: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: error.a-qualified-generic-alias-parameter-is-a-rows-inside-its-body -->
The other direction: inside `relay`, the parameter declared `alpha.Rows` is a `Rows`, so it does not reach a
parameter declared `Mine`.
```maxon
// --- file: alpha/a.maxon
export typealias Rows = Array with String

export function makeRows() returns Rows
	var rows = Rows.create()
	rows.push("a")
	return rows
end 'makeRows'

// --- file: main.maxon
typealias Mine = Array with String

function first(m Mine) returns ExitCode
	return m.count() as ExitCode
end 'first'

function relay(rows alpha.Rows) returns ExitCode
	return first(rows)
end 'relay'

function main() returns ExitCode
	return relay(makeRows())
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:9: argument type mismatch for 'm': expected 'Mine', got 'Rows'
```

<!-- test: a-generic-instance-spelled-bare-and-qualified-is-one-instance -->
`alpha/` writes its element `Integer` bare and `main.maxon` writes the same declaration as `alpha.Integer`. Both
spellings name one declaration, so `Rows` and `Mine` are two aliases of one instance and the `as` is a re-brand.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 1000)
export typealias Rows = Array with Integer

export function makeRows() returns Rows
	var rows = Rows.create()
	rows.push(7)
	return rows
end 'makeRows'

// --- file: main.maxon
typealias Mine = Array with alpha.Integer

function main() returns ExitCode
	let m = makeRows() as Mine
	print("{m.count()}")
	return (try m.get(0) otherwise 0) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
1
```

<!-- test: a-qualified-element-keeps-its-declaration-beside-a-contested-name -->
`alpha/` and `beta/` each export an `Integer`, `alpha/`'s over `int(0 to 1000)` and `beta/`'s over
`int(0 to 5)`: two declarations of one name. `main.maxon` sees both, so it names the one it means through its
directory, and `Wide`'s element is `alpha/`'s declaration, at a pushed element and at a literal adopted by a
field. `7` and `8` fit `alpha.Integer` and would not fit `beta.Integer`.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 1000)

// --- file: beta/b.maxon
export typealias Integer = int(0 to 5)

// --- file: main.maxon
typealias Wide = Array with alpha.Integer

type Holder
	var xs as Wide
	var seeds as Wide

	static function create() returns Self
		var xs = Wide.create()
		xs.push(7)
		return Self{xs: xs, seeds: [8]}
	end 'create'

	function first() returns alpha.Integer
		return (try xs.get(0) otherwise 0) + (try seeds.get(0) otherwise 0)
	end 'first'

	function count() returns ExitCode
		return xs.count() as ExitCode
	end 'count'
end 'Holder'

function seed() returns beta.Integer
	return 3
end 'seed'

function main() returns ExitCode
	let holder = Holder.create()
	let narrow = seed()
	print("{holder.count()}")
	return (holder.first() as ExitCode) + (narrow as ExitCode)
end 'main'
```
```exitcode
18
```
```stdout
1
```

<!-- test: a-generic-instance-over-a-qualified-cross-form-alias-is-one-instance -->
`api/` declares `Score` as a function alias and `legacy/` declares `Score` as a ranged alias: two
declarations of one name, of two forms, so `api/` and `app/` both write the element `api.Score`. `Scores` and
`Mine` are two aliases of one instance and the `as` is a re-brand.
```maxon
// --- file: api/score.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias Score = function(n Integer) returns Integer
export typealias Scores = Array with api.Score

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

export function makeScores() returns Scores
	var scores = Scores.create()
	scores.push(addOne)
	return scores
end 'makeScores'

// --- file: legacy/score.maxon
export typealias Score = int(0 to 100)

// --- file: app/main.maxon
typealias Mine = Array with api.Score

function main() returns ExitCode
	let m = makeScores() as Mine
	let h = try m.get(0) otherwise panic("makeScores pushed one score")
	let rank = 7 as legacy.Score
	print("{m.count()} {rank}")
	return h(6) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
1 7
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
	var t = 0 as Integer
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0 as Integer
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
	var t = 0 as Integer
	for v in c 'each'
		t = t + v
	end 'each'
	return t
end 'sumCol'

function main() returns ExitCode
	var rows = Rows.create()
	rows.push([1, 2])
	var total = 0 as Integer
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
	var t = 0 as Integer
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	var rows = Inline.create()
	rows.push([1, 2])
	rows.push([3])
	var acc = 0 as Integer
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

	static function create(v T) returns Self
		return Self{item: v}
	end 'create'

	function bump() returns Self
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
	var t = 0 as Integer
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
	var t = 0 as Integer
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0 as Integer
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

### `as` — a re-brand costs nothing, and a different element layout is still not a cast target

<!-- test: as-rebrands-both-ways -->
The cast changes the brand and nothing else — the emitted code shows no operation surviving for either `as`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Xs = Array with Integer
typealias Ys = Array with Integer

function sumXs(xs Xs) returns Integer
	var t = 0 as Integer
	for x in xs 'each'
		t = t + x
	end 'each'
	return t
end 'sumXs'

function sumYs(ys Ys) returns Integer
	var t = 0 as Integer
	for y in ys 'each'
		t = t + y
	end 'each'
	return t
end 'sumYs'

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(20)
	xs.push(22)
	view ys = xs as Ys
	view back = ys as Xs
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

<!-- test: a-cast-between-nested-instances-over-one-declaration-is-a-rebrand -->
`alpha/` writes its nested element `Integer` bare and `main.maxon` writes the same declaration as
`alpha.Integer`, one level down. Both spellings name one declaration at every level, so `Grid` and `Mine` are
two aliases of one instance and the `as` is a re-brand.
```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(0 to 1000)
export typealias Grid = Array with (Array with Integer)

export function makeGrid() returns Grid
	var grid = Grid.create()
	grid.push([7])
	return grid
end 'makeGrid'

// --- file: main.maxon
typealias Mine = Array with (Array with alpha.Integer)

function main() returns ExitCode
	let m = makeGrid() as Mine
	let row = try m.get(0) otherwise panic("makeGrid pushed one row")
	print("{m.count()}")
	return (try row.get(0) otherwise 0) as ExitCode
end 'main'
```
```exitcode
7
```
```stdout
1
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

<!-- test: error.a-cast-between-vectors-of-two-sizes-is-not-a-rebrand -->
Two files of one directory each declare a file-private `Limit` over one range. `Wide` is a `Vector with 8`
of `pkg/lib.maxon`'s and `Quad` a `Vector with 4` of `pkg/main.maxon`'s: the elements share a layout but the containers do not, and the cast is
refused.
```maxon
// --- file: pkg/lib.maxon
typealias Limit = int(0 to 9)
typealias Wide = Vector with 8 Limit

export type Holder
	export var slots as Wide

	export static function make() returns Holder
		return Holder{slots: Wide.create()}
	end 'make'
end 'Holder'

// --- file: pkg/main.maxon
typealias Limit = int(0 to 9)
typealias Quad = Vector with 4 Limit

function main() returns ExitCode
	let q = Holder.make().slots as Quad
	return q.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3131: pkg/<fragment>:19:30: Cannot cast to 'Quad': a container's elements have a storage layout of their own, so 'Wide' cannot be retagged as one — build the container with the element type you need, or convert it element by element
```

<!-- test: a-cast-between-a-library-and-an-authors-same-layout-element-is-a-rebrand -->
This file declares a `Count` of its own over the library's range. `Array with Count` and
`Array with stdlib.Count` are two instances whose elements differ only by declaration: one underlying
type, one range, one domain, so one storage layout. The explicit `as` re-brands the container, and the
result is a `Counts` that reaches a `Counts` parameter and reads back the elements pushed as `stdlib.Count`.
```maxon
typealias Count = int(0 to u64.max)
typealias Counts = Array with Count
typealias LibCounts = Array with stdlib.Count

function firstOf(xs Counts) returns Count
	return try xs.get(0) otherwise panic("the caller pushed two counts")
end 'firstOf'

function main() returns ExitCode
	var theirs = LibCounts.create()
	theirs.push(3)
	theirs.push(4)
	let mine = theirs as Counts
	print("{mine.count()} {firstOf(mine)}")
	return mine.count() as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
2 3
```

<!-- test: error.a-cast-between-elements-of-two-ranges-is-refused -->
Two files of one directory each declare a file-private `Limit`, over two ranges of one byte width. `Lows`
is an `Array` of `pkg/lib.maxon`'s `Limit` and `Highs` an `Array` of `pkg/main.maxon`'s: the elements share
a name but not a range, so their layouts differ and the cast is refused.
```maxon
// --- file: pkg/lib.maxon
typealias Limit = int(0 to 9)
typealias Lows = Array with Limit

export type Holder
	export var slots as Lows

	export static function make() returns Holder
		return Holder{slots: Lows.create()}
	end 'make'
end 'Holder'

// --- file: pkg/main.maxon
typealias Limit = int(0 to 99)
typealias Highs = Array with Limit

function main() returns ExitCode
	let h = Holder.make().slots as Highs
	return h.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3131: pkg/<fragment>:19:30: Cannot cast to 'Highs': a container's elements have a storage layout of their own, so 'Lows' cannot be retagged as one — build the container with the element type you need, or convert it element by element
```

<!-- test: error.a-managed-buffer-of-one-brand-does-not-reach-another-brands-parameter -->
<!-- tier: stdlib -->
`Xs` and `Ys` are two brands of one instance. `ys.managed` is `ys`'s own record, so handing it to a
parameter declared `Xs` crosses from `Ys` to `Xs` without the `as` that door requires.
```maxon
typealias Xs = Array with Byte
typealias Ys = Array with Byte

function first(x Xs) returns ExitCode
	return (try x.get(0) otherwise 0) as ExitCode
end 'first'

function main() returns ExitCode
	var ys = Ys.create()
	ys.push(7)
	return first(ys.managed)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:9: argument type mismatch for 'x': expected 'Xs', got '__ManagedMemory with Byte'
```

<!-- test: a-managed-view-reaches-a-parameter-of-its-own-arrays-alias -->
<!-- tier: stdlib -->
The control for the refusal above: `a.managed` keeps `a`'s brand, so it reaches a parameter declared with the
alias `a` was built from, without a cast.
```maxon
typealias Bytes = Array with Byte

function first(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'first'

function main() returns ExitCode
	var a = Bytes.create()
	a.push(7)
	return first(a.managed)
end 'main'
```
```exitcode
7
```

<!-- test: error.a-view-binding-does-not-take-another-aliass-view -->
<!-- tier: stdlib -->
A view keeps its array's brand wherever it is held, not only at a parameter. `m` is bound from
`bytes.managed`, so it holds a view under `Bytes`; `other.managed` is under `Other`, and the rebind crosses
from one brand to the other.
```maxon
typealias Bytes = Array with Byte
typealias Other = Array with Byte

function main() returns ExitCode
	var bytes = Bytes.create()
	bytes.push(7)
	var other = Other.create()
	other.push(9)
	var m = bytes.managed
	m = other.managed
	return (try m.get(0) otherwise 0) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type '__ManagedMemory with Byte' (the view of 'Other') to variable 'm', which holds '__ManagedMemory with Byte' (the view of 'Bytes')
```

<!-- test: error.a-view-join-does-not-merge-two-aliases-views -->
<!-- tier: stdlib -->
The same crossing at a join: the two arms of the `match` give views under two brands.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Bytes = Array with Byte
typealias Other = Array with Byte

function pick(k Integer, bytes Bytes, other Other) returns ExitCode
	let m = match k 'm'
		0 gives bytes.managed
		default gives other.managed
	end 'm'
	return (try m.get(0) otherwise 0) as ExitCode
end 'pick'

function main() returns ExitCode
	return pick(0, bytes: [7], other: [9])
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:10: match arms give incompatible types: '__ManagedMemory with Byte' (the view of 'Other') vs '__ManagedMemory with Byte' (the view of 'Bytes')
```

<!-- test: an-unbranded-view-reaches-a-branded-parameter-of-its-instance -->
<!-- tier: stdlib -->
A row read out of `Rows` is unbranded, because `Rows` spells its element inline, so its view carries no
brand either and decays into `Bytes`, a brand of the same instance.
```maxon
typealias Bytes = Array with Byte
typealias Rows = Array with (Array with Byte)

function first(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'first'

function main() returns ExitCode
	var rows = Rows.create()
	rows.push([7, 9])
	view row = try rows.get(0) otherwise panic("no row")
	print("{first(row.managed)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
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
`File.FileSize`, `Clock.DurationMs`, `Array.Count` — and `int(0 to u64.max)` is written 318
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

<!-- test: a-type-parameter-slot-of-a-concrete-instance-adopts-a-literal -->
### A method of a concrete instance names the slot its type parameter stands for
`Map.upsert`'s `value` is declared `Value`, which `ItemIndex` fixes to `Items`, so a literal written there is
built as an `Items`; and an `otherwise` literal is built as the `Items` the lookup it falls back from hands
back.
```maxon
typealias Key = int(0 to 1000)
typealias Items = Array with Key
typealias ItemIndex = Map with (Key, Items)

function main() returns ExitCode
	var index = ItemIndex.create()
	index.upsert(500, value: [1, 2])
	view found = try index.get(500) otherwise [0]
	print("{index.count()} {found.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: a-clone-of-a-declared-generics-instance-is-a-record-of-its-own -->
### A clone of an instance copies what its type argument fixes
`b.clone()` on a `Box with String` copies the `String` the instance holds, so writing the copy's field leaves
the source's unchanged.
```maxon
type Box uses T
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias Label = Box with String

function main() returns ExitCode
	let original = Label.create("first")
	var copy = original.clone()
	copy.item = "second"
	print("{original.item} {copy.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
first second
```

<!-- test: error.a-union-payload-meeting-a-read-adopted-later-is-judged-against-the-adopted-brand -->
The read `a` has no brand when the payload is built; the call after it gives `a` the brand `Xs`, and the payload holds `Ys`.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

union Slot
	held(v Ys)
	empty
end 'Slot'

function main() returns ExitCode
	var a = ["a"]
	var s = Slot.held(a)
	resetXs(a)

	match s 'go'
		held(v) then print("{v.count()}\n")
		empty then print("empty\n")
	end 'go'

	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'
```
```maxoncstderr
error E3005: <fragment>:12:21: cannot assign a value of type 'Xs' to union payload 'v', which holds 'Ys'
```

<!-- test: error.a-container-element-meeting-a-read-adopted-later-is-judged-against-the-adopted-brand -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String
typealias Rows = Array with Ys

function main() returns ExitCode
	var a = ["a"]
	var rows = Rows.create()
	rows.push(a)
	resetXs(a)
	print("{rows.count()}\n")
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'
```
```maxoncstderr
error E3005: <fragment>:9:7: cannot assign a value of type 'Xs' to the element of 'push', which holds 'Ys'
```

<!-- test: error.a-tuple-column-meeting-a-read-adopted-later-is-judged-against-the-adopted-brand -->
The tuple names its column `Ys` where it is built, so the column is judged like a field of that brand.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String
typealias Count = int(0 to 100)

function main() returns ExitCode
	var a = ["a"]
	print("{pairCount((a, 1))}\n")
	resetXs(a)
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function pairCount(p (Ys, Count)) returns Count
	return p.0.count() as Count
end 'pairCount'
```
```maxoncstderr
error E3005: <fragment>:8:20: cannot assign a value of type 'Xs' to field '_0' of '__Tuple2.Array_String@Ys.Count$0_100', which holds 'Ys'
```

<!-- test: error.the-first-store-fixes-the-brand-even-when-it-stores-a-read-adopted-later -->
`b = a` stores a value whose brand is settled later as `Xs`, and it is the first branded store of `b`, so `b` is an `Xs` and the store after it is the one refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	var b = ["b"]
	b = a
	b = Ys.create()
	resetXs(a)
	print("{b.count()}\n")
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'
```
```maxoncstderr
error E3005: <fragment>:9:2: cannot assign a value of type 'Ys' to variable 'b', which holds 'Xs'
```

<!-- test: error.a-var-holding-a-read-adopted-later-takes-its-brand-and-is-judged-at-a-hand-over -->
`b` is given `a`'s brand by the store, which comes first, so handing it to a `Ys` parameter is the one refusal.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	resetXs(a)
	var b = ["b"]
	b = a
	touchYs(b)
	print("{b.count()}\n")
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function touchYs(ys Ys)
	ys = Ys.create()
end 'touchYs'
```
```maxoncstderr
error E3005: <fragment>:10:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-hand-over-before-a-store-of-a-read-adopted-later-fixes-the-brand-and-the-store-is-refused -->
The hand-over comes first, so `b` is a `Ys` and the store of `a`, whose brand is settled later as `Xs`, is the one refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function touchYs(ys Ys)
	ys = Ys.create()
end 'touchYs'

function main() returns ExitCode
	var b = ["b"]
	touchYs(b)
	var a = ["a"]
	b = a
	resetXs(a)
	print("{b.count()}\n")
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'
```
```maxoncstderr
error E3005: <fragment>:13:2: cannot assign a value of type 'Xs' to variable 'b', which holds 'Ys'
```

<!-- test: error.a-global-stored-a-read-adopted-later-takes-its-brand -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

var shared = ["s"]

function main() returns ExitCode
	var a = ["a"]
	resetXs(a)
	shared = a
	takeYs(shared)
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'
```
```maxoncstderr
error E3005: <fragment>:11:2: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-merge-of-a-read-adopted-later-and-a-branded-value-is-judged-at-the-merge -->
Whichever arm is taken, the merged value would have to be an `Xs` and a `Ys` at once.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	let ys = Ys.create()
	let c = true
	var z = a if c else ys
	resetXs(a)
	takeXs(z)
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'
```
```maxoncstderr
error E3005: <fragment>:9:12: the arms of this merge hold 'Xs' and 'Ys', so the merged value has no one brand
```

<!-- test: error.a-merge-of-two-reads-adopted-later-is-judged-for-every-arm -->
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	var b = ["b"]
	let c = true
	var z = a if c else b
	resetXs(a)
	resetYs(b)
	takeXs(z)
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function resetYs(ys Ys)
	ys = Ys.create()
end 'resetYs'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'
```
```maxoncstderr
error E3005: <fragment>:9:12: the arms of this merge hold 'Xs' and 'Ys', so the merged value has no one brand
```

<!-- test: a-merge-of-two-reads-adopted-alike-wears-the-adopted-brand -->
```maxon
typealias Xs = Array with String

function main() returns ExitCode
	var a = ["a"]
	var b = ["b", "c"]
	let c = true
	view z = a if c else b
	resetXs(a)
	resetXs(b)
	takeXs(z)
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'
```
```stdout
1
```
```exitcode
0
```

<!-- test: error.a-closure-capturing-a-var-adopted-later-reads-its-adopted-brand -->
The closure is the first reader of `a`, so the capture is where its brand is carried from.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String
typealias Count = int(0 to 100)

function main() returns ExitCode
	var a = ["a"]
	resetXs(a)
	let f = function() gives countYs(a)
	print("{f()}\n")
	return 0
end 'main'

function resetXs(xs Xs)
	xs = Xs.create()
end 'resetXs'

function countYs(ys Ys) returns Count
	return ys.count() as Count
end 'countYs'
```
```maxoncstderr
error E3005: <fragment>:9:27: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: a-parameter-spelled-with-the-receivers-bare-base-fits-any-brand-of-its-instance -->
Only a parameter spelled `Self` takes the receiver's brand. One spelled with the generic base is an ordinary parameter, and an ordinary parameter of a bare instance fits every brand of it.
```maxon
typealias Count = int(0 to 100)

type Box uses T
	export let item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function itemOf(other Box) returns T
		return other.item
	end 'itemOf'
end 'Box'

typealias CountsA = Box with Count
typealias CountsB = Box with Count

function main() returns ExitCode
	let a = CountsA.create(1)
	let b = CountsB.create(2)
	print("{a.itemOf(b)}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: error.two-vars-each-first-storing-the-other-resolve-from-the-first-store-and-the-later-direct-store-is-refused -->
`b = a` and `a = b` each store a value whose brand is settled later. Resolution starts at the first store, `b = a`, which needs `a`'s brand. `a`'s first store, `a = b`, needs `b`'s brand, which is the one being resolved, so it carries nothing and `a` adopts the `Xs` of its direct store. `b = a` then carries `Xs`, `b` is an `Xs`, and the direct `Ys` store into it is the one refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	var b = ["b"]
	b = a
	a = b
	a = Xs.create()
	b = Ys.create()
	print("{a.count()}{b.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'Ys' to variable 'b', which holds 'Xs'
```

<!-- test: error.a-return-meeting-a-read-adopted-later-is-judged-against-the-adopted-brand -->
`return a` meets a read whose brand is settled later as `Xs`, and the function returns `Ys`.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function launder() returns Ys
	var a = ["a"]
	refill(a)
	return a
end 'launder'

function main() returns ExitCode
	let r = launder()
	print("{r.count()}\n")
	return 0
end 'main'

function refill(xs Xs)
	xs = Xs.create()
end 'refill'
```
```maxoncstderr
error E3005: <fragment>:8:2: cannot assign a value of type 'Xs' to the return value of 'launder', which holds 'Ys'
```

<!-- test: error.a-chain-resolves-through-the-first-branded-store-of-the-var-it-reads -->
`b` reads `a` before it is given a `Ys`, so `b` is an `Xs` for every var that reads it: `c = b` carries `Xs`, and only the store of `ys` into `b` is refused.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	var a = ["a"]
	var b = ["b"]
	var c = ["c"]
	b = a
	c = b
	var ys = Ys.create()
	b = ys
	refill(a)
	takeXs(c)
	return 0
end 'main'

function refill(xs Xs)
	xs = Xs.create()
end 'refill'

function takeXs(xs Xs)
	print("{xs.count()}\n")
end 'takeXs'
```
```maxoncstderr
error E3005: <fragment>:12:2: cannot assign a value of type 'Ys' to variable 'b', which holds 'Xs'
```

<!-- test: error.an-indirect-call-argument-of-another-brand-is-refused -->
The function value is `takeYs`, whose parameter is a `Ys`.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

function main() returns ExitCode
	let a = Xs.create()
	let f = takeYs
	f(a)
	return 0
end 'main'

function takeYs(ys Ys)
	print("{ys.count()}\n")
end 'takeYs'
```
```maxoncstderr
error E3005: <fragment>:8:3: argument type mismatch for 1: expected 'Ys', got 'Xs'
```

<!-- test: a-tuple-alias-over-a-brand-declared-after-its-uses-is-the-tuple-a-literal-builds -->
The tuple type is named where the alias is declared, and a literal of the same elements is that type, whichever of the declarations the file reads first.
```maxon
function make(x Xs) returns Pair
	return (x, 1)
end 'make'

function total(p Pair) returns Count
	return p.0.count() as Count
end 'total'

function main() returns ExitCode
	var x = Xs.create()
	x.push("a")
	x.push("b")
	let p = make(x)
	print("{total(p)}\n")
	var y = Xs.create()
	y.push("c")
	let q = (y, 2 as Count)
	print("{total(q)}\n")
	return 0
end 'main'

typealias Pair = (Xs, Count)
typealias Count = int(0 to 100)
typealias Xs = Array with String
```
```stdout
2
1
```
```exitcode
0
```

<!-- test: error.the-arms-of-a-three-way-merge-are-judged-against-each-other -->
The first arm's var has no brand of its own, so neither of the other two conflicts with it; the second and third conflict with each other.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String

enum Pick
	one
	two
	three
end 'Pick'

function main() returns ExitCode
	var a = ["a"]
	var b = ["b"]
	let ys = Ys.create()
	refill(a)
	let choice = Pick.two
	var z = match choice 'pick'
		one gives b
		two gives a
		three gives ys
	end 'pick'
	print("{z.count()}\n")
	return 0
end 'main'

function refill(xs Xs)
	xs = Xs.create()
end 'refill'
```
```maxoncstderr
error E3005: <fragment>:17:10: the arms of this merge hold 'Xs' and 'Ys', so the merged value has no one brand
```

<!-- test: error.a-service-message-argument-of-another-brand-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The message parameter is a `Ys`; the argument is an `Xs`.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String
typealias Count = int(0 to 100)

type Sink
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(ys Ys)
		self.seen = self.seen + 1
		print("{ys.count()}\n")
	end 'take'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	let a = Xs.create()
	h.take(a)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:22:9: argument type mismatch for 'ys': expected 'Ys', got 'Xs'
```

<!-- test: error.a-service-message-argument-adopted-later-is-judged-against-the-parameter-brand -->
<!-- unsupported-targets: wasm32-wasi -->
`a` takes the brand `Xs` from the call that follows its creation, and the message parameter is a `Ys`.
```maxon
typealias Xs = Array with String
typealias Ys = Array with String
typealias Count = int(0 to 100)

type Sink
	var seen as Count

	static function create() returns Self
		return Self{seen: 0}
	end 'create'

	export function take(ys Ys)
		self.seen = self.seen + 1
		print("{ys.count()}\n")
	end 'take'
end 'Sink'

function main() returns ExitCode
	let h = spawn Sink.create()
	var a = ["a"]
	refill(a)
	h.take(a)
	return 0
end 'main'

function refill(xs Xs)
	xs = Xs.create()
end 'refill'
```
```maxoncstderr
error E3005: <fragment>:23:9: cannot assign a value of type 'Xs' to union payload 'ys', which holds 'Ys'
```
