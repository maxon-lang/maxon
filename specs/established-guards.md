---
feature: established-guards
status: experimental
keywords: [optimizer, codegen, guard, managed-memory, dataflow, bounds, copy-on-write, unswitching]
category: codegen
---
# A guard asked once per record, not once per access

## Documentation

`foldEstablishedGuards` is a Std-tier pass that runs after `unswitchInvariantGuards` and before the
second value-range run. It folds a guard `InlineManagedPrimitives` put before an element access when an
earlier guard chain, or an earlier accessor's success, has already proved the predicate for that record
and nothing between the two can have changed the answer.

### The facts, and where they come from

Per managed record value the pass tracks the four SHAPE predicates a `set` asks — the buffer is this
record's (`capacity@16 >= 0`), it exists (`buffer@0 != 0`), nobody views it (the buffer's refcount word is
0), the element owes no destructor (`element_destroy@40 == 0`) — and a LENGTH FLOOR `length@8 > K`.

- The proceed arm of a shape guard establishes its predicate; the proceed arm of a constant-index bound
  guard `length@8 <=u K` establishes the floor `K`.
- The success edge of `__managed_set` establishes the first three shape predicates: the runtime states
  (`managedCalleeSuccessProvesShape`) that its copy-on-write detach runs exactly when one of them fails,
  that it detaches after its bound, and that its error path writes nothing. Success never proves the
  destructor predicate — a managed element's `set` succeeds through its destructor — so that fact comes
  from its guard alone.
- The success of `__managed_get` or `__managed_set` at a constant index `K >= 0` establishes the floor
  `K`: both return without an error only when the index is below the length.

A fact survives a `writesNothing` callee, a `detachesUnlessOwnedUnshared` callee (a detach can only make
a record owned and unshared, never the reverse) and an element store — a constant slot, or a variable
one whose bound guard dominates it, so it lands inside the buffer. Every other call and every other
store clears every fact of every record: a `clone` or a `slice` shares the buffer through a name the pass
may not be tracking, a user call may do either, and a store the pass cannot place may reach a header.

The facts flow forward over the function's CFG to a fixpoint, and a join keeps only what holds on every
edge in — so a `set` under one arm of an `if` proves nothing below the join, and a loop whose slow
version may run zero iterations proves nothing after it.

### What folds

A `condBranch` on a shape guard whose predicate is established for its record, or on a constant-index
length bound whose floor is already at or above the index, is decided against the refusal: the compare
becomes `const 0`, the branch takes the proceed arm, the arm it dropped goes with every block only it
reached, and the loads and arithmetic that fed the compare and now feed nothing are retired.

⚠ A green case here proves nothing on its own — a folded guard and an asked guard compute the same thing
whenever the guard would have passed. The evidence is the committed fragment of the first two cases and
the CONTROLS below, each of which puts a fact the rule must NOT hold under a value the program reads back.

## Tests

<!-- test: a-second-store-on-a-record-asks-no-shape-guard -->
The shape the pass was opened for. `swapFirstTwo`'s fragment: the first read proves `length > 1`, so the
read at 0 and both writes carry no bound check; the first write asks the three shape guards, so the second
write is the store alone.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function swapFirstTwo(a WordArray)
	let second = try a.get(1) otherwise panic("swapFirstTwo: a holds two elements")
	let first = try a.get(0) otherwise panic("swapFirstTwo: a holds two elements")
	try a.set(1, value: first) otherwise panic("swapFirstTwo: a holds two elements")
	try a.set(0, value: second) otherwise panic("swapFirstTwo: a holds two elements")
end 'swapFirstTwo'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(3)
	a.push(5)
	a.push(7)
	swapFirstTwo(a)
	if (try a.get(0) otherwise 0) != 5 or (try a.get(1) otherwise 0) != 3 or (try a.get(2) otherwise 0) != 7 'swapped'
		return 1
	end 'swapped'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-after-a-store-on-its-record-enters-without-tests -->
The loop shape. The write at 1 proves `count`'s three shape predicates before the loop, so every test
in the chain `unswitchInvariantGuards` put before it folds and the slow version loses its only way in:
`carry`'s fragment holds one copy of the loop, and the chain's blocks hold only the hoisted length and
buffer loads.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function carry(count WordArray, n Word) returns Word
	try count.set(1, value: 0) otherwise panic("carry: n >= 2")
	var carried = 0
	for i in 1 upto n 'each'
		let digit = try count.get(i) otherwise panic("carry: i < n = count.count()")
		carried = carried + digit + 1
		try count.set(i, value: digit + 1) otherwise panic("carry: i < n = count.count()")
	end 'each'
	return carried
end 'carry'

function main() returns ExitCode
	var count = WordArray.create()
	count.resize(6)
	if carry(count, n: 6) != 5 'firstPass'
		return 1
	end 'firstPass'
	if carry(count, n: 6) != 9 'secondPass'
		return 2
	end 'secondPass'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-clone-between-two-stores-forces-the-second-to-detach -->
Control for the callee rule. `clone` shares the buffer, so the second write must detach: `b` keeps the
first value. A rule that carried the first write's facts across the clone would write through the share.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	try a.set(0, value: 10) otherwise panic("main: a holds two elements")
	let b = a.clone()
	try a.set(0, value: 20) otherwise panic("main: a holds two elements")
	if (try b.get(0) otherwise 0) != 10 'bKeepsTheFirstWrite'
		return 1
	end 'bKeepsTheFirstWrite'
	if (try a.get(0) otherwise 0) != 20 'aTakesTheSecond'
		return 2
	end 'aTakesTheSecond'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-slice-between-two-stores-forces-the-second-to-detach -->
Control for the callee rule, through a view: a slice of `a` reads `a`'s buffer, so `a`'s next write
must detach and the view keeps what it saw.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	try a.set(1, value: 10) otherwise panic("main: a holds three elements")
	let view = try a.slice(0, endIndex: 2) otherwise panic("main: 0..2 is within a")
	try a.set(1, value: 20) otherwise panic("main: a holds three elements")
	if (try view.get(1) otherwise 0) != 10 'viewKeepsTheFirstWrite'
		return 1
	end 'viewKeepsTheFirstWrite'
	if (try a.get(1) otherwise 0) != 20 'aTakesTheSecond'
		return 2
	end 'aTakesTheSecond'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unknown-call-between-two-stores-clears-the-facts -->
Control for the callee rule's default. `share` is a user function the runtime describes nothing about;
here it clones, so the write after it must detach.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function share(a WordArray) returns WordArray
	return a.clone()
end 'share'

function rewrite(a WordArray) returns WordArray
	try a.set(0, value: 10) otherwise panic("rewrite: a holds an element")
	let kept = share(a)
	try a.set(0, value: 20) otherwise panic("rewrite: a holds an element")
	return kept
end 'rewrite'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	let kept = rewrite(a)
	if (try kept.get(0) otherwise 0) != 10 'keptHoldsTheFirstWrite'
		return 1
	end 'keptHoldsTheFirstWrite'
	if (try a.get(0) otherwise 0) != 20 'aTakesTheSecond'
		return 2
	end 'aTakesTheSecond'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-failed-store-establishes-nothing-on-a-shared-record -->
Control for the success edge. `a` shares its buffer with `b`; the out-of-range write fails before any
detach, so the write after it still has to ask, and detaches. A rule that read the failed write's return
as proof would write through the share.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	let b = a.clone()
	try a.set(99, value: 5) otherwise ignore
	try a.set(0, value: 9) otherwise panic("main: a holds two elements")
	if (try b.get(0) otherwise 0) != 1 'bUntouched'
		return 1
	end 'bUntouched'
	if (try a.get(0) otherwise 0) != 9 'aRewritten'
		return 2
	end 'aRewritten'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-store-on-one-path-establishes-nothing-at-the-join -->
Control for the join. The write under the `if` is never taken, so below the join nothing is proved and
the write there must detach from `b`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function rewrite(a WordArray, n Word)
	if n > 100 'never'
		try a.set(0, value: 1) otherwise panic("rewrite: a holds two elements")
	end 'never'
	try a.set(1, value: 7) otherwise panic("rewrite: a holds two elements")
end 'rewrite'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	let b = a.clone()
	rewrite(a, n: 3)
	if (try b.get(1) otherwise 0) != 2 'bUntouched'
		return 1
	end 'bUntouched'
	if (try a.get(1) otherwise 0) != 7 'aRewritten'
		return 2
	end 'aRewritten'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-clone-through-another-name-clears-the-facts -->
Control for the clearing rule's breadth. `a` and `b` name one record; the clone through `b` shares the
buffer `a`'s facts were about, so a rule that cleared only the callee's own record would let the second
write through `a` land in the share.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function rewrite(a WordArray, b WordArray) returns WordArray
	try a.set(0, value: 10) otherwise panic("rewrite: a holds two elements")
	let kept = b.clone()
	try a.set(1, value: 20) otherwise panic("rewrite: a holds two elements")
	return kept
end 'rewrite'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	let kept = rewrite(a, b: a)
	if (try kept.get(1) otherwise 0) != 2 'keptUntouched'
		return 1
	end 'keptUntouched'
	if (try a.get(1) otherwise 0) != 20 'aRewritten'
		return 2
	end 'aRewritten'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-managed-element-store-keeps-its-destructor-guard -->
Control for the destructor predicate. A `String` element owes a release when it is overwritten, so the
second write's destructor guard must still be asked: the first write's success proves nothing about it.
A store that skipped the release leaks the first string, and a leak is exit 101.
```maxon
typealias StringArray = Array with String

function rewrite(a StringArray)
	try a.set(0, value: "first heap string with enough bytes to be allocated") otherwise panic("rewrite: a holds an element")
	try a.set(0, value: "second heap string with enough bytes to be allocated") otherwise panic("rewrite: a holds an element")
end 'rewrite'

function main() returns ExitCode
	var a = StringArray.create()
	a.push("seed")
	rewrite(a)
	let kept = try a.get(0) otherwise ""
	if kept.count() != 52 'secondWriteLanded'
		return 1
	end 'secondWriteLanded'
	return 0
end 'main'
```
```exitcode
0
```
