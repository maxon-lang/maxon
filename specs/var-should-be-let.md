---
feature: var-should-be-let
status: stable
keywords: [variables, var, let, diagnostics, errors, mutability]
category: diagnostics
---

# Var Should Be Let Detection

## Documentation

Maxon requires that `var` declarations are actually mutated. If a variable is declared with `var` but never reassigned, it should be declared with `let` instead.

### Example Error

```maxon
function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/docs-example-1.test:3:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

### A mutable name may not write what a live `let` reads (E3078, E3102, E3159)

Every record is refcounted, and stores co-own it: a field, a container element or a call argument takes its
own reference, whatever the source. What is refused is a mutation that may reach a record a `let` still
reads afterwards — a `var` made from the value (**E3078**), a parameter the callee writes (**E3019**), a
field write or a mutating call such as `append` (**E3159**). A `String` is such a record too; naming a `let`
or its field directly (`var x = h.path`) is refused regardless. A bare `let` that is its record's sole owner
MOVES into a `var` (a later use is **E3102**); `let u = t` aliases. `clone()` gives the mutable side its own
record.

## Tests

<!-- test: var-never-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/var-never-reassigned.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: var-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	x = 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: let-no-error -->
```maxon

function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: var-reassigned-in-if -->
```maxon

function main() returns ExitCode
	var x = 0
	if x == 0 'check'
		x = 42
	end 'check'
	return x
end 'main'
```
```exitcode
42
```

<!-- test: multiple-var-first-reported -->
```maxon

function main() returns ExitCode
	var x = 1
	var y = 2
	return x + y
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/multiple-var-first-reported.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: var-from-immutable-integer-ok -->
```maxon

function main() returns ExitCode
	let x = 10
	var y = x
	y = 20
	return y
end 'main'
```
```exitcode
20
```

<!-- test: var-from-immutable-struct -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	return b.x
end 'main'
```
```exitcode
99
```

<!-- test: var-from-immutable-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	let f = double
	var g = f
	g = triple
	return g(10)
end 'main'
```
```exitcode
30
```

<!-- test: var-from-immutable-struct-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(42))
	var i = o.inner
	i.value = 99
	return i.value
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/var-from-immutable-struct-field.test:23:6: cannot assign from immutable variable to mutable binding 'i'; use 'let' instead of 'var', or use clone()
```

<!-- test: var-from-immutable-value-field-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(1, y: 2)
	var x = p.x
	x = 99
	return x
end 'main'
```
```exitcode
99
```

<!-- test: var-from-mutable-ok -->
```maxon

function main() returns ExitCode
	var x = 10
	x = 20
	var y = x
	y = 30
	return y
end 'main'
```
```exitcode
30
```

<!-- test: unused-takes-precedence -->
```maxon

function main() returns ExitCode
	var x = 10
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/var-should-be-let/unused-takes-precedence.test:4:6: unused variable: 'x'
```

<!-- test: returned-alias-of-a-let-string-field -->
A call's result may be a second reference to an argument's storage, so a mutable name made from it
reaches that storage exactly as `var x = h.path` would. `pass` hands back its borrowed argument, so `x`
would share `h.path`'s record and the append would rewrite a `let` field.
```maxon

type Holder
	export let path as String

	static function create(path String) returns Self
		return Self{path: path}
	end 'create'
end 'Holder'

function pass(s String) returns String
	return s
end 'pass'

function main() returns ExitCode
	let h = Holder.create("base")
	var x = pass(h.path)
	x.append(" more")
	print("{x} {h.path}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-of-a-let-string-field.test:17:6: cannot assign the result of 'pass', which may share storage with a field of an immutable variable, to mutable binding 'x'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-of-a-let-struct -->
The same door for a struct box: the write through `b` would land in `a`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function relay(p Point) returns Point
	return p
end 'relay'

function main() returns ExitCode
	let a = Point.create(1)
	var b = relay(a)
	b.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-of-a-let-struct.test:19:6: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-of-a-let-receiver-field -->
A method that returns one of its receiver's fields hands back the receiver's storage. `FilePath.toString()`
returns its `path` field, and the summary that says so crosses the standard library's cache.
```maxon

function main() returns ExitCode
	let fp = try FilePath.from("some/dir") otherwise panic("a relative path is valid")
	var s = fp.toString()
	s.append(" more")
	print("{s} {fp.toString()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-of-a-let-receiver-field.test:5:6: cannot assign the result of 'toString', which may share storage with immutable variable 'fp', to mutable binding 's'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-through-two-calls -->
What a callee may return includes what ITS callees may return, whichever order they are declared in.
```maxon

function main() returns ExitCode
	let n = 7
	let a = "lit{n}"
	var b = relay(a)
	b.append("x")
	print("{a} {b}\n")
	return 0
end 'main'

function relay(s String) returns String
	return pass(s)
end 'relay'

function pass(s String) returns String
	return s
end 'pass'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-through-two-calls.test:6:6: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-through-recursion -->
A recursive callee is summarised to a fixpoint: the arm that returns `s` is reached through the recursion.
```maxon

typealias Integer = int(i64.min to i64.max)

function pick(s String, depth Integer) returns String
	if depth == 0 'bottom'
		return s
	end 'bottom'

	return pick(s, depth: depth - 1)
end 'pick'

function main() returns ExitCode
	let n = 7
	let a = "lit{n}"
	var b = pick(a, depth: 3)
	b.append("x")
	print("{a} {b}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-through-recursion.test:16:6: cannot assign the result of 'pick', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-through-an-interface-call -->
A call through an interface reaches whichever witness the value carries, and `Tag.name()` returns its
receiver's field.
```maxon

interface Named
	function name() returns String
end 'Named'

type Tag implements Named
	export let label as String

	static function create(label String) returns Self
		return Self{label: label}
	end 'create'

	function name() returns String
		return self.label
	end 'name'
end 'Tag'

function nameOf(n Named) returns String
	return n.name()
end 'nameOf'

function main() returns ExitCode
	let t = Tag.create("tag")
	var y = nameOf(t)
	y.append(" more")
	print("{y} {t.label}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-through-an-interface-call.test:25:6: cannot assign the result of 'nameOf', which may share storage with immutable variable 't', to mutable binding 'y'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-through-a-closure -->
A closure may return what it captured.
```maxon

function main() returns ExitCode
	let n = 7
	let s = "cap{n}"
	let f = function() gives s
	var x = f()
	x.append(" more")
	print("{x} {s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-through-a-closure.test:7:6: cannot assign the result of 'f', which may share storage with immutable variable 's', to mutable binding 'x'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-bound-by-if-let -->
An `if let` binding is immutable storage like any other `let`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function relay(p Point) returns Point
	return p
end 'relay'

function make(n Integer) returns Point throws PointError
	if n < 0 'neg'
		throw PointError.negative
	end 'neg'

	return Point.create(n)
end 'make'

enum PointError implements Error
	negative
end 'PointError'

function main() returns ExitCode
	if let a = try make(1) 'ok'
		var b = relay(a)
		b.x = 99
		return a.x
	end 'ok'

	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-bound-by-if-let.test:31:7: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-bound-by-if-var -->
An `if var` binding is a mutable name like any other `var`.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function relay(p Point) returns Point
	return p
end 'relay'

function make(n Integer) returns Point throws PointError
	if n < 0 'neg'
		throw PointError.negative
	end 'neg'

	return Point.create(n)
end 'make'

enum PointError implements Error
	negative
end 'PointError'

function main() returns ExitCode
	let a = Point.create(1)
	if var b = try relayT(a) 'ok'
		b.x = 99
		return a.x
	end 'ok'

	return 0
end 'main'

function relayT(p Point) returns Point throws PointError
	if p.x < 0 'neg'
		throw PointError.negative
	end 'neg'

	return p
end 'relayT'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-bound-by-if-var.test:31:9: cannot assign the result of 'relayT', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: returned-alias-in-an-unreached-function -->
The rule is asked of every function, whether or not anything calls it; `fp` is read after the append.
```maxon

function helper() returns ExitCode
	let fp = try FilePath.from("some/dir") otherwise panic("a relative path is valid")
	var s = fp.toString()
	s.append(" more")
	print("{s} {fp.toString()}\n")
	return 0
end 'helper'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/returned-alias-in-an-unreached-function.test:5:6: cannot assign the result of 'toString', which may share storage with immutable variable 'fp', to mutable binding 's'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-laundered-through-a-container -->
A callee that stores its parameter in a container and hands an element back may return that
parameter's own record, and `a` is read after the call.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function launder(p Point) returns Point
	var arr = PointArray.create()
	arr.push(p)
	return try arr.get(0) otherwise panic("an element was pushed")
end 'launder'

function main() returns ExitCode
	let a = Point.create(1)
	var b = launder(a)
	b.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-laundered-through-a-container.test:23:6: cannot assign the result of 'launder', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-laundered-through-a-setter -->
The same through a declared method that stores its argument in a field and one that reads it back.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Box
	export var item as Point

	static function create(item Point) returns Self
		return Self{item: item}
	end 'create'

	function set(p Point)
		self.item = p
	end 'set'

	function get() returns Point
		return self.item
	end 'get'
end 'Box'

function launder(p Point) returns Point
	var box = Box.create(Point.create(0))
	box.set(p)
	return box.get()
end 'launder'

function main() returns ExitCode
	let a = Point.create(1)
	var b = launder(a)
	b.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-laundered-through-a-setter.test:37:6: cannot assign the result of 'launder', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-laundered-through-an-array-literal -->
An array literal stores its elements, so an element read back may be the record that went in.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function launder(p Point) returns Point
	let arr = [p]
	return try arr.get(0) otherwise panic("the literal has one element")
end 'launder'

function main() returns ExitCode
	let a = Point.create(1)
	var b = launder(a)
	b.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-laundered-through-an-array-literal.test:20:6: cannot assign the result of 'launder', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-of-a-let-global-through-a-call -->
A module-level `let` never moves, and its record is image data, so a write through a value derived
from it would fault.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

let origin = Point.create(1)

function relay(p Point) returns Point
	return p
end 'relay'

function main() returns ExitCode
	var b = relay(origin)
	b.x = 99
	return origin.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-of-a-let-global-through-a-call.test:20:6: cannot assign the result of 'relay', which may share storage with immutable variable 'origin', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-alias-of-a-parameter-bound-through-a-call -->
A `let` that aliases a parameter is still an immutable name, through a call as much as by name,
and `q` is read after the append.
```maxon

function pass(s String) returns String
	return s
end 'pass'

function tagIt(p String)
	let q = p
	var x = pass(q)
	x.append("!")
	print("{q}\n")
end 'tagIt'

function main() returns ExitCode
	let n = 7
	var a = "lit{n}"
	tagIt(a)
	a.append("?")
	print("{a}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-alias-of-a-parameter-bound-through-a-call.test:9:6: cannot assign the result of 'pass', which may share storage with immutable variable 'q', to mutable binding 'x'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-assigned-to-a-var -->
Assignment into a `var` from a sole `let` owner MOVES it, as a `var` binding does.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1)
	var b = Point.create(0)
	b = a
	b.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3102: specs/fragments/var-should-be-let/let-record-assigned-to-a-var.test:18:9: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-record-assigned-through-a-call -->
The assignment door asks of a call's result what the binding door asks.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1)
	var b = Point.create(0)
	b = relay(a)
	b.x = 99
	return a.x
end 'main'

function relay(p Point) returns Point
	return p
end 'relay'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-assigned-through-a-call.test:16:2: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-constructed-into-a-var -->
A constructor stores its argument, so `box.item` shares `a`'s record: the store is legal, and the
write through `box.item` is refused because `a` is read after it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Box
	export var item as Point

	static function create(item Point) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Point.create(1)
	var box = Box.create(a)
	box.item.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/let-record-constructed-into-a-var.test:24:2: cannot write through 'box.item', which may be the record of immutable variable 'a'; use clone()
```

<!-- test: let-record-pushed-into-a-var-container -->
A push stores `a`'s record in the container, so an element read back out may be it; with `a` read
after, a `var` may not be made from it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	let a = Point.create(1)
	var arr = PointArray.create()
	arr.push(a)
	var e = try arr.get(0) otherwise panic("an element was pushed")
	e.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-pushed-into-a-var-container.test:19:6: cannot assign the result of 'get', which may share storage with immutable variable 'a', to mutable binding 'e'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-stored-into-a-var-field -->
A store into a field of a `var` co-owns `a`'s record, so a write through the field while `a` is
live is refused.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Box
	export var item as Point

	static function create(item Point) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

function main() returns ExitCode
	let a = Point.create(1)
	var box = Box.create(Point.create(0))
	box.item = a
	box.item.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/let-record-stored-into-a-var-field.test:25:2: cannot write through 'box.item', which may be the record of immutable variable 'a'; use clone()
```

<!-- test: let-record-bound-to-a-var-moves -->
Binding a `var` from a sole `let` owner MOVES it.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(1)
	var q = p
	q.x = 99
	return p.x
end 'main'
```
```maxoncstderr
error E3102: specs/fragments/var-should-be-let/let-record-bound-to-a-var-moves.test:17:9: use of moved value 'p': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: let-record-popped-while-its-let-is-live -->
Popping the record back out does not make it the `var`'s alone while `a` is still read.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	let a = Point.create(1)
	var arr = PointArray.create()
	arr.push(a)
	var e = try arr.pop() otherwise panic("one element was pushed")
	e.x = 99
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-popped-while-its-let-is-live.test:19:6: cannot assign the result of 'pop', which may share storage with immutable variable 'a', to mutable binding 'e'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-borrow-live-refuses-a-var-of-the-same-element -->
A `let` bound to an element borrows it for as long as it is used. While `s` is live, a `var` read out
of the same container may be the record `s` names, and a write through it would change what `s` reads.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var arr = PointArray.create()
	arr.push(Point.create(1))
	let s = try arr.get(0) otherwise panic("an element was pushed")
	var e = try arr.get(0) otherwise panic("an element was pushed")
	e.x = 9
	return s.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-borrow-live-refuses-a-var-of-the-same-element.test:19:6: cannot assign the result of 'get', which may share storage with immutable variable 's', to mutable binding 'e'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-borrow-of-a-field-refuses-a-write-through-it -->
A `let` bound to a field of a `var` borrows that record, so a write reaching it through the `var` while
the `let` is live is refused.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Box
	export var item as Point

	static function create(item Point) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

function main() returns ExitCode
	var box = Box.create(Point.create(1))
	let p = box.item
	box.item.x = 99
	return p.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/let-borrow-of-a-field-refuses-a-write-through-it.test:24:2: cannot write through 'box.item', which may be the record of immutable variable 'p'; use clone()
```

<!-- test: let-record-written-through-an-interface-call -->
A method called through an interface writes whichever conformer's record the value holds, so a `var` that may
be `a`'s record is written through when a conformer's implementation writes its receiver.
```maxon

typealias Integer = int(i64.min to i64.max)

interface Bumpable
	function bump()
end 'Bumpable'

type Point implements Bumpable
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'

	function bump()
		self.x = self.x + 1
	end 'bump'
end 'Point'

function relay(p Bumpable) returns Bumpable
	return p
end 'relay'

function main() returns ExitCode
	let a = Point.create(1)
	var b = relay(a)
	b.bump()
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-written-through-an-interface-call.test:27:6: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-record-written-through-a-function-value -->
A call through a function value writes an argument wherever a body it may land on writes that parameter.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

function relay(p Point) returns Point
	return p
end 'relay'

function bumpIt(p Point)
	p.x = p.x + 1
end 'bumpIt'

function main() returns ExitCode
	let a = Point.create(1)
	var b = relay(a)
	let f = bumpIt
	f(b)
	return a.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/let-record-written-through-a-function-value.test:23:6: cannot assign the result of 'relay', which may share storage with immutable variable 'a', to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: let-borrow-taken-after-a-var-of-the-same-element -->
The same two names in the other order: `s` borrows the element `e` already holds, so the write through `e`
while `s` is live changes what `s` reads.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var arr = PointArray.create()
	arr.push(Point.create(1))
	var e = try arr.get(0) otherwise panic("an element was pushed")
	let s = try arr.get(0) otherwise panic("an element was pushed")
	e.x = 9
	return s.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/let-borrow-taken-after-a-var-of-the-same-element.test:20:2: cannot write through 'e', which may be the record of immutable variable 's'; use clone()
```

<!-- test: sibling-fields-do-not-alias -->
Two fields of one record are two places: an element read out of `pair.left` is no element of `pair.right`, so
writing through `dst` changes nothing `src` reads.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

type Pair
	export var left as PointArray
	export var right as PointArray

	static function create() returns Self
		return Self{left: PointArray.create(), right: PointArray.create()}
	end 'create'
end 'Pair'

function main() returns ExitCode
	var pair = Pair.create()
	pair.left.push(Point.create(3))
	pair.right.push(Point.create(4))
	let src = try pair.left.get(0) otherwise panic("an element was pushed")
	var dst = try pair.right.get(0) otherwise panic("an element was pushed")
	dst.x = 9
	return src.x
end 'main'
```
```exitcode
3
```

<!-- test: a-read-on-the-other-arm-is-not-after -->
A `let` read only on the other arm of an `if` is not read after a write on this one: no path runs both.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var arr = PointArray.create()
	arr.push(Point.create(1))
	let s = try arr.get(0) otherwise panic("an element was pushed")

	if arr.count() > 0 'writes'
		var e = try arr.get(0) otherwise panic("an element was pushed")
		e.x = 9
		print("wrote {e.x}\n")
	end 'writes' else 'reads'
		print("read {s.x}\n")
	end 'reads'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
wrote 9
```

<!-- test: one-record-handed-to-two-fields-aliases-them -->
Two fields are two places only when no record went into both: `pair` was built with `p` in each, so `l` and
`pair.right` are one record and the write changes what `l` reads.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Pair
	export var left as Point
	export var right as Point

	static function create(left Point, right Point) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Point.create(1)
	var pair = Pair.create(p, right: p)
	let l = pair.left
	pair.right.x = 9
	return l.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/one-record-handed-to-two-fields-aliases-them.test:26:2: cannot write through 'pair.right', which may be the record of immutable variable 'l'; use clone()
```

<!-- test: one-record-stored-into-two-fields-aliases-them -->
The same when the program stores the one record into both fields itself.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

type Pair
	export var left as Point
	export var right as Point

	static function create(left Point, right Point) returns Self
		return Self{left: left, right: right}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Point.create(1)
	var pair = Pair.create(Point.create(0), right: Point.create(0))
	pair.left = p
	pair.right = p
	let l = pair.left
	pair.right.x = 9
	return l.x
end 'main'
```
```maxoncstderr
error E3159: specs/fragments/var-should-be-let/one-record-stored-into-two-fields-aliases-them.test:28:2: cannot write through 'pair.right', which may be the record of immutable variable 'l'; use clone()
```

<!-- test: a-let-a-closure-captured-is-read-when-the-closure-is-called -->
A closure that captured `s` reads it wherever it is called, so `s` is live until the call.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Point'

typealias PointArray = Array with Point

function main() returns ExitCode
	var arr = PointArray.create()
	arr.push(Point.create(1))
	let s = try arr.get(0) otherwise panic("an element was pushed")
	let f = function() gives s.x
	var e = try arr.get(0) otherwise panic("an element was pushed")
	e.x = 9
	return f() as ExitCode
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/a-let-a-closure-captured-is-read-when-the-closure-is-called.test:20:6: cannot assign the result of 'get', which may share storage with immutable variable 's', to mutable binding 'e'; use 'let' instead of 'var', or use clone()
```
