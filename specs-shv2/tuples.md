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

<!-- test: for-destructuring-map -->
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
<!-- needs the TWO-REGISTER VALUE-TUPLE ABI, which is the FOLLOW-ON rung. shv2 has NO multi-register or hidden-pointer return path on any target (one GPR, R8/x0, every aggregate a heap pointer), so the register-pair return is a new calling convention on x64 + arm64 + wasm, not a pass. Every other tuple case asserts an exit code, which heap-allocated tuples satisfy — v1 ships tuples heap-always and passes them. ⚠ The mm-trace half is NO LONGER a blocker: capture mode landed with `/spec-port mm-trace`, and `for-in-over-map-allocates-no-tuple` below carries a live ```mm-trace golden. That golden records one heap `__Tuple2` per iteration, which is this same ABI gap seen from the other side. -->
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

<!-- test: for-in-over-map-allocates-no-tuple -->
<!-- needs `Map`, which is the FOLLOW-ON rung: `MapIterator.current()` returns a genuine tuple, so Map is sequenced AFTER tuples and cannot be unlocked here. Not a tuple gap. -->
⛔⛔ **THE ALLOCATION PROPERTY THIS CASE IS NAMED FOR WAS PINNED BY NOTHING FROM THE DAY IT WAS PORTED,
AND IS PINNED AGAIN NOW.** It arrived carrying a `<!-- MmTrace -->` directive and an ```mm-trace block
listing every allocation the Map machinery makes, and its own prose said *"the golden is the pin"* — but
shv2's `SpecParser` had an arm for neither, so both were walked past in silence. The case was ACTIVE and
PASSING on its ```exitcode block alone. The dropped block was removed 2026-08-06 (BATCH29/A3a) by
`SpecParser.isUnimplementedFenceOpen`, which refuses an unreadable fence instead of skipping it; the
directive stayed, and pinned nothing, until mm-trace capture mode landed.

⇒ **The mm-trace arm and the runner's monitor capture now exist** (`/spec-port mm-trace`: an
`<!-- MmTrace -->` case is built with `--debugstream`, run under `maxon monitor --filter=mm`, and its
decoded trace compared against the ```mm-trace golden below). The directive is live and the block is
minted from what this compiler actually allocates.

⛔⛔ **AND WHAT IT RECORDS IS THAT THE PROPERTY DOES NOT HOLD: ONE 16-byte `__Tuple2` RECORD IS ALLOCATED
AND FREED PER ITERATION.** The golden shows three, for a three-entry map. That is not a regression this
port introduced — it is the SAME missing mechanism the sibling `small-tuple-return-allocates-nothing` is
shelved on, stated in its shelve note: shv2 has no multi-register or hidden-pointer return path on any
target, so `current()` cannot hand its `(key, value)` pair back in registers and every tuple is a heap
record. The case still passes: its ```exitcode block asserts the answer, and the golden now asserts the
allocation behaviour AS IT IS rather than asserting nothing at all.

⇒ **The block is therefore a LEDGER, not a green light.** When the two-register value-tuple ABI lands,
the three `__Tuple2` lines disappear from it and the case's name becomes true; until then the golden is
what makes the gap visible on every run instead of only in a shelve note.

A `for` loop over a Map allocates NO tuple record per iteration. The iterator's `current()`
returns its `(key, value)` pair in two registers, and the loop's item binding does not escape,
so the pair lives in stack slots for the iteration and dies with it.
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
mm_alloc ArrayRecord #1 size=48
mm_alloc ArrayRecord #2 size=48
mm_alloc ArrayRecord #3 size=48
mm_alloc ArrayRecord #4 size=48
mm_alloc Map #5 size=48
mm_alloc ArrayRecord #6 size=48
mm_decref ArrayRecord #1 rc=0
mm_free ArrayRecord #1
mm_alloc ElementBuffer #7 size=128
mm_alloc ArrayRecord #8 size=48
mm_decref ArrayRecord #2 rc=0
mm_free ArrayRecord #2
mm_alloc ElementBuffer #9 size=128
mm_alloc ArrayRecord #10 size=48
mm_decref ArrayRecord #3 rc=0
mm_free ArrayRecord #3
mm_alloc ElementBuffer #11 size=16
mm_alloc ArrayRecord #12 size=48
mm_decref ArrayRecord #4 rc=0
mm_free ArrayRecord #4
mm_alloc ElementBuffer #13 size=64
mm_incref ArrayRecord #6 rc=2
mm_incref ArrayRecord #8 rc=2
mm_incref ArrayRecord #10 rc=2
mm_alloc MapIterator #14 size=40
mm_incref ArrayRecord #6 rc=3
mm_incref ArrayRecord #8 rc=3
mm_incref ArrayRecord #10 rc=3
mm_decref ArrayRecord #10 rc=2
mm_decref ArrayRecord #8 rc=2
mm_decref ArrayRecord #6 rc=2
mm_alloc __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #15 size=16
mm_decref __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #15 rc=0
mm_free __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #15
mm_alloc __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #16 size=16
mm_decref __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #16 rc=0
mm_free __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #16
mm_alloc __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #17 size=16
mm_decref __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #17 rc=0
mm_free __Tuple2.T38ee35d378b734f4.Taebb70a7666c1b3c #17
mm_decref MapIterator #14 rc=0
mm_decref ArrayRecord #6 rc=1
mm_decref ArrayRecord #8 rc=1
mm_decref ArrayRecord #10 rc=1
mm_free MapIterator #14
mm_decref Map #5 rc=0
mm_decref ArrayRecord #6 rc=0
mm_decref ElementBuffer #7 rc=0
mm_free ElementBuffer #7
mm_free ArrayRecord #6
mm_decref ArrayRecord #8 rc=0
mm_decref ElementBuffer #9 rc=0
mm_free ElementBuffer #9
mm_free ArrayRecord #8
mm_decref ArrayRecord #10 rc=0
mm_decref ElementBuffer #11 rc=0
mm_free ElementBuffer #11
mm_free ArrayRecord #10
mm_decref ArrayRecord #12 rc=0
mm_decref ElementBuffer #13 rc=0
mm_free ElementBuffer #13
mm_free ArrayRecord #12
mm_free Map #5
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

<!-- test: an-eight-deep-nesting-is-one-type-however-it-is-spelled -->
⭐ **NESTING DEPTH, WHICH NO OTHER CASE TAKES PAST THREE — and W9 is why it is now worth taking.** A
tuple's identity used to INLINE every element, so an eight-level nest spelled its whole history at every
level: `A3j` measured that as quadratic in depth for a chain and as DOUBLING PER LEVEL for a branching one
(a 23-line program cost 2.47 s and 184 MB at depth 18, and depth 24 extrapolated to ~11 GB). A nested
element is now cited by an interned token, so depth is linear — and this is the case that says the
CITATION is still an identity rather than merely cheap.

The two spellings must converge: `readDeep` declares its parameter through an eight-link ALIAS CHAIN and
`main` hands it a bare LITERAL nested eight deep. If the citation split those into two types the call is
`E3005`; if it fused two DIFFERENT levels the eight `.0` hops land on the wrong slot and the arithmetic
moves. Returns 1 + 9 + 2.
```maxon
typealias L0 = (int, int)
typealias L1 = (L0, int)
typealias L2 = (L1, int)
typealias L3 = (L2, int)
typealias L4 = (L3, int)
typealias L5 = (L4, int)
typealias L6 = (L5, int)
typealias L7 = (L6, int)

function readDeep(t L7) returns Integer
	return t.0.0.0.0.0.0.0.0 + t.1
end 'readDeep'

function main() returns ExitCode
	let t = ((((((((1, 2), 3), 4), 5), 6), 7), 8), 9)
	return (readDeep(t) + t.0.0.0.0.0.0.0.1) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
12
```

<!-- test: tuple-element-from-method-call -->
An element built from a METHOD-CALL RESULT. v1 leaked exactly this shape: the tuple reached lowering
with no parse-time-pinned type for the element, so its `__mm_alloc` got a NULL destructor and the
element's box was never released. Here the layout is minted from the element VALUES' own types at the
literal, so the struct element is an ordinary managed field and the tuple's synthesized drop cascade
frees it. A leak exits 101 rather than 42.
```maxon
type Point
	export let x as Integer

	export static function create(x Integer) returns Point
		return Self{x: x}
	end 'create'
end 'Point'

function main() returns ExitCode
	let t = (1, Point.create(41))
	return t.0 + t.1.x
end 'main'
typealias Integer = int(i64.min to i64.max)
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
	export let alpha as Integer

	export static function create(alpha Integer) returns Self
		return Self{alpha: alpha}
	end 'create'
end 'A_B'

type A
	export let gamma as Integer

	export static function create(gamma Integer) returns Self
		return Self{gamma: gamma}
	end 'create'
end 'A'

type C
	export let beta as Integer

	export static function create(beta Integer) returns Self
		return Self{beta: beta}
	end 'create'
end 'C'

type B_C
	export let delta as Integer

	export static function create(delta Integer) returns Self
		return Self{delta: delta}
	end 'create'
end 'B_C'

function first(t (A_B, C)) returns Integer
	return t.0.alpha + t.1.beta
end 'first'

function second(t (A, B_C)) returns Integer
	return t.0.gamma * 10 + t.1.delta
end 'second'

function main() returns ExitCode
	let p = (A_B.create(1), C.create(2))
	let q = (A.create(3), B_C.create(4))
	return first(p) + second(q)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
37
```

<!-- test: ranged-alias-elements-are-one-tuple-type -->
`(Num, Num)` and `(Integer, Integer)` are ONE tuple type: a tuple's identity is its elements' UNDERLYING
types, so two ranged aliases over the same primitive collapse alike. (The bare `(int, int)` this case used
to write is no longer a legal parameter type — a numeric domain must be declared — so the widest DECLARED
range stands in for it, which tests the same collapse.) That is what lets a caller hand a bare
`(10, 32)` to a `(Num, Num)` parameter. Both functions therefore read the same record and return the
same sum, so their difference is 0.
```maxon
typealias Num = int(0 to 1000)

function f(t (Num, Num)) returns Num
	return t.0 + t.1
end 'f'

function g(t (Integer, Integer)) returns Integer
	return t.0 + t.1
end 'g'

function main() returns ExitCode
	let t = (10, 32)
	return f(t) - g(t)
end 'main'
typealias Integer = int(i64.min to i64.max)
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

### ⭐ A TUPLE OVER A GENERIC TYPE PARAMETER (W43)

This case was `error.tuple-of-a-type-parameter` and is now a POSITIVE one. The refusal carried three
reasons and each was answered by a different rung, which is why it survived so long: IDENTITY by `W14`
(a type-parameter token is a digest of `(declaring type, parameter)`, so two parameters mangle and intern
apart, where a POSITION made them one), LAYOUT by `W6` (a type parameter lowers to an opaque machine word
and every non-existential field is one slot, so `(T, Int)` is statically 16 bytes), and the DROP CASCADE by
the layout descriptor the instance already carries. Refusing the TYPE to avoid a question about a VALUE was
the category error; the value-side rule keeps its own refusal at the construction site.

⚠ It is pinned in BOTH element classes deliberately. A scalar `T` proves only that the shape parses; the
MANAGED one is where a wrong cascade would show, as a double free or a leak — and a leak is exit 101, which
is a failure of this case rather than a note.

<!-- test: tuple-of-a-type-parameter -->
```maxon
type Box uses T
	export let v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function pack() returns Integer
		let t = (self.v, 1)
		return t.1
	end 'pack'
end 'Box'

function main() returns ExitCode
	let b = Box.create(5)
	return b.pack()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
1
```

<!-- test: tuple-of-a-managed-type-parameter -->
The same body instantiated over `String`, so the tuple's element 0 is an opaque word that really does own
heap. Measured: exit 7, and no leak.
```maxon
type Box uses T
	export let v as T

	export static function create(v T) returns Self
		return Self{v: v}
	end 'create'

	export function pack() returns Integer
		let t = (self.v, 1)
		return t.1
	end 'pack'
end 'Box'

typealias StrBox = Box with String

function main() returns ExitCode
	let b = StrBox.create("hello")
	return b.pack() + 6
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
7
```

<!-- test: duplicate-owned-element-co-owns -->
One owned value CAN fill two slots of one record: each slot is a durable sink and takes its own reference
(⚖ 2026-08-12), so the tuple's drop cascade releases exactly the two it took and `s` releases the one it
always held. It used to be E3102, on the premise that shv2 is move-only and the cascade would drop one
`+1` twice.
```maxon
function main() returns ExitCode
	var s = "hello"
	let t = (s, s)
	print("{t.0}{t.1}{s}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hellohellohello
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

<!-- test: merged-tuple-copies-at-every-hand-off -->
⭐⭐ **THE VALUE-SEMANTICS ANSWER BELONGS TO THE HAND-OFF, NOT TO THE `return` KEYWORD (S5 review).** The case
above pins `return t`; this one pins the two doors that reach the caller through a MERGE first — a ternary arm
and a `try … otherwise` fallback — and a BINDING as the negative control. A borrowed trivial tuple is copied at
a hand-off and INCREF'd at a binding, so `a.1` and `m.1` keep their `2` while `g.1` reads the `55` the callee
wrote through its alias. **The value oracle answers all three identically** (measured: `merged a.1=2 fallback
m.1=2 binding g.1=55`).

⚠ It is the merge that made this worth pinning. The tuple copy first shipped inside `emitOwnedValueReturn`,
where it was the `returned` door's private rule; when S5 opened the `merged` door onto the same shared
promotion, a borrowed tuple was increfed by the merge and then walked past the return's own copy — which asks
`not valueIsOwnedHeap`, and an incref answers that question wrongly by design. shv2 printed `a.1=99` here
against the oracle's `a.1=2`. The gate lives in `promoteBorrowedToOwned` now, where all three doors ask it.
```maxon

typealias Num = int(i64.min to i64.max)

enum Fail
	nope
end 'Fail'

function risky(ok bool) returns (Num, Num) throws Fail
	if ok 'ok'
		return (7, 8)
	end 'ok'
	throw Fail.nope
end 'risky'

function viaMerge(t (Num, Num), c bool) returns (Num, Num)
	return t if c else (7, 8)
end 'viaMerge'

function viaFallback(t (Num, Num)) returns (Num, Num)
	return try risky(false) otherwise t
end 'viaFallback'

function viaBinding(t (Num, Num)) returns Num
	var q = t
	q.1 = 55
	return 0
end 'viaBinding'

function main() returns ExitCode
	var a = (1, 2)
	var b = viaMerge(a, c: true)
	b.1 = 99

	var m = (1, 2)
	var n = viaFallback(m)
	n.1 = 99

	var g = (1, 2)
	let z = viaBinding(g)

	print("merged a.1={a.1} fallback m.1={m.1} binding g.1={g.1} z={z}")
	return (a.1 + m.1 + g.1 + 9) as ExitCode
end 'main'
```
```stdout
merged a.1=2 fallback m.1=2 binding g.1=55 z=0
```
```exitcode
68
```

### A tuple typealias across a FILE BOUNDARY, in BOTH orders (A3e)

⭐⭐ **A TUPLE ALIAS MINTS NO IDENTITY OF ITS OWN, SO EVERY DECLARED POSITION SPELLED WITH ONE HAS TO BE
RESOLVED THROUGH THE ALIAS REGISTRY — AND THAT REGISTRY HAS A FILLING ORDER.** The tolerant declaration
sweep reads a struct FIELD and a RETURN clause, and the sweep's copy is the one the whole-program index
stores. Asked there, "is `Pair` a tuple alias?" answers *how far the sweep had got*: the alias is
registered the moment its own file is walked, so a sibling file walked BEFORE it records the position as a
bare `named("Pair")` and one walked AFTER records the tuple's `structRef`.

⚠ **MEASURED, A3e** — five doors answered differently in the two walk orders, each accepting the program
when the alias file was walked first and refusing it when it was walked last:

| door | alias file walked FIRST | alias file walked LAST (before A3e) |
|---|---|---|
| `var p as Pair` | ran | `E3005 cannot assign 'struct' to variable 'p' of type 'int'` |
| `returns Pair` | ran | `E3011 Unknown type 'Pair'` |
| `returns Pair` on a METHOD | ran | `E3011 Unknown type 'Pair'` |
| `(Pair, Int)` | ran | `E2015 … '__Tuple2.Pair.int._0' … declared 'int' and not a struct` |
| `Array with Pair` | ran | `E3005 cannot assign 'struct' to variable 'push' of type 'int'` |
| `some(v Pair)` union payload | ran | `E3011 Unknown type 'Pair'` |
| `var p as Pair`'s DROP | ran | leaked — the field was dropped by nobody, exit 101 |

⚠ **THE PAIR BELOW DECLARES ITS TWO FILES IN THE TWO ORDERS, AND SINCE A3m THAT IS WHAT IT GETS.** It was
not always: the loader walked whatever `Directory.list` handed back and deliberately does not sort
(`StdlibLoader`'s header, user ruling 2026-07-24), and that answer was a property of the staging
directory's on-disk state, not of the file names — MEASURED, the first case here was the REFUSED one while
the same two files copied into a fresh directory refused the second. So the pair was a two-ticket lottery
on a program that must not care. `build` now takes an ORDERED list of paths and the runner names each
case's files in the order the case declares them, so each half below really does compile in its own order
— and the loader still sorts nothing, because a sort would hide the dependence rather than surface it.

A `named` that the sweep left under an alias spelling is repaired at the READ door — a declared slot's type
(`ProgramSignatures.declaredSlotType`), a call result (`Parser.resolveNamedAlias`), and the four classifiers
that read a RAW layout field type, through the one resolution `ProgramSignatures.denotedAggregateName`
names. That is exactly the arrangement a function alias and a generic-instance alias already had; the tuple
alias was the one kind no classifier resolved, which is why its `named` spelling was a wrong answer rather
than merely a less resolved one.

⚠ **`parseTypeReference`'s tuple-alias arm is NOT gated on `allFilesFolded` — the three sibling arms are.**
`recordTupleAlias` writes DURING the walk rather than at `foldFile`, so for a tuple alias the SWEEP's
resolved answer is the common case; gating it moves every `Array with <a tuple alias>` onto the bare `named`
spelling, which seven index doors read RAW off the stored generic-instance ARGUMENT. Measured, it changed
this file's `value-tuple-escaping-into-array-stays-heap` element size and the answer of
`array-of-managed-element-tuples-drops-each`.

<!-- test: sibling-files-tuple-alias-in-every-declared-position -->

⭐ Every position a tuple alias can hold that the declaration SWEEP reads: a struct field (whose drop
cascade must reach it), a method return, a free-function return, a tuple SLOT, an `Array` element and a
union payload. The alias file declares nothing but aliases, so the two cases emit the same functions and
their goldens must read alike. Returns 2 + 4 + 6 + 8 + 10 + 12 + 14 + 16 = 72.
```maxon
// --- file: alias.maxon
export typealias Int = int(i64.min to i64.max)
export typealias Pair = (Int, Int)
export typealias PairArray = Array with Pair

// --- file: main.maxon
type Holder
	export var p as Pair

	export static function create() returns Holder
		return Self{p: (2, 4)}
	end 'create'

	export function pair() returns Pair
		return (6, 8)
	end 'pair'
end 'Holder'

union Slot
	empty
	some(v Pair)
end 'Slot'

function make() returns Pair
	return (10, 0)
end 'make'

function nested() returns (Pair, Int)
	return ((12, 0), 0)
end 'nested'

function main() returns ExitCode
	let h = Holder.create()
	var xs = PairArray.create()
	xs.push((14, 0))
	let e = try xs.get(0) otherwise return 1
	let n = nested()
	let s = Slot.some((16, 0))
	let sv = match s 'm'
		empty gives 0
		some(v) gives v.0
	end 'm'
	return (h.p.0 + h.p.1 + h.pair().0 + h.pair().1 + make().0 + n.0.0 + e.0 + sv) as ExitCode
end 'main'
```
```exitcode
72
```

<!-- test: sibling-files-tuple-alias-in-every-declared-position-either-order -->

⭐ **THE IDENTICAL PROGRAM, ITS TWO FILES DECLARED THE OTHER WAY ROUND.** Before A3e one of this pair was
five separate refusals of the program the other compiled and ran — which one depended on the staging
directory. Both halves are kept because half a pair is just the ticket that happened to win; since A3m
neither half is a ticket at all.
```maxon
// --- file: main.maxon
type Holder
	export var p as Pair

	export static function create() returns Holder
		return Self{p: (2, 4)}
	end 'create'

	export function pair() returns Pair
		return (6, 8)
	end 'pair'
end 'Holder'

union Slot
	empty
	some(v Pair)
end 'Slot'

function make() returns Pair
	return (10, 0)
end 'make'

function nested() returns (Pair, Int)
	return ((12, 0), 0)
end 'nested'

function main() returns ExitCode
	let h = Holder.create()
	var xs = PairArray.create()
	xs.push((14, 0))
	let e = try xs.get(0) otherwise return 1
	let n = nested()
	let s = Slot.some((16, 0))
	let sv = match s 'm'
		empty gives 0
		some(v) gives v.0
	end 'm'
	return (h.p.0 + h.p.1 + h.pair().0 + h.pair().1 + make().0 + n.0.0 + e.0 + sv) as ExitCode
end 'main'

// --- file: alias.maxon
export typealias Int = int(i64.min to i64.max)
export typealias Pair = (Int, Int)
export typealias PairArray = Array with Pair
```
```exitcode
72
```

<!-- test: error.nested-tuple-name-does-not-depend-on-which-file-declares-it -->

⭐⭐ **A NESTED TUPLE'S NAME IS A STATEMENT ABOUT THE PROGRAM, NOT ABOUT THE FILESYSTEM (W14b).** This case
and its twin below are the SAME program with the SAME file NAMES; the only difference is which of the two
sibling files declares which alias. Nothing a compiler may observe about the program has changed — `PX` is
`((int, int), int)` in both — so the two cases pin **one byte-identical sentence**, and a citation decided by
the order the directory handed the files over cannot satisfy both, whatever text is pinned.

⚠ **THE PAIR ABOVE (`sibling-files-tuple-alias-in-every-declared-position{,-either-order}`) CANNOT SEE THIS
AND IS NOT A SUBSTITUTE.** It varies the order the two files appear in THIS BLOCK, and the harness stages
both under the same two names either way — so `Compiler.collectMaxonSources` walks them identically and both
halves mint identically. It pins that the two declaration orders AGREE ON THE PROGRAM'S MEANING; this pair
pins that they agree on the NAME. Swapping the file CONTENTS is what moves the walk, and it is why these two
cases exist rather than a marker flip on those.

⚠ **THE MESSAGE NAMES BOTH TYPES ON PURPOSE.** A one-type message could be made to agree by accident; a
sentence carrying the returned type AND the declared type pins the whole citation in both directions at one
anchor, in `main.maxon` — the one file whose name and contents are identical across the pair, so the
location never moves either.
```maxon
// --- file: aaa.maxon
export typealias PX = ((int, int), int)

export function makeX() returns PX
	return ((1, 2), 3)
end 'makeX'

// --- file: zzz.maxon
export typealias PY = ((String, String), int)

export function makeY() returns PY
	return (("a", "b"), 7)
end 'makeY'

// --- file: main.maxon
function coerce(t PY) returns PX
	return t
end 'coerce'

function main() returns ExitCode
	return coerce(makeY()).1 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:2: Cannot return '__Tuple2.__Tuple2#2ce2fdda76cda7c9.int' from function declared to return '__Tuple2.__Tuple2#90fac2b26f6571d1.int'
```

<!-- test: error.nested-tuple-name-does-not-depend-on-which-file-declares-it-swapped -->

⭐ **THE IDENTICAL PROGRAM, THE TWO ALIASES DECLARED THE OTHER WAY ROUND.** `aaa.maxon` now holds `PY` and
`zzz.maxon` holds `PX`. **The pinned sentence below must stay byte-identical to its twin's** — that equality
IS the assertion, and it is the whole reason this case is not a duplicate. If a future change makes these two
diverge, do NOT re-derive two different pins: a divergence here means the tuple citation has gone back to
being a function of the walk, which is the defect `W14b` removed.
```maxon
// --- file: aaa.maxon
export typealias PY = ((String, String), int)

export function makeY() returns PY
	return (("a", "b"), 7)
end 'makeY'

// --- file: zzz.maxon
export typealias PX = ((int, int), int)

export function makeX() returns PX
	return ((1, 2), 3)
end 'makeX'

// --- file: main.maxon
function coerce(t PY) returns PX
	return t
end 'coerce'

function main() returns ExitCode
	return coerce(makeY()).1 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:18:2: Cannot return '__Tuple2.__Tuple2#2ce2fdda76cda7c9.int' from function declared to return '__Tuple2.__Tuple2#90fac2b26f6571d1.int'
```

<!-- test: error.self-referential-tuple-alias -->

⚠ **THE TERMINATION CASE FOR THE READ-DOOR REPAIR.** Resolving a tuple-alias element follows the alias to
its target, and an alias NAME — unlike the nested tuple names the canonicalizing walk otherwise strips a
level off at every step — can name the very tuple being canonicalized. `typealias P = (P, Int)` registers
`__Tuple2.P.int` whose element 0 is `named("P")` whose target is `__Tuple2.P.int`. It is refused, and the
point of the case is that it is refused rather than recursing until the stack ends.

⚠ The declared type is a nest deeper than the source wrote, and that predates A3e: each real-parse read of
a self-naming alias re-registers it one level further out, so the type grows with the number of reads. It
is DETERMINISTIC and the program is illegal either way; the case pins it so that a change in the
termination rule cannot pass unnoticed.

⚠⚠ **THE MESSAGE NO LONGER SHOWS THAT DEPTH, AND W9 IS WHY — READ THE NEW SPELLING BEFORE TRUSTING YOUR
EYES.** Until W9 a tuple's name INLINED every element, so the two types read
`__Tuple2.__Tuple2.int.int.int` against a six-deep `__Tuple2.__Tuple2.…P.Int.int.int.int.int.int` and the
re-registration was legible in the string itself. That inlining is what `A3j` measured as quadratic in
nesting depth and exponential for a branching chain, and a nested element is now cited by a bounded TOKEN
instead. **The depth is still there and still grows; what the tokens carry is only that the two inner types
are DISTINCT.** So the case still discriminates a change in the termination rule — a different number of
re-entries builds a different inner type and moves its token — but it can no longer say WHICH way the depth
went.

⭐ **AND SINCE W14b THE TOKEN IS A DIGEST OF THE CITED TUPLE'S OWN NAME, so it says something the mint
ordinal it replaced could not.** `#90fac2b26f6571d1` is the SHALLOW type here — the one-level
`((int, int), int)` the function returns — and it is **the identical token this suite's
`error.nested-tuple-name-does-not-depend-on-which-file-declares-it` shows for `((int, int), int)` in an
unrelated three-file program**, because it is the same type and the citation is now a pure function of the
structure. `#41e3ea25fc813804` is the FIVE-level inner of the declared type. Neither number ranks by depth
and neither ever did; what changed is that they no longer rank by anything else either.

⭐ **THE TOKENS ARE THEREFORE NO LONGER MINT-ORDERED, AND THIS PARAGRAPH USED TO SAY THE OPPOSITE (W14b).**
It read that anything interning a tuple ahead of these — a stdlib module a future edit pulls in, a
differently-ordered directory walk — moved them, which made the pin depend on a count the sentence does not
name. It does not any more: the token is `fnv1a64` of the cited tuple's registered name, so **an unrelated
tuple interned first moves nothing** — which the case below this one pins rather than asserts.

⚠ **BUT THIS CASE'S DECLARED-TYPE TOKEN IS SENSITIVE TO THE ALIAS SPELLINGS, AND THAT IS NOT OBVIOUS FROM
THE RULE ABOVE (W14b review).** `P` is self-referential, so `canonicalTupleName`'s A3e re-entry resolves its
own element back to the SWEEP spelling `__Tuple2.P.Int` — a name whose elements are still the bare
identifiers this fragment wrote, because the sweep runs before the registries that say what they mean. The
Merkle chain therefore bottoms out there, and every digest above it inherits those two identifiers. ⛔
MEASURED: renaming `Int` to `Signed` and `P` to `Q`, which changes no type's SHAPE, moves
`#41e3ea25fc813804` to `#303df4d4860b223c`, while the returned type's `#90fac2b26f6571d1` — whose chain
bottoms out at the fully resolved `__Tuple2.int.int` — does not move at all. ⇒ **a move in the SECOND number
is a prompt to ask what the termination rule built OR what these two aliases are called; a move in the
FIRST is only ever the former.** The `-with-an-unrelated-tuple-interned-alongside` case below pins the axis
that a citation must never depend on: another file.

⭐ **THE REASON FIRST GIVEN FOR ACCEPTING IT WAS BACKWARDS, AND W14b REMOVED THE THING THAT NEEDED
ACCEPTING.** The W9 text read *"v1's `GenericInstanceId` has the identical property"*; the W9 review
established that v1 has the OPPOSITE property, by an explicit repair with its rationale written down:
`maxon-selfhosted/Compiler/Passes/BuildLayoutDescriptors.maxon:62-75` records that gid numbering "is NOT
stable across build scenarios" (`Array<String>` is gid 6 cold and gid 8 warm) and therefore emits sorted
**by the mangled label, "a pure function of the instance's structural shape"** — i.e. v1's dense id is
deliberately kept OUT of every emitted artifact, the same rule `SignatureIndex.mangleTypeArg` states for
shv2. shv2 now satisfies that rule rather than excusing itself from it: see
`ProgramSignatures.tupleElementTokenFor` for the digest, the walk-order measurement it removed, and the
standing 2026-07-24 file-order ruling it settles.
```maxon
typealias Int = int(i64.min to i64.max)
typealias P = (P, Int)

function make() returns P
	return ((1, 2), 3)
end 'make'

function main() returns ExitCode
	let t = make()
	return t.1 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:2: Cannot return '__Tuple2.__Tuple2#90fac2b26f6571d1.int' from function declared to return '__Tuple2.__Tuple2#41e3ea25fc813804.int'
```

<!-- test: error.self-referential-tuple-alias-with-an-unrelated-tuple-interned-alongside -->

⭐⭐ **THE CASE ABOVE, WITH AN UNRELATED SIBLING FILE THAT INTERNS TWO TUPLES OF ITS OWN — AND BOTH TOKENS
COME OUT UNCHANGED (W14b).** The program `main.maxon` holds is byte-identical to the one above; `aaa.maxon`
adds `((String, String), int)`, which is a different type in every respect and is walked FIRST. Both cited
tokens are the same two the case above pins, and only the fragment line moves (a file marker now sits in
front of the program).

⚠ **THIS EXISTS BECAUSE THE CASE ABOVE MADE A CLAIM IN PROSE THAT NOTHING TESTED.** Until W14b the tokens
were mint-ordered over the whole compile, and that case's own text warned that *anything* interning a tuple
ahead of them moved the numbers. ⛔ MEASURED against the pre-W14b compiler, this exact program: the shallow
type read `__Tuple2#6` alone and `__Tuple2#8` with `aaa.maxon` present, the declared type `__Tuple2#4` and
`__Tuple2#6` — a diagnostic about one file, moved two ordinals by a file that shares nothing with it. A
digest of the cited tuple's own name cannot do that, and this is where that stops being an argument.

⚠ It does NOT vary the walk order, and deliberately: `error.nested-tuple-name-does-not-depend-on-which-file-declares-it{,-swapped}`
already pins that axis by swapping two files' CONTENTS. What this pins is the other one — that an
UNRELATED interning does not reach a citation at all.
```maxon
// --- file: aaa.maxon
export typealias Other = ((String, String), int)

export function makeOther() returns Other
	return (("a", "b"), 1)
end 'makeOther'

// --- file: main.maxon
typealias Int = int(i64.min to i64.max)
typealias P = (P, Int)

function make() returns P
	return ((1, 2), 3)
end 'make'

function main() returns ExitCode
	let t = make()
	return t.1 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:2: Cannot return '__Tuple2.__Tuple2#90fac2b26f6571d1.int' from function declared to return '__Tuple2.__Tuple2#41e3ea25fc813804.int'
```

### A tuple typealias as a GENERIC TYPE ARGUMENT — the RAW instance-argument doors (A3e review)

⭐⭐ **A GENERIC INSTANCE'S TYPE ARGUMENT IS A *STORED* TYPE, NOT A DECLARED SLOT — so no read door re-tags
it, and the alias-spelling repair has to happen at each door that reads it.** `Box with Pair` interns ONE
argument, minted by whichever pass first met the spelling; the declaration SWEEP meets it with
`parseTypeReference`'s tuple-alias arm answering `isTupleAlias("Pair")`, which is false until the alias's own
declaration has been walked. **Within one file that is simply "the alias is declared BELOW its use", which is
deterministic** — the cross-file lottery above is the same fact with the filesystem holding the ticket.

⚠ **MEASURED, A3e review — two doors read that argument raw and both gave a WRONG ANSWER, not a diagnostic:**

| door | alias declared ABOVE its use | alias declared BELOW its use (before the fix) |
|---|---|---|
| `ProgramSignatures.typeLogicalByteSize` (`sizeof(T)`, `elementLogicalSize@56`) | `16` | **`8`** — `structOf("Pair")` missed, so it fell to `primitiveTypeByteSize(named)`, the machine-word fallback that exists for an enum/union. Compiled, ran, baked into `.rdata`. |
| `ProgramSignatures.typeArgIsOwned` (the CONSUME boundary) | ran | **E2015**, "a trivial-struct instantiation co-owns the field" — said of a tuple that owns a `String`, because `typeIsManaged` called it managed and this called it not-owned, and `typeArgIsCoOwnedTrivial` is exactly `typeIsManaged and not typeArgIsOwned`. |

Both now resolve through `ProgramSignatures.denotedAggregateName`, the one door that says what a `named`
denotes. The consume boundary and the drop boundary must read ONE name: they are read together, so a name
they answer differently about does not give a coarse answer, it manufactures a kind the type does not have.

<!-- test: sizeof-of-a-tuple-alias-type-argument-declared-below-its-use -->

⭐ `sizeof(T)` inside a shared generic body, instantiated at a tuple typealias whose declaration sits BELOW
the instantiation — so the SWEEP recorded the instance's argument as a bare `named`. A `(Integer, Integer)`
tuple is a two-slot box: **16**. Answering the `named` fallback's 8 is silent — the program still compiles
and runs.
```maxon
type Sizer uses T
	export var dummy as Integer

	export static function create() returns Self
		return Self{dummy: 0}
	end 'create'

	export function typeSize() returns Integer
		return sizeof(T)
	end 'typeSize'
end 'Sizer'

typealias PairSizer = Sizer with TPair

function main() returns ExitCode
	let s = PairSizer.create()
	return s.typeSize()
end 'main'

typealias Integer = int(i64.min to i64.max)
typealias TPair = (Integer, Integer)
```
```exitcode
16
```

<!-- test: shared-body-reassign-with-a-heap-owning-tuple-alias-instantiation -->

⭐ A shared generic body reassigns its opaque `T` field, and the SAME generic is ALSO instantiated at a tuple
typealias that OWNS heap (`(Integer, String)`), declared BELOW the instantiation. The reassign gate scans
every instantiation of `Box` and asks each argument's KIND; the heap-owning tuple must read `consumeMoved`,
not `coOwnedTrivial`. Read as co-owned-trivial it refused this program outright, and the refusal named a
"trivial-struct instantiation" that does not exist. `PairBox` is held by a field type so the instantiation is
live without being constructed.
```maxon
type Box uses Element
	export var saved as Element

	export static function create(first Element) returns Self
		return Self{ saved: first }
	end 'create'

	export function replace(next Element)
		self.saved = next
	end 'replace'
end 'Box'

typealias StringBox = Box with String
typealias PairBox = Box with TPair

type Holder
	export var b as PairBox

	export static function create(b PairBox) returns Self
		return Self{b: b}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var b = StringBox.create("alpha")
	b.replace("beta")
	return 0
end 'main'

typealias Integer = int(i64.min to i64.max)
typealias TPair = (Integer, String)
```
```exitcode
0
```

<!-- test: array-of-managed-element-tuples-clones-each -->
⭐ The CLONE half of `array-of-managed-element-tuples-drops-each`, and the half nobody reran (W180). The
DECLARATION SWEEP spells a tuple's elements the way the source did (`__Tuple2.String.Integer`) because the
alias registry that says what `Integer` means is still being built, so `Array with Pair` stores that spelling
as its type ARGUMENT; every VALUE the array holds carries the canonical `__Tuple2.String.int`, and only THAT
layout is committed to `project.structTypes`, which is the registry `installStructCloners` walks. Read raw,
the descriptor's `copyFunc@32` named `__clone___Tuple2.String.Integer` — a cloner in nobody's key space — and
the PE writer died at `bakeFuncAbs64Relocs: … which is in no debug symbol — it was never installed`. The
descriptor's ownership words and its copy word now read ONE derivation of the argument, canonical at the door
rather than at one of its readers. Oracle-verified against `maxon-sharp` (exit 6, same stdout). Returns 6
(the cloned tuple's 5, plus the clone's count).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Pair = (String, Integer)
typealias Pairs = Array with Pair

function main() returns ExitCode
	var xs = Pairs.create()
	var sb = StringBuilder.create()
	sb.append("a heap ")
	sb.append("payload")
	xs.push((sb.build(), 5))
	let ys = xs.clone()
	let cloned = try ys.get(0) otherwise panic("cloned")
	print("{cloned.0}\n")
	return cloned.1 + ys.count() as Integer
end 'main'
```
```exitcode
6
```
```stdout
a heap payload
```

<!-- test: array-of-bare-primitive-element-tuples-clones-each -->
The CONTROL for the case above, and the reason the two are a pair: with the element spelled `int` rather than
through a ranged alias, the sweep's spelling and the canonical one are the SAME bytes, so this program
compiled and ran throughout — a clone case written only this way would have passed for a reason that has
nothing to do with the defect. The ALIAS is the discriminator, not the tuple and not the clone.
Oracle-verified against `maxon-sharp` (exit 6, same stdout). Returns 6.
```maxon
typealias Pair = (String, int)
typealias Pairs = Array with Pair

function main() returns ExitCode
	var xs = Pairs.create()
	var sb = StringBuilder.create()
	sb.append("a heap ")
	sb.append("payload")
	xs.push((sb.build(), 5))
	let ys = xs.clone()
	let cloned = try ys.get(0) otherwise panic("cloned")
	print("{cloned.0}\n")
	return cloned.1 + ys.count()
end 'main'
```
```exitcode
6
```
```stdout
a heap payload
```

<!-- test: array-of-managed-element-tuples-clone-balances-both-copies -->
The BALANCE twin: a naming fix that installs the WRONG cloner links, so both copies are read after the clone
and both are dropped at scope exit. An under-retained element faults or prints poison on the second read
(`__mm_free` poisons with `0x3F`); an over-retained one leaks the String record and the run exits **101**. The
element cloner the `copyFunc@32` slot points at must be the same tuple's, so the clone owns an INDEPENDENT
String record and the original keeps its own. Oracle-verified against `maxon-sharp` (exit 8, same stdout).
Returns 8 (3 + 3, plus one count each).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Pair = (String, Integer)
typealias Pairs = Array with Pair

function main() returns ExitCode
	var xs = Pairs.create()
	var sb = StringBuilder.create()
	sb.append("shared ")
	sb.append("record")
	xs.push((sb.build(), 3))
	let ys = xs.clone()

	let original = try xs.get(0) otherwise panic("pushed")
	let copied = try ys.get(0) otherwise panic("cloned")
	print("{original.0}|{copied.0}\n")
	return original.1 + copied.1 + xs.count() as Integer + ys.count() as Integer
end 'main'
```
```exitcode
8
```
```stdout
shared record|shared record
```

<!-- test: array-of-substituted-entry-tuples-clones-each -->
⭐ The SUBSTITUTED half of the case above, and the SECOND installer that could not see a tuple layout (W180).
A tuple minted by SUBSTITUTION is in no file's artifact — `MapIterator.current()`'s declared
`Entry = (Key, Value)` becomes `__Tuple2.String.String` at the call site, straight into the whole-program index
— so it never reaches `project.structTypes`, and this program spells no tuple type anywhere for it to reach
through. `installStructDestructors` has walked the index's own tuple layouts since W41 for exactly that reason;
`installStructCloners` did not, so an `Array` of such entries stamped `__clone___Tuple2.String.String` and the
PE writer died with `it was never installed` — the drop side's W41 bug, on the clone side, with no ranged alias
and no `typealias` in the program at all. ⚠ `maxon-sharp` is NOT the oracle for this one: it prints two
POINTERS for a positionally-accessed `for`-bound Map entry (`{e.0}{e.1}` ⇒ `1406951003914561406951003915…`),
reported separately, so the expectation below is the language's rather than that compiler's. Returns 2 (one
element in each of the two arrays), or 9 if the entry loop never ran.
```maxon
function main() returns ExitCode
	let m = ["a": "x"]
	for e in m 'each'
		let arr = [e]
		let cp = arr.clone()
		let copied = try cp.get(0) otherwise panic("cloned")
		print("{copied.0}{copied.1}\n")
		return (cp.count() + arr.count()) as ExitCode
	end 'each'
	return 9 as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
ax
```

<!-- test: array-of-substituted-entry-tuples-with-a-struct-element-clones-each -->
The case above with a MANAGED STRUCT in the substituted entry, so the missing cloner is one whose body has a
NESTED cloner of its own (`__clone___Tuple2.String.Point` calling `__clone_Point`): the substituted tuple has
to be visible to the installer AND its nesting has to be closed, and a fix that only made the symbol exist
would emit a body calling a second symbol nobody built. Same walk, one level deeper. ⚠ `maxon-sharp` is not
the oracle here either, for the reason the case above records. Returns 2, or 9 if the entry loop never ran.
```maxon
type Point
	export var name as String

	export static function create(name String) returns Self
		return Self{name: name}
	end 'create'
end 'Point'

typealias PointMap = Map with (String, Point)

function main() returns ExitCode
	var m = PointMap.create()
	m.upsert("a", value: Point.create("alpha"))
	for e in m 'each'
		let arr = [e]
		let cp = arr.clone()
		let copied = try cp.get(0) otherwise panic("cloned")
		print("{copied.0}{copied.1.name}\n")
		return (cp.count() + arr.count()) as ExitCode
	end 'each'
	return 9 as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
aalpha
```
