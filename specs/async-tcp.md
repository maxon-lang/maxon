---
feature: async-tcp
status: experimental
keywords: [async, await, tcp, network, concurrency]
category: concurrency
---

# Async TCP Networking

## Documentation

Network I/O operations (`TcpClient.connect`, `send`, `recv`, `close`) are non-blocking when called from green threads. The descriptor itself is non-blocking and registered with the scheduler's network poller, so a call that cannot finish at once PARKS its green thread on that descriptor and hands the processor back; the poller readies it when the descriptor becomes readable or writable. A green thread waiting for a peer therefore holds no machine at all. (`specs/netpoll-socket.md` is that mechanism's own oracle.)

This enables `async`/`await` with TCP networking:

```text
function echo() returns ExitCode throws NetworkError
  let client = try TcpClient.connect("host", port: 4242)
  _ = try client.send("Hello\n")
  let response = try client.recv(1024)
  print(response)
  return 0
end 'echo'

function main() returns ExitCode
  var p = async echo()
  let result = try await p otherwise 1
  return result
end 'main'
```

## Tests

<!-- test: async-tcp.connect-error -->
```maxon
function tryConnect() returns ExitCode throws NetworkError
	_ = try TcpClient.connect("192.0.2.1", port: 1)
	return 0
end 'tryConnect'

function main() returns ExitCode
	let p = async tryConnect()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
99
```

<!-- test: async-tcp.resolve-error -->
```maxon
function resolve() returns ExitCode throws NetworkError
	_ = try TcpClient.connect("this.host.does.not.exist.invalid", port: 80)
	return 0
end 'resolve'

function main() returns ExitCode
	let p = async resolve()
	let result = try await p otherwise 42
	return result
end 'main'
```
```exitcode
42
```

<!-- test: async-tcp.trace-connect-error -->
<!-- AsyncTrace -->
Verify that a connect to a HOSTNAME parks twice, once to resolve the name and once to dial it, and that
its failure still reaches `try await`. `localhost` is a name, so the listener's bind parks on the resolver
too. That listener exists only to hand out a free port, and it is closed before the dial, so the dial is
refused without a live host.
```maxon
function connect(port NetworkPort) returns ExitCode throws NetworkError
	_ = try TcpClient.connect("localhost", port: port)
	return 0
end 'connect'

function main() returns ExitCode
	let listener = try TcpListener.bind("localhost", port: 0) otherwise return 1
	let port = listener.port()
	listener.close()

	let p = async connect(port)
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
99
```
```stderr
io_yield #0 [net_listen]
io_resume #0 [net_listen]
io_yield #0 [net_close]
io_resume #0 [net_close]
spawn #1
io_yield #1 [net_connect]
io_resume #1 [net_connect]
io_yield #1 [net_connect]
io_resume #1 [net_connect]
try_await #1 [yield]
```

<!-- test: async-tcp.trace-mixed-io -->
<!-- AsyncTrace -->
Verify that mixed file and network I/O shows distinct operation names in the trace. The peer is the
program's own listener, which never accepts: the kernel completes the handshake into its backlog, so the
dial parks and succeeds with no live host. Both addresses are literals, which wait on no resolver, so the
bind does not park and the connect parks once, for the dial.
```maxon
function mixedIo(port NetworkPort) returns ExitCode throws NetworkError
	_ = File.exists(FilePath from "nofile.txt")
	_ = try TcpClient.connect("127.0.0.1", port: port)
	return 0
end 'mixedIo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let p = async mixedIo(listener.port())
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```
```stderr
spawn #1
io_yield #1 [file_exists]
io_resume #1 [file_exists]
io_yield #1 [net_connect]
io_resume #1 [net_connect]
io_yield #1 [net_close]
io_resume #1 [net_close]
try_await #1 [yield]
io_yield #0 [net_close]
io_resume #0 [net_close]
```

<!-- test: async-tcp.echo -->
<!-- network: live -->
```maxon
function echo() returns ExitCode throws NetworkError
	let client = try TcpClient.connect("tcpbin.com", port: 4242)
	_ = try client.send("Hello Async\n")
	let response = try client.recv(1024)
	print(response)
	return 0
end 'echo'

function main() returns ExitCode
	let p = async echo()
	let result = try await p otherwise 1
	return result
end 'main'
```
```exitcode
0
```
```stdout
Hello Async
```
