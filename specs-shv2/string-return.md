---
feature: string-return
status: experimental
keywords: [string, ownership, return, drop, leak]
category: strings
---

# Returning an Owned String

## Documentation

A function declared `returns String` hands back a uniformly **owned** heap `String`.
The callee moves that ownership out — it does not drop the returned box — and the
**caller** becomes the owner and is responsible for the single drop. A bound result
(`let s = build(5)`) drops at the caller's scope exit; an unbound result
(`print(build(5))`, or a bare-statement call) drops at the end of the consuming
statement.

Because the callee always returns something owned, a return of a borrowed immortal
literal (`return "hi"`) is promoted to a fresh owned heap copy, so the caller's
unconditional drop is always sound and never touches read-only data.

```maxon
function build(x Integer) returns String
	return "val {x}"
end 'build'
typealias Integer = int(i64.min to i64.max)
```

## Tests

### Owned Interpolation Result, Bound

<!-- test: owned-interp-bound -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "val {x}"
end 'build'

function main() returns ExitCode
	let s = build(5)
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
val 5
```

### Owned Interpolation Result, Unbound

<!-- test: owned-interp-unbound -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "val {x}"
end 'build'

function main() returns ExitCode
	print(build(5))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
val 5
```

### Return an Owned Binding

<!-- test: owned-binding -->
```maxon
typealias Integer = int(i64.min to i64.max)

function f(x Integer) returns String
	let t = "n{x}"
	return t
end 'f'

function main() returns ExitCode
	let s = f(3)
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
n3
```

### Return a Borrowed Literal (Promoted to Owned)

<!-- test: borrowed-literal-promoted -->
```maxon
function g() returns String
	return "hi"
end 'g'

function main() returns ExitCode
	let s = g()
	print(s)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hi
```

### Returned String Consumed Repeatedly in a Loop

<!-- test: owned-return-in-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function build(x Integer) returns String
	return "v{x}"
end 'build'

function main() returns ExitCode
	var i = 0
	while i < 3 'loop'
		print(build(i))
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

### Owned Temporary Passed as an Argument in a Return

An interpolation built inside a `return` expression, and handed to a call rather than
returned, is an owned temporary that must be dropped before the `ret` — the caller of
`wrap` keeps ownership of the argument it borrowed. A `ret` terminates the block, so the
drop lands in the return statement itself, not at the (skipped) statement-end drain.

<!-- test: owned-temp-arg-in-return -->
```maxon
typealias Integer = int(i64.min to i64.max)

function wrap(s String) returns String
	return s
end 'wrap'

function outer(x Integer) returns String
	return wrap("v{x}")
end 'outer'

function main() returns ExitCode
	let r = outer(5)
	print(r)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
v5
```
