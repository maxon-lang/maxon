---
feature: return-statement
status: selfhosted
keywords: [return, control-flow]
category: statements
milestone: M4a
---

# Return Statement

## Documentation

`return <expression>` exits the current function, handing the value back to the
caller (in the M4a slice, `main` returns its `ExitCode` in R8). A `return` also
terminates its block: at M4a it may sit in the function body OR in an `if`/`else`
branch, and each branch that returns emits its own `ret`.

### Syntax

```maxon
return expression
```

## Tests

The M4a slice of `specs/return-statement.md`: a bare value return, an expression
return, a return inside an `if`, and a `return` of a variable as the tail
statement. Both cases once recorded under `## Deferred` are now live —
`return-in-if-then-reachable` (function parameters + calls) and
`dead-code-after-return` (E3071, W118).

<!-- test: simple-return -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: expression-return -->
```maxon
function main() returns ExitCode
	return 2 + 3 * 4
end 'main'
```
```exitcode
14
```

<!-- test: conditional-return -->
A function with two exits must have BOTH of them executed. Written as `let x = 5; if x > 3 then
return 1; return 0`, the condition is a constant the run always takes, so the tail `return 0` is
compiled and never entered — its return lowering (the R8 move, the epilogue) is checked by the
golden but never by an actual execution. Putting the branch in a helper and calling it on both
sides of the condition makes each exit an executed path, and each is checked alone so neither can
mask the other.
```maxon
function firstOver(x int) returns int
	if x > 3 'check'
		return 1
	end 'check'
	return 0
end 'firstOver'

function main() returns ExitCode
	if firstOver(5) != 1 'branchTaken'
		return 2
	end 'branchTaken'

	if firstOver(1) != 0 'branchNotTaken'
		return 3
	end 'branchNotTaken'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: tail-return-is-last -->
```maxon
function main() returns ExitCode
	let x = 7
	return x
end 'main'
```
```exitcode
7
```

<!-- test: return-in-if-then-reachable -->
```maxon
function classify(x ExitCode) returns ExitCode
	if x > 0 'positive'
		return 1
	end 'positive'
	return 0
end 'classify'

function main() returns ExitCode
	return classify(5)
end 'main'
```
```exitcode
1
```

<!-- test: dead-code-after-return -->
```maxon
function pick() returns ExitCode
	return 1
	return 2
end 'pick'

function main() returns ExitCode
	return pick()
end 'main'
```
```maxoncstderr
error E3071: <fragment>:4:2: unreachable code after 'return'
```

<!-- test: return-in-if-then-statement-after -->
The OTHER side of E3071, and the one that must keep compiling: a `return` inside an `if`
is NOT followed by dead code, because the statements after the `end` are reachable on the
false path. Written with an ordinary statement — not a second `return` — after the `if`,
so the case fails if the refusal is widened from "the same straight-line block" to "any
block that has already returned".

Both exits are executed, and the false path's answer is the one that pins the bug W118
found: before it, the early `return` in a straight-line body was DISCARDED because
`setTerminator` silently overwrote it. Here the `return` is in its own block, so nothing
overwrites it — and the trailing statement proves the continuation still runs.
```maxon
function pick(x ExitCode) returns ExitCode
	if x > 0 'positive'
		return 1
	end 'positive'
	let fallback = 4
	return fallback
end 'pick'

function main() returns ExitCode
	if pick(9) != 1 'tookFalsePath'
		return 2
	end 'tookFalsePath'

	return pick(0)
end 'main'
```
```exitcode
4
```
