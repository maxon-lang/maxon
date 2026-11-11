---
feature: refcount-global-getter-forward
status: selfhosted
keywords: [refcount, borrow, global, cache, getter, forward, memory, leak]
category: memory-safety
---

# Refcount: Forwarding a Module-Global Cache Getter's Borrow

## Documentation

A getter that returns a **module global** — `return cachedX` — hands back a
*borrow*: the global is image data, which OUTLIVES the call. A DIRECT such read is
correctly never retained.

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
return) is here HARMFUL: retaining the global's outliving image mints a `+1`
that no one ever releases, and the leak gate reports it.

The compiler classifies such getters (`funcReturnsGlobalBorrow`, a strict subset of the
borrow set) and suppresses the forward's retain, so the whole chain stays a clean
borrow. A torn-down-local element forward keeps its retain (its source really does
die at return), so this is scoped to genuinely-outliving global sources.

This test declares two module-global array caches with a getter, forwards one through
a multi-path `build` (one arm returns the forwarded global, another a *different*
cached global), and borrows the result. Under the leak gate the forward must not
leak: the program returns the borrowed array's element count.

## Tests

<!-- test: forward-global-getter-borrow -->
Forwarding a module-global getter's borrow must not retain the outliving image.
```maxon
typealias Count = int(0 to 1000)
typealias Names = Array with String

let cachedBase = ["first base item", "second base item", "third base item"]
let cachedMerged = ["merged item"]

function loadBase() returns immutable Names
	return cachedBase
end 'loadBase'

function buildFor(wantMerged bool) returns immutable Names
	let base = loadBase()
	if not wantMerged 'native'
		return base
	end 'native'
	return cachedMerged
end 'buildFor'

function augment(wantMerged bool) returns Count
	let rt = buildFor(wantMerged)
	return rt.count()
end 'augment'

function main() returns ExitCode
	return augment(false)
end 'main'
```
```exitcode
3
```
