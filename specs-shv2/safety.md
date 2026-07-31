---
feature: safety
status: stable
keywords: panic, fault, safety, crash, runtime
category: runtime
---
# Runtime Safety: CPU Fault Diagnostics

## Documentation

When a Maxon program triggers a CPU fault — a nil pointer dereference or a stack
overflow — the runtime catches it via the platform's
fault-handler mechanism (Windows VEH on x64-Windows, `sigaction` on macOS), prints
a clean diagnostic to stderr, and exits with status 1.

This is implemented to eliminate the previous behavior where a fault on a worker
thread would silently kill the OS thread and leave the scheduler hung. Faults now
produce a deterministic diagnostic instead of either a silent hang or an OS error
dialog.

The fault-handler infrastructure does not yet support `recover()` — once a fault
fires, the process always exits.

Divide-by-zero is NOT one of these faults. `/` and integer `mod` are throwing
operations at the language level: a divide whose divisor cannot be proven non-zero
throws `DivisionByZero`, so the caller must handle it with `try ... otherwise` (or
propagate it). A divisor proven non-zero — a non-zero literal, or a ranged type whose
range excludes 0 — compiles to a bare divide with no check. A divisor the compiler
holds as the constant 0 (`a / 0`) is rejected at compile time. Because the failure is
in the type system rather than in a CPU trap, the behavior is identical on every
target: x64 no longer relies on the `idiv` `#DE`, AArch64 no longer returns 0 from
`SDIV`/`UDIV`, and `wasm32-wasi` no longer trap-exits **3** past the fault handler
that never sees a wasm trap. The fault handler still classifies a stray `SIGFPE` it
happens to receive (e.g. a floating-point trap) to a divide-by-zero panic, but a
correctly compiled integer divide never reaches it. The nil-pointer
(`SIGSEGV`/`SIGBUS`) path traps identically on both architectures, so the
`force-segfault` test runs on `arm64-macos` as well as `x64-windows`.

Float `/` is fallible on the same terms. `x / 0.0` is `±inf` and `0.0 / 0.0` is `NaN` —
representable values, but a division by zero is a logic error all the same, so it is
surfaced in the type rather than silently produced: a possibly-zero float divisor throws
`DivisionByZero`, a constant `0.0` or `-0.0` divisor (both give `±inf`) is a compile-time
error, and a non-zero literal divisor is a bare divide. Only division is affected — an
`inf` or `NaN` from a NON-division source (overflow to `inf`, `inf - inf`, a domain error)
is still produced silently. Float `mod` does not exist (`mod` is integer-only).

⚠ **`INT_MIN / -1` IS STILL UNGUARDED, and the two halves of that sentence must not be
reversed together by mistake.** `idiv` faults on it as well as on a zero divisor, and
NEITHER reference compiler handles it (the bootstrap only declines to *fold* it); the
`DivisionByZero` this rung adds is about the DIVISOR being zero and says nothing about
the quotient being unrepresentable. So `i64.min / -1` still raises a hardware fault on
x64 — a trap in a language that otherwise has none left here.

⭐⭐ **THAT SENTENCE IS ABOUT `/` ONLY, AND IT USED TO BE READ AS COVERING `mod` — WHICH IS
WHERE THE `mod` HALF WENT WRONG (A1x). `i64.min mod -1` IS `0`, ON EVERY TARGET.** The
rationale above is *"the quotient is unrepresentable"*, and that is simply FALSE of the
remainder: **`a mod -1` is `0` for EVERY `a`**, because truncated division gives
`a - (-1)·trunc(a / -1) = a - a`. `mod` faulted only because x86 computes quotient and
remainder in ONE `idiv`, so it inherited a `#DE` raised on account of a quotient it does not
even read. Measured before the fix, all three doors into a `mod` and the same program: x64
died `panic: integer overflow`, x64-linux died `panic: integer divide by zero`, and
**wasm32-wasi already answered `0`** — a valid program's OBSERVABLE ANSWER differing by
target, not merely its diagnostic. Maxon now answers `0` everywhere, with Go and Java.

⭐ **SO `mod` TESTS ONLY ITS DIVISOR, AND `/` NEEDS BOTH OPERANDS — WHICH IS WHY THE TWO
DIVERGE HERE AT ALL.** `a / -1` overflows for exactly one dividend (`i64.min`); `a mod -1`
is `0` for all of them. A divisor the compiler proves is neither `0` nor `-1` — a literal, or
a ranged type excluding both — still compiles to the bare `idiv` with no guard at all, so the
cost is paid only where the proof runs out. **`/` is UNCHANGED: its quotient does not exist,
so there is no value to return and the documented fault stands.**

⚠ **AND `try (a mod b) otherwise …` IS NOW DEAD WEIGHT AT THIS BOUNDARY, NOT NEWLY WORKING.**
A hardware fault was never catchable; the cure is that there is nothing to catch. The `try` is
still REQUIRED when the divisor could be `0` (that error is real), but at `-1` the fallback
never runs — a case written as though it fires is asserting the opposite of the rule.

⭐ **THAT FAULT IS NOW DIAGNOSED, AND IT IS ITS OWN DIAGNOSTIC.** It arrives as
`STATUS_INTEGER_OVERFLOW` (**0xC0000095**), a DIFFERENT exception code from the zero divisor's
`STATUS_INTEGER_DIVIDE_BY_ZERO` (0xC0000094), and the Windows fault thunk used to convert
0xC0000094 **and nothing else** — so the process died with no panic line, no backtrace and a raw
0xC0000095 nobody could interpret (measured: **exit 127, stderr completely EMPTY**). It now
classifies both, and an unrepresentable quotient prints **`panic: integer overflow`** plus the
same symbolized backtrace and exit 1 a zero divisor gets. The wording is the bootstrap's, which
has carried both codes all along (`X86CodeEmitter.Runtime.cs`'s `ExceptionCodeIntOverflow`, its
`__gt_ftp_intovf` arm, and `__gt_panic_msg_int_overflow`) — one fault, one spelling, across two
compilers.

⚠ **ONLY x64-WINDOWS CAN TELL THE TWO APART, and the limitation is the KERNEL's rather than this
compiler's.** Linux delivers both as `SIGFPE`, and reports **`si_code = FPE_INTDIV` (1) for every
`#DE`** — the overflow included, because the CPU does not tell the kernel which of the two `#DE`
causes fired and `exc_divide_error` names `FPE_INTDIV` unconditionally. Measured, not assumed: the
SIGFPE handler was instrumented to print the raw `si_code` and read `0x…0001` for BOTH programs
below. There is nothing in the siginfo to branch on, so x64-linux keeps the one wording it can
justify — `panic: integer divide by zero` and exit 1 — and the overflow case below is `x64-windows`
only. **arm64 needs nothing**: AArch64 `SDIV` does not trap, `i64.min / -1` simply evaluates to
`i64.min`, and neither arm64 backend has a fault handler to add an arm to. **`wasm32-wasi` needs
nothing either, for the opposite reason**: `i64.div_s` DOES trap on an unrepresentable quotient, but
a wasm trap is not deliverable to guest code, so the module exits **3** under `wasmtime` with that
runtime's own `wasm trap: integer overflow` and no Maxon fault handler is involved (measured).

## Tests

On x64-Windows the diagnostic also walks the faulting thread's saved-RBP chain and
prints a symbolized stack trace after the panic line (frame 0 is the faulting
instruction, resolved from the faulting RIP; the remaining frames are the callers).
This mirrors the ordinary `mrt_panic` software-panic trace. The frame addresses
themselves are non-deterministic (ASLR), so only the resolved function names are
asserted. arm64-macOS has no frame-walking fault diagnostic yet, so its tests
assert only the panic line.

<!-- disabled-test: divide-by-zero -->
<!-- BLOCK-FORM `try` — NOT the division rule. This case is ported byte-identical from `specs/safety.md`
     and its DIVISION half is implemented: the divide throws, `otherwise (e)` binds it, and `match e`
     discriminates `divisionByZero`. What shv2 cannot yet parse is the STATEMENT-GROUPING form
     `try 'work' … end 'work' otherwise (e) 'handle' … end 'handle'` — `Parser.parseTry` refuses a
     `try` followed by a block label outright ("block-form `try 'label' … end` (grouping multiple calls
     in one try) — a later slice"). `divide-by-zero-expression-form` below is this exact program in the
     expression form shv2 does parse, and it asserts the same 42. Enable this one with block-form `try`. -->
### Integer divide-by-zero throws DivisionByZero, caught with try/otherwise
```maxon
typealias Integer = int(i64.min to i64.max)

// An opaque source so the divisor is possibly-zero (the compiler cannot fold it,
// so `100 / zero` is a throwing operation rather than a compile-time `a / 0` error).
function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let zero = opaque(0)
	var result = 0
	try 'work'
		result = 100 / zero
	end 'work'
	otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then result = 42
		end 'kind'
	end 'handle'
	return result as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: mod-by-zero -->
### Integer modulo-by-zero throws DivisionByZero, caught with try/otherwise
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let zero = opaque(0)
	let result = try (5 mod zero) otherwise 7
	return result as ExitCode
end 'main'
```
```exitcode
7
```

<!-- disabled-test: float-divide-by-zero -->
<!-- BLOCK-FORM `try` — see `divide-by-zero` above for the whole reason; the division half is
     implemented and `float-divide-by-zero-expression-form` below asserts the same 42 in the
     expression form. Enable with block-form `try`. -->
### Float divide-by-zero throws DivisionByZero, caught with try/otherwise
```maxon
typealias Float = float(f64.min to f64.max)

// An opaque source so the divisor is possibly-zero (the compiler cannot fold it,
// so `100.0 / zero` throws rather than being a compile-time `a / 0.0` error). Float
// `/` is fallible exactly as integer `/` is: `x / 0.0` would be ±inf — a representable
// value, but still a logic error — so it is surfaced in the type, not silently produced.
function opaque(x Float) returns Float
	return x
end 'opaque'

function main() returns ExitCode
	let zero = opaque(0.0)
	var result = 0
	try 'work'
		let q = 100.0 / zero
		// Unreachable: the divide throws before this runs. `q > 0.0` keeps the value used so the
		// divide is not elided, without casting a float to the ExitCode.
		result = 1 if q > 0.0 else 2
	end 'work'
	otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then result = 42
		end 'kind'
	end 'handle'
	return result as ExitCode
end 'main'
```
```exitcode
42
```

### The same three programs in the expression form shv2 parses

The two ported cases above are gated on block-form `try` alone. These carry their DIVISION
subject — the throw, the `(e)` binding, and the `match e` discrimination of `divisionByZero`
— in the `try (…) otherwise (e) 'label' … end` form, so the rule is pinned rather than merely
documented while the block form is outstanding.

<!-- test: divide-by-zero-expression-form -->
### An integer divide's DivisionByZero is bound and discriminated by match
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let zero = opaque(0)
	var result = 0
	// A STATEMENT-position `try`, which is what the block form's `result = 100 / zero` body amounts to.
	// The quotient is discarded, and the throw survives that: what raises the error is the divisor test
	// feeding the fork, not the divide whose value nobody reads.
	try (100 / zero) otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then result = 42
		end 'kind'
	end 'handle'
	return result as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: float-divide-by-zero-expression-form -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
### A float divide's DivisionByZero is bound and discriminated by match
```maxon
typealias Float = float(f64.min to f64.max)

function opaque(x Float) returns Float
	return x
end 'opaque'

function main() returns ExitCode
	let zero = opaque(0.0)
	var result = 0
	try (100.0 / zero) otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then result = 42
		end 'kind'
	end 'handle'
	return result as ExitCode
end 'main'
```
```exitcode
42
```

### A RUNTIME `-0.0` divisor throws too — both zeros, not just the one with a zero bit pattern

`error.float-divide-by-negative-constant-zero` below pins `-0.0` as a *folded constant*, which the
proof settles before any code is emitted. This case pins the other half: a `-0.0` that reaches the
divide at RUNTIME. Nothing else in this suite reaches it — every other runtime float case here
divides by `+0.0`, and `+0.0`'s bit pattern is 0, so it would pass even if the zero test read the
divisor's bits as an integer. `-0.0` is `0x8000000000000000`, so it is the operand that tells a float
compare apart from a bit compare.

⚠ **What this case does NOT pin, measured rather than assumed.** Emitting that zero test as an
INTEGER compare does not silently yield `-inf`: it is refused loudly by the x64 emitter's
register-file guard (`requireClass`, `Targets/X64/X64Backend.maxon` — *"xmm0 is in the xmm register
file where the gpr file is required"*), because the divisor is in an xmm and an integer compare wants
a gpr. Verified by sabotage, and under that sabotage this case fails **together with**
`float-divide-by-runtime-negative-zero`'s `+0.0` sibling rather than instead of it — so it does not
discriminate there, and the structural guard is the real protection. What is left for this case to
hold is the SEMANTICS (both zeros throw, and `x / -0.0` is not quietly `-inf`) and one future
refactor the register guard cannot see: a deliberately well-typed bitcast of the divisor to an
integer to avoid an xmm compare.

<!-- test: float-divide-by-runtime-negative-zero -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
typealias Float = float(f64.min to f64.max)

function opaque(x Float) returns Float
	return x
end 'opaque'

function main() returns ExitCode
	let negZero = opaque(-0.0)
	var result = 0
	try (1.0 / negZero) otherwise (e) 'handle'
		match e 'kind'
			divisionByZero then result = 42
		end 'kind'
	end 'handle'
	return result as ExitCode
end 'main'
```
```exitcode
42
```

### A possibly-zero divisor without `try` is refused (E3057)

The divide is a throwing operation, so a bare one drops an error flag the caller never reads.
The message names DIVISION rather than the array accessor whose wording every other throwing
builtin family used to inherit.

<!-- test: error.divide-without-try -->
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let d = opaque(7)
	let q = 100 / d
	return q as ExitCode
end 'main'
```
```maxoncstderr
error E3057: <fragment>:10:14: throwing division requires try: wrap it as `try (a / b) otherwise …`, or give the divisor a ranged type that excludes 0 (e.g. `int(1 to ...)`) — a bare divide drops the divide-by-zero error
```

<!-- test: error.mod-without-try -->
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let d = opaque(7)
	let r = 100 mod d
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3057: <fragment>:10:14: throwing division requires try: wrap it as `try (a mod b) otherwise …`, or give the divisor a ranged type that excludes 0 (e.g. `int(1 to ...)`) — a bare divide drops the divide-by-zero error
```

### The parenthesized `try` target must have emitted the op it is applied to

⭐⭐ **A `try` TARGET THAT EMITS NOTHING MUST BE REFUSED, AND ONLY A REFUSAL KEEPS THE DOOR THE
WIDTH IT CLAIMS.** `try (a / b)` is accepted because `rewriteLastCallToTryCall` converts the LAST op
in the current block and a possibly-zero divide leaves a `call __checked_div` there. That derivation
is sound only while the target is guaranteed to have PUT an op there: a parenthesized group whose
inner expression is a bare NAME emits no op at all (a name is a lookup), so "the last op the target
emitted" silently becomes *the last op the previous statement emitted*.

⚠ **Measured before the guard existed, and it was not merely a wrong message.** The second case below
COMPILED and returned **99**: the `try`/`otherwise` was transplanted onto the `arr.get(2)` two
statements above it, so the handler fired for the ARRAY's out-of-bounds error, `w` took the handler's
value instead of `a`'s, and the E3057 that bare `arr.get(2)` owed disappeared along the way — because
the op it would have been reported against was no longer a plain `call`. Two diagnostics lost and a
wrong value produced, from one widened door.

The guard is a DERIVATION rather than a paren-shaped special case: `parseTry` records the module's op
count before parsing the target, and an op older than that mark cannot be the target's. Every other
try form is unaffected, because every one of them emits its call.

<!-- test: error.try-over-a-parenthesized-non-call -->
The group is a plain local, so nothing was emitted for it. The blame must land on the `try`, not on
the non-throwing `opaque` whose call happens to be the last op in the block.
```maxon
typealias Integer = int(i64.min to i64.max)

function opaque(x Integer) returns Integer
	return x
end 'opaque'

function main() returns ExitCode
	let v = opaque(5)
	let w = try (v) otherwise 99
	return w as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: `try` must be applied to a call — `try f(…)` or `try obj.method(…)`; the expression after `try` is not a call
```

<!-- test: error.try-over-a-parenthesized-name-cannot-claim-an-earlier-call -->
The same shape with a THROWING call in front of it — the case that compiled and answered 99.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(7)
	let a = arr.get(2)
	let w = try (a) otherwise 99
	return w as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:10: Unsupported: `try` must be applied to a call — `try f(…)` or `try obj.method(…)`; the expression after `try` is not a call
```

### A divisor the compiler HOLDS as 0 is never recoverable — it is E3103

A throw the program could catch would be a lie about a divide that can only ever fail, so a
provably-zero divisor is rejected outright rather than routed to `DivisionByZero`. Both float
zeros are zero: `x / -0.0` is `-inf` exactly as `x / 0.0` is `+inf`, and IEEE-754 makes the two
patterns compare equal.

<!-- test: error.divide-by-constant-zero -->
```maxon
function main() returns ExitCode
	let a = 12
	let q = a / 0
	return q as ExitCode
end 'main'
```
```maxoncstderr
error E3103: <fragment>:4:12: division by zero: the divisor of '/' is always 0
```

<!-- test: error.mod-by-constant-zero -->
```maxon
function main() returns ExitCode
	let a = 12
	let r = a mod 0
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E3103: <fragment>:4:12: division by zero: the divisor of 'mod' is always 0
```

<!-- test: error.divide-by-let-bound-zero -->
```maxon
function main() returns ExitCode
	let a = 12
	let zero = 0
	let q = a / zero
	return q as ExitCode
end 'main'
```
```maxoncstderr
error E3103: <fragment>:5:12: division by zero: the divisor of '/' is always 0
```

<!-- test: error.float-divide-by-constant-zero -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	let a = 12.0
	let q = a / 0.0
	return trunc(q) as ExitCode
end 'main'
```
```maxoncstderr
error E3103: <fragment>:4:12: division by zero: the divisor of '/' is always 0
```

<!-- test: error.float-divide-by-negative-constant-zero -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
```maxon
function main() returns ExitCode
	let a = 12.0
	let q = a / -0.0
	return trunc(q) as ExitCode
end 'main'
```
```maxoncstderr
error E3103: <fragment>:4:12: division by zero: the divisor of '/' is always 0
```

### `i64.min mod -1` is `0`, through every door a `mod` has (A1x)

Three doors reach a `mod`, they are decided by what the compiler can prove about the DIVISOR, and
before A1x **all three died** — so all three are pinned. `-1` is not a special divisor to the
language; it is only special to `idiv`, and the point of these cases is that nothing about the
answer depends on which door the program came through.

They run on **every** target, which is the other half of the claim: `wasm32-wasi` passed the first
of them all along (`i64.rem_s` is defined to answer 0) and arm64's `sdiv`+`msub` computes it too, so
a green x64 lane beside them is what makes the answer the LANGUAGE's rather than one backend's.

<!-- test: mod-at-int-min-by-minus-one-is-zero -->
#### Door 1 — a divisor the compiler cannot prove non-zero (the throwing `mod`)
⚠ The `otherwise` is DEAD WEIGHT here and `77` must never be seen. It is spelled only because a
possibly-zero divisor makes `mod` fallible (E3057 without it); the divide-by-`-1` it is wrapped
around does not throw, because its answer exists.
```maxon
typealias Integer = int(i64.min to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let n = ident(i64.min)
	let d = ident(0 - 1)
	let r = try (n mod d) otherwise 77
	print("r={r}\n")
	return 0
end 'main'
```
```stdout
r=0
```
```exitcode
0
```

<!-- test: mod-by-a-minus-one-literal-is-zero -->
#### Door 2 — a literal `-1` divisor, so no `try` at all
The compiler proves the divisor non-zero, so this `mod` is TOTAL and a `try` around it would be
refused. It nonetheless has to answer `0` rather than fault — the door the fallible-division rule
never touches.
```maxon
typealias Integer = int(i64.min to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let n = ident(i64.min)
	let r = n mod -1
	print("r={r}\n")
	return 0
end 'main'
```
```stdout
r=0
```
```exitcode
0
```

<!-- test: mod-by-a-ranged-divisor-that-admits-minus-one-is-zero -->
#### Door 3 — a ranged divisor that excludes `0` but ADMITS `-1`
`int(-1 to -1)` is the shape that shows "excludes zero" was never the same proof as "cannot
overflow": it earns the bare divide by the fallible-division rule and is `-1` every time. The
dividend arrives through a call argument, the one position a ranged type is not re-checked at
runtime — the same door `divide-by-zero-fault-through-an-unchecked-call-argument` uses below.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOne = int(-1 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function remainder(n Integer, d NegativeOne) returns Integer
	return n mod d
end 'remainder'

function main() returns ExitCode
	let n = ident(i64.min)
	let d = ident(-1)
	let r = remainder(n, d: d)
	print("r={r}\n")
	return 0
end 'main'
```
```stdout
r=0
```
```exitcode
0
```

<!-- test: mod-by-minus-one-is-zero-for-every-dividend -->
#### The guard reads the DIVISOR and never the dividend
`i64.min` is the only dividend that made `idiv` fault, so a guard could pass Door 1 while still
being keyed on the dividend. These three are `0` for the same reason `i64.min` is — `a mod -1`
does not depend on `a` — and a positive, a negative and a zero dividend together say so.
```maxon
typealias Integer = int(i64.min to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let d = ident(0 - 1)
	let a = try (ident(100) mod d) otherwise 77
	let b = try (ident(0 - 7) mod d) otherwise 77
	let c = try (ident(0) mod d) otherwise 77
	print("a={a} b={b} c={c}\n")
	return 0
end 'main'
```
```stdout
a=0 b=0 c=0
```
```exitcode
0
```

<!-- test: mod-by-a-ranged-divisor-below-minus-one-is-a-bare-idiv -->
#### A range that excludes BOTH `0` and `-1` still buys the unguarded divide
The proof is what keeps the guard off the common path, so its precision is worth a case: `int(i64.min
to -2)` is wholly negative — it admits neither hazard — and `specs-shv2/division.md`'s
`ranged-divisor-excluding-minus-one-is-still-a-bare-idiv` holds the golden that shows no `cmp` was
emitted. Here the point is only that a NEGATIVE divisor is not treated as suspicious merely for
being negative: `-13 mod -5` is `-3`, the remainder taking the DIVIDEND's sign.
```maxon
typealias BelowMinusOne = int(i64.min to -2)

function remainder(n int, d BelowMinusOne) returns int
	return n mod d
end 'remainder'

function main() returns ExitCode
	print("r={remainder(0 - 13, d: -5)}\n")
	return 0
end 'main'
```
```stdout
r=-3
```
```exitcode
0
```

### The CPU fault a divide still reaches, and the two codes the thunk must tell apart

Both cases below reach a bare `idiv` carrying an operand the type system was told could not occur,
through the ONE position a ranged typealias is checked at compile time but deliberately NOT at
runtime: a **call argument**. `InsertRangeChecks` skips the runtime cascade there because by the time
a guard could land the value has already travelled into the call, so a `NonZero` parameter handed a
runtime 0 — or a `NegativeOne` parameter handed a runtime `-1` beside an `i64.min` dividend — is how
a hardware trap is still reachable in a language whose `/` otherwise throws.

They are a PAIR: one door, one stack trace, and the two different exception codes
(`STATUS_INTEGER_DIVIDE_BY_ZERO` 0xC0000094 vs `STATUS_INTEGER_OVERFLOW` 0xC0000095) the Windows
thunk classifies. The divide-by-zero case is the CONTROL — it is what a classified fault looks like,
and adding an arm beside it must not move it. It is also the only remaining test of the fault thunk
at all: once `/` became a language-level throw, no program in this suite reached the handler.

<!-- test: divide-by-zero-fault-through-an-unchecked-call-argument -->
<!-- targets: x64-windows, x64-linux -->
#### A runtime zero reaching a bare `idiv` panics `integer divide by zero`
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NonZero = int(1 to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

// `d` excludes 0, so the divide is proven safe and compiles to the unguarded `idiv` — no throw,
// no `try`. The proof is the CALLER's to keep, and nothing at runtime holds it to it.
function divide(n Integer, d NonZero) returns Integer
	return n / d
end 'divide'

function main() returns ExitCode
	let z = ident(0)
	return divide(10, d: z)
end 'main'
```
```exitcode
1
```
```stderr
panic: integer divide by zero
Stack trace:
  in divide
  in main
  in mrt_start
```

<!-- test: integer-overflow-fault-from-int-min-over-minus-one -->
<!-- targets: x64-windows -->
#### `i64.min / -1` panics `integer overflow` — a different code, its own words

`x64-linux` is excluded for a measured reason, not an unexamined one: its kernel reports
`FPE_INTDIV` for this fault too, so there is nothing to classify on (see the Documentation above).

```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOne = int(-1 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

// The divisor's range excludes 0, so this is the unguarded `idiv` again — the `DivisionByZero` a
// possibly-zero divisor would have raised is about the DIVISOR, and says nothing about a quotient
// that does not fit. `i64.min / -1` is `i64.max + 1`, so `idiv` raises `#DE` with a divisor of -1.
function divide(n Integer, d NegativeOne) returns Integer
	return n / d
end 'divide'

function main() returns ExitCode
	let n = ident(i64.min)
	// Opaque, so the divide cannot be strength-reduced to a negation — which would not trap.
	let d = ident(-1)
	return divide(n, d: d)
end 'main'
```
```exitcode
1
```
```stderr
panic: integer overflow
Stack trace:
  in divide
  in main
  in mrt_start
```

<!-- test: a-checked-divide-still-faults-at-int-min-over-minus-one -->
<!-- targets: x64-windows -->
#### ⭐ THE BOUNDARY OF THE `mod` RULE: a CHECKED `/` is still fatal here, deliberately (A1x)

The case above reaches the fault through a bare `idiv`. This one reaches it through the **fallible**
divide — the spelling that has a `try`, a divisor the compiler cannot prove non-zero, and a fallback
sitting right there — and the fallback **still never runs**, because a `#DE` is not a throw. That is
not an oversight left over from `mod`: **`i64.min / -1` has no representable quotient, so there is no
value for a total `/` to return**, and A1x fixed `mod` precisely because its answer DOES exist. Pinned
so the asymmetry is tested rather than inferred from prose — if a later rung makes `/` total too, this
case is what it has to come and change.

`x64-linux` is excluded for the same measured reason as its neighbour: its kernel reports `FPE_INTDIV`
for this fault too, so the wording it prints is the divide-by-zero one.
```maxon
typealias Integer = int(i64.min to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let n = ident(i64.min)
	let d = ident(0 - 1)
	print("before\n")
	let q = try (n / d) otherwise 77
	print("q={q}\n")
	return 0
end 'main'
```
```stdout
before
```
```exitcode
1
```
```stderr
panic: integer overflow
Stack trace:
  in main
  in mrt_start
```

<!-- disabled-test: force-segfault -->
<!-- P1.2 heap + __Builtins -->
<!-- targets: x64-windows -->
### Deliberate access violation produces a clean panic with backtrace
```maxon
function main() returns ExitCode
	__Builtins.forceSegfault()
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic: nil pointer or invalid memory access
Stack trace:
  in maxon_force_segfault
  in main
  in mrt_start
```

<!-- disabled-test: force-segfault-macos -->
<!-- Beyond: arm64-macos target -->
<!-- targets: arm64-macos -->
<!-- SelfhostedOnly -->
### Deliberate access violation produces a clean panic (arm64-macOS)
```maxon
function main() returns ExitCode
	__Builtins.forceSegfault()
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic: nil pointer or invalid memory access
```
