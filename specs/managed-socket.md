---
feature: managed-socket
status: experimental
keywords: [socket, tcp, managed, __ManagedSocket, RAII, handle, network]
category: type-system
---

# __ManagedSocket

## Documentation

### Overview

`__ManagedSocket` is a compiler builtin type that wraps an OS socket handle with automatic cleanup via a destructor when the last reference goes out of scope.

### Type Structure

`__ManagedSocket` has a single field:
- `_handle` (int) — The raw OS socket handle

### Static Methods

- `__ManagedSocket.tcpConnect(managed, port)` — Resolves the hostname and connects a TCP socket. Throws `__ManagedSocketError.resolveFailed` if DNS resolution fails, `connectFailed` if the connection is refused.

### Instance Methods

- `sendFrom(managed, offset, length)` — Sends bytes from managed memory at the given offset. Throws `__ManagedSocketError.bufferOutOfBounds` if `offset + length > capacity`, `sendFailed` on OS error.
- `recv(managed)` — Receives up to `managed.capacity` bytes. Returns 0 if the peer closed gracefully. Throws `recvFailed` on OS error.
- `close()` — Explicitly closes the socket handle. Idempotent. Also called automatically via destructor. Does not throw.

## Tests

<!-- test: managed-socket.error-direct-construction -->
```maxon
function main() returns ExitCode
	let s = __ManagedSocket{_handle: 0}
	return 0
end 'main'
```
```maxoncstderr
error E3072: specs/fragments/managed-socket/managed-socket.error-direct-construction.test:3:26: '__ManagedSocket' is a compiler builtin type and cannot be constructed directly
```

<!-- test: managed-socket.connect-bad-host -->
⚠ The `default` arm says *unreachable*, and in a function declaring no `throws` it has to say so with
`panic` rather than `throws`: `main` has no error channel, so a `default throws` here is silently discarded —
and with a payload-carrying error it leaks the box (measured: `exit 101`). E3059 refuses it (A1s-throwsbox
review).
```maxon
function main() returns ExitCode
	let host = "no-such-host-xyz-98765.invalid"
	try __ManagedSocket.tcpConnect(host.toByteArray().managed, port: 80) otherwise (e) 'connErr'
		match e 'check'
			resolveFailed then print("resolve failed")
			default panic("unreachable: tcpConnect reports only resolveFailed or connectFailed")
		end 'check'
		return 42
	end 'connErr'
	return 0
end 'main'
```
```exitcode
42
```
```stdout
resolve failed
```

<!-- test: managed-socket.connect-refused -->
⚠ The `default` arm says *unreachable*, and in a function declaring no `throws` it has to say so with
`panic` rather than `throws`: `main` has no error channel, so a `default throws` here is silently discarded —
and with a payload-carrying error it leaks the box (measured: `exit 101`). E3059 refuses it (A1s-throwsbox
review).
```maxon
function main() returns ExitCode
	let host = "192.0.2.1"
	try __ManagedSocket.tcpConnect(host.toByteArray().managed, port: 1) otherwise (e) 'connErr'
		match e 'check'
			connectFailed then print("connect failed")
			default panic("unreachable: tcpConnect reports only resolveFailed or connectFailed")
		end 'check'
		return 42
	end 'connErr'
	return 0
end 'main'
```
```exitcode
42
```
```stdout
connect failed
```
