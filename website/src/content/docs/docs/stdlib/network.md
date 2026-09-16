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
| `recv(bufferSize int(0 to u64.max))` | `String` | `NetworkError` | Up to `bufferSize` bytes. Throws `connectionClosed` when the peer has closed. |
| `setReadDeadline(milliseconds int(0 to 4294967295))` | — | `NetworkError` | Make a read that waits longer than this throw `timedOut`. Measured from this call; `0` clears it. |
| `setWriteDeadline(milliseconds int(0 to 4294967295))` | — | `NetworkError` | The same for sends. |
| `close()` | — | — | Close the connection. Idempotent. |

A deadline setter throws `connectionClosed` when the socket is already closed.

`NetworkPort` is `int(0 to 65535)`. Port `0` asks `TcpListener.bind` for any free port; it is never the port
of a live connection.

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
end 'NetworkError'
```

| Case | Meaning |
|------|---------|
| `resolveFailed` | The host name did not resolve |
| `connectFailed` | The connection was refused or could not be made |
| `sendFailed`, `recvFailed` | The OS reported an error |
| `connectionClosed` | The peer closed the connection |
| `timedOut` | A read or write deadline expired |
| `bindFailed` | The address or port could not be bound |
| `acceptFailed` | No connection could be accepted |

## TcpListener

`TcpListener` is a listening socket. It has no `send` or `recv`; `accept()` returns a `TcpClient` for each
connection. `accept()` parks its green thread until a connection arrives, so a waiting server blocks no OS
thread. It closes when the last reference goes away. Same targets as `TcpClient`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpListener.bind(host String, port NetworkPort)` | `TcpListener` | `NetworkError` | Bind and listen. Port `0` takes any free port. |
| `port()` | `NetworkPort` | — | The bound port, as the OS reports it. |
| `accept()` | `TcpClient` | `NetworkError` | The next connection. |
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

An HTTP/1.1 client over `TcpClient`. Every request is sent with `Connection: close` and the response is read
until the server closes the connection.

Limitations: plain HTTP only (no TLS), no chunked transfer decoding, no redirect following (a 3xx is
returned as is), and the whole response is held in memory. The URL's port defaults to 80.

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
| `HttpMethod` | `get`, `post`, `put`, `delete`, `head`, `patch` |
| `HttpError` | `invalidUrl`, `connectFailed`, `sendFailed`, `recvFailed`, `invalidResponse` |
| `StatusCode` | `ok` 200, `created` 201, `noContent` 204, `movedPermanently` 301, `found` 302, `notModified` 304, `badRequest` 400, `unauthorized` 401, `forbidden` 403, `notFound` 404, `methodNotAllowed` 405, `conflict` 409, `gone` 410, `internalServerError` 500, `notImplemented` 501, `badGateway` 502, `serviceUnavailable` 503 |

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
| `invalidHost` | A malformed host, such as an unclosed IPv6 bracket |
| `invalidPort` | A port that is not a number or exceeds 65535 |
| `invalidEncoding` | Malformed percent-encoding, such as `%GG` |
| `invalidPath` | Declared; no current operation raises it |
| `relativeWithoutBase` | `resolve` was given a base with no scheme |
| `fieldNotPresent` | An accessor was called for a component the URL does not have |

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
