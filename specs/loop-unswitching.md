---
feature: loop-unswitching
status: experimental
keywords: [optimizer, codegen, loop, unswitching, versioning, invariant, alias, managed-memory, licm]
category: codegen
---
# A guard the loop asks once

## Documentation

`unswitchInvariantGuards` is a Std-tier pass that runs after `loopInvariantCodeMotion`. A natural
loop whose conditional branches test values that do not change inside the loop is VERSIONED: the
preheader evaluates those conditions once, a fast copy of the loop has every such branch folded to the
arm on which the refusal is false (the arms that can no longer be reached are dropped from the copy), and the
original loop stays as the slow version for the entries where a condition is false. The loads those
conditions and the copy's body read are hoisted out of the copy, so its body is call-free but for the
one arm a bound failure reaches.

### The shape it exists for

`InlineManagedPrimitives` puts three SHAPE guards in front of every inlined `set` — the buffer is
this record's (`capacity@16 ≥ 0`), it exists (`buffer@0 ≠ 0`), and nobody is viewing it (the
buffer's refcount word is 0) — and reloads `length@8` and `buffer@0` for every access. Inside a loop
none of those facts can change: an element store writes a buffer, which is its own allocation and not
a record; `__managed_get` writes nothing; and `__managed_set` writes the header only through a
copy-on-write detach whose precondition is exactly that one of the three guards FAILED. So a loop
that enters with the three guards true keeps them true — the detach the guards protect against can
never fire — and every header load in the loop is invariant. The pass states the two facts it rests on
where the runtime owns them (`elementStoreCannotAliasARecordField`, `managedCalleeHeaderEffect`) and
re-derives nothing.

### What the pass refuses, and why

- **A call whose effect on a record the runtime does not describe** — `push`, `resize`, `slice`,
  `clone`, `retain`, any user call. Such a loop is left alone (`a-loop-that-pushes-stays-unversioned`).
- **A store that is not an element store**, or a loop-defined value read past the loop in a block no
  single exit dominates (a `break 'outer'` that carries a value out of two loops at once), or a
  loop-defined OP RESULT — a hoisted load, a computed value — read after the loop in either mode.
  The copy would need SSA the pass does not rebuild in this version; a loop whose exits carry nothing
  out may have several.
- **A loop with no invariant guard.** Nothing to version.

The slow version is not a fallback for a rare shape: a loop over a slice VIEW enters it, and the
view's first write detaches in the runtime exactly as before (`a-view-detaches-on-its-first-write`).

⚠ A green case here proves nothing on its own — the fast and slow versions compute the same thing.
The evidence is the committed fragment of the first two cases (the guards and the header loads before
the loop, a body of bound check + store and bound check + load) and the CONTROLS below, which put
every path the rewrite touches under a value the program reads back: the slow version's detach, an
out-of-bounds fallback taken inside the fast version, a loop-defined value read after the loop, a
value carried out through two loops at once, a hoisted load read after its loop, two names for one
record. Measured under sabotage — `__managed_push` declared to write nothing — the push control reads
slot 0 through a buffer the push has freed and exits 1, and `a-value-carried-out-of-two-loops` is
versioned around its pushes and answers wrong; the compiler built by that sabotaged compiler then
miscompiles itself, which is the class of defect the callee table exists to refuse.

## Tests

<!-- test: a-store-loop-carries-its-shape-guards-outside -->
The shape the pass was opened for. In `fill`'s fragment the ownership, buffer and sharing tests and
the `length@8`/`buffer@0` loads sit before the loop header; the body is the unsigned bound check and
the store.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function fill(a WordArray, n Word)
	for i in 0 upto n 'each'
		try a.set(i, value: i * 3 + 1) otherwise panic("fill: i < n = a.count()")
	end 'each'
end 'fill'

function main() returns ExitCode
	var a = WordArray.create()
	a.resize(5)
	fill(a, n: 5)
	var seen = 0
	for v in a 'check'
		seen = seen + v
	end 'check'
	if seen != 35 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-read-loop-hoists-its-header-loads -->
The read mirror. `total`'s loop has no guard to version but its `__managed_get` slow arm is a call,
which kept plain LICM out; here the length and the buffer are read once before the loop and the body
is the bound check and the element load.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'sum'
		t = t + (try a.get(i) otherwise panic("total: i < n = a.count()"))
	end 'sum'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a, n: 6) != 55 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-view-detaches-on-its-first-write -->
Control for the slow version. `b` is a slice of `a`, sharing its buffer, so `b`'s sharing guard is
false on entry and the loop runs the slow version: the first write detaches, `b` gets its own buffer,
and `a` is untouched. A fast version entered here would write through the sharing.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function overwrite(b WordArray, n Word)
	for i in 0 upto n 'each'
		try b.set(i, value: 100 + i) otherwise panic("overwrite: i < n = b.count()")
	end 'each'
end 'overwrite'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 4 'seed'
		a.push(i)
	end 'seed'
	var b = try a.slice(0, endIndex: 4) otherwise panic("main: 0..4 is within a")
	overwrite(b, n: 4)
	var sumA = 0
	for v in a 'readA'
		sumA = sumA + v
	end 'readA'
	var sumB = 0
	for v in b 'readB'
		sumB = sumB + v
	end 'readB'
	if sumA != 6 'aUntouched'
		return 1
	end 'aUntouched'
	if sumB != 406 'bRewritten'
		return 2
	end 'bRewritten'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-that-pushes-stays-unversioned -->
Control for the callee table. `push` may grow the buffer, so its effect is `unknown` and the loop
is left alone; the fragment holds one copy of the loop and no `__us_test` block. The array holds an
element on entry and every iteration reads slot 0 after its push: were the loop hoisted, the hoisted
buffer would be the one a growing `push` frees, and the read would follow a stale pointer.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function grow(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		a.push(i)
		t = t + (try a.get(0) otherwise panic("grow: a holds an element on entry"))
	end 'each'
	return t
end 'grow'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(7)
	if grow(a, n: 40) != 280 'sum'
		return 1
	end 'sum'
	if a.count() != 41 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-out-of-bounds-fallback-keeps-the-fast-loop-going -->
Control for the one arm the fast version keeps. Indices past the end reach the slow arm through the
bound check; `__managed_get` and `__managed_set` write nothing on that path, the fallbacks are taken,
and the loop continues with its hoisted length and buffer still true.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function walk(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise 99)
		try a.set(i, value: i * 2) otherwise ignore
	end 'each'
	return t
end 'walk'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 5 'seed'
		a.push(i)
	end 'seed'
	if walk(a, n: 9) != 406 'sum'
		return 1
	end 'sum'
	var seen = 0
	for v in a 'check'
		seen = seen + v
	end 'check'
	if seen != 20 'rewritten'
		return 2
	end 'rewritten'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-defined-value-is-still-live-after-the-loop -->
Control for the exit phi. `last` is assigned inside the loop and read after it, so both versions must
hand it to the exit block through one block argument.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function lastDoubled(a WordArray, n Word) returns Word
	var last = -1
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("lastDoubled: i < n = a.count()")
		try a.set(i, value: v * 2) otherwise panic("lastDoubled: i < n = a.count()")
		last = v * 2
	end 'each'
	return last
end 'lastDoubled'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 4 'seed'
		a.push(i + 1)
	end 'seed'
	if lastDoubled(a, n: 4) != 8 'last'
		return 1
	end 'last'
	if lastDoubled(a, n: 0) != -1 'untouched'
		return 2
	end 'untouched'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-value-carried-out-of-two-loops-is-left-alone -->
Control for the exit rule. The outer loop `push`es, so it is refused; the inner loop `set`s and is a
candidate — but `found` is assigned inside it and read only after BOTH loops, past a `break 'outer'`
that leaves the inner loop through the outer's exit, so no single exit of the inner loop dominates
its reader and the inner loop is refused too. Behaviour is pinned; the fragment holds one copy.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function firstBigRow(rows WordArray, width Word, limit Word) returns Word
	var found = -1
	var r = 0
	while r < limit 'outer'
		rows.push(r * 10)
		for c in 0 upto width 'inner'
			let v = try rows.get(c) otherwise panic("firstBigRow: c < width <= rows.count()")
			try rows.set(c, value: v + 1) otherwise panic("firstBigRow: c < width <= rows.count()")
			if v > 25 'hit'
				found = r * 100 + c
				break 'outer'
			end 'hit'
		end 'inner'
		r = r + 1
	end 'outer'
	return found
end 'firstBigRow'

function main() returns ExitCode
	var rows = WordArray.create()
	for _ in 0 upto 3 'seed'
		rows.push(20)
	end 'seed'
	if firstBigRow(rows, width: 3, limit: 10) != 600 'row'
		return 1
	end 'row'
	if rows.count() != 10 'grown'
		return 2
	end 'grown'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-hoisted-load-read-after-the-loop-is-refused -->
Control for the hoist-only mode. `last` is `a.count()`, an inlined header load the pass would hoist
in place, and it is read after the loop: `elimTrivialBlockArgs` has already folded the one-edge exit
phi onto the load itself, so the reader outside the loop names the loop's own op. Hoisting it would
leave that reader with a value nothing defines on its path; the loop is refused and the program
answers 506. This program used to panic the register allocator.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function tally(a WordArray) returns Word
	var t = 0
	var last = 0
	var i = 0
	while true 'l'
		t = t + (try a.get(i) otherwise 0)
		last = a.count()
		if i >= 2 'stop'
			break
		end 'stop'
		i = i + 1
	end 'l'
	return last * 100 + t
end 'tally'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	a.push(4)
	a.push(5)
	if tally(a) != 506 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: two-names-for-one-record -->
Control for aliasing. `a` and `b` are one record, not a view: a hoisted length or buffer read through
one name must be the record's, so writes through `a` are read back through `b`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function crossWrite(a WordArray, b WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		try a.set(i, value: i + 10) otherwise panic("crossWrite: i < n")
		t = t + (try b.get(i) otherwise panic("crossWrite: i < n"))
	end 'each'
	return t
end 'crossWrite'

function main() returns ExitCode
	var a = WordArray.create()
	a.resize(4)
	if crossWrite(a, b: a, n: 4) != 46 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```
