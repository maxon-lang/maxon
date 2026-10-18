---
feature: default-values
status: experimental
keywords: [default, default-values, parameters, string-default, array-default, enum-default, bool-default]
category: core
---

# Default Parameter Values

## Documentation

Function parameters can have default values that are used when the argument is omitted at the call site. Default values can be any literal: integers, floats, booleans, strings, arrays, and enum cases.

### Integer Defaults

```text
typealias Count = int(0 to 1000)

function repeat(value Count, times Count = 1) returns Count
  return value * times
end 'repeat'

var result = repeat(7)  // times defaults to 1, result is 7
```

### String Defaults

```text
function greet(name String = "World") returns ExitCode
  print("Hello, {name}!")
  return 0
end 'greet'

greet()                    // prints "Hello, World!"
greet("Maxon")       // prints "Hello, Maxon!"
```

### Array Defaults

```text
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [1, 2, 3]) returns Integer
  var total = 0
  for item in items 'loop'
    total = total + item
  end 'loop'
  return total
end 'sum'

var result = sum()  // uses default [1, 2, 3], result is 6
```

### Enum Defaults

```text
enum Color
  red
  green
  blue
end 'Color'

function paint(color Color = Color.blue) returns ExitCode
  return color.rawValue
end 'paint'

var result = paint()  // uses default Color.blue, result is 2
```

### A Defaulted First Parameter May Be Named

A call's first argument may carry a `name:` label when the callee's first parameter (after a method's
receiver, or a message's `self`) has a default. A default may sit on any parameter, before or after the
required ones:

```text
function connect(port Integer = 80, retries Integer = 3)
connect()             // port 80, retries 3
connect(8080)         // port 8080, retries 3
connect(port: 8080)   // port 8080, retries 3
connect(retries: 5)   // port 80, retries 5

function place(a Integer = 1, b Integer) returns Integer
place(b: 2)           // a is 1
```

A first argument whose parameter is required stays positional; labelling it is `E2052`.

## Tests

<!-- test: default-values.string-default-omitted -->
```maxon
function greet(name String = "World") returns ExitCode
	print("Hello, {name}!")
	return 0
end 'greet'

function main() returns ExitCode
	return greet()
end 'main'
```
```stdout
Hello, World!
```
```exitcode
0
```

<!-- test: default-values.string-default-provided -->
```maxon
function greet(name String = "World") returns ExitCode
	print("Hello, {name}!")
	return 0
end 'greet'

function main() returns ExitCode
	return greet("Maxon")
end 'main'
```
```stdout
Hello, Maxon!
```
```exitcode
0
```

<!-- test: default-values.string-default-empty -->
```maxon
function show(label String = "") returns ExitCode
	print("[{label}]")
	return 0
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```stdout
[]
```
```exitcode
0
```

<!-- test: default-values.array-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [10, 20, 12]) returns Integer
	var total = 0 as Integer
	for item in items 'loop'
		total = total + item
	end 'loop'
	return total
end 'sum'

function main() returns ExitCode
	return sum()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.array-default-provided -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function sum(items IntArray = [10, 20, 12]) returns Integer
	var total = 0 as Integer
	for item in items 'loop'
		total = total + item
	end 'loop'
	return total
end 'sum'

function main() returns ExitCode
	return sum([1, 2, 3])
end 'main'
```
```exitcode
6
```

<!-- test: default-values.bool-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

function choose(flag bool = true, a Integer = 40, b Integer = 10) returns Integer
	if flag 'check'
		return a + 2
	end 'check' else 'other'
		return b
	end 'other'
end 'choose'

function main() returns ExitCode
	return choose()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.enum-default-omitted -->
```maxon
enum Color
	red
	green
	blue
end 'Color'

function paint(color Color = Color.blue) returns ExitCode
	return color.rawValue
end 'paint'

function main() returns ExitCode
	return paint()
end 'main'
```
```exitcode
2
```

<!-- test: default-values.mixed-defaults -->
```maxon
typealias Integer = int(i64.min to i64.max)

function process(label String = "result", value Integer = 42) returns ExitCode
	print("{label}: {value}")
	return 0
end 'process'

function main() returns ExitCode
	return process()
end 'main'
```
```stdout
result: 42
```
```exitcode
0
```

<!-- test: default-values.string-default-with-escape -->
```maxon
function show(msg String = "line1\nline2") returns ExitCode
	print(msg)
	return 0
end 'show'

function main() returns ExitCode
	return show()
end 'main'
```
```stdout
line1
line2
```
```exitcode
0
```

<!-- test: default-values.float-default-omitted -->
A float default on a ranged float alias, interpolated.
```maxon
typealias Number = float(f64.min to f64.max)

function scale(factor Number = 2.5) returns ExitCode
	print("{factor}")
	return 0
end 'scale'

function main() returns ExitCode
	return scale()
end 'main'
```
```stdout
2.5
```
```exitcode
0
```

<!-- test: default-values.struct-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function sum(p Point = Point.create(10, y: 32)) returns Integer
	return p.x + p.y
end 'sum'

function main() returns ExitCode
	return sum()
end 'main'
```
```exitcode
42
```

<!-- test: default-values.struct-default-provided -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function sum(p Point = Point.create(10, y: 32)) returns Integer
	return p.x + p.y
end 'sum'

function main() returns ExitCode
	return sum(Point.create(1, y: 2))
end 'main'
```
```exitcode
3
```

<!-- test: default-values.char-default-omitted -->
```maxon
function initial(c Character = 'A') returns ExitCode
	print("{c}")
	return 0
end 'initial'

function main() returns ExitCode
	return initial()
end 'main'
```
```stdout
A
```
```exitcode
0
```

<!-- test: default-values.bytestring-default-omitted -->
```maxon
typealias Integer = int(i64.min to i64.max)

function checkLen(data ByteArray = b"hello") returns Integer
	return data.count()
end 'checkLen'

function main() returns ExitCode
	return checkLen()
end 'main'
```
```exitcode
5
```

### A top-level factory call fills its defaults too

⭐ **A DEFAULT IS THE DECLARATION'S, NOT THE CALL POSITION'S.** A global's initializer is the one place
a call is written outside a function body, and it walks the source itself rather than emitting ops — so
it has to fill omitted arguments on its own. Left out, a declaration with a defaulted parameter was
`E3036 expects 6 argument(s) but 5 were provided` at module scope for a call every body admits.

⚠ **BOTH ARENA SHAPES ARE HERE.** `Pair.create` is nothing but its own `Self{…}` over its parameters, so
it FOLDS into a construction and never runs; `Box.make` multiplies, so it stays an ordinary call
`__module_init` makes before `main`. The fill precedes that fork, which is what puts the two on one
footing.

<!-- test: default-values.top-level-factory-fills-defaults -->
```maxon
typealias Count = int(0 to 255)

type Box
	export let n as Count
	export let tag as String

	static function make(n Count, scale Count = 3, tag String = "d") returns Box
		return Self{n: n * scale, tag: tag}
	end 'make'
end 'Box'

type Pair
	export let a as Count
	export let b as Count

	static function create(a Count, b Count = 9) returns Pair
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

var boxed = Box.make(2)
var paired = Pair.create(1)

function main() returns ExitCode
	print("{boxed.n} {boxed.tag} {paired.a} {paired.b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6 d 1 9
```

### A defaulted container at module scope is materialized, and freed

⚠ **A DEFAULT MAY BE MANAGED, AND THE SLOT THEN OWNS WHAT THE CALL BORROWED.** `tags StringArray =
StringArray.create()` fills with a call to the parameter's synthesized helper, and `__module_init` is
sole owner of the array that helper returns — so the exit-101 leak gate is what this case is really
asserting, over and above the count it prints.

<!-- test: default-values.top-level-factory-fills-a-managed-default -->
```maxon
typealias Names = Array with String

type Roster
	export let names as Names

	static function of(names Names = Names.create()) returns Roster
		return Self{names: names}
	end 'of'
end 'Roster'

var empty = Roster.of()

function main() returns ExitCode
	return empty.names.count() as ExitCode
end 'main'
```
```exitcode
0
```

### Error: A default value must consume everything up to the `,` or `)` that ends it

The capture walks to the delimiter that ends the default, and the expression parsed out of that region
has to reach it. Anything left over is text the author wrote and the compiler was about to ignore —
`b Integer = 7 zzz` silently defaulted to 7, exactly as `"{7 zzz}"` silently printed 7.

<!-- test: default-values.error.param-default-trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

function f(a Integer, b Integer = 7 zzz) returns Integer
	return a + b
end 'f'

function main() returns ExitCode
	return f(1)
end 'main'
```
```maxoncstderr
error E2010: specs/default-values/default-values.error.param-default-trailing-tokens.maxon:4:37: Expected 'end of default value' but got 'zzz'
```

### Error: An exponent without a decimal point is not a float default either

`1e100` is the integer `1` followed by the identifier `e100` — a float literal must contain a decimal
point. Dropped, it made `b Real = 1e100` default to `1`: a hundred orders of magnitude, silently.
Write `1.0e100`.

<!-- test: default-values.error.param-default-exponent-without-point -->
```maxon
typealias Real = float(f64.min to f64.max)

function f(a Real, b Real = 1e100) returns Real
	return a + b
end 'f'

function main() returns ExitCode
	print("{f(1.0)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2010: specs/default-values/default-values.error.param-default-exponent-without-point.maxon:4:30: Expected 'end of default value' but got 'e100'
```

### A defaulted first parameter may be named

<!-- test: default-values.a-defaulted-first-parameter-may-be-named -->
```maxon
typealias Integer = int(i64.min to i64.max)

function connect(port Integer = 80, retries Integer = 3)
	print("port={port} retries={retries}\n")
end 'connect'

function main() returns ExitCode
	connect()
	connect(8080)
	connect(port: 8080)
	connect(retries: 5)
	connect(retries: 5, port: 1)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
port=80 retries=3
port=8080 retries=3
port=8080 retries=3
port=80 retries=5
port=1 retries=5
```

<!-- test: default-values.a-method-with-a-defaulted-first-parameter-may-name-it -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Probe
	let base as Integer

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'

	function configure(retries Integer = 3, delay Integer = 10) returns String
		return "base={self.base} retries={retries} delay={delay}"
	end 'configure'
end 'Probe'

function main() returns ExitCode
	let p = Probe.create(1)
	let delayed = p.configure(delay: 7)
	let retried = p.configure(retries: 1)
	print("{delayed}\n")
	print("{retried}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
base=1 retries=3 delay=7
base=1 retries=1 delay=10
```

<!-- test: default-values.a-static-create-with-a-defaulted-first-parameter-may-name-it -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Grid
	export let size as Integer
	export let fill as Integer

	static function create(size Integer = 4, fill Integer = 0) returns Self
		return Self{size: size, fill: fill}
	end 'create'
end 'Grid'

function main() returns ExitCode
	let g = Grid.create(fill: 1)
	print("size={g.size} fill={g.fill}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
size=4 fill=1
```

<!-- test: default-values.an-omitted-defaulted-first-parameter-lets-a-required-one-be-named-first -->
```maxon
typealias Integer = int(i64.min to i64.max)

function place(a Integer = 1, b Integer) returns Integer
	return a * 10 + b
end 'place'

function main() returns ExitCode
	let omitted = place(b: 2)
	let given = place(5, b: 2)
	print("{omitted} {given}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12 52
```

<!-- test: default-values.overloads-agreeing-on-a-defaulted-first-parameter-may-name-it -->
```maxon
typealias Integer = int(i64.min to i64.max)

function scale(factor Integer = 2, unit String) returns String
	return "{factor} {unit}"
end 'scale'

function scale(factor Integer = 2, unit bool) returns String
	if unit 'metric'
		return "{factor} metric"
	end 'metric'

	return "{factor} imperial"
end 'scale'

function main() returns ExitCode
	let named = scale(factor: 3, unit: "cm")
	let flagged = scale(factor: 4, unit: true)
	let namedDefault = scale(unit: "mm")
	let flaggedDefault = scale(unit: false)
	print("{named}\n{flagged}\n{namedDefault}\n{flaggedDefault}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 cm
4 metric
2 mm
2 imperial
```

<!-- test: default-values.an-async-call-may-name-a-defaulted-first-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

function fetch(retries Integer = 3, delay Integer = 10) returns Integer
	_ = File.exists(FilePath from "noyield.txt")
	return retries * 100 + delay
end 'fetch'

function main() returns ExitCode
	let promise = async fetch(retries: 5)
	let result = await promise
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
510
```

<!-- test: default-values.a-module-scope-constant-may-name-a-defaulted-first-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Scaler
	export let factor as Integer
	export let offset as Integer

	static function make(scale Integer = 1, offset Integer = 0) returns Scaler
		return Self{factor: scale * 2, offset: offset}
	end 'make'
end 'Scaler'

let tripled = Scaler.make(scale: 3)

function main() returns ExitCode
	print("{tripled.factor} {tripled.offset}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6 0
```

<!-- test: default-values.a-generic-function-with-a-defaulted-first-parameter-may-name-it -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pick(times Integer = 1, item T) uses T returns T
	print("times={times}\n")
	return item
end 'pick'

function main() returns ExitCode
	let s = pick(item: "x")
	let n = pick(times: 2, item: 7 as Integer)
	print("{s} {n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
times=1
times=2
x 7
```

<!-- test: default-values.a-generic-types-create-with-a-defaulted-first-parameter-may-name-it -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Crate uses T
	export let size as Integer
	export var item as T

	static function create(size Integer = 1, item T) returns Self
		return Self{size: size, item: item}
	end 'create'
end 'Crate'

typealias StringCrate = Crate with String

function main() returns ExitCode
	let c = StringCrate.create(item: "s")
	let d = StringCrate.create(size: 3, item: "t")
	print("{c.size} {c.item}\n{d.size} {d.item}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 s
3 t
```

### Error: A required first parameter stays positional

<!-- test: default-values.error.a-required-first-parameter-is-still-positional -->
```maxon
typealias Integer = int(i64.min to i64.max)

function shift(a Integer, b Integer = 2) returns Integer
	return a + b
end 'shift'

function main() returns ExitCode
	return shift(a: 1, b: 3)
end 'main'
```
```maxoncstderr
error E2052: <fragment>:9:15: the first argument cannot be named because its parameter has no default
```

<!-- test: default-values.error.a-module-scope-constant-may-not-name-a-required-first-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Window
	export let width as Integer
	export let height as Integer

	static function make(width Integer, height Integer = 1) returns Window
		return Self{width: width, height: height}
	end 'make'
end 'Window'

let wide = Window.make(width: 8, height: 2)

function main() returns ExitCode
	print("{wide.width} {wide.height}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2052: <fragment>:13:24: the first argument cannot be named because its parameter has no default
```

<!-- test: default-values.error.a-callee-without-defaults-keeps-its-first-argument-positional -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Couple
	let a as Integer
	let b as Integer

	static function create(a Integer, b Integer) returns Self
		return Self{a: a, b: b}
	end 'create'

	function combine(first Integer, second Integer) returns Integer
		return a + b + first + second
	end 'combine'
end 'Couple'

function main() returns ExitCode
	let p = Couple.create(1, b: 2)
	return p.combine(first: 3, second: 4)
end 'main'
```
```maxoncstderr
error E2052: <fragment>:19:19: the first argument cannot be named because its parameter has no default
```
