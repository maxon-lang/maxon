---
feature: register-allocator
status: stable
keywords: [regalloc, registers, spilling, codegen]
category: dev
---

## Documentation

These tests exercise register allocation with progressively increasing difficulty. They are organized into six levels:

1. **Basic Value Tracking** — Single values flowing to return. A trivial allocator can pass these.
2. **Multiple Values and Reuse** — More than one live value at a time; values reused across expressions.
3. **Register Pressure and Spilling** — More live values than physical registers, forcing spills to stack.
4. **Function Calls and Fixed Register Constraints** — Caller-saved register preservation, IDIV constraints (RAX/RDX), parameter passing.
5. **Control Flow and Loops** — Values live across branches, loop back-edges, and nested control flow.
6. **Advanced Scenarios** — Combined challenges: recursion, deep expressions, mixed int/float, long live ranges, parallel copy.

## Tests

⚠ **PORT NOTE (BATCH29/A3a).** `status:` reads `stable` here and `selfhosted` in `/specs`: that frontmatter names the runner that owns the file, and the owner here is shv2. Its `/specs` twin stays `status: selfhosted`: 55 of its 60 cases fail the bootstrap on those blocks alone.

⚠ **PORT NOTE (BATCH29/A3a).** The `/specs` original carries 165 `RequiredIR:<target>` block(s) in v1's single-section dump format. None survives the port: shv2's spec parser has no `RequiredIR` arm, so every one of them would be read by nobody while reading as coverage — the shape this batch exists to remove, and `SpecParser.isUnimplementedFenceOpen` now refuses the fence rather than walking past it. What pins the emitted code here is each case's MINTED FRAGMENT GOLDEN, which records what THIS compiler emits rather than what v1 did. The `/specs` copy keeps its blocks and stays `status: selfhosted`; its `status-reason:` names this file.

### Level 1: Basic Value Tracking

<!-- test: int-constant -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```
<!-- test: int-var-roundtrip -->
```maxon
function main() returns ExitCode
	let x = 99
	return x
end 'main'
```
```exitcode
99
```
<!-- test: int-add-constants -->
```maxon
function main() returns ExitCode
	return 30 + 12
end 'main'
```
```exitcode
42
```
### Level 2: Multiple Values and Reuse

<!-- test: int-two-vars-add -->
```maxon
function main() returns ExitCode
	let a = 30
	let b = 12
	return a + b
end 'main'
```
```exitcode
42
```
<!-- test: int-var-reuse-twice -->
```maxon
function main() returns ExitCode
	let x = 21
	return x + x
end 'main'
```
```exitcode
42
```
<!-- test: int-chained-assignments -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = a + 5
	let c = b + 7
	let d = c + 20
	return d
end 'main'
```
```exitcode
42
```
<!-- test: int-reassignment -->
```maxon
function main() returns ExitCode
	var x = 100
	let y = x - 80
	x = 22
	return x + y
end 'main'
```
```exitcode
42
```
### Level 3: Register Pressure and Spilling

<!-- test: int-six-vars-alive -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	return a + b + c + d + e + f
end 'main'
```
```exitcode
21
```
<!-- test: int-ten-vars-alive -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	return a + b + c + d + e + f + g + h + i + j
end 'main'
```
```exitcode
55
```
<!-- test: int-sixteen-vars-spill -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	let k = 11
	let l = 12
	let m = 13
	let n = 14
	let o = 15
	let p = 16
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p) and 125
end 'main'
```
```exitcode
8
```
<!-- test: int-twenty-vars-heavy-spill -->
```maxon
function main() returns ExitCode
	let a = 1
	let b = 2
	let c = 3
	let d = 4
	let e = 5
	let f = 6
	let g = 7
	let h = 8
	let i = 9
	let j = 10
	let k = 11
	let l = 12
	let m = 13
	let n = 14
	let o = 15
	let p = 16
	let q = 17
	let r = 18
	let s = 19
	let t = 20
	return (a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r + s + t) and 125
end 'main'
```
```exitcode
80
```
<!-- test: int-interleaved-lifetimes -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	let ab = a + b
	let c = 30
	let d = 40
	let cd = c + d
	let e = 50
	let f = 60
	let ef = e + f
	let result = ab + cd + ef
	return result and 125
end 'main'
```
```exitcode
80
```
<!-- test: int-parallel-accumulation -->
```maxon
function main() returns ExitCode
	var sum1 = 0
	var sum2 = 0
	var sum3 = 0
	sum1 = sum1 + 10
	sum2 = sum2 + 20
	sum3 = sum3 + 30
	sum1 = sum1 + 5
	sum2 = sum2 + 10
	sum3 = sum3 + 15
	return sum1 + sum2 + sum3
end 'main'
```
```exitcode
90
```
### Level 4: Function Calls and Fixed Register Constraints

<!-- test: int-call-preserves-value -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getForty() returns Integer
	return 40
end 'getForty'

function main() returns ExitCode
	let x = 2
	let y = getForty()
	return x + y
end 'main'
```
```exitcode
42
```
<!-- test: int-multiple-calls-preserve -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getTen() returns Integer
	return 10
end 'getTen'

function getTwo() returns Integer
	return 2
end 'getTwo'

function main() returns ExitCode
	let a = 5
	let b = getTen()
	let c = 7
	let d = getTwo()
	return a + b + c + d
end 'main'
```
```exitcode
24
```
<!-- test: int-call-result-used-later -->
```maxon

typealias Integer = int(i64.min to i64.max)

function compute() returns Integer
	return 100
end 'compute'

function main() returns ExitCode
	let a = compute()
	let b = compute()
	return (a + b) and 125
end 'main'
```
```exitcode
72
```
<!-- test: int-division-fixed-regs -->
```maxon
function main() returns ExitCode
	let a = 126
	let b = 3
	return a / b
end 'main'
```
```exitcode
42
```

<!-- test: int-division-preserves-other-values -->
```maxon
function main() returns ExitCode
	let x = 10
	let a = 84
	let b = 2
	let quotient = a / b
	return quotient - x
end 'main'
```
```exitcode
32
```

<!-- test: int-function-with-params -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(30, b: 12)
end 'main'
```
```exitcode
42
```
<!-- test: int-mov-reg-reg-32bit -->
```maxon

typealias Integer = int(i64.min to i64.max)

function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	let x = 20
	let y = 22
	return add(y, b: x)
end 'main'
```
```exitcode
42
```
### Level 5: Control Flow and Loops

<!-- test: int-if-else-simple -->
```maxon
function main() returns ExitCode
	let x = 10
	if x == 10 'check'
		return 42
	end 'check' else 'other'
		return 0
	end 'other'
end 'main'
```
```exitcode
42
```
<!-- test: int-if-else-value-survives-branch -->
```maxon
function main() returns ExitCode
	let base = 40
	let cond = 1
	var extra = 0
	if cond == 1 'check'
		extra = 2
	end 'check' else 'other'
		extra = 100
	end 'other'
	return base + extra
end 'main'
```
```exitcode
42
```
<!-- test: int-while-loop-counter -->
```maxon
function main() returns ExitCode
	var i = 0
	while i < 42 'loop'
		i = i + 1
	end 'loop'
	return i
end 'main'
```
```exitcode
42
```
<!-- test: int-while-loop-accumulator -->
```maxon
function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 10 'loop'
		sum = sum + i
		i = i + 1
	end 'loop'
	return sum mod 256
end 'main'
```
```exitcode
45
```
<!-- test: int-while-loop-multiple-accumulators -->
```maxon
function main() returns ExitCode
	var even_sum = 0
	var odd_sum = 0
	var count = 0
	var i = 0
	while i < 20 'loop'
		if i mod 2 == 0 'even'
			even_sum = even_sum + i
			count = count + 1
		end 'even' else 'odd'
			odd_sum = odd_sum + i
		end 'odd'
		i = i + 1
	end 'loop'
	return (even_sum + odd_sum + count) and 125
end 'main'
```
```exitcode
72
```
<!-- test: int-nested-if-in-loop -->
```maxon
function main() returns ExitCode
	var result = 0
	var i = 1
	while i <= 10 'loop'
		if i <= 5 'first'
			result = result + i
		end 'first' else 'second'
			result = result + i * 2
		end 'second'
		i = i + 1
	end 'loop'
	return result mod 256
end 'main'
```
```exitcode
95
```
<!-- test: int-nested-loops -->
```maxon
function main() returns ExitCode
	var total = 0
	var i = 0
	while i < 5 'outer'
		var j = 0
		while j < 4 'inner'
			total = total + 1
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return total
end 'main'
```
```exitcode
20
```
<!-- test: int-nested-loops-with-outer-var -->
```maxon
function main() returns ExitCode
	var total = 0
	var i = 1
	while i <= 5 'outer'
		var j = 1
		while j <= i 'inner'
			total = total + 1
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return total
end 'main'
```
```exitcode
15
```
<!-- test: int-loop-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 5 'loop'
		sum = sum + double(i)
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
20
```
### Level 6: Advanced Scenarios

<!-- test: int-nested-expressions-deep -->
```maxon
function main() returns ExitCode
	return ((((1 + 2) * 3) + 4) * 2) + 6
end 'main'
```
```exitcode
32
```
<!-- test: int-expression-both-sides-complex -->
```maxon
function main() returns ExitCode
	let a = 3
	let b = 5
	let c = 7
	let d = 2
	return (a + b) * (c - d)
end 'main'
```
```exitcode
40
```
<!-- test: int-many-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum5(a Integer, b Integer, c Integer, d Integer, e Integer) returns Integer
	return a + b + c + d + e
end 'sum5'

function main() returns ExitCode
	return sum5(5, b: 10, c: 8, d: 12, e: 7)
end 'main'
```
```exitcode
42
```
<!-- test: int-nine-params-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sum9(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer, g Integer, h Integer, i Integer) returns Integer
	return a + b + c + d + e + f + g + h + i
end 'sum9'

function main() returns ExitCode
	return sum9(1, b: 2, c: 3, d: 4, e: 5, f: 6, g: 7, h: 8, i: 9)
end 'main'
```
```exitcode
45
```
<!-- test: int-recursive-factorial -->
```maxon

typealias Integer = int(i64.min to i64.max)

function factorial(n Integer) returns Integer
	if n <= 1 'base'
		return 1
	end 'base'
	return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
	return factorial(5) mod 256
end 'main'
```
```exitcode
120
```
<!-- test: int-loop-pressure-with-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	var a = 1
	let b = 2
	var c = 3
	let d = 4
	var e = 5
	let f = 6
	var i = 0
	while i < 3 'loop'
		a = a + identity(b)
		c = c + identity(d)
		e = e + identity(f)
		i = i + 1
	end 'loop'
	return (a + c + d + e + f) mod 256
end 'main'
```
```exitcode
55
```
<!-- test: float-and-int-mixed-pressure -->
```maxon
function main() returns ExitCode
	let x = 3.14
	let y = 2.86
	let sum_f = x + y
	let a = 10
	let b = 20
	let sum_i = a + b
	return trunc(sum_f) + sum_i
end 'main'
```
```exitcode
36
```
<!-- test: int-value-live-across-nested-control -->
```maxon
function main() returns ExitCode
	let sentinel = 100
	var total = 0
	var i = 0
	while i < 3 'outer'
		var j = 0
		while j < 3 'inner'
			if i == j 'diag'
				total = total + 1
			end 'diag'
			j = j + 1
		end 'inner'
		i = i + 1
	end 'outer'
	return sentinel + total
end 'main'
```
```exitcode
103
```
<!-- test: int-fibonacci -->
```maxon
function main() returns ExitCode
	var a = 0
	var b = 1
	var i = 0
	while i < 13 'loop'
		let temp = a + b
		a = b
		b = temp
		i = i + 1
	end 'loop'
	return a and 125
end 'main'
```
```exitcode
105
```
<!-- test: int-division-high-pressure -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	let c = 30
	let d = 40
	let e = 50
	let f = 60
	let g = 70
	let h = 2
	return ((a + b + c + d + e + f + g) / h) and 125
end 'main'
```
```exitcode
12
```

<!-- test: int-callee-saved-clobber -->
```maxon

typealias Integer = int(i64.min to i64.max)

function useRegs(a Integer, b Integer, c Integer, d Integer) returns Integer
	let x = a + b
	let y = c + d
	let z = x + y
	return z
end 'useRegs'

function main() returns ExitCode
	let sentinel = 42
	let result = useRegs(1, b: 2, c: 3, d: 4)
	return sentinel + result
end 'main'
```
```exitcode
52
```
<!-- test: int-float-survives-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function getInt() returns Integer
	return 40
end 'getInt'

function main() returns ExitCode
	let f = 3.14
	let x = getInt()
	return trunc(f) + x
end 'main'
```
```exitcode
43
```
<!-- test: int-sequential-divisions -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 5
	let c = 84
	let d = 4
	return a / b + c / d
end 'main'
```
```exitcode
41
```

<!-- test: int-remainder-in-arithmetic -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 7
	let c = 10
	let rem = a mod b
	return rem * c
end 'main'
```
```exitcode
20
```
<!-- test: int-call-arg-reverse -->
```maxon

typealias Integer = int(i64.min to i64.max)

function sub(a Integer, b Integer) returns Integer
	return a - b
end 'sub'

function main() returns ExitCode
	let x = 10
	let y = 3
	let result = sub(y, b: x)
	return result + 45
end 'main'
```
```exitcode
38
```
<!-- test: int-subtraction-high-pressure -->
```maxon
function main() returns ExitCode
	let a = 100
	let b = 1
	let c = 2
	let d = 3
	let e = 4
	let f = 5
	let g = 6
	let h = 7
	return a - b - c - d - e - f - g - h
end 'main'
```
```exitcode
72
```
<!-- test: int-multi-var-branch-merge -->
```maxon
function main() returns ExitCode
	var x = 0
	var y = 0
	var z = 0
	if 1 < 2 'branch'
		x = 10
		y = 20
		z = 12
	end 'branch' else 'other'
		x = 1
		y = 2
		z = 3
	end 'other'
	return x + y + z
end 'main'
```
```exitcode
42
```
### Level 7: Match Statements and Expressions

<!-- test: match-statement-simple -->
```maxon
function main() returns ExitCode
	let x = 2
	match x 'check'
		1 then return 10
		2 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```
<!-- test: match-statement-assignment -->
```maxon
function main() returns ExitCode
	let x = 2
	var result = 0
	match x 'process'
		1 then result = 100
		2 then result = 200
		default then result = 0
	end 'process'
	return result and 125
end 'main'
```
```exitcode
72
```
<!-- test: match-statement-or-patterns -->
```maxon
function main() returns ExitCode
	let x = 3
	match x 'check'
		1 or 2 then return 10
		3 or 4 then return 20
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```
<!-- test: match-statement-fallthrough -->
```maxon
function main() returns ExitCode
	let x = 1
	var result = 0
	match x 'cascade'
		1 then result = result + 10 and fallthrough
		2 then result = result + 20 and fallthrough
		3 then result = result + 30
		default then result = 100
	end 'cascade'
	return result
end 'main'
```
```exitcode
60
```
<!-- test: match-expression-basic -->
```maxon
function main() returns ExitCode
	let x = 2
	let result = match x 'eval'
		1 gives 10
		2 gives 20
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
20
```
<!-- test: match-expression-or-patterns -->
```maxon
function main() returns ExitCode
	let x = 4
	let result = match x 'eval'
		1 or 2 gives 10
		3 or 4 gives 20
		default gives 0
	end 'eval'
	return result
end 'main'
```
```exitcode
20
```
<!-- test: match-expression-in-arithmetic -->
```maxon
function main() returns ExitCode
	let x = 2
	let doubled = match x 'eval'
		1 gives 10
		2 gives 20
		default gives 0
	end 'eval' * 2
	return doubled
end 'main'
```
```exitcode
40
```
<!-- test: match-statement-with-function-call -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(n Integer) returns Integer
	return n * 2
end 'double'

function main() returns ExitCode
	let x = 2
	var result = 0
	match x 'process'
		1 then result = double(10)
		2 then result = double(20)
		default then result = 0
	end 'process'
	return result
end 'main'
```
```exitcode
40
```
### Level 8: Error Handling

<!-- test: error-otherwise-ignore -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	try mayFail() otherwise ignore
	return 42
end 'main'
```
```exitcode
42
```
<!-- test: error-otherwise-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var result = 0
	try mayFail() otherwise 'err'
		result = 42
	end 'err'
	return result
end 'main'
```
```exitcode
42
```
<!-- test: error-propagate-through-caller -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function inner() returns Integer throws MyError
	throw MyError.failed
end 'inner'

function middle() returns Integer throws MyError
	let x = try inner()
	return x
end 'middle'

function main() returns ExitCode
	let x = try middle() otherwise 99
	return x
end 'main'
```
```exitcode
99
```
<!-- test: error-multiple-try-calls -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function getA() returns Integer throws MyError
	return 10
end 'getA'

function getB() returns Integer throws MyError
	return 20
end 'getB'

function getC() returns Integer throws MyError
	throw MyError.failed
end 'getC'

function main() returns ExitCode
	let a = try getA() otherwise 0
	let b = try getB() otherwise 0
	let c = try getC() otherwise 12
	return a + b + c
end 'main'
```
```exitcode
42
```
<!-- test: error-throw-in-match -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	invalidInput
	notFound
end 'MyError'

function lookup(key Integer) returns Integer throws MyError
	match key 'dispatch'
		1 then return 100
		2 then return 200
		default then throw MyError.notFound
	end 'dispatch'
end 'lookup'

function main() returns ExitCode
	let a = try lookup(2) otherwise 0
	let b = try lookup(99) otherwise 42
	return (a + b) and 125
end 'main'
```
```exitcode
112
```
### Level 7: Phi-Merge Splitting and Memory-Only Phi Spilling

These tests exercise the LiveRangeSplitter (which breaks each phi-merge's
disjoint anchor intervals into independent sub-ranges so the chordal
allocator doesn't over-coalesce interference) and its memory-only phi
fallback (which spills the parent merge when a sub-range can't be colored,
mirroring LLVM Greedy's stack-slot demotion). Without these techniques the
chordal SSA coloring panics at `colorLookupGpr` on URL.resolve and on
similar functions that mutate many locals across nested control flow.

<!-- test: phi-merge-split-multi-anchor -->
```maxon
function main() returns ExitCode
	var a = 0
	var b = 0
	var c = 0
	var d = 0
	if 1 < 2 'g1'
		a = 1
		b = 2
		c = 3
		d = 4
	end 'g1' else 'g1e'
		a = 10
		b = 20
		c = 30
		d = 40
	end 'g1e'
	if a > 0 'g2'
		a = a + 100
		c = c + 100
	end 'g2' else 'g2e'
		b = b + 100
		d = d + 100
	end 'g2e'
	if b > 0 'g3'
		a = a + b
		c = c + d
	end 'g3' else 'g3e'
		b = a - 1
		d = c - 1
	end 'g3e'
	return (a + b + c + d) and 125
end 'main'
```
```exitcode
88
```

