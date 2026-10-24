---
feature: tls-interop
status: experimental
keywords: [tls, interop, openssl, s_server, s_client, TlsClient, TlsListener, TlsStream, tls 1.3, tls 1.2, mutual tls, subprocess]
category: network
---

# `TlsClient` and `TlsListener` against a live OpenSSL

## Documentation

The other TLS specs replay OpenSSL's transcripts; these cases handshake with a running OpenSSL. Each spawns the
`openssl` command (`s_server` or `s_client`) on loopback with `Subprocess`, so the peer is the library the rest of
the world runs, and checks both ends: what `TlsStream` negotiated, what OpenSSL printed about the same
connection, the echo, and how the connection ended.

- `TlsClient` against `openssl s_server -rev -msg -naccept 1`, which answers each line reversed, prints its protocol and
  cipher suite and the certificate it received, and with `-msg` prints the close_notify alert it reads. The
  server is restricted to one suite per handshake (`-ciphersuites` in TLS 1.3, `-cipher` in TLS 1.2): the three
  TLS 1.3 suites, and the six TLS 1.2 suites with a P-256 (ECDSA) and an RSA certificate. Mutual TLS uses
  `-Verify 1`. A client that drops its stream instead of closing it sends no close_notify, and the server's output
  shows none, which is what makes the close_notify assertion mean something.
- `TlsListener` against `openssl s_client`, which verifies the chain and the name (`-verify_return_error`,
  `-verify_hostname`), sends `ping` and prints the answer. The listener ends its stream with `shutdownWrite` and
  reads the client's close_notify as the end of the stream. Mutual TLS has `s_client` present the client leaf.
- `HttpClient` against `openssl s_server -www` and `-WWW` fetches the status page and a staged file over `https://localhost:<port>/`,
  verifying the chain and the name from the URL. These servers frame the body by the close of the connection. OpenSSL 3
  ends it with a close_notify and `HttpClient` returns the page; OpenSSL 1.1.1 closes the connection without one,
  and `HttpClient` throws `HttpError.tlsFailed(truncated)` rather than take a possibly cut body for a whole one. The
  case reads the `-msg` output for the close_notify the server sent and requires the matching outcome.
- `HttpServer.bindTls` against `openssl s_client`, which sends a raw request and verifies the chain and the name: GET and
  POST over TLS 1.3 and 1.2, ALPN `http/1.1`, a client that offers only `h2`, and a server that requires client
  certificates, which reads the chain `s_client` presents from `exchange.security()`.
- Both verify certificates at the fixed time of the canra test PKI (`setVerificationTime` on our side, `-attime` on
  OpenSSL's), because the certificates are valid for 2025-06-01 to 2026-06-01.

`openssl s_server` is started with `-accept 127.0.0.1:0`, so the kernel picks its port, and the case reads the port off
the `ACCEPT 127.0.0.1:<port>` line the server prints once it listens; the client dials after that. Every wait is
bounded by 30 s: the wait for the `ACCEPT` line polls the server's output and kills an `openssl s_server` that has not
printed it by then, the wait for the server to exit kills it, with its whole process tree, and the client's connect,
handshake, reads and writes, a listener's accept and read, and an `s_client` run carry deadlines of their own. The certificates and keys are written to a directory of the working directory and
removed afterwards.

`MAXON_OPENSSL` names the `openssl` binary to spawn; without it the cases spawn `openssl` from `PATH`. They need
OpenSSL 1.1.1 or later (`-ciphersuites`, `-naccept`, `-attime`); LibreSSL, the macOS default, lacks them, so the
macOS CI lane points `MAXON_OPENSSL` at Homebrew's `openssl@3` (`scripts/openssl-for-interop.sh` prints the
path each CI lane uses). A host without a usable OpenSSL fails these cases; they are not skipped. `wasm32-wasi`
has no process spawning (E3074) and is excluded.

## Tests

<!-- test: tls-interop.client-against-openssl-s_server-tls13 -->
<!-- unsupported-targets: wasm32-wasi -->
A `TlsClient` handshakes with a live `openssl s_server` over loopback in TLS 1.3 under each of the three suites, the server restricted to one with `-ciphersuites`. The client sends `ping`, the server (`-rev`) answers the line reversed, and the client ends the stream with `shutdownWrite`. A line prints the answer, what the client negotiated and what the server's own output reports: its protocol and cipher suite, the client's close_notify (`-msg` prints the alert it read) and its exit code. The server listens on a port the kernel chose and the client dials once the server has printed it on its `ACCEPT` line. `MAXON_OPENSSL` names the `openssl` binary when it is not the one on `PATH`; the case fails when OpenSSL cannot be started.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function idle(child StreamingSubprocess) throws InteropError
	match child.pollExit() 'exit'
		exited then throw InteropError.noPort
		running then Scheduler.yield()
	end 'exit'
end 'idle'

function waitForLine(child StreamingSubprocess, start InstantMs) returns String throws InteropError
	while Clock.elapsedMs(start) < probeTimeoutMs 'polling'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return text
			none then try idle(child)
		end 'polled'
	end 'polling'

	_ = try child.waitWithTimeout(1) otherwise 0

	throw InteropError.timedOut
end 'waitForLine'

function acceptedPort(child StreamingSubprocess) returns NetworkPort throws InteropError
	let start = Clock.nowMs()

	while true 'reading'
		let line = try waitForLine(child, start: start)

		if line.startsWith("ACCEPT ") 'listening'
			let address = try URL.parse("https://{line.replace("ACCEPT ", with: "")}") otherwise throw InteropError.noPort

			return try address.port() otherwise throw InteropError.noPort
		end 'listening'
	end 'reading'

	panic("acceptedPort: the loop returns or throws")
end 'acceptedPort'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		var arguments = ["s_server", "-accept", "127.0.0.1:0", "-naccept", "1", "-rev", "-msg"]

		for option in options 'eachOption'
			arguments.push(option)
		end 'eachOption'

		var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed
		let port = try acceptedPort(child) otherwise (error) 'unlistening'
			child.release()

			throw error
		end 'unlistening'

		return Self{child: child, port: port}
	end 'start'
end 'OpensslServer'

function drainOutput(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainOutput'

function drainErrors(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStderrLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainErrors'

function finish(server OpensslServer) returns String
	let code = try server.child.waitWithTimeout(probeTimeoutMs) otherwise (error) 'stuck'
		return "wait: {error.displayReason()}"
	end 'stuck'

	var lines = StringArray.create()

	drainOutput(lines, child: server.child)
	drainErrors(lines, child: server.child)
	server.child.release()

	var faults = 0 as Count
	var closeNotify = false

	for line in lines 'eachLine'
		if line == "ERROR" 'fault'
			faults = faults + 1
		end 'fault'

		if line.startsWith("<<<") and line.contains("close_notify") 'closed'
			closeNotify = true
		end 'closed'
	end 'eachLine'

	return "exit={code} protocol={valueAfter(lines, prefix: "Protocol version: ")} suite={valueAfter(lines, prefix: "Ciphersuite: ")} peer={valueAfter(lines, prefix: "Peer certificate: ").replace(" ", with: "")} closeNotify={closeNotify} errors={faults}"
end 'finish'

function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function peerEnd(error TlsError) returns String
	let text = describe(error)

	if text == "connectionClosed" or text == "truncated" 'ended'
		return "peer ended"
	end 'ended'

	return text
end 'peerEnd'

function bound(stream TlsStream) throws TlsError
	try stream.setReadDeadline(probeTimeoutMs)
	try stream.setWriteDeadline(probeTimeoutMs)
end 'bound'

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options, deadlineMs: probeTimeoutMs) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	try bound(stream) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	_ = try stream.send("ping\n") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let summary = "reply={reply.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())}"

	if drops 'dropped'
		return "{summary} dropped"
	end 'dropped'

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {peerEnd(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'talk'

function run(label String, serverOptions StringArray, clientOptions TlsClientOptions, drops bool) returns String
	var server = try OpensslServer.start(serverOptions) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	let conversation = talk(server, options: clientOptions, drops: drops)

	return "{label}: {conversation} | {finish(server)}"
end 'run'

function main() returns ExitCode
	let directory = workspace()

	for suite in ["TLS_AES_128_GCM_SHA256", "TLS_AES_256_GCM_SHA384", "TLS_CHACHA20_POLY1305_SHA256"] 'eachSuite'
		var client = trustedClient(rootHex)

		try client.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 1

		print("{run(suite, serverOptions: ["-cert", "{directory}/server.pem", "-key", "{directory}/server.key", "-tls1_3", "-ciphersuites", suite], clientOptions: client, drops: false)}\n")
	end 'eachSuite'

	unstage(directory)

	return 0
end 'main'
```
```stdout
TLS_AES_128_GCM_SHA256: reply=gnip protocol=tls13 suite=tls13/aes128GcmSha256 then peer ended | exit=0 protocol=TLSv1.3 suite=TLS_AES_128_GCM_SHA256 peer=- closeNotify=true errors=0
TLS_AES_256_GCM_SHA384: reply=gnip protocol=tls13 suite=tls13/aes256GcmSha384 then peer ended | exit=0 protocol=TLSv1.3 suite=TLS_AES_256_GCM_SHA384 peer=- closeNotify=true errors=0
TLS_CHACHA20_POLY1305_SHA256: reply=gnip protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.3 suite=TLS_CHACHA20_POLY1305_SHA256 peer=- closeNotify=true errors=0
```
```exitcode
0
```

<!-- test: tls-interop.client-against-openssl-s_server-tls12 -->
<!-- unsupported-targets: wasm32-wasi -->
The same against `openssl s_server` in TLS 1.2 under each of the six suites the client offers: ECDHE with an ECDSA certificate (P-256) for AES-128-GCM, AES-256-GCM and ChaCha20-Poly1305, and with an RSA certificate for the same three.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function idle(child StreamingSubprocess) throws InteropError
	match child.pollExit() 'exit'
		exited then throw InteropError.noPort
		running then Scheduler.yield()
	end 'exit'
end 'idle'

function waitForLine(child StreamingSubprocess, start InstantMs) returns String throws InteropError
	while Clock.elapsedMs(start) < probeTimeoutMs 'polling'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return text
			none then try idle(child)
		end 'polled'
	end 'polling'

	_ = try child.waitWithTimeout(1) otherwise 0

	throw InteropError.timedOut
end 'waitForLine'

function acceptedPort(child StreamingSubprocess) returns NetworkPort throws InteropError
	let start = Clock.nowMs()

	while true 'reading'
		let line = try waitForLine(child, start: start)

		if line.startsWith("ACCEPT ") 'listening'
			let address = try URL.parse("https://{line.replace("ACCEPT ", with: "")}") otherwise throw InteropError.noPort

			return try address.port() otherwise throw InteropError.noPort
		end 'listening'
	end 'reading'

	panic("acceptedPort: the loop returns or throws")
end 'acceptedPort'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		var arguments = ["s_server", "-accept", "127.0.0.1:0", "-naccept", "1", "-rev", "-msg"]

		for option in options 'eachOption'
			arguments.push(option)
		end 'eachOption'

		var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed
		let port = try acceptedPort(child) otherwise (error) 'unlistening'
			child.release()

			throw error
		end 'unlistening'

		return Self{child: child, port: port}
	end 'start'
end 'OpensslServer'

function drainOutput(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainOutput'

function drainErrors(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStderrLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainErrors'

function finish(server OpensslServer) returns String
	let code = try server.child.waitWithTimeout(probeTimeoutMs) otherwise (error) 'stuck'
		return "wait: {error.displayReason()}"
	end 'stuck'

	var lines = StringArray.create()

	drainOutput(lines, child: server.child)
	drainErrors(lines, child: server.child)
	server.child.release()

	var faults = 0 as Count
	var closeNotify = false

	for line in lines 'eachLine'
		if line == "ERROR" 'fault'
			faults = faults + 1
		end 'fault'

		if line.startsWith("<<<") and line.contains("close_notify") 'closed'
			closeNotify = true
		end 'closed'
	end 'eachLine'

	return "exit={code} protocol={valueAfter(lines, prefix: "Protocol version: ")} suite={valueAfter(lines, prefix: "Ciphersuite: ")} peer={valueAfter(lines, prefix: "Peer certificate: ").replace(" ", with: "")} closeNotify={closeNotify} errors={faults}"
end 'finish'

function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function peerEnd(error TlsError) returns String
	let text = describe(error)

	if text == "connectionClosed" or text == "truncated" 'ended'
		return "peer ended"
	end 'ended'

	return text
end 'peerEnd'

function bound(stream TlsStream) throws TlsError
	try stream.setReadDeadline(probeTimeoutMs)
	try stream.setWriteDeadline(probeTimeoutMs)
end 'bound'

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options, deadlineMs: probeTimeoutMs) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	try bound(stream) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	_ = try stream.send("ping\n") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let summary = "reply={reply.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())}"

	if drops 'dropped'
		return "{summary} dropped"
	end 'dropped'

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {peerEnd(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'talk'

function run(label String, serverOptions StringArray, clientOptions TlsClientOptions, drops bool) returns String
	var server = try OpensslServer.start(serverOptions) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	let conversation = talk(server, options: clientOptions, drops: drops)

	return "{label}: {conversation} | {finish(server)}"
end 'run'

function line(directory String, name String, rsa bool) returns String
	var client = trustedClient(rootHex)

	try client.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return "protocols refused"

	if rsa 'rsaCertificate'
		return run(name, serverOptions: ["-cert", "{directory}/server-rsa.pem", "-key", "{directory}/server-rsa.key", "-tls1_2", "-cipher", name], clientOptions: client, drops: false)
	end 'rsaCertificate'

	return run(name, serverOptions: ["-cert", "{directory}/server.pem", "-key", "{directory}/server.key", "-tls1_2", "-cipher", name], clientOptions: client, drops: false)
end 'line'

function main() returns ExitCode
	let directory = workspace()

	for name in ["ECDHE-ECDSA-AES128-GCM-SHA256", "ECDHE-ECDSA-AES256-GCM-SHA384", "ECDHE-ECDSA-CHACHA20-POLY1305"] 'eachEcdsa'
		print("{line(directory, name: name, rsa: false)}\n")
	end 'eachEcdsa'

	for name in ["ECDHE-RSA-AES128-GCM-SHA256", "ECDHE-RSA-AES256-GCM-SHA384", "ECDHE-RSA-CHACHA20-POLY1305"] 'eachRsa'
		print("{line(directory, name: name, rsa: true)}\n")
	end 'eachRsa'

	unstage(directory)

	return 0
end 'main'
```
```stdout
ECDHE-ECDSA-AES128-GCM-SHA256: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaAes128GcmSha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-AES128-GCM-SHA256 peer=- closeNotify=true errors=0
ECDHE-ECDSA-AES256-GCM-SHA384: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaAes256GcmSha384 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-AES256-GCM-SHA384 peer=- closeNotify=true errors=0
ECDHE-ECDSA-CHACHA20-POLY1305: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-CHACHA20-POLY1305 peer=- closeNotify=true errors=0
ECDHE-RSA-AES128-GCM-SHA256: reply=gnip protocol=tls12 suite=tls12/ecdheRsaAes128GcmSha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-RSA-AES128-GCM-SHA256 peer=- closeNotify=true errors=0
ECDHE-RSA-AES256-GCM-SHA384: reply=gnip protocol=tls12 suite=tls12/ecdheRsaAes256GcmSha384 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-RSA-AES256-GCM-SHA384 peer=- closeNotify=true errors=0
ECDHE-RSA-CHACHA20-POLY1305: reply=gnip protocol=tls12 suite=tls12/ecdheRsaChaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-RSA-CHACHA20-POLY1305 peer=- closeNotify=true errors=0
```
```exitcode
0
```

<!-- test: tls-interop.client-against-openssl-s_server-mutual-tls -->
<!-- unsupported-targets: wasm32-wasi -->
Mutual TLS against `openssl s_server -Verify 1`: the server requires a client certificate under the client root, verified at the case's fixed time (`-attime`), and the client presents its P-256 leaf in TLS 1.3 and in TLS 1.2. The server reports the certificate it received.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function idle(child StreamingSubprocess) throws InteropError
	match child.pollExit() 'exit'
		exited then throw InteropError.noPort
		running then Scheduler.yield()
	end 'exit'
end 'idle'

function waitForLine(child StreamingSubprocess, start InstantMs) returns String throws InteropError
	while Clock.elapsedMs(start) < probeTimeoutMs 'polling'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return text
			none then try idle(child)
		end 'polled'
	end 'polling'

	_ = try child.waitWithTimeout(1) otherwise 0

	throw InteropError.timedOut
end 'waitForLine'

function acceptedPort(child StreamingSubprocess) returns NetworkPort throws InteropError
	let start = Clock.nowMs()

	while true 'reading'
		let line = try waitForLine(child, start: start)

		if line.startsWith("ACCEPT ") 'listening'
			let address = try URL.parse("https://{line.replace("ACCEPT ", with: "")}") otherwise throw InteropError.noPort

			return try address.port() otherwise throw InteropError.noPort
		end 'listening'
	end 'reading'

	panic("acceptedPort: the loop returns or throws")
end 'acceptedPort'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		var arguments = ["s_server", "-accept", "127.0.0.1:0", "-naccept", "1", "-rev", "-msg"]

		for option in options 'eachOption'
			arguments.push(option)
		end 'eachOption'

		var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed
		let port = try acceptedPort(child) otherwise (error) 'unlistening'
			child.release()

			throw error
		end 'unlistening'

		return Self{child: child, port: port}
	end 'start'
end 'OpensslServer'

function drainOutput(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainOutput'

function drainErrors(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStderrLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainErrors'

function finish(server OpensslServer) returns String
	let code = try server.child.waitWithTimeout(probeTimeoutMs) otherwise (error) 'stuck'
		return "wait: {error.displayReason()}"
	end 'stuck'

	var lines = StringArray.create()

	drainOutput(lines, child: server.child)
	drainErrors(lines, child: server.child)
	server.child.release()

	var faults = 0 as Count
	var closeNotify = false

	for line in lines 'eachLine'
		if line == "ERROR" 'fault'
			faults = faults + 1
		end 'fault'

		if line.startsWith("<<<") and line.contains("close_notify") 'closed'
			closeNotify = true
		end 'closed'
	end 'eachLine'

	return "exit={code} protocol={valueAfter(lines, prefix: "Protocol version: ")} suite={valueAfter(lines, prefix: "Ciphersuite: ")} peer={valueAfter(lines, prefix: "Peer certificate: ").replace(" ", with: "")} closeNotify={closeNotify} errors={faults}"
end 'finish'

function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function peerEnd(error TlsError) returns String
	let text = describe(error)

	if text == "connectionClosed" or text == "truncated" 'ended'
		return "peer ended"
	end 'ended'

	return text
end 'peerEnd'

function bound(stream TlsStream) throws TlsError
	try stream.setReadDeadline(probeTimeoutMs)
	try stream.setWriteDeadline(probeTimeoutMs)
end 'bound'

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options, deadlineMs: probeTimeoutMs) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	try bound(stream) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	_ = try stream.send("ping\n") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let summary = "reply={reply.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())}"

	if drops 'dropped'
		return "{summary} dropped"
	end 'dropped'

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {peerEnd(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'talk'

function run(label String, serverOptions StringArray, clientOptions TlsClientOptions, drops bool) returns String
	var server = try OpensslServer.start(serverOptions) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	let conversation = talk(server, options: clientOptions, drops: drops)

	return "{label}: {conversation} | {finish(server)}"
end 'run'

function main() returns ExitCode
	let directory = workspace()

	for protocol in TlsProtocol.allCases 'eachProtocol'
		var client = trustedClient(rootHex)

		try client.setProtocols(TlsProtocolArray from [protocol]) otherwise return 1
		client.setIdentity(identityOf(clientLeafHex, keyHex: clientKeyHex))

		print("{run(protocol.name, serverOptions: ["-cert", "{directory}/server.pem", "-key", "{directory}/server.key", "-Verify", "1", "-CAfile", "{directory}/client-root.pem", "-verify_return_error", "-attime", "{verificationTime}"], clientOptions: client, drops: false)}\n")
	end 'eachProtocol'

	unstage(directory)

	return 0
end 'main'
```
```stdout
tls13: reply=gnip protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.3 suite=TLS_CHACHA20_POLY1305_SHA256 peer=CN=alice closeNotify=true errors=0
tls12: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-CHACHA20-POLY1305 peer=CN=alice closeNotify=true errors=0
```
```exitcode
0
```

<!-- test: tls-interop.openssl-s_server-sees-close-notify-only-from-close -->
<!-- unsupported-targets: wasm32-wasi -->
`shutdownWrite` sends close_notify, which `openssl s_server -msg` reports as the alert it read; a client that drops its stream without closing sends none and the server reports none. The assertion that the first reaches the server is meaningful because the second does not.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function idle(child StreamingSubprocess) throws InteropError
	match child.pollExit() 'exit'
		exited then throw InteropError.noPort
		running then Scheduler.yield()
	end 'exit'
end 'idle'

function waitForLine(child StreamingSubprocess, start InstantMs) returns String throws InteropError
	while Clock.elapsedMs(start) < probeTimeoutMs 'polling'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return text
			none then try idle(child)
		end 'polled'
	end 'polling'

	_ = try child.waitWithTimeout(1) otherwise 0

	throw InteropError.timedOut
end 'waitForLine'

function acceptedPort(child StreamingSubprocess) returns NetworkPort throws InteropError
	let start = Clock.nowMs()

	while true 'reading'
		let line = try waitForLine(child, start: start)

		if line.startsWith("ACCEPT ") 'listening'
			let address = try URL.parse("https://{line.replace("ACCEPT ", with: "")}") otherwise throw InteropError.noPort

			return try address.port() otherwise throw InteropError.noPort
		end 'listening'
	end 'reading'

	panic("acceptedPort: the loop returns or throws")
end 'acceptedPort'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		var arguments = ["s_server", "-accept", "127.0.0.1:0", "-naccept", "1", "-rev", "-msg"]

		for option in options 'eachOption'
			arguments.push(option)
		end 'eachOption'

		var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed
		let port = try acceptedPort(child) otherwise (error) 'unlistening'
			child.release()

			throw error
		end 'unlistening'

		return Self{child: child, port: port}
	end 'start'
end 'OpensslServer'

function drainOutput(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainOutput'

function drainErrors(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStderrLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainErrors'

function finish(server OpensslServer) returns String
	let code = try server.child.waitWithTimeout(probeTimeoutMs) otherwise (error) 'stuck'
		return "wait: {error.displayReason()}"
	end 'stuck'

	var lines = StringArray.create()

	drainOutput(lines, child: server.child)
	drainErrors(lines, child: server.child)
	server.child.release()

	var faults = 0 as Count
	var closeNotify = false

	for line in lines 'eachLine'
		if line == "ERROR" 'fault'
			faults = faults + 1
		end 'fault'

		if line.startsWith("<<<") and line.contains("close_notify") 'closed'
			closeNotify = true
		end 'closed'
	end 'eachLine'

	return "exit={code} protocol={valueAfter(lines, prefix: "Protocol version: ")} suite={valueAfter(lines, prefix: "Ciphersuite: ")} peer={valueAfter(lines, prefix: "Peer certificate: ").replace(" ", with: "")} closeNotify={closeNotify} errors={faults}"
end 'finish'

function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function peerEnd(error TlsError) returns String
	let text = describe(error)

	if text == "connectionClosed" or text == "truncated" 'ended'
		return "peer ended"
	end 'ended'

	return text
end 'peerEnd'

function bound(stream TlsStream) throws TlsError
	try stream.setReadDeadline(probeTimeoutMs)
	try stream.setWriteDeadline(probeTimeoutMs)
end 'bound'

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options, deadlineMs: probeTimeoutMs) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

	try bound(stream) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	_ = try stream.send("ping\n") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let reply = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	let summary = "reply={reply.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())}"

	if drops 'dropped'
		return "{summary} dropped"
	end 'dropped'

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {peerEnd(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'talk'

function run(label String, serverOptions StringArray, clientOptions TlsClientOptions, drops bool) returns String
	var server = try OpensslServer.start(serverOptions) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	let conversation = talk(server, options: clientOptions, drops: drops)

	return "{label}: {conversation} | {finish(server)}"
end 'run'

function main() returns ExitCode
	let directory = workspace()

	for protocol in TlsProtocol.allCases 'eachProtocol'
		var closing = trustedClient(rootHex)

		try closing.setProtocols(TlsProtocolArray from [protocol]) otherwise return 1

		let closingLabel = "{protocol.name} shutdownWrite"

		print("{run(closingLabel, serverOptions: ["-cert", "{directory}/server.pem", "-key", "{directory}/server.key"], clientOptions: closing, drops: false)}\n")

		var dropping = trustedClient(rootHex)

		try dropping.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		let droppingLabel = "{protocol.name} dropped"

		print("{run(droppingLabel, serverOptions: ["-cert", "{directory}/server.pem", "-key", "{directory}/server.key"], clientOptions: dropping, drops: true)}\n")
	end 'eachProtocol'

	unstage(directory)

	return 0
end 'main'
```
```stdout
tls13 shutdownWrite: reply=gnip protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.3 suite=TLS_CHACHA20_POLY1305_SHA256 peer=- closeNotify=true errors=0
tls13 dropped: reply=gnip protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 dropped | exit=0 protocol=TLSv1.3 suite=TLS_CHACHA20_POLY1305_SHA256 peer=- closeNotify=false errors=0
tls12 shutdownWrite: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 then peer ended | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-CHACHA20-POLY1305 peer=- closeNotify=true errors=0
tls12 dropped: reply=gnip protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 dropped | exit=0 protocol=TLSv1.2 suite=ECDHE-ECDSA-CHACHA20-POLY1305 peer=- closeNotify=false errors=0
```
```exitcode
0
```

<!-- test: tls-interop.listener-against-openssl-s_client-tls13 -->
<!-- unsupported-targets: wasm32-wasi -->
A `TlsListener` accepts a live `openssl s_client` over loopback in TLS 1.3 under each of the three suites, the client restricted to one with `-ciphersuites`. `s_client` verifies the server's chain and name (`-verify_return_error`, `-verify_hostname`, at the case's fixed time), sends `ping` and prints the answer; the listener ends its stream with `shutdownWrite` and reads the client's close_notify as the end of the stream. A line prints what the listener negotiated, how its stream ended, and the client's exit code, verification, negotiated suite and answer.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000
let maxClientAttempts = 5
let abandonedInputMarker = "SSL_write:bad length"

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function serveOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	try stream.setReadDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	let line = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{line}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let summary = "heard={line.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())} peers={stream.peerCertificates().count()}"

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {describe(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'serveOnce'

union ClientOutcome
	observed(report String)
	abandoned
end 'ClientOutcome'

function attemptClient(directory String, port NetworkPort, options StringArray) returns ClientOutcome
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return ClientOutcome.observed("s_client: {error.displayReason()}")
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	let replied = lines.contains("echo:ping")

	if not replied and result.stderr.contains(abandonedInputMarker) 'abandoned'
		return ClientOutcome.abandoned
	end 'abandoned'

	return ClientOutcome.observed("exit={result.exitCode()} reply={replied} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}")
end 'attemptClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	for _ in 0 upto maxClientAttempts 'attempts'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
			return "{label}: bind: {describe(error)}"
		end 'refused'

		try listener.setAcceptDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
			return "{label}: deadline: {describe(error)}"
		end 'unbounded'

		let peer = async serveOnce(listener)
		let outcome = attemptClient(directory, port: listener.port(), options: clientOptions)
		let server = await peer

		listener.close()

		match outcome 'outcome'
			observed(client) then return "{label}: server[{server}] s_client[{client}]"
			abandoned then continue
		end 'outcome'
	end 'attempts'

	return "{label}: s_client abandoned its own input in all {maxClientAttempts} attempts"
end 'exchange'


function main() returns ExitCode
	let directory = workspace()

	for suite in ["TLS_AES_128_GCM_SHA256", "TLS_AES_256_GCM_SHA384", "TLS_CHACHA20_POLY1305_SHA256"] 'eachSuite'
		var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

		try server.setProtocols(TlsProtocolArray from [TlsProtocol.tls13]) otherwise return 1

		print("{exchange(suite, directory: directory, serverOptions: server, clientOptions: ["-tls1_3", "-ciphersuites", suite])}\n")
	end 'eachSuite'

	unstage(directory)

	return 0
end 'main'
```
```stdout
TLS_AES_128_GCM_SHA256: server[heard=ping protocol=tls13 suite=tls13/aes128GcmSha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.3, Cipher is TLS_AES_128_GCM_SHA256]
TLS_AES_256_GCM_SHA384: server[heard=ping protocol=tls13 suite=tls13/aes256GcmSha384 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.3, Cipher is TLS_AES_256_GCM_SHA384]
TLS_CHACHA20_POLY1305_SHA256: server[heard=ping protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.3, Cipher is TLS_CHACHA20_POLY1305_SHA256]
```
```exitcode
0
```

<!-- test: tls-interop.listener-against-openssl-s_client-tls12 -->
<!-- unsupported-targets: wasm32-wasi -->
The same in TLS 1.2 under each of the six suites: the listener holds the P-256 identity for the ECDSA suites and the RSA identity for the RSA ones.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000
let maxClientAttempts = 5
let abandonedInputMarker = "SSL_write:bad length"

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function serveOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	try stream.setReadDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	let line = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{line}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let summary = "heard={line.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())} peers={stream.peerCertificates().count()}"

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {describe(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'serveOnce'

union ClientOutcome
	observed(report String)
	abandoned
end 'ClientOutcome'

function attemptClient(directory String, port NetworkPort, options StringArray) returns ClientOutcome
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return ClientOutcome.observed("s_client: {error.displayReason()}")
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	let replied = lines.contains("echo:ping")

	if not replied and result.stderr.contains(abandonedInputMarker) 'abandoned'
		return ClientOutcome.abandoned
	end 'abandoned'

	return ClientOutcome.observed("exit={result.exitCode()} reply={replied} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}")
end 'attemptClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	for _ in 0 upto maxClientAttempts 'attempts'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
			return "{label}: bind: {describe(error)}"
		end 'refused'

		try listener.setAcceptDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
			return "{label}: deadline: {describe(error)}"
		end 'unbounded'

		let peer = async serveOnce(listener)
		let outcome = attemptClient(directory, port: listener.port(), options: clientOptions)
		let server = await peer

		listener.close()

		match outcome 'outcome'
			observed(client) then return "{label}: server[{server}] s_client[{client}]"
			abandoned then continue
		end 'outcome'
	end 'attempts'

	return "{label}: s_client abandoned its own input in all {maxClientAttempts} attempts"
end 'exchange'


function line(directory String, name String, rsa bool) returns String
	var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

	if rsa 'rsaCertificate'
		server = trustedServer(identityOf(leafRsaHex, keyHex: keyRsaHex))
	end 'rsaCertificate'

	try server.setProtocols(TlsProtocolArray from [TlsProtocol.tls12]) otherwise return "protocols refused"

	return exchange(name, directory: directory, serverOptions: server, clientOptions: ["-tls1_2", "-cipher", name])
end 'line'

function main() returns ExitCode
	let directory = workspace()

	for name in ["ECDHE-ECDSA-AES128-GCM-SHA256", "ECDHE-ECDSA-AES256-GCM-SHA384", "ECDHE-ECDSA-CHACHA20-POLY1305"] 'eachEcdsa'
		print("{line(directory, name: name, rsa: false)}\n")
	end 'eachEcdsa'

	for name in ["ECDHE-RSA-AES128-GCM-SHA256", "ECDHE-RSA-AES256-GCM-SHA384", "ECDHE-RSA-CHACHA20-POLY1305"] 'eachRsa'
		print("{line(directory, name: name, rsa: true)}\n")
	end 'eachRsa'

	unstage(directory)

	return 0
end 'main'
```
```stdout
ECDHE-ECDSA-AES128-GCM-SHA256: server[heard=ping protocol=tls12 suite=tls12/ecdheEcdsaAes128GcmSha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-ECDSA-AES128-GCM-SHA256]
ECDHE-ECDSA-AES256-GCM-SHA384: server[heard=ping protocol=tls12 suite=tls12/ecdheEcdsaAes256GcmSha384 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-ECDSA-AES256-GCM-SHA384]
ECDHE-ECDSA-CHACHA20-POLY1305: server[heard=ping protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-ECDSA-CHACHA20-POLY1305]
ECDHE-RSA-AES128-GCM-SHA256: server[heard=ping protocol=tls12 suite=tls12/ecdheRsaAes128GcmSha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-RSA-AES128-GCM-SHA256]
ECDHE-RSA-AES256-GCM-SHA384: server[heard=ping protocol=tls12 suite=tls12/ecdheRsaAes256GcmSha384 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-RSA-AES256-GCM-SHA384]
ECDHE-RSA-CHACHA20-POLY1305: server[heard=ping protocol=tls12 suite=tls12/ecdheRsaChaCha20Poly1305Sha256 peers=0 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-RSA-CHACHA20-POLY1305]
```
```exitcode
0
```

<!-- test: tls-interop.listener-against-openssl-s_client-mutual-tls -->
<!-- unsupported-targets: wasm32-wasi -->
Mutual TLS with the listener requiring a client certificate under the client root and `openssl s_client` presenting its P-256 leaf (`-cert`, `-key`), in TLS 1.3 and in TLS 1.2: the listener sees one peer certificate.
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
let leafRsaHex = "30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"
let keyRsaHex = "308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e"
let clientRootHex = "3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed"
let clientLeafHex = "3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"
let clientKeyHex = "3041020100301306072a8648ce3d020106082a8648ce3d030107042730250201010420fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"

let verificationTime = 1767225600
let probeTimeoutMs = 30000
let maxClientAttempts = 5
let abandonedInputMarker = "SSL_write:bad length"

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: rootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: leafP256Hex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: keyP256Hex))
	stage(name, name: "server-rsa.pem", text: pemOf("CERTIFICATE", hex: leafRsaHex))
	stage(name, name: "server-rsa.key", text: pemOf("PRIVATE KEY", hex: keyRsaHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))

	return name
end 'workspace'

function unstage(directory String)
	for name in ["root.pem", "server.pem", "server.key", "server-rsa.pem", "server-rsa.key", "client-root.pem", "client.pem", "client.key"] 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'


function readLine(stream TlsStream) returns String throws TlsError
	var text = ""

	while not text.endsWith("\n") 'reading'
		text.append(try stream.recv(1024))
	end 'reading'

	return text
end 'readLine'


function serveOnce(listener TlsListener) returns String
	var stream = try listener.accept() otherwise (error) 'refused'
		return "accept: {describe(error)}"
	end 'refused'

	try stream.setReadDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
		return "deadline: {describe(error)}"
	end 'unbounded'

	let line = try readLine(stream) otherwise (error) 'unheard'
		return "recv: {describe(error)}"
	end 'unheard'

	_ = try stream.send("echo:{line}") otherwise (error) 'unsent'
		return "send: {describe(error)}"
	end 'unsent'

	let summary = "heard={line.trim()} protocol={stream.protocol().name} suite={suiteName(stream.cipherSuite())} peers={stream.peerCertificates().count()}"

	try stream.shutdownWrite() otherwise (error) 'unshut'
		return "{summary} shutdownWrite: {describe(error)}"
	end 'unshut'

	let after = try stream.recv(16) otherwise (error) 'ended'
		return "{summary} then {describe(error)}"
	end 'ended'

	return "{summary} then data [{after}]"
end 'serveOnce'

union ClientOutcome
	observed(report String)
	abandoned
end 'ClientOutcome'

function attemptClient(directory String, port NetworkPort, options StringArray) returns ClientOutcome
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return ClientOutcome.observed("s_client: {error.displayReason()}")
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	let replied = lines.contains("echo:ping")

	if not replied and result.stderr.contains(abandonedInputMarker) 'abandoned'
		return ClientOutcome.abandoned
	end 'abandoned'

	return ClientOutcome.observed("exit={result.exitCode()} reply={replied} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}")
end 'attemptClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	for _ in 0 upto maxClientAttempts 'attempts'
		var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
			return "{label}: bind: {describe(error)}"
		end 'refused'

		try listener.setAcceptDeadline(probeTimeoutMs) otherwise (error) 'unbounded'
			return "{label}: deadline: {describe(error)}"
		end 'unbounded'

		let peer = async serveOnce(listener)
		let outcome = attemptClient(directory, port: listener.port(), options: clientOptions)
		let server = await peer

		listener.close()

		match outcome 'outcome'
			observed(client) then return "{label}: server[{server}] s_client[{client}]"
			abandoned then continue
		end 'outcome'
	end 'attempts'

	return "{label}: s_client abandoned its own input in all {maxClientAttempts} attempts"
end 'exchange'


function main() returns ExitCode
	let directory = workspace()

	for protocol in TlsProtocol.allCases 'eachProtocol'
		var server = trustedServer(identityOf(leafP256Hex, keyHex: keyP256Hex))

		try server.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1
		try server.setProtocols(TlsProtocolArray from [protocol]) otherwise return 2

		let version = ["-tls1_3"] if protocol == TlsProtocol.tls13 else ["-tls1_2"]
		var options = version.clone()

		options.push("-cert")
		options.push("{directory}/client.pem")
		options.push("-key")
		options.push("{directory}/client.key")

		print("{exchange(protocol.name, directory: directory, serverOptions: server, clientOptions: options)}\n")
	end 'eachProtocol'

	unstage(directory)

	return 0
end 'main'
```
```stdout
tls13: server[heard=ping protocol=tls13 suite=tls13/chaCha20Poly1305Sha256 peers=1 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.3, Cipher is TLS_CHACHA20_POLY1305_SHA256]
tls12: server[heard=ping protocol=tls12 suite=tls12/ecdheEcdsaChaCha20Poly1305Sha256 peers=1 then connectionClosed] s_client[exit=0 reply=true verification=OK negotiated=TLSv1.2, Cipher is ECDHE-ECDSA-CHACHA20-POLY1305]
```
```exitcode
0
```

<!-- test: tls-interop.http-client-against-openssl-s_server -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
`HttpClient` fetches `https://localhost:<port>/` from a live `openssl s_server`, verifying its chain (the case's root, the name `localhost` taken from the URL) in TLS 1.3 and in TLS 1.2. `-www` answers with its status page, which has no `Content-Length` and ends with the connection, so the body is read to the server's close_notify; `-WWW` serves a staged file the same way and the body must arrive byte for byte. A client that trusts another root refuses the chain with the typed `HttpError.tlsFailed`.
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

let probeTimeoutMs = 30000
let FileRows = 1500

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function servedFile() returns String
	var text = StringBuilder.create()

	for row in 0 upto FileRows 'eachRow'
		text.append("{row:06x} the quick brown fox jumps over the lazy dog 0123456789\n")
	end 'eachRow'

	return text.build()
end 'servedFile'

let stagedNames = ["root.pem", "server.pem", "server.key", "client-root.pem", "client.pem", "client.key", "served.txt"]

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-http-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: httpRootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: httpLeafHex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: httpKeyHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))
	stage(name, name: "served.txt", text: servedFile())

	return name
end 'workspace'

function unstage(directory String)
	for name in stagedNames 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'

function idle(child StreamingSubprocess) throws InteropError
	match child.pollExit() 'exit'
		exited then throw InteropError.noPort
		running then Scheduler.yield()
	end 'exit'
end 'idle'

function waitForLine(child StreamingSubprocess, start InstantMs) returns String throws InteropError
	while Clock.elapsedMs(start) < probeTimeoutMs 'polling'
		match child.tryReadStdoutLine() 'polled'
			line(text) then return text
			none then try idle(child)
		end 'polled'
	end 'polling'

	_ = try child.waitWithTimeout(1) otherwise 0

	throw InteropError.timedOut
end 'waitForLine'

function acceptedPort(child StreamingSubprocess) returns NetworkPort throws InteropError
	let start = Clock.nowMs()

	while true 'reading'
		let line = try waitForLine(child, start: start)

		if line.startsWith("ACCEPT ") 'listening'
			let address = try URL.parse("https://{line.replace("ACCEPT ", with: "")}") otherwise throw InteropError.noPort

			return try address.port() otherwise throw InteropError.noPort
		end 'listening'
	end 'reading'

	panic("acceptedPort: the loop returns or throws")
end 'acceptedPort'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(directory String, options StringArray) returns OpensslServer throws InteropError
		var arguments = ["s_server", "-accept", "127.0.0.1:0", "-naccept", "1", "-msg", "-cert", "{directory}/server.pem", "-key", "{directory}/server.key"]

		for option in options 'eachOption'
			arguments.push(option)
		end 'eachOption'

		var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed
		let port = try acceptedPort(child) otherwise (error) 'unlistening'
			child.release()

			throw error
		end 'unlistening'

		return Self{child: child, port: port}
	end 'start'
end 'OpensslServer'

function drainOutput(lines StringArray, child StreamingSubprocess)
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return

		lines.push(line)
	end 'reading'
end 'drainOutput'

function sentCloseNotify(server OpensslServer) returns bool throws InteropError
	_ = try server.child.waitWithTimeout(probeTimeoutMs) otherwise throw InteropError.timedOut

	var lines = StringArray.create()

	drainOutput(lines, child: server.child)
	server.child.release()

	for line in lines 'eachLine'
		if line.startsWith(">>>") and line.contains("close_notify") 'announced'
			return true
		end 'announced'
	end 'eachLine'

	return false
end 'sentCloseNotify'

function negotiatedProtocol(body String) returns String
	var lines = StringArray.create()

	for line in body.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	let parts = valueAfter(lines, prefix: "New, ").split(",")

	return try parts.get(0) otherwise "-"
end 'negotiatedProtocol'

function pageFetched(server OpensslServer) returns String
	let request = try HttpRequest.create(HttpMethod.get, url: "https://localhost:{server.port}/") otherwise return "bad url"
	let response = try HttpClient.send(request, tls: clientOptions()) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let contentType = try response.header("content-type") otherwise "-"

	return "{response.statusCode().number()} {response.reason()} content-type={contentType} protocol={negotiatedProtocol(response.body())}"
end 'pageFetched'

function fileFetched(server OpensslServer, directory String) returns String
	let request = try HttpRequest.create(HttpMethod.get, url: "https://localhost:{server.port}/{directory}/served.txt") otherwise return "bad url"
	let response = try HttpClient.send(request, tls: clientOptions()) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	let contentType = try response.header("content-type") otherwise "-"

	return "{response.statusCode().number()} content-type={contentType} identical={response.body() == servedFile()}"
end 'fileFetched'

function untrustedFetched(server OpensslServer) returns String
	var options = TlsClientOptions.create()

	try options.setTrustedRoots(RootCertificates from [fromHex(otherRootHex)]) otherwise return "the root was refused"
	options.setVerificationTime(verificationTime)

	let request = try HttpRequest.create(HttpMethod.get, url: "https://localhost:{server.port}/") otherwise return "bad url"
	let response = try HttpClient.send(request, tls: options) otherwise (error) 'failed'
		return "error {describeHttp(error)}"
	end 'failed'

	return "answered {response.statusCode().number()}"
end 'untrustedFetched'

function verdict(label String, fetched String, success String, server OpensslServer) returns String
	let announced = try sentCloseNotify(server) otherwise return "{label}: the server did not finish"
	let expected = success if announced else "error tlsFailed(truncated)"

	return "{label}: matches={fetched == expected}"
end 'verdict'

function statusPage(label String, directory String, flags StringArray, success String) returns String
	var server = try OpensslServer.start(directory, options: flags) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	return verdict(label, fetched: pageFetched(server), success: success, server: server)
end 'statusPage'

function stagedFile(label String, directory String, flags StringArray) returns String
	var server = try OpensslServer.start(directory, options: flags) otherwise (error) 'unstarted'
		return "{label}: openssl s_server did not start: {error.name}"
	end 'unstarted'

	return verdict(label, fetched: fileFetched(server, directory: directory), success: "200 content-type=text/plain identical=true", server: server)
end 'stagedFile'

function untrustedChain(directory String) returns String
	var server = try OpensslServer.start(directory, options: ["-www"]) otherwise (error) 'unstarted'
		return "openssl s_server did not start: {error.name}"
	end 'unstarted'

	let fetched = untrustedFetched(server)

	_ = try sentCloseNotify(server) otherwise return "the server did not finish"

	return fetched
end 'untrustedChain'

function main() returns ExitCode
	let directory = workspace()

	print("{statusPage("www tls1.3", directory: directory, flags: ["-www", "-tls1_3"], success: "200 ok content-type=text/html protocol=TLSv1.3")}\n")
	print("{statusPage("www tls1.2", directory: directory, flags: ["-www", "-tls1_2"], success: "200 ok content-type=text/html protocol=TLSv1.2")}\n")
	print("{stagedFile("WWW tls1.3", directory: directory, flags: ["-WWW", "-tls1_3"])}\n")
	print("{stagedFile("WWW tls1.2", directory: directory, flags: ["-WWW", "-tls1_2"])}\n")
	print("untrusted root: {untrustedChain(directory)}\n")

	unstage(directory)

	return 0
end 'main'
```
```stdout
www tls1.3: matches=true
www tls1.2: matches=true
WWW tls1.3: matches=true
WWW tls1.2: matches=true
untrusted root: error tlsFailed(certificateRejected(unknownAuthority))
```
```exitcode
0
```

<!-- test: tls-interop.http-server-against-openssl-s_client -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- procs: 1 -->
`HttpServer.bindTls` answers a live `openssl s_client` that sends a raw request, in TLS 1.3 and TLS 1.2, with a POST, with and without ALPN `http/1.1`, and with a client that offers only `h2` (the handshake is refused and the server goes on). A server that requires client certificates reads the chain `s_client` presents from `exchange.security()`, refuses the client that has none, and serves the next one. `s_client` verifies the server's chain and the name `localhost`.
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

let probeTimeoutMs = 30000
let maxClientAttempts = 5
let abandonedInputMarker = "SSL_write:bad length"
let FileRows = 1500

enum InteropError implements Error
	noPort
	spawnFailed
	timedOut
end 'InteropError'

function opensslExecutable() returns Executable
	let configured = try Process.environmentVariable("MAXON_OPENSSL") otherwise return Executable.name("openssl")
	let path = try FilePath.from(configured) otherwise panic("MAXON_OPENSSL is not a path")

	return Executable.path(path)
end 'opensslExecutable'

function pemOf(label String, hex String) returns String
	return try Pem.encode(label, data: fromHex(hex)) otherwise panic("the case's PEM was refused")
end 'pemOf'

function stage(directory String, name String, text String)
	let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

	try File.writeText(path, content: text) otherwise panic("could not write {name}")
end 'stage'

function servedFile() returns String
	var text = StringBuilder.create()

	for row in 0 upto FileRows 'eachRow'
		text.append("{row:06x} the quick brown fox jumps over the lazy dog 0123456789\n")
	end 'eachRow'

	return text.build()
end 'servedFile'

let stagedNames = ["root.pem", "server.pem", "server.key", "client-root.pem", "client.pem", "client.key", "served.txt"]

function workspace() returns String
	let suffix = toHex(try Random.bytes(6) otherwise panic("no randomness"))
	let name = "tls-interop-http-{suffix}"
	let path = try FilePath.from(name) otherwise panic("not a path")

	if not Directory.create(path) 'created'
		panic("could not create {name}")
	end 'created'

	stage(name, name: "root.pem", text: pemOf("CERTIFICATE", hex: httpRootHex))
	stage(name, name: "server.pem", text: pemOf("CERTIFICATE", hex: httpLeafHex))
	stage(name, name: "server.key", text: pemOf("PRIVATE KEY", hex: httpKeyHex))
	stage(name, name: "client-root.pem", text: pemOf("CERTIFICATE", hex: clientRootHex))
	stage(name, name: "client.pem", text: pemOf("CERTIFICATE", hex: clientLeafHex))
	stage(name, name: "client.key", text: pemOf("PRIVATE KEY", hex: clientKeyHex))
	stage(name, name: "served.txt", text: servedFile())

	return name
end 'workspace'

function unstage(directory String)
	for name in stagedNames 'eachFile'
		let path = try FilePath.from("{directory}/{name}") otherwise panic("not a path")

		try File.delete(path) otherwise ignore
	end 'eachFile'

	let path = try FilePath.from(directory) otherwise panic("not a path")

	try Directory.delete(path) otherwise ignore
end 'unstage'

function valueAfter(lines StringArray, prefix String) returns String
	for line in lines 'eachLine'
		if line.startsWith(prefix) 'found'
			return line.replace(prefix, with: "")
		end 'found'
	end 'eachLine'

	return "-"
end 'valueAfter'

function chainSummary(chain ByteArrays) returns String
	let leaf = try chain.first() otherwise 'anonymous'
		return "no client certificate"
	end 'anonymous'

	return "{chain.count()} certificate, leaf is alice={toHex(leaf) == clientLeafHex}"
end 'chainSummary'

type InteropHandler implements HttpHandler
	static function create() returns InteropHandler
		return InteropHandler{}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		view request = exchange.request()
		let query = try request.url().query() otherwise "-"
		let connection = match exchange.security() 'security'
			plaintext gives "plaintext"
			tls(protocol, _, alpn, chain) gives "{protocol.name} alpn={alpnName(alpn)} {chainSummary(chain)}"
		end 'security'
		let summary = "{request.method().name} {request.url().path()}?{query} body={request.body()} {connection}\n"

		try exchange.respondWith(StatusCode.ok, body: summary, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		if not peerLeft(fault) 'unexpected'
			print("faulted {fault.describe()}\n")
		end 'unexpected'
	end 'faulted'
end 'InteropHandler'

function peerLeft(fault HttpHandlerFault) returns bool
	return match fault 'which'
		threwAfterAFailedResponse gives true
		threw or
		threwAfterResponding or
		threwWithoutAnyAnswer or
		returnedWithoutResponding or
		returnedWithoutAnyAnswer or
		acceptInterrupted gives false
	end 'which'
end 'peerLeft'

function serveInterop(server HttpServer) returns HttpServeEnd
	return server.serve(InteropHandler.create())
end 'serveInterop'

function lineContaining(lines StringArray, text String) returns String
	for line in lines 'eachLine'
		if line.contains(text) 'found'
			return line
		end 'found'
	end 'eachLine'

	return "-"
end 'lineContaining'

union ClientOutcome
	observed(report String)
	abandoned
end 'ClientOutcome'

function attemptClient(directory String, port NetworkPort, request String, options StringArray) returns ClientOutcome
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "localhost", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "localhost", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes(request)
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return ClientOutcome.observed("s_client: {error.displayReason()}")
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	let status = valueAfter(lines, prefix: "HTTP/1.1 ")
	let answer = lineContaining(lines, text: " body=")

	if status == "-" 'unanswered'
		if result.stderr.contains(abandonedInputMarker) 'abandoned'
			return ClientOutcome.abandoned
		end 'abandoned'

		return ClientOutcome.observed("unanswered")
	end 'unanswered'

	return ClientOutcome.observed("exit={result.exitCode()} verification={valueAfter(lines, prefix: "Verification: ")} {status} [{answer}]")
end 'attemptClient'

function runClient(directory String, port NetworkPort, request String, options StringArray) returns String
	for _ in 0 upto maxClientAttempts 'attempts'
		match attemptClient(directory, port: port, request: request, options: options) 'attempt'
			observed(report) then return report
			abandoned then continue
		end 'attempt'
	end 'attempts'

	return "s_client abandoned its own input in all {maxClientAttempts} attempts"
end 'runClient'

function getRequest(target String) returns String
	return "GET {target} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"
end 'getRequest'

type ClientRun
	export let name as String
	export let request as String
	export let flags as StringArray

	static function create(name String, request String, flags StringArray) returns ClientRun
		return ClientRun{name: name, request: request, flags: flags}
	end 'create'
end 'ClientRun'

typealias ClientRunArray = Array with ClientRun

function scenario(label String, directory String, serverOptions TlsServerOptions, runs ClientRunArray) returns String
	var server = try HttpServer.bindTls("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "{label}: bind: {describe(error)}"
	end 'refused'

	let peer = async serveInterop(server)
	var report = StringBuilder.create()

	report.append("{label}\n")

	for run in runs 'eachRun'
		report.append("  {run.name}: {runClient(directory, port: server.port(), request: run.request, options: run.flags)}\n")
	end 'eachRun'

	server.close()

	let ended = await peer

	report.append("  ended={ended.name}")

	return report.build()
end 'scenario'

function main() returns ExitCode
	let directory = workspace()
	let post = "POST /submit HTTP/1.1\r\nHost: localhost\r\nContent-Length: 10\r\nConnection: close\r\n\r\nname=maxon"
	let clientCertificate = ["-tls1_3", "-cert", "{directory}/client.pem", "-key", "{directory}/client.key"]
	var mutual = serverOptions()

	try mutual.requireClientCertificates(RootCertificates from [fromHex(clientRootHex)]) otherwise return 1

	var plainRuns = ClientRunArray.create()

	plainRuns.push(ClientRun.create("tls1.3 GET", request: getRequest("/hello?x=1"), flags: ["-tls1_3"]))
	plainRuns.push(ClientRun.create("tls1.2 GET with ALPN", request: getRequest("/old"), flags: ["-tls1_2", "-alpn", "http/1.1"]))
	plainRuns.push(ClientRun.create("tls1.3 POST", request: post, flags: ["-tls1_3"]))
	plainRuns.push(ClientRun.create("ALPN h2 only", request: getRequest("/never"), flags: ["-alpn", "h2"]))
	plainRuns.push(ClientRun.create("a good one after", request: getRequest("/last"), flags: ["-tls1_3"]))

	var mutualRuns = ClientRunArray.create()

	mutualRuns.push(ClientRun.create("with a certificate", request: getRequest("/who"), flags: clientCertificate))
	mutualRuns.push(ClientRun.create("without one", request: getRequest("/nobody"), flags: ["-tls1_3"]))
	mutualRuns.push(ClientRun.create("with a certificate again", request: getRequest("/who"), flags: clientCertificate))

	print("{scenario("plain server", directory: directory, serverOptions: serverOptions(), runs: plainRuns)}\n")
	print("{scenario("client certificates required", directory: directory, serverOptions: mutual, runs: mutualRuns)}\n")

	unstage(directory)

	return 0
end 'main'
```
```stdout
plain server
  tls1.3 GET: exit=0 verification=OK 200 OK [get /hello?x=1 body= tls13 alpn=none no client certificate]
  tls1.2 GET with ALPN: exit=0 verification=OK 200 OK [get /old?- body= tls12 alpn=http/1.1 no client certificate]
  tls1.3 POST: exit=0 verification=OK 200 OK [post /submit?- body=name=maxon tls13 alpn=none no client certificate]
  ALPN h2 only: unanswered
  a good one after: exit=0 verification=OK 200 OK [get /last?- body= tls13 alpn=none no client certificate]
  ended=closed
client certificates required
  with a certificate: exit=0 verification=OK 200 OK [get /who?- body= tls13 alpn=none 1 certificate, leaf is alice=true]
  without one: unanswered
  with a certificate again: exit=0 verification=OK 200 OK [get /who?- body= tls13 alpn=none 1 certificate, leaf is alice=true]
  ended=closed
```
```exitcode
0
```
