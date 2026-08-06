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

<!-- test: small-tuple-return-allocates-nothing -->
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

<!-- test: nested-tuple-through-an-inner-alias -->
A tuple alias used as an ELEMENT of another tuple alias. `Nested` is `(Pair, Num)` where `Pair` is
itself `(Num, Num)`, and the argument is a bare nested literal. The declared type and the literal name
the SAME type, so the call type-checks and the two levels read back independently. This is the case
that distinguishes an alias that is merely *spelled* differently from one that *denotes* something
different — the identical type written inline as `((Num, Num), Num)` is the sibling case below, and the
two must be accepted alike. Returns 5 + 1 + 7 = 13.
```maxon
typealias Num = int(0 to 1000)
typealias Pair = (Num, Num)
typealias Nested = (Pair, Num)

function take(t Nested) returns Num
	let inner = t._0
	return inner._0 + inner._1 + t._1
end 'take'

function main() returns ExitCode
	return take(((5, 1), 7))
end 'main'
```
```exitcode
13
```

<!-- test: nested-tuple-written-inline -->
The CONTROL for the case above: the same type with no inner alias, written inline as
`((Num, Num), Num)`. A tuple's identity is its element types, so this and `Nested` are one type and
neither spelling may be privileged. Returns 13.
```maxon
typealias Num = int(0 to 1000)

function take(t ((Num, Num), Num)) returns Num
	let inner = t._0
	return inner._0 + inner._1 + t._1
end 'take'

function main() returns ExitCode
	return take(((5, 1), 7))
end 'main'
```
```exitcode
13
```

<!-- test: error.nested-tuple-shapes-stay-distinct -->
⭐ The NEGATIVE control for the two cases above, and the reason they may not be satisfied by comparing
tuples loosely. `((Num, Num), Num)` and `(Num, (Num, Num))` hold the same three scalars in the same
order and differ ONLY in where the nesting falls, so a fix that accepted a nested literal by counting
or flattening its leaves would accept this too. It must stay rejected.
```maxon
typealias Num = int(0 to 1000)

function takeLeft(t ((Num, Num), Num)) returns Num
	let inner = t._0
	return inner._0 + inner._1 + t._1
end 'takeLeft'

function main() returns ExitCode
	return takeLeft((5, (1, 7)))
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/tuples/error.nested-tuple-shapes-stay-distinct.test:10:9: argument type mismatch for 't': expected '__Tuple___Tuple_i64_i64_i64', got '__Tuple_i64___Tuple_i64_i64'
```

<!-- test: tuple-element-names-join-injectively -->
⭐⭐ Two DIFFERENT tuple types whose element names differ only in where an `_` falls. A tuple's
identity is derived from its elements' names, and `_` is inside the identifier alphabet — so joining
with `_` spells `(A_B, C)` and `(A, B_C)` identically, and whichever is interned FIRST donates its
field table to the other. Here `A_B` and `A` carry the same two field names in the OPPOSITE order, so
the mix-up does not fail to find a name: it silently reads the wrong slot. `first` reads `A_B.p` = 1
and `second` reads `A.p` = 3, so the sum is 4; a collision returns 8, having read `A`'s `q`. The join
must use something no element name can hold. Returns 4.
```maxon
typealias Num = int(0 to 1000)

type A_B
	export let p as Num
	export let q as Num

	export static function create(p Num, q Num) returns Self
		return Self{p: p, q: q}
	end 'create'
end 'A_B'

type A
	export let q as Num
	export let p as Num

	export static function create(q Num, p Num) returns Self
		return Self{q: q, p: p}
	end 'create'
end 'A'

type C
	export let z as Num

	export static function create(z Num) returns Self
		return Self{z: z}
	end 'create'
end 'C'

type B_C
	export let z as Num

	export static function create(z Num) returns Self
		return Self{z: z}
	end 'create'
end 'B_C'

function first(t (A_B, C)) returns Num
	let l = t._0
	return l.p
end 'first'

function second(t (A, B_C)) returns Num
	let l = t._0
	return l.p
end 'second'

function main() returns ExitCode
	let pTup = (A_B.create(1, q: 2), C.create(0))
	let qTup = (A.create(7, p: 3), B_C.create(0))
	return first(pTup) + second(qTup)
end 'main'
```
```exitcode
4
```
