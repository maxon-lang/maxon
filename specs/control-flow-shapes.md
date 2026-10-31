---
feature: control-flow-shapes
status: stable
keywords: control flow, loops, break, continue, early return, match, try, diamonds, merges
category: control-flow
---
# Control-Flow Shapes

## Documentation

Programs whose answer depends on the control-flow graph the compiler lowers them to: nested and labelled
loops, loops with several exits, early returns from inside loops, chained diamonds that merge, dense and
sparse `match` statements, `try ... otherwise` inside a loop, and loop-carried values that exchange places on
every iteration. These are the shapes the wasm backend must emit as structured `block`/`loop`/`if`. Each
case prints exact answers on every lane, so a backend that sends any edge of the graph to the wrong place
prints a different number.

## Tests

<!-- test: control-flow-shapes.nested-loops-break-continue -->
```maxon
typealias Integer = int(i64.min to i64.max)

function nestedSum(limit Integer) returns Integer
	var total = 0 as Integer
	var i = 0 as Integer

	while i < limit 'outer'
		i = i + 1

		if i mod 3 == 0 'skipOuter'
			continue
		end 'skipOuter'

		var j = 0 as Integer

		while true 'inner'
			j = j + 1

			if j > i 'doneInner'
				break
			end 'doneInner'

			if j mod 2 == 0 'skipInner'
				continue
			end 'skipInner'

			if total > 400 'bail'
				break 'outer'
			end 'bail'

			total = total + i * j
		end 'inner'
	end 'outer'

	return total
end 'nestedSum'

function main() returns ExitCode
	print("{nestedSum(6)}\n")
	print("{nestedSum(10)}\n")
	print("{nestedSum(40)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
64
464
464
```

<!-- test: control-flow-shapes.loop-with-several-exits -->
```maxon
typealias Integer = int(i64.min to i64.max)

function exitKind(start Integer, step Integer)
	var v = start
	var n = 0 as Integer
	var kind = 0 as Integer

	while n < 50 'walk'
		n = n + 1
		v = v + step

		if v > 100 'high'
			kind = 1
			break
		end 'high'

		if v < 0 'low'
			kind = 2
			break
		end 'low'

		if v == 37 'exact'
			kind = 3
			break
		end 'exact'
	end 'walk'

	print("{kind} {n} {v}\n")
end 'exitKind'

function main() returns ExitCode
	exitKind(5, step: 7)
	exitKind(2, step: 5)
	exitKind(50, step: -9)
	exitKind(0, step: 1)
	exitKind(60, step: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 14 103
3 7 37
2 6 -4
3 37 37
0 50 60
```

<!-- test: control-flow-shapes.early-return-from-nested-loops -->
```maxon
typealias Integer = int(i64.min to i64.max)

function findPair(target Integer) returns Integer
	var a = 1 as Integer

	while a < 30 'outerA'
		var b = a

		while b < 30 'innerB'
			if a * b == target 'hit'
				return a * 100 + b
			end 'hit'

			if a * b > target 'over'
				break
			end 'over'

			b = b + 1
		end 'innerB'

		a = a + 1
	end 'outerA'

	return 0
end 'findPair'

function main() returns ExitCode
	print("{findPair(36)}\n")
	print("{findPair(143)}\n")
	print("{findPair(841)}\n")
	print("{findPair(37)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
218
1113
2929
0
```

<!-- test: control-flow-shapes.chained-diamonds-merge -->
```maxon
typealias Integer = int(i64.min to i64.max)

function classify(n Integer) returns Integer
	var score = 0 as Integer

	if n mod 2 == 0 'even'
		score = score + 10
	end 'even' else 'odd'
		score = score + 1
	end 'odd'

	if n > 50 'big'
		score = score * 3
	end 'big' else if n > 20 'mid'
		score = score * 2
	end 'mid' else 'small'
		score = score + 7
	end 'small'

	if n mod 5 == 0 'five'
		score = score + 100
	end 'five'

	return score
end 'classify'

function main() returns ExitCode
	print("{classify(4)}\n")
	print("{classify(25)}\n")
	print("{classify(70)}\n")
	print("{classify(55)}\n")
	print("{classify(0)}\n")
	print("{classify(21)}\n")

	var total = 0 as Integer
	var n = 0 as Integer

	while n < 80 'sweep'
		total = total + classify(n)
		n = n + 1
	end 'sweep'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
17
102
130
103
117
2
2662
```

<!-- test: control-flow-shapes.dense-match-with-holes -->
```maxon
typealias Integer = int(i64.min to i64.max)

function pick(n Integer) returns Integer
	var r = 0 as Integer

	match n 'sel'
		0 then r = 11
		1 then r = 22
		2 or
		3 then r = 33
		4 then r = 44
		5 then r = 55
		6 then r = 66
		7 then r = 77
		8 then r = 88
		9 then r = 99
		default then r = -1
	end 'sel'

	return r
end 'pick'

function main() returns ExitCode
	var n = -2 as Integer

	while n <= 11 'each'
		print("{pick(n)}\n")
		n = n + 1
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
-1
11
22
33
33
44
55
66
77
88
99
-1
-1
```

<!-- test: control-flow-shapes.match-in-loop-with-continue-and-fallthrough -->
```maxon
typealias Integer = int(i64.min to i64.max)

function drive(limit Integer) returns Integer
	var acc = 0 as Integer
	var n = 0 as Integer

	while true 'loop'
		n = n + 1

		if n > limit 'done'
			break
		end 'done'

		match n mod 7 'sel'
			0 then acc = acc + 1
			1 then acc = acc + 10 and fallthrough
			2 then acc = acc + 100
			3 then acc = acc + 1000
			4 then continue
			5 then acc = acc + 100000
			default then acc = acc + 7
		end 'sel'

		acc = acc * 2 mod 1000003
	end 'loop'

	return acc
end 'drive'

function main() returns ExitCode
	print("{drive(30)}\n")
	print("{drive(200)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
689572
406170
```

<!-- test: control-flow-shapes.try-otherwise-inside-a-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum CheckError implements Error
	tooBig
	negative
end 'CheckError'

function check(n Integer) returns Integer throws CheckError
	if n > 50 'big'
		throw CheckError.tooBig
	end 'big'

	if n < 0 'neg'
		throw CheckError.negative
	end 'neg'

	return n * 2
end 'check'

function sumChecked(count Integer) returns Integer
	var total = 0 as Integer
	var i = -3 as Integer

	while i < count 'loop'
		let v = try check(i * 7) otherwise -1

		if v < 0 'bad'
			total = total - 1
			i = i + 1
			continue
		end 'bad'

		total = total + v
		i = i + 1
	end 'loop'

	return total
end 'sumChecked'

function sumHandled(count Integer) returns Integer
	var total = 0 as Integer
	var i = 0 as Integer

	while i < count 'loop'
		i = i + 1

		let v = try check(i * 9 - 20) otherwise (e) 'err'
			if e == CheckError.tooBig 'tooBig'
				break
			end 'tooBig'

			continue
		end 'err'

		total = total + v
	end 'loop'

	return total
end 'sumHandled'

function main() returns ExitCode
	print("{sumChecked(12)}\n")
	print("{sumChecked(3)}\n")
	print("{sumHandled(4)}\n")
	print("{sumHandled(30)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
385
39
46
250
```

<!-- test: control-flow-shapes.loop-carried-values-rotate-and-swap -->
```maxon
typealias Integer = int(i64.min to i64.max)

function rotate(n Integer) returns Integer
	var a = 1 as Integer
	var b = 2 as Integer
	var c = 3 as Integer

	for _ in 0 upto n 'iter'
		(a, b, c) = (b, c, a + b)
	end 'iter'

	return a * 1000000 + b * 1000 + c
end 'rotate'

function flip(n Integer) returns Integer
	var a = 7 as Integer
	var b = 9 as Integer

	for _ in 0 upto n 'iter'
		(a, b) = (b, a)
	end 'iter'

	return a * 100 + b
end 'flip'

function main() returns ExitCode
	print("{rotate(0)}\n")
	print("{rotate(1)}\n")
	print("{rotate(2)}\n")
	print("{rotate(5)}\n")
	print("{rotate(9)}\n")
	print("{flip(0)}\n")
	print("{flip(1)}\n")
	print("{flip(5)}\n")
	print("{flip(8)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1002003
2003003
3003005
6008011
19025033
709
907
907
709
```

<!-- test: control-flow-shapes.break-and-continue-share-the-loop-exit -->
```maxon
typealias Integer = int(i64.min to i64.max)

function scan(limit Integer) returns Integer
	var found = 0 as Integer
	var i = 0 as Integer

	while i < limit 'scan'
		i = i + 1

		if i mod 4 == 0 'a'
			if i mod 6 == 0 'b'
				found = found + 1000
				break
			end 'b'

			continue
		end 'a'

		if i mod 5 == 0 'c'
			found = found + 3
		end 'c' else 'd'
			found = found + 1
		end 'd'
	end 'scan'

	return found * 100 + i
end 'scan'

function firstOver(limit Integer, cap Integer) returns Integer
	var i = 0 as Integer
	var sum = 0 as Integer

	while i < limit 'loop'
		i = i + 1
		sum = sum + i

		if sum > cap 'capped'
			break
		end 'capped'
	end 'loop'

	return sum * 100 + i
end 'firstOver'

function main() returns ExitCode
	print("{scan(10)}\n")
	print("{scan(11)}\n")
	print("{scan(30)}\n")
	print("{scan(3)}\n")
	print("{firstOver(100, cap: 50)}\n")
	print("{firstOver(5, cap: 50)}\n")
	print("{firstOver(100, cap: 0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1210
1311
101312
303
5510
1505
101
```

<!-- test: control-flow-shapes.short-circuit-chains-in-a-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function gate(limit Integer) returns Integer
	var hits = 0 as Integer
	var n = 0 as Integer

	while n < limit 'loop'
		n = n + 1

		if (n mod 3 == 0 and n mod 5 != 0) or (n > 20 and n mod 2 == 1 and n < 30) 'pass'
			hits = hits + n
		end 'pass'
	end 'loop'

	return hits
end 'gate'

function main() returns ExitCode
	print("{gate(10)}\n")
	print("{gate(40)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
18
305
```

<!-- test: control-flow-shapes.sequence-of-early-returns -->
```maxon
typealias Integer = int(i64.min to i64.max)

function bucket(n Integer) returns Integer
	if n < 0 'negative'
		return -1
	end 'negative'

	if n == 0 'zero'
		return 5
	end 'zero'

	if n == 1 'one'
		return 11
	end 'one'

	if n == 2 'two'
		return 21
	end 'two'

	if n == 3 'three'
		return 31
	end 'three'

	if n == 5 'five'
		return 51
	end 'five'

	if n == 8 'eight'
		return 81
	end 'eight'

	if n == 13 'thirteen'
		return 131
	end 'thirteen'

	if n == 21 'twentyOne'
		return 211
	end 'twentyOne'

	if n == 34 'thirtyFour'
		return 341
	end 'thirtyFour'

	if n == 55 'fiftyFive'
		return 551
	end 'fiftyFive'

	if n == 89 'eightyNine'
		return 891
	end 'eightyNine'

	if n > 1000 'huge'
		return 1000
	end 'huge'

	if n > 100 'large'
		return 100
	end 'large'

	if n > 50 'medium'
		return 50
	end 'medium'

	return 0
end 'bucket'

function main() returns ExitCode
	print("{bucket(-5)}\n")
	print("{bucket(0)}\n")
	print("{bucket(1)}\n")
	print("{bucket(2)}\n")
	print("{bucket(3)}\n")
	print("{bucket(4)}\n")
	print("{bucket(5)}\n")
	print("{bucket(8)}\n")
	print("{bucket(13)}\n")
	print("{bucket(21)}\n")
	print("{bucket(34)}\n")
	print("{bucket(55)}\n")
	print("{bucket(89)}\n")
	print("{bucket(90)}\n")
	print("{bucket(101)}\n")
	print("{bucket(1001)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-1
5
11
21
31
0
51
81
131
211
341
551
891
50
100
1000
```

<!-- test: control-flow-shapes.state-machine-in-a-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function machine(steps Integer) returns Integer
	var state = 0 as Integer
	var acc = 0 as Integer
	var tick = 0 as Integer

	while tick < steps 'run'
		tick = tick + 1

		match state 'st'
			0 then state = 1
			1 then state = 2
			2 then state = 3 if tick mod 2 == 0 else 1
			3 then state = 4
			4 then state = 0
			default panic("bad state")
		end 'st'

		acc = acc * 3 + state
		acc = acc mod 100003
	end 'run'

	return acc
end 'machine'

function main() returns ExitCode
	print("{machine(1)}\n")
	print("{machine(7)}\n")
	print("{machine(50)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1
1366
56917
```

<!-- test: control-flow-shapes.array-walk-with-continue-and-break -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function arrayWalk(values IntArray) returns Integer
	var total = 0 as Integer

	for v in values 'each'
		if v < 0 'skip'
			continue
		end 'skip'

		if v > 90 'stop'
			break
		end 'stop'

		total = total + v
	end 'each'

	return total
end 'arrayWalk'

function main() returns ExitCode
	var first = IntArray.create()
	first.push(3)
	first.push(-1)
	first.push(4)
	first.push(100)
	first.push(9)
	first.push(8)
	var second = IntArray.create()
	second.push(1)
	second.push(2)
	second.push(3)
	let third = IntArray.create()
	print("{arrayWalk(first)}\n")
	print("{arrayWalk(second)}\n")
	print("{arrayWalk(third)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
6
0
```
