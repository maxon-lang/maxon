---
feature: generic-functions
status: experimental
keywords: [function, static, uses, where, generic, inference, Equatable, Comparable]
category: type-system
---

# Generic Functions

## Documentation

A free function or a `static` method may declare type parameters with `uses`, and constrain them with
`where`, in the same words a generic type uses. `uses` follows the parameter list and `where` closes the
declaration line, after `returns` and `throws`:

```text
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'
```

A call never names `T`: it is inferred from the arguments whose parameters are declared `T`.

- Arguments of one alias give that alias. Two different aliases give their nearest common ancestor under
  `implements`; two aliases with no common ancestor are refused (**E3180**).
- An argument with no alias — a literal, an arithmetic result over literals — adopts the alias of the
  other arguments, so `larger(xs.count(), b: 3)` is an `ElementIndex` and `larger(2 + 2, b: 4)` a plain
  `int`. A float literal is a `float`.
- Every type parameter must be the declared type of at least one parameter (**E3179**).
- An inferred type that does not conform to a `where` interface is **E3017**.
- A type parameter binds a number, `String`, `bool`, `Character`, a typealias, a record or an enum; an
  argument of any other kind is **E3181**.

Each distinct inferred type compiles its own copy of the function, in the declaring file, so the body is
checked against the concrete type: `a > b` is an integer comparison for `int`, and `"{a}"` interpolates
whatever the instantiation can print. Every integer and float type, every alias of one, `String`, `bool`
and `Character` are `Equatable`; the numeric types, `String` and `Character` are also `Comparable`. A
record that implements `Comparable` is ordered by `<`, `>`, `<=` and `>=` through its `compare`.

A copy may call its argument type's `where` requirements — `compare`, `equals`, any method of the
constraining interface — and the methods of an `extension` of that interface, whatever the type's
visibility: the caller that passed the type granted the conformance. Any other member of the type — a
method that is no requirement, or a field, even one named like a requirement — needs the type to be
visible from the declaring file, because a type hidden from a file hides its members there (**E3008**). A generic
function follows the visibility rules of any function, so a private one called from another file is
**E3008**.

A non-generic declaration of the same name whose parameter types are exactly the argument types is
chosen over a generic one. Two generic declarations that both infer are ambiguous (**E3007**).

## Tests

<!-- test: infers-from-plain-ints -->
```maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	let x = larger(3, b: 7)
	let y = larger(10, b: 2)
	print("{x} {y}\n")
	return 0
end 'main'
```
```stdout
7 10
```

<!-- test: infers-element-index-from-count-and-a-literal -->
```maxon
typealias Row = int(0 to 100)
typealias RowArray = Array with Row

function same(a T, b T) uses T returns T where T is Equatable
	if a == b 'equal'
		return a
	end 'equal'

	return b
end 'same'

function takesIndex(i ElementIndex) returns ElementIndex
	return i
end 'takesIndex'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(1)
	rows.push(2)
	rows.push(3)
	let n = same(rows.count(), b: 3)
	print("{takesIndex(n)}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: error.an-inferred-element-index-is-nominal -->
```maxon
typealias Row = int(0 to 100)
typealias RowArray = Array with Row

function same(a T, b T) uses T returns T where T is Equatable
	return a
end 'same'

function takesRow(r Row) returns Row
	return r
end 'takesRow'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(1)
	print("{takesRow(same(rows.count(), b: 3))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:10: argument type mismatch for 'r': expected 'Row', got 'ElementIndex'
```

<!-- test: two-aliases-infer-their-nearest-common-ancestor -->
```maxon
typealias Distance = int(0 to 1000)
typealias Meters = int(0 to 100) implements Distance
typealias Feet = int(0 to 300) implements Distance

function larger(a T, b T) uses T returns T
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function takesDistance(d Distance) returns Distance
	return d
end 'takesDistance'

function main() returns ExitCode
	let m = 40 as Meters
	let f = 120 as Feet
	print("{takesDistance(larger(m, b: f))}\n")
	return 0
end 'main'
```
```stdout
120
```

<!-- test: error.the-common-ancestor-is-neither-argument -->
```maxon
typealias Distance = int(0 to 1000)
typealias Meters = int(0 to 100) implements Distance
typealias Feet = int(0 to 300) implements Distance

function larger(a T, b T) uses T returns T
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function takesMeters(m Meters) returns Meters
	return m
end 'takesMeters'

function main() returns ExitCode
	let m = 40 as Meters
	let f = 12 as Feet
	print("{takesMeters(larger(m, b: f))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:21:10: argument type mismatch for 'm': expected 'Meters', got 'Distance'
```

<!-- test: error.sibling-aliases-with-no-common-ancestor -->
```maxon
typealias Apples = int(0 to 100)
typealias Oranges = int(0 to 100)

function larger(a T, b T) uses T returns T
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	let a = 3 as Apples
	let o = 4 as Oranges
	print("{larger(a, b: o)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3180: <fragment>:16:10: the arguments of 'larger' give type parameter 'T' no one type: 'Apples' and 'Oranges' share no alias they both implement
```

<!-- test: error.a-record-that-is-not-equatable -->
```maxon
type Crate
	export var size as CrateSize

	static function create(size CrateSize) returns Self
		return Self{size: size}
	end 'create'
end 'Crate'

typealias CrateSize = int(0 to 100)

function same(a T, b T) uses T returns bool where T is Equatable
	return a == b
end 'same'

function main() returns ExitCode
	let c = Crate.create(1)
	let d = Crate.create(1)
	print("{same(c, b: d)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3017: <fragment>:19:10: Type 'Crate' does not satisfy constraint 'Equatable' required by type parameter 'T' of 'same'
```

<!-- test: error.an-uninferable-type-parameter -->
```maxon
typealias Count = int(0 to 100)

function make(n Count) uses T returns Count
	return n
end 'make'

function main() returns ExitCode
	print("{make(3)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3179: <fragment>:4:29: type parameter 'T' of 'make' cannot be inferred: no parameter is declared 'T'
```

<!-- test: a-generic-static-method -->
```maxon
type MathUtil
	static function larger(a T, b T) uses T returns T where T is Comparable
		if a > b 'first'
			return a
		end 'first'

		return b
	end 'larger'
end 'MathUtil'

function main() returns ExitCode
	print("{MathUtil.larger(4, b: 9)} {MathUtil.larger(2.5, b: 1.5)}\n")
	return 0
end 'main'
```
```stdout
9 2.5
```

<!-- test: a-generic-instance-method -->
An instance method of a type with no `uses` list may declare type parameters of its own.
```maxon
typealias Score = int(0 to 100)

type Tally
	var total as Score

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function describe(v T) uses T returns String
		return "{self.total}:{v}"
	end 'describe'
end 'Tally'

function main() returns ExitCode
	let t = Tally.create()
	print("{t.describe("a")} {t.describe(7)}\n")
	return 0
end 'main'
```
```stdout
0:a 0:7
```

<!-- test: error.a-method-of-a-generic-type-declares-its-own-type-parameter -->
```maxon
type Box uses T
	var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function pair(other U) uses U returns String
		return "{other}"
	end 'pair'
end 'Box'

function main() returns ExitCode
	let b = Box.create(1)
	print("{b.pair(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:11: Unsupported: a method of the generic type 'Box' declaring type parameters of its own — declare it as a `static function` or on a type with no `uses` list
```

<!-- test: a-comparable-record-is-ordered-by-less-than -->
```maxon
typealias VersionNumber = int(0 to 1000)

type Version implements Comparable
	export var number as VersionNumber

	static function create(number VersionNumber) returns Self
		return Self{number: number}
	end 'create'

	function compare(other Self) returns Ordering
		if self.number < other.number 'older'
			return Ordering.lessThan
		end 'older'

		if self.number > other.number 'newer'
			return Ordering.greaterThan
		end 'newer'

		return Ordering.equalTo
	end 'compare'
end 'Version'

function smaller(a T, b T) uses T returns T where T is Comparable
	if a < b 'first'
		return a
	end 'first'

	return b
end 'smaller'

function main() returns ExitCode
	let v = smaller(Version.create(12), b: Version.create(7))
	print("{v.number}\n")
	return 0
end 'main'
```
```stdout
7
```

<!-- test: built-in-types-are-equatable-and-comparable -->
```maxon
typealias Score = int(0 to 100)
typealias Ratio = float(0.0 to 1.0)

function smaller(a T, b T) uses T returns T where T is Comparable
	if a < b 'first'
		return a
	end 'first'

	return b
end 'smaller'

function same(a T, b T) uses T returns bool where T is Equatable
	return a == b
end 'same'

function main() returns ExitCode
	let s = smaller(80 as Score, b: 35 as Score)
	let r = smaller(0.75 as Ratio, b: 0.25 as Ratio)
	let w = smaller("pear", b: "apple")
	print("{s} {r} {w}\n")
	print("{same(s, b: 35)} {same(r, b: 0.5)} {same(w, b: "apple")} {same(true, b: true)}\n")
	return 0
end 'main'
```
```stdout
35 0.25 apple
true false true true
```

<!-- test: two-instantiations-in-one-program -->
```maxon
function twice(a T) uses T returns T
	return a + a
end 'twice'

function main() returns ExitCode
	print("{twice(21)} {twice(1.25)}\n")
	return 0
end 'main'
```
```stdout
42 2.5
```

<!-- test: an-exported-generic-function-is-called-from-another-file -->
```maxon
// --- file: lib.maxon
export function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

// --- file: main.maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	print("{larger(3 as Score, b: 8 as Score)} {larger("fig", b: "date")}\n")
	return 0
end 'main'
```
```stdout
8 fig
```

<!-- test: an-exact-non-generic-overload-wins -->
```maxon
function show(v String) returns String
	return "text {v}"
end 'show'

function show(v T) uses T returns String
	return "generic {v}"
end 'show'

function main() returns ExitCode
	print("{show("a")} / {show(5)}\n")
	return 0
end 'main'
```
```stdout
text a / generic 5
```

<!-- test: error.two-generic-overloads-are-ambiguous -->
```maxon
function show(v T) uses T returns String where T is Equatable
	return "equatable {v}"
end 'show'

function show(v U) uses U returns String where U is Comparable
	return "comparable {v}"
end 'show'

function main() returns ExitCode
	print("{show(5)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3007: <fragment>:11:10: Ambiguous overload for 'show': 2 generic declarations infer a type for this call
```

<!-- test: interpolation-in-a-generic-body -->
```maxon
typealias Score = int(0 to 100)

function describe(v T) uses T returns String
	return "<{v}>"
end 'describe'

function main() returns ExitCode
	print("{describe(7 as Score)} {describe("hi")} {describe(1.5)} {describe(false)}\n")
	return 0
end 'main'
```
```stdout
<7> <hi> <1.5> <false>
```

<!-- test: a-generic-call-feeds-another -->
```maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	print("{larger(larger(3, b: 9), b: 5)}\n")
	return 0
end 'main'
```
```stdout
9
```

<!-- test: a-record-argument-is-its-callers-own-type -->
```maxon
// --- file: lib.maxon
typealias Point = int(0 to 10)

export function smaller(a T, b T) uses T returns T where T is Comparable
	if a < b 'first'
		return a
	end 'first'

	return b
end 'smaller'

export function libraryPoint() returns String
	let p = 7 as Point
	return "lib{p}"
end 'libraryPoint'

// --- file: main.maxon
typealias Rank = int(0 to 100)

type Point implements Comparable
	export var rank as Rank

	static function create(rank Rank) returns Self
		return Self{rank: rank}
	end 'create'

	function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.rank > other.rank 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

function main() returns ExitCode
	let p = smaller(Point.create(9), b: Point.create(3))
	print("{p.rank} {libraryPoint()}\n")
	return 0
end 'main'
```
```stdout
3 lib7
```

<!-- test: an-exported-requirement-reached-only-through-an-instance-is-used -->
```maxon
// --- file: lib.maxon
export function smaller(a T, b T) uses T returns T where T is Comparable
	if a < b 'first'
		return a
	end 'first'

	return b
end 'smaller'

// --- file: point.maxon
export typealias Rank = int(0 to 100)

export type Point implements Comparable
	export var rank as Rank

	export static function create(rank Rank) returns Self
		return Self{rank: rank}
	end 'create'

	export function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.rank > other.rank 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

// --- file: main.maxon
function main() returns ExitCode
	let p = smaller(Point.create(9), b: Point.create(3))
	print("{p.rank}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: error.an-instance-cannot-reach-a-private-method-that-is-no-requirement -->
```maxon
// --- file: lib.maxon
export function weigh(a T, b T) uses T returns bool where T is Comparable
	if a < b 'first'
		return a.heavy()
	end 'first'

	return b.heavy()
end 'weigh'

// --- file: main.maxon
typealias Rank = int(0 to 100)

type Point implements Comparable
	export var rank as Rank

	static function create(rank Rank) returns Self
		return Self{rank: rank}
	end 'create'

	function heavy() returns bool
		return self.rank > 5
	end 'heavy'

	function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		return Ordering.equalTo
	end 'compare'
end 'Point'

function main() returns ExitCode
	print("{weigh(Point.create(9), b: Point.create(3))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:5:12: type 'Point' is not exported
```

<!-- test: an-instance-calls-a-requirement-of-a-type-its-file-may-not-name -->
A requirement the `where` clause names is the constraining interface's surface, so a copy calls it by name even
where the type that satisfies it is hidden.
```maxon
// --- file: lib.maxon
export function smaller(a T, b T) uses T returns T where T is Comparable
	if a.compare(b) == Ordering.lessThan 'first'
		return a
	end 'first'

	return b
end 'smaller'

// --- file: main.maxon
typealias Rank = int(0 to 100)

type Point implements Comparable
	export var rank as Rank

	static function create(rank Rank) returns Self
		return Self{rank: rank}
	end 'create'

	function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.rank > other.rank 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

function main() returns ExitCode
	let p = smaller(Point.create(9), b: Point.create(3))
	print("{p.rank}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: a-closure-in-an-instance-calls-a-requirement-of-a-type-its-file-may-not-name -->
A closure written in a generic function belongs to the copy it is written in, so it calls the requirement too.
```maxon
// --- file: lib.maxon
export function smallerVia(a T, b T) uses T returns T where T is Comparable
	let firstIsSmaller = function() gives a.compare(b) == Ordering.lessThan

	if firstIsSmaller() 'first'
		return a
	end 'first'

	return b
end 'smallerVia'

// --- file: main.maxon
typealias Rank = int(0 to 100)

type Point implements Comparable
	export var rank as Rank

	static function make(rank Rank) returns Self
		return Self{rank: rank}
	end 'make'

	function compare(other Self) returns Ordering
		if self.rank < other.rank 'lower'
			return Ordering.lessThan
		end 'lower'

		if self.rank > other.rank 'higher'
			return Ordering.greaterThan
		end 'higher'

		return Ordering.equalTo
	end 'compare'
end 'Point'

function main() returns ExitCode
	let p = smallerVia(Point.make(9), b: Point.make(3))
	print("{p.rank}\n")
	return 0
end 'main'
```
```stdout
3
```

<!-- test: an-instance-calls-an-extension-method-of-its-constraint-on-a-type-its-file-may-not-name -->
An `extension` of the constraining interface is that interface's surface too, so a copy calls its methods on a
type it may not name.
```maxon
// --- file: lib.maxon
export typealias Score = int(i64.min to i64.max)

export interface Ranked
	function rank() returns Score
end 'Ranked'

extension Ranked
	function doubledRank() returns Score
		return self.rank() * 2
	end 'doubledRank'
end 'Ranked'

export function doubled(a T) uses T returns Score where T is Ranked
	return a.doubledRank()
end 'doubled'

// --- file: main.maxon
type Point implements Ranked
	var value as Score

	static function make(value Score) returns Self
		return Self{value: value}
	end 'make'

	function rank() returns Score
		return self.value
	end 'rank'
end 'Point'

function main() returns ExitCode
	print("{doubled(Point.make(21))}\n")
	return 0
end 'main'
```
```stdout
42
```

<!-- test: an-extension-method-calls-a-file-private-requirement-of-a-conformer-in-another-file -->
An `extension` body calls a requirement through the interface's surface, so the conformer's implementation may be
private to the file that declares it.
```maxon
// --- file: lib.maxon
export typealias Score = int(i64.min to i64.max)

export interface Ranked
	function rank() returns Score
end 'Ranked'

extension Ranked
	export function doubledRank() returns Score
		return self.rank() * 2
	end 'doubledRank'
end 'Ranked'

// --- file: main.maxon
type Point implements Ranked
	var value as Score

	static function make(value Score) returns Self
		return Self{value: value}
	end 'make'

	function rank() returns Score
		return self.value
	end 'rank'
end 'Point'

function main() returns ExitCode
	print("{Point.make(21).doubledRank()}\n")
	return 0
end 'main'
```
```stdout
42
```

<!-- test: error.an-instance-cannot-read-a-field-named-like-a-method-of-its-constraint -->
What a constraint grants is its methods. A field of the same name is a member of the type, and is hidden with it.
```maxon
// --- file: lib.maxon
export typealias Score = int(i64.min to i64.max)

export interface Ranked
	function rank() returns Score
end 'Ranked'

extension Ranked
	function tally() returns Score
		return self.rank()
	end 'tally'
end 'Ranked'

export function tallyOf(a T) uses T returns Score where T is Ranked
	return a.tally
end 'tallyOf'

// --- file: main.maxon
type Point implements Ranked
	export var tally as Score

	static function make(tally Score) returns Self
		return Self{tally: tally}
	end 'make'

	function rank() returns Score
		return self.tally
	end 'rank'
end 'Point'

function main() returns ExitCode
	print("{tallyOf(Point.make(7))}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: specs/generic-functions/error.an-instance-cannot-read-a-field-named-like-a-method-of-its-constraint.maxon:16:11: type 'Point' is not exported
```

<!-- test: an-enum-argument-is-its-callers-own-type -->
```maxon
// --- file: lib.maxon
typealias Shade = int(0 to 10)

export function same(a T, b T) uses T returns bool where T is Equatable
	return a == b
end 'same'

export function libraryShade() returns String
	let s = 7 as Shade
	return "lib{s}"
end 'libraryShade'

// --- file: main.maxon
enum Shade
	light
	dark
end 'Shade'

function main() returns ExitCode
	print("{same(Shade.light, b: Shade.dark)} {same(Shade.dark, b: Shade.dark)} {libraryShade()}\n")
	return 0
end 'main'
```
```stdout
false true lib7
```

<!-- test: two-files-each-call-their-own-private-generic -->
```maxon
// --- file: a.maxon
function pick(a T) uses T returns T
	return a
end 'pick'

export function fromA() returns String
	return "a{pick(1)}"
end 'fromA'

// --- file: main.maxon
function pick(a T) uses T returns T
	return a + a
end 'pick'

function main() returns ExitCode
	print("{fromA()} b{pick(1)}\n")
	return 0
end 'main'
```
```stdout
a1 b2
```

<!-- test: error.a-private-generic-is-not-callable-from-another-file -->
```maxon
// --- file: a.maxon
function pick(a T) uses T returns T
	return a
end 'pick'

export function fromA() returns String
	return "a{pick(1)}"
end 'fromA'

// --- file: main.maxon
function main() returns ExitCode
	print("{fromA()} {pick(2)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:13:20: function 'pick' is not exported
```

<!-- test: a-private-generic-is-bound-past-another-directorys-exported-generic-of-one-name -->
`a/` calls its own private `larger`; the root sees only `b/`'s exported one, which returns the smaller argument so the binding shows.
```maxon
// --- file: a/a.maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

export function runA()
	print("a {larger(3, b: 7)}\n")
end 'runA'

// --- file: b/b.maxon
export function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return b
	end 'first'

	return a
end 'larger'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	print("main {larger(10, b: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 7
main 2
```

<!-- test: two-same-named-files-in-two-directories-each-bind-their-own-private-generic -->
A file-private declaration coexists with anything, so two `util.maxon` files each call their own `larger`; `b/`'s returns the smaller argument so the binding shows.
```maxon
// --- file: a/util.maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

export function runA()
	let x = larger(3, b: 7)
	let y = larger(10, b: 2)
	print("a {x} {y}\n")
end 'runA'

// --- file: b/util.maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return b
	end 'first'

	return a
end 'larger'

export function runB()
	print("b {larger(larger(3, b: 9), b: 5)}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 7 10
b 3
```

<!-- test: a-trailing-comma-after-uses-is-accepted -->
```maxon
function pick(a T) uses T, returns T
	return a
end 'pick'

function main() returns ExitCode
	print("{pick(4)}\n")
	return 0
end 'main'
```
```stdout
4
```

<!-- test: error.expect-close-takes-floats -->
```maxon
function main() returns ExitCode
	try Expect.close("a", expected: "b", within: 0.1) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: argument type mismatch for 'actual': expected 'AssertedReal', got 'String'
```

<!-- test: error.an-undeclared-argument-is-reported-once -->
```maxon
function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

function main() returns ExitCode
	print("{larger(nope(), b: 5)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:11:17: call to undefined function 'nope'
```

<!-- test: two-same-named-files-in-two-directories-each-bind-their-own-module-generic -->
The same pair at the `module` tier: one file name in two directories, one `module` generic of one name, one line and column.
```maxon
// --- file: a/util.maxon
module function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

export function runA()
	print("a {larger(3, b: 7)}\n")
end 'runA'

// --- file: b/util.maxon
module function larger(a T, b T) uses T returns T where T is Comparable
	if a > b 'first'
		return a
	end 'first'

	return b
end 'larger'

export function runB()
	print("b {larger(larger(3, b: 9), b: 5)}\n")
end 'runB'

// --- file: main.maxon
function main() returns ExitCode
	runA()
	runB()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a 7
b 9
```
