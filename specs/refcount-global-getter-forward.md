---
feature: refcount-global-getter-forward
status: selfhosted
keywords: [refcount, borrow, global, cache, getter, forward, memory, leak]
category: memory-safety
---

# Refcount: Forwarding a Module-Global Cache Getter's Borrow

## Documentation

A getter that returns a **module global** — `return cachedX` — hands back a
*borrow*: the `.data` slot owns the occupant, which OUTLIVES the call (until the
process-exit global-cleanup decref). A DIRECT such read is correctly never
retained.

The gap this test guards is the **forward across a call boundary**. When one
function returns the *result* of a global getter —

```text
function build() returns T
	let base = loadCache()   // loadCache: `return cachedX`, a global borrow
	return base              // FORWARD the borrow
end
```

— `build` is itself borrow-returning, so its caller also borrows the result and
never releases it. The returned-borrow retain that a **torn-down-local** element
forward needs (`optionValue`'s `return parts.get(1)`, whose local `parts` dies at
return) is here HARMFUL: retaining the global's outliving occupant mints a `+1`
that no one ever releases. On a cached global that single leaked reference then
survives the exit-time global-cleanup decref, stranding the whole object (and, for
a cached IR module, its entire op/block working set) — the runtime-module leak the
self-hosted compiler carried on every compile.

The fix classifies such getters (`funcReturnsGlobalBorrow`, a strict subset of the
borrow set) and suppresses the forward's retain, so the whole chain stays a clean
borrow. A torn-down-local element forward keeps its retain (its source really does
die at return), so this is scoped to genuinely-outliving global sources.

This test builds a module-global container cache with a getter, forwards it through
a multi-path `build` (one arm returns the forwarded global, another a *different*
cached global — the exact `buildRuntimeModuleForTarget` shape), and borrows the
result. Under the leak gate the forward must not leak: the program returns the
borrowed container's element count.

## Tests

<!-- test: forward-global-getter-borrow -->
Forwarding a module-global getter's borrow must not retain the outliving occupant.
```maxon
typealias Count = int(0 to 1000)

type Item
	export var v as String

	export static function create(s String) returns Item
		return Self{v: s}
	end 'create'
end 'Item'

export type Box uses Elem
	typealias ElemArray = Array with Elem

	export var items as ElemArray = ElemArray.create()

	export static function create() returns Self
		return Self{}
	end 'create'

	export function add(e Elem)
		items.push(e)
	end 'add'

	export function size() returns Count
		return items.count()
	end 'size'
end 'Box'

typealias ItemBox = Box with Item

var cachedBase = ItemBox.create()
var baseLoaded = false
var cachedMerged = ItemBox.create()
var mergedDone = false

// Mirrors loadRuntimeModule: returns a BORROW of the cachedBase global.
function loadBase() returns ItemBox
	if baseLoaded 'hit'
		return cachedBase
	end 'hit'
	var b = ItemBox.create()
	b.add(Item.create("first base item"))
	b.add(Item.create("second base item"))
	b.add(Item.create("third base item"))
	cachedBase = b
	baseLoaded = true
	return cachedBase
end 'loadBase'

// Mirrors buildRuntimeModuleForTarget: `let base = loadBase()` then a multi-path
// return — the native arm forwards `base`, the merged arm returns a DIFFERENT global.
function buildFor(wantMerged bool) returns ItemBox
	let base = loadBase()
	if not wantMerged 'native'
		return base
	end 'native'
	if mergedDone 'cached'
		return cachedMerged
	end 'cached'
	var merged = ItemBox.create()
	merged.add(Item.create("merged item"))
	cachedMerged = merged
	mergedDone = true
	return cachedMerged
end 'buildFor'

// Mirrors augmentWithRuntime: borrow the result, read it, never release.
function augment(wantMerged bool) returns Count
	let rt = buildFor(wantMerged)
	return rt.size()
end 'augment'

function main() returns ExitCode
	// native arm forwards the cachedBase global (3 items)
	return augment(false)
end 'main'
```
```exitcode
3
```
