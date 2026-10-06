---
feature: nominal-function-alias
status: stable
keywords: [typealias, function-type, first-class-functions, nominal-types, brand, closure, cast, as]
category: type-system
---

# A Function-Type `typealias` Is a Brand

## Documentation

`typealias Handler = function(n Integer) returns Integer` and `typealias Callback = function(n Integer)
returns Integer` are one SHAPE under two names, and the name is the type. A value that carries `Handler`
— a parameter declared `Handler`, a call result from a `returns Handler` — does not flow into a
`Callback` slot at an argument or a conditional's arm unless the author writes `h as Callback`. A `return`
carries the cast itself: `return h` from a `returns Callback` function is `return h as Callback`. The
SHAPE is still compared there — a two-parameter function returned where a one-parameter type is declared
is refused.

```text
let h = pickHandler()      // returns Handler
runCallback(h)             // E3005: expected 'Callback', got 'Handler'
runCallback(h as Callback) // a re-brand: the same code pointer, the same environment
```

**Decay.** A closure literal and a declared function carry no brand and fit any function alias whose
shape they match. The shape check comes first; the brand check runs only after two shapes agree. A
function alias declared inside a `type` or `extension` body carries no brand either — `Array.SortComparator`
is a name no source outside `Array` can write, so it can never be the name an author chose to distinguish:
a value of any other alias of that shape flows into it, and a value of it flows into any other alias of
that shape. Two FILE-SCOPE aliases still refuse each other, and a directory-qualified one (`api.Score`,
`legacy.Score`) is file-scope — writable in a type position, and a brand.

**Nested positions are nominal.** In `typealias Outer = function(f Handler) returns Integer`, the
parameter type `Handler` is compared by NAME against a candidate's `(f Callback)` — the descent that
`first-class-functions.md` makes by shape stops at an alias name.

**`as` on a capturing closure** re-brands the value and keeps its environment: the fresh value calls with
the captures the closure was made with.

## Tests

<!-- test: error.handler-into-callback-parameter -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function pickHandler() returns Handler
	return addOne
end 'pickHandler'

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function main() returns ExitCode
	let h = pickHandler()
	let r = runCallback(h)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:10: argument type mismatch for 'f': expected 'Callback', got 'Handler'
```

<!-- test: a-branded-comparator-decays-into-the-per-instance-sort-alias -->
A function alias declared inside a type body is not a brand: no source outside the type can write
`Array.SortComparator`, so it is never the name an author chose to tell two shapes apart. A
`RowComparator` value passes to `Array.sort` unchanged, and a value of the nested alias fits a
`RowComparator` slot the same way.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias RowComparator = function(Row, Row) returns Ordering
typealias Rows = Array with Row

type Row
	export var key as Integer

	static function create(key Integer) returns Self
		return Self{key: key}
	end 'create'
end 'Row'

type Sorter
	export var compare as RowComparator

	static function create() returns Self
		return Self{compare: function(left Row, right Row) gives left.key.compare(right.key)}
	end 'create'
end 'Sorter'

function main() returns ExitCode
	var rows = Rows.create()
	rows.push(Row.create(3))
	rows.push(Row.create(1))
	rows.push(Row.create(2))
	let sorter = Sorter.create()
	rows.sort(sorter.compare)

	for row in rows 'each'
		print("{row.key}")
	end 'each'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
123
```

<!-- test: a-handler-converts-at-a-callback-return -->
`return h` from a `returns Callback` function is `return h as Callback`: the same code pointer under the
declared brand.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function pickHandler() returns Handler
	return addOne
end 'pickHandler'

function pickCallback() returns Callback
	let h = pickHandler()
	return h
end 'pickCallback'

function main() returns ExitCode
	let c = pickCallback()
	print("{c(1)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2
```

<!-- test: error.a-different-shape-returned-is-still-refused -->
The line that does not move: the `return` converts a brand, never a shape. `Pair` takes two arguments and
`Unary` one, so a `Pair` value returned where a `Unary` is declared is refused.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Unary = function(n Integer) returns Integer
typealias Pair = function(a Integer, b Integer) returns Integer

function addBoth(a Integer, b Integer) returns Integer
	return a + b
end 'addBoth'

function pickPair() returns Pair
	return addBoth
end 'pickPair'

function pickUnary() returns Unary
	let p = pickPair()
	return p
end 'pickUnary'

function main() returns ExitCode
	let u = pickUnary()
	print("{u(1)}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:2: function type mismatch in return: expected 'fn(int) returns int', got 'fn(int, int) returns int'
```

<!-- test: error.ternary-over-two-function-aliases -->
A conditional merges its arms through one slot, and that slot has one brand.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function addTwo(n Integer) returns Integer
	return n + 2
end 'addTwo'

function pickHandler() returns Handler
	return addOne
end 'pickHandler'

function pickCallback() returns Callback
	return addTwo
end 'pickCallback'

function main() returns ExitCode
	let h = pickHandler()
	let cb = pickCallback()
	let k = 3 as Integer
	let chosen = h if k > 2 else cb
	print("{chosen(1)}")
	return 0
end 'main'
```
```maxoncstderr
error E2028: <fragment>:26:17: ternary expression type mismatch: true branch is 'Handler' but false branch is 'Callback'
```

<!-- test: a-declared-function-merges-with-an-alias-typed-parameter-in-a-ternary -->
A declared function carries no brand, so it merges with a `UnaryOp` parameter in one conditional, and the
merged value calls whichever arm the condition chose.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function triple(n Integer) returns Integer
	return n * 3
end 'triple'

function pick(f UnaryOp, flag bool) returns Integer
	let g = f if flag else triple
	return g(2)
end 'pick'

function main() returns ExitCode
	print("{pick(twice, flag: true)} {pick(twice, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 6
```

<!-- test: error.ternary-of-an-alias-typed-parameter-and-a-different-shape -->
The shapes are still compared when one arm carries no brand: a two-parameter function does not merge with
a `UnaryOp` parameter.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function triple(n Integer) returns Integer
	return n * 3
end 'triple'

function addBoth(a Integer, b Integer) returns Integer
	return a + b
end 'addBoth'

function pick(f UnaryOp, flag bool) returns Integer
	let g = f if flag else addBoth
	return g(2)
end 'pick'

function main() returns ExitCode
	print("{pick(triple, flag: true)}")
	return 0
end 'main'
```
```maxoncstderr
error E2028: <fragment>:14:12: ternary expression type mismatch: true branch is 'fn(Integer) returns Integer' but false branch is 'fn(Integer, Integer) returns Integer'
```

<!-- test: an-alias-over-a-string-merges-with-a-declared-function -->
```maxon
typealias Label = function(label String) returns String

function plain(label String) returns String
	return label
end 'plain'

function shout(label String) returns String
	return "{label}!"
end 'shout'

function pick(f Label, flag bool) returns String
	let g = f if flag else plain
	return g("p")
end 'pick'

function main() returns ExitCode
	print("{pick(shout, flag: true)} {pick(shout, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
p! p
```

<!-- test: an-alias-over-a-struct-merges-with-a-declared-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export let x as Integer
	export let y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

typealias Measure = function(at Point) returns Integer

function sum(at Point) returns Integer
	return at.x + at.y
end 'sum'

function product(at Point) returns Integer
	return at.x * at.y
end 'product'

function pick(f Measure, flag bool) returns Integer
	let g = f if flag else sum
	return g(Point.create(3, y: 4))
end 'pick'

function main() returns ExitCode
	print("{pick(product, flag: true)} {pick(product, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12 7
```

<!-- test: an-alias-over-an-enum-merges-with-a-declared-function -->
```maxon
enum Shade
	light
	dark
end 'Shade'

typealias Flip = function(shade Shade) returns Shade

function keep(shade Shade) returns Shade
	return shade
end 'keep'

function invert(shade Shade) returns Shade
	match shade 'k'
		light then return Shade.dark
		dark then return Shade.light
	end 'k'
end 'invert'

function pick(f Flip, flag bool) returns String
	let g = f if flag else keep
	match g(Shade.light) 'k'
		light then return "light"
		dark then return "dark"
	end 'k'
end 'pick'

function main() returns ExitCode
	print("{pick(invert, flag: true)} {pick(invert, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
dark light
```

<!-- test: an-alias-with-a-nested-alias-parameter-merges-with-a-declared-function -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = function(n Integer) returns Integer
typealias Driver = function(step Step, n Integer) returns Integer

function applyOnce(step Step, n Integer) returns Integer
	return step(n)
end 'applyOnce'

function applyTwice(step Step, n Integer) returns Integer
	return step(step(n))
end 'applyTwice'

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function pick(d Driver, flag bool) returns Integer
	let g = d if flag else applyOnce
	return g(addOne, 10)
end 'pick'

function main() returns ExitCode
	print("{pick(applyTwice, flag: true)} {pick(applyTwice, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12 11
```

<!-- test: an-alias-from-another-directory-merges-with-a-declared-function -->
`api.Score` is declared over `api`'s own private `Integer`, and `addOne` over `app`'s: the two arms still
merge.
```maxon
// --- file: api/types.maxon
typealias Integer = int(i64.min to i64.max)
export typealias Score = function(n Integer) returns Integer

// --- file: app/main.maxon
typealias Integer = int(i64.min to i64.max)

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function pick(f api.Score, flag bool) returns Integer
	let g = f if flag else addOne
	return g(20)
end 'pick'

function main() returns ExitCode
	print("{pick(twice, flag: true)} {pick(twice, flag: false)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
40 21
```

<!-- test: error.nested-alias-position-is-nominal -->
`Outer` declares its parameter as a `Handler`; `runner` takes a `Callback`. Same shape one level down,
different name — refused, and the diagnostic names the nested aliases.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer
typealias Outer = function(f Handler) returns Integer

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function runner(f Callback) returns Integer
	return f(41)
end 'runner'

function drive(o Outer) returns Integer
	return o(addOne)
end 'drive'

function main() returns ExitCode
	let r = drive(runner)
	print("{r}")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:10: argument type mismatch for 'o': expected 'fn(Handler) returns int', got 'fn(Callback) returns int'
```

<!-- test: a-closure-literal-decays-into-any-function-alias -->
The decay control: a closure literal — bound to a `let` or written at the argument — carries no brand and
fits both `Handler` and `Callback`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function runHandler(f Handler) returns Integer
	return f(10)
end 'runHandler'

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function main() returns ExitCode
	let addThree = function(n Integer) gives n + 3
	let viaLiteral = runCallback(function(n Integer) gives n + 4)
	print("{runHandler(addThree)} {runCallback(addThree)} {viaLiteral}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
13 23 24
```

<!-- test: a-declared-function-decays-into-any-function-alias -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function addOne(n Integer) returns Integer
	return n + 1
end 'addOne'

function runHandler(f Handler) returns Integer
	return f(10)
end 'runHandler'

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function main() returns ExitCode
	print("{runHandler(addOne)} {runCallback(addOne)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 21
```

<!-- test: as-rebrands-a-capturing-closure -->
`capturing` closes over `base`; `capturing as Callback` is a fresh value that must still call with that
environment (50, not 20). The second half re-brands a `Handler` call result the same way.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Handler = function(n Integer) returns Integer
typealias Callback = function(n Integer) returns Integer

function runCallback(f Callback) returns Integer
	return f(20)
end 'runCallback'

function pickHandler() returns Handler
	return function(n Integer) gives n + 1
end 'pickHandler'

function main() returns ExitCode
	let base = 30 as Integer
	let capturing = function(n Integer) gives n + base
	let asCallback = capturing as Callback
	let h = pickHandler()
	print("{runCallback(asCallback)} {runCallback(h as Callback)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
50 21
```
