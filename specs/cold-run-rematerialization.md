---
feature: cold-run-rematerialization
status: experimental
keywords: [register-allocator, cold-block, cold-call, rematerialization, spill, copy-on-write, element-store, view]
category: codegen
---
# A constant live across a cold call comes back intact

## Documentation

A store into an array whose buffer a view shares calls `__managed_cow_detach` on a cold arm. A constant
that is live across that call is rebuilt from its immediate after the call,
so the register it held holds the same word again. Every case below takes the cold arm: the array it stores
into has a live view, so the store detaches, and the stored word and the words the view kept are checked.

## Tests

<!-- test: a-64-bit-constant-survives-a-detach -->
`0x123456789ABCDEF0` needs all 64 bits of its register.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray)
	try arr.set(1, value: 0x123456789ABCDEF0) otherwise panic("set: index 1 of a three-element array")
end 'stamp'

function main() returns ExitCode
	let arr = WordArray from [10, 20, 30]
	let view = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stamp(arr)

	let stored = try arr.get(1) otherwise panic("get: index 1 of a three-element array")
	let kept = try view.get(1) otherwise panic("get: index 1 of a three-element view")

	if stored == 0x123456789ABCDEF0 and kept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: minus-one-survives-a-detach -->
`-1` is all ones: a zero-extending 32-bit move would give `0xFFFFFFFF`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray)
	try arr.set(1, value: -1) otherwise panic("set: index 1 of a three-element array")
end 'stamp'

function main() returns ExitCode
	let arr = WordArray from [10, 20, 30]
	let view = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stamp(arr)

	let stored = try arr.get(1) otherwise panic("get: index 1 of a three-element array")
	let kept = try view.get(1) otherwise panic("get: index 1 of a three-element view")

	if stored == -1 and kept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: the-smallest-signed-word-survives-a-detach -->
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray)
	try arr.set(1, value: i64.min) otherwise panic("set: index 1 of a three-element array")
end 'stamp'

function main() returns ExitCode
	let arr = WordArray from [10, 20, 30]
	let view = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stamp(arr)

	let stored = try arr.get(1) otherwise panic("get: index 1 of a three-element array")
	let kept = try view.get(1) otherwise panic("get: index 1 of a three-element view")

	if stored == i64.min and kept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: the-widest-32-bit-constant-and-the-next-one-survive-a-detach -->
`u32.max` is the widest word a 32-bit move zero-extends; `u32.max + 1` is the first that needs the full
64-bit move.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stampWidest(arr WordArray)
	try arr.set(1, value: u32.max) otherwise panic("set: index 1 of a three-element array")
end 'stampWidest'

function stampNext(arr WordArray)
	try arr.set(1, value: u32.max + 1) otherwise panic("set: index 1 of a three-element array")
end 'stampNext'

function main() returns ExitCode
	let widest = WordArray from [10, 20, 30]
	let widestView = try widest.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stampWidest(widest)

	let next = WordArray from [10, 20, 30]
	let nextView = try next.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	stampNext(next)

	let widestStored = try widest.get(1) otherwise panic("get: index 1 of a three-element array")
	let widestKept = try widestView.get(1) otherwise panic("get: index 1 of a three-element view")
	let nextStored = try next.get(1) otherwise panic("get: index 1 of a three-element array")
	let nextKept = try nextView.get(1) otherwise panic("get: index 1 of a three-element view")

	if widestStored == 4294967295 and nextStored == 4294967296 and widestKept == 20 and nextKept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: a-constant-and-a-variable-survive-the-same-detach -->
The index and the returned tag are variables and the stored word is a constant, all live across one call:
two registers are saved and reloaded and one is rebuilt.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray, at Count, tag Word) returns Word
	try arr.set(at, value: 0x123456789ABCDEF0) otherwise panic("set: an index inside a three-element array")
	return tag + 1
end 'stamp'

function main() returns ExitCode
	let arr = WordArray from [10, 20, 30]
	let view = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	let answer = stamp(arr, at: 2, tag: 41)

	let stored = try arr.get(2) otherwise panic("get: index 2 of a three-element array")
	let kept = try view.get(2) otherwise panic("get: index 2 of a three-element view")

	if answer == 42 and stored == 0x123456789ABCDEF0 and kept == 30 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: successive-detaches-store-a-constant-then-a-variable-then-a-constant -->
A view is taken before each store, so each store detaches. The register that holds the constant at the
first call holds a variable at the second and a different constant at the third; each view keeps the
words that were there when it was taken.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray, seed Word) returns bool
	let first = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	try arr.set(0, value: 0x123456789ABCDEF0) otherwise panic("set: index 0 of a three-element array")
	let second = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	try arr.set(1, value: seed * 3) otherwise panic("set: index 1 of a three-element array")
	let third = try arr.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	try arr.set(2, value: -1) otherwise panic("set: index 2 of a three-element array")

	let first0 = try first.get(0) otherwise panic("get: index 0 of a three-element view")
	let first1 = try first.get(1) otherwise panic("get: index 1 of a three-element view")
	let second0 = try second.get(0) otherwise panic("get: index 0 of a three-element view")
	let second1 = try second.get(1) otherwise panic("get: index 1 of a three-element view")
	let third1 = try third.get(1) otherwise panic("get: index 1 of a three-element view")
	let third2 = try third.get(2) otherwise panic("get: index 2 of a three-element view")

	return first0 == 10 and first1 == 20 and second0 == 0x123456789ABCDEF0 and second1 == 20 and third1 == seed * 3 and third2 == 30
end 'stamp'

function main() returns ExitCode
	let arr = WordArray from [10, 20, 30]

	if not stamp(arr, seed: 7) 'views'
		return 1
	end 'views'

	let zero = try arr.get(0) otherwise panic("get: index 0 of a three-element array")
	let one = try arr.get(1) otherwise panic("get: index 1 of a three-element array")
	let two = try arr.get(2) otherwise panic("get: index 2 of a three-element array")

	if zero == 0x123456789ABCDEF0 and one == 21 and two == -1 'stored'
		return 0
	end 'stored'

	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-constant-passed-on-an-edge-survives-a-detach -->
The constant is live across the detach and is then passed on an edge into the phi that `answer` becomes.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function stamp(arr WordArray, flag Word) returns Word
	let wide = 0x123456789ABCDEF0 as Word
	try arr.set(1, value: wide) otherwise panic("set: index 1 of a three-element array")
	var answer = 5 as Word

	if flag > 0 'taken'
		answer = wide
	end 'taken'

	return answer
end 'stamp'

function main() returns ExitCode
	let taken = WordArray from [10, 20, 30]
	let takenView = try taken.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	let viaEdge = stamp(taken, flag: 1)

	let skipped = WordArray from [10, 20, 30]
	let skippedView = try skipped.slice(0, endIndex: 3) otherwise panic("slice: 0..3 of a three-element array")
	let direct = stamp(skipped, flag: 0)

	let takenStored = try taken.get(1) otherwise panic("get: index 1 of a three-element array")
	let skippedStored = try skipped.get(1) otherwise panic("get: index 1 of a three-element array")
	let takenKept = try takenView.get(1) otherwise panic("get: index 1 of a three-element view")
	let skippedKept = try skippedView.get(1) otherwise panic("get: index 1 of a three-element view")

	if viaEdge == 0x123456789ABCDEF0 and direct == 5 and takenStored == 0x123456789ABCDEF0 and skippedStored == 0x123456789ABCDEF0 and takenKept == 20 and skippedKept == 20 'stored'
		return 0
	end 'stored'

	return 1
end 'main'
```
```exitcode
0
```
