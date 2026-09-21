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
