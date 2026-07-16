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
error E2045: specs/fragments/top-level-let/function-call-in-constant-error.test:8:14: Function calls are not allowed in global variable initializers; 'compute()' is not a constant expression
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
error E2012: specs/fragments/top-level-let/circular-dependency-error.test:3:5: Circular dependency detected among global constants: A, B
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
files. Each participating file folds its own constants, so each reports the cycle it is in, naming
the file that closes it from that file's side. Before constant resolution became order-independent
this was an `E2004 Undefined constant` naming one arbitrary participant — whichever file the
filesystem happened to hand over second — because the cycle guard was never reached at all.
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
error E2012: api/specs/fragments/top-level-let/error.circular-dependency-cross-file.test:10:12: Circular dependency detected among global constants: A, B
error E2012: app/specs/fragments/top-level-let/error.circular-dependency-cross-file.test:3:12: Circular dependency detected among global constants: B, A
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
error E2004: app/specs/fragments/top-level-let/error.file-private-constant-cross-file.test:10:12: Undefined constant 'SECRET'
```
