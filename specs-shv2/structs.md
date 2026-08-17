---
feature: types
status: stable
keywords: [type, field, let, var, mutability, instance]
category: type-system
---

# Types

## Documentation

Types define custom data types with named fields.

### Declaration

```maxon
typealias Score = int(i64.min to i64.max)

type Point
	export var x as Score
	export var y as Score
end 'Point'
```

Fields must use `let` (immutable) or `var` (mutable), and can be `export` for external access:
```maxon
typealias Score = int(i64.min to i64.max)

type Config
	export let version as Score    // Cannot be changed after initialization, accessible externally
	export var count as Score      // Can be modified, accessible externally
	var internal as Score          // Private - only accessible in methods
end 'Config'
```

### Instantiation

Create type instances with literal syntax:
```maxon
var p = Point.create(10, y: 20)
let config = Config.create(1, count: 0)
```

### Instance Mutability

The mutability of a type instance is determined by `let` vs `var`:

**var type** - Can modify `var` fields:
```maxon
var p = Point.create(10, y: 20)
p.x = 30   // OK: type is mutable, field is var
```

**let type** - Cannot modify any fields:
```maxon
let p = Point.create(10, y: 20)
// p.x = 30   // ERROR: type instance is immutable
```

### Field Mutability

Even on a `var` type, `let` fields cannot be modified:
```maxon
var c = Config.create(1, count: 0)
c.count = 5     // OK: field is var
// c.version = 2   // ERROR: field is let
```

## Tests

<!-- test: var-struct-field-assign -->
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
	var p = Point.create(10, y: 20)
	p.x = 30
	return p.x
end 'main'
```
```exitcode
30
```

<!-- test: var-field-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version as Integer
	export var count as Integer

	static function create(version Integer, count Integer) returns Self
		return Self{version: version, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(1, count: 0)
	c.count = 5
	return c.count
end 'main'
```
```exitcode
5
```

<!-- test: error.let-struct-field-assign -->
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
	let p = Point.create(10, y: 20)
	p.x = 30
	return p.x
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/structs/error.let-struct-field-assign.test:16:2: cannot assign to immutable variable: 'p'
```

<!-- test: error.let-field-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let version as Integer
	export var count as Integer

	static function create(version Integer, count Integer) returns Self
		return Self{version: version, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(1, count: 0)
	c.version = 2
	return c.version
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/structs/error.let-field-assign.test:16:2: cannot assign to field 'Config.version' because it is immutable (declare with 'var' to make it mutable)
```

<!-- test: simple-type -->
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
	let p = Point.create(3, y: 4)
	return p.x + p.y
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-access -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Rect
	export var width as Integer
	export var height as Integer

	static function create(width Integer, height Integer) returns Self
		return Self{width: width, height: height}
	end 'create'
end 'Rect'

function main() returns ExitCode
	let r = Rect.create(5, height: 10)
	return r.width * r.height
end 'main'
```
```exitcode
50
```

<!-- test: struct-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Vec2
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Vec2'

function dot(a Vec2, b Vec2) returns Integer
	return a.x * b.x + a.y * b.y
end 'dot'

function main() returns ExitCode
	let v1 = Vec2.create(3, y: 4)
	let v2 = Vec2.create(2, y: 1)
	return dot(v1, b: v2)
end 'main'
```
```exitcode
10
```

<!-- test: struct-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Pair
	export var first as Integer
	export var second as Integer

	static function create(first Integer, second Integer) returns Self
		return Self{first: first, second: second}
	end 'create'
end 'Pair'

function makePair(a Integer, b Integer) returns Pair
	return Pair.create(a, second: b)
end 'makePair'

function main() returns ExitCode
	let p = makePair(5, b: 7)
	return p.first + p.second
end 'main'
```
```exitcode
12
```

<!-- test: struct-literal-as-arg -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function acceptPoint(p Point) returns Integer
	return p.x + p.y
end 'acceptPoint'

function main() returns ExitCode
	return acceptPoint(Point.create(3, y: 4))
end 'main'
```
```exitcode
7
```

<!-- test: struct-field-default -->
```maxon
type Counter
	export var value = 0
	export var step = 1

	static function create() returns Self
		return Self{}
	end 'create'

	static function create(value CounterValue, step CounterStep) returns Self
		return Self{value: value, step: step}
	end 'create'
end 'Counter'

typealias CounterValue = int(0 to u64.max)
typealias CounterStep = int(0 to u64.max)

function main() returns ExitCode
	let c1 = Counter.create()
	let c2 = Counter.create(40, step: 1)
	let c3 = Counter.create(10, step: 2)
	return c1.value + c2.value + c3.step
end 'main'
```
```exitcode
42
```

<!-- test: struct-field-inferred-type -->
```maxon
type Settings
	export let maxRetries = 5
	export var timeout = 50.0

	static function create() returns Self
		return Self{}
	end 'create'
end 'Settings'

function main() returns ExitCode
	let s = Settings.create()
	return s.maxRetries + trunc(s.timeout)
end 'main'
```
```exitcode
55
```

<!-- test: error.return-wrong-struct -->
Returning a DIFFERENT struct than declared is a memory-safety hole, not merely a wrong answer
(OPEN #54). The value passes the scalar tag check — two structs share the `structRef` tag — and is
then handed back and dropped under the DECLARED return type's destructor: a wild free (this program
compiled clean and exited 139 before the check). Struct identity is the interned name, and a struct
is never a subtype of another, so the mismatch is rejected outright.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

type BoxB
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'BoxB'

function bad() returns BoxA
	return BoxB.create(9)
end 'bad'

function main() returns ExitCode
	let b = bad()
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/structs/error.return-wrong-struct.test:21:2: Cannot return 'BoxB' from function declared to return 'BoxA'
```

<!-- test: error.return-union-as-scalar -->
Returning a boxed union where a scalar type is declared (returns Integer) is a memory-safety hole
(OPEN 59). The ValueTypeTag.named tag is overloaded: a boxed union value and a ranged-int alias share
it, so the tag check wrongly agrees and the union box is dropped under the scalar return's absent
destructor, a leak. The aggregate-name check runs after the tag check and catches this case (a real
aggregate meeting a scalar) via the shared namedAggregatesConflict, now extended to fire when exactly
one side is an aggregate.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var s as String

	static function create(x Integer) returns Self
		return Self{s: "v{x}"}
	end 'create'
end 'BoxA'

union Holder
	holds(inner BoxA)
end 'Holder'

function bad() returns Integer
	return Holder.holds(BoxA.create(9))
end 'bad'

function main() returns ExitCode
	let r = bad()
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/structs/error.return-union-as-scalar.test:17:2: Cannot return 'Holder' from function declared to return 'int'
```

<!-- test: error.callarg-wrong-struct-consumed -->
Passing a DIFFERENT struct than the parameter declares is a memory-safety hole, not merely a wrong
answer (OPEN #54 Slice B). At a CONSUMING call site — `WrapA.create` moves its `BoxA` argument into a
managed field — the wrong struct passes the scalar tag check (two structs share the `structRef` tag)
and is then dropped under the DECLARED parameter type's destructor, which reads `BoxB`'s scalar `n` as
a `String` pointer and frees it: a wild free (this program compiled clean and exited 139 before the
check). Struct identity is the interned name, EXACT — a struct is never a subtype of another — and the
check is at the call argument, which every call passes through, so it is caught regardless of ownership.
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var label as String
end 'BoxA'

type BoxB
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'BoxB'

type WrapA
	export var inner as BoxA

	static function create(inner BoxA) returns Self
		return Self{inner: inner}
	end 'create'
end 'WrapA'

function main() returns ExitCode
	let b = BoxB.create(7)
	let w = WrapA.create(b)
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/structs/error.callarg-wrong-struct-consumed.test:26:16: argument type mismatch for 'inner': expected 'BoxA', got 'BoxB'
```

<!-- test: error.callarg-wrong-struct-borrowed -->
The identity check is ownership-independent: a wrong struct handed to a plain BORROWING parameter is a
type error too — the callee would read the wrong type's field layout — so it is rejected at the call
argument exactly as the consuming case is, not only at a consuming move. Here both structs are
scalar-only, so nothing crashes; the value read is simply wrong (this returned 5, `BoxB.b` read through
`BoxA.a`, before the check).
```maxon
typealias Integer = int(i64.min to i64.max)

type BoxA
	export var a as Integer

	static function create(a Integer) returns Self
		return Self{a: a}
	end 'create'
end 'BoxA'

type BoxB
	export var b as Integer

	static function create(b Integer) returns Self
		return Self{b: b}
	end 'create'
end 'BoxB'

function readA(x BoxA) returns Integer
	return x.a
end 'readA'

function main() returns ExitCode
	let bb = BoxB.create(5)
	return readA(bb) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/structs/error.callarg-wrong-struct-borrowed.test:26:9: argument type mismatch for 'x': expected 'BoxA', got 'BoxB'
```

<!-- test: self-returned-from-a-method -->
The receiver itself is a value. `return self` hands the caller the box the method was called on, so a
chainable method (`bump()` mutates and gives the instance back) reads through the returned handle.
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function bump() returns Counter
		self.value = self.value + 1
		return self
	end 'bump'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(41)
	let d = c.bump()
	return d.value as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: self-returned-aliases-the-receiver -->
⭐ **THE OWNERSHIP RULING, PINNED AS A VALUE.** `return self` is REFERENCE semantics: the returned handle
is the SAME box, co-owned through an `__mm_retain`, not a copy. A write through the returned handle is
therefore visible through the receiver. A COPY answer returns 41 here and an alias answer returns 42, so
the case cannot pass for the wrong reason. The value oracle agrees (measured: the bootstrap prints
`p.x=5 q.x=5` for this shape), and it is the same answer `var q = p` on a borrowed struct parameter
already gives.
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'

	export function bump() returns Counter
		self.value = self.value + 1
		return self
	end 'bump'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(40)
	var d = c.bump()
	d.value = d.value + 1
	return c.value as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: self-returned-with-a-managed-field -->
A struct holding a MANAGED field is the case a bare pointer copy would get wrong twice over: the box's
`String` is owned by the box, so a returned `self` must co-own the box rather than duplicate ownership of
its field. Both handles read the one live String, and the box is freed exactly once at scope exit — a
double free crashes here and a missed drop exits 101.
```maxon

type Tag
	export var name as String

	static function create(name String) returns Self
		return Self{name: name}
	end 'create'

	export function itself() returns Tag
		return self
	end 'itself'
end 'Tag'

function main() returns ExitCode
	let a = Tag.create("probe")
	let b = a.itself()
	print("{a.name}/{b.name}")
	return (a.name.count() + b.name.count()) as ExitCode
end 'main'
```
```stdout
probe/probe
```
```exitcode
10
```

<!-- test: self-passed-as-an-argument -->
`self` is a value in every position a struct value may occupy, not only after `return`: it is passed as
an ARGUMENT here, and the borrowed struct PARAMETERS it lands in are returned in their turn (`pick`
gives back whichever of its two borrowed arguments is larger). One rule — a borrowed aggregate handed
across a call boundary is co-owned — covers the receiver, a parameter and a merge of the two.
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'

	export function tally(p Point) returns Integer
		return self.x + p.x
	end 'tally'

	export function combineWith(other Point) returns Integer
		return other.tally(self)
	end 'combineWith'
end 'Point'

function pick(a Point, b Point) returns Point
	if a.x > b.x 'bigger'
		return a
	end 'bigger' else 'smaller'
		return b
	end 'smaller'
end 'pick'

function main() returns ExitCode
	let p = Point.create(11)
	let q = Point.create(31)
	let w = pick(p, b: q)
	return (p.combineWith(q) + w.x) as ExitCode
end 'main'
```
```exitcode
73
```

<!-- test: borrowed-struct-field-returned -->
A struct-typed FIELD read through the receiver is a borrowed aggregate exactly as `self` is, and it
escapes through the same door: `return self.leaf` co-owns the inner box, so the chain `t.branch().tally`
reads a live `Leaf` while the `Trunk` that owns it is still alive, and the inner box is freed once.
```maxon

typealias Integer = int(i64.min to i64.max)

type Leaf
	export var tally as Integer

	static function create(tally Integer) returns Self
		return Self{tally: tally}
	end 'create'
end 'Leaf'

type Trunk
	export var leaf as Leaf

	static function create(leaf Leaf) returns Self
		return Self{leaf: leaf}
	end 'create'

	export function branch() returns Leaf
		return self.leaf
	end 'branch'
end 'Trunk'

function main() returns ExitCode
	let t = Trunk.create(Leaf.create(42))
	return t.branch().tally as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: self-returned-in-a-loop -->
The co-ownership is BALANCED per trip, not per program: a `return self` inside a loop retains once and
the caller drops once on every iteration. An unbalanced retain leaks the box (exit 101) and an
unbalanced drop frees it under the still-live receiver.
```maxon

typealias Integer = int(i64.min to i64.max)

type Tally
	export var n as Integer
	export var label as String

	static function create(n Integer) returns Self
		return Self{n: n, label: "t"}
	end 'create'

	export function itself() returns Tally
		return self
	end 'itself'
end 'Tally'

function main() returns ExitCode
	let t = Tally.create(3)
	var sum = 0
	for _ in 0 upto 14 'trip'
		let handle = t.itself()
		sum = sum + handle.n + handle.label.count()
	end 'trip'
	return sum as ExitCode
end 'main'
```
```exitcode
56
```

<!-- test: error.return-a-let-declared-global -->
The negative control, and the one thing the widened door must still refuse — **at the WRITE** (⚖ user,
2026-08-14, W117). RETURNING a `let`-declared global's record is legal: `expose()` compiles, and a caller
that only reads what it hands back is a working program. What is refused is `b.push("zz")`, because that
mutates a global the program declared immutable. The caller can know this only because the return is
summarised whole-program — `expose`'s body may be in another file — which is the fact this rung added.
```maxon
typealias Names = Array with String

let A = ["ab", "cde"]

function expose() returns Names
	return A
end 'expose'

function main() returns ExitCode
	var b = expose()
	b.push("zz")
	return A.count()
end 'main'
```
```maxoncstderr
error E3019: <fragment>:12:4: cannot pass 'b' to function that mutates parameter 'self' (in main)
```
