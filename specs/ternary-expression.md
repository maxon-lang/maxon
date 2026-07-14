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
error E2028: specs/fragments/ternary-expression/ternary-expression.error.type-mismatch.test:3:13: ternary expression type mismatch: true branch is 'Integer' but false branch is 'Struct'
```

<!-- test: ternary-expression.error.non-bool-condition -->
```maxon
function main() returns ExitCode
	let x = 10 if 42 else 20
	return 0
end 'main'
```
```maxoncstderr
error E2028: specs/fragments/ternary-expression/ternary-expression.error.non-bool-condition.test:3:13: ternary expression requires a bool condition, got 'Integer'
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

Both arms of a ternary are **evaluated**, and only one is selected. A fresh
allocation produced by the arm that is *not* selected is therefore still live,
and is released when the enclosing scope ends. The rule the compiler follows is
to **normalize to owned**: the result retains its own reference to whichever arm
won, and every arm keeps the reference it already held. That is correct whichever
arm the condition picks.

These tests exit `0` only when every allocation is freed — the runtime's leak
check substitutes exit code `101` when any allocation is still live at exit.

<!-- test: ternary-expression.ownership.borrowed-arm-selected -->
### Borrowed arm selected; the other arm's fresh allocation is still released
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
### Owned arm selected; its reference transfers exactly once
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
### A chained ternary reconciles every arm, not just the outermost pair
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

<!-- test: ternary-expression.ownership.result-stored-in-container -->
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
return 0 if d == 0 else (n / d)   // never divides when d is 0
```

An eagerly-evaluated ternary computes the same answer as a lazy one whenever both arms are
total, so no value test can tell them apart. The evidence therefore has to be **observational**:
a side effect that provably did not happen. Two oracles are used below — a counter in a global
(exact), and a division by a runtime zero, which faults the process if it is ever reached.

<!-- test: ternary-expression.elision.guard-actually-guards -->
### A guarded division does not divide
The idiomatic use of the form. If the unselected arm ran, this would die with
`panic: integer divide by zero` instead of answering.
```maxon
typealias Integer = int(i64.min to i64.max)

function safeDiv(n Integer, d Integer) returns Integer
	return 0 if d == 0 else (n / d)
end 'safeDiv'

function main() returns ExitCode
	let guarded = safeDiv(100, d: 0) // the divide is in the arm NOT selected
	let divided = safeDiv(100, d: 5)
	return (guarded + divided) as ExitCode
end 'main'
```
```exitcode
20
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
