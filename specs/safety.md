---
feature: safety
status: stable
keywords: panic, fault, safety, crash, runtime
category: runtime
---
# Runtime Safety: CPU Fault Diagnostics

## Documentation

When a Maxon program triggers a CPU fault — a nil pointer dereference or a stack
overflow — the runtime catches it via the platform's fault-handler mechanism
(Windows VEH on x64-Windows, `sigaction` on macOS), prints a clean diagnostic to
stderr, and exits with status 1.

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
target: x64 no longer relies on the `idiv` `#DE`, and AArch64 no longer returns 0 from
`SDIV`/`UDIV`. The fault handler still classifies a stray `SIGFPE` it happens to receive
(e.g. a floating-point trap) to a divide-by-zero panic, but a correctly compiled integer
divide never reaches it. The nil-pointer (`SIGSEGV`/`SIGBUS`) path traps identically on
both architectures.

Float `/` is fallible on the same terms. `x / 0.0` is `±inf` and `0.0 / 0.0` is `NaN` —
representable values, but a division by zero is a logic error all the same, so it is
surfaced in the type rather than silently produced: a possibly-zero float divisor throws
`DivisionByZero`, a constant `0.0` or `-0.0` divisor (both give `±inf`) is a compile-time
error, and a non-zero literal divisor is a bare divide. Only division is affected — an
`inf` or `NaN` from a NON-division source (overflow to `inf`, `inf - inf`, a domain error)
is still produced silently. Float `mod` does not exist (`mod` is integer-only).

## Tests

The diagnostic also walks the faulting thread's saved frame-pointer chain and prints a
symbolized stack trace after the panic line (frame 0 is the faulting instruction,
resolved from the faulting RIP/PC; the remaining frames are the callers). This mirrors
the ordinary `mrt_panic` software-panic trace. The frame addresses themselves are
non-deterministic (ASLR), so only the resolved function names are asserted — the
runner strips the ` at rip=…` suffix the fault diagnostic appends to the panic line.

Both architectures walk: x64-Windows via `__gt_fault_last_rbp`, arm64-macOS via the
same stash filled from `mcontext->__ss`. Each bounds the chain to the faulting stack
and requires it to strictly ascend, so a corrupt frame pointer shortens the trace
rather than faulting a second time inside the handler.

<!-- test: divide-by-zero -->
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

<!-- test: float-divide-by-zero -->
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

### `i64.min mod -1` is `0`, through every door a `mod` has

`idiv` faults for TWO unrelated reasons — a zero divisor, and a quotient that does not fit — and a
divisor proven non-zero only closes the first. `-1` is not a special divisor to the language; it is
special to one instruction. Three doors reach a `mod`, decided by what the compiler can prove about
the divisor, and each is pinned: nothing about the answer depends on which door the program took.

<!-- test: mod-at-int-min-by-minus-one-is-zero -->
#### Door 1 — a divisor the compiler cannot prove non-zero (the throwing `mod`)
The `otherwise` is dead weight and `77` must never be seen: it is spelled only because a
possibly-zero divisor makes `mod` fallible. The divide by `-1` it wraps does not throw — its answer
exists.
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
The compiler proves the divisor non-zero, so this `mod` is total and no `try` is spellable around
it. It nonetheless has to answer `0` rather than fault — the door the fallible-division rule never
touches.
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
overflow": it earns the bare divide by the fallible-division rule and is `-1` every time.
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

<!-- test: mod-at-int-min-by-a-32-bit-minus-one-is-zero -->
#### Door 4 — the NARROWED `mod`, which overflows in its own width
A ranged operand whose optimal type is 32 bits lowers to a 32-bit `idiv`, and that instruction
raises the same fault on `i32.min mod -1` — a whole door the 64-bit reading of the hazard misses.
The guard has to reach it, so it is computed at one width and applied whatever width the divide
itself is emitted at.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias I32 = int(i32.min to i32.max)
typealias NegativeI32 = int(i32.min to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function remainder(a I32, d NegativeI32) returns I32
	return a mod d
end 'remainder'

function main() returns ExitCode
	let a = ident(i32.min) as I32
	let d = ident(0 - 1) as NegativeI32
	let r = remainder(a, d: d)
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
being keyed on the dividend. These three are `0` for the same reason `i64.min` is — `a mod -1` does
not depend on `a` — and a positive, a negative and a zero dividend together say so.
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
The proof is what keeps the guard off the common path, so its precision is worth a case:
`int(i64.min to -2)` is wholly negative and admits neither hazard. The point here is that a
NEGATIVE divisor is not treated as suspicious merely for being negative — `-13 mod -5` is `-3`, the
remainder taking the DIVIDEND's sign.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BelowMinusOne = int(i64.min to -2)

function remainder(n Integer, d BelowMinusOne) returns Integer
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

<!-- test: error.try-over-a-total-mod-is-not-a-call -->
#### A `try` over a guarded `mod` is still refused, and still as a non-call
The overflow guard is four extra arithmetic ops, not a call — so a `mod` the compiler proved
non-zero remains a bare operator expression and `try` refuses it with the SAME words it always did.
Pinned because the guard could have been built as a compiler-emitted call instead, and that would
have changed what an author is told about an operator they wrote.
```maxon
typealias Integer = int(i64.min to i64.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let n = ident(i64.min)
	let r = try (n mod -1) otherwise 5
	return r as ExitCode
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/safety/error.try-over-a-total-mod-is-not-a-call.test:10:10: try requires a function call or await expression
```

### The boundary of the `mod` rule — `/` is deliberately NOT total

`i64.min / -1` is `i64.max + 1`, so there is no value a guarded `/` could return. `mod` is total
because its answer at that divisor exists; `/`'s does not. Both cases below therefore still fault,
and they are pinned so the asymmetry is tested rather than inferred from prose.

<!-- test: integer-overflow-fault-from-int-min-over-minus-one -->
<!-- targets: x64-windows -->
#### `i64.min / -1` panics `integer overflow` — a bare `idiv`, its own diagnostic
The divisor's range excludes 0, so this is the unguarded `idiv`: the `DivisionByZero` a
possibly-zero divisor would raise is about the DIVISOR and says nothing about a quotient that does
not fit.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOne = int(-1 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function divide(n Integer, d NegativeOne) returns Integer
	return n / d
end 'divide'

function main() returns ExitCode
	let n = ident(i64.min)
	// Opaque, so the divide cannot be strength-reduced to a negation — which would not trap.
	let d = ident(-1)
	return divide(n, d: d) as ExitCode
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
#### A CHECKED `/` is still fatal here — the fallback sits right there and never runs
This reaches the fault through the fallible spelling: a `try`, a divisor the compiler cannot prove
non-zero, and a fallback one line away. The fallback still never runs, because a CPU fault is not a
throw. If a later rung ever makes `/` total, this case is what it has to come and change.
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

### A wholly-negative ranged bound is CHECKED, and both `idiv` hazards rode through when it was not

A range's stored upper bound of `-1` means `u64.max` in exactly one shape — an UNSIGNED range, whose
low bound is non-negative. Reading it as that sentinel whatever the low bound said left a wholly
negative range with no upper compare at all, so a plain `as` cast admitted values the range
excludes. The divide's proof trusts a declared range, so both of `idiv`'s hazards were reachable
through that one hole.

<!-- test: negative-range-cast-guard-fires-before-the-divide -->
#### A zero cast into a wholly-negative divisor range is refused at the CAST, not at the `idiv`
`int(-100 to -1)` excludes `0`, so `100 / d` is a bare `idiv` with no `try` spellable — correct,
given the declared type. The cast guard is therefore the thing that has to stop a runtime `0`
getting there; this died `integer divide by zero` before the bound was checked.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOnly = int(-100 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let z = ident(0)
	let d = z as NegativeOnly
	let q = 100 / d
	print("q={q}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at negative-range-cast-guard-fires-before-the-divide.test:11: Range check failed: value outside typealias 'NegativeOnly'
Stack trace:
  in main
  in mrt_start
```

<!-- test: negative-range-cast-guard-fires-before-the-remainder -->
#### The overflow hazard through the same door — `int(i64.min to -2)` admitting a `-1`
`BelowMinusOne` rules out BOTH of `idiv`'s divisors, so `n mod d` is bare and correct given the
declared type — and `i64.min mod d` came back as `panic: integer overflow` through a cast the
division's proof was entitled to trust. `int(i64.min to -2)` is the total case: its LOWER bound
needs no compare either, so the range carried an empty check list.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BelowMinusOne = int(i64.min to -2)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let bad = ident(0 - 1)
	let d = bad as BelowMinusOne
	let r = i64.min mod d
	print("r={r}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at negative-range-cast-guard-fires-before-the-remainder.test:11: Range check failed: value outside typealias 'BelowMinusOne'
Stack trace:
  in main
  in mrt_start
```

<!-- test: force-segfault -->
<!-- targets: x64-windows, arm64-macos -->
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
