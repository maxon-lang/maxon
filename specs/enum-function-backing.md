---
feature: enum-function-backing
status: experimental
keywords: [enum, function, backing, rawValue, dispatch]
category: type-system
---

# Enum Function Backing

## Documentation

Enums can use function references as backing values. Each case carries a compile-time function pointer accessible via `.rawValue`, which can then be called like any function reference. At runtime, the enum is stored as an ordinal (i64); `.rawValue` lowers to a select chain that recovers the function pointer for the live case.

All cases must share the same function signature. The signature becomes the backing type for the enum.

```text
typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

enum Op
	doubleOp = double
	tripleOp = triple
end 'Op'
```

## Tests

<!-- test: function-backing.basic-dispatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function main() returns ExitCode
	let f = Op.doubleOp.rawValue
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: function-backing.multi-case-dispatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function main() returns ExitCode
	let dFn = Op.doubleOp.rawValue
	let tFn = Op.tripleOp.rawValue
	return dFn(5) + tFn(10)
end 'main'
```
```exitcode
40
```

<!-- test: function-backing.through-variable -->
```maxon

typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function apply(op Op, x Integer) returns Integer
	let f = op.rawValue
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(Op.tripleOp, x: 14)
end 'main'
```
```exitcode
42
```

<!-- test: function-backing.two-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

function addFn(a Integer, b Integer) returns Integer
	return a + b
end 'addFn'

function subFn(a Integer, b Integer) returns Integer
	return a - b
end 'subFn'

enum BinOp
	add = addFn
	sub = subFn
end 'BinOp'

function main() returns ExitCode
	let addF = BinOp.add.rawValue
	let subF = BinOp.sub.rawValue
	return addF(30, 15) - subF(5, 2)
end 'main'
```
```exitcode
42
```

<!-- test: function-backing.forward-reference -->
Function-backed enum cases may name functions declared later in the same file.
The case binding is deferred until all top-level declarations are scanned.

```maxon

typealias Integer = int(i64.min to i64.max)

enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

function main() returns ExitCode
	let f = Op.doubleOp.rawValue
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: function-backing.cross-file -->
Function-backed enums may reference functions defined in other files.

```maxon
// --- file: api/ops.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

export function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

// --- file: api/dispatch.maxon
export enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = Op.tripleOp.rawValue
	return f(14)
end 'main'
```
```exitcode
42
```

<!-- test: function-backing.contested-name-resolves-at-the-declaring-file -->
A case's function name is resolved where the ENUM is declared, under the bare-call rule: `doubleFn` is
declared in `alpha/` (file-private) and in `beta/` (exported), so from `api/dispatch.maxon` it means
beta's. The read happens inside `alpha/a.maxon`, where alpha's own `doubleFn` is visible — so a name
resolved at the reader would answer 30 + 3 instead of 20 + 3.

```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
	return x * 3
end 'doubleFn'

export function viaEnum() returns Integer
	let f = Op.doubleOp.rawValue
	return f(10) + doubleFn(1)
end 'viaEnum'

// --- file: beta/ops.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

// --- file: api/dispatch.maxon
export enum Op
	doubleOp = doubleFn
end 'Op'

// --- file: app/main.maxon
function main() returns ExitCode
	return viaEnum() as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: function-backing.error.contested-name-with-two-visible-candidates -->
Two visible declarations of the case's function name are an ambiguity at the enum's declaration, and
the case is refused rather than bound to either.

```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 3
end 'doubleFn'

// --- file: beta/ops.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

// --- file: api/dispatch.maxon
export enum Op
	doubleOp = doubleFn
end 'Op'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = Op.doubleOp.rawValue
	return f(10) as ExitCode
end 'main'
```
```maxoncstderr
error E3095: api/<fragment>:18:13: Ambiguous bare function name 'doubleFn' backing an enum case: multiple visible definitions found. Qualify with a directory name. Candidates: alpha.doubleFn, beta.doubleFn
```

<!-- test: function-backing.contested-name-qualified-by-its-directory -->
The remedy E3095 names is writable in the declaration: a directory-qualified function name backs the case with
the declaration that directory holds.

```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 3
end 'doubleFn'

// --- file: beta/ops.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

// --- file: api/dispatch.maxon
export enum Op
	doubleOp = beta.doubleFn
	tripleOp = alpha.doubleFn
end 'Op'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = Op.doubleOp.rawValue
	let g = Op.tripleOp.rawValue
	return (f(10) + g(1)) as ExitCode
end 'main'
```
```exitcode
23
```

<!-- test: function-backing.error.a-throwing-backing-function-is-refused-by-the-name-it-was-written-as -->
The case's refusal quotes the function name the declaration wrote, though it resolves to beta's declaration.

```maxon
// --- file: alpha/a.maxon
export typealias Integer = int(i64.min to i64.max)

function doubleFn(x Integer) returns Integer
	return x * 3
end 'doubleFn'

export function viaAlpha() returns Integer
	return doubleFn(1)
end 'viaAlpha'

// --- file: beta/ops.maxon
export typealias Integer = int(i64.min to i64.max)

export enum OpError implements Error
	overflow
end 'OpError'

export function doubleFn(x Integer) returns Integer throws OpError
	if x > 1000 'tooBig'
		throw OpError.overflow
	end 'tooBig'

	return x * 2
end 'doubleFn'

// --- file: api/dispatch.maxon
export enum Op
	doubleOp = doubleFn
end 'Op'

// --- file: app/main.maxon
function main() returns ExitCode
	let f = Op.doubleOp.rawValue
	return (f(10) + viaAlpha()) as ExitCode
end 'main'
```
```maxoncstderr
error E3101: api/<fragment>:30:2: Cannot use throwing function 'doubleFn' as a value: it throws 'OpError', and a function type cannot express 'throws'. Wrap the call in a non-throwing function that handles the error with 'try'.
```

<!-- test: function-backing.error.a-type-method-does-not-back-a-case -->
A dotted name backs a case only where it would read as a function value: `beta.doubleFn` names a function in a
directory, and `Box.make` names a type's method, which `let f = Box.make` does not read as one either.

```maxon
type Box
	export var n as Integer

	static function make(value Integer) returns Integer
		return value * 2
	end 'make'
end 'Box'

enum Op
	double = Box.make
end 'Op'

function main() returns ExitCode
	let f = Op.double.rawValue
	print("{f(21)}\n")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2004: <fragment>:11:11: Undefined variable 'Box.make'
```
