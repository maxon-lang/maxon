---
feature: top-level-let
status: stable
keywords: let, global, constant, compile-time
category: language
---
# Top Level Let

## Documentation

## Top-Level Let Declarations

Top-level `let` declarations define compile-time constants at module scope. These constants are evaluated at compile time and stored in the executable's read-only data section.

### Syntax

```maxon
let CONSTANT_NAME = expression
export let EXPORTED_NAME = expression
```

### Features

- **Compile-time evaluation**: Initializers must be constant expressions
- **Type inference**: Type is inferred from the initializer
- **Forward references**: Constants can reference other constants declared later in the file
- **Export support**: Use `export let` to make constants available to other modules

### Constant Expressions

The following are valid in constant expressions:
- Literals: integers, floats, booleans, strings, bytes, characters
- Arithmetic: `+`, `-`, `*`, `/`, `mod`
- Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
- Logical: `and`, `or`, `not`
- Bitwise: `&`, `|`, `^`, `~`, `<<`, `>>`
- Type casts: `as int`, `as float`, `as bool`
- References to other top-level constants
- Array literals: `[1, 2, 3]` (elements must be constant expressions)
- Enum member access: `Color.Red`
- Map literals: `["key": value]` (keys and values must be constant expressions; initialized at runtime)

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

- A call is not a constant expression: an initializer that calls a static factory or a free function
  returning a record runs in `__module_init` before `main`, and any other call is **E2045**
- Map literals are supported, but require runtime initialization
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
error E2004: app/specs/top-level-let/error.file-private-constant-cross-file.maxon:10:12: Undefined constant 'SECRET'
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

<!-- test: cross-file-exported-cast-to-own-private-alias-declared-last -->
An exported constant whose initializer casts to a ranged `typealias` PRIVATE to its own declaring
file folds in the declarer's perspective, resolving that file-private alias — the cast type is
looked up as the declarer would see it, not the demander. Declaring file fed LAST.
```maxon
// --- file: app/main.maxon
let VALUE = BASE

function main() returns ExitCode
	return VALUE
end 'main'

// --- file: api/base.maxon
typealias Small = int(0 to 100)
export let BASE = 21 as Small
```
```exitcode
21
```

<!-- test: cross-file-exported-cast-to-own-private-alias-declared-first -->
The same program with the declaring file fed FIRST — the same executable either way.
```maxon
// --- file: api/base.maxon
typealias Small = int(0 to 100)
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

### Error: A runtime-initialized global must consume everything up to the end of its line

A global whose initializer is not constant-foldable is re-parsed later, out of the token region the
declaration scan marked off, so whatever the expression does not reach must be refused rather than
abandoned — otherwise `var g = Box.create() zzz` would compile, run, and ignore `zzz`. The
interpolation body and the captured parameter default follow the same rule.

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
error E2010: specs/top-level-let/error.runtime-init-trailing-tokens.maxon:12:22: Expected 'end of global initializer' but got 'zzz'
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

	export static function value() returns Integer
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


<!-- test: error.a-builtin-call-past-a-hidden-same-name-declaration-is-not-a-constant -->
Another file declares a file-private `sqrt`; from `main.maxon` the bare call means the math builtin, as a
body call does, so the initializer is refused as a call rather than as a hidden function.
```maxon
// --- file: main.maxon
public typealias Real = float(0.0 to 10.0)
public typealias Digit = int(0 to 9)

let R = sqrt(4.0)

function main() returns ExitCode
	return (R as Digit) as ExitCode
end 'main'

// --- file: a.maxon
function sqrt(x Real) returns Real
	return x
end 'sqrt'

export function useSqrt() returns Real
	return sqrt(1.0)
end 'useSqrt'
```
```maxoncstderr
error E2045: <fragment>:6:9: Function calls are not allowed in global variable initializers; 'sqrt()' is not a constant expression
```

<!-- test: error.a-refused-initializer-call-beside-an-unused-typealias-reports-both -->
A refused initializer call does not hide an unrelated diagnostic in another file.
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
error E3004: <fragment>:3:9: call to undefined function 'nothere'
error E3062: <fragment>:10:11: unused typealias: 'Unused'
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

<!-- test: error.a-static-initializer-calling-a-sibling-instance-method-bare-is-not-a-constant -->
`fresh` is an instance method, so a `static var` initializer calling it bare has no receiver to call it on.
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
error E2045: <fragment>:7:22: Function calls are not allowed in global variable initializers; 'fresh()' is not a constant expression
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

<!-- test: error.a-tuple-assignment-to-a-refused-call-var-reports-both -->
`G`'s initializer is a refused call, and a tuple assignment naming a module `var` is refused whatever
its value: both are reported.
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
error E3004: <fragment>:4:9: call to undefined function 'nothere'
```

<!-- test: error.a-refused-call-inside-a-record-literal-reports-only-the-call -->
The refused call sits inside a record literal; the type names written before it still count as used.
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


<!-- test: error.a-static-initializer-calling-a-function-valued-binding-past-a-sibling-is-not-a-constant -->
`make` is a top-level binding holding a function value a call built; a body call `make()` inside `Box`
calls it, so the initializer means it too and is refused as a call, not folded to the sibling factory.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.make()
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

	static function originOf() returns Digit
		return Box.origin.n
	end 'originOf'
end 'Box'

function main() returns ExitCode
	return Box.originOf() as ExitCode
end 'main'
```
```maxoncstderr
error E2045: <fragment>:18:22: Function calls are not allowed in global variable initializers; 'make()' is not a constant expression
```


<!-- test: error.a-static-initializer-calling-a-function-valued-binding-past-a-sibling-with-a-parameter-is-not-a-constant -->
The sibling `make` takes a `Digit` the argument does not fit, but the call means the function-valued
binding, as a body call would, so the refusal is the call's, not the sibling's parameter's.
```maxon
typealias Digit = int(0 to 9)
typealias Maker = function() returns Box

function other() returns Box
	return Box.make(7 as Digit)
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
error E2045: <fragment>:18:22: Function calls are not allowed in global variable initializers; 'make()' is not a constant expression
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
error E3005: <fragment>:9:27: Value 42 is outside the range of 'Digit' (int(0 to 9))
```
