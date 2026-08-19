---
feature: match-dispatch
status: experimental
keywords: match, dispatch, binary-search, interval, range, or, enum
category: control-flow
---
# Match Dispatch (binary search over intervals)

## Documentation

A `match` over an integer-like scrutinee — an enum/union tag, a scalar `int`/`char`/`bool`, an
`ExitCode`, or a ranged-int alias — is lowered by first reducing every arm to a sorted, disjoint list of
closed `[lo, hi]` value intervals (a single value is `[v, v]`, a range is `[lo, hi]`, an `or`-list is one
interval per alternative, and an open bound is `i64.min`/`i64.max`). Overlapping intervals are clipped so
the **earlier arm wins**, matching the top-to-bottom order a linear chain would test them in.

The strategy then follows the interval COUNT:

- **fewer than 4 intervals** — a linear compare chain (a small match pays nothing for a dispatch structure).
- **4 or more intervals** — a balanced **binary search**: each internal node compares the scrutinee
  against a pivot and branches to a lower/upper sub-test; each leaf tests one interval (an `==` for a
  point, a two-sided `>=`/`<=` for a range) and branches to the arm body or on toward the default.

The scrutinee is loaded ONCE and every comparison reads it from a register. A `String` scrutinee (whose
only comparison is a runtime byte `==`) and a `float` scrutinee (whose IEEE bits are not monotonic in
value) are not orderable as one `i64`, so they keep the linear compare chain unchanged.

These are behaviour tests: each compiles a program, runs it, and asserts its exit code. A misrouted
dispatch computes a wrong answer and fails.

## Tests

<!-- test: dispatch.dense-nonzero-enum -->
```maxon
enum Priority
	p0 = 10
	p1 = 11
	p2 = 12
	p3 = 13
	p4 = 14
end 'Priority'

function rank(p Priority) returns ExitCode
	return match p 'm'
		p0 gives 1
		p1 gives 2
		p2 gives 3
		p3 gives 4
		p4 gives 5
	end 'm'
end 'rank'

function main() returns ExitCode
	var n = 0 as ExitCode
	if rank(Priority.p0) == 1 'a'
		n = n + 1
	end 'a'
	if rank(Priority.p1) == 2 'b'
		n = n + 1
	end 'b'
	if rank(Priority.p2) == 3 'c'
		n = n + 1
	end 'c'
	if rank(Priority.p3) == 4 'd'
		n = n + 1
	end 'd'
	if rank(Priority.p4) == 5 'e'
		n = n + 1
	end 'e'
	return n
end 'main'
```
```exitcode
5
```

<!-- test: dispatch.enum-range-arm -->
```maxon
enum Day
	mon
	tue
	wed
	thu
	fri
	sat
	sun
end 'Day'

function kind(d Day) returns ExitCode
	return match d 'm'
		mon gives 1
		tue gives 2
		wed to fri gives 3
		sat gives 4
		sun gives 5
	end 'm'
end 'kind'

function main() returns ExitCode
	var n = 0 as ExitCode
	if kind(Day.mon) == 1 'a'
		n = n + 1
	end 'a'
	if kind(Day.tue) == 2 'b'
		n = n + 1
	end 'b'
	if kind(Day.wed) == 3 'c'
		n = n + 1
	end 'c'
	if kind(Day.thu) == 3 'd'
		n = n + 1
	end 'd'
	if kind(Day.fri) == 3 'e'
		n = n + 1
	end 'e'
	if kind(Day.sat) == 4 'f'
		n = n + 1
	end 'f'
	if kind(Day.sun) == 5 'g'
		n = n + 1
	end 'g'
	return n
end 'main'
```
```exitcode
7
```

<!-- test: dispatch.exhaustive-enum-no-default -->
```maxon
enum Color
	red
	green
	blue
	cyan
	magenta
	yellow
end 'Color'

function idx(c Color) returns ExitCode
	return match c 'm'
		red gives 1
		green gives 2
		blue gives 3
		cyan gives 4
		magenta gives 5
		yellow gives 6
	end 'm'
end 'idx'

function main() returns ExitCode
	var n = 0 as ExitCode
	if idx(Color.red) == 1 'a'
		n = n + 1
	end 'a'
	if idx(Color.green) == 2 'b'
		n = n + 1
	end 'b'
	if idx(Color.blue) == 3 'c'
		n = n + 1
	end 'c'
	if idx(Color.cyan) == 4 'd'
		n = n + 1
	end 'd'
	if idx(Color.magenta) == 5 'e'
		n = n + 1
	end 'e'
	if idx(Color.yellow) == 6 'f'
		n = n + 1
	end 'f'
	return n
end 'main'
```
```exitcode
6
```

<!-- test: dispatch.boxed-union-tree -->
```maxon
typealias N = int(0 to 1000)

union Shape
	circle(r N)
	square(s N)
	tri(a N)
	pent(b N)
	hex(c N)
end 'Shape'

function sides(sh Shape) returns ExitCode
	return match sh 'm'
		circle gives 0
		square gives 44
		tri gives 33
		pent gives 55
		hex gives 66
	end 'm'
end 'sides'

function main() returns ExitCode
	var n = 0 as ExitCode
	if sides(Shape.circle(1)) == 0 'a'
		n = n + 1
	end 'a'
	if sides(Shape.square(2)) == 44 'b'
		n = n + 1
	end 'b'
	if sides(Shape.tri(3)) == 33 'c'
		n = n + 1
	end 'c'
	if sides(Shape.pent(4)) == 55 'd'
		n = n + 1
	end 'd'
	if sides(Shape.hex(5)) == 66 'e'
		n = n + 1
	end 'e'
	return n
end 'main'
```
```exitcode
5
```

<!-- test: dispatch.or-list-nonadjacent -->
```maxon
typealias N = int(i64.min to i64.max)

function ored(x N) returns N
	return match x 'c'
		1 or 9 gives 1
		3 or 7 gives 2
		5 gives 3
		default gives 9
	end 'c'
end 'ored'

function main() returns ExitCode
	var n = 0 as N
	if ored(1) == 1 'a'
		n = n + 1
	end 'a'
	if ored(9) == 1 'b'
		n = n + 1
	end 'b'
	if ored(3) == 2 'c'
		n = n + 1
	end 'c'
	if ored(7) == 2 'd'
		n = n + 1
	end 'd'
	if ored(5) == 3 'e'
		n = n + 1
	end 'e'
	if ored(4) == 9 'f'
		n = n + 1
	end 'f'
	if ored(2) == 9 'g'
		n = n + 1
	end 'g'
	return n as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: dispatch.sparse-hits-misses -->
```maxon
typealias N = int(i64.min to i64.max)

function sparse(x N) returns N
	return match x 'c'
		10 gives 1
		20 gives 2
		30 gives 3
		40 gives 4
		50 gives 5
		default gives 9
	end 'c'
end 'sparse'

function main() returns ExitCode
	var n = 0 as N
	if sparse(10) == 1 'h1'
		n = n + 1
	end 'h1'
	if sparse(30) == 3 'h3'
		n = n + 1
	end 'h3'
	if sparse(50) == 5 'h5'
		n = n + 1
	end 'h5'
	if sparse(25) == 9 'm1'
		n = n + 1
	end 'm1'
	if sparse(35) == 9 'm2'
		n = n + 1
	end 'm2'
	if sparse(9) == 9 'o1'
		n = n + 1
	end 'o1'
	if sparse(11) == 9 'o2'
		n = n + 1
	end 'o2'
	if sparse(51) == 9 'o3'
		n = n + 1
	end 'o3'
	return n as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: dispatch.overlapping-first-wins -->
```maxon
typealias N = int(i64.min to i64.max)

function over(x N) returns N
	return match x 'c'
		1 to 100 gives 1
		50 to 60 gives 2
		200 gives 3
		300 gives 4
		400 gives 5
		default gives 9
	end 'c'
end 'over'

function main() returns ExitCode
	var n = 0 as N
	if over(55) == 1 'a'
		n = n + 1
	end 'a'
	if over(50) == 1 'b'
		n = n + 1
	end 'b'
	if over(60) == 1 'c'
		n = n + 1
	end 'c'
	if over(1) == 1 'd'
		n = n + 1
	end 'd'
	if over(100) == 1 'e'
		n = n + 1
	end 'e'
	if over(200) == 3 'f'
		n = n + 1
	end 'f'
	if over(400) == 5 'g'
		n = n + 1
	end 'g'
	if over(150) == 9 'h'
		n = n + 1
	end 'h'
	return n as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: dispatch.negative-extreme -->
```maxon
typealias N = int(i64.min to i64.max)

function band(x N) returns N
	return match x 'c'
		min to -100 gives 1
		-99 to -1 gives 2
		0 gives 3
		1 to 100 gives 4
		101 to max gives 5
		default gives 9
	end 'c'
end 'band'

function main() returns ExitCode
	var n = 0 as N
	if band(0 - 5000) == 1 'a'
		n = n + 1
	end 'a'
	if band(0 - 100) == 1 'b'
		n = n + 1
	end 'b'
	if band(0 - 99) == 2 'c'
		n = n + 1
	end 'c'
	if band(0 - 1) == 2 'd'
		n = n + 1
	end 'd'
	if band(0) == 3 'e'
		n = n + 1
	end 'e'
	if band(1) == 4 'f'
		n = n + 1
	end 'f'
	if band(100) == 4 'g'
		n = n + 1
	end 'g'
	if band(101) == 5 'h'
		n = n + 1
	end 'h'
	if band(999999) == 5 'i'
		n = n + 1
	end 'i'
	return n as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: dispatch.fallthrough-tree-carried -->
```maxon
typealias N = int(0 to 1000000)

function perms(role N) returns N
	var p = 0 as N
	match role 'auth'
		1 then p = p + 1 and fallthrough
		2 then p = p + 2 and fallthrough
		3 then p = p + 4 and fallthrough
		4 then p = p + 8 and fallthrough
		5 then p = p + 16
		default then p = 0
	end 'auth'
	return p
end 'perms'

function main() returns ExitCode
	var n = 0 as N
	if perms(1) == 31 'a'
		n = n + 1
	end 'a'
	if perms(3) == 28 'b'
		n = n + 1
	end 'b'
	if perms(5) == 16 'c'
		n = n + 1
	end 'c'
	if perms(6) == 0 'd'
		n = n + 1
	end 'd'
	return n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: dispatch.carried-var-merge-tree -->
```maxon
typealias N = int(0 to 1000)

function pick(x N) returns N
	var r = 0 as N
	match x 'c'
		1 then r = 10
		2 then r = 20
		3 then r = 30
		4 then r = 40
		5 then r = 50
		default then r = 99
	end 'c'
	return r
end 'pick'

function main() returns ExitCode
	var n = 0 as N
	if pick(1) == 10 'a'
		n = n + 1
	end 'a'
	if pick(3) == 30 'b'
		n = n + 1
	end 'b'
	if pick(5) == 50 'c'
		n = n + 1
	end 'c'
	if pick(99) == 99 'd'
		n = n + 1
	end 'd'
	return n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: dispatch.default-only -->
```maxon
typealias N = int(0 to 100)

function only(x N) returns N
	return match x 'c'
		default gives 7
	end 'c'
end 'only'

function main() returns ExitCode
	var n = 0 as N
	if only(5) == 7 'a'
		n = n + 1
	end 'a'
	if only(0) == 7 'b'
		n = n + 1
	end 'b'
	return n as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: dispatch.fallthrough-to-default-tree -->
```maxon
typealias N = int(0 to 1000)

function ftd(x N) returns N
	var r = 0 as N
	match x 'c'
		1 then r = r + 1 and fallthrough
		2 then r = r + 2 and fallthrough
		3 then r = r + 4 and fallthrough
		4 then r = r + 8 and fallthrough
		default then r = r + 100
	end 'c'
	return r
end 'ftd'

function main() returns ExitCode
	var n = 0 as N
	if ftd(1) == 115 'a'
		n = n + 1
	end 'a'
	if ftd(3) == 112 'b'
		n = n + 1
	end 'b'
	if ftd(4) == 108 'c'
		n = n + 1
	end 'c'
	if ftd(9) == 100 'd'
		n = n + 1
	end 'd'
	return n as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: dispatch.small-stays-chain -->
```maxon
function main() returns ExitCode
	let x = 2
	match x 'check'
		1 then return 10
		2 then return 20
		3 then return 30
		default then return 0
	end 'check'
end 'main'
```
```exitcode
20
```

<!-- test: dispatch.string-scrutinee-chain -->
```maxon
function greet(name String) returns ExitCode
	return match name 'g'
		"alice" gives 1
		"bob" gives 2
		"carol" gives 3
		"dave" gives 4
		"erin" gives 5
		default gives 9
	end 'g'
end 'greet'

function main() returns ExitCode
	var n = 0 as ExitCode
	if greet("alice") == 1 'a'
		n = n + 1
	end 'a'
	if greet("carol") == 3 'b'
		n = n + 1
	end 'b'
	if greet("erin") == 5 'c'
		n = n + 1
	end 'c'
	if greet("zoe") == 9 'd'
		n = n + 1
	end 'd'
	return n
end 'main'
```
```exitcode
4
```

<!-- test: dispatch.float-scrutinee-chain -->
```maxon
function main() returns ExitCode
	let x = 2.5
	let r = match x 'c'
		0.0 to 1.0 gives 1
		1.0 to 2.0 gives 2
		2.0 to 3.0 gives 3
		3.0 to 4.0 gives 4
		default gives 9
	end 'c'
	return r
end 'main'
```
```exitcode
3
```

<!-- test: dispatch.upto-i64min-empty -->
```maxon
function classify(x int) returns int
	return match x 'm'
		0 upto -9223372036854775808 gives 1
		default gives 0
	end 'm'
end 'classify'

function main() returns ExitCode
	// `0 upto i64.min` is the empty range — nothing is below i64.min. A prior bug decremented
	// the exclusive upper to `i64.min - 1`, which wrapped to `i64.max` and matched every value >= 0.
	var n = 0 as ExitCode
	if classify(5) == 0 'a'
		n = n + 1
	end 'a'
	if classify(0) == 0 'b'
		n = n + 1
	end 'b'
	if classify(-1) == 0 'c'
		n = n + 1
	end 'c'
	if classify(9223372036854775807) == 0 'd'
		n = n + 1
	end 'd'
	return n
end 'main'
```
```exitcode
4
```

<!-- test: dispatch.upto-min-upto-min-empty -->
```maxon
function classify(x int) returns int
	return match x 'm'
		-9223372036854775808 upto -9223372036854775808 gives 1
		default gives 0
	end 'm'
end 'classify'

function main() returns ExitCode
	// `min upto min` is also empty. The wrap bug turned it into `[i64.min, i64.max]`, matching every
	// value; that the ordinary values below still fall to default proves the arm is dead.
	var n = 0 as ExitCode
	if classify(5) == 0 'a'
		n = n + 1
	end 'a'
	if classify(0) == 0 'b'
		n = n + 1
	end 'b'
	if classify(-1) == 0 'c'
		n = n + 1
	end 'c'
	return n
end 'main'
```
```exitcode
3
```

<!-- test: dispatch.upto-exclusive-boundary -->
```maxon
function classify(x int) returns int
	return match x 'm'
		10 upto 20 gives 1
		20 upto 30 gives 2
		30 gives 3
		default gives 0
	end 'm'
end 'classify'

function main() returns ExitCode
	// `upto` excludes its upper: 10..20 covers 10..19, 20..30 covers 20..29, and 30 is its own arm.
	var n = 0 as ExitCode
	if classify(19) == 1 'a'
		n = n + 1
	end 'a'
	if classify(20) == 2 'b'
		n = n + 1
	end 'b'
	if classify(29) == 2 'c'
		n = n + 1
	end 'c'
	if classify(30) == 3 'd'
		n = n + 1
	end 'd'
	if classify(9) == 0 'e'
		n = n + 1
	end 'e'
	return n
end 'main'
```
```exitcode
5
```

<!-- test: dispatch.dense-zero-based-enum -->
```maxon
enum Color
	c0
	c1
	c2
	c3
	c4
	c5
	c6
	c7
end 'Color'

function classify(c Color) returns ExitCode
	return match c 'k'
		c0 gives 10
		c1 gives 11
		c2 gives 12
		c3 gives 13
		c4 gives 14
		c5 gives 15
		c6 gives 16
		c7 gives 17
	end 'k'
end 'classify'

function main() returns ExitCode
	// A zero-based dense enum (span 8, covered 8/8) lowers to an O(1) jump table with NO bias subtract --
	// the enum value IS the index. Every ordinal routes to its own arm.
	var n = 0 as ExitCode
	if classify(Color.c0) == 10 'a'
		n = n + 1
	end 'a'
	if classify(Color.c5) == 15 'b'
		n = n + 1
	end 'b'
	if classify(Color.c7) == 17 'c'
		n = n + 1
	end 'c'
	return n
end 'main'
```
```exitcode
3
```

<!-- test: dispatch.dense-scalar-range-arm -->
```maxon
function scalar(x int) returns int
	return match x 'm'
		0 gives 100
		1 gives 101
		2 to 4 gives 102
		5 gives 105
		7 gives 107
		default gives 999
	end 'm'
end 'scalar'

function main() returns ExitCode
	// A dense scalar table (span 8) whose `2 to 4` range arm fills THREE slots with one block; slot 6 is a
	// hole. Every covered value hits its arm; the hole, the two just-outside-span neighbours, a negative,
	// and a far value all fall to the default.
	var n = 0 as ExitCode
	if scalar(0) == 100 'a'
		n = n + 1
	end 'a'
	if scalar(3) == 102 'b'
		n = n + 1
	end 'b'
	if scalar(4) == 102 'c'
		n = n + 1
	end 'c'
	if scalar(5) == 105 'd'
		n = n + 1
	end 'd'
	if scalar(6) == 999 'e'
		n = n + 1
	end 'e'
	if scalar(7) == 107 'f'
		n = n + 1
	end 'f'
	if scalar(8) == 999 'g'
		n = n + 1
	end 'g'
	if scalar(-1) == 999 'h'
		n = n + 1
	end 'h'
	if scalar(100000) == 999 'i'
		n = n + 1
	end 'i'
	return n as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: dispatch.negative-biased-dense -->
```maxon
function classify(x int) returns int
	return match x 'm'
		-3 gives 1
		-2 gives 2
		-1 gives 3
		0 gives 4
		1 gives 5
		default gives 0
	end 'm'
end 'classify'

function main() returns ExitCode
	// A dense span that CROSSES ZERO (min -3, span 5): the bias subtract is `x - (-3)` = `x + 3`, and the
	// UNSIGNED bounds check routes a below-min value (which wraps to a huge unsigned index) to the default.
	var n = 0 as ExitCode
	if classify(-3) == 1 'a'
		n = n + 1
	end 'a'
	if classify(-1) == 3 'b'
		n = n + 1
	end 'b'
	if classify(0) == 4 'c'
		n = n + 1
	end 'c'
	if classify(1) == 5 'd'
		n = n + 1
	end 'd'
	if classify(-4) == 0 'e'
		n = n + 1
	end 'e'
	if classify(2) == 0 'f'
		n = n + 1
	end 'f'
	return n as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: dispatch.span-4096-boundary -->
```maxon
function bucket(x int) returns int
	// 128 width-32 range arms cover [0, 4095] exactly -- span 4096, the WIDEST table admitted (and the arm64
	// bounds-check imm12 boundary: 4096 does not fit a CMP immediate, so the count is materialised into a
	// scratch register). A `cmp idx, #4096` that spilled into the LSL#12 bit would compare against 0 and
	// send every value to the default; the register form is correct.
	return match x 'm'
		0 to 31 gives 1
		32 to 63 gives 2
		64 to 95 gives 3
		96 to 127 gives 4
		128 to 159 gives 5
		160 to 191 gives 6
		192 to 223 gives 7
		224 to 255 gives 8
		256 to 287 gives 9
		288 to 319 gives 10
		320 to 351 gives 11
		352 to 383 gives 12
		384 to 415 gives 13
		416 to 447 gives 14
		448 to 479 gives 15
		480 to 511 gives 16
		512 to 543 gives 17
		544 to 575 gives 18
		576 to 607 gives 19
		608 to 639 gives 20
		640 to 671 gives 21
		672 to 703 gives 22
		704 to 735 gives 23
		736 to 767 gives 24
		768 to 799 gives 25
		800 to 831 gives 26
		832 to 863 gives 27
		864 to 895 gives 28
		896 to 927 gives 29
		928 to 959 gives 30
		960 to 991 gives 31
		992 to 1023 gives 32
		1024 to 1055 gives 33
		1056 to 1087 gives 34
		1088 to 1119 gives 35
		1120 to 1151 gives 36
		1152 to 1183 gives 37
		1184 to 1215 gives 38
		1216 to 1247 gives 39
		1248 to 1279 gives 40
		1280 to 1311 gives 41
		1312 to 1343 gives 42
		1344 to 1375 gives 43
		1376 to 1407 gives 44
		1408 to 1439 gives 45
		1440 to 1471 gives 46
		1472 to 1503 gives 47
		1504 to 1535 gives 48
		1536 to 1567 gives 49
		1568 to 1599 gives 50
		1600 to 1631 gives 51
		1632 to 1663 gives 52
		1664 to 1695 gives 53
		1696 to 1727 gives 54
		1728 to 1759 gives 55
		1760 to 1791 gives 56
		1792 to 1823 gives 57
		1824 to 1855 gives 58
		1856 to 1887 gives 59
		1888 to 1919 gives 60
		1920 to 1951 gives 61
		1952 to 1983 gives 62
		1984 to 2015 gives 63
		2016 to 2047 gives 64
		2048 to 2079 gives 65
		2080 to 2111 gives 66
		2112 to 2143 gives 67
		2144 to 2175 gives 68
		2176 to 2207 gives 69
		2208 to 2239 gives 70
		2240 to 2271 gives 71
		2272 to 2303 gives 72
		2304 to 2335 gives 73
		2336 to 2367 gives 74
		2368 to 2399 gives 75
		2400 to 2431 gives 76
		2432 to 2463 gives 77
		2464 to 2495 gives 78
		2496 to 2527 gives 79
		2528 to 2559 gives 80
		2560 to 2591 gives 81
		2592 to 2623 gives 82
		2624 to 2655 gives 83
		2656 to 2687 gives 84
		2688 to 2719 gives 85
		2720 to 2751 gives 86
		2752 to 2783 gives 87
		2784 to 2815 gives 88
		2816 to 2847 gives 89
		2848 to 2879 gives 90
		2880 to 2911 gives 91
		2912 to 2943 gives 92
		2944 to 2975 gives 93
		2976 to 3007 gives 94
		3008 to 3039 gives 95
		3040 to 3071 gives 96
		3072 to 3103 gives 97
		3104 to 3135 gives 98
		3136 to 3167 gives 99
		3168 to 3199 gives 100
		3200 to 3231 gives 101
		3232 to 3263 gives 102
		3264 to 3295 gives 103
		3296 to 3327 gives 104
		3328 to 3359 gives 105
		3360 to 3391 gives 106
		3392 to 3423 gives 107
		3424 to 3455 gives 108
		3456 to 3487 gives 109
		3488 to 3519 gives 110
		3520 to 3551 gives 111
		3552 to 3583 gives 112
		3584 to 3615 gives 113
		3616 to 3647 gives 114
		3648 to 3679 gives 115
		3680 to 3711 gives 116
		3712 to 3743 gives 117
		3744 to 3775 gives 118
		3776 to 3807 gives 119
		3808 to 3839 gives 120
		3840 to 3871 gives 121
		3872 to 3903 gives 122
		3904 to 3935 gives 123
		3936 to 3967 gives 124
		3968 to 3999 gives 125
		4000 to 4031 gives 126
		4032 to 4063 gives 127
		4064 to 4095 gives 128
		default gives 0
	end 'm'
end 'bucket'

function main() returns ExitCode
	// value v in [0,4095] routes to bucket (v/32)+1; 4096 and -1 are just outside the span, so the default.
	var n = 0 as ExitCode
	if bucket(0) == 1 'a'
		n = n + 1
	end 'a'
	if bucket(31) == 1 'b'
		n = n + 1
	end 'b'
	if bucket(32) == 2 'c'
		n = n + 1
	end 'c'
	if bucket(2047) == 64 'd'
		n = n + 1
	end 'd'
	if bucket(4095) == 128 'e'
		n = n + 1
	end 'e'
	if bucket(4096) == 0 'f'
		n = n + 1
	end 'f'
	if bucket(-1) == 0 'g'
		n = n + 1
	end 'g'
	return n as ExitCode
end 'main'
```
```exitcode
7
```
