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
(`(int, int)`), a union or enum by its declared name (`Shape`, `Color`), and a scalar by its primitive
(`int`, `bool`). A union value is not a number, so an arithmetic, bitwise or shift operator, unary `-`,
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
error E3059: <fragment>:26:10: type mismatch: 'otherwise type 'Point' does not match expected type 'float''
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
error E3005: <fragment>:14:13: '__Builtins.sleep' requires a integer, but its argument is Point
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
error E2004: <fragment>:11:7: Cannot operate on int and Shape
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

<!-- test: error.a-ranged-alias-and-a-bool-on-an-operator-names-int -->
```maxon
typealias Coord = int(0 to 1000)

function main() returns ExitCode
	let c = 5 as Coord
	let a = c + true
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:6:12: Cannot operate on int and bool
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
