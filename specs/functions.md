---
feature: functions
status: selfhosted
keywords: [functions, parameters, calls, arguments, labels, recursion, calling-convention, callee-saved, register-allocator]
category: functions
milestone: M5.5-M5.6
---

# Functions, parameters, and calls

## Documentation

Functions may declare parameters (`name type`, comma-separated) and be called with
arguments. The compiler passes arguments in registers under a custom ABI: the first six
arguments occupy `rcx`, `rdx`, `rax`, `r9`, `rsi`, `rdi` (all caller-saved), and the
return value comes back in `R8`. A seventh parameter would need a stack slot — a later
milestone — so the parser rejects more than six.

### Argument labels

The FIRST argument is positional; every argument after it must be named with its
parameter's label (`name: value`). Labelled arguments are reordered to the callee's
declaration order, so `add(2, b: 3)` binds `2` to `a` and `3` to `b`:

```
function add(a int, b int) returns int
	return a + b
end 'add'
```

A first argument that carries a label is `E2052`; a later argument that omits one is
`E2053`. Calling an undefined function is `E3004`, a wrong argument count is `E3036`,
and a label that names no parameter is `E3037`.

### Calls across the register allocator

A call is a hard clobber point: the callee may overwrite every caller-saved register.
So a value that is LIVE ACROSS a call cannot stay in a caller-saved register — the
allocator colors it into one of the five callee-saved registers (`rbx`, `r12`–`r15`)
instead, and the prologue/epilogue pass push/pops exactly the callee-saved registers a
function actually used. A function that makes a call also reserves a 16-byte-aligned
frame (32 bytes of Win64 shadow space plus alignment padding) so `rsp` is aligned at
the call.

Argument setup is emitted as a move of each argument into its ABI register followed by
the `call`. Because each such move clobbers its target register, a value still needed by
a later argument move can never be colored to an earlier argument register — so the
arguments reach their registers with no read-before-clobber.

The symmetric hazard appears at function ENTRY. Each parameter is captured out of its
incoming ABI register (`mov paramReg, argReg[i]`); emitted in slot order these form a
parallel copy whose SOURCES are the incoming registers. So a parameter's capture
DESTINATION must never be colored onto a *different* parameter's incoming register, or
that capture would clobber the sibling's incoming value before its own capture reads it.
The allocator forbids each parameter from every other parameter's incoming register (its
own is still preferred, so the common case is a self-move that elides). The multi-parameter
tests below are what hold that: a clobbered incoming register hands the callee a wrong
argument, the function returns the wrong answer, and the exit-code assertion fails — this
class shipped as a silent miscompile once, and it is a running test that catches it.

## Tests

<!-- test: add-labelled -->
`add(2, b: 3)` binds `2` positionally to `a` and `3` (labelled) to `b`, returning 5.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(2, b: 3)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
5
```

<!-- test: subtract-order -->
The positional argument fills the first parameter and the labelled one the second, so
`sub(20, b: 8)` is `20 - 8` = 12 — argument ORDER is preserved through the labelling.
```maxon
function sub(a Integer, b Integer) returns Integer
	return a - b
end 'sub'

function main() returns ExitCode
	return sub(20, b: 8)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
12
```

<!-- test: zero-arg-call -->
A call to a parameterless function.
```maxon
function answer() returns Integer
	return 42
end 'answer'

function main() returns ExitCode
	return answer()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
42
```

<!-- test: six-args -->
Six arguments — the full register-argument set (`rcx`, `rdx`, `rax`, `r9`, `rsi`,
`rdi`). Their sum is 1+2+3+4+5+6 = 21.
```maxon
function sum6(a Integer, b Integer, c Integer, d Integer, e Integer, f Integer) returns Integer
	return a + b + c + d + e + f
end 'sum6'

function main() returns ExitCode
	return sum6(1, b: 2, c: 3, d: 4, e: 5, f: 6)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
21
```

<!-- test: nested-calls -->
`inc(inc(inc(zero())))` — each call's result is the next call's argument. `zero()` = 7,
then +1 three times = 10.
```maxon
function zero() returns Integer
	return 7
end 'zero'

function inc(x Integer) returns Integer
	return x + 1
end 'inc'

function main() returns ExitCode
	return inc(inc(inc(zero())))
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
10
```

<!-- test: call-result-in-expression -->
Call results feed a larger expression, `add(2, b: 3) + add(10, b: 20) * 2` = 5 + 60 =
65. The first call's result is live ACROSS the second call, so it lands in a callee-saved
register.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(2, b: 3) + add(10, b: 20) * 2
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
65
```

<!-- test: recursion-factorial -->
Recursive `factorial(5)` = 120. The parameter `n` is live across the recursive call
(`n * factorial(n - 1)`), so it is preserved in a callee-saved register across the call.
```maxon
function factorial(n Integer) returns Integer
	if n <= 1 'base'
		return 1
	end 'base'
	return n * factorial(n - 1)
end 'factorial'

function main() returns ExitCode
	return factorial(5)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
120
```

<!-- test: recursion-fib -->
`fib(10)` = 55 — two recursive calls, where the first call's result is live across the
second (`fib(n - 1) + fib(n - 2)`).
```maxon
function fib(n Integer) returns Integer
	if n <= 1 'base'
		return n
	end 'base'
	return fib(n - 1) + fib(n - 2)
end 'fib'

function main() returns ExitCode
	return fib(10)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
55
```

<!-- test: value-live-across-call -->
`n` (a parameter, non-constant) is passed to `add` AND used again afterward, so it is
live across the call. It cannot stay in a caller-saved register — the allocator colors
it into a callee-saved register the function push/pops. `compute(5)` = add(5, 100) + 5 =
110.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function compute(n Integer) returns Integer
	let y = add(n, b: 100)
	return n + y
end 'compute'

function main() returns ExitCode
	return compute(5)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
110
```

<!-- test: param-passed-as-later-arg -->
An early parameter (`a`) is passed as a NON-FIRST argument (`y: a`) to an internal call
while a LATER parameter (`c`) is live ACROSS that call. The entry parameter captures form
a parallel copy out of the incoming ABI registers, so `a`'s capture destination must never
be colored onto `c`'s incoming register (`rax`) — otherwise `a`'s capture would clobber
`c` before `c`'s own capture reads it (a read-after-clobber miscompile). The fragment shows
`a` captured into a non-argument register (`rsi`), leaving `rax` intact for `c`'s capture.
`combine(10, b: 20, c: 30)` computes `diff(20, y: 10)` = 10, then `+ c` = 10 + 30 = 40.
```maxon
function diff(x Integer, y Integer) returns Integer
	return x - y
end 'diff'

function combine(a Integer, b Integer, c Integer) returns Integer
	let r = diff(b, y: a)
	return r + c
end 'combine'

function main() returns ExitCode
	return combine(10, b: 20, c: 30)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
40
```

<!-- test: call-in-loop -->
A call inside a loop, where the loop-carried accumulator `sum` and counter `i` are both
live across the call — each is colored to a callee-saved register and push/popped once,
with nothing added inside the loop but the argument move and the call. Sum of `dbl(i)`
for `i = 1..4` is 2+4+6+8 = 20.
```maxon
function dbl(x Integer) returns Integer
	return x + x
end 'dbl'

function main() returns ExitCode
	var sum = 0
	var i = 1
	while i <= 4 'loop'
		sum = sum + dbl(i)
		i = i + 1
	end 'loop'
	return sum
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
20
```

<!-- test: bare-call-statement -->
A call statement's result has to go somewhere. `noop` returns a VALUE and is impure (it writes `runs`), so
the statement takes the `_ =` discard — bare it would be E3065, and were `noop` pure the discard itself would
be E3064 (`discarded-results.md`). The BARE form belongs to a callee that returns nothing, which
`discarded-results.md`'s `void-function-ok` pins. The program returns 0.
```maxon
var runs = 0 as Integer

function noop(x Integer) returns Integer
	runs = runs + 1
	return x
end 'noop'

function main() returns ExitCode
	_ = noop(5)
	return runs - 1
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
0
```

<!-- test: first-arg-named -->
The first argument is positional — a label on it is E2052.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(a: 2, b: 3)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2052: <fragment>:7:13: the first argument cannot be named; only the second and later arguments take 'name:' labels
```

<!-- test: second-arg-unnamed -->
The second and later arguments must be labelled — a bare value there is E2053.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(2, 3)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E2053: <fragment>:7:16: the second and later arguments must be named ('name: value')
```

<!-- test: arity-mismatch -->
The argument count must match the callee's parameter count — E3036.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(2)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3036: <fragment>:7:9: 'add' expects 2 argument(s) but 1 were provided
```

<!-- test: unknown-function -->
A call to a function that does not exist is E3004.
```maxon
function main() returns ExitCode
	return frobnicate(2)
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-in-arithmetic -->
The undefined call is named wherever its result goes. Bound to a `let` and added to, the
diagnostic is still E3004 at the CALL — not a complaint about the addition.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	return c + 1
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-interpolated -->
Interpolating the result names the CALL, not the hole. The parser types an undefined
callee's result `unresolved` and defers, so a consumer that refuses the tag would report a
symptom one line below the mistake and abort the file before E3004 could be reached.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	print("c={c}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-iterated -->
Iterating the result names the CALL. A loop header cannot be abandoned once the parser has begun
emitting it, so the deferred source becomes a zero-trip stand-in range rather than a refusal of the
`for`.
```maxon
function main() returns ExitCode
	for x in frobnicate(2) 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:11: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-as-a-range-bound -->
A counted range's end bound defers the same way: E3004 at the call, not a complaint about the bound.
```maxon
function main() returns ExitCode
	for i in 0 to frobnicate(2) 'each'
		print("{i}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-as-a-range-start -->
The range's start is the loop's source expression, and it defers where the end bound does.
```maxon
function main() returns ExitCode
	for i in frobnicate(2) to 10 'each'
		print("{i}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:11: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-destructured -->
A destructuring pattern binds each name to a deferred element of its own. The element count of an
undefined callee's result is unknowable, so the pattern's arity is not refused ahead of E3004.
```maxon
function main() returns ExitCode
	for (k, v) in frobnicate(2) 'each'
		print("{k}{v}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-element-used-in-the-body -->
The element the body binds is deferred too, so a method call on it is not an error about the loop
counter's type.
```maxon
function main() returns ExitCode
	for s in frobnicate(2) 'each'
		print("{s.byteLength()}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:11: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-field-access -->
A field read off the result names the CALL, not the field.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	print("{c.width}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call -->
A method called on the result names the CALL, not the member.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-try -->
A `try` over a method called on the result names the CALL. The member call is a call even though its
receiver's type is unknown, so it is not refused as "not a call" ahead of E3004.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	try c.describe() otherwise return 1
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-try-with-a-fallback-value -->
The same `try` as a value with a fallback names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let d = try c.describe() otherwise 0
	print("{d}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-if-try -->
An `if let … = try` over a method called on the result names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	if let d = try c.describe() 'ok'
		print("{d}\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-bare-if-try -->
A bare `if try` over a method called on the result names the CALL. Whether the call produces a value
is unknown, so the form is not refused for discarding one.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	if try c.describe() 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-under-bare-if-try -->
A bare `if try` over the undefined call itself names the CALL, not a discarded result it cannot know
it has.
```maxon
function main() returns ExitCode
	if try frobnicate(2) 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:9: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-if-try-discarding-the-value -->
An `if let _ = try` over a method called on the result names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	if let _ = try c.describe() 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-awaited-under-bare-if-try -->
A bare `if try await` over a promise of the undefined call names the CALL.
```maxon
function main() returns ExitCode
	let p = async frobnicate(2)
	if try await p 'ok'
		print("ok\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-awaited-under-if-try -->
An `if let … = try await` over a promise of the undefined call names the CALL.
```maxon
function main() returns ExitCode
	let p = async frobnicate(2)
	if let x = try await p 'ok'
		print("{x}\n")
	end 'ok'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-awaited-under-try-with-a-fallback-value -->
A `try await` with a fallback over a promise of the undefined call names the CALL.
```maxon
function main() returns ExitCode
	let p = async frobnicate(2)
	let x = try await p otherwise 0
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-spawn-stored -->
Storing a promise of the undefined call names the CALL, not a mismatch with the storage's type.
```maxon
function main() returns ExitCode
	let p = async frobnicate(2)
	var ps = PendingArray.create()
	ps.push(p)
	return 0
end 'main'
typealias Integer = int(i64.min to i64.max)
typealias Pending = Promise with Integer
typealias PendingArray = Array with Pending
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-spawn-stored-as-throwing -->
Storage typed as a throwing promise is not refused for a callee whose throws clause is unknown.
```maxon
function main() returns ExitCode
	let p = async frobnicate(2)
	var ps = PendingArray.create()
	ps.push(p)
	return 0
end 'main'
enum Failure implements Error
	broken
end 'Failure'
typealias Integer = int(i64.min to i64.max)
typealias Pending = Promise with (Integer, Failure)
typealias PendingArray = Array with Pending
```
```maxoncstderr
error E3004: <fragment>:3:16: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-parenthesized-try -->
Parentheses around the method call do not make it a non-call.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let d = try (c.describe()) otherwise 0
	print("{d}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-on-a-parenthesized-receiver-under-try -->
Parentheses around the receiver alone do not make the method call a non-call.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let d = try (c).describe() otherwise 0
	print("{d}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-on-a-parenthesized-receiver-discarded -->
A discarded method call on a parenthesized receiver names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = (c).describe()
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-field-discarded-in-arithmetic -->
A discard whose right side calls nothing is refused whatever its operand is — a grouping parenthesis
is not a call.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.width + (1)
	return 0
end 'main'
```
```maxoncstderr
error E3067: <fragment>:4:2: expected a function call
```

<!-- test: unknown-function-result-element-method-call-under-try -->
A `try` over a method called on a loop element of the result names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	for x in c.items() 'each'
		let d = try x.describe() otherwise 0
		print("{d}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-try-in-a-closure -->
A closure capturing the result and trying a method on it names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let peek = function(_ Tally) gives try c.describe() otherwise 0
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-in-a-captured-var -->
A reassigned `var` a closure captures lives in a cell, and a cell holding the result names both CALLS
rather than refusing a storage type nobody can know.
```maxon
function main() returns ExitCode
	var c = frobnicate(2)
	c = frobnicate(3)
	let peek = function(_ Tally) gives c.width
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:6: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-captured-and-returned -->
A closure returning the captured result itself names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let peek = function(_ Tally) gives c
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-in-a-captured-var-returned -->
A closure returning a captured, reassigned `var` holding the result names both CALLS.
```maxon
function main() returns ExitCode
	var c = frobnicate(2)
	c = frobnicate(3)
	let peek = function(_ Tally) gives c
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:6: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-on-a-captured-var-under-try -->
A `try` over a method called on a reassigned, captured result names both CALLS. The captured read
emits an op of its own, and that op is not the call the `try` is applied to.
```maxon
function main() returns ExitCode
	var c = frobnicate(2)
	c = frobnicate(3)
	let peek = function(_ Tally) gives try c.describe() otherwise 0
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:6: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-propagating-try -->
A propagating `try` in a throwing function names the CALL.
```maxon
enum Failure implements Error
	broken
end 'Failure'

function run() throws Failure
	let c = frobnicate(2)
	try c.describe()
end 'run'

function main() returns ExitCode
	try run() otherwise return 1
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:7:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-under-try-binding-the-error -->
An `otherwise (e)` handler over a method called on the result names the CALL.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	try c.describe() otherwise (e) 'failed'
		print("{e}\n")
	end 'failed'
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-argument-calls-an-undefined-function -->
The arguments of a method called on the result are still compiled, so an undefined call among them is
named as well.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(missing(1))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:17: call to undefined function 'missing'
```

<!-- test: unknown-function-result-method-call-argument-names-an-undefined-variable -->
An undefined name among those arguments is reported where it is written.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(nowhere)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:17: Undefined variable 'nowhere'
```

<!-- test: unknown-function-result-method-call-under-try-argument-calls-an-undefined-function -->
Under a `try`, the arguments are compiled too.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let d = try c.describe(missing(1)) otherwise 0
	print("{d}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:25: call to undefined function 'missing'
```

<!-- test: unknown-function-result-chained-method-call-argument-calls-an-undefined-function -->
The arguments of every link of the chain are compiled, not only the first link's.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.first().then(missing(1))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:21: call to undefined function 'missing'
```

<!-- test: unknown-function-result-method-call-named-argument-calls-an-undefined-function -->
A named argument is compiled like a positional one.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(1, with: missing(2))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:26: call to undefined function 'missing'
```

<!-- test: unknown-function-result-nested-method-call-argument-calls-an-undefined-function -->
A method call on the result nested in another's arguments has its own arguments compiled.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(c.other(missing(1)))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:25: call to undefined function 'missing'
```

<!-- test: unknown-function-result-method-call-arguments-each-call-an-undefined-function -->
Every argument is compiled, and a second unlabelled argument is not refused: the member is unknown, so
its labelling rule is too.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(missing(1), missing(2))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:17: call to undefined function 'missing'
error E3004: <fragment>:4:29: call to undefined function 'missing'
```

<!-- test: unknown-function-result-method-call-labelled-first-argument -->
A labelled first argument is not refused either.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.describe(with: 1)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-argument-var-may-be-written -->
A `var` passed to the unknown member may be written by it, so it is not reported as never mutated.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	var n = 1
	_ = c.bump(n)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-typed-closure-argument -->
A typed closure passed to the unknown member compiles.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x Tally) gives x)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-method-call-untyped-closure-argument -->
An untyped closure parameter passed to the unknown member is not refused: its type would come from the
member, which is unknown, so it defers with the member rather than hiding E3004.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x) gives x)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-untyped-closure-argument-member-read -->
A member read off the deferred closure parameter defers with it.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x) gives x.width)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-untyped-closure-argument-calls-an-undefined-function -->
The closure's body is still compiled, so an undefined call in it is named.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x) gives missing(x))
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
error E3004: <fragment>:4:31: call to undefined function 'missing'
```

<!-- test: unknown-function-result-untyped-closure-argument-with-two-parameters -->
Every untyped parameter of that closure defers.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x, y) gives y)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-untyped-closure-argument-names-an-undefined-variable -->
An undefined name in that closure's body is reported where it is written.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(function(x) gives nowhere)
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:4:31: Undefined variable 'nowhere'
```

<!-- test: unknown-function-result-untyped-closure-as-a-labelled-argument -->
An untyped closure passed as a later, labelled argument defers too.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.pair(1, with: function(x) gives x)
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-result-untyped-closure-nested-in-a-declared-call-is-refused -->
The deferral reaches only a closure written directly as the unknown member's argument. One passed to a
declared function whose parameter is not function-typed still has to type its parameter.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	_ = c.each(apply(function(x) gives x))
	return 0
end 'main'

function apply(n Tally) returns Tally
	return n
end 'apply'

typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E2015: <fragment>:4:28: Unsupported: parameter 'x' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```

<!-- test: unknown-function-result-captured-by-a-closure -->
A closure capturing the result names the CALL, not the capture.
```maxon
function main() returns ExitCode
	let c = frobnicate(2)
	let peek = function(_ Tally) gives c.width
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:10: call to undefined function 'frobnicate'
```

<!-- test: unknown-function-is-a-closures-whole-body -->
A closure's return type is INFERRED from its body, so an undefined call as the whole body is
the one place a deferred call result reaches a SIGNATURE rather than a value column. It is
still E3004 at the call, and the compiler does not abort.
```maxon
function main() returns ExitCode
	let peek = function(_ Tally) gives frobnicate(2)
	_ = peek(1)
	return 0
end 'main'
typealias Tally = int(0 to u64.max)
```
```maxoncstderr
error E3004: <fragment>:3:37: call to undefined function 'frobnicate'
```

<!-- test: unknown-label -->
A `name:` label that matches no parameter is E3037.
```maxon
function add(a Integer, b Integer) returns Integer
	return a + b
end 'add'

function main() returns ExitCode
	return add(2, zzz: 3)
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3037: <fragment>:7:16: 'add' has no parameter named 'zzz'
```
