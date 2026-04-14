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
	let value Integer

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
	let h = Hello.create(value: 41)
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
	let n Integer

	function score() returns Integer
		return n * 2
	end 'score'

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Alpha'

type Beta implements Scorer
	let n Integer

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
	let a = Alpha.create(n: 10)
	let b = Beta.create(n: 10)
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
	let label Integer

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
	let t = Thing.create(label: 99)
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
	let total Integer

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
	let acc = Accumulator.create(total: 30)
	return addVia(acc, n: 12)
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
	let w Integer
	let h Integer

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
	let r = Rect.create(w: 3, h: 4)
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
	let id Integer

	function describe() returns Integer
		return id
	end 'describe'

	static function create(id Integer) returns Self
		return Self{id: id}
	end 'create'
end 'Widget'

type Gadget implements Describable
	let id Integer

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
	let w = Widget.create(id: 5)
	let g = Gadget.create(id: 3)
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
	let x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'NotRunnable'

function execute(r Runnable) returns Integer
	return r.run()
end 'execute'

function main() returns ExitCode
	let n = NotRunnable.create(x: 1)
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
	let n Integer

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
	let i = Impl.create(n: 10)
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
	let n Integer

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
	let c1 = SimpleCounter.create(n: 17)
	let c2 = SimpleCounter.create(n: 25)
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
