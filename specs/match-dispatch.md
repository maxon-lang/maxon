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
    maxon.assign %0 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [100:m_0.case0, 101:m_0.case1, 102:m_0.case2, 103:m_0.case3, 104:m_0.case4, 105:m_0.case5] default=m_0.case6
  m_0.case0:
    %4 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %4
  m_0.case1:
    %8 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %8
  m_0.case2:
    %12 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %12
  m_0.case3:
    %16 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %16
  m_0.case4:
    %20 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %20
  m_0.case5:
    %24 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %24
  m_0.case6:
    %25 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %25
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %26 = maxon.literal {value = 103 : i64}
    %27 = maxon.call @classify %26
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 4294967295 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-table-biased.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %27
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, __match_m_0
    %1 = memref.load __match_m_0 : i64
    %2 = arith.constant {value = 100 : i64}
    %3 = arith.subi %1, %2
    cf.switch %3 [6 cases] default=m_0.case6
  m_0.case0:
    %4 = arith.constant {value = 1 : i64}
    func.return %4
  m_0.case1:
    %5 = arith.constant {value = 2 : i64}
    func.return %5
  m_0.case2:
    %6 = arith.constant {value = 3 : i64}
    func.return %6
  m_0.case3:
    %7 = arith.constant {value = 4 : i64}
    func.return %7
  m_0.case4:
    %8 = arith.constant {value = 5 : i64}
    func.return %8
  m_0.case5:
    %9 = arith.constant {value = 6 : i64}
    func.return %9
  m_0.case6:
    %10 = arith.constant {value = 0 : i64}
    func.return %10
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %11 = arith.constant {value = 103 : i64}
    %12 = func.call @classify %11
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi lt %12, %13
    %15 = arith.constant {value = 4294967295 : i64}
    %16 = arith.cmpi gt %12, %15
    %17 = arith.ori1 %14, %16
    cf.cond_br %17 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %18 = memref.lea_symdata __panic_msg_0
    %19 = std.ptr_to_i64 %18
    std.call_runtime @mrt_panic %19
  __range_ok_0:
    func.return %12
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 100
    x64.sub rax, rcx
    x64.jump_table rax, 6 cases, default=classify.m_0.case6
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
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
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
    maxon.assign %0 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 2..5:m_0.case1, 6:m_0.case2, 7:m_0.case3] default=m_0.case4
  m_0.case0:
    %4 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %4
  m_0.case1:
    %12 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %12
  m_0.case2:
    %16 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %16
  m_0.case3:
    %20 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %20
  m_0.case4:
    %21 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %21
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %22 = maxon.literal {value = 4 : i64}
    %23 = maxon.call @classify %22
    %24 = maxon.literal {value = 0 : i64}
    %25 = maxon.binop %23, %24 {op = lt}
    %26 = maxon.literal {value = 4294967295 : i64}
    %27 = maxon.binop %23, %26 {op = gt}
    %28 = maxon.binop %25, %27 {op = or}
    maxon.cond_br %28 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-range-arm-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %23
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, __match_m_0
    %1 = memref.load __match_m_0 : i64
    %2 = arith.constant {value = 1 : i64}
    %3 = arith.subi %1, %2
    cf.switch %3 [7 cases] default=m_0.case4
  m_0.case0:
    %4 = arith.constant {value = 1 : i64}
    func.return %4
  m_0.case1:
    %5 = arith.constant {value = 2 : i64}
    func.return %5
  m_0.case2:
    %6 = arith.constant {value = 3 : i64}
    func.return %6
  m_0.case3:
    %7 = arith.constant {value = 4 : i64}
    func.return %7
  m_0.case4:
    %8 = arith.constant {value = 0 : i64}
    func.return %8
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %9 = arith.constant {value = 4 : i64}
    %10 = func.call @classify %9
    %11 = arith.constant {value = 0 : i64}
    %12 = arith.cmpi lt %10, %11
    %13 = arith.constant {value = 4294967295 : i64}
    %14 = arith.cmpi gt %10, %13
    %15 = arith.ori1 %12, %14
    cf.cond_br %15 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %16 = memref.lea_symdata __panic_msg_0
    %17 = std.ptr_to_i64 %16
    std.call_runtime @mrt_panic %17
  __range_ok_0:
    func.return %10
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 1
    x64.sub rax, rcx
    x64.jump_table rax, 7 cases, default=classify.m_0.case4
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
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
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
    maxon.assign %0 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [1:m_0.case0, 17:m_0.case1, 290:m_0.case2, 4000:m_0.case3, 51234:m_0.case4, 99999:m_0.case5] default=m_0.case6
  m_0.case0:
    %4 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %4
  m_0.case1:
    %8 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %8
  m_0.case2:
    %12 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %12
  m_0.case3:
    %16 = maxon.literal {value = 4 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %16
  m_0.case4:
    %20 = maxon.literal {value = 5 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %20
  m_0.case5:
    %24 = maxon.literal {value = 6 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %24
  m_0.case6:
    %25 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %25
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %26 = maxon.literal {value = 51234 : i64}
    %27 = maxon.call @classify %26
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 4294967295 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-binary-search.test:18: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %27
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, __match_m_0
    %1 = memref.load __match_m_0 : i64
    %2 = arith.constant {value = 4000 : i64}
    %3 = arith.cmpi lt %1, %2
    cf.cond_br %3 [then: m_0.dispatch0, else: m_0.dispatch1]
  m_0.dispatch0:
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.cmpi ne %1, %4
    cf.cond_br %5 [then: m_0.dispatch2, else: m_0.case0]
  m_0.dispatch2:
    %6 = arith.constant {value = 17 : i64}
    %7 = arith.cmpi ne %1, %6
    cf.cond_br %7 [then: m_0.dispatch3, else: m_0.case1]
  m_0.dispatch3:
    %8 = arith.constant {value = 290 : i64}
    %9 = arith.cmpi ne %1, %8
    cf.cond_br %9 [then: m_0.dispatch4, else: m_0.case2]
  m_0.dispatch4:
    cf.br m_0.case6
  m_0.dispatch1:
    %10 = arith.constant {value = 4000 : i64}
    %11 = arith.cmpi ne %1, %10
    cf.cond_br %11 [then: m_0.dispatch5, else: m_0.case3]
  m_0.dispatch5:
    %12 = arith.constant {value = 51234 : i64}
    %13 = arith.cmpi ne %1, %12
    cf.cond_br %13 [then: m_0.dispatch6, else: m_0.case4]
  m_0.dispatch6:
    %14 = arith.constant {value = 99999 : i64}
    %15 = arith.cmpi ne %1, %14
    cf.cond_br %15 [then: m_0.dispatch7, else: m_0.case5]
  m_0.dispatch7:
    cf.br m_0.case6
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
    %19 = arith.constant {value = 4 : i64}
    func.return %19
  m_0.case4:
    %20 = arith.constant {value = 5 : i64}
    func.return %20
  m_0.case5:
    %21 = arith.constant {value = 6 : i64}
    func.return %21
  m_0.case6:
    %22 = arith.constant {value = 0 : i64}
    func.return %22
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %23 = arith.constant {value = 51234 : i64}
    %24 = func.call @classify %23
    %25 = arith.constant {value = 0 : i64}
    %26 = arith.cmpi lt %24, %25
    %27 = arith.constant {value = 4294967295 : i64}
    %28 = arith.cmpi gt %24, %27
    %29 = arith.ori1 %26, %28
    cf.cond_br %29 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %30 = memref.lea_symdata __panic_msg_0
    %31 = std.ptr_to_i64 %30
    std.call_runtime @mrt_panic %31
  __range_ok_0:
    func.return %24
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 4000
    x64.cmp rax, rcx
    x64.jge classify.m_0.dispatch1
  m_0.dispatch0:
    x64.mov rax, 1
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case0
  m_0.dispatch2:
    x64.mov rax, 17
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case1
  m_0.dispatch3:
    x64.mov rax, 290
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case2
  m_0.dispatch4:
    x64.jmp classify.m_0.case6
  m_0.dispatch1:
    x64.mov rax, 4000
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case3
  m_0.dispatch5:
    x64.mov rax, 51234
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case4
  m_0.dispatch6:
    x64.mov rax, 99999
    x64.mov rcx, [rbp-8]
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
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
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
    maxon.assign %0 {var = __match_m_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_m_0 [7:m_0.case0, 8:m_0.case1, 9:m_0.case2] default=m_0.case3
  m_0.case0:
    %4 = maxon.literal {value = 1 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %4
  m_0.case1:
    %8 = maxon.literal {value = 2 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %8
  m_0.case2:
    %12 = maxon.literal {value = 3 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %12
  m_0.case3:
    %13 = maxon.literal {value = 0 : i64}
    maxon.scope_end [n, __match_m_0]
    maxon.return %13
  m_0.merge:
  }
  func @main() -> i64 {
  entry:
    %14 = maxon.literal {value = 8 : i64}
    %15 = maxon.call @classify %14
    %16 = maxon.literal {value = 0 : i64}
    %17 = maxon.binop %15, %16 {op = lt}
    %18 = maxon.literal {value = 4294967295 : i64}
    %19 = maxon.binop %15, %18 {op = gt}
    %20 = maxon.binop %17, %19 {op = or}
    maxon.cond_br %20 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-linear-under-threshold.test:15: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %15
  }
}
=== standard
module {
  func @classify(n: i64) -> u8 {
  entry:
    %0 = func.param n : StdI64
    memref.store %0, __match_m_0
    %1 = memref.load __match_m_0 : i64
    %2 = arith.constant {value = 7 : i64}
    %3 = arith.cmpi ne %1, %2
    cf.cond_br %3 [then: m_0.dispatch0, else: m_0.case0]
  m_0.dispatch0:
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.cmpi ne %1, %4
    cf.cond_br %5 [then: m_0.dispatch1, else: m_0.case1]
  m_0.dispatch1:
    %6 = arith.constant {value = 9 : i64}
    %7 = arith.cmpi ne %1, %6
    cf.cond_br %7 [then: m_0.dispatch2, else: m_0.case2]
  m_0.dispatch2:
    cf.br m_0.case3
  m_0.case0:
    %8 = arith.constant {value = 1 : i64}
    func.return %8
  m_0.case1:
    %9 = arith.constant {value = 2 : i64}
    func.return %9
  m_0.case2:
    %10 = arith.constant {value = 3 : i64}
    func.return %10
  m_0.case3:
    %11 = arith.constant {value = 0 : i64}
    func.return %11
  m_0.merge:
  }
  func @main() -> u32 {
  entry:
    %12 = arith.constant {value = 8 : i64}
    %13 = func.call @classify %12
    %14 = arith.constant {value = 0 : i64}
    %15 = arith.cmpi lt %13, %14
    %16 = arith.constant {value = 4294967295 : i64}
    %17 = arith.cmpi gt %13, %16
    %18 = arith.ori1 %15, %17
    cf.cond_br %18 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %19 = memref.lea_symdata __panic_msg_0
    %20 = std.ptr_to_i64 %19
    std.call_runtime @mrt_panic %20
  __range_ok_0:
    func.return %13
  }
}
=== x86
module {
  func @classify(n: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 7
    x64.cmp rax, rcx
    x64.je classify.m_0.case0
  m_0.dispatch0:
    x64.mov rax, 8
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je classify.m_0.case1
  m_0.dispatch1:
    x64.mov rax, 9
    x64.mov rcx, [rbp-8]
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
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
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
    maxon.assign %0 {var = __match_p_0} {kind = i64} {decl = 1 : i1}
    maxon.switch __match_p_0 [1..2000:p_0.case0, 2500..3500:p_0.case1, 3800:p_0.case2, 3900:p_0.case3] default=p_0.case4
  p_0.case0:
    %8 = maxon.literal {value = 1 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %8
  p_0.case1:
    %16 = maxon.literal {value = 2 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %16
  p_0.case2:
    %20 = maxon.literal {value = 3 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %20
  p_0.case3:
    %24 = maxon.literal {value = 4 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %24
  p_0.case4:
    %25 = maxon.literal {value = 0 : i64}
    maxon.scope_end [v, __match_p_0]
    maxon.return %25
  p_0.merge:
  }
  func @main() -> i64 {
  entry:
    %26 = maxon.literal {value = 3000 : i64}
    %27 = maxon.call @pick %26
    %28 = maxon.literal {value = 0 : i64}
    %29 = maxon.binop %27, %28 {op = lt}
    %30 = maxon.literal {value = 4294967295 : i64}
    %31 = maxon.binop %27, %30 {op = gt}
    %32 = maxon.binop %29, %31 {op = or}
    maxon.cond_br %32 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at dispatch.shape-wide-arms-no-table.test:16: Range check failed: value outside typealias 'ExitCode'"
  __range_ok_0:
    maxon.scope_end []
    maxon.return %27
  }
}
=== standard
module {
  func @pick(v: i64) -> u8 {
  entry:
    %0 = func.param v : StdI64
    memref.store %0, __match_p_0
    %1 = memref.load __match_p_0 : i64
    %2 = arith.constant {value = 3800 : i64}
    %3 = arith.cmpi lt %1, %2
    cf.cond_br %3 [then: p_0.dispatch0, else: p_0.dispatch1]
  p_0.dispatch0:
    %4 = arith.constant {value = 1 : i64}
    %5 = arith.subi %1, %4
    %6 = arith.constant {value = 1999 : i64}
    %7 = arith.cmpui ugt %5, %6
    cf.cond_br %7 [then: p_0.dispatch2, else: p_0.case0]
  p_0.dispatch2:
    %8 = arith.constant {value = 2500 : i64}
    %9 = arith.subi %1, %8
    %10 = arith.constant {value = 1000 : i64}
    %11 = arith.cmpui ugt %9, %10
    cf.cond_br %11 [then: p_0.dispatch3, else: p_0.case1]
  p_0.dispatch3:
    cf.br p_0.case4
  p_0.dispatch1:
    %12 = arith.constant {value = 3800 : i64}
    %13 = arith.cmpi ne %1, %12
    cf.cond_br %13 [then: p_0.dispatch4, else: p_0.case2]
  p_0.dispatch4:
    %14 = arith.constant {value = 3900 : i64}
    %15 = arith.cmpi ne %1, %14
    cf.cond_br %15 [then: p_0.dispatch5, else: p_0.case3]
  p_0.dispatch5:
    cf.br p_0.case4
  p_0.case0:
    %16 = arith.constant {value = 1 : i64}
    func.return %16
  p_0.case1:
    %17 = arith.constant {value = 2 : i64}
    func.return %17
  p_0.case2:
    %18 = arith.constant {value = 3 : i64}
    func.return %18
  p_0.case3:
    %19 = arith.constant {value = 4 : i64}
    func.return %19
  p_0.case4:
    %20 = arith.constant {value = 0 : i64}
    func.return %20
  p_0.merge:
  }
  func @main() -> u32 {
  entry:
    %21 = arith.constant {value = 3000 : i64}
    %22 = func.call @pick %21
    %23 = arith.constant {value = 0 : i64}
    %24 = arith.cmpi lt %22, %23
    %25 = arith.constant {value = 4294967295 : i64}
    %26 = arith.cmpi gt %22, %25
    %27 = arith.ori1 %24, %26
    cf.cond_br %27 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %28 = memref.lea_symdata __panic_msg_0
    %29 = std.ptr_to_i64 %28
    std.call_runtime @mrt_panic %29
  __range_ok_0:
    func.return %22
  }
}
=== x86
module {
  func @pick(v: i64) -> u8 {
  entry:
    x64.prologue stack_size=16
    x64.mov [rbp-8], rcx
    x64.mov rax, [rbp-8]
    x64.mov rcx, 3800
    x64.cmp rax, rcx
    x64.jge pick.p_0.dispatch1
  p_0.dispatch0:
    x64.mov rax, 1
    x64.mov rdx, [rbp-8]
    x64.sub rdx, rax
    x64.mov rbx, 1999
    x64.cmp rdx, rbx
    x64.jbe pick.p_0.case0
  p_0.dispatch2:
    x64.mov rax, 2500
    x64.mov rdx, [rbp-8]
    x64.sub rdx, rax
    x64.mov rbx, 1000
    x64.cmp rdx, rbx
    x64.jbe pick.p_0.case1
  p_0.dispatch3:
    x64.jmp pick.p_0.case4
  p_0.dispatch1:
    x64.mov rax, 3800
    x64.mov rcx, [rbp-8]
    x64.cmp rcx, rax
    x64.je pick.p_0.case2
  p_0.dispatch4:
    x64.mov rax, 3900
    x64.mov rcx, [rbp-8]
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
    x64.lea_symdata rax, [__panic_msg_0]
    x64.mov rcx, rax
    x64.call mrt_panic
  __range_ok_0:
    x64.epilogue
    x64.ret
  }
}
```
