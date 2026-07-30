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
<!-- `Array.map` — not in shv2's synthesized Array method roster, so `map(…)` resolves to no function (E2004 "Function 'map' does not return a value") -->
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
<!-- function overloading — shv2 keys a function by its bare NAME, so the two `earliest` declarations collide (E3006 duplicate definition) -->
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

### A closure literal is an expression, not a declaration

`function` opens a DECLARATION only in the three-token shape `function <name> (`. A closure literal
spells the same keyword and declares nothing: it has no name, no `end`, and opens no block of its
own. That distinction is load-bearing outside the parser proper, in the whole-file **declaration
sweep** that runs before any file is parsed: the sweep counts a declaration's block open so it can
tell a type's FIELD (`export var x as Integer` at the type's top level) from a method's local
binding one level down, and every `type` / `enum` / `interface` / top-level `let`-`var` it records
is gated on that counter reading zero.

A closure literal counted as a declaration leaves the counter permanently one too deep, and every
depth-0 gate after it silently stops firing — so the declarations BELOW the closure are never
recorded at all. What the compiler then does is never "compile it anyway": the drift guards that
exist for exactly this mismatch (`requireConstructible`, `ProgramSignatures.recordedDeclFor`) fire
as an internal PANIC on a program that is completely correct. Inside a type body the overshoot is
worse than absent — it runs past the type's own `end` and reads the NEXT declaration's members into
THIS type's layout, so a field of one type is reported missing from another, and the swallowed
type's factory is registered under the swallowing type's name, which types its callers' results
from the wrong signature.

None of the cases below is a diagnostics test. Each is a correct program that must simply compile
and run.

<!-- test: closure-capture.type-declared-after-a-closure-in-a-free-function -->
A closure literal in a FREE function's body, with a `type` declared after it. The sweep must leave
its block depth at zero across the closure, or `type Box` is never recorded and `Self{…}` panics.
```maxon

typealias Integer = int(i64.min to i64.max)

function bumped(n Integer) returns Integer
	let step = function(k Integer) gives k + 1
	return step(n)
end 'bumped'

type Box
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(40)
	return b.v + bumped(1)
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.type-declared-after-a-closure-bearing-type -->
The same overshoot one level in: the closure sits in a METHOD body, so the sweep runs past `type
Counter`'s `end` and consumes `type Box` as though it were more of `Counter`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function bumped(n Integer) returns Integer
		let step = function(k Integer) gives k + 1
		return step(n)
	end 'bumped'
end 'Counter'

type Box
	export var v as Integer

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create(40)
	return b.v + Counter.bumped(1)
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.field-declared-after-a-closure-bearing-method -->
A field declared BELOW a method that contains a closure literal. The overshoot puts the field one
level too deep for the sweep's depth-0 field gate, so it is dropped from the layout and every use of
it is refused as "no field named …" — a wrong answer about a field that is right there.
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair
	export var first as Integer

	static function make(seed Integer) returns Self
		let step = function(k Integer) gives k + 1
		return Self{first: step(seed), second: 2}
	end 'make'

	export var second as Integer
end 'Pair'

function main() returns ExitCode
	let p = Pair.make(39)
	return p.first * p.second
end 'main'
```
```exitcode
80
```

<!-- test: closure-capture.top-level-binding-after-a-closure -->
A top-level `let` and `var` declared after a closure literal. Both are recorded only at depth zero,
so both go missing — and `dispatchTopLevel` then meets a binding the sweep never saw.
```maxon

typealias Integer = int(i64.min to i64.max)

function bumped(n Integer) returns Integer
	let step = function(k Integer) gives k + 1
	return step(n)
end 'bumped'

let Base = 20
var offset = 21

function main() returns ExitCode
	offset = offset + 1
	return Base + offset + bumped(-1)
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.enum-declared-after-a-closure -->
An `enum` declared after a closure literal — the third depth-0 gate, and the one whose failure is a
parse-level diagnostic (an unrecorded `Color.green` is read as a ranged-alias bound) rather than a
panic.
```maxon

typealias Integer = int(i64.min to i64.max)

function bumped(n Integer) returns Integer
	let step = function(k Integer) gives k + 1
	return step(n)
end 'bumped'

enum Color
	red
	green
end 'Color'

function shade(c Color) returns Integer
	return match c 'shade'
		red gives 1
		green gives 41
	end 'shade'
end 'shade'

function main() returns ExitCode
	return shade(Color.green) + bumped(0)
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.bare-self-field-refused -->
A closure body naming a field of the enclosing type by its BARE name is a capture of `self`, and is refused
in the same words — through the same wire format — the `self.n` spelling already was.

It used to be captured as if it were an ordinary enclosing local. A self-field alias holds NO SSA value
(`VarInfo.createSelfField` leaves `boundValue` 0) and **0 is the enclosing method's receiver**, so the env
slot stored the receiver's BOX and the closure handed it back as the field: this program returned **25** —
the box pointer's low byte — where the answer is 4, with no diagnostic anywhere. Declaring the field
through a ranged alias instead PANICKED in lowering, because the env slot's type is filled from the
binding at parse time and a `named` reaches `maxonTypeToStdType` unresolved. One defect, two faces.
```maxon

typealias Fn1 = function(int) returns int

function apply(f Fn1, x int) returns int
	return f(x)
end 'apply'

type Counter
	export var n as int

	static function create() returns Counter
		return Self{n: 3}
	end 'create'

	export function via() returns int
		return apply(function(k int) gives k + n, x: 1)
	end 'via'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	return c.via() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:17:42: Unsupported: a closure that captures `self` (P1.5-A2b: capturing closures + env block)
```

<!-- test: closure-capture.captured-ranged-alias-binding -->
A capture whose declared type is a ranged typealias — where what the slot is ACCESSED as and what the
value IS are not the same type. The env slot's `storeIndirect`/`loadIndirect` pair carries a WIDTH, so
the op's type must be the concrete primitive the alias stands for: a `named` type reaches lowering
unresolved, because nothing after the parser rewrites an op's type. The value the read mints keeps the
DECLARED type instead, and `high shr 62` is the witness — `Word`'s low bound is 0, so the shift
zero-fills and yields 3. Minted at the storage type it would be a bare `int`, sign-fill, and yield
0 - 1.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Word = int(0 to u64.max)

function usesClosure(bump Integer, high Word) returns Integer
	let op = function(n Integer) gives n + bump + (high shr 62)
	return op(1)
end 'usesClosure'

function main() returns ExitCode
	return usesClosure(38, high: 0xC000000000000000) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.capture-exitcode-from-alias-call -->
The result of an INDIRECT call whose function alias returns `ExitCode`, captured into a closure. This
is the storage half of the width claim `first-class-function.alias-returns-exitcode` pins: once the
alias's return is genuinely `MaxonType.exitCode`, the call's SSA result is `exitCode`-tagged, and an
`exitCode` lowers to `StdType.u32` — for which `accessWidthFor` has no memory-access form and panics on
every backend. An env slot is `EnvSlotBytes` wide by construction, so a scalar capture owns a whole
machine word regardless of its declared width; `envSlotStorageType` is the one door the store and the
read both ask, and it gives an `exitCode` capture that word. Zero-extension is unambiguous because the
value is unsigned — the same reason `accessWidthFor` already admits `u8`. Returns `9`.
```maxon
typealias IntThunk = function() returns ExitCode

function nine() returns ExitCode
	return 9
end 'nine'

function useIt(t IntThunk) returns ExitCode
	let v = t()
	let f = function() gives v
	return f()
end 'useIt'

function main() returns ExitCode
	return useIt(nine)
end 'main'
```
```exitcode
9
```

<!-- test: closure-capture.capture-exitcode-from-interface-method -->
The PRE-EXISTING twin of the case above, and the reason the fix belongs at the env slot rather than at
either producing door. `Parser.interfaceReturnMaxonType` has minted `exitCode`-tagged call results
since P1.7a, so an interface method declared `returns ExitCode` whose result is captured hit the same
`accessWidthFor` panic on every target long before a function alias could produce one — it simply had
no committed case. Two entrances, one sink: fixing the sink closes both, and fixing either door would
have left the other reachable. Returns `9`.
```maxon
typealias Integer = int(0 to u64.max)

interface Coded
	function code() returns ExitCode
end 'Coded'

type Box implements Coded
	let n as Integer

	function code() returns ExitCode
		return n
	end 'code'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Wrap uses T where T is Coded
	let item as T

	function run() returns ExitCode
		let v = self.item.code()
		let f = function() gives v
		return f()
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Wrap'

typealias BoxWrap = Wrap with Box

function main() returns ExitCode
	let w = BoxWrap.create(Box.create(9))
	return w.run()
end 'main'
```
```exitcode
9
```

<!-- test: closure-capture.capture-exitcode-wide-value -->
The WIDTH half of the two cases above, and the reason they are not enough on their own. Both of them
carry the value `9`, which fits in a single byte — so they pass unchanged even if `envSlotStorageType`
hands the slot a 1-byte `boolean` instead of the machine word, on every target, clobbering nothing.
They pin THAT the capture works, not the width it works at. This one carries `100000` through the same
env slot and subtracts `99991` back down to an exit code, so the assertion is load-bearing without
needing `print` (which is Beyond on wasm): truncate the slot to a byte and the captured value becomes
`100000 & 0xFF = 160`, which does not come back to `9`. **Verified by sabotage, and the DIFFERENCE is the point.** Force the slot
to one byte: this case fails on its VALUE (`expected 9, got 4294867465`), which is behaviour and needs
nothing but the exit code. The other two fail only as `codegen changed — golden fragment mismatch`, which
is a real catch but a different one — it needs their fragments to already exist, so it would not have
caught the width at the moment they were authored, and it says "something moved" rather than "the value
is wrong".
⚠ `100000` is deliberately above the byte range and below 2^31, which keeps this case pinning ONE fact.
An `ExitCode` above 2^31 used to read back SIGNED on wasm and unsigned on x64; W1's own review found and
fixed that, and it is re-measured here rather than assumed (`3000000009` prints identically on both
targets today). Pinning the width at that boundary would tie this assertion to the sign fix as well, so
a case that failed would not say which of the two had regressed.
```maxon
typealias IntThunk = function() returns ExitCode

function big() returns ExitCode
	return 100000
end 'big'

function useIt(t IntThunk) returns ExitCode
	let v = t()
	let f = function() gives v
	return f() - 99991
end 'useIt'

function main() returns ExitCode
	return useIt(big)
end 'main'
```
```exitcode
9
```
