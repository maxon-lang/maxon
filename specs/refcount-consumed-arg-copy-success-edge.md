---
feature: refcount-consumed-arg-copy-success-edge
status: stable
keywords: [refcount, trycall, consume, copy, memoize, map, cache, memory, leak]
category: memory-safety
---

# Refcount: Copy-Retained Consumed Arg Released on a tryCall Success Edge

## Documentation

A generic container method consumes its **type-parameter** argument *by
convention*: `Map.get(key)` / `Map.insert(key, ..)` take ownership of the
`key` and drop it at last use inside the callee (the type-param
`dropTypeParam` contract). So a `key` re-used at TWO container calls must be
COPY-retained: `insertRefcounts` mints a `+1` before the first consuming call
so the callee moves the COPY while the caller keeps its ORIGINAL for the
second call.

The classic shape is a memoizing cache — build a key once, probe with `get`,
and on a miss store it with `insert`:

```text
let key = keyFor(n)                       // key: +1
if let hit = try cache.get(key) 'hit'     // get CONSUMES key (type-param) -> copy minted
    return hit                            // HIT edge: key dies here, still owns its +1
end 'hit'
try cache.insert(key, ..) otherwise ignore // MISS edge: key re-consumed into the map
return ...
```

`get` is a `tryCall`; its SUCCESS edge is the cache-**hit** block. Because the
copy was minted (the key is live past `get`, re-consumed by `insert` on the
miss edge), the caller keeps the original `+1`. On the hit edge the key dies
without reaching `insert`, so its retained `+1` must be released there. The
edge-death planner's TRYCALL-CONSUMED-ARG success-edge guard, however,
suppressed **every** consumed-arg release on the success edge — it did not
check whether a copy was minted — so the retained original leaked on every
cache hit (the `qualifyCalleeCacheKey` String / `__ManagedMemory` leak family
that dominated the self-hosted self-compile census).

The fix routes that guard through `tryCallArgConsumedReachingSuccess`, which
carries the copy-minted escape (`consumedArgRetainedPastOp`) already used by
the term-drop / into-block guard (`valueConsumedByTryCallOnSuccessEdgeInto`).
Only a PURE MOVE (no copy — a transparent-wrapper factory transferring its
sole `+1`) still suppresses; a copy-retained original now releases on the hit
edge. This is the live-out-of-block leg of ownership-audit gap P0#1; the
term-drop leg was closed by the sibling guard earlier.

## Tests

<!-- test: memoize-get-then-insert-reused-key -->
A memoizing get-then-insert reusing one key across a `tryCall get` (consume,
copy minted) and a later `insert` (re-consume) must release the retained key
on the cache-HIT success edge, or every hit leaks the key.
```maxon
typealias Count = int(0 to 1000000)
typealias MemoMap = Map with (String, Count)

function keyFor(n Count) returns String
	return "k{n}"
end 'keyFor'

// Build the key ONCE; `get` consumes it (type-param convention) so a copy is
// minted to keep the original for the miss-path `insert`. On the HIT edge the
// key dies without reaching `insert`; its retained +1 must be released there.
function memoized(cache MemoMap, n Count) returns Count
	let key = keyFor(n)
	if let hit = try cache.get(key) 'hit'
		return hit
	end 'hit'
	let computed = n + 1
	try cache.insert(key, value: computed) otherwise ignore
	return computed
end 'memoized'

function main() returns ExitCode
	var cache = MemoMap.create()
	var sum = 0
	// Round 0 misses (inserts each key); rounds 1-2 HIT (early-return path).
	// Each memoized() returns n+1 for n in 0..2, every round -> sum = 3 * 6 = 18.
	for _ in 0 upto 3 'rounds'
		for i in 0 upto 3 'keys'
			sum = sum + memoized(cache, n: i)
		end 'keys'
	end 'rounds'
	return sum
end 'main'
```
```exitcode
18
```
