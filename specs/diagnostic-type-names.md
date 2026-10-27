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
(`(int, int)`), and a scalar by its primitive (`int`, `bool`).

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
