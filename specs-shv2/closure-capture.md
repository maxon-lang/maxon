---
feature: closure-capture
status: experimental
keywords: [closure, capture, environment, gives]
category: functions
---
# Closure Variable Capture

## Documentation

Closures can capture variables from their enclosing scope. When a closure references a variable that is not one of its parameters, the variable is captured by reference.

```text
var offset = 10
var f = function(x int) gives x + offset
```

Because captures are by reference, the closure always sees the current value of the captured variable, even if it changes after the closure is created.

This is especially useful with higher-order functions like `map`:

```text
var multiplier = 3
var results = numbers.map(function(x) gives x * multiplier)
```

Use `_` as a parameter name to ignore the parameter:

```text
var values = items.map(function(_) gives defaultValue)
```

## Tests

<!-- test: closure-capture.basic -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let offset = 7
	let result = apply(function(n Integer) gives n + offset, x: 10)
	return result
end 'main'
```
```exitcode
17
```

<!-- test: closure-capture.ignore-param -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let value = 42
	let result = apply(function(_ Integer) gives value, x: 99)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.struct-field -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

type Level
	export var rawValue as Integer

	static function create(rawValue Integer) returns Self
		return Self{rawValue: rawValue}
	end 'create'
end 'Level'

function main() returns ExitCode
	let level = Level.create(5)
	let result = apply(function(_ Integer) gives level.rawValue, x: 0)
	return result
end 'main'
```
```exitcode
5
```

<!-- disabled-test: closure-capture.map-with-capture -->
<!-- P1.7 (Array) -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Level
	export var rawValue as Integer

	static function create(rawValue Integer) returns Self
		return Self{rawValue: rawValue}
	end 'create'
end 'Level'

function main() returns ExitCode
	let level = Level.create(5)
	let arr = [1, 2, 3]
	let result = arr.map(function(_ Integer) gives level.rawValue)
	return result.count()
end 'main'
```
```exitcode
3
```

<!-- test: closure-capture.multiple-captures -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let a = 10
	let b = 20
	let result = apply(function(x Integer) gives x + a + b, x: 5)
	return result
end 'main'
```
```exitcode
35
```

<!-- test: closure-capture.no-capture-regression -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(function(n Integer) gives n * 3, x: 10)
	return result
end 'main'
```
```exitcode
30
```

<!-- test: closure-capture.capture-string -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns String
function apply(f FnTypeAlias1, x Integer) returns String
	return f(x)
end 'apply'

function main() returns ExitCode
	let prefix = "hello"
	let result = apply(function(_ Integer) gives prefix, x: 0)
	print(result)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```


<!-- disabled-test: closure-capture.interface-method-with-captured-field -->
<!-- P1.7a (interfaces) -->
A closure declared inside an interface-conforming method body that captures
a `let`-bound copy of a self-field. The method `Box.greet()` is the
interface-witness target for `Greeter.greet`, so the call ABI carries the
boxed self pointer; the inner closure receives an env containing the
captured local `myv` (a copy of `self.v`). Historically the self-hosted
x64 backend's regalloc panicked here with
`colorLookupGpr: vreg v0 in func=Box.greet … NO live range was built for v0`
— a `mov-arg` for the closure's call-arg setup referenced a value the
backend hadn't defined, because the env-pointer arg slot wasn't being
registered alongside the captured-value arg. Compiling at all confirms
the regalloc allocates a live range for the env pointer's arg setup.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Box implements Greeter
	var v as Integer

	static function make(v Integer) returns Self
		return Self{v: v}
	end 'make'

	function greet() returns Integer
		let myv = v
		let adder = function(x Integer) gives x + myv
		return adder(10)
	end 'greet'
end 'Box'

function main() returns ExitCode
	let m = Box.make(5)
	return m.greet()
end 'main'
```
```exitcode
15
```

<!-- disabled-test: closure-capture.block-local-overload-arg -->
<!-- P1.7 (Array) -->
A `let` declared inside a `while`-loop body — a NESTED block scope — captured
into a closure whose body passes it as an argument to an OVERLOADED function
(`earliest(LiveRange)` vs `earliest(SlotRange)`). The block frame is popped
once the loop finishes parsing, removing the local from the enclosing
function's `Scope`. Historically the capture-type patch and the env-block
build both re-derived the captured slot by NAME from that pruned scope, so the
patch returned `unresolved` and overload resolution reported E3007 ("ambiguous
overload"). The captured slot id is now recorded on the `closureCreate` op at
parse time, so both passes resolve the concrete `LiveRange` type and the
overload disambiguates. Returns `7 + 10`.
```maxon
typealias ValId = int(0 to u64.max)
typealias IntThunk = function() returns ExitCode
typealias LRArray = Array with LiveRange

type LiveRange
	export var valueId as ValId

	export static function create(v ValId) returns LiveRange
		return Self{valueId: v}
	end 'create'
end 'LiveRange'

type SlotRange
	export var off as ValId

	export static function create(o ValId) returns SlotRange
		return Self{off: o}
	end 'create'
end 'SlotRange'

function earliest(r LiveRange) returns ExitCode
	return r.valueId
end 'earliest'

function earliest(r SlotRange) returns ExitCode
	return r.off
end 'earliest'

function callThunk(t IntThunk) returns ExitCode
	return t()
end 'callThunk'

function allocate(ranges LRArray) returns ExitCode
	var total = 0
	var oi = 0
	while oi < ranges.count() 'assign'
		let range = try ranges.get(oi) otherwise panic("oob")
		total = total + callThunk(function() gives earliest(range))
		oi = oi + 1
	end 'assign'
	return total
end 'allocate'

function main() returns ExitCode
	var arr = LRArray.create()
	arr.push(LiveRange.create(7))
	arr.push(LiveRange.create(10))
	return allocate(arr)
end 'main'
```
```exitcode
17
```

<!-- test: closure-capture.block-local-method-receiver -->
<!-- targets: x64-windows -->
<!-- wasm32-wasi OMITTED: the closure returns `ExitCode` (via `b.doubled() returns ExitCode`), and an ExitCode-returning function VALUE traps on wasm — a PRE-EXISTING A1 limitation, closure-INDEPENDENT and NOT the method-on-captured-struct mechanism (the same shape with `Integer` returns runs clean on wasm). `ExitCode` lowers to u32/i32 (valueTagToStdType), but the wasm `call_indirect` functype is ARG-COUNT-derived to `(i64^n) -> i64` (A1's "every fn value is (i64ⁿ)→i64" assumption), so a `(i64)->i32` closure mismatches `(i64)->i64` and wasmtime traps "indirect call type mismatch". Fixing it needs the callIndirect to carry its RESULT width to the wasm tier — an A1-design change, its own follow-up rung. The method-on-captured-struct mechanism itself is target-neutral and covered on x64 here. -->
A `let` declared inside an `if`-block body — a nested block scope — captured
into a closure whose body calls a METHOD on it (`b.doubled()`). Historically a
method call on a captured outer receiver fell through `parseIdentifierExpr`'s
dot-call handling to the qualified-static-call arm (treating `b` as a type
name), which never recorded the capture: the outer `let b` tripped E3012
("unused variable") and, with that silenced, lowering panicked because the
captured name was absent from the popped outer scope. The dot-call path now
captures an outer receiver before the static-call fallback. Returns
`doubled(9) = 18`.
```maxon
typealias IntThunk = function() returns ExitCode

type Box
	export var n as ExitCode

	export static function create(n ExitCode) returns Box
		return Self{n: n}
	end 'create'

	export function doubled() returns ExitCode
		return self.n + self.n
	end 'doubled'
end 'Box'

function callThunk(t IntThunk) returns ExitCode
	return t()
end 'callThunk'

function run(seed ExitCode) returns ExitCode
	if seed > 0 'pos'
		let b = Box.create(seed)
		return callThunk(function() gives b.doubled())
	end 'pos'
	return 0
end 'run'

function main() returns ExitCode
	return run(9)
end 'main'
```
```exitcode
18
```

### Closure body that is a bare string literal

<!-- test: closure-capture.string-literal-body -->
<!-- targets: x64-windows, wasm32-wasi -->
```maxon

typealias Msg = function(Integer) returns String
typealias Integer = int(i64.min to i64.max)

function apply(f Msg, x Integer) returns String
	return f(x)
end 'apply'

// The closure body is a bare string literal — not a capture. The literal comes
// back as a deferred `stringConst` (no backing op), so the lifted closure's
// `ret` referenced an unbound value until the body materializes it. The closure
// captures nothing; it just returns "hi".
function main() returns ExitCode
	let s = apply(function(_ Integer) gives "hi", x: 0)
	return s.byteLength()
end 'main'
```
```exitcode
2
```
