---
feature: symbolic-bounds
status: experimental
keywords: [optimizer, codegen, bounds-check, range, symbolic, interval, loop, induction, array]
category: codegen
---
# An index the length already bounds

## Documentation

`refineValueRanges` carries, beside a value's numeric interval, a SYMBOLIC upper bound of the form
`base + offset`, where `base` is a value known to be non-negative — one that appeared as the limit of
an unsigned bounds guard, whose stated precondition is `length ≥ 0` — and `offset` a small constant.
The true edge of `a < b` gives `a ≤ b − 1`; when `b` itself is bounded by `base + k` the bound
COMPOSES to `a ≤ base + k − 1`; adding a constant moves the offset when the numeric interval proves
no wrap; a phi keeps the bound only when every incoming edge names the same base, at the largest
offset. An unsigned bounds guard `idx <u base` is then decided true when `idx` is numerically
non-negative and bounded by `base + k` with `k ≤ −1`.

### The shape it exists for

Inside a loop `unswitchInvariantGuards` has versioned, the array length is one hoisted value, and
every access's bound is a compare against it. `for i in 0 upto a.count()` bounds `i` by the length
directly; `while low < high` with `high = firstValue − 1` and `firstValue` itself checked against the
length bounds both ends of the reverse loop through composition. Every such check was the last test
an access paid after the shape guards left the loop.

### What the pass refuses, and why

- **A bound below a value not known non-negative.** The upper operand of any `<`/`<=` compare may
  serve as a base, unsigned or signed, but only when the LOWER operand is known non-negative: the
  derivation is exact arithmetic on non-wrapped values, a bounded value is really non-negative on
  every execution, and a signed decision additionally asks the base's own interval to be
  non-negative so that its unsigned and signed readings agree.
- **An index that is not known non-negative.** `a.count() − 1` on an empty array is `−1`; the
  symbolic bound `length − 1` holds and the check must still fire, which the numeric interval
  decides (`an-index-below-zero-keeps-its-check`).
- **Two loads of one length.** A bound derived against one load of `length@8` says nothing about a
  compare against another load of it; only one value relates a bound to a check. Inside a versioned
  loop that value is the hoisted length, and `unswitchInvariantGuards` reuses a load the preheader
  already made — a `for` bound's `a.count()` — rather than minting a second one, so the loop bound
  and the accesses' checks compare against the same value. A loop whose length changes (a `push`
  inside it) is not versioned, its accesses load the length afresh, and its checks stay.

⚠ A green case here proves nothing on its own — a check that is deleted was one the program never
failed. The evidence is the committed fragments of the shape cases (no bound compare in the loop
body) and the CONTROLS, each a program whose index CAN exceed the length and whose fallback the
program reads back.

## Tests

<!-- test: a-loop-to-the-length-needs-no-bound-check -->
The commonest shape. `total`'s loop runs to `a.count()`, the length the bound checks compare against;
in the fragment the loop body is the element load and the add, with no compare against the length.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0
	for i in 0 upto a.count() 'sum'
		t = t + (try a.get(i) otherwise panic("total: i < a.count()"))
	end 'sum'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a) != 55 'sum'
		return 1
	end 'sum'
	var empty = WordArray.create()
	if total(empty) != 0 'none'
		return 2
	end 'none'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-reverse-loop-bounded-through-its-first-element -->
The composed shape, as the fannkuch flip loop has it. `first` is read from the array, so nothing
numeric bounds it — until the outer loop's own test uses it as an index: past that access's join
`first ≤ length − 1` is known, `high = first − 1` inherits `length − 2`, and the inner `low < high`
composes `low ≤ length − 3`. Both loops are versioned together, so every access compares against
the one hoisted length; in the fast copy of `flips` the inner loop's body holds no compare against
it — two loads and two stores under the loop test alone — and the outer loop's body checks `first`
once, at the test.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function flips(p WordArray) returns Word
	var count = 1
	var first = try p.get(0) otherwise panic("flips: p is not empty")

	while (try p.get(first) otherwise panic("flips: first is an element, so an index into p")) > 0 'flip'
		let next = try p.get(first) otherwise panic("flips: first < p.count()")
		try p.set(first, value: first) otherwise panic("flips: first < p.count()")

		if first > 2 'reverseMiddle'
			var low = 1
			var high = first - 1

			while low < high 'swap'
				let atLow = try p.get(low) otherwise panic("flips: low < high < first < p.count()")
				let atHigh = try p.get(high) otherwise panic("flips: high < first < p.count()")
				try p.set(low, value: atHigh) otherwise panic("flips: low is in range")
				try p.set(high, value: atLow) otherwise panic("flips: high is in range")
				low = low + 1
				high = high - 1
			end 'swap'
		end 'reverseMiddle'

		first = next
		count = count + 1
	end 'flip'

	return count
end 'flips'

function main() returns ExitCode
	var p = WordArray.create()
	p.push(4)
	p.push(1)
	p.push(2)
	p.push(3)
	p.push(5)
	p.push(0)
	if flips(p) != 2 'count'
		return 1
	end 'count'
	if (try p.get(1) otherwise -1) != 3 'one'
		return 2
	end 'one'
	if (try p.get(3) otherwise -1) != 1 'three'
		return 3
	end 'three'
	if (try p.get(4) otherwise -1) != 4 'four'
		return 4
	end 'four'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: one-index-checked-once-per-iteration -->
Inside a versioned loop, the first access checks `i` against the hoisted length and every later
access on `i` is decided by that check: `double`'s fragment holds one bound compare per iteration,
not three.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function double(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("double: i < n <= a.count()")
		try a.set(i, value: v * 2) otherwise panic("double: i < n <= a.count()")
		t = t + (try a.get(i) otherwise panic("double: i < n <= a.count()"))
	end 'each'
	return t
end 'double'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(i + 1)
	end 'seed'
	if double(a, n: 5) != 30 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-bound-that-is-not-the-length-keeps-the-check -->
Control. `n` is a parameter, not the length, so `i < n` says nothing about `i < length`: the checks
stay, the four indices past the end take the fallback, and the sum says so.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function walk(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise 99)
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	if walk(a, n: 9) != 406 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-inclusive-loop-to-the-length-fires-at-the-length -->
Control for an inclusive counter. `for i in 0 to a.count()` tests `i <= n` at its header, so the
increment may wrap and the counter widens; no bound attaches to it at all, the check stays and
fires once, at `i == count`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function overrun(a WordArray) returns Word
	var t = 0
	for i in 0 to a.count() 'each'
		t = t + (try a.get(i) otherwise 1000)
	end 'each'
	return t
end 'overrun'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 4 'seed'
		a.push(i)
	end 'seed'
	if overrun(a) != 1006 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-index-below-zero-keeps-its-check -->
Control for non-negativity. `last = a.count() − 1` is bounded by `length − 1` exactly, and on an
empty array it is `−1`; the numeric interval does not prove it non-negative, so the `ElementIndex`
range check stays — and a negative index is refused at the door, uncatchably, before any bound.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function lastOr(a WordArray, fallback Word) returns Word
	let last = a.count() - 1
	return try a.get(last) otherwise fallback
end 'lastOr'

function main() returns ExitCode
	var empty = WordArray.create()
	if lastOr(empty, fallback: 7) != 7 'emptyFallsBack'
		return 1
	end 'emptyFallsBack'
	var a = WordArray.create()
	a.push(3)
	a.push(8)
	if lastOr(a, fallback: 7) != 8 'lastElement'
		return 2
	end 'lastElement'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at an-index-below-zero-keeps-its-check.test:7: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in lastOr
  in main
  in mrt_start
```

<!-- test: a-growing-array-keeps-its-checks -->
Control for two loads of one length. The loop pushes, so it is not versioned and every access reads
the length afresh; the bound computed before the loop is a different value from the one each check
compares against, and the checks stay. Every read is in bounds and the answer is exact.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function growAndSum(a WordArray) returns Word
	var t = 0
	for i in 0 upto a.count() 'each'
		a.push(i)
		t = t + (try a.get(i) otherwise 1000)
	end 'each'
	return t
end 'growAndSum'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 4 'seed'
		a.push(i + 10)
	end 'seed'
	if growAndSum(a) != 46 'sum'
		return 1
	end 'sum'
	if a.count() != 8 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-index-one-past-the-length-fires -->
Control for the offset's strictness. `j = i + 1` inside `for i in 0 upto a.count()` is bounded by
`length + 0` and non-negative, which proves `j <= length` and NOT `j < length`: the check stays, and
the last iteration's read of slot `count` takes the fallback. Measured under sabotage — a strict
compare accepting offset 0 — the check is deleted and the read returns the zeroed slot past the end:
90 where 1090 is right; and a compiler built by that sabotaged compiler had the same check deleted
in its own code and panicked inside its own CSE, spec-green.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function nextSum(a WordArray) returns Word
	var t = 0
	for i in 0 upto a.count() 'each'
		let j = i + 1
		t = t + (try a.get(j) otherwise 1000)
	end 'each'
	return t
end 'nextSum'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(10)
	a.push(20)
	a.push(30)
	a.push(40)
	if nextSum(a) != 1090 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```
