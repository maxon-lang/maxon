---
feature: tuples
status: stable
keywords: [tuple, pair, destructuring, positional]
category: types
---

# Tuples

## Documentation

### Overview

Tuples are fixed-size, ordered collections of values with potentially different types. They use parenthesized syntax for both type annotations and literals.

```text
var point = (10, 20)
var pair = (42, "hello")
```

### Element Access

Access tuple elements using positional dot syntax `.0`, `.1`, `.2`, etc.:

```text
var t = (10, 20)
t.0   // 10
t.1   // 20
```

### Destructuring

Tuples can be destructured into individual variables:

```text
var (x, y) = (10, 20)
// x is 10, y is 20
```

Tuple destructuring also works in `for` loops when the iterator returns a tuple:

```text
var m = ["a": 1, "b": 2]
for (key, value) in m 'loop'
  print("{key}: {value}\n")
end 'loop'
```

### As Function Parameters and Return Types

Tuples can be used as function parameters and return types:

```text
function swap(t (Integer, Integer)) returns (Integer, Integer)
  return (t.1, t.0)
end 'swap'
```

## Tests

<!-- test: basic-tuple -->
```maxon
function main() returns ExitCode
	let t = (10, 32)
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: mixed-type-tuple -->
```maxon
function main() returns ExitCode
	let t = (40, 2.5)
	return t.0 + trunc(t.1)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'sum'

function main() returns ExitCode
	let t = (10, 32)
	return sum(t)
end 'main'
```
```exitcode
42
```

<!-- test: tuple-as-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let t = makePair(10, b: 32)
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-destructuring -->
```maxon

typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let (x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: three-element-tuple -->
```maxon
function main() returns ExitCode
	let t = (1, 2, 39)
	return t.0 + t.1 + t.2
end 'main'
```
```exitcode
42
```

<!-- test: tuple-field-write -->
```maxon
function main() returns ExitCode
	var t = (0, 0)
	t.0 = 20
	t.1 = 22
	return t.0 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-with-string -->
```maxon
function main() returns ExitCode
	let t = (42, "hello")
	return t.0
end 'main'
```
```exitcode
42
```

<!-- test: let-destructuring -->
```maxon
function main() returns ExitCode
	let (x, y) = (10, 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- disabled-test: for-destructuring-map -->
<!-- needs `Map`, which is the FOLLOW-ON rung: `MapIterator.current()` returns a genuine tuple, so Map is sequenced AFTER tuples and cannot be unlocked here. Not a tuple gap. -->
```maxon
function main() returns ExitCode
	let m = ["a": 10, "b": 32]
	var sum = 0
	for (_, value) in m 'loop'
		sum = sum + value
	end 'loop'
	return sum
end 'main'
```
```exitcode
42
```

<!-- disabled-test: small-tuple-return-allocates-nothing -->
<!-- needs the TWO-REGISTER VALUE-TUPLE ABI, which is the FOLLOW-ON rung. This is the only case in the file that asserts ALLOCATION (an `mm-trace` block); every other tuple case asserts an exit code, which heap-allocated tuples satisfy — v1 ships tuples heap-always and passes them. shv2 has NO multi-register or hidden-pointer return path on any target (one GPR, R8/x0, every aggregate a heap pointer), so the register-pair return is a new calling convention on x64 + arm64 + wasm, not a pass. -->
<!-- MmTrace -->
A returned tuple of exactly two primitive fields totalling <= 16 bytes is a VALUE: it comes back
in two registers rather than a heap record, so the call allocates nothing. The only allocation
left is the `String` that `print` builds. Returning the pair by register is what makes this
observable — the heap lowering is still the fallback for every tuple that does not fit the gate.

The single `mm_alloc` is the assertion here; the refcount lines around it are not. `print`'s
`incref`/`decref` pair is the owned reference returned by `String.addressableBytes()`, the
stdlib-internal door that replaced the raw `.managed` field in Stage 4c — see `specs/mm-trace.md`.
```maxon
typealias Num = int(0 to 1000)

function pair(a Num, b Num) returns (Num, Num)
	return (a + 1, b + 2)
end 'pair'

function main() returns ExitCode
	let p = pair(10, b: 20)
	print("{p._0} {p._1}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11 22
```
```mm-trace
mm_alloc String #1 size=54
mm_incref String #1 rc=1
mm_incref String #1 rc=2
mm_decref String #1 rc=1
mm_decref String #1 rc=0
mm_free String #1
```

<!-- test: value-tuple-return-through-function-value -->
A function whose address is taken is called by its TYPE, and a function type cannot say whether
its target throws — a throwing and a non-throwing `(Num, Num) -> (Num, Num)` are the same type.
So an indirect call cannot tell which return convention its target uses, and `pair` is held to
the heap convention here precisely because `apply(pair)` takes its address. Returns 33.
```maxon
typealias Num = int(0 to 1000)
typealias PairOp = function(Num, Num) returns (Num, Num)

function pair(a Num, b Num) returns (Num, Num)
	return (a + 1, b + 2)
end 'pair'

function apply(f PairOp) returns Num
	let r = f(10, 20)
	return r._0 + r._1
end 'apply'

function main() returns ExitCode
	return apply(pair)
end 'main'
```
```exitcode
33
```

<!-- test: value-tuple-return-forwarded -->
`return pair(...)` forwards a two-register result straight back out. The record is never bound
to a user name, so it is stack-promoted and no allocation is made at either end. Returns 33.
```maxon
typealias Num = int(0 to 1000)

function pair(a Num, b Num) returns (Num, Num)
	return (a + 1, b + 2)
end 'pair'

function forward(a Num, b Num) returns (Num, Num)
	return pair(a, b: b)
end 'forward'

function main() returns ExitCode
	let f = forward(10, b: 20)
	return f._0 + f._1
end 'main'
```
```exitcode
33
```

<!-- test: value-tuple-return-of-param -->
Returning a tuple PARAM. Params keep the pointer convention, so the return reads both halves
back out of the record rather than copying registers along. The param is borrowed — returning
it by value must not release it. Returns 11.
```maxon
typealias Num = int(0 to 1000)

function echo(t (Num, Num)) returns (Num, Num)
	return t
end 'echo'

function main() returns ExitCode
	let e = echo((5, 6))
	return e._0 + e._1
end 'main'
```
```exitcode
11
```

<!-- test: value-tuple-escaping-into-array-stays-heap -->
A returned tuple that ESCAPES into an array must fall back to a heap record: an array holds
8-byte element pointers, and a stack record would die with the frame while the array still
pointed at it. Here `p` both escapes into `xs` AND is returned by value, so the record is the
array's while the return copies its halves out — releasing it would be a leak on one side and a
double-free on the other. Returns 66 (11+22 from the return, 11+22 read back out of the array).
```maxon
typealias Num = int(0 to 1000)
typealias Pair = (Num, Num)
typealias PairArray = Array with Pair

function pair(a Num, b Num) returns (Num, Num)
	return (a + 1, b + 2)
end 'pair'

function stash(xs PairArray) returns (Num, Num)
	let p = pair(10, b: 20)
	xs.push(p)
	return p
end 'stash'

function main() returns ExitCode
	var xs = PairArray.create()
	let r = stash(xs)
	let q = try xs.get(0) otherwise panic("element 0 was just pushed")
	return r._0 + r._1 + q._0 + q._1
end 'main'
```
```exitcode
66
```

<!-- test: throwing-value-tuple-return -->
A THROWING tuple-returning function keeps the heap convention: a try-call's second return
register already carries the error flag, and the error path has no tuple to hand back. The
success path yields 33 and the error path takes the `otherwise`, giving 75.
```maxon
typealias Num = int(0 to 1000)

enum Err
	bad
end 'Err'

function pairThrows(a Num, b Num) returns (Num, Num) throws Err
	if a > 900 'guard'
		throw Err.bad
	end 'guard'
	return (a + 1, b + 2)
end 'pairThrows'

function run(a Num) returns Num throws Err
	let t = try pairThrows(a, b: 20)
	return t._0 + t._1
end 'run'

function main() returns ExitCode
	let good = try run(10) otherwise 0
	let bad = try run(950) otherwise 42
	return good + bad
end 'main'
```
```exitcode
75
```

<!-- disabled-test: for-in-over-map-allocates-no-tuple -->
<!-- needs `Map`, which is the FOLLOW-ON rung: `MapIterator.current()` returns a genuine tuple, so Map is sequenced AFTER tuples and cannot be unlocked here. Not a tuple gap. -->
<!-- MmTrace -->
A `for` loop over a Map allocates NO tuple record per iteration. The iterator's `current()`
returns its `(key, value)` pair in two registers, and the loop's item binding does not escape,
so the pair lives in stack slots for the iteration and dies with it.

The golden is the pin: it holds every allocation the Map machinery itself makes, and NOT ONE
`____Tuple_Key_Value_*` line between the iterator's incref and its decref. A per-iteration
tuple record coming back — by the value-return gate ceasing to cover `current()`, or by the
loop's item binding ceasing to be recognised as non-escaping — puts three of them there and
turns this red.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntMap = Map with (Integer, Integer)

function sum(m IntMap) returns Integer
	var total = 0
	for (_, v) in m 'loop'
		total = total + v
	end 'loop'
	return total
end 'sum'

function main() returns ExitCode
	let m = [1: 10, 2: 20, 3: 30]
	return sum(m)
end 'main'
```
```exitcode
60
```
```mm-trace
mm_alloc __ManagedMemory #1 size=40
mm_incref __ManagedMemory #1 rc=1
mm_alloc __ManagedMemory #2 size=40
mm_incref __ManagedMemory #2 rc=1
mm_alloc __Array_i64 #3 size=40
mm_incref __Array_i64 #3 rc=1
mm_alloc __Array_i64 #4 size=40
mm_incref __Array_i64 #4 rc=1
mm_alloc StateArray #5 size=40
mm_incref StateArray #5 rc=1
mm_alloc HashSlotArray #6 size=40
mm_incref HashSlotArray #6 rc=1
mm_alloc __Map_i64_i64 #7 size=48
mm_incref __Array_i64 #3 rc=2
mm_incref __Array_i64 #4 rc=2
mm_incref StateArray #5 rc=2
mm_incref HashSlotArray #6 rc=2
mm_incref __Map_i64_i64 #7 rc=1
mm_decref HashSlotArray #6 rc=1
mm_decref StateArray #5 rc=1
mm_decref __Array_i64 #4 rc=1
mm_decref __Array_i64 #3 rc=1
mm_alloc __MapIterator_Integer_Integer #8 size=40
mm_incref __Array_i64 #3 rc=2
mm_incref __Array_i64 #4 rc=2
mm_incref StateArray #5 rc=2
mm_incref __MapIterator_Integer_Integer #8 rc=1
mm_decref __MapIterator_Integer_Integer #8 rc=0
mm_decref __Array_i64 #3 rc=1
mm_decref __Array_i64 #4 rc=1
mm_decref StateArray #5 rc=1
mm_free __MapIterator_Integer_Integer #8
mm_decref __Map_i64_i64 #7 rc=0
mm_decref __Array_i64 #3 rc=0
mm_free __Array_i64 #3
mm_decref __Array_i64 #4 rc=0
mm_free __Array_i64 #4
mm_decref StateArray #5 rc=0
mm_free StateArray #5
mm_decref HashSlotArray #6 rc=0
mm_free HashSlotArray #6
mm_free __Map_i64_i64 #7
mm_decref __ManagedMemory #1 rc=0
mm_free __ManagedMemory #1
mm_decref __ManagedMemory #2 rc=0
mm_free __ManagedMemory #2
```

<!-- test: destructure-match-result-then-compare -->
Destructuring `let (a, b) = match X { … gives (x, y) }` binds the elements of a
tuple produced by a match-expression arm, then COMPARES each binding. The
match-result merge slot refines to `genericInstance(__Tuple2, [Slot, bool])`
only on a later type-resolution converge pass; before that, the destructure's
`tmp._0` / `tmp._1` field reads off the still-unspecialised `named(__Tuple2)`
receiver yield the bare `_T0` / `_T1` tuple type-parameters. Recording those
froze the bindings (and the cmps on them — the operand-type stamp is kept once
non-unresolved) at the placeholders, so `slot != 5` demanded `_T0 is Equatable`
and `flag != true` reported a category mismatch. The tuple field-load now stays
unresolved until the receiver refines, so both bindings resolve to their
concrete element types. Returns `2`.
```maxon
typealias Slot = int(0 to 100)

union Thing
	alpha(s Slot, flag bool)
	beta
end 'Thing'

function pick(t Thing) returns ExitCode
	let (slot, flag) = match t 'm'
		alpha(s, f) gives (s, f)
		beta gives (0, false)
	end 'm'
	if slot != 5 'notFive'
		return 1
	end 'notFive'
	if flag != true 'notTrue'
		return 3
	end 'notTrue'
	return 2
end 'pick'

function main() returns ExitCode
	return pick(Thing.alpha(5, flag: true))
end 'main'
```
```exitcode
2
```

<!-- The cases below are shv2's OWN, appended after the byte-identical port of `/specs/tuples.md` above. -->
<!-- Each pins a construct this rung ENABLES that the ported half does not reach: the lexer's tuple-index -->
<!-- rule, the two element shapes v1 paid for, the injective name join, and the rejections. -->

<!-- test: nested-tuple-chained-access -->
`t.0.1` reaches the second element of a NESTED tuple. It is the case the lexer had to be taught: a
number greedily takes a following `.` as a fraction, so `t.0.1` lexed as `identifier(t)`, `dot`,
`floatLiteral(0.1)` — two indices silently fused into one token. A number can follow a `.` for exactly
one reason (there is no `.5` float form and every other dotted form has an identifier on its right), so
the lexer refuses the fraction there and both hops survive. Neither reference compiler lexes this:
v1's positional rewrite handles only an `intLiteral`, so `t.0.1` is a parse error there.
```maxon
function main() returns ExitCode
	let t = ((1, 2), 39)
	return t.0.0 + t.0.1 + t.1
end 'main'
```
```exitcode
42
```

<!-- test: tuple-element-from-method-call -->
An element built from a METHOD-CALL RESULT. v1 leaked exactly this shape: the tuple reached lowering
with no parse-time-pinned type for the element, so its `__mm_alloc` got a NULL destructor and the
element's box was never released. Here the layout is minted from the element VALUES' own types at the
literal, so the struct element is an ordinary managed field and the tuple's synthesized drop cascade
frees it. A leak exits 101 rather than 42.
```maxon
type Point
	export let x as int

	export static function create(x int) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let t = (1, Point.create(41))
	return t.0 + t.1.x
end 'main'
```
```exitcode
42
```

<!-- test: tuple-element-names-join-injectively -->
Two DIFFERENT tuple types whose element names differ only in where an `_` falls. A tuple's mangled name
joins its elements, and `_` is inside the identifier alphabet — so an `_` join would spell `(A_B, C)`
and `(A, B_C)` identically, and the second would silently take the first's layout: `t.0.gamma` would
then be resolved against `A_B`'s field table, which has no `gamma`. The join uses a character no element
name can hold, so the two stay distinct. Returns 3 + 34.
```maxon
type A_B
	export let alpha as int

	export static function create(alpha int) returns Self
		return Self{alpha: alpha}
	end 'create'
end 'A_B'

type A
	export let gamma as int

	export static function create(gamma int) returns Self
		return Self{gamma: gamma}
	end 'create'
end 'A'

type C
	export let beta as int

	export static function create(beta int) returns Self
		return Self{beta: beta}
	end 'create'
end 'C'

type B_C
	export let delta as int

	export static function create(delta int) returns Self
		return Self{delta: delta}
	end 'create'
end 'B_C'

function first(t (A_B, C)) returns int
	return t.0.alpha + t.1.beta
end 'first'

function second(t (A, B_C)) returns int
	return t.0.gamma * 10 + t.1.delta
end 'second'

function main() returns ExitCode
	let p = (A_B.create(1), C.create(2))
	let q = (A.create(3), B_C.create(4))
	return first(p) + second(q)
end 'main'
```
```exitcode
37
```

<!-- test: ranged-alias-elements-are-one-tuple-type -->
`(Num, Num)` and `(int, int)` are ONE tuple type: a tuple's identity is its elements' UNDERLYING types,
so a ranged alias collapses to the primitive it erases to. That is what lets a caller hand a bare
`(10, 32)` to a `(Num, Num)` parameter. Both functions therefore read the same record and return the
same sum, so their difference is 0.
```maxon
typealias Num = int(0 to 1000)

function f(t (Num, Num)) returns Num
	return t.0 + t.1
end 'f'

function g(t (int, int)) returns int
	return t.0 + t.1
end 'g'

function main() returns ExitCode
	let t = (10, 32)
	return f(t) - g(t)
end 'main'
```
```exitcode
0
```

<!-- test: tuple-var-field-write-with-string-element -->
A `var` tuple holding a MANAGED element, written through `.1` and read back. The write rides the same
`storeIndirect` a struct field write does, and the String is released by the tuple's drop cascade at
scope exit — a missed drop exits 101.
```maxon
function main() returns ExitCode
	var t = ("a", 1)
	t.1 = 41
	return t.1 + 1
end 'main'
```
```exitcode
42
```

<!-- test: whole-tuple-reassignment-drops-the-old-record -->
Reassigning a whole `var` tuple drops the record it held before binding the new one. The old box owns a
`String`, so a missed drop is a leak (101) and a doubled one an over-release.
```maxon
function main() returns ExitCode
	var t = ("a", 10)
	t = ("b", 32)
	return t.1 + 10
end 'main'
```
```exitcode
42
```

<!-- test: array-of-tuples-from-a-literal -->
A `[<identifier>…]` array literal whose elements are TUPLES, in a program that ALSO declares
`Array with <a tuple typealias>`. Both spellings reach the generic-instance registry, and the registry
keys on the argument's `(tag, id)` while the compiled name reads only the NAME — so an element arg
carrying the wrong tag interns a SECOND instance that compiles to the first's symbol, and
`checkTypeSymbolNamespace` rejected this whole program with `E3006 duplicate definition of
'Array_<T>'`, blaming a `typealias` the author wrote exactly once. A tuple type is `structRef` at every
spelling (`Parser.parseTypeReference`), so `arrayInstanceForNamedElement` must mint that tag too.
Oracle-verified against `maxon-sharp` (exit 45). Returns 45 (3 + 40 from element 1, plus the count).
```maxon
typealias Num = int(0 to 1000)
typealias Pair = (Num, Num)
typealias PairArray = Array with Pair

function count(xs PairArray) returns Num
	return xs.count()
end 'count'

function main() returns ExitCode
	let p = (1, 2)
	let q = (3, 40)
	var xs = [p, q]
	let r = try xs.get(1) otherwise panic("pushed")
	return r.0 + r.1 + xs.count()
end 'main'
```
```exitcode
45
```

<!-- test: array-of-managed-element-tuples-drops-each -->
`Array with <a tuple typealias whose elements are themselves ALIASES>`, holding a MANAGED element. The
DECLARATION SWEEP spells a tuple's elements the way the source did (`__Tuple2.Num.String`), because the
alias registry that says what `Num` means is still being built; every VALUE the array holds carries the
canonical `__Tuple2.int.String`, and only that layout reaches `installStructDestructors`. Read raw, the
array's `element_destroy@40` stamp named a destructor nobody synthesizes and the x64 backend died —
`panic … resolveCallFixups: call to unknown function '__destruct___Tuple2.Num.String'`. A generic
instance's stored ARGUMENT is a second door this index's types leave by, so it canonicalizes exactly as
`adoptType` does. Oracle-verified against `maxon-sharp` (exit 4, same stdout). Returns 4 (2 + the count).
```maxon
typealias Num = int(0 to 1000)
typealias Tagged = (Num, String)
typealias TaggedArray = Array with Tagged

function main() returns ExitCode
	var xs = TaggedArray.create()
	xs.push((1, "hello"))
	xs.push((2, "world"))
	let r = try xs.get(1) otherwise panic("pushed")
	print("{r.1}\n")
	return r.0 + xs.count()
end 'main'
```
```exitcode
4
```
```stdout
world
```

<!-- test: parenthesized-grouping-is-not-a-tuple -->
A parenthesized expression with no comma is grouping, and stays so: `(2 + 3) * 4` is 20.
```maxon
function main() returns ExitCode
	let x = (2 + 3) * 4
	return x + 22
end 'main'
```
```exitcode
42
```

<!-- test: error.one-element-tuple-type -->
A one-element tuple TYPE is refused: `(a)` is grouping and must stay grouping, so parentheses around a
single type would be indistinguishable from it.
```maxon
typealias Solo = (int)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:2:18: Unsupported: a one-element tuple type (a tuple has at least 2 elements, and parentheses around a single type would be indistinguishable from grouping)
```

<!-- test: error.destructure-arity-mismatch -->
Every element must be named. The count is checked against the tuple the initializer produced.
```maxon
function main() returns ExitCode
	let (x, y, z) = (1, 2)
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:2: Unsupported: a destructuring binding of 3 names against a tuple of 2 elements (every element must be named)
```

<!-- test: error.destructure-of-a-non-tuple -->
Only a tuple can be destructured.
```maxon
function main() returns ExitCode
	let (x, y) = 42
	return x
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:2: Unsupported: a destructuring binding of a 'int' initializer (only a TUPLE can be destructured — `let (x, y) = …` needs a right-hand side that is a tuple)
```

<!-- test: error.tuple-of-a-type-parameter -->
A tuple's identity IS its element types, so a generic type PARAMETER cannot key one: the shared body
compiles once against an opaque `T` and does not know which concrete type the record would hold, so
neither its layout nor its drop cascade has anything to be built from.
```maxon
type Box uses T
	export let v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function pack() returns int
		let t = (self.v, 1)
		return t.1
	end 'pack'
end 'Box'

function main() returns ExitCode
	let b = Box.create(5)
	return b.pack()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:11: Unsupported: a tuple whose element 0 is 'type parameter': a tuple's identity IS its element types, and neither a generic type parameter (whose concrete type the shared body does not know) nor an unresolved type can key one
```

<!-- test: error.duplicate-owned-element -->
shv2 is move-only, so one owned value cannot be owned by two slots of one record — the tuple's cascade
would drop it twice.
```maxon
function main() returns ExitCode
	var s = "hello"
	let t = (s, s)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:4:14: use of moved value 's': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: returned-tuple-copies-only-when-trivial -->
⭐ **THE TWO HALVES OF `return t` ON A TUPLE PARAMETER, PINNED SIDE BY SIDE, BECAUSE THEY DIFFER (S5).** A
TRIVIAL tuple gets its own record — the caller's `a` keeps its `2` after the returned `b` is written — and
that is what a tuple is for. A MANAGED-element tuple gets an `__mm_retain` instead, because a shallow copy
would leave two records pointing at one `String` and free it twice, so writing through the returned `n` shows
on `m`. The split is a soundness one, not a taste one, and **the value oracle answers exactly the same on
both halves** (measured: `a.1=2 b.1=99 m.1=99 n.1=99`). The managed half was REFUSED before S5, which is why
this case exists: opening the borrowed-aggregate return opened it for a tuple too, and an unpinned share is
the kind of thing that becomes a wrong answer without a test noticing.

⚠ The exit code deliberately adds 7. `a.1 + m.1` alone is `2 + 99` = **101**, which is the runtime's
LEAK-DETECTED exit code — a spec whose correct answer collides with the failure marker cannot tell the two
apart, and this one nearly did.
```maxon

typealias Num = int(i64.min to i64.max)

function echoTrivial(t (Num, Num)) returns (Num, Num)
	return t
end 'echoTrivial'

function echoManaged(t (String, Num)) returns (String, Num)
	return t
end 'echoManaged'

function main() returns ExitCode
	var a = (1, 2)
	var b = echoTrivial(a)
	b.1 = 99

	var m = ("a heap allocated tuple element", 3)
	var n = echoManaged(m)
	n.1 = 99

	print("trivial a.1={a.1} b.1={b.1} managed m.1={m.1} n.1={n.1}")
	return (a.1 + m.1 + 7) as ExitCode
end 'main'
```
```stdout
trivial a.1=2 b.1=99 managed m.1=99 n.1=99
```
```exitcode
108
```
