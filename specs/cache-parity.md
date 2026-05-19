---
feature: cache-parity
status: stable
keywords: cache, parity, regression
category: testing
---
# Stdlib Cache Parity

<!-- CacheParity -->

## Documentation

Every test in this file is compiled twice — once with the stdlib cache forced
off, once with it on — and the test fails if the captured IR/lowering trace
or the compiled PE's `.rdata` section differs between legs.

The point is to expose **cache-masked bugs**: bugs whose symptom only appears
when the compiler re-derives state from source rather than replaying it from
the cache. Each test below exercises a specific bug pattern that the deletion
of the stdlib cache on `main` surfaced and then fixed. On `tryold` (this
branch) the cache is still present, so the bugs are still here and these
tests should fail until the corresponding K.X fix is ported.

## Tests

<!-- test: parity-harness-smoke -->
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: rdata-prune-orphans -->
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: slot-args-scratch-zeroed -->
```maxon
function main() returns ExitCode
	let a = [1, 2, 3, 4, 5]
	let b = [99]
	let first = try a.get(0) otherwise 0
	let extra = try b.get(0) otherwise 0
	return first + extra - 99
end 'main'
```
```exitcode
1
```

<!-- test: slot-collisions-cross-function -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makeRange() returns Integer
	var total = 0
	for i in 0 upto 3 'loop'
		total = total + i
	end 'loop'
	return total
end 'makeRange'

function makeTuple() returns Integer
	let t = (10, 20, 30)
	return t.0 + t.1 + t.2
end 'makeTuple'

function main() returns ExitCode
	return makeRange() + makeTuple() - 63
end 'main'
```
```exitcode
0
```
