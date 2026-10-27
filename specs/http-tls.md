---
feature: http-tls
status: experimental
keywords: [http, https, tls, HttpClient, HttpServer, HttpExchange, HttpConnectionSecurity, HttpError, mutual tls, alpn, network]
category: network
---

# HTTPS -- `HttpClient` and `HttpServer` over TLS

## Documentation

`HttpClient` speaks HTTPS to an `https` URL, and `HttpServer.bindTls(host, port:, options:)` serves it. Both run the
HTTP/1.1 code of `specs/http-client.md` and `specs/http-server.md` over a `TlsStream` instead of a `TcpClient`.

**The client.** An `https` URL dials port 443, or the URL's port, runs the TLS handshake with the URL's host as the
server name (SNI) and as the name the certificate must match, and offers the ALPN protocol `http/1.1`. The
certificate chain is verified against the system roots unless the caller passes its own `TlsClientOptions` to
`HttpClient.send(request, limits:, tls:)`: custom roots, a different server name, a client certificate for mutual TLS,
the TLS versions, the verification time. The options' ALPN list is replaced by `http/1.1`. A failed handshake or a TLS
failure after it is `HttpError.tlsFailed(error)`, which carries the `TlsError` (a rejected chain is
`certificateRejected(reason)`, an alert is `protocolError` or `peerAlert`). A handshake that outlasts
`HttpClientLimits.timeoutMs` is `HttpError.timedOut`. A response is not followed across a redirect, and a request is
sent with `Connection: close`, over `https` as over `http`.

**The server.** `HttpServer.bindTls(host, port:, options:)` takes `TlsServerOptions` (the identity, the TLS versions, client
authentication) and offers the ALPN protocol `http/1.1`. The handshake of each connection runs when its request is
first read; a connection whose handshake fails is `HttpServerError.tlsFailed(error)` from `accept`, and `serve`,
which serves each connection in its own coroutine, goes on accepting. The request's `url()` is built as `https://<Host><target>`, and an absolute-form target
must be `https://`. `HttpExchange.security()` tells a handler whether the request arrived over TLS, with which
protocol and cipher suite, which ALPN protocol, and the client's certificate chain when client authentication is on.

Every case runs a server on an `async` green thread bound to a port the kernel chose and the real client on the main
thread. The certificates are a P-256 root and a leaf for `localhost` and `127.0.0.1` made for these cases, and canra's
`tls-handshake` PKI (a leaf for `example.com`, a second root, a client root with a client leaf), as hex.

## Tests

<!-- test: http-tls.get-and-post-round-trip -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
An `HttpServer` bound with TLS options answers the real `HttpClient` over `https://`: a GET with a query, a POST with a body and a GET by IP address, each against a client that trusts the case's root. The URL's host is the name the certificate is verified for (`localhost` and `127.0.0.1` are in its SAN), the handler sees `https` in `request.url()` and `HttpConnectionSecurity.tls` from `exchange.security()` with TLS 1.3 and the ALPN protocol `http/1.1`, and the response carries `Connection: close`.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serving(server)
	let port = server.port()

	print("get: {fetch(HttpMethod.get, url: "https://localhost:{port}/hello?q=1", body: "", options: clientOptions())}\n")
	print("post: {fetch(HttpMethod.post, url: "https://localhost:{port}/submit", body: "name=maxon", options: clientOptions())}\n")
	print("ip: {fetch(HttpMethod.get, url: "https://127.0.0.1:{port}/ip", body: "", options: clientOptions())}\n")

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
get: 200 connection=close [get https://localhost/hello?q=1 body=0 tls13 alpn=http/1.1 peers=0]
post: 200 connection=close [post https://localhost/submit?- body=10 tls13 alpn=http/1.1 peers=0]
ip: 200 connection=close [get https://127.0.0.1/ip?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.tls12-and-tls13 -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
Client and server restricted to one TLS version each negotiate that version, and the handler reports it. A client that supports only TLS 1.2 against a server that supports only TLS 1.3, and the reverse, fail with the typed `HttpError.tlsFailed`, while the server keeps serving until it is closed.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function scenario(label String, serverProtocol TlsProtocol, clientProtocol TlsProtocol) returns String
	var options = serverOptions()

	try options.setProtocols(TlsProtocolArray from [serverProtocol]) otherwise return "{label}: the server protocols were refused"

	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: options) otherwise return "{label}: bind failed"
	var client = clientOptions()

	try client.setProtocols(TlsProtocolArray from [clientProtocol]) otherwise return "{label}: the client protocols were refused"

	let peer = async serving(server)
	let result = fetch(HttpMethod.get, url: "https://localhost:{server.port()}/v", body: "", options: client)

	server.close()

	let ended = await peer

	return "{label}: {result} ended={ended.name}"
end 'scenario'

function main() returns ExitCode
	print("{scenario("1.3 and 1.3", serverProtocol: TlsProtocol.tls13, clientProtocol: TlsProtocol.tls13)}\n")
	print("{scenario("1.2 and 1.2", serverProtocol: TlsProtocol.tls12, clientProtocol: TlsProtocol.tls12)}\n")
	print("{scenario("server 1.3, client 1.2", serverProtocol: TlsProtocol.tls13, clientProtocol: TlsProtocol.tls12)}\n")
	print("{scenario("server 1.2, client 1.3", serverProtocol: TlsProtocol.tls12, clientProtocol: TlsProtocol.tls13)}\n")

	return 0
end 'main'
```
```stdout
1.3 and 1.3: 200 connection=close [get https://localhost/v?- body=0 tls13 alpn=http/1.1 peers=0] ended=closed
1.2 and 1.2: 200 connection=close [get https://localhost/v?- body=0 tls12 alpn=http/1.1 peers=0] ended=closed
server 1.3, client 1.2: error tlsFailed(peerAlert(protocolVersion)) ended=closed
server 1.2, client 1.3: error tlsFailed(peerAlert(protocolVersion)) ended=closed
```
```exitcode
0
```

<!-- test: http-tls.a-large-body-round-trips -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
<!-- runs: alone -->
A 2 MiB request body goes through TLS to a handler that echoes it, and the 2 MiB response comes back byte for byte: the server's caps are raised for the body and the client's budget for the time the pure-Maxon record protection takes.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

let PayloadRows = 32768

type BodyEchoHandler implements HttpHandler
	static function create() returns BodyEchoHandler
		return BodyEchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: exchange.request().body(), contentType: "application/octet-stream")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'BodyEchoHandler'

function payload() returns String
	var text = StringBuilder.create()

	for row in 0 upto PayloadRows 'eachRow'
		text.append("{row:08x}0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-\n")
	end 'eachRow'

	return text.build()
end 'payload'

function serveEcho(server HttpServer) returns HttpServeEnd
	return server.serve(BodyEchoHandler.create())
end 'serveEcho'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serveEcho(server)
	let sent = payload()

	var request = try HttpRequest.create(HttpMethod.post, url: "https://localhost:{server.port()}/echo") otherwise return 2

	request.setBody(sent)

	var limits = HttpClientLimits.create()

	limits.timeoutMs = 110000

	let response = try HttpClient.send(request, limits: limits, tls: clientOptions()) otherwise (error) 'failed'
		print("error {describeHttp(error)}\n")
		server.close()

		return 3
	end 'failed'

	server.close()

	let ended = await peer

	print("status={response.statusCode().number()} sent={sent.byteLength()} received={response.body().byteLength()} identical={response.body() == sent} ended={ended.name}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1
	var limits = HttpServerLimits.create()

	limits.maxBodyBytes = 8388608
	limits.readDeadlineMs = 110000
	limits.writeDeadlineMs = 110000
	server.setLimits(limits)

	return exchangeOver(server)
end 'main'
```
```stdout
status=200 sent=2359296 received=2359296 identical=true ended=closed
```
```exitcode
0
```

<!-- test: http-tls.several-requests-are-served -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
Five requests in a row are each a connection and a handshake of their own, since the server and the client send `Connection: close` over TLS as over plain TCP: each answers `connection=close` and the server serves them all until it is closed.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serving(server)
	let port = server.port()

	for number in 1 to 5 'eachRequest'
		print("{number}: {fetch(HttpMethod.get, url: "https://localhost:{port}/n?{number}", body: "", options: clientOptions())}\n")
	end 'eachRequest'

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
1: 200 connection=close [get https://localhost/n?1 body=0 tls13 alpn=http/1.1 peers=0]
2: 200 connection=close [get https://localhost/n?2 body=0 tls13 alpn=http/1.1 peers=0]
3: 200 connection=close [get https://localhost/n?3 body=0 tls13 alpn=http/1.1 peers=0]
4: 200 connection=close [get https://localhost/n?4 body=0 tls13 alpn=http/1.1 peers=0]
5: 200 connection=close [get https://localhost/n?5 body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.mutual-tls -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
With `requireClientCertificates` a client that presents no certificate is refused (the typed `tlsFailed` carries the alert), the server goes on, and a client with the case's client certificate is served: the handler reads the client's chain from `exchange.security()` and finds alice's leaf. With `requestClientCertificates` the anonymous client is served too, and the handler sees an empty chain.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function chainSummary(chain ByteArrays) returns String
	let leaf = try chain.first() otherwise 'anonymous'
		return "no client certificate"
	end 'anonymous'

	return "{chain.count()} certificate, leaf is alice={toHex(leaf) == clientLeafHex}"
end 'chainSummary'

type ChainHandler implements HttpHandler
	static function create() returns ChainHandler
		return ChainHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		let body = match exchange.security() 'security'
			plaintext gives "plaintext"
			tls(_, _, _, chain) gives chainSummary(chain)
		end 'security'

		try exchange.respondWith(StatusCode.ok, body: body, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'ChainHandler'

function serveChains(server HttpServer) returns HttpServeEnd
	return server.serve(ChainHandler.create())
end 'serveChains'

function clientWithCertificate() returns TlsClientOptions
	var options = clientOptions()

	options.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))

	return options
end 'clientWithCertificate'

function scenario(label String, server HttpServer) returns String
	let peer = async serveChains(server)
	let port = server.port()
	let anonymous = fetch(HttpMethod.get, url: "https://localhost:{port}/who", body: "", options: clientOptions())
	let certified = fetch(HttpMethod.get, url: "https://localhost:{port}/who", body: "", options: clientWithCertificate())

	server.close()

	let ended = await peer

	return "{label}\n  anonymous client: {anonymous}\n  client with a certificate: {certified}\n  ended={ended.name}"
end 'scenario'

function main() returns ExitCode
	var required = serverOptions()

	try required.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1

	var requested = serverOptions()

	try requested.requestClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 2

	var requiredServer = try HttpServer.bindTls("127.0.0.1", port: 0, options: required) otherwise return 3
	var requestedServer = try HttpServer.bindTls("127.0.0.1", port: 0, options: requested) otherwise return 4

	print("{scenario("required", server: requiredServer)}\n")
	print("{scenario("requested", server: requestedServer)}\n")

	return 0
end 'main'
```
```stdout
required
  anonymous client: error tlsFailed(peerAlert(certificateRequired))
  client with a certificate: 200 connection=close [1 certificate, leaf is alice=true]
  ended=closed
requested
  anonymous client: 200 connection=close [no client certificate]
  client with a certificate: 200 connection=close [1 certificate, leaf is alice=true]
  ended=closed
```
```exitcode
0
```

<!-- test: http-tls.untrusted-misnamed-and-expired-certificates-are-refused -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
A client refuses a server certificate its roots do not lead to (`unknownAuthority`), one issued for another name (`hostnameMismatch`, from the options' server name, and from the URL's host when the certificate has only a DNS name) and one that has expired at the verification time (`expired`), each as `HttpError.tlsFailed(certificateRejected(reason))`. The server survives each refused handshake and serves the trusted client that follows.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function untrustingClient() returns TlsClientOptions
	var options = TlsClientOptions.create()

	try options.setTrustedRoots(RootCertificates from [fromHex(otherRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'untrustingClient'

function misnamingClient() returns TlsClientOptions
	var options = clientOptions()

	try options.setServerName("other.example") otherwise panic("the case's name was refused")

	return options
end 'misnamingClient'

function lateClient() returns TlsClientOptions
	var options = clientOptions()

	options.setVerificationTime(1800000000)

	return options
end 'lateClient'

function exampleOptions() returns TlsServerOptions
	return TlsServerOptions.create(identityOf(exampleLeafHex, keyHex: exampleKeyHex))
end 'exampleOptions'

function exampleClient() returns TlsClientOptions
	var options = TlsClientOptions.create()

	try options.setTrustedRoots(RootCertificates from [fromHex(exampleRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'exampleClient'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serving(server)
	let port = server.port()

	print("untrusted root: {fetch(HttpMethod.get, url: "https://localhost:{port}/", body: "", options: untrustingClient())}\n")
	print("another name: {fetch(HttpMethod.get, url: "https://localhost:{port}/", body: "", options: misnamingClient())}\n")
	print("after the certificate expired: {fetch(HttpMethod.get, url: "https://localhost:{port}/", body: "", options: lateClient())}\n")
	print("trusted: {fetch(HttpMethod.get, url: "https://localhost:{port}/", body: "", options: clientOptions())}\n")

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'exchangeOver'

function nameFromTheUrl() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: exampleOptions()) otherwise return 2
	let peer = async serving(server)
	let port = server.port()

	print("host from the url: {fetch(HttpMethod.get, url: "https://127.0.0.1:{port}/", body: "", options: exampleClient())}\n")

	var named = exampleClient()

	try named.setServerName("example.com") otherwise return 3

	print("named in the options: {fetch(HttpMethod.get, url: "https://127.0.0.1:{port}/", body: "", options: named)}\n")

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'nameFromTheUrl'

function main() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1
	let first = exchangeOver(server)

	if first != 0 'failed'
		return first
	end 'failed'

	return nameFromTheUrl()
end 'main'
```
```stdout
untrusted root: error tlsFailed(certificateRejected(unknownAuthority))
another name: error tlsFailed(certificateRejected(hostnameMismatch))
after the certificate expired: error tlsFailed(certificateRejected(expired))
trusted: 200 connection=close [get https://localhost/?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
host from the url: error tlsFailed(certificateRejected(hostnameMismatch))
named in the options: 200 connection=close [get https://127.0.0.1/?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.a-plain-peer-on-a-tls-port-is-refused-cleanly -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
A plain HTTP client that reaches the TLS port is refused with the alert and nothing else, and the server keeps serving: a peer that goes on sending 200 KB after the refusal still hears the unexpected_message alert record (the server drains what the peer sent before it closes), `HttpClient` over `http://` gets `invalidResponse`, and the https request that follows is answered. The reverse, an `https` URL on a plain server, is a typed TLS failure on the client, and the plain server keeps serving too.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

let ExtraBytes = 200000

function filler() returns String
	var text = StringBuilder.create()

	for row in 0 upto ExtraBytes / 16 'eachRow'
		text.append("{row:08x}-padding\n")
	end 'eachRow'

	return text.build()
end 'filler'

function plainBytesHeard(port NetworkPort) returns String
	var client = try TcpClient.connect("127.0.0.1", port: port) otherwise return "connect failed"
	let padding = filler()

	try client.setReadDeadline(5000) otherwise return "deadline refused"

	_ = try client.send("GET / HTTP/1.1\r\nHost: x\r\n\r\n") otherwise (error) 'headUnsent'
		return "send {error.name}"
	end 'headUnsent'

	let alert = try client.recv(1024) otherwise (error) 'unanswered'
		return "no alert {error.name}"
	end 'unanswered'

	_ = try client.send(padding) otherwise (error) 'unsent'
		return "send {error.name}"
	end 'unsent'

	var heard = alert.toByteArray()

	while true 'reading'
		let chunk = try client.recv(1024) otherwise break

		heard.append(chunk.toByteArray())
	end 'reading'

	return "heard {toHex(heard)}"
end 'plainBytesHeard'

function plainClientOnTheTlsPort() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1
	let peer = async serving(server)
	let port = server.port()

	print("raw plain bytes: {plainBytesHeard(port)}\n")

	let plain = try HttpClient.post("http://127.0.0.1:{port}/", body: filler()) otherwise (error) 'refused'
		print("HttpClient over http: error {describeHttp(error)}\n")

		print("then over https: {fetch(HttpMethod.get, url: "https://localhost:{port}/after", body: "", options: clientOptions())}\n")
		server.close()

		let ended = await peer

		print("ended: {ended.name}\n")

		return 0
	end 'refused'

	print("a plain client was answered {plain.statusCode().number()}\n")
	server.close()

	return 4
end 'plainClientOnTheTlsPort'

function tlsClientOnThePlainPort() returns ExitCode
	var server = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 1
	var limits = HttpServerLimits.create()

	limits.readDeadlineMs = 500
	server.setLimits(limits)

	let peer = async serving(server)
	let port = server.port()

	print("HttpClient over https to a plain server: {fetch(HttpMethod.get, url: "https://127.0.0.1:{port}/", body: "", options: clientOptions())}\n")

	let plain = try HttpClient.get("http://127.0.0.1:{port}/still") otherwise return 2

	print("then over http: {plain.statusCode().number()} [{plain.body()}]\n")
	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'tlsClientOnThePlainPort'

function main() returns ExitCode
	let first = plainClientOnTheTlsPort()

	if first != 0 'failed'
		return first
	end 'failed'

	return tlsClientOnThePlainPort()
end 'main'
```
```stdout
raw plain bytes: heard 1503030002020a
HttpClient over http: error invalidResponse
then over https: 200 connection=close [get https://localhost/after?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
HttpClient over https to a plain server: error tlsFailed(protocolError(unexpectedMessage))
then over http: 200 [get http://127.0.0.1/still?- body=0 plaintext]
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.a-redirect-to-https-is-returned-as-is -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
`HttpClient` does not follow redirects, so a plain server's `301` to the TLS server's URL is returned as the response, with its `Location`; fetching that location is a second request that gets the TLS server's answer.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

type RedirectHandler implements HttpHandler
	let location as String

	static function create(location String) returns RedirectHandler
		return RedirectHandler{location: location}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		var headers = HttpHeaders.create()

		headers.set("location", value: self.location)

		try exchange.respond(HttpResponse.create(StatusCode.movedPermanently, reasonPhrase: "Moved Permanently", responseHeaders: headers, responseBody: ""))
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'RedirectHandler'

function redirecting(server HttpServer, location String) returns HttpServeEnd
	return server.serve(RedirectHandler.create(location))
end 'redirecting'

function main() returns ExitCode
	var secure = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1
	var plain = try HttpServer.bind("127.0.0.1", port: 0) otherwise return 2
	let securePeer = async serving(secure)
	let location = "https://localhost:{secure.port()}/moved"
	let plainPeer = async redirecting(plain, location: location)

	let redirect = try HttpClient.get("http://127.0.0.1:{plain.port()}/") otherwise return 3
	let target = try redirect.header("location") otherwise "absent"

	print("plain server: {redirect.statusCode().number()} location matches={target == location} body={redirect.body().byteLength()} bytes\n")
	print("the location, fetched: {fetch(HttpMethod.get, url: target, body: "", options: clientOptions())}\n")

	plain.close()
	secure.close()

	let plainEnded = await plainPeer
	let secureEnded = await securePeer

	print("ended: {plainEnded.name} {secureEnded.name}\n")

	return 0
end 'main'
```
```stdout
plain server: 301 location matches=true body=0 bytes
the location, fetched: 200 connection=close [get https://localhost/moved?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed closed
```
```exitcode
0
```

<!-- test: http-tls.stalled-handshakes-are-bounded -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
A server that accepts the connection and never answers the ClientHello makes `HttpClient.send` throw `timedOut` once `HttpClientLimits.timeoutMs` has passed, and the server sees the client leave. A client that connects to the TLS server and sends nothing holds only its own connection: with no handshake deadline, so that nothing but the client's own `close()` can end it, the server serves the next request beside it. The accept deadline, set once that request is answered, ends `serve` as `acceptDeadlinePassed`; a stalled handshake before it does not.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

let ClientBudgetMs = 600
let RequestBytes = 65536
let AcceptWindowMs = 100

function holdOpen(listener TcpListener) returns String
	let connection = try listener.accept() otherwise return "accept failed"

	while true 'held'
		_ = try connection.recv(RequestBytes) otherwise (cause) 'left'
			return cause.name
		end 'left'
	end 'held'
end 'holdOpen'

function stalledServer() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let peer = async holdOpen(listener)
	let request = try HttpRequest.create(HttpMethod.get, url: "https://127.0.0.1:{listener.port()}/") otherwise return 2
	var limits = HttpClientLimits.create()

	limits.timeoutMs = ClientBudgetMs

	let response = try HttpClient.send(request, limits: limits, tls: clientOptions()) otherwise (error) 'failed'
		print("a server that never answers the hello: error {describeHttp(error)}\n")

		let left = await peer

		print("the peer saw the client leave: {left}\n")

		return 0
	end 'failed'

	print("a stalled server was answered {response.statusCode().number()}\n")

	return 3
end 'stalledServer'

function patientServerOptions() returns TlsServerOptions
	var options = serverOptions()

	options.setHandshakeDeadline(0)

	return options
end 'patientServerOptions'

function stalledClient() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: patientServerOptions()) otherwise return 4
	let peer = async serving(server)
	let port = server.port()
	let silent = try TcpClient.connect("127.0.0.1", port: port) otherwise return 5

	print("after a client that sends nothing: {fetch(HttpMethod.get, url: "https://localhost:{port}/after", body: "", options: clientOptions())}\n")
	silent.close()
	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'stalledClient'

function acceptDeadline() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: patientServerOptions()) otherwise return 6
	let peer = async serving(server)
	let port = server.port()
	let silent = try TcpClient.connect("127.0.0.1", port: port) otherwise return 8

	print("a stalled handshake, then a request: {fetch(HttpMethod.get, url: "https://localhost:{port}/after", body: "", options: clientOptions())}\n")

	try server.setAcceptDeadline(AcceptWindowMs) otherwise return 7
	silent.close()

	let ended = await peer

	print("with no more connections serve ended: {ended.name}\n")

	return 0
end 'acceptDeadline'

function main() returns ExitCode
	let first = stalledServer()

	if first != 0 'failed'
		return first
	end 'failed'

	let second = stalledClient()

	if second != 0 'secondFailed'
		return second
	end 'secondFailed'

	return acceptDeadline()
end 'main'
```
```stdout
a server that never answers the hello: error timedOut
the peer saw the client leave: connectionClosed
after a client that sends nothing: 200 connection=close [get https://localhost/after?- body=0 tls13 alpn=http/1.1 peers=0]
ended: closed
a stalled handshake, then a request: 200 connection=close [get https://localhost/after?- body=0 tls13 alpn=http/1.1 peers=0]
with no more connections serve ended: acceptDeadlinePassed
```
```exitcode
0
```

<!-- test: http-tls.alpn-and-the-request-target-follow-the-protocol -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
A `TlsClient` talking HTTP to the server by hand: ALPN `http/1.1` is negotiated when offered, nothing is negotiated when nothing is offered, `h2` alone is refused by the handshake, and the absolute-form request target must name `https` (an `http://` target is a `400`).
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let host = try request.url().host() otherwise "-"
		let summary = "{request.method().name} {request.url().scheme()}://{host}{request.url().path()}?{query} body={request.body().byteLength()} {describeSecurity(exchange.security())}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function fetch(method HttpMethod, url String, body String, options TlsClientOptions) returns String
	var request = try HttpRequest.create(method, url: url) otherwise return "bad-url"

	request.setBody(body)

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let connection = try response.header("connection") otherwise "absent"

	return "{response.statusCode().number()} connection={connection} [{response.body()}]"
end 'fetch'

function rawExchange(port NetworkPort, protocols StringArray, request String) returns String
	var options = clientOptions()

	try options.setApplicationProtocols(protocols) otherwise return "protocols refused"

	var stream = try TlsClient.connect("localhost", port: port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	_ = try stream.send(request) otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	var heard = ""

	while true 'reading'
		let chunk = try stream.recv(4096) otherwise break

		heard.append(chunk)
	end 'reading'

	let lines = heard.split("\r\n")
	let status = try lines.first() otherwise "-"
	let parts = heard.split("\r\n\r\n")
	let body = try parts.get(parts.count() - 1) otherwise "-"

	let alpn = try stream.applicationProtocol() otherwise (error) 'unnegotiated'
		return "alpn: {describe(error)}"
	end 'unnegotiated'

	return "alpn={alpnName(alpn)} {status} | {body}"
end 'rawExchange'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serving(server)
	let port = server.port()
	let plainRequest = "GET /raw HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"

	print("offering http/1.1: {rawExchange(port, protocols: ["http/1.1"], request: plainRequest)}\n")
	print("offering nothing: {rawExchange(port, protocols: StringArray.create(), request: plainRequest)}\n")
	print("offering h2 and http/1.1: {rawExchange(port, protocols: ["h2", "http/1.1"], request: plainRequest)}\n")
	print("offering only h2: {rawExchange(port, protocols: ["h2"], request: plainRequest)}\n")
	print("absolute-form https: {rawExchange(port, protocols: ["http/1.1"], request: "GET https://origin.example/p?x=1 HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")}\n")
	print("absolute-form http: {rawExchange(port, protocols: ["http/1.1"], request: "GET http://origin.example/p HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")}\n")

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1

	return exchangeOver(server)
end 'main'
```
```stdout
offering http/1.1: alpn=http/1.1 HTTP/1.1 200 OK | get https://localhost/raw?- body=0 tls13 alpn=http/1.1 peers=0
offering nothing: alpn=none HTTP/1.1 200 OK | get https://localhost/raw?- body=0 tls13 alpn=none peers=0
offering h2 and http/1.1: alpn=http/1.1 HTTP/1.1 200 OK | get https://localhost/raw?- body=0 tls13 alpn=http/1.1 peers=0
offering only h2: connect: peerAlert(noApplicationProtocol)
absolute-form https: alpn=http/1.1 HTTP/1.1 200 OK | get https://origin.example/p?x=1 body=0 tls13 alpn=http/1.1 peers=0
absolute-form http: alpn=http/1.1 HTTP/1.1 400 Bad Request | 
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.an-ipv6-loopback-server-is-reached-over-https -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
An `https://[::1]:<port>/` URL reaches a TLS server bound on the IPv6 loopback: the client dials the bare `::1`, runs the
handshake with `::1` as the name the certificate must match (a leaf of its own, a CA-flagged P-256 certificate trusted as
its own root, whose SAN holds `localhost`, `127.0.0.1` and `::1`), and the handler sees the bracketed authority in
`request.url()`. The control trusts the case set's other root, so the handshake runs and the chain is refused.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

type EchoHandler implements HttpHandler
	static function create() returns EchoHandler
		return EchoHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view url = exchange.request().url()
		let host = try url.host() otherwise "-"
		let authorityHost = try url.authorityHost() otherwise "-"
		let summary = "{url.scheme()} host={host} authorityHost={authorityHost} path={url.path()}"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'EchoHandler'

function serving(server HttpServer) returns HttpServeEnd
	return server.serve(EchoHandler.create())
end 'serving'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(try TlsIdentity.fromDer(ByteArrays from [fromHex(ipv6LeafHex)], key: fromHex(ipv6KeyHex)) otherwise panic("the case's identity was refused"))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientTrusting(rootHex String) returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(rootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientTrusting'

function fetch(url String, options TlsClientOptions) returns String
	let request = try HttpRequest.create(HttpMethod.get, url: url) otherwise return "bad-url"

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	return "{response.statusCode().number()} [{response.body()}]"
end 'fetch'

function exchangeOver(server HttpServer) returns ExitCode
	let peer = async serving(server)
	let port = server.port()

	print("trusted: {fetch("https://[::1]:{port}/hello", options: clientTrusting(ipv6LeafHex))}\n")
	print("untrusted: {fetch("https://[::1]:{port}/hello", options: clientTrusting(otherRootHex))}\n")

	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'exchangeOver'

function main() returns ExitCode
	var server = try HttpServer.bindTls("::1", port: 0, options: serverOptions()) otherwise return 1

	return exchangeOver(server)
end 'main'

let ipv6LeafHex = "3082017e30820123a003020102020101300a06082a8648ce3d04030230143112301006035504030c096c6f63616c686f7374301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d0301070342000447d4c97c85edaf10936c536fbac9dec9e04fc5b9b89327147779dee7a4a3951b94853517a6e5800399ea7b9b3ff9055227e60709b45909d937de3ab8c49a2fd3a3663064300f0603551d130101ff040530030101ff300e0603551d0f0101ff04040302028430130603551d25040c300a06082b06010505070301302c0603551d110425302382096c6f63616c686f737487047f000001871000000000000000000000000000000001300a06082a8648ce3d0403020349003046022100fd6656f0ec0b0409247e47ff0b4da57ea839883176ae0658015eef00caede8e5022100a7da1239fffe62baf5373ad68208aa218a274fc9c34fc0d3fcf9eb7d7a1134a4"
let ipv6KeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420bf08be140ec271e232c1081101d26a091bc64c958e318d34a3f21bdade6ba412a1440342000447d4c97c85edaf10936c536fbac9dec9e04fc5b9b89327147779dee7a4a3951b94853517a6e5800399ea7b9b3ff9055227e60709b45909d937de3ab8c49a2fd3"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let verificationTime = 1767225600
```
```stdout
trusted: 200 [https host=::1 authorityHost=[::1] path=/hello]
untrusted: error tlsFailed(certificateRejected(unknownAuthority))
ended: closed
```
```exitcode
0
```

<!-- test: http-tls.binding-a-taken-port-throws-bindfailed -->
<!-- unsupported-targets: wasm32-wasi -->
A second TLS bind on a port a TLS server already holds throws `TlsError.bindFailed`.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

function main() returns ExitCode
	var first = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions()) otherwise return 1

	var second = try HttpServer.bindTls("127.0.0.1", port: first.port(), options: serverOptions()) otherwise (error) 'taken'
		print("a second bind on the same port: {describe(error)}\n")
		first.close()

		return 0
	end 'taken'

	second.close()
	first.close()

	return 2
end 'main'
```
```stdout
a second bind on the same port: bindFailed
```
```exitcode
0
```

<!-- test: http-tls.a-silent-client-does-not-delay-another-clients-request -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
`HttpServer.serve` serves each connection in its own coroutine, so a client that connects to a TLS server and never sends its ClientHello holds only its own connection: with no handshake deadline, so that nothing but the client's own `close()` can end the silent connection, a request made beside it is still answered, and `serve` goes on until `close()`.
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

function describeHttp(error HttpError) returns String
	return match error 'error'
		tlsFailed(cause) gives "tlsFailed({describe(cause)})"
		invalidUrl or
		connectFailed or
		sendFailed or
		recvFailed or
		invalidResponse or
		unsupportedScheme or
		timedOut or
		bodyTooLarge gives error.name
	end 'error'
end 'describeHttp'

function alpnName(protocol TlsApplicationProtocol) returns String
	return match protocol 'protocol'
		negotiated(name) gives name
		unnegotiated gives "none"
	end 'protocol'
end 'alpnName'

function describeSecurity(security HttpConnectionSecurity) returns String
	return match security 'security'
		plaintext gives "plaintext"
		tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} peers={chain.count()}"
	end 'security'
end 'describeSecurity'

function identityOf(chainHex String, keyHex String) returns TlsIdentity
	return try TlsIdentity.fromDer(ByteArrays from [fromHex(chainHex)], key: fromHex(keyHex)) otherwise panic("the case's identity was refused")
end 'identityOf'

function serverOptions() returns TlsServerOptions
	var options = TlsServerOptions.create(identityOf(httpLeafHex, keyHex: httpKeyHex))
	options.setVerificationTime(verificationTime)

	return options
end 'serverOptions'

function clientOptions() returns TlsClientOptions
	var options = TlsClientOptions.create()
	try options.setTrustedRoots(RootCertificates from [fromHex(httpRootHex)]) otherwise panic("the case's root was refused")
	options.setVerificationTime(verificationTime)

	return options
end 'clientOptions'

let httpRootHex = "308201393081e0a003020102020101300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30143112301006035504030c094874747020526f6f743059301306072a8648ce3d020106082a8648ce3d030107034200042add37c17913cb18b549a69580089eb9323de7f22de2c3181db98b7b1af13c6b78135256dd11551b86c4f86e3dae6cfcf245eaf6442184dae9ea608c2f3c004aa3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502206d7b3dc25a0ad07455032b4f016cfd69ff1cbc4e021961faeb5b09bf70c9c4a2022100ac903b13c31af0fcbbc8fd0200ef73a4e7877e9f1cd46fdf7e837272898dd63e"
let httpLeafHex = "308201673082010ea003020102020102300a06082a8648ce3d04030230143112301006035504030c094874747020526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a30143112301006035504030c096c6f63616c686f73743059301306072a8648ce3d020106082a8648ce3d03010703420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042ea351304f300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070301301a0603551d110413301187047f00000182096c6f63616c686f7374300a06082a8648ce3d04030203470030440220497a1f88f2932abb12b4accf346bd317243ca613a4fa172595c9956b4fde24790220354de8edd3761aae3fd7480214164f9f348b6310a675ae89043a325099c7c49c"
let httpKeyHex = "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104207dd23c58ab3675629f5831918dc3ab1f33f01b146b91cf0a5a86d9e701dba53ea14403420004f497701f24f5e42fd21956fde64826c0fc9f7c509714de2f66013c4035563f2626251fd4d3af54f434ef90687ba04a87bbe7ea32e2d44a0ba71f96d2be49042e"
let exampleRootHex = "3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7"
let exampleLeafHex = "3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"
let exampleKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d0301070427302502010104200f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"
let otherRootHex = "308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"
let verificationTime = 1767225600

type AnswerHandler implements HttpHandler
	static function create() returns AnswerHandler
		return AnswerHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: "answered", contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		print("faulted {fault.describe()}\n")
	end 'faulted'
end 'AnswerHandler'

function serveAnswers(server HttpServer) returns HttpServeEnd
	return server.serve(AnswerHandler.create())
end 'serveAnswers'

function fetchOnce(url String, options TlsClientOptions) returns String
	let request = try HttpRequest.create(HttpMethod.get, url: url) otherwise return "bad-url"

	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	return "{response.statusCode().number()} [{response.body()}]"
end 'fetchOnce'

function main() returns ExitCode
	var options = serverOptions()

	options.setHandshakeDeadline(0)

	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: options) otherwise return 1
	let peer = async serveAnswers(server)
	let port = server.port()
	let silent = try TcpClient.connect("127.0.0.1", port: port) otherwise return 2

	print("a request beside a silent client: {fetchOnce("https://localhost:{port}/after", options: clientOptions())}\n")

	silent.close()
	server.close()

	let ended = await peer

	print("ended: {ended.name}\n")

	return 0
end 'main'
```
```stdout
a request beside a silent client: 200 [answered]
ended: closed
```
```exitcode
0
```
