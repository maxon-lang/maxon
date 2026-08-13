---
feature: stdlib-autodiscovery
status: stable
keywords: [stdlib, autodiscovery, linking]
category: stdlib
---

# Standard Library Autodiscovery

## Documentation

The standard library is automatically discovered and linked when you use its functions.

### How It Works

When you call a stdlib function like `pow()`, the compiler:
1. Searches the `stdlib/` directory
2. Finds the function definition
3. Compiles it automatically
4. Links it into your program

No imports or includes needed!

### Example

```maxon
function main() returns ExitCode
	// pow() is automatically found in stdlib/math/
	let result = Math.pow(2.0, exponent: 3.0)
	return trunc(result)
end 'main'
```
```exitcode
8
```


### Transitive Dependencies

If a stdlib function depends on other stdlib functions, they're also discovered automatically. For example, `pow()` uses `log()` and `exp()`, which are all linked automatically.

### shv2 note on `wrong-arg-count`

That case carries shv2's own wording for **the same code**, `E3036`, and at a different column. Neither
compiler points at the absent argument — `3:20` is the member name `pow` and `3:15` is the start of the
whole qualified callee `Math.pow`, so the column moves only because shv2 includes the qualifier in the
callee's range. The difference is ratified by the registry and by specs already ported, not decided here:

- `docs/error-codes.txt` names E3036 **`SemanticWrongArgCount`** and documents it as *"a call passes a
  different number of arguments than the callee declares parameters"* — an ARITY rule. shv2's member is
  `callArgCountMismatch` and its sentence is that rule stated directly. The bootstrap folds a
  named-parameter diagnostic (*"missing argument for parameter 'exponent'"*) into the same code; shv2
  does not have a second sentence for it.
- **Eight live cases across seven already-ported specs pin shv2's spelling**, so it is the settled one
  and this file is the outlier: `functions.md` (`'add' expects 2 argument(s) but 1 were provided`),
  `method-calls.md`, `where-clauses.md` (twice), `first-class-functions.md`,
  `implicit-self-methods.md`, `parsable-interface.md`, and the two builtin-arity variants in
  `builtins-clock.md` / `builtins-sleep.md`. Not one of them anchors on an argument; which callee token
  they land on varies with the call shape (bare name in `functions.md`, whole call expression in
  `method-calls.md`, method name in `where-clauses.md`), so the pins settle the SENTENCE, and the column
  is whatever the emit site's own `callRange` already reports.

⇒ Making this file's original text pass would mean changing those eight instead, which is a behaviour
change with a blast radius far beyond this spec. The expectation moves; shv2 does not.

## Tests

<!-- test: basic-autodiscovery -->
```maxon
function main() returns ExitCode
	return trunc(sqrt(16.0))
end 'main'
```
```exitcode
4
```


<!-- test: transitive -->
```maxon
// pow -> log, exp
function main() returns ExitCode
	let result = Math.pow(2.0, exponent: 3.0)
	if result > 7.5 'check'
		return 8
	end 'check'
	return 0
end 'main'
```
```exitcode
8
```


<!-- test: unqualified-call -->
```maxon
function main() returns ExitCode
	let result = sqrt(16.0)
	return trunc(result)
end 'main'
```
```exitcode
4
```


<!-- test: qualified-call -->
```maxon
function main() returns ExitCode
	return trunc(Math.pow(2.0, exponent: 4.0))
end 'main'
```
```exitcode
16
```


<!-- test: wrong-arg-count -->
```maxon
function main() returns ExitCode
	return trunc(Math.pow(2.0))
end 'main'
```
```maxoncstderr
error E3036: specs/fragments/stdlib-autodiscovery/wrong-arg-count.test:3:20: 'Math.pow' expects 2 argument(s) but 1 were provided
```

