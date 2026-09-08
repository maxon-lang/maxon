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
Both are load-bearing rather than stylistic, and each was measured: a BARE String LITERAL is a static
constant that owns no heap, so a case using one is dropped by nobody and leaks nothing whatever the move
state says; and a bind from an IMMUTABLE binding is an ALIAS rather than a move
(`conditional-move-drops.md` states the second rule for its own corpus). With `let s = "…"` these programs
pass every expectation below while testing none of the subject.

## Tests

### A Routed Call Whose Result Is MANAGED

`fetch` returns an owned `String` and throws. The call is bare inside the block body, so it is routed —
and its result is a managed temporary the statement already owes a drop for. The throw edge must not
release it: on that edge the result register was never written. Before this was fixed the landing pad
decref'd it and the program died with an access violation instead of reaching the handler.

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
the body parsed with an empty stack: the throwing call stopped routing and the body's `end` panicked.

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
