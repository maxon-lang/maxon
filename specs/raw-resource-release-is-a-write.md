---
feature: raw-resource-release-is-a-write
status: experimental
keywords: [SharedSegment, DebugPort, SampleTimer, close, release, raw memory, read-only, shared, E3207]
category: memory
---

# Releasing a resource, or writing its raw memory, is a write of the value that owns it

## Documentation

A `SharedSegment`, a `DebugPort` or a `SampleTimer` owns an operating-system resource: a mapping, a window of
scratch pages, a timer handle on Windows. `close()` hands the resource back, and a method that lets the kernel
fill the scratch pages writes them. Neither stores into a field, but both change what the value stands for, so the
compiler counts them as writes of the receiver, exactly as it counts `push` on an array.

A write needs a value that is not frozen and that other threads do not read: a registry `default` or
`SharedValue` read, and any other frozen or read-only view, refuse the call (E3207). A `let` binding only
forbids reassigning the name, so a `let` resource is closed exactly as a `var` one is. A resource that no one
closed is released when its last reference drops.

## Tests

<!-- test: a-let-segment-is-closed-by-its-owner -->
A `let` segment is never reassigned, and the call that unmaps it writes the resource it names, so it is accepted;
every accessor then throws `closed`.
```maxon
function main() returns ExitCode
	let segment = try SharedSegment.create("maxon-spec-raw-let", bytes: 4096) otherwise return 1
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
error E3207: <fragment>:5:25: 'close' writes its receiver; the receiver is immutable
error E3159: <fragment>:5:26: cannot pass a value read out of a `SharedValue` or registry value, which is read-only, to a function that writes parameter 'self' (in main); pass a clone()
```

<!-- test: a-let-pin-is-released -->
<!-- unsupported-targets: wasm32-wasi -->
A `MachinePin` owns the binding of a green thread to its machine, and `release()` hands it back. A `let` pin is never reassigned, so the call that unpins it is accepted.
wasm32-wasi has no green threads, so it answers `E3104` for `MachinePin`; the case is pinned on the native lanes.
```maxon
function main() returns ExitCode
	let pin = MachinePin.acquire()
	pin.release()
	return 0
end 'main'
```
```exitcode
0
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

<!-- test: a-var-debug-port-is-closed-by-its-owner -->
A `DebugPort` frees its scratch pages in `close()`: a port is closed by its owner and answers `isReady()` false afterwards.

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

<!-- test: a-let-sample-timer-is-closed -->
<!-- unsupported-targets: wasm32-wasi -->
wasm32-wasi declares no SampleTimer, so the call is refused with E3004 (call to undefined function 'SampleTimer.create'), not E3104.

A `SampleTimer`'s `close()` writes it — it marks the timer closed, and on Windows releases its timer handle — and a
`let` timer is never reassigned, so `close()` is accepted.

```maxon
function main() returns ExitCode
	let timer = SampleTimer.create(1000000)
	timer.close()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-var-process-trace-is-closed-by-its-owner -->
A `ProcessTrace` frees its scratch pages in `close()`: a trace is closed by its owner and answers `isReady()` false afterwards.

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
