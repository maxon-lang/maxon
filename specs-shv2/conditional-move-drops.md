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

Moving a value declared OUTSIDE a loop from INSIDE the loop body is rejected: dropping it on the loop's
other exit paths needs elaboration across the loop boundary (its back edge would re-move it, or a break
leaves it live on the normal exit), which is a later wave. Moving a value declared inside the loop body is
fine — it is reconciled within the body, once per iteration.

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

### Moving a Value Declared Outside a Loop Is Rejected (Straight-Line Re-Move)

`a` is declared outside the loop and moved into `u` inside the loop body with no `break`. The back edge
would re-move the already-moved `a` on the next iteration — a double-free. Placing the compensating drop
needs elaboration across the loop boundary (a later wave), so the move is refused rather than miscompiled.

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
error E2015: <fragment>:12:7: Unsupported: moving a value declared outside this loop from inside the loop body — its drop on the loop's other exit paths (the back edge would re-move it next iteration; a `break` leaves it live on the normal exit) needs path-sensitive elaboration across the loop boundary, which arrives with a later wave. Move the value into the loop body, or restructure so the move does not cross the loop boundary
```

### Moving a Value Declared Outside a Loop and `break`ing Is Rejected

`a` is declared outside the loop and moved into `u` on the branch that then `break`s. The break edge gives
`a` away while the normal loop exit leaves it live — its drop must land on the normal exit, an elaboration
across the loop boundary deferred to a later wave. Refused at the move rather than leaked or double-freed.

<!-- test: break-out-of-moved-branch-rejected -->
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
	return run(0)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:8: Unsupported: moving a value declared outside this loop from inside the loop body — its drop on the loop's other exit paths (the back edge would re-move it next iteration; a `break` leaves it live on the normal exit) needs path-sensitive elaboration across the loop boundary, which arrives with a later wave. Move the value into the loop body, or restructure so the move does not cross the loop boundary
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
