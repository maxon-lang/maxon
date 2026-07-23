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
	if rank(Priority.p0) == 1 'a' n = n + 1 end 'a'
	if rank(Priority.p1) == 2 'b' n = n + 1 end 'b'
	if rank(Priority.p2) == 3 'c' n = n + 1 end 'c'
	if rank(Priority.p3) == 4 'd' n = n + 1 end 'd'
	if rank(Priority.p4) == 5 'e' n = n + 1 end 'e'
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
	if kind(Day.mon) == 1 'a' n = n + 1 end 'a'
	if kind(Day.tue) == 2 'b' n = n + 1 end 'b'
	if kind(Day.wed) == 3 'c' n = n + 1 end 'c'
	if kind(Day.thu) == 3 'd' n = n + 1 end 'd'
	if kind(Day.fri) == 3 'e' n = n + 1 end 'e'
	if kind(Day.sat) == 4 'f' n = n + 1 end 'f'
	if kind(Day.sun) == 5 'g' n = n + 1 end 'g'
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
	if idx(Color.red) == 1 'a' n = n + 1 end 'a'
	if idx(Color.green) == 2 'b' n = n + 1 end 'b'
	if idx(Color.blue) == 3 'c' n = n + 1 end 'c'
	if idx(Color.cyan) == 4 'd' n = n + 1 end 'd'
	if idx(Color.magenta) == 5 'e' n = n + 1 end 'e'
	if idx(Color.yellow) == 6 'f' n = n + 1 end 'f'
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
	if sides(Shape.circle(1)) == 0 'a' n = n + 1 end 'a'
	if sides(Shape.square(2)) == 44 'b' n = n + 1 end 'b'
	if sides(Shape.tri(3)) == 33 'c' n = n + 1 end 'c'
	if sides(Shape.pent(4)) == 55 'd' n = n + 1 end 'd'
	if sides(Shape.hex(5)) == 66 'e' n = n + 1 end 'e'
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
	if ored(1) == 1 'a' n = n + 1 end 'a'
	if ored(9) == 1 'b' n = n + 1 end 'b'
	if ored(3) == 2 'c' n = n + 1 end 'c'
	if ored(7) == 2 'd' n = n + 1 end 'd'
	if ored(5) == 3 'e' n = n + 1 end 'e'
	if ored(4) == 9 'f' n = n + 1 end 'f'
	if ored(2) == 9 'g' n = n + 1 end 'g'
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
	if sparse(10) == 1 'h1' n = n + 1 end 'h1'
	if sparse(30) == 3 'h3' n = n + 1 end 'h3'
	if sparse(50) == 5 'h5' n = n + 1 end 'h5'
	if sparse(25) == 9 'm1' n = n + 1 end 'm1'
	if sparse(35) == 9 'm2' n = n + 1 end 'm2'
	if sparse(9) == 9 'o1' n = n + 1 end 'o1'
	if sparse(11) == 9 'o2' n = n + 1 end 'o2'
	if sparse(51) == 9 'o3' n = n + 1 end 'o3'
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
	if over(55) == 1 'a' n = n + 1 end 'a'
	if over(50) == 1 'b' n = n + 1 end 'b'
	if over(60) == 1 'c' n = n + 1 end 'c'
	if over(1) == 1 'd' n = n + 1 end 'd'
	if over(100) == 1 'e' n = n + 1 end 'e'
	if over(200) == 3 'f' n = n + 1 end 'f'
	if over(400) == 5 'g' n = n + 1 end 'g'
	if over(150) == 9 'h' n = n + 1 end 'h'
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
	if band(0 - 5000) == 1 'a' n = n + 1 end 'a'
	if band(0 - 100) == 1 'b' n = n + 1 end 'b'
	if band(0 - 99) == 2 'c' n = n + 1 end 'c'
	if band(0 - 1) == 2 'd' n = n + 1 end 'd'
	if band(0) == 3 'e' n = n + 1 end 'e'
	if band(1) == 4 'f' n = n + 1 end 'f'
	if band(100) == 4 'g' n = n + 1 end 'g'
	if band(101) == 5 'h' n = n + 1 end 'h'
	if band(999999) == 5 'i' n = n + 1 end 'i'
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
	if perms(1) == 31 'a' n = n + 1 end 'a'
	if perms(3) == 28 'b' n = n + 1 end 'b'
	if perms(5) == 16 'c' n = n + 1 end 'c'
	if perms(6) == 0 'd' n = n + 1 end 'd'
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
	if pick(1) == 10 'a' n = n + 1 end 'a'
	if pick(3) == 30 'b' n = n + 1 end 'b'
	if pick(5) == 50 'c' n = n + 1 end 'c'
	if pick(99) == 99 'd' n = n + 1 end 'd'
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
	if only(5) == 7 'a' n = n + 1 end 'a'
	if only(0) == 7 'b' n = n + 1 end 'b'
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
	if ftd(1) == 115 'a' n = n + 1 end 'a'
	if ftd(3) == 112 'b' n = n + 1 end 'b'
	if ftd(4) == 108 'c' n = n + 1 end 'c'
	if ftd(9) == 100 'd' n = n + 1 end 'd'
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
	if greet("alice") == 1 'a' n = n + 1 end 'a'
	if greet("carol") == 3 'b' n = n + 1 end 'b'
	if greet("erin") == 5 'c' n = n + 1 end 'c'
	if greet("zoe") == 9 'd' n = n + 1 end 'd'
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
