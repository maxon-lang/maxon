---
feature: safety
status: stable
keywords: panic, fault, safety, crash, runtime
category: runtime
---
# Runtime Safety: CPU Fault Diagnostics

## Documentation

When a Maxon program triggers a CPU fault — divide by zero, integer overflow, nil
pointer dereference, or stack overflow — the runtime catches it via the platform's
fault-handler mechanism (Windows VEH on x64-Windows, `sigaction` on macOS), prints
a clean diagnostic to stderr, and exits with status 1.

This is implemented to eliminate the previous behavior where a fault on a worker
thread would silently kill the OS thread and leave the scheduler hung. Faults now
produce a deterministic diagnostic instead of either a silent hang or an OS error
dialog.

The fault-handler infrastructure does not yet support `recover()` — once a fault
fires, the process always exits.

Integer divide-by-zero and modulo-by-zero are only caught on targets whose CPU
traps the operation. On x64 the `idiv` instruction raises `#DE` (delivered as a
Windows `EXCEPTION_INT_DIVIDE_BY_ZERO` / a POSIX `SIGFPE`), which the handler
converts to the panic below. AArch64 integer `SDIV`/`UDIV` by zero is defined to
return 0 with NO trap, so there is no fault to catch and the divide/modulo-by-zero
tests are gated to `x64-windows`. The handler still classifies a `SIGFPE` it does
receive (e.g. a floating-point trap) to the divide-by-zero panic. The nil-pointer
(`SIGSEGV`/`SIGBUS`) path traps identically on both architectures, so the
`force-segfault` test runs on `arm64-macos` as well as `x64-windows`.

## Tests

On x64-Windows the diagnostic also walks the faulting thread's saved-RBP chain and
prints a symbolized stack trace after the panic line (frame 0 is the faulting
instruction, resolved from the faulting RIP; the remaining frames are the callers).
This mirrors the ordinary `mrt_panic` software-panic trace. The frame addresses
themselves are non-deterministic (ASLR), so only the resolved function names are
asserted. arm64-macOS has no frame-walking fault diagnostic yet, so its tests
assert only the panic line.

<!-- test: divide-by-zero -->
<!-- targets: x64-windows -->
### Integer divide-by-zero produces a clean panic
```maxon
function main() returns ExitCode
	let zero = 0
	let result = 100 / zero
	return result
end 'main'
```
```exitcode
1
```
```stderr
panic: integer divide by zero
Stack trace:
  in main
  in mrt_start
```

<!-- test: mod-by-zero -->
<!-- targets: x64-windows -->
### Integer modulo-by-zero produces a clean panic
```maxon
function main() returns ExitCode
	let zero = 0
	let result = 5 mod zero
	return result
end 'main'
```
```exitcode
1
```
```stderr
panic: integer divide by zero
Stack trace:
  in main
  in mrt_start
```

<!-- test: force-segfault -->
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

<!-- test: force-segfault-macos -->
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
