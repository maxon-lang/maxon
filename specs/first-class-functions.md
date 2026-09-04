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

<!-- test: first-class-function.bind-to-a-second-name -->
⚠ **THE `print` BETWEEN THE BINDINGS IS LOAD-BEARING.** A same-block read of a function binding reuses
the SSA value and emits NO op, so a rule that recovers the signature from the LAST OP emitted answers
about whichever statement happens to precede the read. `let g = f` sitting right after `f`'s own
binding hides that — the previous op is then `f`'s assignment, which names a function. With a
statement in between, the last op is the `print` call and the only honest source left is the binding
itself. Both spellings are held here so neither can pass on the other's evidence.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 5
	let f = function(x Integer) gives x * bump
	let g = f
	print("bound\n")
	let h = g
	return h(8)
end 'main'
```
```stdout
bound
```
```exitcode
40
```

<!-- test: first-class-function.reassign-from-a-second-name -->
The assignment door asks the same question as the binding door and gets the same answer: a named
function value carries its signature on its binding, wherever the read sits.
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
	let h = triple
	print("go\n")
	f = h
	return f(10)
end 'main'
```
```stdout
go
```
```exitcode
30
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

<!-- test: first-class-function.capturing-closure-called-from-nested-block -->
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

<!-- test: first-class-function.capturing-closure-passed-to-try-call -->
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

<!-- test: first-class-function.capturing-closure-in-ternary-arm-errors -->
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-arm-errors.test:9:13: cannot use a closure that captures as an arm of a conditional expression: the two arms merge through a single slot, which carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-ternary-to-global-errors -->
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-to-global-errors.test:14:14: cannot use a closure that captures as an arm of a conditional expression: the two arms merge through a single slot, which carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-ternary-used-in-frame-errors -->
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-used-in-frame-errors.test:12:12: cannot use a closure that captures as an arm of a conditional expression: the two arms merge through a single slot, which carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-returned-from-other-block-errors -->
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

## A function value only fits a function-typed place

A function value is assignable to a place declared with a function `typealias`, and to nothing
else. This is a TYPE rule, and it is deliberately NOT the escape rule above: it fires for a
plain top-level function that captures nothing and escapes nowhere, and it would fire just the
same if closures did not exist. "May this value be represented here?" and "may this value
OUTLIVE here?" are separate questions, and a place can fail either, both, or neither.

<!-- test: first-class-function.function-value-into-int-global-errors -->
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

<!-- test: first-class-function.throwing-function-as-value-errors -->
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

### A function type's identity is its SHAPE, and a kind cannot express it

A function value's type is `function(P...) returns R`. Two function types are the same type only
when their shapes are — same parameters, in order, and the same return. Everywhere a function value
meets a declared function type — a call argument, a `return`, an assignment, a field store, a
struct-literal field initializer, a union payload — that is the comparison the compiler makes.

It cannot be made on the *kind*: every function type is one kind. Compared by kind alone,
`function(Shade) returns Shade` satisfies a declared `function(Color) returns Color`, the callee is
handed a pointer it will call with the wrong argument type, and what comes back is whatever the two
layouts happen to share.

<!-- test: first-class-function.error.wrong-shape-into-function-param -->
The CALL-ARGUMENT door. `apply` declares `f ColorFn` and gets a `function(Shade) returns Shade`.
Both are "a function", so a kind check passes; the program then compiled clean and `apply` called
`shadeIt` with a `Color`, reading `Shade`'s field out of `Color`'s layout — a wrong ANSWER, in
silence, with no diagnostic anywhere.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

function apply(f ColorFn, c Color) returns Color
	return f(c)
end 'apply'

function main() returns ExitCode
	let c = Color.create(3)
	return apply(shadeIt, c: c).v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-into-function-param.test:32:9: argument type mismatch for 'f': expected 'fn(Color) returns Color', got 'fn(Shade) returns Shade'
```

<!-- test: first-class-function.error.wrong-shape-into-struct-literal-field -->
The STRUCT-LITERAL FIELD door — the weakest of them, because it did not merely compare kinds, it
skipped the check entirely unless the field was a numeric primitive. A function-typed field
initializer was therefore never type-checked at all.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

type Holder
	export var op as ColorFn

	export static function create() returns Self
		return Self{op: shadeIt}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	return h.op(Color.create(3)).v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-into-struct-literal-field.test:30:15: cannot assign a value of type 'fn(Shade) returns Shade' to field 'op' of 'Holder', which holds 'fn(Color) returns Color'
```

<!-- test: first-class-function.error.wrong-shape-into-function-local -->
The ASSIGNMENT door. `f`'s type is fixed by its declaration, and an assignment never re-infers it —
the same rule that makes `var x = 5; x = "hi"` an error, applied to the one type a kind cannot name.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

function colorIt(x Color) returns Color
	return Color.create(x.v + 1)
end 'colorIt'

function apply(f ColorFn, c Color) returns Color
	return f(c)
end 'apply'

function main() returns ExitCode
	var f = colorIt
	f = shadeIt
	return apply(f, c: Color.create(3)).v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-into-function-local.test:36:2: cannot assign a value of type 'fn(Shade) returns Shade' to variable 'f', which holds 'fn(Color) returns Color'
```

<!-- test: first-class-function.error.wrong-shape-returned -->
The RETURN door. `pick` declares `ColorFn` and hands back a `ShadeFn`; the caller then calls it
through the signature `pick` promised.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

function pick() returns ColorFn
	return shadeIt
end 'pick'

function main() returns ExitCode
	let f = pick()
	return f(Color.create(3)).v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-returned.test:27:2: Cannot return 'fn(Shade) returns Shade' from function declared to return 'fn(Color) returns Color'
```

<!-- test: first-class-function.error.wrong-shape-into-union-payload -->
The UNION PAYLOAD door. A payload slot has a declared type like any other place.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

union Task
	run(op ColorFn)
end 'Task'

function main() returns ExitCode
	let t = Task.run(shadeIt)
	match t 'go'
		run(op) then return op(Color.create(3)).v
	end 'go'
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-into-union-payload.test:31:26: type mismatch: 'expected fn(Color) returns Color, got fn(Shade) returns Shade'
```

<!-- test: first-class-function.error.wrong-shape-captured-into-closure -->
A CAPTURED function value is the same value, and it keeps its shape across the capture. Every door
above compares the SIGNATURE a value carries, so a value that arrives carrying none is admitted —
the permissive answer, and the right one, because refusing on the compiler's own ignorance refuses
legal programs. That makes each place a function value can be MINTED part of the rule: reading `g`
inside a closure loads it from the closure's environment, and an environment load carried only a
KIND, so the shape `g` was minted with was gone before the call-argument door ever looked. The
identical `apply(g, ...)` this file refuses two cases above therefore compiled clean and RAN — exit
`4`, `shadeIt` handed a `Color` and reading `Shade`'s field out of `Color`'s layout.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

type Shade
	export var s as Integer

	export static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Shade'

typealias ColorFn = function(Color) returns Color
typealias Thunk = function() returns Integer

function shadeIt(x Shade) returns Shade
	return Shade.create(x.s + 1)
end 'shadeIt'

function apply(f ColorFn, c Color) returns Color
	return f(c)
end 'apply'

function runThunk(t Thunk) returns Integer
	return t()
end 'runThunk'

function main() returns ExitCode
	let g = shadeIt
	let t = function() gives apply(g, c: Color.create(3)).v

	return runThunk(t)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/first-class-functions/first-class-function.error.wrong-shape-captured-into-closure.test:37:27: argument type mismatch for 'f': expected 'fn(Color) returns Color', got 'fn(Shade) returns Shade'
```

<!-- test: first-class-function.matching-shape-captured-into-closure -->
The control for the case above, and the reason a captured signature must be the RIGHT one rather
than merely present: a MATCHING function captured into a closure is still accepted at the same door
and still runs. A capture that carried the wrong signature — or the signature of some other value —
would refuse this, which is the failure mode a shape rule has to be held away from.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
typealias Thunk = function() returns Integer

function double(x Integer) returns Integer
	return x * 2
end 'double'

function apply(f UnaryOp, x Integer) returns Integer
	return f(x)
end 'apply'

function runThunk(t Thunk) returns Integer
	return t()
end 'runThunk'

function main() returns ExitCode
	let g = double
	let t = function() gives apply(g, x: 21)

	return runThunk(t)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.matching-shapes-across-every-door -->
The control that keeps the shape rule from becoming a false-refusal generator: the SAME program with
matching shapes, through every door at once — a call argument, a `return`, an assignment, a field
store, a struct-literal field initializer and a union payload. All six must still compile and run.
```maxon
typealias Integer = int(i64.min to i64.max)

type Color
	export var v as Integer

	export static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Color'

typealias ColorFn = function(Color) returns Color

function bump(x Color) returns Color
	return Color.create(x.v + 1)
end 'bump'

function bumpTwice(x Color) returns Color
	return Color.create(x.v + 2)
end 'bumpTwice'

type Holder
	export var op as ColorFn

	export static function create() returns Self
		return Self{op: bump}
	end 'create'
end 'Holder'

union Task
	run(op ColorFn)
end 'Task'

function pick() returns ColorFn
	return bump
end 'pick'

function apply(f ColorFn, c Color) returns Color
	return f(c)
end 'apply'

function main() returns ExitCode
	var f = bump
	f = bumpTwice
	var h = Holder.create()
	h.op = bump
	let t = Task.run(bump)
	let c = Color.create(0)
	let viaLocal = apply(f, c: c).v
	let viaField = h.op(c).v
	let viaReturn = pick()(c).v
	match t 'go'
		run(op) then return viaLocal + viaField + viaReturn + op(c).v
	end 'go'
end 'main'
```
```exitcode
5
```
