---
feature: try-postfix-target
status: experimental
keywords: [try, otherwise, postfix, chain, method, throws, Error]
category: error-handling
---

# `try` Binds the OUTERMOST Call of a Postfix Chain

## Documentation

`try <expr>` catches the error of the call the expression as a whole performs —
the **last** call it evaluates, not the first one it mentions. In

```maxon
let s = try make().slice(1, endIndex: 3) otherwise return 9
```

the throwing operation is `.slice(…)`; `make()` is merely how its receiver was
produced. The `try` therefore binds to `slice`, and `make` not throwing is not an
error — it is not the thing being tried.

The rule is uniform over the whole postfix family:

| expression | the call `try` binds to |
|---|---|
| `try f()` | `f` |
| `try f().g()` | `g` |
| `try f().g().h()` | `h` |
| `try f(g())` | `f` — `g` is an ARGUMENT, evaluated before the call `try` guards |
| `try obj.field.method()` | `method` |

Because the binding is to the outermost call, the non-throwing check (**E3055**)
asks about *that* call. `try make().count()` is rejected — `count` cannot fail —
even though `make` is chained in front of it, and `try th().count()` is rejected
even though `th` throws, because the `try` does not guard `th`.

A builtin (`count`, `capacity`, `isEmpty`, `push`, `print`, `String.append`, …)
cannot fail either, so a `try` on one is E3055 exactly as a `try` on a
non-throwing user function is. Only a `throws` function and the bounds-checked
array accessors (`get`/`set`/`first`/`last`/`pop`/`remove`/`slice`) can be tried.

### Ownership on the error edge

`try make().slice(…)` produces an owned temporary — the array `make()` returned —
that the `try` does NOT own: it is the receiver, and it belongs to the enclosing
statement. It must be released exactly once whichever edge is taken, including
the error edge, where the `try`'s own result register is null.

### One `try`, one throwing call

A `try` opens exactly ONE error edge, so exactly one call in its chain may throw
— the outermost one. A chain with a throwing call *before* the tail
(`try a.slice(…).get(…)`) is rejected with **E3057** against that inner call: its
error flag has nowhere to go, and reading its null result is a use of memory the
throw never produced. (The reference compiler accepts that program and
segfaults on the inner throw, dereferencing the null record `slice` left behind.)

## Tests

<!-- test: chained-throwing-method -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr
end 'make'

function main() returns ExitCode
	let s = try make().slice(1, endIndex: 3) otherwise return 9
	return s.count()
end 'main'
```
```exitcode
2
```

<!-- test: chained-two-hop -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr
end 'make'

function main() returns ExitCode
	let v = try make().clone().get(1) otherwise return 9
	return v
end 'main'
```
```exitcode
20
```

<!-- test: chained-error-edge-drops-receiver -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	return arr
end 'make'

function main() returns ExitCode
	let v = try make().get(5) otherwise return 9
	return v
end 'main'
```
```exitcode
9
```

<!-- test: chained-error-edge-drops-two-temps -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr
end 'make'

function main() returns ExitCode
	let v = try make().clone().get(9) otherwise return 7
	return v
end 'main'
```
```exitcode
7
```

<!-- test: chained-on-array-literal -->
```maxon
function main() returns ExitCode
	let v = try [10, 20, 30, 40].clone().get(1) otherwise return 9
	return v
end 'main'
```
```exitcode
20
```

<!-- test: chained-on-string-literal -->
A STRING literal receiver, which the array-literal case's own argument always covered and the parser did
not: a `String` has three throwing methods a program can reach on one (`findFirst`, `findLast`,
`indexAfter`), and until this case `try "abc".findFirst("b")` was `E2015 … (got 'string literal')` — a
legal program refused, while the reference compiles it and answers 1.
```maxon
function main() returns ExitCode
	let idx = try "abcb".findFirst("b") otherwise return 9
	return idx.charIndex() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: chained-on-interpolated-string-literal -->
An INTERPOLATED literal receiver is the same arm, and it is the half that has an owned temp to drop: the
receiver is a heap String rather than an immortal `.rdata` record, and the error edge has to release it
exactly once. The needle is found at grapheme 3 of `"ab7cb"`.
```maxon
function main() returns ExitCode
	let n = 7
	let idx = try "ab{n}cb".findLast("b") otherwise return 9
	return idx.charIndex() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: chained-on-string-literal-error-edge -->
The error edge of the same shape, taken: nothing in `"abc"` matches, so `otherwise` runs and the
interpolated receiver's allocation must not leak (the suite's leak gate is what checks that).
```maxon
function main() returns ExitCode
	let n = 7
	let idx = try "ab{n}c".findFirst("z") otherwise return 4
	return idx.charIndex() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: void-throwing-static-and-instance -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum E implements Error
	bad
end 'E'

type Gate
	var n as Integer

	static function check(v Integer) throws E
		if v < 0 'neg'
			throw E.bad
		end 'neg'
	end 'check'

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump(v Integer) throws E
		if v < 0 'neg'
			throw E.bad
		end 'neg'
	end 'bump'
end 'Gate'

function main() returns ExitCode
	var g = Gate.create()
	try Gate.check(1) otherwise return 9
	try g.bump(1) otherwise return 8
	try Gate.check(-1) otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

### ⚠⚠ THE THIRD RECEIVER SPELLING — `try self.m()`, WHICH THE CASE ABOVE IS ONE WORD AWAY FROM

**The case above runs a void throwing method through a STATIC (`Gate.check`) and a NAMED receiver
(`g.bump`), and both were always right. `self` — the same method, the same position — was refused
`E2004: Function 'Gate.bump' does not return a value`, about a call whose value nobody asked for.**

The cause is the rule `void-call-result.md` states in bold: *a `try` target is parsed with
`resultUsed: false` BY DESIGN*, because the `try` decides value-ness at its OWN position from the TAG
of the result the target minted. Every arm of `parseTryCallReceiver` threaded that `false` through —
except the `self` arm, which reached `parseSelfPrimary`, a routine shared with VALUE position
(`parsePrimary`) that hardcoded `resultUsed: true`. So the guard that cannot see a `try` target fired
inside one anyway, and only for the one receiver spelling that shares a parse routine with an
expression. It is now the caller's flag at both call sites.

⚠ MEASURED against the bootstrap, which compiles and runs the identical program — so this was a wrong
REJECTION and not a stricter reading.

⚠ **BOTH EDGES ARE IN THE ONE CASE, because a threading bug and a deleted check are told apart by the
error edge.** `driveTwice` runs two `try self.bump(…)` statements for their effect (`n` becomes 5), then
`driveBad`'s bare `try self.bump(-1)` PROPAGATES out of a method whose own `try` has no handler — the
exit code is `total()` read on that error edge, so a `try self.m()` that silently discarded its flag
would return 0 here.

<!-- test: void-throwing-self-receiver -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum E implements Error
	bad
end 'E'

type Gate
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump(v Integer) throws E
		if v < 0 'neg'
			throw E.bad
		end 'neg'
		n = n + v
	end 'bump'

	function driveTwice() throws E
		try self.bump(2)
		try self.bump(3)
	end 'driveTwice'

	function driveBad() throws E
		try self.bump(-1)
	end 'driveBad'

	function total() returns Integer
		return n
	end 'total'
end 'Gate'

function main() returns ExitCode
	var g = Gate.create()
	try g.driveTwice() otherwise return 9
	try g.driveBad() otherwise return g.total()
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: error.void-throwing-self-receiver-in-value-position -->
⚠ **THE CONTROL, AND WITHOUT IT THE CASE ABOVE IS ALSO GREEN WHEN THE CHECK IS SIMPLY GONE.** A void
throwing method under a `try` in VALUE position is still refused — by the TAG, at the `try`'s own
position (`parseTry`'s `voidInValue`, E3059).

⚠⚠ **AND IT PINS MORE THAN "STILL REFUSED": IT PINS *WHICH* REFUSAL, BECAUSE THE `self` SPELLING WAS
REACHING THE WRONG ONE.** MEASURED on the unfixed compiler, this program was `E2004` at column **15**
— the receiver-arm guard, quoting a construct the `try` had already taken responsibility for — where
every other receiver spelling gave `E3059` at column **11**. So the hardcoded flag cost the `self`
spelling BOTH answers: the statement form was refused outright, and the value form was refused by the
wrong check at the wrong anchor. The message names the same method a named receiver's does; the two are
told apart by their programs, not their text.
```maxon
typealias Integer = int(i64.min to i64.max)

enum E implements Error
	bad
end 'E'

type Gate
	var n as Integer

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	function bump(v Integer) throws E
		if v < 0 'neg'
			throw E.bad
		end 'neg'
		n = n + v
	end 'bump'

	function drive() returns Integer throws E
		let x = try self.bump(1) otherwise return 9
		return x
	end 'drive'
end 'Gate'

function main() returns ExitCode
	var g = Gate.create()
	return try g.drive() otherwise 8
end 'main'
```
```maxoncstderr
error E3059: <fragment>:23:11: type mismatch: ''Gate.bump' does not return a value'
```

<!-- test: error.two-throwing-calls-in-one-chain -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	return arr
end 'make'

function main() returns ExitCode
	let v = try make().slice(1, endIndex: 3).get(0) otherwise return 9
	return v
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/try-postfix-target/error.two-throwing-calls-in-one-chain.test:14:21: throwing array accessor requires try: wrap it as `try …(…) otherwise …` — a bare call drops the out-of-bounds error
```

<!-- test: error.chained-non-throwing-method -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function make() returns IntArray
	var arr = IntArray.create()
	arr.push(10)
	return arr
end 'make'

function main() returns ExitCode
	let n = try make().count() otherwise return 9
	return n
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.chained-non-throwing-method.test:12:10: try requires a throwing function: this builtin call cannot fail
```

<!-- test: error.non-throwing-array-accessor -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	let n = try arr.count() otherwise return 9
	return n
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.non-throwing-array-accessor.test:8:10: try requires a throwing function: this builtin call cannot fail
```

<!-- test: error.non-throwing-builtin-static-constructor -->

`Array.create()` cannot fail, so `try` on it is E3055 exactly as `try arr.count()`
is. ⚠ The reference compiler ACCEPTS this program and gets it WRONG: it emits a
`tryCall` on a callee that never writes an error flag, reads whatever the ABI
left in the flag register, and takes the ERROR path — returning 9 where the only
correct answer is 0. Its own E3055 check misses it because a synthesized
constructor is absent from the function registry, which is the very blind spot
this rule closes. shv2 rejecting it is the deliberate divergence.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	let a = try IntArray.create() otherwise return 9
	return a.count()
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.non-throwing-builtin-static-constructor.test:6:10: try requires a throwing function: this builtin call cannot fail
```

<!-- test: error.non-throwing-builtin-print -->
```maxon
function main() returns ExitCode
	try print("x\n") otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.non-throwing-builtin-print.test:3:2: try requires a throwing function: this builtin call cannot fail
```

<!-- test: error.throwing-argument-non-throwing-callee -->
```maxon
typealias Integer = int(i64.min to i64.max)

enum MyError implements Error
	failed
end 'MyError'

function g() returns Integer throws MyError
	return 5
end 'g'

function f(x Integer) returns Integer
	return x + 1
end 'f'

function main() returns ExitCode
	let n = try f(try g() otherwise 0) otherwise return 9
	return n
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.throwing-argument-non-throwing-callee.test:17:10: try requires a throwing function: 'f' does not throw'
```

<!-- test: error.chained-non-throwing-after-throwing -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

enum MyError implements Error
	failed
end 'MyError'

function th() returns IntArray throws MyError
	var arr = IntArray.create()
	arr.push(10)
	return arr
end 'th'

function main() returns ExitCode
	let n = try th().count() otherwise return 9
	return n
end 'main'
```
```maxoncstderr
error E3055: specs/fragments/try-postfix-target/error.chained-non-throwing-after-throwing.test:16:10: try requires a throwing function: this builtin call cannot fail
```
