---
feature: first-class-functions
status: stable
keywords: function, closure, callback, higher-order, function pointer
category: functions
---
# First-Class Functions

## Documentation

Functions in Maxon are first-class citizens. They can be stored in variables, passed as arguments to other functions, and returned from functions.

## Function Types

Function types are introduced with the `function` keyword and must be named via
`typealias` — the literal `function(...) returns T` form is only legal as the
right-hand side of a `typealias` declaration. Anywhere else (parameters, return
types, struct fields, variable annotations, generic arguments), reference the
alias by name.

```maxon
typealias Score = int(i64.min to i64.max)

// A function that takes a Score and returns a Score
typealias Transform = function(Score) returns Score

// A function that takes two Scores and returns a bool
typealias Compare = function(Score, Score) returns bool

// A function with no parameters that returns void
typealias Callback = function()
```

Parameter names inside a function-type signature are optional and act as
documentation:

```maxon
typealias Score = int(i64.min to i64.max)

typealias Operation = function(x Score, y Score) returns Score
```

## Using Function-Type Aliases

Once defined, a function-type alias can be used anywhere a type is expected
(function parameters, return types, struct fields, generic arguments):

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
typealias BinaryOp = function(Integer, Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function pickDouble() returns UnaryOp
	return double
end 'pickDouble'
```

## Function References

To get a reference to a function, use the function name without parentheses:

```maxon
typealias Score = int(i64.min to i64.max)

function double(x Score) returns Score
	return x * 2
end 'double'

function main() returns ExitCode
	let f = double      // f is a function reference
	return f(21)        // calls double(21), returns 42
end 'main'
```
```exitcode
42
```

## Passing Functions as Arguments

Functions can be passed to other functions via a function-type alias:

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreOp = function(Score) returns Score

function apply(f ScoreOp, x Score) returns Score
	return f(x)
end 'apply'

function triple(n Score) returns Score
	return n * 3
end 'triple'

function main() returns ExitCode
	return apply(triple, x: 10)  // returns 30
end 'main'
```
```exitcode
30
```

A function may be referenced by name *before* its declaration appears in the
source. Name resolution of a bare function reference is deferred until every
declaration is known, so a forward reference resolves to the same function
value as a backward one:

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreOp = function(Score) returns Score

function apply(f ScoreOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(triple, x: 10)  // triple is declared below — returns 30
end 'main'

function triple(n Score) returns Score
	return n * 3
end 'triple'
```
```exitcode
30
```

## Function-Typed Fields

A struct field may hold a function. The field is declared with a function-type alias
like any other field, and the value it holds is called through the field directly —
a normal indirect call:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'

	function run(x Integer) returns Integer
		return self.op(x)      // call the field from inside a method
	end 'run'
end 'Handler'
```

A function-typed field is an ordinary value, so every position a function value is
legal in accepts one: call it (`h.op(x)`), bind it (`let f = h.op`), pass it
(`apply(h.op, x: 1)`), and return it (`return h.op`). A field whose signature returns
nothing is called as a statement — which is the shape a table of handlers or
compiler passes keyed by a struct field takes.

A field holds the function POINTER only. A closure that CAPTURES cannot be stored in
one: captures are taken by reference, so the environment is bound to the frame that
built the closure and cannot outlive it. Store a function reference, or a closure that
captures nothing.

## Closures

Closures are inline anonymous functions written with the `function` keyword:

```maxon
typealias Score = int(i64.min to i64.max)

function main() returns ExitCode
	let f = function(x Score) gives x * 2
	return f(21)  // returns 42
end 'main'
```
```exitcode
42
```

Closures can be passed directly to higher-order functions:

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreOp = function(Score) returns Score

function apply(f ScoreOp, x Score) returns Score
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(function(n Score) gives n + 5, x: 10)  // returns 15
end 'main'
```
```exitcode
15
```

## Tests

<!-- test: first-class-function.basic-reference -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let f = double
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.pass-as-argument -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function triple(n Integer) returns Integer
	return n * 3
end 'triple'

function main() returns ExitCode
	return apply(triple, x: 10)
end 'main'
```
```exitcode
30
```

<!-- test: first-class-function.closure-in-variable -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let f = function(x Integer) gives x * 5
	return f(8)
end 'main'
```
```exitcode
40
```

<!-- test: first-class-function.closure-as-argument -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(function(n Integer) gives n + 7, x: 10)
end 'main'
```
```exitcode
17
```

<!-- test: first-class-function.multiple-params -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias BinaryOp = function(Integer, Integer) returns Integer

function calculate(f BinaryOp, a Integer, b Integer) returns Integer
	return f(a, b)
end 'calculate'

function add(x Integer, y Integer) returns Integer
	return x + y
end 'add'

function main() returns ExitCode
	return calculate(add, a: 15, b: 27)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.reassign -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	var f = double
	let a = f(10)
	f = triple
	let b = f(10)
	return a + b
end 'main'
```
```exitcode
50
```

<!-- test: first-class-function.typealias-single-param -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(double, x: 21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.typealias-multi-param -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias BinaryOp = function(Integer, Integer) returns Integer

function add(x Integer, y Integer) returns Integer
	return x + y
end 'add'

function compute(f BinaryOp, a Integer, b Integer) returns Integer
	return f(a, b)
end 'compute'

function main() returns ExitCode
	return compute(add, a: 15, b: 27)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.typealias-with-closure -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(function(n Integer) gives n + 5, x: 37)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.let-from-call-returning-fn -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

function pickDouble() returns UnaryOp
	return double
end 'pickDouble'

function main() returns ExitCode
	let f = pickDouble()
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.forward-reference-arg -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A bare function name may be passed as a function-typed argument even when the
function is declared *later* in the file. The parser can't resolve the
reference at the call site (the signature isn't registered yet), so it defers
to type resolution, which rewrites the read to a function reference once every
declaration is known.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function driver() returns Integer
	return apply(dbl, x: 21)
end 'driver'

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	return driver()
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.forward-reference-named-arg -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Two distinct forward-declared functions passed by name as a NAMED, non-first
argument (the shape the self-hosted compiler's own `computeParamKeySet` uses)
must each dispatch to the correct target: `useDbl` yields 40 and `useTrip`
yields 30, so their difference is 10 only when both references resolved
correctly.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(x Integer, f UnaryOp) returns Integer
	return f(x)
end 'apply'

function useDbl() returns Integer
	return apply(20, f: dbl)
end 'useDbl'

function useTrip() returns Integer
	return apply(10, f: trip)
end 'useTrip'

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function trip(n Integer) returns Integer
	return n * 3
end 'trip'

function main() returns ExitCode
	return useDbl() - useTrip()
end 'main'
```
```exitcode
10
```

<!-- disabled-test: first-class-function.cross-file-extension-typealias-param -->
<!-- P1.6 (generics: Sorter with Number) -->
A function-typed parameter must work even when its typealias is declared
inside an `extension` block in a SEPARATE file that the loader hasn't reached
yet. The stdlib loader walks `stdlib/` in whatever order the OS returns from
`Directory.list`, so the consumer file may parse before the file that
declares the typealias — exactly the shape that bit
`helpers/sort/smallSort.maxon` (which uses `cmp SortComparator`) when
`helpers/sort/insertionSort.maxon` (which declares `SortComparator` inside
`extension Array`) parses later.

The original parser bug: `parseFunctionParametersInner` eagerly stamped the
parameter type as `MaxonType.named("SortComparator")` at parse time because
the inner typealias wasn't yet drained into `unresolvedStructTypes["Array"]
.innerAliases`. Downstream `slotArgsForCall` and the indirect-call lowering
both consult that stamped type — once it's wrong, the function's ABI shape
is permanently wrong, the call site fails the H.2 `validateIndirectCallLabels`
check with E3005, and codegen rejects the call.

The fix should keep the parameter type opaque until after every file in the
project has been parsed, then let TypeResolution drain the typealias and
re-stamp the resolved function type. The parser should not be type-aware at
parameter-declaration time.
```maxon
// --- file: aaa_alias.maxon
module extension Sorter
	typealias Comparator = function(Element, Element) returns Element
end 'Sorter'

// --- file: zzz_consumer.maxon
typealias Integer = int(i64.min to i64.max)

module type Sorter uses Element
	module var stub as Integer

	module static function create() returns Self
		return Self{stub: 0}
	end 'create'

	module function compareAndSwap(a Element, b Element, cmp Comparator) returns Element
		return cmp(a, b)
	end 'compareAndSwap'
end 'Sorter'

// --- file: main.maxon
typealias Number = int(i64.min to i64.max)
typealias NumberSorter = Sorter with Number

function pickLarger(a Number, b Number) returns Number
	if a > b 'aBig'
		return a
	end 'aBig'
	return b
end 'pickLarger'

function main() returns ExitCode
	var s = NumberSorter.create()
	let winner = s.compareAndSwap(10, b: 25, cmp: pickLarger)
	if winner == 25 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```



<!-- test: first-class-function.field-call -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A function stored in a struct field is called through the field. The field holds a
function pointer, so this is an indirect call — the same lowering a function-typed
parameter gets, reached from a different producer.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let h = Handler.create(double)
	return h.op(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-bind -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Binding a function-typed field to a local recovers the field's declared signature, so
the local is callable.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let h = Handler.create(double)
	let f = h.op
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-pass-and-return -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A function-typed field is an ordinary value: it can be passed as an argument and
returned from a function.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function pick(h Handler) returns UnaryOp
	return h.op
end 'pick'

function main() returns ExitCode
	let h = Handler.create(double)
	let viaArg = apply(h.op, x: 10)
	let returned = pick(h)
	return viaArg + returned(11)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-self-dispatch -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A method dispatches through its own function-typed field with `self.op(...)`.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'

	function run(x Integer) returns Integer
		return self.op(x)
	end 'run'
end 'Handler'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	let h = Handler.create(triple)
	return h.run(14)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-nested-receiver -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The receiver of a field call may itself be reached through a field chain.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

type Outer
	export var inner as Handler

	static function create(inner Handler) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let o = Outer.create(Handler.create(double))
	return o.inner.op(21)
end 'main'
```
```exitcode
42
```

<!-- disabled-test: first-class-function.field-void-statement -->
<!-- P1.7 (Array/for-in) -->
A field whose signature returns nothing is called as a statement, with no result to
bind. This is the shape a table of handlers or compiler passes keyed by a struct field
takes: each entry stores a function, and driving the table calls it for its effect.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Pass = function(Integer)

type PassEntry
	export var run as Pass

	static function create(run Pass) returns Self
		return Self{run: run}
	end 'create'
end 'PassEntry'

typealias PassArray = Array with PassEntry

function widen(x Integer)
	print("widen {x}\n")
end 'widen'

function narrow(x Integer)
	print("narrow {x}\n")
end 'narrow'

function main() returns ExitCode
	var passes = PassArray.create()
	passes.push(PassEntry.create(widen))
	passes.push(PassEntry.create(narrow))

	for p in passes 'drive'
		p.run(7)
	end 'drive'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
widen 7
narrow 7
```

<!-- test: first-class-function.call-returned-function -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A call whose return type is a function is itself callable, so the result can be called
without binding it first.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

function pick() returns UnaryOp
	return double
end 'pick'

function main() returns ExitCode
	return pick()(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-cross-file -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A function-typed field declared in another file is called the same way: the field's
signature travels with the type, not with the file that reads it.
```maxon
// --- file: handler.maxon
export typealias Integer = int(i64.min to i64.max)
export typealias UnaryOp = function(Integer) returns Integer

export type Handler
	export var op as UnaryOp

	export static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'

	export function run(x Integer) returns Integer
		return self.op(x)
	end 'run'
end 'Handler'

// --- file: main.maxon
function double(x Integer) returns Integer
	return x * 2
end 'double'

function pick(h Handler) returns UnaryOp
	return h.op
end 'pick'

function main() returns ExitCode
	let h = Handler.create(double)
	let viaField = h.op(10)
	let viaSelf = h.run(5)
	let f = pick(h)
	return viaField + viaSelf + f(1)
end 'main'
```
```exitcode
32
```

<!-- test: first-class-function.non-capturing-closure-in-field -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A closure that captures NOTHING is a plain function reference — it has no environment to
lose — so it is stored in a function-typed field and called like any other. This is the
half of the boundary that must keep working: it is what a table of handlers is built from.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function main() returns ExitCode
	let h = Handler.create(function(n Integer) gives n * 2)
	return h.op(21)
end 'main'
```
```exitcode
42
```

### Capturing closures may not escape their defining frame

A closure captures **by reference**: its environment holds the ADDRESSES of the enclosing
frame's stack slots, so that a read through a capture sees later mutations of the captured
variable. The environment is therefore meaningful only while that frame is alive. Let a
capturing closure outlive its frame and every captured read dereferences a dead frame — the
classic upward-funarg problem, which compiles clean and dies at runtime.

So: **a closure that CAPTURES may not ESCAPE its defining frame.** E3099 refuses every
escape route the compiler can see without interprocedural analysis — returning one out of
the frame that built it, and storing one in a struct field, a global, a container element,
or a union's associated-value payload. Each of the latter is one 8-byte slot holding the
code pointer alone, and each is heap memory outliving every frame, so the store drops the
environment, the call passes env=0, and the first captured read dereferences null.

A closure that captures NOTHING is unaffected and passes every check **by construction** —
it lowers to a plain function reference and has no environment to lose. Nothing is carved
out for it. The accept cases below pin the other half of the boundary: over-rejection would
be the worse failure, and a capturing closure passed *down* to a callee that only CALLS it
is perfectly safe.

Making the refused routes work needs escape analysis and by-value capture, which would
change the by-reference capture semantics the closure tests above pin. It is deliberately
deferred to shv2's P1.5, where it co-lands with `async` — a green-thread capture IS an
escape. One route is deliberately left open for the same reason: a capturing closure passed
as a CALL ARGUMENT to a callee that then stores it. At that store the value is a
*parameter*, and whether it carries an environment is a fact about the CALLER, so deciding
it needs a per-parameter escape summary propagated over the call graph.

### The rule keys on the VALUE, so it must ride every re-mint of one

"Carries an environment" is recorded against the closure's SSA value. Reading a function-typed
variable in a block other than the one that bound it mints a NEW SSA value for the same
function, so a read that does not carry the mark across ERASES it — and the escape check stops
applying to a closure that still very much has an environment. Returning a capturing closure
from a block other than the one that bound it used to compile clean and nil-deref for exactly
that reason.

### A ternary is not an escape route — it is worse, and it is refused

A conditional expression merges its two arms through **one slot**, and that slot holds the code
pointer alone. An environment reaches a callee either as the SSA value its `closure_create`
produced, or through the `__env_<name>` slot the lowering pairs with a function PARAMETER — and
a ternary's result temp is neither. So the environment is dropped on EVERY path, whether or not
the result ever leaves the frame: `let h = f if c else dbl` then `h(2)` compiled and died in
`_$closure_0` without escaping anything.

That makes it a fact the merge cannot carry rather than merely a place the closure must not go,
so a capturing closure is refused as an ARM of a ternary. Refusing there also closes the escape
routes through one, since a capturing closure can no longer reach a return, a global or a field
by way of a conditional expression either. It inherits the boundary above exactly: a capturing
closure that arrived as a PARAMETER is not recognizable as one without an escape summary, and is
not refused here.

<!-- test: first-class-function.capturing-closure-in-field-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A capturing closure stored into a function-typed FIELD is refused rather than miscompiled.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	var h = Handler.create(double)
	let bump = 20
	h.op = function(n Integer) gives n + bump
	return h.op(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-field-errors.test:21:4: cannot store a closure that captures in field 'op' of 'Handler': captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-struct-construction-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A capturing closure stored into a function-typed field at CONSTRUCTION (`Self{op: closure}`) is the
same heap store as `x.op = closure`, but it reaches the box through a different path — the struct
literal, not `emitFieldWrite`. Without a check HERE the construction slipped past the escape gate and
the closure reached lowering, which panicked on its unresolved environment. It is refused with the same
E3099 the field-write route gives, at the field name in the literal.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function trap(bump Integer) returns Self
		return Self{op: function(n Integer) gives n + bump}
	end 'trap'
end 'Handler'

function main() returns ExitCode
	let h = Handler.trap(20)
	return h.op(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-struct-construction-errors.test:10:15: cannot store a closure that captures in field 'op' of 'Handler': captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-returned-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
RETURNING a capturing closure is the idiom people actually write, and it is the route that
matters most: `makeAdder` compiles clean and then nil-derefs inside `_$closure_0`, because
the environment it hands back points at `makeAdder`'s dead frame.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function makeAdder(bump Integer) returns UnaryOp
	let f = function(n Integer) gives n + bump
	return f
end 'makeAdder'

function main() returns ExitCode
	let add = makeAdder(20)
	return add(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-returned-errors.test:8:2: cannot return a closure that captures: captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-global-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A global outlives every frame, so a capturing closure stored in one would dangle — but the
program never gets that far, and the reason is worth stating exactly, because this test used to
claim otherwise.

A global cannot BE function-typed: it takes no type annotation (`var handler UnaryOp = dbl` is a
parse error) and a function reference is not a constant initializer, so `handler` here is an
`int`. The value therefore fails the TYPE rule before the escape rule is ever asked, and E3005 is
the honest answer: it is the one whose advice works. E3099's — "use a function reference" —
does NOT fix this program, because `handler = dbl` is refused by the very same type rule.

So the escape rule's GLOBAL route is unreachable while globals cannot hold a function, and the
rule is carried by the routes that CAN: a struct field, a container, a union payload, a payload
binding, a return, and a ternary arm — each with its own test above.
```maxon

typealias Integer = int(i64.min to i64.max)

var handler = 0

function main() returns ExitCode
	let bump = 20
	handler = function(n Integer) gives n + bump
	return 42
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-global-errors.test:9:2: cannot assign a value of type 'function' to global 'handler', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- disabled-test: first-class-function.capturing-closure-in-container-errors -->
<!-- P1.5-A2 (closures + escape) -->
A CONTAINER's element block is heap memory that outlives the frame, so a capturing closure
put into an array or map literal is refused at the element that carries the environment.
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let bump = 20
	let ops = [double, function(n Integer) gives n + bump]
	return 42
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-container-errors.test:11:56: cannot put a closure that captures in a container: captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-union-payload-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A union's associated-value PAYLOAD is a heap box holding one slot per value, so it drops the
environment exactly as a struct field does. Without this check the union compiles, runs, and
carries a closure whose environment is gone.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

union Action
	run(op UnaryOp)
	idle
end 'Action'

function make(bump Integer) returns Action
	return Action.run(function(n Integer) gives n + bump)
end 'make'

function main() returns ExitCode
	let a = make(20)
	match a 'go'
		run then return 42
		idle then return 1
	end 'go'
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-union-payload-errors.test:12:54: cannot store a closure that captures in payload 'op' of case 'run': captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- disabled-test: first-class-function.capturing-closure-in-payload-binding-errors -->
<!-- shv2 payload bindings are IMMUTABLE frame-local COPIES, not the reference's mutable heap ALIASES. `run(op) then op = …` is refused E2013 ("cannot assign to immutable variable") — a STRONGER rejection that already forbids the store; shv2 never writes back through a payload binding, so the E3099 escape route (an alias into the heap box) does not exist here. Emitting E3099 would require reintroducing mutable payload bindings — the exact write-back unsafety the reference's check guards. shv2's model is safer; the store is refused either way. Its own rung (a payload-binding-mutability decision), NOT A2b. -->
A payload binding LOOKS like a plain local, but it is an alias INTO the enum's heap box:
assigning through it writes back. So it is a heap store outliving the frame, not the
frame-local assignment it resembles — and without this check it compiles, runs, and leaves
the box holding a closure whose environment is gone.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

union Action
	run(op UnaryOp)
	idle
end 'Action'

function main() returns ExitCode
	let bump = 20
	var a = Action.run(double)
	match a 'go'
		run(op) then op = function(n Integer) gives n + bump
		idle then return 1
	end 'go'
	return 42
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-payload-binding-errors.test:19:16: cannot store a closure that captures in payload binding 'op': captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-used-in-frame -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The ACCEPT side: a capturing closure called directly AND passed DOWN to a callee that only CALLS it. The
env travels with the value across the call boundary — `apply` receives a companion environment parameter,
and its `f(x)` threads it — so `apply(f, …)` returns the captured `bump` correctly. A callee that only
calls the closure never persists it, so the pass-down is safe (the reject twins below cover a callee that
STORES or RETURNS it).
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let direct = f(2)
	return apply(f, x: 20) + direct
end 'main'
```
```exitcode
62
```

<!-- test: first-class-function.capturing-closure-stored-by-callee-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The INTERPROCEDURAL escape reject (P1.5-A2b-2). Pass-down to a callee that only CALLS the closure is safe
(above), but a callee that PERSISTS it — here `Handler.create` STORES the parameter into a struct field —
keeps only the fn-ptr; the captured environment belongs to `main`'s frame and dangles the moment `main`
returns (a use-after-free, the OPEN #13 interprocedural escape that A2b-1 blocked at the direct routes and
A2b-2 must re-close now that pass-down is allowed). The whole-program escape summary marks
`Handler.create`'s parameter escaping (its body stores it), so the capturing closure passed to it is
refused. A PLAIN function reference at the same position is fine (no env to lose) — see `field-*`. This
compiled clean and SEGFAULTED (139) until the escape summary landed.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	export static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let h = Handler.create(f)
	return h.op(21)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-stored-by-callee-errors.test:17:10: cannot pass a closure that captures to 'Handler.create', which stores or returns it: captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-returned-by-callee-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The other interprocedural escape route: a callee that RETURNS the parameter (`identity(f) → return f`)
persists it past the passing frame exactly as a store does — the returned fn-ptr outlives `main`'s frame,
whose environment the closure captured. The escape summary marks `identity`'s parameter escaping (its body
returns it), so the capturing closure is refused; a plain function reference returned the same way is fine.
This compiled clean and SEGFAULTED (139) until the escape summary landed.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function identity(f UnaryOp) returns UnaryOp
	return f
end 'identity'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let g = identity(f)
	return g(21)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-returned-by-callee-errors.test:13:10: cannot pass a closure that captures to 'identity', which stores or returns it: captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-called-from-nested-block -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The same closure, called from a DIFFERENT block of the same frame. This is not an escape and
must not be confused with one: the call happens inside `main`, the frame is alive, and nothing
outlives anything — the only thing that changed is which block the call sits in.

It is a separate test from `capturing-closure-used-in-frame` because that one calls `f` in the
block that binds it, and a same-block call reuses the SSA value the `closure_create` produced,
which still knows its environment. Crossing a block boundary forces the value to be re-read from
the variable, and THAT is the route that was broken: the environment reached a callee only from
that SSA value or from a slot the lowering paired with a PARAMETER, so a capturing closure bound
to a LOCAL was called with an environment of 0 and nil-dereffed inside `_$closure_0`. Both arms
of the `if` are exercised so neither the taken nor the untaken path can hide it.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 5
	let f = function(n Integer) gives n + bump
	var total = 0 as Integer

	total = total + f(1)

	if total > 0 'taken'
		total = total + f(10)
	end 'taken'

	var i = 0 as Integer
	while i < 2 'loop'
		total = total + f(100)
		i = i + 1
	end 'loop'

	return total as ExitCode
end 'main'
```
```exitcode
231
```

<!-- test: first-class-function.capturing-closure-bound-outside-loop-called-inside -->
<!-- targets: wasm32-wasi -->
<!-- x64-windows OMITTED: this exact two-int interpolation in a loop (`print("i={i} -> {r}\n")` + `acc = acc + r`) needs 16 simultaneously-live values > the 14-register pool (E5001) — a PRE-EXISTING x64 register-allocator / interpolation-lowering limit PROVEN closure-independent (the same body with a plain `let r = i + 5` instead of a closure E5001s identically). wasm's stack machine has no register cap, so the env-drop-timing probe this test exists for runs and passes there; x64 env-drop-timing is covered by `-rebound-in-loop` and `-called-from-nested-block`, both green on x64. Its own interpolation-pressure rung. -->
The shape people actually write, and the one the two tests around it both miss: the closure is
bound OUTSIDE the loop and called INSIDE it, so ONE environment must survive being read on many
iterations. `capturing-closure-rebound-in-loop` binds afresh each iteration and never carries an
environment across one; `capturing-closure-called-from-nested-block` crosses a block but not a
scope_end that runs repeatedly.

This asserts the VALUES, not just a clean exit, because the failure it guards is a
USE-AFTER-FREE and a freed block is not immediately a wrong one: every `maxon.scope_end` sweeps
every orphan temp in the whole function, so the loop body's scope_end released an environment
the enclosing scope still owned. The first read after the free still found the old bytes and
answered correctly; the reads after `print` had recycled the block returned garbage that CHANGED
per iteration. Exactly two correct iterations, then nonsense.

⚠ A leak gate cannot see this. `mm_alloc == mm_free` balances perfectly here — the block IS
freed, exactly once, just far too early. Freed-too-early and never-freed are different faults,
and only one of them is a leak. `print` is load-bearing: it churns the heap, which is what turns
a silent read of dead memory into a visible wrong answer.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 5 as Integer
	let f = function(n Integer) gives n + bump
	var i = 0 as Integer
	var acc = 0 as Integer

	while i < 5 'loop'
		let r = f(i)
		print("i={i} -> {r}\n")
		acc = acc + r
		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```exitcode
35
```
```stdout
i=0 -> 5
i=1 -> 6
i=2 -> 7
i=3 -> 8
i=4 -> 9
```

<!-- test: first-class-function.capturing-closure-bound-outside-loop-passed-down -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The same environment, read across iterations through a CALLEE rather than directly. The callee
receives the closure as a parameter, so its environment arrives as the caller's — borrowed for
the length of the call. The callee must not release it: its own scope_end cleans a parameter
named just like a binding, and treating the two alike would free the CALLER's live environment
on the first call and leave every later iteration reading dead memory.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let bump = 5 as Integer
	let f = function(n Integer) gives n + bump
	var i = 0 as Integer
	var acc = 0 as Integer

	while i < 5 'loop'
		acc = acc + apply(f, x: i)
		i = i + 1
	end 'loop'

	return acc as ExitCode
end 'main'
```
```exitcode
35
```

<!-- disabled-test: first-class-function.capturing-closure-passed-to-try-call -->
<!-- BLOCKED by STRING-BACKED error-enum raw values, NOT by env threading. `enum ApplyError implements Error / negative = "n must not be negative"` needs a string-backed enum case, which shv2 does not parse yet (E2015 "only integer and float raw values are parsed; string/char/struct/function backings arrive with later rungs") — an orthogonal later rung. The try-call env threading THIS case exists to test IS implemented and verified (lowerTryCall → finalizeCallArgs → appendCalleeEnvArgs; probed green with an int-backed error enum). Enable when string-backed enum raw values land. -->
A capturing closure handed to a THROWING callee, through `try`. A try-call flattens its arguments
exactly as a plain call does, but it was never given the maps that say what environment a function
value carries, so it could only answer 0 — and this failed in ANY block, including the one that
bound the closure, which is why no loop is needed to show it.

That the plain-call and try-call paths could disagree at all is the point: both ask "what
environment travels with this value?", and the answer is now written once, in one helper, rather
than once per call shape.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

enum ApplyError implements Error
	negative = "n must not be negative"
end 'ApplyError'

function applyChecked(f UnaryOp, n Integer) returns Integer throws ApplyError
	if n < 0 'guard'
		throw ApplyError.negative
	end 'guard'

	return f(n)
end 'applyChecked'

function main() returns ExitCode
	let bump = 5 as Integer
	let f = function(n Integer) gives n + bump

	let ok = try applyChecked(f, n: 10) otherwise 0
	let bad = try applyChecked(f, n: -1) otherwise 99

	return (ok + bad) as ExitCode
end 'main'
```
```exitcode
114
```

<!-- test: first-class-function.capturing-closure-rebound-in-loop -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A closure bound afresh on every iteration and called from a block NESTED inside that loop. The
environment is per-iteration, so each call must see its OWN `bump` rather than the first or the
last — a single environment slot reused across iterations would still pass the cross-block test
above while quietly reading a stale frame here.

It also pins the refcount discipline: the variable's environment slot is a BORROWED alias, and
the reference stays owned by the temp the `closure_create` allocated. Five iterations therefore
allocate five environments and free five — an owning alias would double-free them, and a second
incref would leak them.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	var total = 0 as Integer
	var i = 0 as Integer

	while i < 5 'l'
		let bump = i * 10
		let f = function(n Integer) gives n + bump

		if true 'inner'
			total = total + f(1)
		end 'inner'

		i = i + 1
	end 'l'

	return total as ExitCode
end 'main'
```
```exitcode
105
```

<!-- disabled-test: first-class-function.capturing-closure-name-not-leaked -->
<!-- blocked by a PRE-EXISTING borrowed-struct-copy-return limit, unrelated to closures: `storeIt` does `var hh = h; hh.op = op; return hh`, and returning a re-borrowed struct param is refused E2015 ("returning a borrowed struct value … arrives at P1.4b") — shv2 has no struct copy/clone. The test's OWN property (the escape check keys on the VALUE, so an unrelated param named `op` is NOT falsely flagged) IS verified: `hh.op = op` compiles PAST the field-store escape check (op is a plain param, not marked) and reaches the E2015 at the return — a false-flag would have thrown E3099 at the store instead. Unblocks with struct-copy-return (P1.7-adjacent). -->
The check keys on the VALUE, not the name. A variable name means nothing outside the
function that declared it, so a capturing `op` in one function must not make an unrelated
parameter `op` in another look like it carries an environment — that would be a FALSE
rejection, and over-rejection is the worse failure. Here `storeIt` stores a plain function
reference through a parameter that happens to share the name.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function double(x Integer) returns Integer
	return x * 2
end 'double'

function usesClosure(bump Integer) returns Integer
	let op = function(n Integer) gives n + bump
	return op(1)
end 'usesClosure'

function storeIt(h Handler, op UnaryOp) returns Handler
	var hh = h
	hh.op = op
	return hh
end 'storeIt'

function main() returns ExitCode
	let h = Handler.create(double)
	let h2 = storeIt(h, op: double)
	return h2.op(20) + usesClosure(1)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.non-capturing-closure-returned -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A closure that captures NOTHING is returned freely: it lowers to a plain function reference
and has no environment to lose. This is what keeps `makeAdder`'s refusal narrow — the check
keys on carrying an environment, not on being a closure.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function pick() returns UnaryOp
	return function(n Integer) gives n * 2
end 'pick'

function main() returns ExitCode
	let f = pick()
	return f(21)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.non-capturing-closure-in-union-payload -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The union payload's accept side: a non-capturing closure rides in an associated value and is
matched back out.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

union Action
	run(op UnaryOp)
	idle
end 'Action'

function make() returns Action
	return Action.run(function(n Integer) gives n + 1)
end 'make'

function main() returns ExitCode
	let a = make()
	match a 'go'
		run then return 42
		idle then return 1
	end 'go'
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.capturing-closure-in-ternary-arm-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A capturing closure as a ternary ARM is refused. This one also RETURNS the result, which is the
shape that made the defect visible: it compiled clean and nil-dereffed inside `_$closure_0` —
the exact failure the escape rule exists to prevent, reached by laundering the closure through a
merge the rule could not see.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function makeAdder(bump Integer) returns UnaryOp
	let inc = function(n Integer) gives n + bump
	let dbl = function(n Integer) gives n * 2
	return inc if bump > 0 else dbl
end 'makeAdder'

function main() returns ExitCode
	let f = makeAdder(20)
	return f(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-arm-errors.test:9:13: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-ternary-to-global-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The same laundering, into a GLOBAL. Through the ternary this was an internal `StdPtr`/`StdI64`
cast crash at lowering rather than a diagnostic.
```maxon

typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

var handler = 0

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	handler = f if bump > 0 else dbl
	return 42
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-to-global-errors.test:14:14: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-ternary-used-in-frame-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Refused even when the result NEVER LEAVES THE FRAME. This is what makes the merge different
from an escape route: the environment is dropped by the merge itself, so `h(22)` here called a
closure with `env=0` and nil-dereffed without escaping anything.
```maxon

typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let h = f if bump > 0 else dbl
	return h(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-used-in-frame-errors.test:12:12: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-match-arm-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The REACHABLE twin of the ternary-arm refusal above: `a if c else b` is not parsed yet, but a match
EXPRESSION is, and it merges its `gives` arms through the same single result phi. A capturing closure as
an arm carries only its code pointer through that phi (the env rides a side column a phi cannot merge),
so the merged closure would be called with no environment. Without this check it compiled clean and
nil-dereferenced inside `_$closure_0`, exactly as the ternary case describes.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let sel = 1 as Integer
	let h = match sel 'pick'
		1 gives f
		default gives dbl
	end 'pick'
	return h(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-match-arm-errors.test:15:11: cannot use a closure that captures as a `gives` arm of a match expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-otherwise-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The other reachable merge: `try call() otherwise <value>` joins the success value and the fallback
through one result phi. A capturing closure as the `otherwise` fallback would be called with no
environment on the error edge, so it is refused — regardless of whether the call actually throws, since
the parser cannot know which edge runs. The try's SUCCESS value cannot itself be a capturing closure (a
function may not return one), so only the fallback needs guarding.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

enum E implements Error
	bad = 1
end 'E'

function pick(x Integer) returns UnaryOp throws E
	if x < 0 'g'
		throw E.bad
	end 'g'
	return dbl
end 'pick'

function main() returns ExitCode
	let bump = 20
	let f = function(n Integer) gives n + bump
	let h = try pick(-1) otherwise f
	return h(22) as ExitCode
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-otherwise-errors.test:24:33: cannot use a closure that captures as the value of an `otherwise` fallback: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-returned-from-other-block-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The escape rule keys on the VALUE, and a cross-block read mints a new one. Returning the closure
from a block other than the one that bound it must still be refused — this compiled clean and
nil-dereffed, because the read that crossed the block boundary dropped the "has an environment"
mark and the check had nothing left to fire on.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function makeAdder(bump Integer) returns UnaryOp
	let f = function(n Integer) gives n + bump
	if bump > 0 'guard'
		return f
	end 'guard'
	return dbl
end 'makeAdder'

function main() returns ExitCode
	let add = makeAdder(20)
	return add(22)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-returned-from-other-block-errors.test:13:3: cannot return a closure that captures: captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.non-capturing-closure-through-ternary -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The accept side of the ternary rule. A closure that captures NOTHING has no environment to
lose, so it merges through a ternary like any other function value — and the merged result is
callable, which requires the signature to have survived the merge.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let c = 1
	let twice = function(n Integer) gives n * 2
	let thrice = function(n Integer) gives n * 3
	let h = twice if c > 0 else thrice
	return h(21)
end 'main'
```
```exitcode
42
```

### A function value only fits a function-typed place

A function value is assignable to a place declared with a function `typealias`, and to nothing
else. This is a TYPE rule, and it is deliberately NOT the escape rule above: it fires for a
plain top-level function that captures nothing and escapes nowhere, and it would fire just the
same if closures did not exist. "May this value be represented here?" and "may this value
OUTLIVE here?" are separate questions, and a place can fail either, both, or neither.

<!-- test: first-class-function.function-value-into-int-global-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
Unchecked, this store reached the LOWERING, where a function pointer and an integer slot have
different representations and the cast between them failed as an E9001 internal error — quoting
a .NET type name, naming no source position, and describing no defect in the program. An
internal error is by definition a compiler bug when a user program can provoke it.
```maxon

typealias Integer = int(i64.min to i64.max)

var slot = 0 as Integer

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	slot = dbl
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.function-value-into-int-global-errors.test:12:2: cannot assign a value of type 'function' to global 'slot', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.function-value-into-int-local-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The same rule for a LOCAL. This one was worse than an internal error: with the value never
read, the whole program compiled CLEAN and the mismatch was never reported at all.
```maxon

typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	var loc = 0 as Integer
	loc = dbl
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.function-value-into-int-local-errors.test:11:2: cannot assign a value of type 'function' to variable 'loc', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.capturing-closure-into-int-local-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A CLOSURE into the same int local. It is the type rule that answers, not the escape rule: the
value never leaves the frame, so there is no escape to report — it simply does not fit.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 5 as Integer
	var loc = 0 as Integer
	loc = function(n Integer) gives n + bump
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.capturing-closure-into-int-local-errors.test:8:2: cannot assign a value of type 'function' to variable 'loc', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.function-value-returned-as-int-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The RETURN position reaches the same mismatch by a different road: the return check consulted
the numeric widening table directly, and that table answers only for numeric kinds, so a
function kind fell off the end of it as an E9001 "Unhandled cast combination: Function ->
Integer". The correctly worded type error was already written one line below it.
```maxon

typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function bad() returns Integer
	return dbl
end 'bad'

function main() returns ExitCode
	return bad() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.function-value-returned-as-int-errors.test:10:2: Cannot return 'function' from function declared to return 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.function-value-as-arg-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The CALL-ARGUMENT position reaches the same type rule through `SemanticCheck.checkArgTypes`. Without a
test here a sabotage of the `functionIntoNonFunction` routing at that site stays green — the condition
is single-homed (`checkDeclaredType`) but this ROUTE was pinned by nothing (OPEN.md #69). The bootstrap
also rejects this E3005-class.
```maxon

typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function takesInt(n Integer) returns Integer
	return n
end 'takesInt'

function main() returns ExitCode
	return takesInt(dbl) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.function-value-as-arg-errors.test:14:9: cannot pass a value of type 'function' as argument 'n', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.function-value-into-field-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The FIELD-STORE position (`b.slot = dbl`, a struct field holding a non-function). The other unpinned
route of OPEN.md #69 — a sabotage of the field-store `functionIntoNonFunction` arm stayed green without
this. The bootstrap also rejects this E3005-class.
```maxon

typealias Integer = int(i64.min to i64.max)

type Box
	export var slot as Integer

	static function create() returns Self
		return Self{slot: 0}
	end 'create'
end 'Box'

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function main() returns ExitCode
	var b = Box.create()
	b.slot = dbl
	return b.slot as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.function-value-into-field-errors.test:19:4: cannot assign a value of type 'function' to field 'slot', which holds 'int': a function value is only usable where a function type declared with 'typealias' is expected
```

<!-- test: first-class-function.throwing-function-as-value-errors -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
A **`throws` function cannot be taken as a value.** A function type has no throws clause — the
grammar is `function(T) returns U` — so the binding would drop it, and there is no channel to carry
it back: `StdIndirectCallOp` has no error flag, unlike `StdTryCallOp`.

Before this check the indirect call was not merely unchecked, it was **wrong**. `risky(99)` took its
error return (ordinal in RDX, `xor rax, rax` in RAX); the caller read RAX, ignored RDX, and received
**0 — the dummy — as a normal result**. `try` was bypassed entirely by round-tripping the function
through a value.

It is refused rather than supported on evidence: no spec, no stdlib file, and none of
maxon-selfhosted's 191,487 lines ever wanted a throwing function value.
```maxon
typealias Num = int(0 to 1000)

enum Err implements Error
	bad
end 'Err'

function risky(a Num) returns Num throws Err
	if a > 5 'guard'
		throw Err.bad
	end 'guard'
	return a + 1
end 'risky'

function main() returns ExitCode
	let f = risky
	let r = f(99)
	print("unreachable: {r}")
	return 0
end 'main'
```
```maxoncstderr
error E3101: specs/fragments/first-class-functions/first-class-function.throwing-function-as-value-errors.test:16:10: Cannot use throwing function 'risky' as a value: it throws 'Err', and a function type cannot express 'throws'. Wrap the call in a non-throwing function that handles the error with 'try'.
```

<!-- test: first-class-function.non-throwing-function-as-value-still-works -->
<!-- targets: x64-windows, x64-linux, wasm32-wasi -->
The guard above is about `throws` and nothing else — an ordinary function value is untouched.
```maxon
typealias Num = int(0 to 200)
typealias Op = function(Num) returns Num

function double(a Num) returns Num
	return a * 2
end 'double'

function apply(f Op, x Num) returns Num
	return f(x)
end 'apply'

function main() returns ExitCode
	let f = double
	return apply(f, x: 21)
end 'main'
```
```exitcode
42
```

### Float function values carry the callee's float result and param types (P1.5 #78)

A function VALUE returns and takes floats exactly as a direct call does. The indirect-call
lowering carries the function value's own signature, so a float RESULT is captured from the
float return register (xmm0/d0, or a wasm f64 result) instead of the integer one, and a float
ARGUMENT travels in a float argument register (its own separate counter) instead of a GPR. An
integer function value is untouched — the tests above are the no-regression anchor.

<!-- test: first-class-function.float-return-called-indirectly -->
<!-- targets: x64-windows, wasm32-wasi -->
A function value that RETURNS a float, called indirectly. Before #78 the indirect call assumed
an integer result and captured xmm0's value from the integer return register, which colored a
move across register files (x64: `r8` → `xmm0` panic; wasm: an `i64` → `f64` coerce panic).
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias FloatFn = function() returns Ratio

function getVal() returns Ratio
	return 3.75
end 'getVal'

function callIt(f FloatFn) returns Ratio
	return f()
end 'callIt'

function main() returns ExitCode
	let fn = getVal
	let r = callIt(fn)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: first-class-function.float-param-called-indirectly -->
<!-- targets: x64-windows, wasm32-wasi -->
A function value that TAKES a float parameter, called indirectly through its `__fnref_` thunk.
The float argument travels in a float argument register, and the thunk types its forwarded
parameter as the target's real float type so the register files agree caller-to-callee.
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias ScaleFn = function(Ratio) returns Ratio

function scale(x Ratio) returns Ratio
	return x * 2.0
end 'scale'

function apply(f ScaleFn, v Ratio) returns Ratio
	return f(v)
end 'apply'

function main() returns ExitCode
	let fn = scale
	let r = apply(fn, v: 10.5)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
21
```

<!-- test: first-class-function.mixed-int-float-params-indirect -->
<!-- targets: x64-windows, wasm32-wasi -->
An INT parameter followed by a FLOAT one, called indirectly: the int rides a GPR argument
register and the float rides a float one, each on its own counter, so the callee reads each
back from the file the caller wrote it to.
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias Count = int(0 to 1000)
typealias MixFn = function(Count, Ratio) returns Ratio

function combine(n Count, x Ratio) returns Ratio
	if n > 0 'pos'
		return x * 2.0
	end 'pos'
	return x
end 'combine'

function apply(f MixFn, n Count, x Ratio) returns Ratio
	return f(n, x)
end 'apply'

function main() returns ExitCode
	let fn = combine
	let r = apply(fn, n: 3, x: 5.25)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: first-class-function.float-then-int-params-indirect -->
<!-- targets: x64-windows, wasm32-wasi -->
The reverse order — a FLOAT parameter followed by an INT — proves the separate int/float
argument counters, not a shared positional one: a shared counter would put the float in float
slot 0 but the int in GPR slot 1, and the callee reads the int from GPR slot 0.
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias Count = int(0 to 1000)
typealias MixFn = function(Ratio, Count) returns Ratio

function combine(x Ratio, n Count) returns Ratio
	if n > 1 'pos'
		return x * 4.0
	end 'pos'
	return x
end 'combine'

function apply(f MixFn, x Ratio, n Count) returns Ratio
	return f(x, n)
end 'apply'

function main() returns ExitCode
	let fn = combine
	let r = apply(fn, x: 2.5, n: 5)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: first-class-function.float-closure-param-and-return -->
<!-- targets: x64-windows, wasm32-wasi -->
A closure (no `__fnref_` thunk) taking and returning a float, called indirectly: the lifted
closure already declares its float parameter, so the caller's float-argument routing must agree
with the closure's own float-parameter capture.
```maxon

typealias Ratio = float(0.0 to 1000.0)

function main() returns ExitCode
	let f = function(x Ratio) gives x * 2.0
	let r = f(10.5)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
21
```
