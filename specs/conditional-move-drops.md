---
feature: conditional-move-drops
status: experimental
keywords: [ownership, move, drop, conditional, path-sensitive, if, else, match, leak, double-free]
category: memory
---

# Path-Sensitive Drops for Conditional Moves

## Documentation

An owned heap value (an owned `String`, a struct box, a boxed union) is dropped exactly once at its
owner's scope exit. When that value is MOVED on some control-flow paths but not others — moved into a
field on one `if` branch, into a union payload in one `match` arm, consumed by a call on one path — the
drop must be PLACED PATH-SENSITIVELY: emitted on the paths that did NOT move it, skipped on the paths
that did.

The move flag (`VarInfo.movedFrom`) is therefore reconciled at every control-flow JOIN. A binding moved
on some incoming edges but still live on others is DROPPED on the live edges, at compile time, so that
after the join the binding is uniformly not-owned and its later scope-exit drop is skipped. A branch that
MOVED then left (a `return` out of the moved branch) contributes no join edge — the surviving paths keep
their own live state, and drop the value themselves.

⚠ **EVERY SOURCE HERE IS A `var`, and that is load-bearing rather than incidental.** A bind from an
IMMUTABLE binding is an ALIAS, not a move (`specs/ownership.md`; see `moves.md`), so with `let a` these
programs would perform no move at all and would still pass every expectation below while testing
nothing. The `var` is what makes `let u = a` a move and puts the reconciliation on trial.

This is a compile-time elaboration: no runtime "was-it-moved" flag exists. A value moved on ALL paths is
skipped once per path (no double-free); a value moved on NO path is dropped once; and a READ of a value
that is moved on some-but-not-all paths past the join stays a conservative use-after-move (E3102) — being
maybe-moved, it may not be read, even though it is correctly dropped where it is not moved.

A loop is reconciled the same way: its entry and every back edge meet at the header, and its normal exit
and every `break` meet after it. A value declared OUTSIDE a loop and moved INSIDE it is moved on exactly the
paths that move it and dropped on the others; a back edge that brings a moved value round to a read of it is
E3102 ("moved in an earlier iteration of this loop"). A value declared inside the loop body is reconciled
within the body, once per iteration.

## Tests

### Union Payload Moved on One Branch, Other Branch Live

`s` is moved into `Wrap.holds(s)` on the `flag > 0` branch (which returns), and left live on the fall-
through path (`return Wrap.empty`). Driven down the LIVE path (`makeWrap(0)`): `s` must be dropped once
there. Before path-sensitive drops the move flag persisted onto the live path and `s` leaked (exit 101).

<!-- test: union-payload-cond-move-return -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Wrap
	empty
	holds(s String)
end 'Wrap'

function makeWrap(flag Integer) returns Wrap
	let s = "wrap payload {41} padded long enough to heap allocate"
	if flag > 0 'f'
		return Wrap.holds(s)
	end 'f'
	return Wrap.empty
end 'makeWrap'

function main() returns ExitCode
	let w = makeWrap(0)
	print("{w}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty

```

### Consumed-Into-Field Parameter Moved on One Branch, Other Branch Live

`create`'s parameter `f` is consumed into the struct field `name` on the `flag > 0` branch, and left live
on the fall-through branch (which builds the struct from a literal). Driven down the LIVE path
(`create(s, flag: 0)`): the consumed parameter `f` must be dropped once inside `create`. Before path-
sensitive drops the field-consume's move flag persisted onto the fall-through path and `f` leaked.

<!-- test: field-consume-cond-move-return -->
```maxon
type Named
	export var name as String

	static function create(f String, flag Integer) returns Named
		if flag > 0 'g'
			return Self{name: f}
		end 'g'
		return Self{name: "fallback name padded long enough to heap allocate"}
	end 'create'
end 'Named'

function main() returns ExitCode
	let s = "argument {7} padded out long enough to heap allocate"
	let n = Named.create(s, flag: 0)
	print("{n.name}")
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
fallback name padded long enough to heap allocate

```

### Move in One `if`/`else` Branch, Other Branch Live (Both Fall Through)

`a` is moved into the block-local `u` on the `then` branch (which drops it at the branch's `end`) and left
untouched on the `else` branch. Both branches fall through to the merge. Driven down the LIVE `else` path
(`flag = 0`): `a` must be dropped once on that path. The drop is placed on the else edge at the join.

<!-- test: ifelse-move-one-branch -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var a = build(1)
	let flag = 0
	if flag > 0 'b'
		let u = a
		print(u)
	end 'b' else 'e'
		print("no move on this branch padded long enough")
	end 'e'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
no move on this branch padded long enough
```

### Move in an `if` With No `else`, Other Path Live (Then Falls Through)

`a` is moved into block-local `u` on the `then` branch (which falls through), and left live on the implicit
false path. Driven down the LIVE false path (`flag = 0`): `a` must be dropped once. The drop is placed on a
false-edge block minted at the join, since the then branch fell through rather than returning.

<!-- test: ifnoelse-move-fallthrough -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var a = build(1)
	let flag = 0
	if flag > 0 'b'
		let u = a
		print(u)
	end 'b'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
```

### Value Moved on BOTH Branches (Moved on All Paths, No Double-Free)

`s` is moved into `Wrap.holds(s)` on BOTH the `then` and `else` branch (each returns). Before the move
state was restored between the branches, parsing the `else` saw the `then`'s move and rejected it as a
false use-after-move (E3102). With path-sensitive move state each branch moves `s` exactly once, and the
value is given away on every path — dropped nowhere by `makeWrap`, no double-free.

<!-- test: both-branches-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

union Wrap
	empty
	holds(s String)
end 'Wrap'

function makeWrap(flag Integer) returns Wrap
	let s = "wrap payload {41} padded long enough to heap allocate"
	if flag > 0 'f'
		return Wrap.holds(s)
	end 'f' else 'g'
		return Wrap.holds(s)
	end 'g'
end 'makeWrap'

function main() returns ExitCode
	let w = makeWrap(0)
	print("{w}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
holds

```

### Move in One `match` Arm, Other Arms Live

`a` is moved into block-local `u` in the `red` arm; the `green` and `blue` arms leave it live. Driven down
a LIVE arm (`Color.green`): `a` must be dropped once on that arm's path. The drop is placed on the live
arms' exit blocks at the match merge.

<!-- test: match-arm-move-one-arm -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function pick(c Color) returns ExitCode
	var a = build(1)
	var moved = ""
	match c 'm'
		red then moved = a
		green then print("green arm leaves it live padded long")
		blue then print("blue arm leaves it live padded long")
	end 'm'
	print("{moved}")
	return 0
end 'pick'

function main() returns ExitCode
	return pick(Color.green)
end 'main'
```
```exitcode
0
```
```stdout
green arm leaves it live padded long
```

### `match` Arm That Moves and Returns, Other Arm Live

The `red` arm moves `a` into a returned `Wrap.holds(a)` (it leaves via `return`); the other arms leave `a`
live and fall through past the match to `return Wrap.empty`. Driven down a LIVE arm (`Color.green`): `a`
must be dropped once. The returning arm contributes no merge edge, so the live path keeps its live state.

<!-- test: match-arm-move-return -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum Color
	red
	green
	blue
end 'Color'

union Wrap
	empty
	holds(s String)
end 'Wrap'

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function pick(c Color) returns Wrap
	let a = build(1)
	match c 'm'
		red then return Wrap.holds(a)
		green then print("green arm leaves it live padded long")
		blue then print("blue arm leaves it live padded long")
	end 'm'
	return Wrap.empty
end 'pick'

function main() returns ExitCode
	let w = pick(Color.green)
	print("{w}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
green arm leaves it live padded longempty

```

### Conditional Move of a Loop-Body-Local Value (Reconciled Per Iteration)

`s` is declared inside the loop body and conditionally moved into `u` on the `i > 0` iterations. Each
iteration `s` is either moved (and dropped through `u`) or left live (and dropped at the false edge) —
exactly once per iteration. Runs two iterations, one of each kind.

<!-- test: loop-body-local-cond-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "iteration value {x} padded out long enough to heap allocate"
end 'build'

function run(n Integer) returns ExitCode
	var i = 0
	while i < n 'l'
		var s = build(i)
		if i > 0 'b'
			let u = s
			print(u)
		end 'b'
		i = i + 1
	end 'l'
	return 0
end 'run'

function main() returns ExitCode
	return run(2)
end 'main'
```
```exitcode
0
```
```stdout
iteration value 1 padded out long enough to heap allocate
```

### Use of a Maybe-Moved Value After a Conditional Move Is Use-After-Move

`a` is moved into `u` inside the `if` body. Past the merge `a` is maybe-moved (moved on the taken branch,
live on the other), so READING it (`print(a)`) is a conservative use-after-move — E3102 — even though the
value is correctly dropped on the path that did not move it. Path-sensitive DROPS do not relax the
conservative use-after-move rule.

<!-- test: use-after-conditional-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var a = build(1)
	let flag = 1
	if flag > 0 'b'
		let u = a
		print(u)
	end 'b'
	print(a)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:8: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

### Moving a Value Declared Outside a Loop on Every Iteration Is a Use After Move

`a` is declared outside the loop and moved into `u` inside the loop body with no `break`. The back edge
brings the already-moved `a` round to the same move on the next iteration, so the read is E3102.

<!-- test: outer-move-in-loop-rejected -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function run(n Integer) returns ExitCode
	var a = build(1)
	var i = 0
	while i < n 'l'
		let u = a
		print(u)
		i = i + 1
	end 'l'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```maxoncstderr
error E3102: <fragment>:12:11: use of moved value 'a': it was moved in an earlier iteration of this loop
```

### Moving a Value Declared Outside a Loop and `break`ing Moves It Once

`a` is declared outside the loop and moved into `u` on the branch that then `break`s. The break edge gives
`a` away while the normal loop exit leaves it live, so `a` is dropped on the normal exit only. `run(0)` never
enters the body and `run(3)` moves `a` on its second trip; each releases `a` exactly once.

<!-- test: break-out-of-moved-branch-moves-once -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function run(n Integer) returns ExitCode
	var a = build(1)
	var i = 0
	while i < n 'l'
		if i > 0 'b'
			let u = a
			print(u)
			break
		end 'b'
		i = i + 1
	end 'l'
	return 0
end 'run'

function main() returns ExitCode
	_ = run(0)
	return run(3)
end 'main'
```
```stdout
built value 1 padded out long enough to heap allocate
```

### `try … otherwise` Handler That Moves and Terminates, OK Path Live

The two-branch reconciliation reaches the `try` fork as well: `a` is moved into the handler-local `u` by
an `otherwise (e)` handler that then returns, while the OK path never moved it. Driven down the OK path
(`step(1)` does not throw), `a` must be dropped once when `run` exits. Before the fork restored the move
state on its surviving edge, the handler's move persisted onto the ok path and `a` leaked (exit 101).

<!-- test: try-handler-move-return-ok-live -->
```maxon
typealias Code = int(0 to 125)

enum StepError implements Error
	bad
end 'StepError'

function build(x Code) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function step(k Code) returns Code throws StepError
	if k < 1 'b'
		throw StepError.bad
	end 'b'
	return k
end 'step'

function run(k Code) returns ExitCode
	var a = build(1)
	let v = try step(k) otherwise 'bad'
		let u = a
		print("handed={u}\n")
		return 2
	end 'bad'
	print("v={v}\n")
	return 0
end 'run'

function main() returns ExitCode
	return run(1)
end 'main'
```
```exitcode
0
```
```stdout
v=1
```

### `try … otherwise` Handler That Moves and FALLS THROUGH, Both Edges Live

The same fork with both edges reaching the continuation: the handler moves `a` and runs off its end, the
ok path leaves it live. `a` must be dropped on the ok edge and marked moved past the merge, so the
scope-exit drop is right on both paths. Driven down the OK path here.

<!-- test: try-handler-move-fallthrough-both-live -->
```maxon
typealias Code = int(0 to 125)

enum StepError implements Error
	bad
end 'StepError'

function build(x Code) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function step(k Code) returns Code throws StepError
	if k < 1 'b'
		throw StepError.bad
	end 'b'
	return k
end 'step'

function run(k Code) returns ExitCode
	var a = build(1)
	try step(k) otherwise 'bad'
		let u = a
		print("moved={u}\n")
	end 'bad'
	print("done\n")
	return 0
end 'run'

function main() returns ExitCode
	return run(1)
end 'main'
```
```exitcode
0
```
```stdout
done
```

### Value Moved on the Right of a Short-Circuit

`a` is moved into `keep(a)` only when the left of `and` is true. Driven down both paths: where the left is
false the right never runs, so `a` is still owned there and dropped once. A leak exits 101; a double release
faults.

<!-- test: short-circuit-rhs-move -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Holder
	export var text as String

	static function create(text String) returns Holder
		return Self{text: text}
	end 'create'
end 'Holder'

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function keep(s String) returns bool
	let h = Holder.create(s)
	return h.text.byteLength() > 0
end 'keep'

function run(flag Integer) returns bool
	var a = build(flag)
	let kept = flag > 0 and keep(a)
	return kept
end 'run'

function main() returns ExitCode
	print("{run(0)} {run(1)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false true
```

<!-- test: a-string-moved-inside-a-while-loop-that-then-breaks-is-moved-once -->
A `String` declared outside a `while` loop and moved on the path that breaks is moved once; a call whose loop never reaches the move drops it on the normal exit.
```maxon
typealias Integer = int(i64.min to i64.max)

function label(n Integer) returns String
	return "item{n}"
end 'label'

function movedOnceThenLeaves(stop Integer) returns Integer
	var s = label(stop)
	var i = 0
	var seen = 0

	while i < 3 'trips'
		if i == stop 'found'
			let t = s
			seen = t.count() as Integer
			break
		end 'found'

		i = i + 1
	end 'trips'

	return seen
end 'movedOnceThenLeaves'

function main() returns ExitCode
	print("{movedOnceThenLeaves(2)} {movedOnceThenLeaves(9)}\n")
	return 0
end 'main'
```
```stdout
5 0
```

<!-- test: error.a-string-moved-on-every-iteration-is-used-after-its-move -->
A `String` declared outside a loop and moved on every trip is read, on the second trip, after the first moved it.
```maxon
typealias Integer = int(i64.min to i64.max)

function label(n Integer) returns String
	return "item{n}"
end 'label'

function main() returns ExitCode
	var s = label(1)
	var i = 0

	while i < 2 'trips'
		let t = s
		print("{t}\n")
		i = i + 1
	end 'trips'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:13:11: use of moved value 's': it was moved in an earlier iteration of this loop
```

<!-- test: a-binding-reassigned-before-its-move-on-every-trip-is-legal -->
A binding reassigned before it is moved on every trip never meets a moved value; the value it held before the loop is released once.
```maxon
typealias Integer = int(i64.min to i64.max)

function label(n Integer) returns String
	return "item{n}"
end 'label'

function reassignedThenMoved(count Integer) returns Integer
	var s = label(0)
	var total = 0
	var i = 0

	while i < count 'trips'
		s = label(i)
		let t = s
		total = total + t.count() as Integer
		i = i + 1
	end 'trips'

	return total
end 'reassignedThenMoved'

function main() returns ExitCode
	print("{reassignedThenMoved(3)} {reassignedThenMoved(0)}\n")
	return 0
end 'main'
```
```stdout
15 0
```

<!-- test: a-promise-rearmed-before-its-move-on-every-trip-is-legal -->
The promise form of a binding reassigned before its move on every trip, in a `for` loop.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntPromise = Promise with Integer

function work(n Integer) returns Integer
	Scheduler.yield()
	return n + 1
end 'work'

function keep(p IntPromise, v Integer) returns IntPromise
	print("keep {v}\n")
	return p
end 'keep'

function rearmedPromiseEachTrip(count Integer) returns Integer
	var p = async work(0)
	var total = 0

	for i in 0 upto count 'trips'
		p = async work(i)
		let q = keep(p, v: i)
		total = total + await q
	end 'trips'

	return total
end 'rearmedPromiseEachTrip'

function main() returns ExitCode
	print("{rearmedPromiseEachTrip(3)}\n")
	print("{rearmedPromiseEachTrip(0)}\n")
	return 0
end 'main'
```
```stdout
keep 0
keep 1
keep 2
6
0
```

<!-- test: a-string-moved-inside-a-for-loop-that-then-breaks-is-moved-once -->
A `String` declared outside a `for` loop and moved on the path that breaks is moved once.
```maxon
typealias Integer = int(i64.min to i64.max)

function label(n Integer) returns String
	return "item{n}"
end 'label'

function forMoveThenBreak(stop Integer) returns Integer
	var s = label(stop)
	var seen = 0

	for i in 0 upto 3 'trips'
		if i == stop 'found'
			let t = s
			seen = t.count() as Integer
			break
		end 'found'
	end 'trips'

	return seen
end 'forMoveThenBreak'

function main() returns ExitCode
	print("{forMoveThenBreak(1)} {forMoveThenBreak(9)}\n")
	return 0
end 'main'
```
```stdout
5 0
```

<!-- test: a-reassigned-value-moved-on-one-back-edge-is-dropped-on-the-live-one -->
A `continue` back edge carries a freshly assigned live `s` while the fall-through back edge moved it: the live edge drops its value, the moved edge drops nothing.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	var i = 0

	while i < 4 'l'
		i = i + 1
		s = build(i)

		if i mod 2 == 0 'even'
			continue
		end 'even'

		let u = s
		print("{u}\n")
	end 'l'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 1 padded out long enough to heap allocate
built value 3 padded out long enough to heap allocate
```

<!-- test: a-read-through-a-merge-that-may-hold-the-moved-header-value-is-use-after-move -->
`s` is moved on every back edge, and after the `if` with no `else` it may still be the value the previous trip moved, so reading it is E3102.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	var i = 0

	while i < 3 'l'
		i = i + 1

		if i > 1 'r'
			s = build(i)
		end 'r'

		let u = s
		print("{u}\n")
	end 'l'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:11: use of moved value 's': it was moved in an earlier iteration of this loop
```

<!-- test: reassigning-a-value-that-may-be-the-moved-header-value-releases-only-the-fresh-one -->
A merge of the header value (moved on the back edge) with a fresh value is reassigned: only the fresh operand is released, on its own edge.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	var i = 0

	while i < 3 'l'
		i = i + 1

		if i == 2 'r'
			s = build(i)
		end 'r'

		s = build(10 + i)
		let u = s
		print("{u}\n")
	end 'l'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 11 padded out long enough to heap allocate
built value 12 padded out long enough to heap allocate
built value 13 padded out long enough to heap allocate
```

<!-- test: a-value-moved-before-the-loop-and-reassigned-in-it-is-dropped-on-the-back-edge -->
`s` is moved before the loop; each trip assigns a fresh value that reaches the back edge live and is dropped there.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	let u = s
	print("{u}\n")
	var i = 0

	while i < 3 'l'
		i = i + 1
		s = build(i)
	end 'l'

	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 0 padded out long enough to heap allocate
done
```

<!-- test: an-inner-loop-over-a-value-the-outer-loop-moves-drops-its-fresh-values -->
The outer loop moves `s` on its back edge; the inner loop's header merges that header value with fresh values, each released once.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	var i = 0

	while i < 3 'outer'
		i = i + 1
		var j = 0

		while j < 2 'inner'
			j = j + 1
			s = build(100 * i + j)
		end 'inner'

		s = build(i)
		let u = s
		print("{u}\n")
	end 'outer'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 1 padded out long enough to heap allocate
built value 2 padded out long enough to heap allocate
built value 3 padded out long enough to heap allocate
```

<!-- test: a-for-loop-continue-after-a-reassign-drops-the-live-value -->
The `for` form of the first case: the `continue` edge reaches the step block holding a live fresh value.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)

	for k in 1 to 4 'each'
		s = build(k)

		if k mod 2 == 0 'even'
			continue
		end 'even'

		let u = s
		print("{u}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 1 padded out long enough to heap allocate
built value 3 padded out long enough to heap allocate
```

<!-- test: a-break-with-a-live-value-out-of-a-loop-that-moves-it-on-the-back-edge -->
The break edge carries a live fresh value while the normal exit sees it moved, so the break edge drops it.
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "built value {x} padded out long enough to heap allocate"
end 'build'

function main() returns ExitCode
	var s = build(0)
	var i = 0

	while i < 5 'l'
		i = i + 1
		s = build(i)

		if i == 3 'stop'
			break
		end 'stop'

		let u = s
		print("{u}\n")
	end 'l'

	print("done\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
built value 1 padded out long enough to heap allocate
built value 2 padded out long enough to heap allocate
done
```

<!-- test: a-cell-resident-var-moved-on-one-branch-is-released-once-on-every-path -->
`word` lives in a cell because `fill` reassigns its parameter. It is moved by a send on one branch, by an `async` call on another, and not at all on the third; every path releases the cell and its contents exactly once.
```maxon
typealias Integer = int(i64.min to i64.max)

type Echo
	var count as Integer

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function shout(text String) returns String
		self.count = self.count + 1
		return "{text}!"
	end 'shout'
end 'Echo'

function shoutAsync(text String) returns Integer
	Scheduler.yield()
	print("{text}?\n")
	return 1
end 'shoutAsync'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function run(h Echo.handle, send bool, spawn bool) returns Integer
	var word = "hello padded out long enough to heap allocate {1}"
	fill(word, n: 2)
	var total = 0

	if send 'sendIt'
		let loud = try await h.shout(word) otherwise "stopped"
		print("{loud}\n")
		total = total + 1
	end 'sendIt' else if spawn 'spawnIt'
		let p = async shoutAsync(word)
		total = total + (await p)
	end 'spawnIt'

	return total
end 'run'

function main() returns ExitCode
	let h = spawn Echo.create()
	print("{run(h, send: true, spawn: false)}\n")
	print("{run(h, send: false, spawn: true)}\n")
	print("{run(h, send: false, spawn: false)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled 2 padded out long enough to heap allocate!
1
filled 2 padded out long enough to heap allocate?
1
0
```

<!-- test: a-cell-resident-var-reassigned-and-moved-each-trip-with-a-continue -->
The loop form for a cell: reassigned every trip, moved on one back edge, live on the `continue` edge.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {0}"
	var i = 0
	var total = 0

	while i < 4 'l'
		i = i + 1
		word = "word {i} padded out long enough to heap allocate"
		fill(word, n: i)

		if i mod 2 == 0 'skip'
			continue
		end 'skip'

		let p = async shout(word)
		total = total + (await p)
	end 'l'

	print("{total}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
filled 1 padded out long enough to heap allocate!
filled 3 padded out long enough to heap allocate!
2
```

<!-- test: a-cell-resident-var-moved-in-a-loop-and-read-next-trip-is-use-after-move -->
`fill(word, …)` at the top of the next trip reads the contents the previous trip moved.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {0}"
	var i = 0
	var total = 0

	while i < 4 'l'
		i = i + 1
		fill(word, n: i)
		let p = async shout(word)
		total = total + (await p)
	end 'l'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:8: use of moved value 'word': it was moved in an earlier iteration of this loop
```

<!-- test: a-cell-resident-var-restored-on-some-trips-only-is-use-after-move -->
The cell is re-stored on even trips only, so on an odd trip the read reaches the contents the previous trip moved.
```maxon
typealias Integer = int(i64.min to i64.max)

function shout(text String) returns Integer
	Scheduler.yield()
	print("{text}!\n")
	return 1
end 'shout'

function fill(dest String, n Integer)
	dest = "filled {n} padded out long enough to heap allocate"
end 'fill'

function main() returns ExitCode
	var word = "hello padded out long enough to heap allocate {0}"
	fill(word, n: 0)
	var i = 0
	var total = 0

	while i < 4 'l'
		i = i + 1

		if i mod 2 == 0 'restore'
			word = "word {i} padded out long enough to heap allocate"
		end 'restore'

		let p = async shout(word)
		total = total + (await p)
	end 'l'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:23: use of moved value 'word': it was moved in an earlier iteration of this loop
```
