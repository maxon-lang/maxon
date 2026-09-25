---
feature: http-server
status: experimental
keywords: [http, server, listener, request, response, network]
category: network
---

# HttpServer

## Documentation

HTTP/1.1 server for answering requests over TCP connections, on the same green-thread scheduler
`TcpListener` and `HttpClient` run on.

**Types:**
- `HttpServer` — a bound listener that hands out one parsed request at a time
- `HttpExchange` — one accepted connection: the request that arrived and the response owed for it
- `HttpHandler` — the interface `serve` calls for each exchange
- `HttpServerLimits` — the caps a server parses under
- `HttpServerError` — error enum for server operations

**Quick usage:**

```text
type HelloHandler implements HttpHandler
	let greeting as String

	static function create(greeting String) returns HelloHandler
		return HelloHandler{greeting: greeting}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: greeting, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		printError("{fault.describe()}\n")
	end 'faulted'
end 'HelloHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 8080) otherwise return 1
	_ = server.serve(HelloHandler.create("hello\n"))

	return 0
end 'main'
```

**Answering one request at a time:**

```text
function serveOne(server HttpServer) returns ExitCode throws HttpServerError
	let exchange = try server.accept()
	let request = exchange.request()
	try exchange.respondWith(StatusCode.ok, body: request.url().path(), contentType: "text/plain")

	return 0
end 'serveOne'
```

**The HTTP/1.1 subset this server implements:**
- `Content-Length` bodies only. A request carrying `Transfer-Encoding` is answered `501`
- Every response carries `Connection: close`, and the connection is closed after it. There is no
  keep-alive, so one connection is one request
- No TLS, and no `Expect: 100-continue`
- The request `url()` is built as `http://<Host><target>`, so `path()` and `query()` read the target

**Limits, and the status each one answers with.** `HttpServerLimits.create()` is `maxHeaderBytes`
16384, `maxBodyBytes` 1048576, `readDeadlineMs` 10000. A protocol error is ANSWERED on the connection
and then THROWN from `accept()`:

| condition | status | `HttpServerError` |
|---|---|---|
| no request line, a header line with no `:`, no `Host`, a target that does not begin with `/`, or a `Host` or `Content-Length` repeated with a different value | 400 | `malformedRequest` |
| a header block longer than `maxHeaderBytes` | 431 | `headersTooLarge` |
| `Content-Length` above `maxBodyBytes` | 413 | `bodyTooLarge` |
| `Transfer-Encoding` present | 501 | `unsupportedTransferEncoding` |
| nothing readable within `readDeadlineMs` | 408 | `readTimedOut` |

`serve` catches all five, answers them the same way, and goes on accepting. A handler that THROWS
answers `500` and the loop continues. `close()` makes a parked or later `accept()` throw
`listenerClosed` and ends `serve`.

**A header line without a colon is malformed on BOTH sides.** The client shares this server's header
parsing, so `HttpClient` reading a response that carries such a line throws `HttpError.invalidResponse`
rather than dropping the line.

## Tests

### A GET round trip

The floor every other case stands on: a server green thread bound on a kernel-chosen loopback port, the
real `HttpClient` as the peer, and the method, path and query the handler read reported back through the
body.

<!-- test: http-server.get-round-trip -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise return 1
	let request = exchange.request()
	let query = try request.url().query() otherwise "none"
	let described = "{request.method().name} {request.url().path()} {query}"

	try exchange.respondWith(StatusCode.ok, body: described, contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)

	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/hello?q=1") otherwise return 2
	let served = await peer
	let connection = try response.header("connection") otherwise "absent"

	print("served={served} status={response.statusCode().rawValue} body={response.body()} connection={connection}\n")
	server.close()

	return 0
end 'main'
```
```stdout
served=3 status=200 body=get /hello q=1 connection=close
```
```exitcode
0
```

### A POST body arrives whole

⚠ **A BODY LARGER THAN ONE READ IS THE POINT.** A server that answered after its first `recv` would be
right about every short body and wrong about every real one, so the handler answers the byte COUNT it
received and the case pins it against the count the client sent.

<!-- test: http-server.post-body-arrives-whole -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)
typealias BodyBytes = int(0 to 1000000)

let ChunkText = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123"
let ChunkCount = 320

function bigBody() returns String
	var body = ""
	var written = 0

	while written < ChunkCount 'fill'
		body.append(ChunkText)
		written = written + 1
	end 'fill'

	return body
end 'bigBody'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise return 1
	let received = exchange.request().body().byteLength() as BodyBytes

	try exchange.respondWith(StatusCode.ok, body: "{received}", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)

	let sent = bigBody()
	let response = try HttpClient.post("http://127.0.0.1:{server.port()}/upload", body: sent) otherwise return 2
	let served = await peer
	let agreed = response.body() == "{sent.byteLength()}"

	print("served={served} status={response.statusCode().rawValue} agreed={agreed} sent={sent.byteLength()}\n")
	server.close()

	return 0
end 'main'
```
```stdout
served=3 status=200 agreed=true sent=21120
```
```exitcode
0
```

### Request headers are case-insensitive

<!-- test: http-server.request-headers-are-case-insensitive -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise return 1
	let headers = exchange.request().headers()
	let lower = try headers.get("x-maxon-tag") otherwise "absent"
	let upper = try headers.get("X-MAXON-TAG") otherwise "absent"

	try exchange.respondWith(StatusCode.ok, body: "{lower}/{upper}", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)

	var request = try HttpRequest.create(HttpMethod.get, url: "http://127.0.0.1:{server.port()}/tagged") otherwise return 2
	request.setHeader("X-Maxon-Tag", value: "mixed")

	let response = try HttpClient.send(request) otherwise return 3
	let served = await peer

	print("served={served} body={response.body()}\n")
	server.close()

	return 0
end 'main'
```
```stdout
served=3 body=mixed/mixed
```
```exitcode
0
```

### A throwing handler answers 500 and the server serves on

⛔ **A SERVER THAT DIED ON A HANDLER'S ERROR WOULD PASS A ONE-REQUEST CASE.** The second request is the
assertion: the first handler throws, and only a loop that survived it can answer the second.

<!-- test: http-server.a-throwing-handler-answers-500-and-serves-on -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let BoomPath = "/boom"

type PickyHandler implements HttpHandler
	var label as String

	static function create(label String) returns PickyHandler
		return PickyHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		if exchange.request().url().path() == BoomPath 'refuses'
			throw HttpServerError.sendFailed
		end 'refuses'

		try exchange.respondWith(StatusCode.ok, body: label, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'PickyHandler'

function serveUntilClosed(server HttpServer, handler PickyHandler) returns Served
	_ = server.serve(handler)

	return 3
end 'serveUntilClosed'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let handler = PickyHandler.create("second")
	let peer = async serveUntilClosed(server, handler: handler)
	let port = server.port()

	let first = try HttpClient.get("http://127.0.0.1:{port}{BoomPath}") otherwise return 2
	let second = try HttpClient.get("http://127.0.0.1:{port}/fine") otherwise return 3

	server.close()
	let served = await peer

	print("first={first.statusCode().rawValue} second={second.statusCode().rawValue} body={second.body()} served={served}\n")

	return 0
end 'main'
```
```stdout
faulted GET /boom failed with sendFailed and was answered 500
first=500 second=200 body=second served=3
```
```exitcode
0
```

<!-- test: http-server.a-handler-that-throws-after-responding-is-reported-and-not-answered-twice -->
<!-- procs: 1 -->
```maxon
let LatePath = "/late"

type LateThrowingHandler implements HttpHandler
	var label as String

	static function create(label String) returns LateThrowingHandler
		return LateThrowingHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: label, contentType: "text/plain")

		if exchange.request().url().path() == LatePath 'thenFails'
			throw HttpServerError.sendFailed
		end 'thenFails'
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'LateThrowingHandler'

function serveUntilClosed(server HttpServer, handler LateThrowingHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveUntilClosed'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let handler = LateThrowingHandler.create("answered")
	let peer = async serveUntilClosed(server, handler: handler)
	let port = server.port()

	let first = try HttpClient.get("http://127.0.0.1:{port}{LatePath}") otherwise return 2
	let second = try HttpClient.get("http://127.0.0.1:{port}/fine") otherwise return 3

	server.close()
	let ended = await peer

	print("first={first.statusCode().rawValue} body={first.body()} second={second.statusCode().rawValue} ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
faulted GET /late failed with sendFailed after its answer was sent
first=200 body=answered second=200 ended=closed
```
```exitcode
0
```

<!-- test: http-server.a-second-response-is-refused -->
<!-- procs: 1 -->
```maxon
function answerTwice(server HttpServer) returns String
	let exchange = try server.accept() otherwise return "accept-failed"
	try exchange.respondWith(StatusCode.ok, body: "first", contentType: "text/plain") otherwise return "first-failed"

	try exchange.respondWith(StatusCode.ok, body: "second", contentType: "text/plain") otherwise (e) 'refused'
		return e.name
	end 'refused'

	return "answered-twice"
end 'answerTwice'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async answerTwice(server)

	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/") otherwise return 2
	let second = await peer
	server.close()

	print("status={response.statusCode().rawValue} body={response.body()} second={second}\n")

	return 0
end 'main'
```
```stdout
status=200 body=first second=alreadyResponded
```
```exitcode
0
```

<!-- test: http-server.an-accept-deadline-ends-serve -->
<!-- procs: 1 -->
```maxon
let promptMs = 3000

type EchoPathHandler implements HttpHandler
	var label as String

	static function create(label String) returns EchoPathHandler
		return EchoPathHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: "{label}{exchange.request().url().path()}", contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoPathHandler'

function serveUntilClosed(server HttpServer, handler EchoPathHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveUntilClosed'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let handler = EchoPathHandler.create("echo")
	try server.setAcceptDeadline(300) otherwise return 2

	let start = Clock.nowMs()
	let idle = server.serve(handler)
	let tookMs = Clock.elapsedMs(start)

	try server.setAcceptDeadline(0) otherwise return 3
	let peer = async serveUntilClosed(server, handler: handler)
	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/again") otherwise return 4
	server.close()
	let ended = await peer

	print("idle={idle.name} prompt={tookMs < promptMs} body={response.body()} ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
idle=acceptDeadlinePassed prompt=true body=echo/again ended=closed
```
```exitcode
0
```

<!-- test: http-server.repeated-request-headers-are-combined-in-order -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function acceptedHeader(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let accepted = try exchange.request().headers().get("accept") otherwise "absent"
	try exchange.respondWith(StatusCode.ok, body: accepted, contentType: "text/plain") otherwise return "respond-failed"

	return accepted
end 'acceptedHeader'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async acceptedHeader(server)

	let client = try TcpClient.connect("127.0.0.1", port: server.port()) otherwise return 2
	_ = try client.send("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nAccept: text/html\r\nX-Other: 1\r\nACCEPT: text/plain\r\n\r\n") otherwise return 3
	_ = try client.recv(ReadBufferBytes) otherwise return 4
	client.close()

	let seen = await peer
	server.close()

	print("accept={seen}\n")

	return 0
end 'main'
```
```stdout
accept=text/html, text/plain
```
```exitcode
0
```

### A header block past the cap answers 431

⚠ **THE CLIENT HERE IS A RAW SOCKET, BECAUSE `HttpClient` CANNOT PRODUCE THIS INPUT.** The malformed
and oversize requests below are all ones a conforming client will not send, so the peer writes the
bytes itself and reads the status code back out of the status line as text. The status code is the only
thing pinned; the reason phrase is not, because a phrase is prose and a code is the protocol.

<!-- test: http-server.oversize-headers-answer-431 -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)
typealias PadCount = int(0 to 1000)

let HeaderCap = 4096
let PadLines = 400
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function oversizeHeaderBlock() returns String
	var block = "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\n"
	var written = 0 as PadCount

	while written < PadLines 'pad'
		block.append("X-Pad-{written}: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n")
		written = written + 1
	end 'pad'

	block.append("\r\n")

	return block
end 'oversizeHeaderBlock'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.maxHeaderBytes = HeaderCap
	server.setLimits(limits)

	let port = server.port()
	let peer = async serveOne(server)
	let status = shout(port, block: oversizeHeaderBlock())
	let served = await peer

	print("status={status} served={served}\n")

	return 0
end 'main'
```
```stdout
accept threw headersTooLarge
status=431 served=1
```
```exitcode
0
```

### A Content-Length past the cap answers 413

The cap is checked against the DECLARED length, before the body is read, so a client that announced two
megabytes and sent four bytes is still refused.

<!-- test: http-server.oversize-body-answers-413 -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let DeclaredBodyBytes = 2000000
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let peer = async serveOne(server)
	let status = shout(port, block: "POST /upload HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: {DeclaredBodyBytes}\r\n\r\nabcd")
	let served = await peer

	print("status={status} served={served}\n")

	return 0
end 'main'
```
```stdout
accept threw bodyTooLarge
status=413 served=1
```
```exitcode
0
```

### A chunked request answers 501

<!-- test: http-server.chunked-answers-501 -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let peer = async serveOne(server)
	let status = shout(port, block: "POST /upload HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n\r\n4\r\nabcd\r\n0\r\n\r\n")
	let served = await peer

	print("status={status} served={served}\n")

	return 0
end 'main'
```
```stdout
accept threw unsupportedTransferEncoding
status=501 served=1
```
```exitcode
0
```

### A silent client answers 408 and the server serves on

⛔ **THE SECOND ACCEPT IS THE ASSERTION.** A read deadline that took the LISTENER down with the silent
connection would answer 408 exactly as this does and serve nobody afterwards, so the case opens a second
connection and requires a real answer on it.

<!-- test: http-server.a-silent-client-answers-408-and-serves-on -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let SilentReadDeadlineMs = 300
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function saySilence(port NetworkPort) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'saySilence'

function serveTwo(server HttpServer) returns Served
	let refused = try server.accept() otherwise (e) 'timedOut'
		print("accept threw {e.name}\n")

		let exchange = try server.accept() otherwise return 1
		try exchange.respondWith(StatusCode.ok, body: "second", contentType: "text/plain") otherwise return 2

		return 3
	end 'timedOut'

	try refused.respondWith(StatusCode.ok, body: "unexpected", contentType: "text/plain") otherwise return 4

	return 5
end 'serveTwo'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.readDeadlineMs = SilentReadDeadlineMs
	server.setLimits(limits)

	let port = server.port()
	let peer = async serveTwo(server)

	let silent = saySilence(port)
	let response = try HttpClient.get("http://127.0.0.1:{port}/after") otherwise return 2
	let served = await peer

	print("silent={silent} second={response.statusCode().rawValue} body={response.body()} served={served}\n")

	return 0
end 'main'
```
```stdout
accept threw readTimedOut
silent=408 second=200 body=second served=3
```
```exitcode
0
```

### A malformed request line answers 400

<!-- test: http-server.a-malformed-request-line-answers-400 -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let peer = async serveOne(server)
	let status = shout(port, block: "GARBAGE\r\n\r\n")
	let served = await peer

	print("status={status} served={served}\n")

	return 0
end 'main'
```
```stdout
accept threw malformedRequest
status=400 served=1
```
```exitcode
0
```

### A header line without a colon is malformed, on both sides

⭐ **ONE PROGRAM, TWO DIRECTIONS.** The shared header parser is what makes this one change: a request
carrying such a line is `malformedRequest` and a RESPONSE carrying one is `HttpError.invalidResponse`.
A case that only asserted the server half would leave the client free to go on dropping the line.

<!-- test: http-server.a-header-line-without-a-colon-is-malformed -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function answerWithABadHeaderLine(listener TcpListener) returns Served
	let connection = try listener.accept() otherwise return 1
	_ = try connection.recv(ReadBufferBytes) otherwise return 2
	_ = try connection.send("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nBadHeaderLine\r\n\r\nhi") otherwise return 3
	connection.close()

	return 3
end 'answerWithABadHeaderLine'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let serverPort = server.port()
	let serverPeer = async serveOne(server)
	let status = shout(serverPort, block: "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nBadHeaderLine\r\n\r\n")
	let served = await serverPeer
	server.close()

	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 2
	let clientPeer = async answerWithABadHeaderLine(listener)

	var clientSaw = "accepted-a-bad-header-line"

	if let response = try HttpClient.get("http://127.0.0.1:{listener.port()}/") 'answered'
		clientSaw = "status {response.statusCode().rawValue}"
	end 'answered' else (e) 'refused'
		clientSaw = e.name
	end 'refused'

	let answered = await clientPeer
	listener.close()

	print("status={status} served={served} clientSaw={clientSaw} answered={answered}\n")

	return 0
end 'main'
```
```stdout
accept threw malformedRequest
status=400 served=1 clientSaw=invalidResponse answered=3
```
```exitcode
0
```

### A head block sent one byte at a time is parsed

The terminator is searched for only in what arrived since the last read, plus the three bytes before it,
so a `\r\n\r\n` split across reads must still be found. The client sends every character of the head as
its own `send` (a `\r\n` is one character, so the terminator arrives as two), and yields between them so
the server's reads come in pieces.

<!-- test: http-server.a-head-block-sent-one-byte-at-a-time-is-parsed -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let ReadBufferBytes = 4096
let TrickledHead = "GET /trickled HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Note: split\r\n\r\n"

function trickle(port NetworkPort) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"

	for b in TrickledHead.toByteArray() 'eachByte'
		var one = ByteArray.create()
		one.push(b)
		_ = try client.send(String.from(one)) otherwise return "send-failed"
		Scheduler.yield()
	end 'eachByte'

	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'trickle'

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise (e) 'refused'
		print("accept threw {e.name}\n")
		return 1
	end 'refused'

	let request = exchange.request()
	let note = try request.headers().get("x-note") otherwise "absent"
	print("path={request.url().path()} note={note}\n")

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)
	let status = trickle(server.port())
	let served = await peer
	server.close()

	print("status={status} served={served}\n")

	return 0
end 'main'
```
```stdout
path=/trickled note=split
status=200 served=3
```
```exitcode
0
```

### A request the server cannot read one way answers 400, and a head past the cap answers 431

A request with no `Host`, a target that is not a path, and a `Content-Length` spelled twice with two
values each leave the server no single reading of the request, so each is refused. A `Host` repeated
with the SAME value has one reading and is served. A head block whose terminator arrives in the same read
that crosses the cap is still over the cap.

<!-- test: http-server.an-ambiguous-request-answers-400 -->
<!-- procs: 1 -->
```maxon
let HeadCap = 160
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return "respond-failed"

	return "served"
end 'acceptedAs'

function paddedHead() returns String
	var head = "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Pad: "

	while head.byteLength() <= HeadCap 'pad'
		head.append("aaaaaaaaaa")
	end 'pad'

	head.append("\r\n\r\n")

	return head
end 'paddedHead'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.maxHeaderBytes = HeadCap
	server.setLimits(limits)

	let port = server.port()
	let blocks = ["GET / HTTP/1.1\r\n\r\n", "GET @127.0.0.1/ HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", "POST / HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\nContent-Length: 3\r\n\r\nhi", "GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nHost: 127.0.0.1\r\n\r\n", paddedHead()]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	return 0
end 'main'
```
```stdout
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=431 accept=headersTooLarge
```
```exitcode
0
```

### close() ends serve

<!-- test: http-server.close-ends-serve -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

type PlainHandler implements HttpHandler
	var label as String

	static function create(label String) returns PlainHandler
		return PlainHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: label, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'PlainHandler'

function serveUntilClosed(server HttpServer, handler PlainHandler) returns Served
	_ = server.serve(handler)

	return 3
end 'serveUntilClosed'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let handler = PlainHandler.create("serving")
	let peer = async serveUntilClosed(server, handler: handler)

	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/first") otherwise return 2
	server.close()
	let served = await peer

	print("status={response.statusCode().rawValue} body={response.body()} served={served}\n")

	return 0
end 'main'
```
```stdout
status=200 body=serving served=3
```
```exitcode
0
```

### accept() after close() throws listenerClosed

⛔ **THE ROUND TRIP FIRST IS THE CONTROL.** An `accept` equally happy to refuse a live listener would
pass the second half of this case and say nothing about `close()`.

<!-- test: http-server.accept-after-close-throws-listener-closed -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise return 1
	try exchange.respondWith(StatusCode.ok, body: "live", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function acceptAfterClose(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "unexpected", contentType: "text/plain") otherwise return "answered-after-close"

	return "accepted-after-close"
end 'acceptAfterClose'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)

	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/live") otherwise return 2
	let served = await peer

	server.close()
	let refusal = acceptAfterClose(server)

	print("status={response.statusCode().rawValue} served={served} refusal={refusal}\n")

	return 0
end 'main'
```
```stdout
status=200 served=3 refusal=listenerClosed
```
```exitcode
0
```

### A `Host` that is not an authority answers 400

<!-- test: http-server.a-host-that-is-not-an-authority-answers-400 -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let url = exchange.request().url()
	let host = try url.host() otherwise "no-host"
	let query = try url.query() otherwise "no-query"
	let seen = "{host} {url.path()} {query}"

	try exchange.respondWith(StatusCode.ok, body: seen, contentType: "text/plain") otherwise return "respond-failed"

	return "served {seen}"
end 'acceptedAs'

function requestFor(host String, target String) returns String
	return "GET {target} HTTP/1.1\r\nHost: {host}\r\n\r\n"
end 'requestFor'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let blocks = [
		requestFor("x/admin?", target: "/public"),
		requestFor("x#frag", target: "/public"),
		requestFor("user@x", target: "/public"),
		requestFor("a b", target: "/public"),
		requestFor("", target: "/public"),
		requestFor("127.0.0.1:99999", target: "/public"),
		requestFor("127.0.0.1:8x", target: "/public"),
		requestFor("[::1", target: "/public"),
		requestFor("[1:2:3:4:5:6:7:8:9]", target: "/public"),
		requestFor("[1::2::3]", target: "/public"),
		requestFor("[::1]x", target: "/public"),
		requestFor("x", target: "/a%2"),
		requestFor("x", target: "/a#b"),
		requestFor("x", target: "/a b"),
		requestFor("x", target: "*"),
		requestFor("[::1]:8080", target: "/public"),
		requestFor("[::ffff:127.0.0.1]", target: "/public"),
		requestFor("[v1.fe80::a+en1]", target: "/public"),
		requestFor("exa%41mple.com", target: "/public"),
		requestFor("x:", target: "/p?q=/x?y")
	]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	server.close()

	return 0
end 'main'
```
```stdout
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=200 accept=served [::1] /public no-query
status=200 accept=served [::ffff:127.0.0.1] /public no-query
status=200 accept=served [v1.fe80::a+en1] /public no-query
status=200 accept=served exa%41mple.com /public no-query
status=200 accept=served x /p q=/x?y
```
```exitcode
0
```

### A header name is a token, and a folded line is refused

<!-- test: http-server.a-header-name-is-a-token-and-a-folded-line-is-refused -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let tight = try exchange.request().headers().get("x-tight") otherwise "absent"

	try exchange.respondWith(StatusCode.ok, body: tight, contentType: "text/plain") otherwise return "respond-failed"

	return "served x-tight={tight}"
end 'acceptedAs'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let blocks = [
		"POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding : chunked\r\n\r\n4\r\nabcd\r\n0\r\n\r\n",
		"POST / HTTP/1.1\r\nHost: x\r\nContent-Length : 5\r\n\r\nhello",
		"GET / HTTP/1.1\r\nHost : a\r\nHost: b\r\n\r\n",
		"GET / HTTP/1.1\r\nHost: x\r\nX-Folded: a\r\n b\r\n\r\n",
		"GET / HTTP/1.1\r\n Host: x\r\n\r\n",
		"GET / HTTP/1.1\r\nHost: x\r\nX-B@d: 1\r\n\r\n",
		"GET / HTTP/1.1\r\nHost: x\r\nX-Nul: a\x00b\r\n\r\n",
		"GET / HTTP/1.1\r\nHost: x\r\nX-Tight:packed\t \r\n\r\n"
	]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	server.close()

	return 0
end 'main'
```
```stdout
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=200 accept=served x-tight=packed
```
```exitcode
0
```

### The request line is read to its grammar

<!-- test: http-server.the-request-line-is-read-to-its-grammar -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let request = exchange.request()
	let seen = "{request.method().name} {request.url().path()} [{request.body()}]"

	try exchange.respondWith(StatusCode.ok, body: seen, contentType: "text/plain") otherwise return "respond-failed"

	return "served {seen}"
end 'acceptedAs'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let blocks = [
		"GET /a b HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET  / HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET / HTTP/1.1 \r\nHost: x\r\n\r\n",
		"GET / FTP/1.1\r\nHost: x\r\n\r\n",
		"GET / HTTP/1.10\r\nHost: x\r\n\r\n",
		"G(T / HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET / HTTP/2.0\r\nHost: x\r\n\r\n",
		"get / HTTP/1.1\r\nHost: x\r\n\r\n",
		"BREW / HTTP/1.1\r\nHost: x\r\n\r\n",
		"POST / HTTP/1.1\r\nHost: x\r\nContent-Length: -0\r\n\r\n",
		"POST / HTTP/1.1\r\nHost: x\r\nContent-Length: +2\r\n\r\nhi",
		"POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 2 2\r\n\r\nhi",
		"POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 99999999999999999999999999\r\n\r\nhi",
		"GET /old HTTP/1.0\r\nHost: x\r\n\r\n",
		"POST /form HTTP/1.1\r\nHost: x\r\nContent-Length: 2\r\n\r\nhi"
	]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	server.close()

	return 0
end 'main'
```
```stdout
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=505 accept=unsupportedVersion
status=501 accept=unsupportedMethod
status=501 accept=unsupportedMethod
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=400 accept=malformedRequest
status=413 accept=bodyTooLarge
status=200 accept=served get /old []
status=200 accept=served post /form [hi]
```
```exitcode
0
```

### A HEAD response carries no body

<!-- test: http-server.a-head-response-carries-no-body -->
<!-- procs: 1 -->
```maxon
typealias Served = int(0 to 9)

let ReadBufferBytes = 4096

function serveOne(server HttpServer) returns Served
	let exchange = try server.accept() otherwise return 1
	try exchange.respondWith(StatusCode.ok, body: "hello", contentType: "text/plain") otherwise return 2

	return 3
end 'serveOne'

function everythingFrom(client TcpClient) returns String
	var answer = ""
	var open = true

	while open 'untilClosed'
		if let chunk = try client.recv(ReadBufferBytes) 'more'
			answer.append(chunk)
		end 'more' else 'closed'
			open = false
		end 'closed'
	end 'untilClosed'

	return answer
end 'everythingFrom'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveOne(server)

	let client = try TcpClient.connect("127.0.0.1", port: server.port()) otherwise return 2
	_ = try client.send("HEAD /h HTTP/1.1\r\nHost: x\r\n\r\n") otherwise return 3
	let answer = everythingFrom(client)
	client.close()

	let served = await peer
	server.close()

	let parts = answer.split("\r\n\r\n")
	let head = try parts.get(0) otherwise "no-head"
	let body = try parts.get(1) otherwise "no-body"
	var length = "absent"

	for line in head.split("\r\n") 'eachLine'
		if line.toLower().startsWith("content-length:") 'theLength'
			length = line
		end 'theLength'
	end 'eachLine'

	print("served={served} {length} body=[{body}]\n")

	return 0
end 'main'
```
```stdout
served=3 Content-Length: 5 body=[]
```
```exitcode
0
```

### A handler that returns without responding answers 500 and is reported

<!-- test: http-server.a-handler-that-returns-without-responding-answers-500 -->
<!-- procs: 1 -->
```maxon
let SilentPath = "/silent"

type ForgetfulHandler implements HttpHandler
	var label as String

	static function create(label String) returns ForgetfulHandler
		return ForgetfulHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		if exchange.request().url().path() == SilentPath 'forgets'
			return
		end 'forgets'

		try exchange.respondWith(StatusCode.ok, body: label, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'ForgetfulHandler'

function serveUntilClosed(server HttpServer, handler ForgetfulHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveUntilClosed'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let handler = ForgetfulHandler.create("remembered")
	let peer = async serveUntilClosed(server, handler: handler)
	let port = server.port()

	let first = try HttpClient.get("http://127.0.0.1:{port}{SilentPath}") otherwise return 2
	let second = try HttpClient.get("http://127.0.0.1:{port}/fine") otherwise return 3

	server.close()
	let ended = await peer

	print("first={first.statusCode().rawValue} second={second.statusCode().rawValue} body={second.body()} ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
faulted GET /silent ended without an answer and was answered 500
first=500 second=200 body=remembered ended=closed
```
```exitcode
0
```

### A throw whose 500 cannot be sent is reported as unanswered

<!-- test: http-server.a-throw-whose-500-cannot-be-sent-is-reported-as-unanswered -->
<!-- procs: 1 -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
typealias PollRound = int(0 to 500)

let ResetPath = "/reset"
let RequestReadFlag = "http-throw-after-reset.read"
let PeerResetFlag = "http-throw-after-reset.reset"
let FaultReportedFlag = "http-throw-after-reset.faulted"
let PollRounds = 500 as PollRound
let PollIntervalMs = 20

type ResetHandler implements HttpHandler
	static function create() returns ResetHandler
		return ResetHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		raiseFlag(RequestReadFlag)
		_ = awaitFlag(PeerResetFlag)

		throw HttpServerError.malformedRequest
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
		raiseFlag(FaultReportedFlag)
	end 'faulted'
end 'ResetHandler'

function flagPath(name String) returns FilePath
	return try FilePath.from(name) otherwise panic("'{name}' is a plain file name")
end 'flagPath'

function raiseFlag(name String)
	try File.writeText(flagPath(name), content: name) otherwise panic("could not write the flag '{name}'")
end 'raiseFlag'

function lowerFlag(name String)
	if File.exists(flagPath(name)) 'raised'
		try File.delete(flagPath(name)) otherwise panic("could not delete the flag '{name}'")
	end 'raised'
end 'lowerFlag'

function awaitFlag(name String) returns bool
	for _ in 0 upto PollRounds 'poll'
		if File.exists(flagPath(name)) 'raised'
			return true
		end 'raised'

		sleep(PollIntervalMs)
	end 'poll'

	return false
end 'awaitFlag'

function sendThenReset(port NetworkPort) returns bool
	var argv = StringArray.create()

	#if os(Windows)
		argv.push("-NoProfile")
		argv.push("-Command")
		argv.push("$s = New-Object System.Net.Sockets.Socket([System.Net.Sockets.AddressFamily]::InterNetwork, [System.Net.Sockets.SocketType]::Stream, [System.Net.Sockets.ProtocolType]::Tcp); $s.LingerState = New-Object System.Net.Sockets.LingerOption($true, 0); $s.Connect('127.0.0.1', {port}); $null = $s.Send([System.Text.Encoding]::ASCII.GetBytes(\"GET {ResetPath} HTTP/1.1`r`nHost: x`r`n`r`n\")); while (-not (Test-Path '{RequestReadFlag}')) \{ Start-Sleep -Milliseconds {PollIntervalMs} \}; $s.Close()")
		let helper = Executable.name("powershell")
	#else
		argv.push("-MSocket")
		argv.push("-e")
		argv.push("socket(my $s, PF_INET, SOCK_STREAM, 0) or die; setsockopt($s, SOL_SOCKET, SO_LINGER, pack(\"ii\", 1, 0)) or die; connect($s, pack_sockaddr_in({port}, inet_aton(\"127.0.0.1\"))) or die; syswrite($s, \"GET {ResetPath} HTTP/1.1\\r\\nHost: x\\r\\n\\r\\n\") or die; select(undef, undef, undef, 0.02) until -e \"{RequestReadFlag}\"; close($s)")
		let helper = Executable.name("perl")
	#endif

	let res = try Subprocess.run(helper, arguments: argv) otherwise return false

	return res.succeeded()
end 'sendThenReset'

function serveUntilClosed(server HttpServer, handler ResetHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveUntilClosed'

function main() returns ExitCode
	lowerFlag(RequestReadFlag)
	lowerFlag(PeerResetFlag)
	lowerFlag(FaultReportedFlag)

	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveUntilClosed(server, handler: ResetHandler.create())
	let reset = sendThenReset(server.port())
	raiseFlag(PeerResetFlag)
	let reported = awaitFlag(FaultReportedFlag)

	server.close()
	let ended = await peer

	lowerFlag(RequestReadFlag)
	lowerFlag(PeerResetFlag)
	lowerFlag(FaultReportedFlag)
	print("reset={reset} reported={reported} ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
faulted GET /reset failed with malformedRequest, and the 500 sent in its place could not be sent
reset=true reported=true ended=closed
```
```exitcode
0
```

### `serve` answers a protocol fault and serves on

<!-- test: http-server.serve-answers-a-protocol-fault-and-serves-on -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

type PlainHandler implements HttpHandler
	var label as String

	static function create(label String) returns PlainHandler
		return PlainHandler{label: label}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: label, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'PlainHandler'

function serveUntilClosed(server HttpServer, handler PlainHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveUntilClosed'

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async serveUntilClosed(server, handler: PlainHandler.create("after"))

	let client = try TcpClient.connect("127.0.0.1", port: server.port()) otherwise return 2
	_ = try client.send("GARBAGE\r\n\r\n") otherwise return 3
	let refused = statusCodeOf(try client.recv(ReadBufferBytes) otherwise "recv-failed")
	client.close()

	let response = try HttpClient.get("http://127.0.0.1:{server.port()}/next") otherwise return 4
	server.close()
	let ended = await peer

	print("refused={refused} next={response.statusCode().rawValue} body={response.body()} ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
refused=400 next=200 body=after ended=closed
```
```exitcode
0
```

### A 408 reaches a peer that is still sending

<!-- test: http-server.a-408-reaches-a-peer-that-is-still-sending -->
<!-- procs: 1 -->
```maxon
let ReadDeadlineMs = 500
let TrickleIntervalMs = 100
let PromptMs = 3000
let ReadBufferBytes = 4096
let TrickledHead = "GET /slow HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Pad: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n\r\n"

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return "respond-failed"

	return "served"
end 'acceptedAs'

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function trickle(port NetworkPort) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	let bytes = TrickledHead.toByteArray()

	for b in bytes 'eachByte'
		var one = ByteArray.create()
		one.push(b)
		_ = try client.send(String.from(one)) otherwise return "send-failed"
		sleep(TrickleIntervalMs)

		try client.setReadDeadline(1) otherwise return "deadline-failed"

		if let answer = try client.recv(ReadBufferBytes) 'answered'
			client.close()

			return statusCodeOf(answer)
		end 'answered' else (e) 'notYet'
			if e != NetworkError.timedOut 'gone'
				return e.name
			end 'gone'
		end 'notYet'
	end 'eachByte'

	try client.setReadDeadline(0) otherwise return "deadline-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'trickle'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.readDeadlineMs = ReadDeadlineMs
	server.setLimits(limits)

	let peer = async acceptedAs(server)
	let start = Clock.nowMs()
	let status = trickle(server.port())
	let tookMs = Clock.elapsedMs(start)
	let accepted = await peer
	server.close()

	print("status={status} accept={accepted} prompt={tookMs < PromptMs}\n")

	return 0
end 'main'
```
```stdout
status=408 accept=readTimedOut prompt=true
```
```exitcode
0
```

### A 413 reaches a peer that is still sending its body

<!-- test: http-server.a-413-reaches-a-peer-that-is-still-sending-its-body -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096
let DeclaredBodyBytes = 2000000
let SentBodyChunks = 3200
let BodyChunk = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123"

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "served", contentType: "text/plain") otherwise return "respond-failed"

	return "served"
end 'acceptedAs'

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function oversizeRequest() returns String
	var block = "POST /upload HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: {DeclaredBodyBytes}\r\n\r\n"
	var written = 0

	while written < SentBodyChunks 'fill'
		block.append(BodyChunk)
		written = written + 1
	end 'fill'

	return block
end 'oversizeRequest'

function upload(port NetworkPort) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(oversizeRequest()) otherwise (e) 'sendFailed'
		return "send {e.name}"
	end 'sendFailed'

	let answer = try client.recv(ReadBufferBytes) otherwise (e) 'recvFailed'
		return "recv {e.name}"
	end 'recvFailed'

	client.close()

	return statusCodeOf(answer)
end 'upload'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async acceptedAs(server)
	let status = upload(server.port())
	let accepted = await peer
	server.close()

	print("status={status} accept={accepted}\n")

	return 0
end 'main'
```
```stdout
status=413 accept=bodyTooLarge
```
```exitcode
0
```

### A head within the cap is accepted when the body arrives in the same read

<!-- test: http-server.a-head-within-the-cap-arrives-with-a-body-past-it -->
<!-- procs: 1 -->
```maxon
let HeaderCap = 1024
let BodyBytes = 2048
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let received = exchange.request().body().byteLength()

	try exchange.respondWith(StatusCode.ok, body: "{received}", contentType: "text/plain") otherwise return "respond-failed"

	return "served body={received}"
end 'acceptedAs'

function postWithBody() returns String
	var block = "POST /upload HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: {BodyBytes}\r\n\r\n"
	var written = 0

	while written < BodyBytes 'fill'
		block.append("b")
		written = written + 1
	end 'fill'

	return block
end 'postWithBody'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.maxHeaderBytes = HeaderCap
	server.setLimits(limits)

	let peer = async acceptedAs(server)
	let status = shout(server.port(), block: postWithBody())
	let accepted = await peer
	server.close()

	print("status={status} accept={accepted}\n")

	return 0
end 'main'
```
```stdout
status=200 accept=served body=2048
```
```exitcode
0
```

### A peer that never reads is abandoned at the write deadline

⚠ **ON EVERY SOCKET LANE, x64-windows INCLUDED.** The answer is 64 MiB and the peer reads none of it, so the
send cannot finish: a polled lane parks on a send buffer that never drains, and on x64-windows the response is
one overlapped `WSASend` that does not complete while the peer's window stays shut. Either way the write
deadline is what ends it, and the handler is told the answer could not be sent.

<!-- test: http-server.a-peer-that-never-reads-is-abandoned-at-the-write-deadline -->
<!-- procs: 1 -->
```maxon
let WriteDeadlineMs = 500
let AnswerBytes = 67108864
let PeerSilenceMs = 8000

type BigAnswerHandler implements HttpHandler
	let answer as String

	static function create(answer String) returns BigAnswerHandler
		return BigAnswerHandler{answer: answer}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: answer, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'BigAnswerHandler'

function bigAnswer() returns String
	var body = StringBuilder.create()
	var written = 0

	while written < AnswerBytes 'fill'
		body.append("0123456789abcdef")
		written = written + 16
	end 'fill'

	return body.build()
end 'bigAnswer'

function serveOne(server HttpServer, handler BigAnswerHandler) returns HttpServeEnd
	return server.serve(handler)
end 'serveOne'

function main() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()
	limits.writeDeadlineMs = WriteDeadlineMs
	server.setLimits(limits)

	let handler = BigAnswerHandler.create(bigAnswer())
	let peer = async serveOne(server, handler: handler)

	let client = try TcpClient.connect("127.0.0.1", port: server.port()) otherwise return 2
	_ = try client.send("GET /big HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n") otherwise return 3
	sleep(PeerSilenceMs)

	server.close()
	let ended = await peer
	client.close()

	print("ended={ended.name}\n")

	return 0
end 'main'
```
```stdout
faulted GET /big failed with sendFailed after its answer could not be sent
ended=closed
```
```exitcode
0
```

### Absolute-form and `OPTIONS *`

A server MUST accept an absolute-form target and take the authority from it rather than from `Host`
(RFC 9112 §3.2.2). `OPTIONS *` asks about the server as a whole, so the server answers it itself and
hands no exchange to the caller; the asterisk with any other method is malformed.

<!-- test: http-server.absolute-form-and-options-asterisk -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function everythingFrom(client TcpClient) returns String
	var answer = ""
	var open = true

	while open 'untilClosed'
		if let chunk = try client.recv(ReadBufferBytes) 'more'
			answer.append(chunk)
		end 'more' else 'closed'
			open = false
		end 'closed'
	end 'untilClosed'

	return answer
end 'everythingFrom'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = everythingFrom(client)
	client.close()

	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"
	let status = try firstLine.split(" ").get(1) otherwise "no-status-code"
	let allow = answer.contains("allow: GET, POST, PUT, DELETE, HEAD, PATCH, OPTIONS")

	return "{status} allow={allow}"
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let url = exchange.request().url()
	let host = try url.host() otherwise "no-host"
	let port = try url.port() otherwise 0
	let query = try url.query() otherwise "no-query"
	let seen = "{host} {port} {url.path()} {query}"

	try exchange.respondWith(StatusCode.ok, body: seen, contentType: "text/plain") otherwise return "respond-failed"

	return "served {seen}"
end 'acceptedAs'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let blocks = [
		"GET http://origin.example:8080/p?q=1 HTTP/1.1\r\nHost: ignored.example\r\n\r\n",
		"GET HTTP://Origin.example HTTP/1.1\r\nHost: ignored.example\r\n\r\n",
		"GET https://origin.example/p HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET http://a b/p HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET http://user@origin.example/p HTTP/1.1\r\nHost: x\r\n\r\n",
		"GET * HTTP/1.1\r\nHost: x\r\n\r\n",
		"OPTIONS * HTTP/1.1\r\nHost: x\r\nContent-Length: many\r\n\r\n",
		"OPTIONS * HTTP/1.1\r\nHost: x\r\nContent-Length: 99999999999999\r\n\r\n"
	]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	let optionsPeer = async acceptedAs(server)
	let options = shout(port, block: "OPTIONS * HTTP/1.1\r\nHost: x\r\n\r\n")
	let after = shout(port, block: "GET /after HTTP/1.1\r\nHost: x\r\n\r\n")
	let accepted = await optionsPeer
	print("options={options} then={after} accept={accepted}\n")

	server.close()

	return 0
end 'main'
```
```stdout
status=200 allow=false accept=served origin.example 8080 /p q=1
status=200 allow=false accept=served Origin.example 0 / no-query
status=400 allow=false accept=malformedRequest
status=400 allow=false accept=malformedRequest
status=400 allow=false accept=malformedRequest
status=400 allow=false accept=malformedRequest
status=400 allow=false accept=malformedRequest
status=413 allow=false accept=bodyTooLarge
options=200 allow=true then=200 allow=false accept=served x 0 /after no-query
```
```exitcode
0
```

### An HTTP/1.0 request needs no `Host`, and a leading empty line is ignored

<!-- test: http-server.http-1-0-needs-no-host-and-a-leading-empty-line-is-ignored -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function statusCodeOf(answer String) returns String
	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"

	return try firstLine.split(" ").get(1) otherwise "no-status-code"
end 'statusCodeOf'

function shout(port NetworkPort, block String) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send(block) otherwise return "send-failed"
	let answer = try client.recv(ReadBufferBytes) otherwise return "recv-failed"
	client.close()

	return statusCodeOf(answer)
end 'shout'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	let url = exchange.request().url()
	let host = try url.host() otherwise "no-host"
	let seen = "{host} {url.path()}"

	try exchange.respondWith(StatusCode.ok, body: seen, contentType: "text/plain") otherwise return "respond-failed"

	return "served {seen}"
end 'acceptedAs'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let blocks = [
		"GET /old HTTP/1.0\r\n\r\n",
		"GET /new HTTP/1.1\r\n\r\n",
		"\r\nGET /after-one HTTP/1.1\r\nHost: x\r\n\r\n",
		"\r\n\r\nGET /after-two HTTP/1.1\r\nHost: x\r\n\r\n",
		"\r\n\r\n\r\n\r\n\r\nGET /after-five HTTP/1.1\r\nHost: x\r\n\r\n"
	]

	for block in blocks 'eachBlock'
		let peer = async acceptedAs(server)
		let status = shout(port, block: block)
		let accepted = await peer
		print("status={status} accept={accepted}\n")
	end 'eachBlock'

	server.close()

	return 0
end 'main'
```
```stdout
status=200 accept=served no-host /old
status=400 accept=malformedRequest
status=200 accept=served x /after-one
status=200 accept=served x /after-two
status=400 accept=malformedRequest
```
```exitcode
0
```

### A response is held to what its status and its fields allow

A `204` and a `304` carry no content and no `Content-Length` (RFC 9110 §8.6, §15.4.5), so a body offered
with one is refused rather than sent. A field name must be a token, and a field value or a reason phrase
may carry no CR, LF or other control, or one response could be read as two.

<!-- test: http-server.a-response-is-held-to-what-its-status-and-fields-allow -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096

function everythingFrom(client TcpClient) returns String
	var answer = ""
	var open = true

	while open 'untilClosed'
		if let chunk = try client.recv(ReadBufferBytes) 'more'
			answer.append(chunk)
		end 'more' else 'closed'
			open = false
		end 'closed'
	end 'untilClosed'

	return answer
end 'everythingFrom'

function answeredWith(port NetworkPort) returns String
	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect-failed"
	_ = try client.send("GET / HTTP/1.1\r\nHost: x\r\n\r\n") otherwise return "send-failed"
	let answer = everythingFrom(client)
	client.close()

	let firstLine = try answer.split("\r\n").get(0) otherwise return "no-status-line"
	let status = try firstLine.split(" ").get(1) otherwise "no-status-code"
	let length = answer.toLower().contains("content-length")

	return "{status} content-length={length} bytes={answer.byteLength()}"
end 'answeredWith'

function respondedWith(server HttpServer, response HttpResponse) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respond(response) otherwise (e) 'unsent'
		try exchange.respondWith(StatusCode.internalServerError, body: "", contentType: "text/plain") otherwise return "fallback-failed"

		return "respond threw {e.name}"
	end 'unsent'

	return "sent"
end 'respondedWith'

function withHeader(code StatusCode, name String, value String, body String, reason String) returns HttpResponse
	var headers = HttpHeaders.create()
	headers.set(name, value: value)

	return HttpResponse.create(code, reasonPhrase: reason, responseHeaders: headers, responseBody: body)
end 'withHeader'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let port = server.port()
	let responses = [
		withHeader(StatusCode.noContent, name: "x-kind", value: "empty", body: "", reason: "No Content"),
		withHeader(StatusCode.notModified, name: "x-kind", value: "empty", body: "", reason: "Not Modified"),
		withHeader(StatusCode.noContent, name: "x-kind", value: "empty", body: "stray", reason: "No Content"),
		withHeader(StatusCode.notModified, name: "x-kind", value: "empty", body: "stray", reason: "Not Modified"),
		withHeader(StatusCode.ok, name: "x-split", value: "a\r\nSet-Cookie: stolen=1", body: "ok", reason: "OK"),
		withHeader(StatusCode.ok, name: "x bad", value: "1", body: "ok", reason: "OK"),
		withHeader(StatusCode.ok, name: "x-kind", value: "fine", body: "ok", reason: "OK\r\nX-Injected: 1")
	]

	for response in responses 'eachResponse'
		let peer = async respondedWith(server, response: response)
		let seen = answeredWith(port)
		let outcome = await peer
		print("status={seen} {outcome}\n")
	end 'eachResponse'

	server.close()

	return 0
end 'main'
```
```stdout
status=204 content-length=false bytes=61 sent
status=304 content-length=false bytes=63 sent
status=500 content-length=true bytes=102 respond threw invalidResponse
status=500 content-length=true bytes=102 respond threw invalidResponse
status=500 content-length=true bytes=102 respond threw invalidResponse
status=500 content-length=true bytes=102 respond threw invalidResponse
status=500 content-length=true bytes=102 respond threw invalidResponse
```
```exitcode
0
```

### Bytes past the request are drained before the close

A connection closed while the peer's bytes are still unread can be RESET, and a reset can destroy the
answer on its way to the peer. So a server that received more than the one request it serves drains
before it closes.

<!-- test: http-server.bytes-past-the-request-are-drained-before-the-close -->
<!-- procs: 1 -->
```maxon
let ReadBufferBytes = 4096
let OverflowBytes = 131072

function everythingFrom(client TcpClient) returns String
	var answer = ""
	var open = true

	while open 'untilClosed'
		if let chunk = try client.recv(ReadBufferBytes) 'more'
			answer.append(chunk)
		end 'more' else 'closed'
			open = false
		end 'closed'
	end 'untilClosed'

	return answer
end 'everythingFrom'

function acceptedAs(server HttpServer) returns String
	let exchange = try server.accept() otherwise (e) 'refused'
		return e.name
	end 'refused'

	try exchange.respondWith(StatusCode.ok, body: "first answered", contentType: "text/plain") otherwise return "respond-failed"

	return "served {exchange.request().url().path()}"
end 'acceptedAs'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async acceptedAs(server)

	var overflow = StringBuilder.create()

	for _ in 0 upto OverflowBytes 'fill'
		overflow.append("x")
	end 'fill'

	let client = try TcpClient.connect("127.0.0.1", port: server.port()) otherwise return 2
	_ = try client.send("POST /first HTTP/1.1\r\nHost: x\r\nContent-Length: 5\r\n\r\nhello{overflow.build()}") otherwise return 3
	let accepted = await peer
	let answer = everythingFrom(client)
	client.close()
	server.close()

	print("accept={accepted} answered={answer.endsWith("first answered")}\n")

	return 0
end 'main'
```
```stdout
accept=served /first answered=true
```
```exitcode
0
```
