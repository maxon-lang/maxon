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

A closure does not capture by reference. A captured scalar is copied when the closure is created, so a later change to the original variable is not visible inside the closure, and an assignment inside the closure does not reach the outer scope. A captured managed local moves into the closure; a captured parameter, `self` or field is retained (`closure-capture.md`). Because the closure holds its own copy, passing a captured name to a parameter the callee reassigns is E3019: the write could not reach the variable.

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
error E3070: specs/pass-by-reference/pass-by-reference.borrowed-managed-global-to-reassigning-param-error.maxon:13:2: cannot mutate 'pool' via 'wipe' while it is borrowed by 'held' (borrowed at line 12)
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
error E3019: specs/pass-by-reference/pass-by-reference.let-to-mutating-param-error.maxon:11:2: cannot pass 'n' to function that mutates parameter 'x' (in main)
```

<!-- test: pass-by-reference.captured-let-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setIt(dest Integer) returns Integer
	dest = 99
	return dest
end 'setIt'

function main() returns ExitCode
	let x = 1
	let f = function() gives setIt(x)
	print("{f()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/pass-by-reference/pass-by-reference.captured-let-to-mutating-param-error.maxon:12:27: cannot pass 'x' to function that mutates parameter 'dest' (in main$closure_0): the closure holds its own copy of 'x', so a write through 'dest' cannot reach it
```

<!-- test: pass-by-reference.captured-var-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setIt(dest Integer) returns Integer
	dest = 99
	return dest
end 'setIt'

function main() returns ExitCode
	var x = 1
	x = 2
	let f = function() gives setIt(x)
	print("{f()} {x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:13:27: cannot pass 'x' to function that mutates parameter 'dest' (in main$closure_0): the closure holds its own copy of 'x', so a write through 'dest' cannot reach it
```

<!-- test: pass-by-reference.captured-parameter-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setIt(dest Integer) returns Integer
	dest = 99
	return dest
end 'setIt'

function run(p Integer) returns Integer
	let f = function() gives setIt(p)
	return f()
end 'run'

function main() returns ExitCode
	var n = 1
	print("{run(n)} {n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: <fragment>:11:27: cannot pass 'p' to function that mutates parameter 'dest' (in run$closure_0): the closure holds its own copy of 'p', so a write through 'dest' cannot reach it
```

<!-- test: pass-by-reference.overloads-disagreeing-on-by-reference-bind-each-by-its-own-convention -->
```maxon

typealias Integer = int(i64.min to i64.max)

function settle(dest Integer, src Integer)
	dest = src
end 'settle'

function settle(dest String, src String) returns String
	return "{dest}{src}"
end 'settle'

function main() returns ExitCode
	var n = 1
	settle(n, src: 5)
	var s = "a"
	print("{settle(s, src: "b")} {n}\n")
	s = "c"
	return 0
end 'main'
```
```stdout
ab 5
```

<!-- test: pass-by-reference.a-let-to-the-reassigning-overload-is-refused-and-to-the-by-value-one-is-not -->
```maxon

typealias Integer = int(i64.min to i64.max)

function settle(dest Integer, src Integer)
	dest = src
end 'settle'

function settle(dest String, src String) returns String
	return "{dest}{src}"
end 'settle'

function main() returns ExitCode
	let n = 1
	settle(n, src: 5)
	let s = "a"
	print("{settle(s, src: "b")} {n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/pass-by-reference/pass-by-reference.a-let-to-the-reassigning-overload-is-refused-and-to-the-by-value-one-is-not.maxon:15:2: cannot pass 'n' to function that mutates parameter 'dest' (in main)
```

<!-- test: pass-by-reference.an-out-of-range-literal-at-the-reassigning-overload-is-a-compile-error -->
```maxon

typealias Count = int(0 to 1000)

function settle(dest Count, src Count)
	dest = src
end 'settle'

function settle(dest String, src String) returns String
	return "{dest}{src}"
end 'settle'

function main() returns ExitCode
	settle(2000, src: 5)
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/pass-by-reference/pass-by-reference.an-out-of-range-literal-at-the-reassigning-overload-is-a-compile-error.maxon:14:2: Value 2000 is outside the range of 'Count' (int(0 to 1000))
```

<!-- test: pass-by-reference.an-async-call-to-overloads-disagreeing-on-by-reference-binds-its-member -->
```maxon

typealias Integer = int(i64.min to i64.max)

function settle(dest Integer, src Integer) returns String
	dest = src
	return "{dest}"
end 'settle'

function settle(dest String, src String) returns String
	return "{dest}{src}"
end 'settle'

function main() returns ExitCode
	var n = 1
	let p = async settle(n, src: 5)
	var s = "a"
	let q = async settle(s, src: "b")
	print("{await p} {await q} {n}\n")
	s = "c"
	return 0
end 'main'
```
```stdout
5 ab 1
```

<!-- test: pass-by-reference.an-async-call-to-overloads-with-different-results-binds-its-member -->
```maxon

typealias Integer = int(i64.min to i64.max)

function settle(dest Integer, src Integer) returns Integer
	Scheduler.yield()
	dest = src
	return dest
end 'settle'

function settle(dest String, src String) returns String
	Scheduler.yield()
	return "{dest}{src}"
end 'settle'

function main() returns ExitCode
	var n = 1
	let p = async settle(n, src: 5)
	var s = "a"
	let q = async settle(s, src: "b")
	print("{await p} {await q} {n}\n")
	s = "c"
	return 0
end 'main'
```
```stdout
5 ab 1
```

<!-- test: pass-by-reference.overloads-that-both-forward-a-parameter-write-through -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setInt(x Integer)
	x = 77
end 'setInt'

function setText(x String)
	x = "set"
end 'setText'

function relay(a Integer)
	setInt(a)
end 'relay'

function relay(a String)
	setText(a)
end 'relay'

function main() returns ExitCode
	var n = 1
	var s = "unset"
	relay(n)
	relay(s)
	print("{n} {s}\n")
	return 0
end 'main'
```
```stdout
77 set
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
10
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

<!-- test: pass-by-reference.an-async-call-resolving-to-a-by-reference-member-writes-the-coroutines-own-storage -->
The chosen member assigns `dest`. An `async` argument is moved into the coroutine, so the member writes a cell the coroutine owns.
```maxon
typealias Integer = int(i64.min to i64.max)

function settle(dest String, src Integer) returns Integer
	Scheduler.yield()
	dest = "set-{src}"
	return src
end 'settle'

function settle(dest String, src String) returns Integer
	Scheduler.yield()
	print("{dest}{src}\n")
	return 0
end 'settle'

function main() returns ExitCode
	var s = "a-{1}"
	let p = async settle(s, src: 5)
	let r = await p
	print("{r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5
```

<!-- test: pass-by-reference.an-async-call-whose-promise-outlives-the-frame-of-a-by-reference-argument -->
The promise is stored and awaited after the spawning frame returned; the callee's write lands in the coroutine's own cell.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function rename(dest String, n Integer) returns Integer
	Scheduler.yield()
	dest = "renamed-{n}"
	print("callee wrote {dest}\n")
	return n
end 'rename'

function kick(promises IntPromiseArray)
	var s = "orig-{1}"
	promises.push(async rename(s, n: 7))
	print("kick spawned\n")
end 'kick'

function main() returns ExitCode
	var promises = IntPromiseArray.create()
	kick(promises)

	for p in promises 'each'
		print("awaited {await p}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
kick spawned
callee wrote renamed-7
awaited 7
```

<!-- test: pass-by-reference.an-async-call-copies-a-scalar-into-the-coroutines-by-reference-cell -->
A scalar argument is copied into the coroutine's cell, so the caller's variable keeps its value.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function bump(dest Integer, n Integer) returns Integer
	Scheduler.yield()
	dest = dest + n
	return dest
end 'bump'

function kick(promises IntPromiseArray)
	var c = 100
	promises.push(async bump(c, n: 7))
	print("kick sees {c}\n")
end 'kick'

function spray(k Integer) returns Integer
	let a = k + 1
	let b = k + 2
	let d = k + 3
	print("spray {a} {b} {d}\n")
	return a + b + d
end 'spray'

function main() returns ExitCode
	var promises = IntPromiseArray.create()
	kick(promises)
	_ = spray(1)
	for p in promises 'each'
		print("awaited {await p}\n")
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
kick sees 100
spray 2 3 4
awaited 107
```

<!-- test: pass-by-reference.an-async-call-with-a-global-at-a-by-reference-position-leaves-the-global-unchanged -->
A module-level `var` handed to an `async` callee that assigns its parameter is unchanged.
```maxon
typealias Integer = int(i64.min to i64.max)

var shared = "orig"

function rename(dest String, n Integer) returns Integer
	Scheduler.yield()
	dest = "renamed-{n}"
	return n
end 'rename'

function main() returns ExitCode
	let p = async rename(shared, n: 7)
	let r = await p
	print("{r} {shared}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 orig
```

<!-- test: pass-by-reference.an-async-string-argument-a-callee-reassigns-is-released-on-await-and-on-cancel -->
The coroutine's cell releases its final value once on the await path and once on the cancel path.
```maxon
typealias Integer = int(i64.min to i64.max)

function rename(dest String, n Integer) returns Integer
	Scheduler.yield()
	dest = "renamed {n} padded out long enough to heap allocate"
	print("{dest}\n")
	return n
end 'rename'

function main() returns ExitCode
	var first = "first padded out long enough to heap allocate {1}"
	let p = async rename(first, n: 7)
	print("awaited {await p}\n")
	var second = "second padded out long enough to heap allocate {2}"
	let q = async rename(second, n: 8)
	q.cancel()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
renamed 7 padded out long enough to heap allocate
awaited 7
```

<!-- test: pass-by-reference.a-string-read-after-an-async-spawn-to-a-reassigning-callee-is-use-after-move -->
The `var` is moved into the coroutine, not handed as storage, so reading it after the spawn is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

function rename(dest String, n Integer) returns Integer
	Scheduler.yield()
	dest = "renamed-{n}"
	return n
end 'rename'

function main() returns ExitCode
	var s = "orig-{1}"
	let p = async rename(s, n: 7)
	let r = await p
	print("{r} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:14: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: pass-by-reference.a-reassigned-interface-parameter-writes-the-callers-variable -->
A parameter held at an interface type is reassigned; the caller's variable sees the new conformer, and an
r-value argument gets a scratch cell of its own.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function squareShape(side Integer) returns Shape
	return Square.create(side)
end 'squareShape'

function grow(s Shape)
	if s.area() < 5 'small'
		s = squareShape(4)
	end 'small'
end 'grow'

function main() returns ExitCode
	var shape = squareShape(1)
	grow(shape)
	grow(squareShape(3))
	grow(Square.create(1))
	print("{shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
16
```

<!-- test: pass-by-reference.an-interface-parameter-after-a-reassigned-one-keeps-its-witness -->
`s` arrives as a cell holding both halves of the interface value, and `from` arrives by value; `from`'s
dispatch still reaches its own conformer.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function squareShape(side Integer) returns Shape
	return Square.create(side)
end 'squareShape'

function adopt(s Shape, from Shape) returns Integer
	let wanted = from.area()

	if s.area() < wanted 'smaller'
		s = squareShape(2)
	end 'smaller'

	return wanted
end 'adopt'

function main() returns ExitCode
	var shape = squareShape(1)
	let wanted = adopt(shape, from: Square.create(3))
	print("{wanted} {shape.area()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9 4
```

<!-- test: pass-by-reference.error.a-conformer-variable-cannot-take-an-interface-by-reference-parameter -->
`grow` may store any `Shape` into its parameter, so a `Square` variable cannot be the storage it writes.
```maxon
typealias Integer = int(i64.min to i64.max)

interface Shape
	function area() returns Integer
end 'Shape'

type Square implements Shape
	var side as Integer

	static function create(side Integer) returns Self
		return Self{side: side}
	end 'create'

	function area() returns Integer
		return self.side * self.side
	end 'area'
end 'Square'

function squareShape(side Integer) returns Shape
	return Square.create(side)
end 'squareShape'

function grow(s Shape)
	if s.area() < 5 'small'
		s = squareShape(4)
	end 'small'
end 'grow'

function main() returns ExitCode
	var square = Square.create(1)
	grow(square)
	print("{square.area()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:32:7: argument type mismatch for 's': expected 'Shape', got 'Square'
```
