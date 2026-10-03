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

### A wrong argument count

A stdlib call with the wrong number of arguments is **E3036** (`callArgCountMismatch`), an ARITY rule: the
sentence states the declared and supplied counts, the same one every other call reports
(`'Math.pow' expects 2 argument(s) but 1 were provided`). It is anchored at the callee's member name, not at
the absent argument, and there is no separate named-parameter sentence for it.

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

