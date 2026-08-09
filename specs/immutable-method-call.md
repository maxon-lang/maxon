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
error E3019: specs/fragments/immutable-method-call/push-on-let-array-error.test:8:6: cannot pass 'arr' to function that mutates parameter 'self' (in main)
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
error E3019: specs/fragments/immutable-method-call/append-on-let-string-error.test:5:4: cannot pass 's' to function that mutates parameter 'self' (in main)
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
error E3019: specs/fragments/immutable-method-call/set-on-let-array-error.test:8:10: cannot pass 'arr' to function that mutates parameter 'self' (in main)
```

<!-- test: pass-method-call-result-to-mutating-param-ok -->
`g.toUpper()` hands back a FRESH `String`, not a view of `g`. The `let` governs the binding `g`,
and a call's result is not that binding — so a function that writes its parameter may have it.

```maxon
typealias Integer = int(i64.min to i64.max)

function grow(s String) returns Integer
	s.append("XY")
	return s.byteLength()
end 'grow'

function main() returns ExitCode
	let g = "hello"
	return grow(g.toUpper()) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: pass-let-string-through-inline-if-error -->
⭐ **A LAUNDER IS NOT A LOOPHOLE.** `grow(g)` on a `let` is refused, so `grow(g if flag else g)` —
the SAME binding, reached through a merge that copies nothing — must be refused too.

⚠ **THIS IS A MEMORY-SAFETY RULE, NOT A MESSAGE ONE.** Accepted, the program writes into the
literal's read-only `.rdata` record. It is pinned here because the rule that refuses it is the
same one that must NOT refuse `g.toUpper()` above: a merge yields a view of its arms, a call
yields the callee's own value, and only the second is nobody's place.

```maxon
function grow(s String)
	s.append("XY")
end 'grow'

function main() returns ExitCode
	let flag = true
	let g = "hello"
	grow(g if flag else g)
	return 0
end 'main'
```
```maxoncstderr
error E3019: specs/fragments/immutable-method-call/pass-let-string-through-inline-if-error.test:9:2: cannot pass immutable 'let' variable to function that mutates parameter 's' (in main)
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
