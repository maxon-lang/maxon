---
feature: try-block-ownership
status: experimental
keywords: [try, block, otherwise, ownership, move, drop, leak, closure, routing]
category: memory
---

# Ownership Across a Block-Form `try`

## Documentation

A block-form `try 'l' … end 'l' otherwise (e) 'h' … end 'h'` routes every bare throwing call in its body
to ONE shared handler. Each routed call ends its block with `flag != NoErrorFlag ? <throw edge> : <ok
continuation>`, so the construct is a fork like `if`/`else` — and it owes the same two obligations that
fork owes.

**A ROUTED CALL'S OWN RESULT IS OWNED ONLY ON THE OK EDGE.** The `tryCall` writes its result register on
the success path and leaves it untouched when the flag is set, so the throw edge's landing pad — which
releases the temporaries and body-local bindings the half-finished statement owed — must NOT release the
result itself. `desugarTry` states the identical rule for the single-call form: on the error edge that
register is null, and decrefing it faults.

**AND THE MOVE STATE IS RECONCILED AT THE CONSTRUCT'S MERGE**, exactly as `conditional-move-drops.md`
requires of `if`/`else` and `match`. A handler that MOVES an owned binding the body left live must not
leave that binding marked moved on the body's continuation, or its scope-exit drop is skipped and the
value leaks. The handler is parsed against the ownership state the THROW EDGE carries, not the body's end
state, because the edge leaves the body at a routed call and anything the body moved afterwards did not
happen on that path.

⚠ **A BODY THAT MOVES AN OUTER VALUE BETWEEN TWO ROUTED CALLS IS REFUSED.** The block's throw edges all
land on one error block, and a conditional move is reconciled by placing the drop PER EDGE — so a binding
live on the early throw edges and already given away on the later ones would need its release on some of
them and not others. Refused rather than lowered into a leak on one edge or a double free on another,
the same call `conditional-move-drops.md` records for a move that escapes a loop.

**A CLOSURE IN THE BODY IS PARSED IN ITS OWN FUNCTION AND RESTORES THE BODY'S ROUTING STATE.** A closure
is a lifted function with its own blocks and its own SSA space, so the enclosing block-form `try`'s
routing context is set aside while it parses and put back afterwards — the body's later throwing calls go
on routing to the handler.

⚠ **EVERY OWNED VALUE HERE IS BUILT BY `build(x)`, WHICH INTERPOLATES, AND EVERY MOVE SOURCE IS A `var`.**
Both are load-bearing rather than stylistic: a BARE String LITERAL is a static
constant that owns no heap, so a case using one is dropped by nobody and leaks nothing whatever the move
state says; and a bind from an IMMUTABLE binding is an ALIAS rather than a move
(`conditional-move-drops.md` states the second rule for its own corpus). With `let s = "…"` these programs
pass every expectation below while testing none of the subject.

## Tests

### A Routed Call Whose Result Is MANAGED

`fetch` returns an owned `String` and throws. The call is bare inside the block body, so it is routed —
and its result is a managed temporary the statement already owes a drop for. The throw edge must not
release it: on that edge the result register was never written. A landing pad that decref'd it would
kill the program with an access violation instead of reaching the handler.

<!-- test: routed-call-managed-result -->
```maxon
typealias Code = int(0 to 125)

enum FetchError implements Error
	notFound
end 'FetchError'

function fetch(k Code) returns String throws FetchError
	if k < 1 'bad'
		throw FetchError.notFound
	end 'bad'
	return "fetched {k} padded out long enough to heap allocate"
end 'fetch'

function run(k Code) returns ExitCode
	try 'work'
		let s = fetch(k)
		print("got={s}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			notFound then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```exitcode
0
```
```stdout
caught
```

### The Same Routed Managed Result, Driven Down the OK Path

The success path binds the managed result and must drop it exactly once — the removal above is confined
to the throw edge and does not lose the ok path's drop.

<!-- test: routed-call-managed-result-ok-path -->
```maxon
typealias Code = int(0 to 125)

enum FetchError implements Error
	notFound
end 'FetchError'

function fetch(k Code) returns String throws FetchError
	if k < 1 'bad'
		throw FetchError.notFound
	end 'bad'
	return "fetched {k} padded out long enough to heap allocate"
end 'fetch'

function run(k Code) returns ExitCode
	try 'work'
		let s = fetch(k)
		print("got={s}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			notFound then print("caught\n")
		end 'kind'
	end 'bad'
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
got=fetched 1 padded out long enough to heap allocate
```

### A Closure Declared Inside the Body

The closure is a lifted function with its own parse context. The routing stack is set aside for it and
restored afterwards, so `step()` on the following line still routes to the handler. Without the restore
the body would parse with an empty stack: the throwing call would stop routing and the body's `end`
would panic.

<!-- test: closure-in-try-block-body -->
```maxon
typealias Code = int(0 to 125)

enum BumpError implements Error
	tooBig
end 'BumpError'

function step(k Code) returns Code throws BumpError
	if k < 1 'bad'
		throw BumpError.tooBig
	end 'bad'
	return k
end 'step'

function run(k Code) returns ExitCode
	try 'work'
		let double = function(n Code) gives n + n
		print("closure={double(3)}\n")
		let v = step(k)
		print("v={v}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			tooBig then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```exitcode
0
```
```stdout
closure=6
caught
```

### The Handler Moves an Outer Binding and Returns, Body Path Live

`a` is declared before the block and moved into the handler-local `u`, and the handler then returns. Driven down the
OK path, `a` is never moved and must be dropped once when `run` exits. Before the merge reconciled the
move state, the handler's move persisted onto the body's continuation, the function-end drop was skipped
and `a` leaked (exit 101).

<!-- test: handler-move-return-body-live -->
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
	try 'work'
		let v = step(k)
		print("v={v}\n")
	end 'work' otherwise (e) 'bad'
		let u = a
		match e 'kind'
			bad then print("handed={u}\n")
		end 'kind'
		return 2
	end 'bad'
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

### The Handler Moves an Outer Binding and Returns, Driven Down the ERROR Path

The handler's own path: `a` is moved into `u`, which drops it at the handler's exit. It must not ALSO be
dropped by `run` — the move is real on this path.

<!-- test: handler-move-return-error-path -->
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
	try 'work'
		let v = step(k)
		print("v={v}\n")
	end 'work' otherwise (e) 'bad'
		let u = a
		match e 'kind'
			bad then print("handed={u}\n")
		end 'kind'
		return 2
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```exitcode
2
```
```stdout
handed=built value 1 padded out long enough to heap allocate
```

### The Handler Moves an Outer Binding and FALLS THROUGH, Both Edges Live

Both edges reach the merge: the handler moved `a` and the body left it live. Its drop must be placed on
the LIVE edge and the binding marked uniformly moved past the merge, so the function-end drop is right on
both paths. Driven down the OK path, where `a` was never moved.

<!-- test: handler-move-fallthrough-both-live -->
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
	try 'work'
		let v = step(k)
		print("v={v}\n")
	end 'work' otherwise (e) 'bad'
		let u = a
		match e 'kind'
			bad then print("handed={u}\n")
		end 'kind'
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
v=1
done
```

### The Body Moves an Outer Binding AFTER the Routed Call, Error Path Live

`a` is moved into `u` after the only routed call, so the throw edge leaves the body with `a` still owned
while the body's continuation has given it away. Driven down the ERROR path, `a` must be dropped exactly
once. Both edges are live here (the handler falls through), so this is the two-edge reconciliation.

<!-- test: body-move-after-routed-call-error-live -->
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
	try 'work'
		let v = step(k)
		let u = a
		print("moved={u} after v={v}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			bad then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```exitcode
0
```
```stdout
caught
```

### The Same Body Move, Driven Down the OK Path

The move ran on this path, so `a` belongs to `u` and is dropped once as `u` at the block's `end` — the
merge marks it uniformly moved rather than dropping it a second time (which is a double free, not a
leak).

<!-- test: body-move-after-routed-call-ok-path -->
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
	try 'work'
		let v = step(k)
		let u = a
		print("moved={u} after v={v}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			bad then print("caught\n")
		end 'kind'
	end 'bad'
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
moved=built value 1 padded out long enough to heap allocate after v=1
```

### A Move BEFORE the Only Routed Call Is Not a Conflict

The move happens on every path that reaches the throw edge, so all edges agree and the handler is entered
with `a` already given away — no reconciliation and no refusal. This pins that the refusal below keys on
DISAGREEMENT between throw edges rather than on the mere presence of an outer move.

<!-- test: body-move-before-routed-call -->
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
	try 'work'
		let u = a
		print("moved={u}\n")
		let v = step(k)
		print("v={v}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			bad then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```exitcode
0
```
```stdout
moved=built value 1 padded out long enough to heap allocate
caught
```

### Moving an Outer Value BETWEEN Two Routed Calls Is Rejected

`a` is live when the first routed call's throw edge is taken and already moved into `u` when the second's
is. One shared handler cannot be right for both, and the per-edge drop that would settle it has nowhere to
go on a throw edge that owes nothing else. Refused at the block rather than leaked or double-freed.

<!-- test: outer-move-between-routed-calls-rejected -->
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
	try 'work'
		let v = step(k)
		let u = a
		print(u)
		let w = step(v)
		print("w={w}\n")
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			bad then print("caught\n")
		end 'kind'
	end 'bad'
	return 0
end 'run'

function main() returns ExitCode
	return run(0)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:2: Unsupported: a block-form `try` whose body MOVES a value declared outside the block (into a field, a union payload, a consuming call or a `return`) BETWEEN two throwing calls — the block's throw edges share one handler, and they would reach it with that value still owned on the earlier edges and already given away on the later ones, so its drop belongs on some throw edges and not others. Move the value before the `try` block or after it, or give the two throwing calls their own `try` blocks
```

### A Throw From the Middle of a Block Body Releases Exactly the Owners Bound Before It

Seven `fetch` results are bound in one block body and the call that fails is each index in turn, so the
routed throw leaves with zero to six owners live; an owner bound before the block is not released by
the block's throw edge but at the function's exit, and the carried `log` and `steps` reach the handler
with the values the failing call left them.

<!-- test: routed-throw-releases-every-owner-bound-so-far -->
```maxon
typealias Index = int(0 to 255)

enum Refusal implements Error
	refused
end 'Refusal'

function fetch(i Index, stop Index) returns String throws Refusal
	if i == stop 'refuse'
		throw Refusal.refused
	end 'refuse'

	return "fetched {i} padded out long enough to heap allocate"
end 'fetch'

function run(stop Index) returns String
	let outer = fetchOuter(stop)
	var log = "start"
	var steps = 0 as Index

	try 'work'
		let s0 = fetch(0, stop: stop)
		log = "{log},{s0.byteLength()}"
		steps = steps + 1
		let s1 = fetch(1, stop: stop)
		log = "{log},{s1.byteLength()}"
		steps = steps + 1
		let s2 = fetch(2, stop: stop)
		log = "{log},{s2.byteLength()}"
		steps = steps + 1
		let s3 = fetch(3, stop: stop)
		log = "{log},{s3.byteLength()}"
		steps = steps + 1
		let s4 = fetch(4, stop: stop)
		log = "{log},{s4.byteLength()}"
		steps = steps + 1
		let s5 = fetch(5, stop: stop)
		log = "{log},{s5.byteLength()}"
		steps = steps + 1
		let s6 = fetch(6, stop: stop)
		log = "{log},{s6.byteLength()}"
		steps = steps + 1
	end 'work' otherwise (e) 'bad'
		match e 'kind'
			refused then log = "{log} refused"
		end 'kind'
	end 'bad'

	return "{outer} {log} {steps}"
end 'run'

function fetchOuter(stop Index) returns String
	return "outer {stop} padded out long enough to heap allocate"
end 'fetchOuter'

function main() returns ExitCode
	for k in 0 to 8 'each'
		print("{run(k)}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
outer 0 padded out long enough to heap allocate start refused 0
outer 1 padded out long enough to heap allocate start,49 refused 1
outer 2 padded out long enough to heap allocate start,49,49 refused 2
outer 3 padded out long enough to heap allocate start,49,49,49 refused 3
outer 4 padded out long enough to heap allocate start,49,49,49,49 refused 4
outer 5 padded out long enough to heap allocate start,49,49,49,49,49 refused 5
outer 6 padded out long enough to heap allocate start,49,49,49,49,49,49 refused 6
outer 7 padded out long enough to heap allocate start,49,49,49,49,49,49,49 7
outer 8 padded out long enough to heap allocate start,49,49,49,49,49,49,49 7
```

### A Routed `throw` of a Bound Box Releases the Owners Around It and Not the Box

A boxed error bound in the middle of the body is thrown while owners bound before and after it are
live. The box is in flight to the handler, so the throw edge drops every owner except it; the
handler adopts the box once.

<!-- test: routed-throw-of-a-bound-box-releases-the-owners-around-it -->
```maxon
typealias Index = int(0 to 255)

union Failure implements Error
	detail(message String)
	code(value Index)
end 'Failure'

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function run(k Index) returns String
	var log = "start"

	try 'work'
		let a = build(1)
		let err = Failure.detail("{a} thrown after {k} padded out long enough to heap allocate")
		let b = build(2)
		let c = build(3)

		if k == 1 'early'
			throw err
		end 'early'

		let d = build(4)
		let e = build(5)
		let g = build(6)

		if k == 2 'late'
			throw err
		end 'late'

		log = "{log} {b.byteLength()} {c.byteLength()} {d.byteLength()} {e.byteLength()} {g.byteLength()}"
	end 'work' otherwise (caught) 'bad'
		match caught 'kind'
			detail(message) then log = "{log} detail {message}"
			code(value) then log = "{log} code {value}"
		end 'kind'
	end 'bad'

	return log
end 'run'

function main() returns ExitCode
	for k in 0 to 3 'each'
		print("{run(k)}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
start 47 47 47 47 47
start detail owner 1 padded out long enough to heap allocate thrown after 1 padded out long enough to heap allocate
start detail owner 1 padded out long enough to heap allocate thrown after 2 padded out long enough to heap allocate
start 47 47 47 47 47
```

### A Carried Interface Value Reaches the Handler Through the Owners' Drops

An interface-typed `var` reassigned inside the body is carried to the handler as a value and its
witness while the throw edge releases the owners bound so far. The handler dispatches on the
value the failing call left it.

<!-- test: routed-throw-carries-an-interface-value-past-the-owners -->
```maxon
typealias Index = int(0 to 255)

enum Refusal implements Error
	refused
end 'Refusal'

interface Voice
	function say() returns Index
end 'Voice'

type Low implements Voice
	function say() returns Index
		return 1
	end 'say'

	static function create() returns Self
		return Self{}
	end 'create'
end 'Low'

type High implements Voice
	function say() returns Index
		return 2
	end 'say'

	static function create() returns Self
		return Self{}
	end 'create'
end 'High'

function fetch(i Index, stop Index) returns String throws Refusal
	if i == stop 'refuse'
		throw Refusal.refused
	end 'refuse'

	return "fetched {i} padded out long enough to heap allocate"
end 'fetch'

function run(stop Index, first Voice, second Voice) returns String
	var current = first
	var seen = 0 as Index

	try 'work'
		let a = fetch(0, stop: stop)
		let b = fetch(1, stop: stop)
		current = second
		let c = fetch(2, stop: stop)
		let d = fetch(3, stop: stop)
		let e = fetch(4, stop: stop)
		seen = (a.byteLength() + b.byteLength() + c.byteLength() + d.byteLength() + e.byteLength()) as Index
	end 'work' otherwise (caught) 'bad'
		match caught 'kind'
			refused then seen = seen + 1
		end 'kind'
	end 'bad'

	return "{current.say()}-{seen}"
end 'run'

function main() returns ExitCode
	for stop in 0 to 6 'each'
		print("{stop}: {run(stop, first: Low.create(), second: High.create())}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: 1-1
1: 1-1
2: 2-1
3: 2-1
4: 2-1
5: 2-245
6: 2-245
```

### A Block Routing Two Error Types Releases Its Owners Through Shared Drops

The body binds owners between calls that throw two different error enums, so the handler's `match` dispatches
on one fused value and the drops shared by every throw edge carry that value along. Each failing call is driven
in turn, with the owners bound before it released and the ones after it never built.

<!-- test: routed-throw-of-two-error-types-through-shared-drops -->
```maxon
typealias Index = int(0 to 255)

enum NetError implements Error
	timeout
	refused
end 'NetError'

enum DiskError implements Error
	missing
	full
end 'DiskError'

function build(i Index) returns String
	return "owner {i} padded out long enough to heap allocate"
end 'build'

function net(i Index, stop Index) returns String throws NetError
	if i == stop 'timedOut'
		throw NetError.timeout
	end 'timedOut'

	if i == stop + 10 'turnedAway'
		throw NetError.refused
	end 'turnedAway'

	return build(i + 100)
end 'net'

function disk(i Index, stop Index) returns String throws DiskError
	if i == stop 'isFull'
		throw DiskError.full
	end 'isFull'

	if i == stop + 10 'isGone'
		throw DiskError.missing
	end 'isGone'

	return build(i + 150)
end 'disk'

function run(k Index) returns String
	var log = "start"

	try 'work'
		let a = build(1)
		let b = net(1, stop: k)
		let c = build(2)
		let d = disk(2, stop: k)
		let e = build(3)
		let f = net(3, stop: k)
		let g = disk(4, stop: k)
		let h = net(5, stop: k)
		log = "{log} {a.byteLength() + b.byteLength() + c.byteLength() + d.byteLength() + e.byteLength() + f.byteLength() + g.byteLength() + h.byteLength()}"
	end 'work' otherwise (caught) 'bad'
		match caught 'kind'
			NetError.timeout then log = "{log} net timeout"
			NetError.refused then log = "{log} net refused"
			DiskError.missing then log = "{log} disk missing"
			DiskError.full then log = "{log} disk full"
		end 'kind'
	end 'bad'

	return log
end 'run'

function main() returns ExitCode
	for k in 0 to 7 'each'
		print("{k}: {run(k)}\n")
	end 'each'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
0: start 386
1: start net timeout
2: start disk full
3: start net timeout
4: start disk full
5: start net timeout
6: start 386
7: start 386
```
