---
feature: array-sort
status: experimental
keywords: [array, sort, comparable, ordering, glidesort, pdqsort]
category: collections
---

# Array Sort

## Documentation

`Array<T>` exposes four sort entry points:

- `sort()` — stable sort using the element's `Comparable.compare` ordering. Requires `Element is Comparable`.
- `sort(cmp)` — stable sort using a caller-supplied comparator `function(Element, Element) returns Ordering`.
- `sortUnstable()` — unstable sort via the element's `Comparable.compare` ordering. Requires `Element is Comparable`. Routed through the same `comparableInsertionSort` helper as `sort()` until the self-hosted compiler implements interface-method dispatch on type-parameter receivers (Phase 11.4); the API distinction is reserved so callers can opt out of stability up front.
- `sortUnstable(cmp)` — unstable sort using a caller-supplied comparator.

Stage 1: all four entries route to insertion sort. Stage 2 layers in sorting networks for small slices, Stage 3 routes the unstable entries to pdqsort, and Stages 4–7 layer in glidesort for the stable entries.

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

