---
feature: socket-inheritance
status: experimental
keywords: [socket, tcp, listener, accept, subprocess, spawn, inherit, handle, close-on-exec, fin]
category: network
---

# A socket never outlives its close in a child the program spawned

## Documentation

Every socket the runtime opens is private to the process that opened it: the socket `TcpClient.connect`
creates, the listener `TcpListener.bind` creates, and the connection `TcpListener.accept` answers. A child
the program spawns afterwards receives none of them. So `close()` ends the connection at once, whatever
children are still running — the peer reading to the end of the connection sees that end when the socket
is closed, not when the last child that could have held a copy of it exits.

## Tests

<!-- test: socket-inheritance.an-accepted-socket-closes-while-a-spawned-child-lives -->
<!-- unsupported-targets: wasm32-wasi -->
The server accepts a connection, spawns a child that lives for three seconds, then closes the accepted
socket. The client, reading to the end of the connection, must see that end well inside the child's
lifetime: a child holding a copy of the accepted socket would keep the connection open until it exits.
```maxon
typealias StringArray = Array with String

let ChildLifetimeMs = 3000
let PromptEndMs = 1500

function sleepingChild() returns ExitCode
	sleep(ChildLifetimeMs)
	return 0
end 'sleepingChild'

function readsItsEndPromptly(port NetworkPort) returns bool
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return false
	let started = Clock.nowMs()
	let heard = try client.recv(64) otherwise (e) 'ended'
		return e == NetworkError.connectionClosed and Clock.elapsedMs(started) < PromptEndMs
	end 'ended'

	print("unexpected bytes '{heard}'\n")
	return false
end 'readsItsEndPromptly'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return sleepingChild()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 3
	let reader = async readsItsEndPromptly(listener.port())
	let conn = try listener.accept() otherwise return 4

	var argv = StringArray.create()
	argv.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: argv) otherwise return 5
	conn.close()

	let endedPromptly = await reader
	let code = try child.wait() otherwise return 6
	child.release()

	print("ended-promptly={endedPromptly} child={code}\n")
	return 0
end 'main'
```
```stdout
ended-promptly=true child=0
```
```exitcode
0
```

<!-- test: socket-inheritance.a-connected-socket-closes-while-a-spawned-child-lives -->
<!-- unsupported-targets: wasm32-wasi -->
The mirror of the case above, for the socket `TcpClient.connect` creates rather than the one `accept`
answers. The server spawns a child that lives for three seconds and signals the client, which closes its
socket; the server, reading to the end of the connection, must see that end well inside the child's
lifetime.
```maxon
typealias StringArray = Array with String

let ChildLifetimeMs = 3000
let PromptEndMs = 1500

function sleepingChild() returns ExitCode
	sleep(ChildLifetimeMs)
	return 0
end 'sleepingChild'

function closesOnSignal(port NetworkPort) returns bool
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return false
	let signal = try client.recv(1) otherwise return false
	client.close()

	return signal == "!"
end 'closesOnSignal'

function readsItsEndPromptly(conn TcpClient) returns bool
	let started = Clock.nowMs()
	let heard = try conn.recv(64) otherwise (e) 'ended'
		return e == NetworkError.connectionClosed and Clock.elapsedMs(started) < PromptEndMs
	end 'ended'

	print("unexpected bytes '{heard}'\n")
	return false
end 'readsItsEndPromptly'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return sleepingChild()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 3
	let closer = async closesOnSignal(listener.port())
	let conn = try listener.accept() otherwise return 4

	var argv = StringArray.create()
	argv.push("child")
	var child = try StreamingSubprocess.spawn(Executable.path(me), arguments: argv) otherwise return 5
	_ = try conn.send("!") otherwise return 6

	let endedPromptly = readsItsEndPromptly(conn)
	let signalled = await closer
	let code = try child.wait() otherwise return 7
	child.release()

	print("signalled={signalled} ended-promptly={endedPromptly} child={code}\n")
	return 0
end 'main'
```
```stdout
signalled=true ended-promptly=true child=0
```
```exitcode
0
```
