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

- Function calls are not allowed in constant expressions
- Map literals are supported, but require runtime initialization
- Only immutable `let` is supported at top level (no `var`)

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
	return 0 - NEGATIVE
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
Top-level float constants fold `+`, `*`, and `/` with the host's f64 (oracle-verified: X=3.0, Y=6.0, Z=2.5). Native targets only: the exit code 362 exceeds the 8-bit WASI exit-code range (362 mod 256 = 106), a wasm limitation orthogonal to the fold — a bare-integer `return 362` wraps identically. The wasm suite still exercises float const folding via the three sibling tests below, whose codes are <= 255.
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
A mixed int/float constant promotes the int operand to f64 before folding, exactly as the runtime path does (oracle-verified: M=3.0, N=7.5).
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
Float subtraction and a negated float literal fold (oracle-verified: A=6.5, B=-2.0, C=4.5).
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
Float comparisons fold with a real f64 compare, so a negative operand orders correctly where an integer compare over the raw bit patterns would answer backwards (oracle-verified: both true).
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

<!-- disabled-test: from-literal-initializer -->
<!-- P1.2 String — `FilePath from "literal"`, `print` and `.toString()` -->
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
