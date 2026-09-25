---
feature: array-sort
status: experimental
keywords: [array, sort, comparable, ordering, driftsort, pdqsort]
category: collections
---

# Array Sort

## Documentation

`Array<T>` exposes four sort entry points:

- `sort()` — stable sort using the element's `Comparable.compare` ordering. Requires `Element is Comparable`.
- `sort(cmp)` — stable sort using a caller-supplied comparator `function(Element, Element) returns Ordering`.
- `sortUnstable()` — unstable sort via the element's `Comparable.compare` ordering. Requires `Element is Comparable`.
- `sortUnstable(cmp)` — unstable sort using a caller-supplied comparator.

Stage 1: every entry routes to insertion sort. Stage 2 layers in sorting networks for small slices, Stage 3 routes the unstable entries to pdqsort, and Stages 4 onward build up driftsort (an adaptive stable powersort-merge sort, from the same family as Rust's standard-library `slice::sort`) for the stable entries.

The sort algorithms carry no instrumentation. No entry point takes a sink, no helper emits a dispatch key, and nothing the sort cone reaches writes to `Log` — so the whole cone touches no module storage, which is what lets a service handler sort one of its own fields. The cases below assert on the sorted result itself.

## Tests

<!-- test: sort-empty -->
Sorting an empty array is a no-op.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.sort()
	print("count={a.count()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
count=0
```

<!-- test: sort-single -->
Sorting a single-element array is a no-op.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(42)
	a.sort()
	let x = try a.get(0) otherwise return 99
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
42
```

<!-- test: sort-already-ascending -->
Already-sorted input remains sorted.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	a.push(4)
	a.push(5)
	a.sort()
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 
```

<!-- test: sort-descending-input -->
Strictly-descending input becomes strictly ascending.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(5)
	a.push(4)
	a.push(3)
	a.push(2)
	a.push(1)
	a.sort()
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 
```

<!-- test: sort-all-equal -->
All-equal elements are accepted (no shifts performed).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	a.push(7)
	a.push(7)
	a.push(7)
	a.sort()
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 7 7 7 
```

<!-- test: sort-random-permutation -->
A scrambled permutation sorts to ascending order.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(3)
	a.push(1)
	a.push(4)
	a.push(1)
	a.push(5)
	a.push(9)
	a.push(2)
	a.push(6)
	a.push(5)
	a.push(3)
	a.push(5)
	a.sort()
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1 2 3 3 4 5 5 5 6 9 
```

<!-- test: sort-string-elements -->
Reference-typed elements (Strings) sort correctly via the supplied comparator.
This exercises the refcount-on-swap path through `__ManagedMemory.set/get`.
```maxon
typealias StringArray = Array with String

function byLength(a String, b String) returns Ordering
	return a.count().compare(b.count())
end 'byLength'

function main() returns ExitCode
	var a = StringArray.create()
	a.push("banana")
	a.push("fig")
	a.push("apple")
	a.push("kiwi")
	a.push("a")
	a.sort(byLength)
	for s in a 'p'
		print("{s}\n")
	end 'p'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a
fig
kiwi
apple
banana
```

<!-- test: sort-custom-comparator-descending -->
Custom comparator can invert the natural order.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function descending(x Integer, y Integer) returns Ordering
	return y.compare(x)
end 'descending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(2)
	a.push(7)
	a.push(1)
	a.push(8)
	a.push(2)
	a.push(8)
	a.sort(descending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
8 8 7 2 2 1 
```

<!-- test: sortUnstable-comparator -->
`sortUnstable(cmp)` returns a sorted result; ordering of equal elements is not promised.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(4)
	a.push(2)
	a.push(5)
	a.push(2)
	a.push(1)
	a.push(3)
	a.sortUnstable(ascending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 2 3 4 5 
```

<!-- test: sortUnstable-default -->
`sortUnstable()` uses `Comparable.compare`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(9)
	a.push(2)
	a.push(7)
	a.push(4)
	a.sortUnstable()
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2 4 7 9 
```

<!-- test: sort-stability -->
The stable `sort()` preserves relative order of equal-key elements.
We sort `(key, original_index)` pairs by key and verify each equal-key
group's original_index sequence is non-decreasing.
```maxon
typealias Integer = int(i64.min to i64.max)

type KeyTag implements Comparable
	export var key as Integer
	export var tag as Integer

	static function init(key Integer, tag Integer) returns Self
		return Self{key: key, tag: tag}
	end 'init'

	function compare(other Self) returns Ordering
		return key.compare(other.key)
	end 'compare'
end 'KeyTag'

typealias KeyTagArray = Array with KeyTag

function main() returns ExitCode
	var a = KeyTagArray.create()
	a.push(KeyTag.init(2, tag: 0))
	a.push(KeyTag.init(1, tag: 1))
	a.push(KeyTag.init(2, tag: 2))
	a.push(KeyTag.init(1, tag: 3))
	a.push(KeyTag.init(2, tag: 4))
	a.sort()
	for kt in a 'p'
		print("({kt.key},{kt.tag}) ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
(1,1) (1,3) (2,0) (2,2) (2,4) 
```

## Stage 3: pdqsort (unstable path)

Stage 3 routes the comparator-overload `sortUnstable(cmp)` through pdqsort
for inputs larger than the small-sort threshold (n > 32). The no-arg
`sortUnstable()` (Comparable) shares the `comparableInsertionSort` helper
with `sort()` until self-hosted gains interface-method dispatch on
type-parameter receivers (Phase 11.4).

<!-- test: pdq-correctness-50elem -->
pdqsort produces a sorted result on a 50-element scrambled input.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for v in [50, 40, 30, 20, 10, 45, 35, 25, 15, 5, 48, 38, 28, 18, 8, 46, 36, 26, 16, 6, 44, 34, 24, 14, 4, 42, 32, 22, 12, 2, 49, 39, 29, 19, 9, 47, 37, 27, 17, 7, 43, 33, 23, 13, 3, 41, 31, 21, 11, 1] 'fill'
		a.push(v)
	end 'fill'
	a.sortUnstable(ascending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 
```

<!-- test: pdq-correctness-100elem-loop -->
A 100-element fill via `for i in 0 upto 100 { a.push(formula(i)) }` followed by
`sortUnstable` exercises the register allocator's remat-cycle handling: every
iteration of the spill/color loop the pre-sort fill leaves a constant `2`
(from the loop's increment after a multiplication) un-rematerializable at
its use site inside pdqsort's inlined partition body. Before the
`all-remat-stuck` detection landed in `runSpillColorLoop`, this pattern spun
the spill loop to its iteration cap silently leaving a fresh `movRegImm`-defined
vreg uncolored — `applyColoring` then panicked downstream in `colorLookupGpr`.
The test compiles only when the cycle detector demotes the rematerializable
to a real spill on the next-to-last iteration.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 100 'fill'
		a.push((i * 31) + 7)
	end 'fill'
	a.sortUnstable(ascending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7 38 69 100 131 162 193 224 255 286 317 348 379 410 441 472 503 534 565 596 627 658 689 720 751 782 813 844 875 906 937 968 999 1030 1061 1092 1123 1154 1185 1216 1247 1278 1309 1340 1371 1402 1433 1464 1495 1526 1557 1588 1619 1650 1681 1712 1743 1774 1805 1836 1867 1898 1929 1960 1991 2022 2053 2084 2115 2146 2177 2208 2239 2270 2301 2332 2363 2394 2425 2456 2487 2518 2549 2580 2611 2642 2673 2704 2735 2766 2797 2828 2859 2890 2921 2952 2983 3014 3045 3076 
```

<!-- test: pdq-already-sorted -->
Already-sorted input still produces a sorted result.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 35 'fill'
		a.push(i + 1)
	end 'fill'
	a.sortUnstable(ascending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 
```

<!-- test: small-sort-network-n4-all-perms -->
Exhaustive: every permutation of [0..4) sorts to ascending order. There are
24 permutations; we test each via a 4-digit index → permutation mapping.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function isSorted(a IntArray) returns bool
	let n = a.count()
	if n <= 1 'trivial'
		return true
	end 'trivial'
	var i = 1
	while i < n 'scan'
		let prev = try a.get(i - 1) otherwise return false
		let curr = try a.get(i) otherwise return false
		if prev > curr 'oop'
			return false
		end 'oop'
		i = i + 1
	end 'scan'
	return true
end 'isSorted'

// Build the k-th permutation of [0,1,2,3] using factoriadic decomposition.
function nthPerm4(k Integer) returns IntArray
	var pool = IntArray.create()
	pool.push(0)
	pool.push(1)
	pool.push(2)
	pool.push(3)
	var result = IntArray.create()
	var remaining = k
	let divs = [6, 2, 1, 1]
	for i in 0 upto 4 'pick'
		let d = try divs.get(i) otherwise panic("divs OOB")
		// Every `divs` entry is >= 1, so these divides never throw; the `otherwise` arms are
		// unreachable (matching the `panic` used for this function's other impossible failures).
		let idx = trunc(try (remaining / d) otherwise panic("nthPerm4: divs entry was 0"))
		remaining = try (remaining mod d) otherwise panic("nthPerm4: divs entry was 0")
		let v = try pool.get(idx) otherwise panic("pool OOB")
		result.push(v)
		try pool.remove(idx as ElementIndex) otherwise panic("pool.remove OOB")
	end 'pick'
	return result
end 'nthPerm4'

function main() returns ExitCode
	var k = 0
	var failed = 0
	while k < 24 'eachPerm'
		var perm = nthPerm4(k)
		perm.sort(ascending)
		if not isSorted(perm) 'bad'
			failed = failed + 1
		end 'bad'
		k = k + 1
	end 'eachPerm'
	print("{failed}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

## Stage 4: the stable path above the small-sort cutoff

`sort(cmp)` for n > 32 routes through driftsort (Stage 5 and later). The library holds one stable sort
and no second implementation to compare it against: the cross-checks below verify the result against
the definition of a sort instead.

<!-- test: driftsort-correctness-50elem -->
Driftsort produces a sorted result on a 50-element scrambled input.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(50)
	a.push(40)
	a.push(30)
	a.push(20)
	a.push(10)
	a.push(45)
	a.push(35)
	a.push(25)
	a.push(15)
	a.push(5)
	a.push(48)
	a.push(38)
	a.push(28)
	a.push(18)
	a.push(8)
	a.push(46)
	a.push(36)
	a.push(26)
	a.push(16)
	a.push(6)
	a.push(44)
	a.push(34)
	a.push(24)
	a.push(14)
	a.push(4)
	a.push(42)
	a.push(32)
	a.push(22)
	a.push(12)
	a.push(2)
	a.push(49)
	a.push(39)
	a.push(29)
	a.push(19)
	a.push(9)
	a.push(47)
	a.push(37)
	a.push(27)
	a.push(17)
	a.push(7)
	a.push(43)
	a.push(33)
	a.push(23)
	a.push(13)
	a.push(3)
	a.push(41)
	a.push(31)
	a.push(21)
	a.push(11)
	a.push(1)
	a.sort(ascending)
	for x in a 'p'
		print("{x} ")
	end 'p'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 
```

<!-- test: driftsort-stability -->
Driftsort is stable: equal-key elements keep their relative order. We sort
`(key, original_index)` pairs by key and check that within each equal-key
group, the original_index sequence is non-decreasing.
```maxon
typealias Integer = int(i64.min to i64.max)

type KeyTag implements Comparable
	export var key as Integer
	export var tag as Integer

	static function init(key Integer, tag Integer) returns Self
		return Self{key: key, tag: tag}
	end 'init'

	function compare(other Self) returns Ordering
		return key.compare(other.key)
	end 'compare'
end 'KeyTag'

typealias KeyTagArray = Array with KeyTag

function byKey(a KeyTag, b KeyTag) returns Ordering
	return a.key.compare(b.key)
end 'byKey'

function main() returns ExitCode
	var a = KeyTagArray.create()
	a.push(KeyTag.init(2, tag: 0))
	a.push(KeyTag.init(1, tag: 1))
	a.push(KeyTag.init(2, tag: 2))
	a.push(KeyTag.init(1, tag: 3))
	a.push(KeyTag.init(2, tag: 4))
	a.push(KeyTag.init(1, tag: 5))
	a.push(KeyTag.init(3, tag: 6))
	a.push(KeyTag.init(2, tag: 7))
	a.push(KeyTag.init(3, tag: 8))
	a.push(KeyTag.init(1, tag: 9))
	a.push(KeyTag.init(2, tag: 10))
	a.push(KeyTag.init(1, tag: 11))
	a.push(KeyTag.init(3, tag: 12))
	a.push(KeyTag.init(2, tag: 13))
	a.push(KeyTag.init(1, tag: 14))
	a.push(KeyTag.init(3, tag: 15))
	a.push(KeyTag.init(2, tag: 16))
	a.push(KeyTag.init(1, tag: 17))
	a.push(KeyTag.init(2, tag: 18))
	a.push(KeyTag.init(1, tag: 19))
	a.push(KeyTag.init(3, tag: 20))
	a.push(KeyTag.init(2, tag: 21))
	a.push(KeyTag.init(1, tag: 22))
	a.push(KeyTag.init(3, tag: 23))
	a.push(KeyTag.init(2, tag: 24))
	a.push(KeyTag.init(1, tag: 25))
	a.push(KeyTag.init(2, tag: 26))
	a.push(KeyTag.init(1, tag: 27))
	a.push(KeyTag.init(3, tag: 28))
	a.push(KeyTag.init(2, tag: 29))
	a.push(KeyTag.init(1, tag: 30))
	a.push(KeyTag.init(3, tag: 31))
	a.push(KeyTag.init(2, tag: 32))
	a.push(KeyTag.init(1, tag: 33))
	a.push(KeyTag.init(2, tag: 34))
	a.sort(byKey)
	// Walk the sorted output: within each equal-key run the tag sequence
	// must be non-decreasing for stability. Print the first violator if any.
	var ok = true
	var i = 1
	while i < a.count() 'check'
		let prev = try a.get(i - 1) otherwise return 99
		let curr = try a.get(i) otherwise return 99
		if prev.key == curr.key 'sameKey'
			if prev.tag > curr.tag 'outOfOrder'
				ok = false
				print("instability at i={i}: ({prev.key},{prev.tag}) before ({curr.key},{curr.tag})\n")
			end 'outOfOrder'
		end 'sameKey'
		i = i + 1
	end 'check'
	if ok 'stable'
		print("stable\n")
	end 'stable'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stable
```

## Stage 5: Driftsort run creation + powersort merge policy

Stage 5 makes `Array.sort()` and `Array.sort(cmp)` route through driftsort
instead of bottom-up merge sort. Driftsort is the "drift" hybrid of bottom-up
merging and top-down partitioning (the same family as Rust's stdlib
`slice::sort`): a stable quicksort manufactures large runs, which a powersort
merge stack then combines. It:

- Detects natural ascending and strictly-descending runs at the head of the
  unsorted region (`naturalRunLen`), reversing descending runs in place so the
  rest of the algorithm always sees ascending input.
- Uses a `minGoodRunLen` threshold of `min(ceil(n/2), 64)` for n ≤ 4096 and
  `floor(sqrt(n))` beyond — much larger than a classic timsort minrun, which
  keeps the merge stack shallow and shifts work onto the cache-friendly
  quicksort run-builder.
- When a natural run is shorter than `minGoodRunLen`, **creates** a run of that
  length instead of insertion-padding it. On short inputs (n ≤ 64) it sorts the
  block eagerly with the stable quicksort; on larger inputs it records an
  *unsorted logical run* and quicksorts it lazily, right before the run is
  physically merged.
- Pushes each run onto a stack, using the powersort merge policy (Munro & Wild,
  2018; driftsort's exact `merge_tree_depth` / `ceil(2^62/n)` scale form) to
  decide when to merge adjacent stack entries. Powersort approximates the
  optimal merge tree while making each merge decision once per run boundary.

<!-- test: driftsort-sorted-and-multiset-preserved -->
Driftsort's output on a scrambled 40-element input is verified
against the definition of a sort rather than against a second implementation.
Every adjacent pair must be ordered, and the multiset must survive — the
element count, the sum and the sum of squares taken before the sort must all
come back unchanged, so an output that is ordered because the sort dropped,
duplicated or invented an element fails here.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(11)
	a.push(7)
	a.push(3)
	a.push(13)
	a.push(5)
	a.push(2)
	a.push(17)
	a.push(1)
	a.push(19)
	a.push(8)
	a.push(4)
	a.push(15)
	a.push(6)
	a.push(20)
	a.push(10)
	a.push(14)
	a.push(9)
	a.push(18)
	a.push(12)
	a.push(16)
	a.push(31)
	a.push(27)
	a.push(23)
	a.push(33)
	a.push(25)
	a.push(22)
	a.push(37)
	a.push(21)
	a.push(39)
	a.push(28)
	a.push(24)
	a.push(35)
	a.push(26)
	a.push(40)
	a.push(30)
	a.push(34)
	a.push(29)
	a.push(38)
	a.push(32)
	a.push(36)
	let n = a.count()
	var sum = 0
	var squares = 0

	for x in a 'tally'
		sum = sum + x
		squares = squares + x * x
	end 'tally'

	a.sort(ascending)

	var ordered = a.count() == n
	var sumAfter = 0
	var squaresAfter = 0

	for i in 0 upto a.count() 'walk'
		let v = try a.get(i) otherwise return 99
		sumAfter = sumAfter + v
		squaresAfter = squaresAfter + v * v

		if i > 0 'pair'
			let p = try a.get(i - 1) otherwise return 99

			if p > v 'oop'
				ordered = false
			end 'oop'
		end 'pair'
	end 'walk'

	if ordered and sumAfter == sum and squaresAfter == squares 'ok'
		print("sorted\n")
	end 'ok' else 'bad'
		print("BROKEN\n")
	end 'bad'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted
```

## Cross-checks: the sorted result checked against the definition of a sort

Each case checks its own output rather than a second sort's: a second
implementation is one more sort to maintain, and a bug the two shared would
read as agreement. Every adjacent pair must be ordered by the comparator and
the multiset must survive — the element count, the sum and the sum of squares
taken before the sort all unchanged after it, so an output that is ordered
because an element was dropped, duplicated or invented still fails.

<!-- test: driftsort-stage7-cross-check-large -->
Large cross-check on a 100-element input that mixes a long descending run
with a long ascending one — the shape that exercises the buffered and
rotation merges depending on run sizes. The sorted result must be ordered
pairwise and must hold the same multiset it started with: same count, same
sum, same sum of squares.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	// 50 descending then 50 ascending — two large runs after findRun.
	a.push(100)
	a.push(99)
	a.push(98)
	a.push(97)
	a.push(96)
	a.push(95)
	a.push(94)
	a.push(93)
	a.push(92)
	a.push(91)
	a.push(90)
	a.push(89)
	a.push(88)
	a.push(87)
	a.push(86)
	a.push(85)
	a.push(84)
	a.push(83)
	a.push(82)
	a.push(81)
	a.push(80)
	a.push(79)
	a.push(78)
	a.push(77)
	a.push(76)
	a.push(75)
	a.push(74)
	a.push(73)
	a.push(72)
	a.push(71)
	a.push(70)
	a.push(69)
	a.push(68)
	a.push(67)
	a.push(66)
	a.push(65)
	a.push(64)
	a.push(63)
	a.push(62)
	a.push(61)
	a.push(60)
	a.push(59)
	a.push(58)
	a.push(57)
	a.push(56)
	a.push(55)
	a.push(54)
	a.push(53)
	a.push(52)
	a.push(51)
	a.push(1)
	a.push(2)
	a.push(3)
	a.push(4)
	a.push(5)
	a.push(6)
	a.push(7)
	a.push(8)
	a.push(9)
	a.push(10)
	a.push(11)
	a.push(12)
	a.push(13)
	a.push(14)
	a.push(15)
	a.push(16)
	a.push(17)
	a.push(18)
	a.push(19)
	a.push(20)
	a.push(21)
	a.push(22)
	a.push(23)
	a.push(24)
	a.push(25)
	a.push(26)
	a.push(27)
	a.push(28)
	a.push(29)
	a.push(30)
	a.push(31)
	a.push(32)
	a.push(33)
	a.push(34)
	a.push(35)
	a.push(36)
	a.push(37)
	a.push(38)
	a.push(39)
	a.push(40)
	a.push(41)
	a.push(42)
	a.push(43)
	a.push(44)
	a.push(45)
	a.push(46)
	a.push(47)
	a.push(48)
	a.push(49)
	a.push(50)
	let n = a.count()
	var sum = 0
	var squares = 0

	for x in a 'tally'
		sum = sum + x
		squares = squares + x * x
	end 'tally'

	a.sort(ascending)

	var ordered = a.count() == n
	var sumAfter = 0
	var squaresAfter = 0

	for i in 0 upto a.count() 'walk'
		let v = try a.get(i) otherwise return 99
		sumAfter = sumAfter + v
		squaresAfter = squaresAfter + v * v

		if i > 0 'pair'
			let p = try a.get(i - 1) otherwise return 99

			if p > v 'oop'
				ordered = false
			end 'oop'
		end 'pair'
	end 'walk'

	if ordered and sumAfter == sum and squaresAfter == squares 'ok'
		print("sorted\n")
	end 'ok' else 'bad'
		print("BROKEN\n")
	end 'bad'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted
```

<!-- test: driftsort-stage6-cross-check -->
Cross-check (Stage 5 + Stage 6 combined): driftsort uses bounded scratch and
may fall back to the rotation merge. Whichever merge path the 40-element
input takes, the result must come out ordered pairwise with its multiset
intact — same count, same sum, same sum of squares.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(40)
	a.push(20)
	a.push(60)
	a.push(10)
	a.push(50)
	a.push(30)
	a.push(70)
	a.push(15)
	a.push(45)
	a.push(25)
	a.push(65)
	a.push(35)
	a.push(55)
	a.push(5)
	a.push(75)
	a.push(12)
	a.push(42)
	a.push(22)
	a.push(62)
	a.push(32)
	a.push(52)
	a.push(2)
	a.push(72)
	a.push(8)
	a.push(48)
	a.push(28)
	a.push(68)
	a.push(38)
	a.push(58)
	a.push(18)
	a.push(78)
	a.push(4)
	a.push(44)
	a.push(24)
	a.push(64)
	a.push(34)
	a.push(54)
	a.push(14)
	a.push(74)
	a.push(6)
	let n = a.count()
	var sum = 0
	var squares = 0

	for x in a 'tally'
		sum = sum + x
		squares = squares + x * x
	end 'tally'

	a.sort(ascending)

	var ordered = a.count() == n
	var sumAfter = 0
	var squaresAfter = 0

	for i in 0 upto a.count() 'walk'
		let v = try a.get(i) otherwise return 99
		sumAfter = sumAfter + v
		squaresAfter = squaresAfter + v * v

		if i > 0 'pair'
			let p = try a.get(i - 1) otherwise return 99

			if p > v 'oop'
				ordered = false
			end 'oop'
		end 'pair'
	end 'walk'

	if ordered and sumAfter == sum and squaresAfter == squares 'ok'
		print("sorted\n")
	end 'ok' else 'bad'
		print("BROKEN\n")
	end 'bad'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted
```

<!-- test: driftsort-large-sqrt-cross-check -->
Large cross-check crossing the n > 4096 boundary, where `minGoodRunLen`
switches to `floor(sqrt(n))` and run creation uses the stable quicksort
(including its partition path on runs > 32). A pseudo-random 5000-element
input — with duplicate keys, since the values are masked to 16 bits — must
come out ordered pairwise and carrying the same multiset: same count, same
sum, same sum of squares.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	var r = 2463534242

	for i in 0 upto 5000 'fill'
		r = (r * 1103515245 + 12345) and 0x7FFFFFFF
		a.push(r and 0xFFFF)
	end 'fill'

	let n = a.count()
	var sum = 0
	var squares = 0

	for x in a 'tally'
		sum = sum + x
		squares = squares + x * x
	end 'tally'

	a.sort(ascending)

	var ordered = a.count() == n
	var sumAfter = 0
	var squaresAfter = 0

	for i in 0 upto a.count() 'walk'
		let v = try a.get(i) otherwise return 99
		sumAfter = sumAfter + v
		squaresAfter = squaresAfter + v * v

		if i > 0 'pair'
			let p = try a.get(i - 1) otherwise return 99

			if p > v 'oop'
				ordered = false
			end 'oop'
		end 'pair'
	end 'walk'

	if ordered and sumAfter == sum and squaresAfter == squares 'ok'
		print("sorted\n")
	end 'ok' else 'bad'
		print("BROKEN\n")
	end 'bad'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted
```

<!-- test: driftsort-compare-count-grows-as-n-log-n -->
The stable sort does `O(n log n)` comparisons. 65,536 pseudo-random keys are sorted through a comparator
that counts its calls, and the count must stay within 32 per element — twice `log2(65536)`. A merge
policy that folds every new run into the run beside it, or a node power that reads as zero, costs
`O(n^1.5)` here: more than 150 per element.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

type CompareTally
	export var calls as Integer

	static function create() returns CompareTally
		return Self{calls: 0}
	end 'create'
end 'CompareTally'

function counted(x Integer, y Integer, tally CompareTally) returns Ordering
	tally.calls = tally.calls + 1
	return x.compare(y)
end 'counted'

function main() returns ExitCode
	var a = IntArray.create()
	var r = 2463534242

	for i in 0 upto 65536 'fill'
		r = (r * 1103515245 + 12345) and 0x7FFFFFFF
		a.push(r and 0xFFFFF)
	end 'fill'

	let tally = CompareTally.create()
	a.sort(function(x Integer, y Integer) gives counted(x, y: y, tally: tally))

	var ordered = true

	for i in 1 upto a.count() 'walk'
		let p = try a.get(i - 1) otherwise return 99
		let v = try a.get(i) otherwise return 99

		if p > v 'outOfOrder'
			ordered = false
		end 'outOfOrder'
	end 'walk'

	if ordered and tally.calls <= 32 * a.count() 'withinTheBound'
		print("sorted within n log n\n")
	end 'withinTheBound' else 'overTheBound'
		print("ordered={ordered} compares={tally.calls}\n")
	end 'overTheBound'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted within n log n
```

<!-- test: driftsort-survives-a-quicksort-adversary -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

let Items = 65536
let ComparesPerItemBound = 64
let RunBreakerStride = 8

type Adversary
	export var values as IntArray
	export var solid as Integer
	export var candidate as Integer
	export var gas as Integer
	export var calls as Integer

	static function create(items Integer) returns Adversary
		var values = IntArray.create()
		var solid = 0

		for i in 0 upto items 'fill'
			if i mod RunBreakerStride == RunBreakerStride - 1 'breaksARun'
				values.push(solid)
				solid = solid + 1
			end 'breaksARun' else 'undecided'
				values.push(items)
			end 'undecided'
		end 'fill'

		return Self{values: values, solid: solid, candidate: 0, gas: items, calls: 0}
	end 'create'

	function valueOf(item Integer) returns Integer
		return try self.values.get(item) otherwise panic("Adversary.valueOf: every item the sort holds is one this adversary numbered")
	end 'valueOf'

	function freeze(item Integer)
		try self.values.set(item, value: self.solid) otherwise panic("Adversary.freeze: every item the sort holds is one this adversary numbered")
		self.solid = self.solid + 1
	end 'freeze'

	function compare(x Integer, y Integer) returns Ordering
		self.calls = self.calls + 1

		if self.valueOf(x) == self.gas and self.valueOf(y) == self.gas 'bothUndecided'
			if x == self.candidate 'freezeTheCandidate'
				self.freeze(x)
			end 'freezeTheCandidate' else 'freezeTheOther'
				self.freeze(y)
			end 'freezeTheOther'
		end 'bothUndecided'

		if self.valueOf(x) == self.gas 'xUndecided'
			self.candidate = x
		end 'xUndecided' else if self.valueOf(y) == self.gas 'yUndecided'
			self.candidate = y
		end 'yUndecided'

		return self.valueOf(x).compare(self.valueOf(y))
	end 'compare'
end 'Adversary'

function adversarial(x Integer, y Integer, adversary Adversary) returns Ordering
	return adversary.compare(x, y: y)
end 'adversarial'

function main() returns ExitCode
	var items = IntArray.create()

	for i in 0 upto Items 'fill'
		items.push(i)
	end 'fill'

	let adversary = Adversary.create(Items)
	items.sort(function(x Integer, y Integer) gives adversarial(x, y: y, adversary: adversary))

	var ordered = true

	for i in 1 upto items.count() 'walk'
		let before = try items.get(i - 1) otherwise return 99
		let after = try items.get(i) otherwise return 99

		if adversary.valueOf(before) > adversary.valueOf(after) 'outOfOrder'
			ordered = false
		end 'outOfOrder'
	end 'walk'

	if ordered and adversary.calls <= ComparesPerItemBound * Items 'withinTheBound'
		print("sorted within the bound\n")
	end 'withinTheBound' else 'overTheBound'
		print("ordered={ordered} compares={adversary.calls} perItem={adversary.calls / Items}\n")
	end 'overTheBound'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
sorted within the bound
```

