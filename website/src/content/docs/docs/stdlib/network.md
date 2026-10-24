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
`wasm32-wasi`. Windows and macOS resolve host names through the platform resolver. On Windows a non-ASCII
host name reaches the resolver in its Unicode form, and Windows applies IDNA to it; the other targets hand
the name's UTF-8 bytes to their resolver as they are. The two Linux targets link a built-in resolver: it
accepts a numeric address, reads `/etc/hosts`, and otherwise sends an `A` query over TCP to the first
`nameserver` in `/etc/resolv.conf`. It looks the name up exactly as written, ignoring `search`/`domain`
lines, asks that one nameserver only, and waits on it for as long as it takes to answer.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Resolve `host` and connect. `resolveFailed` or `connectFailed`. |
| `TcpClient.connect(host String, port NetworkPort, deadlineMs SocketDeadlineMs)` | `TcpClient` | `NetworkError` | The same, throwing `timedOut` when the connection is not made within `deadlineMs`; `0` waits with no deadline. |
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
| `resolveFailed` | The host name did not resolve. An empty host, or one holding a NUL byte, is always this, for `connect` and `bind` alike |
| `connectFailed` | The connection was refused or could not be made |
| `sendFailed`, `recvFailed` | The OS reported an error |
| `connectionClosed` | The peer closed the connection |
| `timedOut` | A connect, read, write or accept deadline expired |
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
	let port = listener.port()
	let server = async echoOnce(listener)

	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return 2
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

## TlsClient

`TlsClient` connects to a TLS server: it opens a `TcpClient` connection, runs the handshake and returns a
[`TlsStream`](#tlsstream) that reads and writes the decrypted bytes. TLS 1.3 and TLS 1.2 are enabled; the suites
are ChaCha20-Poly1305, AES-128-GCM and AES-256-GCM (TLS 1.2 with ECDHE and an ECDSA or RSA certificate), over
X25519 and secp256r1. The server's chain is verified for the name the client connects to, against the operating
system's root certificates unless `TlsClientOptions` names others. Available on `x64-windows`, `arm64-macos`,
`arm64-linux` and `x64-linux`; refused at compile time (E3104) on `wasm32-wasi`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TlsClient.connect(host String, port NetworkPort, options TlsClientOptions = TlsClientOptions.create(), deadlineMs SocketDeadlineMs = 0)` | `TlsStream` | `TlsError` | Resolve `host`, connect, handshake. `deadlineMs` bounds the connection and, separately, the handshake: a connection that takes longer throws `timedOut`, a handshake that does throws `handshakeTimedOut`; `0` waits with no deadline. A failure after the connection was made closes it. |

The name the certificate is verified for, and the server name sent in the ClientHello, is `host`; `setServerName`
replaces it. An IP address literal is verified against the certificate's IP addresses, and no server name is
sent for it. A client that gets the server's alert, or sends one, throws the matching [`TlsError`](#tlserror).

### TlsClientOptions

`TlsClientOptions.create()` holds the defaults: the system roots, TLS 1.3 and TLS 1.2, no ALPN protocol, no
client certificate, the server name of the host, the current time. A setter returns nothing and replaces what the
option held. A configuration that no handshake could use is refused where it is set.

| Member | Throws | Description |
|--------|--------|-------------|
| `TlsClientOptions.create()` | — | The defaults. |
| `setTrustedRoots(roots RootCertificates)` | `TlsError` | The DER roots a server's chain must end in, in place of the system's. An empty list is `invalidConfiguration`. |
| `setServerName(name String)` | `TlsError` | The name to verify and to send, in place of the host. An empty name is `invalidConfiguration`. |
| `setProtocols(protocols TlsProtocolArray)` | `TlsError` | The versions to offer, `TlsProtocol.tls13` and `TlsProtocol.tls12`. An empty array is `invalidConfiguration`. |
| `setApplicationProtocols(names StringArray)` | `TlsError` | The ALPN protocol names to offer, in order. A name of no bytes or of more than 255 is `invalidConfiguration`. |
| `setIdentity(identity TlsIdentity)` | — | The certificate chain and key to present when the server asks for a client certificate. |
| `setVerificationTime(seconds UnixSeconds)` | — | The Unix time certificates are checked at, in place of the clock. |

When the system roots cannot be read, `connect` throws `trustStoreUnavailable` after the connection was made.

```maxon
function main() returns ExitCode
	var options = TlsClientOptions.create()

	try options.setApplicationProtocols(["h2", "http/1.1"]) otherwise return 1

	var stream = try TlsClient.connect("example.com", port: 443, options: options) otherwise return 2

	_ = try stream.send("GET / HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\n\r\n") otherwise return 3

	let head = try stream.recv(4096) otherwise return 4

	stream.close()
	print(head)

	return 0
end 'main'
```

## TlsListener

`TlsListener` accepts TLS connections: a `TcpListener` whose `accept()` also runs the server handshake and returns
a [`TlsStream`](#tlsstream). The server presents the chain and key of a [`TlsIdentity`](#tlsidentity). It enables TLS 1.3
and TLS 1.2 and chooses by its own preference: ChaCha20-Poly1305 first, then AES-128-GCM, then AES-256-GCM, and for
ALPN the first of its protocols the client lists. Same targets as `TlsClient`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TlsListener.bind(host String, port NetworkPort, options TlsServerOptions)` | `TlsListener` | `TlsError` | Bind and listen. Port `0` takes any free port. `bindFailed` when the address cannot be bound. |
| `port()` | `NetworkPort` | — | The bound port. |
| `accept()` | `TlsStream` | `TlsError` | The next connection, handshaken. A handshake that fails throws its error and leaves the listener accepting; `acceptFailed` when the listener is closed or has failed. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `TlsError` | Make an `accept()` still waiting for a connection `milliseconds` after this call throw `timedOut`; `0` clears it. |
| `close()` | — | — | Stop listening. Idempotent. |

The handshake runs inside `accept()`, so a client that connects and then stalls holds the listener until the options'
handshake deadline passes (30 s by default).

### TlsServerOptions

| Member | Throws | Description |
|--------|--------|-------------|
| `TlsServerOptions.create(identity TlsIdentity)` | — | The chain and key the server presents, TLS 1.3 and TLS 1.2, no ALPN protocol, no client authentication, a 30 s handshake deadline, the current time. |
| `setProtocols(protocols TlsProtocolArray)` | `TlsError` | The versions to accept. An empty array is `invalidConfiguration`. |
| `setApplicationProtocols(names StringArray)` | `TlsError` | The ALPN protocols, in order of preference. A client that offers ALPN and none of them is refused with `no_application_protocol`; a client that offers none is accepted without one. |
| `requestClientCertificates(roots RootCertificates)` | `TlsError` | Ask clients for a certificate. One that is presented must chain to `roots` for the client authentication purpose, or the handshake ends; a client that presents none is accepted. |
| `requireClientCertificates(roots RootCertificates)` | `TlsError` | The same, and a client that presents none is refused with `certificate_required` (TLS 1.3) or `handshake_failure` (TLS 1.2). No roots is `invalidConfiguration`. |
| `setHandshakeDeadline(milliseconds SocketDeadlineMs)` | — | How long `accept()` gives one connection's handshake before it throws `handshakeTimedOut`; `0` waits with no deadline. |
| `setVerificationTime(seconds UnixSeconds)` | — | The Unix time client certificates are checked at, in place of the clock. |

With client certificates asked for, the CertificateRequest names the subjects of `roots`.

```maxon
function serve(listener TlsListener) returns ExitCode
	var stream = try listener.accept() otherwise return 1
	let request = try stream.recv(4096) otherwise return 2

	_ = try stream.send("echo:{request}") otherwise return 3
	stream.close()

	return 0
end 'serve'

function main() returns ExitCode
	let chain = "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----\n"
	let key = "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----\n"
	let identity = try TlsIdentity.fromPem(chain, key: key) otherwise return 1
	var listener = try TlsListener.bind("127.0.0.1", port: 8443, options: TlsServerOptions.create(identity)) otherwise return 2

	return serve(listener)
end 'main'
```

## TlsStream

`TlsStream` is an established TLS connection, the one `TlsClient.connect` and `TlsListener.accept()` return. It is
used from one green thread at a time, through a `var`. `send`, `recv` and the deadlines follow `TcpClient`; the
bytes are the application data, and a write is split into records of at most 16 KiB.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `send(data String)`, `send(data ByteArray)` | `BytePos` | `TlsError` | Send every byte; returns the count. `closed` after `close` or `shutdownWrite`. |
| `recv(bufferSize RecvCapacity)` | `String` | `TlsError` | Between one and `bufferSize` bytes of application data. |
| `recvBytes(bufferSize RecvCapacity)` | `ByteArray` | `TlsError` | The same bytes, unconverted. After the peer's close_notify and the data before it, `connectionClosed`; a TCP connection that ends without one, `truncated`. |
| `protocol()` | `TlsProtocol` | — | `tls13` or `tls12`, whichever was negotiated. |
| `cipherSuite()` | `TlsCipherSuite` | — | `tls13(suite)` with a `Tls13CipherSuite`, or `tls12(suite)` with a `Tls12CipherSuite`. |
| `applicationProtocol()` | `TlsApplicationProtocol` | — | `negotiated(name)` or `unnegotiated`. |
| `peerCertificates()` | `ByteArrays` | — | The chain the peer presented, DER, in the order sent: the server's for a client, the client's (none when it presented none) for a server. |
| `setReadDeadline(milliseconds SocketDeadlineMs)` | — | `TlsError` | A read that waits longer than this throws `timedOut`; the stream stays usable. `0` clears it. |
| `setWriteDeadline(milliseconds SocketDeadlineMs)` | — | `TlsError` | The same for writes. |
| `shutdownWrite()` | — | `TlsError` | Send close_notify and stop sending: the peer reads the end of the stream while this side can still `recv`. |
| `close()` | — | — | Send close_notify and close the connection. Idempotent. |

`TlsApplicationProtocol` is a union: `unnegotiated`, or `negotiated(name String)`. `TlsCipherSuite` is a union of
the two suite enums. A stream that goes out of scope closes its socket as a `TcpClient` does, without a close_notify, which
the peer reads as `truncated`; `close()` is how a peer is told the data is complete. A fatal alert, from the peer
or found by this side, ends the stream: the error is thrown once and every later call throws `closed`.

```maxon
function main() returns ExitCode
	var stream = try TlsClient.connect("example.com", port: 443) otherwise return 1
	let suite = match stream.cipherSuite() 'suite'
		tls13(chosen) gives chosen.name
		tls12(chosen) gives chosen.name
	end 'suite'

	print("{stream.protocol().name} {suite}\n")
	stream.close()

	return 0
end 'main'
```

## TlsIdentity

A `TlsIdentity` is a certificate chain and the private key of its first certificate: what a server presents and
what a client presents to a server that asks. The certificates are DER, the leaf first. The key is accepted as
PKCS #8 (P-256, P-384, Ed25519 or RSA), SEC1 `ECPrivateKey` (P-256 or P-384) or PKCS #1 `RSAPrivateKey`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TlsIdentity.fromDer(chain ByteArrays, key ByteArray)` | `TlsIdentity` | `TlsError` | `invalidCertificate` for an empty chain or a first certificate that is not X.509; `invalidPrivateKey` for a key that cannot be read (an element left after it, an EC scalar that is zero or not below the curve order, an RSA modulus outside 2048 to 4096 bits, a SEC1 curve that differs from the PKCS #8 one) or whose public key is not the first certificate's. |
| `TlsIdentity.fromPem(certificates String, key String)` | `TlsIdentity` | `TlsError` | The `CERTIFICATE` blocks of `certificates`, in order, and the first `PRIVATE KEY`, `EC PRIVATE KEY` or `RSA PRIVATE KEY` block of `key`, which may be the same text. `invalidPem` for text that is not PEM or a `key` with no key block. |

## TlsError

Every failure of `TlsClient`, `TlsListener` and `TlsStream` is a `TlsError`, a union. A handshake that fails sends
the peer the alert that names the failure (`TlsAlert`) and closes the connection. Before it closes, the refusing side shuts down its write side and reads what the peer still sends, for at most 500 ms and 256 KiB, so a peer that is not speaking TLS and is still sending hears the alert and not a reset.

```maxon
union TlsError implements Error
	resolveFailed
	connectFailed
	bindFailed
	acceptFailed
	acceptInterrupted
	timedOut
	handshakeTimedOut
	sendFailed
	recvFailed
	connectionClosed
	truncated
	closed
	trustStoreUnavailable
	invalidConfiguration
	invalidCertificate
	invalidPrivateKey
	invalidPem
	certificateRejected(reason X509VerifyError)
	protocolError(alert TlsAlert)
	peerAlert(alert TlsAlert)
	peerAlertUnrecognised(code Byte)
end 'TlsError'
```

| Case | Meaning |
|------|---------|
| `resolveFailed`, `connectFailed`, `bindFailed`, `acceptFailed`, `acceptInterrupted`, `timedOut`, `sendFailed`, `recvFailed` | The `NetworkError` case of the same name. `timedOut` is a connect, accept, read or write deadline that passed. |
| `handshakeTimedOut` | A connection was made and its handshake did not finish within its deadline. A listener's `accept()` throws this for the connection that stalled and `timedOut` when no connection arrived, so a caller can tell the two apart. |
| `connectionClosed` | The peer ended the stream with close_notify, and every byte before it was read; or the transport refused a write because the peer had gone. |
| `truncated` | The TCP connection ended without a close_notify, so the data may be incomplete. |
| `closed` | The stream was closed, or ended in an error that was already thrown. |
| `trustStoreUnavailable` | The system roots cannot be read. |
| `invalidConfiguration` | An option no handshake could use. |
| `invalidCertificate`, `invalidPrivateKey`, `invalidPem` | A `TlsIdentity` input could not be used. |
| `certificateRejected(reason)` | This side refused the peer's chain, for the `X509VerifyError` `reason`: `hostnameMismatch`, `unknownAuthority`, `expired`, and the others. The alert sent is `bad_certificate`, `unknown_ca` or `certificate_expired`. |
| `protocolError(alert)` | This side ended the connection and sent `alert`: a malformed or unexpected message, no common version (`protocolVersion`), no common ALPN protocol (`noApplicationProtocol`), a missing client certificate (`certificateRequired`, `handshakeFailure`), a record longer than a record can be (`recordOverflow`). |
| `peerAlert(alert)` | The peer sent a fatal alert, or close_notify before the handshake finished. In TLS 1.3 a server decides about a client certificate after the client's handshake is complete, so the client reads that alert from its first `recv`. |
| `peerAlertUnrecognised(code)` | The peer sent an alert whose code `TlsAlert` does not name. |

A record header is checked as it is read: content type, version and length are bounded before any body is waited for,
so input that is not TLS, or a header that declares more than a record holds, ends with `protocolError` at once.

## HttpClient

An HTTP/1.1 client over `TcpClient`, and over [`TlsStream`](#tlsstream) for an `https` URL. A request carries
`Host` and `Connection: close` unless it sets its own, and a `Content-Length` whenever it has a body. `Host` leaves
out the port when it is the default of the URL's scheme, 80 for `http` and 443 for `https`.

A response body is framed by `Transfer-Encoding` when the response has one: a final `chunked` coding is
decoded (a chunk extension after `;` is ignored, and the trailer section is discarded), and any other coding
is read until the server closes the connection. Without `Transfer-Encoding` the body is `Content-Length`
bytes, or is read until the close when that is absent too. A response to `HEAD`, and a `204` or `304`,
ends at its head. Interim `1xx` responses before the final one are skipped, at most 8 of them.
`HttpError.invalidResponse` is thrown for a `101 Switching Protocols`, a response head over 64 KiB, a body over 1 TiB
(`HttpLargestBodyBytes`), and a connection that closes before the body is complete.

Every exchange has a deadline: `HttpClientLimits.timeoutMs`, 30000 by default, covers the whole exchange from
connect to the last body byte, and throws `HttpError.timedOut` when it passes. A body longer than
`HttpClientLimits.maxBodyBytes` throws `HttpError.bodyTooLarge`, however it is framed; the default is
`HttpBodyCap.unbounded`.

Limitations: a URL whose scheme is neither `http` nor `https` throws `HttpError.unsupportedScheme` before any
connection is attempted. A 3xx is returned as is, across `http` and `https` too, and the whole response is held in
memory. The URL's port defaults to 80 for `http` and 443 for `https`. Every request is its own connection, over
TLS as over TCP.

### HttpClient

| Method | Returns | Description |
|--------|---------|-------------|
| `HttpClient.send(request HttpRequest, limits HttpClientLimits = HttpClientLimits.create(), tls TlsClientOptions = TlsClientOptions.create())` | `HttpResponse` | Send a request within `limits`; `tls` configures the handshake of an `https` URL and is not read for an `http` one. |
| `HttpClient.get(url String)` | `HttpResponse` | `GET`. |
| `HttpClient.post(url String, body String)` | `HttpResponse` | `POST` with a body. |
| `HttpClient.put(url String, body String)` | `HttpResponse` | `PUT` with a body. |
| `HttpClient.delete(url String)` | `HttpResponse` | `DELETE`. |

All throw `HttpError`. `get`, `post`, `put` and `delete` use the default limits and the default TLS options.

### HTTPS

An `https` URL is fetched over TLS: the client dials the URL's port (443 by default), runs the handshake, and sends
the request inside the session. The URL's host is the server name sent in the ClientHello (SNI) and the name the
server's certificate must match; an IP address literal is matched against the certificate's IP addresses and
sends no server name. The client offers the ALPN protocol `http/1.1`, whatever `tls` lists. The chain is verified
against the operating system's root certificates, at the current time, unless `tls` says otherwise.

`tls` is a [`TlsClientOptions`](#tlsclientoptions), so every option of `TlsClient` applies:

| Option | Effect |
|--------|--------|
| `setTrustedRoots(roots)` | Verify against these DER roots in place of the system's. |
| `setServerName(name)` | Verify and send this name in place of the URL's host. |
| `setIdentity(identity)` | Present this client certificate to a server that asks for one (mutual TLS). |
| `setProtocols(protocols)` | Offer only these TLS versions. |
| `setVerificationTime(seconds)` | Check certificates at this Unix time in place of the clock. |

The handshake runs within `HttpClientLimits.timeoutMs` like the rest of the exchange, and a handshake or a read that
outlasts it throws `HttpError.timedOut`. Every other TLS failure throws `HttpError.tlsFailed(error)`, which
carries the [`TlsError`](#tlserror): `certificateRejected(reason)` for a chain the client refused (`unknownAuthority`,
`hostnameMismatch`, `expired`), `protocolError` and `peerAlert` for an alert, `truncated` for a connection that
ended without close_notify while a response was still being read, which includes a body that ends with the
connection. A server that requires a client certificate and gets none answers with an alert, which TLS 1.3
delivers on the first read, still as `tlsFailed(peerAlert(certificateRequired))`.

```maxon
function describe(error HttpError) returns String
	return match error 'which'
		tlsFailed(cause) gives "TLS failed: {cause.name}"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'which'
end 'describe'

function main() returns ExitCode
	let request = try HttpRequest.create(HttpMethod.get, url: "https://example.com/") otherwise return 1

	let response = try HttpClient.send(request) otherwise (error) 'failed'
		print("request failed: {describe(error)}\n")
		return 1
	end 'failed'

	print("{response.statusCode().number()} {response.body().byteLength()} bytes\n")
	return 0
end 'main'
```

With a root of your own and a client certificate:

```maxon
function fetch(url String, root ByteArray, identity TlsIdentity) returns String
	var tls = TlsClientOptions.create()

	try tls.setTrustedRoots(RootCertificates from [root]) otherwise return "no usable root"
	tls.setIdentity(identity)

	let request = try HttpRequest.create(HttpMethod.get, url: url) otherwise return "bad url"
	let response = try HttpClient.send(request, tls: tls) otherwise return "request failed"

	return response.body()
end 'fetch'
```

### HttpClientLimits

| Member | Type | Description |
|--------|------|-------------|
| `HttpClientLimits.create()` | `HttpClientLimits` | The defaults: `timeoutMs` 30000, `maxBodyBytes` `unbounded`. |
| `timeoutMs` | `HttpExchangeDeadlineMs` | Milliseconds for the whole exchange, connect included. `int(1 to 4294967295)`. |
| `maxBodyBytes` | `HttpBodyCap` | `unbounded`, or `atMost(bytes HttpByteCap)`. |

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
| `HttpError` | A union: `invalidUrl`, `connectFailed`, `sendFailed`, `recvFailed`, `invalidResponse`, `unsupportedScheme`, `timedOut`, `bodyTooLarge`, and `tlsFailed(error TlsError)`. Unions are matched, not compared with `==`. |
| `StatusCode` | A union; `number()` gives the code as an `HttpStatusNumber` (`int(100 to 999)`). Any status without a named case is `other(code HttpStatusNumber)`. Named cases: `ok` 200, `created` 201, `accepted` 202, `noContent` 204, `movedPermanently` 301, `found` 302, `notModified` 304, `badRequest` 400, `unauthorized` 401, `forbidden` 403, `notFound` 404, `methodNotAllowed` 405, `requestTimeout` 408, `conflict` 409, `gone` 410, `lengthRequired` 411, `payloadTooLarge` 413, `tooManyRequests` 429, `headerFieldsTooLarge` 431, `internalServerError` 500, `notImplemented` 501, `badGateway` 502, `serviceUnavailable` 503, `httpVersionNotSupported` 505 |

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
	let port = listener.port()
	let server = async serveOnce(listener)

	let response = try HttpClient.get("http://127.0.0.1:{port}/hello") otherwise (e) 'failed'
		print("request failed: {e}\n")
		return 1
	end 'failed'

	_ = try await server otherwise 9
	let demo = try response.header("x-demo") otherwise "absent"
	print("{response.statusCode().number()} {response.reason()} {response.body()} {demo}\n")
	return 0
end 'main'
```

Output: `200 OK hi yes`.

### Free functions

| Function | Returns | Description |
|----------|---------|-------------|
| `reasonPhraseOf(code StatusCode)` | `String` | The reason phrase HTTP/1.1 pairs with a status code, such as `Not Found` for 404; empty for `other`. |
| `statusCodeOf(number HttpStatusNumber)` | `StatusCode` | The named case for `number`, or `other(number)`. |

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
run on, serving HTTPS when it is bound with `bindTls`. It implements a documented subset: bodies framed by
`Content-Length`, one request per connection, and every response sent with `Connection: close`, after which the
connection closes. Keep-alive, chunked transfer and `Expect: 100-continue` are outside it.

### HttpServer

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `HttpServer.bind(host String, port NetworkPort)` | `HttpServer` | `NetworkError` | Bind and listen. `port: 0` takes whichever free port the kernel gives. |
| `HttpServer.bindTls(host String, port NetworkPort, options TlsServerOptions)` | `HttpServer` | `TlsError` | Bind and serve HTTPS: each accepted connection runs the TLS handshake under `options` before its request is read. See [HTTPS](#https-server). |
| `port()` | `NetworkPort` | — | The port this server is bound to. |
| `setLimits(caps HttpServerLimits)` | — | — | Replace the caps later requests are parsed under. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `HttpServerError` | Make an `accept()` still waiting `milliseconds` after this call throw `acceptTimedOut`; `0` clears it. Throws `listenerClosed` once the server is closed. |
| `accept()` | `HttpExchange` | `HttpServerError` | Take the next connection and the one request on it. An `OPTIONS *` is answered here, and the next connection taken. |
| `serve(handler HttpHandler)` | `HttpServeEnd` | — | Accept and answer until `close()`, a listener failure or the accept deadline. |
| `close()` | — | — | Stop listening: a parked or later `accept()` throws `listenerClosed`, which ends `serve`. |

`close()` writes the server, so the server it is called on is held in a `var`; through a `let` it is
[E3159](/docs/cli/error-codes/#e3159--semanticwritethroughimmutablerecord).

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

### HTTPS server

`HttpServer.bindTls(host, port:, options:)` takes the [`TlsServerOptions`](#tlsserveroptions) of a
[`TlsListener`](#tlslistener): the identity it presents, the TLS versions, and whether clients are asked for a
certificate (`requestClientCertificates`) or must present one (`requireClientCertificates`). The server offers
the ALPN protocol `http/1.1` whatever `options` lists, so a client that offers only other protocols (`h2`) is
refused by the handshake, and a client that offers none is served. `accept` and `serve` run the handshake of each
connection after taking it and before reading its request; the handshake is bounded by the options'
`setHandshakeDeadline` (30 s by default), and a client that stalls holds the accept loop until it passes, as `tlsFailed(handshakeTimedOut)`. The accept deadline of `setAcceptDeadline` is a different outcome: no connection arrived, `acceptTimedOut`, which ends `serve`.

A connection whose handshake fails is `HttpServerError.tlsFailed(error)` from `accept`, carrying the
[`TlsError`](#tlserror) (`certificateRejected` for a client chain the server refused, `protocolError` for the
alert it sent, `peerAlert` for the client's), and `serve` goes on to the next connection. A plain HTTP client that
reaches the TLS port is refused the same way. A TLS session that fails while the request is read is also
`tlsFailed`.

```maxon
type HelloHandler implements HttpHandler
	static function create() returns HelloHandler
		return HelloHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		let greeting = match exchange.security() 'security'
			plaintext gives "hello over plain HTTP\n"
			tls(protocol, _, _, _) gives "hello over {protocol.name}\n"
		end 'security'

		try exchange.respondWith(StatusCode.ok, body: greeting, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		printError("{fault.describe()}\n")
	end 'faulted'
end 'HelloHandler'

function main() returns ExitCode
	let certificates = try File.readText(FilePath from "server.pem") otherwise return 1
	let key = try File.readText(FilePath from "server.key") otherwise return 2
	let identity = try TlsIdentity.fromPem(certificates, key: key) otherwise return 3
	let server = try HttpServer.bindTls("0.0.0.0", port: 8443, options: TlsServerOptions.create(identity)) otherwise return 4

	_ = server.serve(HelloHandler.create())

	return 0
end 'main'
```

`HttpExchange.security()` tells a handler how the request arrived.

| `HttpConnectionSecurity` case | Meaning |
|-------------------------------|---------|
| `plaintext` | The server was bound with `bind`. |
| `tls(protocol, cipherSuite, applicationProtocol, peerCertificates)` | The TLS version (`TlsProtocol`) and cipher suite (`TlsCipherSuite`) negotiated, the ALPN protocol (`TlsApplicationProtocol`, `unnegotiated` when the client offered none), and the DER chain the client presented, leaf first, empty when it presented none. |

With `requireClientCertificates` the chain has been verified under the roots given there by the time the handler
reads it. With `requestClientCertificates` it is empty for a client that presented none.

### Requests

A request line is `METHOD target HTTP/x.y`, its three parts separated by single spaces. The method is one
of `HttpMethod`'s, in upper case. The target takes one of three forms:

- origin-form, `/path?query`;
- absolute-form, `http://host:port/path?query` (`https://` on a server bound with `bindTls`, and only that), the
  scheme in any case. The URL's host and port come from the target, and an HTTP/1.1 request carries a `Host`
  header beside it as well;
- `*`, with `OPTIONS` alone. `accept()` answers `OPTIONS *` itself with `200`, an `Allow` header listing
  every `HttpMethod` and an empty body, and goes on to the next connection.

A path and query hold RFC 3986 characters and percent escapes, and a host is checked as `URL.parse` checks
one. An HTTP/1.1 request carries a `Host` header; an HTTP/1.0 request may leave it out. Up to four empty
lines ahead of the request line are skipped. Header lines follow [Header parsing](#header-parsing).

### HttpExchange

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `security()` | `HttpConnectionSecurity` | — | Whether the request arrived over TLS, and what the handshake settled. See [HTTPS server](#https-server). |
| `request()` | `HttpRequest` | — | The request that arrived, with its body. For an origin-form target `url()` is built as `http://<Host><target>` (`https://` on a TLS server), so `path()` and `query()` read the target; an HTTP/1.0 request that sent no `Host` has a hostless `url()`, whose `host()` throws `fieldNotPresent`. For an absolute-form target `url()` is read from the target. |
| `respond(response HttpResponse)` | — | `HttpServerError` | Write the response and close the connection. Throws `alreadyResponded` on a second answer, `invalidResponse` for a response the rules below refuse, and `sendFailed` when the write fails or outlasts `writeDeadlineMs`. |
| `respondWith(code StatusCode, body String, contentType String)` | — | `HttpServerError` | Answer a status, a body and its content type, as `respond` does. |

`respond` and `respondWith` record that the exchange is answered, so an exchange from `accept()` is held in
a `var` to answer it; through a `let` it is [E3159](/docs/cli/error-codes/#e3159--semanticwritethroughimmutablerecord).
A handler answers the `exchange` parameter `serve` hands it directly.

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
| The TLS handshake failed, or the TLS session failed while the request was read | none; the connection is closed | `tlsFailed(error)` |

`accept()` also throws `listenerClosed`, `acceptFailed`, `acceptTimedOut` and `acceptInterrupted`, which
concern the listener alone. `respond` throws `alreadyResponded`, `invalidResponse` and
`sendFailed`. `HttpServerError` is a union, matched rather than compared with `==`; `tlsFailed(error)` is its one
case that carries a value, the [`TlsError`](#tlserror).

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
