---
feature: allocations-by-tag
status: experimental
keywords: [allocator, allocation, tally, churn, tag, count, debugstream, memory]
category: system
---

# Allocation churn by tag — what the program ASKED FOR, and on whose behalf

## Documentation

`slab-census.md` says what the heap is HOLDING right now, by walking every live slot. That walk cannot see
a box that was allocated and freed a microsecond later, and a phase whose whole cost is churn — a million
short-lived boxes that leave the residency table exactly where they found it — is invisible to every level
it reports. `builtins-mm-counters.md`'s `__Builtins.mmAllocTotal()` DOES see that churn, as one
process-wide number, and attributes none of it.

Two builtins split that one number by the allocation TAG.

| Builtin | What it answers |
|---|---|
| `__Builtins.mmAllocTotalByTag(i)` | how many `__mm_alloc` calls bucket `i` has taken since the process started |
| `__Builtins.mmAllocBytesByTag(i)` | how many bytes those calls ASKED FOR |

⭐⭐ **THE TALLY IS CUMULATIVE AND NEVER FALLS.** `__mm_alloc` steps a bucket and nothing else touches it —
a free steps nothing, a scavenge steps nothing, a drop steps nothing. That is what makes it a CHURN figure
rather than a level: a phase that allocates a million boxes and frees every one of them moves its bucket by
a million, and the census's tables are back where they started. ⇒ **a bucket that falls is a defect**, and
`a-tagged-population-raises-one-bucket-by-N` reports one.

⭐ **THE GEOMETRY IS 2048 BUCKETS AND EVERY TAG LANDS IN EXACTLY ONE.** Bucket `i` holds the allocations
whose tag index is `i`; bucket 0 holds the UNTAGGED ones; bucket 2047 is the OVERFLOW, holding every
allocation whose tag is 2047 or higher. `slab-census.md` owns the tags themselves — what a tag index means,
and which type minted it.

⭐⭐ **Σ OVER THE 2048 COUNT BUCKETS IS EXACTLY `__Builtins.mmAllocTotal()`.** Both count the same
`__mm_alloc` calls, so the equality is not an approximation and it holds at every instant a program can
read the two. It is the one relation the buckets can be checked against without naming a tag index, and
`the-buckets-total-the-tracked-allocations` is the gate: a tally that misses an arrival breaks it low, and
one that steps two buckets per allocation breaks it high.

⚠ **AN UNTRACED BUILD ANSWERS 0 FOR EVERY BUCKET OF BOTH BUILTINS.** A tag exists only in a
`--debugstream` build — it is carried by the packed word that build prepends to each box — so there is
nothing to index by, and the stepping is not compiled at all. The builtins remain callable, and answer the
honest 0 rather than a figure the program would have to know not to believe.

⚠ **THE PROGRAM CANNOT NAME ITS OWN TAG INDEX.** A tag is minted by the compiler, so a case pinned to a
number would be re-minted by every type the tree adds ahead of the one under test. Every case below states
its relation through the LARGEST-RISING bucket instead, which is the same fact said in something the
program can observe.

⚠ **THE READING ITSELF MUST NOT ALLOCATE.** Each case reserves its holder and sizes its snapshot BEFORE
the first reading, so no buffer growth lands between two readings; a case that grew an array mid-walk would
be reporting its own instrumentation.

`builtins-mm-counters.md` owns the counter family these buckets split, and `slab-census.md` owns the tag
census and the tag names.

## Tests

<!-- test: allocations-by-tag.a-tagged-population-raises-one-bucket-by-N -->
RED: the pre-change tree knows no `__Builtins.mmAllocTotalByTag`, so the case dies at the unknown
`__Builtins` member with E3004 (`callUnknownFunction`) and never runs. Once the builtins exist, a
population of N widgets built between two readings must raise exactly one bucket by exactly N, must lower
none, and must raise that bucket's byte column too — a count column wired to a byte column, or either one
stepped by the wrong amount, fails here.
<!-- MmTrace -->
```maxon
typealias WidgetField = int(0 to 65536)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Population = 64
let TallyBuckets = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	var beforeCounts = TallyArray.create()
	beforeCounts.reserve(TallyBuckets)
	for _ in 0 upto TallyBuckets 'sizeTheCountSnapshot'
		beforeCounts.push(0)
	end 'sizeTheCountSnapshot'

	var beforeBytes = TallyArray.create()
	beforeBytes.reserve(TallyBuckets)
	for _ in 0 upto TallyBuckets 'sizeTheByteSnapshot'
		beforeBytes.push(0)
	end 'sizeTheByteSnapshot'

	for i in 0 upto TallyBuckets 'snapshot'
		try beforeCounts.set(i, value: __Builtins.mmAllocTotalByTag(i)) otherwise panic("the snapshot is sized to every bucket")
		try beforeBytes.set(i, value: __Builtins.mmAllocBytesByTag(i)) otherwise panic("the snapshot is sized to every bucket")
	end 'snapshot'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	var biggestRise = 0
	var biggestIndex = 0
	for i in 0 upto TallyBuckets 'compare'
		let was = try beforeCounts.get(i) otherwise panic("the snapshot is sized to every bucket")
		let now = __Builtins.mmAllocTotalByTag(i)

		if now < was 'aBucketFell'
			return 2
		end 'aBucketFell'

		if (now - was) > biggestRise 'rose'
			biggestRise = now - was
			biggestIndex = i
		end 'rose'
	end 'compare'

	if biggestRise != Population 'noBucketRoseByThePopulation'
		return 1
	end 'noBucketRoseByThePopulation'

	let bytesWere = try beforeBytes.get(biggestIndex) otherwise panic("the snapshot is sized to every bucket")

	if __Builtins.mmAllocBytesByTag(biggestIndex) <= bytesWere 'theBucketsBytesDidNotRise'
		return 3
	end 'theBucketsBytesDidNotRise'

	if holder.count() != Population 'thePopulationIsNotHeld'
		return 4
	end 'thePopulationIsNotHeld'

	return 0
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc ElementBuffer #2 size=512
mm_alloc ArrayRecord #3 size=48
mm_alloc ElementBuffer #4 size=16384
mm_alloc ArrayRecord #5 size=48
mm_alloc ElementBuffer #6 size=16384
mm_alloc Widget #7 size=16
mm_alloc Widget #8 size=16
mm_alloc Widget #9 size=16
mm_alloc Widget #10 size=16
mm_alloc Widget #11 size=16
mm_alloc Widget #12 size=16
mm_alloc Widget #13 size=16
mm_alloc Widget #14 size=16
mm_alloc Widget #15 size=16
mm_alloc Widget #16 size=16
mm_alloc Widget #17 size=16
mm_alloc Widget #18 size=16
mm_alloc Widget #19 size=16
mm_alloc Widget #20 size=16
mm_alloc Widget #21 size=16
mm_alloc Widget #22 size=16
mm_alloc Widget #23 size=16
mm_alloc Widget #24 size=16
mm_alloc Widget #25 size=16
mm_alloc Widget #26 size=16
mm_alloc Widget #27 size=16
mm_alloc Widget #28 size=16
mm_alloc Widget #29 size=16
mm_alloc Widget #30 size=16
mm_alloc Widget #31 size=16
mm_alloc Widget #32 size=16
mm_alloc Widget #33 size=16
mm_alloc Widget #34 size=16
mm_alloc Widget #35 size=16
mm_alloc Widget #36 size=16
mm_alloc Widget #37 size=16
mm_alloc Widget #38 size=16
mm_alloc Widget #39 size=16
mm_alloc Widget #40 size=16
mm_alloc Widget #41 size=16
mm_alloc Widget #42 size=16
mm_alloc Widget #43 size=16
mm_alloc Widget #44 size=16
mm_alloc Widget #45 size=16
mm_alloc Widget #46 size=16
mm_alloc Widget #47 size=16
mm_alloc Widget #48 size=16
mm_alloc Widget #49 size=16
mm_alloc Widget #50 size=16
mm_alloc Widget #51 size=16
mm_alloc Widget #52 size=16
mm_alloc Widget #53 size=16
mm_alloc Widget #54 size=16
mm_alloc Widget #55 size=16
mm_alloc Widget #56 size=16
mm_alloc Widget #57 size=16
mm_alloc Widget #58 size=16
mm_alloc Widget #59 size=16
mm_alloc Widget #60 size=16
mm_alloc Widget #61 size=16
mm_alloc Widget #62 size=16
mm_alloc Widget #63 size=16
mm_alloc Widget #64 size=16
mm_alloc Widget #65 size=16
mm_alloc Widget #66 size=16
mm_alloc Widget #67 size=16
mm_alloc Widget #68 size=16
mm_alloc Widget #69 size=16
mm_alloc Widget #70 size=16
mm_decref ArrayRecord #5 rc=0
mm_decref ElementBuffer #6 rc=0
mm_free ElementBuffer #6
mm_free ArrayRecord #5
mm_decref ArrayRecord #3 rc=0
mm_decref ElementBuffer #4 rc=0
mm_free ElementBuffer #4
mm_free ArrayRecord #3
mm_decref ArrayRecord #1 rc=0
mm_decref Widget #7 rc=0
mm_free Widget #7
mm_decref Widget #8 rc=0
mm_free Widget #8
mm_decref Widget #9 rc=0
mm_free Widget #9
mm_decref Widget #10 rc=0
mm_free Widget #10
mm_decref Widget #11 rc=0
mm_free Widget #11
mm_decref Widget #12 rc=0
mm_free Widget #12
mm_decref Widget #13 rc=0
mm_free Widget #13
mm_decref Widget #14 rc=0
mm_free Widget #14
mm_decref Widget #15 rc=0
mm_free Widget #15
mm_decref Widget #16 rc=0
mm_free Widget #16
mm_decref Widget #17 rc=0
mm_free Widget #17
mm_decref Widget #18 rc=0
mm_free Widget #18
mm_decref Widget #19 rc=0
mm_free Widget #19
mm_decref Widget #20 rc=0
mm_free Widget #20
mm_decref Widget #21 rc=0
mm_free Widget #21
mm_decref Widget #22 rc=0
mm_free Widget #22
mm_decref Widget #23 rc=0
mm_free Widget #23
mm_decref Widget #24 rc=0
mm_free Widget #24
mm_decref Widget #25 rc=0
mm_free Widget #25
mm_decref Widget #26 rc=0
mm_free Widget #26
mm_decref Widget #27 rc=0
mm_free Widget #27
mm_decref Widget #28 rc=0
mm_free Widget #28
mm_decref Widget #29 rc=0
mm_free Widget #29
mm_decref Widget #30 rc=0
mm_free Widget #30
mm_decref Widget #31 rc=0
mm_free Widget #31
mm_decref Widget #32 rc=0
mm_free Widget #32
mm_decref Widget #33 rc=0
mm_free Widget #33
mm_decref Widget #34 rc=0
mm_free Widget #34
mm_decref Widget #35 rc=0
mm_free Widget #35
mm_decref Widget #36 rc=0
mm_free Widget #36
mm_decref Widget #37 rc=0
mm_free Widget #37
mm_decref Widget #38 rc=0
mm_free Widget #38
mm_decref Widget #39 rc=0
mm_free Widget #39
mm_decref Widget #40 rc=0
mm_free Widget #40
mm_decref Widget #41 rc=0
mm_free Widget #41
mm_decref Widget #42 rc=0
mm_free Widget #42
mm_decref Widget #43 rc=0
mm_free Widget #43
mm_decref Widget #44 rc=0
mm_free Widget #44
mm_decref Widget #45 rc=0
mm_free Widget #45
mm_decref Widget #46 rc=0
mm_free Widget #46
mm_decref Widget #47 rc=0
mm_free Widget #47
mm_decref Widget #48 rc=0
mm_free Widget #48
mm_decref Widget #49 rc=0
mm_free Widget #49
mm_decref Widget #50 rc=0
mm_free Widget #50
mm_decref Widget #51 rc=0
mm_free Widget #51
mm_decref Widget #52 rc=0
mm_free Widget #52
mm_decref Widget #53 rc=0
mm_free Widget #53
mm_decref Widget #54 rc=0
mm_free Widget #54
mm_decref Widget #55 rc=0
mm_free Widget #55
mm_decref Widget #56 rc=0
mm_free Widget #56
mm_decref Widget #57 rc=0
mm_free Widget #57
mm_decref Widget #58 rc=0
mm_free Widget #58
mm_decref Widget #59 rc=0
mm_free Widget #59
mm_decref Widget #60 rc=0
mm_free Widget #60
mm_decref Widget #61 rc=0
mm_free Widget #61
mm_decref Widget #62 rc=0
mm_free Widget #62
mm_decref Widget #63 rc=0
mm_free Widget #63
mm_decref Widget #64 rc=0
mm_free Widget #64
mm_decref Widget #65 rc=0
mm_free Widget #65
mm_decref Widget #66 rc=0
mm_free Widget #66
mm_decref Widget #67 rc=0
mm_free Widget #67
mm_decref Widget #68 rc=0
mm_free Widget #68
mm_decref Widget #69 rc=0
mm_free Widget #69
mm_decref Widget #70 rc=0
mm_free Widget #70
mm_decref ElementBuffer #2 rc=0
mm_free ElementBuffer #2
mm_free ArrayRecord #1
```

<!-- test: allocations-by-tag.the-buckets-total-the-tracked-allocations -->
RED: the pre-change tree refuses the unknown `__Builtins.mmAllocTotalByTag` member with E3004
(`callUnknownFunction`) before anything runs. The relation the case gates afterwards is Σ over the 2048
count buckets equals `__Builtins.mmAllocTotal()`, asked at two points with a population built between
them — so a tally that misses a whole family of arrivals is caught even when every bucket it does step
moves by the right amount. The sum accumulates into a scalar rather than an array, because an array that
grew mid-walk would allocate between the two figures being compared.
<!-- MmTrace -->
```maxon
typealias WidgetField = int(0 to 65536)

let Population = 64
let TallyBuckets = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	let totalBefore = __Builtins.mmAllocTotal()
	var sumBefore = 0
	for i in 0 upto TallyBuckets 'totalTheBucketsBefore'
		sumBefore = sumBefore + __Builtins.mmAllocTotalByTag(i)
	end 'totalTheBucketsBefore'

	if sumBefore != totalBefore 'theBucketsMissedTheTotalBeforeTheBuild'
		return 1
	end 'theBucketsMissedTheTotalBeforeTheBuild'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	let totalAfter = __Builtins.mmAllocTotal()
	var sumAfter = 0
	for i in 0 upto TallyBuckets 'totalTheBucketsAfter'
		sumAfter = sumAfter + __Builtins.mmAllocTotalByTag(i)
	end 'totalTheBucketsAfter'

	if sumAfter != totalAfter 'theBucketsMissedTheTotalAfterTheBuild'
		return 2
	end 'theBucketsMissedTheTotalAfterTheBuild'

	return 0
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc ElementBuffer #2 size=512
mm_alloc Widget #3 size=16
mm_alloc Widget #4 size=16
mm_alloc Widget #5 size=16
mm_alloc Widget #6 size=16
mm_alloc Widget #7 size=16
mm_alloc Widget #8 size=16
mm_alloc Widget #9 size=16
mm_alloc Widget #10 size=16
mm_alloc Widget #11 size=16
mm_alloc Widget #12 size=16
mm_alloc Widget #13 size=16
mm_alloc Widget #14 size=16
mm_alloc Widget #15 size=16
mm_alloc Widget #16 size=16
mm_alloc Widget #17 size=16
mm_alloc Widget #18 size=16
mm_alloc Widget #19 size=16
mm_alloc Widget #20 size=16
mm_alloc Widget #21 size=16
mm_alloc Widget #22 size=16
mm_alloc Widget #23 size=16
mm_alloc Widget #24 size=16
mm_alloc Widget #25 size=16
mm_alloc Widget #26 size=16
mm_alloc Widget #27 size=16
mm_alloc Widget #28 size=16
mm_alloc Widget #29 size=16
mm_alloc Widget #30 size=16
mm_alloc Widget #31 size=16
mm_alloc Widget #32 size=16
mm_alloc Widget #33 size=16
mm_alloc Widget #34 size=16
mm_alloc Widget #35 size=16
mm_alloc Widget #36 size=16
mm_alloc Widget #37 size=16
mm_alloc Widget #38 size=16
mm_alloc Widget #39 size=16
mm_alloc Widget #40 size=16
mm_alloc Widget #41 size=16
mm_alloc Widget #42 size=16
mm_alloc Widget #43 size=16
mm_alloc Widget #44 size=16
mm_alloc Widget #45 size=16
mm_alloc Widget #46 size=16
mm_alloc Widget #47 size=16
mm_alloc Widget #48 size=16
mm_alloc Widget #49 size=16
mm_alloc Widget #50 size=16
mm_alloc Widget #51 size=16
mm_alloc Widget #52 size=16
mm_alloc Widget #53 size=16
mm_alloc Widget #54 size=16
mm_alloc Widget #55 size=16
mm_alloc Widget #56 size=16
mm_alloc Widget #57 size=16
mm_alloc Widget #58 size=16
mm_alloc Widget #59 size=16
mm_alloc Widget #60 size=16
mm_alloc Widget #61 size=16
mm_alloc Widget #62 size=16
mm_alloc Widget #63 size=16
mm_alloc Widget #64 size=16
mm_alloc Widget #65 size=16
mm_alloc Widget #66 size=16
mm_decref ArrayRecord #1 rc=0
mm_decref Widget #3 rc=0
mm_free Widget #3
mm_decref Widget #4 rc=0
mm_free Widget #4
mm_decref Widget #5 rc=0
mm_free Widget #5
mm_decref Widget #6 rc=0
mm_free Widget #6
mm_decref Widget #7 rc=0
mm_free Widget #7
mm_decref Widget #8 rc=0
mm_free Widget #8
mm_decref Widget #9 rc=0
mm_free Widget #9
mm_decref Widget #10 rc=0
mm_free Widget #10
mm_decref Widget #11 rc=0
mm_free Widget #11
mm_decref Widget #12 rc=0
mm_free Widget #12
mm_decref Widget #13 rc=0
mm_free Widget #13
mm_decref Widget #14 rc=0
mm_free Widget #14
mm_decref Widget #15 rc=0
mm_free Widget #15
mm_decref Widget #16 rc=0
mm_free Widget #16
mm_decref Widget #17 rc=0
mm_free Widget #17
mm_decref Widget #18 rc=0
mm_free Widget #18
mm_decref Widget #19 rc=0
mm_free Widget #19
mm_decref Widget #20 rc=0
mm_free Widget #20
mm_decref Widget #21 rc=0
mm_free Widget #21
mm_decref Widget #22 rc=0
mm_free Widget #22
mm_decref Widget #23 rc=0
mm_free Widget #23
mm_decref Widget #24 rc=0
mm_free Widget #24
mm_decref Widget #25 rc=0
mm_free Widget #25
mm_decref Widget #26 rc=0
mm_free Widget #26
mm_decref Widget #27 rc=0
mm_free Widget #27
mm_decref Widget #28 rc=0
mm_free Widget #28
mm_decref Widget #29 rc=0
mm_free Widget #29
mm_decref Widget #30 rc=0
mm_free Widget #30
mm_decref Widget #31 rc=0
mm_free Widget #31
mm_decref Widget #32 rc=0
mm_free Widget #32
mm_decref Widget #33 rc=0
mm_free Widget #33
mm_decref Widget #34 rc=0
mm_free Widget #34
mm_decref Widget #35 rc=0
mm_free Widget #35
mm_decref Widget #36 rc=0
mm_free Widget #36
mm_decref Widget #37 rc=0
mm_free Widget #37
mm_decref Widget #38 rc=0
mm_free Widget #38
mm_decref Widget #39 rc=0
mm_free Widget #39
mm_decref Widget #40 rc=0
mm_free Widget #40
mm_decref Widget #41 rc=0
mm_free Widget #41
mm_decref Widget #42 rc=0
mm_free Widget #42
mm_decref Widget #43 rc=0
mm_free Widget #43
mm_decref Widget #44 rc=0
mm_free Widget #44
mm_decref Widget #45 rc=0
mm_free Widget #45
mm_decref Widget #46 rc=0
mm_free Widget #46
mm_decref Widget #47 rc=0
mm_free Widget #47
mm_decref Widget #48 rc=0
mm_free Widget #48
mm_decref Widget #49 rc=0
mm_free Widget #49
mm_decref Widget #50 rc=0
mm_free Widget #50
mm_decref Widget #51 rc=0
mm_free Widget #51
mm_decref Widget #52 rc=0
mm_free Widget #52
mm_decref Widget #53 rc=0
mm_free Widget #53
mm_decref Widget #54 rc=0
mm_free Widget #54
mm_decref Widget #55 rc=0
mm_free Widget #55
mm_decref Widget #56 rc=0
mm_free Widget #56
mm_decref Widget #57 rc=0
mm_free Widget #57
mm_decref Widget #58 rc=0
mm_free Widget #58
mm_decref Widget #59 rc=0
mm_free Widget #59
mm_decref Widget #60 rc=0
mm_free Widget #60
mm_decref Widget #61 rc=0
mm_free Widget #61
mm_decref Widget #62 rc=0
mm_free Widget #62
mm_decref Widget #63 rc=0
mm_free Widget #63
mm_decref Widget #64 rc=0
mm_free Widget #64
mm_decref Widget #65 rc=0
mm_free Widget #65
mm_decref Widget #66 rc=0
mm_free Widget #66
mm_decref ElementBuffer #2 rc=0
mm_free ElementBuffer #2
mm_free ArrayRecord #1
```

<!-- test: allocations-by-tag.dropping-the-population-leaves-the-bucket -->
RED: E3004 at the unknown `__Builtins.mmAllocTotalByTag` member (`callUnknownFunction`) is what the
pre-change tree answers. The property afterwards is the one that separates a CHURN column from a LEVEL:
the population is dropped, `__Builtins.mmAllocLive()` falls to prove the drop really happened, and the
bucket the population raised does not move at all. A bucket the free path decrements would be a residency
table wearing a churn column's name, and it is caught here and nowhere else in this file.
<!-- MmTrace -->
```maxon
typealias WidgetField = int(0 to 65536)
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Population = 64
let TallyBuckets = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	var beforeCounts = TallyArray.create()
	beforeCounts.reserve(TallyBuckets)
	for _ in 0 upto TallyBuckets 'sizeTheSnapshot'
		beforeCounts.push(0)
	end 'sizeTheSnapshot'

	for i in 0 upto TallyBuckets 'snapshot'
		try beforeCounts.set(i, value: __Builtins.mmAllocTotalByTag(i)) otherwise panic("the snapshot is sized to every bucket")
	end 'snapshot'

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	var biggestRise = 0
	var biggestIndex = 0
	for i in 0 upto TallyBuckets 'compare'
		let was = try beforeCounts.get(i) otherwise panic("the snapshot is sized to every bucket")
		let now = __Builtins.mmAllocTotalByTag(i)

		if (now - was) > biggestRise 'rose'
			biggestRise = now - was
			biggestIndex = i
		end 'rose'
	end 'compare'

	let heldCount = __Builtins.mmAllocTotalByTag(biggestIndex)
	let liveHolding = __Builtins.mmAllocLive()

	holder = Widgets.create()

	if __Builtins.mmAllocTotalByTag(biggestIndex) != heldCount 'theCumulativeBucketMoved'
		return 1
	end 'theCumulativeBucketMoved'

	if __Builtins.mmAllocLive() >= liveHolding 'theDropFreedNothing'
		return 2
	end 'theDropFreedNothing'

	return 0
end 'main'
```
```exitcode
0
```
```mm-trace
mm_alloc ArrayRecord #1 size=48
mm_alloc ElementBuffer #2 size=512
mm_alloc ArrayRecord #3 size=48
mm_alloc ElementBuffer #4 size=16384
mm_alloc Widget #5 size=16
mm_alloc Widget #6 size=16
mm_alloc Widget #7 size=16
mm_alloc Widget #8 size=16
mm_alloc Widget #9 size=16
mm_alloc Widget #10 size=16
mm_alloc Widget #11 size=16
mm_alloc Widget #12 size=16
mm_alloc Widget #13 size=16
mm_alloc Widget #14 size=16
mm_alloc Widget #15 size=16
mm_alloc Widget #16 size=16
mm_alloc Widget #17 size=16
mm_alloc Widget #18 size=16
mm_alloc Widget #19 size=16
mm_alloc Widget #20 size=16
mm_alloc Widget #21 size=16
mm_alloc Widget #22 size=16
mm_alloc Widget #23 size=16
mm_alloc Widget #24 size=16
mm_alloc Widget #25 size=16
mm_alloc Widget #26 size=16
mm_alloc Widget #27 size=16
mm_alloc Widget #28 size=16
mm_alloc Widget #29 size=16
mm_alloc Widget #30 size=16
mm_alloc Widget #31 size=16
mm_alloc Widget #32 size=16
mm_alloc Widget #33 size=16
mm_alloc Widget #34 size=16
mm_alloc Widget #35 size=16
mm_alloc Widget #36 size=16
mm_alloc Widget #37 size=16
mm_alloc Widget #38 size=16
mm_alloc Widget #39 size=16
mm_alloc Widget #40 size=16
mm_alloc Widget #41 size=16
mm_alloc Widget #42 size=16
mm_alloc Widget #43 size=16
mm_alloc Widget #44 size=16
mm_alloc Widget #45 size=16
mm_alloc Widget #46 size=16
mm_alloc Widget #47 size=16
mm_alloc Widget #48 size=16
mm_alloc Widget #49 size=16
mm_alloc Widget #50 size=16
mm_alloc Widget #51 size=16
mm_alloc Widget #52 size=16
mm_alloc Widget #53 size=16
mm_alloc Widget #54 size=16
mm_alloc Widget #55 size=16
mm_alloc Widget #56 size=16
mm_alloc Widget #57 size=16
mm_alloc Widget #58 size=16
mm_alloc Widget #59 size=16
mm_alloc Widget #60 size=16
mm_alloc Widget #61 size=16
mm_alloc Widget #62 size=16
mm_alloc Widget #63 size=16
mm_alloc Widget #64 size=16
mm_alloc Widget #65 size=16
mm_alloc Widget #66 size=16
mm_alloc Widget #67 size=16
mm_alloc Widget #68 size=16
mm_alloc ArrayRecord #69 size=48
mm_decref ArrayRecord #1 rc=0
mm_decref Widget #5 rc=0
mm_free Widget #5
mm_decref Widget #6 rc=0
mm_free Widget #6
mm_decref Widget #7 rc=0
mm_free Widget #7
mm_decref Widget #8 rc=0
mm_free Widget #8
mm_decref Widget #9 rc=0
mm_free Widget #9
mm_decref Widget #10 rc=0
mm_free Widget #10
mm_decref Widget #11 rc=0
mm_free Widget #11
mm_decref Widget #12 rc=0
mm_free Widget #12
mm_decref Widget #13 rc=0
mm_free Widget #13
mm_decref Widget #14 rc=0
mm_free Widget #14
mm_decref Widget #15 rc=0
mm_free Widget #15
mm_decref Widget #16 rc=0
mm_free Widget #16
mm_decref Widget #17 rc=0
mm_free Widget #17
mm_decref Widget #18 rc=0
mm_free Widget #18
mm_decref Widget #19 rc=0
mm_free Widget #19
mm_decref Widget #20 rc=0
mm_free Widget #20
mm_decref Widget #21 rc=0
mm_free Widget #21
mm_decref Widget #22 rc=0
mm_free Widget #22
mm_decref Widget #23 rc=0
mm_free Widget #23
mm_decref Widget #24 rc=0
mm_free Widget #24
mm_decref Widget #25 rc=0
mm_free Widget #25
mm_decref Widget #26 rc=0
mm_free Widget #26
mm_decref Widget #27 rc=0
mm_free Widget #27
mm_decref Widget #28 rc=0
mm_free Widget #28
mm_decref Widget #29 rc=0
mm_free Widget #29
mm_decref Widget #30 rc=0
mm_free Widget #30
mm_decref Widget #31 rc=0
mm_free Widget #31
mm_decref Widget #32 rc=0
mm_free Widget #32
mm_decref Widget #33 rc=0
mm_free Widget #33
mm_decref Widget #34 rc=0
mm_free Widget #34
mm_decref Widget #35 rc=0
mm_free Widget #35
mm_decref Widget #36 rc=0
mm_free Widget #36
mm_decref Widget #37 rc=0
mm_free Widget #37
mm_decref Widget #38 rc=0
mm_free Widget #38
mm_decref Widget #39 rc=0
mm_free Widget #39
mm_decref Widget #40 rc=0
mm_free Widget #40
mm_decref Widget #41 rc=0
mm_free Widget #41
mm_decref Widget #42 rc=0
mm_free Widget #42
mm_decref Widget #43 rc=0
mm_free Widget #43
mm_decref Widget #44 rc=0
mm_free Widget #44
mm_decref Widget #45 rc=0
mm_free Widget #45
mm_decref Widget #46 rc=0
mm_free Widget #46
mm_decref Widget #47 rc=0
mm_free Widget #47
mm_decref Widget #48 rc=0
mm_free Widget #48
mm_decref Widget #49 rc=0
mm_free Widget #49
mm_decref Widget #50 rc=0
mm_free Widget #50
mm_decref Widget #51 rc=0
mm_free Widget #51
mm_decref Widget #52 rc=0
mm_free Widget #52
mm_decref Widget #53 rc=0
mm_free Widget #53
mm_decref Widget #54 rc=0
mm_free Widget #54
mm_decref Widget #55 rc=0
mm_free Widget #55
mm_decref Widget #56 rc=0
mm_free Widget #56
mm_decref Widget #57 rc=0
mm_free Widget #57
mm_decref Widget #58 rc=0
mm_free Widget #58
mm_decref Widget #59 rc=0
mm_free Widget #59
mm_decref Widget #60 rc=0
mm_free Widget #60
mm_decref Widget #61 rc=0
mm_free Widget #61
mm_decref Widget #62 rc=0
mm_free Widget #62
mm_decref Widget #63 rc=0
mm_free Widget #63
mm_decref Widget #64 rc=0
mm_free Widget #64
mm_decref Widget #65 rc=0
mm_free Widget #65
mm_decref Widget #66 rc=0
mm_free Widget #66
mm_decref Widget #67 rc=0
mm_free Widget #67
mm_decref Widget #68 rc=0
mm_free Widget #68
mm_decref ElementBuffer #2 rc=0
mm_free ElementBuffer #2
mm_free ArrayRecord #1
mm_decref ArrayRecord #3 rc=0
mm_decref ElementBuffer #4 rc=0
mm_free ElementBuffer #4
mm_free ArrayRecord #3
mm_decref ArrayRecord #69 rc=0
mm_free ArrayRecord #69
```

<!-- test: allocations-by-tag.an-untraced-build-tallies-nothing -->
RED: the pre-change tree stops at E3004 on the unknown `__Builtins.mmAllocTotalByTag` member
(`callUnknownFunction`). Afterwards this is the case that says an UNTRACED build answers 0 rather than a
figure nobody should believe. It carries no trace-capture marker, so it is compiled without
`--debugstream` and there is no tag to index by; `__Builtins.mmAllocTotal()` still moves, which is what
separates "the program allocated nothing" from "the tally is switched off".
```maxon
typealias WidgetField = int(0 to 65536)

let Population = 1000
let TallyBuckets = 2048

type Widget
	export var low as WidgetField
	export var high as WidgetField

	static function create(low WidgetField, high WidgetField) returns Widget
		return Widget{low: low, high: high}
	end 'create'
end 'Widget'

typealias Widgets = Array with Widget

function main() returns ExitCode
	var holder = Widgets.create()
	holder.reserve(Population)

	let totalBefore = __Builtins.mmAllocTotal()

	for k in 0 upto Population 'buildWidgets'
		holder.push(Widget.create((k mod 65536) as WidgetField, high: ((k + 1) mod 65536) as WidgetField))
	end 'buildWidgets'

	if __Builtins.mmAllocTotal() <= totalBefore 'theProgramAllocatedNothing'
		return 1
	end 'theProgramAllocatedNothing'

	for i in 0 upto TallyBuckets 'readCountBuckets'
		if __Builtins.mmAllocTotalByTag(i) != 0 'aCountBucketWasTallied'
			return 2
		end 'aCountBucketWasTallied'
	end 'readCountBuckets'

	for i in 0 upto TallyBuckets 'readByteBuckets'
		if __Builtins.mmAllocBytesByTag(i) != 0 'aByteBucketWasTallied'
			return 3
		end 'aByteBucketWasTallied'
	end 'readByteBuckets'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: allocations-by-tag.alloc-total-by-tag-arity-checked -->
RED: the pre-change tree answers E3004 at the unknown `__Builtins` member (`callUnknownFunction`) rather
than the arity diagnostic below. Both readers take exactly one argument — the bucket index. An intrinsic
has no signature for the ordinary arity check to read, so each is refused by the same `builtinArity` check
`awaitAny`/`readStdin` use, and each names ITSELF — which is what a copy-pasted dispatch arm carrying its
neighbour's name would fail. Front-end only and target-neutral, so no marker.
```maxon
function main() returns ExitCode
	return __Builtins.mmAllocTotalByTag() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmAllocTotalByTag' takes exactly 1 argument, but 0 were given
```

<!-- test: allocations-by-tag.alloc-bytes-by-tag-arity-checked -->
RED: the same E3004 at the unknown `__Builtins` member (`callUnknownFunction`) is what the pre-change tree
reports. The byte reader is refused the same way and naming itself.
```maxon
function main() returns ExitCode
	return __Builtins.mmAllocBytesByTag() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.mmAllocBytesByTag' takes exactly 1 argument, but 0 were given
```
