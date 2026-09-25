---
feature: http-client
status: experimental
keywords: [http, client, request, response, network, url]
category: network
---

# HttpClient

## Documentation

HTTP/1.1 client for making HTTP requests over TCP connections.

**Types:**
- `HttpClient` — stateless HTTP client with static methods for making requests
- `HttpRequest` — represents an HTTP request with method, URL, headers, body
- `HttpResponse` — represents an HTTP response with status code, headers, body
- `HttpHeaders` — case-insensitive header map
- `HttpMethod` — enum of HTTP methods (get, post, put, delete, head, patch)
- `HttpError` — error enum for HTTP operations

**Quick usage:**

```text
function fetchData() returns ExitCode throws HttpError
  let response = try HttpClient.get("http://httpbin.org/get")
  print(response.body())
  return 0
end 'fetchData'
```

**Building requests manually:**

```text
function postData() returns ExitCode throws HttpError
  var request = try HttpRequest.create(HttpMethod.post, url: "http://httpbin.org/post")
  request.setHeader("content-type", value: "application/json")
  request.setBody("hello=world")
  let response = try HttpClient.send(request)
  print(response.statusCode())
  return 0
end 'postData'
```

**Limitations:**
- HTTP only (no TLS): a URL whose scheme is anything but `http` — `https` included — throws
  `HttpError.unsupportedScheme` before any connection is attempted
- A request is sent with `Connection: close` unless the caller sets `Connection`; a response is read to its
  `Content-Length`, through its chunked framing, or to the close when it declares neither. A `101 Switching
  Protocols` is refused (`invalidResponse`)
- No redirect following (returns 3xx as-is)
- No streaming — entire response buffered in memory

## Tests

### Invalid URL

<!-- test: http-client.invalid-url -->
```maxon
function main() returns ExitCode
	if let response = try HttpClient.get("not a url") 'ok'
		return 1
	end 'ok' else 'err'
		return 0
	end 'err'
end 'main'
```
```exitcode
0
```

### An https URL is refused

<!-- test: http-client.an-https-url-is-refused-before-connecting -->
⛔ **`https` IS REFUSED BY SCHEME, NEVER SENT AS PLAINTEXT.** Nothing listens on port 1, so a client that
ignored the scheme would dial and answer `connectFailed`; only a refusal decided before the connect answers
`unsupportedScheme`, which is the one arm that exits 7.
```maxon
function main() returns ExitCode
	let response = try HttpClient.get("https://127.0.0.1:1/") otherwise (e) 'refused'
		return match e 'why'
			unsupportedScheme gives 7
			invalidUrl gives 2
			connectFailed gives 3
			sendFailed gives 4
			recvFailed gives 5
			invalidResponse gives 6
		end 'why'
	end 'refused'

	print("answered with a body of {response.body().byteLength()} bytes\n")
	return 1
end 'main'
```
```exitcode
7
```

### Request Building

<!-- test: http-client.build-request -->
```maxon
function main() returns ExitCode
	let request = try HttpRequest.create(HttpMethod.get, url: "http://example.com/path?q=1") otherwise 'err'
		return 1
	end 'err'
	let url = request.url()
	let host = try url.host() otherwise ""
	if host != "example.com" 'badHost'
		return 2
	end 'badHost'
	let path = url.path()
	if path != "/path" 'badPath'
		return 3
	end 'badPath'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: http-client.a-caller-set-connection-header-is-sent -->
```maxon
function connectionLines(request HttpRequest) returns String
	var found = ""
	let target = try httpConnectTargetOf(request.url()) otherwise return "no-target"

	for line in httpBuildRequest(request, target: target).split("\r\n") 'eachLine'
		var lowered = line
		lowered = lowered.toLower()

		if lowered.startsWith("connection:") 'aConnectionLine'
			found.append("[{lowered}]")
		end 'aConnectionLine'
	end 'eachLine'

	return found
end 'connectionLines'

function main() returns ExitCode
	let plain = try HttpRequest.create(HttpMethod.get, url: "http://example.com/") otherwise return 1
	var chosen = try HttpRequest.create(HttpMethod.get, url: "http://example.com/") otherwise return 2
	chosen.setHeader("Connection", value: "keep-alive")

	print("default={connectionLines(plain)} chosen={connectionLines(chosen)}\n")
	return 0
end 'main'
```
```stdout
default=[connection: close] chosen=[connection: keep-alive]
```
```exitcode
0
```

<!-- test: http-client.a-kept-alive-response-ends-at-its-declared-length -->
<!-- procs: 1 -->
**A RESPONSE WITH A `Content-Length` ENDS AT THAT LENGTH, NOT AT THE CLOSE.** A caller that asks for
`Connection: keep-alive` gets a server that keeps the connection open after answering, so a client reading to
the close would wait as long as the server holds it. The peer here answers, then holds the connection open until
the client leaves or `HeldOpenMs` passes: a prompt answer and a peer that saw the client close are what a client
reading by the length produces, and a slow answer with a peer that timed out is what reading to the close does.

⚠ **A `HEAD` RESPONSE HAS NO BODY WHATEVER ITS `Content-Length` SAYS.** The length describes the body a `GET`
would have received (RFC 9112 section 6.3), so a client that waited for those bytes would wait for the close
here too.
```maxon
let HeldOpenMs = 3000
let PromptMs = 2000
let RequestBytes = 4096

function answerAndHold(listener TcpListener, answer String) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.recv(RequestBytes) otherwise return "recv-failed"
	_ = try conn.send(answer) otherwise return "send-failed"
	try conn.setReadDeadline(HeldOpenMs) otherwise return "deadline-failed"

	let heard = try conn.recv(RequestBytes) otherwise (e) 'held'
		return e.name
	end 'held'

	return "heard {heard.byteLength()} more bytes"
end 'answerAndHold'

function fetch(port NetworkPort, method HttpMethod) returns String
	var request = try HttpRequest.create(method, url: "http://127.0.0.1:{port}/") otherwise return "bad-url"
	request.setHeader("Connection", value: "keep-alive")

	let response = try HttpClient.send(request) otherwise (e) 'failed'
		return e.name
	end 'failed'

	return "{response.statusCode().rawValue} [{response.body()}]"
end 'fetch'

function exchange(listener TcpListener, method HttpMethod, answer String) returns String
	let peer = async answerAndHold(listener, answer: answer)
	let start = Clock.nowMs()
	let got = fetch(listener.port(), method: method)
	let tookMs = Clock.elapsedMs(start)
	let held = await peer

	return "{got} prompt={tookMs < PromptMs} peer={held}"
end 'exchange'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	print("get={exchange(listener, method: HttpMethod.get, answer: "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: keep-alive\r\n\r\nhello")}\n")
	print("head={exchange(listener, method: HttpMethod.head, answer: "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: keep-alive\r\n\r\n")}\n")
	return 0
end 'main'
```
```stdout
get=200 [hello] prompt=true peer=connectionClosed
head=200 [] prompt=true peer=connectionClosed
```
```exitcode
0
```

<!-- test: http-client.a-chunked-response-is-decoded -->
<!-- procs: 1 -->
**A `Transfer-Encoding: chunked` BODY IS DECODED, AND IT ENDS AT THE LAST CHUNK.** Each chunk is a hex size, any
extensions after a `;` ignored, the data and a CRLF; a zero size ends the body, and the trailer fields after it
are read and discarded. `Transfer-Encoding` overrides a `Content-Length` sent beside it (RFC 9112 section 6.3),
so the length says nothing about the body. The peer holds the connection open after a kept-alive answer, so a
client that read to the close would be slow; a size that is not hex is an invalid response, answered promptly.
```maxon
let HeldOpenMs = 3000
let PromptMs = 2000
let RequestBytes = 4096

function answer(listener TcpListener, reply String, hold bool) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.recv(RequestBytes) otherwise return "recv-failed"
	_ = try conn.send(reply) otherwise return "send-failed"

	if not hold 'closing'
		conn.close()
		return "closed"
	end 'closing'

	try conn.setReadDeadline(HeldOpenMs) otherwise return "deadline-failed"

	let heard = try conn.recv(RequestBytes) otherwise (e) 'held'
		return e.name
	end 'held'

	return "heard {heard.byteLength()} more bytes"
end 'answer'

function fetch(port NetworkPort) returns String
	var request = try HttpRequest.create(HttpMethod.get, url: "http://127.0.0.1:{port}/") otherwise return "bad-url"
	request.setHeader("Connection", value: "keep-alive")

	let response = try HttpClient.send(request) otherwise (e) 'failed'
		return e.name
	end 'failed'

	return "{response.statusCode().rawValue} [{response.body()}]"
end 'fetch'

function exchange(listener TcpListener, reply String, hold bool) returns String
	let peer = async answer(listener, reply: reply, hold: hold)
	let start = Clock.nowMs()
	let got = fetch(listener.port())
	let tookMs = Clock.elapsedMs(start)
	let held = await peer

	return "{got} prompt={tookMs < PromptMs} peer={held}"
end 'exchange'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let chunks = "5;name=value\r\nhello\r\n6\r\n world\r\n0\r\nX-Checksum: 1\r\n\r\n"

	print("chunked={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{chunks}", hold: false)}\n")
	print("chunked-with-length={exchange(listener, reply: "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nTransfer-Encoding: chunked\r\n\r\n{chunks}", hold: false)}\n")
	print("chunked-kept-alive={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: keep-alive\r\n\r\n{chunks}", hold: true)}\n")
	print("chunked-with-an-empty-element={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked,\r\nConnection: keep-alive\r\n\r\n{chunks}", hold: true)}\n")
	print("bad-size={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: keep-alive\r\n\r\nzz\r\nhello\r\n0\r\n\r\n", hold: true)}\n")
	print("blank-before-an-extension={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: keep-alive\r\n\r\n5 \t;name\r\nhello\r\n0\r\n\r\n", hold: true)}\n")
	print("text-after-the-size={exchange(listener, reply: "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: keep-alive\r\n\r\n5 junk\r\nhello\r\n0\r\n\r\n", hold: true)}\n")
	return 0
end 'main'
```
```stdout
chunked=200 [hello world] prompt=true peer=closed
chunked-with-length=200 [hello world] prompt=true peer=closed
chunked-kept-alive=200 [hello world] prompt=true peer=connectionClosed
chunked-with-an-empty-element=200 [hello world] prompt=true peer=connectionClosed
bad-size=invalidResponse prompt=true peer=connectionClosed
blank-before-an-extension=200 [hello] prompt=true peer=connectionClosed
text-after-the-size=invalidResponse prompt=true peer=connectionClosed
```
```exitcode
0
```

<!-- test: http-client.an-interim-response-is-skipped -->
<!-- procs: 1 -->
**A 1xx RESPONSE IS INTERIM, AND THE CLIENT READS PAST IT TO THE FINAL ONE** (RFC 9110 section 15.2). A `103 Early
Hints` carries header fields and no body, and the response the request is answered with follows it on the same
connection. Eight interim responses are read past; a ninth is an invalid response.
```maxon
typealias InterimCount = int(0 to 16)

let RequestBytes = 4096
let EarlyHints = "HTTP/1.1 103 Early Hints\r\nLink: </style.css>; rel=preload\r\n\r\n"

function answer(listener TcpListener, interimCount InterimCount) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.recv(RequestBytes) otherwise return "recv-failed"
	var reply = StringBuilder.create()

	for _ in 0 upto interimCount 'eachInterim'
		reply.append(EarlyHints)
	end 'eachInterim'

	reply.append("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok")
	_ = try conn.send(reply.build()) otherwise return "send-failed"
	conn.close()

	return "answered"
end 'answer'

function exchange(listener TcpListener, interimCount InterimCount) returns String
	let peer = async answer(listener, interimCount: interimCount)

	let response = try HttpClient.get("http://127.0.0.1:{listener.port()}/") otherwise (e) 'failed'
		return "{e.name} peer={await peer}"
	end 'failed'

	return "{response.statusCode().rawValue} [{response.body()}] peer={await peer}"
end 'exchange'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1

	print("one={exchange(listener, interimCount: 1)}\n")
	print("eight={exchange(listener, interimCount: 8)}\n")
	print("nine={exchange(listener, interimCount: 9)}\n")
	return 0
end 'main'
```
```stdout
one=200 [ok] peer=answered
eight=200 [ok] peer=answered
nine=invalidResponse peer=answered
```
```exitcode
0
```

<!-- test: http-client.a-switching-protocols-response-is-refused -->
<!-- procs: 1 -->
**A `101 Switching Protocols` IS NOT AN INTERIM RESPONSE TO THIS CLIENT, AND IT IS REFUSED.** After a 101 the
connection speaks another protocol, which this client does not, so reading on for a final HTTP response would
wait on bytes that never come. The peer holds the connection open after the 101: a prompt `invalidResponse` and
a peer that saw the client leave is the refusal, and a slow answer with a peer that timed out is the hang.
```maxon
let HeldOpenMs = 3000
let PromptMs = 2000
let RequestBytes = 4096

function switchAndHold(listener TcpListener) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.recv(RequestBytes) otherwise return "recv-failed"
	_ = try conn.send("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n") otherwise return "send-failed"
	try conn.setReadDeadline(HeldOpenMs) otherwise return "deadline-failed"

	let heard = try conn.recv(RequestBytes) otherwise (e) 'held'
		return e.name
	end 'held'

	return "heard {heard.byteLength()} more bytes"
end 'switchAndHold'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async switchAndHold(listener)

	var request = try HttpRequest.create(HttpMethod.get, url: "http://127.0.0.1:{listener.port()}/") otherwise return 2
	request.setHeader("Connection", value: "Upgrade")
	request.setHeader("Upgrade", value: "websocket")

	let start = Clock.nowMs()

	let outcome = try HttpClient.send(request) otherwise (e) 'refused'
		print("client={e.name} prompt={Clock.elapsedMs(start) < PromptMs} peer={await peer}\n")
		return 0
	end 'refused'

	print("client=answered {outcome.statusCode().rawValue} peer={await peer}\n")
	return 0
end 'main'
```
```stdout
client=invalidResponse prompt=true peer=connectionClosed
```
```exitcode
0
```

<!-- test: http-client.an-endless-response-head-is-refused-at-its-cap -->
<!-- procs: 1 -->
**A RESPONSE HEAD THAT NEVER ENDS IS REFUSED, NOT BUFFERED.** The peer streams header fields with no blank line
after them. The client stops at its head cap and answers `invalidResponse`, and the peer's sends fail when the
client goes, long before the peer has sent everything it would.
```maxon
let RequestBytes = 4096
let MostLines = 65536

function streamAHead(listener TcpListener) returns String
	let conn = try listener.accept() otherwise return "accept-failed"
	_ = try conn.recv(RequestBytes) otherwise return "recv-failed"
	_ = try conn.send("HTTP/1.1 200 OK\r\n") otherwise return "send-failed"

	var line = StringBuilder.create()
	line.append("X-Padding: ")

	for _ in 0 upto 100 'pad'
		line.append("x")
	end 'pad'

	line.append("\r\n")
	let field = line.build()
	var sent = 0

	while sent < MostLines 'stream'
		_ = try conn.send(field) otherwise break
		sent = sent + 1
	end 'stream'

	conn.close()
	return "stoppedEarly={sent < MostLines}"
end 'streamAHead'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async streamAHead(listener)

	let outcome = try HttpClient.get("http://127.0.0.1:{listener.port()}/") otherwise (e) 'failed'
		print("client={e.name} peer={await peer}\n")
		return 0
	end 'failed'

	print("client=answered {outcome.statusCode().rawValue} peer={await peer}\n")
	return 0
end 'main'
```
```stdout
client=invalidResponse peer=stoppedEarly=true
```
```exitcode
0
```

<!-- test: http-client.repeated-response-headers-are-combined -->
```maxon
function finalResponseOf(head String) returns HttpResponse throws HttpError
	match try httpResponseStatusOf(head) 'status'
		final(response) then return response
		interim then throw HttpError.invalidResponse
	end 'status'
end 'finalResponseOf'

function main() returns ExitCode
	let response = try finalResponseOf("HTTP/1.1 200 OK\r\nVary: Accept\r\nContent-Length: 2\r\nSet-Cookie: a=1; Expires=Wed, 21 Oct 2026 07:28:00 GMT\r\nVARY: Origin\r\nSet-Cookie: b=2\r\nContent-Length: 2") otherwise return 1
	let vary = try response.header("vary") otherwise "absent"
	let cookies = try response.header("set-cookie") otherwise "absent"
	let length = try response.header("content-length") otherwise "absent"

	print("vary={vary}\n")
	print("cookies={cookies.split("\n").count()}\n")

	for cookie in cookies.split("\n") 'eachCookie'
		print("cookie={cookie}\n")
	end 'eachCookie'

	print("length={length}\n")
	return 0
end 'main'
```
```stdout
vary=Accept, Origin
cookies=2
cookie=a=1; Expires=Wed, 21 Oct 2026 07:28:00 GMT
cookie=b=2
length=2
```
```exitcode
0
```

<!-- test: http-client.conflicting-repeated-singleton-headers-are-refused -->
```maxon
function verdict(head String) returns String
	_ = try httpParseRequestHead(head) otherwise (e) 'refused'
		return e.name
	end 'refused'

	return "accepted"
end 'verdict'

function main() returns ExitCode
	print("host-twice={verdict("GET / HTTP/1.1\r\nHost: a\r\nHost: a")}\n")
	print("length-agreeing={verdict("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 2\r\nContent-Length: 2")}\n")
	print("length-conflicting={verdict("POST / HTTP/1.1\r\nHost: a\r\nContent-Length: 2\r\nContent-Length: 3")}\n")
	return 0
end 'main'
```
```stdout
host-twice=malformedRequest
length-agreeing=accepted
length-conflicting=malformedRequest
```
```exitcode
0
```

<!-- test: http-client.a-response-header-name-is-a-token-and-host-is-a-request-rule -->
```maxon
function finalResponseOf(head String) returns HttpResponse throws HttpError
	match try httpResponseStatusOf(head) 'status'
		final(response) then return response
		interim then throw HttpError.invalidResponse
	end 'status'
end 'finalResponseOf'

function verdict(raw String) returns String
	let response = try finalResponseOf(raw) otherwise (e) 'refused'
		return e.name
	end 'refused'

	let host = try response.header("host") otherwise "absent"

	return "accepted host={host}"
end 'verdict'

function unfolded(raw String) returns String
	let response = try finalResponseOf(raw) otherwise (e) 'refused'
		return e.name
	end 'refused'

	let folded = try response.header("x-folded") otherwise "absent"

	return "accepted x-folded=[{folded.replace(NoBreakSpace, with: NoBreakSpaceSpelling)}]"
end 'unfolded'

let NoBreakSpace = " "
let NoBreakSpaceSpelling = "<nbsp>"

function main() returns ExitCode
	print("space-before-colon={verdict("HTTP/1.1 200 OK\r\nX-Bad : 1\r\n\r\n")}\n")
	print("folded={unfolded("HTTP/1.1 200 OK\r\nX-Folded: a\r\n b\r\n\t c\r\n\r\n")}\n")
	print("folded-keeps-a-no-break-space={unfolded("HTTP/1.1 200 OK\r\nX-Folded: a\r\n  b \r\n\r\n")}\n")
	print("folded-with-nothing-to-continue={unfolded("HTTP/1.1 200 OK\r\n b\r\n\r\n")}\n")
	print("control={verdict("HTTP/1.1 200 OK\r\nX-Ctl: a\x01b\r\n\r\n")}\n")
	print("host-twice={verdict("HTTP/1.1 200 OK\r\nHost: a\r\nHost: b\r\n\r\n")}\n")
	print("no-reason={verdict("HTTP/1.1 204\r\n\r\n")}\n")
	return 0
end 'main'
```
```stdout
space-before-colon=invalidResponse
folded=accepted x-folded=[a b c]
folded-keeps-a-no-break-space=accepted x-folded=[a <nbsp>b<nbsp>]
folded-with-nothing-to-continue=invalidResponse
control=invalidResponse
host-twice=accepted host=a, b
no-reason=accepted host=absent
```
```exitcode
0
```

### HTTP GET

<!-- test: http-client.get -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	if response.statusCode() == 200 'ok'
		return 0
	end 'ok'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### HTTP POST

<!-- test: http-client.post -->
<!-- network: live -->
```maxon
function doPost() returns ExitCode throws HttpError
	let response = try HttpClient.post("http://httpbin.org/post", body: "hello=world")
	if response.statusCode() == 200 'ok'
		return 0
	end 'ok'
	return 1
end 'doPost'

function main() returns ExitCode
	let p = async doPost()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Status 404

<!-- test: http-client.status-404 -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/status/404")
	if response.statusCode() == 404 'notFound'
		return 0
	end 'notFound'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Response Body Contains Expected Content

<!-- test: http-client.response-body -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	let body = response.body()
	if body.contains("httpbin.org") 'hasContent'
		return 0
	end 'hasContent'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```

### Async HTTP with Concurrent File I/O

Verify that a file probe and an HTTP connect, parked on different kinds of I/O at the same time, resume
interleaved, and that the request's failure still reaches `try await`.

<!-- test: http-client.async-trace-interleave -->
<!-- AsyncTrace -->
⭐⭐ **A TRACE CASE AND A LIVE HOST ARE INCOMPATIBLE.** A trace pins an INTERLEAVING, and a live host makes
that interleaving a function of somebody else's latency. So the peer is a listener this program binds on
`127.0.0.1`, at a port the kernel picks, and the case needs no `--network`. `async-tcp.md` keeps the same
separation: its `AsyncTrace` cases carry no `network: live`, and its `network: live` case carries no trace.

⚠ **THE LISTENER NEVER ACCEPTS, BECAUSE A PARKED ACCEPT RACES THE DIAL.** One loopback handshake readies
both the accept and the connect, and the poller reports the two in either order. Unaccepted, the connection
completes into the backlog, so the dial and the request's send still complete, and closing the listener
resets it, which ends the request with an error.

⚠ **THE `sleep` IS THE HANDOVER, AND IT IS A MARGIN, NOT A SYNCHRONIZATION.** It parks `main` on a timer
rather than requeueing it, so the HTTP task runs alone until it is waiting for a response. Nothing orders the
close after the request's send except `settleMs`: an HTTP task stalled for longer than that would meet the
reset before it waits for a response, and pin a different trace.

Both addresses are literals, which wait on no resolver, so the bind does not park and the HTTP connect
parks once, for the dial.
```maxon
// Far longer than a loopback connect and send, which is all the HTTP task does before it waits.
let settleMs = 200

function doHttp(port NetworkPort) returns ExitCode throws HttpError
	let response = try HttpClient.get("http://127.0.0.1:{port}/get")
	if response.statusCode() == StatusCode.ok 'ok'
		return 0
	end 'ok'
	return 1
end 'doHttp'

function doFileIo() returns ExitCode
	let exists = File.exists(FilePath from "no_such_file.txt")
	if exists 'found'
		return 1
	end 'found'
	return 0
end 'doFileIo'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let fileTask = async doFileIo()
	let httpTask = async doHttp(listener.port())
	let fileResult = await fileTask

	sleep(settleMs)
	listener.close()

	let httpResult = try await httpTask otherwise 99
	return httpResult + fileResult
end 'main'
```
```exitcode
99
```
```stderr
spawn #1
spawn #2
io_yield #1 [file_exists]
io_yield #2 [net_connect]
io_resume #1 [file_exists]
await #1 [yield]
sleep_yield #0
io_resume #2 [net_connect]
io_yield #2 [net_send]
io_resume #2 [net_send]
io_yield #2 [net_recv]
io_resume #2 [net_recv]
sleep_resume #0
io_yield #0 [net_close]
io_resume #0 [net_close]
io_yield #2 [net_close]
io_resume #2 [net_close]
try_await #2 [yield]
```

### Response Headers

<!-- test: http-client.response-headers -->
<!-- network: live -->
```maxon
function doGet() returns ExitCode throws HttpError
	let response = try HttpClient.get("http://httpbin.org/get")
	let contentType = try response.header("content-type")
	if contentType.contains("application/json") 'ok'
		return 0
	end 'ok'
	return 1
end 'doGet'

function main() returns ExitCode
	let p = async doGet()
	let result = try await p otherwise 99
	return result
end 'main'
```
```exitcode
0
```
