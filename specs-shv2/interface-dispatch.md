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
<!-- SLICE 2 / RETURN ABI: an interface RETURN needs a second return register on a NON-throwing call — the witness half has nowhere to ride. That is new Std ops beside `errorReturn`/`tryCall` plus x64, arm64 and wasm support, which is an ABI change, not the storage-and-threading this slice scoped. ⚠ v1 hits the same wall from the other side: a function that is BOTH interface-returning and throwing is not representable there, and v1 SILENTLY SKIPS its return-witness path (`LowerMaxonToStd.maxon:13545-13567`). shv2 refuses with a positioned diagnostic instead — silence is the one unacceptable outcome. -->
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
<!-- SLICE 2 / OWNERSHIP: an interface-typed FIELD needs the 16-byte carve-out PLUS witness-based destruction. Every conformer in shv2 is a heap struct and `__destruct_<T>` is STATIC dispatch, so a fat pointer cannot name its own destructor: a borrow model use-after-frees and an owning model has nothing to call. The measured cheapest mechanism is a `destroyFunc` word replacing the always-zero `parentTablePtr@8` in the witness table, plus a `__drop_existential(witness, value)` runtime. Slice 2. -->
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
<!-- SLICE 2 / OWNERSHIP: the same carve-out + witness destruction as `interface-field-pass-as-arg`, and this case is the one that PROVES a borrow model is unsound — it stores a TEMPORARY (`Holder.create(Marker.create(9))`) into an interface field, so nothing else keeps the conformer alive. Slice 2. -->
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
<!-- SLICE 2 / OWNERSHIP: an interface-typed field read in a loop, whose drop must fire once per iteration through a destructor the fat pointer has to carry. Same missing mechanism as the two above. Slice 2. -->
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

<!-- test: fields-after-an-existential-read-at-the-right-offset -->
⭐⭐ **AN INTERFACE-TYPED FIELD TAKES TWO SLOTS, SO EVERY FIELD AFTER IT MOVES — and a layout that counted
FIELDS rather than SLOTS would put them all one word low, silently.** `StructLayout.offsetOfField` is a
prefix sum over `fieldSlotCount` (`Project.maxon`), which is what this case exists to observe: `label` sits
before the fat pointer and `extra` after it, so reading `extra` correctly is only possible if the carve-out
was counted. `sizeof(Holder)` is **32** — four slots for three fields — where the count-only rule says 24.
It is also the MIXED cascade: the destructor drops a `String` at `+0` through `__str_decref`, the
existential at `+8` through `__drop_existential`, and skips the scalar at `+24`, so the leak gate is what
proves the three offsets and the three drop kinds line up.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Tagged
	function tag() returns Integer
end 'Tagged'

type Marker implements Tagged
	export let s as String
	let n as Integer

	function tag() returns Integer
		return n + self.s.byteLength()
	end 'tag'

	static function create(n Integer) returns Self
		return Self{s: "abc", n: n}
	end 'create'
end 'Marker'

type Holder
	export let label as String
	export let t as Tagged
	export let extra as Integer

	static function create(t Tagged) returns Self
		return Self{label: "hi", t: t, extra: 5}
	end 'create'
end 'Holder'

function callTag(t Tagged) returns Integer
	return t.tag()
end 'callTag'

function main() returns ExitCode
	let h = Holder.create(Marker.create(3))
	print("size={sizeof(Holder)} label={h.label} extra={h.extra}\n")
	return callTag(h.t)
end 'main'
```
```exitcode
6
```
```stdout
size=32 label=hi extra=5
```

<!-- test: interface-field-on-a-generic-type -->
A GENERIC type's own interface-typed field survives substitution into the per-instance destructor cascade.
An interface-typed generic ARGUMENT is refused at the instantiation, so it is easy to read this arm as
unreachable — it is not: `t` here is the base struct's own field, and substitution leaves it exactly as
declared, so `__destruct_<instance>` has to drop it through `__drop_existential` while it drops the
substituted `T` (trivial here) through nothing at all. Two drop KINDS in one cascade, under the leak gate.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntBox = Box with Integer

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

type Box uses T
	export let t as Tagged
	export let v as T

	static function create(t Tagged, v T) returns Self
		return Self{t: t, v: v}
	end 'create'

	function score() returns Integer
		return self.t.tag()
	end 'score'
end 'Box'

function main() returns ExitCode
	let b = IntBox.create(Marker.create(8), v: 1)
	return b.score()
end 'main'
```
```exitcode
8
```

<!-- test: interface-field-reassigned -->
A `var` interface-typed field REASSIGNED: the old fat pointer's value half is released through its OWN
witness (the conformer it was holding, not the one replacing it) and the new pair is stored over both
slots. The witness half has to move with the value or the next dispatch would call the OLD conformer's
method with the NEW conformer's receiver — and under the leak gate an old value released twice, or never,
fails as loudly as a wrong answer.
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

type Holder
	export var t as Tagged

	static function create(t Tagged) returns Self
		return Self{t: t}
	end 'create'

	function replace(other Tagged) returns Integer
		self.t = other
		return self.t.tag()
	end 'replace'
end 'Holder'

function main() returns ExitCode
	var h = Holder.create(Marker.create(3))
	return h.replace(Marker.create(9))
end 'main'
```
```exitcode
9
```

<!-- test: scalar-conformer-co-owned-into-a-var -->
⭐⭐ **AN EXISTENTIAL'S VALUE HALF IS NOT ALWAYS A POINTER, AND CO-OWNING ONE MUST ASK ITS WITNESS FIRST.**
`int` and `bool` conform intrinsically (`isIntrinsicBuiltinConformance`) and are held at an interface as
their RAW VALUE in a general-purpose register — that is precisely what makes them representable there
where a `float` is not (E3121). A `var` bound to a borrowed managed aggregate becomes a SECOND OWNER of it
(OPEN #40's `__mm_retain`), and P1.7a-existentials slice 2 brought existentials under that rule — so the
incref has to be gated on the same `destroyFunc@8` word the DROP is gated on, which is 0 for a conformer
that owns no record. **MEASURED with a plain `__mm_retain` on the value half: this program compiled and
died with 0xC0000005, writing through `7 - 24 + 16`, where it answers 11 the moment the retain is
witness-gated.** `__retain_existential` is that gate, and it is the exact inverse of `__drop_existential`:
a conformer whose drop is inert has an inert retain.
```maxon
typealias Integer = int(i64.min to i64.max)

function use(h Comparable) returns Integer
	var v = h
	return 11 if true else 12
end 'use'

function main() returns ExitCode
	return use(7) as ExitCode
end 'main'
```
```exitcode
11
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

<!-- test: dispatch-discriminating-merge -->
⭐⭐ **shv2-authored, and it is the ONE case that can tell a correct existential from the bootstrap's
broken one.** Every merge case the corpus holds is undiscriminating in BOTH axes at once:
`cross-block-method-receiver`'s `interface-method-statement-after-if-merge` branches on the constant
`if 2 > 1` and hands the SAME implementor to both parameters, so an implementation that dispatches on
the last statically-assigned type — which is what monomorphization structurally does — returns the
right answer for the wrong reason.

This case removes both coincidences: TWO different implementors, and a condition the compiler cannot
fold (`pick` is a parameter). The branch is NOT taken, so the answer must be `A`'s. **MEASURED on the
C# bootstrap: it returns 2 — `B`'s answer — while a control with one implementor for both parameters
returns 1.** The bootstrap is authoritative for VALUES here and never for DISPATCH; shv2's fat pointer
carries the witness with the value across the merge phi, so the branch not taken cannot supply it.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shower
	function show() returns Integer
end 'Shower'

type A implements Shower
	function show() returns Integer
		return 1
	end 'show'

	static function create() returns Self
		return Self{}
	end 'create'
end 'A'

type B implements Shower
	function show() returns Integer
		return 2
	end 'show'

	static function create() returns Self
		return Self{}
	end 'create'
end 'B'

function run(s Shower, again Shower, pick Integer) returns Integer
	var t = s
	if pick > 0 'grow'
		t = again
	end 'grow'
	return t.show()
end 'run'

function main() returns ExitCode
	return run(A.create(), again: B.create(), pick: 0) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: interface-dispatch.narrow-formal-through-existential -->
⭐⭐ **A REQUIREMENT WHOSE FORMAL IS NARROWER THAN THE MACHINE WORD, DISPATCHED THROUGH AN
EXISTENTIAL.** `ExitCode` is a `u32`, so `Runner.take` is emitted with a wasm `i32` parameter while
every other argument of every indirect call is an `i64`; `call_indirect` type-checks the call site's
declared functype against the target's own EXACTLY, so the two must agree on that one argument or the
call cannot be made at all. Returns `31`.
**MEASURED RED before the fix, on this exact program: exit `31` on x64-windows, and
`wasm trap: indirect call type mismatch` under wasmtime — `call_indirect` declaring
`(param i64 i64) (result i64)` against `$Runner.take (param i64 i32) (result i64)`.**
⚠ **`ExitCode` IS THE ONLY NARROW TYPE THAT CAN REACH THIS, WHICH IS WHY 3,958 GREEN TESTS COULD NOT
SEE IT.** `bool` is the other narrow Std type, and it is safe by an accident of SYNTAX rather than by
the mechanism: it is a KEYWORD, so `parseTypeReference` tags it `boolean` directly and never mints a
`named`, and `typealias Flag = bool` is refused (E2015). `ExitCode` is an IDENTIFIER, so the same
function mints a bare `named` for it, which collapses to `i64` — while the conformer's impl declares
its parameter from the RESOLVED signature, where `exitCode` collapses to `u32`. Deriving the call
site's widths from the interface's own declared formals is what makes the two ends one answer.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Exiter
	function take(c ExitCode) returns Integer
end 'Exiter'

type Runner implements Exiter
	let base as Integer

	function take(c ExitCode) returns Integer
		return base + c
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

function useExiter(e Exiter) returns Integer
	return e.take(11 as ExitCode)
end 'useExiter'

function main() returns ExitCode
	return useExiter(Runner.create(20)) as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: interface-dispatch.narrow-formal-through-type-parameter -->
The CONSTRAINED-TYPE-PARAMETER twin of the case above — the other receiver kind the one witness
dispatch mechanism serves. It is not a second repro: the two receivers reach `appendWitnessCall`
by different routes (a threaded witness PARAMETER against a fat pointer's witness half), and a
call-site width derived from anything the RECEIVER carries would have to answer for both.
**MEASURED RED before the fix: `31` on x64-windows, `indirect call type mismatch` on wasm.**
```maxon
typealias Integer = int(i64.min to i64.max)

interface Exiter
	function take(c ExitCode) returns Integer
end 'Exiter'

type Runner implements Exiter
	let base as Integer

	function take(c ExitCode) returns Integer
		return base + c
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T where T is Exiter
	let item as T

	export function run() returns Integer
		return self.item.take(11 as ExitCode)
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RunnerBox = Box with Runner

function main() returns ExitCode
	return RunnerBox.create(Runner.create(20)).run() as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: interface-dispatch.narrow-formal-through-throwing-requirement -->
The THROWING twin, which lowers to `witnessTryCall` rather than `witnessCall` — a different Std op
with its own argument marshalling and its own interned functype (the trailing i64 error flag makes it
a distinct wasm type even at the same arity). The masks are built at both `appendWitnessCall` and
`appendWitnessTryCall`, so a fix applied to one and not the other leaves exactly this program broken.
**MEASURED RED before the fix: `31` on x64-windows, `indirect call type mismatch` on wasm.**
See `witness-throws.md` for the throwing witness ABI itself; this case is about the ARGUMENT width.
```maxon
typealias Integer = int(i64.min to i64.max)

enum TakeError implements Error
	tooSmall
end 'TakeError'

interface Exiter
	function take(c ExitCode) returns Integer throws TakeError
end 'Exiter'

type Runner implements Exiter
	let base as Integer

	function take(c ExitCode) returns Integer throws TakeError
		if c < 5 'small'
			throw TakeError.tooSmall
		end 'small'
		return base + c
	end 'take'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Runner'

type Box uses T where T is Exiter
	let item as T

	export function run() returns Integer
		return try self.item.take(11 as ExitCode) otherwise 55
	end 'run'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RunnerBox = Box with Runner

function main() returns ExitCode
	return RunnerBox.create(Runner.create(20)).run() as ExitCode
end 'main'
```
```exitcode
31
```

<!-- test: error.float-cannot-be-held-at-an-interface -->
⭐⭐ **A `float` HAS NO WAY INTO AN EXISTENTIAL, AND IT CONFORMS — WHICH IS WHY THE REFUSAL IS ITS OWN
RULE.** `float` declares the intrinsic `Comparable`/`Equatable`/`Hashable` conformances, so every
type-system question about this program answers YES. What stops it is REPRESENTATION: a value held at
an interface travels as a fat pointer whose value half is a general-purpose machine word — that word is
what a dispatch hands the impl as its receiver, read out of an integer register by every conformer —
and a float lives in a floating-point register. Widening one is a cross-register-file move, which no
`TargetOp` performs: a move's two ends are colored into one file by construction, and the two converts
and the `movqGprXmm` bitcast are each their own op.
**MEASURED before the rule: this program compiled all the way to the x64 emitter and PANICKED there —
`a register-to-register move from xmm0 to rcx crosses register files` — with no source position and
nothing an author could act on.** It is E2062's fact one widening position over: dictionary passing
gives a type parameter and an existential the SAME opaque slot, so it gives them the same limit.
⚠ ONE parameter, named `_`, and a second one PROVING the check is per-argument is not expressible today —
MEASURED, all three ways. A `float` actual is the only thing that reaches E3121, so the parameter must be
a builtin protocol; those declare only `Self`-typed requirements, which are undispatchable on an
existential (`requireWitnessSelfArgs`), so the parameter can never be USED; an unused NAMED parameter is
E3012 and a second `_` cannot be supplied, because arguments after the first must carry a label and `_`
is not one (E2053). The check itself is written per position (`checkOneArgType` runs once per argument),
and the day a builtin protocol declares a nullary requirement this case grows its second parameter. This case is about the ARGUMENT, and an earlier draft used
the two operands of `c < other` to consume them — which is the address-compare shape
`error.existentials-cannot-be-compared` below refuses, so the program stopped reaching E3121 at all.
```maxon
typealias Integer = int(i64.min to i64.max)

function use(_ Comparable) returns Integer
	return 11
end 'use'

function main() returns ExitCode
	return use(2.5) as ExitCode
end 'main'
```
```maxoncstderr
error E3121: specs/fragments/interface-dispatch/error.float-cannot-be-held-at-an-interface.test:9:9: Cannot pass a `float` as '_', which is declared at the interface type 'Comparable': a value held at an interface type is a two-word fat pointer `(value, witness)` whose value half is a general-purpose machine word, and a float travels in a floating-point register, so it has no way through. This is the same limit `float` has as a generic type argument (E2062). Wrap the float in a type that implements 'Comparable', or take the parameter as a `float`
```

<!-- test: error.existentials-cannot-be-compared -->
⭐⭐ **COMPARING TWO VALUES HELD AT ONE INTERFACE ANSWERED WITH THEIR HEAP ADDRESSES, SILENTLY, ON EVERY
TARGET.** `typeClassOf` gives `interfaceRef` its own class, so a pair of existentials passed the
agreement gate; neither is a float, so the domain test passed them too — and the comparison lowered to
an integer compare of the two fat pointers' VALUE HALVES.
**MEASURED before the rule, identically on x64-windows and wasm32-wasi, on two distinct `Wrapped(7)`
boxes: `7 == 7` answered `false`, `7 != 7` answered `true`, `7 >= 7` answered `false`, and
`a(2) > b(1)` FLIPPED its answer when the two allocations were reordered.** Reference identity
answering a question the author asked about values — word for word the hazard `comparableOperands`
already refused for two structs, one tag over.
It is also the OPERATOR half of a rule whose METHOD half was already closed: dispatching a `Self`-typed
requirement on an existential is refused by `requireWitnessSelfArgs`, because two values held at one
interface may have different dynamic types and nothing can prove they match — which is exactly what
`==` and `<` need. Both doors now read the one sentence in `IrInterface.ExistentialPairUnprovableReason`.
⚠ All six operators are refused, not only the two the corpus exercises. A MIXED pair (an existential
against an `int`) is untouched and still reads as the type mismatch it is.
```maxon
typealias Integer = int(i64.min to i64.max)

type Wrapped implements Comparable
	let v as Integer

	export function compare(other Wrapped) returns Ordering
		if self.v > other.v 'gt'
			return Ordering.greaterThan
		end 'gt'
		if self.v < other.v 'lt'
			return Ordering.lessThan
		end 'lt'
		return Ordering.equalTo
	end 'compare'

	static function create(v Integer) returns Self
		return Self{v: v}
	end 'create'
end 'Wrapped'

function existEq(c Comparable, other Comparable) returns bool
	return c == other
end 'existEq'

function main() returns ExitCode
	return 0 if existEq(Wrapped.create(7), other: Wrapped.create(7)) else 1
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/interface-dispatch/error.existentials-cannot-be-compared.test:23:11: cannot compare values held at an interface type using '==': two values held at one interface may have different dynamic types, and nothing here can prove they match, so the comparison would be answered by the two fat pointers' ADDRESSES rather than by their values. Compare concrete values, or dispatch a requirement the interface declares
```

<!-- test: error.closure-parameter-at-an-interface-type -->
⭐⭐ **A CLOSURE PARAMETER DECLARED AT AN INTERFACE TYPE PANICKED THE PARSER, WITH NO POSITION.**
```text
panic at Parser.maxon: Parser.witnessOfValue: value v0 is typed as an interface but no witness half is
paired with it — every producer of an existential must call `pairInterfaceWitness`
```
A value held at an interface is a two-word fat pointer `(value, witness)`, and the witness half travels
as an ADJACENT HIDDEN ARGUMENT that only a named function's signature reserves. A lifted closure's
parameters are bound by a different door than a function's, and that door paired no witness — so the
first use of the parameter asked for a half that was never there.
**MEASURED identically on the tip and on the control, so it is not a regression** — it is one of the
seven interface-typed positions, and the one a round of this rung missed because a closure's parameters
are parsed somewhere else.
⚠ The closure is bound to a LOCAL rather than passed at a declared function type, and that is
deliberate: `typealias ShapeFn = function(Shape) returns Integer` is itself refused now
(`error.interface-typed-function-type-parameter`), and it is refused EARLIER — so routing this case
through one would test that rule instead of this one. A local binding is the shape that still reaches
the closure's own parameter list.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

function main() returns ExitCode
	let f = function(t Shape) gives t.area()
	return f(Sq.create(7)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.closure-parameter-at-an-interface-type.test:21:19: Unsupported: a closure parameter declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a function value is called through the uniform `(userargs, env)` indirect ABI, which carries one machine word per argument and reserves no adjacent slot for the witness half. Declare the parameter at a concrete type, or pass the interface to a named function DIRECTLY, whose signature reserves the adjacent slot
```

<!-- test: error.interface-typed-requirement-parameter -->
⭐⭐ **AN INTERFACE-TYPED PARAMETER ON A REQUIREMENT WAS THE ONE POSITION THAT STILL COMPILED, AND IT
NEVER WORKED.** Five declared places already refused an existential for the same reason — a struct
field, a union payload, a return type, a container element and a closure parameter — because a value
held at an interface is a two-word fat pointer and each of those has room for one word. A witness
call has room for one word too, and this position was missed.
**MEASURED on the CONTROL, so this is a completion rather than a new restriction: exit 139 (SEGFAULT)
on x64-windows and a trap on wasm — with a proper CONFORMER actual**, not merely with a mistyped one.
The isolating measurement: with the parameter present but never dispatched on, x64 answered CORRECTLY
(the value half arrives, the witness half is simply dropped) while wasm still trapped on the call
itself — which is what says the formal is unrepresentable rather than unchecked.
⚠ Refused at the DECLARATION, where the author has something to change, and not at the dispatch — the
rule the four storage positions already state. The refusal is NOTED by the shared interface reader and
thrown only on the real-parse path, because that reader's other caller is the tolerant whole-program
fold, which swallows a `ParseError` and would silently drop the interface from the index.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

interface Runner
	function run(c Shape) returns Integer
end 'Runner'

type R implements Runner
	let base as Integer

	function run(c Shape) returns Integer
		return self.base + c.area()
	end 'run'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'R'

function useIt(r Runner) returns Integer
	return r.run(Sq.create(11))
end 'useIt'

function main() returns ExitCode
	return useIt(R.create(20)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-requirement-parameter.test:21:15: Unsupported: an interface requirement's parameter declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument, so the witness half is dropped and the impl dispatches through whatever the next slot happens to hold. Declare the parameter at a concrete type, or declare the requirement over a type parameter the interface constrains
```

<!-- test: error.interface-typed-requirement-parameter-through-a-type-parameter -->
The SECOND dispatch door. The requirement is refused at its declaration, so it does not matter which
receiver reaches it — but both doors collapsed the formal identically before the refusal existed
(**MEASURED: 139 on x64, trap on wasm, through a `where T is` body exactly as through an existential**),
and a refusal placed at either dispatch site instead of at the declaration would have closed only one.
This case is what proves the declaration is the right place.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

interface Runner
	function run(c Shape) returns Integer
end 'Runner'

type R implements Runner
	let base as Integer

	function run(c Shape) returns Integer
		return self.base + c.area()
	end 'run'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'R'

type Box uses T where T is Runner
	let item as T

	export function go() returns Integer
		return self.item.run(Sq.create(11))
	end 'go'

	static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias RBox = Box with R

function main() returns ExitCode
	return RBox.create(R.create(20)).go() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-requirement-parameter-through-a-type-parameter.test:21:15: Unsupported: an interface requirement's parameter declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument, so the witness half is dropped and the impl dispatches through whatever the next slot happens to hold. Declare the parameter at a concrete type, or declare the requirement over a type parameter the interface constrains
```

<!-- test: error.interface-typed-parameter-on-a-throwing-requirement -->
The THIRD door: a throwing requirement lowers to `witnessTryCall`, a different Std op with its own
argument marshalling, and it collapsed the formal identically (**MEASURED: 139 on x64, trap on wasm**).
Pinned because a refusal wired into the non-throwing dispatch alone would leave exactly this shape live,
which is the split that has cost this rung two rounds already.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

enum RunError implements Error
	tooSmall
end 'RunError'

interface Runner
	function run(c Shape) returns Integer throws RunError
end 'Runner'

type R implements Runner
	let base as Integer

	function run(c Shape) returns Integer throws RunError
		if self.base < 5 'small'
			throw RunError.tooSmall
		end 'small'
		return self.base + c.area()
	end 'run'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'R'

function useIt(r Runner) returns Integer
	return try r.run(Sq.create(11)) otherwise 55
end 'useIt'

function main() returns ExitCode
	return useIt(R.create(20)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-parameter-on-a-throwing-requirement.test:25:15: Unsupported: an interface requirement's parameter declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a witness call carries one machine word per argument, so the witness half is dropped and the impl dispatches through whatever the next slot happens to hold. Declare the parameter at a concrete type, or declare the requirement over a type parameter the interface constrains
```

<!-- test: interface-dispatch.interface-parameter-on-a-plain-function-still-compiles -->
⭐⭐ **THE FALSE-REJECT CONTROL, AND THE SHARPEST ONE IN THIS CHANGE.** A plain function's parameter is
the position that DOES have room — its signature reserves an adjacent hidden argument for the witness
half — so it must keep compiling and answering correctly while the requirement's parameter is refused.
The two are one token apart in the source and one `DeclaredStoragePosition` apart in the compiler.
MEASURED against the control, unchanged: `20 + 11`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

function takeShape(c Shape, base Integer) returns Integer
	return base + c.area()
end 'takeShape'

type Holder
	let base as Integer

	function measure(c Shape) returns Integer
		return self.base + c.area()
	end 'measure'

	static function create(base Integer) returns Self
		return Self{base: base}
	end 'create'
end 'Holder'

type Helper
	static function measure(c Shape, base Integer) returns Integer
		return base + c.area()
	end 'measure'
end 'Helper'

function main() returns ExitCode
	return (takeShape(Sq.create(11), base: 20) - Holder.create(20).measure(Sq.create(11)) + Helper.measure(Sq.create(11), base: 31)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.interface-typed-function-type-parameter -->
⭐⭐ **A SEVENTH INTERFACE-TYPED POSITION, AND THIS RUNG IS WHAT MADE IT REACHABLE.** (It was not the last: a TUPLE ELEMENT was an eighth, reached by a route no field/parameter/return reader owns — see `error.interface-typed-tuple-element`. The count is deliberately not written down in the compiler either; `DeclaredStoragePosition` is the count.) At the
merge base an interface name in a parameter position was E3011 — existentials did not exist — so the
shape only became writable when this rung landed them. It has never worked:
**MEASURED, exit 139 (SEGFAULT) on x64-windows and a PANIC in the wasm backend**, given a proper
conformer.
The reason is the closure parameter's, exactly: a function VALUE is called through the uniform
`(userargs, env)` indirect ABI, which carries one machine word per argument, so the fat pointer's
witness half has nowhere to travel. The value reaching the call is a `__fnref_` thunk or a lifted
closure and both are that shape — which is why the two positions share one clause rather than
spelling it twice.
⚠ It is refused at the `typealias`, not at the call, so a CLOSURE assigned to such a type is refused
here too — earlier than the closure-parameter rule would have caught it, and pointing at the
declaration the author would change.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

typealias ShapeFn = function(Shape) returns Integer

function measure(c Shape) returns Integer
	return c.area()
end 'measure'

function apply(f ShapeFn, v Shape) returns Integer
	return f(v)
end 'apply'

function main() returns ExitCode
	return apply(measure, v: Sq.create(31)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-function-type-parameter.test:20:30: Unsupported: a function type's parameter declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a function value is called through the uniform `(userargs, env)` indirect ABI, which carries one machine word per argument and reserves no adjacent slot for the witness half. Declare the parameter at a concrete type, or pass the interface to a named function DIRECTLY, whose signature reserves the adjacent slot
```

<!-- test: interface-dispatch.function-values-over-concrete-types-still-compile -->
The FALSE-REJECT CONTROL for the case above. A function type whose parameter is a CONCRETE type is
untouched, and so is a function value stored in a struct FIELD and called back out of it — the two
shapes closest to the refused one. Only an interface-typed parameter loses its second word; nothing
about function values in general changed. `31 + 11`.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

typealias SqFn = function(Sq) returns Integer

function measure(v Sq) returns Integer
	return v.area()
end 'measure'

function apply(f SqFn, v Sq) returns Integer
	return f(v)
end 'apply'

type Holder
	let f as SqFn

	static function create(f SqFn) returns Self
		return Self{f: f}
	end 'create'

	export function run(v Sq) returns Integer
		return self.f(v)
	end 'run'
end 'Holder'

function main() returns ExitCode
	return (apply(measure, v: Sq.create(31)) + Holder.create(measure).run(Sq.create(11))) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.interface-typed-tuple-element -->
⭐⭐ **THE EIGHTH INTERFACE-TYPED POSITION, AND THE ONE THAT REACHED THE BACKEND.** A tuple type is
interned as a SYNTHESIZED STRUCT, so its element list is read by `parseTupleTypeReference` — a third
route, belonging to no field, parameter or return reader — and it asked no door at all. The
`interfaceRef` travelled into `internTupleType` → `mangleTypeArg` and **PANICKED** in
`LayoutDescriptor.primitiveTypeTagName`.
⚠ **The panic asserted something FALSE**, and that is why nobody guarded this: it said an interface
type is refused at the front end by `checkGenericArgType` — true of a generic ARGUMENT and of nothing
else. A tuple element is not a generic argument. The assertion now states the RULE rather than one of
its enforcers, and says that reaching it means a further way to write a type into a slot exists.
**MEASURED: the merge base answered `E3011 Unknown type 'Shape'` (existentials did not exist, so the
shape was unreachable by construction); this rung made it writable and it panicked.**
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

typealias Pair = (Shape, Integer)

function take(p Pair) returns Integer
	return p.1
end 'take'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-tuple-element.test:20:19: Unsupported: a tuple element declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a tuple is a synthesized struct, so an element is a field slot: one machine word. Declare the element at a concrete type, or pass the interface alongside the tuple as a PARAMETER of a named function, which carries its witness as an adjacent argument
```

<!-- test: error.interface-typed-tuple-element-in-a-parameter -->
The SECOND of the three spellings that reach `parseTupleTypeReference` — an inline tuple type in a
parameter position, with no `typealias` in sight. **MEASURED: merge base `E3011`, and a PANIC here
before the door was asked.** Pinned separately because each spelling reaches the element loop by a
different caller, and a guard placed at the `typealias` alone would have closed only one.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

function take(p (Shape, Integer)) returns Integer
	return p.1
end 'take'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-tuple-element-in-a-parameter.test:20:18: Unsupported: a tuple element declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a tuple is a synthesized struct, so an element is a field slot: one machine word. Declare the element at a concrete type, or pass the interface alongside the tuple as a PARAMETER of a named function, which carries its witness as an adjacent argument
```

<!-- test: error.interface-typed-tuple-element-in-a-return -->
The THIRD spelling. It is the one the merge base handled DIFFERENTLY from the other two — `E3005`
rather than `E3011` — which is worth pinning because it shows the three reach the element loop by
genuinely different paths rather than being one shape written three ways.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

function make() returns (Shape, Integer)
	return (Sq.create(1), 2)
end 'make'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-tuple-element-in-a-return.test:20:26: Unsupported: a tuple element declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a tuple is a synthesized struct, so an element is a field slot: one machine word. Declare the element at a concrete type, or pass the interface alongside the tuple as a PARAMETER of a named function, which carries its witness as an adjacent argument
```

<!-- test: interface-dispatch.tuples-over-concrete-types-still-compile -->
The FALSE-REJECT CONTROL for the three above: a tuple whose elements are CONCRETE still interns,
still holds a conformer, and still answers. Only an interface-typed element loses its second word.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Sq implements Shape
	let s as Integer

	function area() returns Integer
		return self.s
	end 'area'

	static function create(s Integer) returns Self
		return Self{s: s}
	end 'create'
end 'Sq'

typealias Pair = (Sq, Integer)

function take(p Pair) returns Integer
	return p.0.area() + p.1
end 'take'

function main() returns ExitCode
	return take((Sq.create(11), 31)) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.interface-typed-requirement-return -->
⭐⭐ **THE RETURN HALF OF THE REQUIREMENT POSITION, WHICH COMPILED AND LINKED.** `parseInterfaceMethod`
reads its return through `parseOptionalReturnType` and never asked the door, so the declared type
degraded silently to the machine word — **MEASURED on the merge base: the program COMPILED**, and the
first dispatch on the result reported the misdirecting `a member access 'area' on a 'int' value`.
⚠ It is not a ninth position: it is a second call site of the SAME `returnType` arm a declared
function's return already used. It is noted-then-thrown rather than thrown in place, for the reason
the parameter half is — the reader is shared with a tolerant whole-program fold.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

interface Maker
	function make() returns Shape
end 'Maker'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-requirement-return.test:9:18: Unsupported: a function's return type declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a return hands back one register, plus a second only for a throwing call's error flag — an interface-returning ABI is a distinct slice. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```

<!-- test: error.interface-typed-function-type-return -->
The other unguarded return: a FUNCTION TYPE's. `readFunctionTypeAlias` read it with a bare
`parseTypeReference`, so it degraded the same way. Also the same `returnType` arm, gated on the same
`recordSignature` flag its parameter half uses so the tolerant sweep cannot veto.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

typealias MakeFn = function(Integer) returns Shape

function apply(f MakeFn) returns Integer
	return f(1).area()
end 'apply'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/interface-dispatch/error.interface-typed-function-type-return.test:8:38: Unsupported: a function's return type declared at the interface type 'Shape' — a value held at an interface type is a two-word fat pointer `(value, witness)`, and a return hands back one register, plus a second only for a throwing call's error flag — an interface-returning ABI is a distinct slice. Declare it at a concrete type, or take the interface as a PARAMETER of a plain function, which carries its witness as an adjacent argument
```
