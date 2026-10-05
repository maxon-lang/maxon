---
feature: closure-capture
status: experimental
keywords: [closure, capture, environment, gives]
category: functions
---
# Closure Variable Capture

## Documentation

Closures can capture variables from their enclosing scope. When a closure references a variable that is not one of its parameters, the closure captures it when the closure is created:

- **A managed local MOVES into the closure.** The closure owns it from then on, and a read of the local
  after the literal is a use after move (E3102).
- **A scalar is COPIED.** The closure keeps the value it saw; its writes do not reach the outer binding,
  and the outer binding stays readable and writable.
- **A parameter, `self`, a field or a borrower is RETAINED.** The closure holds a counted reference of its
  own, and the function keeps reading it.
- **A captured promise moves**, like any other managed local.

```text
var offset = 10
var f = function(x int) gives x + offset
```

Nested closures capture through their enclosing closure, and a value held at an interface type is captured
with its witness. Passing a captured name by reference inside a closure is E3019.

A function value is one word: the address of a reference-counted closure record holding its code and its
captures. A plain function is an immortal static record. The closure owns its environment, so it may be
returned, stored in a global, a field, a container or a union payload, and passed to `async`, which moves
it into the coroutine. Copies share: `var g = f` and `f.clone()` take another reference to the same record.

A closure stored into something it captures would form a reference cycle no drop can release, so a
whole-program check refuses it (E3183). The check follows what the compiler can see; aliasing that only
exists at run time is beyond it.

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

<!-- test: closure-capture.map-with-capture -->
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


<!-- test: closure-capture.interface-method-with-captured-field -->
A closure declared inside an interface-conforming method body that captures
a `let`-bound copy of a self-field. The method `Box.greet()` is the
interface-witness target for `Greeter.greet`, so the call ABI carries the
boxed self pointer; the inner closure's record holds the captured local
`myv` (a copy of `self.v`), and the call hands that record to the closure as
its environment argument, so the closure's call-arg setup references only
values the backend defined. Compiling at all confirms the regalloc allocates
a live range for the environment argument's setup.
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

<!-- test: closure-capture.block-local-overload-arg -->
A `let` declared inside a `while`-loop body — a NESTED block scope — captured
into a closure whose body passes it as an argument to an OVERLOADED function
(`earliest(LiveRange)` vs `earliest(SlotRange)`). The block frame is popped
once the loop finishes parsing, removing the local from the enclosing
function's `Scope`. The capture's type is recorded with its slot when the
closure body names it, while the loop's scope is still open, so building the
closure record resolves the concrete `LiveRange` type without looking the name
up in that pruned scope, and the overload disambiguates. Returns `7 + 10`.
```maxon
typealias ValId = int(0 to u64.max)
typealias IntThunk = function() returns ExitCode
typealias LRArray = Array with LiveRange

type LiveRange
	export var valueId as ValId

	static function create(v ValId) returns LiveRange
		return Self{valueId: v}
	end 'create'
end 'LiveRange'

type SlotRange
	export var off as ValId

	static function create(o ValId) returns SlotRange
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
into a closure whose body calls a METHOD on it (`b.doubled()`).
The parser's dot-call path captures an outer receiver before its
qualified-static-call fallback (which would treat `b` as a type name and record
no capture), so the capture is recorded although the outer scope has been
popped. Returns `doubled(9) = 18`.
```maxon
typealias IntThunk = function() returns ExitCode

type Box
	export var n as ExitCode

	static function create(n ExitCode) returns Box
		return Self{n: n}
	end 'create'

	function doubled() returns ExitCode
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

<!-- test: closure-capture.bare-self-field-captured -->
A closure body naming a field of the enclosing type by its BARE name is a capture of `self`: it CAPTURES
THE RECEIVER and reads the field through it. **The answer is 4.**

⭐ **A self-field alias holds NO SSA value** (`VarInfo.createSelfField` leaves `boundValue` 0), and **0 is
the enclosing method's receiver** — so capturing the alias as an ordinary enclosing local would store the
receiver's BOX in the env slot and hand it back as the field: **25**, the box pointer's low byte, with no
diagnostic anywhere. The capture machinery keys on `baseBindingName`, under which the receiver — bound as
`__self`, which **no token spells** — is an ordinary capture. `closure-self.md` requires exactly this shape
to compile and gives the bare-name form its own case.

**This case fails if the env slot ever hands back the receiver's box** — that reads as **25**, not as a
missing diagnostic. Its sibling in `closure-self.md` cannot replace it, because that spec must spell its
fields through a ranged alias and this one deliberately uses bare `int`.
```maxon

typealias Fn1 = function(int) returns Integer

function apply(f Fn1, x Integer) returns Integer
	return f(x)
end 'apply'

type Counter
	export var n as Integer

	static function create() returns Counter
		return Self{n: 3}
	end 'create'

	function via() returns Integer
		return apply(function(k Integer) gives k + n, x: 1)
	end 'via'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	return c.via() as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
4
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
typealias Word = bits(64)

function usesClosure(bump Integer, high Word) returns Integer
	let op = function(n Integer) gives n + bump + ((high shr 62) as Integer)
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
machine word regardless of its declared width; `wordSlotStorageType` is the one door the store and the
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
The twin of the case above through the other producing door, and the reason the width is decided at the
env slot rather than at either door. `Parser.interfaceReturnMaxonType` mints `exitCode`-tagged call
results, so an interface method declared `returns ExitCode` whose result is captured reaches the same
`accessWidthFor` sink as a function alias. Two entrances, one sink: the sink covers both, and a rule at
either door would leave the other reachable. Returns `9`.
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
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⚠ **A WINDOWS-LANE READING.** `return 100000` is E3005 on every other target — `ExitCode`
is `int(0 to 255)` there — so those lanes cannot express this program, which is what the
`unsupported-targets:` line says. It cannot be re-pinned on wasm through any other type, and the reason it cannot (plus
the array-element route that looks like a substitute and measurably is not) is stated once, in
`exit-code-range.md`'s *"What the narrowing costs the other lanes"*. ⚠ This one's subject is the ENV SLOT
rather than the widen, and it is unreachable for a second, independent reason worth stating on its own:
`wordSlotStorageType` is a one-tag rule — it widens `exitCode` and passes every other declared type to
`fieldStorageType`, which hands a `named` alias its 8-byte underlying. So `exitCode` is the only tag that
ever arrives at this door narrow, and a user alias substituted here would ride a machine word before the
rule ran. The sabotage this case describes would have nothing to truncate.

The WIDTH half of the two cases above, and the reason they are not enough on their own. Both of them
carry the value `9`, which fits in a single byte — so they pass unchanged even if `wordSlotStorageType`
hands the slot a 1-byte `boolean` instead of the machine word, on every target, clobbering nothing.
They pin THAT the capture works, not the width it works at. This one carries `100000` through the same
env slot and subtracts `99991` back down to an exit code, so the assertion is load-bearing without
needing `print` (which is Beyond on wasm): truncate the slot to a byte and the captured value becomes
`100000 & 0xFF = 160`, which does not come back to `9`. **The DIFFERENCE is the point.** With the slot
forced to one byte this case fails on its VALUE, which is behaviour and needs nothing but the exit code.
⚠ `100000` is deliberately above the byte range and below 2^31, which keeps this case pinning ONE fact.
An `ExitCode` above 2^31 reading back unsigned on every target (`3000000009` prints identically on wasm and
x64) is a separate fact; pinning the width at that boundary would tie this assertion to it as well, so a
case that failed would not say which of the two had regressed.
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

<!-- test: closure-body-restores-the-enclosing-functions-parse-context -->
### A closure body restores every column of the enclosing function's parse context

⛔⛔ **`parseClosureExpression` MUST RESTORE EVERY PER-FUNCTION COLUMN IT RESETS.**
The closure body gets its own `IrFunction` with its own dense SSA numbering, so the parser saves the
enclosing function's context, resets it, parses the body and restores it. The reset list and the
save/restore list are written out separately and NOTHING makes them agree. A column reset and not put back
is a compiler PANIC — not a diagnostic, a crash — on programs anyone writes. The three this case reaches:

  • **`iterationLockedBindings`** — the `for..in` lock STACK. A closure anywhere in a loop body that emptied
    it would leave the loop's own `releaseIterationSource` nothing to pop: *"the iteration lock stack is
    empty"*. That is `for x in a` with a closure in it, and nothing more.
  • **`declaringWitnessParamBase`/`declaringWitnessParamCount`** — where a `where`-constrained generic method's
    witness parameters sit. Zeroed by a closure, the first witness dispatch AFTER it would panic in
    `witnessParamValueId`. The same two statements in the OTHER order compile regardless, which hides it.
  • **`charLiteralOps`** — pinned by `char-literal-to-int/char-literal-coerces-across-a-closure-body`, whose
    second half is a SILENT WRONG ANSWER rather than a panic.

This case is the shared one: one program that reaches all three, so a future column added to the reset list
and forgotten in the restore list has somewhere to fail.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias Fn1 = function(Integer) returns Integer

function apply(f Fn1, x Integer) returns Integer
	return f(x)
end 'apply'

interface Named
	function label() returns Integer
end 'Named'

type W implements Named
	export var n as Integer

	function label() returns Integer
		return self.n
	end 'label'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'W'

type Holder uses T where T is Named
	export var item as T

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'

	function go() returns Integer
		let viaClosure = apply(function(n Integer) gives n + 1, x: 0)
		return self.item.label() + viaClosure
	end 'go'
end 'Holder'

typealias WH = Holder with W

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(2)
	var total = 0
	for x in a 'loop'
		total = total + x + apply(function(n Integer) gives n + 1, x: 0)
	end 'loop'
	let h = WH.create(W.create(38))
	print("total={total} go={h.go()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
total=5 go=39
```

### A captured GENERIC INSTANCE — a base is a struct box OR an instance, in a closure too

⛔⛔ **`capturedStructBase` ASKS `tagCarriesStructLayoutHere`, EXACTLY AS ITS TWO SIBLINGS DO, SO A
CAPTURED GENERIC INSTANCE IS A MEMBER BASE.** `requireStructBase` for a local base and `structLayoutOfField`
for a chain hop ask the same predicate, because a struct box OR a generic INSTANCE (`Sizer with bool`) can
be a method/field base. A hand-rolled `!= structRef` test at this door would exclude every generic
instance.

⚠ **A REFUSAL OF AN INSTANCE HERE WOULD CONTRADICT ITSELF.** `typeTagName(genericInstance)` prints
`"struct"`, so the message would read *"the captured 'b', which is declared 'struct' and not a struct
type"*.

<!-- test: closure-capture.captured-generic-instance-method-and-field -->
Both members a base can carry, off one captured instance: a METHOD call and a FIELD read.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias LazyMessage = function() returns String

type Box uses T
	export let v as T

	static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	function get() returns T
		return self.v
	end 'get'
end 'Box'

typealias IntBox = Box with Integer

function lazy(make LazyMessage)
	print("{make()}")
end 'lazy'

function run(b IntBox)
	lazy(function() gives "v={b.get()} field={b.v}")
end 'run'

function main() returns ExitCode
	run(IntBox.create(7))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v=7 field=7
```

<!-- test: closure-capture.captured-plain-struct-still-works -->
The CONTROL. A captured plain struct takes the same door as a generic instance — the gate admits both
of the predicate's tags, and a gate that lost the plain struct would be a worse defect than the one it
cures.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias LazyMessage = function() returns String

type Plain
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function doubled() returns Integer
		return self.n * 2
	end 'doubled'
end 'Plain'

function lazy(make LazyMessage)
	print("{make()}")
end 'lazy'

function run(p Plain)
	lazy(function() gives "d={p.doubled()} field={p.n}")
end 'run'

function main() returns ExitCode
	run(Plain.create(21))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
d=42 field=21
```

<!-- test: error.a-captured-scalar-still-has-no-members -->
⚠ **EVERYTHING ELSE IS REFUSED.** `tagCarriesStructLayoutHere` admits a struct and an instance and nothing
more, so a captured `int` is refused — and the message is accurate for it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias LazyMessage = function() returns String

function lazy(make LazyMessage)
	print("{make()}")
end 'lazy'

function run(n Integer)
	lazy(function() gives "n={n.doubled()}")
end 'run'

function main() returns ExitCode
	run(21)
	return 0
end 'main'
```
```maxoncstderr
error E2015: specs/closure-capture/error.a-captured-scalar-still-has-no-members.maxon:10:28: Unsupported: a field access or method call on the captured 'n', which is declared 'int' and not a struct type (only a struct has fields and methods)
```

<!-- test: closure-capture.a-closure-in-a-generic-body-is-returned -->
A closure written in a shared generic body carries the enclosing instance's descriptor in its environment, and can be returned.
```maxon
type Cell uses T
	export var v as T

	typealias Echo = function(T) returns T

	static function make(v T) returns Self
		return Self{v: v}
	end 'make'

	function echo() returns Echo
		return function(x T) gives x
	end 'echo'
end 'Cell'

typealias StrCell = Cell with String

function main() returns ExitCode
	let c = StrCell.make("returned")
	let e = c.echo()
	print("{e(c.v)}\n")
	return 0
end 'main'
```
```stdout
returned
```


### A closure owns its environment

A capturing closure's value is one word, the address of a refcounted record holding its code and what it
captured. The record holds a reference of its own to every managed capture and releases it when the last
holder of the closure drops it, so a closure may be returned, stored, copied and handed to `async`.

<!-- test: closure-capture.the-environment-releases-a-captured-string -->
The record's destructor releases the captured String when the closure is dropped.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let name = "a name padded long enough to heap allocate {7}"
	let greet = function(times Integer) gives "{name} x{times}"
	print("{greet(2)}\n")
	return 0
end 'main'
```
```stdout
a name padded long enough to heap allocate 7 x2
```
```exitcode
0
```

<!-- test: closure-capture.one-closure-in-two-fields-an-array-and-a-union -->
One closure copied into two fields, an Array and a union payload: every holder takes a reference of its own
and drops it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer
typealias OpArray = Array with Op

type Pair
	export var first as Op
	export var second as Op

	static function create(first Op, second Op) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

union Held
	one(op Op)
	nothing
end 'Held'

function main() returns ExitCode
	let label = "a label padded long enough to heap allocate {3}"
	let f = function(n Integer) gives n + (label.byteLength() as Integer)
	let p = Pair.create(f, second: f)
	var ops = OpArray.create()
	ops.push(f)
	let h = Held.one(f)
	var total = p.first(1) + p.second(2)

	for op in ops 'each'
		total = total + op(3)
	end 'each'

	match h 'held'
		one(op) then total = total + op(4)
		nothing then total = total + 0
	end 'held'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
190
```
```exitcode
0
```

<!-- test: closure-capture.a-returned-closure-runs-after-its-frame-died -->
The closure outlives the frame whose record it captured.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Reader = function() returns Integer

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Counter'

function makeReader(start Integer) returns Reader
	let c = Counter.create(start)
	return function() gives c.n * 2
end 'makeReader'

function main() returns ExitCode
	let r = makeReader(21)
	return r() as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: closure-capture.a-closure-passed-to-async -->
The coroutine owns the closure it was handed and runs it after the block that built it has ended.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Measure = function() returns Integer
typealias Pending = Promise with Integer

function later(m Measure) returns Integer
	Scheduler.yield()
	return m()
end 'later'

function spawnIt() returns Pending
	let text = "a text padded long enough to heap allocate {1}"
	return async later(function() gives text.byteLength() as Integer)
end 'spawnIt'

function main() returns ExitCode
	let p = spawnIt()
	return (await p) as ExitCode
end 'main'
```
```exitcode
44
```

<!-- test: closure-capture.a-var-from-an-immutable-closure-takes-a-reference -->
`var g = f` takes a reference of its own, so `f` stays readable and `g` may be rebound.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {2}"
	let f = function(n Integer) gives n + (word.byteLength() as Integer)
	var g = f
	let first = g(1)
	g = function(n Integer) gives n * 2
	return (first + f(0) + g(1)) as ExitCode
end 'main'
```
```exitcode
91
```

<!-- test: closure-capture.clone-shares-the-closure -->
`f.clone()` is a second reference to the same closure.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {3}"
	let f = function(n Integer) gives n + (word.byteLength() as Integer)
	let g = f.clone()
	return (f(1) + g(2)) as ExitCode
end 'main'
```
```exitcode
91
```

<!-- test: closure-capture.a-closure-as-a-generic-element -->
A closure stored at a type parameter is retained and released through the instance's descriptor.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer

type Box uses T
	export var item as T

	static function make(item T) returns Self
		return Self{item: item}
	end 'make'
end 'Box'

typealias OpBox = Box with Op

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {4}"
	let b = OpBox.make(function(n Integer) gives n + (word.byteLength() as Integer))
	let op = b.item
	return op(2) as ExitCode
end 'main'
```
```exitcode
46
```

<!-- test: closure-capture.a-closure-in-a-union-payload-is-dropped -->
A union box holding a capturing closure releases it when the box is dropped, unmatched.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer

union Held
	one(op Op)
	nothing
end 'Held'

function hold(n Integer) returns Held
	let word = "a word padded long enough to heap allocate {5}"
	return Held.one(function(k Integer) gives k + n + (word.byteLength() as Integer))
end 'hold'

function main() returns ExitCode
	let h = hold(1)
	print("held {h.name}\n")
	return 0
end 'main'
```
```stdout
held one
```
```exitcode
0
```

<!-- test: closure-capture.error.a-captured-local-is-moved-into-the-closure -->
A capture moves an owned local into the closure, so a read of it after the literal is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	let f = function() gives word.byteLength() as Integer
	print("{word}\n")
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:7:10: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: closure-capture.error.a-closure-in-a-loop-cannot-capture-an-outer-local -->
The first trip moves the outer local into its closure, so the second trip's capture reads a moved value.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	var thunks = Thunks.create()

	for i in 0 upto 2 'each'
		thunks.push(function() gives word.byteLength() as Integer)
	end 'each'

	return thunks.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:11:32: use of moved value 'word': it was moved in an earlier iteration of this loop
```

<!-- test: closure-capture.error.a-capture-beside-a-consuming-argument -->
A call that consumes `word` and a closure in the same argument list that captures it would both own the one
record, so the consumed argument is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Held
	export let text as String
	export let size as Integer

	static function create(text String, measure Thunk) returns Self
		return Self{text: text, size: measure()}
	end 'create'
end 'Held'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	let held = Held.create(word, measure: function() gives word.byteLength() as Integer)
	return held.size as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:25: use of moved value 'word': its ownership moved to the closure that captures it
```

<!-- test: closure-capture.a-capture-beside-a-borrowing-argument -->
A call that only borrows `word` may take, in the same argument list, a closure that captures it: the closure
owns the record for as long as the call runs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function sum(text String, measure Thunk) returns Integer
	return (text.byteLength() as Integer) + measure()
end 'sum'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	print("{sum(word, measure: function() gives word.byteLength() as Integer)}\n")
	return 0
end 'main'
```
```stdout
88
```

<!-- test: closure-capture.a-captured-parameter-stays-readable -->
A captured parameter takes a reference of its own, so the parameter is still the function's to read and the
caller's argument outlives the closure that was returned.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function measurer(text String) returns Thunk
	let f = function() gives text.byteLength() as Integer
	print("{text} {f()}\n")
	return f
end 'measurer'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	let f = measurer(word)
	print("{word} {f()}\n")
	return 0
end 'main'
```
```stdout
a word padded long enough to heap allocate 1 44
a word padded long enough to heap allocate 1 44
```

<!-- test: closure-capture.a-captured-self-is-shared-with-the-receiver -->
A closure that reads `self` holds a reference to the receiver's record, not a copy of it: a write through the
receiver after the closure was built is what the closure reads.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function reader() returns Thunk
		return function() gives self.n
	end 'reader'
end 'Counter'

function main() returns ExitCode
	var counter = Counter.create(3)
	let read = counter.reader()
	counter.n = 5
	print("{read()} {counter.n}\n")
	return 0
end 'main'
```
```stdout
5 5
```

<!-- test: closure-capture.a-capture-two-frames-out -->
An inner closure that names a binding of the function two frames out captures it through the closure between
them: the middle closure takes it from the function, and the inner one takes a reference from the middle one's
environment.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = function(Integer) returns Integer

function apply(f Step, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let base = 10
	let word = "abcd"
	let outer = function(k Integer) gives apply(function(n Integer) gives n + k + base + (word.byteLength() as Integer), x: 1)
	return outer(100) as ExitCode
end 'main'
```
```exitcode
115
```

<!-- test: closure-capture.a-captured-scalar-is-a-copy -->
A captured scalar is copied into the environment: the binding stays readable and writable after the literal,
and the closure keeps the value it saw.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var count = 1
	let f = function(n Integer) gives n + count
	count = count + 1
	print("{f(10)} {count}\n")
	return 0
end 'main'
```
```stdout
11 2
```

<!-- test: closure-capture.a-captured-interface-value-keeps-its-witness -->
A value held at an interface type is two words, and the environment captures both: the closure dispatches
through the witness it was handed, and its drop releases the value through that witness.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	let side as Integer
	let label as String

	function area() returns Integer
		return self.side * self.side + (self.label.byteLength() as Integer)
	end 'area'

	static function create(side Integer) returns Self
		return Self{side: side, label: "a label padded long enough to heap allocate {side}"}
	end 'create'
end 'Square'

function measurer(shape Shape) returns Thunk
	return function() gives shape.area()
end 'measurer'

function makeShape(side Integer) returns Shape
	return Square.create(side)
end 'makeShape'

function main() returns ExitCode
	let local = makeShape(2)
	let fromLocal = function() gives local.area() + 1
	let fromParam = measurer(Square.create(3))
	print("{fromLocal()} {fromParam()}\n")
	return 0
end 'main'
```
```stdout
50 54
```
```exitcode
0
```

<!-- test: closure-capture.a-cell-resident-var-hands-its-occupant-to-the-closure -->
A `var` handed to a parameter its callee reassigns lives in a cell, and a capture takes what the cell holds.
```maxon
typealias Integer = int(i64.min to i64.max)

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	let peek = function() gives word.byteLength() as Integer
	print("{peek()}\n")
	return 0
end 'main'
```
```stdout
48
```

<!-- test: closure-capture.a-cell-resident-scalar-is-copied -->
A cell-resident scalar is copied into the environment like any other scalar, so the cell stays the frame's.
```maxon
typealias Integer = int(i64.min to i64.max)

function bump(dest Integer)
	dest = dest + 1
end 'bump'

function main() returns ExitCode
	var n = 1
	bump(n)
	let f = function() gives n
	n = 7
	print("{f()} {n}\n")
	return 0
end 'main'
```
```stdout
2 7
```

### A closure called out of a slot it can rewrite

<!-- test: closure-capture.a-field-closure-that-replaces-itself-keeps-its-captures-while-it-runs -->
The call holds a reference to the closure it reads out of a field for as long as it runs, so a body that
replaces the field's closure still reads its own captures.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = function(Holder) returns Integer

type Holder
	export var step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	export function swap()
		self.step = function(_ Holder) gives 0
	end 'swap'
end 'Holder'

function swapThenMeasure(h Holder, text String) returns Integer
	h.swap()
	return text.byteLength() as Integer
end 'swapThenMeasure'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	var h = Holder.create(function(other Holder) gives swapThenMeasure(other, text: word))
	print("{h.step(h)} {h.step(h)}\n")
	return 0
end 'main'
```
```stdout
44 0
```
```exitcode
0
```

<!-- test: closure-capture.a-module-var-closure-that-replaces-itself-keeps-its-captures-while-it-runs -->
The same hold covers a closure read out of a module-level `var`.
```maxon
typealias Integer = int(i64.min to i64.max)

function idle() returns Integer
	return 1
end 'idle'

var current = idle

function replaceThenMeasure(text String) returns Integer
	current = idle
	return text.byteLength() as Integer
end 'replaceThenMeasure'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	current = function() gives replaceThenMeasure(word)
	print("{current()} {current()}\n")
	return 0
end 'main'
```
```stdout
44 1
```
```exitcode
0
```

<!-- test: closure-capture.a-field-closure-replaced-by-its-own-argument-runs-as-read -->
The call holds the closure it reads out of a field before it evaluates its arguments, so an argument that replaces the field's closure does not free the one being called.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Step = function(Integer) returns Integer

type Holder
	export var step as Step

	static function create(step Step) returns Self
		return Self{step: step}
	end 'create'

	function swap(by Integer) returns Integer
		self.step = function(n Integer) gives n + by
		return 1
	end 'swap'
end 'Holder'

function main() returns ExitCode
	let word = "a word padded long enough to heap allocate {1}"
	var h = Holder.create(function(n Integer) gives n + (word.byteLength() as Integer))
	print("{h.step(h.swap(100))} {h.step(2)}\n")
	return 0
end 'main'
```
```stdout
45 102
```
```exitcode
0
```

### A closure record releases its captures however it is dropped

<!-- test: closure-capture.a-closure-overwritten-in-an-array-releases-its-captures -->
`set` destroys the closure it overwrites through the element's own drop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer
typealias OpArray = Array with Op

function main() returns ExitCode
	let label = "a label padded long enough to heap allocate {3}"
	var ops = OpArray.create()
	ops.push(function(n Integer) gives n + (label.byteLength() as Integer))
	try ops.set(0, value: function(n Integer) gives n * 2) otherwise panic("one element")
	let op = try ops.get(0) otherwise panic("one element")
	print("{op(4)}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: closure-capture.a-closure-held-as-a-map-value-releases-its-captures -->
A map that holds a capturing closure as a value releases it with the map.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer
typealias OpMap = Map with (Integer, Op)

function main() returns ExitCode
	let label = "a label padded long enough to heap allocate {3}"
	var ops = OpMap.create()
	ops.upsert(1, value: function(n Integer) gives n + (label.byteLength() as Integer))
	print("{ops.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

### Module-level function values

<!-- test: closure-capture.module-level-function-values -->
A module-level `let` and `var` hold function values: a plain function, and a capturing closure the `var` is
rebound to and releases at exit.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op = function(Integer) returns Integer

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function adder(text String) returns Op
	return function(n Integer) gives n + (text.byteLength() as Integer)
end 'adder'

let doubler = twice
var current = adder("a text padded long enough to heap allocate 2")

function install(text String)
	current = function(n Integer) gives n * (text.byteLength() as Integer)
end 'install'

function main() returns ExitCode
	print("{doubler(3)} {current(4)}\n")
	install("a text padded long enough to heap allocate {1}")
	print("{current(1)}\n")
	current = doubler
	print("{current(5)} {doubler(6)}\n")
	return 0
end 'main'
```
```stdout
6 48
44
10 12
```
```exitcode
0
```

<!-- test: closure-capture.a-module-let-function-value-its-function-calls-back -->
A module-level `let` bound to a function names the function without running it, so the function may call back through the `let`.
```maxon
typealias Integer = int(i64.min to i64.max)

function countdown(n Integer) returns Integer
	if n == 0 'done'
		return 0
	end 'done'

	return step(n - 1) + 1
end 'countdown'

let step = countdown

function main() returns ExitCode
	print("{step(5)}\n")
	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

### A capture beside the call it is an argument of

<!-- test: closure-capture.error.a-capture-beside-a-by-reference-argument -->
A `var` handed by reference to the call that also takes a closure capturing it: the capture moves `b` into
the closure, so handing `b` to the by-reference parameter is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Box
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

function show(dest Box, m Thunk) returns Integer
	let seen = dest.n + m()
	dest = Box.create(5)
	return seen
end 'show'

function main() returns ExitCode
	var b = Box.create(1)
	print("{show(b, m: function() gives b.n)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:15: use of moved value 'b': its ownership moved to the closure that captures it
```

<!-- test: closure-capture.error.a-capture-of-the-receiver-of-the-call -->
The capture moves the receiver into the closure before `push` writes it, so the push is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

function main() returns ExitCode
	var thunks = Thunks.create()
	thunks.push(function() gives thunks.count() as Integer)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:8:2: use of moved value 'thunks': its ownership moved to the closure that captures it
```

### A self field handed by reference inside a closure

<!-- test: closure-capture.error.a-self-field-handed-by-reference-inside-a-closure -->
A bare self-field name inside a closure is a capture like any other, so a by-reference parameter cannot be
handed its storage.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function setIt(dest Integer) returns Integer
	dest = 99
	return dest
end 'setIt'

type Counter
	export var n as Integer

	static function create() returns Self
		return Self{n: 1}
	end 'create'

	function setter() returns Thunk
		return function() gives setIt(n)
	end 'setter'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	let f = c.setter()
	print("{f()} {c.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:18:27: cannot pass 'n' to function that mutates parameter 'dest' (in Counter.setter$closure_0): the closure holds its own copy of 'n', so a write through 'dest' cannot reach it
```

### A captured Promise

<!-- test: closure-capture.a-captured-promise-moves-into-the-closure -->
A capture moves the promise into the closure, so the green thread stays alive for as long as the closure does
and runs at the next yield, after the frame that spawned it has ended.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias Thunk = function() returns Integer

var flag = 0

function setFlag() returns Integer
	flag = 1
	Scheduler.yield()
	return 1
end 'setFlag'

function alive(p IntPromise) returns Integer
	return 1 if p.inner > 0 else 0
end 'alive'

function later() returns Thunk
	let p = async setFlag()
	return function() gives alive(p)
end 'later'

function main() returns ExitCode
	let f = later()
	Scheduler.yield()
	print("{flag} {f()}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: closure-capture.a-captured-promise-is-released-with-the-closure -->
A closure that never runs releases the promise it captured when it is dropped.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias Thunk = function() returns Integer

function plain() returns Integer
	Scheduler.yield()
	return 7
end 'plain'

function alive(p IntPromise) returns Integer
	return 1 if p.inner > 0 else 0
end 'alive'

function never() returns Thunk
	let p = async plain()
	return function() gives alive(p)
end 'never'

function main() returns ExitCode
	_ = never()
	print("built\n")
	return 0
end 'main'
```
```stdout
built
```
```exitcode
0
```

<!-- test: closure-capture.error.a-captured-promise-is-not-awaited-inside-the-closure -->
The closure owns the promise and may run more than once, so its body cannot give it away.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function plain() returns Integer
	Scheduler.yield()
	return 7
end 'plain'

function later() returns Thunk
	let p = async plain()
	return function() gives await p
end 'later'

function main() returns ExitCode
	let f = later()
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3141: <fragment>:12:26: a promise cannot be borrowed through 'await': it owns a green thread, and a green thread has exactly one owner — so reading one out of the thing that holds it MOVES it. This one is a closure's capture: the closure owns it for as long as the closure lives, and its body may run any number of times, so no run of it may give the promise away. Await it in the function that spawned it, or hand the closure what it needs as a parameter
```

<!-- test: closure-capture.error.a-captured-promise-read-after-the-literal -->
The capture consumed the promise, so awaiting it in the frame afterwards is a use after move.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function plain() returns Integer
	Scheduler.yield()
	return 7
end 'plain'

function alive(p IntPromise) returns Integer
	return 1 if p.inner > 0 else 0
end 'alive'

function main() returns ExitCode
	let p = async plain()
	let f = function() gives alive(p)
	return (f() + (await p)) as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:23: use of moved value 'p': its ownership moved to the closure that captures it
```

### A captured function value

<!-- test: closure-capture.a-captured-function-value-stays-callable -->
A plain function value and a closure that captures nothing are shared by every holder, so capturing one
leaves it callable.
```maxon
typealias Integer = int(i64.min to i64.max)

typealias Op = function(Integer) returns Integer

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function apply(f Op, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let cmp = twice
	let triple = function(n Integer) gives n * 3
	let f = function(n Integer) gives apply(cmp, x: n) + apply(triple, x: n)
	print("{f(1)} {cmp(2)} {triple(2)}\n")
	return 0
end 'main'
```
```stdout
5 4 6
```
```exitcode
0
```

<!-- test: closure-capture.a-captured-function-value-is-called-by-name -->
A captured function value is called by its name inside the closure, as it would be outside one.
```maxon
typealias Integer = int(i64.min to i64.max)

function twice(n Integer) returns Integer
	return n * 2
end 'twice'

function main() returns ExitCode
	let cmp = twice
	let word = "a word padded long enough to heap allocate {1}"
	let measure = function(n Integer) gives n + (word.byteLength() as Integer)
	let f = function(n Integer) gives cmp(n) + measure(n)
	print("{f(1)} {cmp(2)}\n")
	return 0
end 'main'
```
```stdout
47 4
```
```exitcode
0
```

### A closure stored into what it captures

A closure holds a counted reference to every record it captures, so storing it into one of those records'
graphs makes a cycle no drop can release. The store is refused.

<!-- test: closure-capture.error.a-closure-stored-into-a-field-of-self-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: function() gives 0}
	end 'create'

	export function arm()
		self.read = function() gives self.n
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:14:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-stored-into-a-field-of-a-parameter-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: function() gives 0}
	end 'create'
end 'Counter'

function arm(h Counter)
	h.read = function() gives h.n
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:15:11: this closure captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-closure-bound-first-then-stored-into-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: function() gives 0}
	end 'create'

	export function arm()
		let c = function() gives self.n
		self.read = c
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:15:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-pushed-into-an-array-field-of-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

type Counter
	export var n as Integer
	export var reads as Thunks

	static function create() returns Self
		return Self{n: 1, reads: Thunks.create()}
	end 'create'

	export function arm()
		self.reads.push(function() gives self.n)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.reads.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:15:19: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-set-into-an-array-field-of-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

type Counter
	export var n as Integer
	export var reads as Thunks

	static function create() returns Self
		var reads = Thunks.create()
		reads.push(function() gives 0)
		return Self{n: 1, reads: reads}
	end 'create'
end 'Counter'

function arm(h Counter)
	try h.reads.set(0, value: function() gives h.n) otherwise panic("one element")
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.reads.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:18:28: this closure captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-closure-in-a-union-payload-stored-into-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

union Armed
	ready(read Thunk)
	idle
end 'Armed'

type Counter
	export var n as Integer
	export var armed as Armed

	static function create() returns Self
		return Self{n: 1, armed: Armed.idle}
	end 'create'

	export function arm()
		self.armed = Armed.ready(function() gives self.n)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	print("{c.armed.name}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3183: <fragment>:19:16: this value holds a closure that captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.a-closure-stored-into-a-record-it-does-not-capture -->
A closure that captures one record may be stored into another.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var read as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, read: function() gives 0}
	end 'create'

	function lend(other Counter)
		other.read = function() gives self.n
	end 'lend'
end 'Counter'

function main() returns ExitCode
	let source = Counter.create(7)
	var target = Counter.create(1)
	source.lend(target)
	return target.read() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: closure-capture.error.a-closure-in-a-struct-literal-stored-into-what-it-captures -->
A closure inside a struct literal is held by the record the literal builds.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var w as Wrapper

	static function create() returns Self
		return Self{n: 1, w: Wrapper.idle()}
	end 'create'
end 'Counter'

type Wrapper
	export var read as Thunk

	static function idle() returns Self
		return Self{read: function() gives 0}
	end 'idle'

	static function arm(c Counter)
		c.w = Self{read: function() gives c.n}
	end 'arm'
end 'Wrapper'

function main() returns ExitCode
	var c = Counter.create()
	Wrapper.arm(c)
	return c.w.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:22:9: this value holds a closure that captures 'c' and is stored into a record 'c' holds, so 'c' would hold the closure and the closure would hold 'c', and neither would ever be released. Store it somewhere 'c' does not reach, or capture what the closure needs instead of 'c' itself
```

<!-- test: closure-capture.error.a-closure-in-a-tuple-stored-into-what-it-captures -->
A tuple holds its closure as a record field does.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var pair as (Thunk, Integer)

	static function create() returns Self
		return Self{n: 1, pair: (idle, 0)}
	end 'create'

	export function arm()
		self.pair = (function() gives self.n, 1)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.pair.1 as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:18:15: this value holds a closure that captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-in-an-array-literal-stored-into-what-it-captures -->
An array literal holds each element it is built from.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

type Counter
	export var n as Integer
	export var reads as Thunks

	static function create() returns Self
		return Self{n: 1, reads: Thunks.create()}
	end 'create'

	export function arm()
		self.reads = [function() gives self.n]
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.reads.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:15:16: this value holds a closure that captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-local-array-holding-the-closure-stored-into-what-it-captures -->
A container filled first and stored afterwards holds what was pushed into it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

type Counter
	export var n as Integer
	export var reads as Thunks

	static function create() returns Self
		return Self{n: 1, reads: Thunks.create()}
	end 'create'

	export function arm()
		var fresh = Thunks.create()
		fresh.push(function() gives self.n)
		self.reads = fresh
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.reads.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:17:16: this value holds a closure that captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-chosen-by-a-ternary-stored-into-what-it-captures -->
A merge of two closures may be either of them.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function arm(live bool)
		let chosen = (function() gives self.n) if live else idle
		self.read = chosen
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm(true)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:19:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-assigned-in-an-if-stored-into-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function arm(live bool)
		var chosen = idle

		if live 'arming'
			chosen = function() gives self.n
		end 'arming'

		self.read = chosen
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm(true)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:24:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-chosen-by-a-match-stored-into-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

enum Mode
	quiet
	live
end 'Mode'

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function arm(mode Mode)
		let chosen = match mode 'mode'
			quiet gives idle
			live gives function() gives self.n
		end 'mode'

		self.read = chosen
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm(Mode.live)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:28:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-assigned-in-a-loop-stored-into-what-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function arm(rounds Integer)
		var chosen = idle

		for i in 0 upto rounds 'eachRound'
			chosen = function() gives self.n + i
		end 'eachRound'

		self.read = chosen
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm(2)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:24:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-clone-of-the-closure-stored-into-what-it-captures -->
A clone of a closure shares its record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	export function arm()
		let reader = function() gives self.n
		self.read = reader.clone()
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:19:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-returned-by-a-call-stored-into-what-it-captures -->
A callee that hands back its argument hands back the closure.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

function identity(f Thunk) returns Thunk
	return f
end 'identity'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	export function arm()
		self.read = identity(function() gives self.n)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:22:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-a-callee-builds-stored-into-the-var-it-captures -->
The closure is built by a callee that captures its argument, so this function never names a capture — the
refusal names the record by the `var` the store is written through.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: function() gives 0}
	end 'create'
end 'Counter'

function reader(h Counter) returns Thunk
	return function() gives h.n
end 'reader'

function main() returns ExitCode
	var c = Counter.create()
	c.read = reader(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:20:11: this closure captures 'c' and is stored into a record 'c' holds, so 'c' would hold the closure and the closure would hold 'c', and neither would ever be released. Store it somewhere 'c' does not reach, or capture what the closure needs instead of 'c' itself
```

<!-- test: closure-capture.error.a-closure-capturing-a-closure-stored-into-what-the-inner-captures -->
A closure holds everything the closures it captures hold.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	export function arm()
		let inner = function() gives self.n
		self.read = function() gives inner() + 1
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:19:15: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-handed-to-a-method-that-stores-it-into-its-receiver -->
A callee that stores its argument into its receiver stores the closure there.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function setRead(f Thunk)
		self.read = f
	end 'setRead'

	export function arm()
		self.setRead(function() gives self.n)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:22:16: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-handed-to-a-method-of-the-parameter-it-captures -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function install(f Thunk)
		self.read = f
	end 'install'
end 'Counter'

function arm(h Counter)
	h.install(function() gives h.n)
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:23:12: this closure captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-closure-a-closure-body-stores-into-what-it-captures -->
The rule holds inside a closure body, for a record it captured.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'

	function install(f Thunk) returns Integer
		self.read = f
		return 1
	end 'install'

	function arm() returns Integer
		let later = function() gives self.install(function() gives self.n)
		return later()
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	let armed = c.arm()
	return (c.read() + armed) as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:23:45: this closure captures 'self' and is stored into a record 'self' holds, so 'self' would hold the closure and the closure would hold 'self', and neither would ever be released. Store it somewhere 'self' does not reach, or capture what the closure needs instead of 'self' itself
```

<!-- test: closure-capture.error.a-closure-capturing-a-borrowed-field-stored-into-that-field -->
A captured borrow of a field is held too: storing the closure into that field's record closes a cycle, which is refused as a write through the borrow as well.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Inner
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Inner'

type Counter
	export var inner as Inner

	static function create() returns Self
		return Self{inner: Inner.create()}
	end 'create'

	export function arm()
		let held = self.inner
		self.inner.read = function() gives held.n
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	return c.inner.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:3: cannot write through 'self.inner', which may be the record of immutable variable 'held'; use clone()
error E3183: <fragment>:27:21: this closure captures 'held' and is stored into a record 'held' holds, so 'held' would hold the closure and the closure would hold 'held', and neither would ever be released. Store it somewhere 'held' does not reach, or capture what the closure needs instead of 'held' itself
```

<!-- test: closure-capture.a-closure-capturing-a-copy-stored-into-the-record-it-was-copied-from -->
A closure that captures a copy of a field holds no reference to the record, so storing it there is legal.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var label as String
	export var read as Thunk

	static function create() returns Self
		return Self{n: 7, label: "a label padded long enough to heap allocate {1}", read: idle}
	end 'create'

	export function arm()
		let count = self.n
		let copy = self.label.clone()
		self.read = function() gives count + (copy.byteLength() as Integer) - (copy.byteLength() as Integer)
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	print("{c.read()}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: closure-capture.a-closure-capturing-one-field-stored-into-a-sibling-record -->
A closure capturing one record's borrowed field may be stored into a sibling record: neither reaches the other.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Inner
	export var n as Integer
	export var read as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, read: idle}
	end 'create'
end 'Inner'

type Counter
	export var left as Inner
	export var right as Inner

	static function create() returns Self
		return Self{left: Inner.create(3), right: Inner.create(4)}
	end 'create'

	export function arm()
		let borrowed = self.left
		self.right.read = function() gives borrowed.n
	end 'arm'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.arm()
	print("{c.right.read()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: closure-capture.error.a-closure-stored-into-a-fresh-record-another-fresh-record-holds -->
Two records built in one function close a cycle as two handed in would: the closure captures the record that holds the one it is stored into.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Holder
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Holder'

type Box
	export var n as Integer
	export var held as Holder

	static function create() returns Self
		return Self{n: 2, held: Holder.create()}
	end 'create'

	function attach(h Holder)
		self.held = h
	end 'attach'
end 'Box'

function main() returns ExitCode
	var a = Holder.create()
	var b = Box.create()
	b.attach(a)
	a.read = function() gives b.n
	return a.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:35:11: this closure captures 'b' and is stored into a record 'b' holds, so 'b' would hold the closure and the closure would hold 'b', and neither would ever be released. Store it somewhere 'b' does not reach, or capture what the closure needs instead of 'b' itself
```

<!-- test: closure-capture.error.a-closure-stashed-in-a-module-var-stored-into-what-it-captures -->
A module-level `var` hands back what was stored into it, so a closure read back out of one is the closure stored there.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

function idleThunk() returns Thunk
	return idle
end 'idleThunk'

var stash = idleThunk()

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Counter'

function arm(h Counter)
	stash = function() gives h.n
	h.read = stash
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:26:11: this closure captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-closure-a-callee-stashes-in-a-module-var-stored-into-what-it-captures -->
A callee that stores a closure into a module-level `var` stores it for its caller too.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

function idleThunk() returns Thunk
	return idle
end 'idleThunk'

var stash = idleThunk()

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Counter'

function remember(h Counter)
	stash = function() gives h.n
end 'remember'

function arm(h Counter)
	remember(h)
	h.read = stash
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:30:11: this closure captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-callee-stores-a-module-vars-closure-into-what-it-captures -->
A callee that stores what a module-level `var` holds into its argument stores the closure its caller put there.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

function idleThunk() returns Thunk
	return idle
end 'idleThunk'

var stash = idleThunk()

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Counter'

function install(h Counter)
	h.read = stash
end 'install'

function arm(h Counter)
	stash = function() gives h.n
	install(h)
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:30:2: this value holds a closure that captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.an-array-of-closures-appended-into-what-they-capture -->
Appending an array of function values shares its closures with the receiver.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer
typealias Thunks = Array with Thunk

type Counter
	export var n as Integer
	export var reads as Thunks

	static function create() returns Self
		return Self{n: 1, reads: Thunks.create()}
	end 'create'
end 'Counter'

function arm(h Counter)
	var fresh = Thunks.create()
	fresh.push(function() gives h.n)
	h.reads.append(fresh)
end 'arm'

function main() returns ExitCode
	var c = Counter.create()
	arm(c)
	return c.reads.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:18:10: this value holds a closure that captures 'h' and is stored into a record 'h' holds, so 'h' would hold the closure and the closure would hold 'h', and neither would ever be released. Store it somewhere 'h' does not reach, or capture what the closure needs instead of 'h' itself
```

<!-- test: closure-capture.error.a-callee-stores-a-closure-capturing-one-argument-into-the-other-handed-one-record -->
A callee that stores a closure capturing one parameter into another closes a cycle when both arguments are one record.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Counter'

function wire(target Counter, source Counter)
	target.read = function() gives source.n
end 'wire'

function rewire(c Counter)
	wire(c, source: c)
end 'rewire'

function main() returns ExitCode
	var c = Counter.create()
	rewire(c)
	return c.read() as ExitCode
end 'main'
```
```maxoncstderr
error E3183: <fragment>:23:10: this value holds a closure that captures 'c' and is stored into a record 'c' holds, so 'c' would hold the closure and the closure would hold 'c', and neither would ever be released. Store it somewhere 'c' does not reach, or capture what the closure needs instead of 'c' itself
```

<!-- test: closure-capture.a-closure-capturing-a-replaced-field-stored-into-its-replacement -->
A field read before it is overwritten names the record it held then, so a closure capturing that record may be stored into the record that replaced it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Button
	export var n as Integer
	export var onClick as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, onClick: idle}
	end 'create'
end 'Button'

type Panel
	export var left as Button

	static function create() returns Self
		return Self{left: Button.create(1)}
	end 'create'

	export function rewire()
		let old = self.left
		self.left = Button.create(2)
		self.left.onClick = function() gives old.n
	end 'rewire'
end 'Panel'

function main() returns ExitCode
	var p = Panel.create()
	p.rewire()
	print("{p.left.onClick()} {p.left.n}\n")
	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: closure-capture.a-closure-capturing-one-field-of-a-fresh-record-stored-into-its-sibling -->
Each field of a record built in the function holds what was stored into it, so a closure capturing one field may be stored into its sibling.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Inner
	export var n as Integer
	export var read as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, read: idle}
	end 'create'
end 'Inner'

type Outer
	export var n as Integer
	export var read as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, read: idle}
	end 'create'
end 'Outer'

type Pair
	export var left as Inner
	export var right as Outer

	static function build() returns Self
		var pair = Self{left: Inner.create(3), right: Outer.create(4)}
		let borrowed = pair.left
		pair.right.read = function() gives borrowed.n
		return pair
	end 'build'
end 'Pair'

function main() returns ExitCode
	let pair = Pair.build()
	print("{pair.right.read()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: closure-capture.a-call-through-a-function-value-lands-only-on-bodies-of-its-arity -->
A call through a function value lands only on a body taking exactly its arguments, so a function of another arity that stores a closure is not consulted.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create(n Integer) returns Self
		return Self{n: n, read: idle}
	end 'create'
end 'Counter'

function wire(target Counter, source Counter)
	target.read = function() gives source.n
end 'wire'

function readTwice(c Counter) returns Integer
	let reader = function(t Counter) gives t.n + c.n
	return reader(c)
end 'readTwice'

function main() returns ExitCode
	let wiring = wire
	var a = Counter.create(1)
	let b = Counter.create(2)
	wiring(a, b)
	print("{a.read()} {readTwice(b)}\n")
	return 0
end 'main'
```
```stdout
2 4
```
```exitcode
0
```

<!-- test: closure-capture.error.a-closure-stored-into-the-local-it-captured -->
Capturing a local moves it into the closure, so storing the closure into that local's record afterwards uses a moved value.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Thunk = function() returns Integer

function idle() returns Integer
	return 0
end 'idle'

type Counter
	export var n as Integer
	export var read as Thunk

	static function create() returns Self
		return Self{n: 1, read: idle}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var h = Counter.create()
	h.read = function() gives h.n
	return h.n as ExitCode
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:2: use of moved value 'h': its ownership moved to the closure that captures it
```
