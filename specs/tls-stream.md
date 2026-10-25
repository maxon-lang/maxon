---
feature: tls-stream
status: experimental
keywords: [tls, tls-stream, TlsClient, TlsListener, TlsStream, TlsError, TlsIdentity, tls 1.3, tls 1.2, mutual tls, alpn, close_notify, sockets, network, crypto]
category: network
---

# `TlsClient`, `TlsListener` and `TlsStream` -- TLS over sockets

## Documentation

`TlsClient.connect(host, port:)` opens a `TcpClient` connection and runs the TLS client handshake over it;
`TlsListener.bind(host, port:, options:)` and `accept()` do the same on the server side, except that `accept()` returns
once the TCP connection is accepted and the stream runs its handshake on its first `send`, `recv` or `shutdownWrite`,
or on `handshake()`, so a client that stalls holds only its own stream. Both hand back a `TlsStream`
whose `send`, `recv` and deadlines follow `TcpClient`, whose `close` sends close_notify, and which reports what the
handshake negotiated once it has completed: `protocol()`, `cipherSuite()`, `applicationProtocol()` and the peer's chain as
`peerCertificates()`, each throwing `handshakePending` before then. They drive the sans-IO handshake machines of `specs/tls-handshake.md`,
`specs/tls-handshake-server.md`, `specs/tls12-handshake.md` and `specs/tls12-handshake-server.md`: the stream reads
the 5-byte record header off the socket, bounds the length it declares, reads the body and feeds the whole record to the
machine, and writes what the machine queues.

A client verifies the server's chain for the name it dials (or `setServerName`) against the system roots or the roots
it was given, and the cases give it the roots of canra's test PKI and a verification time inside its certificates'
validity (`setVerificationTime`). The system roots are read from the operating system once per process, by the first connection
that trusts them, and a failed read is kept. A server presents a `TlsIdentity` built from DER or PEM, and with
`requestClientCertificates` or `requireClientCertificates` verifies the client's chain under the roots it is given.
Every failure is a `TlsError` that names what went wrong: the transport's `NetworkError`, the verification reason of
a chain this side refused (`certificateRejected`), the alert this side sent (`protocolError`), or the alert the peer
sent (`peerAlert`).

Every case runs a server and a client over loopback in one program: the server on an `async` green thread bound to a
port the kernel chose, the client on the main thread. The certificates are canra's `tls-handshake` PKI (a P-256 root,
leaves for P-256, P-384, RSA and Ed25519, an expired leaf, a second root, and a client root with a client leaf), as hex.

## Tests

<!-- test: tls-stream.echo-tls13 -->
A `TlsListener` and a `TlsClient` in one program: the client trusts the case's root and verifies `example.com`, the server presents the P-256 leaf, and a default configuration negotiates TLS 1.3 with the server's first suite, ChaCha20-Poly1305. The client's `ping` returns as `echo:ping`.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	let server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	print("{exchange(server, clientOptions: trustedClient(rootHex))}\n")

	return 0
end 'main'
```
```stdout
client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.echo-tls12-only-client -->
A client that enables only TLS 1.2 against a server that enables both: the handshake is TLS 1.2 with the server's first suite its key fits.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	let server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var client = trustedClient(rootHex)

	try client.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 1

	print("{exchange(server, clientOptions: client)}\n")

	return 0
end 'main'
```
```stdout
client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.echo-tls12-only-server -->
A server that enables only TLS 1.2 against a client that enables both, with the P-384 leaf: the client offers TLS 1.3 and TLS 1.2, and the server answers with TLS 1.2.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP384Hex = "308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c"
let keyP384Hex = "304e020100301006072a8648ce3d020106052b8104002204373035020101043011afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	var server = trustedServer(identityOf(leafP384Hex, keyHex: keyP384Hex))

	try server.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 1

	print("{exchange(server, clientOptions: trustedClient(rootHex))}\n")

	return 0
end 'main'
```
```stdout
client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.server-key-types -->
Every private key type a server presents completes both versions: P-384 and Ed25519 (TLS 1.3 signs with the scheme of the key; TLS 1.2 needs the matching ECDHE_ECDSA suite) and RSA (RSA-PSS in TLS 1.3, TLS 1.2 ECDHE_RSA suites).
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP384Hex = "308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c"
let keyP384Hex = "304e020100301006072a8648ce3d020106052b8104002204373035020101043011afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9"
let leafEd25519Hex = "308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008"
let keyEd25519Hex = "302e020100300506032b657004220420c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2"
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function line(label String, leafHex String, keyHex String, protocol TlsProtocol) returns String
	var server = trustedServer(identityOf(leafHex, keyHex: keyHex))
	var client = trustedClient(rootHex)

	try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return "protocols refused"

	return "{label} {protocol.name}: {exchange(server, clientOptions: client)}"
end 'line'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		print("{line("p384", leafHex: leafP384Hex, keyHex: keyP384Hex, protocol: protocol)}\n")
		print("{line("ed25519", leafHex: leafEd25519Hex, keyHex: keyEd25519Hex, protocol: protocol)}\n")
		print("{line("rsa", leafHex: leafRsaHex, keyHex: keyRsaHex, protocol: protocol)}\n")
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
p384 tls13: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
ed25519 tls13: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
rsa tls13: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
p384 tls12: client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
ed25519 tls12: client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
rsa tls12: client[echo:ping tls12 tls12/ecdheRsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.mtls-required-accepted -->
Mutual TLS in both versions: the server requires a client certificate under the client root, the client presents the leaf that root signed, and the server reports one peer certificate.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function line(protocol TlsProtocol) returns String
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var client = trustedClient(rootHex)

	try server.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return "roots refused"
	try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return "protocols refused"
	client.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))

	return "{protocol.name}: {exchange(server, clientOptions: client)}"
end 'line'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		print("{line(protocol)}\n")
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=1]
tls12: client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=1]
```
```exitcode
0
```

<!-- test: tls-stream.mtls-requested-accepts-anonymous -->
With client certificates requested rather than required, a client that presents none is accepted and the server sees no peer certificate; a client that presents one is verified like a required one.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function line(protocol TlsProtocol, presents bool) returns String
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var client = trustedClient(rootHex)

	try server.requestClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return "roots refused"
	try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return "protocols refused"

	if presents 'identity'
		client.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))
	end 'identity'

	return "{protocol.name} presents={presents}: {exchange(server, clientOptions: client)}"
end 'line'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		print("{line(protocol, presents: false)}\n")
		print("{line(protocol, presents: true)}\n")
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13 presents=false: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
tls13 presents=true: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=1]
tls12 presents=false: client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=0]
tls12 presents=true: client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=none] server[served ping tls12 peers=1]
```
```exitcode
0
```

<!-- test: tls-stream.mtls-required-refuses-anonymous -->
A server that requires client certificates refuses a client that has none with certificate_required (TLS 1.3) or handshake_failure (TLS 1.2), which the client reads as the peer's alert. In TLS 1.3 the client's handshake is complete before the server decides, so the client sees the alert on its first read.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	var tls13Server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try tls13Server.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1

	var tls13Client = trustedClient(rootHex)

	try tls13Client.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 2

	print("tls13: {silentExchange(tls13Server, clientOptions: tls13Client)}\n")

	var tls12Server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try tls12Server.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 3

	var tls12Client = trustedClient(rootHex)

	try tls12Client.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 4

	print("tls12: {exchange(tls12Server, clientOptions: tls12Client)}\n")

	return 0
end 'main'
```
```stdout
tls13: client[recv: peerAlert(certificateRequired)] server[recv: protocolError(certificateRequired)]
tls12: client[connect: peerAlert(handshakeFailure)] server[recv: protocolError(handshakeFailure)]
```
```exitcode
0
```

<!-- test: tls-stream.mtls-refuses-an-untrusted-client-certificate -->
A client certificate that does not chain to the roots the server was given is refused with the verification reason, and the client reads unknown_ca from the peer.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	var tls13Server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try tls13Server.requireClientCertificates(RootCertificates from [fromHex(rootHex)]) otherwise return 1

	var tls13Client = trustedClient(rootHex)

	tls13Client.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))
	try tls13Client.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 2

	print("tls13: {silentExchange(tls13Server, clientOptions: tls13Client)}\n")

	var tls12Server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try tls12Server.requireClientCertificates(RootCertificates from [fromHex(rootHex)]) otherwise return 3

	var tls12Client = trustedClient(rootHex)

	tls12Client.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))
	try tls12Client.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 4

	print("tls12: {exchange(tls12Server, clientOptions: tls12Client)}\n")

	return 0
end 'main'
```
```stdout
tls13: client[recv: peerAlert(unknownCa)] server[recv: certificateRejected(unknownAuthority)]
tls12: client[connect: peerAlert(unknownCa)] server[recv: certificateRejected(unknownAuthority)]
```
```exitcode
0
```

<!-- test: tls-stream.hostname-mismatch-is-refused -->
The name the client verifies is the host it dialled unless `setServerName` says otherwise. A certificate for `example.com` is refused for `other.example`, and for the IP literal the client dialled, with `hostnameMismatch`, and the server reads bad_certificate from the client.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		let server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
		var client = trustedClient(rootHex)

		try client.setServerName("other.example") otherwise return 1
		try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		print("{protocol.name} other.example: {exchange(server, clientOptions: client)}\n")
	end 'eachProtocol'

	let dialled = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var literal = TlsClientOptions.create()

	try literal.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise return 3
	literal.setVerificationTime(1767225600)

	print("dialled 127.0.0.1: {exchange(dialled, clientOptions: literal)}\n")

	return 0
end 'main'
```
```stdout
tls13 other.example: client[connect: certificateRejected(hostnameMismatch)] server[recv: peerAlert(badCertificate)]
tls12 other.example: client[connect: certificateRejected(hostnameMismatch)] server[recv: peerAlert(badCertificate)]
dialled 127.0.0.1: client[connect: certificateRejected(hostnameMismatch)] server[recv: peerAlert(badCertificate)]
```
```exitcode
0
```

<!-- test: tls-stream.untrusted-and-expired-certificates-are-refused -->
A client that trusts a different root refuses the server with `unknownAuthority` and the server reads unknown_ca; an expired leaf is refused with `expired` and the server reads certificate_expired.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let leafExpiredHex = "3082015b30820100a00302010202010e300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303630313030303030305a170d3235303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020349003046022100ff4547a651cc500d50b4859389ae1e206c61156d46133ee1395c0929ee64c542022100ab9136c155a74943d9c403ebf33dccc8da516a28b45fb81f39fef34ffe5bdb0a"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		let server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
		var client = trustedClient(otherRootHex)

		try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return 1

		print("{protocol.name} other root: {exchange(server, clientOptions: client)}\n")

		let expired = trustedServer(identityOf(leafExpiredHex, keyHex: keyP256Hex))
		var current = trustedClient(rootHex)

		try current.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		print("{protocol.name} expired: {exchange(expired, clientOptions: current)}\n")
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13 other root: client[connect: certificateRejected(unknownAuthority)] server[recv: peerAlert(unknownCa)]
tls13 expired: client[connect: certificateRejected(expired)] server[recv: peerAlert(certificateExpired)]
tls12 other root: client[connect: certificateRejected(unknownAuthority)] server[recv: peerAlert(unknownCa)]
tls12 expired: client[connect: certificateRejected(expired)] server[recv: peerAlert(certificateExpired)]
```
```exitcode
0
```

<!-- test: tls-stream.peer-certificates-are-the-chains-presented -->
Each side reports the chain the other presented, as DER in the order sent: the client the server's leaf, the server the client's leaf, and an unauthenticated client leaves the server's list empty.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function serveOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	try stream.handshake() otherwise (error) 'unshaken'
		return "handshake: {describe(error)}"
	end 'unshaken'

	let chain = try stream.peerCertificates() otherwise (error) 'unpresented'
		return "peers: {describe(error)}"
	end 'unpresented'

	stream.close()

	if chain.isEmpty() 'none'
		return "server saw 0 client certificate(s)"
	end 'none'

	let first = try chain.first() otherwise panic("serveOnce: a chain that is not empty has a first certificate")

	return "server saw {chain.count()} client certificate(s), first is the client leaf: {first.equals(fromHex(clientLeafHex))}"
end 'serveOnce'

function main() returns ExitCode
	var serverOptions = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try serverOptions.requestClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise return 2
	let peer = async serveOnce(listener)

	var clientOptions = trustedClient(rootHex)

	clientOptions.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))

	var stream = try TlsClient.connect("127.0.0.1", port: listener.port(), options: clientOptions) otherwise return 3
	let chain = try stream.peerCertificates() otherwise return 4
	let first = try chain.first() otherwise return 6

	print("client saw {chain.count()} server certificate(s), first is the server leaf: {first.equals(fromHex(leafP256Hex))}\n")
	print("{await peer}\n")

	stream.close()

	var anonymous = trustedClient(rootHex)
	let second = async serveOnce(listener)
	var unauthenticated = try TlsClient.connect("127.0.0.1", port: listener.port(), options: anonymous) otherwise return 5

	print("{await second}\n")
	unauthenticated.close()
	listener.close()

	return 0
end 'main'
```
```stdout
client saw 1 server certificate(s), first is the server leaf: true
server saw 1 client certificate(s), first is the client leaf: true
server saw 0 client certificate(s)
```
```exitcode
0
```

<!-- test: tls-stream.close-notify-is-observed-by-the-peer -->
`close` sends close_notify: the peer reads everything sent before it, then `connectionClosed`. A client that drops its stream without closing leaves the TCP connection ending with no close_notify, which the peer reads as `truncated`.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function readToEnd(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	var heard = ""

	while true 'reading'
		let chunk = try stream.recv(1024) otherwise (error) 'ended'
			return "heard [{heard}] then {describe(error)}"
		end 'ended'

		heard.append(chunk)
	end 'reading'

	panic("readToEnd: the loop returns when the stream ends")
end 'readToEnd'

function closes(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("goodbye") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "closed"
end 'closes'

function drops(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("vanished") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	return "dropped"
end 'drops'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
		var client = trustedClient(rootHex)

		try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		let first = async readToEnd(listener)
		let closing = closes(client, port: listener.port())

		print("{protocol.name} {closing}: server {await first}\n")

		let second = async readToEnd(listener)
		let dropping = drops(client, port: listener.port())

		print("{protocol.name} {dropping}: server {await second}\n")
		listener.close()
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13 closed: server heard [goodbye] then connectionClosed
tls13 dropped: server heard [vanished] then truncated
tls12 closed: server heard [goodbye] then connectionClosed
tls12 dropped: server heard [vanished] then truncated
```
```exitcode
0
```

<!-- test: tls-stream.shutdown-write-half-closes -->
`shutdownWrite` sends close_notify and half-closes the transport: the peer reads the request and the end of the stream, and still answers on its own half.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function answerAfterEnd(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	var request = ""

	while true 'reading'
		let chunk = try stream.recv(1024) otherwise (error) 'ended'
			_ = try stream.send("answer to [{request}] after {describe(error)}") otherwise return "send failed"
			stream.close()

			return "answered"
		end 'ended'

		request.append(chunk)
	end 'reading'

	panic("answerAfterEnd: the loop returns when the stream ends")
end 'answerAfterEnd'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
		var options = trustedClient(rootHex)

		try options.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		let server = async answerAfterEnd(listener)
		var stream = try TlsClient.connect("127.0.0.1", port: listener.port(), options: options) otherwise return 3

		_ = try stream.send("request") otherwise return 4
		try stream.shutdownWrite() otherwise return 5

		let answer = try stream.recv(1024) otherwise return 6
		let after = try stream.recv(1024) otherwise (error) 'ended'
			print("{protocol.name} client heard [{answer}] then {describe(error)}; server {await server}\n")
			listener.close()
			continue
		end 'ended'

		print("unexpected data [{after}]\n")
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13 client heard [answer to [request] after connectionClosed] then connectionClosed; server answered
tls12 client heard [answer to [request] after connectionClosed] then connectionClosed; server answered
```
```exitcode
0
```

<!-- test: tls-stream.alpn-is-negotiated -->
The server picks the first of its protocols that the client offered (its own order wins), a server that configures none negotiates none, and a client whose only protocol the server lacks is refused with no_application_protocol.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function line(label String, serverProtocols StringArray, clientProtocols StringArray) returns String
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var client = trustedClient(rootHex)

	try server.setApplicationProtocols(serverProtocols) otherwise return "server protocols refused"
	try client.setApplicationProtocols(clientProtocols) otherwise return "client protocols refused"

	return "{label}: {exchange(server, clientOptions: client)}"
end 'line'

function main() returns ExitCode
	print("{line("server order wins", serverProtocols: ["h2", "http/1.1"], clientProtocols: ["http/1.1", "h2"])}\n")
	print("{line("single match", serverProtocols: ["h2", "http/1.1"], clientProtocols: ["http/1.1"])}\n")
	print("{line("server offers none", serverProtocols: StringArray.create(), clientProtocols: ["h2"])}\n")
	print("{line("client offers none", serverProtocols: ["h2"], clientProtocols: StringArray.create())}\n")
	print("{line("no overlap", serverProtocols: ["spdy"], clientProtocols: ["h2"])}\n")

	return 0
end 'main'
```
```stdout
server order wins: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=h2] server[served ping tls13 peers=0]
single match: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=http/1.1] server[served ping tls13 peers=0]
server offers none: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
client offers none: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
no overlap: client[connect: peerAlert(noApplicationProtocol)] server[recv: protocolError(noApplicationProtocol)]
```
```exitcode
0
```

<!-- test: tls-stream.alpn-is-negotiated-in-tls12 -->
ALPN in TLS 1.2: the same selection as in TLS 1.3, reported by `applicationProtocol()`.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var client = trustedClient(rootHex)

	try server.setApplicationProtocols(["h2", "http/1.1"]) otherwise return 1
	try client.setApplicationProtocols(["http/1.1", "h2"]) otherwise return 2
	try client.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 3

	print("{exchange(server, clientOptions: client)}\n")

	return 0
end 'main'
```
```stdout
client[echo:ping tls12 tls12/ecdheEcdsaChaCha20Poly1305Sha256 alpn=h2] server[served ping tls12 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.version-mismatch-is-refused -->
A client and a server with no TLS version in common: the server refuses with protocol_version and the client reads it from the peer, whichever side holds TLS 1.3 only.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function main() returns ExitCode
	var onlyTls12 = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var onlyTls13 = trustedClient(rootHex)

	try onlyTls12.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 1
	try onlyTls13.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 2

	print("tls12 server, tls13 client: {exchange(onlyTls12, clientOptions: onlyTls13)}\n")

	var secondServer = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var secondClient = trustedClient(rootHex)

	try secondServer.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 3
	try secondClient.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return 4

	print("tls13 server, tls12 client: {exchange(secondServer, clientOptions: secondClient)}\n")

	return 0
end 'main'
```
```stdout
tls12 server, tls13 client: client[connect: peerAlert(protocolVersion)] server[recv: protocolError(protocolVersion)]
tls13 server, tls12 client: client[connect: peerAlert(protocolVersion)] server[recv: protocolError(protocolVersion)]
```
```exitcode
0
```

<!-- test: tls-stream.a-listener-survives-a-failed-handshake -->
A handshake that fails ends that `accept` with the error and nothing else: the listener takes the next client, in TLS 1.3 and in TLS 1.2.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function acceptTwice(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 2 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[accept {attempt}: {describe(error)}]")
			continue
		end 'refused'

		let heard = try stream.recv(1024) otherwise (error) 'unheard'
			report.append("[recv {attempt}: {describe(error)}]")
			continue
		end 'unheard'

		report.append("[served {attempt}: {heard}]")
		stream.close()
	end 'eachAttempt'

	return report
end 'acceptTwice'

function failure(options TlsClientOptions, port NetworkPort) returns TlsError
	_ = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return error
	end 'refused'

	panic("failure: the connection was expected to be refused")
end 'failure'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
		let peer = async acceptTwice(listener)

		var wrongRoot = trustedClient(otherRootHex)

		try wrongRoot.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		print("{protocol.name} first: {describe(failure(wrongRoot, port: listener.port()))}\n")

		var good = trustedClient(rootHex)

		try good.setProtocols(TlsProtocolArray from [protocol]) otherwise return 3

		var stream = try TlsClient.connect("127.0.0.1", port: listener.port(), options: good) otherwise return 4

		_ = try stream.send("second client") otherwise return 5
		stream.close()

		print("{protocol.name} server: {await peer}\n")
		listener.close()
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13 first: certificateRejected(unknownAuthority)
tls13 server: [recv 1: peerAlert(unknownCa)][served 2: second client]
tls12 first: certificateRejected(unknownAuthority)
tls12 server: [recv 1: peerAlert(unknownCa)][served 2: second client]
```
```exitcode
0
```

<!-- test: tls-stream.large-transfers-fragment-into-many-records -->
<!-- runs: alone -->
A 4 MiB upload in one `send` (256 full records), a 3 MiB download, and 2000 writes of 50 bytes, in both versions: every byte arrives in order, so record framing off the socket, fragmentation and the sequence numbers hold over many records and many reads.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

let uploadBytes = 4194304
let downloadBytes = 3145728
let smallWrites = 2000
let smallWriteBytes = 50

function pattern(count Count, seed Count) returns ByteArray
	var bytes = ByteArray.create()

	bytes.reserve(count)

	for index in 0 upto count 'eachByte'
		bytes.push(((index * 31 + seed) mod 251) as Byte)
	end 'eachByte'

	return bytes
end 'pattern'

function receiveAll(stream TlsStream, total Count) returns ByteArray throws TlsError
	var received = ByteArray.create()
	var reads = 0 as Count

	while received.count() < total 'receiving'
		received.append(try stream.recvBytes(65536))
		reads = reads + 1
	end 'receiving'

	return received
end 'receiveAll'

function serveBulk(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let upload = try receiveAll(stream, total: uploadBytes) otherwise (error) 'unheard'
		return "recv upload: {describe(error)}"
	end 'unheard'

	_ = try stream.send(pattern(downloadBytes, seed: 7)) otherwise (error) 'unsent'
		return "send download: {describe(error)}"
	end 'unsent'

	let small = try receiveAll(stream, total: smallWrites * smallWriteBytes) otherwise (error) 'unheardSmall'
		return "recv small: {describe(error)}"
	end 'unheardSmall'

	stream.close()

	return "upload {upload.count()} bytes intact={upload.equals(pattern(uploadBytes, seed: 3))}, small writes {small.count()} bytes intact={small.equals(pattern(smallWrites * smallWriteBytes, seed: 11))}"
end 'serveBulk'

function clientBulk(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send(pattern(uploadBytes, seed: 3)) otherwise (error) 'unsent'
		return "send upload: {describe(error)}"
	end 'unsent'

	let download = try receiveAll(stream, total: downloadBytes) otherwise (error) 'unheard'
		return "recv download: {describe(error)}"
	end 'unheard'

	let small = pattern(smallWrites * smallWriteBytes, seed: 11)

	for index in 0 upto smallWrites 'eachWrite'
		let slice = try small.slice(index * smallWriteBytes, endIndex: (index + 1) * smallWriteBytes) otherwise panic("clientBulk: the slice lies inside the pattern")

		_ = try stream.send(slice) otherwise (error) 'unsentSmall'
			return "send small: {describe(error)}"
		end 'unsentSmall'
	end 'eachWrite'

	stream.close()

	return "download {download.count()} bytes intact={download.equals(pattern(downloadBytes, seed: 7))}"
end 'clientBulk'

function main() returns ExitCode
	for protocol in TlsProtocol.allCases 'eachProtocol'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
		var client = trustedClient(rootHex)

		try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		let peer = async serveBulk(listener)
		let reported = clientBulk(client, port: listener.port())

		print("{protocol.name}: client[{reported}] server[{await peer}]\n")
		listener.close()
	end 'eachProtocol'

	return 0
end 'main'
```
```stdout
tls13: client[download 3145728 bytes intact=true] server[upload 4194304 bytes intact=true, small writes 100000 bytes intact=true]
tls12: client[download 3145728 bytes intact=true] server[upload 4194304 bytes intact=true, small writes 100000 bytes intact=true]
```
```exitcode
0
```

<!-- test: tls-stream.identities-accept-der-and-pem -->
`TlsIdentity.fromDer` and `fromPem` take the certificate chain, leaf first, and a private key as PKCS #8, SEC1 (`EC PRIVATE KEY`) or PKCS #1 (`RSA PRIVATE KEY`). They refuse text that is not PEM, a PEM with no key, a key that is not the first certificate's, a damaged key, an empty chain and a chain with a certificate that is not X.509.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let keyP256Sec1Hex = "303102010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2ba00a06082a8648ce3d030107"
let leafP384Hex = "308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c"
let keyP384Hex = "304e020100301006072a8648ce3d020106052b8104002204373035020101043011afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9"
let leafEd25519Hex = "308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008"
let keyEd25519Hex = "302e020100300506032b657004220420c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2"
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let keyRsaPkcs1Hex = "308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function report(label String, certificates String, key String) returns String
	_ = try TlsIdentity.fromPem(certificates, key: key) otherwise (error) 'refused'
		return "{label}: {describe(error)}"
	end 'refused'

	return "{label}: accepted"
end 'report'

function reportDer(label String, chain ByteArrays, key ByteArray) returns String
	_ = try TlsIdentity.fromDer(chain, key: key) otherwise (error) 'refused'
		return "{label}: {describe(error)}"
	end 'refused'

	return "{label}: accepted"
end 'reportDer'

function main() returns ExitCode
	var certificate = ""

	certificate.append("-----BEGIN CERTIFICATE-----\n")
	certificate.append("MIIBWTCCAQCgAwIBAgIBCjAKBggqhkjOPQQDAjAPMQ0wCwYDVQQDDARSb290MB4X\n")
	certificate.append("DTI1MDYwMTAwMDAwMFoXDTI2MDYwMTAwMDAwMFowDzENMAsGA1UEAwwEbGVhZjBZ\n")
	certificate.append("MBMGByqGSM49AgEGCCqGSM49AwEHA0IABAsBnP25hNAyqWkHzKmmOIgyNpW2QEp9\n")
	certificate.append("2qzGO1VZNk8spI8rXQ4RUbbAtdFyhtmlIs7kN8QvQQdC67hJN3X8R+mjTTBLMAwG\n")
	certificate.append("A1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQDAgeAMBMGA1UdJQQMMAoGCCsGAQUFBwMB\n")
	certificate.append("MBYGA1UdEQQPMA2CC2V4YW1wbGUuY29tMAoGCCqGSM49BAMCA0cAMEQCIDvBLG93\n")
	certificate.append("Bi5e7VkYa3KpXpZS9DmGhbiEWtgRKlCFu3q2AiB8l2FCb6JgU1+w+knv4mJKHzTo\n")
	certificate.append("4E60PY88ct0HLnNhlw==\n")
	certificate.append("-----END CERTIFICATE-----\n")
	var key = ""

	key.append("-----BEGIN PRIVATE KEY-----\n")
	key.append("MEECAQAwEwYHKoZIzj0CAQYIKoZIzj0DAQcEJzAlAgEBBCAPmZnZGYUo+fmG+DFf\n")
	key.append("4Meq97Ku+EAZjXe4WdxPSoxKKw==\n")
	key.append("-----END PRIVATE KEY-----\n")

	print("{report("literal P-256 PEM", certificates: certificate, key: key)}\n")
	print("{report("SEC1 P-256", certificates: pemOf("CERTIFICATE", hex: leafP256Hex), key: pemOf("EC PRIVATE KEY", hex: keyP256Sec1Hex))}\n")
	print("{report("PKCS8 P-384", certificates: pemOf("CERTIFICATE", hex: leafP384Hex), key: pemOf("PRIVATE KEY", hex: keyP384Hex))}\n")
	print("{report("PKCS8 Ed25519", certificates: pemOf("CERTIFICATE", hex: leafEd25519Hex), key: pemOf("PRIVATE KEY", hex: keyEd25519Hex))}\n")
	print("{report("PKCS8 RSA", certificates: pemOf("CERTIFICATE", hex: leafRsaHex), key: pemOf("PRIVATE KEY", hex: keyRsaHex))}\n")
	print("{report("PKCS1 RSA", certificates: pemOf("CERTIFICATE", hex: leafRsaHex), key: pemOf("RSA PRIVATE KEY", hex: keyRsaPkcs1Hex))}\n")
	print("{report("not PEM", certificates: "certificate", key: "key")}\n")
	print("{report("no key block", certificates: pemOf("CERTIFICATE", hex: leafP256Hex), key: pemOf("CERTIFICATE", hex: leafP256Hex))}\n")
	print("{report("no certificate block", certificates: pemOf("PRIVATE KEY", hex: keyP256Hex), key: pemOf("PRIVATE KEY", hex: keyP256Hex))}\n")
	print("{report("Ed25519 key for a P-256 certificate", certificates: pemOf("CERTIFICATE", hex: leafP256Hex), key: pemOf("PRIVATE KEY", hex: keyEd25519Hex))}\n")
	print("{report("P-384 key for a P-256 certificate", certificates: pemOf("CERTIFICATE", hex: leafP256Hex), key: pemOf("PRIVATE KEY", hex: keyP384Hex))}\n")

	let whole = fromHex(keyP256Hex)
	let damaged = try whole.slice(0, endIndex: whole.count() - 1) otherwise return 1

	print("{reportDer("truncated key", chain: ByteArrays from [fromHex(leafP256Hex)], key: damaged)}\n")
	print("{reportDer("empty chain", chain: ByteArrays.create(), key: whole)}\n")
	print("{reportDer("certificate that is not X.509", chain: ByteArrays from [fromHex(keyP256Hex)], key: whole)}\n")
	print("{reportDer("DER", chain: ByteArrays from [fromHex(leafP256Hex), fromHex(rootHex)], key: whole)}\n")

	return 0
end 'main'
```
```stdout
literal P-256 PEM: accepted
SEC1 P-256: accepted
PKCS8 P-384: accepted
PKCS8 Ed25519: accepted
PKCS8 RSA: accepted
PKCS1 RSA: accepted
not PEM: invalidPem
no key block: invalidPem
no certificate block: invalidCertificate
Ed25519 key for a P-256 certificate: invalidPrivateKey
P-384 key for a P-256 certificate: invalidPrivateKey
truncated key: invalidPrivateKey
empty chain: invalidCertificate
certificate that is not X.509: invalidCertificate
DER: accepted
```
```exitcode
0
```

<!-- test: tls-stream.identities-refuse-damaged-and-foreign-keys -->
`TlsIdentity.fromDer` parses the whole private key before any handshake and compares its public key with the leaf certificate's. It refuses, all as `invalidPrivateKey`, an RSA key whose private-key structure is incomplete or whose modulus is outside 2048 to 4096 bits, an EC scalar that is zero or not below the curve order, an element left after the key in a PKCS #8, SEC1 or Ed25519 encoding, a SEC1 curve that differs from the PKCS #8 algorithm parameters, and a key whose public half is not the leaf's key.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let leafP384Hex = "308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c"
let keyP384Hex = "304e020100301006072a8648ce3d020106052b8104002204373035020101043011afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9"
let leafEd25519Hex = "308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008"
let keyEd25519Hex = "302e020100300506032b657004220420c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2"
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let rsa1024Hex = "3082025d02010002818100c186110882736bbf2b5ff0a6bf07fb4c41afa7a4e7f5b42b0a9dfdb53152c6340378b5e2ddbc04143543621ddd519dd932627e250ee226009f744870f554a1b237819621cd6f0677ccee73bece0cc6f7a0d4e59dfc2c881edaaf95fadb6387ee5ab413df1690e27630cff4aa45c1aa8a5ca932f42f969dec24d2173c7a1509730203010001028180364873d8ae36cfff549c59b57c1a320c26d4adb29cfa17aa8714e008862850be6046e8c6152a5afe8c1e359468f32ed2b0c65dd9e93dd8290e91208cc6d411729e1798a2757af05e67ad831438d79084cc2870e35f60d458cd0ab4b22941064895bcdd9f97fe64e82b7015d416fcecdb14ced1c62f9c26d9eec557ca2360af11024100e73a8c4946aea3b61c6734805618755f2d9332765a69f6c742c089d58358d364cdfe3ce6567cbce24a9f3a8c8a810392e6ddffb9ac624c112f2ecd9946c8946b024100d641753ddd162be5cc7e2103baed52a9aaca337161397c235dacb54393e81ea4ed7d3642a1f1756f5a50a0a83a28be5cb1378cacb31ad4335dd050f4b146611902410093f761625abad2c46cb4966231b60115e1023ade85c91d4431b12802dbc471b22627b52ce8d775bf916507807bf94722305e8b9073818b8fcb93b5b98a143cf5024078b9e996323d8bfeb07b619ef875415ed91d965f0ca9978955ff3b2418f37250fc80d1523a6e13fdeaa603e0c6e4ae5f0c7c8087cf640eade2eebeaf2234c6010241009a7f5f7bd691e6b391e01e793446319d989c8fefe8771bed4d7d929373ab68859f5611ac0a367cd77506023c2dc5305c03f9e7510e880795562fb913b02f8ec1"
let rsaOtherHex = "308204a30201000282010100b69562ad5984050c61af98f9cca16c2f5f6550841c1f1e0268695e6893e8b44ab5680ba84c5b63129b96b2d815fd29dc2490aa519564fe9f5c492f2efd3dd70f2075ac3ee44d55676f7d1ac3fbe90b68c3db41ef6b2bff3ebf5bbde77a119325ed38ae18ff5a4083a081d5dab9a5e37922b18712475294dc771617af39c7e0417bffffa10d9533f49f6e9150a96dd395ad7c650b201b7204c7d4e5aade1313ea1af0f7b3e2edeb42ed979f0148f51b3ac5060441317d3081ec20af13e541a2f1c3b9fb4d5e3ed354ca4570dc112e74080f7da2887dbcad6f3e35f294a1e4d1ce0ee93413118b631aa91238c65e495e65e719e7c149898af52b753b219a03da8f0203010001028201007cfc903b389f0e51a6816d47fb13b99a484473b4ac07852482d336a48233f74f79154b7d9f1fa41775d8d87ee1b6973432f7b10ffe23cfc5c987f3bbc7f1d049739ffa146776a4f314b7e05daf119e14ce9646719dbcd592dfadeeb709b204381f4700aeee069b493fd40150246bf1e6d7a00d65aedb523a056f6331396da10e2a7222fbaebb8c8ed5b1b911cbead180334ce4a170593a053fd1dac2c50eb04633e6f6979b43bccffa41b946ed61b34974cb59cdd225a54c622f9cab237d6ebe7836d8b86eccca985b9f284e27c9cfce9021b5271a59b89a4bb90186a9980c487b65c0f0816cb6a29891e1b5935b0d777f76ba73071605124a030b8256b97b2102818100e374b53deb8a75aee0c4ddd5895a6a0f36ffff8bd725d348eecfca94fa3d8bd2adc0d77d1e1d86373434ab5c87e94e9b5b81872b59a46bf5847080f270172c0d52c0fcf05cd70f25282724731d0a565f318676ba50fa805beb8b82dd28d99dcb003fa5818058a5a82bd08c3a3ae00e2312916d568863d8408e0b2d5b407eab1f02818100cd7f197c50faf830555cb0d48dcc4aed9c3fda7ab858fb27c6e10fce6e771179e02999cb437f2d53666e3c54f899426913a239ea7b46f11477d356598f8c61ed982953758d04e732e6d774a6141ff6c954901a5e3ce9f75288f6fef21c1439b88bf6fbb5f720bdfcd047a8859ab27aef5eee76035effd1aa7ae8173a56eb5291028180685da9b7e2b6e26ebe95b64fffb92bc7156e7b5c37f9142c77df824dbf50b432942552fb06f1771f27662f66c2ba0ba67f633c996885683ffc67fed2bf19f2cd9e360d19611010d3f4cb990ef4b9448e63541670412ddfedcd7373357543fd02237810c20e191ed1be60efa6dc52e849b1806711f87d0fc1f72b2bc19e2340f30281805b29f19277127f9c61a237487487970e066d456d7feee9ace1cbe2893ff44eaa74c9bfd2aee4f2a4b7225c5d8dd3dfe461b36c621bc2831d03739c312b3faa0a0fa0f5c78682105742957ec856d13f585dcd78052a9cd8fda28980dcc39c2d6733a0a4563b862902c91ba12574558db837b32e639c777cfa5680341dcb85a6210281810086071742f1ded95f61ff86bd7306306bee57c6d53159b01929ddf1f834e113612ae3cf99d9883081f7bcd8931f1ac963086c49cd4d5c04b4d342f916d3b8e6a02638897f74fdacd39c30f8b497aafa5d7eec94efa35a8d2323f16eeecc3b18b78e7820b39bb565231466319e29b7aca133009f7ff5ca5868172d8ef01f3b6c28"

let derSequence = 0x30
let derInteger = 0x02
let derBitString = 0x03
let derOctetString = 0x04
let derExplicitParameters = 0xA0
let derExplicitPublicKey = 0xA1
let oidEcPublicKey = "06072a8648ce3d0201"
let oidP256 = "06082a8648ce3d030107"
let oidP384 = "06052b81040022"
let nullElement = "0500"
let p256Order = "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551"
let p256Bytes = 32
let p384Bytes = 48
let ed25519Bytes = 32

function element(tag Byte, body ByteArray) returns ByteArray
	if body.count() >= 0x80 'tooLong'
		panic("the case builds short elements only")
	end 'tooLong'

	var encoded = ByteArray.create()
	encoded.push(tag)
	encoded.push(body.count() as Byte)
	encoded.append(body)

	return encoded
end 'element'

function join(first ByteArray, second ByteArray) returns ByteArray
	var joined = first.clone()
	joined.append(second)

	return joined
end 'join'

function lastBytes(der ByteArray, count Count) returns ByteArray
	return try der.slice(der.count() - count, endIndex: der.count()) otherwise panic("the key is longer than its scalar")
end 'lastBytes'

function otherScalar(scalar ByteArray) returns ByteArray
	var other = scalar.clone()
	let last = try other.get(other.count() - 1) otherwise panic("a scalar has a last byte")

	try other.set(other.count() - 1, value: last xor 1) otherwise panic("the last byte is inside the scalar")

	return other
end 'otherScalar'

function zeros(count Count) returns ByteArray
	var bytes = ByteArray.create()
	bytes.growFilled(count, value: 0)

	return bytes
end 'zeros'

function sec1(scalar ByteArray, tail ByteArray) returns ByteArray
	var body = element(derInteger, body: fromHex("01"))
	body.append(element(derOctetString, body: scalar))
	body.append(tail)

	return element(derSequence, body: body)
end 'sec1'

function pkcs8(algorithm ByteArray, privateKey ByteArray, tail ByteArray) returns ByteArray
	var body = element(derInteger, body: fromHex("00"))
	body.append(element(derSequence, body: algorithm))
	body.append(element(derOctetString, body: privateKey))
	body.append(tail)

	return element(derSequence, body: body)
end 'pkcs8'

function ecAlgorithm(curveOid String) returns ByteArray
	return join(fromHex(oidEcPublicKey), second: fromHex(curveOid))
end 'ecAlgorithm'

function parametersOf(curveOid String) returns ByteArray
	return element(derExplicitParameters, body: fromHex(curveOid))
end 'parametersOf'

function reportDer(label String, leafHex String, key ByteArray) returns String
	_ = try TlsIdentity.fromDer(ByteArrays from [fromHex(leafHex)], key: key) otherwise (error) 'refused'
		return "{label}: {describe(error)}"
	end 'refused'

	return "{label}: accepted"
end 'reportDer'

function main() returns ExitCode
	let p256Key = fromHex(keyP256Hex)
	let p256Scalar = lastBytes(p256Key, count: p256Bytes)
	let p384Scalar = lastBytes(fromHex(keyP384Hex), count: p384Bytes)
	let ed25519Seed = lastBytes(fromHex(keyEd25519Hex), count: ed25519Bytes)
	let p256Public = try EcdhP256.publicKey(p256Scalar) otherwise return 1
	let trailing = fromHex(nullElement)
	var publicKeyBits = ByteArray.create()

	publicKeyBits.push(0)
	publicKeyBits.append(p256Public)

	let publicKeyField = element(derExplicitPublicKey, body: element(derBitString, body: publicKeyBits))
	let rsaWithoutKey = fromHex("3012020100300d06092a864886f70d0101010500")
	let rsaShell = fromHex("3006020100020105")

	print("{reportDer("PKCS8 P-256 with agreeing parameters", leafHex: leafP256Hex, key: pkcs8(ecAlgorithm(oidP256), privateKey: sec1(p256Scalar, tail: parametersOf(oidP256)), tail: ByteArray.create()))}\n")
	print("{reportDer("SEC1 P-256 with parameters and public key", leafHex: leafP256Hex, key: sec1(p256Scalar, tail: join(parametersOf(oidP256), second: publicKeyField)))}\n")
	print("{reportDer("PKCS1 holding two integers", leafHex: leafRsaHex, key: rsaShell)}\n")
	print("{reportDer("PKCS8 RSA without a private key", leafHex: leafRsaHex, key: rsaWithoutKey)}\n")
	print("{reportDer("1024-bit RSA", leafHex: leafRsaHex, key: fromHex(rsa1024Hex))}\n")
	print("{reportDer("RSA key of another certificate", leafHex: leafRsaHex, key: fromHex(rsaOtherHex))}\n")
	print("{reportDer("P-256 scalar zero", leafHex: leafP256Hex, key: sec1(zeros(p256Bytes), tail: ByteArray.create()))}\n")
	print("{reportDer("P-256 scalar n", leafHex: leafP256Hex, key: sec1(fromHex(p256Order), tail: ByteArray.create()))}\n")
	print("{reportDer("P-256 scalar above n", leafHex: leafP256Hex, key: sec1(fromHex("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"), tail: ByteArray.create()))}\n")
	print("{reportDer("P-384 scalar zero", leafHex: leafP384Hex, key: sec1(zeros(p384Bytes), tail: ByteArray.create()))}\n")
	print("{reportDer("P-256 key of another certificate", leafHex: leafP256Hex, key: sec1(otherScalar(p256Scalar), tail: ByteArray.create()))}\n")
	print("{reportDer("P-384 key of another certificate", leafHex: leafP384Hex, key: sec1(otherScalar(p384Scalar), tail: ByteArray.create()))}\n")
	print("{reportDer("Ed25519 key of another certificate", leafHex: leafEd25519Hex, key: pkcs8(fromHex("06032b6570"), privateKey: element(derOctetString, body: otherScalar(ed25519Seed)), tail: ByteArray.create()))}\n")
	print("{reportDer("SEC1 with an element after the parameters", leafHex: leafP256Hex, key: sec1(p256Scalar, tail: join(parametersOf(oidP256), second: trailing)))}\n")
	print("{reportDer("SEC1 with an element after the public key", leafHex: leafP256Hex, key: sec1(p256Scalar, tail: join(publicKeyField, second: trailing)))}\n")
	print("{reportDer("PKCS8 P-256 with an element after the key", leafHex: leafP256Hex, key: pkcs8(ecAlgorithm(oidP256), privateKey: sec1(p256Scalar, tail: ByteArray.create()), tail: trailing))}\n")
	print("{reportDer("PKCS8 Ed25519 with an element after the key", leafHex: leafEd25519Hex, key: pkcs8(fromHex("06032b6570"), privateKey: element(derOctetString, body: ed25519Seed), tail: trailing))}\n")
	print("{reportDer("PKCS8 P-256 whose SEC1 names P-384", leafHex: leafP384Hex, key: pkcs8(ecAlgorithm(oidP256), privateKey: sec1(p384Scalar, tail: parametersOf(oidP384)), tail: ByteArray.create()))}\n")
	print("{reportDer("PKCS8 P-384 whose SEC1 names P-256", leafHex: leafP256Hex, key: pkcs8(ecAlgorithm(oidP384), privateKey: sec1(p256Scalar, tail: parametersOf(oidP256)), tail: ByteArray.create()))}\n")

	return 0
end 'main'
```
```stdout
PKCS8 P-256 with agreeing parameters: accepted
SEC1 P-256 with parameters and public key: accepted
PKCS1 holding two integers: invalidPrivateKey
PKCS8 RSA without a private key: invalidPrivateKey
1024-bit RSA: invalidPrivateKey
RSA key of another certificate: invalidPrivateKey
P-256 scalar zero: invalidPrivateKey
P-256 scalar n: invalidPrivateKey
P-256 scalar above n: invalidPrivateKey
P-384 scalar zero: invalidPrivateKey
P-256 key of another certificate: invalidPrivateKey
P-384 key of another certificate: invalidPrivateKey
Ed25519 key of another certificate: invalidPrivateKey
SEC1 with an element after the parameters: invalidPrivateKey
SEC1 with an element after the public key: invalidPrivateKey
PKCS8 P-256 with an element after the key: invalidPrivateKey
PKCS8 Ed25519 with an element after the key: invalidPrivateKey
PKCS8 P-256 whose SEC1 names P-384: invalidPrivateKey
PKCS8 P-384 whose SEC1 names P-256: invalidPrivateKey
```
```exitcode
0
```

<!-- test: tls-stream.an-identity-from-pem-serves-a-handshake -->
A server built from PEM text (a P-256 key in the SEC1 form, an Ed25519 PKCS #8 key, then an RSA key in the PKCS #1 form) completes a handshake with a client that trusts the root given as PEM.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Sec1Hex = "303102010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2ba00a06082a8648ce3d030107"
let leafEd25519Hex = "308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008"
let keyEd25519Hex = "302e020100300506032b657004220420c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2"
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaPkcs1Hex = "308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function line(label String, certificates String, key String) returns String
	let identity = try TlsIdentity.fromPem(certificates, key: key) otherwise (error) 'refused'
		return "{label}: identity {describe(error)}"
	end 'refused'

	let blocks = try Pem.decode(pemOf("CERTIFICATE", hex: rootHex)) otherwise return "root PEM refused"
	var roots = RootCertificates.create()

	for block in blocks 'eachBlock'
		roots.push(block.bytes().clone())
	end 'eachBlock'

	var client = TlsClientOptions.create()

	try client.setTrustedRoots(roots) otherwise return "roots refused"
	try client.setServerName("example.com") otherwise return "name refused"
	client.setVerificationTime(1767225600)

	return "{label}: {exchange(trustedServer(identity), clientOptions: client)}"
end 'line'

function main() returns ExitCode
	print("{line("P-256 SEC1", certificates: pemOf("CERTIFICATE", hex: leafP256Hex), key: pemOf("EC PRIVATE KEY", hex: keyP256Sec1Hex))}\n")
	print("{line("Ed25519", certificates: pemOf("CERTIFICATE", hex: leafEd25519Hex), key: pemOf("PRIVATE KEY", hex: keyEd25519Hex))}\n")
	print("{line("RSA PKCS1", certificates: pemOf("CERTIFICATE", hex: leafRsaHex), key: pemOf("RSA PRIVATE KEY", hex: keyRsaPkcs1Hex))}\n")

	return 0
end 'main'
```
```stdout
P-256 SEC1: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
Ed25519: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
RSA PKCS1: client[echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none] server[served ping tls13 peers=0]
```
```exitcode
0
```

<!-- test: tls-stream.configuration-is-checked -->
A setter refuses what no handshake could use with `invalidConfiguration`: no TLS version, an empty or 256-byte protocol name, no roots, an empty server name, and no client roots. Dialling nothing is `connectFailed`, an unresolvable name `resolveFailed`, and binding a port a live listener holds `bindFailed`.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function refused(label String, failure TlsError) returns String
	return "{label}: {describe(failure)}"
end 'refused'

function connectOutcome(host String, port NetworkPort) returns String
	_ = try TlsClient.connect(host, port: port) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "connected"
end 'connectOutcome'

function bindOutcome(port NetworkPort, options TlsServerOptions) returns String
	_ = try TlsListener.bind("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "bound"
end 'bindOutcome'

function main() returns ExitCode
	var client = TlsClientOptions.create()
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))
	var longName = ""

	for _ in 0 upto 256 'eachLetter'
		longName.append("a")
	end 'eachLetter'

	try client.setProtocols(TlsProtocolArray.create()) otherwise (error) 'noProtocols'
		print("{refused("client no protocols", failure: error)}\n")
	end 'noProtocols'

	try client.setApplicationProtocols([""]) otherwise (error) 'emptyName'
		print("{refused("client empty protocol name", failure: error)}\n")
	end 'emptyName'

	try client.setApplicationProtocols([longName]) otherwise (error) 'longName'
		print("{refused("client 256-byte protocol name", failure: error)}\n")
	end 'longName'

	try client.setTrustedRoots(RootCertificates.create()) otherwise (error) 'noRoots'
		print("{refused("client no roots", failure: error)}\n")
	end 'noRoots'

	try client.setServerName("") otherwise (error) 'noName'
		print("{refused("client empty server name", failure: error)}\n")
	end 'noName'

	try server.setProtocols(TlsProtocolArray.create()) otherwise (error) 'serverNoProtocols'
		print("{refused("server no protocols", failure: error)}\n")
	end 'serverNoProtocols'

	try server.requireClientCertificates(RootCertificates.create()) otherwise (error) 'requireNone'
		print("{refused("server require with no roots", failure: error)}\n")
	end 'requireNone'

	try server.requestClientCertificates(RootCertificates.create()) otherwise (error) 'requestNone'
		print("{refused("server request with no roots", failure: error)}\n")
	end 'requestNone'

	let probe = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let freePort = probe.port()

	probe.close()

	print("connect to a closed port: {connectOutcome("127.0.0.1", port: freePort)}\n")
	print("connect to an unresolvable name: {connectOutcome("this.host.does.not.exist.invalid", port: 443)}\n")

	var held = try TlsListener.bind("127.0.0.1", port: 0, options: server) otherwise return 2

	print("bind a port in use: {bindOutcome(held.port(), options: server)}\n")

	held.close()

	return 0
end 'main'
```
```stdout
client no protocols: invalidConfiguration
client empty protocol name: invalidConfiguration
client 256-byte protocol name: invalidConfiguration
client no roots: invalidConfiguration
client empty server name: invalidConfiguration
server no protocols: invalidConfiguration
server require with no roots: invalidConfiguration
server request with no roots: invalidConfiguration
connect to a closed port: connectFailed
connect to an unresolvable name: resolveFailed
bind a port in use: bindFailed
```
```exitcode
0
```

<!-- test: tls-stream.handshakes-are-bounded-by-deadlines -->
A client with a deadline gives up on a server that accepts the connection and never answers its ClientHello (`handshakeTimedOut`), and a listener with a handshake deadline gives up on a client that connects and says nothing; neither stalls the program.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function holdOpen(silent TcpListener) returns String
	let connection = try silent.accept() otherwise return "accept failed"
	let hello = try connection.recv(4096) otherwise return "no hello"
	let ended = try connection.recv(4096) otherwise (error) 'ended'
		return "hello {hello.byteLength() > 0}, then {error.name}"
	end 'ended'

	return "hello {hello.byteLength() > 0}, then {ended.byteLength()} more bytes"
end 'holdOpen'

function dial(port NetworkPort, options TlsClientOptions) returns String
	_ = try TlsClient.connect("127.0.0.1", port: port, options: options, deadlineMs: 300) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "connected"
end 'dial'

function acceptOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	_ = try stream.recv(1024) otherwise (error) 'stalled'
		return "recv: {describe(error)}"
	end 'stalled'

	return "accepted"
end 'acceptOnce'

function main() returns ExitCode
	var silent = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let holder = async holdOpen(silent)

	let dialled = dial(silent.port(), options: trustedClient(rootHex))

	print("client: {dialled}; peer: {await holder}\n")

	silent.close()

	var options = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	options.setHandshakeDeadline(300)

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: options) otherwise return 2
	let waiting = async acceptOnce(listener)
	let mute = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 3

	print("server: {await waiting}\n")
	mute.close()
	listener.close()

	return 0
end 'main'
```
```stdout
client: handshakeTimedOut; peer: hello true, then connectionClosed
server: recv: handshakeTimedOut
```
```exitcode
0
```

<!-- test: tls-stream.input-that-is-not-tls-is-refused-before-its-body -->
The server reads the 5-byte record header first and refuses what cannot be a TLS record: an HTTP request, a record of an unknown protocol version, and a record that declares a body past the bound, without waiting for a body that never comes. The peer receives the alert, a plaintext record of type 21.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function refuseThree(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 3 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[{attempt}: accept {describe(error)}]")
			continue
		end 'refused'

		try stream.handshake() otherwise (error) 'unshaken'
			report.append("[{attempt}: {describe(error)}]")
			continue
		end 'unshaken'

		report.append("[{attempt}: accepted]")
	end 'eachAttempt'

	return report
end 'refuseThree'

function talk(port NetworkPort, bytes ByteArray) returns String
	let raw = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect failed"

	_ = try raw.send(String.from(bytes)) otherwise return "send failed"

	let reply = try raw.recv(64) otherwise (error) 'unanswered'
		return "no reply: {error.name}"
	end 'unanswered'

	return toHex(reply.toByteArray())
end 'talk'

function main() returns ExitCode
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let peer = async refuseThree(listener)

	print("http request: {talk(listener.port(), bytes: "GET / HTTP/1.1\r\nHost: x\r\n\r\n".toByteArray())}\n")
	print("unknown version: {talk(listener.port(), bytes: ByteArray from [0x16, 0x04, 0x01, 0x00, 0x05])}\n")
	print("oversized body: {talk(listener.port(), bytes: ByteArray from [0x16, 0x03, 0x03, 0xFF, 0xFF])}\n")
	print("server: {await peer}\n")
	listener.close()

	return 0
end 'main'
```
```stdout
http request: 1503030002020a
unknown version: 15030300020246
oversized body: 15030300020216
server: [1: protocolError(unexpectedMessage)][2: protocolError(protocolVersion)][3: protocolError(recordOverflow)]
```
```exitcode
0
```

<!-- test: tls-stream.a-closed-stream-refuses-further-use -->
After `close`, `send`, `recv`, `shutdownWrite` and the two deadline setters throw `closed`, and `close` again does nothing.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function sendOutcome(stream TlsStream) returns String
	_ = try stream.send("late") otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "sent"
end 'sendOutcome'

function recvOutcome(stream TlsStream) returns String
	_ = try stream.recv(16) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "received"
end 'recvOutcome'

function shutdownOutcome(stream TlsStream) returns String
	try stream.shutdownWrite() otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "shut down"
end 'shutdownOutcome'

function readDeadlineOutcome(stream TlsStream) returns String
	try stream.setReadDeadline(1000) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "set"
end 'readDeadlineOutcome'

function writeDeadlineOutcome(stream TlsStream) returns String
	try stream.setWriteDeadline(1000) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "set"
end 'writeDeadlineOutcome'

function serveAndClose(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	stream.close()

	return "heard {heard}"
end 'serveAndClose'

function main() returns ExitCode
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let peer = async serveAndClose(listener)
	var stream = try TlsClient.connect("127.0.0.1", port: listener.port(), options: trustedClient(rootHex)) otherwise return 2

	_ = try stream.send("last words") otherwise return 3
	stream.close()
	stream.close()

	print("send: {sendOutcome(stream)}\n")
	print("recv: {recvOutcome(stream)}\n")
	print("shutdownWrite: {shutdownOutcome(stream)}\n")
	print("setReadDeadline: {readDeadlineOutcome(stream)}\n")
	print("setWriteDeadline: {writeDeadlineOutcome(stream)}\n")

	print("server: {await peer}\n")
	listener.close()

	return 0
end 'main'
```
```stdout
send: closed
recv: closed
shutdownWrite: closed
setReadDeadline: closed
setWriteDeadline: closed
server: heard last words
```
```exitcode
0
```

<!-- test: tls-stream.a-handshake-that-times-out-is-not-an-accept-that-times-out -->
`TlsError.timedOut` is an accept deadline that passed with no connection; `handshakeTimedOut` is a connection that was accepted and did not finish its handshake in time, for the listener and for the client. A listener goes on accepting after a stalled handshake.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function refuseThree(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 3 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[{attempt}: accept {describe(error)}]")
			continue
		end 'refused'

		try stream.handshake() otherwise (error) 'unshaken'
			report.append("[{attempt}: {describe(error)}]")
			continue
		end 'unshaken'

		report.append("[{attempt}: accepted]")
	end 'eachAttempt'

	return report
end 'refuseThree'

function talk(port NetworkPort, bytes ByteArray) returns String
	let raw = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect failed"

	_ = try raw.send(String.from(bytes)) otherwise return "send failed"

	let reply = try raw.recv(64) otherwise (error) 'unanswered'
		return "no reply: {error.name}"
	end 'unanswered'

	return toHex(reply.toByteArray())
end 'talk'

function describeStall(error TlsError) returns String
	return match error 'error'
		handshakeTimedOut gives "handshakeTimedOut"
		timedOut gives "timedOut"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem or
		certificateRejected or
		protocolError or
		peerAlert or
		peerAlertUnrecognised gives "other: {describe(error)}"
	end 'error'
end 'describeStall'

function holdOpen(silent TcpListener) returns String
	let connection = try silent.accept() otherwise return "accept failed"

	_ = try connection.recv(4096) otherwise return "no hello"

	let ended = try connection.recv(4096) otherwise (error) 'ended'
		return "then {error.name}"
	end 'ended'

	return "then {ended.byteLength()} more bytes"
end 'holdOpen'

function acceptOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describeStall(error)}"
	end 'refused'

	_ = try stream.recv(1024) otherwise (error) 'stalled'
		return "recv: {describeStall(error)}"
	end 'stalled'

	return "accepted"
end 'acceptOnce'

function main() returns ExitCode
	var options = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	options.setHandshakeDeadline(5000)

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: options) otherwise return 1

	try listener.setAcceptDeadline(300) otherwise return 2

	print("nobody connects: {acceptOnce(listener)}\n")

	try listener.setAcceptDeadline(0) otherwise return 3

	let waiting = async acceptOnce(listener)
	let mute = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 4

	print("a client that says nothing: {await waiting}\n")
	mute.close()

	let again = async serveEcho(listener)
	let reply = clientTurn(trustedClient(rootHex), port: listener.port())

	print("a client after it: {reply} | {await again}\n")
	listener.close()

	var silent = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 5
	let holder = async holdOpen(silent)

	print("a client dialling a server that never answers: {dialOutcome(silent.port())} {await holder}\n")
	silent.close()

	return 0
end 'main'

function dialOutcome(port NetworkPort) returns String
	_ = try TlsClient.connect("127.0.0.1", port: port, options: trustedClient(rootHex), deadlineMs: 300) otherwise (error) 'refused'
		return describeStall(error)
	end 'refused'

	return "connected"
end 'dialOutcome'
```
```stdout
nobody connects: accept: timedOut
a client that says nothing: recv: handshakeTimedOut
a client after it: echo:ping tls13 tls13/chaCha20Poly1305Sha256 alpn=none | served ping tls13 peers=0
a client dialling a server that never answers: handshakeTimedOut then connectionClosed
```
```exitcode
0
```

<!-- test: tls-stream.a-peer-that-sent-more-than-the-header-still-hears-the-alert -->
A peer that is not speaking TLS and has sent far more than a record header still receives the alert the listener refused it with: the refusing side shuts down its write side and drains what the peer sent, bounded, before it closes, so the close does not reset the connection ahead of the alert. The peer's whole send completes.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function refuseThree(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 3 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[{attempt}: accept {describe(error)}]")
			continue
		end 'refused'

		try stream.handshake() otherwise (error) 'unshaken'
			report.append("[{attempt}: {describe(error)}]")
			continue
		end 'unshaken'

		report.append("[{attempt}: accepted]")
	end 'eachAttempt'

	return report
end 'refuseThree'

function talk(port NetworkPort, bytes ByteArray) returns String
	let raw = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect failed"

	_ = try raw.send(String.from(bytes)) otherwise return "send failed"

	let reply = try raw.recv(64) otherwise (error) 'unanswered'
		return "no reply: {error.name}"
	end 'unanswered'

	return toHex(reply.toByteArray())
end 'talk'

let ExtraBytes = 200000

function filler() returns String
	var text = StringBuilder.create()

	for row in 0 upto ExtraBytes / 16 'eachRow'
		text.append("{row:08x}-padding\n")
	end 'eachRow'

	return text.build()
end 'filler'

function hearAll(raw TcpClient) returns String
	var heard = ByteArray.create()

	while true 'reading'
		let chunk = try raw.recv(1024) otherwise break

		heard.append(chunk.toByteArray())
	end 'reading'

	return toHex(heard)
end 'hearAll'

function talkMore(port NetworkPort, head String) returns String
	let raw = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect failed"
	let padding = filler()

	try raw.setReadDeadline(5000) otherwise return "deadline refused"

	_ = try raw.send(head) otherwise (error) 'headUnsent'
		return "send: {error.name}"
	end 'headUnsent'

	let alert = try raw.recv(1024) otherwise (error) 'unanswered'
		return "no alert: {error.name}"
	end 'unanswered'

	_ = try raw.send(padding) otherwise (error) 'unsent'
		return "send: {error.name}"
	end 'unsent'

	return "heard {toHex(alert.toByteArray())}{hearAll(raw)}"
end 'talkMore'

function main() returns ExitCode
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let peer = async refuseThree(listener)

	print("http request and more: {talkMore(listener.port(), head: "GET / HTTP/1.1\r\nHost: x\r\n\r\n")}\n")
	print("other bytes and more: {talkMore(listener.port(), head: "hello hello hello\n")}\n")
	print("a record of an unknown version and more: {talkMore(listener.port(), head: String.from(ByteArray from [0x16, 0x04, 0x01, 0x00, 0x05]))}\n")
	print("server: {await peer}\n")
	listener.close()

	return 0
end 'main'
```
```stdout
http request and more: heard 1503030002020a
other bytes and more: heard 1503030002020a
a record of an unknown version and more: heard 15030300020246
server: [1: protocolError(unexpectedMessage)][2: protocolError(unexpectedMessage)][3: protocolError(protocolVersion)]
```
```exitcode
0
```
<!-- test: tls-stream.a-failed-flush-fails-the-write-side-for-good -->
<!-- runs: alone -->
A write deadline that passes while a peer that does not read holds the send in the kernel throws `timedOut`, and leaves the stream with part of its records written. The write side is then failed for good: a later `send` or `shutdownWrite` throws the same `timedOut` without writing a byte, even with the deadline cleared and the peer reading again, so the peer never reads a record out of step. `close` still closes, and the peer reads the end of the connection as `truncated`.

```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function acceptOne(listener TlsListener) returns TlsStream throws TlsError
	var stream = try listener.accept()

	try stream.handshake()

	return stream
end 'acceptOne'

let ChunkBytes = 1048576
let MostChunks = 256
let StalledWriteMs = 300

function chunk() returns ByteArray
	var bytes = ByteArray.create()

	bytes.resize(ChunkBytes)

	return bytes
end 'chunk'

function sendUntilStalled(stream TlsStream) returns String
	let block = chunk()

	for _ in 0 upto MostChunks 'eachChunk'
		_ = try stream.send(block) otherwise (error) 'stalled'
			return describe(error)
		end 'stalled'
	end 'eachChunk'

	return "never stalled"
end 'sendUntilStalled'

function laterSend(stream TlsStream) returns String
	_ = try stream.send("late") otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "sent"
end 'laterSend'

function laterShutdown(stream TlsStream) returns String
	try stream.shutdownWrite() otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "shut down"
end 'laterShutdown'

function drain(stream TlsStream) returns String
	while true 'draining'
		_ = try stream.recvBytes(65536) otherwise (error) 'ended'
			return describe(error)
		end 'ended'
	end 'draining'

	panic("drain: the loop returns when the stream ends")
end 'drain'

function main() returns ExitCode
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let accepted = async acceptOne(listener)
	var client = try TlsClient.connect("127.0.0.1", port: listener.port(), options: trustedClient(rootHex)) otherwise return 2
	var server = try await accepted otherwise return 3

	try client.setWriteDeadline(StalledWriteMs) otherwise return 4

	print("first send: {sendUntilStalled(client)}\n")

	try client.setWriteDeadline(0) otherwise return 5

	let draining = async drain(server)

	print("send after: {laterSend(client)}\n")
	print("shutdownWrite after: {laterShutdown(client)}\n")

	client.close()
	client.close()

	print("peer read: {await draining}\n")
	listener.close()

	return 0
end 'main'
```
```stdout
first send: timedOut
send after: timedOut
shutdownWrite after: timedOut
peer read: truncated
```
```exitcode
0
```

<!-- test: tls-stream.a-refused-stream-answers-closed-to-every-call -->
A server that requires a client certificate answers a client that presented none after the client's handshake is complete, so the client reads the alert from its first `recv`. That stream refused what its peer sent and is closed: every later call, the deadline setters included, throws `closed`.

```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function suiteName(suite TlsCipherSuite) returns String
	return match suite 'suite'
		tls13(chosen) gives "tls13/{chosen.name}"
		tls12(chosen) gives "tls12/{chosen.name}"
	end 'suite'
end 'suiteName'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function serveEcho(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let peers = (try stream.peerCertificates() otherwise return "peerCertificates: unavailable").count()
	let version = (try stream.protocol() otherwise return "protocol: unavailable").name

	stream.close()

	return "served {heard} {version} peers={peers}"
end 'serveEcho'

function clientTurn(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("ping") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let version = (try stream.protocol() otherwise return "protocol: unavailable").name
	let suite = suiteName(try stream.cipherSuite() otherwise return "cipherSuite: unavailable")
	let alpn = alpnName(try stream.applicationProtocol() otherwise return "applicationProtocol: unavailable")

	stream.close()

	return "{reply} {version} {suite} alpn={alpn}"
end 'clientTurn'

function clientListens(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	return "heard {reply}"
end 'clientListens'

function exchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientTurn(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'exchange'

function silentExchange(serverOptions TlsServerOptions, clientOptions TlsClientOptions) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "bind: {describe(error)}"
	end 'refused'

	let peer = async serveEcho(listener)
	let client = clientListens(clientOptions, port: listener.port())
	let server = await peer

	listener.close()

	return "client[{client}] server[{server}]"
end 'silentExchange'

function acceptOne(listener TlsListener) returns TlsStream throws TlsError
	return try listener.accept()
end 'acceptOne'

let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"

function recvOutcome(stream TlsStream) returns String
	_ = try stream.recv(16) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "received"
end 'recvOutcome'

function sendOutcome(stream TlsStream) returns String
	_ = try stream.send("late") otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "sent"
end 'sendOutcome'

function shutdownOutcome(stream TlsStream) returns String
	try stream.shutdownWrite() otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "shut down"
end 'shutdownOutcome'

function readDeadlineOutcome(stream TlsStream) returns String
	try stream.setReadDeadline(1000) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "set"
end 'readDeadlineOutcome'

function writeDeadlineOutcome(stream TlsStream) returns String
	try stream.setWriteDeadline(1000) otherwise (error) 'refused'
		return describe(error)
	end 'refused'

	return "set"
end 'writeDeadlineOutcome'

function main() returns ExitCode
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	try server.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1

	var options = trustedClient(rootHex)

	try options.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 2

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: server) otherwise return 3
	let peer = async serveEcho(listener)
	var stream = try TlsClient.connect("127.0.0.1", port: listener.port(), options: options) otherwise return 4

	print("the refusing read: {recvOutcome(stream)}\n")
	print("recv: {recvOutcome(stream)}\n")
	print("send: {sendOutcome(stream)}\n")
	print("shutdownWrite: {shutdownOutcome(stream)}\n")
	print("setReadDeadline: {readDeadlineOutcome(stream)}\n")
	print("setWriteDeadline: {writeDeadlineOutcome(stream)}\n")
	print("server: {await peer}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
the refusing read: peerAlert(certificateRequired)
recv: closed
send: closed
shutdownWrite: closed
setReadDeadline: closed
setWriteDeadline: closed
server: recv: protocolError(certificateRequired)
```
```exitcode
0
```


<!-- test: tls-stream.the-system-roots-are-read-once-for-every-connection -->
<!-- tier: stdlib/helpers/tls -->
Two connections made with default options, which trust the system roots, read the roots from the operating system once between them: the second connection verifies against the roots the first one loaded. The case replaces the program-wide roots source with one that answers the case's root and counts its reads.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type CountingTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns CountingTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)

		return RootCertificates from [fromHex(rootHex)]
	end 'read'
end 'CountingTrust'

function serveTwice(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 2 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[accept {attempt}: {describe(error)}]")
			continue
		end 'refused'

		let heard = try stream.recv(1024) otherwise (error) 'unheard'
			report.append("[recv {attempt}: {describe(error)}]")
			continue
		end 'unheard'

		report.append("[served {attempt}: {heard}]")
		stream.close()
	end 'eachAttempt'

	return report
end 'serveTwice'

function dial(port NetworkPort) returns String
	var options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("hello") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "connected"
end 'dial'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	SystemRoots.register(SystemRoots.loading(spawn SystemRootsLoader.create(CountingTrust.create(reads.clone()))))

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let peer = async serveTwice(listener)

	print("first: {dial(listener.port())}\n")
	print("second: {dial(listener.port())}\n")
	print("server: {await peer}\n")
	print("reads: {reads.current()}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
first: connected
second: connected
server: [served 1: hello][served 2: hello]
reads: 1
```
```exitcode
0
```

<!-- test: tls-stream.connections-that-start-together-read-the-system-roots-once -->
<!-- tier: stdlib/helpers/tls -->
Four connections made at once with default options, while the roots source is still answering its first read, wait for that one read instead of each making their own: all four verify against it, and the source is read once.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type SlowTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns SlowTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)
		sleep(150)

		return RootCertificates from [fromHex(rootHex)]
	end 'read'
end 'SlowTrust'

function serveFour(listener TlsListener) returns String
	var report = ""

	for attempt in 1 to 4 'eachAttempt'
		var stream = try listener.accept() otherwise (error) 'refused'
			report.append("[accept {attempt}: {describe(error)}]")
			continue
		end 'refused'

		let heard = try stream.recv(1024) otherwise (error) 'unheard'
			report.append("[recv {attempt}: {describe(error)}]")
			continue
		end 'unheard'

		report.append("[served {heard}]")
		stream.close()
	end 'eachAttempt'

	return report
end 'serveFour'

function dial(port NetworkPort) returns String
	var options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("hello") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "connected"
end 'dial'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	SystemRoots.register(SystemRoots.loading(spawn SystemRootsLoader.create(SlowTrust.create(reads.clone()))))

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let peer = async serveFour(listener)
	let port = listener.port()

	let first = async dial(port)
	let second = async dial(port)
	let third = async dial(port)
	let fourth = async dial(port)

	print("clients: {await first} {await second} {await third} {await fourth}\n")
	print("server: {await peer}\n")
	print("reads: {reads.current()}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
clients: connected connected connected connected
server: [served hello][served hello][served hello][served hello]
reads: 1
```
```exitcode
0
```

<!-- test: tls-stream.a-failed-read-of-the-system-roots-is-kept -->
<!-- tier: stdlib/helpers/tls -->
A roots source that cannot be read fails the connection that asked with `trustStoreUnavailable`, and the failure is kept: the next connection fails the same way without the source being read again.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

typealias ReadCount = int(0 to 1000000)
typealias ReadCountCell = SharedValue with ReadCount

type MissingTrust implements TrustSource
	let reads as ReadCountCell

	static function create(reads ReadCountCell) returns MissingTrust
		return Self{reads: reads}
	end 'create'

	function read() returns RootCertificates throws TrustStoreError
		reads.publish(reads.current() + 1)

		throw TrustStoreError.unavailable
	end 'read'
end 'MissingTrust'

function dial(port NetworkPort) returns String
	var options = TlsClientOptions.create()

	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	_ = try TlsClient.connect("127.0.0.1", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	return "connected"
end 'dial'

function main() returns ExitCode
	let reads = ReadCountCell.create(0 as ReadCount)
	SystemRoots.register(SystemRoots.loading(spawn SystemRootsLoader.create(MissingTrust.create(reads.clone()))))

	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1

	print("first: {dial(listener.port())}\n")
	print("second: {dial(listener.port())}\n")
	print("reads: {reads.current()}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
first: connect: trustStoreUnavailable
second: connect: trustStoreUnavailable
reads: 1
```
```exitcode
0
```

<!-- test: tls-stream.a-silent-client-does-not-delay-another-clients-echo -->
`accept` returns once the TCP connection is accepted and the handshake runs on the stream's first use, so a server that accepts in a loop and serves each stream on its own green thread is not held up by a client that connects and says nothing: the second client's echo completes while the first connection is still waiting for its ClientHello, and that connection's handshake then fails on its own when the client goes away.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	var bytes = ByteArray.create()
	var high = 0 as Byte
	var isHigh = true

	for symbol in hex.bytes() 'eachSymbol'
		if isHigh 'first'
			high = digit(symbol)
		end 'first' else 'second'
			bytes.push(high * 16 + digit(symbol))
		end 'second'

		isHigh = not isHigh
	end 'eachSymbol'

	return bytes
end 'fromHex'

function describe(error TlsError) returns String
	return match error 'error'
		certificateRejected(reason) gives "certificateRejected({reason.name})"
		protocolError(alert) gives "protocolError({alert.name})"
		peerAlert(alert) gives "peerAlert({alert.name})"
		peerAlertUnrecognised(code) gives "peerAlertUnrecognised({code})"
		resolveFailed or
		connectFailed or
		bindFailed or
		acceptFailed or
		acceptInterrupted or
		timedOut or
		handshakeTimedOut or
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		handshakePending or
		trustStoreUnavailable or
		entropyUnavailable or
		invalidConfiguration or
		invalidCertificate or
		invalidPrivateKey or
		invalidPem gives error.name
	end 'error'
end 'describe'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function trustedServer(identity TlsIdentity) returns TlsServerOptions
	var options = TlsServerOptions.create(identity)
	options.setVerificationTime(1767225600)

	return options
end 'trustedServer'

let rootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let leafP256Hex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let keyP256Hex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"

function trustedClient(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	try options.setServerName("example.com") otherwise panic("the case's server name was refused")
	options.setVerificationTime(1767225600)

	return options
end 'trustedClient'

let EchoBudgetMs = 1500

function echoOne(stream TlsStream) returns String
	let heard = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{heard}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	stream.close()

	return "echoed {heard}"
end 'echoOne'

function acceptTwo(listener TlsListener) returns String
	var first = try listener.accept() otherwise (error) 'firstRefused'
		return "accept 1: {describe(error)}"
	end 'firstRefused'

	let firstTask = async echoOne(first)

	var second = try listener.accept() otherwise (error) 'secondRefused'
		return "accept 2: {describe(error)}"
	end 'secondRefused'

	let secondTask = async echoOne(second)
	let secondDone = await secondTask
	let firstDone = await firstTask

	return "[silent: {firstDone}][second: {secondDone}]"
end 'acceptTwo'

function dial(options TlsClientOptions, port NetworkPort) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: port, options: options, deadlineMs: EchoBudgetMs) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send("hello") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try stream.recv(1024) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	stream.close()

	return reply
end 'dial'

function main() returns ExitCode
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))) otherwise return 1
	let server = async acceptTwo(listener)
	let silent = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2

	print("echo client: {dial(trustedClient(rootHex), port: listener.port())}\n")

	silent.close()

	print("server: {await server}\n")

	listener.close()

	return 0
end 'main'
```
```stdout
echo client: echo:hello
server: [silent: recv: truncated][second: echoed hello]
```
```exitcode
0
```
