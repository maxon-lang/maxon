---
feature: stable-partition-managed-pivot
status: selfhosted
keywords: [sort, partition, borrow, managed, element, use-after-free, stability, driftsort]
category: memory
---
# A partition's pivot must live in a storage the partition does not write

## Documentation

`stdlib/helpers/sort/driftQuicksort.maxon`'s `stablePartition` read its pivot straight out of the
range it was about to rearrange:

```maxon
let pv = try managed.get(pivotIndex) otherwise panic("…")   // a BORROW of managed[pivotIndex]
…
	try managed.set(w, value: v) otherwise panic("…")        // w walks up to and past pivotIndex
```

Under the self-hosted ownership model an element read is a **borrow** and `managed.set` **releases
the occupant it displaces** (`Internals.__managed_mem_set`), so the moment the compaction cursor
`w` arrives at `pivotIndex` the record `pv` is still comparing against is freed. Nothing else holds
it: the store takes its own reference through `retainFunc@64`, which for a byte record is
`__str_clone` — a DEEP CLONE — so the copy the buffer or the lower slot receives is a *different*
record and the borrowed one drops to zero.

`smallSort.maxon:24-32` names this exact hazard as the reason `swap` routes through the raw
`managed.swap` builtin rather than get + set. `stablePartition` was written without that shield.

**It is a silent WRONG ANSWER, not a crash.** MEASURED on the tree that shipped it, with the suite
green over it — the freed record reads back as poison, its length comes out huge, and every later
element is misclassified:

| branch | correct | measured before the repair |
|---|---|---|
| `bufferGe`, lens `[1,2,10,3,20,30]`, pivot slot 2 | `p=3 lens= 1 2 3 10 20 30` | `p=5` — two elements misfiled |
| `bufferLess`, lens `[20,1,10,30,40,50]`, pivot slot 2 | `p=1 lens= 1 20 10 30 40 50` | `p=1 lens= 1 20 10 30 30 40` — `40` and `50` LOST, `30` duplicated |

The repair parks the pivot in a one-slot storage of its own before the pass begins. No store in the
pass names that storage, so no store can reach the pivot — the soundness argument is one sentence
instead of a per-index case analysis, which is what a borrow this long-lived needs.

**Why these programs are transcriptions.** When they were written, shv2 did not load
`stdlib/helpers/sort/` at all — the loader's whitelist listed neither it nor `stdlib/Array.maxon`, and
`Array` was synthesized and did not serve `sort`. ⚠ **That half of the reason has expired: the filter is
gone and every file under `stdlib/` now loads.** What still makes this file the only place the algorithm
runs under the model the defect lives in is the OTHER half. The C# bootstrap does compile that cone, but its refcount
model is the opposite one — *"loads take refs, stores release the displaced occupant's"*
(`MaxonToStandardConversion.ManagedMemory.cs`, the `__managed_mem_swap` arm) — so a get + set
exchange balances there and the defect **cannot** be reached through it. That leaves this file as
the only place the algorithm runs under the model the defect lives in, so the bodies below are
copied from `driftQuicksort.maxon` rather than called into. Keep them in step with it.

## Tests

<!-- test: a-partition-parks-its-pivot-out-of-its-own-write-range -->
### Both buffering branches, with a pivot no store can displace
`stablePartition` transcribed whole. `bufferGe` buffers the `>= pivot` class and compacts `< pivot`
in place; `bufferLess` buffers `< pivot`, compacts `>= pivot`, then shifts it right. Both drive the
compaction cursor over the pivot's original slot, and both must still answer correctly.
```maxon
typealias Small = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	typealias Cmp = function(Element, Element) returns Ordering
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	// `stdlib/Array.maxon:363`'s own one-line body. `clear` left the synthesized surface at ARR4, so a
	// container declared here declares it exactly as the library does — the transcription stays in step
	// with `driftQuicksort.maxon`, whose receiver is that library `Array`.
	export function clear()
		managed.clear()
	end 'clear'

	export function stablePartition(lo Small, hi Small, pivotIndex Small, scratch Self, pivotHold Self, cmp Cmp) returns Small
		let pivotAtHome = try managed.get(pivotIndex) otherwise panic("get OOB at pivot")
		pivotHold.clear()
		pivotHold.push(pivotAtHome)
		let pv = try pivotHold.get(0) otherwise panic("pivotHold empty one line after a push")
		var lessCount = 0 as Small
		var scan = lo
		while scan < hi 'count'
			let v = try managed.get(scan) otherwise panic("get OOB during count")
			if cmp(v, pv) == Ordering.lessThan 'isLess'
				lessCount = lessCount + 1
			end 'isLess'
			scan = scan + 1
		end 'count'
		let geCount = (hi - lo) - lessCount

		if geCount <= lessCount 'bufferGe'
			var w = lo
			var k = 0 as Small
			var i = lo
			while i < hi 'splitGe'
				let v = try managed.get(i) otherwise panic("get OOB at i (ge)")
				if cmp(v, pv) != Ordering.lessThan 'toScratch'
					try scratch.set(k, value: v) otherwise panic("scratch.set OOB (ge)")
					k = k + 1
				end 'toScratch' else 'keepLeft'
					try managed.set(w, value: v) otherwise panic("set OOB at w (ge)")
					w = w + 1
				end 'keepLeft'
				i = i + 1
			end 'splitGe'
			var out = lo + lessCount
			var s = 0 as Small
			while s < geCount 'writeGe'
				let v = try scratch.get(s) otherwise panic("scratch.get OOB (ge)")
				try managed.set(out, value: v) otherwise panic("set OOB at out (ge)")
				out = out + 1
				s = s + 1
			end 'writeGe'
			return lo + lessCount
		end 'bufferGe' else 'bufferLess'
			var w = lo
			var k = 0 as Small
			var i = lo
			while i < hi 'splitLess'
				let v = try managed.get(i) otherwise panic("get OOB at i (less)")
				if cmp(v, pv) == Ordering.lessThan 'toScratch2'
					try scratch.set(k, value: v) otherwise panic("scratch.set OOB (less)")
					k = k + 1
				end 'toScratch2' else 'keepRight'
					try managed.set(w, value: v) otherwise panic("set OOB at w (less)")
					w = w + 1
				end 'keepRight'
				i = i + 1
			end 'splitLess'
			var src = lo + geCount
			while src > lo 'shiftGe'
				let v = try managed.get(src - 1) otherwise panic("get OOB during shift")
				try managed.set(src - 1 + lessCount, value: v) otherwise panic("set OOB during shift")
				src = src - 1
			end 'shiftGe'
			var out = lo
			var s = 0 as Small
			while s < lessCount 'writeLess'
				let v = try scratch.get(s) otherwise panic("scratch.get OOB (less)")
				try managed.set(out, value: v) otherwise panic("set OOB at out (less)")
				out = out + 1
				s = s + 1
			end 'writeLess'
			return lo + lessCount
		end 'bufferLess'
	end 'stablePartition'
end 'Array'

typealias StrArray = Array with String
typealias LenArray = Array with Small

// Built by interpolation, so every record is a heap record rather than an
// immortal `.rdata` literal a release could never free.
function pad(n Small) returns String
	var s = ""
	for k in 0 upto n 'grow'
		s.append("{k mod 10}")
	end 'grow'
	return s
end 'pad'

function lensOf(a Small, b Small, c Small, d Small, e Small, f Small) returns LenArray
	var out = LenArray.create()
	out.push(a)
	out.push(b)
	out.push(c)
	out.push(d)
	out.push(e)
	out.push(f)
	return out
end 'lensOf'

function run(label String, lens LenArray, pivotIndex Small) returns String
	var a = StrArray.create()
	var scratch = StrArray.create()
	for i in 0 upto lens.count() 'build'
		a.push(pad(try lens.get(i) otherwise 1))
		scratch.push(pad(1))
	end 'build'
	var hold = StrArray.create()
	let p = a.stablePartition(0, hi: lens.count(), pivotIndex: pivotIndex, scratch: scratch, pivotHold: hold, cmp: function(x String, y String) gives x.byteLength().compare(y.byteLength()))
	var out = "{label} p={p} lens="
	for j in 0 upto lens.count() 'show'
		out.append(" {(try a.get(j) otherwise "").byteLength()}")
	end 'show'
	return out
end 'run'

function main() returns ExitCode
	print("{run("ge", lens: lensOf(1, b: 2, c: 10, d: 3, e: 20, f: 30), pivotIndex: 2)}\n")
	print("{run("less", lens: lensOf(20, b: 1, c: 10, d: 30, e: 40, f: 50), pivotIndex: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
ge p=3 lens= 1 2 3 10 20 30
less p=1 lens= 1 20 10 30 40 50
```

<!-- test: a-parked-pivot-keeps-the-partition-stable -->
### … and the two classes keep their input order
Soundness is not enough on its own: `sort()` is the STABLE entry, and the partition is where its
stability comes from. Six records with three distinct keys, each key carried twice, and a tag in
the first byte that the comparator cannot see. Both classes must come out in input-tag order —
`1 3 5` (the `< pivot` class) then `0 2 4` (the `>= pivot` class).
```maxon
typealias Small = int(0 to 1000)

type Array uses Element implements BuiltinArrayLiteral
	typealias ElementMemory = __ManagedMemory with Element
	typealias Cmp = function(Element, Element) returns Ordering
	export var managed as ElementMemory

	export static function init(managed ElementMemory) returns Self
		return Self{managed: managed}
	end 'init'

	export static function create() returns Self
		return Self{}
	end 'create'

	// `stdlib/Array.maxon:363`'s own one-line body. `clear` left the synthesized surface at ARR4, so a
	// container declared here declares it exactly as the library does — the transcription stays in step
	// with `driftQuicksort.maxon`, whose receiver is that library `Array`.
	export function clear()
		managed.clear()
	end 'clear'

	export function stablePartition(lo Small, hi Small, pivotIndex Small, scratch Self, pivotHold Self, cmp Cmp) returns Small
		let pivotAtHome = try managed.get(pivotIndex) otherwise panic("get OOB at pivot")
		pivotHold.clear()
		pivotHold.push(pivotAtHome)
		let pv = try pivotHold.get(0) otherwise panic("pivotHold empty one line after a push")
		var lessCount = 0 as Small
		var scan = lo
		while scan < hi 'count'
			let v = try managed.get(scan) otherwise panic("get OOB during count")
			if cmp(v, pv) == Ordering.lessThan 'isLess'
				lessCount = lessCount + 1
			end 'isLess'
			scan = scan + 1
		end 'count'
		let geCount = (hi - lo) - lessCount

		if geCount <= lessCount 'bufferGe'
			var w = lo
			var k = 0 as Small
			var i = lo
			while i < hi 'splitGe'
				let v = try managed.get(i) otherwise panic("get OOB at i (ge)")
				if cmp(v, pv) != Ordering.lessThan 'toScratch'
					try scratch.set(k, value: v) otherwise panic("scratch.set OOB (ge)")
					k = k + 1
				end 'toScratch' else 'keepLeft'
					try managed.set(w, value: v) otherwise panic("set OOB at w (ge)")
					w = w + 1
				end 'keepLeft'
				i = i + 1
			end 'splitGe'
			var out = lo + lessCount
			var s = 0 as Small
			while s < geCount 'writeGe'
				let v = try scratch.get(s) otherwise panic("scratch.get OOB (ge)")
				try managed.set(out, value: v) otherwise panic("set OOB at out (ge)")
				out = out + 1
				s = s + 1
			end 'writeGe'
			return lo + lessCount
		end 'bufferGe' else 'bufferLess'
			var w = lo
			var k = 0 as Small
			var i = lo
			while i < hi 'splitLess'
				let v = try managed.get(i) otherwise panic("get OOB at i (less)")
				if cmp(v, pv) == Ordering.lessThan 'toScratch2'
					try scratch.set(k, value: v) otherwise panic("scratch.set OOB (less)")
					k = k + 1
				end 'toScratch2' else 'keepRight'
					try managed.set(w, value: v) otherwise panic("set OOB at w (less)")
					w = w + 1
				end 'keepRight'
				i = i + 1
			end 'splitLess'
			var src = lo + geCount
			while src > lo 'shiftGe'
				let v = try managed.get(src - 1) otherwise panic("get OOB during shift")
				try managed.set(src - 1 + lessCount, value: v) otherwise panic("set OOB during shift")
				src = src - 1
			end 'shiftGe'
			var out = lo
			var s = 0 as Small
			while s < lessCount 'writeLess'
				let v = try scratch.get(s) otherwise panic("scratch.get OOB (less)")
				try managed.set(out, value: v) otherwise panic("set OOB at out (less)")
				out = out + 1
				s = s + 1
			end 'writeLess'
			return lo + lessCount
		end 'bufferLess'
	end 'stablePartition'
end 'Array'

typealias StrArray = Array with String

// A heap record of byte length `key` whose FIRST byte is the input tag. The
// comparator reads only the length, so the tag is invisible to the ordering and
// any reshuffle of equal keys shows up in the tag sequence.
function tagged(key Small, tag Small) returns String
	var s = "{tag mod 10}"
	while s.byteLength() < key 'grow'
		s.append(".")
	end 'grow'
	return s
end 'tagged'

function main() returns ExitCode
	var a = StrArray.create()
	var scratch = StrArray.create()
	a.push(tagged(10, tag: 0))
	a.push(tagged(5, tag: 1))
	a.push(tagged(10, tag: 2))
	a.push(tagged(5, tag: 3))
	a.push(tagged(10, tag: 4))
	a.push(tagged(5, tag: 5))
	for f in 0 upto 6 'fillScratch'
		scratch.push(tagged(3, tag: f))
	end 'fillScratch'
	var hold = StrArray.create()
	let p = a.stablePartition(0, hi: 6, pivotIndex: 0, scratch: scratch, pivotHold: hold, cmp: function(x String, y String) gives x.byteLength().compare(y.byteLength()))
	var tags = ""
	var lens = ""
	for j in 0 upto 6 'show'
		let v = try a.get(j) otherwise ""
		tags.append("{v.slice(v.startIndex(), length: 1)}")
		lens.append(" {v.byteLength()}")
	end 'show'
	print("p={p} tags={tags} lens={lens}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
p=3 tags=135024 lens= 5 5 5 10 10 10
```
