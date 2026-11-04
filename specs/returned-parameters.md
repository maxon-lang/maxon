---
feature: returned-parameters
status: stable
keywords: [refcount, retain, release, parameter, return, ownership, fusion, leak]
category: memory
---

# A function that returns its parameter

A function whose every return hands back one parameter returns that record counted: the caller adopts the
reference and releases it, or the one it overwrote, later. A `var` rebound through such a call
(`last = step(last)`) takes a reference to the record it already holds and releases the old one, so the net
change in the count is nothing; the compiler does not emit the pair.

These cases pin that nothing observable changes: every record is released exactly once (exit 0, never the leak
gate's 101), a write through one name is seen through every name that shares the record, and a function that
is reached other than by a direct call keeps returning a counted reference.

The cases whose subject is the emitted code are in `ir-specs/returned-parameters.md`.

## Tests

<!-- test: a-rebound-var-keeps-its-record-across-a-loop -->
`last = touch(last)` returns the record `last` already holds, a thousand times. The record is written
through each time, the loop ends with one record, and the leak gate finds nothing.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	try cells.set(0, value: (try cells.get(0) otherwise panic("get")) + 1) otherwise panic("set")

	return cells
end 'touch'

function main() returns ExitCode
	var last = TallyArray.create()
	last.push(0)

	for _ in 0 upto 1000 'each'
		last = touch(last)
	end 'each'

	print("{try last.get(0) otherwise 0} {last.count()}\n")
	return 0
end 'main'
```
```stdout
1000 1
```
```exitcode
0
```

<!-- test: the-result-is-the-argument-whichever-name-holds-it -->
`let` and `var` names bound to the result of `touch(a)` share `a`'s record: a write through any of them is
seen through all, and the record is released once when the last name goes.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var a = TallyArray.create()
	a.push(1)
	var b = touch(a)
	var c = touch(b)
	c.push(2)

	print("{a.count()} {b.count()} {c.count()}\n")
	return 0
end 'main'
```
```stdout
2 2 2
```
```exitcode
0
```

<!-- test: a-returned-parameter-also-stored-in-a-field-is-counted-for-both -->
`keep` stores its parameter in a field and also returns it, so the one record has the field, the returned
name and the caller's name as owners. Releasing them in any order frees it once.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Shelf
	export var held as TallyArray

	static function create() returns Shelf
		return Self{held: TallyArray.create()}
	end 'create'

	function keep(cells TallyArray) returns TallyArray
		self.held = cells
		return cells
	end 'keep'
end 'Shelf'

function main() returns ExitCode
	var shelf = Shelf.create()
	var cells = TallyArray.create()
	cells.push(5)
	var again = shelf.keep(cells)
	again.push(6)
	cells = TallyArray.create()

	print("{shelf.held.count()} {again.count()} {cells.count()}\n")
	return 0
end 'main'
```
```stdout
2 2 0
```
```exitcode
0
```

<!-- test: a-function-that-returns-a-parameter-on-one-path-only-still-balances -->
`pick` returns its parameter on one path and a fresh record on the other, so it is not a function that
returns a parameter; both paths hand back one counted reference.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function pick(cells TallyArray, keepIt bool) returns TallyArray
	if keepIt 'keep'
		return cells
	end 'keep'

	var fresh = TallyArray.create()
	fresh.push(9)
	return fresh
end 'pick'

function main() returns ExitCode
	var last = TallyArray.create()
	last.push(1)

	for round in 0 upto 6 'each'
		last = pick(last, keepIt: round mod 2 == 0)
	end 'each'

	print("{try last.get(0) otherwise 0} {last.count()}\n")
	return 0
end 'main'
```
```stdout
9 1
```
```exitcode
0
```

<!-- test: a-returned-parameter-reached-through-a-function-value-is-counted -->
`touch` is passed as a function value, so it is also reached through a pointer by a caller that expects a
counted reference. Calling it directly and through the value must both leave one owner per name.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias Step = function(TallyArray) returns TallyArray

function touch(cells TallyArray) returns TallyArray
	cells.push(1)

	return cells
end 'touch'

function apply(step Step, cells TallyArray) returns TallyArray
	return step(cells)
end 'apply'

function main() returns ExitCode
	var last = TallyArray.create()

	for _ in 0 upto 4 'each'
		last = touch(last)
		last = apply(touch, cells: last)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
8
```
```exitcode
0
```

<!-- test: a-returned-parameter-reached-through-an-interface-is-counted -->
`Stepper.advance` returns its parameter and implements an interface requirement, so a call through the
interface and a call on the concrete type must both hand back a counted reference.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

interface Advances
	function advance(cells TallyArray) returns TallyArray
end 'Advances'

type Stepper implements Advances
	export let bump as Tally

	static function create() returns Stepper
		return Self{bump: 1}
	end 'create'

	function advance(cells TallyArray) returns TallyArray
		cells.push(self.bump)

		return cells
	end 'advance'
end 'Stepper'

function throughTheInterface(advancer Advances, cells TallyArray) returns TallyArray
	return advancer.advance(cells)
end 'throughTheInterface'

function main() returns ExitCode
	let stepper = Stepper.create()
	var last = TallyArray.create()

	for _ in 0 upto 5 'each'
		last = stepper.advance(last)
		last = throughTheInterface(stepper, cells: last)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
10
```
```exitcode
0
```

<!-- test: a-returned-parameter-that-is-a-field-read-survives-the-field-being-replaced -->
`touch(shelf.held)` returns the record the field holds while `reset` replaces that field. The result must
keep the old record alive until its last use.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Shelf
	export var held as TallyArray

	static function create() returns Shelf
		var held = TallyArray.create()
		held.push(7)
		return Self{held: held}
	end 'create'

	function reset()
		self.held = TallyArray.create()
	end 'reset'
end 'Shelf'

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var shelf = Shelf.create()
	view seen = touch(shelf.held)
	shelf.reset()

	print("{try seen.get(0) otherwise 0} {shelf.held.count()}\n")
	return 0
end 'main'
```
```stdout
7 0
```
```exitcode
0
```

<!-- test: two-parameters-naming-one-record-return-the-first-counted -->
`first` returns its first parameter and is called with one record as both arguments, so the one record is
returned counted and each parameter name is a borrow.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function first(a TallyArray, b TallyArray) returns TallyArray
	b.push(a.count() as Tally)

	return a
end 'first'

function main() returns ExitCode
	var cells = TallyArray.create()
	cells.push(4)
	var out = first(cells, b: cells)
	out = first(out, b: out)

	print("{out.count()} {cells.count()}\n")
	return 0
end 'main'
```
```stdout
3 3
```
```exitcode
0
```

<!-- test: a-throwing-function-that-returns-its-parameter-balances-on-both-edges -->
`touch` throws on an odd count and returns its parameter otherwise, so a call is balanced whether it returns
or throws.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

enum Refused implements Error
	odd
end 'Refused'

function touch(cells TallyArray) returns TallyArray throws Refused
	if cells.count() mod 2 == 1 'odd'
		throw Refused.odd
	end 'odd'

	return cells
end 'touch'

function main() returns ExitCode
	var last = TallyArray.create()
	last.push(1)
	last.push(2)

	for _ in 0 upto 3 'each'
		last = try touch(last) otherwise last
	end 'each'

	last.push(3)
	last = try touch(last) otherwise TallyArray.create()
	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: a-record-handed-on-by-reassigning-its-old-name-survives -->
`b = touch(a)` names the record `a` holds, and `a` is then given a new record. The record is released by
`a`'s reassignment only if `b` did not take it over, so it must still be there, whole and writable, through
`b`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var a = TallyArray.create()
	a.push(3)
	var b = touch(a)
	a = TallyArray.create()
	b.push(4)

	print("{a.count()} {b.count()} {try b.get(0) otherwise 0}\n")
	return 0
end 'main'
```
```stdout
0 2 3
```
```exitcode
0
```

<!-- test: a-write-through-the-result-before-the-old-name-is-released-is-kept -->
A write through the result lands between taking the reference and releasing the old name's, so the two
cannot be treated as a pair that does nothing: the write is seen by the record that outlives `a`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var a = TallyArray.create()
	a.push(3)
	var b = touch(a)
	b.push(4)
	b.push(5)
	a = TallyArray.create()

	print("{a.count()} {b.count()}\n")
	return 0
end 'main'
```
```stdout
0 3
```
```exitcode
0
```

<!-- test: a-result-stored-in-a-container-outlives-the-name-it-came-from -->
`holder.push(touch(a))` stores the record `a` holds, and `a` is then reassigned. The container's reference
must keep the record alive after `a`'s is released.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias TallyArrays = Array with TallyArray

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var holder = TallyArrays.create()
	var a = TallyArray.create()
	a.push(8)
	holder.push(touch(a))
	a = TallyArray.create()
	a.push(1)
	a.push(2)

	view kept = try holder.get(0) otherwise TallyArray.create()
	print("{kept.count()} {try kept.get(0) otherwise 0} {a.count()}\n")
	return 0
end 'main'
```
```stdout
1 8 2
```
```exitcode
0
```

<!-- test: a-call-nested-in-a-call-of-the-same-function-balances -->
`touch(touch(last))` hands the record of the inner call to the outer one, and `last` takes the outer
result; every step of the chain is a counted reference to one record.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function fill(cells TallyArray, count Tally)
	for i in 0 upto count 'each'
		cells.push(i)
	end 'each'
end 'fill'

function touch(cells TallyArray) returns TallyArray
	fill(cells, count: 1)

	return cells
end 'touch'

function main() returns ExitCode
	var last = TallyArray.create()
	var other = TallyArray.create()

	for _ in 0 upto 5 'each'
		last = touch(touch(last))
		other = touch(other)
	end 'each'

	print("{last.count()} {other.count()}\n")
	return 0
end 'main'
```
```stdout
10 5
```
```exitcode
0
```

<!-- test: an-image-literal-passed-through-a-returning-function-is-written-through-a-copy -->
`b"abc"` is an immortal record in read-only memory. `touch` returns its parameter, so the result is a
private copy the caller may write; the writes, and the rebinds that follow, must not fault or reach the
literal.
```maxon
function touch(bytes ByteArray) returns ByteArray
	return bytes
end 'touch'

function main() returns ExitCode
	var last = touch(b"abc")
	last.push(100)

	for _ in 0 upto 3 'each'
		last = touch(last)
		last.push(7)
	end 'each'

	print("{last.count()} {touch(b"abc").count()}\n")
	return 0
end 'main'
```
```stdout
7 3
```
```exitcode
0
```

<!-- test: a-module-constant-array-passed-through-a-returning-function-leaves-the-constant-alone -->
The module-level array is an image record. Passing it through `touch` into a `var`, writing through the
`var` and rebinding it repeatedly must leave the constant at three elements.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

let Seed = TallyArray from [1, 2, 3]

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	let kept = touch(Seed)
	var last = Seed.clone()
	last.push(4)

	for _ in 0 upto 3 'each'
		last = touch(last)
		last.push(5)
	end 'each'

	print("{last.count()} {Seed.count()} {kept.count()}\n")
	return 0
end 'main'
```
```stdout
7 3 3
```
```exitcode
0
```

<!-- test: a-struct-method-that-returns-self-rebinds-without-a-reference -->
`bump` returns `self`, the plain-box form of a returned parameter. A loop rebinding its `var` to the
result must end with one record whose count is the lap count.
```maxon
typealias Tally = int(0 to u64.max)

type Plain
	export var hits as Tally

	static function create() returns Plain
		return Self{hits: 0}
	end 'create'

	function bump() returns Plain
		self.hits = self.hits + 1

		return self
	end 'bump'
end 'Plain'

function main() returns ExitCode
	var last = Plain.create()


	for _ in 0 upto 25 'each'
		last = last.bump()
	end 'each'

	print("{last.hits}\n")
	return 0
end 'main'
```
```stdout
25
```
```exitcode
0
```

<!-- test: a-closure-that-returns-its-argument-through-a-returning-function-balances -->
A closure calls `touch` and a free function value is built from `touch` itself, so both a direct call
inside a lifted body and a call through a pointer reach the function.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias Step = function(TallyArray) returns TallyArray

function touch(cells TallyArray) returns TallyArray
	cells.push(1)

	return cells
end 'touch'

function main() returns ExitCode
	let viaClosure = function(cells TallyArray) gives touch(cells)
	let viaValue = touch as Step
	var last = TallyArray.create()

	for _ in 0 upto 4 'each'
		last = viaClosure(last)
		last = viaValue(last)
		last = touch(last)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
12
```
```exitcode
0
```

<!-- test: an-async-spawn-of-a-returning-function-hands-back-a-counted-record -->
`async touch(cells)` runs the function on another green thread and `await` returns what it returned; the
record it hands back must be counted for the awaiting `var`.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	cells.push(1)

	return cells
end 'touch'

function main() returns ExitCode
	var last = TallyArray.create()

	for _ in 0 upto 3 'each'
		let pending = async touch(last.clone())
		last = await pending
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-generic-instantiation-of-a-returning-function-balances -->
`Pass with TallyArray` instantiates a method that returns its parameter. The instance is a distinct
function, called directly and through the instance's own methods.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Pass uses Element
	export let marker as Tally

	static function create() returns Self
		return Self{marker: 1}
	end 'create'

	function through(item Element) returns Element
		return item
	end 'through'
end 'Pass'

typealias CellsPass = Pass with TallyArray

function main() returns ExitCode
	let pass = CellsPass.create()
	var last = TallyArray.create()
	last.push(0)

	for _ in 0 upto 6 'each'
		last = pass.through(last)
		last.push(pass.marker)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-slice-passed-through-a-returning-function-detaches-on-write -->
A slice shares its parent's buffer until a write. Passing one through `touch` and writing to the result
must leave the parent as it was.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function main() returns ExitCode
	var parent = TallyArray.create()
	parent.push(1)
	parent.push(2)
	parent.push(3)
	var part = touch(try parent.slice(0, endIndex: 2) otherwise TallyArray.create())
	part.push(9)

	for _ in 0 upto 2 'each'
		part = touch(part)
		part.push(8)
	end 'each'

	print("{parent.count()} {part.count()}\n")
	return 0
end 'main'
```
```stdout
3 5
```
```exitcode
0
```

<!-- test: a-retain-in-code-nothing-reaches-is-left-alone -->
The statement after the exhaustive `if`/`else` returns is never reached. Its call to `touch` still lowers,
holds a retain that has no partner on any path, and must not trip the pairing walk.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function touch(cells TallyArray) returns TallyArray
	return cells
end 'touch'

function pick(flag bool, cells TallyArray) returns TallyArray
	var last = cells

	if flag 'yes'
		return last
	end 'yes' else 'no'
		return touch(last)
	end 'no'

	last = touch(last)

	return last
end 'pick'

function main() returns ExitCode
	var cells = TallyArray.create()
	cells.push(2)
	cells = pick(true, cells: cells)
	cells = pick(false, cells: cells)

	print("{cells.count()}\n")
	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-rebind-in-one-arm-of-a-branch-beside-an-image-literal-in-the-other-is-written-through-a-copy -->
`route` rebinds `last` through `touch` in one arm and takes `touch(b"xyz")` in the other, so the join is
reached from a predecessor that holds a counted record and one that holds the immortal literal's copy. Each
call writes to its result; none may write to the literal, and the counts must not drift between calls.
```maxon
function touch(bytes ByteArray) returns ByteArray
	return bytes
end 'touch'

function route(flag bool) returns ByteArray
	var last = ByteArray.create()
	last.push(1)

	if flag 'again'
		last = touch(last)
	end 'again' else 'first'
		last = touch(b"xyz")
	end 'first'

	last.push(9)

	return last
end 'route'

function main() returns ExitCode
	print("{route(true).count()} {route(false).count()} {route(false).count()} {touch(b"xyz").count()}\n")
	return 0
end 'main'
```
```stdout
2 4 4 3
```
```exitcode
0
```

<!-- test: a-struct-with-a-managed-field-rebinds-through-a-method-that-returns-self -->
`bump` returns `self` for a struct that owns an array, so the owned copy of the struct and its release run
the field's own release. A loop rebinding through `bump` and through a free function that returns its
parameter must keep each struct's array whole.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

type Counter
	export var hits as Tally
	export var log as TallyArray

	static function create() returns Counter
		return Self{hits: 0, log: TallyArray.create()}
	end 'create'

	function bump() returns Counter
		self.hits = self.hits + 1
		self.log.push(self.hits)

		return self
	end 'bump'
end 'Counter'

function spin(counter Counter, laps Tally)
	for _ in 0 upto laps 'each'
		counter.hits = counter.hits + 1
	end 'each'
end 'spin'

function adopt(counter Counter) returns Counter
	spin(counter, laps: 2)

	return counter
end 'adopt'

function main() returns ExitCode
	var first = Counter.create()
	var second = Counter.create()

	for _ in 0 upto 10 'eachFirst'
		first = adopt(first)
	end 'eachFirst'

	for _ in 0 upto 20 'eachSecond'
		second = second.bump()
	end 'eachSecond'

	print("{first.hits} {first.log.count()} {second.hits} {second.log.count()}\n")
	return 0
end 'main'
```
```stdout
20 0 20 20
```
```exitcode
0
```

<!-- test: a-parameter-stored-in-a-container-and-returned-keeps-one-owner-per-holder -->
`stash` pushes its parameter into a container and returns it, so the record is owned by the container, by the
returned reference and by the caller's name. Rebinding the name through `stash` repeatedly must leave every
slot of the container naming the one record.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally
typealias TallyArrays = Array with TallyArray

function stash(holder TallyArrays, cells TallyArray) returns TallyArray
	holder.push(cells)

	return cells
end 'stash'

function main() returns ExitCode
	var holder = TallyArrays.create()
	var last = TallyArray.create()

	for _ in 0 upto 3 'each'
		last = stash(holder, cells: last)
		last.push(1)
	end 'each'

	view kept = try holder.get(2) otherwise TallyArray.create()
	print("{holder.count()} {last.count()} {kept.count()}\n")
	return 0
end 'main'
```
```stdout
3 3 3
```
```exitcode
0
```

<!-- test: a-closure-that-returns-a-small-value-tuple-is-read-through-a-function-value -->
A closure builds a small value tuple, with the second element chosen by a conditional, and is called directly
and through a function value. Both calls must read the same pair back.
```maxon
typealias Num = int(0 to 1000)
typealias PairOp = function(Num, Num) returns (Num, Num)

function apply(op PairOp, a Num, b Num) returns Num
	let pair = op(a, b)

	return pair._0 + pair._1
end 'apply'

function main() returns ExitCode
	let widen = function(a Num, b Num) gives (a + 1, (b + 2) if a > 5 else b)
	let direct = widen(10, 20)
	let miss = widen(1, 2)

	print("{direct._0} {direct._1} {miss._0} {miss._1} {apply(widen, a: 10, b: 20)}\n")
	return 0
end 'main'
```
```stdout
11 22 2 2 33
```
```exitcode
0
```

<!-- test: a-closure-that-returns-its-parameter-balances-through-a-rebound-var -->
The closure's whole body is its array parameter, so the function behind the value returns one of its parameters.
The caller rebinds its `var` through the closure on every lap and writes through the result.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function main() returns ExitCode
	let same = function(cells TallyArray) gives cells
	var last = TallyArray.create()

	for _ in 0 upto 9 'each'
		last = same(last)
		last.push(1)
	end 'each'

	print("{last.count()}\n")
	return 0
end 'main'
```
```stdout
9
```
```exitcode
0
```

<!-- test: a-recursive-function-that-returns-its-parameter-balances -->
`descend` recurses and returns the parameter its caller handed it, so the references it takes unwind to one.
```maxon
typealias Tally = int(0 to u64.max)
typealias TallyArray = Array with Tally

function descend(cells TallyArray, depth Tally) returns TallyArray
	if depth == 0 'bottom'
		return cells
	end 'bottom'

	cells.push(depth)
	var inner = descend(cells, depth: depth - 1)
	inner.push(0)

	return cells
end 'descend'

function main() returns ExitCode
	var cells = TallyArray.create()
	cells = descend(cells, depth: 5)

	print("{cells.count()}\n")
	return 0
end 'main'
```
```stdout
10
```
```exitcode
0
```
