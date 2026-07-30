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
the quotient being unrepresentable. So `i64.min / -1` still raises `#DE` on x64 and is
still classified by the fault handler to the `panic: integer divide by zero` diagnostic
— a hardware trap in a language that otherwise has none left here.

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
