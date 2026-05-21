---
feature: if-try
status: experimental
keywords: [if, try, let, else, error, binding]
category: error-handling
---

# If-Try Expressions

## Documentation

### Conditional Error Handling

The `if try` construct provides conditional execution based on whether a throwing expression succeeds or fails.

#### Boolean Form

Check if an expression succeeds without binding the result:

```maxon
if try mayFail() 'check'
	print("Operation succeeded!")
end 'check'
```

The if-block executes only if the expression succeeds (doesn't throw).

#### Binding Form

Unwrap and bind the success value:

```maxon
if let value = try mayFail() 'check'
	print("Got: {value}")
end 'check'
```

If successful, the unwrapped value is bound to `value` and available within the if-block.

#### With Else Clause

Handle the error case:

```maxon
if try mayFail() 'check'
	print("Success!")
end 'check' else 'err'
	print("Failed!")
end 'err'
```

#### With Error Binding

Capture the error value in the else block:

```maxon
if let value = try mayFail() 'check'
	print("Got: {value}")
end 'check' else (e) 'err'
	print("Error occurred")
end 'err'
```

The error is bound to `e` and available within the else-block.

## Tests

<!-- test: if-try-boolean-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(true) 'check'
		result = 1
	end 'check'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: if-try-boolean-failure -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(false) 'check'
		result = 1
	end 'check'
	return result
end 'main'
```
```exitcode
0
```

<!-- test: if-try-binding-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if let value = try mayFail(true) 'check'
		return value
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: if-try-binding-failure -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if let value = try mayFail(false) 'check'
		return value
	end 'check'
	return 99
end 'main'
```
```exitcode
99
```

<!-- test: if-try-else-block -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(false) 'check'
		result = 1
	end 'check' else 'err'
		result = 2
	end 'err'
	return result
end 'main'
```
```exitcode
2
```

<!-- test: if-try-else-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(true) 'check'
		result = 1
	end 'check' else 'err'
		result = 2
	end 'err'
	return result
end 'main'
```
```exitcode
1
```

<!-- test: if-try-binding-with-else -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if let value = try mayFail(false) 'check'
		return value
	end 'check' else 'err'
		return 77
	end 'err'
end 'main'
```
```exitcode
77
```

<!-- test: if-try-binding-with-else-success -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if let value = try mayFail(true) 'check'
		return value
	end 'check' else 'err'
		return 77
	end 'err'
end 'main'
```
```exitcode
42
```

<!-- test: if-try-var-binding-reassign -->
The `if var` form produces a mutable binding that can be reassigned inside the then-block.
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if var value = try mayFail(true) 'check'
		value = value + 10
		return value
	end 'check'
	return 0
end 'main'
```
```exitcode
52
```

<!-- test: if-try-var-binding-failure -->
The `var` keyword does not change failure dispatch — the then-block is still skipped on error.
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	if var value = try mayFail(false) 'check'
		value = value + 10
		return value
	end 'check'
	return 99
end 'main'
```
```exitcode
99
```

<!-- test: if-try-var-binding-managed-struct -->
A mutable binding to a managed type (String) can be mutated via append; the binding is cleaned up
correctly at end-of-then-block.
```maxon

enum MyError implements Error
	failed
end 'MyError'

function makeGreeting(succeed bool) returns String throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return "hello"
end 'makeGreeting'

function main() returns ExitCode
	if var s = try makeGreeting(true) 'check'
		s.append(" world")
		return s.bytes().count()
	end 'check'
	return 0
end 'main'
```
```exitcode
11
```

<!-- test: if-try-else-with-error-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	first
	second
end 'MyError'

function mayFail(which Integer) returns Integer throws MyError
	if which == 1 'check1'
		throw MyError.first
	end 'check1'
	if which == 2 'check2'
		throw MyError.second
	end 'check2'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(1) 'check'
		result = 100
	end 'check' else (e) 'err'
		result = 50
	end 'err'
	return result
end 'main'
```
```exitcode
50
```

<!-- test: if-try-nested -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'mayFail'

function main() returns ExitCode
	var result = 0
	if try mayFail(true) 'outer'
		if try mayFail(true) 'inner'
			result = 3
		end 'inner'
	end 'outer'
	return result
end 'main'
```
```exitcode
3
```

<!-- test: if-try-in-loop -->
```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail(n Integer) returns Integer throws MyError
	if n < 3 'check'
		throw MyError.failed
	end 'check'
	return n
end 'mayFail'

function main() returns ExitCode
	var sum = 0
	var i = 0
	while i < 5 'loop'
		if let val = try mayFail(i) 'check'
			sum = sum + val
		end 'check'
		i = i + 1
	end 'loop'
	return sum
end 'main'
```
```exitcode
7
```

<!-- test: error.if-try-redundant-contains-get -->
Pattern `if x.contains(k) ... try x.get(k) otherwise ...` performs two lookups when one
suffices via `if let`/`if var`. Flagged as a compile-time error to push users toward the
single-lookup form.

```maxon
typealias StrMap = Map with (String, String)

function main() returns ExitCode
	var m = StrMap.create()
	m.upsert("k", value: "v")
	let key = "k"
	if m.contains(key) 'has'
		let v = try m.get(key) otherwise panic("nope")
		print("{v}\n")
	end 'has'
	return 0
end 'main'
```
```maxoncstderr
error E3087: specs/fragments/if-try/error.if-try-redundant-contains-get.test:8:7: redundant 'Map.contains' followed by 'Map.get' on 'm': use 'if let v = try m.get(key)' (or 'if var') instead — performs one lookup instead of two
```

<!-- test: error.if-try-redundant-contains-get-field-receiver -->
The double-lookup lint matches receivers structurally, so field-access chains
(e.g. `holder.cache.contains(k)` paired with `holder.cache.get(k)`) are flagged
the same as bare-local receivers.

```maxon
typealias StrMap = Map with (String, String)

type Holder
	export var cache StrMap

	static function create() returns Self
		return Self{cache: StrMap.create()}
	end 'create'
end 'Holder'

function lookup(holder Holder, key String) returns String
	if holder.cache.contains(key) 'has'
		let v = try holder.cache.get(key) otherwise panic("nope")
		return v
	end 'has'
	return "missing"
end 'lookup'

function main() returns ExitCode
	let h = Holder.create()
	let s = lookup(h, key: "x")
	print("{s}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3087: specs/fragments/if-try/error.if-try-redundant-contains-get-field-receiver.test:13:18: redundant 'Map.contains' followed by 'Map.get' on 'holder.cache': use 'if let v = try holder.cache.get(key)' (or 'if var') instead — performs one lookup instead of two
```

<!-- test: error.if-try-non-throwing -->
Using `if try` with a non-throwing function is a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)

function noThrow() returns Integer
	return 42
end 'noThrow'

function main() returns ExitCode
	if try noThrow() 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/if-try/error.if-try-non-throwing.test:10:5: try requires a throwing function: 'if-try.noThrow' does not throw'
```

<!-- test: if-try-binding-struct-multiple-managed-fields -->
When using if-let with a struct that has multiple managed fields (like Array and String fields),
all managed fields must be properly cleaned up when the binding goes out of scope.

```maxon
enum MyError implements Error
	failed
end 'MyError'

typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type MultiManaged
	export var numbers IntArray
	export var text String
	export var tag String

	static function create(numbers IntArray, text String, tag String) returns Self
		return Self{numbers: numbers, text: text, tag: tag}
	end 'create'
end 'MultiManaged'

function mayFail(succeed bool) returns MultiManaged throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	var nums = IntArray.create()
	nums.push(10)
	nums.push(20)
	return MultiManaged.create(numbers: nums, text: "hello", tag: "world")
end 'mayFail'

function main() returns ExitCode
	var result = 0
	var i = 0
	while i < 3 'loop'
		if let item = try mayFail(true) 'check'
			result = result + (try item.numbers.get(0) otherwise 0)
		end 'check'
		i = i + 1
	end 'loop'
	return result
end 'main'
```
```exitcode
30
```

<!-- test: complex-nested-struct-cleanup -->
Test cleanup of deeply nested structs with multiple managed fields at function return.

```maxon
enum MyError implements Error
	failed
end 'MyError'

typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int
typealias StringArray = Array with String

type Inner
	export var name String
	export var values IntArray

	static function create(name String, values IntArray) returns Self
		return Self{name: name, values: values}
	end 'create'
end 'Inner'

type Outer
	export var label String
	export var inner Inner
	export var tags StringArray

	static function create(label String, inner Inner, tags StringArray) returns Self
		return Self{label: label, inner: inner, tags: tags}
	end 'create'
end 'Outer'

function createOuter() returns Outer
	var inner = Inner.create(name: "test", values: IntArray.create())
	inner.values.push(1)
	inner.values.push(2)
	var outer = Outer.create(label: "outer", inner: inner, tags: StringArray.create())
	outer.tags.push("tag1")
	outer.tags.push("tag2")
	return outer
end 'createOuter'

function main() returns ExitCode
	let outer = createOuter()
	return try outer.inner.values.get(0) otherwise 0
end 'main'
```
```exitcode
1
```

<!-- test: if-try-elseif-scope-cleanup -->
Else-if containing try where the inner scope has no block_exit (all paths return).
The else path must not segfault when cleaning up the else-if scope.

```maxon
enum MyError implements Error
	failed
end 'MyError'

typealias Int = int(i64.min to i64.max)

function mayFail() returns Int throws MyError
	throw MyError.failed
end 'mayFail'

function helper(x Int) returns Int throws MyError
	if x == 1 'case1'
		return 10
	end 'case1' else if x == 2 'case2'
		let r = try mayFail()
		return r
	end 'case2' else 'default'
		return 30
	end 'default'
end 'helper'

function main() returns ExitCode
	return try helper(3) otherwise 99
end 'main'
```
```exitcode
30
```

<!-- test: if-try-else-if-let -->
Chained `if let = try ... end 'a' else if let = try ... end 'b'`. The else
branch must accept a new `if`/`if let` as its body without requiring a label.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function tryA() returns Integer throws MyError
	throw MyError.failed
end 'tryA'

function tryB() returns Integer throws MyError
	return 42
end 'tryB'

function main() returns ExitCode
	var result = 0
	if let a = try tryA() 'a'
		result = a
	end 'a' else if let b = try tryB() 'b'
		result = b
	end 'b'
	return result
end 'main'
```
```exitcode
42
```

<!-- test: if-try-else-if-let-three-way -->
Three-way `if let / else if let / else` chain.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function tryA() returns Integer throws MyError
	throw MyError.failed
end 'tryA'

function tryB() returns Integer throws MyError
	throw MyError.failed
end 'tryB'

function main() returns ExitCode
	var result = 0
	if let a = try tryA() 'a'
		result = a
	end 'a' else if let b = try tryB() 'b'
		result = b
	end 'b' else 'fallback'
		result = 7
	end 'fallback'
	return result
end 'main'
```
```exitcode
7
```

<!-- test: if-try-else-if-plain-after-binding -->
`else if <plain-expr>` after an `if let = try`.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Integer throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	let x = 2
	var result = 0
	if let v = try mayFail() 'check'
		result = v
	end 'check' else if x == 2 'two'
		result = 22
	end 'two' else 'fallback'
		result = 99
	end 'fallback'
	return result
end 'main'
```
```exitcode
22
```
