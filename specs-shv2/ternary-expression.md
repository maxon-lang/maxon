---
feature: ternary-expression
status: experimental
keywords: [ternary, conditional, if, else, inline]
category: expressions
---

# Ternary Expression

## Documentation

Ternary expressions provide a concise way to choose between two values based on a condition. The syntax places the true value first, followed by the condition, then the false value:

```text
<true_value> if <condition> else <false_value>
```

The condition must be a `bool` expression, and both arms must produce the same type.

**Basic usage:**

```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

**With comparisons:**

```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let max = a if a > b else b
	return max
end 'main'
```
```exitcode
5
```

**With strings:**

```maxon
function main() returns ExitCode
	let flag = true
	let status = "Active" if flag else "Offline"
	print(status)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Active
```

**In string interpolation:**

```text
let msg = "Status: {"Active" if is_online else "Offline"}"
```

**Chaining:**

Ternary expressions can be chained. The else branch is parsed as a full expression, so chained ternaries associate to the right:

```text
let x = 1 if a else 2 if b else 3
// equivalent to: 1 if a else (2 if b else 3)
```

### Precedence

The ternary operator binds **looser** than all binary operators:

```text
a + b if cond else c * d
// equivalent to: (a + b) if cond else (c * d)
```

### Rules

- The condition must be a `bool` expression
- Both arms must produce the same type
- Binds looser than all binary operators
- Chainable via right-association of the else branch

## Tests

<!-- test: ternary-expression.basic-true -->
```maxon
function main() returns ExitCode
	let x = 10 if true else 20
	return x
end 'main'
```
```exitcode
10
```

<!-- test: ternary-expression.basic-false -->
```maxon
function main() returns ExitCode
	let x = 10 if false else 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: ternary-expression.with-variable-condition -->
```maxon
function main() returns ExitCode
	let flag = true
	let x = 1 if flag else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-comparison -->
```maxon
function main() returns ExitCode
	let a = 5
	let b = 3
	let x = 1 if a > b else 0
	return x
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
yes
```

<!-- test: ternary-expression.string-interp-expression -->
```maxon
function main() returns ExitCode
	let x = 5
	let msg = "value: {x if x > 0 else 0}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
value: 5
```

<!-- test: ternary-expression.string-interp-nested-strings -->
```maxon
function main() returns ExitCode
	let flag = true
	let msg = "status: {"on" if flag else "off"}"
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
status: on
```

<!-- test: ternary-expression.complex-expressions -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 2
	let cond = true
	let x = a + b if cond else a * b
	return x
end 'main'
```
```exitcode
5
```

<!-- test: ternary-expression.chained -->
```maxon
function main() returns ExitCode
	let a = false
	let b = true
	let x = 1 if a else 2 if b else 3
	return x
end 'main'
```
```exitcode
2
```

<!-- test: ternary-expression.in-return -->
```maxon
function main() returns ExitCode
	let done = true
	return 1 if done else 0
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-floats -->
```maxon
function main() returns ExitCode
	let x = 1.5 if true else 2.5
	return trunc(x)
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.mixed-int-float-promotes -->
The two branches cross register classes — a `float` `1.0` and an `int` `2` — so the integer branch
is promoted to float (`cvtsi2sd`) and the result is uniformly float, exactly as the reference oracle
does (the old E2015 "different register classes" refusal is gone now that floats are a nameable,
working type). `trunc` reads the float result back to an int for the exit code. With the condition
true the float branch is selected, so `trunc(1.0)` is 1.
```maxon
function main() returns ExitCode
	let c = true
	let x = 1.0 if c else 2
	return trunc(x)
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.with-bools -->
```maxon
function main() returns ExitCode
	let x = true if true else false
	let result = 1 if x else 0
	return result
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.false-path-strings -->
```maxon
function main() returns ExitCode
	let flag = false
	let s = "yes" if flag else "no"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no
```

<!-- test: ternary-expression.nested-in-loop-body -->
A ternary in a `let` binding inside a loop body. The `while`'s carried-variable
token scan (`blockStatementEndIndex`/`opensBlockAt`) walks this body BEFORE it is
parsed, and must NOT count the ternary's `if`/`else` as block openers — they open
no `end`, so counting them over-runs the loop's extent and trips the drift guard.
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 3 'l'
		let x = 5 if i > 0 else 9
		total = total + x
		i = i + 1
	end 'l'
	return total
end 'main'
```
```exitcode
19
```

<!-- test: ternary-expression.nested-in-if-body -->
The same token-scan hazard for an `if` body rather than a loop body.
```maxon
function main() returns ExitCode
	var total = 0
	let c = true
	if c 'g'
		let x = 5 if c else 9
		total = total + x
	end 'g'
	return total
end 'main'
```
```exitcode
5
```

<!-- test: ternary-expression.in-if-condition -->
A ternary inside an `if` CONDITION. The condition is parsed as a value expression,
and the enclosing `if`'s token scan runs over the ternary's `if`/`else` in the
condition — which must not be mistaken for the statement's own block structure.
```maxon
function main() returns ExitCode
	let c = true
	if (5 if c else 3) > 4 'g'
		return 1
	end 'g'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.in-while-condition -->
A ternary inside a `while` CONDITION.
```maxon
function main() returns ExitCode
	var i = 0
	let lim = true
	while i < (3 if lim else 1) 'l'
		i = i + 1
	end 'l'
	return i
end 'main'
```
```exitcode
3
```

<!-- test: ternary-expression.as-match-scrutinee -->
A ternary as a `match` SCRUTINEE — the same token-scan hazard for the `match`
statement's extent.
```maxon
function main() returns ExitCode
	let c = true
	match (1 if c else 2) 'm'
		1 then return 11
		default then return 22
	end 'm'
end 'main'
```
```exitcode
11
```

<!-- test: ternary-expression.owned-arm-moved-read-in-condition -->
The TRUE arm gives an owned binding (moving it out), while the CONDITION — which
runs first but parses second — reads that same binding LIVE. The move state the
true arm left must be rewound before the condition parses, or the condition falsely
sees the binding as already moved (a spurious use-after-move). Selecting the true
arm here, its value transfers exactly once with no leak.

⚠ **`k` IS A `var`, AND THE KEYWORD IS THE WHOLE CASE.** A ternary arm reading an
IMMUTABLE binding CO-OWNS it (⚖ 2026-08-04) and poisons nothing, so with `let k`
there is no move for the rewind to rewind and this case would pass without ever
reaching its own subject. Only a MUTABLE source still moves, so only a `var` puts a
poison between the arm's parse and the condition's.
```maxon
function hasContent(s String) returns bool
	return s.byteLength() > 0
end 'hasContent'

function main() returns ExitCode
	var k = "key{1}"
	let s = k if hasContent(k) else "fallback"
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
key1
```

<!-- test: ternary-expression.in-extension-method-before-sibling -->

Regression: a postfix ternary on a `let` binding inside an extension method
must not confuse the parser's extension-block scanner. The scanner walks
tokens at depth 1 to find function declarations; an inline `if ... else`
must not be counted as a block opener, otherwise sibling methods declared
later in the same extension block fail to register and become "Undefined
method" at every call site.

```maxon
typealias Small = int(0 to 1000)

interface Bounded
	function lo() returns Small
	function hi() returns Small
end 'Bounded'

extension Bounded
	function pickSmaller() returns Small
		let smaller = self.lo() if self.lo() < self.hi() else self.hi()
		return smaller
	end 'pickSmaller'

	function describe() returns Small
		return self.hi() - self.lo()
	end 'describe'
end 'Bounded'

type Pair implements Bounded
	let l as Small
	let h as Small

	function lo() returns Small
		return l
	end 'lo'

	function hi() returns Small
		return h
	end 'hi'

	static function create(l Small, h Small) returns Self
		return Self{l: l, h: h}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(7, h: 12)
	let s = p.pickSmaller()
	let d = p.describe()
	return s + d
end 'main'
```
```exitcode
12
```

<!-- test: ternary-expression.error.unused-loopvar-before-ternary -->

Regression: a postfix ternary appearing downstream of an inner-loop
`try ... otherwise` (which allocates a `try_N.merge` block) inside an
outer loop whose induction variable is **unused** must still surface the
unused-variable diagnostic (E3012), not crash. The self-hosted parser
previously left the inner `try_N.merge` block without a terminator in this
exact CFG shape and tripped `assertAllBlocksTerminated` before E3012 could
be reported. The block-terminator wiring must remain well-formed regardless
of how many `if ... else` merge blocks are allocated later in the same
function body.

```maxon
typealias Idx = int(i64.min to i64.max)
typealias IdxList = List with Idx

function splice(lst IdxList, anchor Idx, after bool)
	for outer in 0 upto 2 'eachBlock'
		var insertPos = -1 as Idx
		for posIdx in 0 upto lst.count() 'eachOpRef'
			let ref = try lst.get(posIdx) otherwise panic("oob")
			if ref == anchor 'foundAnchor'
				insertPos = posIdx
				break
			end 'foundAnchor'
		end 'eachOpRef'
		if insertPos >= 0 'doInsert'
			let target = insertPos + 1 if after else insertPos
			try lst.insert(target, value: 99) otherwise panic("insert oob")
			return
		end 'doInsert'
	end 'eachBlock'
	panic("not found")
end 'splice'

function main() returns ExitCode
	var a = IdxList.create()
	a.append(10)
	a.append(20)
	a.append(30)
	splice(a, anchor: 20, after: false)
	print("pos1={try a.get(1) otherwise -1} pos2={try a.get(2) otherwise -1}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/ternary-expression/ternary-expression.error.unused-loopvar-before-ternary.test:6:6: unused variable: 'outer'
```

<!-- test: ternary-expression.error.type-mismatch -->
```maxon
function main() returns ExitCode
	let x = 10 if true else "hello"
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.type-mismatch.test:3:13: ternary expression type mismatch: true branch is 'int' but false branch is 'String'
```

<!-- test: ternary-expression.error.non-bool-condition -->
```maxon
function main() returns ExitCode
	let x = 10 if 42 else 20
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.non-bool-condition.test:3:13: ternary expression requires a bool condition, got 'int'
```

<!-- test: ternary-expression.error.struct-type-mismatch -->

Both arms are managed (struct) types, so a coarse Bool/Integer/Float/Struct
kind check sees them as equal. The arm-match check must compare the concrete
type names, otherwise `Cat if c else Dog` slips through and crashes in lowering
when the merged result is read with the wrong field set.

```maxon
typealias Integer = int(i64.min to i64.max)

type Cat
	export var legs as Integer

	static function create() returns Self
		return Self{legs: 4}
	end 'create'
end 'Cat'

type Dog
	export var tails as Integer

	static function create() returns Self
		return Self{tails: 1}
	end 'create'
end 'Dog'

function main() returns ExitCode
	let flag = true
	let x = Cat.create() if flag else Dog.create()
	return x.legs
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.struct-type-mismatch.test:22:23: ternary expression type mismatch: true branch is 'Cat' but false branch is 'Dog'
```

<!-- test: ternary-expression.logical-op-condition -->
A `bool or bool` (or `and`/`xor`) expression is a valid ternary condition. The
`and`/`or`/`xor` operators are dual-purpose — bitwise on ints, logical on bools
— so their result type must follow the operand kind: bool operands yield a bool
result, not an integer. When the operand types aren't known until after type
resolution (here `f.a` / `f.b` are field loads), the result stays unresolved and
the condition-is-bool check defers to TypeResolution rather than wrongly reading
as `Integer` and tripping E2028. This mirrors the compiler's own
`useStringLiteralBacking = isStringBacked or isCharBacked` used in a ternary.
```maxon
type Flags
	export let a as bool
	export let b as bool

	export static function make(a bool, b bool) returns Flags
		return Flags{a: a, b: b}
	end 'make'
end 'Flags'

function pick(f Flags) returns ExitCode
	let either = f.a or f.b
	let both = f.a and f.b
	let one = 4 if either else 0
	let two = 1 if both else 0
	return (one + two) as ExitCode
end 'pick'

function main() returns ExitCode
	return pick(Flags.make(true, b: false))
end 'main'
```
```exitcode
4
```

### Ownership across the two arms

A ternary is a **merge of two values whose ownership can differ**: one arm may
yield a value the expression only *borrows* (a field read, a variable, a
parameter), while the other yields a **freshly-owned** one returned by a call.
The merged result carries a single obligation, so the merge must reconcile the
two rather than adopt one arm's and be wrong on the other.

Only the SELECTED arm runs (see *Only the selected arm is evaluated* below), so the
rule is about what the arm that DID run owes the result. When both arms yield owned
values the result is uniformly owned, by one of two routes: an owned TEMPORARY hands
over the one reference it already holds, while a value an IMMUTABLE binding still owns
is CO-OWNED — the arm's edge takes a SECOND reference and the binding keeps its own, so
it stays readable after the merge (⚖ 2026-08-04). A **mutable** source still moves. When
one arm borrows a **String**, the borrow is promoted to an owned copy so the result is
still uniformly owned.

A borrowed **non-text aggregate** (a struct or a boxed union) has no COPY promotion, and it
does not need one: it is CO-OWNED instead, by an `__mm_retain` incref of the shared box —
the same promotion a `var q = p` binding of a borrowed struct parameter gets, and the same
one `return <borrowed aggregate>` gets. Left unpromoted the borrowed box really would be
freed by the merged result AND by the borrow's real owner: a double free, and a leak
(exit 101) whenever the borrowed arm is the one taken. The incref is what balances it —
the result phi's single drop releases a real second reference — so the construct is
CORRECT on either arm rather than refused on both, which is what the parser needs given
that it cannot know which arm a runtime condition will pick. See OPEN #14, and S5 for why
the refusal it originally shipped as was reasoning from a mechanism the tree already had.

Every ownership test below exits `0` only when every allocation is freed (the runtime's
leak check substitutes exit code `101` when any allocation is still live at exit), so the
borrowed-arm cases pin the absence of the leak by RUNNING rather than by rejecting.

<!-- test: ternary-expression.ownership.borrowed-arm-selected -->
### A borrowed-aggregate arm merged with an owned arm is CO-OWNED, not leaked
One arm gives a BORROWED boxed union (`e.kind`, a field read); the other gives an OWNED
one (`remapKind(e.kind)`, a fresh call result). The merged result is owned, so the borrowed
arm is increfed on its own edge: the result's drop releases that second reference and `e`
still owns the box it always owned. Unpromoted this leaked (exit 101) with the borrowed arm
taken, which is the run this case pins — the exit code is the assertion.
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

type Entry
	export var kind as Kind

	static function create(kind Kind) returns Self
		return Self{kind: kind}
	end 'create'
end 'Entry'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let e = Entry.create(Kind.value(3))
	let identity = true
	// `e.kind` is BORROWED; `remapKind(e.kind)` is FRESHLY OWNED.
	let n = record(e.kind if identity else remapKind(e.kind))
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: ternary-expression.ownership.owned-arm-selected -->
### The SAME construct with the condition inverted is balanced identically
The only change from the case above is `identity = false`, which at runtime takes the OWNED
arm and never touches the borrowed one. The promotion is a PARSE-time property of the
construct, not of the runtime path — the parser cannot know the condition, so it increfs the
borrowed edge whichever arm the condition would pick — and the incref sits on that edge, so
the untaken arm costs nothing. (This spelling never leaked even before the fix: it was passing
by dodging the unsound arm. It is the CONTROL, and it must keep exiting `0` and printing the
OWNED arm's answer, which is how a promotion wrongly hoisted out of the arm would show up.)
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

type Entry
	export var kind as Kind

	static function create(kind Kind) returns Self
		return Self{kind: kind}
	end 'create'
end 'Entry'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let e = Entry.create(Kind.value(3))
	let identity = false
	let n = record(e.kind if identity else remapKind(e.kind))
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: ternary-expression.ownership.owned-arm-first -->
### The owned arm may be either arm — here the TRUE arm is the fresh one
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let k = Kind.value(3)
	let remapped = false
	let n = record(remapKind(k) if remapped else k)
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: ternary-expression.ownership.both-arms-owned -->
### Both arms freshly owned — the arm not selected is still released
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let k = Kind.value(3)
	let c = true
	let n = record(remapKind(k) if c else remapKind(k))
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: ternary-expression.ownership.chained-arms -->
### A chained ternary co-owns at the outer merge that borrows an aggregate
The chain right-associates to `e.kind if a else (remapKind(e.kind) if b else remapKind(e.kind))`.
The inner ternary merges two OWNED arms; the OUTER merge pairs the borrowed `e.kind` with the
inner's owned result, so the borrowed edge is increfed at the outer merge and the inner result
is moved in at its own. Two merges, one rule, and the box is freed exactly once.
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

type Entry
	export var kind as Kind

	static function create(kind Kind) returns Self
		return Self{kind: kind}
	end 'create'
end 'Entry'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let e = Entry.create(Kind.value(3))
	let a = true
	let b = false
	let n = record(e.kind if a else remapKind(e.kind) if b else remapKind(e.kind))
	print("{n}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

<!-- test: ternary-expression.ownership.immutable-binding-arms-are-co-owned -->
### An arm reading an IMMUTABLE binding CO-OWNS it — the binding stays readable
⭐⭐ **THE MERGE ASKS THE SAME QUESTION `try … otherwise <binding>` ALREADY ANSWERED (⚖ 2026-08-04,
⚖ 2026-08-12).** An arm whose value IS an immutable binding's own value hands the phi a reference
the binding still owes a drop for, so the phi must take a SECOND one — exactly what
`Parser.transferFallbackToPhi` does on a value `try`'s error edge, and exactly what a durable sink
does (`Parser.storeOwnedValueIntoDurableSink`). Poisoning the source instead made `let` behave one
way at a rebind (an ALIAS, both names live) and the opposite way one construct over, so this pair of
ternaries — each reading both bindings, in opposite order — was refused E3102 on the second line
while the identical `let receiversOwn = programsOwn` was accepted. Both bindings are read AFTER both
merges here, and each box is released exactly once, so the leak gate is the assertion.
```maxon
function describe(spelling String, first bool) returns String
	let programsOwn = "the `type {spelling}` this program declares"
	let librarys = "the `{spelling}` the standard library declares"
	let receiversOwn = programsOwn if first else librarys
	let theOther = librarys if first else programsOwn
	return "{receiversOwn} / {theOther} / {programsOwn}"
end 'describe'

function main() returns ExitCode
	print(describe("Array", first: true))
	print("\n")
	print(describe("Array", first: false))
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the `type Array` this program declares / the `Array` the standard library declares / the `type Array` this program declares
the `Array` the standard library declares / the `type Array` this program declares / the `type Array` this program declares
```

<!-- test: ternary-expression.ownership.immutable-arm-merged-with-a-fresh-temp -->
### One immutable-binding arm, one FRESH arm — one edge increfs, the other transfers
The two edges owe the phi its single reference by different routes: the binding's edge takes a
SECOND reference (the binding keeps its own and drops it at scope exit), while the fresh
interpolation's edge hands over the one it already holds. Getting either backwards is visible
here — increfing the temp leaks and moving the binding double-frees — and the binding is read
after the merge on both runs, so the co-owning half is exercised whichever arm the condition picks.
```maxon
function pick(tag String, fresh bool) returns String
	let held = "held {tag} padded out long enough to be a real heap allocation"
	let chosen = "fresh {tag} padded out long enough to be a real heap allocation" if fresh else held
	return "{chosen} | {held}"
end 'pick'

function main() returns ExitCode
	print(pick("a", fresh: true))
	print("\n")
	print(pick("b", fresh: false))
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fresh a padded out long enough to be a real heap allocation | held a padded out long enough to be a real heap allocation
held b padded out long enough to be a real heap allocation | held b padded out long enough to be a real heap allocation
```

<!-- test: ternary-expression.ownership.immutable-arm-inside-a-loop -->
### The co-owning arm lifts a LOOP refusal that only a move ever earned
⭐ **A REFUSAL WHOSE PREMISE WAS THE MOVE.** `poisonOwningBinding` refuses a move of a binding declared
outside the innermost loop — the back edge would re-move it next iteration and a `break` would leave it
live on the normal exit, neither of which the join model reaches. An arm that CO-OWNS moves nothing, so
none of that reasoning applies: the incref is taken on the arm's edge inside the loop and the phi's drop
falls inside the loop too, balanced per iteration, and `base` keeps its one scope-exit drop. The `var`
spelling of the same program is still refused by that E2015 — the refusal is intact, it simply no longer
stands over a program that never moved anything. Three iterations, one of them taking the fresh arm.
```maxon
function main() returns ExitCode
	let base = "base {1} padded out long enough to be a real heap allocation"
	var i = 0
	var total = 0
	while i < 3 'l'
		let pick = i > 0
		let chosen = base if pick else "alt {2} padded out long enough to be a real heap allocation"
		total = total + chosen.byteLength()
		i = i + 1
	end 'l'
	print("{total}|{base}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
173|base 1 padded out long enough to be a real heap allocation
```

<!-- test: ternary-expression.error.mutable-binding-arm-still-moves -->
### A MUTABLE binding's arm still MOVES — the control for the case above
⚠ **THE BOUNDARY, NOT AN OVERSIGHT.** Co-ownership rests on neither name being writable
(⚖ 2026-08-04): a `var` source can be written through after the merge, so a second name for its
value could watch it change, and the arm therefore keeps the MOVE it always had. Without this case
the co-owning arm above could be widened to every source and nothing in the suite would notice.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x} padded out long enough to be a real heap allocation"
end 'build'

function main() returns ExitCode
	var t = build(1)
	let c = true
	let u = t if c else build(2)
	print(u)
	print(t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:8: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: ternary-expression.error.owned-string-arm-undeclared-call -->
### An owned-String arm merged with an undeclared-call arm still reports the call error
The owned interpolation arm makes the result phi owned, so the false arm is routed through the
same borrowed-give promotion. Its value is UNRESOLVED (a call no file declares), which is not a
struct/union to refuse and not a String to promote — so the merge must DEFER, not crash: the
undeclared call is a `E3004` from semantic analysis, and a `E3004` program must surface that, never
a parser panic. (Regression guard: refusing the borrowed-aggregate merge must not swallow this arm.)
```maxon
function pick(x int, c bool) returns String
	return "{x}" if c else undefinedThing()
end 'pick'

function main() returns ExitCode
	let s = pick(5, c: true)
	return 0
end 'main'
```
```maxoncstderr
error E3004: specs/fragments/ternary-expression/ternary-expression.error.owned-string-arm-undeclared-call.test:3:25: call to undefined function 'undefinedThing'
```

<!-- disabled-test: ternary-expression.ownership.result-stored-in-container -->
<!-- needs a generic container typealias `Array with Kind` (E2015 "a typealias over 'identifier'"), unbuilt in shv2 — its own generics rung. (It ALSO merges a borrowed-aggregate arm with an owned one, which is now the clean E2015 reject of OPEN #14; the borrowed+owned merge stays deferred to cross-call consume at P1.5, so this shape needs BOTH generics and P1.5 to run.) -->
### The merged result can be stored, and is owned exactly once when it is
This is the shape that found the defect: a table fold choosing between an
already-interned entry and a freshly remapped one, then storing the winner.
```maxon
typealias Id = int(0 to 1000)

union Kind
	none
	value(inner Id)
end 'Kind'

typealias KindArray = Array with Kind

type Entry
	export var kind as Kind

	static function create(kind Kind) returns Self
		return Self{kind: kind}
	end 'create'
end 'Entry'

function remapKind(k Kind) returns Kind
	return match k 'r'
		none gives Kind.none
		value(inner) gives Kind.value(inner + 1)
	end 'r'
end 'remapKind'

function record(k Kind) returns Id
	return match k 'r'
		none gives 0
		value(inner) gives inner
	end 'r'
end 'record'

function main() returns ExitCode
	let e = Entry.create(Kind.value(3))
	var out = KindArray.create()
	let identity = true
	out.push(e.kind if identity else remapKind(e.kind))
	print("{record(try out.get(0) otherwise Kind.none)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

### Only the selected arm is evaluated

A ternary **chooses before it evaluates**. The arm the condition does not select is never
run — not its calls, not its arithmetic, not its allocations. This is the whole point of the
form: it is what lets a ternary act as a *guard*.

```maxon
return small if fits else (big as Small)   // never range-check-panics when it doesn't fit
```

An eagerly-evaluated ternary computes the same answer as a lazy one whenever both arms are
total, so no value test can tell them apart. The evidence therefore has to be **observational**:
a side effect that provably did not happen. Two oracles are used below — a counter in a global
(exact), and an out-of-range cast, which range-check-panics the process if it is ever reached.
(Integer `/` no longer serves as this oracle: a possibly-zero divide is a throwing operation, so
it cannot appear bare in an arm — it would need a `try`, which would swallow the very fault the
oracle relies on.)

<!-- test: ternary-expression.elision.guard-actually-guards -->
### A guarded out-of-range cast does not run
The idiomatic use of the form. If the unselected arm ran, the out-of-range cast would die with
a range-check panic instead of answering.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 10)

function clampOrGuard(useDefault bool, big Integer) returns Integer
	// `big as Small` range-check-panics for big > 10; it sits in the arm that
	// `useDefault` does NOT select, so it must never run.
	return 5 if useDefault else (big as Small)
end 'clampOrGuard'

function main() returns ExitCode
	let guarded = clampOrGuard(true, big: 1000) // the cast is in the arm NOT selected
	let direct = clampOrGuard(false, big: 7)
	return (guarded + direct) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: ternary-expression.elision.false-arm-not-evaluated -->
### The false arm is not evaluated when the condition is true
```maxon
var sideEffectCount = 0

function track(result Integer) returns Integer
	sideEffectCount = sideEffectCount + 1
	return result
end 'track'

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 7 if true else track(9)
	if x != 7 'v'
		return 99
	end 'v'
	return sideEffectCount
end 'main'
```
```exitcode
0
```

<!-- test: ternary-expression.elision.true-arm-not-evaluated -->
### The true arm is not evaluated when the condition is false
```maxon
var sideEffectCount = 0

function track(result Integer) returns Integer
	sideEffectCount = sideEffectCount + 1
	return result
end 'track'

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let x = track(9) if false else 7
	if x != 7 'v'
		return 99
	end 'v'
	return sideEffectCount
end 'main'
```
```exitcode
0
```

<!-- test: ternary-expression.elision.selected-arm-is-evaluated-exactly-once -->
### The selected arm IS evaluated — exactly once, not zero times and not twice
The counterpart direction: elision must not become elimination.
```maxon
var sideEffectCount = 0

function track(result Integer) returns Integer
	sideEffectCount = sideEffectCount + 1
	return result
end 'track'

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let x = track(4) if true else track(9)
	if x != 4 'v'
		return 99
	end 'v'
	return sideEffectCount
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.elision.chained-evaluates-only-the-winner -->
### In a chain, every arm but the winner is skipped
```maxon
var sideEffectCount = 0

function track(result Integer) returns Integer
	sideEffectCount = sideEffectCount + 1
	return result
end 'track'

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	// Right-associates: track(1) if false else (track(2) if true else track(3))
	let x = track(1) if false else track(2) if true else track(3)
	if x != 2 'v'
		return 99
	end 'v'
	return sideEffectCount
end 'main'
```
```exitcode
1
```

<!-- test: ternary-expression.elision.condition-always-evaluated -->
### The condition itself is always evaluated, exactly once
```maxon
var sideEffectCount = 0

function pick() returns bool
	sideEffectCount = sideEffectCount + 1
	return false
end 'pick'

function main() returns ExitCode
	let x = 1 if pick() else 2
	if x != 2 'v'
		return 99
	end 'v'
	return sideEffectCount
end 'main'
```
```exitcode
1
```

### The arms move, and every fact about them must move with them

The true arm is parsed before anything reveals it is an arm — the `if` only arrives after it —
so it is emitted into the unconditional path and then **relocated** into the true branch. That
relocation is the source of two whole classes of defect, and both are questions the parser must
answer about the arm rather than about the tokens around it.

**Where a value LIVES.** The parser caches, per variable, the SSA value it last read and the
block that value was defined in; a later read in that same block reuses it instead of reloading.
Relocate the defining op and that cache names a block the value has left. The repair must key on
**value provenance** — *which values did the ops that moved define?* — because the only other
available question, *which variable names are new since the arm began?*, is a different one: a
self-field alias is a **pre-existing name whose value is rewritten in place** when a call
invalidates it, so name-novelty cannot see it. `n if n > threshold else base` in a method that
had just called another method emitted a comparison in the entry block against a field load that
had moved into the true arm.

**What a value CARRIES.** The merged result is a new temp holding a copy of the winning arm, so
every fact the arms carried is either merged into it or dropped — and a fact that is silently
neither is a miscompile. The type and the concrete struct name are merged and the arms must
agree on them. **A function signature is merged the same way**: it lives on the variable, not on
the `var_ref` the merge is read back through, so without merging it there is nothing left to
recover it from. A capture **environment** is the one fact the merge cannot carry at all, and
that is not a silent drop either — see `first-class-functions.md`.

<!-- test: ternary-expression.self-field-in-condition-and-arm -->
### A self-field read in BOTH the condition and an arm reloads in each
`pick` calls another method first, which invalidates the cached `self.n`. The arm's reload is
relocated into the true branch; the condition, which runs unconditionally, must then get a
reload of its own rather than reusing the one that moved. Pinning the ANSWER, not just that it
compiles: the earlier compiler answered 0 here, and the one after it crashed.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function bump() returns Integer
		return self.n + 1
	end 'bump'

	export function pick(threshold Integer) returns Integer
		let base = self.bump()
		return n if n > threshold else base
	end 'pick'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(5)
	return c.pick(1)
end 'main'
```
```exitcode
5
```

<!-- test: ternary-expression.self-field-in-condition-and-arm-false-path -->
### The same shape, taking the false arm
The false arm's value must win when the condition is false, which proves the condition's own
reload is a real read of the field and not a constant that happened to agree.
```maxon
typealias Integer = int(i64.min to i64.max)

type Counter
	export var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function bump() returns Integer
		return self.n + 1
	end 'bump'

	export function pick(threshold Integer) returns Integer
		let base = self.bump()
		return n if n > threshold else base
	end 'pick'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(5)
	return c.pick(99)
end 'main'
```
```exitcode
6
```

<!-- test: ternary-expression.function-arms -->
### Both arms are functions, and the result is callable
The merged result's SIGNATURE has to survive the merge. It is read back through a `var_ref`,
which carries only a kind, so if the ternary does not put the signature on the merged binding
nothing downstream can recover it and `let h = ...` fails to compile.
```maxon
typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function trp(n Integer) returns Integer
	return n * 3
end 'trp'

function main() returns ExitCode
	let c = 1
	let h = dbl if c > 0 else trp
	return h(21)
end 'main'
```
```exitcode
42
```

<!-- test: ternary-expression.function-arms-called-directly -->
### A parenthesized ternary of functions is callable in place
The same signature, reaching the other consumer: the `(` suffix that turns a function VALUE
into a call.
```maxon
typealias Integer = int(i64.min to i64.max)

function dbl(n Integer) returns Integer
	return n * 2
end 'dbl'

function trp(n Integer) returns Integer
	return n * 3
end 'trp'

function main() returns ExitCode
	let c = 0
	return (dbl if c > 0 else trp)(14)
end 'main'
```
```exitcode
42
```

<!-- test: ternary-expression.error.function-arm-signature-mismatch -->
### The two function arms must have the SAME signature
The merged slot holds one signature and either arm may end up in it, so a caller checked
against one arm's signature could be handed the other's.
```maxon
typealias Integer = int(i64.min to i64.max)

function unary(n Integer) returns Integer
	return n * 2
end 'unary'

function binary(a Integer, b Integer) returns Integer
	return a + b
end 'binary'

function main() returns ExitCode
	let c = 1
	let h = unary if c > 0 else binary
	return h(21)
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.function-arm-signature-mismatch.test:14:16: ternary expression type mismatch: true branch is 'fn(Integer) returns Integer' but false branch is 'fn(Integer, Integer) returns Integer'
```

<!-- test: ternary-expression.error.function-arm-signature-mismatch-forward-ref -->
### Forward-referenced function arms are checked whole-program
The helpers are declared BELOW `main`, so at parse the arms' parameter types are not yet in any
registry the parser holds — signature agreement cannot be checked there. It is drained WHOLE-PROGRAM,
after every file's signatures are merged, so a forward-referenced mismatch is caught exactly as a
same-file backward one (and a cross-file one) is. Without this the merge silently adopted the first
arm's signature and `h(21)` ran `binary` with one argument, reading an undefined second.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let c = 1
	let h = unary if c > 0 else binary
	return h(21)
end 'main'

function unary(n Integer) returns Integer
	return n * 2
end 'unary'

function binary(a Integer, b Integer) returns Integer
	return a + b
end 'binary'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.function-arm-signature-mismatch-forward-ref.test:6:16: ternary expression type mismatch: true branch is 'fn(Integer) returns Integer' but false branch is 'fn(Integer, Integer) returns Integer'
```
