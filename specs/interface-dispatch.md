---
feature: interface-dispatch
status: stable
keywords: [interface, dispatch, monomorphization, polymorphism, implements]
category: type-system
---

# Interface Dispatch

## Documentation

### Interface-Typed Parameters

Functions can declare parameters with interface types. Any concrete type that implements the interface can be passed as an argument:

```text
interface Drawable
  function draw() returns int
end 'Drawable'

type Circle implements Drawable
  function draw() returns int
    return 1
  end 'draw'
end 'Circle'

function render(item Drawable) returns int
  return item.draw()
end 'render'
```

At compile time, the compiler creates specialized copies of the function for each concrete type used at call sites (monomorphization). This means `render(myCircle)` calls a version of `render` specialized for `Circle`, with direct static dispatch to `Circle.draw`.

### Multiple Concrete Types

When multiple concrete types are passed to the same interface-typed parameter at different call sites, the compiler generates one specialization per concrete type:

```text
render(myCircle)   // calls renderScene$Circle
render(mySquare)   // calls renderScene$Square
```

### Type Safety

The compiler verifies at each call site that the argument's type actually implements the required interface. Passing a type that does not implement the interface is a compile error.


## Tests

<!-- test: basic-interface-dispatch -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value as Integer

	function greet() returns Integer
		return value + 1
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function callGreet(g Greeter) returns Integer
	return g.greet()
end 'callGreet'

function main() returns ExitCode
	let h = Hello.create(41)
	return callGreet(h)
end 'main'
```
```exitcode
42
```


<!-- test: dispatch-multiple-types -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Scorer
	function score() returns Integer
end 'Scorer'

type Alpha implements Scorer
	let n as Integer

	function score() returns Integer
		return n * 2
	end 'score'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Alpha'

type Beta implements Scorer
	let n as Integer

	function score() returns Integer
		return n * 3
	end 'score'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Beta'

function getScore(s Scorer) returns Integer
	return s.score()
end 'getScore'

function main() returns ExitCode
	let a = Alpha.create(10)
	let b = Beta.create(10)
	return getScore(a) + getScore(b)
end 'main'
```
```exitcode
50
```


<!-- test: dispatch-void-method -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Printer
	function show()
end 'Printer'

type Thing implements Printer
	let label as Integer

	function show()
		print("{label}\n")
	end 'show'

	static function create(label Integer) returns Self
		return Self{label: label}
	end 'create'
end 'Thing'

function display(p Printer)
	p.show()
end 'display'

function main() returns ExitCode
	let t = Thing.create(99)
	display(t)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```


<!-- test: dispatch-with-method-args -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Adder
	function add(n Integer) returns Integer
end 'Adder'

type Accumulator implements Adder
	let total as Integer

	function add(n Integer) returns Integer
		return total + n
	end 'add'

	static function create(total Integer) returns Self
		return Self{total: total}
	end 'create'
end 'Accumulator'

function addVia(a Adder, n Integer) returns Integer
	return a.add(n)
end 'addVia'

function main() returns ExitCode
	let acc = Accumulator.create(30)
	return addVia(acc, n: 12)
end 'main'
```
```exitcode
42
```


<!-- test: dispatch-named-first-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

// An interface (or type-parameter) method call may name its FIRST argument:
// the receiver dispatches through interface monomorphization, which is
// receiver-type-dependent and so cannot be ruled out syntactically at parse
// time. The C# bootstrap accepts this (it routes interface/type-param method
// calls through a path that silently consumes a first-arg label); the self-
// hosted parser defers the E2052 check to TypeResolution, which re-applies it
// only for concrete-struct / enum / builtin receivers. This is the regression
// guard for that split.
interface Combiner
	function combine(first Integer, second Integer) returns Integer
end 'Combiner'

type Summer implements Combiner
	let bias as Integer

	function combine(first Integer, second Integer) returns Integer
		return bias + first + second
	end 'combine'

	static function create(bias Integer) returns Self
		return Self{bias: bias}
	end 'create'
end 'Summer'

function combineVia(c Combiner) returns Integer
	return c.combine(first: 12, second: 30)
end 'combineVia'

function main() returns ExitCode
	let s = Summer.create(0)
	return combineVia(s)
end 'main'
```
```exitcode
42
```


<!-- test: dispatch-error-missing-arg-label -->
The label grammar is the SAME rule at an interface dispatch as at a direct call:
`parameter-labels` rules that the first argument is positional and every argument
after it must carry a `name:` label. Only the FIRST argument's label is optional
here (see `dispatch-named-first-arg` for why); the rest are not, and a dispatch is
not a hole in the grammar.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Combiner
	function combine(first Integer, second Integer) returns Integer
end 'Combiner'

type Summer implements Combiner
	let bias as Integer

	function combine(first Integer, second Integer) returns Integer
		return bias + first + second
	end 'combine'

	static function create(bias Integer) returns Self
		return Self{bias: bias}
	end 'create'
end 'Summer'

function combineVia(c Combiner) returns Integer
	return c.combine(12, 30)
end 'combineVia'

function main() returns ExitCode
	let s = Summer.create(0)
	return combineVia(s)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/interface-dispatch/dispatch-error-missing-arg-label.test:22:11: Second and subsequent arguments must be named. Use 'name: value' syntax
```


<!-- test: dispatch-error-unknown-arg-label -->
A label that names no parameter of the dispatched requirement is the same error it
is at a direct call. The dispatch path used to CONSUME a label without reading it,
so `bogus:` bound to `second` positionally and the program compiled and ran — a
wrong answer with no diagnostic, which is the worst of the three ways this can go.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Combiner
	function combine(first Integer, second Integer) returns Integer
end 'Combiner'

type Summer implements Combiner
	let bias as Integer

	function combine(first Integer, second Integer) returns Integer
		return bias + first + second
	end 'combine'

	static function create(bias Integer) returns Self
		return Self{bias: bias}
	end 'create'
end 'Summer'

function combineVia(c Combiner) returns Integer
	return c.combine(12, bogus: 30)
end 'combineVia'

function main() returns ExitCode
	let s = Summer.create(0)
	return combineVia(s)
end 'main'
```
```maxoncstderr
error E3003: specs/fragments/interface-dispatch/dispatch-error-unknown-arg-label.test:22:23: unknown parameter name: 'bogus'
```


<!-- test: dispatch-named-args-out-of-order -->
`parameter-labels` rules that named arguments may be supplied in ANY order, and a
dispatch binds each label to the parameter it NAMES rather than to the parameter at
the argument's source position. Subtraction is deliberately non-commutative so the
binding is observable in the exit code: bound by LABEL this returns 8, bound by
source POSITION it would return -8, which is not an exit code at all.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Subtractor
	function subtract(minuend Integer, subtrahend Integer) returns Integer
end 'Subtractor'

type Difference implements Subtractor
	let bias as Integer

	function subtract(minuend Integer, subtrahend Integer) returns Integer
		return minuend - subtrahend + bias
	end 'subtract'

	static function create(bias Integer) returns Self
		return Self{bias: bias}
	end 'create'
end 'Difference'

function subtractVia(s Subtractor) returns Integer
	return s.subtract(subtrahend: 2, minuend: 10)
end 'subtractVia'

function main() returns ExitCode
	let d = Difference.create(0)
	return subtractVia(d)
end 'main'
```
```exitcode
8
```


<!-- test: dispatch-multiple-methods -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
	function perimeter() returns Integer
end 'Shape'

type Rect implements Shape
	let w as Integer
	let h as Integer

	function area() returns Integer
		return w * h
	end 'area'

	function perimeter() returns Integer
		return 2 * (w + h)
	end 'perimeter'

	static function create(w Integer, h Integer) returns Self
		return Self{w: w, h: h}
	end 'create'
end 'Rect'

function measure(s Shape) returns Integer
	return s.area() + s.perimeter()
end 'measure'

function main() returns ExitCode
	let r = Rect.create(3, h: 4)
	return measure(r)
end 'main'
```
```exitcode
26
```


<!-- test: dispatch-with-print -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Describable
	function describe() returns Integer
end 'Describable'

type Widget implements Describable
	let id as Integer

	function describe() returns Integer
		return id
	end 'describe'

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Widget'

type Gadget implements Describable
	let id as Integer

	function describe() returns Integer
		return id * 10
	end 'describe'

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Gadget'

function showDescription(d Describable)
	print("{d.describe()}\n")
end 'showDescription'

function main() returns ExitCode
	let w = Widget.create(5)
	let g = Gadget.create(3)
	showDescription(w)
	showDescription(g)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
30
```


<!-- test: dispatch-nonconforming-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Runnable
	function run() returns Integer
end 'Runnable'

type NotRunnable
	let x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'NotRunnable'

function execute(r Runnable) returns Integer
	return r.run()
end 'execute'

function main() returns ExitCode
	let n = NotRunnable.create(1)
	return execute(n)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/interface-dispatch/dispatch-nonconforming-error.test:23:9: argument type mismatch for 'r': type 'NotRunnable' does not implement interface 'Runnable'
```


<!-- test: dispatch-extended-interface -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Base
	function base() returns Integer
end 'Base'

interface Derived extends Base
	function derived() returns Integer
end 'Derived'

type Impl implements Derived
	let n as Integer

	function base() returns Integer
		return n
	end 'base'

	function derived() returns Integer
		return n * 2
	end 'derived'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Impl'

function callBase(b Base) returns Integer
	return b.base()
end 'callBase'

function callDerived(d Derived) returns Integer
	return d.derived()
end 'callDerived'

function main() returns ExitCode
	let i = Impl.create(10)
	return callBase(i) + callDerived(i)
end 'main'
```
```exitcode
30
```


<!-- test: dispatch-same-type-twice -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Counter
	function count() returns Integer
end 'Counter'

type SimpleCounter implements Counter
	let n as Integer

	function count() returns Integer
		return n
	end 'count'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'SimpleCounter'

function getCount(c Counter) returns Integer
	return c.count()
end 'getCount'

function main() returns ExitCode
	let c1 = SimpleCounter.create(17)
	let c2 = SimpleCounter.create(25)
	return getCount(c1) + getCount(c2)
end 'main'
```
```exitcode
42
```


<!-- test: dispatch-three-types -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Valued
	function value() returns Integer
end 'Valued'

type One implements Valued
	function value() returns Integer
		return 1
	end 'value'

	static function create() returns Self
		return Self{}
	end 'create'
end 'One'

type Two implements Valued
	function value() returns Integer
		return 2
	end 'value'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Two'

type Three implements Valued
	function value() returns Integer
		return 3
	end 'value'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Three'

function getValue(v Valued) returns Integer
	return v.value()
end 'getValue'

function main() returns ExitCode
	let a = One.create()
	let b = Two.create()
	let c = Three.create()
	return getValue(a) + getValue(b) + getValue(c)
end 'main'
```
```exitcode
6
```


<!-- test: dispatch-interface-return-type -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Producer
	function produce() returns Integer
end 'Producer'

type Widget implements Producer
	let value as Integer

	function produce() returns Integer
		return value
	end 'produce'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Widget'

type Factory
	let seed as Integer

	function make() returns Producer
		return Widget.create(seed)
	end 'make'

	static function create(seed Integer) returns Self
		return Self{seed: seed}
	end 'create'
end 'Factory'

function consume(p Producer) returns Integer
	return p.produce()
end 'consume'

function main() returns ExitCode
	let f = Factory.create(42)
	let p = f.make()
	return consume(p)
end 'main'
```
```exitcode
42
```


<!-- test: dispatch-transitive-leak -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

interface Worker
	function process(values IntArray) returns Integer
end 'Worker'

type SumWorker implements Worker
	let tag as Integer

	function process(values IntArray) returns Integer
		var total = 0
		var i = 0
		while i < values.count() 'loop'
			let v = try values.get(i) otherwise 0
			total = total + v
			i = i + 1
		end 'loop'
		return total
	end 'process'

	export static function create() returns Self
		return Self{tag: 0}
	end 'create'
end 'SumWorker'

// Level 3: leaf function that uses the interface and creates temporaries
function processOne(w Worker, value Integer) returns Integer
	var arr = IntArray.create()
	arr.push(value)
	return w.process(arr)
end 'processOne'

// Level 2: intermediate function that calls level 3
function processTwo(w Worker, a Integer, b Integer) returns Integer
	let r1 = processOne(w, value: a)
	let r2 = processOne(w, value: b)
	return r1 + r2
end 'processTwo'

// Level 1: entry point that creates the concrete type and passes through
function doWork(w Worker) returns Integer
	return processTwo(w, a: 10, b: 32)
end 'doWork'

function main() returns ExitCode
	let worker = SumWorker.create()
	return doWork(worker)
end 'main'
```
```exitcode
42
```


<!-- test: interface-field-pass-as-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

type Marker implements Tagged
	let n as Integer

	function tag() returns Integer
		return n
	end 'tag'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Marker'

// Holder stores an interface-typed field. main() constructs a Holder
// over a Marker, then reads the field (h.t) and passes it to a
// function expecting the same interface type. The construction and
// the field-read happen in the same function so monomorphization can
// propagate the concrete type (`Marker`) through the Holder instance
// and rewrite the indirect dispatch in `callTag` to `Marker.tag`.
//
// Regression guard for two parser bugs around interface-typed fields,
// both diagnosed via the FunctionRegAllocator interface-field
// workaround in the self-hosted register allocator:
//   1. Field-access op produced a MaxonStruct with empty TypeName,
//      crashing FillDefaultArgs with NullReferenceException at the
//      call site (Dictionary.TryGetValue, key=null).
//   2. After fixing the empty TypeName, the call-site type-check
//      rejected the value with E3005 "type '' does not implement
//      interface 'Tagged'" because the value registered in the
//      variables table for the field still carried no struct-type
//      name, so the resolved MaxonStruct came back with an empty
//      TypeName at the next use.
type Holder
	export let t as Tagged

	static function create(t Tagged) returns Self
		return Self{t: t}
	end 'create'
end 'Holder'

function callTag(t Tagged) returns Integer
	return t.tag()
end 'callTag'

function main() returns ExitCode
	let m = Marker.create(42)
	let h = Holder.create(m)
	return callTag(h.t)
end 'main'
```
```exitcode
42
```


<!-- test: interface-param-threaded-through-helpers -->
An interface-typed parameter passed down through several helper functions —
each receiving it as a parameter and forwarding it — must still dispatch its
method at the innermost call. When the receiver value's recorded type degrades
to a bare `named(InterfaceName)` (as happens when an interface param is threaded
arg→param→arg across call boundaries), the resolver's dynamic-dispatch guard
must still recognize it as an interface receiver and route the method through
witness dispatch rather than flagging the call as unresolved. Mirrors the
compiler's own register allocator threading a `RegAllocTarget` interface through
`computeLiveness → scanUseDef → scanOpUseDefPacked` before calling
`regTarget.noteOpDefs(...)`.
```maxon
typealias Tick = int(0 to 100)

interface Counter
	function value() returns Tick
end 'Counter'

type Fixed implements Counter
	let n as Tick

	static function create(n Tick) returns Self
		return Self{n: n}
	end 'create'

	function value() returns Tick
		return n
	end 'value'
end 'Fixed'

// Innermost: dispatches the interface method on a value whose static type is
// the interface, reached only after two forwarding hops.
function deepest(c Counter) returns Tick
	return c.value() + c.value()
end 'deepest'

function middle(c Counter) returns Tick
	return deepest(c)
end 'middle'

function outer(c Counter) returns Tick
	return middle(c)
end 'outer'

function main() returns ExitCode
	let f = Fixed.create(5)
	return outer(f)
end 'main'
```
```exitcode
10
```


<!-- test: interface-param-declared-after-consumer -->
An interface used as a parameter type may be DECLARED LATER in source than the
function that consumes it (or in a later-parsed file). A concrete conformer
passed to such a parameter must compile AND dispatch correctly — the parameter
is a fat-pointer/witness receiver regardless of whether the interface
declaration has been seen yet when the annotation is interned. A pre-parse
interface-name scan registers every interface name before any annotation is
resolved (mirroring the bootstrap's prescan), so `run(q OpQuery)` here — which
precedes both `OpQuery` and its conformer `X64Query` — interns `OpQuery` as an
interface type and `q.value()` dispatches through the witness table rather than
crashing for a missing witness companion. Mirrors the compiler's own
`RegisterAllocator` consuming a `TargetOpQuery` declared in a later-parsed file.
```maxon
typealias Num = int(0 to 100)

function run(q OpQuery) returns Num
	return q.value()
end 'run'

type X64Query implements OpQuery
	export var v as Num

	export static function make(v Num) returns X64Query
		return X64Query{v: v}
	end 'make'

	export function value() returns Num
		return self.v
	end 'value'
end 'X64Query'

interface OpQuery
	function value() returns Num
end 'OpQuery'

function main() returns ExitCode
	let q = X64Query.make(7)
	return run(q)
end 'main'
```
```exitcode
7
```

<!-- test: interface-self-field-passed-as-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

type Marker implements Tagged
	let n as Integer

	function tag() returns Integer
		return n
	end 'tag'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Marker'

function callTag(t Tagged) returns Integer
	return t.tag()
end 'callTag'

// Holder reads its OWN interface-typed field via a bare `tagged` reference
// inside a method (a selfFieldLoad) and passes it to a function expecting the
// interface. selfFieldLoad must pair the field's witness half (offset+8) the
// same way an explicit `h.field` load does — else passing it as an interface
// arg panics in `witnessForInterfaceArg` with "no witness paired".
type Holder
	let tagged as Tagged

	function dispatch() returns Integer
		return callTag(tagged)
	end 'dispatch'

	static function create(tagged Tagged) returns Self
		return Self{tagged: tagged}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(Marker.create(9))
	return h.dispatch()
end 'main'
```
```exitcode
9
```

<!-- test: interface-borrowed-field-drop-in-loop -->
```maxon
typealias StrArray = Array with String
typealias Tag = int(0 to u64.max)

interface Query
	function score() returns bool
end 'Query'

type ConcreteQuery implements Query
	export let tag as Tag

	static function create(tag Tag) returns ConcreteQuery
		return Self{tag: tag}
	end 'create'

	function score() returns bool
		return self.tag >= 0
	end 'score'
end 'ConcreteQuery'

// `Allocator` stores a BORROWED interface value into a field, alongside enough
// fresh state that its constructor exceeds the inline budget (a real non-inlined
// constructor, like the compiler's own `FunctionRegAllocator`). The allocator is a
// short-lived local dropped at the end of `allocateFor`, and its synthesized
// destructor decrefs the interface field. The conformer passed in is OWNED by
// `main` and kept alive across the whole loop, so the field holds only a BORROW —
// the destructor must NOT release it. Before interface values were refcount-managed
// consistently (the field is drop-tracked, but the borrowed value stored into it
// got no store-incref), the destructor over-released the caller's conformer — the
// `__destruct_FunctionRegAllocator` `regTarget`/`opQuery` over-release that blocked
// the self-hosted bootstrap. Runs under the suite's leak gate (and `--rc-sanitize`),
// so an over-release or leak fails it.
type Allocator
	export var query as Query
	export var f0 as StrArray
	export var f1 as StrArray
	export var f2 as StrArray
	export var f3 as StrArray
	export var f4 as StrArray
	export var f5 as StrArray

	static function create(query Query) returns Allocator
		return Self{query: query, f0: StrArray.create(), f1: StrArray.create(), f2: StrArray.create(), f3: StrArray.create(), f4: StrArray.create(), f5: StrArray.create()}
	end 'create'

	function run() returns bool
		return self.query.score() and self.f0.count() == 0
	end 'run'
end 'Allocator'

function allocateFor(query Query) returns bool
	var allocator = Allocator.create(query)
	return allocator.run()
end 'allocateFor'

function main() returns ExitCode
	let concrete = ConcreteQuery.create(7)
	var trueCount = 0
	var i = 0
	while i < 4 'loop'
		if allocateFor(concrete) 'ok'
			trueCount = trueCount + 1
		end 'ok'
		i = i + 1
	end 'loop'
	return trueCount
end 'main'
```
```exitcode
4
```

<!-- test: interface-param-checked-divide-survives-specialization -->
### A possibly-zero divide in an interface-param function survives specialization
An interface-parameter function is CLONED per concrete argument type (monomorphization's
interface-alias specialization path). A possibly-zero integer `/` in its body is a throwing
`__checked_div` op carrying its own mod/signedness metadata; the clone must rebuild it as
itself, not as a plain try-call the lowering cannot emit.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Scorer
	function score() returns Integer
end 'Scorer'

type Alpha implements Scorer
	let n as Integer
	function score() returns Integer
		return n * 2
	end 'score'
	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Alpha'

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function ratioVia(s Scorer, d Integer) returns Integer
	let base = s.score()
	return try (base / d) otherwise panic("ratioVia: d was 0")
end 'ratioVia'

function main() returns ExitCode
	let a = Alpha.create(21)
	return ratioVia(a, d: opaque(3)) as ExitCode
end 'main'
```
```exitcode
14
```

<!-- test: interface-param-checked-float-divide-survives-specialization -->
### A possibly-zero FLOAT divide in an interface-param function survives specialization
The float sibling of the integer case above. Float `/` is throwing too, so a possibly-zero one is
a `__checked_div` op whose ResultKind is Float; the interface-alias specialization cloner must carry
that kind through, or the clone strands as a plain try-call the lowering cannot emit.
```maxon
typealias Real = float(f64.min to f64.max)

interface Scorer
	function score() returns Real
end 'Scorer'

type Alpha implements Scorer
	let n as Real
	function score() returns Real
		return n * 2.0
	end 'score'
	static function create(n Real) returns Self
		return Self{n: n}
	end 'create'
end 'Alpha'

function opaque(x Real) returns Real
	return x
end 'opaque'

function ratioVia(s Scorer, d Real) returns Real
	let base = s.score()
	return try (base / d) otherwise panic("ratioVia: d was 0")
end 'ratioVia'

function main() returns ExitCode
	let a = Alpha.create(21.0)
	let r = ratioVia(a, d: opaque(3.0))
	return 14 if r == 14.0 else 1
end 'main'
```
```exitcode
14
```

<!-- test: interface-param-ucd-load-survives-specialization -->
### A UCD table load in an interface-param function survives specialization
`__Builtins.ucdByteAt` / `ucdI64At` lower to dedicated ops rather than calls. The interface-alias
specialization cloner has to reproduce them, exactly as the generic-type-parameter cloner does — a
clone path that knows only about calls drops the whole family on the floor.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Codepoint = int(0 to 65535)
typealias SuppIndex = int(0 to 805)

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value as Integer

	function greet() returns Integer
		return value + 1
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function categoryVia(g Greeter, cp Codepoint, idx SuppIndex) returns Integer
	let bmp = __Builtins.ucdByteAt("__ucd_bmp", offset: cp)
	let supp = __Builtins.ucdI64At("__ucd_supp", index: idx)
	return g.greet() + bmp * 1000 + supp - (supp / 1000) * 1000
end 'categoryVia'

function main() returns ExitCode
	let h = Hello.create(0)
	print("{categoryVia(h, cp: 65, idx: 0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1201
```

<!-- test: interface-param-managed-cursor-survives-specialization -->
### A managed-memory cursor read in an interface-param function survives specialization
`current()` and `index()` on a `__ManagedMemoryCursor` are ops, not calls. The specialized clone
must carry the element kind and storage width with them, or the read narrows to the wrong width.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMemory = __ManagedMemory with Integer

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value as Integer

	function greet() returns Integer
		return value + 1
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function cursorVia(g Greeter, mem IntMemory) returns Integer
	let c = try mem.createCursor() otherwise return -1
	return g.greet() + c.current() * 10 + c.index()
end 'cursorVia'

function main() returns ExitCode
	let h = Hello.create(0)
	var mem = try IntMemory.create(4, elementSize: 8) otherwise panic("alloc")
	try mem.setLength(2) otherwise panic("setLength")
	try mem.set(0, value: 7) otherwise panic("set 0")
	try mem.set(1, value: 9) otherwise panic("set 1")
	print("{cursorVia(h, mem: mem)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
71
```

<!-- test: interface-param-managed-list-cursor-survives-specialization -->
### A managed-list cursor read in an interface-param function survives specialization
`cursorReset()` and `cursorValue()` on a `__ManagedList` are ops too, and `cursorValue()` carries
the element type the read is typed by.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntList = __ManagedList with Integer

interface Greeter
	function greet() returns Integer
end 'Greeter'

type Hello implements Greeter
	let value as Integer

	function greet() returns Integer
		return value + 1
	end 'greet'

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Hello'

function firstVia(g Greeter, list IntList) returns Integer
	list.cursorReset()
	try list.cursorStart() otherwise return -1
	return g.greet() + list.cursorValue()
end 'firstVia'

function main() returns ExitCode
	let h = Hello.create(0)
	var list = IntList.create()
	list.insertLast(42)
	print("{firstVia(h, list: list)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
43
```
