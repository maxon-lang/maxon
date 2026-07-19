---
feature: variables
status: selfhosted
keywords: [variables, let, var]
category: declaration
milestone: M2
---

## Documentation

Maxon has two kinds of variable declaration:

- `let` — an immutable binding
- `var` — a mutable binding

At M2 both bind an identifier to the value of its initializer expression. A
reference to the name resolves to that value, so `let x = 42; return x` behaves
exactly like `return 42`. Explicit type annotations are never written — the type
is always inferred — so a `:` after the name is a parse error (E2010).

Expressions at M2 are integer literals, variable references, and a
left-associative binary `+`. (The full operator set, comparison, and precedence
arrive at M3.)

## Tests

These are the M2 slice of `specs/variables.md`: `let`/`var` bindings, variable
references, and `+`. The `top-level-string-constant` case from
`specs/variables.md` is DEFERRED — it needs string literals (M10), `if` (M4),
and `==` (M3) — and is recorded under `## Deferred` below so it is re-enabled at
those milestones rather than forgotten.

<!-- test: let-declaration -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let x = 42
	return x
end 'main'
```
```exitcode
42
```

<!-- test: var-declaration -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: multiple-variables -->
<!-- targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let a = 10
	let b = 20
	return a + b
end 'main'
```
```exitcode
30
```

<!-- test: var-explicit-type-error -->
<!-- targets: wasm32-wasi -->
Explicit type annotations are not allowed on var declarations.
```maxon
function main() returns ExitCode
	let x: int = 0
	return x
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
```

<!-- test: let-explicit-type-error -->
<!-- targets: wasm32-wasi -->
Explicit type annotations are not allowed on let declarations.
```maxon
function main() returns ExitCode
	let x: int = 0
	return x
end 'main'
```
```maxoncstderr
error E2010: <fragment>:3:7: Expected '=' but got ':'
```

## Deferred

Tests recorded for re-enablement at the milestone that unblocks them. They live
in this `## Deferred` section — NOT `## Tests` — so the spec-test parser (which
scans only `## Tests`, up to the next `## ` heading) never extracts them, and
they carry NO `<!-- test: … -->` marker. To re-enable: move the test up into
`## Tests` and prefix it with its `<!-- test: NAME -->` marker.
<!-- targets: wasm32-wasi -->

### top-level-string-constant

Re-enable once its prerequisites land: string literals (M10), `if` (M4), and
`==` (M3).

```maxon
let GREETING = "hello"

function main() returns ExitCode
	if GREETING == "hello" 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```
