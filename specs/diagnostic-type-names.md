---
feature: diagnostic-type-names
status: stable
keywords: [diagnostics, operators, otherwise, match, struct, generic, tuple, type-names]
category: diagnostics
---

# A Diagnostic Names A Value's Type As The Author Wrote It

## Documentation

A diagnostic that names the type of a value names it as the author wrote it: a declared struct by its name
(`Point`), a generic instance by the alias the value was made through (`Ys` for a value of `typealias Ys =
Array with Count`, even when `Xs` is another alias of the same instance), a tuple by its tuple type
(`(int, int)`), a union or enum by its declared name (`Shape`, `Color`), a ranged alias by its alias wherever a
declaration wrote one (`expected 'Digit', got 'bool'` for a parameter `n Digit`; `declared to return 'Digit'`;
`which holds 'Digit'`; `cannot implicitly convert 'float' to 'Digit'`; the parameter of a function type
included), a value that wears a ranged alias by that alias on the other side of the sentence too (`got 'Ratio'`,
`Cannot return 'Digit' from function declared to return 'bool'`, `Cannot operate on Digit and bool`), and a scalar no declaration gave an alias by its
primitive (`int`, `bool`). A float alias met by an integer alias is a mismatch between two aliases (E3005), at an
argument, a return, an assignment, a field and a payload alike. A union value is not a number, so an arithmetic, bitwise or shift operator, unary `-`,
`not` or a condition refuses it as it refuses a struct (`Cannot operate on Shape and int`); an integer- or
float-backed enum keeps its numeric operators.

The same naming covers every diagnostic that quotes a value's type: an operator's operands (arithmetic,
division and shift operands, an operator applied to two operands of one type, a word operator's integer-only
operand, unary `-` and `not`, the comparison operators), an assignment, a `return`, a cast, a condition, an
`otherwise` fallback of the wrong type, a `match` scrutinee, a builtin's operand (`sleep`, a command-line or
subprocess string operand), `@heap`, an interpolated value, a member access, a destructuring of a value that
is not a tuple, an equality operator whose `equals` does not return `bool`, and a generic function's argument
of an unsupported kind (E3181).

## Tests

<!-- test: error.two-different-structs-on-an-operator-name-both-types -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

type Size
	export let w as Coord

	static function make(w Coord) returns Size
		return Size{w: w}
	end 'make'
end 'Size'

function main() returns ExitCode
	let p = Point.make(1)
	let s = Size.make(2)
	let a = p + s
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:23:12: Cannot operate on Point and Size
```

<!-- test: error.a-struct-and-an-int-on-an-operator-name-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = p + 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:14:12: Cannot operate on Point and int
```

<!-- test: error.an-int-and-a-struct-on-an-operator-name-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = 1 * p
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:14:12: Cannot operate on int and Point
```

<!-- test: error.the-same-struct-on-both-sides-of-an-operator-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = p + p
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:12: operator '+' is not defined for type 'Point'
```

<!-- test: error.a-struct-shifted-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = p shl 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:14:12: Cannot operate on Point and int
```

<!-- test: error.a-struct-on-a-word-operator-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = p and p
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:12: operator 'and' requires integer operands, but this one is Point
```

<!-- test: error.a-negated-struct-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = -p
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:14:10: Cannot negate Point
```

<!-- test: error.not-of-a-struct-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let a = not p
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:10: operator 'not' requires integer operands, but this one is Point
```

<!-- test: error.comparing-a-struct-with-no-equals-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let q = Point.make(2)
	if p < q 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:7: type mismatch: 'cannot compare Point with Point'
```

<!-- test: error.comparing-two-different-structs-names-both-types -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

type Size
	export let w as Coord

	static function make(w Coord) returns Size
		return Size{w: w}
	end 'make'
end 'Size'

function main() returns ExitCode
	let p = Point.make(1)
	let s = Size.make(2)
	if p < s 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:7: type mismatch: 'cannot compare Point with Size'
```

<!-- test: error.a-generic-instance-operand-of-an-operator-names-its-alias -->
```maxon
typealias Coord = int(0 to 1000)

type Box uses T
	export var v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

typealias CoordBox = Box with Coord

function main() returns ExitCode
	let b = CoordBox.create(1)
	let a = b + 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:16:12: Cannot operate on CoordBox and int
```

<!-- test: error.an-instance-over-a-float-range-names-the-range-in-decimal -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function half() returns Ratio
	return 0.5
end 'half'

function main() returns ExitCode
	let ratios = [half()]
	let bumped = ratios + 1
	print("bumped {bumped}")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:10:22: Cannot operate on Array with float(0 to 1) and int
```

<!-- test: error.an-instance-over-a-bit-pattern-names-its-width -->
```maxon
typealias Hash = bits(64)

function seed() returns Hash
	_ = File.exists(FilePath from "noyield.txt")
	return 7
end 'seed'

function main() returns ExitCode
	let p = async seed()
	let bumped = p + 1
	print("bumped {bumped}")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:17: Cannot operate on Promise with bits(64) and int
```

<!-- test: error.a-branded-instance-operand-of-an-operator-names-the-alias-the-value-wears -->
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count
typealias Ys = Array with Count

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	let a = ys + 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:13: Cannot operate on Ys and int
```

<!-- test: error.a-branded-instance-compared-with-an-int-names-the-alias-the-value-wears -->
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count
typealias Ys = Array with Count

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	if ys < 1 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:8: type mismatch: 'cannot compare Ys with int'
```

<!-- test: error.a-negated-branded-instance-names-the-alias-the-value-wears -->
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count
typealias Ys = Array with Count

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	let a = -ys
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:10: Cannot negate Ys
```

<!-- test: error.an-otherwise-fallback-struct-against-a-float-result-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)
typealias Ratio = float(0.0 to 1.0)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

enum LookupError implements Error
	missing
end 'LookupError'

function ratioOf(n Coord) returns Ratio throws LookupError
	if n == 0 'none'
		throw LookupError.missing
	end 'none'
	return 0.5
end 'ratioOf'

function main() returns ExitCode
	let p = Point.make(1)
	let r = try ratioOf(1) otherwise p
	return 0
end 'main'
```
```maxoncstderr
error E3059: <fragment>:26:10: type mismatch: 'otherwise type 'Point' does not match expected type 'Ratio''
```

<!-- test: error.a-match-over-a-branded-instance-names-the-alias-the-value-wears -->
```maxon
typealias Count = int(0 to 1000)
typealias Xs = Array with Count
typealias Ys = Array with Count

function main() returns ExitCode
	var xs = Xs.create()
	xs.push(1)
	var ys = Ys.create()
	ys.push(2)
	match ys 'm'
		1 then return 1
		default then return 0
	end 'm'
end 'main'
```
```maxoncstderr
error E2028: <fragment>:12:3: pattern type 'int' does not match scrutinee type 'Ys'
```

<!-- test: error.a-match-over-a-struct-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	match p 'm'
		1 then return 1
		default then return 0
	end 'm'
end 'main'
```
```maxoncstderr
error E2028: <fragment>:15:3: pattern type 'int' does not match scrutinee type 'Point'
```

<!-- test: error.sleep-of-a-struct-names-the-struct -->
<!-- tier: stdlib -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	__Builtins.sleep(p)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:13: '__Builtins.sleep' requires an integer, but its argument is Point
```

<!-- test: error.destructuring-a-struct-names-the-struct -->
```maxon
typealias Coord = int(0 to 1000)

type Point
	export let x as Coord

	static function make(x Coord) returns Point
		return Point{x: x}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make(1)
	let (a, b) = p
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:14:2: Unsupported: a destructuring binding of a 'Point' initializer (only a TUPLE can be destructured — `let (x, y) = …` needs a right-hand side that is a tuple)
```

<!-- test: error.an-equals-returning-a-struct-names-the-struct -->
```maxon
typealias Count = int(0 to 100)

type Box
	export let n as Count

	static function make(n Count) returns Box
		return Box{n: n}
	end 'make'

	function equals(other Box) returns Box
		return other
	end 'equals'
end 'Box'

function main() returns ExitCode
	let a = Box.make(1)
	let b = Box.make(1)
	let r = a == b
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:12: Operator '==' dispatches 'Box.equals', which returns 'Box' — an equality operator requires the 'bool' result 'Equatable' declares
```

<!-- test: error.a-generic-function-argument-of-an-instance-names-its-alias -->
```maxon
typealias Count = int(0 to 100)
typealias Counts = Array with Count

function ident(a T) uses T returns T
	return a
end 'ident'

function main() returns ExitCode
	var counts = Counts.create()
	counts.push(1)
	let c = ident(counts)
	return 0
end 'main'
```
```maxoncstderr
error E3181: <fragment>:12:10: type parameter 'T' of 'ident' cannot be 'Counts': a generic function's type parameter is inferred as a number, `String`, `bool`, `Character`, a typealias, a record or an enum
```

<!-- test: error.a-union-and-an-int-on-an-operator-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = s + 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:12: Cannot operate on Shape and int
```

<!-- test: error.a-union-constructor-and-an-int-on-an-operator-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let a = Shape.circle(5) + 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:10:26: Cannot operate on Shape and int
```

<!-- test: error.an-int-and-a-union-on-an-operator-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = 1 * s
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:12: Cannot operate on int and Shape
```

<!-- test: error.the-same-union-on-both-sides-of-an-operator-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = s + s
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:12: operator '+' is not defined for type 'Shape'
```

<!-- test: error.a-union-shifted-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = s shl 1
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:12: Cannot operate on Shape and int
```

<!-- test: error.a-union-on-a-word-operator-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = s and s
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:12: operator 'and' requires integer operands, but this one is Shape
```

<!-- test: error.a-negated-union-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = -s
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:10: Cannot negate Shape
```

<!-- test: error.not-of-a-union-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let a = not s
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: operator 'not' requires integer operands, but this one is Shape
```

<!-- test: error.a-payload-free-union-case-added-to-an-int-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let n = 3 as Coord
	if n + Shape.empty == 1 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:11:7: Cannot operate on Coord and Shape
```

<!-- test: error.ordering-a-union-is-refused-for-a-match -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let t = Shape.empty
	if s < t 'lt'
		return 1
	end 'lt'
	return 0
end 'main'
```
```maxoncstderr
error E3066: <fragment>:12:7: cannot compare union values using '<', use 'match' instead
```

<!-- test: error.a-union-as-a-condition-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	if s 'cond'
		return 1
	end 'cond'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:5: 'if' requires a bool condition, got 'Shape'
```

<!-- test: error.a-union-as-a-range-bound-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	for i in s to 10 'each'
		print("{i}")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:11: Unsupported: a counted `for … in <lo> to|upto <hi>` range needs INTEGER bounds — got a 'Shape'. The loop variable is a counter stepped by 1, which no other domain has a meaning for; iterate a `float`/`String` by indexing an `Array` over it
```

<!-- test: error.a-union-as-an-array-index-names-the-union -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let xs = [1, 2, 3]
	let x = try xs.get(s) otherwise 0
	return x as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:17: argument type mismatch for 'index': expected 'ElementIndex', got 'Shape'
```

<!-- test: error.a-bool-at-a-ranged-alias-parameter-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function f(n Digit) returns Digit
	return n
end 'f'

function main() returns ExitCode
	let d = f(true)
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:10: argument type mismatch for 'n': expected 'Digit', got 'bool'
```

<!-- test: error.a-bool-at-a-ranged-float-alias-parameter-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function scale(r Ratio) returns Ratio
	return r
end 'scale'

function main() returns ExitCode
	let r = scale(true)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:10: argument type mismatch for 'r': expected 'Ratio', got 'bool'
```

<!-- test: error.a-bool-pushed-onto-an-array-of-a-ranged-alias-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = Array with Digit

function main() returns ExitCode
	var digits = Digits.create()
	digits.push(true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:9: argument type mismatch for 'value': expected 'Digit', got 'bool'
```

<!-- test: error.an-int-at-a-bool-parameter-names-the-primitive -->
```maxon
function check(flag bool) returns bool
	return flag
end 'check'

function main() returns ExitCode
	let ok = check(3)
	print("{ok}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:11: argument type mismatch for 'flag': expected 'bool', got 'int'
```

<!-- test: error.a-bool-returned-from-a-ranged-alias-function-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function pick() returns Digit
	let flag = true
	return flag
end 'pick'

function main() returns ExitCode
	print("{pick()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return 'bool' from function declared to return 'Digit'
```

<!-- test: error.a-bool-returned-from-a-ranged-float-alias-function-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function pick() returns Ratio
	let flag = true
	return flag
end 'pick'

function main() returns ExitCode
	print("{pick()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return 'bool' from function declared to return 'Ratio'
```

<!-- test: error.a-bool-assigned-to-a-ranged-alias-variable-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function main() returns ExitCode
	var d = 5 as Digit
	d = true
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: cannot assign a value of type 'bool' to variable 'd', which holds 'Digit'
```

<!-- test: error.a-bool-assigned-to-a-ranged-float-alias-variable-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-bool-assigned-to-a-ranged-alias-field-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

type Cell
	export var d as Digit

	static function make() returns Cell
		return Cell{d: 1}
	end 'make'
end 'Cell'

function main() returns ExitCode
	var c = Cell.make()
	c.d = true
	print("{c.d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:4: cannot assign a value of type 'bool' to field 'd' of 'Cell', which holds 'Digit'
```

<!-- test: error.a-string-assigned-to-a-ranged-float-alias-field-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)

type Cell
	export var r as Ratio

	static function make() returns Cell
		return Cell{r: 0.5}
	end 'make'
end 'Cell'

function main() returns ExitCode
	var c = Cell.make()
	c.r = "s"
	print("{c.r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:4: cannot assign a value of type 'String' to field 'r' of 'Cell', which holds 'Ratio'
```

<!-- test: error.a-float-converted-to-a-ranged-alias-argument-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function take(n Digit) returns Digit
	return n
end 'take'

function main() returns ExitCode
	let w = 2.5
	let d = take(w)
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:10:10: argument 'n': cannot implicitly convert 'float' to 'Digit': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.a-float-converted-to-a-ranged-alias-return-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function pick() returns Digit
	let w = 2.5
	return w
end 'pick'

function main() returns ExitCode
	print("{pick()}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:6:2: cannot implicitly convert 'float' to 'Digit': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.a-float-converted-to-a-ranged-alias-variable-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function main() returns ExitCode
	var d = 5 as Digit
	let w = 2.5
	d = w
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:7:2: cannot implicitly convert 'float' to 'Digit': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.a-function-value-into-a-ranged-alias-variable-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function one() returns Digit
	return 1
end 'one'

function main() returns ExitCode
	var d = 5 as Digit
	d = one
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:2: cannot assign a value of type 'function' to variable 'd', which holds 'Digit': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: error.a-bool-passed-through-a-function-type-parameter-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)
typealias Pick = function(n Digit) returns Digit

function apply(f Pick) returns Digit
	return f(true)
end 'apply'

function main() returns ExitCode
	print("{apply(function(n Digit) gives n)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:10: argument type mismatch for 1: expected 'Digit', got 'bool'
```

<!-- test: error.a-bool-pushed-onto-an-array-of-a-ranged-float-alias-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias Ratios = Array with Ratio

function main() returns ExitCode
	var ratios = Ratios.create()
	ratios.push(true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:9: argument type mismatch for 'value': expected 'Ratio', got 'bool'
```

<!-- test: error.a-bool-given-to-a-union-payload-of-a-ranged-alias-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

union Mark
	digit(n Digit)
	none
end 'Mark'

function main() returns ExitCode
	let m = Mark.digit(true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:25: type mismatch: 'expected Digit, got bool'
```

<!-- test: error.a-ratio-local-rebound-from-a-literal-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	r = 0.25
	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-handed-to-a-callee-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function bump(r Ratio)
	r = 0.25
end 'bump'

function main() returns ExitCode
	var r = 0.5 as Ratio
	bump(r)
	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-after-a-loop-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	for i in 0 upto 3 'l'
		r = 0.25
	end 'l'
	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-after-an-if-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	let n = 3

	if n > 2 'a'
		r = 0.25
	end 'a' else 'b'
		r = 0.75
	end 'b'

	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-from-a-ratio-call-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function half() returns Ratio
	return 0.5
end 'half'

function main() returns ExitCode
	var r = half()
	r = true
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-inside-a-loop-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	for i in 0 upto 3 'l'
		r = true
	end 'l'
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:3: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-value-at-a-digit-argument-is-a-mismatch -->
```maxon
typealias Digit = int(0 to 9)
typealias Ratio = float(0.0 to 1.0)

function take(n Digit) returns Digit
	return n
end 'take'

function main() returns ExitCode
	let w = 0.5 as Ratio
	let d = take(w)
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 'n': expected 'Digit', got 'Ratio'
```

<!-- test: error.a-ratio-value-assigned-to-a-digit-variable-is-a-mismatch -->
```maxon
typealias Digit = int(0 to 9)
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var d = 5 as Digit
	let w = 0.5 as Ratio
	d = w
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:2: cannot assign a value of type 'Ratio' to variable 'd', which holds 'Digit'
```

<!-- test: error.a-ratio-value-at-a-bool-argument-names-the-alias -->
```maxon
function take(flag bool) returns bool
	return flag
end 'take'

typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	let w = 0.5 as Ratio
	let d = take(w)
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:10: argument type mismatch for 'flag': expected 'bool', got 'Ratio'
```

<!-- test: error.a-bool-at-the-second-parameter-of-a-function-type -->
```maxon
typealias Digit = int(0 to 9)
typealias Pick = function(a Digit, n Digit) returns Digit

function apply(f Pick) returns Digit
	return f(1, true)
end 'apply'

function main() returns ExitCode
	print("{apply(function(a Digit, n Digit) gives n)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:10: argument type mismatch for 2: expected 'Digit', got 'bool'
```

<!-- test: error.a-bool-at-a-ratio-parameter-of-a-function-type -->
```maxon
typealias Ratio = float(0.0 to 1.0)
typealias PickR = function(r Ratio) returns Ratio

function apply(f PickR) returns Ratio
	return f(true)
end 'apply'

function main() returns ExitCode
	print("{apply(function(r Ratio) gives r)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:10: argument type mismatch for 1: expected 'Ratio', got 'bool'
```

<!-- test: error.a-named-function-value-called-indirectly -->
```maxon
typealias Digit = int(0 to 9)

function g(a Digit, n Digit) returns Digit
	return n
end 'g'

function main() returns ExitCode
	let h = g
	print("{h(1, true)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:11: argument type mismatch for 2: expected 'Digit', got 'bool'
```

<!-- test: error.a-default-parameter-direct-call -->
```maxon
typealias Digit = int(0 to 9)

function g(a Digit, n Digit, k Digit = 1) returns Digit
	return n
end 'g'

function main() returns ExitCode
	print("{g(1, n: true)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:10: argument type mismatch for 'n': expected 'Digit', got 'bool'
```

<!-- test: error.a-generic-method-with-a-ranged-parameter -->
```maxon
typealias Digit = int(0 to 9)

type Box uses T
	export var item as T

	static function create(v T) returns Self
		return Self{item: v}
	end 'create'

	function tag(n Digit) returns Digit
		return n
	end 'tag'
end 'Box'

typealias IntBox = Box with Digit

function main() returns ExitCode
	var b = IntBox.create(1 as Digit)
	print("{b.tag(true)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:12: argument type mismatch for 'n': expected 'Digit', got 'bool'
```

<!-- test: error.a-generic-type-parameter-instantiated-with-an-alias -->
```maxon
typealias Digit = int(0 to 9)

type Box uses T
	export var item as T

	static function create(v T) returns Self
		return Self{item: v}
	end 'create'

	function put(v T)
		self.item = v
	end 'put'
end 'Box'

typealias RatioBox = Box with Digit

function main() returns ExitCode
	var b = RatioBox.create(1 as Digit)
	b.put(true)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:4: argument type mismatch for 'v': expected 'Digit', got 'bool'
```

<!-- test: error.a-float-narrowed-into-a-ranged-alias-field -->
```maxon
typealias Digit = int(0 to 9)

type Cell
	export var d as Digit

	static function make() returns Cell
		return Cell{d: 1}
	end 'make'
end 'Cell'

function main() returns ExitCode
	var c = Cell.make()
	let w = 2.5
	c.d = w
	print("{c.d}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:15:4: cannot implicitly convert 'float' to 'Digit': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: error.a-digit-local-inside-a-loop-then-a-bool -->
```maxon
typealias Digit = int(0 to 9)

function main() returns ExitCode
	var d = 5 as Digit
	for i in 0 upto 3 'l'
		d = true
	end 'l'
	print("{d}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:3: cannot assign a value of type 'bool' to variable 'd', which holds 'Digit'
```

<!-- test: error.a-ratio-parameter-reassigned-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function f(r Ratio) returns Ratio
	r = true
	return r
end 'f'

function main() returns ExitCode
	print("{f(0.5)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-field-assigned-in-a-method -->
```maxon
typealias Ratio = float(0.0 to 1.0)

type Cell
	var r as Ratio

	static function make() returns Cell
		return Cell{r: 0.5}
	end 'make'

	function flip()
		self.r = true
	end 'flip'
end 'Cell'

function main() returns ExitCode
	var c = Cell.make()
	c.flip()
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:8: cannot assign a value of type 'bool' to field 'r' of 'Cell', which holds 'Ratio'
```

<!-- test: error.a-ratio-local-captured-by-a-closure-then-a-bool -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var r = 0.5 as Ratio
	let f = function() gives r
	r = true
	print("{f()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:2: cannot assign a value of type 'bool' to variable 'r', which holds 'Ratio'
```

<!-- test: error.a-ratio-value-assigned-to-a-bool-variable -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	var b = true
	let w = 0.5 as Ratio
	b = w
	print("{b}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:2: cannot assign a value of type 'Ratio' to variable 'b', which holds 'bool'
```

<!-- test: error.a-digit-value-returned-from-a-bool-function -->
```maxon
typealias Digit = int(0 to 9)

function f() returns bool
	let d = 3 as Digit
	return d
end 'f'

function main() returns ExitCode
	print("{f()}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return 'Digit' from function declared to return 'bool'
```

<!-- test: error.a-ratio-and-a-bool-on-an-operator-names-the-alias -->
```maxon
typealias Ratio = float(0.0 to 1.0)

function main() returns ExitCode
	let r = 0.5 as Ratio
	let flag = true
	let both = r + flag
	print("{both}")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:7:15: Cannot operate on Ratio and bool
```

<!-- test: error.comparing-a-ranged-alias-with-a-bool-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function main() returns ExitCode
	let d = 5 as Digit
	let flag = true
	if d == flag 'same'
		print("same")
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:7: type mismatch: 'cannot compare Digit with bool'
```

<!-- test: error.a-ranged-alias-as-a-condition-names-the-alias -->
```maxon
typealias Digit = int(0 to 9)

function main() returns ExitCode
	let d = 5 as Digit
	if d 'on'
		print("on")
	end 'on'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:5: 'if' requires a bool condition, got 'Digit'
```

<!-- test: error.a-union-cast-to-a-ranged-alias-is-refused -->
```maxon
typealias Coord = int(0 to 1000)

union Shape
	circle(radius Coord)
	empty
end 'Shape'

function main() returns ExitCode
	let s = Shape.circle(5)
	let n = s as Coord
	print("{n}")
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:11:12: Cannot cast from union to int
```

<!-- test: error.an-enum-and-a-bool-on-an-operator-names-the-enum -->
```maxon
enum Color
	red
	green
end 'Color'

function main() returns ExitCode
	let c = Color.red
	let a = c + true
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:9:12: Cannot operate on Color and bool
```

<!-- test: error.an-enum-as-a-condition-names-the-enum -->
```maxon
enum Color
	red
	green
end 'Color'

function main() returns ExitCode
	let c = Color.red
	if c 'cond'
		return 1
	end 'cond'
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:5: 'if' requires a bool condition, got 'Color'
```

<!-- test: error.a-ranged-alias-and-a-bool-on-an-operator-names-the-alias -->
```maxon
typealias Coord = int(0 to 1000)

function main() returns ExitCode
	let c = 5 as Coord
	let a = c + true
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:6:12: Cannot operate on Coord and bool
```

<!-- test: an-integer-enum-keeps-its-numeric-operators -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function main() returns ExitCode
	let c = Color.blue
	let sum = c + 1
	let negated = -c
	let shifted = c shl 1
	let masked = c and c
	let flipped = not c
	print("{sum} {negated} {shifted} {masked} {flipped} {c < Color.green} {c >= Color.green} {c == Color.blue} {c != Color.blue}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 -2 4 2 -3 false true true false
```
