---
feature: immutable-method-call
status: stable
keywords: immutable, let, method, mutation
category: semantics
---
# Immutable Method Call

## Documentation

Calling a mutating method on an immutable (`let`) variable is a compile-time error. Mutating methods include `push`, `pop`, `set`, `remove`, `clear`, `resize`, `reserve`, `append`, and similar operations that modify the receiver.

## Tests

<!-- test: push-on-let-array-error -->
Calling `push` on a `let` array should produce a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
	arr.push(42)
	return 0
end 'main'
```
```maxoncstderr
error E3063: specs/fragments/immutable-method-call/push-on-let-array-error.test:8:6: cannot pass 'arr' to function that mutates parameter 'self' (in main)
```

<!-- test: append-on-let-string-error -->
Calling `append` on a `let` string should produce a compile-time error.

```maxon

function main() returns ExitCode
	let s = "hello"
	s.append(" world")
	return 0
end 'main'
```
```maxoncstderr
error E3063: specs/fragments/immutable-method-call/append-on-let-string-error.test:5:4: cannot pass 's' to function that mutates parameter 'self' (in main)
```

<!-- test: set-on-let-array-error -->
Calling `set` on a `let` array should produce a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
	try arr.set(0, value: 99) otherwise panic("test invariant: set OOB")
	return 0
end 'main'
```
```maxoncstderr
error E3063: specs/fragments/immutable-method-call/set-on-let-array-error.test:8:10: cannot pass 'arr' to function that mutates parameter 'self' (in main)
```

<!-- test: read-on-let-array-ok -->
Reading from a `let` array (non-mutating methods) should work fine.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let arr = IntArray.create()
	let n = arr.count()
	return n
end 'main'
```
```exitcode
0
```

<!-- test: push-on-var-array-ok -->
Calling `push` on a `var` array should work fine.

```maxon

typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(42)
	let x = try arr.get(0) otherwise 0
	return x
end 'main'
```
```exitcode
42
```
