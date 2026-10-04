---
feature: tcp-client
status: experimental
keywords: tcp, network, socket, client, connect
category: system
---
# TcpClient

## Documentation

TCP client networking with automatic resource cleanup via the managed memory system.

**Types:**
- `TcpClient` — TCP client connection that auto-closes when it goes out of scope
- `NetworkError` — Error enum for network operations
- `NetworkPort` — Typed range for port numbers (0 to 65535; `0` is what `TcpListener.bind` asks for when it wants an ephemeral port)

**NetworkError cases:**
- `resolveFailed` — DNS resolution failed
- `connectFailed` — TCP connection failed
- `sendFailed` — Send operation failed
- `recvFailed` — Receive operation failed
- `connectionClosed` — Remote peer closed the connection

**API:**

```text
// Connect to a TCP server
let client = try TcpClient.connect("hostname", port: 4242)

// Send a string
let bytesSent = try client.send("Hello\n")

// Receive up to 1024 bytes
let response = try client.recv(1024)

// Explicit close (also happens automatically on scope exit)
client.close()
```

**Automatic cleanup:** `TcpClient` wraps a `__ManagedSocket` builtin type. When the last
reference goes out of scope, the socket is automatically closed via the destructor mechanism.

## Tests

<!-- test: tcp-client.echo -->
<!-- network: live -->
```maxon
function runEcho() returns ExitCode throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
	let msg = "Hello Maxon\n"
	_ = try client.send(msg)
	let response = try client.recv(1024)
	print(response)
	return 0
end 'runEcho'

function main() returns ExitCode
	let result = try runEcho() otherwise 1
	return result
end 'main'
```
```exitcode
0
```
```stdout
Hello Maxon
```

<!-- test: tcp-client.connect-error -->
⚠ The `default` arm says *unreachable*, and in a function declaring no `throws` it has to say so with
`panic` rather than `throws`: `main` has no error channel, so a `default throws` here is silently discarded —
and with a payload-carrying error it leaks the box (`exit 101`). E3059 refuses it.
```maxon
function main() returns ExitCode
	if let client = try TcpClient.connect("192.0.2.1", port: 1) 'ok'
		return 1
	end 'ok' else (e) 'err'
		match e 'check'
			connectFailed then return 0
			default panic("unreachable: TcpClient.connect reports only resolveFailed or connectFailed")
		end 'check'
	end 'err'
end 'main'
```
```exitcode
0
```

<!-- test: tcp-client.resolve-error -->
⚠ The `default` arm says *unreachable*, and in a function declaring no `throws` it has to say so with
`panic` rather than `throws`: `main` has no error channel, so a `default throws` here is silently discarded —
and with a payload-carrying error it leaks the box (`exit 101`). E3059 refuses it.
```maxon
function main() returns ExitCode
	if let client = try TcpClient.connect("this.host.does.not.exist.invalid", port: 1) 'ok'
		return 1
	end 'ok' else (e) 'err'
		match e 'check'
			resolveFailed then return 0
			default panic("unreachable: TcpClient.connect reports only resolveFailed or connectFailed")
		end 'check'
	end 'err'
end 'main'
```
```exitcode
0
```

<!-- test: tcp-client.a-host-holding-a-nul-resolves-to-nothing-not-its-prefix -->
A host holding a NUL names no host on any lane: the connect fails to resolve rather than reaching the host
the bytes before the NUL name, or the local machine an empty name means to some resolvers. The listener
the prefix names is live, so a connect that reached it would answer `connected`.
```maxon
function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 2

	if let client = try TcpClient.connect("127.0.0.1\0junk", port: listener.port()) 'ok'
		print("connected\n")
	end 'ok' else (e) 'err'
		print("refused {e.name}\n")
	end 'err'

	return 0
end 'main'
```
```stdout
refused resolveFailed
```
```exitcode
0
```

<!-- test: tcp-client.a-listener-host-holding-a-nul-resolves-to-nothing-not-its-prefix -->
A listener's host holding a NUL names no address on any lane: the bind fails to resolve rather than binding
the address the bytes before the NUL name, or every local address an empty name means to some resolvers.
```maxon
function main() returns ExitCode
	if let listener = try TcpListener.bind("127.0.0.1\0junk", port: 0) 'ok'
		print("bound {listener.port() > 0}\n")
	end 'ok' else (e) 'err'
		print("refused {e.name}\n")
	end 'err'

	return 0
end 'main'
```
```stdout
refused resolveFailed
```
```exitcode
0
```

<!-- test: tcp-client.a-non-ascii-host-name-resolves-through-its-unicode-form -->
<!-- network: live -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
A host name holding non-ASCII characters reaches the Windows resolver as the characters it spells, so
`münchen.de` resolves as its IDNA form `xn--mnchen-3ya.de` does; the ASCII form is the control that proves
the network answers. The other lanes pass the name's bytes to the resolver unchanged and no POSIX resolver
applies IDNA, so the case is about the Windows code-page boundary.
```maxon
let ConnectDeadlineMs = 10000 as SocketDeadlineMs

function report(label String, host String)
	if let client = try TcpClient.connect(host, port: 80, deadlineMs: ConnectDeadlineMs) 'ok'
		client.close()
		print("{label}: connected\n")
	end 'ok' else (e) 'err'
		print("{label}: refused {e.name}\n")
	end 'err'
end 'report'

function main() returns ExitCode
	report("ascii form", host: "xn--mnchen-3ya.de")
	report("unicode form", host: "münchen.de")
	return 0
end 'main'
```
```stdout
ascii form: connected
unicode form: connected
```
```exitcode
0
```
