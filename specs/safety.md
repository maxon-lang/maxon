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

## Tests

<!-- test: divide-by-zero -->
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
```

<!-- test: mod-by-zero -->
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
```
