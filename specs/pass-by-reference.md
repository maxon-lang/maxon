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

If a `let` variable is passed to a function that assigns to that parameter, the compiler reports an error. This ensures immutable bindings cannot be modified indirectly.

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
	let p = Point.create(1, y: 2)
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

