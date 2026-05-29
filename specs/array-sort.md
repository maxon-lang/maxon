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
- `sortUnstable()` — unstable sort via the element's `Comparable.compare` ordering. Requires `Element is Comparable`. Routed through the same `comparableInsertionSort` helper as `sort()` until the self-hosted compiler implements interface-method dispatch on type-parameter receivers (Phase 11.4); the API distinction is reserved so callers can opt out of stability up front.
- `sortUnstable(cmp)` — unstable sort using a caller-supplied comparator.

Stage 1: all four entries route to insertion sort. Stage 2 layers in sorting networks for small slices, Stage 3 routes the unstable entries to pdqsort, and Stages 4–7 build up driftsort (an adaptive stable powersort-merge sort, from the same family as Rust's standard-library `slice::sort`) for the stable entries.

Dispatch verification uses `Log.trace`: the insertion-sort body emits `insertionSort.run` so tests can assert which algorithm engaged.

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
	let la = a.count()
	let lb = b.count()
	return la.compare(lb)
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

<!-- test: sort-dispatch-trace -->
`Log.trace("insertionSort.run")` fires when the insertion-sort body runs.
A later stage will reroute big inputs through other algorithms, and the
dispatch test there will assert this key *does not* fire for large arrays.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var a = IntArray.create()
	a.push(3)
	a.push(1)
	a.push(2)
	Log.startCapture()
	a.sort()
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "insertionSort.run") 'k'
		print("insertion-fired\n")
	end 'k' else 'nk'
		print("insertion-MISSING\n")
	end 'nk'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
insertion-fired
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

	export static function init(key Integer, tag Integer) returns Self
		return Self{key: key, tag: tag}
	end 'init'

	export function compare(other Self) returns Ordering
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

## Stage 2: Small-sort networks

Stage 2 dispatches small slices to hardcoded sorting networks instead of the
insertion-sort loop. The comparator-overload entry (`sort(cmp)` /
`sortUnstable(cmp)`) routes through `smallSortRange`, which picks:

- n ∈ {2, 4, 8} → branchless sorting network (Batcher's odd-even merge)
- everything else (including 3, 5, 6, 7, 9..32) → insertion sort

Dispatch tests assert the right path engaged via `Log.trace` keys
`smallSort.network` and `smallSort.insertion`.

<!-- test: small-sort-dispatch-n2 -->
n=2 routes to the 2-element network.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(2)
	a.push(1)
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "smallSort.network") 'n'
		print("network ")
	end 'n'
	if not Log.fired(keys, key: "smallSort.insertion") 'noi'
		print("no-insertion ")
	end 'noi'
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
network no-insertion 1 2 
```

<!-- test: small-sort-dispatch-n4 -->
n=4 routes to the 4-element network.
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
	a.push(3)
	a.push(1)
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "smallSort.network") 'n'
		print("network ")
	end 'n'
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
network 1 2 3 4 
```

<!-- test: small-sort-dispatch-n8 -->
n=8 routes to the 8-element network.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(8)
	a.push(3)
	a.push(1)
	a.push(7)
	a.push(2)
	a.push(6)
	a.push(4)
	a.push(5)
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "smallSort.network") 'n'
		print("network ")
	end 'n'
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
network 1 2 3 4 5 6 7 8 
```

<!-- test: small-sort-dispatch-n16-insertion -->
n=16 falls through to insertion sort (network sizes only cover {2,4,8}).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 16 'fill'
		a.push(16 - i)
	end 'fill'
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if not Log.fired(keys, key: "smallSort.network") 'nn'
		print("no-network ")
	end 'nn'
	if Log.fired(keys, key: "smallSort.insertion") 'i'
		print("insertion ")
	end 'i'
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
no-network insertion 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 
```

## Stage 3: pdqsort (unstable path)

Stage 3 routes the comparator-overload `sortUnstable(cmp)` through pdqsort
for inputs larger than the small-sort threshold (n > 32). The no-arg
`sortUnstable()` (Comparable) shares the `comparableInsertionSort` helper
with `sort()` until self-hosted gains interface-method dispatch on
type-parameter receivers (Phase 11.4).

Dispatch tests assert via Log trace keys:
- `pdq.partition` — partition pass ran
- `pdq.medianOf3` — median-of-3 pivot selection ran
- `pdq.medianOfMedians` — median-of-medians ran (n > 128)
- `pdq.heapsortFallback` — bad-allowance exhausted
- `pdq.commonPrefix` — already-partitioned-prefix optimization engaged
- `pdq.equalElements` — equal-elements fast path engaged

<!-- test: pdq-dispatch-large -->
For n > 32, `sortUnstable(cmp)` routes through pdqsort and fires partition.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for v in [50, 40, 30, 20, 10, 45, 35, 25, 15, 5, 48, 38, 28, 18, 8, 46, 36, 26, 16, 6, 44, 34, 24, 14, 4, 42, 32, 22, 12, 2, 49, 39, 29, 19, 9, 47, 37, 27, 17, 7] 'fill'
		a.push(v)
	end 'fill'
	Log.startCapture()
	a.sortUnstable(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "pdq.partition") 'p'
		print("partition ")
	end 'p'
	if Log.fired(keys, key: "pdq.medianOf3") 'm3'
		print("medianOf3 ")
	end 'm3'
	if not Log.fired(keys, key: "pdq.heapsortFallback") 'nf'
		print("no-heap-fallback")
	end 'nf'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
partition medianOf3 no-heap-fallback
```

<!-- test: pdq-dispatch-small-no-pdq -->
For n ≤ 32, `sortUnstable(cmp)` short-circuits to smallSortRange (no partition).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(8)
	a.push(3)
	a.push(1)
	a.push(7)
	a.push(2)
	a.push(6)
	a.push(4)
	a.push(5)
	Log.startCapture()
	a.sortUnstable(ascending)
	let keys = Log.stopCapture()
	if not Log.fired(keys, key: "pdq.partition") 'p'
		print("no-partition ")
	end 'p'
	if Log.fired(keys, key: "smallSort.network") 'n'
		print("network")
	end 'n'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no-partition network
```

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

<!-- test: pdq-all-equal -->
All-equal input — fires the equal-elements fast path.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for _ in 0 upto 35 'fill'
		a.push(7)
	end 'fill'
	Log.startCapture()
	a.sortUnstable(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "pdq.equalElements") 'e'
		print("equal-path-fired\n")
	end 'e'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal-path-fired
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
		let idx = trunc(remaining / d)
		remaining = remaining mod d
		let v = try pool.get(idx) otherwise panic("pool OOB")
		result.push(v)
		try pool.remove(idx) otherwise panic("pool.remove OOB")
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

## Stage 4: Bottom-up stable merge sort (oracle)

`sort(cmp)` for n > 32 routes through driftsort (Stage 5 and later); the
bottom-up stable merge sort that drove Stage 4 is still callable as
`referenceMergeSort(cmp)` so driftsort tests can cross-check their output
against a slow-but-known-good baseline.

The dispatch test for the stable path now asserts driftsort's trace keys
(`findRun.ascending` / `driftsort.push`) instead of the Stage 4
`mergeSort.pass` / `merge.twoPointer`.

<!-- test: driftsort-dispatch-large -->
For n > 32, `sort(cmp)` routes through driftsort. Run detection finds the
strictly-descending runs in this scrambled input and reverses each one,
then powersort merges them.
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
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "findRun.descending") 'fd'
		print("findRun.descending ")
	end 'fd'
	if Log.fired(keys, key: "driftsort.push") 'pp'
		print("driftsort.push ")
	end 'pp'
	if Log.fired(keys, key: "driftsort.merge") 'pm'
		print("driftsort.merge")
	end 'pm'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
findRun.descending driftsort.push driftsort.merge
```

<!-- test: driftsort-dispatch-small-no-driftsort -->
For n ≤ 32, `sort(cmp)` short-circuits to smallSortRange (no run-detection,
no powersort, no merge passes).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(8)
	a.push(3)
	a.push(1)
	a.push(7)
	a.push(2)
	a.push(6)
	a.push(4)
	a.push(5)
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if not Log.fired(keys, key: "driftsort.push") 'np'
		print("no-driftsort ")
	end 'np'
	if Log.fired(keys, key: "smallSort.network") 'sn'
		print("network")
	end 'sn'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no-driftsort network
```

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

	export static function init(key Integer, tag Integer) returns Self
		return Self{key: key, tag: tag}
	end 'init'

	export function compare(other Self) returns Ordering
		return key.compare(other.key)
	end 'compare'
end 'KeyTag'

typealias KeyTagArray = Array with KeyTag

function byKey(a KeyTag, b KeyTag) returns Ordering
	let ak = a.key
	let bk = b.key
	return ak.compare(bk)
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

<!-- test: referenceMergeSort-callable -->
`referenceMergeSort(cmp)` is the public entry that Stage 5+ driftsort tests
cross-check against. Same output as `sort(cmp)` for the same input.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(7)
	a.push(3)
	a.push(9)
	a.push(1)
	a.push(5)
	a.push(2)
	a.push(8)
	a.push(4)
	a.push(6)
	a.referenceMergeSort(ascending)
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
1 2 3 4 5 6 7 8 9 
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

Trace keys:

- `findRun.ascending`     — a natural ascending run was detected
- `findRun.descending`    — a strictly-descending run was found and reversed
- `driftsort.naturalRun`  — a natural run ≥ minGoodRunLen was used as-is
- `driftsort.eagerRun`    — a short run was sorted eagerly via stable quicksort
- `driftsort.logicalRun`  — a short run was recorded as an unsorted logical run
- `driftQuicksort.partition` — the stable quicksort ran a partition pass
- `driftsort.push`        — a run was pushed onto the merge stack
- `driftsort.merge`       — two adjacent stack runs were merged

<!-- test: driftsort-eager-run-dispatch -->
For a short input (n ≤ 64) whose natural runs are all shorter than
`minGoodRunLen`, run creation sorts each block eagerly with the stable
quicksort (`driftsort.eagerRun` fires; the lazy `driftsort.logicalRun` does
not). Input: 10 descending blocks of 4 (length 4 < minGood 20).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	var base = 0
	var blk = 0
	while blk < 10 'blocks'
		a.push(base + 4)
		a.push(base + 3)
		a.push(base + 2)
		a.push(base + 1)
		base = base + 10
		blk = blk + 1
	end 'blocks'
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "driftsort.eagerRun") 'er'
		print("eagerRun ")
	end 'er'
	if not Log.fired(keys, key: "driftsort.logicalRun") 'nl'
		print("no-logical ")
	end 'nl'
	var ok = true
	for i in 1 upto 40 'c'
		let p = try a.get(i - 1) otherwise return 90
		let cv = try a.get(i) otherwise return 91
		if p > cv 'bad'
			ok = false
		end 'bad'
	end 'c'
	if ok 'o'
		print("sorted")
	end 'o'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
eagerRun no-logical sorted
```

<!-- test: driftsort-logical-run-dispatch -->
For a larger input (n > 64) whose natural runs are shorter than
`minGoodRunLen`, run creation records *unsorted logical runs* and the stable
quicksort runs lazily at merge time (`driftsort.logicalRun` fires;
`driftsort.eagerRun` does not). Input: 25 descending blocks of 4 (n = 100,
minGood 50).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	var base = 0
	var blk = 0
	while blk < 25 'blocks'
		a.push(base + 4)
		a.push(base + 3)
		a.push(base + 2)
		a.push(base + 1)
		base = base + 10
		blk = blk + 1
	end 'blocks'
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "driftsort.logicalRun") 'lr'
		print("logicalRun ")
	end 'lr'
	if not Log.fired(keys, key: "driftsort.eagerRun") 'ne'
		print("no-eager ")
	end 'ne'
	var ok = true
	for i in 1 upto 100 'c'
		let p = try a.get(i - 1) otherwise return 90
		let cv = try a.get(i) otherwise return 91
		if p > cv 'bad'
			ok = false
		end 'bad'
	end 'c'
	if ok 'o'
		print("sorted")
	end 'o'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
logicalRun no-eager sorted
```

<!-- test: driftsort-ascending-fast-path -->
A fully-ascending input is detected as one big run; no merges fire.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
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
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "findRun.ascending") 'fa'
		print("ascending ")
	end 'fa'
	if not Log.fired(keys, key: "findRun.descending") 'nd'
		print("no-descending ")
	end 'nd'
	if not Log.fired(keys, key: "driftsort.merge") 'nm'
		print("no-merge")
	end 'nm'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ascending no-descending no-merge
```

<!-- test: driftsort-descending-reversed -->
A fully-descending input fires findRun.descending and is reversed in place.
After reversal, the powersort stack holds one run and no merges happen.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(35)
	a.push(34)
	a.push(33)
	a.push(32)
	a.push(31)
	a.push(30)
	a.push(29)
	a.push(28)
	a.push(27)
	a.push(26)
	a.push(25)
	a.push(24)
	a.push(23)
	a.push(22)
	a.push(21)
	a.push(20)
	a.push(19)
	a.push(18)
	a.push(17)
	a.push(16)
	a.push(15)
	a.push(14)
	a.push(13)
	a.push(12)
	a.push(11)
	a.push(10)
	a.push(9)
	a.push(8)
	a.push(7)
	a.push(6)
	a.push(5)
	a.push(4)
	a.push(3)
	a.push(2)
	a.push(1)
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "findRun.descending") 'fd'
		print("descending ")
	end 'fd'
	if not Log.fired(keys, key: "driftsort.merge") 'nm'
		print("no-merge ")
	end 'nm'
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
descending no-merge 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 
```

<!-- test: driftsort-cross-check-with-reference -->
Cross-check: driftsort and `referenceMergeSort` produce byte-identical output
on a scrambled input. This is the oracle pattern Stages 6-7 will rely on.
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
	var b = IntArray.create()
	for x in a 'copy'
		b.push(x)
	end 'copy'
	a.sort(ascending)
	b.referenceMergeSort(ascending)
	var equal = a.count() == b.count()
	if equal 'check'
		for i in 0 upto a.count() 'walk'
			let av = try a.get(i) otherwise return 99
			let bv = try b.get(i) otherwise return 99
			if av != bv 'diff'
				equal = false
			end 'diff'
		end 'walk'
	end 'check'
	if equal 'eq'
		print("equal\n")
	end 'eq' else 'ne'
		print("diverged\n")
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
```

## Stage 6: Logarithmic auxiliary buffer + rotation-merge fallback

Stage 6 replaces driftsort's O(n) scratch buffer with an O(log n) buffer.
Each merge uses `mergeAdaptive`, which picks one of two paths:

- **Buffered merge** (`mergeBuffer.fits`): the smaller of the two runs fits
  in scratch. Copy it into the buffer, then sweep-merge in place. Same
  performance profile as the Stage 4 two-pointer merge but uses
  `min(leftLen, rightLen)` slots instead of `leftLen + rightLen`.
- **Rotation merge** (`mergeBuffer.exhausted` + `rotationMerge.fire`):
  when even the smaller side exceeds the buffer cap, fall back to an
  in-place merge built from three-reverse rotations. Slower asymptotically
  (worst-case O(n²) in the rotation step) but uses no auxiliary storage.

Buffer cap: starts at 64 and grows by 4 per doubling of n. For n = 10^6
that's ~144 slots — about four orders of magnitude less memory than the
old O(n) buffer. For typical inputs the buffered path covers >99% of
merges; the rotation fallback only fires on highly-unbalanced merges.

Stage 8 will revisit the cap if the rotation fallback turns out to fire
too often in practice.

Trace keys:

- `buffer.alloc`           — scratch buffer was allocated (once per sort)
- `mergeBuffer.fits`       — buffered merge ran (smaller side fit in scratch)
- `mergeBuffer.exhausted`  — smaller side exceeded scratch; rotation fired
- `rotationMerge.fire`     — in-place rotation merge ran on a slice

<!-- test: driftsort-bounded-buffer-fits -->
A typical scrambled input has lots of small runs (after `minRun` padding)
that all fit in the log-sized buffer. `mergeBuffer.fits` fires; the
exhaustion path does not.
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
	Log.startCapture()
	a.sort(ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "buffer.alloc") 'ba'
		print("buffer.alloc ")
	end 'ba'
	if Log.fired(keys, key: "mergeBuffer.fits") 'mf'
		print("mergeBuffer.fits ")
	end 'mf'
	if not Log.fired(keys, key: "mergeBuffer.exhausted") 'no'
		print("no-rotation")
	end 'no'
	print("\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
buffer.alloc mergeBuffer.fits no-rotation
```

<!-- test: rotation-merge-correctness -->
Direct exercise of the rotation merge primitive. `mergeRotation` is
exported from mergeSort.maxon; call it directly on two pre-sorted halves
to verify correctness independent of driftsort's dispatcher.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	// Left half [0, 5): ascending. Right half [5, 10): ascending.
	a.push(1)
	a.push(4)
	a.push(6)
	a.push(8)
	a.push(10)
	a.push(2)
	a.push(3)
	a.push(5)
	a.push(7)
	a.push(9)
	Log.startCapture()
	a.mergeRotation(0, mid: 5, hi: 10, cmp: ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "rotationMerge.fire") 'rf'
		print("rotationMerge.fire ")
	end 'rf'
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
rotationMerge.fire 1 2 3 4 5 6 7 8 9 10 
```

## Stage 7: Quicksort-flavored stable partition merge (experimental, off the default path)

`mergePartition` is a quicksort-flavored stable merge: when merging two large sorted runs that both exceed the scratch buffer, pick a pivot from the longer run's midpoint, use binary search to find where that pivot lands in the shorter run, rotate the middle block so the partition is in place, then recurse on the two smaller sub-merges. Each recursion halves the larger run, so after O(log(maxRunLen / scratchCap)) levels each sub-merge's smaller side fits in scratch and `mergeBuffered` finishes in O(n) time. Stable: equal-keyed elements on the left run retain their position before equal-keyed right-run elements (`lowerBound` / `upperBound` semantics).

This is glidesort's signature large-run speedup. It is NOT on driftsort's default path: `mergeAdaptive` uses the in-place rotation merge for the large-both-sides case instead. `mergePartition` is kept compiled and directly tested (the tests below call it explicitly) so it can be benchmarked and potentially re-enabled after profiling, but `Array.sort` never reaches it. The performance primitives needed for true Rust parity (branchless small-sort, uninitialized scratch, refcount-bypassing element moves) are deferred until after profiling.

Trace keys:

- `partitionMerge.fire`   — the quicksort-flavored stable partition ran on a slice
- `rotationMerge.fire`    — the in-place rotation fallback ran (the rotation merge is the default large-run fallback)

<!-- test: partition-merge-direct-correctness -->
Direct exercise of `mergePartition` with a tiny scratch cap, forcing the
partition path. Inputs: two sorted runs (odds then evens). Expected: fully
merged ascending output.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias ScratchArr = Array with Integer
typealias Idx = int(0 to u64.max)

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 20 'l'
		a.push(i * 2 + 1)
	end 'l'
	for j in 0 upto 20 'r'
		a.push(j * 2 + 2)
	end 'r'
	var scratch = ScratchArr.create()
	let cap = 4 as Idx
	scratch.resize(cap)
	Log.startCapture()
	a.mergePartition(0, mid: 20, hi: 40, scratch: scratch, scratchCap: cap, cmp: ascending)
	let keys = Log.stopCapture()
	if Log.fired(keys, key: "partitionMerge.fire") 'pf'
		print("partitionMerge.fire ")
	end 'pf'
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
partitionMerge.fire 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40 
```

<!-- test: partition-merge-stability -->
Stability check: partition merge with duplicate keys. Tagged tuples sorted
by key; equal-key elements from the LEFT run must come before equal-key
elements from the RIGHT run (use lowerBound for ascending positions).
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Idx = int(0 to u64.max)

type Tagged implements Comparable
	export var key as Integer
	export var tag as Integer

	export static function init(key Integer, tag Integer) returns Self
		return Self{key: key, tag: tag}
	end 'init'

	export function compare(other Self) returns Ordering
		return key.compare(other.key)
	end 'compare'
end 'Tagged'

typealias TaggedArr = Array with Tagged
typealias ScratchArr = Array with Tagged

function byKey(a Tagged, b Tagged) returns Ordering
	let ak = a.key
	let bk = b.key
	return ak.compare(bk)
end 'byKey'

function main() returns ExitCode
	var a = TaggedArr.create()
	// Left run: keys 1,1,2,2,3 with tags 0..4
	a.push(Tagged.init(1, tag: 0))
	a.push(Tagged.init(1, tag: 1))
	a.push(Tagged.init(2, tag: 2))
	a.push(Tagged.init(2, tag: 3))
	a.push(Tagged.init(3, tag: 4))
	a.push(Tagged.init(3, tag: 5))
	a.push(Tagged.init(4, tag: 6))
	a.push(Tagged.init(4, tag: 7))
	a.push(Tagged.init(5, tag: 8))
	a.push(Tagged.init(5, tag: 9))
	// Right run: keys 1,2,3,4,5 with tags 100..109 (interleaved values).
	a.push(Tagged.init(1, tag: 100))
	a.push(Tagged.init(1, tag: 101))
	a.push(Tagged.init(2, tag: 102))
	a.push(Tagged.init(2, tag: 103))
	a.push(Tagged.init(3, tag: 104))
	a.push(Tagged.init(3, tag: 105))
	a.push(Tagged.init(4, tag: 106))
	a.push(Tagged.init(4, tag: 107))
	a.push(Tagged.init(5, tag: 108))
	a.push(Tagged.init(5, tag: 109))
	var scratch = ScratchArr.create()
	let cap = 4 as Idx
	scratch.resize(cap)
	a.mergePartition(0, mid: 10, hi: 20, scratch: scratch, scratchCap: cap, cmp: byKey)
	// Check stability: within each key, all tag-< 100 elements come first.
	var stable = true
	var prevKey = 0
	var seenRight = false
	for i in 0 upto a.count() 'walk'
		let cur = try a.get(i) otherwise return 99
		if cur.key != prevKey 'newGroup'
			prevKey = cur.key
			seenRight = false
		end 'newGroup'
		if cur.tag >= 100 'isRight'
			seenRight = true
		end 'isRight' else 'isLeft'
			if seenRight 'leftAfterRight'
				stable = false
			end 'leftAfterRight'
		end 'isLeft'
	end 'walk'
	if stable 'ok'
		print("stable\n")
	end 'ok' else 'no'
		print("unstable\n")
	end 'no'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stable
```

<!-- test: driftsort-stage7-cross-check-large -->
Large cross-check: driftsort vs `referenceMergeSort` on a 100-element
input that mixes ascending and descending runs. Both algorithms must
produce byte-identical output. On driftsort's default path this exercises
the buffered and rotation merges depending on run sizes (the partition
merge is off the default path).
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
	var b = IntArray.create()
	for x in a 'copy'
		b.push(x)
	end 'copy'
	a.sort(ascending)
	b.referenceMergeSort(ascending)
	var equal = a.count() == b.count()
	if equal 'check'
		for i in 0 upto a.count() 'walk'
			let av = try a.get(i) otherwise return 99
			let bv = try b.get(i) otherwise return 99
			if av != bv 'diff'
				equal = false
			end 'diff'
		end 'walk'
	end 'check'
	if equal 'eq'
		print("equal\n")
	end 'eq' else 'ne'
		print("diverged\n")
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
```

<!-- test: driftsort-stage6-cross-check -->
Cross-check (Stage 5 + Stage 6 combined): driftsort now uses bounded scratch
and may fall back to rotation merge. Output must still match
`referenceMergeSort` byte-for-byte.
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
	var b = IntArray.create()
	for x in a 'copy'
		b.push(x)
	end 'copy'
	a.sort(ascending)
	b.referenceMergeSort(ascending)
	var equal = a.count() == b.count()
	if equal 'check'
		for i in 0 upto a.count() 'walk'
			let av = try a.get(i) otherwise return 99
			let bv = try b.get(i) otherwise return 99
			if av != bv 'diff'
				equal = false
			end 'diff'
		end 'walk'
	end 'check'
	if equal 'eq'
		print("equal\n")
	end 'eq' else 'ne'
		print("diverged\n")
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
```

<!-- test: driftsort-large-sqrt-cross-check -->
Large cross-check crossing the n > 4096 boundary, where `minGoodRunLen`
switches to `floor(sqrt(n))` and run creation uses the stable quicksort
(including its partition path on runs > 32). A pseudo-random 5000-element
input must sort identically to `referenceMergeSort`, byte-for-byte.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function ascending(x Integer, y Integer) returns Ordering
	return x.compare(y)
end 'ascending'

function main() returns ExitCode
	var a = IntArray.create()
	var b = IntArray.create()
	var r = 2463534242
	for i in 0 upto 5000 'fill'
		r = (r * 1103515245 + 12345) and 0x7FFFFFFF
		let v = r and 0xFFFF
		a.push(v)
		b.push(v)
	end 'fill'
	a.sort(ascending)
	b.referenceMergeSort(ascending)
	var equal = a.count() == b.count()
	if equal 'check'
		for i in 0 upto a.count() 'walk'
			let av = try a.get(i) otherwise return 99
			let bv = try b.get(i) otherwise return 99
			if av != bv 'diff'
				equal = false
			end 'diff'
		end 'walk'
	end 'check'
	if equal 'eq'
		print("equal\n")
	end 'eq' else 'ne'
		print("diverged\n")
	end 'ne'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal
```

