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
arguments. shv2 passes arguments in registers under a custom ABI: the first six
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
