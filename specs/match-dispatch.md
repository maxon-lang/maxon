---
feature: match-dispatch
status: experimental
keywords: [match, switch, jump-table, binary-search, dispatch, range, or]
category: control-flow
---

# Match Dispatch Strategy

## Documentation

A `match` whose arms all test integer-like patterns (an exact integer, an enum case's
tag, or an integer range) lowers to a single **switch** operation carrying the plan the
match lowering already knows: the sorted, disjoint list of `(lo, hi) -> arm` intervals
plus the default target. The strategy is then chosen in exactly one place, during
Maxon→Standard conversion:

| Condition | Strategy |
|---|---|
| fewer than 4 intervals | linear compare chain |
| span ≤ 4096 slots **and** covered/span ≥ 0.4 **and** span ≤ 32 × intervals | jump table, **biased by the minimum value** |
| otherwise | binary search over the intervals, recursing (a dense subrange of ≥ 4 becomes a table at a leaf) |

The third table condition is a separate test from density because the two sides of the
trade scale with different quantities: a table **costs** one slot per value in the span,
but only **buys** the compares it replaces — and that is the interval *count*, not the
covered-value count. Without it, a plan of a few very wide arms is dense enough to pass
yet spends thousands of near-identical slots to remove three compares.

Consequences that are visible from the language:

- A dense case set does **not** have to start at zero — `100 … 115` gets a table, biased
  by 100.
- A **range arm** and an **`or`-list** fill their several slots in the table, so one range
  arm no longer forces the whole match onto a linear chain.
- A **sparse** case set gets a binary search instead of a linear scan.
- A handful of **very wide** range arms is dense, but still gets a binary search — the
  table would spend more slots than the compares it saves are worth.
- The scrutinee is loaded **once** for the whole dispatch, not once per comparison.

Arms whose patterns are not integer-like — a `String`, a `Character` (a variable-length
grapheme cluster, compared byte-wise), or a float — keep the linear comparison chain.
Every other behaviour is unchanged: payload bindings, `and fallthrough`,
`default throws` / `default panic`, exhaustiveness, and first-arm-wins for overlapping
patterns.

## Tests

### Dense but not zero-based

A 16-case match on 100…115. The table is biased by 100; the probe walks every covered
value plus the neighbours just outside the span on both sides.

<!-- test: dispatch.dense-non-zero-based -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		100 then return 1
		101 then return 2
		102 then return 3
		103 then return 4
		104 then return 5
		105 then return 6
		106 then return 7
		107 then return 8
		108 then return 9
		109 then return 10
		110 then return 11
		111 then return 12
		112 then return 13
		113 then return 14
		114 then return 15
		115 then return 16
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	for i in 97 to 118 'p'
		print("{classify(i)} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```stdout
0 0 0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 0 0 0 
```

### Dense enum with a range arm

`c04 to c06` covers three tags in one arm. Every one of the 16 tags must still reach its
own arm.

<!-- test: dispatch.dense-enum-range-arm -->
```maxon
typealias Result = int(0 to 100)

enum Code
	c00
	c01
	c02
	c03
	c04
	c05
	c06
	c07
	c08
	c09
	c10
	c11
	c12
	c13
	c14
	c15
end 'Code'

function classify(c Code) returns Result
	match c 'm'
		c00 then return 1
		c01 then return 2
		c02 then return 3
		c03 then return 4
		c04 to c06 then return 5
		c07 then return 6
		c08 then return 7
		c09 then return 8
		c10 then return 9
		c11 then return 10
		c12 then return 11
		c13 then return 12
		c14 then return 13
		c15 then return 14
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify(Code.c00)} ")
	print("{classify(Code.c01)} ")
	print("{classify(Code.c02)} ")
	print("{classify(Code.c03)} ")
	print("{classify(Code.c04)} ")
	print("{classify(Code.c05)} ")
	print("{classify(Code.c06)} ")
	print("{classify(Code.c07)} ")
	print("{classify(Code.c08)} ")
	print("{classify(Code.c09)} ")
	print("{classify(Code.c10)} ")
	print("{classify(Code.c11)} ")
	print("{classify(Code.c12)} ")
	print("{classify(Code.c13)} ")
	print("{classify(Code.c14)} ")
	print("{classify(Code.c15)}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 5 5 6 7 8 9 10 11 12 13 14
```

### `or`-list of alternatives

Each alternative contributes its own interval to the same arm.

<!-- test: dispatch.or-list-alternatives -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		1 or 2 or 3 then return 10
		4 or 5 then return 20
		6 then return 30
		7 then return 40
		8 then return 50
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	for i in 0 to 10 'p'
		print("{classify(i)} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```stdout
0 10 10 10 20 20 30 40 50 0 0 
```

### Sparse case set

Six values spread over 0…99999. Too sparse for a table; the dispatch is a binary search
and every value — hit and miss — must still land on the right arm.

<!-- test: dispatch.sparse-binary-search -->
```maxon
typealias Big = int(0 to 1000000)
typealias Result = int(0 to 100)

function classify(n Big) returns Result
	match n 'm'
		1 then return 1
		17 then return 2
		290 then return 3
		4000 then return 4
		51234 then return 5
		99999 then return 6
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify(0)} {classify(1)} {classify(2)} ")
	print("{classify(16)} {classify(17)} {classify(18)} ")
	print("{classify(289)} {classify(290)} {classify(291)} ")
	print("{classify(3999)} {classify(4000)} {classify(4001)} ")
	print("{classify(51233)} {classify(51234)} {classify(51235)} ")
	print("{classify(99998)} {classify(99999)} {classify(100000)}\n")
	return 0
end 'main'
```
```stdout
0 1 0 0 2 0 0 3 0 0 4 0 0 5 0 0 6 0
```

### A dense run inside a sparse set

Eight consecutive values and one far-away outlier. The whole set is far too sparse for a
table, but the binary search's lower half is `1 … 4` — dense, four intervals — so that
leaf becomes a table of its own.

<!-- test: dispatch.nested-table-at-search-leaf -->
```maxon
typealias Big = int(0 to 10000000)
typealias Result = int(0 to 100)

function classify(n Big) returns Result
	match n 'm'
		1 then return 1
		2 then return 2
		3 then return 3
		4 then return 4
		5 then return 5
		6 then return 6
		7 then return 7
		8 then return 8
		1000000 then return 9
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	for i in 0 to 9 'p'
		print("{classify(i)} ")
	end 'p'
	print("{classify(999999)} {classify(1000000)} {classify(1000001)}\n")
	return 0
end 'main'
```
```stdout
0 1 2 3 4 5 6 7 8 0 0 9 0
```

### Fewer than four intervals stays linear

<!-- test: dispatch.three-cases-linear -->
```maxon
typealias Probe = int(0 to 100)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		7 then return 1
		8 then return 2
		9 then return 3
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	for i in 6 to 10 'p'
		print("{classify(i)} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```stdout
0 1 2 3 0 
```

### A float scrutinee is never switched

<!-- test: dispatch.float-scrutinee -->
```maxon
typealias Result = int(0 to 100)
typealias Measure = float(f64.min to f64.max)

function classify(x Measure) returns Result
	match x 'm'
		1.5 then return 1
		2.5 then return 2
		3.5 then return 3
		4.5 then return 4
		5.5 then return 5
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify(1.5)} {classify(2.5)} {classify(3.5)} ")
	print("{classify(4.5)} {classify(5.5)} {classify(6.5)}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 0
```

### A String scrutinee is never switched

<!-- test: dispatch.string-scrutinee -->
```maxon
typealias Result = int(0 to 100)

function classify(s String) returns Result
	match s 'm'
		"alpha" then return 1
		"beta" then return 2
		"gamma" then return 3
		"delta" then return 4
		"epsilon" then return 5
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	let a = "alpha"
	let b = "beta"
	let g = "gamma"
	let d = "delta"
	let e = "epsilon"
	let z = "zeta"
	print("{classify(a)} {classify(b)} {classify(g)} ")
	print("{classify(d)} {classify(e)} {classify(z)}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 0
```

### A Character scrutinee is never switched

A `Character` is a variable-length grapheme cluster compared byte-wise, not an integer
code point, so its arms keep the comparison chain.

<!-- test: dispatch.char-scrutinee -->
```maxon
typealias Result = int(0 to 100)

function classify(c Character) returns Result
	match c 'm'
		'a' then return 1
		'b' then return 2
		'c' then return 3
		'd' then return 4
		'x' to 'z' then return 5
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify('a')} {classify('b')} {classify('c')} {classify('d')} ")
	print("{classify('x')} {classify('y')} {classify('z')} {classify('q')}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 5 5 0
```

### Overlapping range arms — the first arm wins

The arms overlap on 20…30 and on 30…40; the earlier arm owns the overlap, exactly as the
comparison chain would decide it.

<!-- test: dispatch.overlapping-ranges-first-wins -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		10 to 30 then return 1
		20 to 40 then return 2
		30 to 50 then return 3
		45 then return 4
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify(9)} {classify(10)} {classify(19)} {classify(20)} ")
	print("{classify(30)} {classify(31)} {classify(40)} {classify(41)} ")
	print("{classify(45)} {classify(50)} {classify(51)}\n")
	return 0
end 'main'
```
```stdout
0 1 1 1 1 2 2 3 3 3 0
```

### An exhaustive enum match with no default

Falling off the end of an exhaustive match reaches the merge block, which is the switch's
default target.

<!-- test: dispatch.exhaustive-enum-no-default -->
```maxon
typealias Result = int(0 to 100)

enum Weekday
	mon
	tue
	wed
	thu
	fri
	sat
	sun
end 'Weekday'

function classify(d Weekday) returns Result
	match d 'm'
		mon then return 1
		tue then return 2
		wed then return 3
		thu then return 4
		fri then return 5
		sat then return 6
		sun then return 7
	end 'm'
end 'classify'

function main() returns ExitCode
	print("{classify(Weekday.mon)} {classify(Weekday.tue)} {classify(Weekday.wed)} ")
	print("{classify(Weekday.thu)} {classify(Weekday.fri)} {classify(Weekday.sat)} ")
	print("{classify(Weekday.sun)}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4 5 6 7
```

### Negative and extreme values

A biased table must not let a value below the minimum wrap into the table, and a binary
search must order the far ends of the i64 range correctly.

<!-- test: dispatch.negative-and-extreme-values -->
```maxon
typealias Signed = int(i64.min to i64.max)
typealias Result = int(0 to 100)

function classify(n Signed) returns Result
	match n 'm'
		-4 then return 1
		-3 then return 2
		-2 then return 3
		-1 then return 4
		0 then return 5
		1 then return 6
		default then return 0
	end 'm'
end 'classify'

function extremes(n Signed) returns Result
	match n 'm'
		-9223372036854775807 then return 1
		-7 then return 2
		0 then return 3
		9 then return 4
		9223372036854775807 then return 5
		default then return 0
	end 'm'
end 'extremes'

function main() returns ExitCode
	let lo = -9223372036854775807
	let hi = 9223372036854775807
	print("{classify(-6)} {classify(-5)} {classify(-4)} {classify(-3)} ")
	print("{classify(-2)} {classify(-1)} {classify(0)} {classify(1)} {classify(2)}\n")
	print("{extremes(lo)} {extremes(lo + 1)} {extremes(-7)} {extremes(-6)} ")
	print("{extremes(0)} {extremes(9)} {extremes(10)} {extremes(hi - 1)} {extremes(hi)}\n")
	return 0
end 'main'
```
```stdout
0 0 1 2 3 4 5 6 0
1 0 2 0 3 4 0 0 5
```

### Open-ended ranges

`min to X` and `X to max` are unbounded on one side; they can never be a table slot set
and must stay comparisons.

<!-- test: dispatch.open-ended-ranges -->
```maxon
typealias Signed = int(i64.min to i64.max)
typealias Result = int(0 to 100)

function classify(n Signed) returns Result
	match n 'm'
		min to -10 then return 1
		-9 to -5 then return 2
		0 to 4 then return 3
		100 to max then return 4
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	let lo = -9223372036854775807
	let hi = 9223372036854775807
	print("{classify(lo)} {classify(-11)} {classify(-10)} {classify(-9)} ")
	print("{classify(-5)} {classify(-4)} {classify(0)} {classify(4)} {classify(5)} ")
	print("{classify(99)} {classify(100)} {classify(hi)}\n")
	return 0
end 'main'
```
```stdout
1 1 1 2 2 0 3 3 0 0 4 4
```

### `and fallthrough` is preserved

<!-- test: dispatch.fallthrough-preserved -->
```maxon
typealias Probe = int(0 to 100)
typealias Total = int(0 to 100000)

function score(role Probe) returns Total
	var permissions = 0
	match role 'auth'
		10 then permissions = permissions + 1000 and fallthrough
		11 then permissions = permissions + 100 and fallthrough
		12 then permissions = permissions + 10 and fallthrough
		13 then permissions = permissions + 1
		default then permissions = 0
	end 'auth'
	return permissions
end 'score'

function main() returns ExitCode
	print("{score(10)} {score(11)} {score(12)} {score(13)} {score(14)}\n")
	return 0
end 'main'
```
```stdout
1111 111 11 1 0
```

### `default throws` target

<!-- test: dispatch.default-throws -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

enum ClassifyError implements Error
	outOfRange
end 'ClassifyError'

function classify(n Probe) returns Result throws ClassifyError
	match n 'm'
		200 then return 1
		201 then return 2
		202 then return 3
		203 then return 4
		204 then return 5
		default throws ClassifyError.outOfRange
	end 'm'
end 'classify'

function main() returns ExitCode
	for i in 199 to 205 'p'
		print("{try classify(i) otherwise 9} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```stdout
9 1 2 3 4 5 9 
```

### Associated-value payload bindings still work under a table

<!-- test: dispatch.assoc-value-payload-bindings -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Shape
	circle(r Integer)
	square(s Integer)
	triangle(b Integer, h Integer)
	rectangle(w Integer, h Integer)
	point
end 'Shape'

function area(s Shape) returns Integer
	match s 'sh'
		circle(r) then return r * r * 3
		square(side) then return side * side
		triangle(b, h) then return b * h / 2
		rectangle(w, h) then return w * h
		point then return 0
	end 'sh'
end 'area'

function main() returns ExitCode
	print("{area(Shape.circle(2))} {area(Shape.square(4))} ")
	print("{area(Shape.triangle(3, h: 4))} {area(Shape.rectangle(2, h: 5))} ")
	print("{area(Shape.point)}\n")
	return 0
end 'main'
```
```stdout
12 16 6 10 0
```

### A dense match expression

<!-- test: dispatch.match-expression-dense -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 1000)

function classify(n Probe) returns Result
	let v = match n 'm'
		50 gives 5
		51 gives 15
		52 gives 25
		53 gives 35
		54 gives 45
		55 gives 55
		default gives 0
	end 'm'
	return v
end 'classify'

function main() returns ExitCode
	for i in 49 to 56 'p'
		print("{classify(i)} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```stdout
0 5 15 25 35 45 55 0 
```

## Emitted shape

These four pin the IR the strategy selector actually produces, one per strategy. Their
`main` is deliberately a single call so the pinned block is the dispatch and little else.

### Shape: a biased jump table

Six dense values starting at 100 — one `x64.jump_table` preceded by the `sub` that biases
the index to zero.

<!-- test: dispatch.shape-table-biased -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		100 then return 1
		101 then return 2
		102 then return 3
		103 then return 4
		104 then return 5
		105 then return 6
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(103)
end 'main'
```
```exitcode
4
```
```RequiredIR:x64-windows
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-table-biased.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [100:m_0.case0, 101:m_0.case1, 102:m_0.case2, 103:m_0.case3, 104:m_0.case4, 105:m_0.case5] default=m_0.case6
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %22 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case4:
    %26 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case5:
    %30 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %30
  m_0.case6:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %31
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 103 : i64}
    %33 = maxon.call @classify %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 4294967295 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-table-biased.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 100 : i64}
    %11 = arith.subi %9, %10
    cf.switch %11 [6 cases] default=m_0.case6
  m_0.case0:
    %12 = arith.constant {value = 1 : i64}
    func.return %12
  m_0.case1:
    %13 = arith.constant {value = 2 : i64}
    func.return %13
  m_0.case2:
    %14 = arith.constant {value = 3 : i64}
    func.return %14
  m_0.case3:
    %15 = arith.constant {value = 4 : i64}
    func.return %15
  m_0.case4:
    %16 = arith.constant {value = 5 : i64}
    func.return %16
  m_0.case5:
    %17 = arith.constant {value = 6 : i64}
    func.return %17
  m_0.case6:
    %18 = arith.constant {value = 0 : i64}
    func.return %18
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %19 = arith.constant {value = 103 : i64}
    %20 = func.call @classify %19
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.cmpi lt %20, %21
    %23 = arith.constant {value = 4294967295 : i64}
    %24 = arith.cmpi gt %20, %23
    %25 = arith.ori1 %22, %24
    cf.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %26 = memref.lea_symdata __panic_msg_1
    %27 = std.ptr_to_i64 %26
    std.call_runtime @mrt_panic %27
  __range_ok_0:
    func.return %20
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.mov rdx, 1000
    x64.cmp rcx, rdx
    x64.jg classify.__range_panic_0
    x64.cmp rcx, rax
    x64.jl classify.__range_panic_0
    x64.jmp classify.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rbx, [rbp-8]
    x64.mov [rbp-16], rbx
    x64.mov rbx, [rbp-16]
    x64.mov rsi, 100
    x64.sub rbx, rsi
    x64.jump_table rbx, 6 cases, default=classify.m_0.case6
  m_0.case0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  m_0.case1:
    x64.mov rax, 2
    x64.epilogue
    x64.ret
  m_0.case2:
    x64.mov rax, 3
    x64.epilogue
    x64.ret
  m_0.case3:
    x64.mov rax, 4
    x64.epilogue
    x64.ret
  m_0.case4:
    x64.mov rax, 5
    x64.epilogue
    x64.ret
  m_0.case5:
    x64.mov rax, 6
    x64.epilogue
    x64.ret
  m_0.case6:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 103
    x64.call classify
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-table-biased.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [100:m_0.case0, 101:m_0.case1, 102:m_0.case2, 103:m_0.case3, 104:m_0.case4, 105:m_0.case5] default=m_0.case6
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %22 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case4:
    %26 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case5:
    %30 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %30
  m_0.case6:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %31
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 103 : i64}
    %33 = maxon.call @classify %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 255 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-table-biased.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 100 : i64}
    %11 = arith.subi %9, %10
    cf.switch %11 [6 cases] default=m_0.case6
  m_0.case0:
    %12 = arith.constant {value = 1 : i64}
    func.return %12
  m_0.case1:
    %13 = arith.constant {value = 2 : i64}
    func.return %13
  m_0.case2:
    %14 = arith.constant {value = 3 : i64}
    func.return %14
  m_0.case3:
    %15 = arith.constant {value = 4 : i64}
    func.return %15
  m_0.case4:
    %16 = arith.constant {value = 5 : i64}
    func.return %16
  m_0.case5:
    %17 = arith.constant {value = 6 : i64}
    func.return %17
  m_0.case6:
    %18 = arith.constant {value = 0 : i64}
    func.return %18
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    %19 = arith.constant {value = 103 : i64}
    %20 = func.call @classify %19
    %21 = arith.constant {value = 0 : i64}
    %22 = arith.cmpi lt %20, %21
    %23 = arith.constant {value = 255 : i64}
    %24 = arith.cmpi gt %20, %23
    %25 = arith.ori1 %22, %24
    cf.cond_br %25 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %26 = memref.lea_symdata __panic_msg_1
    %27 = std.ptr_to_i64 %26
    std.call_runtime @mrt_panic %27
  __range_ok_0:
    func.return %20
  }
}
=== arm64
module {
  func @classify(n: i64) -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne classify.__range_panic_0
    arm64.b classify.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.mov x2, #100
    arm64.sub x3, x1, x2
    arm64.jump_table x3, 6 cases, default=classify.m_0.case6
  m_0.case0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case1:
    arm64.mov x0, #2
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case2:
    arm64.mov x0, #3
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case3:
    arm64.mov x0, #4
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case4:
    arm64.mov x0, #5
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case5:
    arm64.mov x0, #6
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case6:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #103
    arm64.bl classify
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_1
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Shape: a range arm fills its table slots

<!-- test: dispatch.shape-range-arm-table -->
```maxon
typealias Probe = int(0 to 1000)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		1 then return 1
		2 to 5 then return 2
		6 then return 3
		7 then return 4
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(4)
end 'main'
```
```exitcode
2
```
```RequiredIR:x64-windows
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-range-arm-table.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 2..5:m_0.case1, 6:m_0.case2, 7:m_0.case3] default=m_0.case4
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %18 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case2:
    %22 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case3:
    %26 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case4:
    %27 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %27
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %28 = maxon.literal {value = 4 : i64}
    %29 = maxon.call @classify %28
    %30 = maxon.literal {value = 0 : i64}
    %31 = maxon.binop %29, %30 {op = lt}
    %32 = maxon.literal {value = 4294967295 : i64}
    %33 = maxon.binop %29, %32 {op = gt}
    %34 = maxon.binop %31, %33 {op = or}
    maxon.cond_br %34 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-range-arm-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %29
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 1 : i64}
    %11 = arith.subi %9, %10
    cf.switch %11 [7 cases] default=m_0.case4
  m_0.case0:
    %12 = arith.constant {value = 1 : i64}
    func.return %12
  m_0.case1:
    %13 = arith.constant {value = 2 : i64}
    func.return %13
  m_0.case2:
    %14 = arith.constant {value = 3 : i64}
    func.return %14
  m_0.case3:
    %15 = arith.constant {value = 4 : i64}
    func.return %15
  m_0.case4:
    %16 = arith.constant {value = 0 : i64}
    func.return %16
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %17 = arith.constant {value = 4 : i64}
    %18 = func.call @classify %17
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 4294967295 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_1
    %25 = std.ptr_to_i64 %24
    std.call_runtime @mrt_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.mov rdx, 1000
    x64.cmp rcx, rdx
    x64.jg classify.__range_panic_0
    x64.cmp rcx, rax
    x64.jl classify.__range_panic_0
    x64.jmp classify.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rbx, [rbp-8]
    x64.mov [rbp-16], rbx
    x64.mov rbx, [rbp-16]
    x64.mov rsi, 1
    x64.sub rbx, rsi
    x64.jump_table rbx, 7 cases, default=classify.m_0.case4
  m_0.case0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  m_0.case1:
    x64.mov rax, 2
    x64.epilogue
    x64.ret
  m_0.case2:
    x64.mov rax, 3
    x64.epilogue
    x64.ret
  m_0.case3:
    x64.mov rax, 4
    x64.epilogue
    x64.ret
  m_0.case4:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 4
    x64.call classify
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-range-arm-table.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 2..5:m_0.case1, 6:m_0.case2, 7:m_0.case3] default=m_0.case4
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %18 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case2:
    %22 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case3:
    %26 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case4:
    %27 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %27
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %28 = maxon.literal {value = 4 : i64}
    %29 = maxon.call @classify %28
    %30 = maxon.literal {value = 0 : i64}
    %31 = maxon.binop %29, %30 {op = lt}
    %32 = maxon.literal {value = 255 : i64}
    %33 = maxon.binop %29, %32 {op = gt}
    %34 = maxon.binop %31, %33 {op = or}
    maxon.cond_br %34 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-range-arm-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %29
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 1 : i64}
    %11 = arith.subi %9, %10
    cf.switch %11 [7 cases] default=m_0.case4
  m_0.case0:
    %12 = arith.constant {value = 1 : i64}
    func.return %12
  m_0.case1:
    %13 = arith.constant {value = 2 : i64}
    func.return %13
  m_0.case2:
    %14 = arith.constant {value = 3 : i64}
    func.return %14
  m_0.case3:
    %15 = arith.constant {value = 4 : i64}
    func.return %15
  m_0.case4:
    %16 = arith.constant {value = 0 : i64}
    func.return %16
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    %17 = arith.constant {value = 4 : i64}
    %18 = func.call @classify %17
    %19 = arith.constant {value = 0 : i64}
    %20 = arith.cmpi lt %18, %19
    %21 = arith.constant {value = 255 : i64}
    %22 = arith.cmpi gt %18, %21
    %23 = arith.ori1 %20, %22
    cf.cond_br %23 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %24 = memref.lea_symdata __panic_msg_1
    %25 = std.ptr_to_i64 %24
    std.call_runtime @mrt_panic %25
  __range_ok_0:
    func.return %18
  }
}
=== arm64
module {
  func @classify(n: i64) -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #1000
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne classify.__range_panic_0
    arm64.b classify.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.mov x2, #1
    arm64.sub x3, x1, x2
    arm64.jump_table x3, 7 cases, default=classify.m_0.case4
  m_0.case0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case1:
    arm64.mov x0, #2
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case2:
    arm64.mov x0, #3
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case3:
    arm64.mov x0, #4
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case4:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #4
    arm64.bl classify
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_1
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Shape: a binary search over a sparse set

<!-- test: dispatch.shape-binary-search -->
```maxon
typealias Big = int(0 to 1000000)
typealias Result = int(0 to 100)

function classify(n Big) returns Result
	match n 'm'
		1 then return 1
		17 then return 2
		290 then return 3
		4000 then return 4
		51234 then return 5
		99999 then return 6
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(51234)
end 'main'
```
```exitcode
5
```
```RequiredIR:x64-windows
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-binary-search.test:5: Range check failed: value outside typealias 'Big'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 17:m_0.case1, 290:m_0.case2, 4000:m_0.case3, 51234:m_0.case4, 99999:m_0.case5] default=m_0.case6
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %22 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case4:
    %26 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case5:
    %30 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %30
  m_0.case6:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %31
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 51234 : i64}
    %33 = maxon.call @classify %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 4294967295 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-binary-search.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 4000 : i64}
    %11 = arith.cmpi lt %9, %10
    cf.cond_br %11 [then: m_0.dispatch0, else: m_0.dispatch1]
  m_0.dispatch0:
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.cmpi ne %9, %12
    cf.cond_br %13 [then: m_0.dispatch2, else: m_0.case0]
  m_0.dispatch2:
    %14 = arith.constant {value = 17 : i64}
    %15 = arith.cmpi ne %9, %14
    cf.cond_br %15 [then: m_0.dispatch3, else: m_0.case1]
  m_0.dispatch3:
    %16 = arith.constant {value = 290 : i64}
    %17 = arith.cmpi ne %9, %16
    cf.cond_br %17 [then: m_0.dispatch4, else: m_0.case2]
  m_0.dispatch4:
    cf.br m_0.case6
  m_0.dispatch1:
    %18 = arith.constant {value = 4000 : i64}
    %19 = arith.cmpi ne %9, %18
    cf.cond_br %19 [then: m_0.dispatch5, else: m_0.case3]
  m_0.dispatch5:
    %20 = arith.constant {value = 51234 : i64}
    %21 = arith.cmpi ne %9, %20
    cf.cond_br %21 [then: m_0.dispatch6, else: m_0.case4]
  m_0.dispatch6:
    %22 = arith.constant {value = 99999 : i64}
    %23 = arith.cmpi ne %9, %22
    cf.cond_br %23 [then: m_0.dispatch7, else: m_0.case5]
  m_0.dispatch7:
    cf.br m_0.case6
  m_0.case0:
    %24 = arith.constant {value = 1 : i64}
    func.return %24
  m_0.case1:
    %25 = arith.constant {value = 2 : i64}
    func.return %25
  m_0.case2:
    %26 = arith.constant {value = 3 : i64}
    func.return %26
  m_0.case3:
    %27 = arith.constant {value = 4 : i64}
    func.return %27
  m_0.case4:
    %28 = arith.constant {value = 5 : i64}
    func.return %28
  m_0.case5:
    %29 = arith.constant {value = 6 : i64}
    func.return %29
  m_0.case6:
    %30 = arith.constant {value = 0 : i64}
    func.return %30
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %31 = arith.constant {value = 51234 : i64}
    %32 = func.call @classify %31
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 4294967295 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_1
    %39 = std.ptr_to_i64 %38
    std.call_runtime @mrt_panic %39
  __range_ok_0:
    func.return %32
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.mov rdx, 1000000
    x64.cmp rcx, rdx
    x64.jg classify.__range_panic_0
    x64.cmp rcx, rax
    x64.jl classify.__range_panic_0
    x64.jmp classify.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rbx, [rbp-8]
    x64.mov [rbp-16], rbx
    x64.mov rbx, [rbp-16]
    x64.mov rsi, 4000
    x64.cmp rbx, rsi
    x64.jge classify.m_0.dispatch1
  m_0.dispatch0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case0
  m_0.dispatch2:
    x64.mov rax, 17
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case1
  m_0.dispatch3:
    x64.mov rax, 290
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case2
  m_0.dispatch4:
    x64.jmp classify.m_0.case6
  m_0.dispatch1:
    x64.mov rax, 4000
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case3
  m_0.dispatch5:
    x64.mov rax, 51234
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case4
  m_0.dispatch6:
    x64.mov rax, 99999
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case5
  m_0.dispatch7:
    x64.jmp classify.m_0.case6
  m_0.case0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  m_0.case1:
    x64.mov rax, 2
    x64.epilogue
    x64.ret
  m_0.case2:
    x64.mov rax, 3
    x64.epilogue
    x64.ret
  m_0.case3:
    x64.mov rax, 4
    x64.epilogue
    x64.ret
  m_0.case4:
    x64.mov rax, 5
    x64.epilogue
    x64.ret
  m_0.case5:
    x64.mov rax, 6
    x64.epilogue
    x64.ret
  m_0.case6:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 51234
    x64.call classify
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 1000000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-binary-search.test:5: Range check failed: value outside typealias 'Big'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 17:m_0.case1, 290:m_0.case2, 4000:m_0.case3, 51234:m_0.case4, 99999:m_0.case5] default=m_0.case6
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %22 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %22
  m_0.case4:
    %26 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %26
  m_0.case5:
    %30 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %30
  m_0.case6:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %31
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 51234 : i64}
    %33 = maxon.call @classify %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 255 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-binary-search.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 1000000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 4000 : i64}
    %11 = arith.cmpi lt %9, %10
    cf.cond_br %11 [then: m_0.dispatch0, else: m_0.dispatch1]
  m_0.dispatch0:
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.cmpi ne %9, %12
    cf.cond_br %13 [then: m_0.dispatch2, else: m_0.case0]
  m_0.dispatch2:
    %14 = arith.constant {value = 17 : i64}
    %15 = arith.cmpi ne %9, %14
    cf.cond_br %15 [then: m_0.dispatch3, else: m_0.case1]
  m_0.dispatch3:
    %16 = arith.constant {value = 290 : i64}
    %17 = arith.cmpi ne %9, %16
    cf.cond_br %17 [then: m_0.dispatch4, else: m_0.case2]
  m_0.dispatch4:
    cf.br m_0.case6
  m_0.dispatch1:
    %18 = arith.constant {value = 4000 : i64}
    %19 = arith.cmpi ne %9, %18
    cf.cond_br %19 [then: m_0.dispatch5, else: m_0.case3]
  m_0.dispatch5:
    %20 = arith.constant {value = 51234 : i64}
    %21 = arith.cmpi ne %9, %20
    cf.cond_br %21 [then: m_0.dispatch6, else: m_0.case4]
  m_0.dispatch6:
    %22 = arith.constant {value = 99999 : i64}
    %23 = arith.cmpi ne %9, %22
    cf.cond_br %23 [then: m_0.dispatch7, else: m_0.case5]
  m_0.dispatch7:
    cf.br m_0.case6
  m_0.case0:
    %24 = arith.constant {value = 1 : i64}
    func.return %24
  m_0.case1:
    %25 = arith.constant {value = 2 : i64}
    func.return %25
  m_0.case2:
    %26 = arith.constant {value = 3 : i64}
    func.return %26
  m_0.case3:
    %27 = arith.constant {value = 4 : i64}
    func.return %27
  m_0.case4:
    %28 = arith.constant {value = 5 : i64}
    func.return %28
  m_0.case5:
    %29 = arith.constant {value = 6 : i64}
    func.return %29
  m_0.case6:
    %30 = arith.constant {value = 0 : i64}
    func.return %30
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    %31 = arith.constant {value = 51234 : i64}
    %32 = func.call @classify %31
    %33 = arith.constant {value = 0 : i64}
    %34 = arith.cmpi lt %32, %33
    %35 = arith.constant {value = 255 : i64}
    %36 = arith.cmpi gt %32, %35
    %37 = arith.ori1 %34, %36
    cf.cond_br %37 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %38 = memref.lea_symdata __panic_msg_1
    %39 = std.ptr_to_i64 %38
    std.call_runtime @mrt_panic %39
  __range_ok_0:
    func.return %32
  }
}
=== arm64
module {
  func @classify(n: i64) -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #1000000
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne classify.__range_panic_0
    arm64.b classify.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.mov x2, #4000
    arm64.cmp x1, x2
    arm64.cset x3, lt
    arm64.cmp x3, #0
    arm64.b.ne classify.m_0.dispatch0
    arm64.b classify.m_0.dispatch1
  m_0.dispatch0:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch2
    arm64.b classify.m_0.case0
  m_0.dispatch2:
    arm64.mov x0, #17
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch3
    arm64.b classify.m_0.case1
  m_0.dispatch3:
    arm64.mov x0, #290
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch4
    arm64.b classify.m_0.case2
  m_0.dispatch4:
    arm64.b classify.m_0.case6
  m_0.dispatch1:
    arm64.mov x0, #4000
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch5
    arm64.b classify.m_0.case3
  m_0.dispatch5:
    arm64.mov x0, #51234
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch6
    arm64.b classify.m_0.case4
  m_0.dispatch6:
    arm64.mov x0, #99999
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch7
    arm64.b classify.m_0.case5
  m_0.dispatch7:
    arm64.b classify.m_0.case6
  m_0.case0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case1:
    arm64.mov x0, #2
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case2:
    arm64.mov x0, #3
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case3:
    arm64.mov x0, #4
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case4:
    arm64.mov x0, #5
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case5:
    arm64.mov x0, #6
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case6:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #51234
    arm64.bl classify
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_1
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Shape: under the threshold, a linear chain

<!-- test: dispatch.shape-linear-under-threshold -->
```maxon
typealias Probe = int(0 to 100)
typealias Result = int(0 to 100)

function classify(n Probe) returns Result
	match n 'm'
		7 then return 1
		8 then return 2
		9 then return 3
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	return classify(8)
end 'main'
```
```exitcode
2
```
```RequiredIR:x64-windows
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 100 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-linear-under-threshold.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [7:m_0.case0, 8:m_0.case1, 9:m_0.case2] default=m_0.case3
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %19 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %19
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %20 = maxon.literal {value = 8 : i64}
    %21 = maxon.call @classify %20
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 4294967295 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-linear-under-threshold.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %21
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 100 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 7 : i64}
    %11 = arith.cmpi ne %9, %10
    cf.cond_br %11 [then: m_0.dispatch0, else: m_0.case0]
  m_0.dispatch0:
    %12 = arith.constant {value = 8 : i64}
    %13 = arith.cmpi ne %9, %12
    cf.cond_br %13 [then: m_0.dispatch1, else: m_0.case1]
  m_0.dispatch1:
    %14 = arith.constant {value = 9 : i64}
    %15 = arith.cmpi ne %9, %14
    cf.cond_br %15 [then: m_0.dispatch2, else: m_0.case2]
  m_0.dispatch2:
    cf.br m_0.case3
  m_0.case0:
    %16 = arith.constant {value = 1 : i64}
    func.return %16
  m_0.case1:
    %17 = arith.constant {value = 2 : i64}
    func.return %17
  m_0.case2:
    %18 = arith.constant {value = 3 : i64}
    func.return %18
  m_0.case3:
    %19 = arith.constant {value = 0 : i64}
    func.return %19
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %20 = arith.constant {value = 8 : i64}
    %21 = func.call @classify %20
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 4294967295 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_1
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.mov rdx, 100
    x64.cmp rcx, rdx
    x64.jg classify.__range_panic_0
    x64.cmp rcx, rax
    x64.jl classify.__range_panic_0
    x64.jmp classify.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rbx, [rbp-8]
    x64.mov [rbp-16], rbx
    x64.mov rbx, [rbp-16]
    x64.mov rsi, 7
    x64.cmp rbx, rsi
    x64.je classify.m_0.case0
  m_0.dispatch0:
    x64.mov rax, 8
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case1
  m_0.dispatch1:
    x64.mov rax, 9
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je classify.m_0.case2
  m_0.dispatch2:
    x64.jmp classify.m_0.case3
  m_0.case0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  m_0.case1:
    x64.mov rax, 2
    x64.epilogue
    x64.ret
  m_0.case2:
    x64.mov rax, 3
    x64.epilogue
    x64.ret
  m_0.case3:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 8
    x64.call classify
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @classify(n: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = n} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 100 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-linear-under-threshold.test:5: Range check failed: value outside typealias 'Probe'"
  __range_ok_0:
    %6 = maxon.var_ref {var = n} {type = i64}
    maxon.assign %6 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [7:m_0.case0, 8:m_0.case1, 9:m_0.case2] default=m_0.case3
  m_0.case0:
    %10 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %10
  m_0.case1:
    %14 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %14
  m_0.case2:
    %18 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %18
  m_0.case3:
    %19 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %19
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %20 = maxon.literal {value = 8 : i64}
    %21 = maxon.call @classify %20
    %22 = maxon.literal {value = 0 : i64}
    %23 = maxon.binop %21, %22 {op = lt}
    %24 = maxon.literal {value = 255 : i64}
    %25 = maxon.binop %21, %24 {op = gt}
    %26 = maxon.binop %23, %25 {op = or}
    maxon.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-linear-under-threshold.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %21
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, n
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 100 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load n : i64
    memref.store %8, __match_m_0
    %9 = memref.load __match_m_0 : i64
    %10 = arith.constant {value = 7 : i64}
    %11 = arith.cmpi ne %9, %10
    cf.cond_br %11 [then: m_0.dispatch0, else: m_0.case0]
  m_0.dispatch0:
    %12 = arith.constant {value = 8 : i64}
    %13 = arith.cmpi ne %9, %12
    cf.cond_br %13 [then: m_0.dispatch1, else: m_0.case1]
  m_0.dispatch1:
    %14 = arith.constant {value = 9 : i64}
    %15 = arith.cmpi ne %9, %14
    cf.cond_br %15 [then: m_0.dispatch2, else: m_0.case2]
  m_0.dispatch2:
    cf.br m_0.case3
  m_0.case0:
    %16 = arith.constant {value = 1 : i64}
    func.return %16
  m_0.case1:
    %17 = arith.constant {value = 2 : i64}
    func.return %17
  m_0.case2:
    %18 = arith.constant {value = 3 : i64}
    func.return %18
  m_0.case3:
    %19 = arith.constant {value = 0 : i64}
    func.return %19
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    %20 = arith.constant {value = 8 : i64}
    %21 = func.call @classify %20
    %22 = arith.constant {value = 0 : i64}
    %23 = arith.cmpi lt %21, %22
    %24 = arith.constant {value = 255 : i64}
    %25 = arith.cmpi gt %21, %24
    %26 = arith.ori1 %23, %25
    cf.cond_br %26 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %27 = memref.lea_symdata __panic_msg_1
    %28 = std.ptr_to_i64 %27
    std.call_runtime @mrt_panic %28
  __range_ok_0:
    func.return %21
  }
}
=== arm64
module {
  func @classify(n: i64) -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #100
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne classify.__range_panic_0
    arm64.b classify.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.mov x2, #7
    arm64.cmp x1, x2
    arm64.cset x3, ne
    arm64.cmp x3, #0
    arm64.b.ne classify.m_0.dispatch0
    arm64.b classify.m_0.case0
  m_0.dispatch0:
    arm64.mov x0, #8
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch1
    arm64.b classify.m_0.case1
  m_0.dispatch1:
    arm64.mov x0, #9
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne classify.m_0.dispatch2
    arm64.b classify.m_0.case2
  m_0.dispatch2:
    arm64.b classify.m_0.case3
  m_0.case0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case1:
    arm64.mov x0, #2
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case2:
    arm64.mov x0, #3
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.case3:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  m_0.merge:
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #8
    arm64.bl classify
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_1
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### Shape: a few wide arms do not buy a table

Four intervals spanning 3900 slots at 77% density: dense enough and inside the span cap,
but the table would cost 3900 slots to remove three compares, so the budget of 32 slots
per interval rejects it and a binary search is emitted instead. The counterpart is the
`grade` match in `match-statements` — 5 arms over a span of 101, well inside its budget
of 160 — which does form a table.

<!-- test: dispatch.shape-wide-arms-no-table -->
```maxon
typealias Val = int(0 to 200000)
typealias Code = int(0 to 255)

function pick(v Val) returns Code
	match v 'p'
		1 to 2000 then return 1
		2500 to 3500 then return 2
		3800 then return 3
		3900 then return 4
		default then return 0
	end 'p'
end 'pick'

function main() returns ExitCode
	return pick(3000)
end 'main'
```
```exitcode
2
```
```RequiredIR:x64-windows
=== maxon
module {
  func @pick(v: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = v} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 200000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-wide-arms-no-table.test:5: Range check failed: value outside typealias 'Val'"
  __range_ok_0:
    %6 = maxon.var_ref {var = v} {type = i64}
    maxon.assign %6 {var = __match_p_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_p_0 [1..2000:p_0.case0, 2500..3500:p_0.case1, 3800:p_0.case2, 3900:p_0.case3] default=p_0.case4
  p_0.case0:
    %14 = maxon.literal {value = 1 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %14
  p_0.case1:
    %22 = maxon.literal {value = 2 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %22
  p_0.case2:
    %26 = maxon.literal {value = 3 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %26
  p_0.case3:
    %30 = maxon.literal {value = 4 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %30
  p_0.case4:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %31
  p_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 3000 : i64}
    %33 = maxon.call @pick %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 4294967295 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-wide-arms-no-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @pick(v: i64) -> u8 {
  entry:
    %0 = func.param v : StdI64
    memref.store %0, v
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 200000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load v : i64
    memref.store %8, __match_p_0
    %9 = memref.load __match_p_0 : i64
    %10 = arith.constant {value = 3800 : i64}
    %11 = arith.cmpi lt %9, %10
    cf.cond_br %11 [then: p_0.dispatch0, else: p_0.dispatch1]
  p_0.dispatch0:
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.subi %9, %12
    %14 = arith.constant {value = 1999 : i64}
    %15 = arith.cmpui ugt %13, %14
    cf.cond_br %15 [then: p_0.dispatch2, else: p_0.case0]
  p_0.dispatch2:
    %16 = arith.constant {value = 2500 : i64}
    %17 = arith.subi %9, %16
    %18 = arith.constant {value = 1000 : i64}
    %19 = arith.cmpui ugt %17, %18
    cf.cond_br %19 [then: p_0.dispatch3, else: p_0.case1]
  p_0.dispatch3:
    cf.br p_0.case4
  p_0.dispatch1:
    %20 = arith.constant {value = 3800 : i64}
    %21 = arith.cmpi ne %9, %20
    cf.cond_br %21 [then: p_0.dispatch4, else: p_0.case2]
  p_0.dispatch4:
    %22 = arith.constant {value = 3900 : i64}
    %23 = arith.cmpi ne %9, %22
    cf.cond_br %23 [then: p_0.dispatch5, else: p_0.case3]
  p_0.dispatch5:
    cf.br p_0.case4
  p_0.case0:
    %24 = arith.constant {value = 1 : i64}
    func.return %24
  p_0.case1:
    %25 = arith.constant {value = 2 : i64}
    func.return %25
  p_0.case2:
    %26 = arith.constant {value = 3 : i64}
    func.return %26
  p_0.case3:
    %27 = arith.constant {value = 4 : i64}
    func.return %27
  p_0.case4:
    %28 = arith.constant {value = 0 : i64}
    func.return %28
  p_0.merge:
  }
  func @main() -> u32 {
  entry:
    %29 = arith.constant {value = 3000 : i64}
    %30 = func.call @pick %29
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 4294967295 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %36 = memref.lea_symdata __panic_msg_1
    %37 = std.ptr_to_i64 %36
    std.call_runtime @mrt_panic %37
  __range_ok_0:
    func.return %30
  }
}
=== x86
module {
  func @pick(v: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.xor eax, eax
    x64.mov rdx, 200000
    x64.cmp rcx, rdx
    x64.jg pick.__range_panic_0
    x64.cmp rcx, rax
    x64.jl pick.__range_panic_0
    x64.jmp pick.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.mov rbx, [rbp-8]
    x64.mov [rbp-16], rbx
    x64.mov rbx, [rbp-16]
    x64.mov rsi, 3800
    x64.cmp rbx, rsi
    x64.jge pick.p_0.dispatch1
  p_0.dispatch0:
    x64.mov rax, 1
    x64.mov rdx, [rbp-16]
    x64.sub rdx, rax
    x64.mov rbx, 1999
    x64.cmp rdx, rbx
    x64.jbe pick.p_0.case0
  p_0.dispatch2:
    x64.mov rax, 2500
    x64.mov rdx, [rbp-16]
    x64.sub rdx, rax
    x64.mov rbx, 1000
    x64.cmp rdx, rbx
    x64.jbe pick.p_0.case1
  p_0.dispatch3:
    x64.jmp pick.p_0.case4
  p_0.dispatch1:
    x64.mov rax, 3800
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je pick.p_0.case2
  p_0.dispatch4:
    x64.mov rax, 3900
    x64.mov rcx, [rbp-16]
    x64.cmp rcx, rax
    x64.je pick.p_0.case3
  p_0.dispatch5:
    x64.jmp pick.p_0.case4
  p_0.case0:
    x64.mov rax, 1
    x64.epilogue
    x64.ret
  p_0.case1:
    x64.mov rax, 2
    x64.epilogue
    x64.ret
  p_0.case2:
    x64.mov rax, 3
    x64.epilogue
    x64.ret
  p_0.case3:
    x64.mov rax, 4
    x64.epilogue
    x64.ret
  p_0.case4:
    x64.xor eax, eax
    x64.epilogue
    x64.ret
  p_0.merge:
  }
  func @main() -> u32 {
  entry:
    x64.prologue stack_size=16
    x64.mov rcx, 3000
    x64.call pick
    x64.xor ecx, ecx
    x64.mov edx, 4294967295
    x64.cmp rax, rdx
    x64.jg main.__range_panic_0
    x64.cmp rax, rcx
    x64.jl main.__range_panic_0
    x64.jmp main.__range_ok_0
  __range_panic_0:
    x64.lea_symdata rax, [__panic_msg_1]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
```RequiredIR:arm64-macos
=== maxon
module {
  func @pick(v: i64) -> i64 {
  entry:
    %0 = maxon.param {index = 0 : i32} {name = v} {type = i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %0, %1 {op = lt}
    %3 = maxon.literal {value = 200000 : i64}
    %4 = maxon.binop %0, %3 {op = gt}
    %5 = maxon.binop %2, %4 {op = or}
    maxon.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-wide-arms-no-table.test:5: Range check failed: value outside typealias 'Val'"
  __range_ok_0:
    %6 = maxon.var_ref {var = v} {type = i64}
    maxon.assign %6 {var = __match_p_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_p_0 [1..2000:p_0.case0, 2500..3500:p_0.case1, 3800:p_0.case2, 3900:p_0.case3] default=p_0.case4
  p_0.case0:
    %14 = maxon.literal {value = 1 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %14
  p_0.case1:
    %22 = maxon.literal {value = 2 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %22
  p_0.case2:
    %26 = maxon.literal {value = 3 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %26
  p_0.case3:
    %30 = maxon.literal {value = 4 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %30
  p_0.case4:
    %31 = maxon.literal {value = 0 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %31
  p_0.merge:
  }
  func @main() -> i64 {
  entry:
    %32 = maxon.literal {value = 3000 : i64}
    %33 = maxon.call @pick %32
    %34 = maxon.literal {value = 0 : i64}
    %35 = maxon.binop %33, %34 {op = lt}
    %36 = maxon.literal {value = 255 : i64}
    %37 = maxon.binop %33, %36 {op = gt}
    %38 = maxon.binop %35, %37 {op = or}
    maxon.cond_br %38 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-wide-arms-no-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %33
  }
}
=== standard
module {
  func @pick(v: i64) -> u8 {
  entry:
    %0 = func.param v : StdI64
    memref.store %0, v
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.cmpi lt %0, %1
    %3 = arith.constant {value = 200000 : i64}
    %4 = arith.cmpi gt %0, %3
    %5 = arith.ori1 %2, %4
    cf.cond_br %5 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %6 = memref.lea_symdata __panic_msg_0
    %7 = std.ptr_to_i64 %6
    std.call_runtime @mrt_panic %7
  __range_ok_0:
    %8 = memref.load v : i64
    memref.store %8, __match_p_0
    %9 = memref.load __match_p_0 : i64
    %10 = arith.constant {value = 3800 : i64}
    %11 = arith.cmpi lt %9, %10
    cf.cond_br %11 [then: p_0.dispatch0, else: p_0.dispatch1]
  p_0.dispatch0:
    %12 = arith.constant {value = 1 : i64}
    %13 = arith.subi %9, %12
    %14 = arith.constant {value = 1999 : i64}
    %15 = arith.cmpui ugt %13, %14
    cf.cond_br %15 [then: p_0.dispatch2, else: p_0.case0]
  p_0.dispatch2:
    %16 = arith.constant {value = 2500 : i64}
    %17 = arith.subi %9, %16
    %18 = arith.constant {value = 1000 : i64}
    %19 = arith.cmpui ugt %17, %18
    cf.cond_br %19 [then: p_0.dispatch3, else: p_0.case1]
  p_0.dispatch3:
    cf.br p_0.case4
  p_0.dispatch1:
    %20 = arith.constant {value = 3800 : i64}
    %21 = arith.cmpi ne %9, %20
    cf.cond_br %21 [then: p_0.dispatch4, else: p_0.case2]
  p_0.dispatch4:
    %22 = arith.constant {value = 3900 : i64}
    %23 = arith.cmpi ne %9, %22
    cf.cond_br %23 [then: p_0.dispatch5, else: p_0.case3]
  p_0.dispatch5:
    cf.br p_0.case4
  p_0.case0:
    %24 = arith.constant {value = 1 : i64}
    func.return %24
  p_0.case1:
    %25 = arith.constant {value = 2 : i64}
    func.return %25
  p_0.case2:
    %26 = arith.constant {value = 3 : i64}
    func.return %26
  p_0.case3:
    %27 = arith.constant {value = 4 : i64}
    func.return %27
  p_0.case4:
    %28 = arith.constant {value = 0 : i64}
    func.return %28
  p_0.merge:
  }
  func @main() -> u8 {
  entry:
    %29 = arith.constant {value = 3000 : i64}
    %30 = func.call @pick %29
    %31 = arith.constant {value = 0 : i64}
    %32 = arith.cmpi lt %30, %31
    %33 = arith.constant {value = 255 : i64}
    %34 = arith.cmpi gt %30, %33
    %35 = arith.ori1 %32, %34
    cf.cond_br %35 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %36 = memref.lea_symdata __panic_msg_1
    %37 = std.ptr_to_i64 %36
    std.call_runtime @mrt_panic %37
  __range_ok_0:
    func.return %30
  }
}
=== arm64
module {
  func @pick(v: i64) -> u8 {
  entry:
    arm64.prologue stack_size=48
    arm64.str x0, [x29, #-8]
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #200000
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x0, x2, x3
    arm64.cmp x0, #0
    arm64.b.ne pick.__range_panic_0
    arm64.b pick.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_0
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.ldr x0, [x29, #-8]
    arm64.str x0, [x29, #-16]
    arm64.ldr x1, [x29, #-16]
    arm64.mov x2, #3800
    arm64.cmp x1, x2
    arm64.cset x3, lt
    arm64.cmp x3, #0
    arm64.b.ne pick.p_0.dispatch0
    arm64.b pick.p_0.dispatch1
  p_0.dispatch0:
    arm64.mov x0, #1
    arm64.ldr x1, [x29, #-16]
    arm64.sub x2, x1, x0
    arm64.mov x3, #1999
    arm64.cmp x2, x3
    arm64.cset x4, hi
    arm64.cmp x4, #0
    arm64.b.ne pick.p_0.dispatch2
    arm64.b pick.p_0.case0
  p_0.dispatch2:
    arm64.mov x0, #2500
    arm64.ldr x1, [x29, #-16]
    arm64.sub x2, x1, x0
    arm64.mov x3, #1000
    arm64.cmp x2, x3
    arm64.cset x4, hi
    arm64.cmp x4, #0
    arm64.b.ne pick.p_0.dispatch3
    arm64.b pick.p_0.case1
  p_0.dispatch3:
    arm64.b pick.p_0.case4
  p_0.dispatch1:
    arm64.mov x0, #3800
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne pick.p_0.dispatch4
    arm64.b pick.p_0.case2
  p_0.dispatch4:
    arm64.mov x0, #3900
    arm64.ldr x1, [x29, #-16]
    arm64.cmp x1, x0
    arm64.cset x2, ne
    arm64.cmp x2, #0
    arm64.b.ne pick.p_0.dispatch5
    arm64.b pick.p_0.case3
  p_0.dispatch5:
    arm64.b pick.p_0.case4
  p_0.case0:
    arm64.mov x0, #1
    arm64.epilogue stack_size=48
    arm64.ret
  p_0.case1:
    arm64.mov x0, #2
    arm64.epilogue stack_size=48
    arm64.ret
  p_0.case2:
    arm64.mov x0, #3
    arm64.epilogue stack_size=48
    arm64.ret
  p_0.case3:
    arm64.mov x0, #4
    arm64.epilogue stack_size=48
    arm64.ret
  p_0.case4:
    arm64.mov x0, #0
    arm64.epilogue stack_size=48
    arm64.ret
  p_0.merge:
  }
  func @main() -> u8 {
  entry:
    arm64.prologue stack_size=16
    arm64.mov x0, #3000
    arm64.bl pick
    arm64.mov x1, #0
    arm64.cmp x0, x1
    arm64.cset x2, lt
    arm64.mov x1, #255
    arm64.cmp x0, x1
    arm64.cset x3, gt
    arm64.orr x1, x2, x3
    arm64.cmp x1, #0
    arm64.b.ne main.__range_panic_0
    arm64.b main.__range_ok_0
  __range_panic_0:
    arm64.adrp_add_symdata x0, __panic_msg_1
    arm64.mov x1, x0
    arm64.mov x0, x1
    arm64.bl mrt_panic
  __range_ok_0:
    arm64.epilogue stack_size=16
    arm64.ret
  }
}
```

### A 4096-slot table dispatches correctly (arm64 bounds-check boundary)

128 dense width-32 range arms cover exactly 0…4095 — a span of 4096, the largest
table the strategy selector admits. On arm64 the bound was once encoded as a `CMP`
immediate, whose 12-bit field cannot hold 4096: the value spilled into the `LSL #12`
shift bit and the check compared against 0, sending every input to the default. This
is the canonical byte-dispatch shape; it must land on the right arm for values across
the whole span and reject the neighbours just outside it. Runs natively on arm64 CI.

<!-- test: dispatch.table-span-4096 -->
```maxon
typealias Full = int(0 to 100000)
typealias Result = int(0 to 200)

function classify(n Full) returns Result
	match n 'm'
		0 to 31 then return 1
		32 to 63 then return 2
		64 to 95 then return 3
		96 to 127 then return 4
		128 to 159 then return 5
		160 to 191 then return 6
		192 to 223 then return 7
		224 to 255 then return 8
		256 to 287 then return 9
		288 to 319 then return 10
		320 to 351 then return 11
		352 to 383 then return 12
		384 to 415 then return 13
		416 to 447 then return 14
		448 to 479 then return 15
		480 to 511 then return 16
		512 to 543 then return 17
		544 to 575 then return 18
		576 to 607 then return 19
		608 to 639 then return 20
		640 to 671 then return 21
		672 to 703 then return 22
		704 to 735 then return 23
		736 to 767 then return 24
		768 to 799 then return 25
		800 to 831 then return 26
		832 to 863 then return 27
		864 to 895 then return 28
		896 to 927 then return 29
		928 to 959 then return 30
		960 to 991 then return 31
		992 to 1023 then return 32
		1024 to 1055 then return 33
		1056 to 1087 then return 34
		1088 to 1119 then return 35
		1120 to 1151 then return 36
		1152 to 1183 then return 37
		1184 to 1215 then return 38
		1216 to 1247 then return 39
		1248 to 1279 then return 40
		1280 to 1311 then return 41
		1312 to 1343 then return 42
		1344 to 1375 then return 43
		1376 to 1407 then return 44
		1408 to 1439 then return 45
		1440 to 1471 then return 46
		1472 to 1503 then return 47
		1504 to 1535 then return 48
		1536 to 1567 then return 49
		1568 to 1599 then return 50
		1600 to 1631 then return 51
		1632 to 1663 then return 52
		1664 to 1695 then return 53
		1696 to 1727 then return 54
		1728 to 1759 then return 55
		1760 to 1791 then return 56
		1792 to 1823 then return 57
		1824 to 1855 then return 58
		1856 to 1887 then return 59
		1888 to 1919 then return 60
		1920 to 1951 then return 61
		1952 to 1983 then return 62
		1984 to 2015 then return 63
		2016 to 2047 then return 64
		2048 to 2079 then return 65
		2080 to 2111 then return 66
		2112 to 2143 then return 67
		2144 to 2175 then return 68
		2176 to 2207 then return 69
		2208 to 2239 then return 70
		2240 to 2271 then return 71
		2272 to 2303 then return 72
		2304 to 2335 then return 73
		2336 to 2367 then return 74
		2368 to 2399 then return 75
		2400 to 2431 then return 76
		2432 to 2463 then return 77
		2464 to 2495 then return 78
		2496 to 2527 then return 79
		2528 to 2559 then return 80
		2560 to 2591 then return 81
		2592 to 2623 then return 82
		2624 to 2655 then return 83
		2656 to 2687 then return 84
		2688 to 2719 then return 85
		2720 to 2751 then return 86
		2752 to 2783 then return 87
		2784 to 2815 then return 88
		2816 to 2847 then return 89
		2848 to 2879 then return 90
		2880 to 2911 then return 91
		2912 to 2943 then return 92
		2944 to 2975 then return 93
		2976 to 3007 then return 94
		3008 to 3039 then return 95
		3040 to 3071 then return 96
		3072 to 3103 then return 97
		3104 to 3135 then return 98
		3136 to 3167 then return 99
		3168 to 3199 then return 100
		3200 to 3231 then return 101
		3232 to 3263 then return 102
		3264 to 3295 then return 103
		3296 to 3327 then return 104
		3328 to 3359 then return 105
		3360 to 3391 then return 106
		3392 to 3423 then return 107
		3424 to 3455 then return 108
		3456 to 3487 then return 109
		3488 to 3519 then return 110
		3520 to 3551 then return 111
		3552 to 3583 then return 112
		3584 to 3615 then return 113
		3616 to 3647 then return 114
		3648 to 3679 then return 115
		3680 to 3711 then return 116
		3712 to 3743 then return 117
		3744 to 3775 then return 118
		3776 to 3807 then return 119
		3808 to 3839 then return 120
		3840 to 3871 then return 121
		3872 to 3903 then return 122
		3904 to 3935 then return 123
		3936 to 3967 then return 124
		3968 to 3999 then return 125
		4000 to 4031 then return 126
		4032 to 4063 then return 127
		4064 to 4095 then return 128
		default then return 0
	end 'm'
end 'classify'

function main() returns ExitCode
	// walk the two edges, both interval boundaries, and the just-outside neighbours
	if classify(0) != 1 'a'
		return 101
	end 'a'
	if classify(31) != 1 'b'
		return 102
	end 'b'
	if classify(32) != 2 'c'
		return 103
	end 'c'
	if classify(2048) != 65 'd'
		return 104
	end 'd'
	if classify(4095) != 128 'e'
		return 105
	end 'e'
	if classify(4096) != 0 'f'
		return 106
	end 'f'
	return 0
end 'main'
```
```exitcode
0
```
