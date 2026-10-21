---
feature: raw-resource-release-is-a-write
status: experimental
keywords: [SharedSegment, DebugPort, SampleTimer, close, release, raw memory, read-only, shared, E3159]
category: memory
---

# Releasing a resource, or writing its raw memory, is a write of the value that owns it

## Documentation

A `SharedSegment`, a `DebugPort` or a `SampleTimer` owns an operating-system resource: a mapping, a window of
scratch pages, a timer handle on Windows. `close()` hands the resource back, and a method that lets the kernel
fill the scratch pages writes them. Neither stores into a field, but both change what the value stands for, so the
compiler counts them as writes of the receiver, exactly as it counts `push` on an array.

A write needs a value nothing else can read at the same moment. A `let` binding, a `let` field, a registry
`default` or `SharedValue` read, and any other read-only view refuse the call (E3159). The owner holds the
value in a `var` and closes it there; a resource that no one closed is released when its last reference drops.

## Tests

<!-- test: error.a-let-segment-cannot-be-closed -->
A `let` segment is read-only, so the call that unmaps it is refused.
```maxon
function main() returns ExitCode
	let segment = try SharedSegment.create("maxon-spec-raw-let", bytes: 4096) otherwise return 1
	segment.close()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:10: cannot pass 'segment', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.a-registry-segment-cannot-be-closed-by-a-reader -->
Every thread reads a registry `default`, so no thread may unmap it under the others.
```maxon
default SharedSegment = try SharedSegment.create("maxon-spec-raw-registry", bytes: 4096) otherwise panic("no segment")

function main() returns ExitCode
	SharedSegment.current().close()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:5:26: cannot pass a value read out of a `SharedValue` or registry value, which is read-only, to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.a-let-pin-cannot-be-released -->
<!-- unsupported-targets: wasm32-wasi -->
A `MachinePin` owns the binding of a green thread to its machine, and `release()` hands it back. A `let` pin is read-only, so the call that unpins is refused.
wasm32-wasi has no green threads, so it answers `E3104` for `MachinePin` before it reaches this refusal; the case is pinned on the native lanes.
```maxon
function main() returns ExitCode
	let pin = MachinePin.acquire()
	pin.release()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:6: cannot pass 'pin', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: a-var-segment-is-closed-by-its-owner -->
The accepted twin: the owner holds the segment in a `var`, closes it, and every accessor then throws `closed`.

```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-raw-var", bytes: 4096) otherwise return 1
	segment.close()
	let refused = try segment.copyOut(0, byteCount: 8) otherwise 'closed'
		return 0
	end 'closed'
	return refused.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: error.a-let-debug-port-cannot-be-closed -->
A `DebugPort` frees its scratch pages in `close()`, so a `let` port refuses it.

```maxon
function main() returns ExitCode
	let port = DebugPort.create()
	port.close()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:7: cannot pass 'port', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: a-var-debug-port-is-closed-by-its-owner -->
The accepted twin: a `var` port is closed by its owner and answers `isReady()` false afterwards.

```maxon
function main() returns ExitCode
	var port = DebugPort.create()
	port.close()
	print("{port.isReady()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
```

<!-- test: error.a-let-sample-timer-cannot-be-closed -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi declares no SampleTimer, so the call is refused with E3004 (call to undefined function 'SampleTimer.create'), not E3104.

A `SampleTimer`'s `close()` writes it — it marks the timer closed, and on Windows releases its timer handle — so a
`let` timer refuses `close()`.

```maxon
function main() returns ExitCode
	let timer = SampleTimer.create(1000000)
	timer.close()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:8: cannot pass 'timer', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: error.a-let-process-trace-cannot-be-closed -->
A `ProcessTrace` frees its scratch pages in `close()`, so a `let` trace refuses it.

```maxon
function main() returns ExitCode
	let trace = ProcessTrace.create()
	trace.close()
	return 0
end 'main'
```
```maxoncstderr
error E3159: <fragment>:4:8: cannot pass 'trace', which is read-only (a `let` binding), to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: a-var-process-trace-is-closed-by-its-owner -->
The accepted twin: a `var` trace is closed by its owner and answers `isReady()` false afterwards.

```maxon
function main() returns ExitCode
	var trace = ProcessTrace.create()
	trace.close()
	print("{trace.isReady()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
false
```
