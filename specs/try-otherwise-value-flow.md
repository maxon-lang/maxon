---
feature: try-otherwise-value-flow
status: stable
keywords: [try, otherwise, regalloc, codegen, ssa, block-args]
category: error-handling
---

# Try-Otherwise Value Flow

## Documentation

`try CALL otherwise FALLBACK` lowers to a conditional branch on the call's
error flag: the success path uses the call's result, the fallback path
substitutes `FALLBACK`. The parser emits `cmp ne, errorFlag, 0` followed by
`condBr cond, then=fallbackBlock, else=successBlock` — i.e. the success
path is on the **else** edge of the conditional branch.

These tests pin down value flow through that lowering shape end-to-end.
A regression here typically points at the SSA-destruction / edge-copy
machinery in the register allocator: the call result must reach the merge
block via a parallel copy on the success edge, even when that edge ends up
emitted as a conditional jump's target after layout fall-through elimination.

## Tests

<!-- test: try-otherwise-value-flow.success-path-returns-call-result -->
The call returns `10` and does not throw. `try` evaluates to the call's
result, not the fallback. Stresses the success edge of `condBr` (which is
the `else` edge) — the call result must flow through to the merge block.
```maxon
enum E
	bad
end 'E'

function double(x ExitCode) returns ExitCode throws E
	return x * 2
end 'double'

function main() returns ExitCode
	let v = try double(5) otherwise 99
	return v
end 'main'
```
```exitcode
10
```

<!-- test: try-otherwise-value-flow.fallback-path-returns-otherwise-value -->
The call throws, so `try` evaluates to the fallback `99` instead of the
call's (default) primary value. Stresses the error edge of `condBr`.
```maxon
enum E
	bad
end 'E'

function alwaysThrows(x ExitCode) returns ExitCode throws E
	if x >= 0 'always'
		throw E.bad
	end 'always'
	return x
end 'alwaysThrows'

function main() returns ExitCode
	let v = try alwaysThrows(5) otherwise 99
	return v
end 'main'
```
```exitcode
99
```

<!-- test: try-otherwise-value-flow.identity-call-success -->
Identity call result reaches the merge block — confirms the value flow is
not specific to a multiplication or any particular arithmetic expression.
```maxon
enum E
	bad
end 'E'

function ident(x ExitCode) returns ExitCode throws E
	return x
end 'ident'

function main() returns ExitCode
	let v = try ident(7) otherwise 99
	return v
end 'main'
```
```exitcode
7
```

<!-- test: try-otherwise-value-flow.propagation-success -->
Propagation form: a throwing helper wraps `try CALL` without an `otherwise`.
A successful inner call returns its value; the wrapper then appears in the
outer `try ... otherwise` site. Exercises the propagation lowering shape
(error path re-publishes the flag and returns a default) on the success
branch.
```maxon
enum E
	bad
end 'E'

function double(x ExitCode) returns ExitCode throws E
	return x * 2
end 'double'

function wrap() returns ExitCode throws E
	let v = try double(5)
	return v
end 'wrap'

function main() returns ExitCode
	let v = try wrap() otherwise 99
	return v
end 'main'
```
```exitcode
10
```

<!-- test: try-otherwise-value-flow.nested-try-in-arg -->
Nested `try ... otherwise X` in an argument position: the inner try's
result must be visible to the outer call's argument list. Reproduces the
`unresolved value name '$tN'` parser binding bug.
```maxon
enum E
	bad
end 'E'

function getString(i ExitCode) returns ExitCode throws E
	if i == 0 'zero'
		throw E.bad
	end 'zero'
	return 42
end 'getString'

function consume(x ExitCode) returns ExitCode throws E
	if x == 0 'empty'
		throw E.bad
	end 'empty'
	return x
end 'consume'

function main() returns ExitCode
	let n = try consume(try getString(1) otherwise 0) otherwise 0
	return n
end 'main'
```
```exitcode
42
```
