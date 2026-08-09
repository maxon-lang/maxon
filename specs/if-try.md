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

#### Bare Form

Run a throwing call that returns nothing, and branch on whether it worked:

```maxon
if try runStep() 'check'
	print("Operation succeeded!")
end 'check'
```

The if-block executes only if the expression succeeds (doesn't throw).

That is a THROW test, not a value test, which is why the bare form is legal ONLY when the callee
returns nothing. A call that PRODUCES a value is refused (E3124): the result would be dropped
while the source reads as though the result were what is being tested. `if try json.getBool(node,
key: "enabled")` reads as *"if enabled"* and means *"if the key exists and is a bool"* — which is
true when the stored value is `false`.

The rule is on "the call produces a value", not on the type of that value. A `bool` is only where
the misreading is loudest; a rule keyed to it would leave every other type silently dropping its
result.

#### Binding Form

Unwrap and bind the success value:

```maxon
if let value = try mayFail() 'check'
	print("Got: {value}")
end 'check'
```

If successful, the unwrapped value is bound to `value` and available within the if-block.

#### Discarding the Result

A value-producing call has to say what happens to its result, and `_` — the discard identifier —
is how a caller says "nothing":

```maxon
if let _ = try appendAndCount(item) 'check'
	print("appended")
end 'check'
```

That is open to an IMPURE callee only. A PURE function's result must be USED, and `_` does not save
it (E3064): a pure call whose answer nobody wants is the wrong call. Ask `s.contains(x)` rather than
discarding `s.findLast(x)`.

#### With Else Clause

Handle the error case:

```maxon
if try runStep() 'check'
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

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(true) 'check'
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

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(false) 'check'
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

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(false) 'check'
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

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(true) 'check'
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

function runStep(which Integer) throws MyError
	if which == 1 'check1'
		throw MyError.first
	end 'check1'
	if which == 2 'check2'
		throw MyError.second
	end 'check2'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(1) 'check'
		result = 100
	end 'check' else (e) 'err'
		match e 'kind'
			first then result = 50
			second then result = 60
		end 'kind'
	end 'err'
	return result
end 'main'
```
```exitcode
50
```

<!-- test: if-try-nested -->
```maxon

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	var result = 0
	if try runStep(true) 'outer'
		if try runStep(true) 'inner'
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
	export var cache as StrMap

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
error E3055: specs/fragments/if-try/error.if-try-non-throwing.test:10:5: try requires a throwing function: 'noThrow' does not throw'
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
	export var numbers as IntArray
	export var text as String
	export var tag as String

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
	return MultiManaged.create(nums, text: "hello", tag: "world")
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
	export var name as String
	export var values as IntArray

	static function create(name String, values IntArray) returns Self
		return Self{name: name, values: values}
	end 'create'
end 'Inner'

type Outer
	export var label as String
	export var inner as Inner
	export var tags as StringArray

	static function create(label String, inner Inner, tags StringArray) returns Self
		return Self{label: label, inner: inner, tags: tags}
	end 'create'
end 'Outer'

function createOuter() returns Outer
	var inner = Inner.create("test", values: IntArray.create())
	inner.values.push(1)
	inner.values.push(2)
	var outer = Outer.create("outer", inner: inner, tags: StringArray.create())
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

<!-- test: if-try-fn-typed-result -->
A throwing function that returns a FUNCTION must bind through `if let` with its
signature intact, exactly as `let f = try pick(...) otherwise ...` does. The two
forms are one rewrite of the call into a try-call, and they were once two copies
of that rewrite: only the expression copy carried the resolved signature across,
so the `if let` form declared the binding with no function type and calling it
dereferenced null — the compiler crashed with an internal E9001 rather than
compiling the program.
```maxon
typealias Score = int(i64.min to i64.max)
typealias Transform = function(Score) returns Score

enum PickError implements Error
	nope
end 'PickError'

function double(x Score) returns Score
	return x * 2
end 'double'

function pick(ok bool) returns Transform throws PickError
	if ok 'y'
		return double
	end 'y'
	throw PickError.nope
end 'pick'

function main() returns ExitCode
	let f = try pick(true) otherwise panic("pick(true) cannot fail")
	let a = f(3)

	if let g = try pick(true) 'ok'
		let b = g(4)
		return a + b
	end 'ok' else 'bad'
		return 1
	end 'bad'
end 'main'
```
```exitcode
14
```

<!-- test: error.if-try-discards-a-result -->
`if try` is a THROW test, not a value test: the then-block runs whenever the call did not throw,
whatever it produced. So a callee with a result has that result silently dropped while the source
reads as though the result were what is being tested. MEASURED before this refusal existed — this
exact program compiled, and `answer(true)` returning `false` still took the branch and exited 7.

```maxon

enum MyError implements Error
	failed
end 'MyError'

function answer(succeed bool) returns bool throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return false
end 'answer'

function main() returns ExitCode
	if try answer(true) 'check'
		return 7
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3124: specs/fragments/if-try/error.if-try-discards-a-result.test:15:5: 'if try' discards the result of 'answer': only a call that returns nothing may be tested bare — bind the result with 'if let'
```

<!-- test: error.if-try-discards-a-non-bool-result -->
ONE law, not a type-keyed one. `bool` is only where the misreading is loudest; the refusal is on
"the call produces a value", so an `Integer` result is dropped exactly as silently and is refused
exactly the same way.

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
	if try mayFail(true) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3124: specs/fragments/if-try/error.if-try-discards-a-non-bool-result.test:17:5: 'if try' discards the result of 'mayFail': only a call that returns nothing may be tested bare — bind the result with 'if let'
```

<!-- test: if-try-void-callee-runs-its-effect -->
The form the rule leaves standing: "run this, branch on whether it worked". The callee's effect
happens on the success path and is skipped on the throwing one, and nothing is dropped because
there is nothing to drop.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

var visits = 0 as Integer

function recordVisit(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	visits = visits + 1
end 'recordVisit'

function main() returns ExitCode
	var taken = 0
	if try recordVisit(true) 'ok'
		taken = taken + 1
	end 'ok'
	if try recordVisit(false) 'bad'
		taken = taken + 10
	end 'bad'
	print("{taken} {visits}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 1
```

<!-- test: if-try-impure-result-may-be-discarded -->
An IMPURE callee's result may be dropped, provided the program SAYS so: `_` is the discard
identifier, and writing it is the whole of what the rule asks for. The call still runs — the
counter it bumps proves it — and the then-block still tests only whether it threw.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

var calls = 0 as Integer

function impureFail(succeed bool) returns Integer throws MyError
	calls = calls + 1
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'impureFail'

function main() returns ExitCode
	var taken = 0
	if let _ = try impureFail(true) 'ok'
		taken = taken + 1
	end 'ok'
	if let _ = try impureFail(false) 'bad'
		taken = taken + 10
	end 'bad'
	print("{taken} {calls}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- test: error.if-try-pure-result-is-not-saved-by-discard -->
`_` does not save a PURE callee, and that is the rule's point rather than a gap in it: a pure call
whose answer nobody wants is the wrong call. The site has to bind the result and USE it, or ask a
function that answers the question it actually has — `s.contains(x)` rather than a discarded
`s.findLast(x)`.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function pureFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'pureFail'

function main() returns ExitCode
	if let _ = try pureFail(true) 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/if-try/error.if-try-pure-result-is-not-saved-by-discard.test:17:9: result of pure function 'pureFail' must be used
```

<!-- test: if-try-binding-uses-the-result -->
The cure for a pure callee, spelled out: the result is bound and used, and the branch still turns
only on whether the call threw.

```maxon

typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function pureFail(succeed bool) returns Integer throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
	return 42
end 'pureFail'

function main() returns ExitCode
	if let value = try pureFail(true) 'check'
		return value
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: error.if-try-binds-a-void-result -->
The DUAL of the rule above, and the same question from the other side: a binding is a value
position, so `if let x = try runStep()` asks for a value the call does not produce. It is the fault
the expression `try` already refuses (`let x = try runStep() otherwise …`), with the same code, the
same sentence and the same anchor — the condition form shares that rewrite and had never shared the
question about its result. MEASURED before this refusal existed: the bootstrap declared no binding
at all and blamed the USE (`E2004: Undefined variable 'x'`, about a name written one line above),
and shv2 did not survive it — it PANICKED in `maxonTypeOfTag`, "a `void` tag names no value".

```maxon

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	if let x = try runStep(true) 'v'
		print("{x}\n")
		return 4
	end 'v'
	return 0
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/if-try/error.if-try-binds-a-void-result.test:14:13: type mismatch: ''runStep' does not return a value'
```

<!-- test: error.if-try-discards-a-void-result -->
`_` is exempt from the unused checks, not from arithmetic: it discards a result, and a void call has
no result to discard. Refused on the same code as its named-binding sibling, because it is the same
mistake with a different spelling — and it compiled clean before, binding nothing from nothing.

```maxon

enum MyError implements Error
	failed
end 'MyError'

function runStep(succeed bool) throws MyError
	if not succeed 'check'
		throw MyError.failed
	end 'check'
end 'runStep'

function main() returns ExitCode
	if let _ = try runStep(true) 'v'
		return 4
	end 'v'
	return 0
end 'main'
```
```maxoncstderr
error E3059: specs/fragments/if-try/error.if-try-discards-a-void-result.test:14:13: type mismatch: ''runStep' does not return a value'
```
