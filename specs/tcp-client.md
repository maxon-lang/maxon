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
- `NetworkPort` — Typed range for valid port numbers (1 to 65535)

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
```maxon
function main() returns ExitCode
	if let client = try TcpClient.connect("192.0.2.1", port: 1) 'ok'
		return 1
	end 'ok' else (e) 'err'
		match e 'check'
			connectFailed then return 0
			default throws NetworkError.resolveFailed
		end 'check'
	end 'err'
end 'main'
```
```exitcode
0
```

<!-- test: tcp-client.resolve-error -->
```maxon
function main() returns ExitCode
	if let client = try TcpClient.connect("this.host.does.not.exist.invalid", port: 1) 'ok'
		return 1
	end 'ok' else (e) 'err'
		match e 'check'
			resolveFailed then return 0
			default throws NetworkError.connectFailed
		end 'check'
	end 'err'
end 'main'
```
```exitcode
0
```
