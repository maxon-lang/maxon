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

## Calling a Function Value as a Statement

A function value is called for its EFFECT the same way a named function is — as a bare-call
statement, with its result discarded. This is the position a function value whose signature returns
nothing is called in (a table of callbacks or compiler passes, each stored as a function value and
driven for effect), and a value-returning function value called this way simply drops its result:

```maxon
typealias Integer = int(i64.min to i64.max)

function record(n Integer) returns Integer
	return n
end 'record'

function main() returns ExitCode
	let f = record
	f(1)                 // called as a statement; the result is discarded
	return 0 as ExitCode
end 'main'
```

A bare name that is not a function value stays an ordinary call: `frob()` where nothing named `frob`
is a function-typed local is still resolved as a direct call, so an undefined callee is an
undefined-function error, not an indirect call.

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

<!-- test: first-class-function.void-value-called-as-statement -->
A VOID function value is called at STATEMENT position, for its effect, with no result to bind — the
call the statement position exists for, and the shape a table of void callbacks is driven by. A void
function value used to be misreported E3004 ("call to undefined function") because the
statement-position call path treated the local's name as a direct callee; it now diverts to the same
indirect-call lowering the expression position uses. The side effect (two increments of a module
`var`) proves the call actually RAN, not merely that it compiled — a no-op would return 0.
```maxon
var sideEffect = 0

function bump()
	sideEffect = sideEffect + 7
end 'bump'

function main() returns ExitCode
	let cb = bump
	cb()
	cb()
	return sideEffect as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: first-class-function.value-called-as-statement-discarded -->
A VALUE-returning function value called at statement position discards its result, exactly as a
value-returning DIRECT call does there. Discarding the first result must not disturb a later call:
`f(10)` is dropped, `f(41)` yields 42.
```maxon
typealias Integer = int(i64.min to i64.max)

function inc(n Integer) returns Integer
	return n + 1
end 'inc'

function main() returns ExitCode
	let f = inc
	f(10)
	return f(41)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.undefined-value-called-as-statement-errors -->
The over-acceptance guard. The statement-position indirect-call diversion fires ONLY for a
function-typed local; a bare name that is no such binding stays a direct call, so a genuinely
undefined callee at statement position is still E3004 — the diversion widens what compiles, never
what is silently accepted.
```maxon
function main() returns ExitCode
	cb()
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:2: call to undefined function 'cb'
```

### Calling a Postfix Callee as a Statement

A function value produced by a POSTFIX expression — a function-typed FIELD read (`h.op`), a field reached
through a chain (`o.inner.op`), or a call whose result is itself a function (`pick()`) — is called for its
EFFECT at statement position exactly as a bare-name function value is. The statement dispatcher used to
parse the postfix expression and then reject the trailing `(` as `( statement`; it now applies the trailing
`(args)` through the SAME indirect-call lowering the expression position uses. Only a postfix FOLLOWED BY
`(` becomes a call statement — a bare field read (`h.op`) is not a statement and stays an error.

<!-- test: first-class-function.field-void-called-as-statement -->
A VOID function-typed FIELD called at STATEMENT position, for its effect (#97) — the shape a table of
callbacks or compiler passes keyed by a struct field is driven by, each entry a function value called for
effect. The statement dispatcher parsed `h.op` as a field load and then rejected the trailing `(` as
`( statement`; it now applies the trailing call through the indirect-call lowering. The side effect (two
increments of a module `var`) proves the call RAN — a no-op would return 0.
```maxon
var sideEffect = 0

typealias Task = function()

type Holder
	export var op as Task

	static function create(t Task) returns Self
		return Self{op: t}
	end 'create'
end 'Holder'

function bump()
	sideEffect = sideEffect + 7
end 'bump'

function main() returns ExitCode
	let h = Holder.create(bump)
	h.op()
	h.op()
	return sideEffect as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: first-class-function.field-value-called-as-statement-discarded -->
A VALUE-returning function-typed field called at statement position discards its result, exactly as a
value-returning DIRECT call does there. Discarding the first result must not disturb a later call: `h.op(10)`
is dropped, `h.op(41)` yields 42.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function inc(n Integer) returns Integer
	return n + 1
end 'inc'

function main() returns ExitCode
	let h = Handler.create(inc)
	h.op(10)
	return h.op(41)
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.field-two-fields-called-as-statements -->
A struct with TWO function-typed fields, each called at statement position. Each call must dispatch through
its OWN field — `p.first` runs `bumpA` (+10) and `p.second` runs `bumpB` (+4) — so the total is 14 only when
the two field loads are not confused.
```maxon
var effA = 0
var effB = 0

typealias Task = function()

type Pair
	export var first as Task
	export var second as Task

	static function create(first Task, second Task) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function bumpA()
	effA = effA + 10
end 'bumpA'

function bumpB()
	effB = effB + 4
end 'bumpB'

function main() returns ExitCode
	let p = Pair.create(bumpA, second: bumpB)
	p.first()
	p.second()
	return (effA + effB) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: first-class-function.field-nested-void-called-as-statement -->
The receiver of a field call may itself be reached through a field CHAIN. `o.inner.op()` resolves the chain
to the function-typed field and applies the trailing call for effect — the statement-position twin of the
value-position `o.inner.op(21)`. The side effect (two increments) proves it ran.
```maxon
var sideEffect = 0

typealias Task = function()

type Holder
	export var op as Task

	static function create(t Task) returns Self
		return Self{op: t}
	end 'create'
end 'Holder'

type Outer
	export var inner as Holder

	static function create(inner Holder) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function bump()
	sideEffect = sideEffect + 5
end 'bump'

function main() returns ExitCode
	let o = Outer.create(Holder.create(bump))
	o.inner.op()
	o.inner.op()
	return sideEffect as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: first-class-function.call-result-called-as-statement -->
A call whose RESULT is a function value is itself called at statement position, for its effect. `pickBump()`
yields the `bump` function and the trailing `()` calls it — the statement-position twin of the value-position
`pick()(21)`, and the chaining `parsePostfix` already does in expression position. The side effect (two
increments) proves both hops ran.
```maxon
var sideEffect = 0

typealias Task = function()

function bump()
	sideEffect = sideEffect + 6
end 'bump'

function pickBump() returns Task
	return bump
end 'pickBump'

function main() returns ExitCode
	pickBump()()
	pickBump()()
	return sideEffect as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: first-class-function.field-string-result-called-as-statement-leak-free -->
The managed-result-discard guard. A function-typed field whose signature returns a STRING is called at
statement position and its result DISCARDED. The owned heap String is adopted as a statement temp and dropped
by `drainPendingTemps` — twice — so a leak would trip the runtime's exit-101 balance check. Exit 0 is the
pass: the discarded managed result is freed, not leaked.
```maxon
typealias Msg = function() returns String

type Holder
	export var op as Msg

	static function create(op Msg) returns Self
		return Self{op: op}
	end 'create'
end 'Holder'

function greet() returns String
	return "hello world this is a heap string"
end 'greet'

function main() returns ExitCode
	let h = Holder.create(greet)
	h.op()
	h.op()
	return 0 as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: first-class-function.field-read-statement-still-errors -->
The over-acceptance guard for a postfix callee: ONLY a postfix FOLLOWED BY `(` becomes a call statement. A
bare function-typed field READ (`h.op` with no `(`) is not a call and not a statement, so it stays the
unsupported-statement error it was — the fix widens what compiles, never what is silently accepted.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

type Handler
	export var op as UnaryOp

	static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'
end 'Handler'

function inc(n Integer) returns Integer
	return n + 1
end 'inc'

function main() returns ExitCode
	let h = Handler.create(inc)
	h.op
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:2: Unsupported: identifier statement
```

<!-- test: first-class-function.method-call-statement-still-direct -->
The no-regression anchor: a real instance METHOD called at statement position stays a DIRECT method
dispatch, not routed through the indirect-call path. `c.tick()` is a method on `Counter`, not a
function-typed field, so it must keep dispatching exactly as before. Two calls (+3 each) prove it ran.
```maxon
var sideEffect = 0

typealias Ticks = int(i64.min to i64.max)

type Counter
	export var n as Ticks

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function tick()
		sideEffect = sideEffect + 3
	end 'tick'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create()
	c.tick()
	c.tick()
	return sideEffect as ExitCode
end 'main'
```
```exitcode
6
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
<!-- targets: arm64-macos, arm64-linux, wasm32-wasi -->
<!-- x64 OMITTED — a REGISTER-POOL restriction, the only kind of technical reason a marker may state: this exact two-int interpolation in a loop (`print("i={i} -> {r}\n")` + `acc = acc + r`) needs 18 simultaneously-live values > x64's 14-register pool, so BOTH x64 targets refuse it at compile time with E5001. A PRE-EXISTING x64 register-allocator / interpolation-lowering limit, PROVEN closure-independent (the same body with a plain `let r = i + 5` instead of a closure E5001s identically), and its own interpolation-pressure rung. Everything with a wider pool runs it: AAPCS64's 30-register file holds the same 18 values with room to spare (measured on arm64-macos — exit 35, all five stdout lines), and wasm's stack machine has no register cap at all. x64 env-drop-timing is covered by `-rebound-in-loop` and `-called-from-nested-block`, both green there. -->
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-arm-errors.test:9:13: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-to-global-errors.test:14:14: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
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
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-in-ternary-used-in-frame-errors.test:12:12: cannot use a closure that captures as an arm of a conditional expression: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-in-match-arm-errors -->
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

### A function value only fits a function-typed place

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

<!-- test: first-class-function.function-value-as-arg-errors -->
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

### Float function values carry the callee's float result and param types (P1.5 #78)

A function VALUE returns and takes floats exactly as a direct call does. The indirect-call
lowering carries the function value's own signature, so a float RESULT is captured from the
float return register (xmm0/d0, or a wasm f64 result) instead of the integer one, and a float
ARGUMENT travels in a float argument register (its own separate counter) instead of a GPR. An
integer function value is untouched — the tests above are the no-regression anchor.

<!-- test: first-class-function.float-return-called-indirectly -->
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

### The escape rule is DERIVED, not a list of places to remember

The refusals above each name the construct the user wrote — the field, the union case, the `gives`
arm — and they can, because the parser still holds those words. But a hand-written check at each
syntactic sink can only cover the sinks somebody remembered, and the rule it enforces is not about
syntax at all: it is that a capturing closure's ENVIRONMENT must be able to follow the value. So the
rule is also stated once, structurally, over the finished IR: a capturing closure may appear only
where the environment travels with it — as the callee of an in-frame indirect call, or as an argument
to a direct call whose callee is known not to persist it. Every other slot is refused.

Stating it that way covers routes the enumeration missed, and these are not hypothetical. A `var`
holding a capturing closure and REASSIGNED inside an `if` or a `while` merges through a block-arg phi
exactly as a ternary does — one slot, carrying the code pointer and nothing else — and neither shape
is a ternary, a match `gives` or an `otherwise`. Both compiled clean and segfaulted.

<!-- test: first-class-function.capturing-closure-across-if-merge-errors -->
A capturing closure reassigned inside an `if` merges through the continuation's phi. Refused at the
closure literal — the phi edge that loses the environment has no source position of its own, and the
literal is the token that has to change either way.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 20
	var f = function(n Integer) gives n + bump
	if bump > 5 'branch'
		f = function(n Integer) gives n * bump
	end 'branch'
	return f(2)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-across-if-merge-errors.test:7:10: cannot carry a closure that captures across a branch merge: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-across-loop-merge-errors -->
The same merge through a LOOP-HEADER phi. `capturing-closure-rebound-in-loop` (accepted, above) binds
a fresh `let` inside the body and carries nothing across the back edge; this carries a `var` across it,
which is a merge and loses the environment.
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let bump = 20
	var f = function(n Integer) gives n + bump
	var i = 0
	while i < 2 'loop'
		f = function(n Integer) gives n * bump
		i = i + 1
	end 'loop'
	return f(2)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-across-loop-merge-errors.test:7:10: cannot carry a closure that captures across a branch merge: a merge joins its arms through a single slot that carries the function pointer but not the capture environment, so the closure would be called with no environment. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-through-witness-dispatch-errors -->
A capturing closure handed to an interface method dispatched through a WITNESS. The interprocedural
check that decides "does the callee persist this parameter?" is keyed on a callee NAME, and a witness
dispatch has none — the concrete method is chosen at run time from the witness table, so no summary can
be consulted and the environment cannot be threaded to it either. Before the rule was derived this
reached lowering and panicked; the identical shape through a plain call was already E3099.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer

interface Stasher
	function stash(fn UnaryOp) returns Integer
end 'Stasher'

type Slot implements Stasher
	export var op as UnaryOp

	export static function create(op UnaryOp) returns Self
		return Self{op: op}
	end 'create'

	export function stash(fn UnaryOp) returns Integer
		self.op = fn
		return 7
	end 'stash'
end 'Slot'

type W uses T where T is Stasher
	export var inner as T

	export static function create(inner T) returns Self
		return Self{inner: inner}
	end 'create'

	export function put(k Integer) returns Integer
		return self.inner.stash(function(n Integer) gives n + k)
	end 'put'
end 'W'

typealias SlotW = W with Slot

function plain(n Integer) returns Integer
	return n
end 'plain'

function main() returns ExitCode
	let s = Slot.create(plain)
	let w = SlotW.create(s)
	return w.put(5)
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-through-witness-dispatch-errors.test:30:27: cannot pass a closure that captures to an interface method dispatched through a witness: the concrete method is chosen at run time, so the compiler cannot tell whether it stores the closure, and its environment cannot be threaded to it. captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.capturing-closure-into-container-errors -->
A capturing closure pushed into a heap container. The array literal route is refused by an unrelated
element-type rule, but `.push()` is a call to a compiler runtime entry — and a runtime entry is in no
signature registry, so the escape summary the call-site check consults cannot be built for it. Rather
than delegate the decision to a check that structurally cannot run, the derived rule refuses it: the
Array outlives the frame and takes the code pointer alone.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(Integer) returns Integer
typealias OpArray = Array with UnaryOp

function main() returns ExitCode
	let bump = 20
	var a = OpArray.create()
	a.push(function(n Integer) gives n + bump)
	return a.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3099: specs/fragments/first-class-functions/first-class-function.capturing-closure-into-container-errors.test:10:9: cannot pass a closure that captures to a compiler runtime entry: it puts the value in heap memory that outlives this frame, and a runtime entry has no signature for the escape summary to be built from. captures are taken by reference to the enclosing function's frame, so a closure that captures cannot outlive that frame. Use a function reference, or a closure that captures nothing
```

<!-- test: first-class-function.int-literal-widens-at-indirect-call -->
An INT LITERAL passed at a `float` parameter of a function value. A DIRECT call widens it
(`LowerMaxonToStd.widenIntArgsToFloatParams`); an indirect call must widen it the same way, off the
DECLARED parameter types of the function type the call goes through. Without that the raw i64 `2`
travels in a GPR to a callee reading `xmm0`, and `v * 2.0` multiplies whatever was left there —
a silent wrong answer, not a crash.
```maxon

typealias Real = float(f64.min to f64.max)
typealias Scaler = function(v Real) returns Real

function twice(v Real) returns Real
	return v * 2.0
end 'twice'

function apply(f Scaler) returns Real
	return f(2)
end 'apply'

function main() returns ExitCode
	let fn = twice
	let r = apply(fn)
	return trunc(r) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: first-class-function.int-literal-widens-through-function-ref -->
The same widening where the callee value is a bare FUNCTION REFERENCE rather than a function-typed
parameter — its declared type is the function's own signature, not an alias's, so the two must reach
the same answer through one lookup.
```maxon

typealias Ratio = float(0.0 to 1000.0)

function scale(x Ratio) returns Ratio
	return x * 2.0
end 'scale'

function main() returns ExitCode
	let fn = scale
	return trunc(fn(3)) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: first-class-function.int-literal-widens-through-closure -->
The third callee kind: a LIFTED CLOSURE, whose declared parameter types are its own. The closure
declares `x Ratio`, so the literal `3` widens exactly as it does for a named function.
```maxon

typealias Ratio = float(0.0 to 1000.0)

function main() returns ExitCode
	let f = function(x Ratio) gives x * 2.0
	return trunc(f(3)) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: first-class-function.mixed-int-float-literal-args-indirect -->
The formal→actual MAPPING, pinned rather than "some argument widened": an int literal at an `int`
parameter must STAY an integer while an int literal at a `float` parameter must widen. Each failure
mode has its own answer — widening the wrong one makes `n == 3` compare an f64 bit pattern (5), and
widening neither leaves `x` an integer whose f64 reading is ~2.5e-323 (0).
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias Count = int(0 to 1000)
typealias MixFn = function(n Count, x Ratio) returns Ratio

function combine(n Count, x Ratio) returns Ratio
	if n == 3 'exact'
		return x * 10.0
	end 'exact'
	return x
end 'combine'

function apply(f MixFn) returns Ratio
	return f(3, 5)
end 'apply'

function main() returns ExitCode
	let fn = combine
	return trunc(apply(fn)) as ExitCode
end 'main'
```
```exitcode
50
```

<!-- test: first-class-function.matching-signature-cast-accepted -->
A cast to a function type whose signature MATCHES stays legal and stays a no-op — the guard against
an over-strict signature rule, which would otherwise refuse every cast a program legitimately writes.
```maxon

typealias Real = float(f64.min to f64.max)
typealias Scaler = function(v Real) returns Real

function twice(v Real) returns Real
	return v * 2.0
end 'twice'

function main() returns ExitCode
	let f = twice as Scaler
	return trunc(f(3.0)) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: first-class-function.closure-into-alias-typed-param-accepted -->
A closure passed into a parameter declared with a function typealias. Its declared parameter types
are the alias's, and its RETURN type is INFERRED — `float`, where the alias spells `Ratio` — so a
signature rule that compared the two by NAME would refuse a program both reference compilers accept.
The rule compares the RESOLVED representation, which is what the call ABI actually depends on.
```maxon

typealias Ratio = float(0.0 to 1000.0)
typealias ScaleFn = function(x Ratio) returns Ratio

function apply(f ScaleFn, v Ratio) returns Ratio
	return f(v)
end 'apply'

function main() returns ExitCode
	let g = function(x Ratio) gives x * 3.0
	return trunc(apply(g, v: 4.0)) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: first-class-function.error.cast-return-type-mismatch -->
A cast whose target function type returns something else. Accepted silently before this rule, after
which the integer the function really returns is read back out of the float return register.
```maxon

typealias Real = float(f64.min to f64.max)
typealias Integer = int(i64.min to i64.max)
typealias FloatFn = function(x Real) returns Real

function intish(x Real) returns Integer
	return trunc(x) + 14
end 'intish'

function main() returns ExitCode
	let f = intish as FloatFn
	return trunc(f(1.0)) as ExitCode
end 'main'
```
```maxoncstderr
error E3009: <fragment>:12:17: function type mismatch in cast: expected 'fn(float) returns float', got 'fn(float) returns int'
```

<!-- test: first-class-function.error.cast-param-type-mismatch -->
The parameter half of the same rule: the target function type declares a `float` parameter the
function itself declares as an int, so every call through the cast value would write the argument to
the wrong register file.
```maxon

typealias Real = float(f64.min to f64.max)
typealias Integer = int(i64.min to i64.max)
typealias FloatFn = function(x Real) returns Real

function intParam(x Integer) returns Real
	return x * 2.0
end 'intParam'

function main() returns ExitCode
	let f = intParam as FloatFn
	return trunc(f(1)) as ExitCode
end 'main'
```
```maxoncstderr
error E3009: <fragment>:12:19: function type mismatch in cast: expected 'fn(float) returns float', got 'fn(int) returns float'
```

<!-- test: first-class-function.error.cast-arity-mismatch -->
The arity half: a one-parameter function cast to a two-parameter function type. Every call through it
supplies an argument the callee never reads and reads a parameter the caller never wrote.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Bin = function(a Integer, b Integer) returns Integer

function one(a Integer) returns Integer
	return a + 1
end 'one'

function main() returns ExitCode
	let f = one as Bin
	return f(3) as ExitCode
end 'main'
```
```maxoncstderr
error E3009: <fragment>:11:14: function type mismatch in cast: expected 'fn(int, int) returns int', got 'fn(int) returns int'
```

<!-- test: first-class-function.error.wrong-signature-into-function-param -->
The rule is NOT cast-only. The same disagreement reached through a function-typed PARAMETER — no cast
anywhere — was accepted just as silently, because the argument check compared only the `function` TAG.
```maxon

typealias Real = float(f64.min to f64.max)
typealias Integer = int(i64.min to i64.max)
typealias FloatFn = function(x Real) returns Real

function intish(x Real) returns Integer
	return trunc(x) + 14
end 'intish'

function use(f FloatFn) returns Real
	return f(1.0)
end 'use'

function main() returns ExitCode
	let r = use(intish)
	return trunc(r) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:10: argument type mismatch for 'f': expected 'fn(float) returns float', got 'fn(float) returns int'
```

<!-- test: first-class-function.error.wrong-signature-returned -->
And through a `return`, the third door — a two-parameter function handed back where a one-parameter
function type is declared.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias UnaryOp = function(n Integer) returns Integer

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function pick() returns UnaryOp
	return add
end 'pick'

function main() returns ExitCode
	let f = pick()
	return f(4) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: function type mismatch in return: expected 'fn(int) returns int', got 'fn(int, int) returns int'
```

<!-- test: first-class-function.nested-function-type-agrees-by-shape -->
A function type whose own PARAMETER is a function type. The rule is the same one level down — RESOLVED
shape, not the alias NAME — so `Outer = function(f InnerA)` accepts a function declared `(f InnerB)`
when `InnerA` and `InnerB` resolve alike. Comparing the nested types by name instead refuses a program
both reference compilers accept, and prints both sides identically while doing it.

⚠ NOT a wasm case, and the reason is a BACKEND defect rather than anything about this rule: passing a
function value into a parameter whose function type has a function-typed parameter emits a wasm module
that fails validation (`expected i64 but nothing on stack`). It reproduces byte-for-byte on the parent
commit, with the SAME alias on both sides and with no indirect call anywhere, so it is neither this
rule's nor newly reachable — it is filed for its own rung.

⚠ **The marker also excludes both arm64 targets, and NOTHING above explains that** — the wasm reason
does not reach arm64, which shares the register backend x64 uses. Flagged by the 2026-07-28 targets
audit, which could not settle it: the audit ran x64-windows, x64-linux and wasm32-wasi locally, and the
arm64 lanes are synced by hand from a Mac. **Widen it to `arm64-macos, arm64-linux` and keep it if it
passes** — an unexplained exclusion is the shape a "it was red once" gate hides in.
<!-- targets: x64-windows, x64-linux -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias InnerA = function(x Integer) returns Integer
typealias InnerB = function(x Integer) returns Integer
typealias Outer = function(f InnerA) returns Integer

function dbl(x Integer) returns Integer
	return x * 2
end 'dbl'

function runner(f InnerB) returns Integer
	return f(21)
end 'runner'

// The door under test is the ARGUMENT one: `runner` is an `fn(InnerB) returns Integer` arriving where an
// `Outer` — an `fn(InnerA) returns Integer` — is declared.
function drive(o Outer) returns Integer
	return o(dbl)
end 'drive'

function main() returns ExitCode
	return drive(runner) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: first-class-function.error.nested-function-type-mismatch -->
The other half of the same descent: when the nested function types genuinely DISAGREE the door must
refuse, because `runner`'s own body calls its parameter through `InnerB`'s signature while the caller
supplied an `InnerA` — the wrong answer lands one level in, where nothing else looks. The diagnostic
names the nested types, which is what makes it readable at all.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Real = float(f64.min to f64.max)
typealias InnerA = function(x Integer) returns Integer
typealias InnerB = function(x Real) returns Real
typealias Outer = function(f InnerA) returns Integer

function runner(_ InnerB) returns Integer
	return 1
end 'runner'

function drive(_ Outer) returns Integer
	return 2
end 'drive'

function main() returns ExitCode
	return drive(runner) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:9: argument type mismatch for '_': expected 'fn(InnerA) returns int', got 'fn(InnerB) returns int'
```

<!-- test: first-class-function.error.indirect-call-too-few-args -->
An indirect call that supplies fewer arguments than the function type declares. The missing argument
was read out of whatever the register held — `f(3)` against `a + b` returned 3 — and the arity a
function value promises is exactly the fact the declared type now carries.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Bin = function(a Integer, b Integer) returns Integer

function two(a Integer, b Integer) returns Integer
	return a + b
end 'two'

function use(f Bin) returns Integer
	return f(3)
end 'use'

function main() returns ExitCode
	return use(two) as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:11:10: 'Bin' expects 2 argument(s) but 1 were provided
```

<!-- test: first-class-function.error.indirect-call-arg-type-mismatch -->
And an indirect call whose argument is the wrong KIND. A `String` at an int parameter reached the
backend untouched and the callee did integer arithmetic on a heap pointer.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Un = function(a Integer) returns Integer

function one(a Integer) returns Integer
	return a + 1
end 'one'

function use(f Un) returns Integer
	return f("hello")
end 'use'

function main() returns ExitCode
	return use(one) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:10: argument type mismatch for 1: expected 'int', got 'String'
```

<!-- test: first-class-function.indirect-call-arity-13 -->
The BOUNDARY below the arity that used to panic, so a regression that moves the cliff is caught
from both directions. A plain function reached as a VALUE is called through its `__fnref_` env
thunk, whose signature is `(userargs, __env)` — so a 13-argument function value makes a
14-parameter thunk, exactly the x64 pool, and it fits. Result is `sum(1..13) = 91`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op13 = function(Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) returns Integer

function sum13(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12
end 'sum13'

function callIt(f Op13) returns Integer
	return f(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13)
end 'callIt'

function main() returns ExitCode
	return callIt(sum13) as ExitCode
end 'main'
```
```exitcode
91
```

<!-- test: first-class-function.indirect-call-arity-14 -->
⭐ THE ARITY THAT PANICKED THE REGISTER ALLOCATOR — and the call site was never the reason. The
thunk's trailing `__env` is materialized and then read by nothing, so it is a DEAD DEF: live at no
program point, invisible to every popcount over a live set, and still occupying a real register
(the load that produces it clobbers one whatever becomes of the value). Fourteen live forwarded
arguments plus that one dead load is fifteen against a pool of fourteen — the splitter saw no
overflow at all and `chooseRegister` died with every register blocked. A DIRECT call of the same
arity was always fine, which is what made the boundary look like an indirect-call fact rather than
the dead-def fact it is (see `specs-shv2/register-pressure.md`, where the same demand appears with
no function value anywhere). Result is `sum(1..14) = 105`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op14 = function(Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) returns Integer

function sum14(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13
end 'sum14'

function callIt(f Op14) returns Integer
	return f(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)
end 'callIt'

function main() returns ExitCode
	return callIt(sum14) as ExitCode
end 'main'
```
```exitcode
105
```

<!-- test: first-class-function.indirect-call-arity-20 -->
Comfortably PAST the boundary, so a fix that merely shifted the cliff by one does not pass. Twenty
forwarded arguments plus the dead `__env` is twenty-one against fourteen, relieved by cold splits
in the thunk and in the caller alike. Result is `sum(1..20) = 210`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op20 = function(Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) returns Integer

function sum20(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19
end 'sum20'

function callIt(f Op20) returns Integer
	return f(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)
end 'callIt'

function main() returns ExitCode
	return callIt(sum20) as ExitCode
end 'main'
```
```exitcode
210
```

<!-- test: first-class-function.indirect-call-arity-26 -->
The arm64 twin: arm64 allocates from 26 GPRs, so its `__fnref_` thunk cliff is at 26 user
arguments (27 thunk parameters) where x64's is at 14. It is not gated to arm64 — on x64 it is
simply further past the pool, which is worth pinning on both lanes. The trailing arguments are zero
so the sum fits an exit code while the first twenty stay distinct. Result is `sum(1..20) = 210`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op26 = function(Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) returns Integer

function sum26(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer, a20 Integer, a21 Integer, a22 Integer, a23 Integer, a24 Integer, a25 Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19 + a20 + a21 + a22 + a23 + a24 + a25
end 'sum26'

function callIt(f Op26) returns Integer
	return f(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 0, 0, 0, 0, 0, 0)
end 'callIt'

function main() returns ExitCode
	return callIt(sum26) as ExitCode
end 'main'
```
```exitcode
210
```

<!-- test: first-class-function.indirect-call-arity-20-computed-args -->
The same arity with arguments that CANNOT be rematerialized, which is the case that pins the real
relief path. Every other test here passes literals, and the splitter's cheapest tier re-emits a
constant for free — so a caller full of literals is relieved without a single store, and only the
thunk exercises a spill. Here each argument is `base + k`, a computed value the remat tier refuses,
so the caller's twenty live arguments must be relieved by genuine cold splits. Result is
`sum(1..20) = 210`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Op20 = function(Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) returns Integer

function sum20(a0 Integer, a1 Integer, a2 Integer, a3 Integer, a4 Integer, a5 Integer, a6 Integer, a7 Integer, a8 Integer, a9 Integer, a10 Integer, a11 Integer, a12 Integer, a13 Integer, a14 Integer, a15 Integer, a16 Integer, a17 Integer, a18 Integer, a19 Integer) returns Integer
	return a0 + a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15 + a16 + a17 + a18 + a19
end 'sum20'

function callIt(f Op20, base Integer) returns Integer
	return f(base + 1, base + 2, base + 3, base + 4, base + 5, base + 6, base + 7, base + 8, base + 9, base + 10, base + 11, base + 12, base + 13, base + 14, base + 15, base + 16, base + 17, base + 18, base + 19, base + 20)
end 'callIt'

function main() returns ExitCode
	return callIt(sum20, base: 0) as ExitCode
end 'main'
```
```exitcode
210
```

<!-- test: first-class-function.exitcode-return-through-alias -->
⭐ **THE WIDTH A FUNCTION TYPEALIAS USED TO DROP (W1).** `ExitCode` is the ONE builtin type name
whose tag carries a sub-64 width (`MaxonType.exitCode` → u32), and a function typealias stores its
declared return interner-free as a `(tag, NAME)` pair — so a `returns ExitCode` arrives at every
reader as the tag `named` plus the bytes `ExitCode`, and rebuilding it through `maxonTypeOfTag`
alone gave back a `named`, an i64. The call site then declared `(i64) -> i64` against a
`__fnref_nine` thunk whose own return width came from the resolved declaration and was i32.

Invisible on every register target — an i64 and a u32 occupy the same GPR — and a hard
**`wasm trap: indirect call type mismatch`** on wasm, whose `call_indirect` checks the declared
functype against the funcref's own EXACTLY. So this case is worth nothing on the lane that
computed the right answer anyway and is the whole assertion on the lane that trapped; it runs on
all of them for that reason. No closure is involved: a bare function reference through a typealias
is enough.
```maxon
typealias Thunk = function() returns ExitCode

function nine() returns ExitCode
	return 9
end 'nine'

function callThunk(t Thunk) returns ExitCode
	return t()
end 'callThunk'

function main() returns ExitCode
	return callThunk(nine)
end 'main'
```
```exitcode
9
```

<!-- test: first-class-function.ranged-alias-return-through-alias -->
⭐ **THE CONTROL FOR THE CASE ABOVE, AND THE CONTROL IS THE POINT (W1).** `Code` is declared over
`ExitCode`'s EXACT range, so the two cases differ in the NAME and in nothing else — which is what
shows the divergence was a WIDTH recovered from a name and not a name treated specially. A user
ranged alias erases to width-free `integer` on both sides of the call (an i64 either way), so it
agreed before the fix and agrees after it; a regression that re-broke `ExitCode` by teaching the
rebuild to answer for one name would leave this case green and the one above red, which is
precisely the pair that says which.
```maxon
typealias Code = int(0 to u32.max)
typealias CodeThunk = function() returns Code

function nine() returns Code
	return 9
end 'nine'

function callThunk(t CodeThunk) returns Code
	return t()
end 'callThunk'

function main() returns ExitCode
	return callThunk(nine) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: first-class-function.exitcode-return-through-alias-computed -->
The recovered return type is a VALUE, not just a return slot: the indirect call's result is bound
and then arithmetic is done on it. A width recovered only where the call's functype is declared
would still leave the bound value carrying the wrong tag, and the tag is what every later rule
reads — so this pins that an `ExitCode` recovered from a function typealias is usable as the
integral value it is, and not merely callable. `4 + 4 + 1`.
```maxon
typealias Thunk = function() returns ExitCode

function four() returns ExitCode
	return 4
end 'four'

function twice(t Thunk) returns ExitCode
	let v = t()
	return v + v + 1
end 'twice'

function main() returns ExitCode
	return twice(four)
end 'main'
```
```exitcode
9
```

<!-- test: first-class-function.exitcode-param-through-alias -->
The PARAMETER half of the same registry, isolated from the return half — the alias takes an
`ExitCode` and returns a user ranged alias, so the only sub-64 name in the signature is on the
argument side. It already agreed, and the asymmetry is the point: an indirect call's argument
widths come from the RESOLVED `functionAliasShapes` (and the uniform function-value ABI passes
every non-float argument as one machine word regardless), where the RETURN width was taken from
the parser's own rebuild instead. So this case is the evidence for that asymmetry rather than a
second repro of it, and it is what would catch a fix that single-sourced the two columns by moving
the return's answer onto the param's footing instead of the other way round. `8 + 1`.
```maxon
typealias Outcome = int(0 to u32.max)
typealias Bump = function(ExitCode) returns Outcome

function bump(c ExitCode) returns Outcome
	return c + 1
end 'bump'

function applyIt(f Bump, c ExitCode) returns Outcome
	return f(c)
end 'applyIt'

function main() returns ExitCode
	return applyIt(bump, c: 8) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: first-class-function.exitcode-return-through-alias-high -->
⭐⭐ **THE WIDTH RECOVERED ABOVE 2^31 — THE CASE THE VALUE `9` CANNOT SEE (W1 review).** The case above
proves an `ExitCode` returned through a function typealias is the right WIDTH; it cannot prove the
right VALUE, because 9 is the same number under every extension rule. `ExitCode` is a **u32**
(`valueTagToStdType`), so on wasm it lives in an `i32` and has to be widened back to the `i64` world
every Maxon value inhabits — and widening it as SIGNED reads its top bit as a sign. MEASURED, before
the fix: the host printed `4000000000` and wasm printed **-294967296**, through the alias and through
a DIRECT call alike. A silent wrong answer, on the one lane that had just been taught to compute the
width at all.

Both readings are asserted, and the pair is the assertion: a fix that corrected only the indirect
path would leave `direct=` red, and one that corrected only the declared functype would leave both
red while `exitcode-return-through-alias` above stayed green. `4000000000` is chosen because it
exceeds `i32.max` and fits `u32.max`, which is exactly the band where the two extensions disagree.
```maxon
typealias Thunk = function() returns ExitCode

function big() returns ExitCode
	return 4000000000
end 'big'

function viaAlias(t Thunk) returns ExitCode
	let v = t()
	print("alias={v}\n")
	return 0
end 'viaAlias'

function main() returns ExitCode
	print("direct={big()}\n")
	return viaAlias(big)
end 'main'
```
```exitcode
0
```
```stdout
direct=4000000000
alias=4000000000
```

<!-- test: first-class-function.character-return-through-alias -->
`Character` is DELIBERATELY absent from `TypeResolution.builtinTypeNameTag`, and this is the case that
turns the reason into a measurement rather than an argument (W1 review). `parseTypeReference` settles
`Character` SYNTACTICALLY, so a function typealias stores it by TAG with an EMPTY name and the
name→tag table is never consulted for it — which is only true while the storage convention holds. If a
later change ever routed `Character` through the NAME column, this case goes red at the door, instead
of the omission being justified by prose nothing checks.
```maxon
typealias CharThunk = function() returns Character

function ch() returns Character
	return 'x'
end 'ch'

function callIt(f CharThunk) returns ExitCode
	let c = f()
	print("{c}\n")
	return 7
end 'callIt'

function main() returns ExitCode
	return callIt(ch)
end 'main'
```
```exitcode
7
```
```stdout
x
```

<!-- test: first-class-function.string-return-through-alias -->
`Character`'s twin, and the one that matters for OWNERSHIP rather than width (W1 review). A `String` is
MANAGED, so a function typealias that returned it under the wrong tag would not merely mis-size the
value — it would put it on the wrong side of the drop walk. It rides the TAG column for
`Character`'s reason (`parseTypeReference` settles `String` syntactically, storing an empty name), so
the table is never asked; the leak gate is what makes the ownership half of that an assertion.
```maxon
typealias Namer = function() returns String

function name() returns String
	return "abc"
end 'name'

function callIt(f Namer) returns ExitCode
	let s = f()
	print("{s}\n")
	return 3
end 'callIt'

function main() returns ExitCode
	return callIt(name)
end 'main'
```
```exitcode
3
```
```stdout
abc
```

<!-- test: first-class-function.bool-return-through-alias -->
The other keyword-settled tag, and the other sub-64 one: `bool` is an `i1` — a wasm `i32` — so a
function typealias returning it has the same width to lose that `ExitCode` did. It cannot lose it by
the same route (`bool` is a KEYWORD, so it can never arrive as a `named` name), and that asymmetry is
what this case pins beside `exitcode-return-through-alias`: the two sub-64 returns reach their width
through DIFFERENT columns, and only one of them was ever at risk.
```maxon
typealias Num = int(0 to 100)
typealias Pred = function(Num) returns bool

function isBig(n Num) returns bool
	return n > 50
end 'isBig'

function callIt(p Pred) returns ExitCode
	return 5 if p(90) else 0
end 'callIt'

function main() returns ExitCode
	return callIt(isBig)
end 'main'
```
```exitcode
5
```

<!-- test: first-class-function.exitcode-through-alias-struct-field -->
A THIRD door into the function-alias registry, and one no case reached before (W1 review): the alias
names a struct FIELD's type, so the recovered return width has to survive being stored in a box and
loaded back out before the indirect call is made. A width recovered only where a PARAMETER is typed
would leave this one declaring `() -> i64` against a `() -> i32` thunk, which is the original trap
arriving through a different door.
```maxon
typealias Thunk = function() returns ExitCode

type Holder
	export let cb as Thunk
	export static function create(cb Thunk) returns Self
		return Self{ cb: cb }
	end 'create'
end 'Holder'

function nine() returns ExitCode
	return 9
end 'nine'

function main() returns ExitCode
	let h = Holder.create(nine)
	return h.cb()
end 'main'
```
```exitcode
9
```

<!-- test: first-class-function.exitcode-through-alias-array-element -->
The FOURTH door — the alias as an `Array` ELEMENT type (W1 review). It is the struct-field case's twin
with one difference that is worth its own case: the element type reaches the alias registry through
the GENERIC instance machinery rather than a field declaration, so the two are separate readers of the
same stored `(tag, name)` pair, and either could have been left behind.
```maxon
typealias Thunk = function() returns ExitCode
typealias ThunkArray = Array with Thunk

function nine() returns ExitCode
	return 9
end 'nine'

function main() returns ExitCode
	var a = ThunkArray.create()
	a.push(nine)
	let f = try a.get(0) otherwise panic("a.get(0) on a one-element array")
	return f()
end 'main'
```
```exitcode
9
```
