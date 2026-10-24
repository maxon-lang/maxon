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
- Both verify certificates at the fixed time of the canra test PKI (`setVerificationTime` on our side, `-attime` on
  OpenSSL's), because the certificates are valid for 2025-06-01 to 2026-06-01.

The server is started on a port the case found free and the client dials after the server printed `ACCEPT`; when
the server cannot bind the port the case picks another, five times at most. A hung peer is killed after 30 s
with its whole process tree. The certificates and keys are written to a directory of the working directory and
removed afterwards.

`MAXON_OPENSSL` names the `openssl` binary to spawn; without it the cases spawn `openssl` from `PATH`. They need
OpenSSL 1.1.1 or later (`-ciphersuites`, `-naccept`, `-attime`); LibreSSL, the macOS default, lacks them, so the
macOS CI lane points `MAXON_OPENSSL` at Homebrew's `openssl@3` (`scripts/openssl-for-interop.sh` prints the
path each CI lane uses). A host without a usable OpenSSL fails these cases; they are not skipped. `wasm32-wasi`
has no process spawning (E3074) and is excluded.

## Tests

<!-- test: tls-interop.client-against-openssl-s_server-tls13 -->
<!-- unsupported-targets: wasm32-wasi -->
A `TlsClient` handshakes with a live `openssl s_server` over loopback in TLS 1.3 under each of the three suites, the server restricted to one with `-ciphersuites`. The client sends `ping`, the server (`-rev`) answers the line reversed, and the client ends the stream with `shutdownWrite`. A line prints the answer, what the client negotiated and what the server's own output reports: its protocol and cipher suite, the client's close_notify (`-msg` prints the alert it read) and its exit code. The server is started on a port the case found free and the client dials once the server has printed `ACCEPT`. `MAXON_OPENSSL` names the `openssl` binary when it is not the one on `PATH`; the case fails when OpenSSL cannot be started.
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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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


function freePort() returns NetworkPort throws InteropError
	let probe = try TcpListener.bind("127.0.0.1", port: 0) otherwise throw InteropError.noPort
	let port = probe.port()

	probe.close()

	return port
end 'freePort'

function isReady(child StreamingSubprocess) returns bool
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return false

		if line.startsWith("ACCEPT") 'listening'
			return true
		end 'listening'
	end 'reading'

	panic("isReady: the loop returns when the stream ends")
end 'isReady'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		for _ in 1 to 5 'attempts'
			let port = try freePort()
			var arguments = ["s_server", "-accept", "127.0.0.1:{port}", "-naccept", "1", "-rev", "-msg"]

			for option in options 'eachOption'
				arguments.push(option)
			end 'eachOption'

			var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed

			if isReady(child) 'listening'
				return Self{child: child, port: port}
			end 'listening'

			child.release()
		end 'attempts'

		throw InteropError.noPort
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

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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


function freePort() returns NetworkPort throws InteropError
	let probe = try TcpListener.bind("127.0.0.1", port: 0) otherwise throw InteropError.noPort
	let port = probe.port()

	probe.close()

	return port
end 'freePort'

function isReady(child StreamingSubprocess) returns bool
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return false

		if line.startsWith("ACCEPT") 'listening'
			return true
		end 'listening'
	end 'reading'

	panic("isReady: the loop returns when the stream ends")
end 'isReady'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		for _ in 1 to 5 'attempts'
			let port = try freePort()
			var arguments = ["s_server", "-accept", "127.0.0.1:{port}", "-naccept", "1", "-rev", "-msg"]

			for option in options 'eachOption'
				arguments.push(option)
			end 'eachOption'

			var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed

			if isReady(child) 'listening'
				return Self{child: child, port: port}
			end 'listening'

			child.release()
		end 'attempts'

		throw InteropError.noPort
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

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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


function freePort() returns NetworkPort throws InteropError
	let probe = try TcpListener.bind("127.0.0.1", port: 0) otherwise throw InteropError.noPort
	let port = probe.port()

	probe.close()

	return port
end 'freePort'

function isReady(child StreamingSubprocess) returns bool
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return false

		if line.startsWith("ACCEPT") 'listening'
			return true
		end 'listening'
	end 'reading'

	panic("isReady: the loop returns when the stream ends")
end 'isReady'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		for _ in 1 to 5 'attempts'
			let port = try freePort()
			var arguments = ["s_server", "-accept", "127.0.0.1:{port}", "-naccept", "1", "-rev", "-msg"]

			for option in options 'eachOption'
				arguments.push(option)
			end 'eachOption'

			var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed

			if isReady(child) 'listening'
				return Self{child: child, port: port}
			end 'listening'

			child.release()
		end 'attempts'

		throw InteropError.noPort
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

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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


function freePort() returns NetworkPort throws InteropError
	let probe = try TcpListener.bind("127.0.0.1", port: 0) otherwise throw InteropError.noPort
	let port = probe.port()

	probe.close()

	return port
end 'freePort'

function isReady(child StreamingSubprocess) returns bool
	while true 'reading'
		let line = try child.readStdoutLine() otherwise return false

		if line.startsWith("ACCEPT") 'listening'
			return true
		end 'listening'
	end 'reading'

	panic("isReady: the loop returns when the stream ends")
end 'isReady'

type OpensslServer
	export var child as StreamingSubprocess
	export var port as NetworkPort

	static function start(options StringArray) returns OpensslServer throws InteropError
		for _ in 1 to 5 'attempts'
			let port = try freePort()
			var arguments = ["s_server", "-accept", "127.0.0.1:{port}", "-naccept", "1", "-rev", "-msg"]

			for option in options 'eachOption'
				arguments.push(option)
			end 'eachOption'

			var child = try StreamingSubprocess.spawn(opensslExecutable(), arguments: arguments) otherwise throw InteropError.spawnFailed

			if isReady(child) 'listening'
				return Self{child: child, port: port}
			end 'listening'

			child.release()
		end 'attempts'

		throw InteropError.noPort
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

function talk(server OpensslServer, options TlsClientOptions, drops bool) returns String
	var stream = try TlsClient.connect("127.0.0.1", port: server.port, options: options) otherwise (error) 'refused'
		return "connect: {describe(error)}"
	end 'refused'

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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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

function runClient(directory String, port NetworkPort, options StringArray) returns String
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return "s_client: {error.displayReason()}"
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	return "exit={result.exitCode()} reply={lines.contains("echo:ping")} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}"
end 'runClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "{label}: bind: {describe(error)}"
	end 'refused'

	let peer = async serveOnce(listener)
	let client = runClient(directory, port: listener.port(), options: clientOptions)
	let server = await peer

	listener.close()

	return "{label}: server[{server}] s_client[{client}]"
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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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

function runClient(directory String, port NetworkPort, options StringArray) returns String
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return "s_client: {error.displayReason()}"
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	return "exit={result.exitCode()} reply={lines.contains("echo:ping")} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}"
end 'runClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "{label}: bind: {describe(error)}"
	end 'refused'

	let peer = async serveOnce(listener)
	let client = runClient(directory, port: listener.port(), options: clientOptions)
	let server = await peer

	listener.close()

	return "{label}: server[{server}] s_client[{client}]"
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
		sendFailed or
		recvFailed or
		connectionClosed or
		truncated or
		closed or
		trustStoreUnavailable or
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

function runClient(directory String, port NetworkPort, options StringArray) returns String
	var arguments = ["s_client", "-connect", "127.0.0.1:{port}", "-servername", "example.com", "-CAfile", "{directory}/root.pem", "-verify_return_error", "-verify_hostname", "example.com", "-attime", "{verificationTime}", "-ign_eof"]

	for option in options 'eachOption'
		arguments.push(option)
	end 'eachOption'

	var configuration = Configuration.create(opensslExecutable())

	configuration.arguments = arguments
	configuration.standardInput = InputSource.bytes("ping\n")
	configuration.timeoutMs = probeTimeoutMs

	let result = try configuration.run() otherwise (error) 'failed'
		return "s_client: {error.displayReason()}"
	end 'failed'

	var lines = StringArray.create()

	for line in result.stdout.split("\n") 'eachLine'
		lines.push(line.trim())
	end 'eachLine'

	return "exit={result.exitCode()} reply={lines.contains("echo:ping")} verification={valueAfter(lines, prefix: "Verification: ")} negotiated={valueAfter(lines, prefix: "New, ")}"
end 'runClient'

function exchange(label String, directory String, serverOptions TlsServerOptions, clientOptions StringArray) returns String
	var listener = try TlsListener.bind("127.0.0.1", port: 0, options: serverOptions) otherwise (error) 'refused'
		return "{label}: bind: {describe(error)}"
	end 'refused'

	let peer = async serveOnce(listener)
	let client = runClient(directory, port: listener.port(), options: clientOptions)
	let server = await peer

	listener.close()

	return "{label}: server[{server}] s_client[{client}]"
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
