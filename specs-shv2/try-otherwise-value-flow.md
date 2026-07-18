---
feature: try-otherwise-value-flow
status: stable
keywords: [try, otherwise, regalloc, codegen, ssa, block-args]
category: error-handling
---

# Try-Otherwise Value Flow

## Documentation

`try CALL otherwise FALLBACK` lowers to a conditional branch on the call's
error flag: the success path uses the call's result, the fallback path
substitutes `FALLBACK`. The parser emits `cmp ne, errorFlag, 0` followed by
`condBr cond, then=fallbackBlock, else=successBlock` — i.e. the success
path is on the **else** edge of the conditional branch.

These tests pin down value flow through that lowering shape end-to-end.
A regression here typically points at the SSA-destruction / edge-copy
machinery in the register allocator: the call result must reach the merge
block via a parallel copy on the success edge, even when that edge ends up
emitted as a conditional jump's target after layout fall-through elimination.

## Tests

<!-- test: try-otherwise-value-flow.success-path-returns-call-result -->
The call returns `10` and does not throw. `try` evaluates to the call's
result, not the fallback. Stresses the success edge of `condBr` (which is
the `else` edge) — the call result must flow through to the merge block.
```maxon
enum E
	bad
end 'E'

function double(x ExitCode) returns ExitCode throws E
	return x * 2
end 'double'

function main() returns ExitCode
	let v = try double(5) otherwise 99
	return v
end 'main'
```
```exitcode
10
```

<!-- test: try-otherwise-value-flow.fallback-path-returns-otherwise-value -->
The call throws, so `try` evaluates to the fallback `99` instead of the
call's (default) primary value. Stresses the error edge of `condBr`.
```maxon
enum E
	bad
end 'E'

function alwaysThrows(x ExitCode) returns ExitCode throws E
	if x >= 0 'always'
		throw E.bad
	end 'always'
	return x
end 'alwaysThrows'

function main() returns ExitCode
	let v = try alwaysThrows(5) otherwise 99
	return v
end 'main'
```
```exitcode
99
```

<!-- test: try-otherwise-value-flow.identity-call-success -->
Identity call result reaches the merge block — confirms the value flow is
not specific to a multiplication or any particular arithmetic expression.
```maxon
enum E
	bad
end 'E'

function ident(x ExitCode) returns ExitCode throws E
	return x
end 'ident'

function main() returns ExitCode
	let v = try ident(7) otherwise 99
	return v
end 'main'
```
```exitcode
7
```

<!-- test: try-otherwise-value-flow.propagation-success -->
Propagation form: a throwing helper wraps `try CALL` without an `otherwise`.
A successful inner call returns its value; the wrapper then appears in the
outer `try ... otherwise` site. Exercises the propagation lowering shape
(error path re-publishes the flag and returns a default) on the success
branch.
```maxon
enum E
	bad
end 'E'

function double(x ExitCode) returns ExitCode throws E
	return x * 2
end 'double'

function wrap() returns ExitCode throws E
	let v = try double(5)
	return v
end 'wrap'

function main() returns ExitCode
	let v = try wrap() otherwise 99
	return v
end 'main'
```
```exitcode
10
```

<!-- test: try-otherwise-value-flow.nested-try-in-arg -->
Nested `try ... otherwise X` in an argument position: the inner try's
result must be visible to the outer call's argument list. Reproduces the
`unresolved value name '$tN'` parser binding bug.
```maxon
enum E
	bad
end 'E'

function getString(i ExitCode) returns ExitCode throws E
	if i == 0 'zero'
		throw E.bad
	end 'zero'
	return 42
end 'getString'

function consume(x ExitCode) returns ExitCode throws E
	if x == 0 'empty'
		throw E.bad
	end 'empty'
	return x
end 'consume'

function main() returns ExitCode
	let n = try consume(try getString(1) otherwise 0) otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- disabled-test: try-otherwise-value-flow.chained-method-on-try-receiver -->
<!-- [later:Array] StringArray + get/set/push -->
Regression guard: a method call chained onto a `(try CALL otherwise diverge)`
receiver, followed by another statement in the same block. The receiver's try
moves control flow onto its merge block; the chained method call (and its arg
parse) must emit there, not back on the pre-try block. Previously the method's
argument parse re-seeded the emit block to the statement's entry block, leaving
the receiver's try-merge block unterminated (assertAllBlocksTerminated panic).
Two such statements in one block expose it — the second try overwrote the
entry block's terminator, orphaning the first try's merge.
```maxon
function swapFirstTwo(rows StringArray, doSwap bool)
	if doSwap 'swap'
		let tmp = (try rows.get(0) otherwise panic("oob")).clone()
		try rows.set(0, value: try rows.get(1) otherwise panic("oob")) otherwise panic("oob")
		try rows.set(1, value: tmp) otherwise panic("oob")
	end 'swap'
end 'swapFirstTwo'

function main() returns ExitCode
	var rows = StringArray.create()
	rows.push("a")
	rows.push("b")
	swapFirstTwo(rows, doSwap: true)
	let first = try rows.get(0) otherwise panic("oob")
	return first.byteLength() as ExitCode
end 'main'
```
```exitcode
1
```


<!-- disabled-test: try-otherwise-value-flow.parenthesized-try-in-if-condition -->
<!-- [later:Array] BoolArray + get/push -->
A parenthesized `(try CALL otherwise VALUE)` used as an `if` condition. The
bare-`try`-as-if-condition form (`if try f() otherwise … 'l'`) lets the
if-parser own the success/error split and never consumes an `otherwise`, but
that special-case applies only to the condition's DIRECT, unparenthesized try.
Once wrapped in parens, the try is a self-contained boolean value expression
that must consume its own `otherwise` — so this must parse without an
"otherwise requires try" (E3058) error.
```maxon
typealias Tally = int(0 to 125)
typealias BoolArray = Array with bool

function countSet(bits BoolArray) returns Tally
	var n = 0
	for i in 0 upto bits.count() 'each'
		if (try bits.get(i) otherwise false) 'set'
			n = n + 1
		end 'set'
	end 'each'
	return n as Tally
end 'countSet'

function main() returns ExitCode
	var b = BoolArray.create()
	b.push(true)
	b.push(false)
	b.push(true)
	b.push(true)
	return countSet(b)
end 'main'
```
```exitcode
3
```


<!-- disabled-test: try-otherwise-value-flow.try-in-call-arg-in-if-condition -->
<!-- [later:Array] OctetArray + get/push -->
A `try CALL otherwise VALUE` used as a CALL ARGUMENT inside an `if` condition.
The bare-`try`-as-if-condition special-case (parseTryExpression's `asIfCond`
early return, which leaves `otherwise` unconsumed) applies only to the
condition's DIRECT top-level try. A try nested inside a call argument is a
self-contained value that must consume its own `otherwise` — so this must parse
without an "otherwise requires try" (E3058) error. Mirrors the compiler's own
`validateEscapes` (`if ... not isHexDigitByte(try bytes.get(i + 2) otherwise 0)`).
```maxon
typealias Octet = int(0 to 255)
typealias OctetArray = Array with Octet

function isBig(b Octet) returns bool
	return b > 100
end 'isBig'

function check(bytes OctetArray, i Octet) returns ExitCode
	if isBig(try bytes.get(i) otherwise 0) 'big'
		return 1
	end 'big'
	return 0
end 'check'

function main() returns ExitCode
	var a = OctetArray.create()
	a.push(200)
	return check(a, i: 0)
end 'main'
```
```exitcode
1
```


<!-- disabled-test: try-otherwise-value-flow.field-then-method-on-try-with-break-receiver -->
<!-- [later:Array] RowArray + get/push -->
Regression guard: a FIELD access (not a method call) chained onto a
`(try CALL otherwise break)` receiver, followed by a further method call —
`(try pairs.get(j) otherwise break).label.count()` inside a loop. The receiver's
`otherwise break` moves control flow onto the try's merge block, where the
receiver value is defined; the chained `.label` fieldLoad must emit on THAT
merge block, not the pre-try block. Previously the postfix field-load arm emitted
into its stale `block` parameter while the method-call arm correctly used
`currentBlock`, so the fieldLoad referenced the receiver value before its
defining op, leaving the producer type unresolved and crashing the cmp operand
typing (`pickOperandType` panic). Mirrors the compiler's own
`sortSanitizedPairsByLengthDesc`.
```maxon
type Row
	export var label as String

	export static function create(label String) returns Row
		return Self{label: label}
	end 'create'
end 'Row'

typealias RowArray = Array with Row

function longestLen(rows RowArray) returns ExitCode
	var best = 0
	var i = 0
	while i < rows.count() 'scan'
		let len = (try rows.get(i) otherwise break).label.count()
		if len > best 'bigger'
			best = len
		end 'bigger'
		i = i + 1
	end 'scan'
	return best as ExitCode
end 'longestLen'

function main() returns ExitCode
	var rows = RowArray.create()
	rows.push(Row.create("ab"))
	rows.push(Row.create("abcd"))
	rows.push(Row.create("a"))
	return longestLen(rows)
end 'main'
```
```exitcode
4
```
