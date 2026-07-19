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
statement. The tests needing function calls + unreachable-code detection
(`dead-code-after-return`, which is E3071) or function parameters + calls
(`return-in-if-then-reachable`) are DEFERRED and recorded under `## Deferred`
below.

<!-- test: simple-return -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: expression-return -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	return 2 + 3 * 4
end 'main'
```
```exitcode
14
```

<!-- test: conditional-return -->
<!-- targets: wasm32-wasi -->
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
<!-- targets: wasm32-wasi -->
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
<!-- targets: wasm32-wasi -->
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


## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live
in this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which
scans only `## Tests`, up to the next `## ` heading) never extracts them, and
they carry NO `<!-- test: … -->` marker. To re-enable: move the test up into
`## Tests` and prefix it with its `<!-- test: NAME -->` marker.
<!-- targets: wasm32-wasi -->

### dead-code-after-return

Re-enable once its prerequisites land: function calls AND unreachable-code
detection after a `return` (E3071), which arrives with the reachability pass.

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

