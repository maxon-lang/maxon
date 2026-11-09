---
feature: pass-by-reference
status: experimental
keywords: [reference, pass-by-reference, mutable, mutation, closure, capture]
category: core
---

# Pass by Reference — the `mutable` parameter

## Documentation

A parameter is passed by value and is a `let`: reassigning it is **E2013**. A parameter of a value type — a number, `bool`, an enum or `String` — that the function reassigns is declared `mutable` before its type, and a `mutable` parameter is passed by reference: the function receives the caller's variable, not a copy. Nothing is inferred; only the marker makes a parameter by-reference.

```text
function increment(x mutable Integer)
  x = x + 1
end 'increment'

var n = 10 as Integer
increment(n)
// n is now 11
```

### Reading Parameters

A parameter without the marker is passed by value, whether or not the function reads it:

```text
function double(x Integer) returns Integer
  return x * 2
end 'double'

let n = 21 as Integer
let result = double(n)  // result is 42
```

### What a `mutable` Parameter Takes

The caller passes a `var`, or a field or element that is writable. A `let`, a literal or an expression result is refused, because no variable exists for the write to reach: a `let` that is handed to a `mutable` parameter is **E2013**, which also covers a loop variable; a captured name is E3159. A record-type parameter is never rebound: reassigning one is E2013 even where it is marked, and writing a field of it or growing an array it holds (`p.x = 99`, `p.items.push(x)`) is an ordinary write through the parameter.

### Closure Capture

A closure does not capture by reference. A captured scalar is copied when the closure is created, so a later change to the original variable is not visible inside the closure, and an assignment inside the closure does not reach the outer scope. A captured managed local moves into the closure; a captured parameter, `self` or field is retained (`closure-capture.md`). Because the closure holds its own copy, passing a captured name to a `mutable` parameter is E3159: the write could not reach the variable.

### Reassigning a `mutable` Parameter

The reassignment is visible to the caller: the function reassigns a `String`, a number, an enum, a float, `bool` or a byte exactly as it would a local, and the caller always observes the reassigned value. A `mutable` `String` releases the caller's previous value exactly once and takes ownership of the new one, whether the new value is a literal, a freshly built string or another local.

### A call through a function value

A function type spells the marker too: `typealias Step = function(mutable Count)`. A function, closure or interface conformer fits a function type or requirement only when every parameter's marker is the same as the type's, so a function that reassigns its parameter does not convert to `function(Count)`, and one that declares `mutable` but only reads does not either. A call through a function value passes the argument's storage at each `mutable` position, and a `let` handed to such a position is refused as it is at a direct call (**E2013**).

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

function setTo99(x mutable Integer)
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

<!-- test: pass-by-reference.mutate-self-field-ref -->
### A self field at a by-reference position is written
A field is storage the receiver owns, reached as an offset from its base. Handing one to a
by-reference parameter must write the field, not a copy of it.
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x mutable Integer)
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

function setTo99(x mutable Integer)
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

function setTo99(x mutable Integer)
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

<!-- test: pass-by-reference.mutate-ranged-float-alias-ref -->
### A RANGED FLOAT ALIAS field and local are written, and the address is not converted
A ranged float alias is spelled `named`, not `float`, so a door that retypes the handed-over address
without resolving the alias hands the callee a value whose machine class disagrees with the parameter's
— and an argument that crosses the float domain has a conversion inserted on it. The address is an
address in either case, so the alias is resolved where the slot is retyped.
```maxon

typealias Weight = float(0.0 to 1000.0)

function setTo99(w mutable Weight)
	w = 99.0
end 'setTo99'

type Box
	export var w as Weight

	static function create() returns Self
		return Self{w: 1.0}
	end 'create'
end 'Box'

function main() returns ExitCode
	var scale = 1.0 as Weight
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

function setTo99(x mutable Integer)
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

<!-- test: pass-by-reference.let-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setTo99(x mutable Integer)
	x = 99
end 'setTo99'

function main() returns ExitCode
	let n = 5
	setTo99(n)
	return 0
end 'main'
```
```maxoncstderr
error E2013: specs/pass-by-reference/pass-by-reference.let-to-mutating-param-error.maxon:11:2: 'n' is a let and cannot be passed to the `mutable` parameter 'x'; bind it to a `var`
```

<!-- test: pass-by-reference.captured-let-to-mutating-param-error -->
```maxon

typealias Integer = int(i64.min to i64.max)

function setIt(dest mutable Integer) returns Integer
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

function setIt(dest mutable Integer) returns Integer
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

function setIt(dest mutable Integer) returns Integer
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

<!-- test: pass-by-reference.nested-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

function inner(x mutable Integer)
	x = 77
end 'inner'

function outer(x mutable Integer)
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

<!-- test: pass-by-reference.multiple-params-mixed -->
```maxon

typealias Integer = int(i64.min to i64.max)

function process(a Integer, b mutable Integer, c Integer)
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

function switchColor(c mutable Color)
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

function setTo99(x mutable Float)
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

<!-- test: pass-by-reference.enum-float-backed-ref -->
```maxon

enum Ratio
	half = 0.5
	quarter = 0.25
end 'Ratio'

function switchRatio(r mutable Ratio)
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

<!-- test: pass-by-reference.an-async-call-resolving-to-a-by-reference-member-writes-the-callers-variable -->
`settle` assigns `dest`. The coroutine writes the caller's own `s`, which is lent to it until `p` is awaited.
```maxon
typealias Integer = int(i64.min to i64.max)

function settle(dest mutable String, src Integer) returns Integer
	Scheduler.yield()
	dest = "set-{src}"
	return src
end 'settle'

function settleText(dest mutable String, src String) returns Integer
	Scheduler.yield()
	print("{dest}{src}\n")
	return 0
end 'settleText'

function main() returns ExitCode
	var s = "a-{1}"
	let p = async settle(s, src: 5)
	let r = await p
	print("{r} {s}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
5 set-5
```

<!-- test: pass-by-reference.an-async-call-whose-promise-outlives-the-frame-of-a-by-reference-argument-is-refused -->
The coroutine holds the caller's `s` by reference, so its promise is tied to the frame: it is awaited or cancelled there and cannot be stored for a later await.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer
typealias IntPromiseArray = Array with IntPromise

function rename(dest mutable String, n Integer) returns Integer
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
	var promises = IntPromiseArray.create(0)
	kick(promises)

	for p in promises 'each'
		print("awaited {await p}\n")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3212: <fragment>:15:11: the promise of this `async` call is tied to the mutable data it was given: it is awaited or cancelled and nothing else, never returned, stored, captured, sent or passed on, because the coroutine still holds the caller's data
```


<!-- test: pass-by-reference.an-async-call-hands-the-coroutine-the-callers-scalar-cell -->
The coroutine adds to the caller's own `c`: after the await `c` holds the sum.
```maxon
typealias Integer = int(i64.min to i64.max)

function bump(dest mutable Integer, n Integer) returns Integer
	Scheduler.yield()
	dest = dest + n
	return dest
end 'bump'

function main() returns ExitCode
	var c = 100 as Integer
	let p = async bump(c, n: 7)
	let r = await p
	print("{c} {r}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
107 107
```

<!-- test: pass-by-reference.an-async-string-argument-a-callee-reassigns-is-released-on-await-and-on-cancel -->
The caller's `var` holds the coroutine's final value after the await and releases it once, and a coroutine cancelled before it starts never runs.
```maxon
typealias Integer = int(i64.min to i64.max)

function rename(dest mutable String, n Integer) returns Integer
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

<!-- test: pass-by-reference.a-string-handed-to-an-async-callee-that-reassigns-it-holds-the-coroutines-write-after-the-await -->
The coroutine writes the caller's `s`, so the caller reads it after the await and sees the write.
```maxon
typealias Integer = int(i64.min to i64.max)

function rename(dest mutable String, n Integer) returns Integer
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
7 renamed-7
```

<!-- test: pass-by-reference.function-value-call-updates-the-variable -->
A call through a function value is a call like any other: `bump` assigns its parameter, so the function value passes the variable's cell and the write reaches `v`.
```maxon
typealias Small = int(1 to 100)

function bump(n mutable Small)
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

<!-- test: pass-by-reference.closure-literal-callee-is-read-through-the-cell -->
A closure literal of a `mutable` function type declares its parameter `mutable` too; one that only reads it leaves the variable alone.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(mutable Small) returns Small

function bump(n mutable Small) returns Small
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
	let untouched = run(function(n mutable Small) gives n)
	let capturing = run(function(_ mutable Small) gives offset)
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

function setTo99(x mutable Float)
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
`apply` marks its own parameter `mutable` to hand it to the function it is given, so that parameter is passed by reference as it is for a direct `bump(n)`, and the caller's variable is updated.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(mutable Small)

function bump(n mutable Small)
	n = 7
end 'bump'

function apply(n mutable Small, f Step)
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

<!-- test: pass-by-reference.function-value-call-with-an-r-value-at-a-by-value-position-leaves-no-scratch-cell -->
A function-value call takes an argument as a place only at a position its function type marks `mutable`, and a literal or an expression at such a position is refused (E3159). In a program that declares a `mutable` parameter, a literal and a freshly built `String` handed to a by-value position of a function-value call are read as values and bound to no cell, so the `String` is released once and the run ends without a leak.
```maxon
typealias Count = int(0 to 1000)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function measure(text String) returns ExitCode
	return text.byteLength() as ExitCode
end 'measure'

function main() returns ExitCode
	var tally = 0 as Count
	bump(tally)
	let f = measure
	let n = 12
	let a = f("literal")
	let b = f("n is {n}")
	print("{tally} {a} {b}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 7 7
```

<!-- test: pass-by-reference.error.an-unmarked-parameter-handed-to-a-by-reference-function-value-is-refused -->
An unmarked parameter is a `let`, so handing it to a position the function value writes is refused as it is at a direct call.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(mutable Small)

function bump(n mutable Small)
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
```maxoncstderr
error E3159: <fragment>:10:3: cannot pass 'n', which is read-only (a parameter), to a function that writes argument 1 (in apply); pass a clone()
```

<!-- test: pass-by-reference.error.a-function-value-names-no-parameter-so-the-refusal-counts-the-argument -->
A function type declares no parameter names, so the refusal for an unmarked parameter handed to a by-reference function value names the argument's position, not a parameter spelled like the caller's variable or like the function the value happens to be.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(Small, mutable Small)

function bump(first Small, target mutable Small)
	target = first
end 'bump'

function apply(x Small, f Step)
	f(3, x)
end 'apply'

function main() returns ExitCode
	var v = 3 as Small
	apply(v, f: bump)
	return v as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:3: cannot pass 'x', which is read-only (a parameter), to a function that writes argument 2 (in apply); pass a clone()
```

<!-- test: pass-by-reference.error.a-let-handed-to-a-by-reference-function-value-is-refused -->
A `let` handed to a position the function value writes is refused as it is at a direct call.
```maxon
typealias Small = int(1 to 100)

function bump(n mutable Small)
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
error E2013: <fragment>:11:3: 'v' is a let and cannot be passed to the `mutable` parameter 'n'; bind it to a `var`
```

<!-- test: pass-by-reference.error.a-literal-handed-to-a-by-reference-function-value-is-refused -->
A literal is not storage, so a call through a function value has no variable for the write to reach, exactly as at a direct call.
```maxon
typealias Count = int(0 to 1000)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	let f = bump
	f(5)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:3: cannot pass a literal to a function that writes argument 1 (in main); pass a `var`
```

<!-- test: pass-by-reference.error.an-expression-handed-to-a-by-reference-function-value-is-refused -->
An expression is not storage either, so the write through a function value would land in a scratch cell and be lost.
```maxon
typealias Count = int(0 to 1000)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function main() returns ExitCode
	let base = 5
	let f = bump
	f(base + 1)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:11:3: cannot pass an expression to a function that writes argument 1 (in main); pass a `var`
```

<!-- test: pass-by-reference.error.a-literal-handed-to-a-by-reference-parameter-of-a-function-type-is-refused -->
A function-type alias that marks a position `mutable` fixes the convention at every call through a value of that type, whichever function the value holds.
```maxon
typealias Count = int(0 to 1000)
typealias Step = function(mutable Count)

function bump(n mutable Count)
	n = n + 1
end 'bump'

function run(step Step)
	step(5)
end 'run'

function main() returns ExitCode
	run(bump)
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:10:6: cannot pass a literal to a function that writes argument 1 (in run); pass a `var`
```

<!-- test: pass-by-reference.error.a-literal-handed-to-a-by-reference-interface-requirement-is-refused -->
A requirement that marks a parameter `mutable` is called by reference through the interface too.
```maxon
typealias Count = int(0 to 1000)

interface Counter
	function bumpBy(mutable self, n mutable Count)
end 'Counter'

type Tally implements Counter
	export var total as Count

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function bumpBy(n mutable Count)
		n = n + 1
		self.total = n
	end 'bumpBy'
end 'Tally'

function drive(c Counter)
	c.bumpBy(5)
end 'drive'

function main() returns ExitCode
	var t = Tally.create()
	drive(t)
	return t.total as ExitCode
end 'main'
```
```maxoncstderr
error E3159: <fragment>:22:4: cannot pass a literal to a function that writes argument 1 (in drive); pass a `var`
```


<!-- test: pass-by-reference.function-value-closure-and-named-share-one-convention -->
A closure literal and a named function of one function type are called alike: the named one writes through the cell, the closure declares the parameter `mutable` and reads it, leaving the variable alone.
```maxon
typealias Small = int(1 to 100)

function bump(n mutable Small) returns Small
	n = 7
	return n
end 'bump'

function main() returns ExitCode
	var a = 3 as Small
	var b = 3 as Small
	let named = bump
	let literal = function(n mutable Small) gives n
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
typealias Step = function(mutable Small)

function bump(n mutable Small)
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

<!-- test: pass-by-reference.error.a-let-handed-through-a-callback-parameter-is-refused -->
A `let` handed through a function-typed parameter to a position the function it may hold writes is refused, as at a direct call.
```maxon
typealias Small = int(1 to 100)
typealias Step = function(mutable Small)

function bump(n mutable Small)
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
error E2013: <fragment>:11:3: 'v' is a let and cannot be passed to the `mutable` parameter 'n'; bind it to a `var`
```

<!-- test: pass-by-reference.a-ranged-var-handed-to-a-function-value-keeps-its-range-guard -->
A `var` handed to a function value that takes the position by reference lives in a cell; the guard its sum owes is still run, before the cell is ever handed over.
```maxon
typealias Wide = int(0 to 5000)
typealias Num = int(0 to 1000) implements Wide

function bump(n mutable Wide)
	n = 1
end 'bump'

function keep(n mutable Num)
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

<!-- test: pass-by-reference.error.a-let-handed-to-a-callback-is-refused-when-a-body-of-its-shape-writes-it -->
A `let` handed through a callback is refused when any body of the callback's shape writes the parameter, although other bodies of that shape only read it.
```maxon
typealias Wide = int(1 to 100)
typealias Step = function(mutable Wide)

function bump(n mutable Wide)
	n = 7
end 'bump'

function show(n mutable Wide)
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
error E2013: <fragment>:15:3: 'v' is a let and cannot be passed to the `mutable` parameter 'n'; bind it to a `var`
```

<!-- test: pass-by-reference.error.a-loop-variable-handed-to-a-parameter-that-reassigns-it-is-refused -->
A `for` loop variable cannot be reassigned, so handing it to a parameter its callee reassigns is refused, as `filter` handing its element to such a callback is.
```maxon
typealias Count = int(0 to 1000)
typealias Counts = Array with Count

function isBig(n mutable Count) returns bool
	n = n / 2
	return n > 3
end 'isBig'

function main() returns ExitCode
	var xs = Counts.create(0)
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
error E2013: <fragment>:17:6: 'item' is a let and cannot be passed to the `mutable` parameter 'n'; bind it to a `var`
```

<!-- test: pass-by-reference.a-field-typed-with-a-directory-qualified-function-alias-passes-by-reference -->
A field whose type is a function alias named through its directory is a function value, so a call through it passes its argument by reference.
```maxon
// --- file: alpha/ops.maxon
export typealias Count = int(0 to 1000)
export typealias Bump = function(mutable Count)

// --- file: main.maxon
function bump(n mutable alpha.Count)
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

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-mutable-parameter-is-refused-at-the-call -->
A callee that takes the position at a wider alias may write any wider value through the cell, which the variable naming the narrower alias cannot hold, so the call is refused.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	bumpWide(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:11: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-mutable-parameter-of-a-function-value-is-refused-at-the-call -->
The same through a function value of the callee's own type: the position is the function type's, and a function value may write any value of it.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide)
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
```maxoncstderr
error E3005: <fragment>:12:4: argument type mismatch for 1: expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-var-of-the-position-alias-is-written-through-a-function-value -->
A variable of the alias the function value's position names takes every value the callee may write.
```maxon
typealias Wide = int(0 to 100)

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Wide
	let g = bumpWide
	g(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-mutable-position-of-a-function-typed-parameter-is-refused -->
A function-typed parameter declares the position it is called through, so a narrow variable handed to its wider `mutable` position is refused in the function that calls it.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide
typealias Bump = function(mutable Wide)

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function run(step Bump)
	var x = 3 as Narrow
	step(x)
	print("{x}\n")
end 'run'

function main() returns ExitCode
	run(bumpWide)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:7: argument type mismatch for 1: expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-var-of-the-position-alias-is-written-through-a-function-typed-parameter -->
The function-typed parameter's own position alias is the alias its caller's variable declares.
```maxon
typealias Wide = int(0 to 100)
typealias Bump = function(mutable Wide)

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function run(step Bump)
	var x = 3 as Wide
	step(x)
	print("{x}\n")
end 'run'

function main() returns ExitCode
	run(bumpWide)
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-mutable-parameter-of-a-generic-function-is-refused -->
A generic function's `mutable` parameter that names an alias is the alias whatever the type argument is, so a narrow variable handed to it is refused at the call.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide, tag T) uses T returns T
	n = 50
	return tag
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	let tag = bumpWide(x, tag: "t")
	print("{x}{tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:21: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-var-of-the-parameter-alias-is-written-through-a-generic-function -->
A variable of the alias the generic function's parameter names takes every value the callee may write.
```maxon
typealias Wide = int(0 to 100)

function bumpWide(n mutable Wide, tag T) uses T returns T
	n = 50
	return tag
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Wide
	let tag = bumpWide(x, tag: "t")
	print("{x}{tag}\n")
	return 0
end 'main'
```
```stdout
50t
```
```exitcode
0
```

<!-- test: pass-by-reference.a-narrow-parameter-relayed-to-a-wider-callee-is-refused -->
A by-reference parameter of a narrow alias handed on to a callee that takes the wider alias is refused in the frame that hands it on.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function relay(n mutable Narrow)
	bumpWide(n)
end 'relay'

function main() returns ExitCode
	var x = 3 as Narrow
	relay(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:11: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-parameter-of-a-throwing-callee-is-refused -->
The refusal follows a call routed through `try` exactly as it follows a plain one.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

enum StepError implements Error
	refused
end 'StepError'

function bumpWide(n mutable Wide) throws StepError
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
```maxoncstderr
error E3005: <fragment>:19:15: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-callee-taking-the-vars-own-alias-needs-no-copy-back-check -->
A callee that takes the variable's own alias writes only values that alias admits, so the variable is read back as written.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpNarrow(n mutable Narrow)
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

<!-- test: pass-by-reference.a-narrow-field-at-a-wider-mutable-parameter-is-refused -->
A field handed by reference to a callee that takes the wider alias is refused: the field cell has the field's declared type.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Meter
	export var level as Narrow

	static function create(level Narrow) returns Self
		return Self{level: level}
	end 'create'
end 'Meter'

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var m = Meter.create(3)
	bumpWide(m.level)
	print("{m.level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:11: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.a-narrow-local-var-at-a-wider-mutable-parameter-is-refused -->
A local `var` handed by reference to a callee that takes the wider alias is refused.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide)
	n = 50
end 'bumpWide'

function main() returns ExitCode
	var level = 3 as Narrow
	bumpWide(level)
	print("{level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:11: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.an-async-call-refuses-a-narrow-var-the-coroutine-could-widen -->
The coroutine writes the variable itself, so a wider parameter than the variable's own type is refused.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide) returns Wide
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
```maxoncstderr
error E3005: <fragment>:13:25: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```


<!-- test: pass-by-reference.a-service-message-copies-a-narrow-var-so-the-handler-cannot-widen-it -->
A message carries its arguments by value in the request, so a handler that widens its copy of the parameter leaves the sender's narrower variable alone.
```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Meter
	var level as Wide

	static function create() returns Self
		return Self{level: 1}
	end 'create'

	export function set(n Wide)
		var copy = n
		copy = 50
		self.level = copy
	end 'set'

	export function read() returns Wide
		return self.level
	end 'read'
end 'Meter'

function main() returns ExitCode
	let h = spawn Meter.create()
	let x = 3 as Narrow
	try h.set(x) otherwise panic("an open mailbox with room takes the send")
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
typealias Step = function(mutable Narrow)

function bumpWide(n mutable Wide)
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
error E3005: <fragment>:17:2: argument type mismatch for 'f': expected 'fn(mutable Narrow)', got 'fn(mutable Wide)'
```

<!-- test: pass-by-reference.a-narrow-var-at-a-wider-parameter-of-a-callee-that-then-throws-is-refused -->
The refusal is at the call, so no handler is ever reached with a widened variable.
```maxon
typealias Wide = int(0 to 10000)
typealias Narrow = int(0 to 10) implements Wide

enum StepError implements Error
	refused
end 'StepError'

function bumpWide(n mutable Wide) throws StepError
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
```maxoncstderr
error E3005: <fragment>:17:15: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: pass-by-reference.error.a-float-var-handed-to-an-integer-parameter-is-refused-before-any-cell-is-shared -->
A variable and a parameter of different register classes never share a cell: the argument is refused where it is written.
```maxon
typealias Count = int(0 to 100)
typealias Real = float(0.0 to 100.0)

function bump(k mutable Count)
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

function scale(k mutable Real)
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

<!-- test: pass-by-reference.an-expression-handed-to-a-mutable-record-parameter-reaches-the-shared-record -->
### `mutable` on a record parameter is the write-through capability, not a reference to the caller's variable
The record is shared, not copied, so the callee's field store reaches the caller's object and no variable is needed for the write to land: a call's result is accepted where a `var` is required for a value-type `mutable` parameter.
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

type Shelf
	var row as Row

	static function create() returns Self
		return Self{row: Row.create(1)}
	end 'create'

	function first() returns Row
		return self.row
	end 'first'

	function total() returns Count
		return self.row.n
	end 'total'
end 'Shelf'

function fill(row mutable Row)
	row.n = 9
end 'fill'

function main() returns ExitCode
	var shelf = Shelf.create()
	fill(shelf.first())
	print("{shelf.total()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
9
```

<!-- test: pass-by-reference.one-record-handed-to-two-mutable-record-parameters-is-not-an-overlap -->
Records alias freely inside a region, so two record parameters naming one object overlap no storage: only the object is shared.
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

function both(a mutable Row, b mutable Row)
	a.n = a.n + 1
	b.n = b.n + 10
end 'both'

function main() returns ExitCode
	var row = Row.create(1)
	both(row, b: row)
	print("{row.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
12
```

<!-- test: pass-by-reference.a-record-handed-through-a-mutable-record-function-value-is-written-through -->
A function type spells `mutable Row` the same way, and a call through a function value hands the object over like a direct call, including a record the caller received as an ordinary parameter.
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

typealias RowEdit = function(mutable Row)

function raise(row mutable Row)
	row.n = row.n + 5
end 'raise'

function apply(edit RowEdit, row Row)
	edit(row)
	edit(row)
end 'apply'

function main() returns ExitCode
	var row = Row.create(1)
	apply(raise, row: row)
	print("{row.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: pass-by-reference.a-let-record-handed-to-a-mutable-record-function-value-is-written -->
A `mutable` position of a function type declares a write. A record is written in place through its parameter and is never rebound, so a `let` record handed to it is accepted: `let` forbids reassigning `row`, not writing the record it names.
```maxon
typealias Count = int(0 to 1000)

type Row
	export var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Row'

typealias RowEdit = function(mutable Row)

function raise(row mutable Row)
	row.n = row.n + 5
end 'raise'

function apply(edit RowEdit, row Row)
	edit(row)
end 'apply'

function main() returns ExitCode
	let row = Row.create(1)
	apply(raise, row: row)
	return row.n as ExitCode
end 'main'
```
```exitcode
6
```
