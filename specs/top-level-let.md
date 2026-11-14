---
feature: top-level-let
status: stable
keywords: let, global, constant, compile-time
category: language
---
# Top Level Let

## Documentation

## Top-Level Let Declarations

Top-level `let` declarations bind values at module scope. An initializer is read by the function body's expression parser, and it calls nothing: a call in it is refused (E3220). A scalar binding is a compile-time constant inlined at every use; a record or array built from literals is laid down in the executable's read-only data.

### Syntax

```maxon
let CONSTANT_NAME = expression
export let EXPORTED_NAME = expression
```

### Features

- **Literal expressions**: An initializer is a literal expression, typed and diagnosed as a body types and diagnoses it; a call is E3220
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

A call is not a constant: it is **E3220**, and a call of a non-generic function that returns a scalar is **E2045**. A literal construction (`X from [..]`, `[..]`, `{..}`) is not a call. Strings, byte strings, characters, arrays,
maps, tuples and other records are not scalars: a constant one is laid down as read-only data.

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

- A call in an initializer is **E3220** (a call of a non-generic function that returns a scalar is **E2045**): write a literal, or build the value where it is used
- Initializers that depend on each other in a cycle are **E2012**
- A top-level `var` does not exist (`specs/module-var-removed.md`): a module-level binding is a `let`

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
`and` and `or` over constants are constants themselves, so they initialize another constant.
```maxon
let BOTH = true and true
let EITHER = false or true
let NEITHER = false and true
let READY = BOTH and EITHER and not NEITHER

function main() returns ExitCode
	print("{BOTH} {EITHER} {NEITHER} {READY}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
true true false true
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
An unmarked (non-exported) top-level `let` is scoped to its declaring directory, so files in two directories may declare the same bare name with different values. Each file's reads resolve to its OWN directory's constant.
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

<!-- test: error.file-private-same-name-three-directories-every-kind -->
The initializer of a module-level or `static` `let` calls `Tag.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
```maxon
// --- file: a/a.maxon
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

// --- file: b/b.maxon
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
```maxoncstderr
error E3220: a/<fragment>:8:14: the initializer of 'anchor' calls 'Tag.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: b/<fragment>:32:14: the initializer of 'anchor' calls 'Pin.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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

	static let origin = Holder{step: (7, 9)}

	static function get() returns immutable Holder
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
let other = ("a", "b", 3 as Num)

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

<!-- test: a-top-level-let-of-an-unstated-tuple-holding-an-empty-container -->
A column that is an empty container built by its alias keeps the alias in the tuple's identity, as it does in a body.
```maxon
typealias Item = int(0 to 1000)
typealias Items = Array with Item

let held = (Items.create(0), "held")

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

<!-- test: error.a-tuple-argument-of-a-top-level-factory -->
The initializer of a module-level or `static` `let` calls `Pair.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:13:9: the initializer of 'p' calls 'Pair.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-tuple-argument-written-in-the-factory-signature -->
The initializer of a module-level or `static` `let` calls `Pair.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:13:9: the initializer of 'p' calls 'Pair.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
	let first = try table.get(1) otherwise panic("absent")
	let second = try table.get(3) otherwise panic("absent")
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

<!-- test: error.parenthesised-records-and-tuples-at-file-scope -->
The initializer of a module-level or `static` `let` calls `Pair.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:13:15: the initializer of 'grouped' calls 'Pair.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: <fragment>:15:13: the initializer of 'mixed' calls 'Pair.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
let ROOT = 10
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
error E2012: app/<fragment>:3:12: Circular dependency detected among global constants: B, A
```

<!-- test: error.file-private-constant-cross-file -->
An unmarked top-level `let` is not a constant another directory may read, so a reference to one
from another directory's constant initializer is undefined — the whole-program view the
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
An exported constant whose initializer reads a constant UNEXPORTED from its own directory resolves
to the right value from another directory — because it is folded in ITS declarer's perspective, where
that constant is visible, not in the demander's. The declaring file is fed LAST here; it must give
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
Two directories each declare an UNMARKED constant of the same name with different values, each read through
an exported constant. The exported constants must fold against their OWN directory's constant — proving
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
expression does not reach is refused rather than abandoned — otherwise `let g = Box.create() zzz` would
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

let g = Box.create() zzz

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

<!-- test: error.a-top-level-let-is-initialized-by-a-directory-types-static -->
The initializer of a module-level or `static` `let` calls `Calc.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:21:9: the initializer of 't' calls 'Calc.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: <fragment>:22:12: the initializer of 'deep' calls 'Deep.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-parenthesised-qualified-factory-at-file-scope -->
The initializer of a module-level or `static` `let` calls `Calc.create`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:12:9: the initializer of 'r' calls 'Calc.create': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-module-let-cloning-an-array-literal-is-a-call -->
`clone` is a call, and a call on a literal is still a call: the initializer of a module-level `let` calls
nothing, so the program is refused (E3220).
```maxon
let numbers = [1, 2].clone()

function main() returns ExitCode
	return numbers.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:2:15: the initializer of 'numbers' calls 'Array.clone': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-module-let-cloning-a-map-literal-is-a-call -->
The `clone` of a map is the compiler's own copy of the container rather than a function a file declares, and it
is a call all the same: the program is refused (E3220).
```maxon
typealias Tally = int(0 to 1000)

let ledger = ["a": 1 as Tally].clone()

function main() returns ExitCode
	return ledger.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:4:14: the initializer of 'ledger' calls 'Map.clone': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-module-let-slicing-an-array-literal-is-a-call -->
```maxon
let head = [1, 2, 3].slice(0, endIndex: 1)

function main() returns ExitCode
	return head.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:2:12: the initializer of 'head' calls 'Array.slice': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-module-let-copying-a-module-let-record-is-a-call -->
The `clone` of a record that owns a `String` is the compiler's generated deep copy, and it is a call: the program is
refused (E3220).
```maxon
typealias Tally = int(0 to 1000)

type Pair
	export let left as Tally
	export let label as String

	static let seed = Self{left: 1, label: "a"}
end 'Pair'

let copy = Pair.seed.clone()

function main() returns ExitCode
	return copy.left as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:11:12: the initializer of 'copy' calls 'Pair.clone': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-module-let-rendering-a-number-is-a-call -->
```maxon
typealias Tally = int(0 to 1000)

let limit = 5 as Tally
let label = limit.toString()

function main() returns ExitCode
	return label.byteLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3220: <fragment>:5:13: the initializer of 'label' calls 'Tally.toString': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
A generic function's call in a top-level `let`'s initializer is a call, and it is refused as one under the generic function's own name.
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
error E3220: <fragment>:8:11: the initializer of 'big' calls 'larger': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```


<!-- test: error.a-static-initializer-calling-a-sibling-static-past-a-hidden-free-function-is-not-a-constant -->
Another directory declares an unmarked free `makeSeed`; inside `Registry` the bare call means
the sibling static, as a body call would, so it is refused as a call rather than as a hidden function.
```maxon
// --- file: hidden/hidden.maxon
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
Another directory declares an unmarked `sqrt`; from `main.maxon` the bare call means the math builtin, as a
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

// --- file: a/a.maxon
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

// --- file: a/a.maxon
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

<!-- test: error.a-refused-initializer-call-beside-an-unused-typealias-reports-both -->
An unused typealias in another directory is reported beside an initializer's refused call.
```maxon
// --- file: main.maxon
let G = nothere(7)

function main() returns ExitCode
	return 0
end 'main'

// --- file: lib/other.maxon
typealias Unused = int(0 to 5)
```
```maxoncstderr
error E3062: lib/<fragment>:10:11: unused typealias: 'Unused'
error E3220: <fragment>:3:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-refused-function-body-call-beside-an-unused-typealias-reports-both -->
The same call in a function body: the unused typealias and the undefined call are independent, so both are reported.
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
error E3004: <fragment>:4:10: call to undefined function 'nothere'
error E3062: <fragment>:9:11: unused typealias: 'Unused'
```

<!-- test: error.a-non-constant-initializer-beside-an-undefined-call-in-another-function-reports-both -->
An initializer that calls a function is refused (E2045) whatever else the program holds, so an undefined call
in another function is reported beside it.
```maxon
typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
	return 42
end 'compute'

let RESULT = compute()

function broken() returns Integer
	return nothere(7)
end 'broken'

function main() returns ExitCode
	return RESULT
end 'main'
```
```maxoncstderr
error E2045: <fragment>:8:14: Function calls are not allowed in global variable initializers; 'compute()' is not a constant expression
error E3004: <fragment>:11:9: call to undefined function 'nothere'
```

<!-- test: error.the-uses-of-a-refused-local-binding-report-only-the-refusal -->
Every use of `g` below meets a binding whose initializer is a refused call: only the call is reported.
```maxon
typealias Digit = int(0 to 9)

function use(n Digit) returns Digit
	return n
end 'use'

function main() returns ExitCode
	let g = nothere(7)
	let a = use(g)
	print("{g} {a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:9:10: call to undefined function 'nothere'
```

<!-- test: error.an-unused-typealias-in-one-file-beside-a-type-mismatch-in-another-reports-both -->
An unused typealias and an argument of the wrong type in different files are independent, so both are reported.
```maxon
// --- file: main.maxon
typealias Small = int(0 to 255)

function takesSmall(n Small) returns Small
	return n
end 'takesSmall'

function main() returns ExitCode
	let code = 3 as ExitCode
	return takesSmall(code) as ExitCode
end 'main'

// --- file: other.maxon
typealias Unused = int(0 to 5)
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode' — cast the argument with 'as Small'
error E3062: <fragment>:15:11: unused typealias: 'Unused'
```

<!-- test: error.a-parse-error-in-one-file-beside-an-undefined-call-in-another-reports-both -->
A file that fails to parse and an undefined call in another file are independent, so both are reported.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'main'

// --- file: other.maxon
function broken() returns ExitCode
	let x: int = 0
	return 0
end 'broken'
```
```maxoncstderr
error E3004: <fragment>:4:10: call to undefined function 'nothere'
error E2010: <fragment>:10:7: Expected '=' but got ':'
```

<!-- test: error.a-failed-file-beside-a-same-named-function-reports-the-healthy-files-argument-mistake -->
The file that fails to parse declares a `helper`; a healthy file in another directory declares its own and
calls it with the wrong argument. The mistake is in the healthy declaration's call, so it is reported
beside the parse error.
```maxon
// --- file: a/broken.maxon
typealias Digit = int(0 to 9)

function helper(n Digit) returns Digit
	let x: int = 0
	return n
end 'helper'

// --- file: b/main.maxon
typealias Digit = int(0 to 9)

function helper(n Digit) returns Digit
	return n
end 'helper'

function main() returns ExitCode
	let answer = helper(true)
	return answer as ExitCode
end 'main'
```
```maxoncstderr
error E2010: a/<fragment>:6:7: Expected '=' but got ':'
error E3005: b/<fragment>:18:15: argument type mismatch for 'n': expected 'b.Digit', got 'bool'
```

<!-- test: error.a-failed-file-beside-a-same-named-private-function-reports-the-healthy-declarations-refusal -->
The file that fails to parse declares an unmarked `helper`, which no other directory can name. A healthy file calls
a `helper` that a healthy file in another directory keeps unexported: that refusal is the healthy declaration's, so it is
reported beside the parse error.
```maxon
// --- file: a/broken.maxon
function helper(n ExitCode) returns ExitCode
	let x: int = 0
	return n
end 'helper'

// --- file: c/lib.maxon
function helper(n ExitCode) returns ExitCode
	return n
end 'helper'

// --- file: b/main.maxon
function main() returns ExitCode
	return helper(1)
end 'main'
```
```maxoncstderr
error E2010: a/<fragment>:4:7: Expected '=' but got ':'
error E3004: b/<fragment>:15:9: call to undefined function 'helper'
```

<!-- test: error.a-failed-file-beside-a-same-named-type-reports-the-healthy-files-mistake -->
The file that fails to parse declares a typealias `Shape` that only it can name; a healthy file declares a
type of that name and calls a member it does not have. The call is the healthy type's mistake, so it is
reported beside the parse error.
```maxon
// --- file: a/broken.maxon
typealias Shape = int(0 to 9)

function broken(n Shape) returns Shape
	let x: int = 0
	return n
end 'broken'

// --- file: b/main.maxon
type Shape
	var sides as ExitCode

	static function make() returns Shape
		return Shape{sides: 4}
	end 'make'
end 'Shape'

function main() returns ExitCode
	return Shape.nothere()
end 'main'
```
```maxoncstderr
error E2010: a/<fragment>:6:7: Expected '=' but got ':'
error E3004: b/<fragment>:20:15: call to undefined function 'Shape.nothere'
```

<!-- test: error.a-failed-file-leaves-a-healthy-callers-ownership-unjudged -->
The file that fails to parse declares `pick`, which hands back a record. A healthy file binds a `let` to
`pick`'s result while it also writes the record it handed `pick`. Nothing is known of what a refused
declaration returns, so no ownership mistake is the healthy caller's: only the parse errors are reported.
```maxon
// --- file: a/broken.maxon
export type Box
	export var n as ExitCode

	export static function create() returns Box
		return Box{n: 0}
	end 'create'
end 'Box'

export function pick(box Box) returns Box
	let end = 1
	return box
end 'pick'

// --- file: b/main.maxon
function main() returns ExitCode
	var box = Box.create()
	let picked = pick(box)
	box.n = 7
	return picked.n
end 'main'
```
```maxoncstderr
error E2010: a/<fragment>:12:6: Expected identifier but got 'end'
error E2001: a/<fragment>:12:10: Expected function declaration, got '='
error E2001: a/<fragment>:14:1: Expected function declaration, got 'end'
```

<!-- test: error.a-failed-file-leaves-a-healthy-callers-function-argument-unjudged -->
A healthy file passes the result of a refused declaration, called with a record it still writes, to a
function-typed parameter. What a refused declaration returns is unknown, so the pass is not judged: only the parse
errors are reported.
```maxon
// --- file: a/broken.maxon
export type Box
	export var n as ExitCode

	export static function create() returns Box
		return Box{n: 0}
	end 'create'
end 'Box'

export function pick(box Box) returns Box
	let end = 1
	return box
end 'pick'

// --- file: b/main.maxon
typealias TakesBox = function(Box) returns ExitCode

function main() returns ExitCode
	var box = Box.create()
	return passOn(function(b) gives b.n, box: box)
end 'main'

function passOn(take TakesBox, box Box) returns ExitCode
	var other = Box.create()
	other.n = box.n
	return take(pick(other))
end 'passOn'
```
```maxoncstderr
error E2010: a/<fragment>:12:6: Expected identifier but got 'end'
error E2001: a/<fragment>:12:10: Expected function declaration, got '='
error E2001: a/<fragment>:14:1: Expected function declaration, got 'end'
```

<!-- test: error.a-parse-error-in-one-function-beside-an-undefined-call-in-another-function-of-the-same-file-reports-both -->
A parse error abandons only the body it is in: the rest of the file is read, so an undefined call in another
function of the same file is reported beside it.
```maxon
function broken() returns ExitCode
	let x: int = 0
	return 0
end 'broken'

function other() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
error E3004: <fragment>:8:10: call to undefined function 'nothere'
```

<!-- test: error.a-call-to-a-function-whose-body-failed-to-parse-reports-only-the-parse-error -->
The function whose body failed to parse keeps its signature, so a correct call to it is no consequence of the
parse error and is not reported.
```maxon
function broken(n ExitCode) returns ExitCode
	let x: int = 0
	return n
end 'broken'

function main() returns ExitCode
	return broken(1)
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
```

<!-- test: error.a-wrong-call-to-a-function-whose-body-failed-to-parse-reports-both -->
The signature of a function whose body failed to parse survives, so a call that disagrees with it is refused
beside the parse error.
```maxon
function broken(n ExitCode) returns ExitCode
	let x: int = 0
	return n
end 'broken'

function main() returns ExitCode
	return broken(true)
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
error E3005: <fragment>:8:9: argument type mismatch for 'n': expected 'ExitCode', got 'bool'
```

<!-- test: error.a-parse-error-in-one-function-beside-a-refused-initializer-reports-both -->
A parse error in a function body and a refused top-level initializer in the same file are independent, so both
are reported.
```maxon
typealias Digit = int(0 to 9)

let TOO_BIG = 42 as Digit

function broken() returns ExitCode
	let x: int = 0
	return 0
end 'broken'

function main() returns ExitCode
	return TOO_BIG as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:18: Value 42 is outside the range of 'Digit' (int(0 to 9))
error E2010: <fragment>:7:7: Expected '=' but got ':'
```

<!-- test: error.a-parse-error-in-a-method-body-beside-an-undefined-call-in-a-function-reports-both -->
A method body is a body too: the type around it and the functions after it are read.
```maxon
type Counter
	export var total as ExitCode

	function bump() returns ExitCode
		let x: int = 0
		return self.total
	end 'bump'
end 'Counter'

function other() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:6:8: Expected '=' but got ':'
error E3004: <fragment>:12:10: call to undefined function 'nothere'
```

<!-- test: error.a-parse-error-in-a-function-body-beside-a-missing-main-reports-both -->
A file whose only fault in reading is a body is readable, so it is known to hold no `main`.
```maxon
function broken() returns ExitCode
	let x: int = 0
	return 0
end 'broken'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
error E3001: No 'main' function found
```

<!-- test: error.a-parse-error-in-a-declaration-head-beside-an-undefined-call-in-another-function-reports-both -->
A parse error outside every body abandons only the declaration it is in: the declarations after it are read,
so an undefined call in a later function of the same file is reported beside it.
```maxon
function broken( returns ExitCode
	return 0
end 'broken'

function other() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:2: Expected ')' but got 'return'
error E3004: <fragment>:7:10: call to undefined function 'nothere'
```

<!-- test: error.an-unclosed-parameter-list-with-a-default-value-abandons-only-its-declaration -->
An unclosed `(` after a parameter with a default value fails at the token that cannot continue the list, as it
does without a default, and abandons only its own declaration: a second such head is reported beside the
first, and the undefined call in a later function is reported too.
```maxon
function broken(step ExitCode = 1 returns ExitCode
	return 0
end 'broken'

function alsoBroken(step ExitCode = 2 returns ExitCode
	return 0
end 'alsoBroken'

function other() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2010: <fragment>:2:35: Expected ')' but got 'returns'
error E2010: <fragment>:6:39: Expected ')' but got 'returns'
error E3004: <fragment>:11:10: call to undefined function 'nothere'
```

<!-- test: error.a-parse-error-in-a-type-body-beside-an-undefined-call-in-a-later-function-reports-both -->
A parse error in a type's member abandons that type, not the file. The uses of the type are no mistake of
their own and are not reported; the undefined call in a later function is.
```maxon
type Counter
	export var total as ExitCode
	export var step ExitCode

	static function make() returns Counter
		return Counter{total: 0, step: 1}
	end 'make'
end 'Counter'

function read(c Counter) returns ExitCode
	return c.total
end 'read'

function other() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'other'

function main() returns ExitCode
	return read(Counter.make())
end 'main'
```
```maxoncstderr
error E2010: <fragment>:4:18: Expected '=' but got 'ExitCode'
error E3004: <fragment>:16:10: call to undefined function 'nothere'
```

<!-- test: error.a-malformed-declaration-line-between-two-functions-reports-only-itself -->
A line that begins no declaration is abandoned to the next declaration; `main` calls the functions on both
sides of it, and neither call is a mistake.
```maxon
function first() returns ExitCode
	return 1
end 'first'

zzz not a declaration

function second() returns ExitCode
	return 2
end 'second'

function main() returns ExitCode
	let a = first()
	let b = second()

	if a == b 'same'
		return 1
	end 'same'

	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:6:1: Expected function declaration, got 'zzz'
```

<!-- test: error.a-call-to-a-function-whose-head-failed-to-parse-reports-only-the-parse-error -->
`broken` is abandoned with its head, so its signature is gone; the call to it is the parse error's consequence
and is not reported.
```maxon
function broken( returns ExitCode
	return 0
end 'broken'

function main() returns ExitCode
	return broken()
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:2: Expected ')' but got 'return'
```

<!-- test: error.a-function-whose-head-failed-to-parse-used-as-a-function-value-reports-only-the-parse-error -->
`broken` is abandoned with its head, so its signature is gone; passing it as a function value is the parse error's consequence and is not reported.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function broken( returns ExitCode
	return 0
end 'broken'

function main() returns ExitCode
	return apply(broken, x: 1) as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:10:2: Expected ')' but got 'return'
```

<!-- test: error.a-file-with-many-independent-refusals-reports-every-one -->
Fourteen independent errors in one file, in all three kinds of region a parse refusal can be confined to (a function body, a declaration head, a top-level binding) and in a body that reads an undefined name, are each reported once and in source order. The valid declarations between them, which instantiate a generic type, build closures and take parameter defaults, are read in full, and the functions whose bodies were abandoned still accept the calls `main` makes to them.
```maxon
typealias Tally = int(0 to 1000000)
typealias Digit = int(0 to 9)

type Pair uses T
	var first as T
	var second as T

	static function create(first T, second T) returns Self
		return Self{first: first, second: second}
	end 'create'

	function sum() returns Tally
		return 1
	end 'sum'
end 'Pair'

type Crate
	var weight as Tally

	static function create(weight Tally) returns Crate
		return Crate{weight: weight}
	end 'create'

	function heavy(extra Tally = 1) returns Tally
		return self.weight + extra
	end 'heavy'
end 'Crate'

typealias CrateArray = Array with Crate
typealias CratePair = Pair with Crate

let CrateBase = 3

function totalCrates(scale Tally = 2) returns Tally
	var crates = CrateArray.create(0)
	crates.push(Crate.create(CrateBase))

	let bump = function(n Tally) gives n + scale
	var sum = 0 as Tally

	for crate in crates 'eachCrate'
		sum = bump(crate.heavy())
	end 'eachCrate'

	return sum
end 'totalCrates'

function brokenBody0() returns Tally
	let x: int = 0
	return CrateBase
end 'brokenBody0'

function brokenHead0( returns Tally
	return CrateBase
end 'brokenHead0'

let BadDigit0 = 10 as Digit

function missingName0() returns Tally
	return missing0
end 'missingName0'

type Sack
	var weight as Tally

	static function create(weight Tally) returns Sack
		return Sack{weight: weight}
	end 'create'

	function heavy(extra Tally = 1) returns Tally
		return self.weight + extra
	end 'heavy'
end 'Sack'

typealias SackArray = Array with Sack
typealias SackPair = Pair with Sack

let SackBase = 4

function totalSacks(scale Tally = 2) returns Tally
	var sacks = SackArray.create(0)
	sacks.push(Sack.create(SackBase))

	let bump = function(n Tally) gives n + scale
	var sum = 0 as Tally

	for sack in sacks 'eachSack'
		sum = bump(sack.heavy())
	end 'eachSack'

	return sum
end 'totalSacks'

function brokenBody1() returns Tally
	let y: int = 1
	return SackBase
end 'brokenBody1'

function brokenHead1 returns Tally
	return SackBase
end 'brokenHead1'

let BadDigit1 = 11 as Digit

function brokenBody2() returns Tally
	let z: int = 2
	return CrateBase
end 'brokenBody2'

function brokenHead2( returns Tally
	return SackBase
end 'brokenHead2'

let BadDigit2 = 12 as Digit

function missingName1() returns Tally
	return missing1
end 'missingName1'

type Bundle
	var weight as Tally

	static function create(weight Tally) returns Bundle
		return Bundle{weight: weight}
	end 'create'

	function heavy(extra Tally = 1) returns Tally
		return self.weight + extra
	end 'heavy'
end 'Bundle'

typealias BundleArray = Array with Bundle
typealias BundlePair = Pair with Bundle

let BundleBase = 5

function totalBundles(scale Tally = 2) returns Tally
	var bundles = BundleArray.create(0)
	bundles.push(Bundle.create(BundleBase))

	let bump = function(n Tally) gives n + scale
	var sum = 0 as Tally

	for bundle in bundles 'eachBundle'
		sum = bump(bundle.heavy())
	end 'eachBundle'

	return sum
end 'totalBundles'

function brokenBody3() returns Tally
	let w: int = 3
	return BundleBase
end 'brokenBody3'

function brokenHead3( returns Tally
	return BundleBase
end 'brokenHead3'

let BadDigit3 = 13 as Digit

function pairWeights() returns Tally
	let crates = CratePair.create(Crate.create(1), second: Crate.create(2))
	let sacks = SackPair.create(Sack.create(1), second: Sack.create(2))
	let bundles = BundlePair.create(Bundle.create(1), second: Bundle.create(2))
	return crates.sum() + sacks.sum() + bundles.sum()
end 'pairWeights'

function main() returns ExitCode
	var acc = 0 as Tally
	acc = (acc + totalCrates()) and 7
	acc = (acc + totalSacks(scale: 3)) and 7
	acc = (acc + totalBundles()) and 7
	acc = (acc + pairWeights()) and 7
	acc = (acc + brokenBody0()) and 7
	acc = (acc + brokenBody1()) and 7
	acc = (acc + brokenBody2()) and 7
	acc = (acc + brokenBody3()) and 7
	acc = (acc + missingName0()) and 7
	acc = (acc + missingName1()) and 7
	return (acc and 7) as ExitCode
end 'main'
```
```maxoncstderr
error E2010: <fragment>:50:7: Expected '=' but got ':'
error E2010: <fragment>:55:2: Expected ')' but got 'return'
error E3005: <fragment>:58:20: Value 10 is outside the range of 'Digit' (int(0 to 9))
error E2004: <fragment>:61:9: Undefined variable 'missing0'
error E2010: <fragment>:96:7: Expected '=' but got ':'
error E2010: <fragment>:100:22: Expected '(' but got 'returns'
error E3005: <fragment>:104:20: Value 11 is outside the range of 'Digit' (int(0 to 9))
error E2010: <fragment>:107:7: Expected '=' but got ':'
error E2010: <fragment>:112:2: Expected ')' but got 'return'
error E3005: <fragment>:115:20: Value 12 is outside the range of 'Digit' (int(0 to 9))
error E2004: <fragment>:118:9: Undefined variable 'missing1'
error E2010: <fragment>:153:7: Expected '=' but got ':'
error E2010: <fragment>:158:2: Expected ')' but got 'return'
error E3005: <fragment>:161:20: Value 13 is outside the range of 'Digit' (int(0 to 9))
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
error E3220: <fragment>:3:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
error E3220: <fragment>:2:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-static-initializer-calling-a-sibling-instance-method-bare-has-no-receiver -->
`fresh` is an instance method, so a `static let` initializer calling it bare has no receiver to call it on,
and is refused as the same call in a static method's body is.
```maxon
typealias Digit = int(0 to 9)

type Holder
	export var n as Digit

	static let shared = fresh()

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
error E3220: <fragment>:2:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
error E3220: <fragment>:2:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
error E3220: <fragment>:2:9: the initializer of 'f' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
error E3220: <fragment>:4:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```


<!-- test: error.a-field-assignment-through-a-refused-call-var-reports-the-call-once -->
`G`'s initializer is a refused call, so writing one of its fields is not reported again.
```maxon
typealias Digit = int(0 to 9)

let G = nothere(7)

function main() returns ExitCode
	G.n = 3 as Digit
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:4:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-factory-initializer-past-a-same-named-constant-in-another-file-folds -->
The initializer of a module-level or `static` `let` calls `make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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

// --- file: other/other.maxon
export typealias Zero = int(0 to 0)

let make = 0

export function other() returns Zero
	return make
end 'other'
```
```maxoncstderr
error E3220: <fragment>:17:9: the initializer of 'G' calls 'make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-field-assignment-through-a-refused-call-static-var-reports-the-call-once -->
`Holder.shared`'s initializer is a refused call, so writing one of its fields is not reported again.
```maxon
typealias Digit = int(0 to 9)

type Holder
	export var n as Digit

	static let shared = nothere(7)

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
error E3220: <fragment>:7:22: the initializer of 'Holder.shared' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```


<!-- test: error.a-static-initializer-folds-its-sibling-factory-past-a-same-named-constant -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:9:22: the initializer of 'Box.origin' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-static-initializer-calling-a-generic-sibling-is-not-a-constant -->
`pick` is a generic sibling static, so the initializer's bare call means it and is refused as a call under the sibling's own name.
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
error E3220: <fragment>:5:20: the initializer of 'Registry.seed' calls 'Registry.pick': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-top-level-initializer-call-to-a-hidden-generic-is-not-exported -->
The only `larger` is an unmarked generic in `other/`, so `main.maxon`, outside it, cannot name it.
```maxon
// --- file: main.maxon
let big = larger(1 as Integer, b: 2 as Integer)

function main() returns ExitCode
	return (big + useLarger()) as ExitCode
end 'main'

// --- file: other/other.maxon
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

<!-- test: error.a-refused-call-inside-a-record-literal-reports-the-call-alone -->
The refused call sits inside a record literal; the unneeded `as Small` written before it is a secondary
finding, held while the call's error stands, as in a function body.
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
error E3220: <fragment>:13:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-refused-call-inside-a-record-literal-in-a-function-body-reports-the-call-alone -->
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
```

<!-- test: error.uses-of-a-refused-call-constant-report-the-call-once -->
Every use of `G` below meets a constant whose initializer is a refused call: only the call is reported.
```maxon
typealias Digit = int(0 to 9)

let G = nothere(7)

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
error E3220: <fragment>:4:9: the initializer of 'G' calls 'nothere': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```


<!-- test: error.a-static-initializer-calling-a-function-valued-binding-past-a-sibling-calls-the-binding -->
The initializer of a module-level or `static` `let` calls `makerOf`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:13:12: the initializer of 'make' calls 'makerOf': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: <fragment>:18:22: the initializer of 'Box.origin' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-static-method-body-calling-a-function-valued-binding-past-a-sibling-calls-the-binding -->
The initializer of a module-level or `static` `let` calls `makerOf`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:13:12: the initializer of 'make' calls 'makerOf': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: a-closure-bound-by-a-top-level-let-is-called-and-passed-as-a-function-value -->
`addSeven` is a closure literal in a top-level initializer. Calling it and handing it to a function-typed
parameter both resolve its shape.
```maxon
typealias Tally = int(0 to 1000000)
typealias Step = function(Tally) returns Tally

let addSeven = function(n Tally) gives n + 7

function run(step Step, n Tally) returns Tally
	return step(n)
end 'run'

function main() returns ExitCode
	print("{addSeven(35)} {run(addSeven, n: 100)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42 107
```

<!-- test: a-closure-bound-by-a-top-level-let-may-call-in-its-body -->
A closure literal in a top-level initializer builds a function value, and its body runs only when the closure is
called, so a call inside the body is not a call the initializer makes.
```maxon
typealias Tally = int(0 to 1000000)

function doubled(n Tally) returns Tally
	return n * 2
end 'doubled'

let addDoubled = function(n Tally) gives doubled(n) + 1
let measure = function(label String) gives label.byteLength()

function main() returns ExitCode
	print("{addDoubled(20)} {measure("abcd")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
41 4
```

<!-- test: error.a-call-around-a-closure-in-a-top-level-let-is-still-refused -->
The closure's body is not the initializer, but the call that receives the closure is: `wrap` runs while the
initializer builds its value, so the initializer calls a function, and the refusal names `wrap`, not the `doubled` the
closure's body calls.
```maxon
typealias Tally = int(0 to 1000000)
typealias Step = function(Tally) returns Tally

function doubled(n Tally) returns Tally
	return n * 2
end 'doubled'

function wrap(step Step) returns Step
	return step
end 'wrap'

let wrapped = wrap(function(n Tally) gives doubled(n))

function main() returns ExitCode
	print("{wrapped(1)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3220: <fragment>:13:15: the initializer of 'wrapped' calls 'wrap': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: top-level-closures-of-different-shapes-keep-their-own-shapes -->
Each top-level closure is its own function type: `weigh` takes two parameters and `describe` returns a
`String`, beside `addSeven`, and every call and every hand-over is judged against its own closure's shape.
```maxon
typealias Tally = int(0 to 1000000)
typealias Step = function(Tally) returns Tally
typealias Describe = function(Tally) returns String
typealias Weigh = function(Tally, Tally) returns Tally

let addSeven = function(n Tally) gives n + 7
let describe = function(n Tally) gives "n={n}"
let weigh = function(a Tally, b Tally) gives a * 10 + b

function runStep(step Step, n Tally) returns Tally
	return step(n)
end 'runStep'

function runDescribe(step Describe, n Tally) returns String
	return step(n)
end 'runDescribe'

function runWeigh(step Weigh, a Tally, b Tally) returns Tally
	return step(a, b)
end 'runWeigh'

function main() returns ExitCode
	print("{runStep(addSeven, n: 3)} {runDescribe(describe, n: 91)} {runWeigh(weigh, a: 4, b: 5)}\n")
	print("{addSeven(1)} {describe(2)} {weigh(6, 7)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10 n=91 45
8 n=2 67
```

<!-- test: a-closure-bound-by-an-exported-top-level-let-is-used-from-another-file -->
`main.maxon` reads `triple` and hands it to `apply`; neither file's parse is ordered against the other's, so
the closure's shape reaches the reader through the declaration sweep.
```maxon
// --- file: main.maxon
function main() returns ExitCode
	print("{triple(14)} {apply(triple, n: 5)}\n")
	return 0
end 'main'

// --- file: steps/steps.maxon
export typealias Tally = int(0 to 1000000)
export typealias Step = function(Tally) returns Tally

export let triple = function(n Tally) gives n * 3

export function apply(step Step, n Tally) returns Tally
	return step(n)
end 'apply'
```
```exitcode
0
```
```stdout
42 15
```

<!-- test: error.a-static-initializer-calling-a-function-valued-binding-past-a-sibling-with-a-parameter-calls-the-binding -->
The initializer of a module-level or `static` `let` calls `makerOf`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:13:12: the initializer of 'make' calls 'makerOf': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
error E3220: <fragment>:18:22: the initializer of 'Box.origin' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```

<!-- test: error.a-static-method-body-calling-a-function-valued-binding-past-a-sibling-with-a-parameter -->
The initializer of a module-level or `static` `let` calls `makerOf`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:13:12: the initializer of 'make' calls 'makerOf': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
```


<!-- test: error.a-static-initializer-calling-a-sibling-past-a-same-named-constant-reports-the-siblings-defect -->
The initializer of a module-level or `static` `let` calls `Box.make`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
error E3220: <fragment>:9:22: the initializer of 'Box.origin' calls 'Box.make': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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
error E3005: <fragment>:11:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode' — cast the argument with 'as Small'
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
error E3005: <fragment>:10:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode' — cast the argument with 'as Small'
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
error E3005: <fragment>:12:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode' — cast the argument with 'as Small'
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
error E3005: <fragment>:11:9: argument type mismatch for 'n': expected 'Small', got 'ExitCode' — cast the argument with 'as Small'
```

<!-- test: error.a-match-expression-in-a-call-argument-ends-a-top-level-let-at-its-own-line -->
The initializer of a module-level or `static` `let` calls `tag`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:11:13: the initializer of 'LABEL' calls 'tag': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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

<!-- test: error.a-handler-block-otherwise-in-a-call-argument-ends-a-top-level-let-at-its-own-line -->
The initializer of a module-level or `static` `let` calls `parse`, and a module-level initializer calls nothing, so the program is refused (E3220).
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
```maxoncstderr
error E3220: <fragment>:17:13: the initializer of 'LABEL' calls 'parse': a module-level or static `let` initializer calls nothing, so write a literal, or build the value where it is used
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

<!-- test: error.a-type-name-of-a-refused-alias-written-in-a-healthy-body-reports-only-the-refusal -->
```maxon
typealias Odd = bits(5)

function main() returns ExitCode
	let x = 1 as Odd
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: <fragment>:2:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-type-refused-in-one-file-and-written-in-a-healthy-file-reports-only-the-refusal -->
```maxon
// --- file: odd.maxon
export typealias Odd = bits(5)

// --- file: main.maxon
function main() returns ExitCode
	let x = 1 as Odd
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: <fragment>:3:29: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-call-to-a-refused-reserved-name-from-a-healthy-function-reports-only-the-refusal -->
```maxon
function __helper() returns ExitCode
	return 0
end '__helper'

function other() returns ExitCode
	return __helper()
end 'other'

function main() returns ExitCode
	return other()
end 'main'
```
```maxoncstderr
error E2051: <fragment>:2:10: identifier '__helper' is reserved: declarations starting with '__' are reserved for compiler internals
```

<!-- test: error.an-unknown-type-no-refused-declaration-names-is-still-reported-beside-a-refusal -->
```maxon
typealias Odd = bits(5)

function main() returns ExitCode
	let x = 1 as Missing
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: <fragment>:2:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
error E3011: <fragment>:5:12: Unknown type 'Missing'
```

<!-- test: error.a-body-refused-in-one-file-leaves-a-clone-handed-to-a-let-field-in-another-file-unreported -->
```maxon
// --- file: refused.maxon
typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function classify(c Container) returns ExitCode
	match c 'check'
		empty then return 1
		value(_) then return 0
	end 'check'
end 'classify'

// --- file: main.maxon
type Pair
	let left as ByteArray
	let right as ByteArray

	static function of(left ByteArray, right ByteArray) returns Self
		return Self{left: left, right: right}
	end 'of'
end 'Pair'

function settle(text ByteArray) returns Pair
	return Pair.of(text.clone(), right: text.clone())
end 'settle'

function main() returns ExitCode
	let settled = settle(ByteArray.create(0))
	return 0
end 'main'
```
```maxoncstderr
error E3081: <fragment>:13:3: use 'value' instead of 'value(_)' to ignore associated values
```

<!-- test: error.a-program-that-folds-twice-with-an-explained-consequence-does-not-panic -->
```maxon
typealias Digit = int(0 to 9)
typealias Odd = bits(5)

function same(v T) uses T returns T
	return v
end 'same'

function main() returns ExitCode
	let b = same(4 as Digit)
	let x = 1 as Odd
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: <fragment>:3:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-broken-body-keeps-a-discarded-pure-result-elsewhere -->
```maxon
typealias Tally = int(0 to 1000)

function broken(v Tally) returns Tally
	let s = v + "x"
	return v
end 'broken'

function same(v Tally) returns Tally
	return v
end 'same'

function main() returns ExitCode
	_ = broken(1)
	_ = same(1)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:12: Cannot operate on Tally and String
error E3064: <fragment>:15:2: result of pure function 'same' must be used
```

<!-- test: error.a-refused-call-keeps-a-discarded-pure-result-on-another-line-of-the-same-body -->
```maxon
typealias Tally = int(0 to 1000)

function same(v Tally) returns Tally
	return v
end 'same'

function main() returns ExitCode
	let s = nothere(7)
	_ = same(1)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:9:10: call to undefined function 'nothere'
error E3064: <fragment>:10:2: result of pure function 'same' must be used
```

<!-- test: error.a-generic-services-function-valued-reply-is-reported-once-at-the-handler -->
```maxon
typealias Tally = int(0 to 1000)
typealias Op = function(Tally) returns Tally

type Maker uses T
	var seen as Tally

	static function create(seed T) returns Self
		return Self{seen: 0}
	end 'create'

	export function make() returns Op
		self.seen = self.seen + 1
		return function(n Tally) gives n + 1
	end 'make'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create(1 as Tally)
	let op = try await h.make() otherwise panic("the maker is running")
	return op(1) as ExitCode
end 'main'
```
```maxoncstderr
error E3217: <fragment>:19:10: the message `Maker.make` returns a function value, a closure record that owns what it captures and does not cross to another thread, and this `spawn` makes `Maker` a service — whose reply is handed to the green thread that awaits it. `await` a promise in the handler and return the result it yields, return a value whose graph holds no function value or thread-bound handle, or drop the `returns` clause
```

<!-- test: error.a-spawn-refusal-beside-another-methods-abandoned-body-is-still-reported -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Maker uses T
	export typealias Items = Array with T
	var seen as Integer

	static function create(seed T) returns Self
		var first = Items.create(0)
		first.push(seed)
		return Self{seen: first.count() as Integer}
	end 'create'

	export function make() returns Items
		self.seen = self.seen + 1
		return Items.create(0)
	end 'make'

	export function other() returns Integer
		return (1 +
	end 'other'
end 'Maker'

function main() returns ExitCode
	let h = spawn Maker.create(MachinePin.acquire())
	let items = try await h.make() otherwise panic("the maker is running")
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:21:2: Expected expression but got 'end'
error E3217: <fragment>:25:10: the message `Maker.make` returns a `Maker.Items` with no capability word, and this `spawn` makes `Maker` a service — whose reply is handed to the green thread that awaits it, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `returns isolated …` or `returns immutable …`
```

<!-- test: error.a-spawn-refusal-beside-an-unrelated-error-builds-no-loop-for-the-refused-instance -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Maker uses T
	export typealias Items = Array with T
	var seen as Integer

	static function create(seed T) returns Self
		var first = Items.create(0)
		first.push(seed)
		return Self{seen: first.count() as Integer}
	end 'create'

	export function make() returns Items
		self.seen = self.seen + 1
		return Items.create(0)
	end 'make'
end 'Maker'

function unrelated() returns Integer
	return nothere(7)
end 'unrelated'

function main() returns ExitCode
	let h = spawn Maker.create(MachinePin.acquire())
	let items = try await h.make() otherwise panic("the maker is running")
	print("{items.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:21:9: call to undefined function 'nothere'
error E3217: <fragment>:25:10: the message `Maker.make` returns a `Maker.Items` with no capability word, and this `spawn` makes `Maker` a service — whose reply is handed to the green thread that awaits it, so what crosses must be owned by one side or read-only on both. `isolated` gives the value away: the sender builds it for this one send and no other name reaches it afterwards. `immutable` shares it: the sender freezes it (`freeze x.clone()`), and both threads only read it. Write it `returns isolated …` or `returns immutable …`
```

<!-- test: error.an-unclosed-bracket-ends-at-a-column-one-module-function -->
```maxon
typealias Digit = int(0 to 9)

function broken() returns Digit
	return foo(1,
end 'broken'

function helper() returns ExitCode
	return nothere(7)
end 'helper'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2053: <fragment>:6:1: the second and later arguments must be named ('name: value')
error E3004: <fragment>:9:9: call to undefined function 'nothere'
```

<!-- test: error.an-unclosed-bracket-ends-at-a-column-one-default-declaration -->
```maxon
typealias Digit = int(0 to 9)

function broken() returns Digit
	return foo(1,
end 'broken'

default Feed = nothere(7)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2053: <fragment>:6:1: the second and later arguments must be named ('name: value')
error E3011: <fragment>:8:9: Unknown type 'Feed'
```

<!-- test: error.an-abandoned-declaration-ends-at-a-default-declaration -->
```maxon
typealias Digit = int(0 to 9)

typealias Bad =

default Feed = nothere(7)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:16: Unsupported: a typealias over 'newline' (a typealias names `int(low to high)`, `float(low to high)`, `bits(n)`, `function(...)`, a tuple `(A, B)` or a generic instance `Base with …`)
error E3011: <fragment>:6:9: Unknown type 'Feed'
```

<!-- test: error.an-abandoned-declaration-ends-at-a-let-declaration -->
```maxon
typealias Digit = int(0 to 9)

typealias Bad =

let shown = 3

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:16: Unsupported: a typealias over 'newline' (a typealias names `int(low to high)`, `float(low to high)`, `bits(n)`, `function(...)`, a tuple `(A, B)` or a generic instance `Base with …`)
```

<!-- test: error.a-fold-redone-after-a-refused-binding-with-an-explained-consequence-does-not-panic -->
```maxon
typealias Digit = int(0 to 9)
typealias UnaryOp = function(Digit) returns Digit

let Broken = 3 + "x"

function same(v T) uses T returns T
	return v
end 'same'

function twice(v Digit) returns Digit
	return v
end 'twice'

function pickFn() returns UnaryOp
	return twice
end 'pickFn'

function main() returns ExitCode
	let a = Broken
	let b = same(4 as Digit)
	return pickFn().x
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:16: Cannot operate on int and String
error E2015: <fragment>:22:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the byte records (`String`, `Character`) carry members here
```

<!-- test: error.a-body-reading-a-refused-global-keeps-its-own-independent-member-access -->
```maxon
typealias Digit = int(0 to 9)
typealias UnaryOp = function(Digit) returns Digit

let Broken = 3 + "x"

function twice(v Digit) returns Digit
	return v
end 'twice'

function pickFn() returns UnaryOp
	return twice
end 'pickFn'

function main() returns ExitCode
	let a = Broken
	return pickFn().x
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:16: Cannot operate on int and String
error E2015: <fragment>:17:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the byte records (`String`, `Character`) carry members here
```

<!-- test: error.a-parameter-sharing-a-refused-globals-name-keeps-its-bodys-member-access -->
```maxon
typealias Digit = int(0 to 9)
typealias UnaryOp = function(Digit) returns Digit

let total = 3 + "x"

function twice(v Digit) returns Digit
	return v
end 'twice'

function pickFn() returns UnaryOp
	return twice
end 'pickFn'

function show(total Digit) returns Digit
	_ = total
	return pickFn().x
end 'show'

function main() returns ExitCode
	return show(3)
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:15: Cannot operate on int and String
error E2015: <fragment>:17:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the byte records (`String`, `Character`) carry members here
```

<!-- test: error.a-refused-global-named-like-the-member-keeps-the-member-access -->
```maxon
typealias Digit = int(0 to 9)
typealias UnaryOp = function(Digit) returns Digit

let x = 3 + "x"

function twice(v Digit) returns Digit
	return v
end 'twice'

function pickFn() returns UnaryOp
	return twice
end 'pickFn'

function main() returns ExitCode
	return pickFn().x
end 'main'
```
```maxoncstderr
error E2004: <fragment>:5:11: Cannot operate on int and String
error E2015: <fragment>:16:18: Unsupported: a member access 'x' on a 'function' value — only a struct, a generic instance and the byte records (`String`, `Character`) carry members here
```

<!-- test: error.a-refused-global-keeps-an-undefined-method-of-the-same-member-name -->
```maxon
typealias Digit = int(0 to 9)

type Point
	let x as Digit

	static function create() returns Point
		return Self{x: 1}
	end 'create'
end 'Point'

let make = 3 + "x"

function main() returns ExitCode
	let p = Point.make()
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:12:14: Cannot operate on int and String
error E3004: <fragment>:15:16: call to undefined function 'Point.make'
```

<!-- test: error.a-refused-global-keeps-a-wrong-arity-call-to-a-same-named-method -->
```maxon
typealias Digit = int(0 to 9)

type Point
	let x as Digit

	static function create() returns Point
		return Self{x: 1}
	end 'create'

	function scale(k Digit) returns Digit
		return k
	end 'scale'
end 'Point'

let scale = 3 + "x"

function main() returns ExitCode
	let q = Point.create().scale(1, extra: 2)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:16:15: Cannot operate on int and String
error E3036: <fragment>:19:25: 'Point.scale' expects 2 argument(s) but 3 were provided
```

<!-- test: error.a-keyword-less-main-head-is-the-only-diagnostic -->
```maxon
main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:1: Expected function declaration, got 'main'
```

<!-- test: error.a-fold-redone-with-a-stdlib-body-demand-reports-only-the-width -->
```maxon
typealias Digit = int(0 to 9)
typealias Odd = bits(5)

function same(v T) uses T returns T
	return v
end 'same'

function main() returns ExitCode
	let b = same(4 as Digit)
	let x = 1 as Odd
	let loud = "quiet".toUpper()
	print("{loud}{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3148: <fragment>:3:22: 'bits(5)' is not a legal width: bits(n) takes 1, 2, 4, 8, 16, 32 or 64 — the widths a slot or a sub-byte packed field can hold
```

<!-- test: error.a-refused-extension-keeps-an-undefined-call-on-an-unrelated-type -->
```maxon
typealias Digit = int(0 to 9)

type Foo
	let n as Digit

	static function create() returns Foo
		return Self{n: 1}
	end 'create'
end 'Foo'

type Bar
	let n as Digit

	static function create() returns Bar
		return Self{n: 2}
	end 'create'
end 'Bar'

extension Foo
	function shifted(amount) returns Digit
		return n
	end 'shifted'
end 'Foo'

function main() returns ExitCode
	let c = Bar.missing()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:19: Unsupported: parameter 'amount' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
error E3004: <fragment>:27:14: call to undefined function 'Bar.missing'
```

<!-- test: error.a-refused-extension-keeps-a-method-miss-on-a-list-not-built-from-the-extended-type -->
```maxon
typealias Digit = int(0 to 9)
typealias Digits = List with Digit

type Box
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

extension Box
	typealias Items = Array with Digit

	function all() returns Items
		return Items.create()
	end 'all'
end 'Box'

function main() returns ExitCode
	var xs = Digits.create()
	xs.pushh(1)
	return 0
end 'main'
```
```maxoncstderr
error E3061: <fragment>:15:12: Duplicate typealias 'Items': `Box.Items` is already declared in the body of `Box`
error E4006: <fragment>:24:5: Type 'List' has no method named 'pushh'
```

<!-- test: error.a-refused-extension-keeps-a-missing-static-method-whose-argument-came-from-the-refused-producer -->
```maxon
// --- file: lib/token.maxon
export type Token
	export var n as Count

	export static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

// --- file: main.maxon
typealias Digit = int(0 to 9)

type Box
	typealias Items = List with Digit
	export var n as Digit

	static function create(n Digit) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

extension Box
	typealias Items = Array with Digit

	function all() returns Items
		return Items.create()
	end 'all'
end 'Box'

function main() returns ExitCode
	let box = Box.create(3)
	let xs = box.all()
	lib.Token.nosuch(xs)
	return 0
end 'main'
```
```maxoncstderr
error E3061: <fragment>:24:12: Duplicate typealias 'Items': `Box.Items` is already declared in the body of `Box`
error E4006: <fragment>:34:12: Type 'Token' has no method named 'nosuch'
```

<!-- test: error.a-call-with-too-few-arguments-to-a-function-with-an-unknown-parameter-type-keeps-its-arity-error -->
```maxon
typealias Digit = int(0 to 9)

function firstOf(b Bag) returns Digit
	return try b.get(0) otherwise 0
end 'firstOf'

function main() returns ExitCode
	return firstOf() as ExitCode
end 'main'
```
```maxoncstderr
error E3011: <fragment>:4:20: Unknown type 'Bag'
error E3036: <fragment>:9:9: 'firstOf' expects 1 argument(s) but 0 were provided
```

<!-- test: error.a-mismatched-argument-beside-an-unknown-parameter-type-keeps-its-own-error -->
```maxon
typealias Digit = int(0 to 9)

function pair(b Bag, n Digit) returns Digit
	return n
end 'pair'

function main() returns ExitCode
	return pair(1, n: true) as ExitCode
end 'main'
```
```maxoncstderr
error E3011: <fragment>:4:17: Unknown type 'Bag'
error E3005: <fragment>:9:9: argument type mismatch for 'n': expected 'Digit', got 'bool'
```

<!-- test: error.an-unknown-type-name-no-declaration-reported-stays-reported-beside-one-that-was -->
```maxon
typealias Digit = int(0 to 9)

function firstOf(b Bag) returns Digit
	return try b.get(0) otherwise 0
end 'firstOf'

function main() returns ExitCode
	return 5 as Nosuch
end 'main'
```
```maxoncstderr
error E3011: <fragment>:4:20: Unknown type 'Bag'
error E3011: <fragment>:9:11: Unknown type 'Nosuch'
```

<!-- test: error.a-refused-extension-hides-a-call-to-its-own-member-on-the-extended-type -->
```maxon
typealias Digit = int(0 to 9)

type Foo
	let n as Digit

	static function create() returns Foo
		return Self{n: 1}
	end 'create'
end 'Foo'

extension Foo
	function shifted(amount) returns Digit
		return n
	end 'shifted'
end 'Foo'

function main() returns ExitCode
	let c = Foo.create().shifted(1)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:19: Unsupported: parameter 'amount' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```

<!-- test: error.a-refused-interface-extension-hides-a-call-to-its-member-on-a-conformer -->
```maxon
typealias Digit = int(0 to 9)

interface Named
	function label() returns Digit
end 'Named'

type Thing implements Named
	let n as Digit

	static function create() returns Thing
		return Self{n: 1}
	end 'create'

	function label() returns Digit
		return n
	end 'label'
end 'Thing'

extension Named
	function shouted(amount) returns Digit
		return label()
	end 'shouted'
end 'Named'

function main() returns ExitCode
	let t = Thing.create()
	return t.shouted(1)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:19: Unsupported: parameter 'amount' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```

<!-- test: error.a-call-to-a-function-with-an-abandoned-body-from-a-conformer-reports-only-the-syntax-error -->
The body of `scaled` is abandoned at its syntax error, so whether it waits is unknown: `Ruler.measure`, which calls it
and satisfies a requirement that never waits, is not reported as one that may wait. `other` is an independent
error and stays reported.
```maxon
typealias Tally = int(0 to 1024)

interface Measured
	function measure() returns Tally
end 'Measured'

type Ruler implements Measured
	var base as Tally

	static function create() returns Self
		return Self{base: 3}
	end 'create'

	function measure() returns Tally
		return scaled(self.base)
	end 'measure'
end 'Ruler'

function scaled(n Tally) returns Tally
	let total = n + )
	return total
end 'scaled'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function report(item Measured) returns Tally
	return item.measure()
end 'report'

function main() returns ExitCode
	let ruler = Ruler.create()
	return report(ruler) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:21:18: Expected expression but got ')'
error E2004: <fragment>:26:11: Cannot operate on Tally and String
```

<!-- test: error.a-function-value-naming-a-function-with-an-abandoned-body-reports-only-the-syntax-error -->
`scaled` is passed by name where a function type never waits. Its body is abandoned at the syntax error, so whether it
waits is unknown and the function value is not reported as one that may wait. `other` stays reported.
```maxon
typealias Tally = int(0 to 1024)
typealias Step = function(Tally) returns Tally

function scaled(n Tally) returns Tally
	let total = n + )
	return total
end 'scaled'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function drive(step Step) returns Tally
	return step(3)
end 'drive'

function main() returns ExitCode
	let r = drive(function(n) gives scaled(n))
	let s = drive(scaled)
	return (r + s) as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:6:18: Expected expression but got ')'
error E2004: <fragment>:11:11: Cannot operate on Tally and String
```

<!-- test: error.a-result-of-a-function-with-an-abandoned-body-stored-into-a-frozen-array-reports-only-the-syntax-error -->
`pick` is abandoned at its syntax error, so what its result holds is unknown: storing the result into an array of
`immutable Box` is not reported as storing mutable data. Storing it into an array of `Box` is clean, and `other`
stays reported.
```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(first Box, second Box) returns Box
	let chosen = first if first.n > ) else second
	return chosen
end 'pick'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	let loose = Box.create(2)
	let items = Boxes.create(0)
	let kept = FrozenBoxes.create(0)
	items.push(pick(frozen, second: loose))
	kept.push(pick(frozen, second: loose))
	let direct = pick(frozen, second: loose)
	items.push(direct)
	kept.push(direct)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:15:34: Expected expression but got ')'
error E2004: <fragment>:20:11: Cannot operate on Tally and String
```

<!-- test: error.an-undefined-call-as-the-result-of-a-function-stored-into-a-frozen-array-reports-only-the-undefined-call -->
`pick` returns the result of `nothere`, which does not exist, so that result is unknown: nothing is concluded about what
`pick` returns or about the stores of it into either array. `other` stays reported.
```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(first Box, second Box) returns Box
	let chosen = nothere()
	return chosen
end 'pick'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	let loose = Box.create(2)
	let items = Boxes.create(0)
	let kept = FrozenBoxes.create(0)
	items.push(pick(frozen, second: loose))
	kept.push(pick(frozen, second: loose))
	let direct = pick(frozen, second: loose)
	items.push(direct)
	kept.push(direct)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:15:15: call to undefined function 'nothere'
error E2004: <fragment>:20:11: Cannot operate on Tally and String
```

<!-- test: error.a-result-passed-through-a-wrapper-of-an-abandoned-function-stored-into-a-frozen-array-reports-only-the-syntax-error -->
`pick` is abandoned at its syntax error, and `wrap` returns the result of calling it, so what `wrap` returns is unknown
too: storing the result of `wrap` into an array of `immutable Box` is not reported as storing mutable data. Storing it
into an array of `Box` is clean, and `other` stays reported.
```maxon
typealias Tally = int(0 to u64.max)
typealias Boxes = Array with Box
typealias FrozenBoxes = Array with immutable Box

type Box
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function pick(first Box, second Box) returns Box
	let chosen = first if first.n > ) else second
	return chosen
end 'pick'

function wrap(a Box, second Box) returns Box
	return pick(a, second: second)
end 'wrap'

function other(n Tally) returns Tally
	return n + "x"
end 'other'

function main() returns ExitCode
	let frozen = freeze Box.create(1)
	let loose = Box.create(2)
	let items = Boxes.create(0)
	let kept = FrozenBoxes.create(0)
	items.push(wrap(frozen, second: loose))
	kept.push(wrap(frozen, second: loose))
	let direct = wrap(frozen, second: loose)
	items.push(direct)
	kept.push(direct)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:15:34: Expected expression but got ')'
error E2004: <fragment>:24:11: Cannot operate on Tally and String
```

<!-- test: error.a-call-to-a-function-whose-head-lost-its-keyword-reports-only-the-lost-head -->
`helper() returns ExitCode` has no `function` keyword, so the declaration is refused and every call to `helper` is a
consequence of that refusal, not an undefined function. `other` stays reported.
```maxon
helper() returns ExitCode
	return 0
end 'helper'

function other(n ExitCode) returns ExitCode
	return n + "x"
end 'other'

function twice() returns ExitCode
	return helper()
end 'twice'

function main() returns ExitCode
	let a = helper()
	return twice() + a
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:1: Expected function declaration, got 'helper'
error E2004: <fragment>:7:11: Cannot operate on ExitCode and String
```

<!-- test: error.a-lost-head-in-one-file-does-not-hide-a-wrong-call-to-a-same-named-function-of-another -->
`a.maxon` loses its `function` keyword, so `helper` there is refused. `b.maxon` declares its own `helper`, which takes one
argument, and `main` calls it with none: that call resolves to `b.maxon`'s declaration, not to the refused head, so the
arity error is reported beside the lost head.
```maxon
// --- file: a.maxon
helper() returns ExitCode
	return 0
end 'helper'

// --- file: b.maxon
function helper(n ExitCode) returns ExitCode
	return n
end 'helper'

function main() returns ExitCode
	return helper()
end 'main'
```
```maxoncstderr
error E2001: <fragment>:3:1: Expected function declaration, got 'helper'
error E3036: <fragment>:13:9: 'helper' expects 1 argument(s) but 0 were provided
```

<!-- test: error.a-typealias-of-an-unknown-type-written-at-several-declarations-reports-the-refusal-once -->
`Reading` is refused, so each declaration that names it (a field, a parameter, another function's parameter) is a
consequence of that one refusal and is not an unknown type of its own. `other` stays reported.
```maxon
typealias Reading = Missing

type Sensor
	export var level as Reading

	static function create(level Reading) returns Self
		return Self{level: level}
	end 'create'
end 'Sensor'

function show(r Reading) returns String
	return "{r}"
end 'show'

function other(n ExitCode) returns ExitCode
	return n + "x"
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:21: Unsupported: a typealias over 'identifier' (a typealias names `int(low to high)`, `float(low to high)`, `bits(n)`, `function(...)`, a tuple `(A, B)` or a generic instance `Base with …`)
error E2004: <fragment>:17:11: Cannot operate on ExitCode and String
```

<!-- test: error.a-result-of-a-function-returning-an-unknown-type-reports-only-the-unknown-type -->
`measure` returns a type that does not exist, so its result has no known type and passing it where a `String` is
expected is not a mismatch. `other` stays reported.
```maxon
function measure() returns Missing
	return 3
end 'measure'

function describe(text String) returns String
	return text
end 'describe'

function other(n ExitCode) returns ExitCode
	return n + "x"
end 'other'

function main() returns ExitCode
	let label = describe(measure())
	print(label)
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:2:28: Unknown type 'Missing'
error E2004: <fragment>:11:11: Cannot operate on ExitCode and String
```

<!-- test: error.a-field-of-an-unknown-type-used-in-arithmetic-reports-only-the-unknown-type -->
`reading` has a type that does not exist, so `g.reading + 1` has no known operand type and is not an error of its own.
`other` stays reported.
```maxon
typealias Count = int(0 to 1000)

type Gauge
	export var reading as Missing
	export var floor as Count

	static function create(floor Count) returns Self
		return Self{reading: 0, floor: floor}
	end 'create'
end 'Gauge'

function label(g Gauge) returns Count
	let c = g.reading + 1
	return g.floor
end 'label'

function other(n ExitCode) returns ExitCode
	return n + "x"
end 'other'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:5:24: Unknown type 'Missing'
error E2004: <fragment>:19:11: Cannot operate on ExitCode and String
```

<!-- test: error.a-diagnostic-recorded-inside-one-method-hides-no-independent-error-on-its-type -->
The reused label in `Counter.make` is reported without losing any fact about `Counter`, so an undefined static
member and a wrong-arity call to another method of `Counter` are both reported beside it.
```maxon
typealias Count = int(0 to 1000)

type Counter
	export var n as Count

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	static function make(seed Count) returns Self
		if seed > 1 'outer'
			if seed > 2 'outer'
				return Self{n: 2}
			end 'outer'
		end 'outer'

		return Self{n: 1}
	end 'make'

	function bump(by Count) returns Count
		return self.n + by
	end 'bump'
end 'Counter'

function main() returns ExitCode
	let a = Counter.nothere(1)
	let c = Counter.create()
	let d = c.bump(1, by: 2)
	return d as ExitCode
end 'main'
```
```maxoncstderr
error E2081: <fragment>:13:16: Block label 'outer' is already the label of an enclosing block or declaration (line 12, column 15); an `end 'outer'` could close either, so rename the inner block's label
error E3004: <fragment>:27:18: call to undefined function 'Counter.nothere'
error E3036: <fragment>:29:12: 'Counter.bump' expects 2 argument(s) but 3 were provided
```

<!-- test: error.a-refused-member-typealias-after-a-method-refuses-the-whole-type -->
A member typealias is part of the type, not of the method written above it, so its unknown type refuses `Holder` and
the read of a missing field of `Holder` is a consequence of that refusal. `other` stays reported.
```maxon
type Holder
	export let flag as ExitCode

	export function value() returns ExitCode
		return self.flag
	end 'value'

	typealias Bad = Array with Nowhere

	static function create() returns Holder
		return Holder{flag: 1}
	end 'create'
end 'Holder'

function other() returns ExitCode
	let broken = "text"
	return broken + 1
end 'other'

function main() returns ExitCode
	let h = Holder.create()
	return h.flag + h.absent
end 'main'
```
```maxoncstderr
error E3011: <fragment>:9:29: Unknown type 'Nowhere'
error E2004: <fragment>:18:16: Cannot operate on String and int
```

<!-- test: error.a-refused-extension-supplying-a-required-method-reports-only-the-refusal -->
`Foo`'s only `shifted` is in an extension that is refused, so the missing method the conformance reports is a
consequence of that refusal. `other` stays reported.
```maxon
typealias Digit = int(0 to 9)

interface Shifts
	function shifted(amount Digit) returns Digit
end 'Shifts'

type Foo implements Shifts
	let n as Digit

	static function create() returns Foo
		return Self{n: 1}
	end 'create'
end 'Foo'

extension Foo
	function shifted(amount) returns Digit
		return n
	end 'shifted'
end 'Foo'

function other(n Digit) returns Digit
	return n + "x"
end 'other'

function main() returns ExitCode
	let c = Foo.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:17:19: Unsupported: parameter 'amount' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
error E2004: <fragment>:23:11: Cannot operate on Digit and String
```

<!-- test: error.a-generic-alias-written-only-inside-an-abandoned-body-of-another-file-reports-only-the-syntax-error -->
`collect` spells `Tallies` after its syntax error, so the body is abandoned before the name is read. The spelling that
was lost is not evidence that `Tallies` is unused. The undefined call in `main` stays reported.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Tallies = Array with Tally

// --- file: main.maxon
function collect() returns ExitCode
	let broken = )
	let xs = Tallies.create(0)
	return 0
end 'collect'

function main() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:15: Expected expression but got ')'
error E3004: <fragment>:14:10: call to undefined function 'nothere'
```

<!-- test: error.a-generic-alias-written-only-in-a-file-that-fails-outright-reports-only-that-failure -->
A syntax error inside a generic body fails its whole file, so every name that file spells is lost with it. `larger`
spells `Tallies` after its syntax error, and the lost spelling is not evidence that `Tallies` is unused. The undefined
call in `main.maxon` stays reported.
```maxon
// --- file: a.maxon
typealias Tally = int(0 to 1000)
typealias Tallies = Array with Tally

// --- file: b.maxon
function larger(a T, b T) uses T returns T where T is Comparable
	let broken = )
	let xs = Tallies.create(0)
	return a
end 'larger'

function collect() returns ExitCode
	return larger(1, b: 2) as ExitCode
end 'collect'

// --- file: main.maxon
function main() returns ExitCode
	let g = nothere(7)
	return g as ExitCode
end 'main'
```
```maxoncstderr
error E2004: <fragment>:8:15: Expected expression but got ')'
error E3004: <fragment>:19:10: call to undefined function 'nothere'
```

<!-- test: error.a-closure-handed-to-a-refused-function-type-alias-reports-only-the-refusal -->
`NamedFn` is refused because a function type may not take an interface-typed parameter, so the closure given to
`apply` has no known parameter type: its `item.label()` is not a member access on an `int`. `other` stays reported.
```maxon
interface Named
	function label() returns String
end 'Named'

type Thing implements Named
	static function create() returns Self
		return Self{}
	end 'create'

	function label() returns String
		return "thing"
	end 'label'
end 'Thing'

typealias NamedFn = function(Named) returns Count

function apply(f NamedFn, item Named) returns Count
	return f(item)
end 'apply'

function other(n ExitCode) returns ExitCode
	return n + "x"
end 'other'

function main() returns ExitCode
	let measured = apply(function(item) gives item.label().byteLength(), item: Thing.create())
	print("{measured}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:16:30: Unsupported: a function type's parameter declared at the interface type 'Named' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a function value is called indirectly, through a call that reserves no adjacent slot for the witness half. Declare the parameter at a concrete type, or pass the interface to a named function DIRECTLY, whose signature reserves the adjacent slot
error E2004: <fragment>:23:11: Cannot operate on ExitCode and String
```

<!-- test: error.an-unneeded-cast-beside-an-independent-error-in-another-function-reports-only-the-error -->
An unneeded cast is a secondary finding: it is held while any other error stands, and reported once none does (a lone
one is pinned by `type-casting`'s `error.unneeded.same-type-int`).
```maxon
typealias Score = int(0 to 100)

function passMark() returns Score
	let base = 50 as Score
	return base
end 'passMark'

function broken() returns Score
	return nothere(7)
end 'broken'

function main() returns ExitCode
	return (passMark() + broken()) as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:10:9: call to undefined function 'nothere'
```

<!-- test: error.constants-initialized-by-a-match-are-each-refused -->
A `match` expression is not a constant expression, so a constant whose initializer holds one is refused, and every such constant in the file is reported, the second as well as the first.

```maxon
let first = (match 2 'pickFirst'
	1 gives 10
	2 gives 20
	default gives 0
end 'pickFirst')

let second = (match 3 'pickSecond'
	1 gives 11
	3 gives 33
	default gives 0
end 'pickSecond')

function main() returns ExitCode
	print("{first} {second}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2045: <fragment>:2:13: Global initializer for 'first' is not a constant expression: '(match 2 'pickFirst'newline 1 gives 10newline 2 gives 20newline default gives 0newline end 'pickFirst')' cannot be evaluated at compile time
error E2045: <fragment>:8:14: Global initializer for 'second' is not a constant expression: '(match 3 'pickSecond'newline 1 gives 11newline 3 gives 33newline default gives 0newline end 'pickSecond')' cannot be evaluated at compile time
```

<!-- test: error.a-constant-refused-for-an-unknown-type-leaves-another-files-use-of-it-unreported -->
A constant whose initializer names a type that does not exist is refused where it is written. Another file's
read of it is that refusal's consequence, and is no second error.
```maxon
// --- file: app/first.maxon
export let n = 5 as Nope

// --- file: app/main.maxon
function main() returns ExitCode
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: app/<fragment>:3:18: Unknown type 'Nope'
```
