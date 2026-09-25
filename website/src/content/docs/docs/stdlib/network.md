---
title: Networking
description: TCP clients and listeners, the HTTP client, and URLs.
sidebar:
  order: 5
---

## TcpClient

`TcpClient` is a TCP connection over IPv4. It closes its socket when the last reference to it goes away, so
`close()` is optional.

Available on `x64-windows`, `arm64-macos`, `arm64-linux` and `x64-linux`; refused at compile time (E3104) on
`wasm32-wasi`. Windows and macOS resolve host names through the platform resolver. The two Linux targets
link no C library and use a built-in resolver instead: it accepts a numeric address, reads `/etc/hosts`,
and otherwise sends an `A` query over TCP to the first `nameserver` in `/etc/resolv.conf`. It does not apply
`search`/`domain` suffixes, does not fall over to a second nameserver and has no timeout of its own.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Resolve `host` and connect. `resolveFailed` or `connectFailed`. |
| `TcpClient.adopt(socket)` | `TcpClient` | — | Wrap an already-connected socket; how `TcpListener.accept()` returns its connections. |
| `send(data String)` | `int(0 to u64.max)` | `NetworkError` | Send every byte, looping over partial sends; returns the byte count. |
| `recv(bufferSize RecvCapacity)` | `String` | `NetworkError` | Between one and `bufferSize` bytes. Throws `connectionClosed` when the peer has closed. |
| `setReadDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | Make a read that waits longer than this throw `timedOut`. Measured from this call; `0` clears it. |
| `setWriteDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | The same for sends. A send goes to the OS in 1 MiB slices, so the deadline also ends a large send to a peer that has stopped reading. |
| `shutdownWrite()` | — | `NetworkError` | Stop sending: the peer reads end-of-stream while this side can still `recv`. |
| `close()` | — | — | Close the connection. Idempotent. |

A deadline setter and `shutdownWrite` throw `connectionClosed` when the socket is already closed.

`NetworkPort` is `int(0 to 65535)`. Port `0` asks `TcpListener.bind` for any free port; a live connection
always has a nonzero one. `SocketDeadlineMs` is `int(0 to 4294967295)`, the milliseconds a deadline setter takes.
`RecvCapacity` is `int(1 to i64.max)`: a read of zero bytes is how a closed peer is reported, so a buffer
holds at least one.

```maxon
enum NetworkError implements Error
	resolveFailed
	connectFailed
	sendFailed
	recvFailed
	connectionClosed
	timedOut
	bindFailed
	acceptFailed
	acceptInterrupted
end 'NetworkError'
```

| Case | Meaning |
|------|---------|
| `resolveFailed` | The host name did not resolve |
| `connectFailed` | The connection was refused or could not be made |
| `sendFailed`, `recvFailed` | The OS reported an error |
| `connectionClosed` | The peer closed the connection |
| `timedOut` | A read, write or accept deadline expired |
| `bindFailed` | The address or port could not be bound |
| `acceptFailed` | The listener can accept nothing more: it is closed, or the OS refused it for good |
| `acceptInterrupted` | The OS refused one connection, for example with the process out of descriptors; the listener can take the next |

These also arise without any OS error: a coroutine whose promise has been dropped or `cancel()`ed throws the
variant of the next operation that would wait on a far end rather than starting it — see
[Cancellation and Dropped Promises](/docs/language/async/#cancellation-and-dropped-promises). `close()` is
unaffected.

## TcpListener

`TcpListener` is a listening socket. It has no `send` or `recv`; `accept()` returns a `TcpClient` for each
connection. `accept()` parks its green thread until a connection arrives, so a waiting server blocks no OS
thread. It closes when the last reference goes away. Same targets as `TcpClient`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpListener.bind(host String, port NetworkPort)` | `TcpListener` | `NetworkError` | Bind and listen. Port `0` takes any free port. |
| `port()` | `NetworkPort` | — | The bound port, as the OS reports it. |
| `accept()` | `TcpClient` | `NetworkError` | The next connection. Throws `timedOut` past the accept deadline, `acceptInterrupted` when the OS refused one connection, and `acceptFailed` when the listener is closed or has failed. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | Make an `accept()` still waiting `milliseconds` after this call throw `timedOut`; `0` clears it. Throws `acceptFailed` when the listener is closed. |
| `close()` | — | — | Stop listening and release the port. Idempotent. |

`SO_REUSEADDR` is not set, so binding a port a live listener holds throws `bindFailed`.

```maxon
function echoOnce(listener TcpListener) returns ExitCode throws NetworkError
	let conn = try listener.accept()
	let data = try conn.recv(1024)
	_ = try conn.send(data)
	return 0
end 'echoOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let server = async echoOnce(listener)

	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
	try client.setReadDeadline(5000) otherwise return 3
	_ = try client.send("ping") otherwise return 4
	let reply = try client.recv(1024) otherwise return 5
	client.close()

	_ = try await server otherwise 9
	print("{reply}\n")
	return 0
end 'main'
```

Output: `ping`.

## HttpClient

An HTTP/1.1 client over `TcpClient`. A request carries `Host` and `Connection: close` unless it sets its own,
and a `Content-Length` whenever it has a body.

A response body is framed by `Transfer-Encoding` when the response has one: a final `chunked` coding is
decoded (a chunk extension after `;` is ignored, and the trailer section is discarded), and any other coding
is read until the server closes the connection. Without `Transfer-Encoding` the body is `Content-Length`
bytes, or is read until the close when that is absent too. A response to `HEAD`, and a `204` or `304`,
ends at its head. Interim `1xx` responses before the final one are skipped, at most 8 of them.
`HttpError.invalidResponse` is thrown for a `101 Switching Protocols`, a response head over 64 KiB, a body over 1 TiB
(`HttpLargestBodyBytes`), and a connection that closes before the body is complete.

Limitations: plain HTTP only (no TLS) — a URL whose scheme is anything but `http`, `https` included, throws
`HttpError.unsupportedScheme` before any connection is attempted. A 3xx is returned as is, and the whole
response is held in memory. The URL's port defaults to 80.

### HttpClient

| Method | Returns | Description |
|--------|---------|-------------|
| `HttpClient.send(request HttpRequest)` | `HttpResponse` | Send a request. |
| `HttpClient.get(url String)` | `HttpResponse` | `GET`. |
| `HttpClient.post(url String, body String)` | `HttpResponse` | `POST` with a body. |
| `HttpClient.put(url String, body String)` | `HttpResponse` | `PUT` with a body. |
| `HttpClient.delete(url String)` | `HttpResponse` | `DELETE`. |

All throw `HttpError`.

### HttpRequest

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpRequest.create(method HttpMethod, url String)` | `HttpRequest` | Throws `HttpError.invalidUrl`. |
| `setHeader(name String, value String)` | — | Set a header. |
| `setBody(body String)` | — | Set the body. |
| `method()` | `HttpMethod` | |
| `url()` | `URL` | |
| `headers()` | `HttpHeaders` | |
| `body()` | `String` | |

### HttpResponse

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpResponse.create(code StatusCode, reasonPhrase String, responseHeaders HttpHeaders, responseBody String)` | `HttpResponse` | Build a response. |
| `statusCode()` | `StatusCode` | |
| `reason()` | `String` | The reason phrase, such as `OK`. |
| `headers()` | `HttpHeaders` | |
| `body()` | `String` | |
| `header(name String)` | `String` | Throws `HttpError` when absent. |

### HttpHeaders

A case-insensitive header map; names are stored lowercased. The underlying map is the public `headers`
field.

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpHeaders.create()` | `HttpHeaders` | An empty map. |
| `set(name String, value String)` | — | Set a header. |
| `get(name String)` | `String` | Throws `HttpError` when absent. |
| `has(name String)` | `bool` | Presence test. |

### Enums

| Enum | Cases |
|------|-------|
| `HttpMethod` | `get`, `post`, `put`, `delete`, `head`, `patch`, `options` |
| `HttpError` | `invalidUrl`, `connectFailed`, `sendFailed`, `recvFailed`, `invalidResponse`, `unsupportedScheme` |
| `StatusCode` | `ok` 200, `created` 201, `accepted` 202, `noContent` 204, `movedPermanently` 301, `found` 302, `notModified` 304, `badRequest` 400, `unauthorized` 401, `forbidden` 403, `notFound` 404, `methodNotAllowed` 405, `requestTimeout` 408, `conflict` 409, `gone` 410, `lengthRequired` 411, `payloadTooLarge` 413, `tooManyRequests` 429, `headerFieldsTooLarge` 431, `internalServerError` 500, `notImplemented` 501, `badGateway` 502, `serviceUnavailable` 503, `httpVersionNotSupported` 505 |

```maxon
function serveOnce(listener TcpListener) returns ExitCode throws NetworkError
	let conn = try listener.accept()
	_ = try conn.recv(4096)
	_ = try conn.send("HTTP/1.1 200 OK\r\nX-Demo: yes\r\nContent-Length: 2\r\n\r\nhi")
	conn.close()
	return 0
end 'serveOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let server = async serveOnce(listener)

	let response = try HttpClient.get("http://127.0.0.1:{listener.port()}/hello") otherwise (e) 'failed'
		print("request failed: {e}\n")
		return 1
	end 'failed'

	_ = try await server otherwise 9
	let demo = try response.header("x-demo") otherwise "absent"
	print("{response.statusCode()} {response.reason()} {response.body()} {demo}\n")
	return 0
end 'main'
```

Output: `200 OK hi yes`.

### Free functions

| Function | Returns | Description |
|----------|---------|-------------|
| `reasonPhraseOf(code StatusCode)` | `String` | The reason phrase HTTP/1.1 pairs with a status code, such as `Not Found` for 404. |

### Header parsing

A header line is `name:value`. The name is an HTTP token, and the value is trimmed of spaces and tabs at both
ends, so a line with no space after its colon is a valid header. A value may hold any byte but a control
character, tab excepted. A line that breaks one of these rules, or has no colon, is malformed: `HttpClient`
reading such a response throws `HttpError.invalidResponse`, and `HttpServer` answers such a request `400`.

A header that arrives more than once is read as one, its values joined by `, ` in arrival order.
`Set-Cookie` values are joined by a line feed, since a cookie's own value can hold a comma.
`Content-Length` repeated with a different value is malformed, and a repeat of the same value reads as one.
A request with more than one `Host` line is malformed.

A response line that opens with a space or tab continues the header above it (obsolete line folding), and
`HttpClient` joins the two with one space. In a request that line is malformed.

A `Host` header set with `HttpRequest.setHeader` is sent as set: the URL's authority supplies `Host` only
when the caller set none.

## HttpServer

An HTTP/1.1 server over `TcpListener`, on the same green-thread scheduler `TcpClient` and `HttpClient`
run on. It implements a documented subset: bodies framed by `Content-Length`, one request per connection,
and every response sent with `Connection: close`, after which the connection closes. Keep-alive, chunked
transfer, TLS and `Expect: 100-continue` are outside it.

### HttpServer

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `HttpServer.bind(host String, port NetworkPort)` | `HttpServer` | `NetworkError` | Bind and listen. `port: 0` takes whichever free port the kernel gives. |
| `port()` | `NetworkPort` | — | The port this server is bound to. |
| `setLimits(caps HttpServerLimits)` | — | — | Replace the caps later requests are parsed under. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `HttpServerError` | Make an `accept()` still waiting `milliseconds` after this call throw `acceptTimedOut`; `0` clears it. Throws `listenerClosed` once the server is closed. |
| `accept()` | `HttpExchange` | `HttpServerError` | Take the next connection and the one request on it. An `OPTIONS *` is answered here, and the next connection taken. |
| `serve(handler HttpHandler)` | `HttpServeEnd` | — | Accept and answer until `close()`, a listener failure or the accept deadline. |
| `close()` | — | — | Stop listening: a parked or later `accept()` throws `listenerClosed`, which ends `serve`. |

`serve` hands each exchange to `handler.handle`. A handler that throws, or returns without answering, has
its exchange answered `500` with an empty body, and `handler.faulted` is told. A request `accept()` refuses
has already been answered with its status (see [Protocol errors](#protocol-errors)), and `serve` goes on
to the next connection. When the OS refuses one connection (`acceptInterrupted`), `serve` tells
`handler.faulted`, waits 5 ms and accepts again; each consecutive interruption doubles the wait, up to
1 s, and the next connection taken resets it. `HttpAcceptRetryMs` is `int(0 to 1000)`, that wait.

`serve` returns an `HttpServeEnd` naming why it stopped:

| Case | Meaning |
|------|---------|
| `closed` | `close()` was called |
| `listenerFailed` | The listener failed (`acceptFailed`) |
| `acceptDeadlinePassed` | The accept deadline passed (`acceptTimedOut`) |

### Requests

A request line is `METHOD target HTTP/x.y`, its three parts separated by single spaces. The method is one
of `HttpMethod`'s, in upper case. The target takes one of three forms:

- origin-form, `/path?query`;
- absolute-form, `http://host:port/path?query`, the scheme in any case. The URL's host and port come from
  the target, and an HTTP/1.1 request carries a `Host` header beside it as well;
- `*`, with `OPTIONS` alone. `accept()` answers `OPTIONS *` itself with `200`, an `Allow` header listing
  every `HttpMethod` and an empty body, and goes on to the next connection.

A path and query hold RFC 3986 characters and percent escapes, and a host is checked as `URL.parse` checks
one. An HTTP/1.1 request carries a `Host` header; an HTTP/1.0 request may leave it out. Up to four empty
lines ahead of the request line are skipped. Header lines follow [Header parsing](#header-parsing).

### HttpExchange

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `request()` | `HttpRequest` | — | The request that arrived, with its body. For an origin-form target `url()` is built as `http://<Host><target>`, so `path()` and `query()` read the target; an HTTP/1.0 request that sent no `Host` has a hostless `url()`, whose `host()` throws `fieldNotPresent`. For an absolute-form target `url()` is read from the target. |
| `respond(response HttpResponse)` | — | `HttpServerError` | Write the response and close the connection. Throws `alreadyResponded` on a second answer, `invalidResponse` for a response the rules below refuse, and `sendFailed` when the write fails or outlasts `writeDeadlineMs`. |
| `respondWith(code StatusCode, body String, contentType String)` | — | `HttpServerError` | Answer a status, a body and its content type, as `respond` does. |

Every response carries `Connection: close`, and a `Content-Length` computed from its body whenever its
status allows a body: every status but 204 and 304, which end at their headers. The server writes both
headers itself, in place of any `Connection` or `Content-Length` the handler set. `respond` throws
`invalidResponse` for a 204 or 304 given a body, a header name that is an invalid HTTP token, or a reason
phrase or header value holding a control character other than tab. The answer to a `HEAD` request is the
head alone, its `Content-Length` the length of the body it would have carried.

When the peer sent bytes past the request, beyond its `Content-Length`, the server answers, shuts its
write side, and reads and discards what the peer still sends — for up to 500 ms or 256 KiB — before it
closes. Closing with those bytes unread would reset the connection, which can destroy the answer before
the peer reads it. A refused request's answer closes the same way, unless the peer has already gone.

### HttpHandler

The interface `serve` calls for each exchange.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `handle(exchange HttpExchange)` | — | `HttpServerError` | Answer one exchange. |
| `faulted(fault HttpHandlerFault)` | — | — | Told of each exchange `serve` answered in the handler's place, and of each interrupted accept. |

`HttpHandlerFault` says what happened. Every case but the last carries the `request`, and the `threw`
cases the `fault` the handler threw.

| Case | Meaning |
|------|---------|
| `threw(request, fault)` | The handler threw before answering; the exchange was answered `500` |
| `threwAfterResponding(request, fault)` | The handler threw after its answer was sent |
| `threwAfterAFailedResponse(request, fault)` | The handler threw after its own answer failed to send |
| `threwWithoutAnyAnswer(request, fault)` | The handler threw before answering, and the `500` failed to send too |
| `returnedWithoutResponding(request)` | The handler returned without answering; the exchange was answered `500` |
| `returnedWithoutAnyAnswer(request)` | The handler returned without answering, and the `500` failed to send too |
| `acceptInterrupted(retryMs)` | The OS refused one connection; `serve` accepts again in `retryMs` milliseconds |

`describe()` renders a fault as one line, such as `POST /items failed with invalidResponse and was
answered 500`.

### HttpServerLimits

`HttpServerLimits.create()` gives `maxHeaderBytes` 16384, `maxBodyBytes` 1048576, and `readDeadlineMs`
and `writeDeadlineMs` 10000 each. All four are public fields.

| Member | Type | Description |
|--------|------|-------------|
| `HttpServerLimits.create()` | `HttpServerLimits` | The defaults above. |
| `maxHeaderBytes` | `HttpByteCap` | How much of a header block is read before `headersTooLarge`. |
| `maxBodyBytes` | `HttpByteCap` | The largest `Content-Length` accepted. |
| `readDeadlineMs` | `SocketDeadlineMs` | How long a request may take to arrive whole, from the moment its connection is taken, before `readTimedOut`. |
| `writeDeadlineMs` | `SocketDeadlineMs` | How long each answer may take to send before the send fails. |

`HttpByteCap` is `int(0 to 1099511627776)`, 1 TiB.

### Protocol errors

A request `accept()` refuses is answered on its connection with the status below and an empty body before
its error is thrown, so the peer hears why.

| Condition | Status | `HttpServerError` |
|-----------|--------|-------------------|
| A request line of other than three parts, a method or target that breaks its grammar, a version spelled other than `HTTP/<digit>.<digit>`, a malformed header line, an HTTP/1.1 request without `Host`, a repeated `Host`, a `Content-Length` other than a run of digits, or one that repeats with a different value, or `*` with a method other than `OPTIONS` | 400 | `malformedRequest` |
| The connection closed or failed before the request was whole | 400, when it can still be sent | `connectionClosed` |
| A header block longer than `maxHeaderBytes` | 431 | `headersTooLarge` |
| `Content-Length` above `maxBodyBytes`, checked before the body is read | 413 | `bodyTooLarge` |
| `Transfer-Encoding` present | 501 | `unsupportedTransferEncoding` |
| A method outside `HttpMethod` | 501 | `unsupportedMethod` |
| An HTTP major version other than 1 | 505 | `unsupportedVersion` |
| The request incomplete when `readDeadlineMs` has passed | 408 | `readTimedOut` |

`accept()` also throws `listenerClosed`, `acceptFailed`, `acceptTimedOut` and `acceptInterrupted`, which
concern the listener alone. `respond` throws `alreadyResponded`, `invalidResponse` and
`sendFailed`.

```maxon
type HelloHandler implements HttpHandler
	var greeting as String

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

## URL

`URL` parses, prints and resolves URI references following RFC 3986. It implements `Equatable` and
`Stringable`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `URL.parse(input String)` | `URL` | `URLError` | Parse an absolute URL or a relative reference. |
| `URL.resolve(base URL, reference String)` | `URL` | `URLError` | Resolve `reference` against `base` (RFC 3986 section 5). |
| `scheme()` | `String` | — | `""` for a relative reference. |
| `path()` | `String` | — | Always present, possibly empty. |
| `host()` | `String` | `URLError.fieldNotPresent` | |
| `port()` | `int(0 to 65535)` | `URLError.fieldNotPresent` | |
| `userinfo()` | `String` | `URLError.fieldNotPresent` | The part before `@`. |
| `query()` | `String` | `URLError.fieldNotPresent` | Without the `?`. |
| `fragment()` | `String` | `URLError.fieldNotPresent` | Without the `#`. |
| `toString()` | `String` | — | The serialized URL. |
| `equals(other URL)` | `bool` | — | Component-wise equality. |

| URLError | Meaning |
|----------|---------|
| `emptyInput` | The input is empty or only whitespace |
| `invalidScheme` | The scheme does not start with a letter or has invalid characters |
| `invalidHost` | A host outside RFC 3986's grammar: a character a host cannot hold (a space, say), an empty host before `:port`, an IP literal that is unclosed or an invalid IPv6 or IPvFuture address, or text between `]` and the port |
| `invalidPort` | A port holding a non-digit, or above 65535 |
| `invalidEncoding` | Malformed percent-encoding, such as `%GG` |
| `relativeWithoutBase` | `resolve` was given a schemeless base |
| `fieldNotPresent` | An accessor was called for a component the URL lacks |
| `invalidPath` | Raised while `HttpServer` reads a request target, for a path that must open with `/` and opens with something else |

```maxon
function main() returns ExitCode
	let url = try URL.parse("https://user@example.com:8080/a/b?q=1#top") otherwise return 1
	let host = try url.host() otherwise ""
	let port = try url.port() otherwise 0
	let query = try url.query() otherwise ""
	print("{url.scheme()} {host} {port} {url.path()} {query} {url}\n")

	let base = try URL.parse("http://a/b/c/d?q") otherwise return 1
	let resolved = try URL.resolve(base, reference: "../g") otherwise return 1
	let mail = try URL.parse("mailto:someone@example.com") otherwise return 1
	let mailHost = try mail.host() otherwise "no host"
	print("{resolved} {mailHost}\n")
	return 0
end 'main'
```

Output: `https example.com 8080 /a/b q=1 https://user@example.com:8080/a/b?q=1#top` and `http://a/b/g no host`.
