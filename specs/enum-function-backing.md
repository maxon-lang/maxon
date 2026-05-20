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
// --- file: ops.maxon
export typealias Integer = int(i64.min to i64.max)

export function doubleFn(x Integer) returns Integer
	return x * 2
end 'doubleFn'

export function tripleFn(x Integer) returns Integer
	return x * 3
end 'tripleFn'

// --- file: dispatch.maxon
export enum Op
	doubleOp = doubleFn
	tripleOp = tripleFn
end 'Op'

// --- file: main.maxon
function main() returns ExitCode
	let f = Op.tripleOp.rawValue
	return f(14)
end 'main'
```
```exitcode
42
```
