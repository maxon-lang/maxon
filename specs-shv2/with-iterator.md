---
feature: withIterator
status: stable
keywords: [withIterator, iterator, for-in, advance, current, index, retreat]
category: stdlib
---

# withIterator and iterator navigation

## Documentation

### Overview

`.withIterator()` wraps any `Iterable` and yields `(Iterator, Element)` tuples in a for-loop. The `iter` binding exposes navigation methods on the underlying iterator — `index()`, `advance()`, `retreat()`, `peek()` — so the loop body can introspect or steer iteration.

### Usage

```text
let arr = ["a", "b", "c"]
for (iter, item) in arr.withIterator() 'loop'
    print("{iter.index()}: {item}\n")
end 'loop'
// 0: a
// 1: b
// 2: c
```

### Protocol

The `Iterator` interface is `current()` (infallible) + `advance()` (throws `IterationError.exhausted` at end). A live iterator always points at a valid element; the constructor throws on empty collections. Manual navigation (`iter.advance()`, `iter.retreat()`) inside the body composes with the loop's own advance — the next iteration's header then advances on top of that, so one extra `advance()` in the body skips one element, and a `retreat()` re-visits the current element.

Iterators are first-class values. Pass a half-consumed iterator into a function and iterate with `for x in iter`: the loop resumes from wherever the iterator is positioned.

## Tests

<!-- disabled-test: withIterator.basic -->
<!-- BLOCKED: `withIterator()` needs a tuple over generic type PARAMETERS. `stdlib/helpers/itertools/withIterator.maxon:12` declares `returns (Source, Element)` inside `type WithIterIterator uses Source, Element`, which shv2 refuses (`E2015: a tuple whose element 0 is .type parameter.: a tuple.s identity IS its element types`) — so the module cannot be listed and `Iterable.withIterator` has no result type. It ALSO needs `Array.createIterator()`, which is not on shv2.s `Array` roster (board row `S2l`). -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	for (iter, value) in arr.withIterator() 'loop'
		print("{iter.index()}:{value}\n")
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:20
2:30
```

<!-- disabled-test: withIterator.bytes -->
<!-- BLOCKED: `String.bytes()` yields the `ByteView` of `stdlib/helpers/string/views.maxon`, which the stdlib loader does not list — it stops at `Iterable.withIterator` for the tuple reason above. -->
Iterate over string bytes via the ByteView iterable.
```maxon
function main() returns ExitCode
	let s = "abc"
	var sum = 0
	for b in s.bytes() 'loop'
		sum = sum + b
	end 'loop'
	print("{sum}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
294
```

<!-- test: withIterator.empty-array -->
Empty array yields zero iterations, no error.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
function main() returns ExitCode
	let arr = IntArray.create()
	var total = 0
	for v in arr 'loop'
		total = total + v
	end 'loop'
	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0
```

<!-- disabled-test: withIterator.body-advance-skips-element -->
<!-- BLOCKED: `withIterator()` — same tuple-over-type-parameter blocker as `withIterator.basic`. -->
Calling `iter.advance()` inside the for-loop body skips the next element. Body runs on 10, advances to 20, header advances again to 30; so 20 is skipped.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40]
	for (iter, value) in arr.withIterator() 'loop'
		print("{value}\n")
		try iter.advance() otherwise break
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
10
30
```

<!-- disabled-test: withIterator.body-retreat-revisits-element -->
<!-- BLOCKED: `withIterator()` — same tuple-over-type-parameter blocker as `withIterator.basic`. -->
Calling `iter.retreat()` inside the body causes the next iteration to re-visit the current element. Without the guard, this would loop forever — we stop after a fixed count.
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30]
	var visits = 0
	var retreated = false
	for (iter, value) in arr.withIterator() 'loop'
		visits = visits + 1
		if visits >= 5 'stop'
			break
		end 'stop'
		print("{iter.index()}:{value}\n")
		if iter.index() == 1 and not retreated 'once'
			retreated = true
			try iter.retreat() otherwise break
		end 'once'
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0:10
1:20
1:20
2:30
```

<!-- disabled-test: withIterator.iterator-as-parameter -->
<!-- BLOCKED: `ArrayIterator` and `Array.createIterator()` — shv2 iterates an `Array` with an index counter and mints no cursor object at all (board row `S2l`, whose own STOPPED note records the measurement). -->
An iterator passed to a helper function is still iterable from its current position. The helper consumes the remaining elements.
```maxon
typealias TokenIter = ArrayIterator with String

function printRest(iter TokenIter)
	for t in iter 'rest'
		print("{t}\n")
	end 'rest'
end 'printRest'

function main() returns ExitCode
	let toks = ["a", "b", "c", "d"]
	var iter = try toks.createIterator() otherwise return 9
	try iter.advance() otherwise return 9
	try iter.advance() otherwise return 9
	printRest(iter)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
c
d
```
