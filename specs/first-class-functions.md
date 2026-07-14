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

<!-- test: first-class-function.cross-file-extension-typealias-param -->
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

<!-- test: first-class-function.field-void-statement -->
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

## Capturing closures may not escape their defining frame

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

<!-- test: first-class-function.capturing-closure-in-field-errors -->
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

<!-- test: first-class-function.capturing-closure-returned-errors -->
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
A GLOBAL outlives every frame, so a capturing closure stored in one dangles by definition.
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-global-errors.test:9:2: cannot store a closure that captures in global 'handler': captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-container-errors -->
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

<!-- test: first-class-function.capturing-closure-in-payload-binding-errors -->
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
The ACCEPT side, and the ordinary case: a capturing closure that stays inside its own frame.
It may be called directly, and it may be passed DOWN to a callee that only CALLS it — the
frame is still alive for the whole of that callee's execution, so the environment is live.
Over-rejecting this would be the worse failure.
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

<!-- test: first-class-function.capturing-closure-name-not-leaked -->
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
