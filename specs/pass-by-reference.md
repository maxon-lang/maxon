---
feature: pass-by-reference
status: experimental
keywords: [reference, pass-by-reference, mutation, ref, closure, capture]
category: core
---

# Pass by Reference

## Documentation

In Maxon, a parameter the function only reads is passed by value. A parameter the function assigns to — directly, or through one of its fields or elements — is passed by reference: the function receives the caller's variable, not a copy.

### Reading Referenced Values

A function that only reads a parameter receives its value:

```text
function double(x Integer) returns Integer
  return x * 2
end 'double'

var n = 21 as Integer
var result = double(n)  // result is 42
```

### Mutating Referenced Values

A function can assign to its parameters, and a caller that passed a `var` sees the change:

```text
function increment(x Integer)
  x = x + 1
end 'increment'

var n = 10 as Integer
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

A closure does not capture by reference. A captured scalar is copied when the closure is created, so a later change to the original variable is not visible inside the closure, and an assignment inside the closure does not reach the outer scope. A captured managed local moves into the closure; a captured parameter, `self` or field is retained (`closure-capture.md`). Because the closure holds its own copy, passing a captured name to a parameter the callee writes is E3159: the write could not reach the variable.

### Reassigning Reference-Typed Parameters

A reference-typed (managed) parameter may be reassigned, and the caller observes the new value. The reassignment releases the caller's previous value exactly once and takes ownership of the new one, whether the new value is an element read from a container, a freshly created value, or another local — no reference leaks and no value is released twice. This holds for a user `type` struct and for `String`.

### Reassigning Scalar Parameter Types

Write-back on reassignment is independent of a scalar parameter's representation. A `float` (or a `Float`-aliased ranged type), a float-backed enum, an integer-backed enum, a payload-free union, `bool`, and byte all propagate their reassignment to the caller, exactly as an integer does. The caller always observes the reassigned value.

### A call through a function value

A call through a function value of arity n passes the argument's storage at position i exactly when some free function of arity n writes its parameter i, whichever function the value holds: one that writes the parameter updates the caller's variable, one that only reads it reads the value held there. A closure literal takes its parameters by value and so only reads. A function that forwards one of its parameters to a function value passes that parameter by reference on the same terms. A `let` handed to a position the function the value may hold writes is refused as it is at a direct call (**E3159**), and an expression that is not storage gives the callee a temporary whose writes have no visible effect, also as at a direct call.

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
	var n = 0 as Integer
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

var counter = 0 as Integer

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

<!-- test: pass-by-reference.element-of-a-managed-global-outlives-a-reassigning-param -->
### An element read out of a managed global outlives a reassigning parameter
The reassignment inside the callee drops the record the caller's slot was holding — the same write
`pool = StringArray.create()` performs at the caller. `held` holds its own reference to the element it read,
so the element outlives the array and is still readable after the call.
```maxon

typealias StringArray = Array with String

var pool = ["alpha"]

function wipe(items StringArray)
	items = StringArray.create()
end 'wipe'

function main() returns ExitCode
	pool.push("held padded out long enough to heap allocate {40 + 2}")
	let held = try pool.get(1) otherwise ""
	wipe(pool)
	print("[{held}] {pool.count()}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
[held padded out long enough to heap allocate 42] 0
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
	var pair = (1 as Integer, 2 as Integer)
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
	var v = 100 as Integer
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
	var other = Node.create(42)
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
error E3159: specs/pass-by-reference/pass-by-reference.let-to-mutating-param-error.maxon:11:2: cannot pass 'n', which is read-only (a `let` binding), to a function that writes parameter 'x' (in main); pass a clone()
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
error E3159: specs/pass-by-reference/pass-by-reference.captured-let-to-mutating-param-error.maxon:12:27: cannot pass 'x' to a function that writes parameter 'dest' (in main$closure_0): the closure holds its own copy of 'x', so a write through 'dest' cannot reach the original; pass a clone()
```

<!-- test: pass-by-reference.captured-var-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setIt(dest Integer) returns Integer
	dest = 99
	return dest
end 'setIt'

function main() returns ExitCode
	var x = 1 as Integer
	x = 2
	let f = function() gives setIt(x)
	print("{f()} {x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:13:27: cannot pass 'x' to a function that writes parameter 'dest' (in main$closure_0): the closure holds its own copy of 'x', so a write through 'dest' cannot reach the original; pass a clone()
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
	var n = 1 as Integer
	print("{run(n)} {n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:27: cannot pass 'p' to a function that writes parameter 'dest' (in run$closure_0): the closure holds its own copy of 'p', so a write through 'dest' cannot reach the original; pass a clone()
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
	var n = 1 as Integer
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
error E3159: specs/pass-by-reference/pass-by-reference.a-let-to-the-reassigning-overload-is-refused-and-to-the-by-value-one-is-not.maxon:15:2: cannot pass 'n', which is read-only (a `let` binding), to a function that writes parameter 'dest' (in main); pass a clone()
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
	var n = 1 as Integer
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
	var n = 1 as Integer
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
	var n = 1 as Integer
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
	var n = 0 as Integer
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
	var y = 2 as Integer
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
	var x = 10 as Integer
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
	var n = 0.0 as Float
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
	var v = 100.0 as Float
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
The chosen member assigns `dest`. A reassigned parameter of an `async` callee gets storage the coroutine owns, so the member writes a cell of its own.
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
	var c = 100 as Integer
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

<!-- test: pass-by-reference.a-string-read-after-an-async-spawn-to-a-reassigning-callee-is-the-callers-own -->
The coroutine writes a cell of its own, and an `async` argument is shared rather than moved, so the caller
reads its `var` after the spawn and sees the value it passed.
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
```exitcode
0
```
```stdout
7 orig-1
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

### A call through a function value passes the variable's storage

<!-- test: pass-by-reference.function-value-call-updates-the-variable -->
A call through a function value is a call like any other: `bump` assigns its parameter, so the function value passes the variable's cell and the write reaches `v`.
```maxon
typealias Small = int(1 to 100)

function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	var v = 3 as Small
	let f = bump
	f(v)
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.callback-parameter-with-mixed-by-reference-and-by-value-candidates -->
`run` calls whichever function it is given on a local; `bump` writes its parameter and `keep` only reads it. Both reach the same call site through one convention: the cell is passed, `bump` writes through it and `keep`'s thunk reads it.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small) returns Small

function bump(n Small) returns Small
	n = 7
	return n
end 'bump'

function keep(n Small) returns Small
	return n
end 'keep'

function run(f Step) returns Small
	var v = 3 as Small
	_ = f(v)
	return v
end 'run'

function main() returns ExitCode
	let updated = run(bump)
	let untouched = run(keep)
	print("{updated} {untouched}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 3
```

<!-- test: pass-by-reference.closure-literal-callee-is-read-through-the-cell -->
A closure literal takes its parameters by value, so in a class with a by-reference function it is reached through a thunk that reads the cell, and it leaves the variable alone.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small) returns Small

function bump(n Small) returns Small
	n = 7
	return n
end 'bump'

function run(f Step) returns Small
	var v = 3 as Small
	_ = f(v)
	return v
end 'run'

function main() returns ExitCode
	let moved = run(bump)
	let offset = 10 as Small
	let untouched = run(function(n Small) gives n)
	let capturing = run(function(_ Small) gives offset)
	print("{moved} {untouched} {capturing}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 3 3
```

<!-- test: pass-by-reference.function-value-call-updates-a-float-variable -->
A by-reference `float` position is an address through a function value too, not a value in a float register.
```maxon
typealias Float = float(f64.min to f64.max)

function setTo99(x Float)
	x = 99.0
end 'setTo99'

function main() returns ExitCode
	var n = 0.0 as Float
	let f = setTo99
	f(n)
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

<!-- test: pass-by-reference.function-value-forwarded-through-a-caller-updates-the-variable -->
`apply` forwards its own parameter to the function it is given, so that parameter is passed by reference as it is for a direct `bump(n)`, and the caller's variable is updated.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

function apply(n Small, f Step)
	f(n)
end 'apply'

function main() returns ExitCode
	var v = 3 as Small
	apply(v, f: bump)
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.error.a-let-handed-to-a-by-reference-function-value-is-refused -->
A `let` handed to a position the function value writes is refused as it is at a direct call.
```maxon
typealias Small = int(1 to 100)

function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	let v = 3 as Small
	let f = bump
	f(v)
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:3: cannot pass 'v', which is read-only (a `let` binding), to a function that writes parameter 'n' (in main); pass a clone()
```


<!-- test: pass-by-reference.function-value-closure-and-named-share-one-convention -->
A closure literal and a named function of one function type are called alike: the named one writes through the cell, the closure reads it and leaves the variable alone.
```maxon
typealias Small = int(1 to 100)

function bump(n Small) returns Small
	n = 7
	return n
end 'bump'

function main() returns ExitCode
	var a = 3 as Small
	var b = 3 as Small
	let named = bump
	let literal = function(n Small) gives n
	_ = named(a)
	_ = literal(b)
	print("{a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 3
```

<!-- test: pass-by-reference.function-value-held-in-a-field-updates-the-variable -->
A function value read out of a field passes the variable's storage as a local one does.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

type Holder
	export let op as Step

	static function create(op Step) returns Holder
		return Self{op: op}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(bump)
	var v = 3 as Small
	holder.op(v)
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.function-value-call-with-an-expression-gives-the-callee-a-temporary -->
An expression at a by-reference position of a function value gives the callee a temporary whose writes have no visible effect, exactly as a direct call does.
```maxon
typealias Small = int(1 to 100)

function bump(n Small)
	n = 7
end 'bump'

function main() returns ExitCode
	let v = 3 as Small
	let f = bump
	f(v + 1)
	return v as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: pass-by-reference.function-value-shadowing-a-declared-function-updates-the-variable -->
A local function value named like a declared function is the one the call reaches, so the call is through a function value and passes the variable's storage.
```maxon
typealias Small = int(1 to 100)

function bump(n Small)
	n = 7
end 'bump'

function f(n Small) returns Small
	return n
end 'f'

function main() returns ExitCode
	var v = 3 as Small
	let f = bump
	f(v)
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.parameter-shadowing-a-declared-function-updates-the-variable -->
A function-typed parameter named like a declared function is called through its value, and the caller's variable is the one written.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

function f(n Small) returns Small
	return n
end 'f'

function run(f Step) returns Small
	var v = 3 as Small
	f(v)
	return v
end 'run'

function main() returns ExitCode
	return run(bump) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.field-named-like-a-method-called-dotted-updates-the-variable -->
A function-typed field is called through its value even when another type declares a method of that name.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

type Holder
	export let step as Step

	static function create(step Step) returns Holder
		return Self{step: step}
	end 'create'
end 'Holder'

type Counter
	let total as Small

	static function create() returns Counter
		return Self{total: 1}
	end 'create'

	function step(by Small) returns Small
		return total + by - by
	end 'step'
end 'Counter'

function main() returns ExitCode
	let holder = Holder.create(bump)
	let counter = Counter.create()
	var v = 3 as Small
	holder.step(v)
	return (v as ExitCode) + (counter.step(2) as ExitCode)
end 'main'
```
```exitcode
8
```

<!-- test: pass-by-reference.field-called-by-bare-name-in-a-method-updates-the-variable -->
Inside a method a field is in scope by its bare name; a function-typed field is called through its value even when a declared function has that name.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

function step(n Small) returns Small
	return n
end 'step'

type Runner
	let step as Step

	static function create(step Step) returns Runner
		return Self{step: step}
	end 'create'

	function run() returns Small
		var v = 3 as Small
		step(v)
		return v
	end 'run'
end 'Runner'

function main() returns ExitCode
	let runner = Runner.create(bump)
	return runner.run() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.captured-function-value-shadowing-a-declared-function-is-called-through-its-value -->
A closure calls a captured function value named like a declared function, through the value.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small) returns Small

function bump(n Small) returns Small
	n = 7
	return n
end 'bump'

function f(n Small) returns Small
	return n
end 'f'

function apply(op Step, x Small) returns Small
	return op(x)
end 'apply'

function main() returns ExitCode
	let f = bump
	let viaClosure = function(x Small) gives apply(f, x: x)
	return apply(viaClosure, x: 3) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.match-payload-named-like-a-declared-function-updates-the-variable -->
A name bound by a match arm shadows a declared function, so a call through it is a call through a function value.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

function keep(n Small) returns Small
	return n
end 'keep'

union Choice
	use(op Step)
	skip
end 'Choice'

function main() returns ExitCode
	let choice = Choice.use(bump)
	var v = 3 as Small

	match choice 'which'
		use(keep) then keep(v)
		skip then v = 1
	end 'which'

	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-by-reference.agreeing-shapes-share-one-convention-bump-first -->
Two function values whose parameter types differ only by an `implements` widening are one convention: the call through the `Percent` callback passes the cell the `Small` body writes through, whichever value is taken first.
```maxon
typealias Percent = int(0 to 100)
typealias Small = int(0 to 10) implements Percent
typealias Cb = function(Percent)

function bump(v Small)
	v = 3
end 'bump'

function show(v Percent)
	print("{v}\n")
end 'show'

function run(cb Cb)
	var p = 5 as Percent
	cb(p)
	print("{p}\n")
end 'run'

function main() returns ExitCode
	let b = bump
	let s = show
	var small = 8 as Small
	var big = 9 as Percent
	b(small)
	s(big)
	run(bump)
	print("{small}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
3
3
```

<!-- test: pass-by-reference.agreeing-shapes-share-one-convention-show-first -->
The same program with the values taken in the other order.
```maxon
typealias Percent = int(0 to 100)
typealias Small = int(0 to 10) implements Percent
typealias Cb = function(Percent)

function bump(v Small)
	v = 3
end 'bump'

function show(v Percent)
	print("{v}\n")
end 'show'

function run(cb Cb)
	var p = 5 as Percent
	cb(p)
	print("{p}\n")
end 'run'

function main() returns ExitCode
	let s = show
	let b = bump
	var small = 8 as Small
	var big = 9 as Percent
	b(small)
	s(big)
	run(bump)
	print("{small}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
3
3
```

<!-- test: pass-by-reference.library-callback-that-only-reads-is-unchanged -->
The program the next case differs from by one assignment: with no function writing a parameter, the stdlib parse the next program reuses carries no places, and the types here are named alike so the two are compiled as two programs.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(v Count) returns bool
	return v / 2 > 3
end 'isBig'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	xs.push(20)
	xs.push(9)
	let big = xs.filter(isBig)
	print("{big.count()} {xs.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3 4
```

<!-- test: pass-by-reference.error.a-let-handed-through-a-callback-parameter-is-refused -->
A `let` handed through a function-typed parameter to a position the function it may hold writes is refused, as at a direct call.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small)

function bump(n Small)
	n = 7
end 'bump'

function run(f Step)
	let v = 3 as Small
	f(v)
end 'run'

function main() returns ExitCode
	run(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:3: cannot pass 'v', which is read-only (a `let` binding), to a function that writes parameter 'n' (in run); pass a clone()
```

<!-- test: pass-by-reference.a-ranged-var-handed-to-a-function-value-keeps-its-range-guard -->
A `var` handed to a function value that takes the position by reference lives in a cell; the guard its sum owes is still run, before the cell is ever handed over.
```maxon
typealias Wide = int(0 to 5000)
typealias Num = int(0 to 1000) implements Wide

function bump(n Wide)
	n = 1
end 'bump'

function keep(n Wide)
	print("{n}\n")
end 'keep'

function f(a Num, b Num) returns Num
	let g = keep
	var t = a + b
	g(t)
	return t
end 'f'

function main() returns ExitCode
	var w = 3 as Wide
	bump(w)
	print("{f(900, b: 900)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-ranged-var-handed-to-a-function-value-keeps-its-range-guard.test:15: Range check failed: value outside typealias 'Num'
Stack trace:
  in f
  in main
  in mrt_start
```

<!-- test: pass-by-reference.error.a-branded-var-read-back-from-a-cell-keeps-its-brand -->
A cell-resident `var` of one array alias read back and handed where another alias is declared is the same refusal a plain local gets.
```maxon
typealias Item = int(0 to 100)
typealias Xs = Array with Item
typealias Ys = Array with Item

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function take(xs Xs)
	print("{xs.count()}\n")
end 'take'

function main() returns ExitCode
	var ys = Ys.create()
	reset(ys)
	take(ys)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:2: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: pass-by-reference.error.a-let-handed-to-a-callback-is-refused-when-only-a-widening-body-writes-it -->
A body whose parameter alias implements the callback's parameter alias stands for that callback type, so a `let` handed through the callback is refused even though no body of the callback's own alias writes it.
```maxon
typealias Wide = int(1 to 100)
typealias Narrow = int(1 to 10) implements Wide
typealias Step = function(Wide)

function bump(n Narrow)
	n = 7
end 'bump'

function show(n Wide)
	print("{n}\n")
end 'show'

function run(f Step)
	let v = 3 as Wide
	f(v)
end 'run'

function main() returns ExitCode
	run(show)
	run(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:16:3: cannot pass 'v', which is read-only (a `let` binding), to a function that writes parameter 'n' (in run); pass a clone()
```

<!-- test: pass-by-reference.error.a-function-variable-repointed-by-a-callee-takes-the-union -->
A function variable a callee re-points lives in a cell, so a call through it may land on the new function and a `let` handed to it is refused.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count
typealias Mutator = function(Counts)

function noop(xs Counts)
	print("{xs.count()}\n")
end 'noop'

function grow(xs Counts)
	xs.push(1)
end 'grow'

function repoint(g Mutator)
	g = grow
end 'repoint'

function make() returns Counts
	var made = Counts.create()
	made.push(2)
	return made
end 'make'

function main() returns ExitCode
	let data = make()
	var f = noop
	repoint(f)
	f(data)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:3: cannot pass 'data', which is read-only (a `let` binding), to a function that writes parameter 'xs' (in main); pass a clone()
```

<!-- test: pass-by-reference.library-callback-beside-a-writing-function-value -->
A program that takes a writing function as a value still calls a library method with a callback that only reads: the method hands its element on as a value, and the writer is no landing the method's element could reach, because nothing hands it in.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function bump(n Count)
	n = n / 2
end 'bump'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	xs.push(20)
	xs.push(9)
	var halved = 40 as Count
	let step = bump
	step(halved)
	let big = xs.filter(function(c Count) gives c > 3)
	print("{big.count()} {xs.count()} {halved} {try big.get(0) otherwise 0} {try big.get(2) otherwise 0}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4 4 20 4 20
```

<!-- test: pass-by-reference.error.a-writing-callback-handed-to-a-library-method-is-refused -->
`filter` hands each element of the array it walks to the callback as an immutable binding, so a callback that writes its parameter is refused where it is handed in, as the direct call `isBig(item)` inside a `for` is.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	let big = xs.filter(isBig)
	print("{big.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:14:15: 'isBig' writes its parameter 'n', but 'Array.filter' passes it the immutable 'item'
```

<!-- test: pass-by-reference.error.a-loop-variable-handed-to-a-parameter-that-reassigns-it-is-refused -->
A `for` loop variable cannot be reassigned, so handing it to a parameter its callee reassigns is refused, as `filter` handing its element to such a callback is.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	var big = 0 as Count

	for item in xs 'walk'
		if isBig(item) 'big'
			big = big + 1
		end 'big'
	end 'walk'

	print("{big}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:17:6: cannot pass 'item', which is read-only (a `for` loop variable), to a function that writes parameter 'n' (in main); pass a clone()
```

<!-- test: pass-by-reference.error.a-let-handed-to-a-type-parameter-a-callback-writes-is-refused -->
A callback typed over the type parameter writes the record it is called with, so the parameter the body hands it to is written, and a `let` handed there is refused.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Applier uses T
	typealias Each = function(T)
	export let size as Count

	static function create(size Count) returns Self
		return Self{size: size}
	end 'create'

	function apply(f Each, x T)
		f(x)
	end 'apply'
end 'Applier'

function grow(xs Counts)
	xs.push(1)
end 'grow'

typealias CountsApplier = Applier with Counts

function main() returns ExitCode
	let applier = CountsApplier.create(1)
	let xs = Counts.create()
	applier.apply(grow, x: xs)
	print("{xs.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:10: cannot pass 'xs', which is read-only (a `let` binding), to a function that writes parameter 'x' (in main); pass a clone()
```

<!-- test: pass-by-reference.a-field-typed-with-a-directory-qualified-function-alias-passes-by-reference -->
A field whose type is a function alias named through its directory is a function value, so a call through it passes its argument by reference.
```maxon
// --- file: alpha/ops.maxon
export typealias Count = int(0 to 1000)
export typealias Bump = function(Count)

// --- file: main.maxon
function bump(n alpha.Count)
	n = n + 1
end 'bump'

type Holder
	export var op as alpha.Bump

	static function create() returns Holder
		return Self{op: bump}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	var n = 1 as alpha.Count
	h.op(n)
	print("{n}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: pass-by-reference.error.a-writing-callback-handed-to-a-generic-user-method-is-refused -->
The same rule for a callback typed over a type parameter of a user type: `visit` passes its `let` to the callback.
```maxon
typealias Count = int(0 to 1000)

type Visitor uses T
	typealias Each = function(T)
	let seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function visit(f Each)
		let v = seed
		f(v)
	end 'visit'
end 'Visitor'

function bump(n Count)
	n = n / 2
end 'bump'

typealias CountVisitor = Visitor with Count

function main() returns ExitCode
	let visitor = CountVisitor.create(8)
	visitor.visit(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:26:10: 'bump' writes its parameter 'n', but 'Visitor.visit' passes it the immutable 'v'
```

<!-- test: pass-by-reference.a-sort-beside-an-unrelated-writer-of-the-same-arity-is-accepted -->
A comparator handed to `sort` is the only function value the sort's helpers call, so a function of the comparator's arity that writes its second parameter does not make them write theirs.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer

function byValue(a Integer, b Integer) returns Ordering
	return a.compare(b)
end 'byValue'

function addTo(n Integer, total Integer)
	total = total + n
end 'addTo'

function main() returns ExitCode
	var xs = Ints.create()
	xs.push(3)
	xs.push(1)
	xs.push(2)
	xs.sort(byValue)
	let add = addTo
	var total = 0 as Integer
	add(5, total)

	for x in xs 'show'
		print("{x} ")
	end 'show'

	print("{total}\n")
	return 0
end 'main'
```
```stdout
1 2 3 5
```
```exitcode
0
```

<!-- test: pass-by-reference.error.a-writing-callback-chosen-by-a-ternary-is-refused-where-it-is-handed-in -->
A callback picked between two functions by a ternary is handed in as whichever it is, so the writer among them refuses the immutable element.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function isSmall(n Count) returns bool
	return n < 3
end 'isSmall'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	let pick = isBig if xs.count() > 1 else isSmall
	let big = xs.filter(pick)
	print("{big.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:19:15: 'isBig' writes its parameter 'n', but 'Array.filter' passes it the immutable 'item'
```

<!-- test: pass-by-reference.error.a-writing-callback-read-from-a-field-is-refused-where-it-is-handed-in -->
A callback read out of a field is handed in as any function of its type, so a writer of that type refuses the immutable element.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count
typealias Pred = function(Count) returns bool

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function isSmall(n Count) returns bool
	return n < 3
end 'isSmall'

type Holder
	export let pred as Pred

	static function make(pred Pred) returns Holder
		return Holder{pred: pred}
	end 'make'
end 'Holder'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	let holder = Holder.make(isBig)
	let big = xs.filter(holder.pred)
	print("{big.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:15: 'isBig' writes its parameter 'n', but 'Array.filter' passes it the immutable 'item'
```

<!-- test: pass-by-reference.error.a-writing-callback-returned-by-a-call-is-refused-where-it-is-handed-in -->
A callback returned by a call is handed in as any function of its type.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count
typealias Pred = function(Count) returns bool

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function isSmall(n Count) returns bool
	return n < 3
end 'isSmall'

function choose() returns Pred
	return isBig
end 'choose'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	let big = xs.filter(choose())
	print("{big.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:23:15: 'isBig' writes its parameter 'n', but 'Array.filter' passes it the immutable 'item'
```

<!-- test: pass-by-reference.error.a-writing-callback-held-in-a-var-is-refused-where-it-is-handed-in -->
A callback held in a variable that is re-pointed is handed in as any function of its type.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(n Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function isSmall(n Count) returns bool
	return n < 3
end 'isSmall'

function main() returns ExitCode
	var xs = Counts.create()
	xs.push(4)
	xs.push(10)
	var pick = isSmall
	pick = isBig
	let big = xs.filter(pick)
	print("{big.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:20:15: 'isBig' writes its parameter 'n', but 'Array.filter' passes it the immutable 'item'
```

<!-- test: pass-by-reference.error.a-writing-callback-over-a-generic-instance-is-refused-where-it-is-handed-in -->
A type parameter inside a generic instance of the callback's parameter type still leaves the body compiled once for every instantiation, so the writer is refused where it is handed in.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

type Walker uses T
	typealias Items = Array with T
	typealias Step = function(Items)
	let items as Items

	static function create(items Items) returns Self
		return Self{items: items}
	end 'create'

	function walk(step Step)
		let view = items
		step(view)
	end 'walk'
end 'Walker'

function grow(xs Counts)
	xs.push(1)
end 'grow'

typealias CountWalker = Walker with Count

function main() returns ExitCode
	let walker = CountWalker.create(Counts.create())
	walker.walk(grow)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:28:9: 'grow' writes its parameter 'xs', but 'Walker.walk' passes it the immutable 'view'
```

<!-- test: pass-by-reference.error.a-writing-callback-returning-a-type-parameter-is-refused-where-it-is-handed-in -->
A type parameter only in the callback's return type is enough for the body to be compiled once for every instantiation, so a `let` handed through the body to a writing callback is refused.
```maxon
typealias Count = int(0 to 1000)

type Picker uses T
	typealias Make = function(Count) returns T
	let fallback as T

	static function create(fallback T) returns Self
		return Self{fallback: fallback}
	end 'create'

	function pick(make Make, count Count) returns T
		return make(count)
	end 'pick'
end 'Picker'

function bump(n Count) returns Count
	n = n / 2
	return n
end 'bump'

typealias CountPicker = Picker with Count

function main() returns ExitCode
	let picker = CountPicker.create(1)
	let eight = 8
	print("{picker.pick(bump, count: eight)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:27:17: cannot pass 'eight', which is read-only (a `let` binding), to a function that writes parameter 'count' (in main); pass a clone()
```

<!-- test: pass-by-reference.error.a-callback-union-is-refused-for-the-position-its-second-writer-writes -->
A callback chosen at run time between two functions is handed in as either, so a `let` at the position only the second one writes is refused.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count
typealias Pair = function(Counts, Counts)

function growFirst(x Counts, y Counts)
	x.push(1)
	print("{y.count()}\n")
end 'growFirst'

function growSecond(x Counts, y Counts)
	y.push(1)
	print("{x.count()}\n")
end 'growSecond'

function choose(k Count) returns Pair
	return growFirst if k > 0 else growSecond
end 'choose'

type Walker uses T
	typealias Each = function(T, T)
	export let size as Count

	static function create(size Count) returns Self
		return Self{size: size}
	end 'create'

	function both(f Each, a T, b T)
		let r = b
		f(a, r)
	end 'both'
end 'Walker'

typealias CountsWalker = Walker with Counts

function main() returns ExitCode
	let walker = CountsWalker.create(2)
	var xs = Counts.create()
	var ys = Counts.create()
	walker.both(choose(0), a: xs, b: ys)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:40:9: 'growSecond' writes its parameter 'y', but 'Walker.both' passes it the immutable 'r'
```

<!-- test: pass-by-reference.error.a-cell-resident-var-initialised-from-a-branded-local-keeps-the-brand -->
A `var` that lives in a cell and was initialised from a bare local of one array alias wears that alias on every read, so it is not an argument for a parameter of another.
```maxon
typealias Item = int(0 to 100)
typealias Xs = Array with Item
typealias Ys = Array with Item

function reset(ys Ys)
	ys = Ys.create()
end 'reset'

function take(xs Xs)
	print("{xs.count()}\n")
end 'take'

function main() returns ExitCode
	let ys = Ys.create()
	var a = ys.clone()
	reset(a)
	take(a)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:2: argument type mismatch for 'xs': expected 'Xs', got 'Ys'
```

<!-- test: pass-by-reference.a-narrow-var-written-wider-by-a-callee-is-caught-at-its-next-read -->
A callee that takes the position at a wider alias writes through the cell, and the caller's variable, which names the narrower alias, is range-checked when it is next read.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	bumpWide(x)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-var-written-wider-by-a-callee-is-caught-at-its-next-read.test:11: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-narrow-var-written-wider-through-a-function-value-is-caught-at-its-next-read -->
The same through a function value of the callee's own type.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	let g = bumpWide
	g(x)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-var-written-wider-through-a-function-value-is-caught-at-its-next-read.test:12: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-narrow-parameter-relayed-to-a-wider-callee-is-caught-at-the-call -->
A by-reference parameter of a narrow alias handed on to a callee that takes the wider alias is range-checked when that call returns, in the frame that handed it on.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function relay(n Narrow)
	bumpWide(n)
end 'relay'

function main() returns ExitCode
	var x = 3 as Narrow
	relay(x)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-parameter-relayed-to-a-wider-callee-is-caught-at-the-call.test:10: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in relay
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-narrow-var-written-wider-by-a-throwing-callee-is-caught-at-the-call -->
The copy-back check follows a call routed through `try` exactly as it follows a plain one.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

enum StepError implements Error
	refused
end 'StepError'

function bumpWide(n Wide) throws StepError
	n = 50

	if n > 60 'never'
		throw StepError.refused
	end 'never'
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	try bumpWide(x) otherwise return 1
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-var-written-wider-by-a-throwing-callee-is-caught-at-the-call.test:19: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-callee-taking-the-vars-own-alias-needs-no-copy-back-check -->
A callee that takes the variable's own alias writes only values that alias admits, so the variable is read back as written.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpNarrow(n Narrow)
	n = 7
end 'bumpNarrow'

function main() returns ExitCode
	var x = 3 as Narrow
	bumpNarrow(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: pass-by-reference.a-narrow-field-written-wider-by-a-callee-is-caught-at-the-call -->
A field handed by reference to a callee that takes the wider alias is range-checked when the call returns.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Meter
	export var level as Narrow

	static function create(level Narrow) returns Self
		return Self{level: level}
	end 'create'
end 'Meter'

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var m = Meter.create(3)
	bumpWide(m.level)
	print("{m.level}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-field-written-wider-by-a-callee-is-caught-at-the-call.test:19: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-narrow-module-var-written-wider-by-a-callee-is-caught-at-the-call -->
A module `var` handed by reference to a callee that takes the wider alias is range-checked when the call returns.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

var level = 3 as Narrow

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	bumpWide(level)
	print("{level}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-module-var-written-wider-by-a-callee-is-caught-at-the-call.test:12: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.an-async-call-copies-a-narrow-var-so-the-coroutine-cannot-widen-it -->
An `async` callee receives a copy of a scalar argument in a cell of its own, so a wider parameter it assigns never reaches the caller's narrower variable.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n Wide) returns Wide
	Scheduler.yield()
	n = 50
	return n
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	let p = async bumpWide(x)
	let r = await p
	print("{x} {r}\n")
	return 0
end 'main'
```
```stdout
3 50
```
```exitcode
0
```

<!-- test: pass-by-reference.a-service-message-copies-a-narrow-var-so-the-handler-cannot-widen-it -->
A message carries its arguments by value in the request, so a handler that assigns its wider parameter leaves the sender's narrower variable alone.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Meter
	var level as Wide

	static function create() returns Self
		return Self{level: 1}
	end 'create'

	export function set(n Wide)
		n = 50
		self.level = n
	end 'set'

	export function read() returns Wide
		return self.level
	end 'read'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	var x = 3 as Narrow
	h.set(x)
	let level = try await h.read() otherwise 0
	print("{x} {level}\n")
	return 0
end 'main'
```
```stdout
3 50
```
```exitcode
0
```

<!-- test: pass-by-reference.error.a-callback-typed-at-a-narrower-alias-refuses-a-body-taking-a-wider-one -->
A function type over the narrower alias does not admit a body that takes the wider alias, so the wider body can never be reached through it.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide
typealias Step = function(Narrow)

function bumpWide(n Wide)
	n = 50
end 'bumpWide'

function run(f Step)
	var x = 3 as Narrow
	f(x)
	print("{x}\n")
end 'run'

function main() returns ExitCode
	run(bumpWide)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:2: argument type mismatch for 'f': expected 'fn(Narrow)', got 'fn(Wide)'
```

<!-- test: pass-by-reference.error.a-union-cannot-be-generic-so-no-callback-position-mentions-one -->
A union takes no type parameters, so a function type cannot name an instance of a generic union.
```maxon
union Maybe uses T
	some(value T)
	none
end 'Maybe'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:2:13: unexpected token: 'uses'
```

<!-- test: pass-by-reference.error.an-indirect-call-with-too-few-arguments-beside-a-writer-of-the-written-arity -->
A call through a function value written with one argument to a two-parameter value is the arity error, whatever functions of the written arity write their parameter.
```maxon
typealias Small = int(1 to 100)

function bump(n Small)
	n = 7
end 'bump'

function add(a Small, b Small)
	print("{a}{b}\n")
end 'add'

function main() returns ExitCode
	let g = add
	var x = 3 as Small
	g(x)
	bump(x)
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:15:3: 'add' expects 2 argument(s) but 1 were provided
```

<!-- test: pass-by-reference.a-narrow-var-written-through-a-type-parameter-formal-is-caught-at-the-call -->
A formal that names no alias, a type parameter here, may be wider than the variable handed to it, so the variable is range-checked when the call returns.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

type Slot uses T
	export var held as T

	static function create(held T) returns Self
		return Self{held: held}
	end 'create'

	function overwrite(n T, with T)
		n = with
	end 'overwrite'
end 'Slot'

typealias WideSlot = Slot with Wide

function main() returns ExitCode
	var x = 3 as Narrow
	let s = WideSlot.create(7)
	let big = 5000 as Wide
	s.overwrite(x, with: big)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-var-written-through-a-type-parameter-formal-is-caught-at-the-call.test:23: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.a-narrow-var-written-wider-by-a-callee-that-then-throws-is-caught-before-the-handler -->
The check runs when the call returns, ahead of the branch to the handler, so a handler never reads the widened variable.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

enum StepError implements Error
	refused
end 'StepError'

function bumpWide(n Wide) throws StepError
	n = 5000
	throw StepError.refused
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow

	try bumpWide(x) otherwise 'handler'
		print("caught {x}\n")
	end 'handler'

	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at pass-by-reference.a-narrow-var-written-wider-by-a-callee-that-then-throws-is-caught-before-the-handler.test:17: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in main
  in mrt_start
```

<!-- test: pass-by-reference.error.a-float-var-handed-to-an-integer-parameter-is-refused-before-any-cell-is-shared -->
A variable and a parameter of different register classes never share a cell: the argument is refused where it is written.
```maxon
typealias Count = int(0 to 100)
typealias Real = float(0.0 to 100.0)

function bump(k Count)
	k = 7
end 'bump'

function main() returns ExitCode
	var r = 2.5 as Real
	bump(r)
	print("{r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: argument type mismatch for 'k': expected 'Count', got 'Real'
```

<!-- test: pass-by-reference.error.an-integer-var-handed-to-a-float-parameter-is-refused-before-any-cell-is-shared -->
```maxon
typealias Count = int(0 to 100)
typealias Real = float(0.0 to 100.0)

function scale(k Real)
	k = 2.5
end 'scale'

function main() returns ExitCode
	var n = 3 as Count
	scale(n)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: argument type mismatch for 'k': expected 'Real', got 'Count'
```

<!-- test: pass-by-reference.error.a-callback-writing-two-parameters-is-refused-once-per-parameter-however-often-the-body-calls-it -->
Both parameters of `bump` are written, and the body of `visit` calls the callback twice. The site where `bump` is handed in earns one diagnostic per parameter it writes, not one per call.
```maxon
typealias Count = int(0 to 1000)

type Visitor uses T
	typealias Each = function(T, T)
	let seed as T

	static function create(seed T) returns Self
		return Self{seed: seed}
	end 'create'

	function visit(f Each)
		let v = seed
		let w = seed
		f(v, w)
		f(v, w)
	end 'visit'
end 'Visitor'

function bump(a Count, b Count)
	a = a / 2
	b = b / 2
end 'bump'

typealias CountVisitor = Visitor with Count

function main() returns ExitCode
	let visitor = CountVisitor.create(8)
	visitor.visit(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:29:10: 'bump' writes its parameter 'a', but 'Visitor.visit' passes it the immutable 'v'
error E3159: <fragment>:29:10: 'bump' writes its parameter 'b', but 'Visitor.visit' passes it the immutable 'w'
```
