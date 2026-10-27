---
feature: top-level-let
status: stable
keywords: let, global, constant, compile-time
category: language
---
# Top Level Let

## Documentation

## Top-Level Let Declarations

Top-level `let` declarations bind values at module scope. An initializer is read by the function body's expression parser, so it accepts every expression a body does, with the same meaning. A scalar binding is a compile-time constant inlined at every use; a record whose value is decided at compile time is laid down in the executable's read-only data, and any other record is built by `__module_init` before `main`.

### Syntax

```maxon
let CONSTANT_NAME = expression
export let EXPORTED_NAME = expression
```

### Features

- **Body expressions**: An initializer is any expression a function body accepts, typed and diagnosed as a body types and diagnoses it
- **Type inference**: Type is inferred from the initializer, and a derived constant keeps its operands' typealias
- **Forward references**: Constants can reference other constants declared later in the file, or in another file that exports them
- **Export support**: Use `export let` to make constants available to other modules

### Constant Expressions

A scalar binding's initializer must have a value the body parser knows at compile time:
- Literals: integers, floats, booleans
- Arithmetic, comparison, logical (`and`, `or`, `not`) and bitwise operators over known values
- Casts, `countof(T)`, a sized type's `.min` / `.max`, and a builtin over constants (`sqrt(4.0)`)
- References to other top-level constants
- Enum member access: `Color.Red`

A call whose value is not known at compile time is **E2045**. Strings, byte strings, characters, arrays,
maps, tuples and other records are not scalars: a constant one is laid down as read-only data, and any other
is built before `main`.

### Examples

```maxon
let PI = 3.14159265358979
let TAU = PI * 2.0
let MAX_SIZE = 1024
let DEBUG = false
let GREETING = "Hello, World!"
let PRIMES = [2, 3, 5, 7, 11]
```

### Restrictions

- A call that returns a record runs in `__module_init` before `main`; a call that returns a scalar not
  known at compile time is **E2045**
- Initializers that depend on each other in a cycle are **E2012**
- A top-level `var` is module state rather than a constant: its initializer runs before `main`, and any
  function in the file may reassign it (`docs/LANGUAGE_REFERENCE.md`, *Top-Level Variables*)

---

## Tests

<!-- test: basic-integer-constant -->
```maxon
let ANSWER = 42

function main() returns ExitCode
	return ANSWER
end 'main'
```
```exitcode
42
```

<!-- test: basic-float-constant -->
```maxon
let PI = 3.14

function main() returns ExitCode
	return trunc(PI)
end 'main'
```
```exitcode
3
```

<!-- test: arithmetic-in-constant -->
```maxon
let BASE = 10
let DOUBLED = BASE * 2

function main() returns ExitCode
	return DOUBLED
end 'main'
```
```exitcode
20
```

<!-- test: forward-reference -->
```maxon
let TOTAL = FIRST + SECOND
let FIRST = 30
let SECOND = 12

function main() returns ExitCode
	return TOTAL
end 'main'
```
```exitcode
42
```

<!-- test: boolean-constant -->
```maxon
let DEBUG = true

function main() returns ExitCode
	if DEBUG 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: constant-in-expression -->
```maxon
let OFFSET = 10

function main() returns ExitCode
	let x = 5
	return x + OFFSET
end 'main'
```
```exitcode
15
```

<!-- test: multiple-constants -->
```maxon
let A = 1
let B = 2
let C = 3

function main() returns ExitCode
	return A + B + C
end 'main'
```
```exitcode
6
```

<!-- test: unary-minus-in-constant -->
```maxon
let NEGATIVE = -42

function main() returns ExitCode
	return -NEGATIVE
end 'main'
```
```exitcode
42
```

<!-- test: comparison-in-constant -->
```maxon
let IS_LARGE = 100 > 50

function main() returns ExitCode
	if IS_LARGE 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: logical-operations -->
```maxon
let BOTH = true and true
let EITHER = false or true
let NEITHER = not false

function main() returns ExitCode
	if BOTH and EITHER and NEITHER 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: a-top-level-short-circuit-is-a-constant -->
`and` and `or` over constants are constants themselves, so they initialize a scalar `var`, whose initializer must be known before `main`.
```maxon
let BOTH = true and true
let EITHER = false or true
let NEITHER = false and true
var READY = BOTH and EITHER and not NEITHER

function main() returns ExitCode
	print("{BOTH} {EITHER} {NEITHER} {READY}\n")
	READY = false
	print("{READY}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true true false true
false
```

<!-- test: cast-in-constant -->
```maxon
let OFFSET = 10
let SCALED = OFFSET * 3

function main() returns ExitCode
	return SCALED
end 'main'
```
```exitcode
30
```

<!-- test: complex-constant-chain -->
```maxon
let A = 2
let B = A * 3
let C = B + 4
let D = C * 2

function main() returns ExitCode
	return D
end 'main'
```
```exitcode
20
```

<!-- test: float-arithmetic-in-constant -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
Top-level float constants fold `+`, `*`, and `/` with the host's f64 (X=3.0, Y=6.0, Z=2.5). x64-windows only: the exit code 362 is outside `ExitCode` everywhere but there. `ExitCode` is `int(0 to u32.max)` on Windows and `int(0 to 255)` on Linux, macOS and WASI, so the computed 362 panics at the `return` on those lanes. The three sibling tests below (codes <= 255) exercise float const folding on every other target.
```maxon
let X = 1.0 + 2.0
let Y = X * 2.0
let Z = 10.0 / 4.0

function main() returns ExitCode
	return trunc(X)*100 + trunc(Y)*10 + trunc(Z)
end 'main'
```
```exitcode
362
```

<!-- test: float-mixed-int-promotion-in-constant -->
A mixed int/float constant promotes the int operand to f64 before folding, exactly as the runtime path does (M=3.0, N=7.5).
```maxon
let M = 1 + 2.0
let N = 5 * 1.5

function main() returns ExitCode
	return trunc(M) * 10 + trunc(N)
end 'main'
```
```exitcode
37
```

<!-- test: float-subtraction-and-negation-in-constant -->
Float subtraction and a negated float literal fold (A=6.5, B=-2.0, C=4.5).
```maxon
let A = 10.0 - 3.5
let B = -2.0
let C = A + B

function main() returns ExitCode
	return trunc(C)
end 'main'
```
```exitcode
4
```

<!-- test: float-comparison-in-constant -->
Float comparisons fold with a real f64 compare, so a negative operand orders correctly where an integer compare over the raw bit patterns would answer backwards (both true).
```maxon
let LT = -1.0 > -2.0
let GE = 2.5 >= 2.5

function main() returns ExitCode
	if LT and GE 'both'
		return 1
	end 'both'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: function-call-in-constant-error -->
Function calls are not allowed in constant expressions.
```maxon
typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
	return 42
end 'compute'

let RESULT = compute()

function main() returns ExitCode
	return RESULT
end 'main'
```
```maxoncstderr
error E2045: <fragment>:8:14: Function calls are not allowed in global variable initializers; 'compute()' is not a constant expression
```

<!-- test: circular-dependency-error -->
```maxon
let A = B + 1
let B = A + 1

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2012: <fragment>:3:5: Circular dependency detected among global constants: A, B
```

<!-- test: export-let-cross-file -->
Exported constants are visible from other files.
```maxon
// --- file: api/constants.maxon
export let MAGIC = 42
export let OFFSET = -10

// --- file: app/main.maxon
function main() returns ExitCode
	return MAGIC + OFFSET
end 'main'
```
```exitcode
32
```

<!-- test: file-private-same-name-cross-file -->
A file-private (non-exported) top-level `let` is scoped to its declaring file, so two files may declare the same bare name with different values. Each file's reads resolve to its OWN constant.
```maxon
// --- file: featA/a.maxon
let SHARED = 99

export function getA() returns ExitCode
	return SHARED as ExitCode
end 'getA'

// --- file: featB/b.maxon
let SHARED = 7

export function getB() returns ExitCode
	return SHARED as ExitCode
end 'getB'

// --- file: app/main.maxon
function main() returns ExitCode
	return getA() - getB()
end 'main'
```
```exitcode
92
```

<!-- test: file-private-same-name-same-directory-every-kind -->
Three files of ONE directory each declare file-private `let`s of the same names, of every kind a `let`
can be: a number, a constant derived from it, a `String`, a byte string, an array and a record built by a
factory. Each file's reads, in its own functions, resolve to its OWN declaration; the file that
declares nothing but `main` reads the third pair of declarations. The order the files arrive in does not
change an answer.
```maxon
// --- file: a.maxon
let base = 5
let derived = base * 2
let name = "first"
let label = b"alpha"
let table = [base, derived, 3]
let anchor = Tag.create(base)

type Tag
	var weight as Count

	static function create(weight Count) returns Self
		return Self{weight: weight}
	end 'create'

	function value() returns Count
		return self.weight
	end 'value'
end 'Tag'

export function describeA() returns String
	return "{name} {String.from(label)} {derived} {table.count()} {anchor.value()}"
end 'describeA'

// --- file: b.maxon
let base = 7
let derived = base * 3
let name = "second"
let label = b"beta"
let table = [base, derived, 3, 4]
let anchor = Pin.create(base)

type Pin
	var weight as Count

	static function create(weight Count) returns Self
		return Self{weight: weight}
	end 'create'

	function value() returns Count
		return self.weight
	end 'value'
end 'Pin'

export function describeB() returns String
	return "{name} {String.from(label)} {derived} {table.count()} {anchor.value()}"
end 'describeB'

// --- file: main.maxon
let base = 11
let name = "third"

function main() returns ExitCode
	print("{describeA()}\n{describeB()}\n{name} {base}\n")
	return 0
end 'main'
```
```stdout
first alpha 10 3 5
second beta 21 4 7
third 11
```

<!-- test: from-literal-initializer -->
Top-level let with `Type from "literal"` syntax (runtime-initialized via `__module_init`).
```maxon
let path = FilePath from "test.txt"

function main() returns ExitCode
	print(path.toString())
	return 0
end 'main'
```
```stdout
test.txt
```

<!-- test: a-top-level-array-literal-of-tuples -->
A module-scope `Alias from [ ... ]` whose elements are tuples reads back each element's fields, as the
same literal does in a body.
```maxon
typealias Word = bits(64)
typealias Step = (Word, Word)
typealias Steps = Array with Step

let steps = Steps from [(0, 1), (2, 3)]

function main() returns ExitCode
	print("{steps.count()}")

	for step in steps 'eachStep'
		print(" {step.0} {step.1}")
	end 'eachStep'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 0 1 2 3
```

<!-- test: a-top-level-array-literal-of-three-tuples -->
A tuple element of three fields, one of them a string, takes the same module-scope form.
```maxon
typealias Index = int(0 to 100)
typealias Entry = (Index, String, bool)
typealias Entries = Array with Entry

let entries = Entries from [(1, "one", true), (2, "two", false)]

function main() returns ExitCode
	print("{entries.count()}")

	for entry in entries 'eachEntry'
		print(" {entry.0} {entry.1} {entry.2}")
	end 'eachEntry'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 one true 2 two false
```

<!-- test: a-top-level-array-literal-of-nested-tuples -->
A tuple element that is itself a tuple takes the stated element type's columns in turn.
```maxon
typealias Word = bits(64)
typealias Inner = (Word, Word)
typealias Outer = (Inner, Word)
typealias Outers = Array with Outer

let outers = Outers from [((1, 2), 3), ((4, 5), 6)]

function main() returns ExitCode
	print("{outers.count()}")

	for outer in outers 'eachOuter'
		print(" {outer.0.0} {outer.0.1} {outer.1}")
	end 'eachOuter'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 2 3 4 5 6
```

<!-- test: a-static-struct-literal-tuple-field -->
A struct literal's field declared at a tuple type takes a tuple literal in a static initializer.
```maxon
typealias Word = bits(64)
typealias Step = (Word, Word)

type Holder
	export var step as Step

	static var origin = Holder{step: (7, 9)}

	static function get() returns Holder
		return Holder.origin
	end 'get'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.get()
	print("{holder.step.0} {holder.step.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 9
```

<!-- test: a-top-level-let-of-an-unstated-tuple -->
A module-scope `let` whose initializer is a tuple literal takes the tuple type the literal states, as `let t = (1, 2)`
does in a body, and two such constants of one type share it.
```maxon
let first = (1, 2)
let second = (30, 40)

function main() returns ExitCode
	print("{first.0} {first.1} {second.0} {second.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 30 40
```

<!-- test: a-top-level-let-of-an-unstated-tuple-that-owns-a-string -->
A tuple constant with a `String` column is built before `main` and released after it, through the destructor
the tuple type owes, exactly as the same literal is in a body.
```maxon
typealias Num = int(0 to 1000)

let named = (7, "seven")
var other = ("a", "b", 3 as Num)

function main() returns ExitCode
	print("{named.0} {named.1} {other.0}{other.1} {other.2}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 seven ab 3
```

<!-- test: error.a-top-level-var-of-an-unstated-tuple-of-bare-numbers -->
A module-scope `var` born from a tuple of bare numbers is refused as it is in a body.
```maxon
var point = (10, 20)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:13: A var cannot start from a bare number: write 'var name = expr as Type'.
```

<!-- test: a-top-level-let-of-an-unstated-tuple-holding-an-empty-container -->
A column that is an empty container built by its alias keeps the alias in the tuple's identity, as it does in a body.
```maxon
typealias Item = int(0 to 1000)
typealias Items = Array with Item

let held = (Items.create(), "held")

function main() returns ExitCode
	print("{held.0.count()} {held.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 held
```

<!-- test: a-top-level-let-of-an-unstated-tuple-holding-a-construction-by-its-alias -->
A column built by its alias's `from` keeps the alias in the tuple's identity, as it does in a body, so the constant and
a body-built tuple over the same construction are one type and share an array.
```maxon
typealias Item = int(0 to 1000)
typealias Items = Array with Item

let held = (Items from [7], "held")

function main() returns ExitCode
	let fresh = (Items from [7, 8], "fresh")
	let both = [fresh, held.clone()]

	for pair in both 'eachPair'
		print("{pair.0.count()} {pair.1}\n")
	end 'eachPair'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 fresh
1 held
```

<!-- test: a-top-level-let-of-an-unstated-nested-tuple -->
A tuple whose element is a tuple states both types, with no alias written anywhere.
```maxon
let nested = ((1, "one"), (2, 3))

function main() returns ExitCode
	print("{nested.0.0} {nested.0.1} {nested.1.0} {nested.1.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 one 2 3
```

<!-- test: a-tuple-argument-of-a-top-level-factory -->
A factory parameter declared at a tuple type takes a tuple literal in a file-scope call, as it does in a body.
```maxon
typealias Num = int(0 to 1000)
typealias Step = (Num, String)

type Pair
	export var step as Step

	static function create(step Step) returns Pair
		return Pair{step: step}
	end 'create'
end 'Pair'

let p = Pair.create((1, "one"))

function main() returns ExitCode
	print("{p.step.0} {p.step.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 one
```

<!-- test: a-tuple-argument-written-in-the-factory-signature -->
The same call when the parameter spells its tuple type inline instead of through an alias.
```maxon
typealias Num = int(0 to 1000)

type Pair
	export var a as Num
	export var b as Num

	static function create(ends (Num, Num)) returns Pair
		return Pair{a: ends.0, b: ends.1}
	end 'create'
end 'Pair'

let p = Pair.create((5, 6))

function main() returns ExitCode
	print("{p.a} {p.b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 6
```

<!-- test: a-tuple-payload-of-a-top-level-union-case -->
A union case whose payload is a tuple takes a tuple literal in a file-scope construction.
```maxon
typealias Num = int(0 to 1000)
typealias Step = (Num, String)

union Move
	moved(step Step)
	still
end 'Move'

let move = Move.moved((3, "three"))

function main() returns ExitCode
	match move 'which'
		moved(step) then print("{step.0} {step.1}\n")
		still then print("still\n")
	end 'which'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 three
```

<!-- test: a-tuple-column-of-a-top-level-map -->
A map literal whose value column is a tuple fixes its columns from the first pair's tuple.
```maxon
let table = [1: (2, "two"), 3: (4, "four")]

function main() returns ExitCode
	view first = try table.get(1) otherwise panic("absent")
	view second = try table.get(3) otherwise panic("absent")
	print("{first.0} {first.1} {second.0} {second.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 two 4 four
```

<!-- test: an-untyped-top-level-array-literal-of-tuples -->
An array literal of tuple literals with no stated alias is an `Array` of the tuple type its first element states.
```maxon
let pairs = [(1, "one"), (2, "two")]

function main() returns ExitCode
	print("{pairs.count()}")

	for pair in pairs 'eachPair'
		print(" {pair.0} {pair.1}")
	end 'eachPair'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 1 one 2 two
```

<!-- test: a-parenthesised-tuple-element-of-a-typed-array-literal -->
A tuple literal written in extra parentheses is the same tuple in a slot that states one.
```maxon
typealias Word = bits(64)
typealias Step = (Word, Word)
typealias Steps = Array with Step

let steps = Steps from [((0, 1)), (((2, 3)))]

function main() returns ExitCode
	print("{steps.count()}")

	for step in steps 'eachStep'
		print(" {step.0} {step.1}")
	end 'eachStep'

	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 0 1 2 3
```

<!-- test: parenthesised-records-and-tuples-at-file-scope -->
Redundant parentheses around a factory call, a tuple literal or a tuple column change nothing, and the comma between
a call's arguments does not make the parentheses around the call a tuple.
```maxon
typealias Num = int(0 to 1000)

type Pair
	export var a as Num
	export var b as Num

	static function create(a Num, b Num) returns Pair
		return Pair{a: a, b: b}
	end 'create'
end 'Pair'

let grouped = (((Pair.create(1, b: 2))))
let wrapped = (((((3, "three")))))
let mixed = ((Pair.create(4, b: 5)), ((6, "six")))

function main() returns ExitCode
	print("{grouped.a} {grouped.b} {wrapped.0} {wrapped.1} {mixed.0.a} {mixed.0.b} {mixed.1.0} {mixed.1.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 three 4 5 6 six
```

<!-- test: an-unstated-tuple-constant-declared-in-another-file -->
A constant whose tuple type no file writes is built by the module init whichever file declares it and whichever
file reads it, and a second file's own constant of the same type shares the layout.
```maxon
// --- file: api/table.maxon
export let origin = (0, "origin")

// --- file: bin/main.maxon
let here = (9, "here")

function main() returns ExitCode
	print("{origin.0} {origin.1} {here.0} {here.1}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0 origin 9 here
```

<!-- test: error.a-top-level-tuple-literal-in-an-array-of-scalars -->
A tuple literal is a tuple wherever it stands, so an array of `Word` refuses it as the same array literal in a
function body does.
```maxon
typealias Word = bits(64)
typealias Words = Array with Word

let words = Words from [(0, 1)]

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3009: <fragment>:5:25: Cannot cast from (int, int) to int
```

<!-- test: error.a-tuple-literal-in-an-array-of-scalars-in-a-body -->
```maxon
typealias Word = bits(64)
typealias Words = Array with Word

function main() returns ExitCode
	let words = Words from [(0, 1)]
	return words.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3009: <fragment>:6:26: Cannot cast from (int, int) to int
```

<!-- test: an-array-of-element-alias-casts-in-a-body -->
An element cast to the array's own element alias is an element of that type, so `Items from [7 as Item]` builds the array.
```maxon
typealias Item = int(0 to 1000)
typealias Items = Array with Item

function main() returns ExitCode
	let xs = Items from [7 as Item]
	return (try xs.get(0) otherwise 0) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-top-level-array-of-element-alias-casts -->
The same literal at file scope is the same array, read back after `main` starts.
```maxon
typealias Item = int(0 to 1000)
typealias Items = Array with Item

let xs = Items from [7 as Item]

function main() returns ExitCode
	return (try xs.get(0) otherwise 0) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: cross-file-constant-in-initializer -->
A top-level `let` initializer may reference an exported constant from another file. The declaring
file here is fed to the compiler LAST, after the file that reads it — resolution is by declaration,
not by the order the files happen to arrive, so the reference resolves either way.
```maxon
// --- file: app/main.maxon
let TOTAL = BASE * 2

function main() returns ExitCode
	return TOTAL
end 'main'

// --- file: api/base.maxon
export let BASE = 21
```
```exitcode
42
```

<!-- test: cross-file-constant-in-initializer-declared-first -->
The same program with the declaring file fed FIRST. Both orders must produce the same executable.
```maxon
// --- file: api/base.maxon
export let BASE = 21

// --- file: app/main.maxon
let TOTAL = BASE * 2

function main() returns ExitCode
	return TOTAL
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-constant-chain -->
A cross-file constant chain folds transitively, in either direction, regardless of which file
declares which link.
```maxon
// --- file: app/main.maxon
let FINAL = MIDDLE + 2

function main() returns ExitCode
	return FINAL
end 'main'

// --- file: api/middle.maxon
export let MIDDLE = ROOT * 4

// --- file: api/root.maxon
export let ROOT = 10
```
```exitcode
42
```

<!-- test: error.circular-dependency-cross-file -->
A cycle among top-level constants is reported as a circular dependency even when the cycle spans
files.

⚠ **ONE LINE PER CYCLE, NOT ONE PER PARTICIPATING FILE**, and that is
architectural rather than chosen: the compiler folds every top-level constant ONCE, whole-program, before any file
is parsed (`ProgramSignatures.evaluateInitializers`), so there is one walk to find the cycle and one place
to report it. A per-file folder would find the same cycle once per file it participates in.
```maxon
// --- file: app/main.maxon
export let A = B + 1

function main() returns ExitCode
	return 0
end 'main'

// --- file: api/b.maxon
export let B = A + 1
```
```maxoncstderr
error E2012: api/<fragment>:10:12: Circular dependency detected among global constants: A, B
```

<!-- test: error.file-private-constant-cross-file -->
A file-private (non-exported) top-level `let` is not a constant another file may read, so a
cross-file reference to one from a constant initializer is undefined — the whole-program view the
compiler takes of constant DECLARATIONS does not widen their VISIBILITY.
```maxon
// --- file: api/secret.maxon
let SECRET = 5

export function useSecret() returns ExitCode
	return SECRET as ExitCode
end 'useSecret'

// --- file: app/main.maxon
let COPY = SECRET * 2

function main() returns ExitCode
	return COPY + useSecret()
end 'main'
```
```maxoncstderr
error E2004: app/specs/top-level-let/error.file-private-constant-cross-file.maxon:10:12: Undefined variable 'SECRET'
```

<!-- test: error.file-private-constant-read-in-a-body-cross-file -->
```maxon
// --- file: api/secret.maxon
let SECRET = 5

export function useSecret() returns ExitCode
	return SECRET as ExitCode
end 'useSecret'

// --- file: app/main.maxon
function main() returns ExitCode
	let copy = SECRET * 2
	return copy + useSecret()
end 'main'
```
```maxoncstderr
error E2004: app/<fragment>:11:13: Undefined variable 'SECRET'
```

<!-- test: cross-file-exported-reads-own-private-declared-last -->
An exported constant whose initializer reads a constant PRIVATE to its own declaring file resolves
to the right value from another file — because it is folded in ITS declarer's perspective, where
that private is visible, not in the demander's. The declaring file is fed LAST here; it must give
the same result as when fed first (below).
```maxon
// --- file: app/main.maxon
let TOTAL = BASE * 2

function main() returns ExitCode
	return TOTAL
end 'main'

// --- file: api/base.maxon
let SECRET = 20
export let BASE = SECRET + 1
```
```exitcode
42
```

<!-- test: cross-file-exported-reads-own-private-declared-first -->
The same program with the declaring file fed FIRST. Both orders must produce the same executable —
this is the order-independence the residual fix closes.
```maxon
// --- file: api/base.maxon
let SECRET = 20
export let BASE = SECRET + 1

// --- file: app/main.maxon
let TOTAL = BASE * 2

function main() returns ExitCode
	return TOTAL
end 'main'
```
```exitcode
42
```

<!-- test: cross-file-exported-cast-to-own-alias-declared-last -->
An exported constant whose initializer casts to a ranged `typealias` of its own declaring file folds in
the declarer's perspective, resolving that file's alias — the cast type is looked up as the declarer
would see it, not the demander. The alias is `export` because a constant's type may not be less visible
than the constant (E3167). Declaring file fed LAST.
```maxon
// --- file: app/main.maxon
let VALUE = BASE

function main() returns ExitCode
	return VALUE
end 'main'

// --- file: api/base.maxon
export typealias Small = int(0 to 100)
export let BASE = 21 as Small
```
```exitcode
21
```

<!-- test: cross-file-exported-cast-to-own-alias-declared-first -->
The same program with the declaring file fed FIRST — the same executable either way.
```maxon
// --- file: api/base.maxon
export typealias Small = int(0 to 100)
export let BASE = 21 as Small

// --- file: app/main.maxon
let VALUE = BASE

function main() returns ExitCode
	return VALUE
end 'main'
```
```exitcode
21
```

<!-- test: cross-file-private-constant-name-collision -->
Two files each declare a PRIVATE constant of the same name with different values, each read through
an exported constant. The exported constants must fold against their OWN file's private — proving
the fold memo keys by declaration identity, not by bare name. A name-keyed memo would serve the
first `SECRET` folded (20) for the second, making BVAL 21 and the total 42 instead of 122.
```maxon
// --- file: app/main.maxon
let RESULT = AVAL + BVAL

function main() returns ExitCode
	return RESULT
end 'main'

// --- file: api/a.maxon
let SECRET = 20
export let AVAL = SECRET + 1

// --- file: lib/b.maxon
let SECRET = 100
export let BVAL = SECRET + 1
```
```exitcode
122
```

### Error: A global's initializer must consume everything up to the end of its line

A global's initializer is parsed out of the token region the declaration scan marked off, so whatever the
expression does not reach is refused rather than abandoned — otherwise `var g = Box.create() zzz` would
compile, run, and ignore `zzz`. The refusal is the one a body's binding gets. The interpolation body and
the captured parameter default follow the same rule.

<!-- test: error.runtime-init-trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	static function create() returns Self
		return Self{v: 3}
	end 'create'
end 'Box'

var g = Box.create() zzz

function main() returns ExitCode
	return g.v
end 'main'
```
```maxoncstderr
error E2001: specs/top-level-let/error.runtime-init-trailing-tokens.maxon:12:22: unexpected token: 'zzz'
```

<!-- test: error.a-body-binding-trailing-tokens -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var v as Integer

	static function create() returns Self
		return Self{v: 3}
	end 'create'
end 'Box'

function main() returns ExitCode
	let g = Box.create() zzz
	return g.v
end 'main'
```
```maxoncstderr
error E2001: <fragment>:13:23: unexpected token: 'zzz'
```

<!-- test: a-top-level-let-is-initialized-by-a-directory-types-static -->
A top-level `let` is initialized by a static of a type in another directory, through a one-segment
chain, `sub.Calc`, and a two-segment one, `outer.inner.Deep` (30 + 12).
```maxon
// --- file: sub/calc.maxon
export type Calc
	export let v as ExitCode

	export static function create() returns Calc
		return Calc{v: 30}
	end 'create'
end 'Calc'

// --- file: outer/inner/calc.maxon
export type Deep
	export let v as ExitCode

	export static function create() returns Deep
		return Deep{v: 12}
	end 'create'
end 'Deep'

// --- file: main.maxon
let t = sub.Calc.create()
let deep = outer.inner.Deep.create()

function main() returns ExitCode
	return t.v + deep.v
end 'main'
```
```exitcode
42
```

<!-- test: a-parenthesised-qualified-factory-at-file-scope -->
Parentheses around a directory-qualified factory call are the same call, as they are around an unqualified one.
```maxon
// --- file: sub/calc.maxon
export type Calc
	export let v as ExitCode

	export static function create(v ExitCode) returns Calc
		return Calc{v: v}
	end 'create'
end 'Calc'

// --- file: main.maxon
let r = (sub.Calc.create(7))

function main() returns ExitCode
	return r.v
end 'main'
```
```exitcode
7
```


<!-- test: error.a-call-through-a-module-function-value-in-a-constant-is-not-a-constant -->
`doubler` is a module-level function value; calling it in another top-level `let`'s initializer is a call,
not a constant, and it is refused as one rather than as an undefined function.
```maxon
typealias Integer = int(i64.min to i64.max)

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

let doubler = twice
let six = doubler(3)

function main() returns ExitCode
	return six as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:9:11: Function calls are not allowed in global variable initializers; 'doubler()' is not a constant expression
```

<!-- test: error.a-static-initializer-calling-a-sibling-static-is-not-a-constant -->
A type's `static let` initializer calling a sibling static by its bare name is a call, not a constant, and
it is refused as one rather than as an undefined function.
```maxon
typealias Integer = int(i64.min to i64.max)

type Registry
	static let seed = makeSeed()

	static function makeSeed() returns Integer
		return 4
	end 'makeSeed'

	static function value() returns Integer
		return Registry.seed
	end 'value'
end 'Registry'

function main() returns ExitCode
	return Registry.value() as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:5:20: Function calls are not allowed in global variable initializers; 'makeSeed()' is not a constant expression
```

<!-- test: a-static-method-body-calling-a-sibling-static-bare-calls-it -->
The same bare call in a static method's body calls the sibling.
```maxon
typealias Integer = int(i64.min to i64.max)

type Registry
	static function makeSeed() returns Integer
		return 4
	end 'makeSeed'

	static function value() returns Integer
		let seed = makeSeed()
		return seed
	end 'value'
end 'Registry'

function main() returns ExitCode
	return Registry.value() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.a-generic-call-in-a-constant-is-not-a-constant -->
A generic function's call in a top-level `let`'s initializer is a call, not a constant, and it is refused
as one rather than as an undefined function.
```maxon
typealias Integer = int(i64.min to i64.max)

function larger(a T, b T) uses T returns T where T is Comparable
	return a if a > b else b
end 'larger'

let big = larger(1 as Integer, b: 2 as Integer)

function main() returns ExitCode
	return big as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:8:11: Function calls are not allowed in global variable initializers; 'larger()' is not a constant expression
```


<!-- test: error.a-static-initializer-calling-a-sibling-static-past-a-hidden-free-function-is-not-a-constant -->
Another file of the directory declares a file-private free `makeSeed`; inside `Registry` the bare call means
the sibling static, as a body call would, so it is refused as a call rather than as a hidden function.
```maxon
// --- file: hidden.maxon
export typealias Integer = int(i64.min to i64.max)

function makeSeed() returns Integer
	return 9
end 'makeSeed'

export function hiddenSeed() returns Integer
	return makeSeed()
end 'hiddenSeed'

// --- file: main.maxon
type Registry
	static let seed = makeSeed()

	static function makeSeed() returns Integer
		return 4
	end 'makeSeed'

	export static function value() returns Integer
		return Registry.seed
	end 'value'
end 'Registry'

function main() returns ExitCode
	return (Registry.value() + hiddenSeed()) as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:15:20: Function calls are not allowed in global variable initializers; 'makeSeed()' is not a constant expression
```


<!-- test: a-builtin-call-past-a-hidden-same-name-declaration-folds-to-a-constant -->
Another file declares a file-private `sqrt`; from `main.maxon` the bare call means the math builtin, as a
body call does, and a builtin over a constant is the constant the body knows it to be, so `R` is 2.0.
```maxon
// --- file: main.maxon
public typealias Real = float(0.0 to 10.0)

let R = sqrt(4.0)

function main() returns ExitCode
	if R > useSqrt() 'big'
		return 1
	end 'big'

	return 0
end 'main'

// --- file: a.maxon
function sqrt(x Real) returns Real
	return x
end 'sqrt'

export function useSqrt() returns Real
	return sqrt(1.0)
end 'useSqrt'
```
```exitcode
1
```

<!-- test: a-builtin-call-in-a-function-body-past-a-hidden-same-name-declaration-calls-the-builtin -->
The same bare call in a function body means the math builtin.
```maxon
// --- file: main.maxon
public typealias Real = float(0.0 to 10.0)

function main() returns ExitCode
	let r = sqrt(4.0)

	if r > useSqrt() 'big'
		return 1
	end 'big'

	return 0
end 'main'

// --- file: a.maxon
function sqrt(x Real) returns Real
	return x
end 'sqrt'

export function useSqrt() returns Real
	return sqrt(1.0)
end 'useSqrt'
```
```exitcode
1
```

<!-- test: error.a-refused-initializer-call-beside-an-unused-typealias-reports-the-typealias -->
An unused typealias in another file is reported ahead of an initializer's undefined call, as it is ahead
of the same call in a function body.
```maxon
// --- file: main.maxon
let G = nothere(7)

function main() returns ExitCode
	return 0
end 'main'

// --- file: other.maxon
typealias Unused = int(0 to 5)
```
```maxoncstderr
error E3062: <fragment>:10:11: unused typealias: 'Unused'
```

<!-- test: error.a-refused-function-body-call-beside-an-unused-typealias-reports-the-typealias -->
The same call in a function body.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'main'

// --- file: other.maxon
typealias Unused = int(0 to 5)
```
```maxoncstderr
error E3062: <fragment>:9:11: unused typealias: 'Unused'
```

<!-- test: error.a-constant-reading-a-refused-call-constant-reports-the-call-once -->
`H` reads `G`, whose initializer is a refused call: the call is reported once, and `H` is not given a value.
```maxon
let H = G + 1
let G = nothere(7)

function main() returns ExitCode
	return H as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'nothere'
```

<!-- test: error.a-constant-read-after-a-refused-call-constant-reports-the-call-once -->
The same, with `G` declared first.
```maxon
let G = nothere(7)
let H = G + 1

function main() returns ExitCode
	return H as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:2:9: call to undefined function 'nothere'
```

<!-- test: error.a-static-initializer-calling-a-sibling-instance-method-bare-has-no-receiver -->
`fresh` is an instance method, so a `static var` initializer calling it bare has no receiver to call it on,
and is refused as the same call in a static method's body is.
```maxon
typealias Digit = int(0 to 9)

type Holder
	export var n as Digit

	static var shared = fresh()

	function fresh() returns Holder
		return Holder{n: 1}
	end 'fresh'

	static function value() returns Digit
		return Holder.shared.n
	end 'value'
end 'Holder'

function main() returns ExitCode
	return Holder.value() as ExitCode
end 'main'
```
```maxoncstderr
error E3193: <fragment>:7:22: 'fresh' is an instance method of 'Holder' and needs a receiver: call it on a value, such as 'value.fresh(...)', or make it `static`
```

<!-- test: error.a-static-method-body-calling-a-sibling-instance-method-bare-has-no-receiver -->
The same bare call in a static method's body.
```maxon
typealias Digit = int(0 to 9)

type Holder
	export var n as Digit

	function fresh() returns Holder
		return Holder{n: 1}
	end 'fresh'

	static function value() returns Digit
		let shared = fresh()
		return shared.n
	end 'value'
end 'Holder'

function main() returns ExitCode
	return Holder.value() as ExitCode
end 'main'
```
```maxoncstderr
error E3193: <fragment>:12:16: 'fresh' is an instance method of 'Holder' and needs a receiver: call it on a value, such as 'value.fresh(...)', or make it `static`
```


<!-- test: error.a-field-read-of-a-refused-call-constant-reports-the-call-once -->
`G`'s initializer is a refused call, so a read of one of its fields is not reported again.
```maxon
let G = nothere(7)

function main() returns ExitCode
	return G.n as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:2:9: call to undefined function 'nothere'
```

<!-- test: error.a-method-call-on-a-refused-call-constant-reports-the-call-once -->
A method called on `G` is not reported again either.
```maxon
let G = nothere(7)

function main() returns ExitCode
	return G.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:2:9: call to undefined function 'nothere'
```


<!-- test: error.a-call-through-a-refused-call-constant-reports-the-call-once -->
`f`'s initializer is a refused call, so calling `f` is not reported again.
```maxon
let f = nothere(7)

function main() returns ExitCode
	return f(3) as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:2:9: call to undefined function 'nothere'
```

<!-- test: error.an-assignment-to-a-refused-call-var-reports-the-call-once -->
`G`'s initializer is a refused call, so assigning `G` is not reported again.
```maxon
typealias Digit = int(0 to 9)

var G = nothere(7)

function main() returns ExitCode
	G = 3 as Digit
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:9: call to undefined function 'nothere'
```

<!-- test: error.a-type-read-after-a-refused-call-constant-is-used -->
`H` stops at its read of `G`, whose initializer is a refused call; `Small`, named after that read, still
counts as used.
```maxon
typealias Small = int(0 to 9)

let G = nothere(7)
let H = G + (1 as Small)

function main() returns ExitCode
	return H as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:9: call to undefined function 'nothere'
```


<!-- test: error.a-field-assignment-through-a-refused-call-var-reports-the-call-once -->
`G`'s initializer is a refused call, so writing one of its fields is not reported again.
```maxon
typealias Digit = int(0 to 9)

var G = nothere(7)

function main() returns ExitCode
	G.n = 3 as Digit
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:9: call to undefined function 'nothere'
```

<!-- test: a-factory-initializer-past-a-same-named-constant-in-another-file-folds -->
`make` names a constant in `other.maxon`, which `main.maxon` cannot see, and a factory `main.maxon`
declares; the initializer's call means the factory, as a body call would. Returns 7.
```maxon
// --- file: main.maxon
typealias Digit = int(0 to 9)

type Box
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function make() returns Box
	return Box.create(7)
end 'make'

let G = make()

function main() returns ExitCode
	if other() == 0 'zero'
		return G.n as ExitCode
	end 'zero'

	return 1
end 'main'

// --- file: other.maxon
export typealias Zero = int(0 to 0)

let make = 0

export function other() returns Zero
	return make
end 'other'
```
```exitcode
7
```


<!-- test: error.a-field-assignment-through-a-refused-call-static-var-reports-the-call-once -->
`Holder.shared`'s initializer is a refused call, so writing one of its fields is not reported again.
```maxon
typealias Digit = int(0 to 9)

type Holder
	export var n as Digit

	static var shared = nothere(7)

	static function reset()
		Holder.shared.n = 3 as Digit
	end 'reset'
end 'Holder'

function main() returns ExitCode
	Holder.reset()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:22: call to undefined function 'nothere'
```


<!-- test: a-static-initializer-folds-its-sibling-factory-past-a-same-named-constant -->
A top-level constant named `make` is not a function, so inside `Box` the bare call means the sibling
factory, as a body call would. Returns 7.
```maxon
typealias Digit = int(0 to 9)

let make = 3 as Digit

type Box
	export var n as Digit

	static let origin = make()

	static function make() returns Box
		return Box{n: 7}
	end 'make'

	static function originOf() returns Digit
		return Box.origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	if make == 3 'three'
		return Box.originOf() as ExitCode
	end 'three'

	return 1
end 'main'
```
```exitcode
7
```

<!-- test: error.a-static-initializer-calling-a-generic-sibling-is-not-a-constant -->
`pick` is a generic sibling static, so the initializer's bare call means it and is refused as a call.
```maxon
typealias Integer = int(i64.min to i64.max)

type Registry
	static let seed = pick(1 as Integer, b: 2 as Integer)

	static function pick(a T, b T) uses T returns T where T is Comparable
		return a if a > b else b
	end 'pick'

	static function value() returns Integer
		return Registry.seed
	end 'value'
end 'Registry'

function main() returns ExitCode
	return Registry.value() as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:5:20: Function calls are not allowed in global variable initializers; 'pick()' is not a constant expression
```

<!-- test: error.a-top-level-initializer-call-to-a-hidden-generic-is-not-exported -->
The only `larger` is a file-private generic in `other.maxon`, so `main.maxon` cannot name it.
```maxon
// --- file: main.maxon
let big = larger(1 as Integer, b: 2 as Integer)

function main() returns ExitCode
	return (big + useLarger()) as ExitCode
end 'main'

// --- file: other.maxon
export typealias Integer = int(i64.min to i64.max)

function larger(a T, b T) uses T returns T where T is Comparable
	return a if a > b else b
end 'larger'

export function useLarger() returns Integer
	return larger(1 as Integer, b: 2 as Integer)
end 'useLarger'
```
```maxoncstderr
error E3008: <fragment>:3:11: function 'larger' is not exported
```

<!-- test: error.a-tuple-assignment-to-a-refused-call-var-reports-the-tuple-assignment -->
`G`'s initializer is a refused call, and a tuple assignment naming a module `var` is refused whatever
its value. The tuple assignment's refusal is reported ahead of the call, as it is when the same call is
written in a function body.
```maxon
typealias Digit = int(0 to 9)

var G = nothere(7)

function pair() returns (Digit, Digit)
	return (1 as Digit, 2 as Digit)
end 'pair'

function main() returns ExitCode
	var y = 0 as Digit
	(G, y) = pair()
	return y as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:3: Unsupported: a tuple-assignment target naming the module-scope binding 'G' (a module binding is written through a different store, which parses its own right-hand side; assign the element to a local and store that)
```

<!-- test: error.a-tuple-assignment-to-a-module-var-beside-a-refused-call-in-a-function-body -->
The same refused call written in a function body, beside the same tuple assignment.
```maxon
typealias Digit = int(0 to 9)

var G = 0 as Digit

function pair() returns (Digit, Digit)
	return (1 as Digit, 2 as Digit)
end 'pair'

function main() returns ExitCode
	let h = nothere(7)
	var y = 0 as Digit
	(G, y) = pair()
	return (y + h) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:3: Unsupported: a tuple-assignment target naming the module-scope binding 'G' (a module binding is written through a different store, which parses its own right-hand side; assign the element to a local and store that)
```

<!-- test: error.a-refused-call-inside-a-record-literal-reports-the-call-and-the-cast-before-it -->
The refused call sits inside a record literal; the cast written before it is still read, so its unneeded
`as Small` is reported beside the call, as in a function body.
```maxon
typealias Small = int(0 to 9)

type Pair
	export var a as Small
	export var b as Small

	static function create(a Small, b Small) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

let G = Pair.create(1 as Small, b: nothere(2))

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:13:36: call to undefined function 'nothere'
error E3010: <fragment>:13:23: unneeded cast: the literal 1 already fits in 'Small'
```

<!-- test: error.a-refused-call-inside-a-record-literal-in-a-function-body-reports-the-call-and-the-cast-before-it -->
The same call in a function body.
```maxon
typealias Small = int(0 to 9)

type Pair
	export var a as Small
	export var b as Small

	static function create(a Small, b Small) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let g = Pair.create(1 as Small, b: nothere(2))
	return g.a as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:14:37: call to undefined function 'nothere'
error E3010: <fragment>:14:24: unneeded cast: the literal 1 already fits in 'Small'
```

<!-- test: error.uses-of-a-refused-call-constant-report-the-call-once -->
Every use of `G` below meets a constant whose initializer is a refused call: only the call is reported.
```maxon
typealias Digit = int(0 to 9)

var G = nothere(7)

function use(n Digit) returns Digit
	return n
end 'use'

function main() returns ExitCode
	G.push(1)

	for x in G 'each'
		print("{x}\n")
	end 'each'

	print("{G}\n")
	let b = use(G)
	let read = function() gives G
	print("{b} {read()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:4:9: call to undefined function 'nothere'
```


<!-- test: a-static-initializer-calling-a-function-valued-binding-past-a-sibling-calls-the-binding -->
`make` is a top-level binding holding a function value a call built; a body call `make()` inside `Box`
calls it, so the initializer calls it too, not the sibling factory: `origin` is `other`'s `3`, not the
sibling's `7`.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.three()
end 'other'

function makerOf() returns Maker
	return other
end 'makerOf'

let make = makerOf()

type Box
	export var n as Digit

	static let origin = make()

	static function make() returns Box
		return Box{n: 7}
	end 'make'

	static function three() returns Box
		return Box{n: 3}
	end 'three'

	static function originOf() returns Digit
		return Box.origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	return (Box.originOf() + Box.make().n) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: a-static-method-body-calling-a-function-valued-binding-past-a-sibling-calls-the-binding -->
The same call in a static method's body.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.three()
end 'other'

function makerOf() returns Maker
	return other
end 'makerOf'

let make = makerOf()

type Box
	export var n as Digit

	static function make() returns Box
		return Box{n: 7}
	end 'make'

	static function three() returns Box
		return Box{n: 3}
	end 'three'

	static function originOf() returns Digit
		let origin = make()
		return origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	return (Box.originOf() + Box.make().n) as ExitCode
end 'main'
```
```exitcode
10
```


<!-- test: error.a-static-initializer-calling-a-function-valued-binding-past-a-sibling-with-a-parameter-calls-the-binding -->
The sibling `make` takes a `Digit` the argument does not fit, but the call means the function-valued
binding, as a body call does, so the refusal is the binding's arity, not the sibling's parameter's.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.make(7)
end 'other'

function makerOf() returns Maker
	return other
end 'makerOf'

let make = makerOf()

type Box
	export var n as Digit

	static let origin = make(42)

	static function make(n Digit) returns Box
		return Box{n: n}
	end 'make'

	static function originOf() returns Digit
		return Box.origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	return Box.originOf() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:18:26: 'Maker' expects 0 argument(s) but 1 were provided
```

<!-- test: error.a-static-method-body-calling-a-function-valued-binding-past-a-sibling-with-a-parameter -->
The same call in a static method's body.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.make(7)
end 'other'

function makerOf() returns Maker
	return other
end 'makerOf'

let make = makerOf()

type Box
	export var n as Digit

	static function make(n Digit) returns Box
		return Box{n: n}
	end 'make'

	static function originOf() returns Digit
		let origin = make(42)
		return origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	return Box.originOf() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:23:20: 'Maker' expects 0 argument(s) but 1 were provided
```


<!-- test: error.a-static-initializer-calling-a-sibling-past-a-same-named-constant-reports-the-siblings-defect -->
`make` names a constant that is not a function, so the call means the sibling factory, and its argument's
range is refused exactly as it is with no constant of that name.
```maxon
typealias Digit = int(0 to 9)

let make = 0

type Box
	export var n as Digit

	static let origin = make(42)

	static function make(n Digit) returns Box
		return Box{n: n}
	end 'make'

	static function originOf() returns Digit
		return Box.origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	if make == 0 'zero'
		return Box.originOf() as ExitCode
	end 'zero'

	return 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:22: Value 42 is outside the range of 'Digit' (int(0 to 9))
```

<!-- test: error.a-static-method-calling-a-sibling-past-a-same-named-constant-reports-the-siblings-defect -->
The same call in a static method's body, reported at the same token, the callee's name.
```maxon
typealias Digit = int(0 to 9)

let make = 0

type Box
	export var n as Digit

	static function make(n Digit) returns Box
		return Box{n: n}
	end 'make'

	static function originOf() returns Digit
		let origin = make(42)
		return origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	if make == 0 'zero'
		return Box.originOf() as ExitCode
	end 'zero'

	return 1
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:16: Value 42 is outside the range of 'Digit' (int(0 to 9))
```

<!-- test: error.an-undeclared-base-in-a-top-level-initializer-is-an-undefined-variable -->
A dotted name whose base no file declares is an undefined variable in a top-level initializer, as it is in
a body, and not a construct the initializer cannot hold.
```maxon
let table = Nope.thing

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:13: Undefined variable 'Nope'
```

<!-- test: error.an-undeclared-base-inside-a-top-level-array-is-an-undefined-variable -->
```maxon
let table = [Nope.first, Nope.second]

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:2:14: Undefined variable 'Nope'
```


<!-- test: error.a-top-level-exit-code-constant-is-refused-where-a-ranged-alias-is-required -->
A constant cast to `ExitCode` keeps being an `ExitCode` where it is read: handed to a parameter of a ranged alias it
is refused by name, exactly as the body twin below refuses an `ExitCode` local.
```maxon
typealias Small = int(0 to 255)

let FAIL = 3 as ExitCode

function takesSmall(n Small) returns Small
	return n
end 'takesSmall'

function main() returns ExitCode
	return takesSmall(FAIL) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode'
```

<!-- test: error.an-exit-code-local-is-refused-where-a-ranged-alias-is-required -->
```maxon
typealias Small = int(0 to 255)

function takesSmall(n Small) returns Small
	return n
end 'takesSmall'

function main() returns ExitCode
	let fail = 3 as ExitCode
	return takesSmall(fail) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode'
```

<!-- test: error.a-constant-derived-from-a-top-level-exit-code-constant-is-refused-where-a-ranged-alias-is-required -->
Arithmetic over an `ExitCode` constant is an `ExitCode` constant, as it is in a body.
```maxon
typealias Small = int(0 to 255)

let FAIL = 3 as ExitCode
let NEXT = FAIL + 1

function takesSmall(n Small) returns Small
	return n
end 'takesSmall'

function main() returns ExitCode
	return takesSmall(NEXT) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode'
```

<!-- test: error.a-derived-exit-code-local-is-refused-where-a-ranged-alias-is-required -->
```maxon
typealias Small = int(0 to 255)

function takesSmall(n Small) returns Small
	return n
end 'takesSmall'

function main() returns ExitCode
	let fail = 3 as ExitCode
	let next = fail + 1
	return takesSmall(next) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode'
```

<!-- test: a-top-level-exit-code-var-is-reassigned-as-an-exit-code -->
A module `var` born from an `ExitCode` cast is an `ExitCode` slot, read and written as one.
```maxon
var code = 3 as ExitCode

function bump()
	code = code + 4
end 'bump'

function main() returns ExitCode
	bump()
	return code
end 'main'
```
```exitcode
7
```

<!-- test: a-match-expression-in-a-call-argument-ends-a-top-level-let-at-its-own-line -->
The newlines a `match` expression keeps between its arms inside a call's parentheses belong to the initializer: the `let` ends at the line holding the closing parenthesis.
```maxon
enum Mode
	quiet
	loud
end 'Mode'

function tag(text String, tail String) returns String
	return "{text}{tail}"
end 'tag'

let LABEL = tag(match Mode.loud 'm'
	quiet gives "quiet"
	loud gives "loud"
end 'm', tail: "!")
let NEXT = "next"

function main() returns ExitCode
	print("{LABEL} {NEXT}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
loud! next
```

<!-- test: a-match-expression-in-a-call-argument-ends-a-top-level-var-at-its-own-line -->
```maxon
enum Mode
	quiet
	loud
end 'Mode'

function tag(text String, tail String) returns String
	return "{text}{tail}"
end 'tag'

var LABEL = tag(match Mode.quiet 'm'
	quiet gives "quiet"
	loud gives "loud"
end 'm', tail: "?")
var NEXT = "next"

function main() returns ExitCode
	print("{LABEL} {NEXT}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
quiet? next
```

<!-- test: a-match-expression-in-a-parenthesised-group-ends-a-top-level-let-at-its-own-line -->
```maxon
enum Mode
	quiet
	loud
end 'Mode'

let LABEL = (match Mode.loud 'm'
	quiet gives "quiet"
	loud gives "loud"
end 'm')
let NEXT = "next"

function main() returns ExitCode
	print("{LABEL} {NEXT}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
loud next
```

<!-- test: a-match-expression-in-an-array-element-ends-a-top-level-let-at-its-own-line -->
```maxon
enum Mode
	quiet
	loud
end 'Mode'

let LABELS = [match Mode.loud 'm'
	quiet gives "quiet"
	loud gives "loud"
end 'm', "tail"]
let NEXT = "next"

function main() returns ExitCode
	for label in LABELS 'each'
		print("{label}\n")
	end 'each'
	print("{NEXT}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
loud
tail
next
```

<!-- test: a-handler-block-otherwise-in-a-call-argument-ends-a-top-level-let-at-its-own-line -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

function parse(n Count) returns Count throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return n
end 'parse'

function tag(n Count, tail String) returns String
	return "{n}{tail}"
end 'tag'

let LABEL = tag(try parse(3) otherwise 'bad'
	print("recovering\n")
	panic("unreachable")
end 'bad', tail: "!")
let NEXT = "next"

function main() returns ExitCode
	print("{LABEL} {NEXT}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3! next
```

<!-- test: error.a-return-in-a-handler-block-of-a-top-level-let-names-the-initializer -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

function parse(n Count) returns Count throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return n
end 'parse'

let LEVEL = try parse(3) otherwise (e) 'h'
	return 3
end 'h'

function main() returns ExitCode
	print("{LEVEL}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2080: <fragment>:14:2: `return` has no function to leave in the initializer of 'LEVEL' — end a handler there with `panic(…)`, or give the `try` a fallback value with `otherwise <expr>`
```

<!-- test: error.a-return-in-a-handler-block-of-a-top-level-var-names-the-initializer -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

function parse(n Count) returns Count throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return n
end 'parse'

var level = try parse(3) otherwise (e) 'h'
	return 3
end 'h'

function main() returns ExitCode
	level = level + 1
	print("{level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2080: <fragment>:14:2: `return` has no function to leave in the initializer of 'level' — end a handler there with `panic(…)`, or give the `try` a fallback value with `otherwise <expr>`
```

<!-- test: error.a-return-in-a-handler-block-of-a-top-level-record-let-names-the-initializer -->
```maxon
enum ParseError implements Error
	bad
end 'ParseError'

type Config
	export let level as Count

	static function create(level Count) returns Config
		return Self{level: level}
	end 'create'
end 'Config'

function makeConfig(n Count) returns Config throws ParseError
	if n > 5 'big'
		throw ParseError.bad
	end 'big'
	return Config.create(n)
end 'makeConfig'

let CONFIG = try makeConfig(3) otherwise (e) 'h'
	return Config.create(4)
end 'h'

function main() returns ExitCode
	print("{CONFIG.level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2080: <fragment>:22:2: `return` has no function to leave in the initializer of 'CONFIG' — end a handler there with `panic(…)`, or give the `try` a fallback value with `otherwise <expr>`
```
