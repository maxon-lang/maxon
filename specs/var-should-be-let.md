---
feature: var-should-be-let
status: stable
keywords: [variables, var, let, diagnostics, errors, mutability]
category: diagnostics
---

# Var Should Be Let Detection

## Documentation

Maxon requires that `var` declarations are actually mutated. If a variable is declared with `var` but never reassigned, it should be declared with `let` instead.

### Example Error

```maxon
function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/docs-example-1.test:3:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

## Tests

<!-- test: var-never-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/var-never-reassigned.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: var-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	x = 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: let-no-error -->
```maxon

function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: var-reassigned-in-if -->
```maxon

function main() returns ExitCode
	var x = 0
	if x == 0 'check'
		x = 42
	end 'check'
	return x
end 'main'
```
```exitcode
42
```

<!-- test: multiple-var-first-reported -->
```maxon

function main() returns ExitCode
	var x = 1
	var y = 2
	return x + y
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/multiple-var-first-reported.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: unused-takes-precedence -->
```maxon

function main() returns ExitCode
	var x = 10
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/var-should-be-let/unused-takes-precedence.test:4:6: unused variable: 'x'
```
