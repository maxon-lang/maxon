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

Integer divide-by-zero is NOT one of these faults. Integer `/` and `mod` are
throwing operations at the language level: a divide whose divisor cannot be proven
non-zero throws `DivisionByZero`, so the caller must handle it with `try ... otherwise`
(or propagate it). A divisor proven non-zero — a non-zero literal, or a ranged type
whose range excludes 0 — compiles to a bare divide with no check. A divisor the
compiler holds as the constant 0 (`a / 0`) is rejected at compile time. Because the
failure is in the type system rather than in a CPU trap, the behavior is identical on
every target: x64 no longer relies on the `idiv` `#DE`, and AArch64 no longer returns
0 from `SDIV`/`UDIV`. The fault handler still classifies a stray `SIGFPE` it happens to
receive (e.g. a floating-point trap) to a divide-by-zero panic, but a correctly
compiled integer divide never reaches it. The nil-pointer (`SIGSEGV`/`SIGBUS`) path
traps identically on both architectures.

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
