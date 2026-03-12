---
feature: async-tcp
status: experimental
keywords: [async, await, tcp, network, concurrency]
category: concurrency
---

# Async TCP Networking

## Documentation

Network I/O operations (`TcpClient.connect`, `send`, `recv`, `close`) are non-blocking when called from green threads. They delegate to the sync worker thread, allowing other green threads to run while waiting for network operations to complete.

This enables `async`/`await` with TCP networking:

```text
function echo() returns ExitCode throws NetworkError
  let client = try TcpClient.connect("host", port: 4242)
  let _ = try client.send("Hello\n")
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
function connect() returns ExitCode throws NetworkError
  let _ = try TcpClient.connect("192.0.2.1", port: 1)
  return 0
end 'connect'

function main() returns ExitCode
  var p = async connect()
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
  let _ = try TcpClient.connect("this.host.does.not.exist.invalid", port: 80)
  return 0
end 'resolve'

function main() returns ExitCode
  var p = async resolve()
  let result = try await p otherwise 42
  return result
end 'main'
```
```exitcode
42
```

<!-- test: async-tcp.trace-connect-error -->
<!-- AsyncTrace -->
Verify that async network connect yields and resumes the green thread.
```maxon
function connect() returns ExitCode throws NetworkError
  let _ = try TcpClient.connect("192.0.2.1", port: 1)
  return 0
end 'connect'

function main() returns ExitCode
  var p = async connect()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
99
```
```stderr
spawn #1
io_yield #1 [net_connect]
io_resume #1 [net_connect]
try_await #1 [yield]
```

<!-- test: async-tcp.trace-mixed-io -->
<!-- AsyncTrace -->
Verify that mixed file and network I/O shows distinct operation names in the trace.
```maxon
function mixedIo() returns ExitCode throws NetworkError
  let _ = File.exists(FilePath from "nofile.txt")
  let _ = try TcpClient.connect("192.0.2.1", port: 1)
  return 0
end 'mixedIo'

function main() returns ExitCode
  var p = async mixedIo()
  let result = try await p otherwise 99
  return result
end 'main'
```
```exitcode
99
```
```stderr
spawn #1
io_yield #1 [file_exists]
io_resume #1 [file_exists]
io_yield #1 [net_connect]
io_resume #1 [net_connect]
try_await #1 [yield]
```

<!-- test: async-tcp.echo -->
```maxon
function echo() returns ExitCode throws NetworkError
  let client = try TcpClient.connect("tcpbin.com", port: 4242)
  let _ = try client.send("Hello Async\n")
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
```exitcode
0
```
```stdout
Hello Async
```
