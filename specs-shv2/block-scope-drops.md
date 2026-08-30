---
feature: block-scope-drops
status: experimental
keywords: [ownership, drop, block, loop, break, continue, leak]
category: memory
---

# Block-Scope Ownership Drops

## Documentation

An owned heap value (a `String`, a struct box) bound inside an `if` or `while` body
is owned by that block and must be released when control leaves it, on every edge
that leaves the block alive:

- **fall-through** — the binding drops at the block's `end`, so a loop body releases
  its per-iteration bindings each time round;
- **`break` / `continue`** — the jump skips past the block's `end`, so the drop is
  emitted before the branch, releasing everything declared since the target loop's
  body was entered;
- **`return`** — the whole in-scope set is released before the terminator, and a
  binding that is itself returned is moved out first (so it is not double-freed).

Outer bindings are untouched: each drops when its own block closes.

```maxon
while more() 'loop'
	let line = readLine()   // owned; dropped at the loop body's end each iteration
	print(line)
end 'loop'
```

## Tests

### Owned Binding in a Loop Body

<!-- test: owned-binding-in-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		let s = build(i)
		print(s)
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v0v1v2
```

### Owned Binding in an If Body

<!-- test: owned-binding-in-if -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	let flag = 1
	if flag > 0 'b'
		let s = build(flag)
		print(s)
	end 'b'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1
```

### Owned Binding Released on Break

<!-- test: owned-binding-with-break -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var i = 0
	while i < 100 'loop'
		let s = build(i)
		print(s)
		i = i + 1
		if i > 3 'stop'
			break
		end 'stop'
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v0v1v2v3
```

### Owned Binding Released on Continue

<!-- test: owned-binding-with-continue -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		i = i + 1
		let s = build(i)
		print(s)
		continue
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v1v2v3
```

### Nested Blocks, Both Bind Owned Strings

<!-- test: nested-owned-bindings -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var i = 0
	while i < 4 'loop'
		let a = build(i)
		print(a)
		if i < 2 'b'
			let bb = build(i)
			print(bb)
		end 'b'
		i = i + 1
	end 'loop'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v0v0v1v1v2v3
```

### Returning a Block-Local Binding Frees Exactly Once

<!-- test: return-block-local-binding -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function pick(x Integer) returns String
	if x > 0 'b'
		let p = build(x)
		return p
	end 'b'
	return "neg"
end 'pick'

function main() returns ExitCode
	let s = pick(5)
	print(s)
	let t = pick(-3)
	print(t)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v5neg
```

### Returning a Loop-Local Binding Frees the Non-Returning Iterations

A `return` inside a loop hands ONE iteration's owned binding to the caller, but the binding
is declared fresh each time round: every OTHER iteration builds a copy that falls through the
loop body's `end` and must be dropped there. Moving the returned value out of the drop set must
not also strip it from that per-iteration fall-through — `firstHit(2)`, whose `return` is never
even reached, still allocates a `cand` each iteration and must free it.

<!-- test: return-loop-local-frees-iterations -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function firstHit(n Integer) returns String
	var i = 0
	while i < n 'loop'
		let cand = build(i)
		if i == 2 'hit'
			return cand
		end 'hit'
		i = i + 1
	end 'loop'
	return "none"
end 'firstHit'

function main() returns ExitCode
	print(firstHit(10))
	print(firstHit(2))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v2none
```

### Struct Bound in a Block Is Released

<!-- test: owned-struct-in-block -->
```maxon
type Box
	var v as Integer

	static function create(v Integer) returns Box
		return Self{v: v}
	end 'create'
end 'Box'

function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		if i < 2 'b'
			_ = Box.create(i)
		end 'b'
		i = i + 1
	end 'loop'
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```
```stdout
```

<!-- test: local-declared-before-a-loop-drops-once-after-it -->
A local declared BEFORE a loop drops ONCE, in the block control actually leaves through — not once
per iteration, and not only on one arm of a branch.

`scratch` outlives the `while` and is never read after it, so the function's implicit-void end owes
its release. That drop used to be emitted into `self.currentBlock`, which after a nested `if`/`while`
is that construct's BODY rather than the block the `retVoid` ends: the decref landed inside the loop
and ran once per iteration — a double free that corrupts the heap — while the loop's exit path got
none. `emitScopeDrops` now takes the destination block as a parameter, so every caller names the
block it means and there is no implicit one left to go stale.

The exit code is `sink.count()`, so a `sink` corrupted by the double free cannot answer 3.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function fill(sink ByteArray)
	var scratch = ByteArray.create()
	scratch.push('x')
	var i = 0
	while i < 3 'each'
		sink.push('y')
		i = i + 1
	end 'each'
end 'fill'

function main() returns ExitCode
	var sink = ByteArray.create()
	fill(sink)
	return sink.count()
end 'main'
```
```exitcode
3
```
