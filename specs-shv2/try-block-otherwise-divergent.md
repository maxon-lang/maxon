---
feature: try-block-otherwise-divergent
status: experimental
keywords: [try, otherwise, panic, throws, block, divergent]
category: error-handling
---

# Try Block — Terminal `otherwise panic` / `otherwise throws`

## Documentation

The multi-call `try 'label' ... end 'label'` block accepts three forms of `otherwise`:

1. `otherwise (binding) 'handler' ... end 'handler'` — block handler that must contain a `match` on the binding (existing form).
2. `otherwise [(binding)] panic("message")` — terminate the program when the body throws.
3. `otherwise [(binding)] throws ErrorType.case` — re-throw a fixed error to the caller. The enclosing function must declare `throws ErrorType`.

The optional `(binding)` in forms 2 and 3 declares the original error as a typed enum (or synthesized error union, if the body throws multiple distinct error types). When bound, the binding may be referenced inside the panic message's interpolation or inside the throw expression — for example, to wrap the original error as a payload of the new error case. If the binding is declared but never read, the standard unused-variable check (E3012) rejects it.

```maxon
try 'reading'
    parseFile("data.json")
end 'reading'
otherwise panic("unreachable: data.json is bundled")
```

```maxon
try 'reading'
    parseFile("data.json")
end 'reading'
otherwise throws AppError.parseFailed
```

```maxon
try 'reading'
    parseFile("data.json")
end 'reading'
otherwise (e) throws AppError.wrap(e)
```

## Tests

<!-- test: otherwise-panic.not-hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum PanicNotHitError implements Error
	bad
end 'PanicNotHitError'

function maybeFail(b bool) returns Integer throws PanicNotHitError
	if b 'c'
		throw PanicNotHitError.bad
	end 'c'
	return 7
end 'maybeFail'

function main() returns ExitCode
	var total = 0
	try 'work'
		let a = maybeFail(false)
		let b = maybeFail(false)
		total = a + b
	end 'work'
	otherwise panic("should not happen")
	return total
end 'main'
```
```exitcode
14
```

<!-- test: otherwise-panic.hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum PanicHitError implements Error
	bad
end 'PanicHitError'

function maybeFail(b bool) returns Integer throws PanicHitError
	if b 'c'
		throw PanicHitError.bad
	end 'c'
	return 7
end 'maybeFail'

function main() returns ExitCode
	var total = 0
	try 'work'
		let a = maybeFail(true)
		total = a
	end 'work'
	otherwise panic("call failed")
	return total
end 'main'
```
```exitcode
1
```
```stderr
panic at otherwise-panic.hit.test:21: call failed
Stack trace:
  in main
  in mrt_start
```

<!-- test: otherwise-throws.not-hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum ThrowsNotHitInner implements Error
	bad
end 'ThrowsNotHitInner'

enum ThrowsNotHitOuter implements Error
	failed
end 'ThrowsNotHitOuter'

function maybeFail(b bool) returns Integer throws ThrowsNotHitInner
	if b 'c'
		throw ThrowsNotHitInner.bad
	end 'c'
	return 5
end 'maybeFail'

function compute() returns Integer throws ThrowsNotHitOuter
	var total = 0
	try 'work'
		let a = maybeFail(false)
		total = a
	end 'work'
	otherwise throws ThrowsNotHitOuter.failed
	return total
end 'compute'

function main() returns ExitCode
	let r = try compute() otherwise 99
	return r
end 'main'
```
```exitcode
5
```

<!-- test: otherwise-throws.hit -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum ThrowsHitInner implements Error
	bad
end 'ThrowsHitInner'

enum ThrowsHitOuter implements Error
	failed
end 'ThrowsHitOuter'

function maybeFail(b bool) returns Integer throws ThrowsHitInner
	if b 'c'
		throw ThrowsHitInner.bad
	end 'c'
	return 5
end 'maybeFail'

function compute() returns Integer throws ThrowsHitOuter
	var total = 0
	try 'work'
		let a = maybeFail(true)
		total = a
	end 'work'
	otherwise throws ThrowsHitOuter.failed
	return total
end 'compute'

function main() returns ExitCode
	let r = try compute() otherwise 99
	return r
end 'main'
```
```exitcode
99
```

<!-- test: otherwise-binding-throws.wraps-error -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum BindingWrapInner implements Error
	bad
end 'BindingWrapInner'

union BindingWrapOuter implements Error
	wrap(inner BindingWrapInner)
end 'BindingWrapOuter'

function maybeFail(b bool) returns Integer throws BindingWrapInner
	if b 'c'
		throw BindingWrapInner.bad
	end 'c'
	return 5
end 'maybeFail'

function compute() returns Integer throws BindingWrapOuter
	var total = 0
	try 'work'
		let a = maybeFail(true)
		total = a
	end 'work'
	otherwise (e) throws BindingWrapOuter.wrap(e)
	return total
end 'compute'

function main() returns ExitCode
	let r = try compute() otherwise 77
	return r
end 'main'
```
```exitcode
77
```

<!-- test: error.unused-binding-panic -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum UnusedBindingError implements Error
	bad
end 'UnusedBindingError'

function maybeFail(b bool) returns Integer throws UnusedBindingError
	if b 'c'
		throw UnusedBindingError.bad
	end 'c'
	return 7
end 'maybeFail'

function main() returns ExitCode
	var total = 0
	try 'work'
		let a = maybeFail(true)
		total = a
	end 'work'
	otherwise (e) panic("never read e")
	return total
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/try-block-otherwise-divergent/error.unused-binding-panic.test:21:13: unused variable: 'e'
```
