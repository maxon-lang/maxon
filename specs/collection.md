---
feature: collection
status: stable
keywords: collection, array, map, transform, functional, higher-order, get, set, count
category: stdlib
---
# Collection

## Documentation

The `Collection` interface provides indexed access and functional operations for ordered collections like arrays.

**Interface:**
```text
interface Collection uses Element extends Iterable
  function count() returns int
  function get(index int) returns Element throws ArrayError
  function set(index int, value Element) returns Self
  function map(transform (Element) Element) returns Self
end 'Collection'
```

Arrays automatically implement the Collection interface.

## count

Returns the number of elements in the collection.

```maxon
function main() returns ExitCode
	let arr = [1, 2, 3, 4, 5]
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

## get

Returns the element at the specified index, or throws ArrayError if out of bounds.

```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let val = try arr.get(1) otherwise 0
	return val
end 'main'
```
```exitcode
20
```

## set

Sets the element at the specified index. Returns self for method chaining.

```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(1, value: 99) otherwise panic("test invariant: set OOB")
	let val = try arr.get(1) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

## map

Transforms each element of a collection by applying a function, returning a new collection with the transformed elements.

**Signature:**
```text
collection.map(transform) collection
```

**Parameters:**
- `transform` - A function that takes an element and returns a transformed value

**Returns:**
A new array containing the transformed elements.

### Using Named Functions

Transform an array using a named function:

```maxon
typealias Score = int(i64.min to i64.max)

function double(x Score) returns Score
	return x * 2
end 'double'

function main() returns ExitCode
	let numbers = [1, 2, 3, 4, 5]
	let doubled = numbers.map(double)
	let val = try doubled.get(2) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
```

### Using Closures

Transform using an inline closure with `gives`:

```maxon
typealias Score = int(i64.min to i64.max)

function main() returns ExitCode
	let numbers = [1, 2, 3]
	let squared = numbers.map(function(x Score) gives x * x)
	let val0 = try squared.get(0) otherwise 0
	let val1 = try squared.get(1) otherwise 0
	let val2 = try squared.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
```

## Tests

<!-- test: count-basic -->
```maxon
function main() returns ExitCode
	let arr = [1, 2, 3, 4, 5]
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: count-empty -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	print("{arr.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- test: get-valid -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	let val0 = try arr.get(0) otherwise 0
	let val2 = try arr.get(2) otherwise 0
	return val0 + val2
end 'main'
```
```exitcode
40
```

<!-- test: get-out-of-bounds -->
```maxon
function main() returns ExitCode
	let arr = [1, 2, 3]
	let val = try arr.get(10) otherwise 6
	return val
end 'main'
```
```exitcode
6
```

<!-- test: set-basic -->
```maxon
function main() returns ExitCode
	var arr = [1, 2, 3]
	try arr.set(0, value: 100) otherwise panic("test invariant: set OOB")
	try arr.set(2, value: 300) otherwise panic("test invariant: set OOB")
	let val0 = try arr.get(0) otherwise 0
	let val1 = try arr.get(1) otherwise 0
	let val2 = try arr.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
100
2
300
```

<!-- test: map-basic-transform -->
```maxon
typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let arr = [1, 2, 3, 4, 5]
	let result = arr.map(double)
	let val0 = try result.get(0) otherwise 0
	let val1 = try result.get(1) otherwise 0
	let val2 = try result.get(2) otherwise 0
	let val3 = try result.get(3) otherwise 0
	let val4 = try result.get(4) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	print("{val3}\n")
	print("{val4}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
4
6
8
10
```

<!-- test: map-closure-multiply -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let arr = [2, 3, 4]
	let result = arr.map(function(x Integer) gives x * 3)
	let val0 = try result.get(0) otherwise 0
	let val1 = try result.get(1) otherwise 0
	let val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
9
12
```

<!-- test: map-closure-square -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let arr = [1, 2, 3, 4]
	let squared = arr.map(function(n Integer) gives n * n)
	let val0 = try squared.get(0) otherwise 0
	let val1 = try squared.get(1) otherwise 0
	let val2 = try squared.get(2) otherwise 0
	let val3 = try squared.get(3) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	print("{val3}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
4
9
16
```

<!-- test: map-identity-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let arr = [10, 20, 30]
	let result = arr.map(identity)
	let val0 = try result.get(0) otherwise 0
	let val1 = try result.get(1) otherwise 0
	let val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
20
30
```

<!-- test: map-negate -->
```maxon
typealias Integer = int(i64.min to i64.max)

function negate(x Integer) returns Integer
	return -x
end 'negate'

function main() returns ExitCode
	let arr = [1, 2, 3]
	let result = arr.map(negate)
	let val0 = try result.get(0) otherwise 0
	let val1 = try result.get(1) otherwise 0
	let val2 = try result.get(2) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	print("{val2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
-2
-3
```

<!-- test: map-single-element -->
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let arr = [42]
	let result = arr.map(function(x Integer) gives x + 8)
	let val = try result.get(0) otherwise 0
	print("{val}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50
```

<!-- test: map-declared-ranged-alias-element -->
An element that arrives DECLARED rather than from a literal. `Array with Integer` names its element with
a ranged typealias, which carries the `named` tag until TypeResolution collapses it — so the transform's
`Integer` return and the container's `Integer` element are one type spelled one way, and the door that
compares them has to say so.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(2)
	let t = a.map(function(x Integer) gives x + 1)
	let val0 = try t.get(0) otherwise 0
	let val1 = try t.get(1) otherwise 0
	print("{val0}\n")
	print("{val1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
3
```

<!-- test: map-set-with-declared-ranged-alias -->
The same element, one container over. A `Set`'s element arrives by the identical route, so a rule that
reads the two containers differently is reading something other than the element.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntSet = Set with Integer

function main() returns ExitCode
	var s = IntSet.create()
	s.insert(1)
	s.insert(2)
	let t = s.map(function(a Integer) gives a + 1)
	print("{t.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: error-map-transform-param-type-mismatch -->
⭐ **The transform's PARAMETER is the container's element, by the declaration `map` is read from.** A
transform declaring another type is handed the element regardless: `nums.map(function(a String) gives
a.count())` over an int array compiled clean and SEGFAULTED, dereferencing the integer `1` as a `String`.
The arity half of this contract is E3122's; this is the type half, and both are decided whole-program
because a bare reference to a named function carries no closure literal to read.

⚠ **THE SENTENCE MOVED AT X-array-retire AND THE VERDICT DID NOT.** `map` left the `Array` roster, so this
is now a call to `stdlib/Interfaces.maxon:199`'s declared `transform fn(Element) returns Element` and the
ORDINARY argument-agreement check refuses it, in the voice every other call gets. `Set`/`Map` still get the
tailored sentence because those surfaces are still synthesized — see
`closure-param-type-inference.md`, which carries the whole argument and the `print`/`sleep` precedent.
```maxon

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(a String) gives a.count())
	print("v={try out.get(0) otherwise 9}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:17: argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn(String) returns int'
```

<!-- test: map-struct-element-preserved -->
⭐ **THE ANTI-FALSE-REFUSAL CONTROL for the parameter half.** A struct element declared through a generic
alias arrives at the transform door as a `named` — the sweep that recorded the type argument ran before
anything could say what `Point` denoted — so a parameter check that asked the TAG read it as identity-less
and refused this program as *"the container's own element, which is 'int'"*. Only the NAME says which kind
of thing a `named` is, and a struct's name is the one identity that survives resolution.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var a = PointArray.create()
	a.push(Point.create(7))
	let out = a.map(function(p Point) gives p)
	let first = try out.get(0) otherwise Point.create(0)
	print("x={first.x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
x=7
```

<!-- test: map-enum-element-preserved -->
⭐ **THE POSITIVE CONTROL for the enum half of element preservation.** A boxed enum element ERASES to bare
`integer` at resolution — it is the one aggregate whose identity `paramTypes` cannot carry — so the
parameter check has to recover the name from `FunctionShape.paramAggregateNames` rather than from the
resolved type. A check that recovered nothing would read two enums as one int and admit any of them; a check
that recovered the wrong thing would refuse this program.
```maxon

enum Color
	red
	green
	blue
end 'Color'

typealias ColorArray = Array with Color

function good(c Color) returns Color
	match c 'k'
		red then print("saw red\n")
		green then print("saw green\n")
		blue then print("saw blue\n")
	end 'k'
	return Color.red
end 'good'

function main() returns ExitCode
	var a = ColorArray.create()
	a.push(Color.blue)
	let b = a.map(good)
	print("n={b.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
saw blue
n=1
```

<!-- test: error-map-transform-param-enum-mismatch -->
⭐ **THE ENUM TWIN OF THE SEGFAULT, AND IT IS A CRASH OF ITS OWN.** A boxed union/enum parameter resolves to
bare `integer`, so a check that asks only the RESOLVED parameter type reads every enum as every other enum:
`<Array with Color>.map(bad)` where `bad` takes a `Shade` compiled clean and died `STATUS_STACK_OVERFLOW` —
`Color.blue` is ordinal 2 and `Shade` has two cases, so the `match` had no arm for the value it was handed.
The name survives only on the pre-erasure carrier, which is why `FunctionShape` grew one.
```maxon

enum Color
	red
	green
	blue
end 'Color'

enum Shade
	dark
	light
end 'Shade'

typealias ColorArray = Array with Color

function bad(s Shade) returns Color
	match s 'k'
		dark then print("saw dark\n")
		light then print("saw light\n")
	end 'k'
	return Color.red
end 'bad'

function main() returns ExitCode
	var a = ColorArray.create()
	a.push(Color.blue)
	let b = a.map(bad)
	print("n={b.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:27:12: argument type mismatch for 'transform': expected 'fn(Color) returns Color', got 'fn(Shade) returns Color'
```

<!-- test: error-map-transform-param-enum-at-an-int-element -->
⭐ **THE SAME DEFECT WITH THE SIDES SWAPPED, and the reason the verdict cannot let the ELEMENT decide which
question is asked.** Here the element is a plain `int` — no nominal identity — and the transform's PARAMETER
is the enum. A rule that fell to the tag comparison whenever the element was identity-less admitted it (a
boxed enum is integral), and `[1,2,3]` arrived inside a two-case `match` as 1, 2 and 3: compiles clean,
`STATUS_STACK_OVERFLOW` at run. **The identical call by DIRECT dispatch was already `E3005: argument type
mismatch for 'c': expected 'Color', got 'int'`** — one compiler answering one question two ways. An identity
on EITHER side settles the question.
```maxon

enum Color
	red
	green
end 'Color'

typealias Integer = int(i64.min to i64.max)

function show(c Color) returns Integer
	match c 'k'
		red then print("red\n")
		green then print("green\n")
	end 'k'
	return 0
end 'show'

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(c Color) gives show(c))
	print("n={out.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:17: argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn(Color) returns int'
```
