---
feature: pass-by-reference
status: experimental
keywords: [reference, pass-by-reference, mutation, ref, closure, capture]
category: core
---

# Pass by Reference

## Documentation

In Maxon, all parameters are passed by reference. When you pass a variable to a function, the function receives a reference to the original value, not a copy.

### Reading Referenced Values

A function can read a parameter that was passed by reference:

```text
function double(x Integer) returns Integer
  return x * 2
end 'double'

var n = 21
var result = double(n)  // result is 42
```

### Mutating Referenced Values

A function can assign to its parameters, and the caller will see the change:

```text
function increment(x Integer)
  x = x + 1
end 'increment'

var n = 10
increment(n)
// n is now 11
```

### Immutability Enforcement

If a `let` variable is passed to a function that writes that parameter — assigning to it, or writing a field of
it — the compiler reports an error. This ensures immutable bindings cannot be modified indirectly.

### Temporaries from Literals and Expressions

When a literal or expression result is passed to a function, a temporary is created. The function can read it normally:

```text
var result = double(42)       // literal creates a temporary
var result2 = double(a + b)   // expression result creates a temporary
```

### Closure Capture

Closures capture variables by reference. Changes to the original variable are visible inside the closure, and assignments inside the closure are visible to the outer scope.

### Reassigning Reference-Typed Parameters

A reference-typed (managed) parameter may be reassigned, and the caller observes the new value. The reassignment releases the caller's previous value exactly once and takes ownership of the new one, whether the new value is a borrow from a container, a freshly created value, or another local — no reference leaks and no value is released twice. This holds for a user `type` struct and for `String`.

### Reassigning Scalar Parameter Types

Write-back on reassignment is independent of a scalar parameter's representation. A `float` (or a `Float`-aliased ranged type), a float-backed enum, an integer-backed enum, a payload-free union, `bool`, and byte all propagate their reassignment to the caller, exactly as an integer does. The caller always observes the reassigned value.

## Tests

<!-- test: pass-by-reference.basic-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let n = 42
	return readVal(n)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.mutate-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	var n = 0
	setTo99(n)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.mutate-module-global-ref -->
### A module-level `var` at a by-reference position is written
Every parameter is passed by reference, and a module-level `var` is storage with an address like any
other. It is not cell-resident the way a local `var` is, so the write has to reach the global itself.
```maxon

typealias Integer = int(i64.min to i64.max)

var counter = 0

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	setTo99(counter)
	print("{counter}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.mutate-self-field-ref -->
### A self field at a by-reference position is written
A field is storage the receiver owns, reached as an offset from its base. Handing one to a
by-reference parameter must write the field, not a copy of it.
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

type Counter
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump()
		setTo99(self.n)
	end 'bump'

	function value() returns Integer
		return self.n
	end 'value'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create()
	c.bump()
	print("{c.value()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.mutate-struct-binding-field-ref -->
### A field of an ordinary struct binding at a by-reference position is written
`self.n` is not the only field that reaches a by-reference parameter. A field of any mutable struct
binding is storage with an address, and the write has to reach the field itself.
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

type Point
	export var x as Integer

	static function create() returns Self
		return Self{x: 0}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create()
	p.x = 1
	setTo99(p.x)
	print("{p.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.mutate-nested-field-ref -->
### A field reached through a CHAIN is written, from either kind of base
One hop is not a special case. A chain names storage exactly as a single field does, and the address
handed over is the one the matching assignment would store through — so the rule is the store door's:
the last field decides, and an intermediate `let` fixes only which record is reached, never whether
its own fields may be written.
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

type Inner
	export var b as Integer

	static function create() returns Self
		return Self{b: 0}
	end 'create'
end 'Inner'

type Outer
	export var a as Inner

	static function create() returns Self
		return Self{a: Inner.create()}
	end 'create'

	function bumpOwn()
		setTo99(self.a.b)
	end 'bumpOwn'

	function inner() returns Integer
		return self.a.b
	end 'inner'
end 'Outer'

function main() returns ExitCode
	var p = Outer.create()
	p.a.b = 1
	setTo99(p.a.b)

	var q = Outer.create()
	q.a.b = 2
	q.bumpOwn()

	print("{p.a.b} {q.inner()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99 99
```

<!-- test: pass-by-reference.mutate-managed-module-global-ref -->
### A MANAGED module-level `var` at a by-reference position is written
The slot handed over holds a record, so the callee's reassignment releases what the global was holding
and stores what it built. The caller reads the new container out of the same slot.
```maxon

typealias StringArray = Array with String

var pool = ["alpha"]

function refill(items StringArray)
	items = StringArray.create()
	items.push("beta")
	items.push("gamma")
end 'refill'

function main() returns ExitCode
	refill(pool)
	let first = try pool.get(0) otherwise "?"
	print("{pool.count()} {first}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 beta
```

<!-- test: pass-by-reference.borrowed-managed-global-to-reassigning-param-error -->
### Handing a BORROWED managed global to a reassigning parameter is E3070
The reassignment inside the callee drops the record the caller's slot was holding, freeing every element
borrowed out of it — the same write `pool = StringArray.create()` performs at the caller, and refused on
the same terms. The hand-over of real storage is what makes the two spellings one write: a callee that
rebinds a by-reference parameter of a container type writes that argument's storage, which is the column
E3070 settles every call site against.
```maxon

typealias StringArray = Array with String

var pool = ["hello world this is a long string for heap allocation"]

function wipe(items StringArray)
	items = StringArray.create()
end 'wipe'

function main() returns ExitCode
	let held = try pool.get(0) otherwise ""
	wipe(pool)
	print("[{held}]")
	return 0
end 'main'
```
```maxoncstderr
error E3070: specs/fragments/pass-by-reference/pass-by-reference.borrowed-managed-global-to-reassigning-param-error.test:13:2: cannot mutate 'pool' via 'wipe' while it is borrowed by 'held' (borrowed at line 12)
```

<!-- test: pass-by-reference.mutate-ranged-float-alias-ref -->
### A RANGED FLOAT ALIAS field and global are written, and the address is not converted
A ranged float alias is spelled `named`, not `float`, so a door that retypes the handed-over address
without resolving the alias hands the callee a value whose machine class disagrees with the parameter's
— and an argument that crosses the float domain has a conversion inserted on it. The address is an
address in either case, so the alias is resolved where the slot is retyped.
```maxon

typealias Weight = float(0.0 to 1000.0)

var scale = 1.0 as Weight

function setTo99(w Weight)
	w = 99.0
end 'setTo99'

type Box
	export var w as Weight

	static function create() returns Self
		return Self{w: 1.0}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create()
	setTo99(b.w)
	setTo99(scale)
	print("{b.w} {scale}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99.0 99.0
```

<!-- test: pass-by-reference.mutate-tuple-member-ref -->
### A POSITIONAL tuple member at a by-reference position is written
`t.0` and `t._0` name one field, so they cannot have opposite semantics at a by-reference position. The
token walk that predicts the hand-over asks the same member-name predicate the real chain walk does,
which is what admits the positional spelling.
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	var pair = (1, 2)
	setTo99(pair.0)
	setTo99(pair._1)
	print("{pair.0} {pair.1}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99 99
```

<!-- test: pass-by-reference.immutable-primitive-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let n = 37
	return readVal(n)
end 'main'
```
```exitcode
37
```

<!-- test: pass-by-reference.literal-creates-temporary -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	return readVal(42)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.expression-creates-temporary -->
```maxon

typealias Integer = int(i64.min to i64.max)

function readVal(x Integer) returns Integer
	return x
end 'readVal'

function main() returns ExitCode
	let a = 20
	let b = 22
	return readVal(a + b)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.reassign-literal-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

function reassign(x Integer) returns Integer
	x = x + 1
	return x
end 'reassign'

function main() returns ExitCode
	return reassign(41)
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.reassign-call-result-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

function reassign(x Integer) returns Integer
	x = x + 1
	return x
end 'reassign'

function makeVal() returns Integer
	return 41
end 'makeVal'

function main() returns ExitCode
	return reassign(makeVal())
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.reassign-rvalue-and-variable -->
```maxon

typealias Integer = int(i64.min to i64.max)

function reassignReturn(x Integer) returns Integer
	x = x + 1
	return x
end 'reassignReturn'

function makeThirty() returns Integer
	return 30
end 'makeThirty'

function reassignVoid(x Integer)
	x = x + 50
end 'reassignVoid'

function main() returns ExitCode
	let fromLiteral = reassignReturn(5)
	let fromCall = reassignReturn(makeThirty())
	var v = 100
	reassignVoid(v)
	print("{fromLiteral}\n")
	print("{fromCall}\n")
	print("{v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6
31
150

```

<!-- test: pass-by-reference.struct-ref-field-mutation -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function setX(p Point)
	p.x = 99
end 'setX'

function main() returns ExitCode
	var p = Point.create(1, y: 2)
	setX(p)
	print("{p.x}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
```

<!-- test: pass-by-reference.struct-ref-reassignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function replacePoint(p Point)
	p = Point.create(99, y: 99)
end 'replacePoint'

function main() returns ExitCode
	var p = Point.create(1, y: 2)
	replacePoint(p)
	print("{p.x}\n")
	print("{p.y}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99
99

```

<!-- test: pass-by-reference.managed-container-borrow-reassign -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

function repl(nodes NodeArray, cur Node) returns Integer
	cur = try nodes.get(0) otherwise return -1
	return cur.id
end 'repl'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	print("r={repl(nodes, cur: c)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=0
```

<!-- test: pass-by-reference.managed-owned-value-reassign -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

function replaceWithNew(cur Node) returns Integer
	cur = Node.create(99)
	return cur.id
end 'replaceWithNew'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	print("r={replaceWithNew(c)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=99
```

<!-- test: pass-by-reference.managed-local-variable-reassign -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

function replaceFromLocal(cur Node) returns Integer
	let other = Node.create(42)
	cur = other
	return cur.id
end 'replaceFromLocal'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	print("r={replaceFromLocal(c)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=42
```

<!-- test: pass-by-reference.let-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	let n = 5
	setTo99(n)
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/pass-by-reference/pass-by-reference.let-to-mutating-param-error.test:11:2: cannot pass 'n' to function that mutates parameter 'x' (in main)
```

<!-- test: pass-by-reference.nested-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

function inner(x Integer)
	x = 77
end 'inner'

function outer(x Integer)
	inner(x)
end 'outer'

function main() returns ExitCode
	var n = 0
	outer(n)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
77
```

<!-- test: pass-by-reference.transitive-reassign-through-try-chain -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

enum ForwardError implements Error
	failed
end 'ForwardError'

function reassign(cur Node, replacement Node) returns Integer throws ForwardError
	if replacement.id < 0 'negative'
		throw ForwardError.failed
	end 'negative'

	cur = replacement
	return cur.id
end 'reassign'

function forward2(cur Node, replacement Node) returns Integer throws ForwardError
	return try reassign(cur, replacement: replacement)
end 'forward2'

function forward1(cur Node, replacement Node) returns Integer throws ForwardError
	return try forward2(cur, replacement: replacement)
end 'forward1'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	let r = try forward1(c, replacement: Node.create(99)) otherwise return 2
	print("r={r} c={c.id}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=99 c=99
```

<!-- test: pass-by-reference.transitive-reassign-through-sibling-methods -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

type Forwarder
	export var tag as Integer

	static function create() returns Forwarder
		return Self{tag: 0}
	end 'create'

	function reassign(cur Node, replacement Node) returns Integer
		cur = replacement
		return cur.id
	end 'reassign'

	function forwardInner(cur Node, replacement Node) returns Integer
		return reassign(cur, replacement: replacement)
	end 'forwardInner'

	function forwardOuter(cur Node, replacement Node) returns Integer
		return forwardInner(cur, replacement: replacement)
	end 'forwardOuter'

	function run() returns Integer
		var nodes = NodeArray.create()
		nodes.push(Node.create(0))
		nodes.push(Node.create(1))
		var c = try nodes.get(1) otherwise return -1
		let r = forwardOuter(c, replacement: Node.create(88))
		return r * 100 + c.id
	end 'run'
end 'Forwarder'

function main() returns ExitCode
	let f = Forwarder.create()
	print("{f.run()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8888
```

<!-- test: pass-by-reference.transitive-reassign-through-method-receiver -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

type Forwarder
	export var tag as Integer

	static function create() returns Forwarder
		return Self{tag: 0}
	end 'create'

	function reassign(cur Node, replacement Node) returns Integer
		cur = replacement
		return cur.id
	end 'reassign'
end 'Forwarder'

function helper(cur Node, f Forwarder, replacement Node) returns Integer
	return f.reassign(cur, replacement: replacement)
end 'helper'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	let f = Forwarder.create()
	let r = helper(c, f: f, replacement: Node.create(77))
	print("r={r} c={c.id}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=77 c=77
```

<!-- test: pass-by-reference.transitive-reassign-through-try-method-receiver -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

enum ForwardError implements Error
	failed
end 'ForwardError'

type Forwarder
	export var tag as Integer

	static function create() returns Forwarder
		return Self{tag: 0}
	end 'create'

	function reassign(cur Node, replacement Node) returns Integer throws ForwardError
		if replacement.id < 0 'negative'
			throw ForwardError.failed
		end 'negative'

		cur = replacement
		return cur.id
	end 'reassign'
end 'Forwarder'

function helper(cur Node, f Forwarder, replacement Node) returns Integer throws ForwardError
	return try f.reassign(cur, replacement: replacement)
end 'helper'

function main() returns ExitCode
	var nodes = NodeArray.create()
	nodes.push(Node.create(0))
	nodes.push(Node.create(1))
	var c = try nodes.get(1) otherwise return 1
	let f = Forwarder.create()
	let r = try helper(c, f: f, replacement: Node.create(88)) otherwise return 2
	print("r={r} c={c.id}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
r=88 c=88
```

<!-- test: pass-by-reference.transitive-reassign-through-sibling-to-free -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias NodeArray = Array with Node

type Node
	export var id as Integer

	static function create(id Integer) returns Node
		return Self{id: id}
	end 'create'
end 'Node'

function reassignFree(cur Node, replacement Node) returns Integer
	cur = replacement
	return cur.id
end 'reassignFree'

type Runner
	export var tag as Integer

	static function create() returns Runner
		return Self{tag: 0}
	end 'create'

	function forward(cur Node, replacement Node) returns Integer
		return reassignFree(cur, replacement: replacement)
	end 'forward'

	function run() returns Integer
		var nodes = NodeArray.create()
		nodes.push(Node.create(0))
		nodes.push(Node.create(1))
		var c = try nodes.get(1) otherwise return -1
		let r = forward(c, replacement: Node.create(66))
		return r * 100 + c.id
	end 'run'
end 'Runner'

function main() returns ExitCode
	let runner = Runner.create()
	print("{runner.run()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6666
```

<!-- test: pass-by-reference.multiple-params-mixed -->
```maxon

typealias Integer = int(i64.min to i64.max)

function process(a Integer, b Integer, c Integer)
	b = a + c + 90
end 'process'

function main() returns ExitCode
	let x = 1
	var y = 2
	let z = 3
	process(x, b: y, c: z)
	print("{x}\n")
	print("{y}\n")
	print("{z}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
94
3

```

<!-- test: pass-by-reference.enum-ref -->
```maxon

enum Color
	red
	blue
	green
end 'Color'

function switchColor(c Color)
	c = Color.green
end 'switchColor'

function main() returns ExitCode
	var c = Color.red
	switchColor(c)
	return c.rawValue
end 'main'
```
```exitcode
2
```

<!-- test: pass-by-reference.default-param-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

function addOffset(x Integer, offset Integer = 10) returns Integer
	return x + offset
end 'addOffset'

function main() returns ExitCode
	let result = addOffset(32)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.closure-capture-by-ref -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function() returns Integer
function apply(f FnTypeAlias1) returns Integer
	return f()
end 'apply'

function main() returns ExitCode
	let x = 42
	let result = apply(function() gives x)
	return result
end 'main'
```
```exitcode
42
```

<!-- test: pass-by-reference.closure-capture-after-mutation -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function() returns Integer
function apply(f FnTypeAlias1) returns Integer
	return f()
end 'apply'

function main() returns ExitCode
	var x = 10
	let f = function() gives x
	x = 99
	let result = apply(f)
	return result
end 'main'
```
```exitcode
99
```

<!-- test: pass-by-reference.mutate-float-ref -->
```maxon

typealias Float = float(f64.min to f64.max)

function setTo99(x Float)
	x = 99.0
end 'setTo99'

function main() returns ExitCode
	var n = 0.0
	setTo99(n)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
99.0
```

<!-- test: pass-by-reference.reassign-float-rvalue-and-variable -->
```maxon

typealias Float = float(f64.min to f64.max)

function reassignReturn(x Float) returns Float
	x = x + 1.0
	return x
end 'reassignReturn'

function makeThirty() returns Float
	return 30.0
end 'makeThirty'

function reassignVoid(x Float)
	x = x + 50.0
end 'reassignVoid'

function main() returns ExitCode
	let fromLiteral = reassignReturn(5.0)
	let fromCall = reassignReturn(makeThirty())
	var v = 100.0
	reassignVoid(v)
	print("{fromLiteral}\n")
	print("{fromCall}\n")
	print("{v}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
6.0
31.0
150.0

```

<!-- test: pass-by-reference.enum-float-backed-ref -->
```maxon

enum Ratio
	half = 0.5
	quarter = 0.25
end 'Ratio'

function switchRatio(r Ratio)
	r = Ratio.quarter
end 'switchRatio'

function main() returns ExitCode
	var r = Ratio.half
	switchRatio(r)
	if r == Ratio.quarter 'changed'
		return 1
	end 'changed'
	return 0
end 'main'
```
```exitcode
1
```

