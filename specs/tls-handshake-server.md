---
feature: tls-handshake-server
status: stable
keywords: [tls-handshake-server, TlsServerHandshake, TlsServerReplay, TlsLoopback, tls 1.3, server, handshake, state machine, client authentication, mutual tls, rfc8446, tls, crypto]
category: crypto
---

# `TlsServerHandshake` — the TLS 1.3 server handshake state machine

## Documentation

`TlsServerHandshake` is the TLS 1.3 server (RFC 8446) as a state machine over records, with no socket, and the
counterpart of `TlsClientHandshake` (`specs/tls-handshake.md`): `receive` takes one inbound record, and the
`TlsSession` it exposes through `session()` gives back the records to send (`takeRecords`), the application data
received (`takeReceived`), and `write` and `close` for the connected state. It is `module` code in `stdlib/`, so a
program outside `stdlib/` cannot name it (E3088); the cases here are library-tier (`<!-- tier: stdlib -->`) and
reach it, and the test seams, as a stdlib source file does.

It follows the canra `tls-handshake-server` contract. It negotiates in the contract's order, in the server's own
order of preference: the first enabled TLS 1.3 suite the client offered, the first signature scheme of its own list
that the client offered and its key fits (never RSA PKCS #1 in TLS 1.3), the first enabled group the client sent a
share for, a HelloRetryRequest only when there is none, and the first of its ALPN protocols the client listed. It
answers a missing signature_algorithms, supported_groups or key_share with missing_extension, checks the second
ClientHello against its HelloRetryRequest, sends the server flight as one protected record, sends no session
tickets, and writes no application data before the client's Finished has verified, which is compared in constant
time. With client authentication on it sends a CertificateRequest, verifies the client's chain with `X509.verify`
for the clientAuth purpose and no host name, and the client's CertificateVerify under the client context string;
`require` refuses a client without a certificate with certificate_required, `request` continues without one. A
defect sends the contract's fatal alert, protected when write keys exist, and stops the machine: every later
`receive` throws the same `TlsHandshakeError`. A client that selects TLS 1.2 continues in the TLS 1.2 server of
`specs/tls12-handshake-server.md`.

Randomness and ephemeral keys come through a `TlsServerEntropy`: `fresh` draws them from `Random.bytes`, `create`
takes them as given, which is how a case replays a transcript. Whether a client certificate is verified is the
`TlsPeerAuthenticator` the machine is built with (`TlsX509Peer` with the roots clients are trusted under and the
clientAuth purpose). The machine and the client share their record handling, key-update and secret bookkeeping in
`TlsSession`, and both are driven by `TlsReplay` (a transcript) or `TlsLoopback` (one against the other) through
the `TlsEndpoint` interface; `TlsServerReplay`, `TlsClientReplay` and the drivers live in files no production
source names.

The vectors are the canra `tls-handshake-server` contract: transcripts of the client of OpenSSL 3.0.13, the
reference client against the reference server, and the errors. A line prints the vector id and the canra expected
object, the Trace as compact JSON or the error name. Every one of the 40 canra vectors of this spec has a case: the
types leave none unspellable. Behaviour the vectors do not reach is pinned below them: 57 more transcripts made with
the canra reference client and checked by the canra reference server (more server signatures and preferences, hello
and record shapes, the second ClientHello, the records between the flight and the client's Finished, post-handshake
records, more client authentication), the production entropy, the alert a failure sends and the record layer's
refusals, the alert's encryption under the server's application keys, the configurations the machine refuses, and
the client of this stdlib handshaking with its server in memory for each TLS 1.3 suite, with and without client
authentication.

## Tests

<!-- test: tls-handshake-server.replay-openssl-transcripts -->
<!-- tier: stdlib -->
Four transcripts the client of OpenSSL 3.0.13 sent to the canra reference server replay byte for byte: every record the server sends is the record the vector lists, ServerHello, change_cipher_spec, the protected flight, the application data and close_notify included. They are a default handshake (OpenSSL offers X25519 and compatibility mode), a HelloRetryRequest to secp256r1, an Ed25519 server key, and a handshake where the server required, and verified, OpenSSL's P-256 client certificate.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("66edb5d5f1582be89e360f743ff88a00482584cd91367d080973eb2d063d7ec9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5da2b595467d91e8b13f235462e2e932fb19c0fdb8b885782c615a935aa93d3d")), secret(23, privateKey: fromHex("9949a015e9d765e12263cbbf7df83609bcefe8c68e604c80fefebd5a5492e4d8"))], pssSalt: fromHex("3a0a6063b7f09169498b343bc9c1e78a069e2b4d90fcec74f87dd196d9968d4cf069292e4fddc9d816c9c369622e7d7eb16ce559fa0f81dec06b440fc85acb6e"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-openssl-default", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010200010001fc0303f751ab73e2918fd0968d060b060c39b6257be5f3f55a987dc528dc4adf1c15f620317e776e1a6d7e07586842a62b2532335e958b2193c77cb63c05223bb11ebbf50024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300260024001d0020b0bf4f038fa5e263c8f7afd265f76f8c11e84396316ea5c0381ab6cbb3681d65001500d300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("140303000101"), fromHex("1703030035749b957e924f354166117c9fe45db1eeb23abb57ea0130ab5a5d86f562f867396206d3b70878a8f19ad8969a83ac0901f81411dfad"), fromHex("170303002a3ed5b8b9cc22dd548b4105c26db1145339671a48b8636eb5577ccec9325af8e88b5f753492109e0528a5")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("1bbb6ae1897589b8013f5ba40849fe862732e4dcd4cba631bdf0692813b1a421"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("3d28dfb75df134f0453777cc672191783c3563148d708537ada86e2d29c3b150")), secret(23, privateKey: fromHex("2320d9ea2541b33b7ffd34a00990ad5dd03b5299f61f5cc7f3caa2a9229540b9"))], pssSalt: fromHex("3cf3797be3bc4271623e3d7705c7883d0f8ca48fd5e613c058975dc85a6cd5e32f64c12c4bd45eb4de72f2bac06baf183ae799dc489e8d5949dce0d205c5eb05"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-openssl-hrr", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010200010001fc03033f9bd7ba38b475351667fd13c1bf9b01933774c670dff20690cf295756fa0ad820b8437ae8daddab634b91d57e343a99a14e344cc527ff9226d7b08896127ca5e00024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300260024001d0020285148e63058b63461c29f106cecb1fcd02adf914bb7430da0274a4d7770fc1f001500d300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("140303000101"), fromHex("1603030200010001fc03033f9bd7ba38b475351667fd13c1bf9b01933774c670dff20690cf295756fa0ad820b8437ae8daddab634b91d57e343a99a14e344cc527ff9226d7b08896127ca5e00024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300470045001700410443989b90df7b7e4c1ec1d7ce21ec405b686ce267a6452e2bf27491087734af5c3b8f1dfbba5eaadaf9b16abd4c3bb89c46057c9591a31d02ae2d9602fad45319001500b200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("17030300350bb24cfcee1bf000e46899737d95665de69bb9c9f5de7acbab2fcacfcd90367d9acefeae391969741f579d1f52fe520a7e8db8f2c1"), fromHex("170303002ae5799a746e7a27d32eb321f2718159ebf103647c92b6ce17299cea4e3d67e359b254fb573941f29a6112")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008")], key: TlsPrivateKey.ed25519(fromHex("c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b6cfda7e41438e410be66e3127deeacff2a9fb7d31e8c527f78c83da65000759"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("f23a017264c17b4e343bc44ab02a82254a1235d541d93ee5a872fe640af1554d")), secret(23, privateKey: fromHex("34f2096fcd7a6392cc850088cd83e4c4efaea3c664adb97ddc46185cd498bac9"))], pssSalt: fromHex("d8ce6fab6f0f6b6e059f41e57a9461438657306488313eed1691ad4d8dcb517d6874e9c1699de66d23f57a59c1a07514ae0b803e56df7e98f2ae61fab86c7287"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-openssl-ed25519", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010200010001fc03039d17f94aa8d1cf93307784b757fc0c776c5b937191f05cea0201230645f1f1d4203f90d2dfda982490402dc4e5d910f3f67f6f04df9316aa09014c53cfa18e66160024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300260024001d0020c4fa769c152df36f2fe825bf46c124ffaeecf7479892b825b25f7639ba622a5a001500d300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("140303000101"), fromHex("17030300351290bb61bd916d45c55647e6075335d6ef758f9a76d24659d470630208ad6741236228b04ae6ae12b57ba73cf014f1dc089774c4e1"), fromHex("170303002a3f1edb9db32223d0f90d350736e565ea5f97dfbba857c23077186623905dc1ddcb2dd38b3d41440c42f6")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b4da4f3590c16efd2b0068de174fc11a5c77a3e3376eda2a1230a03560148eff"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4fec289987fe41d0f63796284d0fc07635e45956d3627238aed23580cc2d27ab")), secret(23, privateKey: fromHex("2b2162b2918ae920106d8fcbc4fb290c27c98aa69a5c3075b38d35682a65950a"))], pssSalt: fromHex("f7b734b438f336057174ff23276ad71be0302dbdf528ffbd5c23a764e8018640ba0c7cbb1299e4a8d65dd7270a8bcf247f45815446bc7d3e0bb0ebcafe388628"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-openssl-mtls-ecdsa-p256", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010200010001fc03034800582c0810de7ec61a108e59521ab9064c09732dd49e2960e578e753f5ab0020866fbf207f68ac89624e856ca19e92520ce072ea22779da1b4d11057867f98d30024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300260024001d0020de3b90918a15dbccf0854bc96abdbd720b8f7b1f66fae585c6236f5ee0f0b933001500d300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("140303000101"), fromHex("170303018d2d397d69708ed3870b3ec686f66e412ec56cc1083b479d0f28be9f77c11f219f386cf53bf1419095ca8239c17a6cd0302fb84dd569b44650d1de1130526c8d364c69e29e97e72965d01954db9be2e85ad64802d421b48748cbc0eec9d0734fdd6ce3bdf4a3b21de5d5179b2cb7fc10dd0a2d7267459bfa167112a630fe22c7ba12a56eaa3692470d2d6c252943cfc1ad81f9c4d422e65c2aded04ba1a06be699ebea43b03c1b55d5bdced26dabb46e1d60bae921d2f1165d0dd41453dc46ac987f8d33aaf65782f51bfadec105300a6a16d732c88f8b33bb4f605cf599ff085d7125b695616a34e9bf8d2f8e12f8bbe4475879b0e022db6d56371e72605b736e46623823d58b4f00e022872fae8aaa2e0b9c0d3a394c3a45b9be6e82a072ab00c0b32488c512d7493bde501e87425d791c0fb8567451e7b8c3b0f35914c8d6b05472e4ae6195fee3ce2a4825a16af086c2a5f80457067f9092cc169467c52ecf1674b61e1f2cc08e814a3c423dd684efb3727461f064c3aa66101124bb425d747313dd88816ce25d4a1256b0d3"), fromHex("1703030061a64774bb13ef59e34c62d6c010e2454e78721f86069f31c38ffaed41a5bd17d7ab329c66f9327ff7a9d7f02a7762978305acf07a9ad243bebbe48436fe212da0a9a41011a54592c9fcc433ec84ef4c8dea0c3b04d3158824db02d0699b251f655d"), fromHex("1703030035e611b86f16524e82f5418637bf73c58ac5ff75e9b0120454e16b6a4f6c25f0a7b8daf9b66237a70851231ad9c1b1653229c6977472"), fromHex("170303002a943de6646bff3160c6b52344c0a115ac47529d36a16b416f5634d3015a8eeb1bd796a160a9b5a7cc6178")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-openssl-default {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030366edb5d5f1582be89e360f743ff88a00482584cd91367d080973eb2d063d7ec920317e776e1a6d7e07586842a62b2532335e958b2193c77cb63c05223bb11ebbf5130300002e00330024001d002058740687ba02c4cf7e5716bb37c270ca48884f6c42ba376d3dbea688603fc14f002b00020304","140303000101","17030302079f6ef4a7edf11c92e40f9e62c748a0faac065c96bc311a9ef9ca1c075e217aab0843c996627e1d5ea7b18ce549e9f4d49be6e85f186bc6f4ed89d2773dbc22a104ad0ec6a75b911de9b52c8bb802430ce26a0e7ace5677d86990a116252db0e738350094d2f8b70ca332add40b8aa505c9df571f306865f1505c9ee1319f8a23ffd0ebd33b0a2fc0ee851d0c1c6b7405288cef4f63db80a2c18e1fc65705c038966eecad6588431357c5699956a2d61e074733257b4edb355ee2feedcc15178f235c77397f82eefcc002d79966bd94a2f03a747e57e6b78f8c2e74fafcbfb2f17fbdbcda8d9200243206924cf9efe63eb48f24a1fd886b87552a768fe8767221300d4903eba691c1d8e19955bd4f8d48d4b100b75e20175c4ac58535b1bfd809cc5ca27011da5965d3104d807e913695fe20953292b92fde84988d385b4d869ae629574c757c4eec131e009db10ddf934f56d8bce6d065285e8416ae55cfac2ce9d432c816f34ad2261f75dd803052bfb00190d49c6230c9c4886870802fff5896054a9472f758faee333c3c7cf59e6c4c7909b75be30173241e33bc4d1eee1c3cd5e96ae2b32a0de655cc2b2696227368f564155c2ebb559517a9555adf1f6f3e3c3e67d1fe9c7d4f7d0c3df1c48fc1f03e4a88dd3b825ec1ca436b68b0ef6121ea8872a398fba112383c589268c980404e548cb850a7c5af45b8a4a61bb096d68cd76e498e48","170303003935730e8ef5c674b642fc96ab5deb5cf91066e6a77e0de3a6ad6f8365e41c0974e99c766e598fa90f4fcde9fcd21c70d72276b4dc310a60c2e4","17030300136bc7a2eca2e0a0e3d024714c9378cf56f4ae8b"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c20636c69656e74","peer_closed":false,"peer_authenticated":false}
server13-openssl-hrr {"state":"connected","version":772,"cipher_suite":4865,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c20b8437ae8daddab634b91d57e343a99a14e344cc527ff9226d7b08896127ca5e0130100000c003300020017002b00020304","140303000101","160303009b0200009703031bbb6ae1897589b8013f5ba40849fe862732e4dcd4cba631bdf0692813b1a42120b8437ae8daddab634b91d57e343a99a14e344cc527ff9226d7b08896127ca5e0130100004f0033004500170041044377df67f86d13a413196f2f94a2466db1f46b7b0ea09e1d9a868508e660a3fa3e83bb9e26f7c9a74644b86c327525ddb6a28e685e4d109aa309bae3d1b306df002b00020304","17030302087189926c7f067f0151dc06ef3df41b2eac66b4e4d4b8fadbbfcabc7bcfba0c2876ac181dce747a48cf0ac2af30146a8a9323e5cdca361627b2fe3868468613e9d7189242c66331b4b8406c7164c5275f598820264c34ec34604573cff1945696c5982578837d31b325cd9442f166ab6b736a90f89074045ce263eb58002f626c84e627576ab5b1589ca9ce024638360cbcbddff63fbb3782b535de41d1c7d81bec56952cff9c547b104e397504b0c335918a42b4d24c2c39bc2db1f72c17a9d625be339536a1430077dbb5504c2f4b073d66427de3645fb8dbd29838721afaf5d1959d0bbf4822c3ed3c8714a7311793a85c4de24fb45cdb2ab24274ec71ed27259c450b800036b3cc68b6eac757918efedae0b103b20e0fdaf884f3cdefe4e32b4d9bc099fc303faf3eb95f36646dbf3212a0bdce6cb132b0c375c19837e27f5a3d39896c5b40252b2b6dee949e6f90e703db7c730f1de8e872144820f6663d9223237d18b7926e2fa33092366bc2a839468d6ea3ac924150ac37b5f1c4a15eb36dbf54d81646f0c6881a7d90e3f1ccd0b95a2614cd2ea3e502a7216ad0c957c329c982ff32bbfa50118149912dfb129352c0962f2dbd8c7b29211105817dd3f8f38fb3d6fb4e16780fe1dbe737c93138d4c802f850d3991e6fc26c193c72bacf31b06356c07b630459dc175d46b69692e701b9b0f9789bd22ee6764f11edf9847c8f471cea2945","1703030039554b7dfe42f3472ca8a3a98842fac362caa13a7514f83862994ec33d2b95dadec60cfba107f9202cef66cb71d86aa291127ac3b6b37f9e48ee","1703030013ae5cc1c02f19909c95e69546b1d84e430256c8"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c20636c69656e74","peer_closed":false,"peer_authenticated":false}
server13-openssl-ed25519 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303b6cfda7e41438e410be66e3127deeacff2a9fb7d31e8c527f78c83da65000759203f90d2dfda982490402dc4e5d910f3f67f6f04df9316aa09014c53cfa18e6616130300002e00330024001d00208ebb7ff6f0c10d33fee11918d3264bef5fc4504765ce5171c44a6295e5ab022a002b00020304","140303000101","17030301d0b6d0a315a441395325f00870fa730e9e375876e6c178a308d348c1e65ac28ace0c79a494670c4b561540d31564e12ff819239a783057c5968d6dd592859140eedc94dc6e4ddc9357f50890102d1a2685f92069514a094a7cb2e02cfd3f9824191b4c840e18fc7c4121250a63739cdc62419a8f6b452e408ab7a97c2af93bb44d6c3894e0a4faeeb4f4f6b0612d27980dad57e5389d26fcc007fc9e5ce70c344dc7bea68a93222a4a3b738048991883c4a3c2b96bd3f11e9e4c18f538cf0289f3b7e2dfb39bbd83b4b49e4fd22b90f62194e1734c309d82a71f95c72af32e8154cd725b053435926999c90a37e1d597c7e48519b3c9800ab549bc802892237edfea9f822bcc0ca399e8f0bce3987f4add0d85cdedfcd2b3381d1d876e87e29a7ea30bab28823676fd9a02d96a48955b5881033b49903aabc3489572306c1cbf19344b7dd78fc6e499387c74835bc1acaca020881f7202041c0bffa93c7b03af08a27f18bb8e78e40d43a3d18831dcede3911dc4988d084725e6f40f9c1be6182b704bf7d10b67f5ded7f4bc3c1c3366abde7d96d38189ddfe1a2ed6cd40372eabed3a3dd4ae9062c1482e860185757f5f2c169a6dd6fff93e8b2f46c30adfb7fd4ac1398c83b4eb5809be2ea14ad74fec","1703030039855f859a4739e79fd4fb6739fed8d210c848761f1a4981646f124aac901c6bd74123c44e8e15e401dd96742f22be4afd7e95706c371e06c5d2","170303001356d2801d4a1df12b237b324c39b91095544298"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c20636c69656e74","peer_closed":false,"peer_authenticated":false}
server13-openssl-mtls-ecdsa-p256 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303b4da4f3590c16efd2b0068de174fc11a5c77a3e3376eda2a1230a03560148eff20866fbf207f68ac89624e856ca19e92520ce072ea22779da1b4d11057867f98d3130300002e00330024001d0020181cb5ed93fc61f2bdbe9476ad96ef2d7c8fc779155e2b13edbbb73e23dacd0e002b00020304","140303000101","1703030221bd18ffb571a4164eee4f6c29105145131f3c41c7fe759885c808013d75262c6eeaa0cef0adbdedf432c2054ae43f28ca5edfccfbca5423db186f01aec2403aca6e91be214d2b37b114a00f99d1e893939b8a57757bc98d86471b003ae990f0de37fbbd56a98669126032018f0001cc270e1439c94c58ab6e6a108d9dd6d3574b38d726c296ee528670ff66fc5d325601cfdb8f49aab8bde0550283a5e34e51ae02ba7d6a2a2efdd11cbe17cfeb8a4264d09e6132de76acb59d347d62f37cc8fa72d503dfa45dea4444bc502db05b1281a9cfa2d61140384cfa71c65226c72557c317ed679c6e1eebd4b23ecf3ac6b5f3abac6c84caaa9a9c4efb7f6b55f10a7a4e3d719f1df3ec738ff62d5d72fb0ed0d59ae42da934993d0cf87a3ff492da5ae5ab6a8a06425c54177f25dfaf6ec284b6b7e078da90839c05e288be10686ed9d4b77aded410837b352f1b533b7d2ce4309654096f3eb367b7fab68db8f44f3ecbd2d3750188718cf4b2853b257d8afe699c17586087382298917917f307960dc438b073a9409ca4b8ae7d1f49af52fed58204df29628105862816a0be16a8fc2dfe52d28b5cd37d8746d6974ec4d24097db735f33d4e6882e96bc3745b00c80502e76f339172a0cb1d95f2534bd0a0d3faa446f6525bed273bfa2436b18c9aa35d9bd7ddfa4f6f66d793e4b3718d5b6adfed74bdd486f5a34a8f4d88c4a1028e1e63cf2e3889a32dd8c25039603447a35bf936b21781d52faac0dca931c35d425","1703030039a0c4956dfd5962e69d0a2183b309e6ab2cfb5bf29e7cbd2dd32362b272a47d2436b18d3b35f7d60f05729a9e8ddba29e4ff62de2d15b216f1b","170303001368177a57b3b3bd5abdd66ed089b4db2b9a7987"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c20636c69656e74","peer_closed":false,"peer_authenticated":true}
```

<!-- test: tls-handshake-server.handshakes-by-server-key -->
<!-- tier: stdlib -->
The canra reference client's handshakes with each server key type: the default (ECDSA P-256, compatibility mode, ALPN), RSA-PSS with no compatibility mode, ECDSA P-384 and Ed25519. The server signs the CertificateVerify with the first of its own schemes that the client offered and the key fits.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b3797dace6f5dfdde198176354da5a242e6ca971ce9cb6306395f384546888eb"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78b23ce497e8080e1e5bca97a466071b99f8204e3b9d1a9e9dfe52fa86dbacc6")), secret(23, privateKey: fromHex("4e994f156d979f4ff3a5deb19eb4d7b2e1580c6660086043de9e446ab11a3381"))], pssSalt: fromHex("4fe158385e7e9c7566674942171e114087c94a2454594c9632b634c98d79e86bfca625cb32ab4a05fba9b6d0733584196d3ff270f94fdb3e6d35f0b03f437634"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-default", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030332e1707ee79bb5e98fb04609ffc0969ed8e3b8a1a3fa25e4c8d2d2111030f4b5208da713486a7f372cd80164ce077e6c3cd5ce272198aea8d9f01a71bca742f7650012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002034d72227b61e1051bfdf1f0efba593086f7ec567a401bf0f3abcb8379af29e0f002b00050403040303"), fromHex("140303000101"), fromHex("17030300354173f8c098fa387e217e7f980c6a20bf7c0574c258cfcc81e18b0fc91a9bd53270efb02a8f59f036deb440647fbe434a9e1f6ec47f"), fromHex("170303003604a1e28c1e2e830344ecca885cdc4f0a06b6df2ad793d8ce296e8fe4c4b982c5b36b7f1ba4066a6b56012e9d627a2275d780ba0a4172"), fromHex("17030300136b3e5d19fa92350f373d5e03367621fe004c8e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], key: TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("a9cfed338d770d68ec8a5e37541a68e3325d898a25e8adf0c055dd619d56e0d6"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("00e0efdfdb2b64e2784952b217897c50f1400e8765a3876aba0e89fd2b884d77")), secret(23, privateKey: fromHex("78a94896495d6633ecfaa18e0b07c62acda4483d0d591e56081eac283ab99172"))], pssSalt: fromHex("a9f014270784f57e34d1db7f56bf640854e82d851c6c4e3f4df564c8c09d9493752cd8e3089653ed8c5515309873927cd5945af7b557771f6f690132b50c9d93"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-rsa-pss", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100c6010000c203032ce38fc50bde9a972221139c3c74291d89e49cd176f844707885d0c79c1e43cb000012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ca691c6de726126f859f543a43af103d763d5fd5e8fb13309ec5b8ee6112e453002b00050403040303"), fromHex("1703030035ab8386fb3da2510105b3c0807152fc9bcc3baef5199dd02f24102371d7a3be43f7a42cf7d8a2b792ca4063f111c10421453fc74118"), fromHex("170303003661256a4c4e4599dd720e112ce9d6a59164f172b4eb8e1c7a21bf3a17070aa618735344ead46f5ef19bd15851f1c3d584656cc4b32aac"), fromHex("1703030013b8c77547de16b99b02367293eecfce7f53d9ae")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c")], key: TlsPrivateKey.ecP384(fromHex("11afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("9c458e98ba1b74875425a6473f09bfbd99d27de6e4ceca05de57532cc3808555"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("50a4f5a74f7c53f584e363cdfe08310d5062904c46ca47ca47e528fcdeb9a4c2")), secret(23, privateKey: fromHex("cf7e54e5a1524cc12ec416c592964046a3f5006645a84c8495937ff09fe77aac"))], pssSalt: fromHex("7327fb169a91140c46e171647fc09c64f3d45e9e3f4b465514d0e2cd42ee990be7623ae50b6ac0e52e03c2800f2dbb6cbf0b62075ef8d61a3927b076b4a4f6c7"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-ecdsa-p384", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303cab74f89f6af03e73fee9af89ca2f2d009654d74657eafda1c2df49ad7b9359e2031c210b033fa76209d1cffe89ee67a4a8b7a679b837caf7e3456f735d754f41d0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020c343bb2c2b41a7bf2055d2cdb35bda8156e81ea796da32137ab5854db75afc42002b00050403040303"), fromHex("140303000101"), fromHex("1703030035fe5ced3ef55483255ce4fe7db1eacefc689bbd57cf5e490681f266d11fe188830119331e9262673bb0ed5fab46b0471edf79c6b291"), fromHex("1703030036a0dce384978af511239bffab1faba000f66a579eb569d9d0ab933b1ec14aaceb570d057123de3fb7cdd2ceb9f9aa3db826ca4850a51d"), fromHex("1703030013a07a03be7a1792c28fefd58dcccc74213c91a9")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008")], key: TlsPrivateKey.ed25519(fromHex("c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("2884367f4d6cb7b929cbda87ac458d9d6a949c8c4575b3d29813522c83d7b294"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a369ed6fcd5cedc87d797bf28f67c3b43bf738ae8f484a879bea2efeab428949")), secret(23, privateKey: fromHex("bc81470ba30343da99509a20a16370e01684471d0666e73109f8d5ef1eb6d004"))], pssSalt: fromHex("b04cab16b0210e9721447ae90f79a74105365f7d06d45071f526478cc1c6650720fbec8aa5ed470fb356a95f37662fd1210afffe910eacc40c6f3d9580e88533"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-ed25519", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303f67ab97c48df0e1b7e9e2c1dfcb8921c45bd521e38676927a1d30d2633837b74202330e420ef33a9fde1525760f156ba05ffde1bee23130a81241654d004331b730012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020fa5425e9196436b4b73ef2af7ddaaaee3c60776134dfc788a382d0a9557d862c002b00050403040303"), fromHex("140303000101"), fromHex("1703030035879a5a2b89120b09f7f79f2bf08e3e29834e47b041e45c72c5050fb966794072e24b8cb4c7c2484da798124cfbb28a5ecf087128b0"), fromHex("1703030036ad57260f20aedb98380781ac39f05041ab3f99be1991e27812be32d558afb6e2db9a01e3190f7647e831e7a9dbd7ae6a1d88a96e647c"), fromHex("170303001318f185b2ed9f4039c1195b42f7281400002f7a")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-default {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303b3797dace6f5dfdde198176354da5a242e6ca971ce9cb6306395f384546888eb208da713486a7f372cd80164ce077e6c3cd5ce272198aea8d9f01a71bca742f765130300002e00330024001d0020a37ab6473689c8fda66a9918fb9bcbfbf97bd8fa26319ab2cdc3cf371505cc7a002b00020304","140303000101","1703030207d445c0629410de5a60bc3a36ca140307f516980e41e5aa78678d5281e8a984e1e219c54a6e7730cf08591f768692a886a69dc5a158e462d7c03297521659d83069f93ac2a9c5b9e9a8e77b9e014676ae87805cd274d87df8b6203fed9dd50c160d60290371a26d883d3b6aad2469830caa9febb12605ca732de68d3f299df2ab754d410e5fbd4876ec75c31d21526184b2f65be4a4c2692667b3b4249262211f371e3c77a75b34dd823a70bb2dda03d46d19694659dfcbe753a2cf30bbf0502780cd9fb2afe70d34f6ff709fbc53c2e44a113846ddffc12d20fa46498887d1e4ccf45302ece6e9bf5c6f5c32049afcaf9c968ccd5feb4c9dfcc4d4b881c703f0936cf465e050b409c498a59e61ac5e7e97e0d46df7ad6eb108d4e7324752ef324c521e9ec9f2177104e4b59f5629b560f3bf6b02ebf3ccb8fc1e8dfbe8b61ce97b55d052cf6f4942436660473bade9e2480ace88be218c04e87aa1e2816cf45161174fc465fc7eeb0f0045638218c9c45df963d229ed85ac1f7c59fd0c25560c807f4b89b9f8f326f560349f45784531b42673eb95ff94482ffa35fcd61f8aa31c15b08c8b30f4f2d6a2c69335220cddb41752a353671fdec545701173a30b5e8aba465f8a155ba4a445fc2b1dea14b8500f3f1da6eab778cc437c28cf43a3d98ff5eb8a7b74ca2ea05cf6040a42a60c83595927a6abb31ba4338221994d581ed414b1f9e7778c","17030300399592354a58a617dbf7311cdb1012b06e83263653431cff2c8da32efae817d295471f738bdfa87a6a33ecdae02dee722b790ee2375f72e272d0","17030300132fd26d573244fe6d14880f5f9edb1f01405558"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-rsa-pss {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303005a020000560303a9cfed338d770d68ec8a5e37541a68e3325d898a25e8adf0c055dd619d56e0d600130300002e00330024001d00205bdb7dd8e0663e09c09b66609384659688100406d24dda2c5c85e9e619775e1f002b00020304","170303038c4e384b2cd81a42e84c492ea6eda7d69e3347b4d98eb7bd4c426f321c6955302dff8a827c7263b8cc50ad993c7a39a8207a4cade47cc5b0bafa44ec69646ba2ca9da756c1f9d0a83daf9f8534d9a817a3f751cad0e5bd54c50512d1f365c4dd60368fc133d778fb481c1e8cdadbc2a939190004fbb7a0113d14835ae4369e29ae84390713d32350819d0d06d5a07f4a25d3cea7c171fdb7214e0e8109b7d5f6487fc52e80a77e927a41197cf2c16bc5dd37cacb01e80a64475a550ed531732ee4d88ffc05ec4328826e36f9b2dfb5f0876bba65dda5e4e2f4a5abc7b827aa21868dd4d000aa66e7be6149d68ae2ae8e10d141cd45b26214b50156d313004b6070103dd8ed7412a4d8849d69e33b1472eaf71c91dcf7106af48ab603b6face3f992e4304448ad09fa16c92a1f2e8bc3680bb5d5c283a4d9fe8d5906278588641b51c0a33bc7908b96719389457a7b5f8c4ebb015c44ddaadb65439610ab65d9f81aeccf44f5352796fd2a2710f2916ace62c6b04f2a413683abf6207f9c12330fbd7adcd536dbd66d2462df2daf5b0a3e4cc9a5d13005c86f41531c73f051015b0ed20adf35061940bf2c76f698ad870b5a40a4818ba9dfd932fff0b5340b60d68099fd3051760832960182225e5eee6715a46a08f8005f8a5abd3e63802ba6283bf1be8fceac2d726d00bfd036637d65bfab21404879c5307be51fa1c5dafa52115e5281a23fbef460e7e2fbfe255d650eb69bd903d3d8b2bca641a49a6b0f217a97bbead51cc095fc8e98e1357478997a9305b5f50b1d5ccc4d3d3e2ddc2456604958ea51f7636fff5b6033ad9807da898211bdbdb76194497d3017d5d09e2acdae57b210b77da6159bec2806774c672bf9b70986b884b294045af6c92a97175f17c0990f5ac32ca3c54ccc10abcbcbe7a94503a938d18d626a619de133f1a64371390f3a80f3aeb46d09a461bbe64a3772f2fdc7afae04b6e2ad7ce53f30a1a6cbf4405545c4b503ede3798be64567ebb67fb1f9d192ebd01fc05a18bab1f66abdefa8aee2640b40bc3ba86a80f945e83d5c0f532a5c5508b914a296b4221b604d5d94765f6552fdb4e83c20b23af31b051e99efc294b46160ce0dfd7845ae84645adeeb82e4276217bd942397b7fc3b6395ed9aab8a7b3efee1c338e966298810901d765a4c73ef83fdb27b270fdc63f98d0fc58b0bbfc3920338279670e320585bd6fcf585cf1107e9ada54b8d1124b5a8a449faaa2b7a2b87fbc7d4149d0e9108e0e8e0e9fb37f5494e9","1703030039c9032cf7abd7c10b320ca1b40d3a6e18bbb92808fbdc8626562ce0a71b8ea25aaf167f42b7b1276219604c6edd050342d45466cc87f1c6a10c","1703030013b38e8bc7dca690b0dfbbdf48261664fe5537e8"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-ecdsa-p384 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a0200007603039c458e98ba1b74875425a6473f09bfbd99d27de6e4ceca05de57532cc38085552031c210b033fa76209d1cffe89ee67a4a8b7a679b837caf7e3456f735d754f41d130300002e00330024001d00200e59809364f9988cb6efe88723e4212c9db12779ac64b6bfc323d398a9527416002b00020304","140303000101","1703030242643897026533db6af28dd409ecb5ef99acf935c326891a33aefbc6254e4d4006465293045ea51c142bb87e3102134099a7e78407a56544662ef49942428fcc025085ecfe51a556d3b410574ae4d0e74e5de0e65f762d7e765e6762d91cc60cfb5db91fe3ba2ebbe18c9b0d352a2395d9cb552696b6a9bf601f6cce4f12358bb7a1f941cb54154952137dd87ccd3abaae036fbd44326ff0036dfc2573ad3f843aa6e7aa402e57e026cd2469c9129c61332aaf78d656c49fa6832472d1c9aed3175cf49d191ae4ef76cf346c25a1f2cb5d3c59714f71c7eab2b79759972a6b78d85f14e4a47ad8a212be227695ea1290154a05a48efc56fe677aeba779932ffed28ab6704bdaf5592b6a55df7eb7fe1c0cbd9bc390a7065e9ac956562c44a1054d8d5121f407ef31b9d30ec6c073d2fc23609b30471e85201f426430ad4e1f86ab84ee272f51844ae7fbc63f6910a2487d5a3ec0c91884a5654dca4b2db9239bd6d7f607f6eb56d31d8b5c14eac17ec8b366c9026dd55e5b581d17ee69cb4a1b751f398af4b14eddb3137a6bb28fb50205d96a9527411a88e60bf8cb4adc95c6fe103aef30b046b55270638bb70d1cf54b77bb02d5d698462021ebaddfe22318fdf4e971bc2b7776921d9a7926ead44a4b5b4ef13eae30cec4a41a238b6d7a8f6f14142e736adaed6b2a19b6d39c6082b1fe82e9c159bb6cc930ee9f30f2b9bab5c2a6f55d56f0b4aad1c14a8d7fdac7ccbd7d7e5e1937d8c3f07945c3e8106b92001880d8e61c893e0667a0bff786ccedbd44f0a0c386e793d2b17406542a3320e274","1703030039c99025f4eb8b4dbbf3925d0c5456d686d812a9a282171320d11b062c27a812b648346d3887e65ff58c7f6ed9c10957376e2f46f62a8ce4a3f9","170303001398a021fe68aae533b582a3120b41c2d1b1e202"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-ed25519 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a0200007603032884367f4d6cb7b929cbda87ac458d9d6a949c8c4575b3d29813522c83d7b294202330e420ef33a9fde1525760f156ba05ffde1bee23130a81241654d004331b73130300002e00330024001d0020430a09fca028028b45172d76aae6342f141a401f3629e418de83480906cb8e42002b00020304","140303000101","17030301d0d570c556f10cda0b7740191aa7cb413f70cbd4a0db1250218e4f0079eebefac1383b2ada08e3d5a8ee7c17cb3145ebfe4a3e51ec5957eb15c7b63aa95246c66d46e16e4e72f968ecd161e00e2725dc89aa8d6ef148d5b66d178517c2153ce0fcec078e8943d11ea83286ea0ceb892131bba54fa7fbadd7c09984c045485a4c40086ff4ca01f3b3fc35b905f991a860418a6f46c92de0f3f87aca075a153152f1772248178ed89e5d36870bc8da0e26d94fc8613bf5290fde03e7911ed560ccc7fa7c844f81c71fcdc5c45977d9b71393baec78c0c054142ca64258c7a3f7eb352714f332724655557db81c26ebce47e77d2bf86a0714076e844c9c7a5d4ad308d958ec63c5ac1b71abe668e45d60298ae484185d14f8010ab0bb0c1f45a76d7c5153b93bbc363d8a92c8fc3d20835b88ff251ed588a3562df36e7eeb583d1c95ea9d988123d61ebb6776dcddfae2709fdef256a73019382e4baf726273847d6ed9e4620268677be95ae5a21d3dafc1a59511ee2d8d5099fa0035b589e11c4b85f2592f75dedb938abd79e97439ab51ffdb634d112d63ee0edf209b06ffcf663d4940a6e195063a3e1975b77c119551f202bf6181af028b34c07683225fc511125f1341c5fa29acc944397412edd67130","170303003924ab9ce5a45b62de532f05aaceb25beeafea027246f3f67d26ac12c09429ef58c3fee1958dbb4d9bd08ef9c4c215ba34e4853e3aaa82690dda","1703030013eabe937cedb4f4e307db7e52c41c23bdf7bb00"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
```

<!-- test: tls-handshake-server.negotiated-handshakes -->
<!-- tier: stdlib -->
Handshakes that negotiate: a HelloRetryRequest to secp256r1, a client that sends a P-256 share the server prefers less than X25519 and gets P-256 without a retry, a KeyUpdate that asks the server to update its own keys, a client without server_name, and a ClientHello alone, which gets the server's whole first flight and leaves the handshake open.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("e0b1d1eff06fd907685e30adeb64f1d90edd62a228d419e2fecf0950466b046d"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a4553405120833a133df735395c941bb1bfd8156a6cb56b9285cc809da122450")), secret(23, privateKey: fromHex("5e9b540dde155fe0b374645e9615c505da2971cdee443a8ec5129bf9b2c5404f"))], pssSalt: fromHex("72b1e55dd83d0756e6fc1423860f8dd54e800fb4e5fef3790769b56da363e382f2ad4b69f89bdaccc3954f433726433124c16522897cfdae2d2c9c811947c565"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-hello-retry-request", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030392f6f4581f320ca1f99867fea00999d665dfdb29947b795bd9d06f3325c620e220983ab77101a121188f2610f7c34098b299b41b7f5d4143a311c567dcc2b159be0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201fd4716a4cbe7386cfd2b224fe01d239cd57f86ae60492716f2cc9b753653a08002b00050403040303"), fromHex("140303000101"), fromHex("160303010701000103030392f6f4581f320ca1f99867fea00999d665dfdb29947b795bd9d06f3325c620e220983ab77101a121188f2610f7c34098b299b41b7f5d4143a311c567dcc2b159be0012130313011302cca9cca8c02bc02fc02cc030010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104f97ca3d8ac5fd7b0d84f9ecfdd5c28d88ef4cdbdb0867b97e330f0dea0279758c34691d668408ff7710f8b2ba69252eb12b6f5f5223f8f7a36664f0e1de978ba002b00050403040303"), fromHex("170303004571a104921a128197342a3a8e88811503997d77c024aabcc029913274958f902ace67958b8d908ca1e1f0ec51319fccadb55ba256b12a60d9e5c064b70138c6ef8205a842c4"), fromHex("17030300365100550e9ac6df7f6f2b1bd546070219d6f729add12596c649d780b2bf0286a3c8741fab8f5f92a0f96689150c1f9406e46ac841796b"), fromHex("17030300137616088205430154b06064ad98f97f6145bae3")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("acd5638432b37f87e6c55b3fd64a6c1422970141c8f9a55cd336fbb3ddff9718"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6fcc7c8f36caac5022f7a508409d04d4fff1c2a91c6a50ac757f29f2c31cce31")), secret(23, privateKey: fromHex("b534df9ef52d3f9d516a1522421391a92cea4e6c55100abd19cf46b1651ab315"))], pssSalt: fromHex("d732d4b01fae2cd48b044d51cd4c286f1bbaf4133a30c9eb66d8e59a91660cdf04a1f0b6d420e56c5fbd59bae265174e9b842fab1e2f3774bf15c5fab4e5a0d8"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-p256-share-no-retry", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010107010001030303cb2e761ef0f6f581544c60ef7910bfc2dd2d6838cc943b00cb94d9122a158b962024afdb19e97fd6358431012a74c266555d08aafe71d9c2cdda7078d79831d8e50012130313011302cca9cca8c02bc02fc02cc030010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104875e9d9fe298c130a95c0f374fa2aa291b2a2e9f70d008cc041d8d21d8dd71d7dd0bfed6e1e576352cd5d1726403713a0ce02240d24ec25f5a9a5ae47b24900d002b00050403040303"), fromHex("140303000101"), fromHex("170303003533afd48a5dfbc58024fe07afa153600f91f2bdaeb5f76d3e4e2a455e53586afa1146035f3eee8fa0ab16351dc37c201ebd81ba2141"), fromHex("170303003614c62a2c8c6c5f20b93edc4203a4e6114074a1b14fd20e9beae47c0b80c5905ec995d15ebf48f817d5e83b5eea254b425a171b014ddf"), fromHex("1703030013e24d002aa0c175a30c2b8be2b38bb81979a04e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("432f1d3c94c6d8b3d84b73c05592e5988b827187155b525600214f299ec5ac8f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("c219a894e66495c1419a62f552f0a50dc7c90eef64d9909d8b728465c458a5d0")), secret(23, privateKey: fromHex("6b105bc1a19a31d4defaf300dce65600daa60e3f6a50faf1af049cca4426d4f0"))], pssSalt: fromHex("5475ca9cf474542f78c4b56111fc50f220c481cc997b49f5b365395e1d34d51ec69a10feb5c3eb7887bb0576a7e3489fd0c879d5f17a1812cd75f046f7b7d6db"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-key-update", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303bc2c6c6b6f1ab4e90033e6c7dd5a0f71b8233cc3778a150238e7014124544bd920732406ad6316252fc7fe6d402d4ad4098efe80ccf5d81fe8bafef161a8e9059a0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201f948c3886ba269cb79bd3ca299e774d060ddeb6e99e5acc6f443f8fd2b2e006002b00050403040303"), fromHex("140303000101"), fromHex("1703030035e073237640ac146edead18e567985acc494519904bb8e2f81562a44270963b49bebd357f1dcfdc4eb0a1a1f97cd46388d5613ecdb3"), fromHex("1703030016ac060e32fdceb3b6be5c9aade5959c03adbb4e9dd7cd"), fromHex("170303003650806ab930f777b8b27288103af3677f814848d83a697b1a76768ebdbfaf0b80d9d5c6c3ee1ff981f4ab936307775782f76e72ea9486"), fromHex("1703030013f6f664bdb88c01958e65336ec67720e5e48aa5")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("1efed4145d535f830f068c6dc1ba61496d19ed21cdd6322fff79af1b7eb0217f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("209fa1e4669de7d816a34800c0406e9d20e8cecfd38ac4cc04ac574143153f8b")), secret(23, privateKey: fromHex("010c45a9d37dde21dd075c011d518bb0cb796737e567e25c70645bc6b31e713d"))], pssSalt: fromHex("e2ccb1c23cc69f1bf7e0c9925fd3164490b426b9cc557faed0c41af4d61077ef5ddf2b2ce5bb39d84bca33fdc2e5a129a5c03ac250a0e76ca7213c5263eb28b7"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-no-sni", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d2010000ce03032a8969aa907e4ddd3ef8ae517b2bb3a5bb8088e5458366045f0694589568fdf720995d8af099245beb3b170bd57ebb5fe79c3dd3438df565713340e39dc6d478930012130313011302cca9cca8c02bc02fc02cc0300100007300170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020b6e91d358cef556d3b28410748a2d4648821c40c0df45dfae1a41f7a8ef4da61002b00050403040303"), fromHex("140303000101"), fromHex("1703030035213a5e9a3fe0de713217763268a81567803b92d176a7fa9fcbac09e180935c2f2cc4af3c4629d9d354e44535c91995b3e626015643"), fromHex("170303003647b84359fbd8fcf5b934e6428b2be7ed3a4620b19782e40a06a3937e9ffce2b7e4c709621a96d3c7904352bde306da48343cc328934b"), fromHex("170303001393242d430997dfb2baf20180a29db444e8ed11")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("63bc43320fca3580db3619b182bdc7ea8ffc41da19b993493da5fef6accb7f16"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("ac96a4532109bf39dde5c2b82bfe18dfaddeb47c5591799f5c6d8d7aaf0ef310")), secret(23, privateKey: fromHex("a5268aa75ae4800eac8d26aa9256366f18cc042ced3b482041d1b81024c8977d"))], pssSalt: fromHex("acf837a64bfa1f77d46103307b48e1dea97f4e5423d5ce5eccf12441d2f2d0437909442c3385d57c4323faf718cfcc3cee31b6d2b01d211f40be1848e9d75da4"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-hello-only", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203030a4f33a445360c0a6bd954acf36ba3c0af37a5f472a11ebfef047b3b72bb5513208a1c619b434f1a7596cfb37bffa2b819a95fbd4cf30691a396bb9ccad1deb0600012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020570ee01e8453c58a5cf67c5806b7a8eefec379e149e017a921dfb64709a93120002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-hello-retry-request {"state":"connected","version":772,"cipher_suite":4866,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c20983ab77101a121188f2610f7c34098b299b41b7f5d4143a311c567dcc2b159be130200000c003300020017002b00020304","140303000101","160303009b020000970303e0b1d1eff06fd907685e30adeb64f1d90edd62a228d419e2fecf0950466b046d20983ab77101a121188f2610f7c34098b299b41b7f5d4143a311c567dcc2b159be130200004f003300450017004104f0d27441783ed56f14d6c63e726753473866a6dad55b099998616ec6a7ab829fe9365290f848dba53d45fd3b8a82d26099457943b521529e58f162d99cca9a1a002b00020304","1703030217145da185264ff3a1f4ef237fbc19ecc76ab7ab5acb8d69a44908f66b758d90c83119a81a8afc900c4c1880778f96207184a4c6566586a11b9079956982e752f614236b5acabf381af1677619a2bf0db7ec005468570c4a61b64b1443a890f2a3c3f5e0fe91aa812b36f61713eb925860e021b4fbddb9bcd679e15ba9153448d7ceee4c9b5f2c6bf16308742a20fd1d945f7daea49f60e3a7645090605e0b34e48ab9a18cb4326433f4729c148a774d1321b7f97f3a7538ea0acae98844cd32cbad6bd309c4ac879c41075bc823db1cde9642f179561116991852be95fc5021e08cc6373a0392f950ddcf0c45e4e8700ea21ad5a3e364799d82bc14c3626fd5f6dff4e5227021bcce4152b8ebcdee56ee4e06f5ec7d9955c890b1e0ea7c7c1196623087c62b5654c1d37185410de57b591391d8a184f9c07eb0d514cb049ebdf7e064eca3326aa6e89f8350fc9b56cdff1247d9b32925c76ccb6bae8cc83797f9bcabf6636b6b1cc80ec35915c0634d955b22e51d9be4a19097909eaffd8db01dff54844f37c76d6acfa8d163938a7f527a0cf65a0f0496aad2f5d7cfc3787aff121baabac40848235f0fd52939a3927659dff5cd1a444ef589bf3b917361178f70aa649b94d747471bf8fe86635b13c2d39982858761ef7ec00267872f32d7939456cb94945c533b1d9ddcabbe23edc02cdf6f3a944f17e230cf80b962657b5b097da5d2bbcf545c8cd0c60e28e9fa3638d14adb9c1079","1703030039fbfdd82b4db033c5d09a21be24f0e8e724cbb88a16b7fd0396d4a8ad920cca002a6c8eb54b7fa48f2bcd2d7293a98dab7493eee6c8849cdd5a","1703030013f43677695c8de0bde16fa1eba8530903d0c02b"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-p256-share-no-retry {"state":"connected","version":772,"cipher_suite":4867,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303009b020000970303acd5638432b37f87e6c55b3fd64a6c1422970141c8f9a55cd336fbb3ddff97182024afdb19e97fd6358431012a74c266555d08aafe71d9c2cdda7078d79831d8e5130300004f003300450017004104a3a36406f5bf04c90b1e0a40596b127bcceb4911fb590799f2d3ba68f27b4291ecc61ff0d1b4eb64337e6381a7525a0ef8d9175b77d46c0c3d942de462cfb5d5002b00020304","140303000101","1703030206a0fa463c04993ab2d892bd34759238e89921894a417c9f5eeb6e53e10097a2141c1a984d452c23b77f7dd2ad2040d0de13347e6e244b399f5f9b1ec22d46b22c332fd36cd3acd9dfcbcb6163258735fd50b1bb0257d5d45333ced6726174591753d99a8e9d068c6aeeccbf3709d88711e0bae7b1d3247265e63d287a5ba38ec7859714a027b3797b34a50776a05957d30cf74d84642dedff25e82a1d877a5723f9a476081563f12569f59899ae4f8b4b1fc3094742119c42a85ad99c5badb35c24a343ce0d72eeef8bb62e3ff1e2aed95de2b62c26acc52875363c700bbc17b5debb1966c1573a323c69fa34ffe4c48194f17f847baba5db20797cdcb7b6e3fd7175c0106b476096be90135398053b2d16ba6c8f07af9fdeb4613d7650592f684b898b741389988fca982fe429916491cca7b5930741e75da6dc9eaa76582fae4c40c15a3a41536d1172afe63b5b4f992bd2664d5758b4302a9f347e4d42d87776050aac2dbae50fdc31991b996e6c86f25237900a5849125661f0855974fd88827f99153586038459741364e099b4877a43f125570d821f1a1e3c72c279ac5e45aa1fa8df3b2bff13999affec42f71823ed16dc4fffeb41c3dc93786eff37f20b28cb30fc10a22ea31407e112f4367e106472c79bd9543adb2b7eddad06178afb218408fa9a1d59d15270faa4e16f282781a8f45549b2caf1b5a776aef350e5efd88fe37e5c","1703030039dd641ac3a7cd31ec69b3ba5fa3bb6c2f5e613aa98abb571e0052fb1635630e05568dcfe75540edb1c99785a10cb88d938f8af579b1169f7ab0","170303001360f4347c01c8f4480ca066294c8214b86e5213"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-key-update {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303432f1d3c94c6d8b3d84b73c05592e5988b827187155b525600214f299ec5ac8f20732406ad6316252fc7fe6d402d4ad4098efe80ccf5d81fe8bafef161a8e9059a130300002e00330024001d002099f4d1ef1ce237ccc868d750b66a4730645f2cc32556e6b0023b308e4c641d65002b00020304","140303000101","1703030208f23e731a0fb0cdbd4988c6c561eb25df5838d88742e2aa1a44a5cd0616031308eeab844e43edcfaf61c93e03671c1d6de41befdf86fca49fbb9057ecf6844135ce7ea9f72a218da8b12e843d57a635d4f35c65acbf36e870e68d5d977fad890613455d14075c02194ea8cb15fe3750e212c5be6726147bfc97dd520cc90f8575719a7d1bf4a9bdd527e45023b5a2e20ebed427ccbfca4546f85d51f02ec63b3506634714ad87f3afda1faebfffd1047b36267cec4a9356df2288043a6e651206dc618858395918c685e71eecd1c9f5ec8ccd6b62dcab9ca6f95a9f0929de67551e6a09d31550cbbcab33feb5afc800ac1e93fa3ffea61bf4775dbb1922f61207a2d161b064b23ac6aa9e0db44aa445b8c1a5f796ca1e5fe845be6fed5204dde35af890432daa2acbe76728b663dbdffc223b1ebb9e7442d890db3f53980044814936e4d07b848abb67eac57479920d534795d992aff434814f1c0143cadeb25ad55bac3b5739479800d00d4c7b14db602ae7beb6db87fdd0b21c03b6aefed81565919b3fe2de59348a8174d94443db99a30d5d340575ea0b36f2d415cc8293abea93eff982f734e52df5a9eea3ee8d12dc3de8192439dfc4e0e9a32663f044dc889cfc8199a55d01fd0587abda52f09e6403c6e3d9cbe4286cc4465da56c07248d69f560ad7d7fe4cae18fc6f164305b6af4b05f05bdd3d7b9e767181981152ec1f0e093e5381d79","170303003929b5bc81e89fb06369bb0ebbb0197eb108a2c989ec4a9a5b8a287d54c6adbee6b31f6aceb2ac090aa60134f4ca9ff3ad0779dbccdc96c74d7d","170303001650adb78b165c72c3609c3c664689771b57395ec406a6","1703030013d7f318d087d2697e672a2be2109229f07266d3"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-no-sni {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a0200007603031efed4145d535f830f068c6dc1ba61496d19ed21cdd6322fff79af1b7eb0217f20995d8af099245beb3b170bd57ebb5fe79c3dd3438df565713340e39dc6d47893130300002e00330024001d0020535c5f7da78e1f444e5f2a1c31eb6177b69ccc7423f4859ded383f84d9d97004002b00020304","140303000101","170303020384d8998dc25fc260540dc4c3a10bebed5cc5edf52ccf774421c81b5b1ef8def0db77eb62e2628a8e2999a2884a458396a5a00bf041bc00d7f7974901ba691c197435ad3823af81217ef013f832fc5edd8c4ffa254d8f8728fe4f472a7a9c04572e2465253ca8ea5936711b45616c14b8b692d147e46a9228d042de7c03e7ff2bca99a3535b6e566bde177e71f3f21aa8c3d3899df0205ce0c7206da59a383939efe29fba32c72e6b2225b4f8944835944697956fd5f18b3b71e1a95ef4796df712a3aef34b42e7e91b7b18c596805e875ec9a3e1cafcb03374c78a9d2c40780a46d370bb03e522bc8065f32dc5e1ef382d7131ef22d405173bc999b7f2f023d417e6173f7ffdb1c64b7ef6aa9c677beeb6a6089afe5ec94836d26b6fb81dffcd5db6070cfb3e53a09f605ea7b9ee659d0ee14b1725dd17fed1b11da6505b96bc1bda4dc8fe0ad65c0bf2b145f9aefd5daaa7c55ca5b96a42c6f817cd60b4da9e86fafc884ab108066d198733ef9bdf6cb81559142b06a25a9e84d9338cb4c3388a34482c5b383bbb5f248ff3a69505153087788fd537ea33acaa1b47fb77c3583b2f379919ff8ab7d223f11a0b9f76c2c90af7aa12eee5b7888725f1e86e1c16351b055a453769e8d48407a7c3d8c1df58d89410bc966ecb515b01580cc9aafc6c4501e1fb982db09af974fee594a5118863f333ff7925a4ca152e909f1c1c9f1b609b","1703030039c899b97c80c3ad649512024b31b658f50dfa59b155e30036bd2d700f8cf9f47fe9854636c26a8b534d9a881d06604e3a22a75573fe6b2789f7","1703030013bf0912b7367c2adc6cb1ff84f69eee6ad97613"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-hello-only {"state":"handshaking","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030363bc43320fca3580db3619b182bdc7ea8ffc41da19b993493da5fef6accb7f16208a1c619b434f1a7596cfb37bffa2b819a95fbd4cf30691a396bb9ccad1deb060130300002e00330024001d0020a32821bd3151d81c4647eee66200539623f5bb0882e2dfe85f474c70b86a0802002b00020304","140303000101","1703030207a9b978ba15efecd341d6a122b5c465c8763d6f5944906408bc9a73c0e3383a4795eb2dded9d99ab9473e8c9f7eaab3f224984ce87be569b13749c84b91ab63b5337c3034eba83107efcc8fdd55cb9ede68a16969466871e0461b0967e2ca84acc6c3c3c7b455f0595717221ca4f62ed6c3e65f7b84621f50ae3565ed0c689018723bcb8f1856eb2ecc980eff9df55ff62efa096209814e12a5a8745d5f2a4356b0ce030a7935da058dcfe8ebd6cae904e47f7c1074efa5fe371cdd37e4bdaa28ff51949055fd0a689c9362bd40a8c1676f231b73b1a1552798d2f84c7f89c20aed41e8942b7fd9780be4a1ffd492124da5a7345092b877f6bed91ddc792b53e6e1e0f088a913c1fb2db420957660aef8c36187c4e4a386853b53387693463eea48b72e9f009d75d28a20ba737e73636ce2cdd01f2356192fbed1682d13a2c65c18b93aa5004c9c9941035605b34b6da611ad2b4734e9938bc019a2800a4838c392a6662f3c6ca1101d32da5d47183379d4c433f3c96573fe8b53b7846549150f72f5591800c70502fa7ab755cd9afb7f0114c4c1376acfd3967b821b6e1b45f61adf7791dff384a05f81523c92fb458cfe043292c35f3edb210bd052e36a0a4f79c23c05221da9d21b303931f070399970ab4c4381c0f24bd32cf4cba9450a37ee68e29000843b1d4a3d1c96e20382d52ed49e732f8ef97a0593d0973bd9f81f79428977064eea"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":false}
```

<!-- test: tls-handshake-server.client-authentication -->
<!-- tier: stdlib -->
A server that asks for a client certificate: it requires, and verifies with the clientAuth purpose and no host name, an ECDSA P-256 or an RSA-PSS client certificate; with `request` it continues when the client has none (the peer is not authenticated); its CertificateRequest carries certificate_authorities when configured.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("e070af0bcc8071efdc301ce9a00b414758d543669e164348fa19ca165eff34b4"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("ea255a9801815ca79af1e05770bc5cae1794e027ffb05fc73a960e9f61116d8a")), secret(23, privateKey: fromHex("5afc5f95f02bcd6d58133f22ff80aa3e3e4fb911c35d7210d79e593917e01b79"))], pssSalt: fromHex("c4dc4ece3be04638e8c6ea6e91f4c74cf9ca15d2bbf6804f94436e7c55bf5b5d18a2d1d09612bda8253371ffbd2fa6f45f9195e1cd326805d439276650324058"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-mtls-require-ecdsa-p256", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303f420aed346ffb9fbb799feb60f2172449285c8273bdc05c7ee4ee50fdca1e92d20c6e9d0617335555e9bb32d22fac1515e61cb894468ee8e9b1d36d9f9000e39900012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020b94e71e1a3ad84815e348a11aa5a4feacf30129ece5916e6ec9184adb5ec2c38002b00050403040303"), fromHex("140303000101"), fromHex("170303020116d1899a329b5294d5db80713ff760b7a4c0150668ab360018486cddb330e55506d0ac67363757b678650e5e874f29ce9136192695fed4946ebce570b353f5fd2b91a116544ac2c8fc662ffde7de753b34e6da14880aa5b75a1586437fec058ef7493b9036798e9367536af77054bb59fd86cbfc791ee406a136b852d772ae418267b8e01d2ebc10d266c8f3a55bb5dd04ba75d4ae440bae90f43cd8b6db7113994360ffbaf26603fc84de5633715e4b77df8527a48c8d5eb82f471354d543e97b406993188c3b1b783c66c71dfb76accc344be14d21de75043426bfacaf508c188b82859b4277a42a981ddf67c38df1ce2703a5bdfa544bb8723037d1e15449d17656c2c93bf431e7c4a8d13ec248b9bfc8288d546ccf919cee6f2be72a4bcdd6c559022db3e272ac07d62e3ef569ed3845579dfde6a0d72f9e4474292ff92b7531961f2fe6fe5eb30ddedd44a2a4bf4aa27301c9b38020d154ea659cd691aa2ef96fcafffa1e99679767070a30dd550bb706825242bb21e71a43a7d3db9db43ef7b8bbf8efa6aef44212b7677cb821a1e033fabafaedec64f2fb8a924054bd2aaf2d3469e3075a9463f41c711a73c3ab86e9e65cf6fbc2cd927a4899bc5302c924589d02c15641c5e0fa22ba03e785674418e8499863e860b8649bb255654884664af13c35350e1fe42e836830c7676f146137522702c8c3fc46f704aa3e0483"), fromHex("17030300369a8076f0acfabab5733a311116d4438fe0753f6eca4090cf2fc5e4def60f1df7d68e6518d702aefe0bd8ee524ca5dbd64b9b14340ae5"), fromHex("1703030013864fbfd8be4946a952042198df8503c20b43a4")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c653a168051470356f96fa81fa4170729c4afca8a00ee5af6624a8d4e4de6392"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2eb5a65b5602082ce4dec708ec151190cbc56bbd4e5d4526b78c22073d51f430")), secret(23, privateKey: fromHex("bf3c87852cbb13273290e97886e8f796cab710780849cea0617ba61381a9aa18"))], pssSalt: fromHex("ca4d4960a10d7a6a0cc218614ea1ae289effc30df7a7e81cd9e8986b24bcc76fa272e6bf3452474884dac87f82b467d08958be2925ef18ded2ed53f41c49f7e7"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-mtls-require-rsa-pss", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303a740e538a645674212a389ecfccebcb8ab619176766f9f25cef505ab0d41ceda20e4424627a4be6ca9b3131ab39b86f053364f3de4bd963dcfb3ca5e3cddb47fa10012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00200e6d85ff7095affd236b26e17e9247c2366ce8fe46274af440d630a688458569002b00050403040303"), fromHex("140303000101"), fromHex("17030303828286f5e9cf91be7d33d210f6c634c28703d31232e125e7b0558d8d7571ff749a6a2f72079a8fbe8559a334e66cf92c38cf4ca5538fa658d1720c08af4f5ac5748f2d9032e7228df03f5ff81af7092b7b4e2d19b62a8ce4b2734ec182df133a80062d41c0069ed7db03a72827bb04cb6538a30b86b66d0a26e40b60e32ad439b393ecda602e9213408976ebe293bf3e8aed5fdcb3a99b2ae918f6346292ec5869bf1cef4ac1f7c8a56ce2dea226422297b65bd8e4fa64c3fd907a48ff417e7f385f58e3a82c8919e71ccc9e379aca4ab2772d1254ea72c35409dbef4a3e35f597f216559e454453c598b2cd9908984ef62efdf5cf3900f56acb5c139a6582d176d8c72a4f119dc180fca5e23e989c5b75be9994059346ab1e4ab6ee33ec4ef22d32fa3d996c23c5089b9d93202026a7026defefae3b62a986d48e2783a42243bac653983dc4514371f17956f4bd310fc6c5a949c8b6145bbcd0fcc4b84b9074456949da40ad75731f2d886b3c69e73a8a4a8354d42a9e6b5cc573bc98221e75a04f038bd6326deb6e1cbed86f8d4dad5a76d9639f66f8fff1012442dd0093476faad8b3330001deb194a9d4073663f6e15903fe396e78adf1361ed7c54d1799cbb999e61ed0631a68591bb3edc5ff9b71af57e414fc2e0b97f6912011bafc510ec9e1c904670a24a1d81bb8155e0cf2fb1c3d86f09c434cb15d10718d47321660af4f79a3b67bf9ec6016bc98351b27518c73b72aa64221bb94b11014f0f81269b06c6d61e783ecc9655c6c13748b22feb172a69bf6cd90bd79c41c90f82309eb91d3361a4fc6a62325cb2fe4f8fbfc0ee551b1cb8d8aef125e6855d2c9739c57ac5099855a8052e35d2b1f9b6411614e4fd09379c3d2b9c8f4d67abf2ce40c3ea0971df8004822007c6b7ebd27522bf5ace081d30194fa034d84a5fed5bb0f3aa718776dac22a0b3c45e059bb847658b1ddbab739c1e32f12a214ed561c9c9fa8f07ff5974b53d2bc38da38a0b198abc934deb05bd62b006499baed63f87b63bc5ed3516f9040d67f0489bf6fab3ce8a7fba7d675c2bfe8e709e2b567d5e094f8c22b09dc4ba2a3eec488e8accaadb414556db5ab64564862d1508f07174f7a2cb5428269ff5423e5fe8c0e6d42693d48a72d6691fbb42b114131851731b252abcbc93d6281de4ce2b9acb8081c1235d7394b4cb286349a9513578a89764b8f7bbfa8b1fada4803dca919070cb70963274455dde680c529fd48dbf9b30a54342a173"), fromHex("1703030036e3959a103510aea70ed7bfb172aa47ccc74f09e0184b60c45a1576ae8607e4525a80b230b6133882045f86c42ef68241117e0ee9bc25"), fromHex("1703030013104862509a372ba0a040d4def5c6cef9279509")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.request, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("bd5520116a6f9458a51d57ef906d61d3e13d1432151881ef3e6dd8484c531ecc"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("bb124f2f61f6647e79026ec928a5e5e9ad4cd7843a17914c58cc79d8c4152ebd")), secret(23, privateKey: fromHex("9725c9b91369a9990671f72e85569357c5199e392364029dbf6e7c987f5c6087"))], pssSalt: fromHex("91b2a60b678ef07ac624029a58d9d33db48b2a72982b4b4b659c8633ca50ee56fe94b1f3966deb71a8563cd4272c43748f68dd443d791746a699f1598ecd86e5"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-mtls-request-no-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c8d5baf6b1b6d91ae7030ce5a8beb2ad1e94296858aad6a72683e597929fa2d9200a5b6670447a312fd38fd368fda01183ee00ea78410f215c1ac2705c44c18f5e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00200acd7b1dbe220305510f68a087d6e26f50da8b3e7cf2cd27211ece968a50cc63002b00050403040303"), fromHex("140303000101"), fromHex("170303003dfb069db4276abc11a2e84f1635fe318f47f0b34e317468726436549bcf729f048d3b7624900c535b4121e84e6cbcd1b918d38995fc0b26bc2ea201d4f9"), fromHex("17030300367970a20b8c87cf5a58e72ecb09366bea601260560d6fa0705056529466bb9691e1997172fbe834d3a0a341ab3df11e64d24c8f4b60b9"), fromHex("1703030013d284fe0fd1f0bb11c503ee5dd66aeb93b20790")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays from [fromHex("30163114301206035504030c0b436c69656e7420526f6f74")])
	let entropy = TlsServerEntropy.create(fromHex("9bb87d810469bd7b271c6c8a29d552d6bde45a82a6d69942750d81c9aa359b97"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("774545e4f4150333a1d3d66c05753fea476392f3cc8999ca984ade9f7642ff66")), secret(23, privateKey: fromHex("29d75ab06c0b6eb33451a764ac21c4ef08a7b9b3bb04afb109ced04350ce9292"))], pssSalt: fromHex("b4e4edc62dd19a8cba845adaa4e229648f2427bc4d3422e55a302881e8f31e582c4d09e1a2aa784b20c246b1c20f1f1103081232d7724db48080cc9dfc0a1e23"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-mtls-certificate-authorities", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303dc53351d0e878413cf6c4205e10fd582670557a1cc304946f7734cfd77b78c38208f3a9ceb2db5f99b2ad6728520d7b413eb05ea746b32da29047ccaf1a36d16310012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00207282b264be6c2c9f1c388c123eb8588382bbbcb51442f1a8f43e410980126d3f002b00050403040303"), fromHex("140303000101"), fromHex("17030301ca71633b4ea54654f445d6b45923519c1aaa5b3a65e067f3006bf8970bf7b6a0c9bc2cb7a7bba802090809721e1b3e5207f89dc7e484862e1ba3253a3c6618a23c1ce3690a5a8b7ab6c31116894e09aea7a28dd4ea0d14911aa32b961fe3ca2133aab33309eb69e536a1ed6d94943516aea293e9b45983baa3b28d8ffad76b211c710eaabbc507a546a0a0495f5f96282332d11a0b4a693fdca484f3cf29084ad5af6ef1888fb89afc35edc8bc66d169ad119901ec3ed59d6cf6db902b7b2926997bdb3e4c4f38d38c23e95149908cd5d1e3c05f382fa445c8f04ce44354473bff28ab70bd19802ff11f01202969f39919e00afda9361e1a6d3f1b0959bfae226bd563d8e24c4394db7399fb3431fd915986b604806c765ac3b42479b884f1b186f33559d90fb9678b04a4486c30cfb458b277eeb9c66351812b73035cc315483704cf41266cd577d35f04fddbd4f7856b1e3490cf5d9c34e199ed11d67b17c0e76b2f8e515900c413e526919db517a747cb9a2798b2bdd3235242662701b1b99b112d50721c8a0c04b8b25c0add4bbdbbe0d10fbb772c2660d6cf5f449f35ea04772448c51723a1582e579d5cd921d9b6a5f7a1da6bbd2170756a957ca1cd452a91d3079c4723200e5f61"), fromHex("1703030036090841d4b16d2b1da9e782c3f146eb806f63725fbccdba8c16bbe2b35a75b5ee79bc102e51cc06596e650d9aae36c80a93a9ac9ac234"), fromHex("1703030013d49e9d9b2e51fdcd5499aa5ee464028bcf4f98")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-mtls-require-ecdsa-p256 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303e070af0bcc8071efdc301ce9a00b414758d543669e164348fa19ca165eff34b420c6e9d0617335555e9bb32d22fac1515e61cb894468ee8e9b1d36d9f9000e3990130300002e00330024001d0020e3912a4cde2ca9b8b1b2f555a6245f76b132f3005d40d860645e547cb52efc20002b00020304","140303000101","1703030220b9252c691d43cb8c7defc3a3a69208514430ff2b6ffdfb4895a78793112e0c48db7eb0d054b560b9c02176965fbc491c8a3e35269c4e0876703b60bdc7cf9f26fdab65721db1daa5539a0c82fc330444412f1018ae9fa2dedb5c1b7842779062560475dcbdcae5ebc1fd2f772233a9affaae2432b7ac809618d5af0cf6d954c9ab71fae3bc0a350215ca81905d494de7854a2b50160caec35758cb535e914f4832bec47145e38ed6657a4bcf653911c0cce5101c2b2f080ac9086ccf62aaf44ef404e5bcea5a66dc0aa4d3bf3169ad93ced59da1380cd3a466df9941c8b5a836365f137e153c5b7a8139a4770a7561f4249eddd870392432f970796fd14a509d02396956e2783f2776686f86e07edc11ec5d639a0994e1b3705880fb689d4caef203153c10fccc295b05bfe545cad84fe0687e99e395b5791ca98b6c2cf3790d263a5c5d10fa8a575eea4ee5b3c057b483d53d234aa3bbc64b1374c1d381f380e102d8b77458617924415f5687069f3674539d45331f8e5320dd1fe5391a12276bc5562a55c7893d14be55f5ff70feb0618de94639dabe3587dff7f8b6133a6e5f1495728af7e8237522309fc4cbebc54563bbab7489d95278c016e3f5f24b048a7758cc57c997606a5ce8de45de6088bcbd9ab96939f1df2ea5b588fff7fb4985a81244fd5a95b52171a5aa14160a1ce7c5ff5a168ed1b0f067cdbbb2b72e6b314a749aeda8a09f29f2fe75633a1687d90d82f6ed8b5fcf572216df2e4b49be","1703030039267db673fbd7c10c7200aca309985e481e929dd6729f1877067415e20f53e3e75e53afe806ef2ccc07b0ca1d8439317b00569a3821bbccc856","17030300138affe4e3eca4668892e79ae03456ba9ac3fcb8"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
server13-mtls-require-rsa-pss {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303c653a168051470356f96fa81fa4170729c4afca8a00ee5af6624a8d4e4de639220e4424627a4be6ca9b3131ab39b86f053364f3de4bd963dcfb3ca5e3cddb47fa1130300002e00330024001d00201b80e4a16316f338e0c74c5e4ce97740671d7d568bf04635df93319ec361e659002b00020304","140303000101","170303021f5513d90a016ca3ce19567ec705e3b248beaab5e89ecbf6e5c3b2f0338a2baea515d0a3ee26a0b0af9f18c6eb36fcf82caf87b62cb125be711c94d2a0604e4c92de3d54af4e42a7e36207c34ee4fd801e8bd366745bbdfac9ad03bcf8c721eb68ba8d7611c87f57b829d3813d92b8b83bc948d079d15efe38049df46f55ab8918d0bd2c1b9c70e3c0b2df33754669e5c9e2ecd39b21cd2901900fc251eed1a1dcfea7ee9f57d574f39f5dbe908fdd16582e6f5b5208bcb8b1517f158d5fe75c61dad23422ef58cbddd015527b325af964b59b993c6f2de2a5ce732dc919336cd25637b5aa5c24958d1d2a0dea0c1b92ac5fc278c3e8e7f6d77c6d2dfa73eac6f539464c38796c0e45ffcef69f2cd16b9e9b4806c2c5cf6b2532dc48f2d834e0ff78a98cd64abf3205c2a11cacea01e7651f4bb833e7aaf8831b18c66dc6ca3141b36f5ef2216fee5eda9245b6058c68c6d430468d4bca2a7219006b252e8e20294b9a553954888c45bacce4a9b862c1e169664dae4130693cf1db65eb0f3b7133dcc8839bcaf0112b21d52d0e5f34081f5dbd261845ebb7319af6d165bb70a64e1c7f26f4c9f5cb4dc55d4b00841c42e3f3de652378bb3c3a3dcf4c5b4264d58497e0cb52402a60a1d676024621d4053d48795451d19a080a67aca5b28a386050a708d0802facabc147116e26cca9c34a0614979744944403d3df918012f40af708fd7943504bad90467a62316504fb0a3811a3cab54c30b04cc30113ebec57","170303003930ab6d714030bd7fc01bcea3e80764c490a00774169ac493109b23b240e6af2fb6bbc1e71d377153e07408e3538dd459be84a12656ae94a74c","1703030013c1b1600747e412ee88d969adafc68bd41ce5d0"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
server13-mtls-request-no-certificate {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303bd5520116a6f9458a51d57ef906d61d3e13d1432151881ef3e6dd8484c531ecc200a5b6670447a312fd38fd368fda01183ee00ea78410f215c1ac2705c44c18f5e130300002e00330024001d002055ea4ec8c54cc8df133438b9241d0d726415da7bf0c86533cc2e2953b1ceb405002b00020304","140303000101","1703030220df231a1a4866f339b9d1e7335b61465450eea886a5402c5bc539ecd91adef979f335012bb62d845e56cf6f21a19df310c8e72e37a4f038178da58168013aeaae423ac0f60395bc594383c3f8af743f4c20b1f9fbccd9027a90f6f784e5c90a6001b5873890df726d6320a4af96b53a5761718c63a4deaabb958900115db57b3772159d6c220e6801e2a0d4f6c42de96299763b73ff36cbf3d1080b16af0c390a11bb84d58c20d6ac137c8d88237015a516aae5f1e847fc8791d2a3184dd12adad5ca4ec91f9630c7f153a38c2044ddf3a6db6f015964adcbb09a3f495dd83327d3db52f9f5aece3a42b44fafd26f2bd81b42b7837af91a012fd8f2bd627955c070d135fb929ea272ba57c002565b84adb0fc8376eda32cf73682d13a92f35efdd2eb17c286e871b4094dc252cf8672f4434e61458a0e27d53cd16b28e1f0c2302e0be082b565b9e032ab0cb1602c87372aa59f2ce83af990ef7f6aaa372e5f3a57f29db9588f0f26c83670e69970cbd0a9b59182c73f03429bee0f28b0beb89651972661caf76f5a44c439c21a540fd915aeb55e218845032f21c5bb2785feab60004a26a2bfff3af9ff084f4820e78aee3ff0b2530d207c4094fe1fcf586add98552b7641ae3cb2873b68491b262abb3b749e7440cfda075e09986ced569172174b597d9f5667faf536710b0bad14142cf2b6f12bfe58a4ea0eb7a29ccf8d46a4290432f4b242d2cfd3c07ebebb4c76ab3c28b95b511187986155852e7e0ab9","17030300392c52973d05961582f3789ced556f93364382d6dd4f23cef666640347e6359bfe7294668c79a1f4f102440a557deffd811d6ecde2a13d35ed09","17030300137ed6e59e72e77a17412b6bd1f7aa475715b8ca"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
server13-mtls-certificate-authorities {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a0200007603039bb87d810469bd7b271c6c8a29d552d6bde45a82a6d69942750d81c9aa359b97208f3a9ceb2db5f99b2ad6728520d7b413eb05ea746b32da29047ccaf1a36d1631130300002e00330024001d00204cca16a43694bb3b58a64ea2672c751a715c81e832456956d916b6f199ba1b21002b00020304","140303000101","1703030241242288320b5c4bf880db4e7d96379087cebb69ac76291b65a3cb325a6c61d5d5049af5e867aaa7911c48c04ec14ab4480f5e0b49cf79b27f5eef3f3ae7277cc372b8dc406f5f6860f15382a47a9f150bbe7235acba06936a91b64e181f77fb86e877bf33eab4439a448ad63016fc0cb32c32fbb25e6b8ba6847c0dffbeab46ca0f7bb54dd26a86cb7b859b694a095e1341fcb60e171645f5cd086928a46f10e0b70badfdedeb8b64fc15dd29b4e17a9517cb82f77c9a48def864d99b41f31b356b1ddd3fd497d9d8e49126921cdf5f4dcf8cd085d0cbd3f6d98f37b249b41f0636cfc49494324145160139d7d5ed6f45916d719e2ee85a28709ef7f2503acb27ad78f6b2e4d176e493dd506441ac2f0ebba8b23b41ca9373455630a9618159a3702426ce2a8ca69e0a120b0b669f0afd41008b018aa59658cd16406f4131346b5de72b20bba01de95bb2660cab61587a190c2979a8f42eb09112d9cf3054d30d91686aa013d75fd2e7c8eec9859e9fa893feaf86de3fd7c1509d8d40814bae6c65a5037f5b4e4f4d4fc7e0d9cfa5ba19a83441528c93dd94425551564150bf5ef98b37f7ae5c1826a5dfa3b26f1bfac2e2f52d7c50e1f5ea0a7147f559032a6285929b5192aba65617887dface02a1361b5e0ec96e6a5861fdfd82a800c59857ab8e773f2992ac394932cf1db073c9b0aba19a84796fc9937f1171f6d85e7d0be7aaaf10eb3211295678ef065b86112141041c3bb53b3e88274bdb519ac297b6144366ccfdf26b5d29829882103bca6bcab1681f8a24102fee8d3b32f85e25cb7a","170303003949f68f59a38f92a1f8e3ab1966a8a5def3af3d4eaada59d4e01a11c6cdc7da0cbffdd03b6c51d0b8fcaeeb09c02d50606e5c0c12734546c3a3","17030300130aa06f5b056b92be6e5e389dcbb83b481d0b45"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake-server.negotiation-errors -->
<!-- tier: stdlib -->
A ClientHello the server refuses, each with the alert the server sends, in the contract's negotiation order: no common suite, signature_algorithms, key_share or supported_groups missing, a key share for a group the client does not list, no common group, no common signature scheme, no common ALPN protocol, no common version and a compression method.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("31a8ca49880bccc574b42c36f5ebe848eb643e259de04278ad0d6f42631d0a31"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6f6066b069dfd61980bebc7ade15b0cd83ffce0d63f7c405329c8c0c78973b47")), secret(23, privateKey: fromHex("bc0c0a8e94642b56fad2d92456754518f72956ecc7bef07e87e26e63fa37a307"))], pssSalt: fromHex("683443889f34aaaec487cb512e30d6c5a3211aa6e2afc29759932c80fd5340e28f04962cf0ad9b0f9ffb227be541361ba29575ac09cbfa773b5dbd86e255a0c4"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-compression", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e7010000e303035bb99374d03afb6294775e5c25f0fd567d095d7b5243bb84e2bc55a594f96e52200c675ea112371654929901e84e94efa591b3163194b4d72aa226addc5c9d622c0012130313011302cca9cca8c02bc02fc02cc030020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020e20a15aad0aeb66d2046408f1d4de3dc2ad712351f74d1020af68bd9e98ac909002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("43a3a766d7eb8ceb0ea72835f902ef2f4c63c327adf06c421f89c0e47143a85c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a1cba233b7564eda30e01373ebbc1b16d8a9ad0d22d12afe53304b782cd4bf1b")), secret(23, privateKey: fromHex("95c97a6dd929c30ecabce595b5dcafd80a4a44d01528b0c9f57f899fc9123508"))], pssSalt: fromHex("0b721c29a20b117bf81173171bb7059a93b1cd9ff03c4f978afacf3fd17d190efb8a61a84a8cf7d1a9884767cedaad62a0169d28b9e98cec53507ee132984db6"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-protocol-version", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e4010000e00303c823b3fc2a75127bbf238c8eb99f6975fdaa581c9678c6e503cf7c63d3b8701820f57608aa40c00fb3d394bd38bd61b00d016a1e0930fcaff306e3b47c3f9ea9040012130313011302cca9cca8c02bc02fc02cc0300100008500000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002011ae7ef67f8e856e3f5b1b86a1ed178f1ae0710957ee31e9fe15f0c97aabee1f002b0003020302")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("617d814227cf7a1f84ac60c403add435334f204386a333d39f8f1bc0cb201861"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8f65ef5f90f2bd3bfb8abe44f7fa1857ed740b02063636669cf629fb2a67021f")), secret(23, privateKey: fromHex("d33f59cdb404e474089552d6842f67187268b9aae3672bab4e8c6949e3b25d8d"))], pssSalt: fromHex("a734ebbade5fe6c87c0359867e7f4bc61c50ceaf0cd86c706a764eea2b60005e0cf8bba0ddec117754eced2c57958852909bbd071296bf7a4640a999a5a56ecf"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-no-common-suite", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d8010000d40303c1c2f33f6e7f8767369047c94b5b4707994e7a204d61eaace92618335e4d640a20a6d66e6b9d0311b6c1d7b1dfcab59e5454bd7de785578980befe00bffff10a1600041302c02b0100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020839765731928776c2eacffac75a93a0ac73e1da5f429d1b5a8b59d8b367d6f7d002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("ef3eddfc2405b94f97bc57a6c4a3593ef3ef60858feca8f7700af9189f5fc2bc"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("827b154b14838395c34b8748bcac91b93b757fa99af8405ff9527f7d267b6514")), secret(23, privateKey: fromHex("fde0bab1359c519c2c955da8c35742a073d4b5996aa96a70bb35f48006167fc0"))], pssSalt: fromHex("5142d6c4d91b755a27f4dcee68fd1b1338fbf44ec4ef3f8eb1804efb44f370295ada9a8b8c3db9ffc6055706f9f25e8e1369d54b5dfed9f65b578641c51833c5"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-missing-signature-algorithms", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100ce010000ca0303f8afe90abe817e7befaac1a2778f04f245fc601308481c5c4dc3d28d6a3f40f7203f29618a1230a8881384dc56b5971063b84a10957bc30703aa0cf60649a9080a0012130313011302cca9cca8c02bc02fc02cc0300100006f00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b000201000010000b000908687474702f312e31003300260024001d0020f52143458ceb79b017d79b786c133f6e5a0a10d4a18067ca90fbb4caa2cea659002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("40a17fdcdf9cd2e37f4f1ad36967373787e31c94e397e82fc8f85c5ac6a01a6a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fe8733598383fb6c6d8ed072444da84ab4bec52ceb37dc889ad7bf396c45c4bc")), secret(23, privateKey: fromHex("34a5d98cc9459c4c984e1de41cda55e44922ba3ae71bde2486484eb8b36422cd"))], pssSalt: fromHex("202bf0327a386228b865073c02203077a22a0c36aff3e91d3cbebf1b849efb4c4febff99730c8cfb474a6c3f1a081dcd28c6b548a61d0245438f39b768bf4e43"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-no-common-signature-scheme", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d8010000d40303a846d89c2d0b08977ebb6a47729965d37e0794b68a18c386fd8fd5076328692f20afa5bf8e2310978bd4b14834b889426b16bca38e3982ab91643ff2eeae5ab78b0012130313011302cca9cca8c02bc02fc02cc0300100007900000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00060004080408070010000b000908687474702f312e31003300260024001d0020460fc5de84405f618221e1a5e6b28e46b47f1ff830ebd770c279ac609d0e351f002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c19e882c59eaf765c26e573091c0f40711120ca207a602951cdfeb72512d8367"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2200ae2414718b0aa2e49a65d98c13b905de370c88d987b1adad1a11d578c537")), secret(23, privateKey: fromHex("ab81c7081176fc7a1837f545fb3d72310d45186799155a0c01f04cdbb8c84bb7"))], pssSalt: fromHex("2e62db310832bf1aa99b1244b142b2982be9b23b80b3658d5f3c00784e4f8ba9a031bf281cd10936a9d2c4688f91ff4f6851231958373e094c0ffd815680036b"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-missing-supported-groups", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100dc010000d803035bcb11df752bee1de7f26c74e31c9099830108029455661acba922a1f31936e620bb3223edb3ccb747a6e685689e9bc302941de92df3f1c54bedac3ab1c5ffdcb80012130313011302cca9cca8c02bc02fc02cc0300100007d00000010000e00000b6578616d706c652e636f6d00170000ff01000100000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020d1184bfb06326f1034803b3e78f00f43d2a83fc2cadcb55de25ff00b20759d48002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("1c54f97a06b110ff699f74452d350738801ac50efa641308a022ece018ce1c17"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("19ab9826025c97e93b21944c80c919a002dcc8eb09447abf8a004422594f0ff1")), secret(23, privateKey: fromHex("e55b7dd41250f40cc25c0fdc9e277828c74a5b253e7f26f91e154a688bfab0e6"))], pssSalt: fromHex("739a762cbb832fdf58f41821f6d49682739cfadb2d47688feac8932377e2039b7ac9e8f3729e29d4f9dc21367489df99f512493d99dd6fe267502933d7eddca7"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-missing-key-share", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100bc010000b80303ef6fc35fabb1b00d9754f1848f870b69f37eeac42b592d5d875d4b6f06cde14a206d7acb62c6961e624f211a36407ae77fed2794863ad41cc6d9a8badc7b4bf1f60012130313011302cca9cca8c02bc02fc02cc0300100005d00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("4708ca992d11b0a5d12cc0874857f5ac6c32684924a63b39def9c4356432a210"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("248b56dd982ace6a74f59081473d2dec028c6ae2ad1eae8958273c2034a14684")), secret(23, privateKey: fromHex("a2c970ef7f49bd54a9ccacc163e5676878c724a3e6717396a03f65ffab613ba6"))], pssSalt: fromHex("dfe7c6da11b048ae8af71edd8e75a97dc7e8fb68542116c89681b4bcaa9bfb0f10878a9e76d5e9391a9b59bef0aac8d391e084a8b9274aefe30d9218b3d19a1f"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-key-share-group-not-supported", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e4010000e00303dbaca644512e0d2953511cb0c826dffd4347602230a0ead741632bb21a1e98da203328fa816ba140f2cbaed0f25d6d517a82e1b6e4e4526ca90b8b07533daf10d30012130313011302cca9cca8c02bc02fc02cc0300100008500000010000e00000b6578616d706c652e636f6d00170000ff01000100000a000400020017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020af8ae48e8a6857c28345702b6f44ed6a67e85a8cf05463f2f4cccf8adfd10646002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector7'

function vector8()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c6edcf096811594d91f125e6e7482c93530ec16bd6721e227d6e4df88ca3573a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8d6bda5505ca7c725d26c3519225e7fb4f506df9872ff6d894dc35dc6b3223a5")), secret(23, privateKey: fromHex("ff9001eb1d04619518785263d232e6fc88348b2397f20810b659a1382d799ea7"))], pssSalt: fromHex("60ce9fcbd79b5abf880e7eea136a4a630fdf808fb60d25fe70c46dcba9d4417cdb3ad985debfbdb259c62ce47110b6d32f598056b9aadf7086da7d8ee3994e0c"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-no-common-group", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e4010000e0030311c4270193fe7c6fd0ab2d4605aa620b3cb5a7155ba8a0c2289e3fb4bb1c1b7e20873d781c90edff510f2bfc7c95405dfd97e0eafa5ffa4b55377148fa281a56440012130313011302cca9cca8c02bc02fc02cc0300100008500000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00040002001d000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209f425dce09e4866f270786bca9df05a68291cad790777cb598df31f8141f6400002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector8'

function vector9()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("09f11e5bd8551b9da9e88cf0e1075efef5ffe6cbf92a3a02e1583d90b47f1112"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8ebe4277352d0a7d0beadd30791643070713424cf401888af12dad0ce29420c8")), secret(23, privateKey: fromHex("1cd71aa97d9595cc941e2bfabc1cb4f8916d4a1775c282cea084cdd37f0eaa22"))], pssSalt: fromHex("d048a0eec5f88ab43aee2c741a7f974ea1300d8cf937375877b278bfcc98459e893c0ed349a3fe3478980e0e0e6064d1fae574158b8c5fc614f38dd64ea4d199"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-no-application-protocol", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e0010000dc0303a63ff121998d8b015270588adcbf383e15381ee60dc34cdc0db7e86c56a936062071fe31a9248cbb3b18881d85803bf830f0996b6b04b79d26ef4c51b0c0884b920012130313011302cca9cca8c02bc02fc02cc0300100008100000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00140012040308040807050308050806040105010601001000050003026832003300260024001d00204ea5a341543a9248d4eac4034ef66b18dc7f2dfb2d92fdaf91999a6a44eb752b002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector9'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	vector7()
	vector8()
	vector9()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-err-compression illegalParameter
server13-err-protocol-version protocolVersion
server13-err-no-common-suite handshakeFailure
server13-err-missing-signature-algorithms missingExtension
server13-err-no-common-signature-scheme handshakeFailure
server13-err-missing-supported-groups missingExtension
server13-err-missing-key-share missingExtension
server13-err-key-share-group-not-supported illegalParameter
server13-err-no-common-group handshakeFailure
server13-err-no-application-protocol noApplicationProtocol
```

<!-- test: tls-handshake-server.hello-retry-request-errors -->
<!-- tier: stdlib -->
A second ClientHello the server refuses after its HelloRetryRequest: one whose key_share is not the one share for the selected group.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("805a1665575395813dfbc341e62b646f2672f427a1df28f267547222f1ed54a5"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6bde4a988704c5476e198087855a26721dfe66bca6107bc0b74f5a6baa9bdaf8")), secret(23, privateKey: fromHex("5b34221196ccfc0231fbe1f316085175df7163fc6985d0ccdcf017f897b1de7b"))], pssSalt: fromHex("f7322c78fd127c6912994e8e7a25edf52a2d354e97967f0d873d544d9c270c9e98f3d53e2e4a0a27a261782b770bb5e59ccdc121adad78392d56902d483523df"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-second-client-hello-group", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303eb8683f521b0f479743522abb6ed266eaa05f1a41d98bf305d783d2d80c81b2a20517a79a2996d1724ff335805b74a8886b3bcb8c92c1b19889c07c0bcddd074340012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00203e027e7192225f56bcdfad266de6506661c044a2b09581e6157ef354083ebc35002b00050403040303"), fromHex("140303000101"), fromHex("16030300e6010000e20303eb8683f521b0f479743522abb6ed266eaa05f1a41d98bf305d783d2d80c81b2a20517a79a2996d1724ff335805b74a8886b3bcb8c92c1b19889c07c0bcddd074340012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020c2aa5f44fe6a524297d86b06062e4be0676a3cd7941eefa9cc2259dabfe73e0f002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function main() returns ExitCode
	vector0()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-err-second-client-hello-group illegalParameter
```

<!-- test: tls-handshake-server.record-and-order-errors -->
<!-- tier: stdlib -->
Records and messages the server refuses: a change_cipher_spec before the ClientHello, a duplicate extension, a client Certificate nobody asked for, a tampered record and a Finished that does not verify.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("8e25733dcb9b685ce8b24c99a26eb3fa5b3d083e442179e3f8234cc29e792f43"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6f952c49b559900b191c0045311e4b6907c4d77a118737bad65cb65f5630f1d2")), secret(23, privateKey: fromHex("ff6b501f00b013b9add6d7294e214fafb15c4b2ecd7fae7201cc9627602eb410"))], pssSalt: fromHex("eb3e2af7b92ceec03926370291c63ae9e76d8d1764224284570c504b3a3fe2eb8b6d5cd4f49d28cc1bf78636a876613fd6ca56a2fba822abab741c2f8a5fd260"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-change-cipher-spec-before-hello", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("140303000101")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("88c24c8d2a9b9a36285d4cde6fab40180a70b009d54c80b827d21ec765307bd1"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5c754e91ea31535d075c8b0db5104885c792489ed3557480f57f24f2b99a495b")), secret(23, privateKey: fromHex("ab832c0c26a099413f5536cbbb4dca9570508358a1085b6562e28a554e33eb87"))], pssSalt: fromHex("7af872a4781f6e86527f012725eea6adec94c1586d2b609d553e94ba1d5ee88634253c620537caf49d1bab4334b3d804a6e68ca892570be78ff9eeb69d3e5cfc"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-duplicate-extension", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100fa010000f60303dfb8c8f222befb9a9740ea3811914e7de6b332a64f0e7aa1b699ff7746a3b70b2029687a68965d7638ee3d1ea62ec4ebf4b1592da83f61027f383240cc966e58e00012130313011302cca9cca8c02bc02fc02cc0300100009b00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00206c971294b2b1bfcd8e90b61fd4d0fefd8668ab2e30011de49e7520e8ed73bb0e002b0005040304030300000010000e00000b6578616d706c652e636f6d")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("078ad0b32ccaafa99cd7792f51c67f39ac4640d98684dfb6c0ad255f520b2f94"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6c9608f77f61cc1b14d61b659e62dffd010da495b50eb77b2681042b911b0aff")), secret(23, privateKey: fromHex("167ba70345dea22d90952035d7b8abe2312ad913e1e3db776b0e8c9d82b9493d"))], pssSalt: fromHex("727b823256c7d0aa7a8af64b6d720cb970f497bab3de482d814b13dda1cc435e98681775d2efa60af13ec93fa99d5df773747483698bc45b066d62a763259cf2"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-unrequested-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303906a353df6abe0d144004f10b076e322cdc910ffa2e9f4680dad94ce8b6678e920c40dd6de378706fd10e2cf7f1b392eb0ee521e0b627eb6983362ea19073efb160012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201b357cedcf9a17c4d3052e14bd4ec33e61be57ab2c5ec80afa0785c9bbdcc34d002b00050403040303"), fromHex("140303000101"), fromHex("170303001910b0eed15ac6a410526dbe61ae32da6670fc288a66320ef44d")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("97ddfddd349c296655cfaee8d6b0465f8276844b424fe020db04ae8511a40752"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("199d45cb8dfdaa66352a89250936b7f848728280a2669210d5e607e6f7d1d941")), secret(23, privateKey: fromHex("c51d2cbb2ff8b16d0050d269e1b79fe9c52bcfb0a8a8998fa0958ef078c18daf"))], pssSalt: fromHex("cd52b150e1e63fc58d22f2949ba91310f86f12e2440330d79f784f8a5bdd0e92e6fd2de23a2a1a17d71a484894a7fc2e529ccc2d8cd19b876bcf67a21ab2a940"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-record-tampered", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203031afda1fd3dec84cc4af68e03c418a418cd60454bb12b977a47d664e2ef2494e2204549e847de4f459cd6f7d42f1f74d410968f3c9737fe67334b85c185a1fb03440012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00208764ad9611e70a0bf810c28c6c2c22506efb3a5838188499791e6260c38ba648002b00050403040303"), fromHex("140303000101"), fromHex("17030300354e9f54600574f1ff0e727e58ab4601d4a733032bdaf5eff7981686ca7d3a288487e8e9eee14e0a31c07537eab4be1952993cc05f0e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("a951666f5e8ff6dc3d0022035c99f8c96550113fa615c65afafb6df99ce18bc7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8e38a24fc7e4bc4cb528b1fbcbbc1afb9f90d85ce8b5725d27e46c8333f6242c")), secret(23, privateKey: fromHex("3afb402d1e15bc90b95058af8eb8685819602dec36a93ac7ef60943cc83b8c6b"))], pssSalt: fromHex("df70d97ed0a5573bd3a5bbb0209d58966f461e92d948aaa91e6025f3adab659b66f044f3934af5d351ae89b0f51b1bea43788bfee4cc4f5790ad134eff06e6fe"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-finished", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203037e96f7ada50b96d28d0e83be6af8e48f68671cf6ea105e3df4803cb20f05a610206168dc8f4f92e11b7ada862731a084c1a82c611fee72617353561c440ad979090012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020925560c9db2456e1e8eab8070ecbf22fe32aebd2f18365ef74889873c302e57b002b00050403040303"), fromHex("140303000101"), fromHex("1703030035e87ac2e612689e366a9bbbdc9a66b91a409053764eb20d6ce8d2e802d717a8fafcc273c3814bdea9be966fc34cf7592e927ff98d72")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-err-change-cipher-spec-before-hello unexpectedMessage
server13-err-duplicate-extension illegalParameter
server13-err-unrequested-certificate unexpectedMessage
server13-err-client-record-tampered badRecordMac
server13-err-client-finished decryptError
```

<!-- test: tls-handshake-server.client-authentication-errors -->
<!-- tier: stdlib -->
A client the server refuses when it asks for a certificate: none at all under `require` (CertificateRequired, also when no scheme in common leaves the client without a signature), a chain from an unknown CA, one for the wrong purpose, an expired one, a CertificateVerify that does not verify and one whose scheme the request did not list.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b7da1dc63f8ec05b400398b032ec0f365ba013232a04764cea6305e37c30913d"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d152fc7ca626d546d0db8f888f75ce5f32b4c1d5c05893309ef22433e6d32e00")), secret(23, privateKey: fromHex("7b32cee113148b2f8c12b5859f185535eb32b42b2527c93c4ef15c6730400d3d"))], pssSalt: fromHex("3375b416ec024429390be4b995e28f41b48960735d16d43894f29f406f505b1591e7f1f3a2b60cb5aab1a953f16a900d008d609048560fe2543d36cabfad7d3e"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-certificate-required", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030341599d9c60ac41e13181587db4a1f4cb79e7d097b9dba5d428e9d21e2700f40f208747f021825682cf41d758e6396aecefdcc1bd1f72ef7805a05e2f1d1847fc880012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202e36fb60ede8ed886a2227c14a9e044520d121c29e759fe60edb550aba259909002b00050403040303"), fromHex("140303000101"), fromHex("170303003d6a75da8c8b46b99444254860a754ba2cf48e9f162b8747511fb53a1c70d45fc34cce6b971cb53532db45615f6221d4ac7c9b022f6217d45491989da901")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [2054], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("5515894f8c84dc887517e65f2a4ea966eba644e60a0cf55a28beded144bd9c03"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2dc8669aabf79b2b487fff50576e8f5e45391033b5a62e7e04b18a89fd20867c")), secret(23, privateKey: fromHex("64199f408dc0580486fea65da8a00e1f9ae2f41b0c50f5289e48bed13303a34f"))], pssSalt: fromHex("d0bd664188c618e0ba03844a1836980312569ed33f68cb1e2b16ab01963544c3ef3aa0495ae7d27d2bdba3e49084c741dcd7432391800b09563f58edaa986660"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-no-common-client-scheme", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203031f52d3818792397e0ad48b861ab1231ef72db10e7fb5bf74ea5079501540cad620b8b71acfc7528e1450618e64e3f0e7f883f647c67be6be74451b5bb088684d6d0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002090d8259f15b0611b5a284b376c287dc909d7e8be73323f26e90bda8c14b49a68002b00050403040303"), fromHex("140303000101"), fromHex("170303003d10ecce85a8405f065f1c3e8dc7abbc2b7234a5359c73594a8e3e57bad17f429335398a1ff823fa9affebbe32f1e7c16c88c8963b51eaee714f2be434b0")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c92db9cc05b8680759813364b6ffa5d68b4a1cf5605d28a3ccb46d41b9431c8a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("96780a4a18c5ef0ed0db26ffe81bf33b1ac24387f259229d38b78cf4a59808e2")), secret(23, privateKey: fromHex("d1197fd4027c1fafcc5013e367844a28b491ebbc08876d39677a2ee8b907a706"))], pssSalt: fromHex("65bf5a279c434f02089525a504e4c185c40cafcc23dcdfff65ac24d0543e48221938e5e9f20bda7c21638892f424b19c642f9172a7ae8e7f99833914d4554590"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-unknown-ca", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303aa147eaed73927fb9a8aa099d97c8195512fdbd8834e57a6846f3cfd24b8bddd20debb1cb780f562e30d20119fc7442ac1fee337e620357039f7fd3c6b966aef110012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ff38dae0b3a30c984373309ce9c2d1b3f80274729f41bbe1fd9ace49b0d20e71002b00050403040303"), fromHex("140303000101"), fromHex("17030301ff56e43cc49e7568f9538a4689640171bf2b1d5baece49ab18c0bb123778e7721ae6a1e5e3beee9753a45eaf975fb09aadf99e4dd60c458b7b6f5796f8275d150bc092e05835a63c39b5a3db0235f5d81114f8c4bde3d89a9e2b065faf17e63088ef69c2b16a8b2044945e0e7212f45bdd09ac314cd844f901473c22735aef646856724021d6f0c3ae67a5f9e1ae0db755337b8cf5904d0deeb0aaf7087a5f86257369959349bf179fbfb680b567d564c94fd3c588f273fba1e146ce798de4fb0dae2449329a43fea3a4ea105f927310452b65ad7a4962ec9643b04d8645094df23813478649e285e156e89be496aef11db45fbecca979fb664556a29c8cfa153c5658ca66b65d8f4282935402fb82541a7505967248eacdaca3261b04514e9ad295197c5f34d9afbac1bac12db3931bcc56795c8cae113f4ae049cc58fe2ae3a59a8a1098847fade2cf9d12fb281d01ffc814e52f5c4947d90a9c2124194931c40d246aa36d7548dc94ba4622beee07bc54889baf20690e41a36c6ab03a61ea32f41b28f2157df9928881edf408eae63f95d4325695133b0f1ce3390f9ddf3b76b14e5b2e0f7bb3f4df1c64bbf78ac9e48ed72805514a0e458b2ce97e4ed90840113de91f17fbda59c293092c9e69fd7ce53b84088e314a26227674270c60a9eb86a38a74abefd02f331f933f76ea590bc681353c0812245b7d2f7d8a05269b")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("6fa593824f99e6cc642f14380f2e731dc44c815cc89fc1c5ccc668e5708f25b9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("40b21ab21018185cac8cf396363f484e16daf0414144cd39519ff6fe76c22f52")), secret(23, privateKey: fromHex("a242da3779cdc9d95d6e90ca38c155d7e1e8013bfbcc8b2356c14b9c9113f7d4"))], pssSalt: fromHex("fcb9cddafbf4c0b8d9858195ffa665482d5626802647a863af6816e4ff4bfa7dfbc43cb5291a7f1214258e81b778f7bf13124705c2999da9f9b86a8360fb0331"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-certificate-purpose", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303befff12fe4fbb3f7753f2d57fafeb92274c854261e69bc5075e7b6120e7d1bb720824082ccfe603a4590b0630748efcfc77b818270164498268582484acf0cb5e80012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002044d08be700daa0fad99725450b54ee105383755a589e1bfaa2d3f234ad83a64f002b00050403040303"), fromHex("140303000101"), fromHex("170303021980185750500e96dd99c580a417c7f7f88f9e84dd771e5486e74c2f3ccfd04cc36a82086b93caf22f5314cdb2143428a976c4f4ff76dcdd54cc9a0089f5f1d200d59b913d5778ca7cb0e704feb1525d880403a104f25f7fd25b6e8bd3fd0fcbda06666f9a569d068f240a8ad09831738413d148f3080eff72173cdf5bbc2d5b3a0f0b33dec339a83aeed8f14a4f484af747c4d9c7fdfc02901d6b83ec6811c1a39ffc72fd8e1be0237b010a0dc616bab63cb7fed34e883104349cfcc10f2943dcf8b70e6a4403eb049d531e29e22c484c21fe9f9bb17b364fd81f3b36daa3232985caa5d00b8c38bec2e9bc0829c64f02273f6d12f20be5880ce7077ad0240ec4552600b05db1df7b3d40e7a2d8c1ae759b8ccab1953358958d4cd9cd799a27078ea8fc317e35538683cec8e6d1aaf5c1361f7f9f3d40a39e4a995da4155ca5fb161d4e92ad1eb91fa498e6b0c797ae859e26b4243e6168cbc7d90bfbca2693351d646fcac0cf86a829a58098fffba26e93d210ffa5469c9e802562755cd9387dc76eed7d618b0391a8b29b01c2dd5e148eff5fffd7f4b7b2c286ac4486e282c5f404be066055696475e3d7e0f38a4b248467405307e9f35abac46650fee8cdcbf0630a2e019bebf6d52fb2ed290d333eb34ec77e00fbb5b54cc710f8c657c45c1c23ba38d5440ba04d01e0fa3fe6ac2cea0af33935de02a5767582489e7dc7bf0aef1fb6200288bb215c508daa4b91a39e7f1495d0a1e31126")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("54bb8dd60674a2c6695831d26607ed575d058578eb3ee1b42b2d1d39e167e210"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8dbc89e97520246de85d9f8b5518d5a8b512e4eaaa45cc8fdfbaa33c4914667a")), secret(23, privateKey: fromHex("f55d12e4f6db4d756f8faffe2382588329568f7cf466a7eeb80a5a6211d3c88f"))], pssSalt: fromHex("03cd61b09be96b8931a58968b204f1fa7afac3eb4c4652a3403964de5c45b6785d6b212c59af2b0013c4b9175fce1086becb307186591ea21a0f1de8e86512cc"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-certificate-expired", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c14208e0785afc37d7af8ec7ac6313f90f8b00570f42496efaf9b4447e8d502e20fdec58ef8c9958e1b0d6fedbe4d6f6c097cac3e0bd918b423c388f8d9791751f0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002087745a5d594312f8d770542bfb339efe0cd15e3b278d42248afaf7c6c0ec6651002b00050403040303"), fromHex("140303000101"), fromHex("17030302017d5b18479ea0bb8f91eea2b225954e41344dbd61d124f1c7d002de5b932ebb307167750608ffdf3cb60bdbb9371e72bb063ed0193c0a3968f773b64af854318c3da86b19ba4b49ce3c14a53567c9741c9790c13a0a2ecfa02854b57762eea9fe6a9b04a88d1b08a17d27db8b779f207cd9f42baffc7c855b8f9fc5169a0234bcb50d5ba851dc6db1c7f266de0dfd01f7d811187dc89a3541aeed2344cef6500863851e3acbe8de7e777e7304acd98075d551de0f9a0bec0b0921b80d4d6e09d020cf3b1b691f303755d6feffa136069778cc0db4a2b032d31fd5de1f3970e4925d5e509f2df28237440478ce79c84b51c7dd8f2965a67ac3101c74075e552cd93657215e0c7223c0041e14e9ca89510422206ff6e36787a5fa06f479db25fcd5968ca913fb0ff7931c7e68053c49ff295d7e017367b952fbdaa651408b7b8bc73f8f166c6f9d5e0a11b7a3f3be670f47c6c18c5fff96d418b078ef9dffe40ce79fd303a2f5e46db226231e94dd11b3b027b3582cb01c9478fc7f8827492a206997aa36300a888985e39c8e44f8d59b31127c2cb19377ee24cabfb9deb64f7d5e78336ffbc7c64fd3c131213c7b1b97faa2fabbe196fd9f4697ad69f6ec283cb70102db00baf2cba081481fa78f5d7ee8677f590d6077116650248fc972c9f89b4720caf4f935f5937a34b74b372dde9b6c7b464743c55279f222cc923032575bd9")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("d8eff535557553203cbc2dc331724e9b7c9ccad3d1b85df69908d82048aa2adb"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b6fe3fda14ec5d89af032c586660ed4a71a31dddde8022b5c2ea2d402d8777ff")), secret(23, privateKey: fromHex("6a475d248167aaaa0d1765ad09cd6fedc07c04a216155858f8572232c7222f50"))], pssSalt: fromHex("14a72d1cc958476c9c0ddf2ca9a5b86ba5c11946e8591ddc717756beea92e604f788e4f0923b15e7ef856b9f2e7a7991c7f3d232418a6a5cb413c7c16f189e31"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-certificate-verify", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203030097fe7baefd1d628f9a245fd341c3792efc950357092d0318c105c4cbf95cef204c6db1bc4f404675d0f694eaa398b35d3f042410146b98a1ba5d33723204d9870012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020d1d8a85a13dc0b46f8b17f484d6d738ccf6519bbd51eaca93043595772adc836002b00050403040303"), fromHex("140303000101"), fromHex("1703030201d1e879228249bcb24094160bbcb9422f1bd5bd48e52d679278df87d4a044faaeb9e43b63406287d25871b428963d137c68fd34a1daec7b65a0b3a6d72c02cd1bbbd81d72367c9a737bc963c61ac110f56dc48e85226a4832ee1577d3f50c5fc24b779a67543b72660310f7e299cb5a5a344fb499114912cd1174853dd19c7673f0db90ef7546c7c4769c05b7d3962ba13752a05b9d0b8cbf9f01e54bf691e1ba3e7d902f1374613a44eefc484a87755d2b24766749277c99ac2d2515fd52d8fb22f9ff46c29b6ca00f2d1b537fdcbdbde27591257555ea0170794d99354bb612802c511129ce2a8d9fb0cac7c0e71c3885c39e2f32de784895b2d61423a596061880328060a6d830c614ef9fac37e228aaee9aae460c9c00796ea03bc0e45441c1bb0bf30278960908d728d9d3750f03094ad6ff66e052f9d7fe3a9263658b87d2de2cdd95e8707e01363bf0928d3d1c6ca66468983325624c696e3483b5e268701acfb54d630cfaed77b36031ab8c41ffba376dc3838a5518d30045fdacec940a1bb2dac9878a637e7ec0e1afc588ecc757014d992831cc47c078d92e39823c732cd8b2cdfa970294ed4096159feca57346c99969df2fe423f75f59e27b2db5583db710ecf9ef3268388bdb3160089baf508d847657d4ce5b71de847612c9f1a797a7a0f4c675b8acbb0328418a144964fc5253e8e334e6ee75eef3cd7b0b22b8")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("45ba2028d7be04f07dc06a1baa572b461415e460fcaf88fca47ae988df4f74e1"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2a033d0859a0c5b6cc351f923dada7f3806026ca1b52205ae7e2288d572bfcf2")), secret(23, privateKey: fromHex("e876cb036b6931f7c094cf9de9550a41f5baeb734a3d9c16135826733e084db0"))], pssSalt: fromHex("6b66d303cc25b35b1d70236f1536e7142d16fae0d43e7550996533ce421a02b15430bac7f10277fdd82e87e08594e5a7d8b25227186a7d9d4d4c8dcb5b6adadf"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("server13-err-client-scheme-not-offered", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303b36da98a9efb37b7252ebb24bc7cd147a438e31c9d2f1e4b95d2161f83e6260f20c4172961163a9cca5fa0592420e9c66ca2d1718f3f664fbd1232daf775c341e80012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ecc9de6d1ddc25261258fa6bb2ebe54b12fefd8f656316d777cd55c3e6ec0712002b00050403040303"), fromHex("140303000101"), fromHex("1703030382476e7dfd379b75111751d9ac5591f44a765f7d2eb40ebd3d64147fcfca602adbc397b93ff9c1e635dcd9da2559b3952ff59c5d3c9c889d14f24246311086e5a686c3c2fdacb473e06b21f2844d74f4c4c7d7d4c2d31eab6d012df7472d100091cb81e9270ef84f13e07e6babc726ee646ebb419d29b353507fde74c971e3553b7c314e5e9a5a8841f475c822568f37c0a61db826f2494805f84183796014dfd501a52d39ed8942970f29bccb8976fb43e10ab553a647dba4e5b320ff357ef72e7fed1c22f1b332ba700f8a774505e9c27acbec594c823249f3289dc684b66375cd675421a450a71ef077acfb05e66da35d72d0e38664ed74642cc76d2ba68aee0e6dd2dceffbd085e9fc310691abe7683e59f929eb3bd610c9d80225c20d299ef82ffc26d1935fc846ba8bbe0b67e27878f142157a9b176fbff4a1f4b9d668461219abe26c26b60b35cf4f3ae8b3a2bc4a980d80623aa384aab6ece24e234499df61bfaa606e56830308c36e0428b88460c061f27edf02bcda0311587230065e20dc8e4de4f1629d85046d7d80eb37d1fb5416f2871676ccf6f7db0375ddde824f92c1f527ae5bda5c9288e0a8dac72681098c240e860ce6d7e1e11341ff90c67ff8d456d46a8dfdb3e6eab7d54b64b4ce599721c7998c5a95d2b35926cbc8345cebd40fe8a4dd8a519fd24cb2ed9d3fe557817981b05ffb8348ade9c11082ff6bd42a8dd77f38183537f00878993deea85e3867401b1ffe0bd6254aa4b1891a3b9a2eaf89b337aad273165c8e3304b56237dfb70d19b175375649f67e30b3f2209d9bfc0d8a6077c684585d23b860b72866988cfa6a9ab6f6ed2a35eb6ba940b633cb22e2267668b1d2f3981a2f09e9a7f2b54683115f92a1c46825e6cede34152c5116cf242fcf4b4de1c05eaa9f00b2bcf575dbb2d26f12ef68aac851480f04ae88e5770f7d03f4845026beb7be28512ed9a711014265ed69de5cf53cbac93ce5844ce8c6163916d6dac5b9972082207d1183b59c7bc433724897304810e242001961ca2deaa41dd2d73d801d1e26eab8b8224ae3a8cbfe0a621c509c1dbf7de650ff7e65708727eabcaf2a63a351d3be70db67c83184206f3407519c52509be5da76a942bd5a6b26fcceb34cc51aa1e1eed4ee41f87135d6460ce33dda7b4c4d1d6e2f7a395e4f59720a2fb3a3774299496b54ae9a1a36d52b4a7b0514d43d36bc2649c78acb3a815902c0a0dc183d86418bd88063174ccf37736145f157557e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
server13-err-certificate-required certificateRequired
server13-err-no-common-client-scheme certificateRequired
server13-err-client-unknown-ca unknownCa
server13-err-client-certificate-purpose badCertificate
server13-err-client-certificate-expired certificateExpired
server13-err-client-certificate-verify decryptError
server13-err-client-scheme-not-offered illegalParameter
```

<!-- test: tls-handshake-server.reference-signatures-and-preferences -->
<!-- tier: stdlib -->
Handshakes made with the canra reference client and checked by the canra reference server, for the server's choices the canra vectors do not reach: RSA-PSS with SHA-384 and SHA-512 (the salt is the first digest-length bytes of the configured salt), the first of the server's schemes the client offered and the key fits, the server's own suite order, a sent share preferred over a retry that its own group order would prefer, the server's own ALPN order, and no ALPN when either side has none.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [2053], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], key: TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("790c1897413d68d97883f6f586bea8f00369e060d461108e5a6508c2c7958d7b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5ad123509ea755ea0b19e4ace5dc50488811c0e4a36f48ec96feecf559b848e7")), secret(23, privateKey: fromHex("bf16e109881fc5ce6f1538f6405f224194ff4553db5378f24763bda6fd70fe78"))], pssSalt: fromHex("cfc70e7e7b96725da77ebb6cb025ab005d2b172afef425e8dfbfbd263f1883606edb6310e3e5730ed2ee795c9e22d6fff0b00f3aa105a24372e2eb7dccd1b12c"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-rsa-pss-sha384", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303105a0e0470a8b03e4d163315faac4d626ea46fa21be64928d5d28ada57ce9a70207fa5e6882eb52d532ad7391177653948a4e628046e55d91b7e5e58deec0f37ce0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020712962e0c5edac6ac1d6404150e8cee03dc9f82a80aae98d86b5a917ea41257f002b00050403040303"), fromHex("140303000101"), fromHex("1703030035469fdd5ec6ea97f80749b935897ea8418b3614b17a764d9f772b1274b77ce945142cf68a8431234a06f3ca503ddeadcb89b59393ff"), fromHex("1703030036948f690b45ea220bf0c7a36189c41b81161d86c65af60ba3a64f3e319b38eae79501c918afd83b308e4543da0be5e6b0e81eb4444288"), fromHex("17030300133424d4f35bc5824a734340d1d5ade725c13f2d")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [2054], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], key: TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c9aabbe86593f9b22ae823b98ccfd80a3c25a424afa47eba901a58e9ff4c92da"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("1a2712e493a32c5c55124ecca6632f710a7f9fd99885f6d8055d982250aec7c4")), secret(23, privateKey: fromHex("fdf510efab5d4b71e1165b81ea7d0149fa82b29d0b1905ead104be0c8d591fed"))], pssSalt: fromHex("95b38bdb3386414e6f0d2cd4e2bb4210981483178a2d4b6a5f7f5ce702692c159ba53eb6c5dce734c53ef49ff8bd4230a7471561fbeb14e7f189c2fb4c0e2d8a"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-rsa-pss-sha512", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203033725cde6522897d8e893b908036615919373df1aa8786a4d451feac6cfb06e7a2081894510f90371c33a148760bf4c09e4247118dcc7831210459fb1f8ced9f2410012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f563117d881e3fb406f07ea1ba88a438570b9e5383fe3831ab2ca6f35118ae58002b00050403040303"), fromHex("140303000101"), fromHex("170303003533bdda351f71b3bad816ed220c9b00407de5b5f5e6d7a28e1b7eab77f2e57e8a71b16e5ddd8180321aba3e0142ea38deb7a8b17b8c"), fromHex("1703030036b4b6427f3c9d2641b19c4c13c8a7769deb5363fd72e9876024b9a6225d6a5c73e77a5e2471b8230dd820ef129324b90a0e6ec77d28b0"), fromHex("1703030013a9e9afa7b1d715089819a305ee218e8701fbab")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1025, 2055, 2053, 2052], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], key: TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("28c47208ec72dcc2c187e881d03c66cc181e67ec3eb002cb0a91e2f0fb7a3e2f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("1e84488d31fc249e73da0441a0a5dfb6a9e9835c2bf3225f23a5cdef162b2bd7")), secret(23, privateKey: fromHex("a2836e69762ec13ea7fbbb9a21e54116142def1f9e90944a15a0ba563daf49da"))], pssSalt: fromHex("fd822c51780a151d7e1532832b46e0061bcf8e549bd1e6e12707059cbfeb4d90090947f10c3cbb99181f61ce65283b82d31a9d9b8058d7a1e7aaca98f0ba2b81"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-the-first-scheme-the-client-offered-and-the-key-fits", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d8010000d403032b8080aa0d7236353224211c13ea150fd5b807cbebf8bed8ff27c62cf0ebef4d20f6a19763e7d8e53470ea4d9ed2f1432c04f9f07dc01f94a8afd86c9fd7be89a90012130313011302cca9cca8c02bc02fc02cc0300100007900000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00060004080408050010000b000908687474702f312e31003300260024001d00203b654923653e80f12d2d4f167a049371b9585fa0eb5030b0462ea28e72df0915002b00050403040303"), fromHex("140303000101"), fromHex("1703030035f7201f53068d6d1b1ed1cc296a176988127acd04446ea446262cce11ca3726d07d2fb91cd586751e5c3786475789fe626c2528e813"), fromHex("17030300365300841e873b6a38970301e1b4c6ca0841c4ecfdf82de96d3e2e39e26416f25c94de55dea079ffa7463a3ef0387ccbe27c14b8d7958f"), fromHex("17030300135a3fd29f39fa48fd8535e68caebd7626d32165")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866, 4867], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("e628eea6a9e1c47b8fd210a00019627ca1542acb7dd82c583f0d6fa442fd0547"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fd0257fe2b324e9e61ff04cbf07754adb63ace263659d385b76751ee7a469157")), secret(23, privateKey: fromHex("3607c695b607a1e33364ba9970a2f06009a1a69225a349d49ecdc82ad7fdbefb"))], pssSalt: fromHex("02b1480ccad80fa078381b9d5a638a879dd74ad104ffd000ee323deb97bd1da1f5ffc654977e04e300bf552177ac4fe35cd4b75429fb7b9101ab092b4647d6a3"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-server-prefers-its-own-suite", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303cfc183a94ba68a13eefc73a6c167f4980485b0ded30a41564589d0c5c6a1be5c20eff5006f673b3df453b85d4b45154f647fa43dd8597de46cac7eaedc043f72290012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020575b8dd14cf5147ee447a1144bc749c062e4519e34142aa338bcbe8918a92209002b00050403040303"), fromHex("140303000101"), fromHex("170303004532fc92bb2700d3f08a8d62d32623f3dfc9e226e539ae3db4eb12be7ef12304a602c69f551a28bd8fd937f434f258ffa233ea8ccf63cdd84b7e2ed897154860aa6e904dc15e"), fromHex("1703030036244f84698b104bcf97828630434d2a296dcd749efbbd025cbd5b844b9d93cf42039dac51bccc390937c60e58c5e98a7de54a7795498a"), fromHex("17030300137ab4e7232dcf46997be8289ff5aafe9c02caa6")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23, 29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("418492c62086689d902a26a411b37cc0b9698ad02d4221bef717acd09b12b27a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8a9e15d79158cb481f11fb5e1ad1cab08071c2ab5be5f590e28ad59976b3eb8c")), secret(23, privateKey: fromHex("db87423cac3c6d945aee58ebb0c1fc0a871dea872830e757387d7afdf6358151"))], pssSalt: fromHex("1b848f7a764822b91333f1ed0354419f3e4fcb37d34b3bd1ff4f45273f667d83894ccea19149dc280a57b33b42743acf8647ac88e46ac54ebe2a0aac07943e89"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-a-sent-share-is-preferred-over-a-retry", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c14846b02d0af77e6e6c5b47a741593c9893a268fbce1870a6cade836b554c5b20e0f01f06a4c0cac92f46bb44f813c409f5e18f588401deeb3565d655d2d897b90012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f0c1017ca51495cbf3f9a2824756fa5e70cd6a098d628e5d9899f83c31f4f458002b00050403040303"), fromHex("140303000101"), fromHex("170303003558fb1e2afab29a47844ca6f01217d24b501f74fb4d76e4981c96677312457423a97b7b91ec7868fd4ea9369f4bc90a447ea78d1b6c"), fromHex("1703030036b9d7e8286c2e39abb0f64533df9136b9fd5cb0b5b3fe8897ec85047dd47ed953560c1f5e88b87c31e250fcea3c8910df296ee94a6007"), fromHex("170303001385ad9b4832d04e029a918351db0f92927df007")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("6832"), fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("852abf6e5b89c358e8e05c651b1a54f6832e82f85641df87da5138f4d31fa6af"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("1d793c43109396a7bc0e8fb82d690ec1f924c473f0709138e3e70bf6987f0f3b")), secret(23, privateKey: fromHex("95f23904e5bb5fded72a3bd5326ca560117f699e85cfd463fd4c0f4a090ef8ac"))], pssSalt: fromHex("5cdcd1a153d00df14df6034e63e5236c5c5fa506cb3a0c49c288ab9ba2b06a6052709acc86289a55c9bf05f8d6f4eee7d63df5e4d74c8d3fd8c7908a261c97ca"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-server-prefers-its-own-protocol", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e9010000e50303da4bb8b2c7151282701007fc6a6edf56fe197afaffc7586c48986a2a82d0d00b20a6750db4e8c810005c5f7f58b1a0258aa0de862a7f8aa12bfb1c02cd87b6df590012130313011302cca9cca8c02bc02fc02cc0300100008a00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000e000c08687474702f312e31026832003300260024001d002002377bef3f524a83285c0056002095efb7b3935b75fb48dd251f972966bc481c002b00050403040303"), fromHex("140303000101"), fromHex("17030300356c08f56cddd84c6a50683670afb021a40a41a0afa7145bc12c3fc1137980f4fe9985d858fd503d9582aa5f47d9cd3f49cf2eb7b6c3"), fromHex("17030300366ad809656705039a4a6d7fdcad1a47d5ca54ee6a2130b7e86c4f32f84043688fcc780c34981f5e91652d054ada2b6706c2d6cb780b60"), fromHex("1703030013334861e88b768f22bf8e3a76081685806691d7")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("64efa8afbf6d6bbfdd34ce3819fa5769dea2f4d06f0c31d33be85bc3aa5c65ee"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d5a5d7be7c25b712fc9579ac47097093e9362899d7862257b63cc6c7770c8df9")), secret(23, privateKey: fromHex("b3c24e02ee574baadaf4e656ef492094089dd3ce60b3f74bfbc7eec9380bbc43"))], pssSalt: fromHex("3937ca346d7998a05928d2d6b46d20115f298a4db7c3d9377548f59e8cfc91180637bcaa8cecc2e15addee315ec7d500ec0b00c93f8a43a3918dd620616fdca6"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-no-alpn-when-the-client-offers-none", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d7010000d30303616dd1dfead0295de73a0b84b0f94c660d2d264856cf2e6d69c7b0c2b83dbd6e2047ad309e9aee78a8b083bc1e742150a2e62e471cc22012b322cc720823abbbe50012130313011302cca9cca8c02bc02fc02cc0300100007800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00140012040308040807050308050806040105010601003300260024001d00206d7f9f7180457becfc06c93d1cab807bb3f0e54c18606a63658e01407122c20f002b00050403040303"), fromHex("140303000101"), fromHex("17030300353dee7ef63b0eadd5f6b76dd766f3fe8a6c520f2e865c22823a6ac0f87d9d507dda4956f052b33a67c7813169876e611558ca91d799"), fromHex("1703030036aaca249c77ef188835417e64b22f2e3f00c3771628df124f8c1490f656e9c70a56cad5d4105fda43badbfce7fad8372053a9fdb21dcd"), fromHex("170303001378882c9e3d0c2627c3c1fd741dee5d07620e55")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray.create(), chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("95e7ab8f36b1175e024985d0f644391b05747776dcfed77b70611a39c05535b7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5be7d766e62759049057c633d8880b92f92f7b44159a4baddcae2483d49c5561")), secret(23, privateKey: fromHex("ab397a2741fc2dbf7e3bf2a453fd0243c2d942ebcca55851748f01f2346f4323"))], pssSalt: fromHex("8faa22c120eb75d09d3e360312f04a0f0945c0ada06a7c25c0ab0116e5269cf14eaa36c5ea080b04b8f8697a7023aa20c66b3c2d238c6c71e1b24771b49ba5a0"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-no-alpn-when-the-server-has-none", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303529696621bcf26132294d3889f38c5855e3471cc08e20b114e362ee9e2fd3ce620204c9a6b7802c2797701fc9cfb2fb34a5dd8de4fd01ae2285955e28ea6a838370012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00207722084af3c77bf37faf863601ceaeed2301e8b13de0fd465cac6a6d68e51f21002b00050403040303"), fromHex("140303000101"), fromHex("17030300356e8fe3f715e7d79f1085fa67759798a3b82e0c20459399c28c7fb4545fdb8ac1b60b23baea6e519b918275c042f743eaf9290cf7da"), fromHex("1703030036ead1271274b4ac382ec79c107cbd613d01adeccaae619a52a196ddc762fc4113bb53e5b1dc50eb9ce710797c51952adf378bfbf06589"), fromHex("1703030013e69475855d6f11061bf66fb76da060b50820f6")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector7'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	vector7()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-rsa-pss-sha384 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303790c1897413d68d97883f6f586bea8f00369e060d461108e5a6508c2c7958d7b207fa5e6882eb52d532ad7391177653948a4e628046e55d91b7e5e58deec0f37ce130300002e00330024001d0020e6b1cd911d40dfe3d0df04be2923610a010325449c41ed6a9075c0c918c9195c002b00020304","140303000101","170303038c97a252a544b50e78ed3f19a87eb81e79eb7d79a0f974afe9238f17b96fbd205b40fcaa9e7607fe7046baa89fe9639ebc3ac22e6a9e3d7b27ad46ad145efd198a68ebaf8584bfeef68b3f57113d6a152608e0b79fc3e1fa6e4f2886e446b39a29b164794d0a6a29dad9a88f9df0ce51f303e3f7ead75dad7ebe33680dbcf58b1dfaee6095dd8f5589b2c0c27e4007dc7ef5b9a5c2884eebd931ad772b8904a6df80d684739d43fab319cf1f4479f4264fae621d22ade5037fd9f3e2333641cc80250105a6a5e2c2fab0c4743a8d95e152986d98a920069eac223bc02a4c32371987aa3d5f8e98e33b5a3a3c6a24312df1a2c15d1f701e0ae24de6fe6c5cbdfa9214b1a8f3bf320df84b63f3bd5bfd5cd735ab1036c3eb334bf50a939f178258a975b7adf6a9c5edfd02b6d532fb6e43c89ac259625115ae8231e8769c187b601f77243377a86e9566789420b5bd426b080d480dcf800d9d4fe200010c1782f3bad44e61468a7a5ff13d21292360bd51a881dea6d043db4998408cbde0623721020f754761b6c1612aafe4744ae2e3a3e1720c6b50262bd427314513748170fb34066401b05e1d10ece1aaddb81f204d98f3546d9844191b1e5eb36adad3e255e040b9729efef8371f1824f679ce9afb9e3617398d0c1a9d2e07d756ffa976350ccada44c8104adefec920f1082cf12d593e89e3683e1ea57e567701605ec14d3aa0d5e8eed62de3eb762042ba235fc1e9d862be0a313784bd34f59a112c4e8e4320e2b67c8ae81d3050359f82537ba33a14671eb591de8837f56d32c149ee6436ced3d186f3e8defc4f0364c70216e112bad5d32ad78b53a1a8e32192607806e2ec764ffd07341ceca5e331b0d01c028115b61f04971e9ee31cef55f8069169d4685ebe1e977bd82aa560532e04b52d13d2087691717abcb54f4aeff6f1860d80c584117f7b5cdca899263566b76ebfcee0d2d3b42636e5cde93394203a258a143ebef7e0e148b8d5ef93799fa60782694be9ec4a54ae7fb16928323c550a1ad68177e02fcce3a7b6b6e9d6e8cf245be2741fe5581c3aaf719c5ac75633619ce83a67a79fc689a54c54930f43c72c2ca8f1a685b9d792282d5ac080444626b1014f8bdbf7ecde050d68fbb525e6f390a3e40967c75567d2dc625c9fd3fd2f11e4ea587f7d01656d107b7d0ae4a34e4749d5945638bbcc01061bb85677df8e58be826192667f2ad0ef6913feca33b82ded64a5d21a53822b7b5e77c8ae1261cb6e3dec89a6ec3f0c2dd9665085","17030300390b41ad3777719e98e43da6c53980b3e23b43755b6cc29de637dfbfdf6b40bf2bc200043d19896e002d8b288bbf21ffc78a52b0ae090c33a447","1703030013d655de5a7bfa0489c9b12e002987219ab83455"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-rsa-pss-sha512 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303c9aabbe86593f9b22ae823b98ccfd80a3c25a424afa47eba901a58e9ff4c92da2081894510f90371c33a148760bf4c09e4247118dcc7831210459fb1f8ced9f241130300002e00330024001d00205a0367b7c526b6397d2b3a8c7d01d4a9734c6878ba6d9c239aed0d936f88a806002b00020304","140303000101","170303038ce1446568084ea6bce8fd26249d7c16686c2dae133e1a3610e66bcda2b7c22a4bd44d4bfaf1b9fb91db9fac1f5e659fe3363062beb45b360ab8e9939292003928dc7ba6dba20f1adb31c1455defca25f8a46fc662594965a982a58a7c5fdca878f070de42d0df9ba4192fc4af969615416e9d76a6394dadf8cbe4e7309ea1cfe1dad0a37f9c19c655ae98039d4e77660f13dd38ceb440299bd65f8763ecb05a73eed1532909eb776c7c2d37b1fecbd83a26d859e47737141a1aebeaf69294547274b174090a8a8000931af4f6edf60d6770fff51931f7cb69028a228789439f8ea5aba91265da390cdcfd123800bb80d16fa3cd0cbf70a6a6c964747bd70161fff64b0038f1763862ded40c595fd2452f495d2c4c7da67dece2c9c640892f3cbbeb1ba2f908936ccda3c3b867faada73e271470873e885e746b561761567f820312bf8bb45176669ca1e4d96fa66553f0b898fa4357212b091f5d26abb97ba9b78e8b4bd47fd296a0acc63ed57a0f97895fb0cbb1c94dabbba13a352411ca9d7408014a0d0ee7e8a45c34324775fcf0c58e209f437982e5f7f4dfd892f23354ac34ce0b1735b2f0858588f2b8e099291b88ea8784f14daf7127e9ae6ccb6b6e629699a62a22554130ee689905b519e9847daa8e96302075984dfc73115a0e463fbac30ec1fd89abc0f3ebbbd70ff50e151072ec3fe2a9bb244b2efb662e10928cb7dc44ca26149a6c74fd1ca6759a561b2f860bcdc08da24e371d4becb7cf07ebecc8eaaf5022193eb85858e6f903b7d1aabee24791b139ba0b93983b579ae56a96cff5e3388412c5895aac9ad86234dbb0d558780031fd5a0d0685d7489a5196cfb1b2d9c8ff15291b8b6f4103bd9c80bd47b7e21b78f39d358fa5692a5e752b19eedada2dbbc84bc9e37f4a030bdeb24f70ae99210e88b785e5c724b3c59faec06f98486e3be1b677c3b92bb9a75cf3543d83b01fd385de019fa327a177a3b0245d711c305ed1cee73b95894b70b5127a4e4e6c149e5efef202d7359a7d5a86569ca1b096f72c3f847cde973c780ef389af6031fb5caea2b00d255c65633a0823ab6ccc6da702cb286daf8a91d6cfc4ab81ddae2f474c791f8891d1f6dee10981973e831a112aae36564db9badc19f74cdf73676f80c5b9136167cba002a9abc4cd3f5cf9fef9bf8db29987a1642926f3b78e75c21a93c71ae371334eaad4d46c20a2d6ca2e8db1dcd84bd5e334ab1d9ec20aa13300de69649f964bdb6355f8b2a8bbc4c00d7af6b44d3992","17030300393d510e775c6dabdfadf16a72937ae758650686c27aeb0e91157ae7e3ea6245e3bc88b4634cb549599d539ac60ef7b89c1f16553dabde817b52","17030300133d011c2437a102ac87b52c44b8798602a94c14"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-the-first-scheme-the-client-offered-and-the-key-fits {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030328c47208ec72dcc2c187e881d03c66cc181e67ec3eb002cb0a91e2f0fb7a3e2f20f6a19763e7d8e53470ea4d9ed2f1432c04f9f07dc01f94a8afd86c9fd7be89a9130300002e00330024001d0020b2fac6b8cae54277b78857b7ea922feba0f753054efcec3963887b302859536a002b00020304","140303000101","170303038cb4e9af866ad8eb6c156601f514b9ee6f8b131879520b0a081afa75bf10cfe14a315c2b222c4820d6b553d27c6f25d974c1cc89a0040508ac3f7dc453a29ff2aa076a092a65bc0b52823cba453d07fe9028e67ea3bcab90b74534dfadc2f7b87b7af1650678532ded26e06900e0a3a892b9761f4d32990ccbe0a23876af342893be905c485816e915cae78944deda2853dbd2ad82cfb76b0aa29427b96717e8ab2e5f6cf322e33a16878c72410574fdbddf98de68206cc6caed7706723a0d87422add789dc5db5b9efffd8728f87289aab37326a266ffdc190104e8aa1c520b053c435db7a7496b57b9a4ed3d8ad89742f94803e82b475e8ea7739df0f2a62cb3e813c9051981a9235bc1cd219a7931571811b2f6f82f726d9a99335aa96426d8b50b7e63892db17b7270e11640bd54422c1e1442bce614bd29aee1872019fd4c77745f3152e464b7e53eb7198515aeb0c4c773a8b0f584bd8f3faa779f6d66ddecdc5b3aeceb889e9de54a0dd64b1aa01eca83351233821d9f9d63d815ab31efd971f99e35231680781bbcde360e35afcdd7a20fcdcff90010811206196bb68b484bb206f4385839ed36ae5eda6a9ced2b00ed1af04b88d3ea9fc10f69933670ddea8e8c4d8ffc30086ddd7d283253d93f4bc238a251f8fb4b5047c17a9555196bab962178c97589b9e2933ae00668eb1a309ecf20b559ed84ec8913bb5541556556d62ae144bbfc508873dbfcf474a5723b3f54a8ce5b00cdc32b02d3f6f99ab0d26c3f54b03c128ebabcf13a8d1cf6838686fd0efc9733c59ce28b5ca27ef6d6504fb3faccff1a6faee256b5badc50c13470f762ee058c430706cedf693e89b4b4533ce06a7c41704c5311e91727b56eacc4130125fca8a7813e93748d8288b08b1688503229ba231c22d9ec43697e4478428d81c854feb5c2e74e902ad0da75dc8420afb072690a2ae7d2e7a35beacebe50bdefbdb2b218e85df4a589fb252805c5bf40f6d6cf5760abfb17bcf2194d77b47e3a68e376d4eb3012830b5f3bef8306478a9dc54fceaa19f36bcec050bc264a549fceb25f99655e3377f7c88a058925f22df1425afc7b2ad1975f144663a827e62f65b7c5400b3b3d21a3affe148b216eda9d27521ac407976a234168cd50becf3dbe680dad1a3dabca617d62b132b8ed1940d18813c871474e0d9efae93a55d62f57017165c11fbfe3c4cefd9c1dbd34f94f346d8cfb2e23049c4ca5a31761fcf499b56590cdeaf2686ac223ed5590ed5546f605a6829cb1","170303003920c5233c418627c2f855812a4b46c76d60e8e9e4df57beb93a229c7cb9a5e3443046ac716f6f27a7053e4f4e238b1e1668887ff890037367b8","17030300139a1b2a666b09788f6d966309ce5557d1c9e114"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-server-prefers-its-own-suite {"state":"connected","version":772,"cipher_suite":4866,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303e628eea6a9e1c47b8fd210a00019627ca1542acb7dd82c583f0d6fa442fd054720eff5006f673b3df453b85d4b45154f647fa43dd8597de46cac7eaedc043f7229130200002e00330024001d0020a20365489b11a46c91327b7c39b7d2f1d3c3bc44b29a8961f68532e8b37d9449002b00020304","140303000101","170303021679e91d221e5b4eeb0c9e4d03b018d4005ed840ffdccd3fcc3c97ac8eba1484bc5af08e66ef1015aaa6287a6fa01fc49ba6e8d61f995518267a81087ae1691319b1f006e9bc5fcaef8ab0f225b0ea279d59a277326f9fbf18722b680be5584f2c50b7c84d7c69782a05626dda91e5b538436476cb37477f1c1b544456edf1c90397118354929b755bc1f7fbc0794b10f6730eee1d55fd88f6cbca7d0bcc0f6059c5c17a299aaae382923481bff9c24564028bd670fd7a3a102b26909a3c72edb0d8192f84042a46e7d7a68e562a451773ac8ed5d2f0ff729cfb9bc48342fc496b888e64a647a7f4657a7cb5ea35af9c50d844e2a88ea0a78cc663382b59474f61651aeb06128f89d1e3d7b24a2783ad06a41c80b0ec79889ed4060550acaf151ea2299b3f2f58ade7dcc32df3eb9fdb9ebb5e7b312f138a311e552c8ece0cd833082dae5d664036359bc34ea60b6b99e2f62e3a6df3180488c7f5e7619df17f686f901397a861f0c0d99f8e1594af0e94cf75394d3f2957b2f58112a82fcd0c80fb6eb3fe02944b8412c5a4b20d68a2715e4c734df0961509677796d86237d3c78c6a0f96538fd958541a22398ac9f896f812073b700eb33730298d3501e2b7f1bea513f516a51ba8852d5265f4d8173271f1180b95ffc33ef9da23885d53c82ce47a6af4d92b72035849a025e2ab69b39b11f955150d47f56bebac0f49822eaf5f45a368ba54736d3b64dcc5bb781059edf9a9441ef0","1703030039bf2c3d78bf03ef09877bab747c414b973f20397724ad51bd186ca74542be939a638d5132e2ef6ba58d80cc01ebcab356b6ae78bbe72ad14eb9","170303001398e5cec1cbd17175c037c204ccb22a15180fe9"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-a-sent-share-is-preferred-over-a-retry {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303418492c62086689d902a26a411b37cc0b9698ad02d4221bef717acd09b12b27a20e0f01f06a4c0cac92f46bb44f813c409f5e18f588401deeb3565d655d2d897b9130300002e00330024001d00204a2cbc40e4c11e7176ca93bfeca049b0aa8647726b24323ad2dc2f8e6ae53d36002b00020304","140303000101","17030302066d976360bc27bfc84c167c653f4e22b8f50babbfb4eefce46ae983889aad02f6b2b5f3ff42207ec428577223e7eff7fac4d87c029db3a07480939bbdc7e7b83c84f85b2206995304af8f7165b6b36354372cd992349006b3dd2fcb2e7289006fe7abe5fbc273763de49604db270ea8fdf9ac53b6e38aa56748b9178ef5254b9cc983ec2b72095087882ba5eb8543ff3fd18311e8ad6164e19b318e4301f5d50d889546f5137dc16679059eb4401ee622437531393566bc45b568cf001848cdfbb6ac0b32c8d4fd7316078c981534f747af7441c90a4268b1f8b65f4f6357f461638105fd392a877b49b0ba00457e68bb54b7e3896d40f62cde6f60e8a1daa47379f08574053717596742e3dfdab45a4264e6e9f2cbeb245f3d6a45fb9bcb8eb730b00d77e0d803e435e24276621bdce26d45552e6b197669cd7c986495b47db7b3a10faf5a11cfe97124c9fe06966e0fa487e6e311ff2db14b70d741db67f1484dc20cd3530429cc283dff3ceda7561d5e4f6434379f4642fcb3560ae80a5dbf164df79f1986b8f95673d9a98e750741594291338b1881c101d99a42afae7ae662af8a075e7acaffe91e7fb9622a720c71adb0eae10359404d38a5233166a31b225bcff33d2c866180e25fa43d9a367563288a0597e013eb1df445f44a43f6f56c27224c3935576547e53a3d21f697d15a2304319f26968b037baa832fd6216088b4a2d8a14e","17030300396988f43b6da5ed56353599821d4f99629c913ea9dc2ea2fb77f86c07638f670f6a62f26e31cfc155237cd412fb87fa9fa3f851f0a3a51db8d7","1703030013e312dde73afd7954b8bf0303f7dd297e91ea0f"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-server-prefers-its-own-protocol {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"6832","outbound_records_hex":["160303007a020000760303852abf6e5b89c358e8e05c651b1a54f6832e82f85641df87da5138f4d31fa6af20a6750db4e8c810005c5f7f58b1a0258aa0de862a7f8aa12bfb1c02cd87b6df59130300002e00330024001d002002f61b7f10b91b206debf0d908f2cf9a7b6d1a1c328f1b18bcd3e90de2fdee70002b00020304","140303000101","1703030200d67c3de9db557bcda0aa4e94b99a4a14b49804e7c46af0977d98c8887961445acf9175beecf3938237f1d24e4facc099824431d0679f834f71ccda4670ff73875c481eb0a8857eaf0f6d7f64282e5f6d9f91ed60c75d0108fb3a19f9433ae3697e847c10ea3a5cbf501864dd3b80130c72c65407a148f16da2ad61ff1aa0e9c39fe47ee4b316d7077fe5055c118c1275f932a19982d60943afba41a2cf07159b3f742ed36751382657b1cce7a14c94b2b8b20344c9d965d8cd4e1ec28478699e620215e749f6382f9fc14aeda8fef49ad219f8bf70980755b17c14aa0d64879e0d563053c1c967b436eb51152cae0d5658b5dd6ac20f8b20ecc25574293ed3460c2a9184afa56d4a57757cd7a4a7655b4a4c7d0aac3dd92f12431bf1ac76f691b05e4942a4ece8a6f7e63ed1fdb5f45eaddaef6dbdfe13f0a2ce45061a83fe775b30be976c835612fa20f4d1d1ab4c3bfd9695b66c6a3df6b97204aeb61d99a32e8c39e155c22ddd51801193fb54dd598ea291f7a98fbb3bba903cc30489f9540ed90e094952d7a8dc8bf0797861d55463e249428b1cddeb6a6f111ee9aaf5f76246dcefd5647572da58ddfd8f64b89cb2c93905f3a812cbf0b5db73d23ec23c7895e54192b35292c9989bbff0e49dc83371087082db72d528e01d1eef3f4d5870103a2e0995d2784bfcd1635e09455bd402f300f6cfeff3733844b97988d4ee","170303003960eaf5ae99552ad60459d22fff4ad617f3a3ab58d0a1d5e9dd38d550c7d524bcaac33e958b336034b761dc7295acd6095e1c79a660277d57eb","1703030013f2719a0ec97df759d36e657cb77d7f2674b973"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-no-alpn-when-the-client-offers-none {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":null,"outbound_records_hex":["160303007a02000076030364efa8afbf6d6bbfdd34ce3819fa5769dea2f4d06f0c31d33be85bc3aa5c65ee2047ad309e9aee78a8b083bc1e742150a2e62e471cc22012b322cc720823abbbe5130300002e00330024001d0020f83020f157c55e9c81bc57759361c8d25fd20ada5870805e643b64d03f46b825002b00020304","140303000101","17030301f7336499ded679dc6e420fac6732d8f97f38a50f7845ae960d81ff8c0ace753e80efcf0180245e31e31833c628c1562f184dad86ef3b56b301a96eda12a8bd1af90595acccbd7ac9cfc4b5fbd44fd8b0fd7ab3c216530119970cad8da2268d078bf996e782d635c47c2c9ab28992ad6ad993660cd8489babb9c910648656ed880a78d0b1cff3b34fcf213fb28e9d3a9660c53ede7ae3f28c3f96a1bf125b155c2e5d8c31b6ffdfe7b1d1b3a7496cc5e3fc71be76698d494364e7b4ecbbe09a1b756ddab8d75f64f0e96d65944b5fe64faf793a9e2e3bf56f6748455c25c4ddbcb0b94662b06af52d3a0711595409c09b29840dd8e3514da95f79fd7fd36808d8211e70d6a31194635791806ca0af138aeef2dcfd6e6170b12b5f3bdaa00fea78ad30fa0aa71137abe991cdb62349dd0bd2d5aac903cb4db80c49f146e7c4587add1b8ff77f07c5f367e77b977d57937fc5e48a41c0a820385ef8c496cf7ac7c379d692600ae1d7aeca8ad6e355195936fa0fb888e4f78ec857cfe7b24c989447b37fbdf2c468b6ca7e5c9fbdc354fe2afc21d388bd9a64f4b26bf2303cdcf4525876304753d89c16b46db0943810f8c7b30c60cbf843aa3919329898e7b5f1f18a42816b036e33d8e1455c225c06541295fe7e22e9bb4076d6c8df47882ab5d0dac714a393862c29a214f58abfe03cc7e6e1ec4cca479c7c","1703030039bc11df029e793f53239857516a2be6f7ea8b2f1c3a71a81e2ce5800aecabb39ea7473b8328686034af45e94909ffbd8754b5e38fc441a5b80b","1703030013cc073695779dd4087fa3844b0794f894ba0117"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-no-alpn-when-the-server-has-none {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":null,"outbound_records_hex":["160303007a02000076030395e7ab8f36b1175e024985d0f644391b05747776dcfed77b70611a39c05535b720204c9a6b7802c2797701fc9cfb2fb34a5dd8de4fd01ae2285955e28ea6a83837130300002e00330024001d0020f4f2e5219dd02cfcb578127716006691768dd7d062739dadd2c7e5c0e2e0b065002b00020304","140303000101","17030301f9713ba61c6c9e3a146d166e5b98624603c03d1faa60816ac363f95326d9f7e774b4990b6b3e12702883986954c194a97896b7de1e26d735299a0ed8bc42dfe49e55d6877602f524a87109ed6255506fb74a290affc2f8b299a0b8897897ad5d53309043da1366bddfad414f783456891a75aa8af7ad82860c54360576707b884ab3bf7d4fbbfd0f4418714fa218ff6a9af21e17c9ba3254167cd9e671897fa40f101d4a35d197089cdfc6db282cf3b03ccc7052288a28747bc1aa98bd165a23f30dfb253106e7244e498b030db020c91fadd8dc6b69d4a16551d15290148d41cc2d55a49810ef2971d97c6c818e47409e3bec66dc8c91a8fce203ca0baf02f98c6a19ef1d5a3d24e54ccc8f55063a1f2fa11e614321d38bcaf6bcef46cdffcf6d6e2d7ffba490953f842d49e0b738a99f0021271294ba73632a738b62773f8d2416b5f0e9c8e3f9eb6f323b5f34d40de92e67f7e290139880684f801e2e934bdbddea80ce91808327c4b1b18457ba29c10e899b51289eb905b7a6e040e8ea0a5d6ddd300d46834e82c4518ff6748249cceb86ed833a7f4c7f86f297663e745ca7b0d7bf3493ab9dfc95fea1247c026a5c70e16f2ca0e325adbbc80c4f9303c53ef1fb61c180cc9dcd54756f217c2b1c3951cfa2ab4c42469fe22998a7fa9c173247310aa50b83e5e047b344d4f30039e0a8fb46c59df5a504bf","17030300394ad65cc057d30cd3f3ad4aba53c261c64b1c8279e2e23066a570d81e3833ac1138119a3ab8c5b081d737fa516eb1c4de513f2099d40816a1ba","17030300136e7c3b879dbba92fa919f71e957389a152b1cd"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
```

<!-- test: tls-handshake-server.reference-hello-and-record-shapes -->
<!-- tier: stdlib -->
More handshakes made with the canra reference client: a HelloRetryRequest with no compatibility mode (no change_cipher_spec at all), a KeyUpdate that asks for no reply, an ignored user_canceled alert, records after close_notify (ignored unread), the server's application data written in fragments of 16384 bytes with an empty write between, a certificate chain long enough to split the server flight over records, and a ClientHello split over two records.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("a77cc085f587cf8c4f0a7786bb478920732b4ca5e4d8d03bd97b9cb75fe8b3db"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5fc9f91551c568d8bd57836b231e4f0b5fab5de60c07bc6052836a0e02c365b2")), secret(23, privateKey: fromHex("9d734c119faa9ce46a7ae119edea45373768a0b1e01a3d9c8f4b4dd1656914b2"))], pssSalt: fromHex("c9952d798afed22c3bd0ea0717b9dc91f798dfc6047de3d5917fd2452b3b5bb48b96dc7da16a1e8015fd62792ef25582d1f4a984c88c1b9f689c277bed1311bb"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-hello-retry-request-without-compatibility-mode", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100c6010000c20303c5d8934b98a4e70001de0a94f0ac953ad0fd00b0af3838b64a1a30d2e810fa0d000012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020a1a7d7a61c3ca1cebe23dfafe3c38de393ca47a3bd019085380bb66decd3e136002b00050403040303"), fromHex("16030300e7010000e30303c5d8934b98a4e70001de0a94f0ac953ad0fd00b0af3838b64a1a30d2e810fa0d000012130313011302cca9cca8c02bc02fc02cc030010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104876d45631057dd76d82b5be0ae0694dfc5038b221df771eae1711944da0e9eba938625b723405535048b7dac6ad03d39820956d8de30d4de774d38021965c3cd002b00050403040303"), fromHex("1703030035ea4167d47cb59c328d26f43aa54007d82e1ce2e7b198828fd2066807d5097bb529c955f5ac54c000ac7913106885728ab128b0968c"), fromHex("1703030036b848174d2bcdbfc0b903a7e0613df788257f75ee382958e5854c6388c397454918dcbd25e4081b8a7cecc8ea2ea3674ba86f9487b727"), fromHex("1703030013b0fa3a61a3c9dd70dc30416fa7133698ad0613")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("a66ec8d970021c6b01943a176131ead2d587b16d8fd8fa987bcfde00ea126dc8"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("15b94199c3797e7a81228f2656accdd0d4b7b1480b90d02e31190ea15261f9a8")), secret(23, privateKey: fromHex("b0e5fa9a696b52d16b7e893308696b9813e78b36f77685f3b3b20ff0665c228c"))], pssSalt: fromHex("9246ca60cca67951a877ec794c80ebf47d33e60aefa223b56106186eb3bedc09f21c62f508e6db3e265e7d9491b4cc62b979c9ce94c6715c60a844bdfa7cb128"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-key-update-not-requested", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c1fd848afec3db413aeb9fa1f58a89b2bf1026f55e61458440a5d257f17794a420b47dc3ec41b8c1421c99518e099e55e4fd3b94eceb21b30363ca428aace194560012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00206a6320614852e9c79c7ea855700f63add5b0a9c07083dbd6bfb5922f1d874a5b002b00050403040303"), fromHex("140303000101"), fromHex("1703030035e9c137eb7cc23c26d88480b3a862f414b30ea14a1f8ec0d19515561f8aeb924f9e82b11364ea1961c8c4138b95bc64c41e2fe44b62"), fromHex("17030300160f50a6bc546f0bedf79828f760acf5450e0a30b9b51f"), fromHex("17030300360c4842df2da5ec8da28c478b30ee73f6dac4514169ad1d7207c83042dfba7f33491c6d6b5ca6c5637d2daf73e487adc21a43ce4a2520"), fromHex("170303001318418c6fecb2250fc8508e6341787aff93e91b")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("1dfee66f448622ca483f5d938048aa0e8830a74d3a9a2dbbc6852ae2c4a4f08c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("24b8d3d4988ed7680e7d73f2a945b667cb9cfa2b9924d5d92eee752ce0d22a81")), secret(23, privateKey: fromHex("f62298371a83c2402ae0228f00677d0867f64c137736ae3b44a3db6d0e971167"))], pssSalt: fromHex("b241ee4191cb909546f3b42098ecad7d742e3d55b651effcb866ec44f4cdae92fc3ec6f5449f88ff0fea7a808a9c262957ea944782a324561f2aec3f58063575"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-user-canceled-is-ignored", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303a9473b2d3a7e91cc57c7bf7ff55f64c09455b53f8f02ede2067f76dc9204dd082052d7cfb7cefc18eadc4c3c8cab8cc6920af9f0517a363749ceccd7dbf87f5f7e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209512ff0f7895fc90f4b6be5128b67df325f1a4b83b38aaa2ea3ad43418dc591c002b00050403040303"), fromHex("140303000101"), fromHex("1703030035565b770647a8ace5f183fb772c9bcb7a1f31d4c1bc94a804ad3138408cc36c3c91c76f9ef54978c3c7705d5d98912d53bc9cb64fac"), fromHex("170303001336a3b62eb94d45ef6663d077e43e056370118f"), fromHex("17030300366806de3dd6448222a6802f0255034b33131c747beb41bc9ef8d2826c6b98d22939d264e13c987b9a08a2decf58905568633ace82f487"), fromHex("170303001348c9da35fefb2fa1d0c5915dc62a51ef2d9173")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("97c26bfc6718058745d0319d935ed134cf0186670a3aea89458d3ab70a165c86"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("02504284b49c9ae3379c12da14b4f2994d21f0a3e2a380f04caa6b55773df24d")), secret(23, privateKey: fromHex("5bc6f5ea3a5ae3e8404606b691363f47f56c5c016f8f48b4636eed05a4ffe78d"))], pssSalt: fromHex("a4a1f52d7f59dcda8369948833f98b0676f2c19aeeed470f97ea813f678a0126050d19c0401afa4ca5803659617f53f2195ec52a5272a1b48658424a0375df7e"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-records-after-close-notify-are-ignored", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203032832c778b4911a028922536590a3d5b111d976cd2c6b5b0778ae08669e69e542201a6b8ef84df03e655f15dd1dd573d4a6b697ea0a2c77d3b37c2a3353d2ea6c390012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020e7f8603c5467cc1f1f63af52e28e4e3b9b861e356fdfeb404c5a4e80cd7a635c002b00050403040303"), fromHex("140303000101"), fromHex("1703030035620a667b4eb0e92345842af8a71084c904743e7af1582c91f55b64cd8483b66980bc12453df1ffbde5a980e2c010a283c4e4019c38"), fromHex("1703030036a7d0d842934069141e5844156084289a1574d96cc7936520ca2fea9fdd632795166f6ad2bdba8e65f6637118c8e13bd7a7373a54b929"), fromHex("170303001307cd11c4b04ac9ac8a5b123dc21f9438bbaeb1"), fromHex("1703"), fromHex("150303000100")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("57eaf44384539f52b8084ea9a8aefffddd38a1d4bee999990086bfb01320612b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8aadbb45f700e088259111a33a32ce2faea25159bd3b3c5136f60ae9335fc600")), secret(23, privateKey: fromHex("6edd40fc6a64d918ba41e4d6ef19d12dc0f197277b23ff9c722e04d7781a4b3c"))], pssSalt: fromHex("0a67c6e34d72e9a6884a77f293cfedb15404702aa46a1682768b997d17257a303b9bad271a6d50c27cccf3cbec86f5de1740b3e9bdd407c22f2028636890ac0f"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-application-data-fragmentation", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303df65e104ed31becfaba77917a97e38f040047518a908bfad42ba301bbdfc3ead209eaa66f1360480c0d8f461820836761bb1b79e5ac6f25432061bd43557353f800012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020c319f8b527b43c9f22b6c7e7a9e08b01e23b96f551a3f0377687d6baefc16f72002b00050403040303"), fromHex("140303000101"), fromHex("17030300358372839164b8ebf18e6b1bf3b5e739a035897ea2f8367702cc56538a13b2e4b6f819830fbbc0f6daca5b37beaf0ebcc9f52e78a1ef"), fromHex("170303003685dec30f0a2f40a55f0973ae8f6cbe320b8f360288568562b632f44a0786f7e44bbe060f8db59b7d483b1516a8e6cc456ec6e1764b22"), fromHex("170303001367d83ba8e55dc39f664beaae3e32ebf8c9961f")], writes: ByteArrays from [fromHex("ababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab"), fromHex(""), fromHex("cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"), fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("91f6014281366460681f4bd0ed3e59cc91173d0a51688d0e769284e60b126088"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("c081266b47b3661fa7c6e1aef01f1cada00ca9f0eb796aa7fadf6b059e565c82")), secret(23, privateKey: fromHex("da983d00e274185cf7e116d7d9e028e9380a09c6992f489f89b5c07538915fdc"))], pssSalt: fromHex("0988270f0a071caefaf25392586b620fbba603b314969fe122d3b9fb1b057fa8f65ed9fcfeff1bfd4fda1d2241dacf0684a2d9993089677ba52bf5b7a6a074fb"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-a-long-flight-is-fragmented", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303787f8fbada1301677de069595b97a11ce649c2a915660a3f56b9aaa032d30a6a20c25adf209044c6f36546b8970bfffd3bd6253a037e00315de9e9d98b95e806c70012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002093cfe18fee5d0a271110fd4709ce65c90ca88ee34a95fce8e6d7be09febb874f002b00050403040303"), fromHex("140303000101"), fromHex("17030300352fe54c56d3428b942c35630cc71fec08b2c2073df6df1382f21ef10b17e3e765961b7c21a92db4c525e135f76533abdac940b2234b"), fromHex("170303003656616174b9b1fd83d615069ba3fae2afcf55cb8c4445fb01854638f5d18de835344ca84de1c209a6efa984413cf14edd7e0899c30b4d"), fromHex("170303001379f4361eefb16fb262f15e27f5f47007e1199f")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c214c61ddb8934a1bd79a8edeb5618a22e950b65aedaa1a52227c70c4a01ec87"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4194b68dc911cb2d5ae682f680c7e3f7e6ff995cea45b3badf8814504f6d004b")), secret(23, privateKey: fromHex("9baa7fd417fc65dbddb7b833e9077e2d5c808ef13c705478ad2f73a8043061df"))], pssSalt: fromHex("ebd145cb10d4b585fa27c8f1c6b6ac0f77ab04b122f8cb1d79f2377ac8b2d58f11c78f23e983abcd65d621a0a9e2a794e209b50546b22c934b7169c33e2fb872"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-client-hello-split-over-two-records", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603030064010000e203031de325caae0698eb70695bb1858f258293dee6184b2af91f8bfcaa8fcfe583d220ea2f66b52d3d963ed3169b821dec4ec8911ac9693bdf67ce7c593b9ba73a1fd90012130313011302cca9cca8c02bc02fc02cc030010000870000001000"), fromHex("16030300820e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f48624d4bc307a6b4bee41525f24a5b46992c265b3f8b3306ef4ae846442ae77002b00050403040303"), fromHex("140303000101"), fromHex("1703030035e050e598107d43f18196e3113625b96dba87f5a22baa04c14bda782301f28b849e8a0298c96d361c460cbf46a8569da405e1b06093"), fromHex("17030300363dad8489b47df8bb5994edbe04e830a69f3aeed9d2de27b86f68d4142ce9a412e3b978ed494f70417678f2aa6e59ece36a961c6fa597"), fromHex("1703030013a1f1c5292ce2735f9074362d7c8e05cdd98c06")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-hello-retry-request-without-compatibility-mode {"state":"connected","version":772,"cipher_suite":4867,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["1603030038020000340303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c00130300000c003300020017002b00020304","160303007b020000770303a77cc085f587cf8c4f0a7786bb478920732b4ca5e4d8d03bd97b9cb75fe8b3db00130300004f00330045001700410479947a1f42f9c59729b13f3fd427a861c5337720a4e9d3fd8cd6c512ffbbff40eb250abfe5cd66555eb21c0e7ae478a659410b19497d23d5ec7a8726d1e65aca002b00020304","17030302079a19a3a906ebe4add50686782db95dbd9409c6660663847e84a6089a51fbc4615cb9347b4298c000cf2a1467e8022d6389602e6069dbb3aba821b45b19c58c9587b9c36937eb460e9be3a2b530464710cd0e16f82f783e181f6832e79208a1f6ab178edfbc69934153b302ab2a67c71412b12abe3edb9e6af3b7be31f9f94b8ac73a35d842175086a37b33065d6b26a4570212bac9d4691815a4a209237b29c0ccfbf349739fe0cfcd7bfedafedb9dbc92f19cdfaffbe8a949ea3a6ae7bff35bc7ebf7007a9c3f8ad5491738847af69fbb602dfbc730f8aa797f67b468bfef64142f376eb9fabf5b0677921179988f0d29236ac94a2ccdbec9a21bcc6781b5f284d683c3a49ea9b2ec4a5b3478566255a3788c4614dee0938dc386f25a5f9007c7b5f0c3876ed11d5589333d8846fb33df1170da073644a8f7c5b3f5584a985d069efe286ba6f7261dfe0cc8a4d7b520f6d0c9a69f67e1813a36947b6aed521bcbed346633192a92991a714c1b039c973d6df781a2982ebb64349b6cfd8d41e3a3c682a166e55a8ba64e4777a7030cf4868eeea9a64e9bfbc3efd42a14a7fb6edb93276a8ae286cf4551d766f1b66fcc2a7928eee25d4326cb8673b2702c034ba71988dd4c51848c5034ed5a0450266d6267c1c35cafe532af2c9402b260806a6f571180d2ad403fa4b2325347b6a67a59fa6cf4f4eeca32601a01eb019cecbdb8b32298381f2d","1703030039dff475a38a90e9c7fa6c697f4341ae134ff6751756cff575c31143cbaf45e1a1c7a35a3134edafd8418c583cf4ad5ecf2e01d83e0c4f6d895e","170303001319bb8b3092ef9184a292a48bc63be15551f1a7"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-key-update-not-requested {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303a66ec8d970021c6b01943a176131ead2d587b16d8fd8fa987bcfde00ea126dc820b47dc3ec41b8c1421c99518e099e55e4fd3b94eceb21b30363ca428aace19456130300002e00330024001d0020d74b46728262373cf92cf7993b168882e6724097fc21cffe0e463f1711cd052b002b00020304","140303000101","1703030207667bfd130b6fa8346881f48afde2db326389c276ddc84091a6433c761b6bd7335208cdae64467a0464194d8b3b63f3d4f2c18075b61dcfa1501d753222a125452fd50b25e2c6aed7ff1e75b00df471c22a3362791c4c3f806f9f426cf47409421b50d53bf60b7d55184168caea63677a8771eff301995d53fd5051760f0212274ff00e993d66239acce34c97eca7433449c62946c5aa21c8c9faf421bb69f9f1b738b2acfe34228dc5bbf411905373984d9074902ca29b98c5ec4c2553a0f9598547647b1142a0d26ab4464a12fbd5fb47664551a21e12efdaaa1e68204e4d2af7a34912e4e5caa0bdb88e00e87e79a0348a32d1b547349d078ac7f1fa1df99e0ecf92d278a6a8b835d72cbaf7d8cf629c77b8b656fb01d6dbbf48cc9b16205fe8a69e42f5eb51246e7572ad106ca62d0845baa2a5a2d3a783482b680e786159a7215e70d3a5ab30196a9bf2eb41f6f810662109e5257a9d7af208b5d0e8e0b1f87774e91d7a190172256f25d17da3f58af617ccf69ebd760c00534a75d33cc33a810a1ce2fb0a0d1d78288e3cb1a3ba245743585a805b8557270371ebb6153c564df7a33ff0d793ab6d5bc0a9413d3036a581590f370d78cb5a441ff97ec6ecf2adb4603bd6504476eb72c73941af22ae0e90b56b49ff950b12ee4f5e009543046a0c4f547d4c8fd4c8e65e67b4259d3cc9e5be39a1afae6c9a44701f46ada2881627fd559398","170303003975f49d073e1d4bf1c63333fe182c8c62852a196c8a654d0d600098411a2259ff79fc78c3445f33daa8e457fbd57c64cdf8b1a54932b4befbee","1703030013bb25506007fde51ee85d197c305524529fa47c"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-user-canceled-is-ignored {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a0200007603031dfee66f448622ca483f5d938048aa0e8830a74d3a9a2dbbc6852ae2c4a4f08c2052d7cfb7cefc18eadc4c3c8cab8cc6920af9f0517a363749ceccd7dbf87f5f7e130300002e00330024001d002076f125d8354152027d299f3f4c880aecb0487ec3f38584ab37b480dbba4e0713002b00020304","140303000101","1703030206d9a54a6a745da4fde936e970b27cf11a8c9850cacb4d0ee00b03d33c3f7b5706cad4ef661f66c91fa04f3c37a1285b8878bfcde8d63d0040ae19883cd4178094a0425fab27fce86d2909c06c312eb6072e7d179a66ab00a2cb96ce85d98ec38e259a27d428a6455a7d9d5bafb072087d65b1548cfd75a39b9010197adbbb6adceafb3beff2816fc37788b24d8851910fa766b147d4e2b33de7818c376a0c83203d4ba708213b9d46dc7eacc010e7aee730c3446516beccd643ea3e2b36c4b5988a39795ff6b623e4788800bd600fdc867ee01bbc339a98b7af5cd823bc2370467cf4a947928e7bbad2893d92d3564520ddc265f5e80a907889126e4619b657fffdcdd810865e956d9541d083e6b42e593353f664c1e9ada03bf737567efefb164ba8840775492416be0a4dabd43431004719a9664ba41a3028d9c0a02b25ea618d881ca5123795b795b864431c2e92344d8c0d4ebf9a87ab7cf07a100a6cb23eb7bdbbf2f0fb4c978191a5f02dba65c3b12379508aca89f67ebca4e18cefb98376cbed83f08b669b743e6f6db1fccb18305b309fd5bb3ba5f3fab8ef4d12d2ceeb301ea7067422c47964b65def93d28347e902402de923364486233b818840922b6f138d33436e10e284e53e23bd27bba33ae674ff8dec5a82115d0571958214b33592e6f7843f7ff6dcac151b05c62abcee8bd2cbee2ea362edfb6dfe35400347d7d6ea50e4","1703030039d3d82232487b47e7511171aca6db591dd49d53c61559df00e03415922b2c1cb7dc20f979149a4d832a1b0336d16ddb4cc97f420578256734f5","1703030013856ea36dae49848b4d47477549f4d5d5860c81"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-records-after-close-notify-are-ignored {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030397c26bfc6718058745d0319d935ed134cf0186670a3aea89458d3ab70a165c86201a6b8ef84df03e655f15dd1dd573d4a6b697ea0a2c77d3b37c2a3353d2ea6c39130300002e00330024001d0020c309cccc768ccddd7809a4049f053964fb06e08ad6389e46d8aa3a9aba370a34002b00020304","140303000101","17030302089d548a23969fc4f4a069223609087c93c2a2dfb50c21174de68d01755b9e93f2468ce25a573669ee4f8e9cf6bc3b6d3cd1b052e0cd30510b39be7ce7b0dea436c382769e231f80e2264a7c73b0b9ac5a412ac696e66f31b92b70a45936c8b83f81d9768974bde6bd972bf337a8aaa8bdedb7e24ee2e62d73d5185c97aa216e1b51db4e062a656fb8ee8e6bc0255768e795211fab095a9ed2b789846dbe6097835e10b85bf1245c6655f853b08b2c92f432d26aa568b8402288226a2346e3ff6c4de8b7ac4176a13a0928cacab3a489136829a95f4c5d592141df6c90a33ea1c84fe22e2c0d6770decc2d576b2c90b55c374f1d55c9cb825b0a872473a95fc3320c1c2de5f1b15f54473902c2ac881c8b2cd2ddce3c450ff985965bc5076d9d9a94d86d1f17006dccf7fae9b727ee5afa9433b8e762d19cec46a5d86147d39730d94963df0573e4a98ab3cdccb68f666aa109411aeb0deb1dd83daad524a78cddc03a5f202385f3812c7764373d9d896c476f82bcac537d2d134883b3e92ef941b96450bd56a3194b827b124b1e0f2eab5a7fbdada30497fcde48f5610ec4e82f3cabd0083e90ce6c42218ae3cd6831cab553cd7fd7acd974613d55261517f224166cf0af19b3e5a15fc52c74605cb07b195888a7581f5ff7cf1de85e3a594fb6807526b08d26e9ed5e0d6d0e8ad21243332d9e1e2153a470d8c7206d49502391758a050f757df324","1703030039895b7fcca5a57edf20647e8d1df21728807b680840a72adf1073f186889e4a36ceb7e0240d3fc7e920ad720cf77b9cad0c7836cd48faead200","17030300133df906712c4e162e0c88bea57d8fd48c82faa1"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-application-data-fragmentation {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030357eaf44384539f52b8084ea9a8aefffddd38a1d4bee999990086bfb01320612b209eaa66f1360480c0d8f461820836761bb1b79e5ac6f25432061bd43557353f80130300002e00330024001d0020adea2133efd2e860b5cf7cf1c6cc2a2fb906c0095c625c27bba0f2112cf83348002b00020304","140303000101","1703030208dc99c0ac786fc16d58036a9db73cb6ce539681422e5fe75e6ea2c664962a0d0e359815938b9a54958a2e09937efc085993b2ee5c9dfed8de7c72ef60c18fa7fb0180d3b7bf86cbcf225dea4fdf07bccaa8cdab149bf347e80eb2ff0747a14f8495d01a2b6c42a9db3ffe196af22969125286fe11874310bd5fd459fd7d156f1e1bfcce965fd32edc43b8bdee69b26192f0cc034620e51c871ac65ebf522dba2be72e62a55cf1d070e16e5a46ff28c2622a7586cd6114c1763f98f9557af61eafa004c254ae5f8864bcbf08376edecef487968067e5f4fd6e37c97f49907c3d631fa78f57d374437bcbc9558df4c9e51be3da4bc7b13dcec44a97425150510abae56454625a6c3456ead4b1a36df5c72ba9cdda4308de80b2757089c20586b4509ab601091a60c78b69e845e0e6f080d1fd8474dbfce3c2c3852763596275ec1a7dd0ab1d831379eb372c6428acf89f57facfb1d6f0ee0636ba6c509265f6c43900b02e62f5e66c4e616103825383f2f5e070743dd6cf13d28e12e2257092c3039a6c60aacc080d3b54d39016a1c871d7d2c15b70ea301e08a213c65d245483fed8bf0f88576f639313039028e212030d02329b7c40889a7ebe2413e22f3f9df99b06ca1745642e7949bd2f0ff27caed5ac58fca15282772ab8a8694eb69dae875de0f6c08732cad44d170c4b5d34a3c6114134487a01f933ba1845b34866e8621a3e05bd647e283a","17030340115187c7de52b2e6598f8463af529ee47365bd3822be2d1b2511b73aaefdb99a358bc57b8ee86c2b53cc55b73982dc13728fa49107e811f40bef7c5c4830f8a462547ac0f94f4451fa394ae914c2e7d704d380cfce68609b5e69cad3457c0a58f761de36923ced85691e05a94b201875f8d75edf23e0c3562e18f50f8c57c8386337871e72c1e6baa99bca47330509d1d1516aa58fd6743716bfe86089ef56a68b8c18dcf27320df474e32f50d3091c772176f047e8ed7a743da0d92720918833f80279aab302ecf648743ba77c02f00ec52b41c9f1249406085c153c83000f5f73147c772595c585472cdf82956bc002d752147b4f468f0ca3b65156af1e654558905e5a15656221ddb9cbcff09f36b884c8103f5d1b65bf8ad7b9087429e3ec8d75d22c98faa25fb98de5a09dd7e01fdcfeda520ab0ead75fc10151a14b2ff317a01a03050b05521b4bb07d2013294e973502d0e5d56a9eef1bbaeb21b1f769b09b8e5c98f39e89bb83719b268c9f597c28851931fda217fdc03ef272e6f6a5aea645638e617923c3a29c3d65a2ae421dd441f42491d65c07ac6c8e25a35311d4e4fa6b6e24310ecb4d3c4c850da23b91b883f3d8fdd16474c8f12d2aea3eca7969f9138aae5cfedab41b02f4640214ba3d973854caf486a53a22be542af3b804d19f3a3541688687702701bd443b47cc649d4a5d4766d3b528fa520c78f8b1c5d295fa07aa950f8fa432787677adc37c46a4eeb5b29bf8bc03110760921441bb7bbcd9d8fdaa24618222bdb22e2665c7d13342efcb310d7fd1537e9162c3d880c84831a7ee40f9f8c22de5c9d627004da5ec62ef3c22be3c6ce8fae0dcf0c2c630cf6556b66b5431fdecea908bae2b29078787dec7dbc2e460a0000958fc65e3c07c430d23ca85fcfea85cf2a57ed5a075e0c8bf343f8261b12b2872799b637f4c9447ec046291c042bc4a3a0a7354ed74e1f31f8785309e1572dc0a2ea58707eb0ce6fc10edb111c19a973ec6259e2c7e963e4af686c1d166d5adf52c9a4208990baebf054fdfd9d65c7af93a00475ee8a17af9a863ab0da28f31bb554faf6b1777f2aac8a1592e4ba1001aa2b25ea94bdfa81e1751b5babd96ff7b155122c3d997b185c6d691f857ae700b843a46c06f2246c77c8ad05eed96ab50f951d3edaa004628504aae939248eff006ebefb5fc89bc8c87bda473b3ec9c9fe59a064b5695823375f50edb0eaae26324a5c95435a05964af451c1f09fee523338d9c8ee74e9dd94fbae30137337bc0e97944e317c5f4284cb50e475236ba3b32b659abc5ef27fdcecdc8ee8019ecc184b847222d4db85b49352612e3093226edfe59400bbdcfb79ba69278e49b3ecd402ad3ce801f96a2b356e6d3ab9275de5abf5ed133d16d531a78a4021f3cd27c5de760fa9c19373d12be4f9aed7e40d3c6d5b4917bb632f4bac3f813274780d6c6d8087fc253369cbf41d2005d725383b957d8b9a6f5c79c4bae69ddfe66dbff504efcd61a7884f715eb79fefc1453af6b36f717df00ac4e29a86230b88f47c0e6bc45b8b81e8a3ba0bee41ea119a1dc8cbe937b5a82c45721b1de81cc043dc3bfb8ce06088ce3e3c704557baf5252c71f4811204f519902871044dee8a3e6721988eb779b24fbe1b27557debed73b88b56ea7f07ab4588bc7ad788d82433916fd06e2c75fa78bec1a65b361bc44156b914d91382a72165b669af0cad7c2e3d3a92f0c12f3f82cfefeed091e89b2f428ee5463a75a8b5bac9373466051570e1f829583be2b7544bc9bcd885061b4be0a30a35a6de00dac9ce03751d9340a57116741b99caedb26cac8517c0d240f2964177bbc95a3518e9ed2af4dc196be9f8f63951c15420d983fbdb13472db13bd0bcc09078a6fb5059d1475899a2eb0f891b49e664a0d3fe7d8485b1dbba11f07616fd162c0c4a3c455cf6495a7e841997ad9397cc16e32838566e88c4440763bb4f908664be7ccf0bb040fe3db659ebdb805438f9a22181ffe0f32e57d8f923608a39c5b5f614f68825c6d20b4ec05e391a3f2df4395fa45954f9ff7b9acf302768eb7dd8f46fb45d0d3b81ae46d2c734e90726384794449a59afb9eb06d139592cf33ee48a23a1835824410c4f0b678fabb9ed1e1f1b7936078fc98cbe83b10cdc75f9771d970eb33c069ea01a11e7db2203f956ff057a832bdf3c184ba937b996442c643ca22ce766d6965911bdc4a4d389dc4b6fc366a71eb7536bc8ef28abf63d801cfe506f2d3a4bf771a0c5dc8e1e162805179ccd8282ae085c83ae27c82ba071e16656c8d223ddb6ac9d4c3214269ac480902c84c4fe2d21381bff7369b59b6f1000648842ec5b4d39367bec0dd98d6c8060abceef473866584a436795090e89ed4b6ef5bef11470c893397941a484ebb25c127b29e68c6e9e944ce1cb8eea2497623c14bbfc1eee23d53d89280a66c23645717be0fe256023bb04b9240cad0d37ab8484bc18a114c199cbae1076d151cadbd8c884c9126bb53ee3007220b0cb2ed87f6533ddb6d550b70af01a9bf97344d9ce9274683edcba198dfb7ec6941949abb8c8c983f27b0883b7f2f3cad9da352036784c07d30324c649da6cd7dda283616782975c0344679fc19e96c41140e6910238e92f1d2e8c263500563dae41216f1edcfccc9c03105e49f57aae835b6073f0094caab4ed257c9578359e0bbfe685e961b01c9aca47d50ee5ef805d881d183b222c6f610139be3cb7f3a05124f5cd7d5281cd96ce16a1bb0f27f19bed81a699a19d8710ce04e272596dbfef0687a9959b9b0e44363171a4d6d47b40553a3478efce73a3c1e52ace1f79e41e4fe4d0f7a3fad234426691f08ae19b7189e7ce5e4e77136f59a87aae728b15112ec32b4104dc79ea05d8e42b74249399ddd04a712fbee8129ac5bf0a36e74fb428197ccb0eec65d35c9a86426413c4a8d2a58b5260492cac342d2a8801f97aa8780268af441fab51e4d4e3eeedce3f581a08644f5711d03ce065e4dbdfd45da9caf7f29046f551aa4eb620fd3d4136d648cc8504019ecf7dbcc336a5cd5635f4228638d6009a5b0e72c42903c2a0f27e3fc556d4ec0fe6ff7e41df154252498633c4fd4b09b9eefac360fedef4d8d57d858b2cde5c32542d98902fef166e730c449a84d481231973e4e6a7a6bc4ea5dbb34c794c646764f8d1ec02e1a6650c65f11f09a105f108665076c71e18580ae2457a523c8a4686cd2d06b4cdf80a1b1498dd237e2f54a22466d4ca5d6e944a7bfb2dfc9e75287b01420ad6db2f6ace6be2173823d11b7b4a0e1f0ec3e55f52bb44576e9f336a8eb7bc8b905adad51de2c50bdb7bfaee964a5916dbb672ab13b45e7cf4b99c90b35cd0f9a0ef1382933f01aa54f69b256dbc9bb32284bc34fede9f80f46bf20c00c120d0a20196aa95023bc5776c5c613ff3f43b026d306664cc0c03ee97af990e42f5b8f8fe73e246e85fcdb74af089ee84844e3551b3b27e586455aa42adc9108172d2bcb23672d3c0d4966aedc9e18d126019087c3f060593293554dfbbde40613499aa7ca84002b8f63ccdbc3c9a9b80881461f231d515241d51179012682d4b76f25029c98c48b0ac0abc075d9f4beee06e637cde5ce921743c296d912fc3864cb54de7b1017059d3226b39336081f298dcb214a165a2fa56a3cc66a4c07c60bb9f46d4c68f7308ab95f6bdc19125e4c3c44825bdfce0932a5aa315b448c87bf05a9f62fdc7582fcb9ec987a9fb59b82f3159c63269e2c859a7443b83ff521135d0e49e52414e0c9554524e0dc9907ba656df5e38993d5f6275118afa2163c56a8f24b55d590e5e5ea5f18159f1289523460a865841495baf51e278eb4e81b2649dd0a9c28a63322583e244dc32b97b35e405e90c096d5b090c98fb7cf7c00938956fc6fb359bc5408d0fdd9958fb52d584be44fe4c638dc93e9f5913717492cfcf11d5ed91f63d83c640ed7acfe2210032b378f036f075b300d2e94309a03807438445a59b7f6177b2203ede08e64dc7aa02b06bff45ec358ba741500f9bf2a5836cced5357d052bd01b4a2e6e2cc0bca87358554a1baaec55f9b97bc46ba296dccb4c715d453cc80abb53209bb4e4bd505486503e4f72b4576dfd9bed33c1478d2b506fe6a22ac673fb5e652548ca20b8a41248e5109c1f5f4f7e408235da30c122a0dbb8bd4644ad5461a7076571ee2f4c09f0dc5f4584877c9a5db9cc04be4dbab3acfae88803598a1c6e0ba89bf2caca96e8e2a1cfb3bb30a029d0c1476d0cbf7ace428c00b66f2c6e4470495e6272ae8d7c3186fc0876c0b90eaba6c5fd88524f4e170e48d9beeb18ce3fe19cd9920d92417a9d6507a1bca8f247b0e0d3997192d414a1ed9ef8c868926c73b5190f12ff266ff1bf58d5082a5b1cde4360baf243ef91c6e9cca76dda18312ec6d6c50a65073da7836a9cbe911209346197d5c3ee0a3aeef22a59f7ceaa0230be45863acd54446a039bc7be27c8a93c9e49a11c09fa6c999cb9f5fce09eb1b84845f28ffbeffabf1572239d2e25d0c9bddb93175cb78f0934a6870521bbded37137181378dfdc98680472c9c3900232b0e4169ad36422fc29c76ffa37c7ac4767fb35e0d6affc94dcc47f5f329bc4b67bb4e12344f7e0cc7ce51f62a1124be6be16fb8877fea5073467a0b99fa2a93474539651a82ccce142509bb5e8aac80ceca717ac181145264a85ccc5192bad55f09a249d9ff86679fa5986bae0393fbc0c60e445d45aaa86ded59e214f53f2741aaa762a3d74028c3a9a62486b3c00e09db356c74a93a2c2cfd6ef6ad68d00a3774e6ab912600280c0b260533c09bd6561917bb0d3b0bb8a9a17e9b2e558eb1332ab7aca5aad160e7d5359640404ebc35e649f710e7b4e1b761c0e77430ffcef706679949328ac5c30611de6c29e0df2db7deab767d8ac4553b1c5d187619ea13d0138d77b5fbc19ccb4a7f80a127d72d92d109c73c569c76fea9c530e7529c258f04a6499117c5ae126beb764e99cff16a9bbcc392b08d26ae921a8a5f939b817cadac91f6056740c26c0de86c636c893bfaae1f01abbbe46403cadd4e4eb4f7b5befd82183a4ffdec12e6d8218ba7195f953ce219a98975b6e3a4882b736ba47defa0ebfb8fa43f6d98c7bf0b20f4ef91febbdfb91d9df888b19d317b142cad3d7cab7d23988fbec6d5380891cd20d2417f9421d34b86062551487ccaeb6b612aec724f2735b6c1a4d4059a8d224fa6f2f4e107decf4e5993509b0e9402b99674919f50701739d5f419275baed32d669f65cd06198bfd91abfa447f30fb6e45f94c1a2d4cca66abeef446d2b5512b2c445299b72497d370a2e07a91d665c5111af244d30cbf75ddef3a2dac417f786b34463ede7ea86074b1fa5b148e1461ca95946b3fb632e7a20db844d60d8b0bab896078c94be47fcdbed9d198303b6203caecbe4098110fed26a88aad2ea6e8ac0d0232f8d1eb1fbddd4b7afb24c4ed9b32cfff3132650261605c46b7040a2ae8db687536efcb1a1d7fcb33797a39e64b363d73d66c89d33c919cf0b0f5e1dab119203309b791d50da3279f67f4a979ba285207a97864e2eac24c67c303dce2bb2cb87a1c1a5fafe745843e90449ddfb2224cf770a86da4a58ee0f3841dedb631d507c92d123f8de41b49d2f1166c80334ecec84d99162fc0afd58702b14ff5125a032b34042ec9ba700d604063b81289d8801990a0d580af196bb7e2d299bc12eec10df8fe48a1031869b860063cf27b0e1fae4eecf72d50930f8a152ac749e35bf400f8220f7e8ec237f2b0cbfc0cf78a05b3a5aaf0d5326d4fff28cd0a4ea515c49d1bfc646d0cad425fa6afa91223be096b5c56c5202eea7be7923fc8d010d779d13bd6bd974b1b46a3693c69145c60cfe7262b60d418aa759060af40aae0c7a1f13b7090e9838cb2630c40150917ff37037fdf4fcf35c19a0b2241720208d236be5f739220a0ab515216d263ad7fb1940a4bf1aab4fe4d47c09ed324327a9b4ec34e9896c3b987bfb4dd58d5c1f1c8218683dc4580f812feb35b3044ab33097ec210acbcafbc91c4fd15dc2e162bc530dd9ac16f2bb2351d4516ecb1d57b7f3d68d11a1d1514e427c7adb00fb988039f42466c97df42e8a50b8bb2a440e63af7311c2daf2417b2611ec73804c38ca5d0df048d540bea403565a18ebcd20d9a0a21544d6b0e655c9b88bb0d7c1187b52a7e2b4c7c95b204be19e8371be0fab2f09a3d6e327125162f3ff02a19dc80c93443b2daa65cc8b71ff49a1b0c48be1a7c7450f529642bc26321281c1dbe6aa6bd65f77f51cf8d012aae10f4dcdd549100edd8d93cf29b7393f755b92f13993fd199419fd11277125dbd1676fbd3f6906b0dfbc3d3fd91f1acac9c67586c8d16ad3ec3b08d9ffbe3914156545f8fb596d5ff16f10dc62a5fea475aa08519d46874d5d9ac22ac35bffed9f6def431dfec2a2e949be0d59b425b545e90662a38e20f88c9e3aa7274e44a9e366a55bfc6f52e0e3049d6b1ac69a1020583b9b0178846a4126e80cc15197aa423ebad4734dceea47e294f8aa4149099eb2523fd8736886e80b6383bd0102f050121fd43f10d9fa3cd24168a9cca188cbd65f46821acce581dd48ff586e1959630042ecd35c932a275b58fb59b5bb5c45d1c2b4b907cad8a19c4dfb031e159909bb260496e26ff8aa39e343421d863a0b4844c0b809f435df3b4862832dcf9d37238dfe3936112e6acd1c05d6eda5a32978eca937a4998d01f6209cd3b86b5c7346c8d2d5acf1e8ac4476d9e70f2360d9c634f6879390b0513c986f2aab602a8664838c11641b31f2f8e314025f47e9afca62cee41c99d5121281126ae29524af3e28ad93b595c1879da463a7ae274d73f9df816267f86ce65218435c970e3eaf45d7cc6212f5046d1c3025bc2c7a81e6b03e409c7af000ec14bb647e8f60459a82b9ba0a6a3d9c0b942817752856f1986344ffe2fc6d7677f7bf48f63ef70aff41e3bae2277d00e4953542ce3ed018003c68c83dd8f44ae61b14b07221f78e15c80fda262cc276b46f9a23a729adee2c0ddd56e383d103dd4a24f0fd78949f0d97e46e791ec82a8b843882ed8893739a0d4ce59db4520a3ac88250292fbeaff72b08459147ba034fe37e08253380f8b3f50b3e3b7f047c8d56268630bf871778a1c7c5c6074f6dffbe0dabfa69648a9770af59dfcb73346867a69b4c7bf9cd7231e9eabe7ee198b2a0430e796d388988bfb4618255f46f53400b6aab5fb030bf43f5aae96c92a6f30daddeb343c4afd45479ea7b5bd87f276665bcf7f5e07430dd225ce80df0b3e291116280f6b0681ed740417e0c53f18274fd996a502177a68cb6fddcf7ef06abead9eb1930f0d7ad29bb32af536e9917ea8a055dcab248e11f98b3b32bf0e07ac1b815a36e4d1f602d6fcf46222208092b2e1df6feba9565f7932f730fe55bf6f29765d17e47a6d712c8245e36f6955bf6572e65063b15fe33f21d43ccb9b65ca87e9a90a564ab46588fda562c4c66824b7db9d0ae5f3c3f238e04338b5835b763967a82c76396c291a4f6392a789d168c397669ed18638536014a96c7ffef5e94ced54251f77fd19a6039a25b68eddd31e04417899bf85bcb6935032514f42ed836b47f972e1e2e341756c015e1105e823490c33b18de8dfef79350f375adaa98a642c0bdc805d8bb892d2dd6dae3831011175053684a551f74655107eed445cde1c5f9dcf7ef5b280f2564770289ffe259bbc996687e1579d6b8d281f45015a9b5a2d15b518c192bb89352db7d49539d4e5497b85d8af36d117a505d32cf975f0f7c86d2af40d3e226a5cc58feaf3bef797126eadd2773020dfd04b468bd5881ff471ee0af10d7f07b118ff97d1925282d424757217e3e42513e1bb2345a61e344f2e522dc088c88150c1af99212b38004d28c73669fac746071542e7d7d2ce576b575b0df09081cab34fe198b95666842c971150b3f78bcce201fbbf704dae8011a2b7fc8b9dd77b5737cbdbeeb9849660c82db265b0674cb765a55e13677acecf51745b516e64254b9fbbcdabda25d30cf70df69a2b973662d30b348fea32478b2da39b4dd22984fb3fd361459807d5e6744299af86883a4c293a772de109cba3f29c529fdcaccf4fa71fd6d748441480d4baa426105862c948e7fac1477b0b3f2960a1a3c35f3edc4b923d0300dfe2b9a6abef47d4922411872940ec0325974c96337a8f378a8d7907432327dcb8a116af4f38379e2f425515adcfcfe2245dfc3b1cf3755b1765c99e846b117ce33102ec3d20fbd292d35ede20586c284bbd2a25e6b4ff90b3d051e5da80c749dfae6255b241a0f8401804cd6336548bd1a7612b562512f8a09be412fead2f979d7e86f081266ab1da566e859fe2d9e2364074940d514a8d051ddf9461ca43ac01d2c57256c988ad3cad2b56f8d270ec43e5d9607acc09efa4f2f2f67d2d70ece5479596a30e6cb6e2bdddea6534b85d0327523ab04870c2680f560e1adadeb6d181245824e69a187ca8c16966447ec509672edd4e4f7d03dbe59ff216b4eede0bb6b4207087e1a198bdb6a5a323fd7af61c346dd75e0a42fbbb0f220ef846152185e8728ac3c648d49c0b336fdcd2a8e841f5e2b70f2aa70273d5ed3fc595fd0c13c5b9b7099f83ff20eb1eededb1ec69a66e2b7514b9bce699517e465a5a1e625850a6752f70d10a6ba5580489983daf9dc0946e7bbaadf8d36d3517724f7127624cb42d694ea61fe8fe0dbfb98f4ae484d57698ad3652f790927bd3c00a3e990fd09919406b470d9afb4151ddd3a7e9db1def7b87489c1247e2274a0ab017c69488a1ec00df29832b2a702914abf274fe97f7382dcbfcda61ed8f634707ed7090a10fdc7e19e004380438906cfbf7b83b6d783811d2e13a36f90f9a8b5e7f3f43b3362514e7164d248f19a0a6a17be27ccecfc5dda95ee112a9b00df255a1d098ef4c8abb49d3ff99147a972ed1d4fe0621e47a0bc3cf9318de68ee89de5745cc5fecf10676f72e20d5d8b7b3499df381cc4a4ca33854b99189886776978fd06ff13e50cf8cf4c5d7205869f4ec95d4ee58cf79b583ec28d8487d25efa8027dc82ad68e825b47e1ccbda1c8a90b609d11c810e12201b650c9c2fa784cdd13f4ada295f90c7fc4ac7baf4a3881a269b3b289b2376fe452471fb39c946d5ca7a9c6f5aa8e0075ceae56e175c13a1f9a3c2718313559eb46ffbd8d5caf8491a67b320f95e4d62a317d4410fec75f411cf9d1d7cde307f6830fe4e75c4445405fe23a54257fe27e4ce3c8c02ebb6d4567ca2393eeff997e04eef2c4bbfc8d7913d9b336bf4466c9f3fc99e603069a1ad4a7ad97379e6df653ea7b788fc40e6e29143be101108bfbe7738f3b181223f6fd668e0012a5f2a6dc66f83e6248da938b72fda613b70fa669af3b06f63c0e46fc61cd29ad8ca53ae902bf15e8682220d8e0571ed3bda32b3cb63e288baa678c40c102b12f4f2f5a529efd24ecadfcaf78a3fc9a6950204cd8ffae3d2d0f101ab11682604f3912488d5dbbcb419c35a2baf6ed88dcb07a439f8bcb3e5baf75ceac8bc8322a327fa0c15079cfa34f9f0dee2ae30a383dac460fbaa26430922bf17a08a51be7bca90913695a50b8dcc44de2cd2359448b500dc1b602c49c5e68b6ffb947edb0b06a16a88a23993b545ae641cdc96cb84c81a36864ee1bcd0375412d2153189051030b7861283054fda4770594660b5174edbea70c375d5ca2ea6b98ac413d61986f617f8a510ace456ad2f6fa0a50316fa1b4c83167038b15b855513581f2acf1d48b1dffea4172a6d44d6c82c2f22a4d46cb4aae051e63918313b5bf0e1d52e8716ec9199d17c1158dffc06357c97920df24530e15c2dd21d7c45e7140d93ab2451a67b8f28cff53259a9dcceb19ea9946a602481ae9dce1356c2734634fc23619f875dc3bf77aef84884b430cda85e56b741261bbeefccfcaa4a9e35b7b530e836b0d2e4d3956ad81ef7e8b5210521d2df7ff15d77f382c9a5ddeff10b5fdc2286d09861ae250e10de6bbed967d4b8ed50c79e7b17182823ab83c18a16cb4cdd54e8ecebfdb02c4d54dbbb460a0aefad0b67a75fa4703ab73e1f20e3a387c6b978622cd81f391fe2c6d120b5752301b81bd467d218c0d84bcf1a2e4255ed0a8d684b11563f894006bf9acebced59138a12ad91a270b27ee6b9fe5c9cd02bb575dead26e39d971eba64de89715a127bd784fc3d0ca175704f7e468e48616b63ed21d0088795851097064fd885fec7d793d288f5721ce09aacccfbc9fc1695fe5754c886dc495f35451c51c422d5106964dee510f5ab39aafa8a40e0d2ddf8fe22cf1cd4972b53a854bca51649538641579e2822c7224802dd1f22157d50585686fdbe0b9b7efc92cffdbf325ad3ca5bc48dc83970a43b6ac7a9a6c4fceb24ea7454dc5170937ac426eb49aa7692c02bcf6c295bd5b4481eacb138e0a3e05d0f3d37ede314281a7fe28ed87da93362d9e032221a5558a21964f280cb84d1c2d3f60136f8b15e201663eacfd12b7c494f95aadaea7071a89f439259aa57e088c88522b41fbd88040d0cef7f10365fa4ad54f41fe190d842e3c1d397b79e9202b3618e7c12471fcefb4f5a2b52628d95647b12cf2ad50f7d519715f436de51aa114fe4cd86dfd8dcce2f43f1f09e165c7926cdafe6c60bef2d596b5b9cd756b0180e3906d7074d0316cd1d2abcda832746f7f05ca0ff0acc06db6ad55f1a65c699107539022405f7db5100196f893d3a5fbd03e4a7627cf9f50e1b65616b1d80aa9b5bfc0c397f292fc090fa5e1cfc1ed3c8463091b6de6416896dff697711103caa153fbcc1999f7cca3c95054c984099947d22dbcc24e227b198f64171aa8e3bd7c3fedaa3e021d37ecc536cb5cfa8bde1c8cfba0d9a13a213b5a59bf1c036b60ca9344f3f3ab1f5790c5390256833c5f6416c4e6585971846a172c3cdf79d415da70145039ef71c4538e13f6c1b2e3bca7e574429c1c512896a4fb1e2fdac9ab3c2c1167a7a00a07cc0079bc0bf50ecf6ba0aee83810a8916d488530e96928b0140ad8d4e8fdeebe5ab0e0c9d1cf6e4124ba9f13c65525317985922da9504b6de1b317a6edb018b54d6eb983d1426b22f8bc11c8ea8e446df06aa2f5c326ffeb122551ac514e2e70f375ae01d816093ff5ba7ac1ce36bb94cd86cf99de88da44e48ad4d03fa9e3e99e66281843e0fda2b58136a3169c6035ec207c67c5fabab2a28a18dfd3f3d17d478508278fe833c3e32b6f35ccaa1da71c836aa9fc2f3146afc15e50cb6571c897a6417ae8e4bc4ad0d23337a59ba3e9da2b741065f313dcc30335cf23c621f2ffebd528709ba1e46bb3eddfc9af3b9f0bd2915f52fb9eedbcc4ea9b390ff3f686ad54ba09c7322f314f31ba1d6e16a32b4d62ad797f0054b61bc701d5c26c3b3bd2516a1783268b910806d7a691ed606ec4a9f5440c84877fff58a14ff20dbe0c60f3ef6e01f175f188c58168080007a4072cf603d65241159e2992e9a8d8e0160530b5da228c6a24a6a51cb50eabe9992cc7c2e274ee5f4eed643b7b9ab431da9b64bdaeac63b1407e7b197ebba3a4e67114f5665d222c1993798df4d727987d39a19949ab06fee3e2deeec5012c06400fe06fadad7e9da64b63b1e9f3bb3c676200531081be6dc0c17983ca82824dd7b30afa7ae38bd3870655c8b4892827534b54ce26632a7736d34d0d9167bd97d54d41f10027c5b35a65b9774b1f74b60ffa80b8e6fb82a28141f30ba6498d2164a41ecddb15fd9e60ab2e26207d9b32f7a2da0d2bfd5cc6612f8bce8a07a0b12fa760486186a22f567048ba65d63f1bc790313df32c6d3ee7522a9e1e85a7e1a245c4f0cb1c4e9d15dbd1d59b7b73d4dc06d477340c398f1f0d47298d82e9515ea02bf278dd11da430aed1d55496ee86bf0e7a8e822d699cfdb6e477430675070ff04dbee8e8edfad4b311ee61276c825a68d68dbb1fd97c2b008d568ba8ff879d1c266400881b5d9779c0cda7879dbca97455f29b658bd112acbc9aefc78fe7025f9d6629bfcdc5644984a8a6ad09522bd8ba17d1192017b2647eee0467784d52cc5bf0508ec33cc48945c337e1e099483ae82d7b3aacb620e853ce800510846f9e165bf3fd795a8bd98804c2f025c771fd610dd7527db8693df0abbf9b0a05feea4354adfd0b02e1427503d55615aa31936b3d06a4df113e88719bdd90d31691644ef4ba33602290de4c7bf331883ba30e31973a655ba6e372d9df11a0b7903d74c02ac522ce608a1dd8863f40b4218b2f2203e100506f3d52d7a6bb31aa00e970a266165b4785d6254eaa285e85e816ef6edd55b3251ce44f1b96796f3c19dd5d23e2c66219a8a6b5678b50e45bea6e28a1cb217492889f9a655a3d10942677b4031bd5837d1582e6de2300dfc379e364d89718b460d6ed73e6389b8157c404bf29072bb4e467673f61335e01604725815e1f54f03c6a82745804ec4ebad8d90dddcc568cb60b13b3e876104fddb094c94f81208f70aa2572c789d37e0f0e4312804dea8f422cf1e5d10c0d521a953ef743b1f8cf4497652c7f6470600222818c714d4476c97b5bcb6c4f4168eef7908c5a38f819858be48836de24ac871f4df7720bb68422bbc6390ea637ef4ef5a0652867c9e4753259f8766acf5e5fdb8728ca51f0d23e48a77e53c9d5cb8ffd709ecf3d189da5b83e00e6849665cad1258a8b0f000faa9caca41627e80f496e71deb5d4bb4b6a6b7aa6d03b89279ad5d6f67f9b362a7a7e0b886ab0f3bd7643249ef09e9a57dba46af32c3ce23532278b05054225a5f6db82a7af44ad134aa3a36e29c500dbb130db3dc2d1ccca8d7f55784d49891588f2581451a0a0ac7a6ed901945c9933d6f6692f39644e40d7b50cc87bb7151103e9df6c1f9bb4ef2458303af4c0293cd0743bf9be6c71e1714eee6b655b09ca33e6f063ced2434f19428dc965cb028b2c921fecb1e93029630db271211a6f2d089e7b4aec5d8a45da57e1f6f2d776900594454f17a69c660ea234e6ee5f48e05c70c7eb17749ef83dbb65a0195c0fb6134e8d947176582da072ca89ae2d30449837a2b183c7b3810170d55fa01ab3fdd8813b18afc9fba743f4a560f198ffa5498a1b087ad6c59efbc005831c7f42420c20cc1a07f343727b9c5ae49b2395c8d9bb94750881626d99a2c142d721db91516e662fb1514f66730d8b8db20e2ad4bf9da085cb2358e18d795a103ed26813e80de39b22991d5c382f92c382e783138c18c193b69dc3be6633838fcfeec602f54157f4a67216e2fc84fbcdc1db35128770cdceb82bb278c5562491e5b9ec2e841e8644702796e062948d8d66bd04e1b72a0412b9363a1ba252cc0747f4f6a8cc8057b7137048a85b71852ca0cc705e1e4f8913cca13498f31fbd3da4e3acd7120dfcc1f1c731299bf3cd54b0f4139c5bda7fa9e8c0e133b78c5de82e9398ec3fb24dbf9ce7571f0187d4eec11a07d88374b09e8a1088968dea013aea5fd31fd75dc9172a46999e39e91e59aa50aaeb1bff3d597f00b96a888c808ce782ae42eb42c64c6a31b26aabe0a78581fc0553e025a865abafd9de1b291c308365fd76a6fb163e5cadec09d8f7d73ece93a94a6f9d871532a4e8c36047b86eeba05115f5ed257b5445621f45849e8e2320fc61a65178e50894dc9a717fac7f46ec74eb155ba17ceca7dae360a1c3b357fd99cf32e0bb0e994c921545a1479a5a7d130294a8f6ea0ebf4cc5b8e49a3a9a3263b867cee03a0d1d80d40ba01ba811ab083bf9103c2c324123ccd63e4fca928c3f3284986ea2e2a050641d7aad20d0fe95939a60dd64c3f00fe32c4663f82cbbe8f63a23fe1bb224952bfb218498b003764c30b7c3035d2a32206a4e328f5263d44cea99c4340b98b83dfae67a8e8fdfa34b6e474f0e54c83d5927710375cc202fc536608de9475fedb891a7a9997e46580a523cb546ba8b2af643e35f3c92a85faf797af6a5c51b572f27d4aecd15ec8cde9321bb1cf659022bc56d5f801d91fb044c5aa2824805fb13815f97bacc0fedc5aa37800da6dffe632208f69042b7fa7b8a5a22cec8766394d2e9604cab7d85255cedcc6c10f1e40a81904d3ffa581df8448a8db48cc7d197a874f1bd28b6d39293ab31b8d4f2a89fbdf62b143d6bbc4947dbca4a1c9576194d42f54045f131883312038ba80f6e077968769208f87093849327949ec34a94105e73801133058621e0d209950022c392381328d4ec84f6e72c4094a2473e4bc9c0a411b7815fcdb832c015bf6fdf45a1bd2aa6969cda63f54ea6475973918ea8def950e951a0eafd0677d05b9734e098ed65ff5b11127a698fe3bf88b4b325b276945b6a0566570269a7d9d750476d31f352962ced510a40bea7eac691cf319a75460456419fcdb13dbc8adc2b953d7b0895243037fab17185589f2bb16a237248ddaa3fb7e8f427aa541b784df6905362b9455e07a0efeab638daea546b45cffb1367da9cdffe818b2bb16b9d5b3f161c49afac697b2735d59f5d079a0d87fe41365872f8da5d0caa7351493729231aeefa74f2fdd8ad74b89224d9640cff6073f4e130aec619c6f12523c15c687cab0d75990cdae8232d83b5129a2f78ca15d977c0faa64a24945615ca4c4aec30edf12a584e77dcb818c96a7960ed229624b43f1717955c792e85a0091973052917ec865c69c85664f7dc65a1f903400b06a3cfba8c2db1a04ee3a0390b2fbccb9c3529a914165a03c53529018ac6226d40023cff42ee39dc1cbc92dc9a098388ad693f33ec1a86985f632d0f1b8c4bb0af8c2fcecb933f7607d9db028c74cbaa186a95037e7a3e1c8333187ef20e3d7e2fc47a493cc2b580033c5c41e77183ef09e7718dc6320a749c85a03558a80a9edf9b087ec616b4b2c19355152b0ab9ceaf5a8c8a28fcf8e267906ea0df8fc549e184fe2f485132aefc5347485442f6b0d40b6b78090f0a34805cdd878a6c023cedde6d9e2690cb63fa8d2c1c1ec689d3a703cf6495c73ac821e08c1028132d31506f32c69473a5803cb3f41789b04e9e488b42d786d83e3bb38f690a300db42b61fb5f353e12949fbdb1245f9f680c6097fcc33a983ea9fc782190112e94af03d785a624049f1b8c117f7f3c796c5912802c0957ad6ad9ff69630b3b711eb94fcc3dce3d2e478ce1f56a34984eaf938a221d9b216f3070356994c4c4da1a4f4395fbcc3703be42748c67844e87c937d5a5342d3a2eec82534708be6d0c92b89b536786c48aa9d2b0c93d2d67deaeac7ac4777ae05788d1e8167c29608f154685febe6834bb74bc13e0304643769f697dfb715274849c9cb0af418a08ccbdd89d925a24866d2def03de541b5ae96b3c52c74582b4fd0be02c1e13dfe304451b915c1c5b70b5123daebf445bbb87523882ce738c9e03ead7dfcfd8e8df4bae623efb556cbae375c4b2605b4d5e57ee1ffa283a7b8a653c6fd16668c65ece2dabd3e2281a0f8e8393cd3d99ac8e19c4240a6caefee5ef00d79df52dae43c70e52ef3d1323b348cdfea40119e0ee25df36eac19d7ff1b4d502dd5620a1a2cafbf43f68d809c826beddcc96077a3df78b1d31de0352f2aa8c8e7cf4ed30fc2b6da84fc43cd600aa22ecaec2e92b9bb440f313c7b32f77bca77ffa9da6469ffe3c1c4bc6549f36137b6789fcede95a055908f56d7fea61bf2700c77faa5d5e3d44a7fd5c3410682da294248ff78f57a39d401057b782f9ff8c3733cb249b630dbbd4cd40d8965b6bc39ff90377670683b6e8bc960bb4500305a3593431f8ab76d561d80dcd97c778cad8ca6b889a6046994b744324edf37e34372e12ec2cafe5a32834f5d430cd376c8d45ef9c4330a05e5964313768620dd78f55955b2d1521607c4290823dfc7fb9531fe3869b1f25c259b8ecc2fe60a0de6a4ad9f19d857456164e75db764e57f16607ee1d1a742269329628775938e9353f5dc95edfd81fb2c97f7504fb787956db34bc7109c0bb6619eadc99661289d6e7f0352592597d9a6867dc72b10b9c959ec3ec7f7d62c4decb2c0eeae01266d807b1bb7442a83f23f40b8e42a75abdbc5b374c215d271c1d033ce72babf7edb0ed9d062b0a57c4a8af2dcd39b9af6794655fa99f0ebc13efde13384733bb2a178dd5cd0311a0ceb01d8bdc6cea8353078543f8719be9e32fc1ba541531ac9e7d3ff45e535809fa420573a5caa057c2e100e7958c5cb1c21b1999a4b4fb52c4ab423e1ff7dafcb4ce12389c328718eff46319a1a07ee6db61e0bec5f44b6add688971b285bd02644f463c3cb9fd5b3bc985d5f2f95e7cf148c331dc910279533aaf92b6530e80412dd8741362086a6757b5decdaac489bbd6f4a8fdcbb920b504e047a532e6bfa6d433a1e806d2f4c0a129b1afbcf03a333b8a0ea021f839a5c0f10360b3d90319efdfffbac6fbbf015d65094fc5fa1e90b24e02beabb340e7f9dc3b44020df735e292ff2340de381ae02642b1a2058efc3229ea58841722501ed2754ae6a4d5efea5abd004f608774c2c0b2d047d895b50241d2cb9fb5f477d6fb63227907f16a0eed2f9b08ed76376d0ac131a5bbd5176971726150d2fbb323e23c835ee438beaab4a5b733112752f5c892e37505525e39c75601651da0c81bdced20fa3eb4ca60659c9583e87c8425941814d762a90c96fdba2a6b0c729e3066b65982c787216c71bfdab7f1eb905e63a8d8f95040198b7f72845ceda7638932bc85c3c83dcb81cec3dc4f821f67ac039276335bd3a49275a921ed086a9b829cb5c8fae10263fa2a0378552b8fba6a64fea433c4a1991bfbb14e971803e2c9eeef412a581968e6bac859811cc527f49b554da82468dca5b9e5ecdd987ea139452e0ef44a5b131216eb6d19a1929672f2184aa6de2d5587d8061b5f0cbe8b737d74902f935f5362635f1ca7464461aa34cb5e4a4926f75defbf2b19a1697c9bd3a3f16adf2767a432e608f77128fd2124380863ff06b0ebdb70e25413ba185fc0e788592aa8415f0c398fa3d5e75b5aee0f2762e70030d53a17822284087bbf5bfb30db8b2b519a3284f9261aeee213da7bcd04628810bdfe946362f2a9bb301043ac830c0a17880585c3e8ee59d5bb90360e8410a519877d845eb82b0f75112415d47b274b7ce0ed52f74eed99b18f135a0e729fea9dbc7f8b4905a9ff788e04ff10f924d2723053f15b1fda1df51827a3458b79288cf2276a483ceb77d16a09e59be927b4d34e9b933aa8d646cd8f0e4aa1937bdb5db962ce0cc64226e4010403c50df40d395061dfd7eab6f43a122c49c4c40e75e6c72eaff4f44db74563d87f588205bf51ba77cb3ce0d1688d57f1d1585fea3b7e9b8173656eb86f2b6338bd12a2035f7fad327f6e00c42db46746507c4799f983159c96d299c94e43ba467040e50fc5ed8d2262d88bbf725dd8fc56881d3b2770e7561bc21bfae036351a17826a3ad38893d1726e301d98d2b9f3f78141fda1556db8846197a11d37b51267209921d959a8cc24c780e1d61d6714bbcf50229e5a894c88319d0287894a99bfcf439f5b0b26e1368f9209bc6cc7a306787f03ba1be3e4b4c5b7431c859a81f8549d65656cdc8e0a6e378169244b35054ccfe9882b31ea49f7b126a131fda27fdc10f3a1b1dc3a74a4586d5e3e509de1bdac8b1ffef2e6d8a29672f47e76b4f34d4a5408aa716e1a3e58eda3f0dfba3218c5f227ca2384a6d5a74284bec16c612f4c58b9162ec2e21b4c9a40d1ed71c26734ffafa8ca7e210462cb38dc93e3b2aaec00a3ad2cf4fa6d286f42d56266edfc533660508b2b4fa170bd76581bff4dac6357494a8c60f9a3317564d57714ce044c9f6b1b88e2140ba0c548efa99cf7881c2071aa046a6385e22cffbedf5fb5e7a2b55f90a4327899f2b2f17575a8aa2b1633f515051de8c730d97c2f0d1ff7402aede15b4500d88596563c7196814147c5e8a71f2b3c5c5c5c3d99d9e7ca2399b78d2ba4416444db2d93ec4406d0e8da600536e8fed48334b548dbf5591ef75fd7752f52f875b9bbb6f4f57eb5d5a439fc9b55b6053dde2ea8ff5c87b25b8f66b05f047337f307b13c09a880a13cc391dec3a239e5a2750683108a89ec465ae7d4a321f8d3cedcaf06217b4198ef35235002c620a567814ff5cf9457212e68e98116e1d73e636561dc153062fcbcbf0f64556c33049c3ce194d8a201c74e9c743a1e7042c5da850343e15ad74eda4bfce50d489a5584ccd5bf3d1131a58d84dc41729df8626fa0eaf4eba0ea075465cf9fb507220d8e20a5de025eecafa75cb7500271a8ff2b5fe268f7a0b1eefbc155256207d335a9fab16d46383f46e0c6e30d850e690d0453c04a35c88e121c509c648a8db0d9d3bdb8b5f8ecd1386a8a8771ae0020430417a70cacda3868034d71b762f3f94adce5c40c9b48c1671115379547fafa832ae468f621051cf1640059d5a3d748466b0a6cdf4ff934c987839ad6757e2f000cd2e98804fae40745faa4301380a5f01e0a60a53bc5921c2878f3d854bc3d734ee19b093e58ae35eb51954c84a0130a23b33ba5080f238b85a43b47c1d9bca9ad07d64840b3c78d2b839430217ff33f1467206a6a99c6ebf7595a9af3368bc191a15d05d6716595f2358224e8ebb393d1b3d981ec0e1bcea45a09a3517a4c781f55e12887a1632ee097e6f95f23473a79a2584db182198774b9b58bfb63761d55c357c319f07a5356e5c3528e2bff8640ea04d95ab95cfae2690e4d91d6efafade9f375673a6ce0f158a46e661e4c2464dc08f83abf8964911591917a27df0acc5f439c19d381e38ee16902e78be1c5cb1f8ccbb46ea63af26c7567d4ae3acc29416b7645e7c50ced961fb50206c93c20ac2bd01a62edc5e6c0bae29f17fe4f03de59fc55403e1817371fcbf4874e85a045ee653a4c675b70561b3e2185957692f19d6d0b80028a6a00d3d018088b7e87b2a2244af5e1a7f758bd4341daa78b2af48c308cdf33ba54ce93e1066066aa85f2f969a7a8245e95b50665c43a367597531ebfb3622475281e99b6acc813129a47f6ed84bdb8485382b827ff4099b8d813e1af711dc1e9974b07c7ef3f4c7d16c403db271b540e24ef8ed9f11d208a58d9f796b2c5c47339d445e47b2b927dca459605019fcb3fbf4752fa690f7a036df8676bd30cc1f465ebf81681f70b645b3af8fddfd3ba270bc6f56e609334f71ee330d84edc1b74702edf5d043836630444f5e18d2663fda0ca0ead19603ab7de96960779649ec85c959f43a6c0fea6df724ca956968e72d3b6e5c561f3d2f1fde557d9951a876d0e0be5e8244cad8c56a042c8c104d2a07f45f208ac1af64ed94efb6d094b686ffc07521161844fa80e106e74354b4d010e1f6834c73baf8e36deeaba1c93ab6711ec3cf3b4127547c16d7ff7e4d10008e5822e49c6ca38614e19235d1a1349ff921c2275e5c7504990acc523b83aacf54100a4c191622a53805f47076fd001788cd393e0a68bd689129fd27f971c7e7a24bd581dbc5b1a5e12bdbc5fe469a2c6b17eaf90727257d98d62362d259ef9be63fd518821f2fb43030cd1a794b2e44cef1a427347e580553f57a147fe4d47bf711b27670a5c48b1d74990f542247d4ea04fb93bae753225c81a732636a995025145e0f8b8be9ebc0efa403bec4ba5791e06de7bcbdec9cde8b4ac45fe93b3947ddf4d6be9ee417fa59efa285bd6207366fef9b0c92ae71ed1db42e601158b7a1555e7dee62fa614e46ff328c1a36471689b455378f9c0826e23dc19d1d51b0e55f4d7ecf0bfef69b37d47f983c72cae40a9bf2c44555bf3c562ef527c15c4c5a4baf41c4424a475d312938e6b4d1d696bb7b1928840600eb83aa463fb382adcf5a378639e0c93b2096c556cb5a4a0776ceb3f20246d5e8597c7aa31ff3c18b1572b163c255f1c95d4900a22f0304863717e42015c75bebeac01c0c2d318078e348204b30cba6a582e1a0aca2ce98842b68b93676898b191e8c8b95239acdd844c0d3e9402ac51239d2e09ac586c0535664c8b4d9abadf0d7da86233293eb78af1e8e5fe019ca96c2f1c8c769344573d3d265598bd894caeeef706d21d4ba0b51467f3690fdf858a64d2ae3ba81ed6c83a34e6845b6cc232c870f22bdd46003981a21ba8287acf374d7effd14dea71f25d75f78cac052da4c51c4484b30c5dc3bc518c44f517d445986b533f4badb7c623c83ab424b12e31331320edb4b2fc2014cb6b9e372e5bb4013cacb61ee9f2f36b854b2dbbcef87c544fc61cd90f6f252e51a5231494debc36b817257d524f560793f0d637845a8d32a5f8bad02b1161e0b5ee1d1957c4740be6ff2c6c6f548f7e74550577f056a524587a39f9e361d3e6fc18254af8e2d7978ce87e0d19ec2e54b4ed82ab14d4122fcebb3645513405d6e661328ec4b3a98b555d645f345e716e93458340d1355781442eace52afab7de397c993e0fb3e59fffe107ac084e9b9f30d416de43402dfa5f876a15045ceb14d0a2d238e9cf6a2066adb9944d308c6efee64060f5645ba2d56fc9176924552ac894d87d4a066d53f2622d51dd3e0d64b6fa3b15e6059619b54cdfe446798c94624fa68f2cd33944fa8c049dd53d370dc985b5afc9cc88e36646bfe15e6d20d5df492b1f311b9a2d7ab45b2cd9f9e6d45ed7d87c29c13d5dc2f54035d2aa79411aade5d6fd75c3ede67b4d82d8dd42d09b397f14baedb219ec907be9d77b1b9eaa680c3680ad4e709ab9b8e109ece9217d4ba1d703048d64f3ce1440dfe89e9a73f5355117d8941a44c45f5e6af619ea0c3336a8ce259c19a47e989914bfa7bd283138fe967ba42416e4dc1d57ee1871bfe6f4b64c36c9843cc2feba167d93e243dde62dc61861a2a5e5e04939d86242b14f5cdc44cce15f3d91a8277314f38dcbe3daf84b8a87ea5f886b2272f88d19318203b184da3c70f91e1ed93048954f4ad3629b3c57dff84e3bcc598e348d3ce670ccc61ed39398b353002f39ad33e1f0850c400a9c8ca3b55b0bc37d030e2f800f344d2a5a82fa4f0c567eb206a459588c9b97fbba4e29692196023f638b573d3cd5f7264c61e2671414ee1c61a3e7fdb9cde6e241c0d98f3ef70473aa8034b93ec90d94c37893fd29ba37436536a82a6a6e5b5c5081b7299a05c54a59c4ce56d93e148212d9243c995d52a1248a99461ed27b71eb301f5f86ad2852268006a3bb60b1cfdb84cdb04864933ddb9924794ad8cffff2c3b0b7b52dbe173d8fc9a5b640e8db66506c7316a63663c0ff105b8ef24a63b76e253a1a5ab341434de2ed3dd1fde7eb2a0b7ea7dbd876279bed6c04e3ba3b5b7f8c447b702e5d7506e910b11c46223c2dbc8ea9fb6011e523134c5b84d833d7cd498357040ac3760bf4f9781ab88898bfe83d03a790cbe37423efedd26b5e76624ba8f42070704c20dd82213a61755943bbce50da61c4842742e4643d4b6e433e53a43d6753c12e741a929ec2b052562d0fca5267d910c054c2e2746a49fd2f01c532a53ed21acd756809f2bbacdbb5d0cc814e8b1a2eeb0493c603c258789df84fd650b56cb1d129c0905db16fa04f7e339ed96a1d6368e48fee7b1cc5bfaa337d08f9da859bc8abf6e3cc27a6abd91e724a3aaf66dc6a41a0dc165f6616b2e4dfd59fee256ebcbe91925aa37b09b73d96011c57214c1c97de6d0affe5690e168cb359c30a5a5f8e1b3173172bf8a7d2cb9d127a626066d4acf4ae25553255112b4262ee13510fa3e31be64123969e6d2c2fde80251fe998a25d86422fe705d14fb94a6ea305bbd86b3a37674f0c243ca79c0e4471b2fc3fc80134a7fa42493835c234aa0402e851ae94dc9930187e5e198a8ab7a979fca7cd796b14b65ddae49f04affb82473b14aff99f486f19f60e4de423a5f703189ffad874e4aaef6863533d95ecc247236780c7eb4f214d7aa116e73785d6559f73b315e4de82e2ed3b7bd0206feb75358ab2203f4da4d34d0c4efe752cd1e5dc2595de2d25b57246ab93efa18a88f418aa3d5acbae450a6d1ab0730cffbe666f9ffc5581d0e2301c26a02d57feb410a89e7f5b41fbd066f82aeb6aa60e0fbca1b85ff73d032673c7a2eafcf4cf50895fa09ef26438407b3572c220bff20ce1e8bc8af450a1c4667122d133c76c4fe20e3030d69bc352016d9116047f7f870c0a4fef49265b693f854f6b19f1c03d52978857b6e58998cc0bb584acd053ddff9919cddadebcc73fc8e78a18240a23d27f851ec3135e4131b265973892576d2078106d52f7db3c020b6b24edf14b7aadf803ff9c834daa37cb88309bd065a4493b21a01af17d0f310a03d1dbd81c1279fdcab49560a9c7a1924a3d1844e56c609a318cd0a763bd88352df003a484c33144d238aeadecde65bd9dd331f7b69b5712d5cbcf7d5fc6a42640321571b9c2b36114590fc47ce6f062d91e1a741a40348c309af3564ba6b85b76f9c8f451d1d6a6c8538c397b426f133e35aec6e9499273e709d5302d2037b60fa9056b04751ea83803c6757cce37ce8054ea1020a675cb5e06242b8cdcf81132a2f648538aa6f75712c6867366f023344755e5c8522e5262645141487aa09cc2cdd696f9355f9514626f61994dd2a2e6f76fd35778e2055b2f27bdcba65629a5d952ab1012a1001cca7aeac1ce64de8e85ca543772af195ada4afc9963916fcb7602d31672c7c8fa008865e98548393695d05ed78665ac5aa96b4372b06443cf02f4a6295fd62075e025bc054f88074adebc7af15eaa9521afec1cd16b4056ce5699c9411f3f52d9d9d69632e0bfabd39316220e4993acce9f86e30f55d814648ceedcaa8cd128290ae2dc5f56e38d5419911f02388d6e6e1138138957584edaabf2038c67b1da603e446d3e99db22012cbdd879d3626958e9644bf586ddf21f28901043b5608cf6d3e586a05dc6c2db28cf01e966a2457644f7edd4a6e1953a273e95f5c4c7bbf2a50458cfa5836ca06e43168d3ebd9593991e09bd9a2c07e0fb6394233135463b11e3eed8dcd814abfbf5a96c66f133d873699cdf70ac42c2bd3b2b53311d94d2eec896576195cbebad383e05673e4987578db7bb97e655a62b0fe78a7fcb6bd5bc2f2698bd9eca","170303001211ac0ef9144a7677f976dcc60a4700f48a84","1703030011c72a7af9b3b8b85a178262a76d27d5de6a","1703034011e17e3beeab597668740d68e613b2ffa26a8f574412d4648dc0831386d4e46683954aadab2b80ec4c7ab9f00004c52f1657b8c70492b86a0e5e1891ba3e6313993d44f5d3f836487796c599a04fdf9cc9b13c849a57b5819f81a5597abe50a90e448896678398af4c6fc231e4ac4131a85dab4cbf3217325d048c9d6f9dccdc37622925a6f5eb92c3ea090352f98cb77a350919c6af772cf6032cc121aaa2d72df25ddf20859a20d063420cb0e908717afe03250ff94320d6ca7d669314b2de7932cf0a3c69ff8e35a5df496a5f92f6d1f0ca03d6e0eb04a4d23f1ab6fe755e6f6bb79affc5f665a0c0ddcd6bf61cace033a51b573b1512a2d6c0b0c98b1f9542c26336e598b5ef9cf103b1a0ced4596cef4d133a157b68acf0330d5367cef8feec81b4908b361224e68d4f0996ef96384d74419b8c8d54bd15d88cd9af5db69a1681e61a02a092b77fa1d315ddb9f6b4a77e1424c5f3ba3c113939c4131643823322afdba05076c926236d92731659c9859c6b7555f80bfa94c5ea73a1d3bef5e2f133f9499013a428645ab6707c6c1d02c89d5d69ac471e307d49b5908966cce2b3ae6c55143581c33ae7b3b1371b029941ed2a0b3928b41f228f069faad4102505c3c807b8d4a70695e8f319c238db0c841f3d13442093d520854b33c9b3379f830045ea4aef98cd7f59de4c8fb55dae964764a1e49e8ef2c8934897858fb783730004c5965848e0a8c69887c9607568a5971b70d501fb6b82035082fb974c5289418a14feb2544500bb26b94b00ebce968919715006a254b8b442e92f5c97fccfa5d5d587821511130b38096e1b58bbf24719c500bc3d8c585f749ed1330aa5480f6b77583826c243771f5efaa3c8c227848455a984d235bf7478c86884fef9fb5435cd55366a980159bc1c1af05456f8cdbc84b1c0b9bdd032fa30501d2f9bd362be9351889b4bdd3dd3083940bc7c3072f6165677aa851868b287a35685c983abef32b01fef124e7743f5b489e74e8b35b9369637ca18d7a36ddfab35251248d03b85397faf111fb947d22ad7f7400150ba25f94b71a855ab18be60664bd4d1888df1db58ca71060c6ced63ca9b63b50ba3d97ce4c6057ed65645d194465f9aa42531a5c365aa4da936b5101e4b5d286c482ca3c5354ead947c43084e08641cefbd49115aae4f2ddd3035c9fe7178474e5f879819240e6d2eba906c1af90b6f3feda79e01f99cfd4cf6051c3a7b23d1e3fd39c00903c3edf6820f054ef2874bd5c1303bad5d0c0e95854098b65d332acd3e47a8552d0b7b9948a6de06871e32d2a2368e363f9237859f98732190833eda7d88de9e4f1ae856f6fc97492868dbd5b791f633cc0716808612e9e21e62b47b7014117532ccce580f85c51561e7a631b27babe4834972dab87d6630f00a367dff82a08370bd49dfe0e2d9b0247d9b2cafb0ba73b7c6e4f121d9c3c1ea00bb984c6495fdf45405badb1a0c3c98a648e72ef07df666caf340b75281ff4a1227022fe8111a4aa25c70470337e81b9e29fda4a8ca3bc602e57248e742c142ab41417259e30104abd16e7ecb9dcefa6e6bf1c551d0ff8c48ff9634b833ef893dcb905236fca13c96904eee17b7232a90754329c3f3f78c22d71d69a990507d2312e89d127e299b25905e87dc6511c1066bc8f70ade04d5ceada01ffee262f49f8397bb3da16b6dcd63db3e5a7cb0a3b1db0c6d3285c160648eea415122b62ac4a583e7808acedf480454b75b049434c29829a9a826fe39c32654241396de828f5ba8b47eeadea9a1e76b141c911bbaf6d37a04ca9ccc0ab31fbfe1156e8be3634f9bbf4fa4b94b79345c099b1e8f76a9c7207f930d628c7256d1e4e5384ee13bd10200c70b62609b72b13a8121fd6a7d1e652ee8ed1bd50f8424ffbc6fd694edfd06fbf4ed35bc49fbc39dba7891eb86d580b37ab312cec381879852100ce03709ef62b0ca623d64fc3041c4d1a041ce61bab1cc05224ecb604e206ce677fb741efed92b2d7f241d9cbef5c3dd122395bc41dd7ed4ecbea883fd1f3b8d6c1250307345313e1d6028117be3ed976b96689445a47089442964cae99d01100e64c424aeb0e2ae506886cbe1d89b80914194df8d95875a29cbdd229782d8bb02199d576e985a16619bb1fdf777240750efaba96dafba0a9607204ae24512c00ba5e80be7815ceaefd11ce6e233a6aed2ccc58173725aa6879b138198a25ce2e73c04c74a300ba202b896cc6a7af8f5730dfb381ea50b1a1ab14815f522936388da36abe37810bddd40ece521db0d2919eb55724e08b221f48adce85875bc057e73d63a437fb81c9ce2eb97c04a9d7c57f19429747c718ad9261165ade0cbd3c66540027dd3a80c6f2298aa8d8cfed8d9ac98dcf47ae07dc77980b23bb11fd450a332c5eece43380be314462caaa479aea0f892ecb4a14c6a4003f963f3e4e90f4c935c2068b5b4f8056a8aff747f5aa568697eb355267aa9edbee997b0c11c51d7464a6e8ed84f99b6145270c33564f4fb76be0e4131f8b2f85117d9316dbc687f99dafa3135b70d38a051b891e7d481e37c427e4e4f4c11d712e45ee7a9374fe701675163e636e82a9ef0e0c1df4c9970b788facc8f0bf1480874429e5b2f7af3092673264aa7af20be67ea2f05cce3d8eb2c344a903a0f15d1c12d66e0c0dbb15d67128140e15316b78fd704261785d1911138a2449c116dcf6dd6d0d6643c44452adcf50d1172a149963293fafbed59987e183f92a21d9d11f8b6bb4bd7029af1bdb8dafdd0b7f778e49a53c442d69526845380e3ceee8e01dcee80b0f55dad2da1c058cf2005bf649a339e6843303662353bea3478f72e8af0c7ed3cfae860da2f6723e41d2df34210ec112782e8af66747ef1ef458ccc2219f91fab5768977b2310552b22249e2d81ac4ea27dfb158f52a035433f7ead8739ff9d2211ca41039be7c3dfb37f80a17aa7c95ae0c76a5d885096ef59087e6a49b0f08f41d37727f4ff405da79a8f3a7af62c3840f8ebbd98755f3a467412fd1eab8b617cfa5609025e140d46f0aff33e0050726a1e47206a543243bb4fc11e0b7ed3e3cf2a107d86d50a8e9beef565002e0b88054e6720088f6cd33fb332b965d877b95e9f4ddf952c8bb6ffb9dab6c7194877493772b6ff0913a138d2ab01ecc6d689f7ff773973d0ccbb0b47cae042974986466a5968b0843b684243aa4a4c27380cd9a5e514de1aa70570445a8fcd0850d3fe4e387036a30c2b457355ed677ecfa0f69a7cf11222c026030e7682d308da2c53b6b4049a77d6081a2c53acc321da8f397ccec8463c0688dacae6a59e3acfb3d2853a10a23d6ac6a90b8c11b44e5a853a3759edddae4daf3f99c2ad0912b904f794122495fa7e36b4a524eb242f7e7fcce00cc7a16598f0629efbf5046080b0bb640c01ca5c74f0060dd20fddbe1e787cf3e8ab8d834d234d1e9c47dcb50ac5723109c7941e8ef90c5273a60f74f0310f1afcd93f2a7b68788539fd46945d9c8b8169adb284a753f054657bd8f3c7c108c3318ef027d45be348b98f68bcd139148f78dd147a2afdb2b698a5ca2a959f1d977fa38367fb904f590a5bab9cd9287ac6738121e04999fec50cdf2b84f8ce59cf7b93276ae26fe7e3e4efd078fa4e1aa0d22a30d36d1b75396e4eb0252dfd16fbda6ab1ac60ae0d909dee80478eed42511317a755292ddeaca5edd3dd0b27ccea09df5fc14a5070973d1ba8de04ded280ba305003e636cbcda23677c8491e9010c3676e82ca0d4cd1f3575d40ed5fb35c1e84095359d40bd026138563c71181ada304584b7461330636fc61b3059e3f0574e781d14beaeeb8721f5421a60b1c348b7d301f58a5a3ac3578e006801859c5a9a2c0fb141eea556691cc2bf269e193df212a6f9ddb34bb6d4b5c522bc977474a6e100a645705b5251f21f34c7da113f27e09c32807710b1afecbca5b5e451c652a55bb4eb0af58c4c24f41fb1050deb45f30e5bcda1d9c61c1c99f02d58218a9d0cab69f77ffb90f4e99bbda6d7dff4e5533fa1ff4fac21f77bca60d22a20150490e45c3cd190cc9352be6a8ac989818c2e78b40d4ee81cb2892df02c8fd743225afc7e75c0fe9bf2bf69cbb916eceb89b65df15e0d0c0bf1411a2de413f059b7564954652f1b91a60eb7039b880081b5bda72366f6bef95a10331555c5db9357a91e06a9bdffe4a00562f46adaefcaecd18ca96251a7537c916e1815edf82535e182c69317f21dcb12e4fec4b0bedd362a438120625833e64ecb0d3d1ba362d7fb64a7eda7ea1284a68fba36b770a4ad81b15e114d4d36d1bc39dc2ab13bb38078e477cbcd90df2386c287f14af25a89b4db2e377a13e4b1eba90aa34c02cccc166de00315c5ccb1403ed8c6ef040584aee52fa152db8c99f614e9db4a0d54ed15461467e353fc420c47281add9fb11a6b2a906e86e15a2144d74790265f3264cad16594a4e3184181d9b6a1b29c43339e40dc7601534deccf6708a17fcb28418d04cb998343d411434f95ee06758c66d44f476abf4975ff254a7bc62ad97536f39e4bf4feb6627d176af170494b689b3b51caee8302cac547dfdb63e049a9ffa09cdf8e52ea7b404d556fb0f7297e8c41f54102cae2730f0e215976f42f43b99ae7a8f67d098cc9314653a6070f75aa6e6aabb5b29b220df08d6eb70e7d25567aa7a8f521dcfa5d6388d98ec26a3a365ce0e0ce211f813146fa62b1b40eebe671e5965bc76882d6f1d649210baba0f9a7eeb88ea5521061a04363258f05d5bd5782372e97b0722653d752291ab829da7fa7160ac3375dfbae0a4fed1105500bd83b60b22dab261929c16b113bc5d3c54151fd0df7154c89aceebe9e1d7afbaf2e91a4b910c241d7367e409610e97a2a150cab2263c83d74eef2ca045400356c867ab7c53371fe13c95e2bb146469da33fca842bdcf83f1d63920e72ab7aa4a859cea1c84ae673c7a5fe3d5c2e18924f435efb6622acd2f50ab59dd838afa45b9814241dca9351c18577dc82bdf2cd7939da0bedbc7ea8221d64758107e0feb5f9076d6a7a4178d94cd97964c0af4cdd91394239117dfd2b77d72a085b255e03676229de1056e131af03d0f191d74feb8214f846ac0cf845a9a635b602750da682def9e48f97086c59f8e2d250c3ef9e91323690002609caae6e4c37b6152d6d3b04856059ac3a5f15af9444cc5e0499573848d64d341650554594b9f9c8393ca742629c6ec2e5c43b92d7233227389990c49a496fa40526e1eade328dd3aa6b973f9c02bee43c36045fa1b7c9169c9bfddb329357ae665846fac7208079e8c05c2c9ab220ff4bfe932410907b2e311671d77f1f3358e0100014da922d9371afb51fbf20e91cd1627c2e5e0c14d83f45759811387e082bc2910c70f76476f37639b196f3bfb17657eb25978e2e896e5580587d29d6e0ecffd7a2b086707740af910ab5bd9fd7c486e04448b25e01ddb55804db8c577158feacaee2e829f9ddaaf518042b918190f40d0a1e2c464b4309d455a154535554d5e785092d06d25587fd3f7a8e735b089ef765dde27f55a883c24a6de18cd46f4c7b20113f28256602d8387f389fbeea4aa0bfa2d5c6d75d8514d2a912aaed32e7c0b15b714d37cb1b4159a19c20e5c4f24e1e9fcd8e9541131aa6d90772fa146c05c287ecf08118be289e15100fdf7f796f4b0373ad7f8421f3535deb44c599608ea0a340c3807873f77d44d4c8755ba51923671d92ff0997319e565f5fefbf4f28c2f06ed6fe092b62a1fe7d99b1843cbd60356662d0d48846de61352655f23470476439dfaf45f3295e921d28503ada9b2b91b4329c6814bb5977b8c4d19d6cf63e8d41aa74dea8cf4ce09435e199fa209a60176e2f0846e3b8b4858a66ba00ab4f397991be78ee0dfa1b06520d46319fe8c15ddc16a9e540fbf26adaa0bae46ef2ef60b69024bb0f3f9e87047e7d066c4a6388bce0c533ef8a9a1776223bbd7ef7df9f722adcea30d8f62ede9aa4e218c148e834724e3235813a300e6d3c2534227c61af4e94ba8b54bfa5f5279b8556081b7252aa33644b27b07c6639c296411e49d00b866ea36d3f056faf44d27c11079a8a9d1121fb29f7cdd55073467e70a45fbc129f24911fb19eb883cea9706aa43ada903f1bba6522b2430f2cf3d655a7364ce7da3d57e23130bf6f4d1203faf047799ed113aef66fb1ee255c4a1ea5965cdeff84343d7bd79e300b6c7b296a6b3a1038c7096f823e3cc5632f92dfa4e4083034f16f5ddb335ec95da3f06ec735f666facd07c6b6d027d35721481918adb928607afc72ab814def4a606181e87ce4000b325970202f4c3ce90629837532d500a8a56618d56c4797b3a8a7685bb7f9f750c14f116a341cb8527ef8089c0d9e94cbd374b3d0f8f403c983d16d510fb8a6e9918517f60301b3bbe17a9f6b9dc07103acfdc24ea9ec6bd8253f3e6f449b9df9903a9ca3a3cc309afddc9b3d89e77b9af1fd2ec3c668b4afa4522f3c0a73474f8b32829b168250c82d6cb3d9661c6aab9806ccd4dd08d9bc0c2754836801d7766416b7ae1458e09d5a72ce41dd420b73f8334c0c7be33e462f734aa5ea836dfaf3c3e9576536682839c017a088ff8fd5c5cad6b65f0263f7b5228812b60e2d56bb28fde9ea1e9dd017687697ef83644a88cc68b68ffc066af9ec8c105bb4bb675cc514a2ba862647e8e32b7c46588f1faa999e2cb490067808882975c945c941db42790b117a9811e9def19185df7300c6474f34a5a0b0ae44e07ccb3ef9167f680b2ec6afcd8b63ff98d9461d020b4895541f06591432c7e9e633c0f33b7b21d38941a8b3549a571813ba99598b8b84f134c4be550d3227c79b5ebc32576c62d4f2d42b69ba18850f410e11866e88dea6c3e15c02b0424f0d63c54db4590a1998cc7bb429060ebb04f02a099615b06aa8165d6f45eacf471e50aecc54ab6d2147a1d3bed1fe88246006f56223a09998527150e64f60d89f82bedf451942d3855e054d4ed89d06231de399daba20993e80e4323d0ff5d5db293d724bbd4a4088beb26dcd477c3dff8fa917d67c6ebfc16088a9ec31bc14a2c1f1c633e3dc1d921d4336845b2acd09ed62719e5a7d39e2ec5e92ad33a9ef72b10cef63f50f95e5783b6db5ba0f9db08910ea464d1fec4df5b587a604b0d70f29115727529907986604ac38092b0eb6437f5cfbae5d2d3c1d8999a93d64475738d9a8d6b66b62e37762de2b926c490210ee848a44e14be13a6f1e3278ec43648e7acb848b4552cab7e9c08cfd054734fc8e47591e5af66a203050725b65de5e3e8aab86e24454292d76af5d098bc118503c589055276392af8fa35fd06ba7e803e7a5fab1ec765fa2704aeb55112bcd14f3d9ef27a1b875458fb560d7b955a140037d2670a17c9f8884667811c8d373cb087eb38edfcf5c7ccb3cd5bb005c69bc4ea060891d52c1d333a4292a5264bbb91e8828c16c187a13a6270717b5fc3255c2539fadb0fbd6d564b2ff0125a2f621099ad7b455d519aae8ed2128d759551a4cdd129ef10a8301dcf046c0a58a2eb0e0fab5986218b2de5dd94bf2be165d0db76107f2f40b71847765e948cade8ed8219e9a9bb7e30e9c420003899eee7d8a9e0328d01cef974bf4bf8271fd2050135b5cfd1d761c37b2a3d02bd677e74ca6803ac5b2313973e246492e8f9133c3c270dbfca31604e05fcd7cefeac233a97767e7bbcbff27cee094725b7ebbe39808fa01c721b495e973be2cefe78295fbc546a5773e56ecfd09270da20d620906096f60673b4901ada0a0011baba83ca3b0e0b910b566155cbbf9f539fef61cce78882133a1d35fe13ab466e5907f0636f772e5ecdf667c1a2b17495e1a194715a785ac66be57559838fe0d21b3cdf2f6d7c672fb605faee5e27fad07e8824d887f5c6e1a4d62b38493badb0e72f3e52150fe0287b36cc5367005c9d7b6a7d79f65801096f01487f8e5be4c820e53e63c2e215b68a9a081cbcd34b35612ba4afbe8ce62a90629d10c2af4f1c1ce4d03bf20b93b9f661ee8900132c23928aa7dfa9e1c3409f6c5a951b21903f52f14d7c459f4ca58bb6e02a6c84499bacc09d365c1689bcbf10b7c75abb59a8dd6410268c896ea82c9cfe36d819569d85080cc38de0e6ab079529ec042a773d2bc6906480bf9eff3a59d142e6340dd1d4980c70d342843abecabe3d28a04eea081e31624a53a8714ed2b30c55840bdd033fab8c88ceae4d49f26567b7125625d85875a7cde0b9efa1dabfe472d767a30f6401b146bf6054eae0356338661d78626fd09c8cbed5a2e299870e11c30536c7e2b80efef03701e0f47a9781413e27b6acec7830561c952c7b658371ef6919742366f6ae9784aff4c1c50a4d0530046474ed2e4edae8298de68b381683c4a0ec3913ebe4589c6e10ecdf841e089f22d668e0b1ef328c26721ebb57c7a098ae5fd832ef21d37ba20f2678bd49ac9af46e20d3324e86ef4c04de807294261ea5919ba6db7461f1391c98d7d5a38e15c471405fc28441cc3d83197459ff86a48ed764a06be294bdc3d9390b4d38b486f6dea4e94669877ebc56dc27483190c1ad67ea433b0ab09ba92de1df6e7ab5a9c3ea01be994a36715590e6f6fe1c2abbb482ba4f3f7697e9b0f048faed6d0bbc1f809411ec2b5b704f545136383e3b2e9718d3c1275ea851dc9253482b6ffc2ad9ad3b536a80d1e9bd529fb36e47ef26e74350879966988e9f904d490ce3e2940ecc1f6a225c46b35bcb13ff8974c3a670ae10fcf64c346406e939fc83a23b94d0ee52f62007fae2b91ed41c0afc4adb54950b2f87e6a12ab6058414bc91bf51f8eb414732592f7d1eb63a0422c310f55fc43fd8b6a4466e67087dc8872eef2f55104c11db642cc9b082685e8e388b25857d9d8b7fa34f3efa92e84977e77393a05e6980b8c65aac0a9b08470082c1abe7cb0d0a346ebfd6c3371919383a34850d849f674843c5d6489b4a93952af0149b29fe739566584e7adf594c2594080a2519bdfd7d7ee1f8d25606f922694776c0befdf96fc148d10b0ae7a800c46407f5ccbe25a0362254942921fc957fceb50206f44386ae2495c3a469f76fd8dd841b87ec1eea23c02403604dd2c485ce2000043963e1c68702f2dd5c4e694ad799f1662bc2db18979c364f2f229cd6c9a56c160edc088b879d3a4312ae7e649ec8c0b78644ebce266ff726aa5558d5e47fae12e37a34b356c44c227fdcc807adff26ca0cfcab9706c692b9b7b8c5641e3a006b2072e7111e3eece9728efffc45fde85c7c43d251e4e53f45518160c7b738f0a569888334db9739314dcbc71f1a195bcfe310d61a98d6423c8582bd2a28d97a38609a2ed09de44ff04799a0f86ee28a375edeb1593c474362aeb51c9387a8f25a1201b4a6a7ad71c8c198822c545dcee86b6938bfe95c52d28aecbbc53527e7ac982c75c1199014faa20cd903f12d855a1032133453fda861d0163cb3e0d29d8603216303dbc73d25f7f6dd959f7315bfc1133a24f9aa2bd58cd6bb8cdfa1a264b18b21eebe05d46195040baa5da8fc8b731b5b5e30cc35d48b6ff3ab079ddfa699a64dd3410e15e04aaf979e0444fe6de76d204dbd18fcad23580fbb1dd61c5745e6678a2fbe74ea63de47cc31403814c2b0ad1136050ad19cb56ce472ef09f616e708a4d545d626556b2e0addf945920c624827bd7af7e3631d79b9d54d37dce5ef2d9603ad2c8e3c7c93ee717d269657a3e083743a747c3fa787de25b02236f417e5ec857b59bca26905db830b7bf94aa0cafc7e47d0d5e8d48f345de5e064138f56583ed446a9db619a2df112b0a7042f08c80ea264b96a218362dde03c798aa234a4fb36090dc7c925ae797a084581e5c921f8d18094e72f6b8b30dffc16e7043863f7f143b803ed777d321b5c26fa98bed951266a5d28a117f0aa76c38515b792e49ccd0e6c556f14bf70a40a213afc04b628dc00d45232474e11250bbc58c7d9a98ed6fce77a629d35c1649823ed96f1e7233a4311cd21196b85d69389fa9b1dc49becc6b38bb302745683d6d4d2f5fe4baf1756972c156cc451ffa06dea8cfe449fd2c881e3bcb171304ec4bde050799e16fb18de54c6698ec6e6d5bbc794522190a57b8e77d06576d2819d7e8a58ce586b06c697c995879d6945ff607cbf980604f16d8c2c7125c47c36c3c6283a81e7db1c8e4160b1f392968cb942f1a6f8a8dac8001191a067c1b4c8a5c851264f94463dbd8f83631989d6d248760836bbdbfcb93eafc6197e400c963a35d97df92f69b8036e9fa67609adf6e68e198f0f08bce29621711b64bc026de2276380f50d26d508613dc01aa5f32175cc3fef49c96af5afe6a9b4d353ac0d21a72c44170532be5bed8b77d0e5121611082e188d5ed3d394a48a8ebfb46a5813ea7e1c3307016ac07e307de22ef25d40511d1d57349312da69f34dc6fa156d14c06737b21f06c700507de9c11fc3bc5ad32a1705a98a85af94b4c33458a9de05ce7041caa77f49a76bf5d476fa6dfa7857d96c1ff72faf87891198fd43b36cf51213c5b9ac2b6583efedab1517216a4f79bb44f6c751e1c50e8565775ea68639ab0ed89ca00a5d9090b810f380cc7615c2146ebbe835a6dcd8f24f88e336252abf9f3292a6ab696b39a5c0827c7571d7b0bb6f234fb185726e1ce718505a5c0b58e9c8807ee01efa7face968a49d346d9bb31d85a31dfda315f14e45e5452422693b26adf1dc77a0572f92122f2b8018055b5db2219a18dbf67526df57791ff279da8dd6ade319de276c5eda05e93f3a6b3e343b6399c0357aafd93e12862bd7291d9433e3885baebf147376e4413f84c617c643bab1a15eaa1e810ae738e1fb77e85d4f49e3b2e3171858cf046688ad07f2cb03282dd8f04dd7b2f50293d3907d4d0afc6128de0550a7500f3764ac4f4aec0e617ef1d0b537c349326952f7982f4cda79b958735b5d46a8d8f2bcbe115e3ec1cbbad956b901a9bbc987c62e7582b1eb0beb065fc12046ce86f104321a69450bdca44bb732f8832a6031660f32b3f9c74aa6f2fa2c55d84cb88f29087bf7bcea409cea7756d68622e0535c5b7121c3d7d68d3133f73d20b41fe23c01aa8636f1085d8b1cfef69858153685d3865a79ebddfacda0daafc4a7f1148c361387d041ab515e17087ba45c9528a5ce3102a015f1f20797070ef8fd153781539aceda07215699d6107772934e4a18f3bd5dfa87ce2c8efbdf52197d3b0e3361a72dcb28830fb04fadb9e99fe8d45f1b0d1901c1743cacff35cf82233cfbfe1588488dcf413956e194d357364496550bbb65d60d4a6dc726277b440a8f37521768999dabb7647f3f29aaa178de587ae4026f85d3e6daeeb9c0037064a706b167b25bbc63795182dfcce9f24c5581cc85228d32054818a9a2d064e4a104bae8f20edaba2be7cdabc4faca0d13eb755679be5884abd1582ee776b344dba537d139f06bee00a99cd593b2823f3e16d5a9a2c250475abdfb570c27cfa362d44b6900e4bca5450b4f11d11de4a3a8712dd932dd6c082354f2e8ed4c59e51bd53d969bb358752fe5547d45010f50ab3e748e27e8ff33410600e97d66f5417635d343b303f07b8b64b9125cfe7da0b0e3a0c967adadb14f0a6ab3c143c69e0306fe6395aab7bf7408560e34514cd154e5de7d6a04746de8519ca42dcf4eab0381e429de9242f9f1cbd18f1e9ba1e94735c1888cdec2c6f3ea656319deae676ad8a3ec265356145b5c32ebe6e7497e0e124243594ad9664458ce0613f6232e7f4d0767d4b9e4c5d2a66330bea23466c13fa3aa08e2b586796d2ea7f1bfe23b63b0760de611cffbdb096d17768cb7f8b7ae4c5f113ab15e6b09cb4f23bd648c1661503dc448f77848f1d9f47864d5642b0f9ba379af85a3b054a92d9ad889034c4857b7a8062e782f81beb9e8b3acfa3da9cbf80abdcc672bc555106f23d14e0e4c76c5fdb00c1977164a00c0c65e8f23fe15f40fa444f704d1728e27d02e13663cffe6c0483dc67be39bebb770fe81a9d269816fba36c05c27d791143adf4a5aee5e8e3dda22ffca9e1f3d9ecab945a94bfa4e5bbcddfb0cf55c5df5304e6f85f67b3cf7a6ec666e8c4938255ed9f08f111581bcb5df64f39a7c8596fd8ec2a68244f9740a15af8d3529a94914550d85845065ce8d4bba2e3caa60045582d5bb929333a0000d36b0a7d813358d7438c8cdbc1f7d373e9c5d7c67f4675347435c693b7d4902b9ebb5f736e00a44d7bd0490d566c7850cb2bf1145719d6f02d27fcdabc7582feeab9d67b100895863b9ec9741e3726f81110f0344fdc2db7a653ad94370bcbe0a381789529e88d402a1651cae73c430602ad0898d1a5366e53b1068cefb92e6a9d268e32a3c048fbcdf176e6ea03a6c8e8fa56f3d6c30d8ee7c8736036730b953c7937b0b57c25c55ac7544e2df5a04569f953b8bb683f2e12ab022b41ea511b67f8d7579d6b651685dde8b236bdfbc72202c2cd5972fc72f4cdd071cb9424de511208ea0af69417bc233a4ea5676a4734b82642d615fa0b9dd3e359ca8815372c6de6ae986bc15535aeb434f3084fd9916dbaef95641fc73ea5e6328b7abd73ec4e74b3d76ee825da6cfcf25f49095848e69f2f80e3cf3290fa2033206996f4207d807fe555354a8d2a3c933a79404b2fea3423a3a82842cf3b2e0f091547e4e774e4e26673090f04ac2a88f98aaf9b8ba6d58f457c5ff71925b6b06bce60f876a7c152ad50e806eb91d5cc3e18bb57f36d6b62a78a97e17a083daab45c577d2933daa30f40f053a900e47a721a7651abc5d78bf7d1d2bf38a5375959ce854afe6c5af628f585c04dfd12fe9efbc0560e416483f3cc3e92ca511f21b778397686f947cdf471ae48b50de41df42ea8cd3e1885c141d832fd989aff5d35b88d9e89198c57d3778a74bc2769c62c06ea6a4604a4206b4dadc979e6d40eef31f87c5720ad11c7b10c236f824e3d971f492f4fece393adf0b7f55bdd9cffe350e9fb2a7a6056889d40be804f3c6e29e531545487c6b6721170ff57a76eeb35a0e3f0fedf883b8f8cdc0eb1c69d1212349eab979dab188d07a53748a3cb320f7138ab975cc23b054612c808ed3ef69156e7624041cd6a6c25b50689645c0f0361ac205149beccaf160c49b6692f41ed205a1393a62df96f27d1270cb23fa59ab4f66da3edaf3ae9dcbaad8105c19f88fa40703837a0e0b8159a5335ca18768a4d4a39f20858b1bfc752e914c5314c77994ce635f4754f78f25af74126c1c872f1face4f75f5cdcfd0c340e33aedf6817dec9d31b92b02a4f7fdd6835b926b23886575ee060ba9ce5895f5263cdeffd2e97af719d89395fbab2230c3198f8255772128e06580fc820ba7e8aa351f103adc29cf61bcdd82ba009d52329fbd0bdb2273e663499e019dbf061b06128b54faa0787b97b3e436c10f07ded7a3fdb5c16e4d3231ad6b8f2f814982064275cec447e328634ddb6ebf0808b43d5c989217990ca10252fb9455933bfef6ca1d9c80cbe19993b1d1725c424bc4ed2c8105f0aa2f76df63ae1cad1172898349962b08c7e0cec950a49ffd0a26aa0f8ba2b5d3e31448c8560ef5e8a517740d13b67deed52d6e5dba1d6c17f377cf63d2eae6df7b0b947f28bd42f1166ce2542da8e197ee30566cf6d3e52a92d6fe9e1f5eb1a77ab9c2b95b996c9525429cff501709b3556eee997a236a7f4a811caf15f7790ab3cfb88ba8480576de2270e073c4e8b49684c5d73619b6acf2a8feb7961298ab9fe5ac24eff71ceaa720451a57e96463d23f88a1cf826d818e1987ba9c433899f050eb59d4def892ef1cc19fda372a7fc8081787ca2be44c652ace20f519c15fdfab76a49f9db8b0ca2a6461eba32a857feea76447b7171d7302a091e80e7bbd13795e8005fddd68a34c58509829106ec554000ff7220525668db9cba944703798b3e5c66c27c8af17cda304912d35b778f177aafe52a803a54dbbbe9fc3151aaeab58b2f981ea55b576cc02737031ec7dc1afa12e6624bae283bcddae4d09ea9aa2bcd9c1cedf2a02a6866e653e47f5b29f3301f2a295263dfef76f239661c88fe1606eb446c3c8582879ee5e7449f8ecaff379c417a1816cc8a3c1b279c03bb1e9ffb585c8f78c3aa0711bc04e5495b0a71dff188eba99c931fd4d34dba30c8e2c4c453844c721fe8c478f2662a8dec958038769bde1cd28ef6e082dab275b754afa63012825d7129b9fd5d010b2c1bc9728927eae10566b645dc01bf68b4b690d004f7f83c0b3b780e00fbd88adffe10dee4eb6e8015d99026fda308ceda516c751e483f7f7ce28d276bc88d392e00b72ae452a2ac9aef7c1b669040ed944a0ef856aa16a89297fb3c2acdc0d6c6701685a37be5b720488fc645a7be8aebedcdd08054db2a3f922f3376a60c8b723ce8837116956371f7862711a40cb6d933d6dc7ede0eed34af3ea07221cd3d6a021e95b6ed39bc3c205e9436d20de4b92873af22707ececbc3970a65042319f6161e12ae6b3a4cab09683b526f166507c73a0289f54482231008b2f23a1086c4b6916dfac7fb50bb28ef87c13332a4b46fb89baa71e06e3a7ff161ebcfe4b12dae5f5325427f057aeb6e181ebaab9261ef4aa5477ff5276e6c0f972f952e52d131580f69b7dcbc5aa637bf75bb00035c3eea9931752359cdf8863071d17511dac3e4de15e80e837750b525c76f4742a53ddbafba97cb8a29696a6f1448fb612482f4b1e1139e30aeeada7b0c70a7391f88c4742f6453d462e0e669faa9cbd836f3202421436de3a61d6eab5e2c73a9be27c1673cdb51ebd6be95e2707805cf58aa824f5321642fffc91840bd98f289f04e12f7140f1f58c4e1f18e669bf302e2708bf04456fb9a03337fec42f73144402e13596a77c9e57977e225bfea5e94c73c6ec6c18240a0354dce6daa40e99b0d92e03b22fe2ec999ec5c9fb558639b5bf78891e5d7c71a9d8bf9f405431782f374885d5facde10aebf9e19f6a90373d025b3539ce9866d51434f676d55df6bb585bb81ad2f88e85c09abaa212aea97ac2775de5d456dd72a1dd6c2d35c39b79eebfe6015498cca0486a67a49496ad6ac731167e6dec4113d23a33de4a0f535da4842befc491bf150c31312b29b1ac7b88c6ae1f557e3dc2ca9cc80915e8eb69abd06c78de43e0fb481e17c92c652dcddbcc8dde604f4479518b7aa33dade7857ad16574e053fa9fd8ae59d25d45b098a6773f3a90e27d3503989c60c7d41eae3e9cf307cc78dd29db082c8e91cf527595684d7174a72a372fa604e9cebbdbf2db794935bc145f50ab445459afb80cca7977868e29558fb2c3f546a3de1d670c038bda4470f515f1e8e6c406831a7fc49bc995ba6d2cb89b3bd69caecb29a2e86614d3690b9d578fa15ba1a34f12dd7bc65c86194efa3f9e0c5d87b372650a4270495697ba49a3080cd23cb398a98da985cff3acf4549f83bbab4cfcd4955b3eb6d1ac554c7fcb07d2764f63e743426caa2c8d32927900e3c27526672aa8c46c6034bb6c81d197947c9a8dcba02dc251f62ddd6517d52fa1f727f9889cc2bd1729698cb73943a7435ad11104f22fc821c2c415e99d203df75221ac432c502b6407dd2f41c1b5b40d5c67df7cee66cf4906a4f1f5edd3a9d656d67f06f7ba25cc944775e5f026255ddb1ded232d00f266f766df6685e855b887318a0eb43dd0fb8f42a8bb730994700034fe36845413853bd555e6ed5ebde4621cce6e4eec2b2d2b799424e48e4c1c78d5d7215498a1e9d6d083cf17febd4aed114007a3b59cbced4a14bdb6f0395c9c596138605141d26c1d1c5050a62015f8437579c0c26e0925ba06d534b8dd4ee7bdb14f54d593d59d6337a5320b0081e79d1ca7c4ff934d5b0aac6a8aec2574064024896dc8b5fb6a9a2061d05a7461a123f18460c7d474bd50411302f6d4a8b2d381faf28544079ecac6a75573e663a47aaaf6ade8b12ab2b88c99019c085afe813b9c86bf3b33fa6ce9b91ef290097265078dcac7565da446d3230069fcc052b8a4ba81ba3980a20fd3b9feacea841320db172fe3686d200b1365e8e3b21bdeeee544683e9ec48bad25a8569f8fe87139104b67a7076affcba7f171e2ff1c158a901dd5bc9722d23227ed85c5cf631eaac0bf004bae3a2e11328a94747360448091f8ac0d87ec70eba5f161454fa9447b0f29bb83d9d00b22c8b364f2ba14abfee1f09cd49758bb57208af25dd399116fac043ddf136c3479f9d497355237760c17a847a0e0a775700a3b1a210f5def42d97275fd249ae8a00947bba202ffc3c89dc4aa31b3b284bb679a3407d88664c7f0a128875453dd0ab8d4b9cb59a0a1f997e01ae07e815bc49be1936f97edef84e41a3c2915d5322e9df48cabe4f425b2214d13939fc5867bb1ad8663240cab2d3933f2b3d096661ad9ea31ae96b6c61e149a0e0d545a010a7691ed2a9933e99217604a93c3fcfd8670606159f4b5d4643aa3628b3d297dedaba4e92caf603321e072df372c3d97677b2c6abad1acabe313e8f1337e037784e0fe092d4e619c148ba2a1a811e194ed8ab7a9a82d10ca51a509308b68b78e112c7972126104999b09d5fab9a00ebb1d5ef9ef078252d93c88e2e548b76f227822b26ad80f7291029d57e4e6a35385f9ca472d36d20708bffb0faaff69e691993c706f3bc0c901dc3d5b927751772aeb76b378f5ec5bf54c1b501a820bf15e0017c1201a02f6226ac4ac254ba15ad7f0aad90f9bb024acfcd468277aa0c85f0586b271cdaf94d06ee1cc6223f7d9b70d6f5e2ad9bdfb814d5088ed60c50de816bf9e267e9b5fbabf26987fc49f1bc9b05a9376c45224273fe73a8b862251d32878041a3fd3813bb3223a76d633a31469d1bc44b3c9fcaf31015d54c7d44687e9c48412d097c479c19686dc6fef05d89fd06686d2146f0a5ce9f5b95d722c60cc86a8205ef40315603fb4d42597f827b16277540d473c8b5a4181b3b12979cf8645c71bf8a790e13b99b200c01014d72cc2f2d0edf965bc718ebcb540546a5723a0d543fa975d63163f6dd4fed1f0e569811c694aefe80762d27ac5a7aa16a35f508d693a0187859635597c4826718ca088dad659a84b15cb0a1fe940a1cf218464d3a877d21329bac97e48d4a5057ea82f1a78e32215a800d4c067a4d4f80875144e2143a205ba4fef93246237f084d80b16cc24fc6355f43e7752a8cb41693a9c8318d8b8b577988d5d1cf310d113fed9edb13f241cf55c3e718974a90271d09ad18f6b2343ce6b8088481861e8368113769693ee7520ab0acb32c2177aac8592b0f86b4787374f3bc62867f339a3e0a82e21ce30b1b1f8374ea2c43657878a09bf57718b048d33488a919e9b04c66647d6a48bacc74a138596637c2b6abc92aa80e42bccebd1b51fcde1b12ee5c08304f8989bd98ec2aa86c334a9cef70ebfb734bc30249b280f482a36f6931cededd74c1846a0390d483bdb990f8b1da6143544ca35ac3bc72a3918fe7837900c4513b8c8c89cf99de7fbf18596c30616094833cf92f8105b93f31d7badff9358cffefface40501658f54b566dc7fe47f65fd44269a8c2a57d97656fe8953bca09cb07b507532304890124f1730d3520b1e8ecf2b19ad3566df4e22b9d6d6dad144511a860c5e40432469d276e0681dedf029ed5c9ecd29f9179ddab2f28897cdaf4a73e7aab4e3fc513122adeefb2df11fd9fb125c05e023ab7f6931abf3cddbae3f2b5c14a6a7a1ef4b1ee5cbbfe3d8e1687f31e37448b9ac247f4782e7055070c1dca3c9498799fa47f18e0c05ca1419f4774d480eb0315983a7087acffaa55df55f20dff69b41e48ea485964fd6a312e24444a7c288f8ef46f482ed918aa09e513039b83ea065aa49f056462f99370e48309b125249674059078aa542373682fea56ab6ea7fcb0d1fa4b5ade011833a5733c2d81410dbcec4b22ba773f11a349dac8b119220f6dbae4ed47ad9978c9e0e1d86ab02abcc6912cfc6a213e7347799e3842c32cfc67c770fc31c6381ac3200de826ff16124bb370bfa44fc119857ba9a21ebc81f5e239afca517130715d84e1d04d4e7c62eb497f6fa622f029ccc4c2f8116eb9d5f1c8365f32c43e4547eb467e971805d98e80432ae3dff3870153910361d75536fdea23b1066a18dd3216de5dc9b5dbf067e8951abc7bd0c0deb8b766625b47757027d6d770b707017d19e8ea40c0eb4d8740420767972102444c9e47cb82a00c52c3a98cae509a90e8af18007d4a10d1b7b06fdfd48f9d02e5325df3c66e05928e4cdd83d2f3ef4317a05dc284170f799efcef065ef4c4286265ae6aabd70cf4d7c7f66733be240bc8c26ac2dc418a14105eb83c302512bdc65afbd073ed74fbd6d0437151143d64b177b34a4364fd5e16db9f89d63d6c9088057effbdb9067812ecede48e2d80728e9b92687b14d497bd6dc5a42f1da428d6fe73f22690e8c990da0e67b59e649d2a0882f669c252b587971819a8d48e4c3512c0cdbc7772180f357c5ccbd67450db5d2bbbeb370d9ffc1d875be9f3d17bf3dda1ecfc6b2a33d955a2711f5f3ddad40af7d7942c1bddb63ce341e06707f45340a887c514fb2c5f527bba115423cd92385c407318d4afcf7ad603f76845b2ffd348e8e909c9c5340b942500f408f2d8f1cccffc0f309cb419d3406bb93cbcd9c4e812b81be94041fa7ad2f398e7327a17a24633b2f1fe5b2c4a2078307571fccc5b4a61bc301b5eba9359b09cf72ac72c6582b5e346ffe43785f3ad86f6f05e497c667cb213a46dcd694c0cbaccd5f22764f1577922d9e4b392aef6c79603ec855fa8a4f3ae7f180cfdb0f8b110d35dccc2208323e9bb04a9ae1e09b03b1aa51c6f1b0bd75de67fd5bc82d054fc09f0805c5953fd168ce2f3695d8be73c6932650a58b45c285b325dae599f7b7cc095763050d95b2b525e0815509f5b3dd177671df2579ecc566a6e481aba250221f58f3475747adc4324cabe536588b3552aaaf877f103029678baf0adb7268a295fb3c5aa32f92a91c0c26e6722fd8214920caa3bf93519a4e01b6abda31bc490b67526a21eed45e76f919cc26eb4f18cd361092c78cf7a5db180f9bf13d29c5d40543192c283f9471087faae4eaaeeaa987fa4dbe77f992097ee72c484a66cc6ccc4e96ed741c6b836f95e35ab702199b66f11a8852ffceec92c38f48d71f74c6b4aced2daa2c0a955b58508f17abc3f375c21e9c779608830a64a7fde1d5e13b468b4d5eed0af60bb326930988a0e5f4f3fadf1b816690d1f8e85156b5e6e32cc66b35cf96e8a5f0bd46cdd21534f47a4e085a652db92a8104e8d0f489c03e2853ecf2cc716ffacd6af0cd846cf4614e5ce956c92499f27cfc1f17afcfec18ef29f9ee3feacc4ea25f4b5d0aa9e31467c3322fc7ab857934ad85be81a859160892f869a93235f9956854bd8028219c238045f4519dcb06893390a9b44a6e9904efe6beeb49facf149eeda2dfd7aadea77671019b3f5e8b66a6ea5281a46a79845c7ec87a8ea0ea1bf127d499789a5821c4d5c1236feb944086c6de24708c0838ab53d7e65c1b4487a2c0ee5c83c7e5606cff1845df2a22904bc57bc2c6e8fd94b7bef48dbd6a92e61fb7257104d930b45597a6a8a46802752058a01dec1d467286890e626701d4ab100159d20df519eacc13851d14c8d9b734a07ffa553ef5153962e9b484dbc8fa5da524cf7795773d5b25d8186f7b1a748f3e767414b2439b70b5abb3a5559cc2878d2d12017b597dab21d6bcd04ca4bc388b524f9411e66cffe207c82d452bc129f0e2f3e9a1a2b6baecc65f32b8407a1aba6b45c5d53c7fc8d93c2025e9b6972bdb0afd28a25d7cf844d59beff4d615e445b087fbe841ac7586e3f8a22267913b2acbec67e100f2863b4b4171acce940568754a4bdb8756700edf97157ccf958ebb10ae5bd831ce986af2c390a956685af4cbdba2ab331454139530979d647a6980fcab5ec8f08cf15e5a93ed69d68edca7a90921689ca5a51954bfdc6c21dbd4cc8e41c8c1fabbff65f2b328aac212e610d8cba213ff64950a62860eda110310da1e45599d4ce1219d5829e8b990de492654f17e5548ab0e34b624ff5a47fbd1a8f0372cce2609b7db8c53b406af27352cd15f98865a27ad0d534a99f39e74c71cd885d8c7ca6698fd9b14fd9e434d067efdd17f721f62f2f5a7e04813f58de4920b112b95df2ff6bb6a20e365ffb5df86fd3f396fdd06e06bb533b736e962c59eb96d35ae9fe67d2e7968c6dfec886cdcd1b37342ff83a0664dc336a790ba63892f5bf8bb37fcc26bd949f6854b6a8c884f9d5fa0d223146af714c6d35ac18ed80a9b8233467565e70dafa125e87c6198522f093757697c3c0899c6c2004375387f2f9bbe76671345118fa596311389ecf75ab79e9bd3222569e502237f6e33a2cba007fcfd9024908cec08cd040588671c5b3745524526ff64eaeebc9db97e4ceebd93c98759e0272883350f19ee8b4605b4f871ab150df5a1cfddd2bb536ca55691f137b260b0ce7507801a2f933e09eed427243b38502a4269192a3e670fc7392ce80cb91109f0b24de529efe1333ceb84ef7a9f9a53b96af4ae97245b3e9b673e1f929f55b133c99f469242966e43218c4977eb234993c342c2a2662ea37736ed2aac6597972a15f0c8690447dcf9151b2b743ea9e5787b19a6c112102ab7275e0a8721474f30e10eef6f8921deca09b9ac62227290ee274c1b7325d02992feed0cd2c9976455b8842104e86ccface28635ae05834be0dc3dd7342f616564ed21242d7c016099067733ed22650869df4893f03fa74d95e883561206e5b13ad53dc4f5390fe050aabc79193d3dadb6a7343fd3edc183debe03bcd111c4b479eb4825dc7ed818260761b099c853b8ca5385e176883f6b2d4743e62f7b67662f9bbfe1d0968a1f566a5c18c8c4410bd49c73a758cf3464b38ccf3edfa529a97fed6611b68936e7f4642b7e4284bdf6582cb071161995d940507fa137c8a9008f9e0434a2c79d8d8e40a6da7bfb4dfbf24fd60a78cefbc32b343e4e55bdebae25069bf54acb5cd7eec98bfa8e7d6996e41000d85adb0a4465f2180d6bf6a61c7a1f032a9f6ea816c1304d2521432344f16e24a3032d5b6c15b2633bdf693f3fcf40483d062918ee27cdbbcbfecff17cad605bd10cb452515804e69efa2271a9185ceb6ec7b91791d2da337eb9923aef6d6c27ff145add05e0c0cc911d6096ccde6039f62d52edea328b3ed7d760d11e05a845e7a51b61387d047b31b3f8201f6e5c2452689612f3cfee0c8d9093ab0d8a629a6e43606f1bcee3706b9f9780047dd3cc78f0237f0e3a5cf9033a33d0f1ac6e806f9d9c26851b71b454ca3e7fefa81b0dbe8f4a34499f70b5db373adb1bed943e3f8a1d837237683e71de02f3e3e935332910d6135af577f175a24026a88cd9cb497848fb76486ecc500cb576eb500a6f724e277da0f56a25fb9f223744dd5626d08601b1c4a82fa650eae267baa2c538ebbd630d3db4aa6a9b6d2caa7b1a6b420743782253a6ae2b845d286cdf604ed56e7aa9cb74f8d7cce78208022745dfdcc00f615b47ad6390092d1bcb29ccd7bb2fdb52ca19fa265e5ab34f9378e9c30825d9ce764fc7a55b6f9e3dd1051cf10d37e4cdad92aa13e4c0d5bc9ac8f886c0f89f2c67c2af2f778612409ecb60606ebbec82591f2101a3362d9c075612f4a584391b463ee9c90f109aff5129333f548aaa31d990e11e3de7cd03cb2b8bc4fb00b967d5d1188e86d49f80faeb11d5d746d7cca6b74200b57bede2163f4eb0f7f7b0b67ea3055f32412c440b116d3900ed3dcb4392b59d17d7fbdbb6a4c2e710925eadd8855d1cb3e2ca0bac21a415437da39eb37d50ad34e17dfdf756e5412b69057aca9b25ecb2a970ef18cbadf61b603db67f525bbedaf3076e36b49f90659b03b010bf19c92f6e9fb60b5e43667773ed750ccaf175786f3882c07c5e2f3bd7a6d712febdbe598b39ad4ccfbc7c069118c7d60beb79d8a2ee8fe91923155b22d8f2e0b23c3521ee07825ff40876cff1962b5a24250b5af39aa8453070ac1135a90772bfbc4a600de812c90fc5f52f74e7591690608cde0f6fc03926861611b22b07d2872ea4cef95d4126827c0a48431fadd358ff0f8ea4cae33ccdebc60ff90330080e27646aff996d9d1fa5cf8d11cc802ebe5fa1aac4e3240f82cbaf4efce1aa5bc217273b61eba3abc477f1295386b4a28b3f0ea4323a451befae0c6ce4393a8bf4e4f8a931b23f546add558998bb2eaddde10d1d2b545b36f7b06225536a92b6a2994b9d080956a193b727f9da1cd8a61afa19f87dcc10fbd7d3d67e5455c0e67d884177ec916440f7c99ccf7cd0c3e0989962b9a52b26ab126d1f30b0fcd470a8b082f89ed798284be5a296f881a152becb830352da739e3482b2eb1f6612cb6b1997d46744ddcce3b72adbdf6dba5ab94e20dac225f5d8bac63ebe56e1309907983ec84380a450d6ff58f075b14a983c01d489a584e4dd8160aacf2297d4a3a1831146207aa339b686dce299661634479db97fe49898cf399492c6d53a6dc059af53d9e00305aef5678049e77d8d361dc2e8a006c8838821029f8cca78fe6206360ff58b0179a92f289dbb61eb89a4bbbeede4bd402c6e5705426c347f5f92c4420c4907e4c7da4d49b4c89189af609533e6021de77a52ca92f83752eaca24b4a23cd05206f2f23a230050762a5af21bc999d2df991e88e387425e71ccc40bcdd6cffcad266f9c18feb45a6495510e44cde9ef8c2251dcc4bf1f90c5b3d5dad18169b83aa94a1890b882ad9d127d9495d3675f9b8c31611f8e1c9e0d56dbd2fcdb4bf4fecea36cd699117f97ecafbe3cc3bd908453d3e53ee6131945fd6789d79517a909ac97bd56ca3f4313c424e0cb22cee09ccf7d82c1403c53ed2a8b1c3c3ca6cadbd32eedf5677fbc7a7a5bd7552626b38d2f3469bbcbb4cb07648420d2e0ee544bb98aad1e076a41d96786d9ad8fc4862f40c845c4c092e1d377fa86f1723407c9b70414eb525124ec313fd9de4da37564762455ba6a99e4a22e27501aa6003f426d299656abf36a78b769af00c63","1703030013ee39b92e36972b641a8f8ce9058c8fb348d179"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-a-long-flight-is-fragmented {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030391f6014281366460681f4bd0ed3e59cc91173d0a51688d0e769284e60b12608820c25adf209044c6f36546b8970bfffd3bd6253a037e00315de9e9d98b95e806c7130300002e00330024001d0020b6fd1bd4501b6220091a2ed9c133b214ebaadba4f0a4c5abbb54c95d7bccb80b002b00020304","140303000101","1703034011d70abbaa6a3aed6faacbc1be5510d8a0ee4c39b3e13434b868065e5ce41eef2eebbdbac0bc403a79d6d2bda6bd98b5ca777444bd8a31170193dc46b3f081c78eec528453e4180753343562e3fff0b4abe52b94bd86f0d6206d8be971808fdb02f8a42498c5557ccd7091f77c12054b4a798c30672cfa37005d39469a06dccb36dca2f9d3886b064945db8c9f2927354295dcc6ca3f5db1946aa40e15c0a610b695f3c002b5cff3796f9e03fe76f407471d87b725b2a7608790129ac8b80641921d7924ecd2f86e12aa8f259da682ee262511865005d27504a0ad67dfd9db35b3f132579bae6d2296ad807351ae877db47e6474c76cf29e4979790df59c2eac9ab62c777f8ad8a9a8341c8d2f8dece8612f27099406f909b3383f6bb60fc08d6590726b10f1b3a48bf7ec9cb7b43f0ace37531c096b8bc5c9898b2031d34bd03af0edff6f7c09518637e6ce76ca062c4b89faf08e058af8a71e124c25496a30bd4883db25081d4ae58d40dfcf2a0d5fb750e9df52391f9246218d2fcf7cd80fc4d4544c1a6e4691d6f33335c28aedaa3702e29377533f976a9c8682b95f20b2469a8fd779b329d7fb18fb8e2c21ab76ab6d5c5cb6e17092cf58e2cb324cfb551470e1880c5e6b4af43ecc4265689c6eee39ddb99bfb8f563c27c608e5bb7a367984bf9fec10b115115b80d7ce6a73fa2f2e2c20f3de318ac1f02cb660a37efc0cca4baf29653907b318ed2a3e1bb66cadc8f26d85909bc8f24bb300b25c283e825b4236da6ce1f4b71026e75cd40106c80bfcb89d465da610cc52f307f242cfe3faa1455bf5b33e526f1809fa67942f532674eab2369e8c08da7ff4862fe0558823136268747e258217ea4bce7d3fadb9d72710e1463e636489de9e9d4d8a8d0e307438276dbd89d5662d57d7cd1dcc20460c03636d5cc77214d484dc7fc9c527f3fe538823bf8ca6be319fcfb8755a5a5d5696cb23f2dee7b5e06320f7ae5436211fc55d96bc0975f7cbe532085f656b051e77d5204283d31227854d8a7f44fbbdc4d985a0264176249a9b1feb5207ae968a4b8961a06a70a95ed1363a0622ca5bafa554fbc3ad03c5d0ef32937f2950a48d6eac59ce663834daaabd42c2ebeec1ed552bb49db8481237baeab2abc9bd96f19abe0b631a18fd45321b34c501488b227a67be7168051e1a9c7e25df79f031643a2ae3095b249336281700538ac03c49918caa75508bea789b9fca45726a1357241a96616a6ddc6539492e5880e8d419e1f2d78701648d1f1f88d0f35f510d4f063c4d7985b208e974d7ee4346454cb0955e28580af96a0b650d93829c1ba08a75720b8265ecadf0f7d51027dfe1b3cd401c0549428d2f0c0ec54c2929c54b520f8e92819fe5cae7ffa2ee1a6e2e9c0d72242ff08e7e8f17f892bb4f5157c2ecf7de02d4640b75808799c0f2d3e2bfaeec0543bdbc4700f18707507cf3fef0dec2331456f88f3448eac0a9b6064c668dbd48f900a15adba6032bc3e933ccae1e0ed7fd51d8c7d4f523ba2c38fd80e4cd5e3a284b92111ccef19c01a93703f3eb8c250d6e0c3d9de8724a10ce6eb6036c4881be30629eed51837430239e2a31d4113d2886b47932352ab98b4231f0599496ec318d38024e3a2e4edcf3a37b7386db22f28e6c2d76fa106aa8a835104f1e848dc6202b1f91f6dd587d0b74b1f21f1a56403263174842463039a85cbab52d4deeb7406245c9d3242a7fcfdb3c68abf31c5dde75f18ab13260cd8b4f8d5c296f64722bc8f823571177cc8b4c5f333d856acf136b0f167eaf2d3c4b0e5ff0c804d75443fd6b696c0c97863a6e31cf0d5486929bed4bdd53cf87363d1503f3bb18c38fd4a56f3cbfd3fd747fed6e30f2036c215c7d0306e1d57d279a154b962f35a5a80dd16fcb4e2aa1e323e07a8ce9ead6e537eefd8eb300e98da8847456ec87eec0c601b9fbb74eadbfb4e23166853d093109a8728cbd7e561f711253a0106a9479df7e0e2d46ff64eb77a26198f6d71a6161f2e182c8b63ae0de78a5317403368f406623cd8bc6fddfdbee3ccf47d4689860e3fee0285806af17d8abdef7a1d7c5ee926ecdcae410122a8209fa07fa1420cac2ab850d52527ec6ac5434e1ecbd972e1b8bff85e0dc0f935bd12770d126b93af5a8f12d04fea0452f7ba312566429607d2cc6b999821d8ab9892e1a38d4699aaa79f499bda23ddb089d957fe228dd058d6fa98329e6484f96b0d35428ee84b6df8267f2233c7145c6c7e502ebbba0cce870176dbebec0ef6b7cb052e49e45ed23d6316449b6b835558d53f7c41d90904502e8962df9af4f82378d41d2c52ad53aa9e94343cd4ae5f7ba441e4c8f487d156b0595334927657a34bdf551f452074165649e83f99560cb5d97f2a0e0cec2e87dd5e63c9553dac7078d9853eb1383af06184b96d498c3bb98f820edf53350bddcb537b3c14f8c90fdd07b0aad1e1c3d5b70374995b7d1f5f70b7eb35a317815888bb089dcd8643a971513563fb7684823286deba90ffce00f6b424965065718f4f5d3076e1535afa02afe93fe5a21b7a51bb1b9c309f3e83d8e3a65849ea44288c79cf68c9befefc8623ec672fe7dc529e13d4e5fe6491f9b683b36c16ee2d4237d6f777173cf651da63a082ce2eee6986067488e5644f8cdb97c64f8e3958437d08a4b3b6343fc3f52729491f70cab2ba6859da9cdea76478efaec767155b56e7424cb9f964b815f26b33a2c47afc6ba9c5e3cf5799e399941075261e5debc438cffbf66fd5f155d2dd2fda17ab44282110dbc7175d03742c23bfa189cabde221979070e723e2f2026d98ae7594b125b6c0fa6c146c21023f3e1af527df122ba421490921cfe915958eb6fc2c711677e29fb88eec2ffc8b043f4dc7fb8e3395570246231c593d8cfea68259b4c990ee375705b00279edf0d59a2c5fa86c86660aac59dfe3f1960dd4fda9e125f94fe92088099ec6d8028c22fe29462cd20c65b97682d3f543bd04db3267ec3e1b3fb07a9f343303577c2d69166fbaa66458ec0fcc09ada46a7d47bc64717ebf00f89ba0d7d177683887e6d37546f2b23a6a240f08a5b7bcfe9dda0a65b8184273d71296d9a05370c3595f63d3cb2018ad4b6b734713c81416708a884b79b714487fe39bfca3f474bdfe180bfa411e68fabb9956af7c921d0f1641211e047674954c4fe66f0c583674f8640d276a5e1e47d1ae3661ef4dfb51365142c12e7c588b094a603ff4aee3726eaaa67418a0e3c6406b8f92727bae0a89685830805985e95491d2895e6f4f98211ddd49fd2ac328db8eb2022b0984c72d1e56ba847b5189c589ba3df6823d850ec463aaf176cd853fd085c91cb1d6e48152e517a16ecfd5cfa5511284e20a0901b4f16a7389bd2a7443ca0038b55ffe53299851ffa07499e52c0681e68fe3b26c4437993f9a8dc268602be8b680b0c896722f915b8942aa1669bc05e8225f7836f05a9ef0b1f28498b15a0ef4df16db9aa5ee5c546e196bbbe0089e63bbb84c65ae653166192b3e7467586a7ccef656b022cfc7adfefa88f8435a22fbf7826e8c256f67a761d4da83957e2f6239fa7fe840ec463ae26800e1f91adada2bf9ff05c86b9e28af4eece39ce77f8fb029fd37cef7b816986764b5377789b1e53ebe49189df3477de24ce2141757a7afa6a8507f116cfa25c0a576aadf4bcbac6de64aff8e8e0a522069f46985639826173e317b04196be18e2bcd4c78437a9b85e52d021e49416f0ed4a183d487b9fb6026ec2dddbe71cbe767ef169db01efebfa80e99e838418d6ea8428ab30127ede162b8d973b1af1dbf0b49f849a84f23754e1ea4ea9fca83e7774a2114a857e43a0b6ea09affc199e52ff44c3587dc67be185b66407dd49e6b5f66fc580808630d2040bf594b24857859b27046cbac8eb91fa545121191f327526bc2287463fd735f51e5326bbfeb51180c02c38fec4fe19e49ead049a04b034e3c6f52b6827410ef37112a1e08b6aa255c0d78f7fa1522fb860667f27d27d649bd5e42ca2f3df31855f6147c95d0f6685e3a26fe752c00e26d03d2e6dbbaa615165e697ddf6068fc83bfdf4d69a5c41c38f40d643217f0bf58e2222e8601cfb13a5a80d6b1814727e3f581667fafd584a765b342fc71d93422330bad0ca85024ebce81166b343082b90988b3e77fba7f462a68612cbbfd7e550668aa940c14f59728c6db1bf10feb27187740d619a36eee6b2cb75f12ac819ba488047d29f9169f7ccbab721cddeaf8eeb7190dade103a780a2a398ff2222a40bc7212ef447c2133bd31f9a2bcf4fb6225316dff9493873579e1c2920f1f15c570e20d23de63ee7a21cc8a8bc9bdab169f44fc970dbfe4571b0ff140dbfa9167bea9b660d48c2c7672ec6167e5cddca8aa18edd0e0859cc6399b57292a090cd03a1027ed9b563f520fe2e845ef1e14ae10e58a2e8e6dcea9ef83d11d81bf3022467930e99268e121c3740931cf2dab1824ee9c9640f901325dcff194dda0e979804f3dbcc18f87d0c902950c69eb68fc2df000c0d73dac2c2ab71b9c51a3f1a54bdc342543f3aa9859c7fb09ac88a6acd46cb9f594c7783dc1eb3c7619d9d5d4f7b2b17936c70229673f88af0b2b721bdd9426f8e4a41756026219fd3d46176d62a913c9ad44aed37bf98e95b0f3ef4981ab03bda46df28c08afccfd5d2e6de651746ca3143546e4011aa99457c6de7341eb0d29bcd0aa75cac2620b44710ae7326dbe1b75998ccaa3d8f67e5ba5cdc4d0b3f8fac841ef6685bddc07f09019ae3b3aa839938a0754737a3dc807e7ae4bc85981964983077142d87f66a8c33349291cb9d1673c37403784e0a2b3d3b7329de49121f6f74a5fc68af0ccd5a48c2a16a895f0aae0b36bde306569417746dd2c37c64de430877154918aaeefb9626bd1593d905dcac815d33bb362717eb5c8044d796859efb9cb01074fd1f0a2e2682dbb9879fc13c8c0af3edc7f126e4680d05df4aa49424fbf54dccf4d6a628705a64958553e24670ed2895bdf4ea87d3c0605edc928e20ea88a33c76ccec6ad072ec884de1538e1d6895c1b11b0858ea7994f1eb0954e98871bd741476558fbe65f88f0c472410da47e51dcaf7ade98bc37ab5bf9d54e8de05d2c5d4f0e4851c7e97b4305a1659b370a3b1f58205030ce133b7026ab605ab0826730b96c554875f2e715c773d6f9560781e92f57863e127cc0e37acf403159fcee6729193ed8ed61dd9bb7927d232cf877c5aee4f0e5807c0e6a203b4af03da797bd6690a928208288e793bfc0fccc4c4fe4be571a423bf38b28503ce5abec24e871b283d0d874a32522315fa89d380ad8bf0112a7bdb202682e12f913552d38e6ec8113dffd6e0a83fe21cf1cb9b21514db2c9e572e60180b0e95fea63bfcc44dd805032dd4faf5c87625c075a8e659980c63dd8623de7724f46898f189937a8bf753af54bbd063ec1b4eb9aaed35d309cf12b05d62bedf07fafd15fa1aeaa3686369afb9839ab4f62c3bd474e5c17de207906992e0ab6ac7ca770e22a98d45d7108676dc1dcfb4e997cd9336d585b0c1a91395aac0198a2a2f863bfb8792e3c845fca1e9adb5854a6aa9d429ce3cad1242f87d6c140f8c2010650766e83c6f50da4d8bd37889d2a04571eaa9112b26a438725d3eeea53842453ee64dfb1d769101c401c90c358f897d3b1a3cd0d4e30eae89931c5a8f2e2f4f3850adb39e6fffce2c1cc84566cade9488d7fdd64c3113d6b175be8bd4d8c8b8ba9a68763d9c77057903e78170d8ec9cf3eacca5bf21821e8ab5aa4fb5a886533b62cc917cb915d2ba21538509fd85251f03a7533c72a2a4e7fe3514887ca04b4ff769b89a60f10ab9f04cfa1fce4e3aa80e8da01efe49a18bd379114f9564defaade3fbd648f142290a1532bfedf4b15e7faf07eab5fb4099db5cd3790610361b3949f1ae8bf0842c11bd54cb9805a5d1c7110e157535f2dcd1784f4dab71511c3c6d0114343d6877c2388cc3162a46c0a2ccb35f7fdf2edd8b2f613bc06cf9355c4dcbdc7e81b2e571206c8a4c782780dee9e045cfb34c89ef90660c6cf5d449fe37eb83bc33d1f5c76356231ce5f8a8650901463c34dad74c0e953b0ab6dda8f11c4d5e2ae8e3b1db82aff15532175b14fb722f92322f901b4d569fababe51515efcb04537f2a9b54fee21d624bcd3c3212b5c47c31ae0726eca07ac115dc12f3648296b43ffbf2e9dcb17b8def38229e157304a7628845cf498dccdeaa5c50ddf73748a874d0123144ca91c9d811c9dc4539b9c552038057be55beddcbdb6d3f6755dcb2280c5a22c3b68ba64168fde1a66bfea0accfc67598b6ca33ca0d63128bb315212ca0a2bf68bf5a630b6130fd8846988f134173a6c963412e946ae79d0efded3de6c2533ee461dc7a5a8582bac36a6ab5d86cace0c7c5249670cf3f4d71e9c5f0e30648a447eb8831a251746196f2176ab6be3340bcdc67ca367961181e33bd527db776b34e2d0c6a0f9221d3a50b1010b1104bfb9f3aacec769a9a43b863848cbb978556d5a2783a6edf922a8b91cce9a12b30e664b78d6fdb01b8c7db27601a97aa34e8c3acffd4368aef4178d2be46c9b75e9a66818d779a9d90ba8207acd88683456ddab593a36ecb2a971fb8d2d4a0ff7d7ef9a30ac15e2c81991860fd786406f8c055cf77de6b1065dc1418f69bad5159b94d149fb3c1ecdfc7e533be58cf6278627c11a96f55dc1826a4a3e98b8b4c3ad58f521528de07ead956145d3be28ce5b79b611055e71ad83bded9384b0febaaf3ed80bf0210f88c8df36475059184ca2c7d2cb1e29916e7c588af4f92516c9e1c0ca92b947c61e55953396dce85fd8ab0a0dd344be84254cf2738cb61c50cdfe4b1532bb6ee6e056375e809d5ec2a0276010f013a4df43a49d5e615291786ee8bcf37224b9d21264671e0df98248538046594b4a753b179b4cdadb809ca4570aef8d47e02ae159a70cf3dda44d4e7d8e75c8bfc16495c08208a1993020f46261a57604810cd0079b6023594ba7d84310668ef62d66579580694d594d3f1ac6563e0939e64cef665a2c4f6a98be30f3d2daacd46816bfdfa6e81619717fa882df1fa25175655629694382702632ed8c4984e93f596852777238820cb32940488fc1b2e49a0e9c93ca12e046b44c3eda3afc7cf83e94c83a97e10b04c494e5ad095612bd8c3fd2d8144cbe94d97f7163771c1da48c39707c30e5411fafefcbc1a3f53d6cfa664f1fc767a989e59031a38bd0b2201ef1a508b0d2d804a2f6797f69b9b6236bdcf3914995d641cb174a496b750196e35963f8a36a9b8392dbeb7ce4d124f85200e6eb8411d811cf1bd37d4dd41e5dda75936084e2f1654a3e5f8a3f6cdd8fbe933cba1a9d88a31cbafb548ae913953e0932705460cbac93ead65ec7b4c7372f7be03fb7f955a2d9798fb7e02b38df31d40bab5d4c09072dad465d0799b9868b70cfa616034dc46e7cca47be459d2779a5da2db9484074a28044eb44251e0ff2f330d2a0b33144cabb6d8cc16d090ca70f5f33615edc1dd56a1e4dd3bdd7e929fb0d3234e0a4e2b0c8c9781fb0fb06d41826c0e02ee4f85b38587fa8535844c3a8014c56a528912bcfb9bd38c2a53d9a995fa4373aec26b65eb67f8d6b6c708002eebfa7ee9c742b3545b5290929553bbf73fe2155c9189abc92e265169460960e13fc6709b02afb3dfb93008033c4ab3afb01705c31254c5ae4ae49f49c8cf298b2c30c4285df1f0a9b93dc3c3c0ee7b6c161381274674c21b3d9378a398d3c9638a09f0c26d80496d63166cf2877b65e6e37ad32ec5ecd91214f1dee9a052bf84e7854283e1b28150263e2d7de1ec016c8b71b499b9d64016cde2e58dff73ce3153d46e6a5d1893029aeedbe738395c3fb61be392d2cd2ba54714f850adca5fe29790cd26d358b504cd9d19e82385b18df6237bd1f115d0f5d73eb940eaa86b30bb073a347f2ba81709af078f775b54508a770f7bbf300e1cd911fbaabdec41bdec7ee2ef4000c7c65e7919b1a8caed0f9535df76964ac9c82b6c0d52de3f501f85008901a034320887837dd5f528468465467061cd139a32d4ccadaddf5164a8c91407adf1e7b49e1dd15fd32e5a5b7769ffb0be13e41e5fc5bc98e002bb2c38ab4a3964e3b210a15e8b0be83633f36874be1428e4821923bc39f79eede50be23e4c611506d78d1bb22afb575a5ccc45565f3d9d1c867885687e7c904c2924ddc7c7dfcaa890edc6cda31a538cccd88ca8cb98cfb7917bd12c42b1abc6dec3bcc3305f2f3cbaf3ace7e0a519077d3c2e5b7dfe43e9e116634cf596a3fe128c70ba9db5bc2234ad36a6d404996939f14191b14d3ba59d71d498b17d17812bea5856bb0b81f836fded9235b4740f8494fb9c1be07517e270f5b50686c1143130cb9970f6dad327358e2235895ecce26b6386e10123bec5d66100cc83d4387c6c63b55464e4f67078d63fc73cbf903498b4f20b903fa273758114bb3b8b6842bb67ce69189aab0f6fc285bfad2b9a0d03948093df56ca1810a05b11baf21c1171c14338a6803c0bd4a6e3fc684d3cd411576f27cedd16c5f9d1d82152d55d47e53c743011d96a3522f0e03e37eb1023e8a9aa94d837bea8fb4f123e54a3852265fa8eba88d2454b178426e8525b603b9e69bbb5c14434f2e92065f6a36a2afa8aa8e9efb5dcc50935cb2ad4de104e47ec6556fbaf1d2b758d75e7132f8885b971ba6edbc3b55118ad46f81bb4ea29d5225482cfd2a1ccb8fbc1247092dfe6960a61c3771ce763842d589774a16b6ffd3c08bb2e765e8c8aac3290f7707a80637582631911025f2d602c802633f5296b0046fdec6d756ae83250c0b913534047bc657027c35b340aecf88098b2760b72ee78e21367612fadb433bae6219914e33540225d55ff2e78c8905ec34ac5a472000285e7a675d033667cfcdc7518e66ae2a01f141e5f4d05487b4f3032b0ca580723b3f2b09e35caada3cdf2686a82af2da7bc8a23234905b18f9da5fcc9531537db389214d9533e33e2c5d1f26523b98eed887cb7aa988a4ba546232f2784d03fefbf39169c71cec9126fcf7422760842d0a6a8254fe7e20e5f320a54ae6523732eec912feb75d38e948e9e9c64852ce89636c91d7ee10130959997ac34afd35c940e4c706f45374151a1f4860ff81b11e46548e2c17cfaa6d88de3287509926cc648ede70dae6de68be52476415abaf305cf17286cc3bb0c9cccbce682eca2121bc0324871f908478af4f3d6f737fa3258c2ec3e8ebdc7782de89160d299e3c054ebf2eb0de5b813923ae2fb3771e6f9d3d75c6c4f92245633154b28ebdc679f5aa026247b33b1369e2b70ea103f1ec9dc213d3293237e01bd34c5c110a6f70d910ecf2d9559c2905951098d82a45beec704690134d852ddcd3ca29bb8278e24dcc06f5fa92b8b7f99678b3a7d0d4b861857e4249799c95b941c3fb82b6d14a96020263d9c44a17ec26f6316aa84f4f16286afad27cae13099ddf0093b5b04c0460b192862a54611687ce44e56088f711cd6530b31d4e182b105d6f0ee8a99da1951f1e11d3a8778cb6bf7bbcf2469aeceef5bab3f4344848a5dda4500febb9f0da4780837c29b3d025e98e6598f6eeb5793f20d1a3ff9513b07468d55e982d4684a1a5fe7e0ae581869c8b486aeac8ace97dcc9b50fb31b7254e9a99cc04689ab15c33690304c12643b2efb1ecb55c971ebd588a3e52cd5cef173b6b20b694502a9237d7683784fad3fe21a3de32b1710802d61fc653b5e935c8fe02fd21382b923744a0db519016aae712ea9dcd8f73dbc38ad7fc765805e6cc963d91b91aaebba449368653c50ed80bdb2a5352a458f18860c72ad05b98fc28baf5ad02da0fb22e574c1293198a8b72eaf10a908e8e9b339441a543dccdd177a415294e1f856cec12404ec2f09fb41b23d2be25b7a0ad234e75c41395427621d1dcd54aa12d86c09ffd9e024b9daa3f51f668447087eed40a64e1ff544443077d3fa98b463daf6ec2d4da1903b58b9c71e3ed391331d0faa1afce33ddae9cc8981473486fca4568b6b1ad345fd7266ddddf1a93c26c0f855c89c0c6e3c64c8a1c92c437dfb492a812b73bdc8f5274a4fe1e9a6947abb711d93908c9bb65eb8fe0d3586764090d397490265280e9bc5ce51149ecec4fddea634b9ea916d0e40e62ceb003ffe971551163fac5f5da7a8757fd1c7dd1eda245446e90e5d7088addb3894f0baf61dcb8be4fa1fce7b36ee3e64b0b7f4ddb6dffd639e39323704fa4bd9ef9cc93266b8f70c28391069bc7513f4799469094ab82e47cdbc4bfadc118df0d8371c905831ad0731d67d154df30be475e500756d8bdc86a457ff3f259d25acf549d3a605e9fd9c9a3f3dd47b16fb734a21b9c8b1d599f347a0648066e022593cc186be2356ce989d3384969e9bcb6829a258d33916b3442d95a520ce8159a2ea2d5e096648dfaa0860d059888de424f2cc25d9ff6abbc41ce25bac44670c43a4f3bc0b68df96621289d2eea83b301ec2de1226a6d522853b59245bf1fe9ba7a083164499ea59deaffdcd002718b1a49db8fc61c957cf623e0b777109757e06cd3371f297e17212b9a6c99fb134f3cc2595f929b05fde6e0b342325f922c8e9a13ba71e19bfc436eb7a6a3de1507b967e933e9f1925505da6bc5c4445cb0d7f7e74c330bdb0e122254778a38ddf3563b9a76d01ccd25e7faa211c57132e056c640709b03924d5b169a9c7e2698e8b8812fa6dce63392b9deadefb6349af9d0a328d9fa0877d09b59f8883a3b28a4c797ca8b504c2964fea357dd3ddece4a19d2cfced4c7ef8a1d39c3a7097931640198b3711cfeafe50d270a482e207808596406bd021c036022367016a4c858aa962ac28fd80fcd92269ddd3b75acfc70c70e0576ccb5dda2aab155f5b48e6cfcfac7585278c1e97e2b6fd80430d3a7363e87def9447b847e2c698d0d0f7bf6abf3d9298ea87ed797e4db04734a86aa593841d131ed1020a90790dadfa9241c6e3d5cb711ee06a04350cffcc728c72229cbf6560980332f1d786cccdbcda3738972ff41ad64b46f7fec2e831f891b0ce41630142a7f8d6a4720d37a354dc1ce2d7eb0202e296984c967e4651c14cd226bf39af8fc2f29c35ef644b0cb63a1d0cb061e1bb8753bf5c822945c348c6004b80ede224ab644ba2c947712bb2ac09af2f28bf5e82609765a294bc91c86d1d076924f8e104ff9432d62dc1ed224625964c118dcf1a22fcd0d9749b72955776a3f20df9c2e5db6d741d3edd318e41469eeef04d2e4d14886cdca781d361197ed3b7496f057ae126bfc29ab444ad4d1c3f420ee63bad65f4a1cc22ceb9de37ff6b3dbbe417a0f6772d413ccc21be3730e9e890588a15a6c8777e022c7f6f85d95a5899f649f43ff2d4493ee20fae8ed9d6c00a04427b205aa0bb45a8e521c5ee521002406a1340bdc5264a989fb41d5fbcf087cec6e62398c013d55b86e68e1d5869347019b04e16410da71c961a332b2a19edc10a15798688cb69f143252017b0ba892943794e1e1aaa6aa9152c962ecf133b3fb7dd4b6bf647e40fd1329b237121efa7859a2a485d0ccb1a205749f861d95d373214a1754f9a95742ecea80b44ab3a37e395ab11498c436eb53df017137ea81ef72ca827c5a8d6c3f751a3db87cee00ad2f673110c3a71dd5254b3c8070eb4ec99335d43c447d915809db514ef3b88f5c6c41b0e6e09b17dabdaf08e0bdbca5da8649e6f4bdd189c8546d4526830137889bffbd14bf0c9784bbed14919d82d0d126f18a47741426bc83f4c6a49bef293dec72f9e15cc15cc3dbd0ea8c7c5aaef39fdc1928b1b46cfa0038dddde2dc12c91ec025448244f7041906d8ba33c038a3796704cd892ee77305744f3c00d7e755da407b2824c3012301ed4d2490ec5164614195158aad686f0f974db7bde5b50063857466b038b476ea948c0f429d3a45383d3a442bccfc083cbb299a66d1fde7877e8483c1ba6aff04b62e45a4253124900b77c146d7064844b21a0ffdaa7af5e393e8f6e18422c8a611d0feaa4c149837786aafaa335a583659a388f2930fe3e15e09c1b17271a9228a9518c70e1b8e31f777527d727c11c054d9aac13efecc656096b20465513e48c34f007bada16e9fb31ab235492a8cf73591c424011f06418df7eaa9abb070a36fe1f62f4410022bbaa2a1674bba7755bd05b814f2c251aee72b4715fd46dcbf001f1325fc6cc8f37ca992949763f3302f5938dd8bda1915effb6bdd75076ede71bbbb723883263e390239d16e4f7af7cfeb42ca38149d59cb54ec1e7c2170a99246f7e3e8da9ed002d67459290a500f8b1495ad4e129b317c9d49387d825dac61d5eace6dc6581eebbea923ab953b95b8cea6ec26c1514896812b45a9b5f503fb89ce02fe181917a72331f6d7794dcec357a6250a0fce2fdc3ee3d73c821245da0077b5615adb9d10e651c5d6f4c69e039444aee39db79eab9359083ec7514bec92f7cf063a75030f54a0daa302b981a5de930046af402a665fd28c0be0d14c35786936b6da1988ede573c33fdec7ec1c12372544216de55afebdc95c4d1d1e824a41fd5c3ef1340b80577d92f40f91008aa29b4838e18f164bd0160028f80cb22c3b9871da81395adcb6b4ac2e2332a39a5691600edb2d279a0b40c9cb89e94edffef683fe0b08670dc043c537b9305055c9c1f753d5e5dd7e0c50b314924244d873a90b75f4421718c830ee98cddec57358077f9365e0b0c2a5cce7d2397a459543462423baa00148f30cd5e79de6db5797aad5c83ee113c1eaf464320f27801c9280d55e7be88a1020118080b70ca0b8ef7efd4ca12c5845179b7c70ee14b35912e7b933352dbaf4ec9a388621552902ec904baee34be1fca460314d3f3a27c8b3e593792014ee3d56ac69c595c225d60ca3b26b0d5219e7c71d342ede8e908f1f966eb4ed9f5e5c42302441368f9a1b7e1b20c8b2f82e814ba570a826477a39ba01ab6536de6bd7f5d626f1ebf0867c9a5a69438559009ddf8b56682fa9d5cd95dd8efe3183e1472cdab713764a543a9bfab9030ea7330213622222341bf573338c5ad1113d829db69967b4b752413a325da393798789be282a0c4d6e24299c0e2a0ca03ba6ee866be343451e41c2d50419f2f697c76010b9060686b56cc88e73b27c4837447d8d2b10c47d135e451bd73b3f31783479c161ca14d986a0d110a103ef6ad7765fd187a05c100e497ef185060895643d18213fc7d9cea7eb4b591675085c6ee6a8a1df9f1c1e358a5cca730360948e9bac529f5eb2690bb2947e68dd3e5a13215b1b0b3b063f6734346ed204675bacd9ff473c709682af4b83731b90abf78c3972647331c7853874eb34b17c032e88c7f584925c86a207311eca01bc69cfdc6e667a6a18896af67024a30c4795aaf08229622fed5ad73ec95de06b9086cb7f9c2e552f1271a4a3233b8879203037d09e335eea4c302e9fbd9d665b11df96c453c7c73021648b43e7710b978559a1ea96ae98eed1a208765e584fe943fc26b587ad1b014d6351cc0eacf97a08163a69716f1a7d25290e7e6681122e3d7a321b0c6df36e560b83e285de0019af0cfece4a1ca312e9c48794635905eb770fe6960086e387e4976a77f1d18f14f0b346cd9e2671722c6bf63eea3ef12c380a6e46e9bc489dbfcfa8c4324fc4c3405f74fc70a9fbed99b207d855131e13b57b01745be59f5d5236db7bc1f9ef38c7ce5706cf8cd90aeb9990b9c90367f5b4e8b3ab36b64b8e77a0a341007423aa0bcf0caad6019642a6105ed643d6e68221db4322a159224b94d97803aef52303e0695d304b20ad803ece7ef3ed981607bfb1977d416877324e0893d49e90bacde8aa83e8ad306f069ecc320e32b0348716b8bb5bed560a2488a85afe8ce2f2925f0e88365d1747231273c7cb3a12dd3fb940cb6e4264e7838b1a14366a924d1236ed5021331fc94d07628031698997ceb7bac52a7a360d49cf20f7d51cd698b7c2dacbffa27957b4e118c5e731c286936a86ccd0a2cabbf41050443b1554251493bbd20b14ec77b2e74a4a32590c71859eb1b1711ddebea795f2f4b345757bdd9ef3fcb86d37bca2db7b519736dd43e20c6bb49027601d8a00e68804803a82304460531e3fbf2b73f7f323cf72602ea93c2dadb01e90d4bb9f036de27756f44b6d84db322de2959495db81561d7d83bf4c8520dd1310c22951a6336e17171c936a35a99192f14fae472abba1f8f8c1de560838a8f37f0b972b307592d31e5db3ee4078165ecad852222d26695fa822038cc25f948f41385b0ce9cf41510d24981674e50b7ccdcce0a5da3f2890d30884a2e22b111d1f775c004bc0b43ab45b633488b385150934154337bbc16bf7cb0dfc42583d786104cbfdc7314b733f4460d388d2e32e42d65dc6d6166ff1eb1ebd29b388fd792e5cd11b70028e60531973b4877c494ac919b12255477cc4fde58338badd091c7f7ac40c10b9bc20132687b307f92af334a51e745e12a2694dcf420ab59bc9013218e34c8ad76f2961c71767b73198dc8c8733b4babcc4b7b688ed5dc0fadf9a07fa76fc5fa4cd576bb91f286374e163501881b3af447404a3b640e4e923641312f402a6bfb6981088bc4eacb2adf5575fc29ad5832343f0b7eeaeaf1fc5dbb39e8773912e8159dcff3b27803efdad3d01a7a5a45663120843a340b2fec2ec4c55be67a5e99ef59b2957dbe5394e3985513714c770fe4966adfd2b415fd4d1417fc01b7e8d2ea88ded2a34b13293b1fc2bd8f432b5fa278487e760bac718e41d360452fea622d0cfd1f26ddabbaa1bcdadf5d674f45e156817a1d174214130dd112238fdb3db056520e3c4a980c5f2592b68f35f420429773a42352563a91113d23887ee17f0531fdc07605f39f7c894244652c1bd17e68bc8fa6750bc017f521c89b14b26bec0256ca5b2303f29017418f07cf82a973a2348f4d2b5b78c26bcfc1c2e6307b7181f6c0b5b5b167c9f0de9ecfc5fabfaab2fff825f1226d6f0e4b878d876d68a462374739deb4d9d3bca65cd4df5be6df0b539a5366792ca96e1ad019fb41ef3efa55b456842caa1ed66b718d83d6adc441686153a7c8e7d237340a0bf58cb9c9e4f841a2880a659fadf0b73b01cc8b8feaa3b5385c2521293494eb020acfd93a084636640812ecaf7eac345f552172aab7852e048aa58eeccb828bcaf0ee4992371ef8e7583806ec02e86072e4d29414880c868cdb52507b74936ddef42968d63f3320bbe9dc66c5e1f3e33bbaa16d551b22d0d8fccf1ce3ae506e834a7c841d7993cb48965919a36de4e33d6f4e5947bef22a41bd8e68ee7cd06ba1404911dc8b4ac7301fd63849ebf63c11f6e25c5c204e9170530e25e08a104e949249f74d17dd8b54f3e47be3f0d5b959751c8b8a210653d75cdff56fda1c7854695dc3ca1a6d0873d5dbfb7d13a34ced55337030b874cf4f11416d3c621a598959fd4f0844c74f9f5fbcfd3131451fd9cccc92f5eb4fcf6dde4ac24f52cca85a5a1b49b45c7f1556f9aded5b119d26e2ffcc957f8a885be279e588b8ce7e85a6eb4645c2d9ae727a4f6e9202d01736add302bb727a5bffd6b92064c6390c66c6ce664fa6ec4a14c0a2b2d380b8e5fa8c0616f0003763eca7ee313d3bac17c27f9d4d6cf4c8eccb87eb476697e156becd6f9ab6489649822764a4e5ae93a307fa560f214d23a60aa6d23fec17ad3156194ef91b5a6e1b88cba9be92a671844b4229d79a9de75a87129875243861b65d5bff626f93c20af57e40aba4a7e2ed225deb6fab854859b524ae60e44f0beca744ee457ab9fab0c85e48f43724cefe3c6f13668a90b66b72279884da120fe38678d31f2c5124b4f47eed7f373d3ef92da9683586e5987462ce1d4b1c9b112195c47eefcbdbac79906c60a796735a0e1b7042197958dc7407fd34404264942a4273caf9e96b485cdf5b3c0c7652af9bd915c44f20bb43369ba7cb7f14fa3ce76f1116a5e01f9a39b0288208b9aec8154c4217ac95a88d1023d4474e7a53ca3a486970093d5776def29261922f1debbce634ba4041f3234d5ec636031facb6235d5379b5bb8f9432164a5e1ca8d47ece9623cef883b2b9d34844e2916059500ad37f897b772c2361e468107662ecc4066403da520001ae5f1f518a8bb8b2222a62c07938afbc18aa6087816ea5a17a38d732699491be0a5bb565c63c7eb47741d5928d61d05d408c851839d7c24c6c87f2b15e40c13e76e7573df01435fdbcc29aabb7cc6b626a2b30f75cbc42c6fee90ee1fb013e4d549d7257394bfd0d25ccd35b6af32c8924a40e171af7354f3529167e075c257e061304ce5a767b7576c0e7e37e76f63b20d409cec56bcc6f875622a013b2ea9b6ce47626aa4ba0c547b5803ca4f28562a3a562bbb54dec5cb8b1848cac56135bbc2dbda773126ae59e10921cb1f766c9e8b8b54328865f3264e8d1452e54592bf84e1a6ac41051e3e3e61e78dbadc81cc63b5dfbb3d969c759de9a499216e55a4af636dfc23505e5951a2094eccfd6f47d0154379ab5c3ce39dc7805acdaa9d25c47793c94b245c3296223cc451b2f719d66353da7067c312323ce03ae010a41847ae373f6b5755e5e866f0436685cc3b4c9128b70931b28b4ea0eab0885170a4f766fef6288adaef4d218cfb97c28ae50a630349d706d802be45e09cdf47eada95b63f6bda0df59c07b98a9adf937eaaa0a9cb7962543ee552df01e6010b73c5b7060e5c91d19d76138c615ce54a49e5361c78ffb6185aeb8681b16bd0b2ae4f0fff0ff732b023cc0b1c5f578fc36437bf0c9a34b3d78e3be7f52d586e3269f598462cc037ccf172c96cef3fb7446b952c82e122c9d65f5d3c24ab75f97d2a1b0bab0434185ee3d2d5bba50ba677a687a5c1a3f57511401260f9636c25a9810f8b2b0218e0107fc80d23216804ba7f470061f1c173cb08f5d39099e689f5d83167a05be775af27649118cac711a6167c0b5fd0211123d54a2a3be00564c2b1dffe69960663597bda48ba3f3854d58eb592e29fbdb636d5ae4f4be86f02a21fc6f0a24c3a164562d9db825f73db28ae5e05fec67ae0d4dea2b418bdebd28261bb174703b130207de899f5a06f76acf27eb97b89280d65103bb86a48f4715a0ba8511a376398b8bb0d03746a11ee2f33d9ca69d48af85ec60360ced81297407f30bea687508c93c8b5641ce46906f06871e342f659a80c4c1af9633d0f3276ed09d67b6d5aa2a666d202313287ab4ccebc4c54d94859fb606fa834356b8b267a3d08cb203ee5ff5cf62a48980d40e884e0878ff998348ea87445f3c6f2e34e38a26d0166c1682bfff0b6eaaa5018e87e009e205bbef3bc11d9c770962a9df6e16977ecce3344f9a2c9085a5cdc28a45d0ac318e26e25a151f65f59adc1dfb79829d0bab6e73f515b28562d9655385cb3b3502263e5eb80e54883abb5a876aa3074a8a1825f08d7ce77540cbb164f9a5021890f5c4115cfd29c8016695a619cbbde5810b7a011505d94febbb710b2b46e5c5666511ada959e17373313b5588091896d561cea7ec2eddd124550cd7601d74698f7473201b2714179c1e8702c1b3128f918e9d5f77c3ff5e46cdb20914d02ae74e9a7a0e436c5edf770bbca7807364afb1d4bc5767e3ec85d79a4b31bb03545c0f1b3c81c744bbe5517b9206f2053bc4d9409f0c0614f79c9a1683a1b063c30d518e7a3d8b3086e7872e6e964892b1de1c43f68ccd192e5d43ecad63417db78b40c013a497ed46e75c54234a4530d0b963d40f1231a3ecbd48b75e7d60f330bfae2195ffbe311b01a2de16636f2ead7b8d032a034acf1d836f28abe4e0738d96571003301815adccb4cb412044f965a21d0e565ab9000917a0977a72f36647dfb1355eebdef32d21be99c4770eb208e932ad26733b44b2904895f84d520ae55b05db76030b241ecc8cc803149a7a07bdc3885c9cd749aaa3e0edb8306d09a087502d6d734f26f61728d99807bba4531f5d19ef1b946554d04c8357a43ce8fe9f259f9b19b3a9571ff2d913406a028e53a0c8552c69aaa52547722bfe90a9ad3d5be8711d9ea6396bf43e021c85049cbf836422723f1ad20ff033daca7277bb46568b5720d26e264d8baa98c909cde42fb67b22de52c57d749d9d2bbdc0f202ffcdd30b5eb50d2d88a3d225447a64accc809e53c83545c4ad8e39fbfc459be385ea5b55bbfc6230f1e11d6d1a23e335ebea3ba5e95114a7e6542c5bd14c2a6cc33bc0b81d3ee8ecf4fc2869ba75a7443ccd433fd25c4f230122bde74ff88851a7f505aa87d53c7ba893a52b0b26cccbbfa3e7c476c0544884ef9fba3bd01323f67c23ad1feccb4d302b98a392430c9d05ce3dddae02ed611ff07d2068fa2e7d670c5fbcb1036273acae769ec16e5e746486018e8c59aa244a9ac55d40b6a3046a9d464f31e9aed9d9f43f7e086b2670463a9a7f468362ab04dbbe7861b6e6427b79852c92e956bc5ac8cd04e411a9689320fbc4b0174bb0d6b9ca76eed24ff0e7c763f492dd4cbf22b5caa2c7b58acab9f9c92d03237f7c11211ce331663e9364e45631fc5650959e8c9eb84910caaaf5b8f4200fb4702dcf66d5d4cf1d8e25593b0d3d9a48fce2cb35c92814e96a89e7db3ce6659dadba0fd5826ddfb0b5ab76c8479cb95081ca873d84eea09e0ac1dd69e40abd679559081efa3a5cb97761d52bb411079efaa1b28123a2aff1e698e969c4ff18de864ad165123a264bcffb08606c4255416818696999935e58909e6f87484705565f2a607ffde2eb8c505e9c24b729f16419538814ece40b45fd6dd4964f9b33db3009aa880f53030a5646f8c964c67224150cb10f8eceb586752827ed6bc761011646ffb76a6a2f74ad0e8809836c38d522a94e91508deb02b14f2cb7f87e4e3eb209d22385e6a63ee30c6274aefd33d7d0ad799a194d936f73551dbff177d10295ef15a735aa3289f03bf1f3d6c0a4ac0c314a22e4f54a5e8fab4f5273d6e28062a5a9e42e0ee6cb3e145fc9f8c54c14642a57d57a8fceeb22eec056f9274ae1e95441d6af6bfad571b33b6711303f84e574c0a15c5a587167e36168155cdcc290e87820afcf8dab6461b84c3f218c5144542c5f4fd713d964c92ce112222a00a4fcdfec01bed85736f71fc165686a8791c9f9fc75478db20b493a73e574a049fb1049341cac761570afb3c83a676f4ccb7a9dbe8b2493021911111bc51779f2a8addd32d80e1b3cb32d601ead5e5353dd29a0530cc72bd55ad09faf09546af9676cf5a383550318c69a857a8046a2e159f57a460d22ee0f3df146d9cf372a77814c196564cfb9e044ee520e7245916a1b32cf2fda3755bf6a2c83a9dcd4c1ca013a257dd48dbfe0b15f68516681c5e3980f648fe926f7f4c708014597afd10b541dc13562b531244c961d5561d7a8a397e8edc34424f1085882ff377231a318a53a4060e579daeff3695142ca57d1c49a2ca019c1401bc01815f3b2e32606d2cfb0002d2989c9391e6a5a03c792e2a83120ca21d1be61454bb0b985085f552749e7038dbda96eeab81b8d8b69060fe3e53856e1606ad81d743a16a10429453cb71633c74522f86c3ed8cf76bd741afdce8ef1f5f83245c553d80d6e17ad4a925d02fc54a5f9eebe294c4f4ae34ae8b1b68c6fa10da48457c5dd238fc439ef9f34400de94d3955965d031c6bccfd09590cb98a798ca43149f30ac201ec0b1f9e6d1ee000957a3fcbadf3044ec6c7b81c0f4cd1493dbdf1ee5f9900cb6ae1430835efa301fb960b37219c715b9433da9b0fd1740087eb9acfd3a244d8f5b426d59f7f21205b88174f302de29cc84b5884ec3355fa22186ec733c26417fe0371cb65f27d4c50ae57e3613d38b03abebbc4f97d1aac1e0f8e21af99d74f61468de08a25d79f75f29b136b54c501314edbb39c2e20b9776cbbe9b45bf22ebfef5c6e05090776778d1ce7fe838230833522fc7123f61775006c739ab2d46eee4ceab67c8b8beef803a9822225b4f0a001ae232b0aee9d56e71e988f8c45b41b0107c114adb799ae35182d34ec02f25cec987983c64c5e61141a5eb14bb212c769cf78356e3d835b8573e124c48022cf478584be0df360ff41999b17a1008f937b71ce485f96bccf249bec68d7e362dd142b7cb4d3cc9f8c8791e90199c2b552a43894463a3f9b355a293a132cf1ac619cf2e9b267de72b28391b55a7fac8cfe0a00eb9ec3e26d623b7f875bf6c6a0ce5d4151b3c14b302ced732e5fd1775a4aa2610e04ef3dea8e45b6c1284f21095854306026a9bf865f95af61c8ecf890a54563f9f42c4da0485b68ac06e7b0023c0aeeb09e76a9e957991f173870678d6855b9ca9518a9dbed9881757aa7574b17e803066b9d3bf73b05f2a8e88317d0f8b87b29488729091fdbcffdfeda9316c7d3a0ba3bb826b0e051b0d7340233d964373408355abc3a03e76973f1a37928c6267466687309c78a4a172a7f97d479a64b5982b88356a4be97486ec33c30fc10767ddec429c213e2b43ec5aab397aab6394565d9c71fa754cc20923e5e0e76b0c02130da1aa72e309d8326a73389be13fc84d56a5ebb333127a663b9169dfd7ea8babbbe4844d616eda65695262ec85d95455216eddce0e26cc4540aa15f0ca50374e6e4f2c57143bca7d8a5dec7463962e281ad1c5e3cfd3e1abab367e94aa25cc46e4a8129a659307a18104a9e0937babe71c558de21f7bf02d40f32f91808df20af6112a4373ebba26759eb7e519ff4a24fcec3d56c6205e4e39af169228fa038053f1b0ea7265e773adbfa547fba32715f4bac5bbd9423775f31fda81738ca9824711f304ac9fd9e8496701a69f4400b3ff44ec50961e57515a6e0bfb61c6323a193bb91f897cd826e8b2e1be5f122772739522f717a41697b21f667a34aafc4cb4f09bfd49d8d9d29622ace620d44312195dece4135f2fbb2adde2e9f1acbf25e7f46986a458837c4159dd0f18f1077e5e96a16efca7711ba606bdfeb3e95ea750858a00587f0d9356444c2cf350fcc5b00064fe0e20297bddc7d945f0d4411cd9ee663cf8141aa09b3b054c5d12ba35f4ec6cd202f5feed37d6fdc94b4a5b07a524523300bb017421cd804005d64451bc49af869a3878475d77702dabcf043348c54d703829181ee10f64bf4cd21bdb89b81086c052e51a38a24c40e429df292fb2ff6e441411b6eb5931530de5c1ac96fceb34e7df0e46f79966096bff66ff5cf72babb18be101b56ac3eddc4d9e95b59c8c43c18d13ee7a80f89a88278f8767455d820e3bd005c31f05b08b9a9564b2efad418d3aa7f0773d53b6c9a64272beff16fefea016081af78b625a060b247187eec26d6b76f10907a3adbe5efce65c69b2d83f1e4b47802cba537e45b5305401c4e84a44e23fdfea57f0a8e05ffbe09f45dc329faab27ec4545803d1b32bf16f4a2993fa1bbb8c2dd3a889e9a2758186dc1115e684b546993752d01f2ff4e6368832c05741f11a4280d9b55f0dde0f9b500a1f08e8ec4640570f2b8cb40a0418d640e7d96ce323040c9f63df57999ba476b29ee565a1b1214d198353ae951c4a59def2ba7fdb8aa2546170ee5ab6317305765fa6573bf825753e14181927f12880db11d19072eac8ee105b2243b9d622a650b7591d328d133c8ad640799fc6674c39f63776ff79125de509eb1b9ef2e213f69c55e5597aad1abc83dbfac8a1cb263d0335161f18a95fe2d6be5f4da93c372fec9c371b41b9729e34e7659fee62a698917b7e482d0bc8f73dbc2eae58a64bbfe3fdbc56e93c8298cf624173d17a96f388305464fe277b46a6a86ad0f4b933d5357b0c7b6f253a1ce45d3e49b762f69d7ed21f2ad4e9c6d1fba403a4e1d96b7e6112b62e4be53cc57fce2a5e6335a2d6ebc471c4eff3e2c48557a445273147137f81756b7a09d84abd106982fa8101f535ae2e5de1a3202ef01a0384c5833ca413ad7b5cb61d4744c695d48c0f53368f9bdd0f0709510dcfde9ff97f6644ddcbe2f8bcd82e3b1d4dd10d36b59204d75858b3e64d318b110127bc0ce5c6b269ce15c2fe8213d4b75c65caf9ec9d8669d5da1efadfe05638af25cf627f9cff47e2cbf71bb306fb6ae02fcd534b1370aab4cfa161fba6d279d46fc66f0b84e7d5c4c7b459c84c062aa7fa0c61f7a4865565db459c7a425a086e77e7ab613ce0e64033eec6aa603de7301490eb9d937752412ad73ce9c81dfb3aa7be83ae3320db8eefdc65c099efee6b17592536f643a8589eb8ccb633c05d71c3459771b15dd9cefda23a102f0e3605f9dbdedf2cd7801f9fc537e79cb7313c43fb9c1ef760388f2bfea95e0aa662be6d160022d0be3cfc439cc8a7116c6ccc91f5487d29b9be3fda47493665b013b331791d1a3330f5397c3c68ec2c92508aec1da11484b40b4b1917d64e3085d9f844a565a563377d5e1c9d2a3fe87164ae0e52c5418cf3342a356092c2355c50a81d7e36387acd3924c9b2445b2407bafb6d9f5880902dae75bae72b1ab472aee89b4b88d2ca915795d5f75ca69fecf535d67aaf6c637d204406705c4dc6cf2ce88816d3651820f758ee84fbf86875facf38da8cc796327e800b73e6ba8747f158a508921bd997e8a993b01d8383bf3a4f2694ed3baf95be3675e1927c8aae5bffbbe040cc501f6ed28bec8068dd8addbf01b3f28b6a2255a3d75671ca4a86af03929899cae93583b2de75b00348239363e68fd47062ebfb1a7036870cb661d9cde7f7bc57e5593770367c9e6f913458ef110fbc1fdfb99ed2a957252b13b80ec2e5c7a985f0d7f997da9fe09474a7aae2657b01b2765d5d026a69aec370191df60ad5c2d54fed48b235da8cf99dc206bd4f2337f93c8c94d6c97442b4a77c447e3dc0c1ff33dea3c58a417812573abaf75743e74d71865bf14b9bac06b6d3146f4baec817f09bedfb65974cdae2d79cc4709adc8238971f26664dc5b53174855657f90c598f85ce77353edf4a435cbb3b20a3c0a93d8d805783f66991240b4776dc4f46b3009ad5aeb3c64df9ce1278bb19d19119a9ed28a1932132e980cb2cbe85e3827100c05c50167957bfef22e1fe57c1461c5615deff6d71c","170303139d096d60d9d53c6c7db76682ca4b9f374156fb6f02475d775c73e429b202d9d671014a758970c2e9cbd3b7771d2de7af29b3ff342cf0fabe5af43c085a00987b19667446f1389c05f11ef090920ad7a935b7506a2e836b0af00d4734a4ef9118cce78304d2b4695b4e87e46435304a6588cb920fce1ef2c9239d219e74740c9c4ae71393752e788510afaac5fa47ea01d18229163161cc9e26a829b70ad4a24197a9d1902c6632656fdbaca6f136ac94117c851140bc428508c7720f0dd9f86c94e6bad9d785823c67f76d6b10aaa094caf106811cc25515c0a61711d02a1c09ecf98b2671d6b8a3f6204ac2c89e35af234694ded7bbe7f989581f1ab5aa9cb30a2e202d45addf264a34690f98f37275b6d08040a7e627fbc996ae1ed2bbadadd51447580dea5fb0526c93a7b7c24590aeaa532b324cc99dd688b6a2e95c51b48edcb2272486dd54a36e6d346ba07fc4298b0238e6d5e8548a8ed4ae157eae4578666e989c0d1ae5dd9fe26ee00452585c6dbc6dd560de72ed37e30d69f2b715915eafe38e84f8eb187735343aab25bc69122cebc0ee393364cd9d8d31ed50f4b3fb6ab0b5ab2db2745f74234e2523c2817d6dd76d58fed41ccd3da6315dfb5f7598a0e64e3d25093c86afe4aee8a514679e4abfd0261b91c542fc22f8729c6ef14af275ce65b5c3144c0621fa26546c388467fe145ebe87721c5b836295e190dc9b10ac13509095d54021eaf62eeb7400de9885a8ea74c375fe4ced1319a90d0c520251b0cce4ddc396dd5533b8ca69d8ae2fe01b5ab290125475dbfaaf095a2af1031aa1065998edd81bf389a4c4064963a0766bf79eba97a0e2b41efb14a675345a5f33caa5d638e8ee021c3b38feb73709dad16c35c1a49e4965fa177768ddca36656d6d9c93d92f24e3d93bf37ef46f2367de17bb58b6d7d9056529d27c1c6823c686b0fdec54d5d92a3bc94b058c99aaf5178aa88b749d9cad4c80b9f1491626c4fb3f149236becb7008bcd0c720c406d9fff5d363f69b69329e965c45507809121d6d3e9a93340cb5b5b9748cdf943bd84b20b7dbbf4792f4f78e8673e2db47570f1ffd5614036c53777f9ec62b5618315c87894bba335d4380dbe1426fbf923b950c21bcba6e99806b82171abf048979a1d983a8460684d66ed1bafbef8a50e97e65f9febd5a535432258b10ee56818996c96d84fb76edb520d5710513b8ea68e7333158142b87f11b1a5e9ff27da21b86a907a8ae4cc1f463b3dd78a3097b49254c055ee8a776c81e00a521142f4eb38e54784ce1b49ecd96a7de46a60f729b29046ad9b7f3edac168292cf001e0d35797e6451d38a3531a3226027c90d1cf0c589d3e6c19ef2b06eab760dbb4aeea31c0d5b32778d48643e7fe157b8bb7a313eb9137e6656a82c4fa3aa475656545ebc70dab0c3a5c8d5d4c5d11ab9ccb1daebd4b61a3e7836166c2b92c54a781ad70e8ba9474babbb76a1393cf6ed00c57e00c6f3884fa253c647eb2e782e79ee3717996aac10ffdf9c882e00d5e6d021114afd42b337905c5b9719491a990b439d7c4752c0464f10ea2c6ffd8e81a8127367d1315709a833c10cd30f08478b54379447df8a3b01e8e79e76b25cc7f78f6a3549eedb17f344af34a83896354b1f436e05ee568513a82cd9a4fd2a323c4053f8dff70fce863ab251aa0a7bc27c598fba8368c502f6891cf5ba4c0e6b056caf389bf093c94e22b784bc453ad5668f4213f6a76bcf44e574221778bc69e16b583b0e2d72b868bf71f5508eb3063bddd6f5a758da3e6d699fef99fc9c428d8cdd451e4168ce06185bb77481c30cf446ca52bb1eeaa1656f8591d49ebdb22d6c179482cee9750b3eec588ce06250832a48f9364a6168a0fc7f32b0e8cb4d3b04d27676a24b1177a161a3c78cf60e13b9a1604d3c42ea8bb3fafe05c2e9c61862784d19bb5b9d8c4c757c9e05278113eb5b828986e7072c74bb66e8e2d84b2ba3431d8ea227795a50cbe77ad019e265ccafbf4ce1156965cea644b55659ea0db164bcf55da602aac99c829a09827076848efc8795b8163bdf8b89455fad3f72e3664df8327a6b1c5b575d62f39ec7d44bbacdd07786e884ab453e85dbb604f50a263de36b3116abf83335b6bdc1d93e281ccb226e99b9a039825fd40be1d34cb4d6e9bc830e6af74b139f1769f6ef71303a0bab794d487066dd38514a33d866ed54f3196b3db0994a68f1798bbe7288ff64cbd8f3f0afe3e0e76d2a8b58e1b4619cf3f51a102481e5cfe51716ff283862b42f4d162584146ceb59a507a40db96760b40f4b45cea63810c7e971511be1cea58b467dacb83a5d7279452776e10c287ed20436ba73b1094dd653f40f1ed1a1358125e9035f378cb695da4296696a14065bb0dcd4b07667b80a8f6872476e272d34212fcd7e8a4050ff7e35203b932cfcded981ee28f299efd5cb3151cf4cbfe308571879507a883d441ee09a7de7534fe2a358afe4afc3193c7cadecefe8dd419c8a8693a51b84ca3feb4e3daebcdaa676c4c909b74ee934b49cdd30a03a00c17db270677d732d77e64737ae7dad466c463e01ce9b27d6f3edfd0980e7c4c9ed40e17628c8b5ac575c1feeb6249a6ec1cc2100bfffe822a2a148ab0eaf19c854c3871833090e0233c3675563a13a380bab7f51177d15dc34209868dae0996cad3bb54abe4be159609a1e901e7e529d83e850b874590d2a95948c9246c29ab3ff68c24065509c5ea9e9f1b1f8d5c4f8f9bc7898cee62067cdceb6dc834873488580b39a3ee0ea456470c309679cea789ba5554469dbbcb97c79bafb19461b260385c7677efaf852e6191d9a9a4bb27ee10dd520716cc568e37fbcf7c4ee26205b1d1de254f19ee1ffc4020d52c26b5812a3837ec58a30049348766a221d900a1bf130000d3361f4ee3f54da95aa24660c2041312dd56efe43eb7677a01977cff5a52eea0f501df895f740b2552a0089f5b9059acfdd81a6244e4f6597151c52600d90a46ba1d69f1b39752d83b106bf2efa8ceb3e135de03a275d0485f15753df16c069295939b39a113ae8f65f07728bfeaf44304e9ab62837dfb822e2e26fbad534b8ba6f5a699d07556067a4166a16ff77967cabcdeeb72df5125d766e17aeceea8e205cb019599421b7675d6b29d565c08770327065e23727c54284c277bdaaa30bdcc6a5d3e09f43ddf303aafe7759b8f928d9b5f38482bf8133b6b4bf7f329d14808bedb8c293d83b5c9b71f8a80f68a1eca07c913c6aa85d8a3c4c41c50363ac775c416d49f7414ade9088a1deef73c9d7642a830dd0afcb0e3036fa6652cc469d3a6424e06c158a8a7d2b4e390d727f19e481ac163a7e9426d016d589af8c193a5ee047c7f02c1ee49c562278529d1460a649c3586d566dc14ca05a016535166883909ef434127529d53b5ba578b2a2fcc4aa43615fdf8447466b36d9723d50f9d06cc918516cd8715f9ab10235c0b1f077f9f98100bee410b79ca0dc04a5bc1432099f4a6b4ed86e4078c014f7f79f16fff1e6c9f90a8b255356ec409a5e7fbda47691e3d927b75bfaa769e47e72c18c7a6a43d2cdbacb24fe531c77b17a26038f98128277e2b3af3ec399441efa65773eac64e87a81b9f7e97ed0b2fba9fb0b7479a77f0fc60e70382331fda4d0b0ded697fba438ad0ccf3e07acec746c109e0c2244bffbed58dd143a26c0e9e8c039fe9e8c2e09d04e6a967a5f6dfa994212e325317f42b6ac349ea0cf52d6b94349b287dee45956369e27f2cf3791b03cc02cdcc6b681ec44eecc7c259effc2b063ae88a5a4cea460ff71ebc7a3b47c98c177ddd09c1c5f1e27cf7852d535f37a5df85534b8c40987db3aa543b0ff200fce391ecc3f88bec4c481547c795256ab7e5717949388b141175a92e058d921a1030ce8da28e645a9f60afb0b82914d44c2c5d167062aca1570bfbcd58fe1c54153d944c6eed91d5c083493f346354aea2b502a2c3c2a496d303f77677c9cff1d460796a418adb65ad594fd577d8b9571a697037d1fc2d498c7408e44cd4cfb85f0f4003da0793096f205c19e3d1e8ba9e038c5a6c0bfca1ace3a49075271cfdaf529d6a29fdcb745800fd57ce26694d33e0f4de2e70123797f8cce037d0daa70c30623bfc0bb030000782e6eb7ab882277448795cd1dd8db78159d12fbfbae38a15ffabd8c516edd4610412ce77113d1a36a2f9ba5df88ba3b248e4dd65d30cde269d3e68c6c283f7f931e6f8f13ec0e33f3f0ad8f379ea20c4c4338a66970f588793a7c1e0ad6447f33701b79cd0ad959a93314bdc0dec826a5d17fd3a96d1cd832197fd4ad438a9ff2eaff52ef219c235c52d06e2ce658f0b4d17898850d47f54f84fbfaf5fff462cdfd37b4c79ce3b49c59ba6a431bb4a0d73720c1b8a00d07c893eae13eb5d6f5253d5dea1f0c2285fa2fb6a5a7325b7543025d5bf4a6c9b81090afab57e2f0e205a2979ed9467020133cfd144375024830719f3a2d223c8bdad2553931d3b0007a716af758594e3669b4a2454a684fed9c3bc7a762c5e3df3c8b72d777e45572b074695d47007cc98ef9d11d7842b9a1c3d9ae01790b7bf30001f4c962d79cb8c686bac509e39251e2a7cfa9dc377fc3b9d46a7747aed1a6f14c32417a5636ce3303869e7694f4dd1183bf801ad25b9e6682021b59104957e01c77291efa1e5eabc745d510cda39b353b2aa9a19658398ff14747066fa243e98e64338b33f2360505c0ddb18278438a0ac1c2deba33ad01dc4668a8117ffb14e6197753fc10bf314143a8a9072c55108ba10ef91974ea14c9db7c58947a0153bf7d77cec1b47d6d2370c76183b75196b8a7c6b71ed798a906bc74e4e3895aad1964c5ed6e56a741806d736b3ae80c53dc0c22a98637727000e497a43194bba6a2c0c8f10b3b73056001f9d98df051ffc841cb9791e6139c3915933ac5eb0f48ef330f71c680c209e9b41946e3dc3395a1e77456bdc87805a55c2963184b7153b087182bb101325d6037a612fab03ad089faa01a4c8ef8169e987f4bf75fc6decd9de95d954abdaa21a178ec7df2a93d55084bd99fa1fa24cc669b7082fc166d50933396c99049003e822ce5a3bd9cd3c57139bbaf809424097899d8ce0f5eb56408807b1f16f9945545cb86c6fb8b2a5f43f26e8813d50e4fadb7ab864ce607a53f948d598fb2fb16e1481a92c62c59e80ee32d8ed9c57ea4ba08c9580cc29a8c2db2e59a0b092b9d762d8e65c2776b82864cc86aeeefe040e9dcf28b1cbf0e5b311fd55252d59f486d9681cca3d1a429ed1945e5d5557c8782407f89f2c0f2e90e00ec6631fc2d1c7a8e42a91035e964f613b6490da6f4172bac74d0ca97cec1aead5ff4daefc30528daa698689c59ec1461aa5eaa285a9ac64de7f2adb460a1d610417a2804d7046d5097721b36f99e6677a5e0684f3c8a222927b56cae28f9b4d2f1822d60bb47c0a0b4b5cf0ced31a791fd021a4a8b6cfe6b9ca10bec0f9b899d8d49f0dba9b08b61556a2a1cd70efa4cee36ad65e949860d7949d0925c93c73cb16933e06f2650f4429ac34402c2028c9fdd935dcc85d5723e59cb2dd2f8e68768b30b19efafaf391aff88c9d5ca6a4317e24a747a25711adc27d664fe020f9110cd8763328a7729347d6733dbe78b45f5696208331e9f1eb929dc69ceaab71a495f63b52a5e4a119ec2de67ce16df7d3407c319d6a7840a6993a2141a9935b3732ec7d0cd4f7f26288c1f7cf407898df70ed728c2483c06d74942a0c97bc437526fbe8eb956f0d8d306a79f192b5992c37680bad9fabb9b606217cf812bb5e5f9e32d743bbfbdd8841c69e0f79866065bb60321e13fc0e646354943736f964ba41a31b7fa37fd283522b940989b8fb508b19c8061ae63bdcb29ebc8cc1b4b446db3e0a05d0c03c05c3c048f21a7a7361e9423b710acb21e4126ddf761b88a8acd38d9ccab0192ca1c5472242b3207f42c80de224db44871389ee20c0e60b1989193235560b524a7b7b9d6e7af8f8f7943c60a941872cca719651c19a5681d07ef2c3e2b315ac4e586d0130c0a441db177ace4b4f16c4a6537f442e779c4574c63af8a959a73a4737d93ded994e0297146af5ef744a853946747821f26e120f107af63337251b302a6e66bd3ff042f7bf28037ea784f94ea02efadc7bad3a27451698cd93838c1717935a5d6884aee8b20f05240f08376f947350577fd2f8e951c7b5b10cf1cf73fedb991f1339040b61a4f2583569ae581fa5a6e76408ad65e5c7f5712d36ea574b4e85f11939952faac455af1e9aaee5cb9ccaa9358e57e851c23f831e706eb796fd9ed7003522f89f5520d0142a6029b1e733d55a3420d4b9c5916daa1d97c14c67c9bc9cf4cc0ed5e20098dc14c89fad68e5766c95e10dcab6ab80ecbfbc6f736f554f8be8ff0d5dda9ea2b0c60fa21c3a300f6f7c792ae842667a47c1a0379272d2348353e2ccaae1fd6342a41b5ad1bd200b9f08f32822bddd979113de3988edc6e3d3409e2e68bea2ccee8e7e3359571f34904c73be238788c7cb1f67e0df2d70d9cda00dd5e9aa190327c31663ecc8b6758d5eb1efe12019860a0c96e9c56b67bab7019a8018859940c54b7570c12fa1887e9274c52661825911042d99915ae53c6d36144075eb6acab61205aa0ec9a62ab3aff24aa8d3ce9eedf0475e862771e6dda75169ace79db01dbfd7d2cbcc2328d44c093b6eedba1d0ad32a9244eeec10b730eb37229b7abdd97243ed343ea0de83d45ac484d9334d626d9e47a44e9a239a6d0b81d4b1dabb935ce1bdb9dabeb17903dd887fe16418514efbaf4eb1ff19235a29ca2814044566b08011185933afa828b8fb6743ae7a3faea6f097a936879eafba7d774d5c0cba7b1cf3fe817bce679de2a925029c2b13b26eab702a1abe12a55a66851ff78f59a2c7bc25e251b0f81c1d4562b289bdb4736c2c2b26d3338b08534ebdbe01dc740514631023b6fe09ca08356b1228e363936fa65c10e1687215d5afa601ab814056b8dea1f004ceb47fe151dcc9c8fc08eab825c5bcd344da8f2a2f27a50d0317d2848a7a04fc56652fcbe8e99fb","170303003985d32f7c4ee2d6b1f6c19d43de14c767f2f3f1975881e3435947af7547a170f495d6e3285082e79a20ba5d923cf8a0f01b636b14564d3e2ab5","17030300136bc9a581fcf51b1c5b256ecbe111babd3f6eda"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
reference13-client-hello-split-over-two-records {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303c214c61ddb8934a1bd79a8edeb5618a22e950b65aedaa1a52227c70c4a01ec8720ea2f66b52d3d963ed3169b821dec4ec8911ac9693bdf67ce7c593b9ba73a1fd9130300002e00330024001d0020184d08886b0e2b5888b4904bb4e2a4c0b56eb507755601de2def3ea85673ac50002b00020304","140303000101","1703030206a6448766e9eb8ee5a292e5d5f38128657963bef76c15173dce1f645a8e53963a19db0fa3b0609864ab7d9f1a4fd5a954f5e6e1f32124a92e90c901aafc360dad83adfd2fd3b5ab7dac7fc9454eb0699ca1a4d10f4bea7fd04e3f41d054cf098ea96c71bb46ecc9620860cf7159eccab02a63056bc4d02c7ce3b42f8f4f00f53644cc7ba274397373cc702d20980700f19b5280e9587c6f789356011e218ed0410344913ff0a7c46a61de86984e956ac99bae99c3949d7f27a0e9a47761a220b6393d1eebef84b35cbbf87de016728187400cc3818cb46ec9bc027f1fc3d8b8fad78c5df990ec73bc8e355163e26850e1f7f5212a2fc7692c7c4f59c7980366f42fb1be9aa137321b5d5498aec6fd21e93675b6969507ca782331283f16ac1d69e78f89eed2064f4fd3b0f4655eaeb2f396b8e5b378d146846cc9772b5d490398d9f290749c917a0fccd4f813af1ebfbbe0ae162d8b62bba181b5a52bb8249e1f016be71e3617d5d5ca728ab0534019656cf111959ac1cc99ea877c4064229a3ed48a3887e1a7b840c937a5a4e5b76334a98d4fa4057b34ff628a9e4c638dab475b585de3818e30890e1eefa5b52ddd0d6e4f13af28f927d530f9e78b214678a1182e22c91c1015c0fd9182926ff972aea96b1d8e63bbfa9d9f6ed4bfae346bc1747c0257629ae00e9bfa70d93e9feb91ec4431d7e6bca4d092dec9e1f94ffa732735a4be6380","1703030039c3ce4be9ae8bdaf7eeaafaaf3e5d054c5fb48badcfbae1b703006c2fcd16c48ed4d8a29f6c0f30f062fef4969c5d43fae084db120c7c55270c","1703030013c796e86f7680073036f87cab5f9cfad35528ce"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":false}
```

<!-- test: tls-handshake-server.reference-client-authentication -->
<!-- tier: stdlib -->
More client authentication made with the canra reference client: an Ed25519 client certificate under `require`, a P-256 one under `request`, and an RSA-PSS one after a HelloRetryRequest.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("02f973efdf34afdd51aa29e7e75af652b6e085370f9e1c3b9a4f547337193ea9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("9e3c2592594aa5f6a416afa341bb089da24f7a33beb5be857cfc6ced8ce428d4")), secret(23, privateKey: fromHex("ff56d09253fcb12bc6861b03aa27705dea3f19c9b7f8c53ae829d0cb68f260ad"))], pssSalt: fromHex("2fc6ff96f5098ff899dacb71d5eef44104807885b44c23be90b74b2087161ebaac334b5fd77ac420620d2c094ba8e72573c34e2c04196cd860b38f329c957166"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-mtls-require-ed25519", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303cd1767e46e7916a958d048f2419edcdf5585f58f41f2cd670296331f513c8f942040a881b98fbb6a7dedd294543d1e2734a96b016fa739c76df499b3d00c10ec030012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202645183a7752fd6902a43fcf95fc2d80dcb490f8207d8d6760226e46af28911d002b00050403040303"), fromHex("140303000101"), fromHex("17030301cad13ddc3ede69681f7a4dcfbb30d95c5af465ec5672af17e8db860a3ac16777a5491acc52324c6dc8b79b56d988eb35c267af388de9eea2aa56c96c7cf65891193aeecc0233922af0be2460c87746f86169d2af887e6797e8ddb897eae3d96885ff93bdfec93fbf01476b05bc4c44164e80465f6a8576265f13fe2543986774f5c322ebe15079248dc697f7068b6fb1707b3847129a4f3a49ecfe8529716d3f11268d475a29c05ba3221d3b75e6d12f9153881f3959418bf8e09c54521203329979e382f61e3855588068d16fd17a2a2a2ac5740d3f1a61b8a51507e6e92565c3980b21857dd96a1458f57a40c22bc3baede76072926c3e5331f008c355eae3d4d529b97fb01dc00052c1e0a71fbacbab80d39910b0f224ef7cef70150d17bb2b9ecd1b19241295cb2c99913ca0a528f885a194fdf9962eebdac6c59c3f882458b0e592c80ca1d592a911e8701b42bb2abb01eb16b6c450720d80139eff01af65a724764548ca1288980b2b89be988e112f0798ae0c52b9300803c7f232bdf9dae2ea105d138223f0c11a5d093a573b3f1b39c9e280de7a57511fadd6809c4504712f98705de490d42fc76ded9a8cf5400e61f70ba05798821f63b4338fcc35fda02aab4fbe979f7e4a71"), fromHex("1703030036064ffc924085d287199f701b52a8c25b99851f5d1eb0c854032c000bb95d27c77d6d551929c480f7aa92ea0fe840721aba29e9e86ac9"), fromHex("17030300130a84cba9b514211ee0f8ef476d8080efc10855")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.request, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("562af925a9c0870cdbd39b8caa07d4efee8562c7bc603357e30a938f6ae07635"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("9df883afc0ab89bf2aab5f2ce8ad89f9d310b92313b79cfa3ac0cb422f43bdea")), secret(23, privateKey: fromHex("3ff1392f41d671ee657f0c2670ac932b35291307771ae94896854d56bae14372"))], pssSalt: fromHex("4b21702888951f8dc8c907a642ea268ada8bdfddcef914b736f628dd4fb06bb2ca98b5100a03a892eb07a46e56a2dd142083d6e7393138a7f855f47129b5c2a4"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-mtls-request-with-a-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303d9ab61d424006141425eb5bee5278acc66501ca3934fc6a1251c160fecc8e2dc20e68a0fb43c4efff8a96822230b6f6289e9d1d41f9d5959e3f67704f7c310fe490012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020101a731512f9de271f4f025d4c396ae38462a57b14cbada405aa84cc5725d340002b00050403040303"), fromHex("140303000101"), fromHex("1703030201544dc35ca077b9e289ff03f37a6c010064cf53c5e88549db8998f5430e4b38427076bc97b1691f9adc3a1ea8a66727490215e5409d33d670ff706bde75d9d1b342f96f343e2cc15824c95f01549dcc818a39abfb3d9e50b59a6a5b50c79cf09e4dcd06da302ecda935410dbb753b23fc530bd77e3f0a5d82b040282bca0739a8a321a3f5154591857935270d1f7e18e38e16429d1fa591708cff071f2f9446e7bb381101f1ffec36f7ca6bcbca78408cfd20fc46a5b30492b9b5587f65893a933fbc81f8c67367f92d00b42f40b4cea412aa5fbee31ab72c0ac81f9e57c715d5bda93ee5aa66a52d83fa55bdcead90c587bdfb009f9252c637996b0dd477b4259cf59862284291bc0afffa4a4bc78cbd228e6b5e5355019d0b611f1f890e06165f8dd344e25f6592c5b9c70cd7d7295c23181bac73894adfc5d961e9ecc9e36f62f2ac93500d564234f8fe4dc00dbcad953eacb647c67e1b7e3ccfdbaa5eb55c78f10949d729b98a0128b8485c1e542195826f8c1be65bbe0edd9094846a9d7e49655d2b4d92ceee3e98519d3ceee28ed40a980094ee625712e47f2af8ee3552cc35e5f670c92e7a068185a648d717f06ed6916f644b46dea04f71f4fde55ffc9e9de4337633eadace2a9a29506ca3442f341f6df62aacbc916e25f2625dc4b2d63381692b7b879812034fd16563eb1ec23a6bc5c3e1896601ad17113879523224"), fromHex("1703030036459d0bd48a4f85802783bed759ea8c21efb865ff82bf13987ea0a4a6dba5b2bfd15175f19f2a272a3676a5fbaf4af5b542eb49ca22d1"), fromHex("17030300133deac224d5364d3663a6fa4f7b6c0b93cd4f40")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("9b6e6216469d79e3144b83dae392d5230c4e9c057cb2f4a57b93d292bb224e9f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("65b38ee349147e286e61d8c161af96ebf4f191c8321a09728e5ef4e33729cf7c")), secret(23, privateKey: fromHex("f5263a6e3637263ab142f208ecb81c507f016360c287f77849d13d6479bfaf80"))], pssSalt: fromHex("d792207cb61ec695b407819df5d16ec9f7fd8e95550850f87480adba99e1c9edacd8f8cb74afa5371fcad24a661b04d70853d4da769f354595e4d140447f9f92"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-mtls-hello-retry-request", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203036407c605f5850da103858e869fc99d038fcc5c45f451ad7bbdcb2ec1cb2f0710208ee2eb497d72af902d7c9469898e354d0edec1a85263dbef2d17f85b389a10c10012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020d333d05a721ca1ef77450a4d531eaea3760286b0e7bb9a29ab470a858e331604002b00050403040303"), fromHex("140303000101"), fromHex("16030301070100010303036407c605f5850da103858e869fc99d038fcc5c45f451ad7bbdcb2ec1cb2f0710208ee2eb497d72af902d7c9469898e354d0edec1a85263dbef2d17f85b389a10c10012130313011302cca9cca8c02bc02fc02cc030010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104e332fc35b00f0fce194b7a0344eeb599664b8285b1664c4a30660c0c946633ba01ae1b5cf0f1fdc0c774e3e4be62cf2cc865093cfff41630ea8cb58231480e9e002b00050403040303"), fromHex("1703030382eb93506001754f5b74fc132b4d3c5eccbc98d9c11c93c38bccdd4d5e6833b58af79f97e829125e519978126676e620ed1dcce48a33bd1f58896e86385872c9c19e17550ad82495a73985d65c3223a3c0f1834a818499c4463b485ce46f135baad82fef8c02bdfed337f8fed465425df5bcfcc0456087fef40351a1a6574b022c5cc0db4ff4f9b75270721f1c0a512b1e6a75823df45df9a95323471d2c7ce276c45ac09c2461689a7bda17ea41e89d8912b8ff39bcc7b6fcb1014126b255a04b9da2d60bd13d42b78a1745cee7a971e2d1d47c056969152620dad4d7948d5481d855a581682a717bf3232c62ad2b0b9252a75d0fa42f6ab50e6e13c0e2d4dd9d5e0ce390548b243c59212361889c7a067ef3eeb0a786792fe09f357f086ba82aedb55b8cd96f8dcbfc7bb75509b462fcd2a2dea57cb3babbde493d784c0a4b9dc78f1b5643301dd4d7366fbac820dab6efc52aa8cc43c4cd2005a6dc5a2ce08785717926af69056d3245cf663acdc30875ea4a4a8dbf5670ebf1f92aed121f93f95b937fb468cc855891d656d85c61e23f674412332057ec86605c5fd6cca82e9324a25d3a9d2ae16116fa378d137669c04e4ed2bc34d065c13c408ee97669c64e79471e37b3e2b77312dd29d813622ac4aa8e6be66d0ca5b57765efbe6cb17a644b883d570ab84b89e7dec3edb16385b897d77f98c8272adf5529ded7415783333de77c051955da170a7a73a19d80377b8b68d00b39bf2a6ab4f940e38292d4a7ab06b03706dd8684f18ac95648989285213048832a4b68f69a7b7ed345f4c8facf706be3e7dbcda7d99219fb61e21eb021b22d0cb7bd472fe57cf15629ee48d8b77465c22055024b60195f4015b3f16d2b8f9264a62fc5388441c3226cc2c84560ce469b19ce1aa65dccab88abe4bacdcfe00199ce178baeecea7a205fab2443d4bf2aceb8124601b0dbbf6b2beee04199f66a3324403687a4a98b2fb0a889129a87ed60c11e2c4ac0c74064486e5125bcf29fe15f20952b3de207e106d4298667a056c4fb7cf51fd6521ca8d144607bd84447766dd19b463725bff0d73b9ce97665d4a79a83d2ade29b09e805731a932054785445e33a369a5e67410d5e1c22883eb9318559c30a0ec306c07314f45383e7fdd14731541764cedf87eb6e44f6d0ea8368baf73b4d6a3a1dab1c3e0caacae58aa648b5fc6640050fccea5a63d613f14df4522389f43c467567d7fdbb23d4219bb8961a124cc7c7234da239c92b42"), fromHex("17030300365cb9d6b128bbdcc9713933d625b68100361784fbbab8d05e295e9741d165aad7421c34921c71c8f098564e356c84be3640ab9199b1f4"), fromHex("1703030013ee8f41cbb764653a197aedcf2a275c8d3af40e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-mtls-require-ed25519 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a02000076030302f973efdf34afdd51aa29e7e75af652b6e085370f9e1c3b9a4f547337193ea92040a881b98fbb6a7dedd294543d1e2734a96b016fa739c76df499b3d00c10ec03130300002e00330024001d0020b70ebc8394b626d95d3800012f9713df48b050ed55fe59587320b0dc2e122029002b00020304","140303000101","17030302213e07221033b96c1f1ee36ba218506b73258680d3a48f0ae55bad0aa7ebb5965742f11fab19581ec0cce8b794fc774812bd273d57c2c739e1d6c368aa3750e69b49d92b51ed29232827933004c752ab48209408ae741b3169436411cb09664fc0b386057f4139ae598c49dc6172127765c5fdbd0a4d4ae9911796b5d5b252aa57a5ceea6deee46443bacfc830d86b0dc7ec10cad2a9111a6ef9aa5fa6a57772a90534c95be5186eba146b66cd6018bcf67a5cdc696e80e9b6a0e6a1a8f2d958aba8c7ca60cbc9873db1f0254e189d71bf14cf53223d3d7be4e2586d46e0a81f2afa8b271a6cd11498d1ae7d71714b9e3c3a07b898c7408b81c17bf4deedb26eb206712723836371a91f778690b3110d3775268c4506e5751051c0318650f22f3ff1a2c9d7599c68300605bae79253d6687ac39dace980599753c8d99cf9e688a10e803d1c22ca1edc2a4b3d642cfeb9c7fd0ab3abadd196bcebfbfd4375a8497fd614a58a02eda684105b66c479d16f0002917237399cc8f40a960b9189a247c628a937fd3fb25e5c8b8fbac4dbd2dbc834e7e32fbf87aecb793b925308c18b60c944ba9c5b87320e364a5bc1f672afc8524dfc048c2f515bf76693b4e052e3f0d892d0db19ef92222c0d09b4a1c58e1c9cf3f9e80e31ae33dbe3ba02c2cc018dceef90bc87174fc20e3a41306b904575d5a8432d451947f91846d2f94379bf5c11f42b09aac1ffe66231d2aa623bcc498f8f299ad1f813274e90011ddfab19f571","1703030039d5aa7370e61dd1ab224c96ea17b4612c2f16d4e4a78ecca5a03e7db7267bb6e5548885afe5eb377964b8d6e73d8b6e94c8434f17169854e1c4","17030300136cbc348acc4f180834cf32a3b5820ff50cb2e7"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
reference13-mtls-request-with-a-certificate {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["160303007a020000760303562af925a9c0870cdbd39b8caa07d4efee8562c7bc603357e30a938f6ae0763520e68a0fb43c4efff8a96822230b6f6289e9d1d41f9d5959e3f67704f7c310fe49130300002e00330024001d00203248d2a56abefc8916c2cbe60fa1665640d56efddfdf1fef3d1f19a413668473002b00020304","140303000101","170303021f03480aa078f5b5c5be7345307f0f1ce1e5851c04cc4538e8719211c2b36439f64ba5939b64d6e8b1ac178b67e8e7eec6b0c0b241233a090cfd5edb7e3f74280d2e4d9c42346102887cf61e586ba50a93b8cacd2e8c49c8145dd81d2f554a2ab71d3ca064eb630d358bf90ba81e9e0f7682504dbb5e76f5fc922afccc4473b575f68718fcf4f79ba19e2b40848331fa9c9fe0d40d2c477a1f6075e5f06b22dac176e2653910d22fcd7c035ccbd86876627c01dc0a77ae64efeb36d20390a81b0ee4650752b9ca3cb12515ed9eed0acd89d604d054676622380cec5d5aa5bd1df917442008655f7e8c5620447a62f9df2025a6ce21b91cad46332f23c11980ac860491d482fa9d897e78ee8603b4c7187f87de0a9bb3d2426bcb60de7b6f429b00b0bab11a09944157d5a744dd849e31542019cfa1868a70611423c01ba21a45fe7d3231a91f8080f65a04653e5228d7d55b06c7d61ecdb50efe1a166cd8318c55dafa5ced2f137156197ebb09b6cc91c584f126a2e3331f2ce29466a3f4cebbcb936594b483691f99a94eeffd971eb4b68a82816b60392bec6a0eb8e77aab129e50980a0eee99ee07ded0bd7883436bb9e7b301156c59ad96f98becd4ccf0f2f16d1c822d3092006692860ac5684d99cebf665ee8b9dca5bbe74557e896b995817642748e1ad98cdf66a5f3b15931cbbce3ff6279ddb3bc9bd9fb3030e74f56865bc0541ccc904c4ecfdef2f629797e1380926e90ce4386ee05ed650f938cc3","17030300395768cf1aaad5965650abb51e9583bb746bf55d7a7f6cb9a1539ec05def8ca64750d8814827333021752d3b92338c22210c9f5c89d38edf3a61","17030300139463a7fcca967fdfd63cf79cf12b7fd90144fc"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
reference13-mtls-hello-retry-request {"state":"connected","version":772,"cipher_suite":4867,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c208ee2eb497d72af902d7c9469898e354d0edec1a85263dbef2d17f85b389a10c1130300000c003300020017002b00020304","140303000101","160303009b0200009703039b6e6216469d79e3144b83dae392d5230c4e9c057cb2f4a57b93d292bb224e9f208ee2eb497d72af902d7c9469898e354d0edec1a85263dbef2d17f85b389a10c1130300004f00330045001700410452697e01b357538cbfebc5936df91302ab7612e9b9c27a698dd8a79fd58f71c3853344306a692c721876e86968e9fa3d446afa07b6f4b66e839c8a8f0912db64002b00020304","170303022156daab22afe7f0b06e8c3c9f7ffb83b93a6a9d937be33f2caa02d42fec0bcc1b0676a266ea1e557bd5f981efa96c5971ec1129b78544ea0d78a2055e13ee6489fee2fd7be2e383b19907e1e6ada66caf63c7ff53816effdbd412529b43c0e36b0a6fc056cbfb078f41f58b9892e41e7bfa2af395014a0b2bf6d4c8c30adbd0ab625039b0f4ae57601689fc4723a66d5d8d6fed324586283f863640dc5e67ef8fe615a46214de0d3fb149e9e57a26661420c45afe1141c43d8b5042519afa723f88d3b75ab4b5620280c50a37add3ee73dbbff47ef7a0ad496b4a231bac87c6760d530feeb40891c38435f5954e1bd962076c8b5c0a114053be4ce7a93e8a848d54cccb4c1e991c22e509b926245c0988afaf45b2ea83c89a6f2fc336d86c2f8b2ffeb3a01a152365012e035f793c9691cac453812235a48928d131dddb45b83a3f1dfd77b1f19551ff6d40f3fdcdc9788f40df8a0940e12660764170ea1f1421e9c913098083e2fcdd12cc9c0435265c1ac103021339279a8880cfdee2bf58ab25341f1b5e3f2dd352dc51003cf3b5abd545c491a56b13f5ec5fbadd54945c47b478c0fe14338e02284eec8898c3ba54f907b3e20b8ac55cbfcc8a5dc171328532cfefec2bc07e39a9722598a6c476e56dded0c6b3f6eef159bee1b234d63c18fed659e76a8962b9c7b1b863c991aa9a6f416d1c4ed198cfdb56ab4f6ca69cc99b3a5fbb85d708078fee79c95de505cf3d0ac354ea4b8b25b81e905123a84f5f17","1703030039bc256cf958da93043e5cdec0932b8393b1c3cee3ad3bb4818426ae8fd796691864e71904e6dfa5e05d25978c8c10aa642ffe749c807a8d3670","170303001342780fa6d0d5afbc600c31624bd1a6cd148e5a"],"received_application_data_hex":"474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake-server.reference-hello-refusals -->
<!-- tier: stdlib -->
ClientHellos and first records the server refuses, made with the canra reference client: an RSA key offered only RSA PKCS #1 schemes (never used in TLS 1.3), a key no offered scheme fits, a ClientHello that shares its record with another message, a first message that is not a ClientHello, an empty handshake record, a ClientHello cut short, an alert, a change_cipher_spec whose body is not 1 or that comes after the handshake, a client that offers only TLS 1.2 or an old legacy version, and invalid X25519 and P-256 key shares.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], key: TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("888b3b22f35337083d20ee7e12e75309c7fd5b48d34240300faf8cb415cbac34"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0c86c1d6ff96737cb36f79b2fbd14d3cd7939cd6614e6a9e5d4483129dd1e66f")), secret(23, privateKey: fromHex("b38185b0f6d7dfa3266e6afeff4a8faef94f77f06a9ebd1ac576ecf80dae7cf8"))], pssSalt: fromHex("00313ebfa38ac0e511ad83df3f9f1041861e337f10f8aef80729639d40831396806769d5db5e71290e73c8558402295c89f71f7acae060ef34a153fc17108962"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-an-rsa-key-offered-only-pkcs1", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100da010000d60303d46cdd33916b60f9d8cfdb9852f0cf1e4921b562ae6d5aab767ac5238580905e20e51e8db5ee4e493f8a4d4bd5742f43c004ee4982ac72faffb8b46e13b36e60b40012130313011302cca9cca8c02bc02fc02cc0300100007b00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d000800060401050106010010000b000908687474702f312e31003300260024001d0020bb0005b6ee922af7b1433cf73526df8667cde5b82e9098a48060978ba7c57433002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008")], key: TlsPrivateKey.ed25519(fromHex("c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("ea6d3c9b1fe2fa0f38b306aad1c1b16a12d99d8433b511ce4c68c2400a79507a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("795f28dfde68372612faa9f94eb3439b706e8e5eb6990c7fbeb39cf044088752")), secret(23, privateKey: fromHex("68be18715bb712f4435f5d725b14f188b6b00c09657d62a7ebe88cdce206ab14"))], pssSalt: fromHex("348671292e6ec143c8cab0ebec8543fc37d06c9fe3dad9e08bb2138aad9d2e754e1c3d63efb3450d0c0086cfbc716a597b04ff4c5883e342fff890721207be67"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-a-key-that-fits-no-offered-scheme", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100d8010000d40303d544ba70202f0e6da6a851259deea9aeb4782adf114c2a1a4d60160168ef126920d79022401687b7d8805659aaa1a5a2078dcfa743d18255b0f110bbe9e692d2c90012130313011302cca9cca8c02bc02fc02cc0300100007900000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00060004040308040010000b000908687474702f312e31003300260024001d002069b0836fa7455ab21e420c7441cc59a1a4ee595b07c5f9a9b5299cf98dc6df09002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("ddf5fa276da03d92c82fc74b9c8a700aba37dcf6f42fb7767d846a12956d4f67"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b2c29195aa758da4537d28ca1ef85b7fa29731ddd4331e7599dd68dc8744e3f7")), secret(23, privateKey: fromHex("1368c2c66635ccf41622fc5a0c664f7dc36903b8b76ef903032d6c9039f00a20"))], pssSalt: fromHex("721e489ea392e43d0a453e305ce3b94d43d6cc652fdef01e991025af77c06c9b933968317881cc6bde569421d53b1912adbd6a16a168d9b0791cdce6a6579e0d"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-hello-spans-the-key-change", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("160303010a010000e20303f3ab1166cfcdf24a2ef871fee8e873b0b51ea259def7de26d52c223e14d1aa6320654650a9e3e2a2b6d61af18128a2cdb22366896ac02a1fbfcb19df46ab07ec180012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201409f7f25e28072b4d065cdc96c8f942219cfe950f9698144b688ab0149b756e002b00050403040303140000200000000000000000000000000000000000000000000000000000000000000000")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("968d8a32a92757ea9815ca9917eb0fdf667f11fa48c155a698c5f3ddca381c44"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("f18f28b2a1c360241bd866869b3f4a0bd4d6b20ed8f870d6f74ccfcdc2c4dc50")), secret(23, privateKey: fromHex("e2e858aa8b4908133e30bf9f436850bf800f785326041fd86cd86b176f43898d"))], pssSalt: fromHex("9e792379b73334c76b7c095a8685f580fe39449450e7e473016b83dd0c302e179c7cc08b45b60830d28aa814902a78e5bdfd12dbc9229b3e860824ef6e1c7bac"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-hello-is-not-first", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603030024140000200000000000000000000000000000000000000000000000000000000000000000")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("9563da40cbcf44ef8f00606018f413af151768aa70beec2b035f982f1c4f56a0"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("66406ef82bc810c6b9e0641d4b388c65bf949f09a4135a01bba7cf60cb15e1e3")), secret(23, privateKey: fromHex("a61929c4abd597e9f8940baa50b6b4415720ab372ed3a03cf81a4499b7a286a5"))], pssSalt: fromHex("96381ff7e563172f0e57e87bbb31a11ea058bf684da94ce70d677321f740ae3fa8bb9bd633b35134b9f7bbc3e3442ac9d865644eb01388fedd52c767926f3b20"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-empty-handshake-record", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603030000")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b362c1468e8c08474112326e7dbe0850620d35f0973957467f60c558f44a6c4f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4851ffa87e26f26ee9ec09c2b012dc4ed18044fa7af9aff61d22876f625f5f1e")), secret(23, privateKey: fromHex("3c89389b83d19375ce06ba4dc63b710323d494f21500c65f17abf6dcabe9dcc1"))], pssSalt: fromHex("d3383479ba8078852992d2faf8d4d7cc9758dac436678e07ab9c83f405c00851e92d45e3b0c48c62f48de4513889b1140876966d3526d040c495637d13ed1a2d"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-hello-cut-short", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603030006010000020303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("76c4e69b9a0916d99529d7503f40b8b2ffc982e889e7366c8c75688504ed0622"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("9ceef514291283754d107f4bed042d47ea70eaee6f462a99e3aaf1a747c31237")), secret(23, privateKey: fromHex("48d5bbbcf533c10a3f2937da4d6f1d866083c23cdd41b7a26633dd1e3e221e87"))], pssSalt: fromHex("32b1a6bef29dbee0a6cad06146544c8d7b5263ff283e78d5eef421afbb80a4afc30f65f6bb5edf5312c4ca7862c25cc009ab9f315eb5ffc26a4f33cd1da1ec05"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-alert-before-the-hello", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("15030300020228")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("0221fc3db1dfcc1b3f3ca502469fb7881402e03820b8a307a66d897a98f6a1c9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("ddf71dd186e8430da88f351716a1c081dea3e25f68a1f5212872b29e0031cd38")), secret(23, privateKey: fromHex("0ba8390d56b06669f021b994c492fbae0b3a46f32d7d0e59a8ddb1998f5aca00"))], pssSalt: fromHex("9ccc741963330dccc4c73ea1d9f7b4a22abdbbc53b283a6b5bd7b07b678068f2d26d87199f0bc95f348ea1118ebe1e4ca6c2e108f967d4e901f34c2ea6bc723a"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-change-cipher-spec-value", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303e2f00085c724a1f1d7290044e2391d2c4d970b611a5244973e64f1c6c67f1a4820ee519b5399a0ef44af6ffc9d4a3ae8aa5d7048294a567fa58d3d0eb2ac18ff5a0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00203f156be62ee9b5d7ae1c3aac4238433705b6085d090c013bd9fa6a20e348bd79002b00050403040303"), fromHex("140303000102")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector7'

function vector8()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("fa3851407176470bf4c4bcce485c7c04ef2ad3ada2978c3cb67b11dc27d2dd7e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0592d5f5fe45329c2a23a5340bceae02f19681348c11fea7f6ada75790212f1d")), secret(23, privateKey: fromHex("b06d20c60d71a6ad061b7f4e1b78bd54640470956b9fb548d5c99b6e8af48c9c"))], pssSalt: fromHex("d2b2fc54c42b7c7cd28801969d4f7a3ffce241840950a11adec0de9ee4d600436275bf1959dd0122deadf3aceab79f493a7c17ef1ec993e8461673468457f6fe"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-change-cipher-spec-after-the-handshake", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030351ac54e7f49771463bc2ad32d4c1c0e22383b8fc6cb549be108bdff570d85fb520f5caf1e91e5041b74ebd93de1c33a244ad1fe0bfe2218867872247ec53d57d0b0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020589ec65ac97ea2a30c4a32b74549bd9d3cdfd77a0388492e4c612313d9029619002b00050403040303"), fromHex("140303000101"), fromHex("170303003577a1891732916ff73de96356e1830d39d69feead83a30ae51c4b5f3bbb75a2162e2b0115bd9c1ec0ea4b41fcae1ee3595830933d1f"), fromHex("140303000101")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector8'

function vector9()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("83c718b0dea9fc216173a44868a3f11ccba87ab05c126b155fb022c56dd9d757"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("89150fbfbdb44ca8210013f1994f08cef1c0ee2c02d83e3bbed4f369058a9b83")), secret(23, privateKey: fromHex("a5c1b565e622b889a54e9c2021d2e895baa256de785675d2eb5e8a58a7bfc913"))], pssSalt: fromHex("ee57e58d8e01d63e91102ef82c3a8909a8fa7ec50cbcc5c9a79c21847b5620f20df04feb766b7d0be155d7d6f1896db8fedcd5107fb51427af4bccbede7c41ee"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-offers-only-tls12", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100b3010000af03034b0e77b63ef31f033cfa772a8d2e16617ca745d28be54041658e648f86433a5320980d2dc882c9324eb2421997af16b2451343ce1c4320fb0e873db3ee55fc91fe0012130313011302cca9cca8c02bc02fc02cc0300100005400000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector9'

function vector10()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c0bb6631c5cbe9253c679f730f8b20f08341fd8500a31a7ff426953adc8d59a0"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4bc6a3854014b9c9d2d7ceefd77f11b503857d0b19566e49dc1e6de80ee1f216")), secret(23, privateKey: fromHex("aab57f8b47872787ed1c1cd8ff66a4c5d4e1339b01cbf51d9e5bb990ac7c17b3"))], pssSalt: fromHex("f65cb2049e43cabd1bde627491987b64888c1fce2da25f3fb8bca72202aa22882b75fe600587b1ea173df0e28eef86cf87a1ecf1b51b2b3507f102759f1988da"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-offers-an-old-legacy-version", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100a4010000a003036ded4140005755108e76fdb6bfac52bf7c5ce0d52d36c8aa9f56cb39011f82bd20747f02a409f69ee892cd39c798728652bc16b415fa2111356688e4195df0d2a60012130313011302cca9cca8c02bc02fc02cc0300100004500000010000e00000b6578616d706c652e636f6d000a00060004001d0017000d001400120403080408070503080508060401050106010010000b000908687474702f312e31")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector10'

function vector11()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("855881bb09d823352c6c5706427922808028358752036083483ba513c4d12ef2"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("3c2fe8ab20bd71fb8b8c36f4d44bf2f251ff75be72872134a2d2b8c1705fa1bb")), secret(23, privateKey: fromHex("3914c0dfa7d9e340564061ef2bbf25cb09e70fb806fd456212e7269a192431a5"))], pssSalt: fromHex("12885748d1916bd4fddd65a0ba72ff10748718c226e49393e01639003570c674c28416561c15479239b10c97b4b8f8ef264c8a176ddc0d274670aaecadcb177b"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-x25519-share-length", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e5010000e103032f586f66f46e17435ab08a1533fc657a0237c7fcfabd55e674a629223b99deda20b0529a4ed5d48f87aacf02803ffefa2b92784b8661893dbae96275b69a2d31b90012130313011302cca9cca8c02bc02fc02cc0300100008600000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300250023001d001f00000000000000000000000000000000000000000000000000000000000000002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector11'

function vector12()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("4c7ce4d6d80020029950e2d5ca92352ae1e8578e141d06eb717ad913467ce976"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("f59ae774688444483e03e3ed7b7987f605f53c9b96145669968ca0c707306766")), secret(23, privateKey: fromHex("ccf21711fa7f336ee65aefc4f5473300afc4cadeaf9b3352ec1b7b4309f3041d"))], pssSalt: fromHex("3d9314100b7a9b38ffaabfe9f736c548172dd7aaec748f667deb650ee399eeb16862c14d9f57a4fbc1bc3d5151b512ac4973418b7b132d6228ea5f9e3ee20bc7"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-x25519-all-zero-share", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303679421c21e7a3103dd62f41c6b72505a1a4d0dcb9ff1adb1e0a88d0ef5d1340620ddc5e24a09e65eab7ce2761028aed52f1ee38feffdd9712465ca3a22edf56c470012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00200000000000000000000000000000000000000000000000000000000000000000002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector12'

function vector13()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("6a1fab18314f98e3d334d4c86e07003d74742a9838d1dbd291774d07ca1ea5dc"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fb7711d6f0fdde9db68e036d0cca74278e6f2a2a932bf26c28dc0546ecab76e7")), secret(23, privateKey: fromHex("eaf9b2c1963a6aa3744cbfa7a61a76e716fd24d466eab8fbfeae7fd30d782fff"))], pssSalt: fromHex("96602d62ea675607f9f12dca868ba483a07244a4e69af79aa2115c4122fb9676bae6299947ba4e6688dc547b8020963d07e338c74b0a2739abf0a5a3638ed107"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-p256-share-not-on-the-curve", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("1603010107010001030303ed98e808fb1a2f85b8f9b148794b75e5946133bca31c67dac090e7e935553f6920134eae84324dc182d0722849c4d20fe92355e3d274f6b252ca8634cd89b073300012130313011302cca9cca8c02bc02fc02cc030010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300470045001700410401010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101010101002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector13'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	vector7()
	vector8()
	vector9()
	vector10()
	vector11()
	vector12()
	vector13()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-err-an-rsa-key-offered-only-pkcs1 handshakeFailure
reference13-err-a-key-that-fits-no-offered-scheme handshakeFailure
reference13-err-client-hello-spans-the-key-change unexpectedMessage
reference13-err-client-hello-is-not-first unexpectedMessage
reference13-err-empty-handshake-record unexpectedMessage
reference13-err-client-hello-cut-short decodeError
reference13-err-alert-before-the-hello alertReceived
reference13-err-change-cipher-spec-value unexpectedMessage
reference13-err-change-cipher-spec-after-the-handshake unexpectedMessage
reference13-err-client-offers-only-tls12 protocolVersion
reference13-err-client-offers-an-old-legacy-version protocolVersion
reference13-err-x25519-share-length illegalParameter
reference13-err-x25519-all-zero-share illegalParameter
reference13-err-p256-share-not-on-the-curve illegalParameter
```

<!-- test: tls-handshake-server.reference-second-client-hello-refusals -->
<!-- tier: stdlib -->
A second ClientHello the server refuses after its HelloRetryRequest: one without the suite it chose, without TLS 1.3 in supported_versions, with two key shares or none, and one that is not a ClientHello.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("0075ddb6428e3798768dc9b67e4d21a0fb9b2708e56436ce95eb111c47016062"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("987e5ab5cd26a1ad88425496322b6ae6b124a9af3f9b129f6c25dab8490f38ab")), secret(23, privateKey: fromHex("c14a906a864706dae9806efa10fa0b35cb06effe57bec5f45750d7e627ddda69"))], pssSalt: fromHex("4552e743dd274d1231bea9f738d40ae9938d7cd91b7d43925d03b424c56097b22740347325d76388d59ad1ab6bfb7a6f1dd8cdffec8b75c9e637db2babcba777"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-second-client-hello-suite", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030352cf5ba5156b505355e675ad7986e5f80023c0a073a6289af49091b8cc08205d2082a8984eddeed5c77285bbd93c5a495cbbf1559f0a68a4382e9a1d45012382f40012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002022a1381af7fdec729c82383e32628def3fb08d372845a32b566601555d517616002b00050403040303"), fromHex("140303000101"), fromHex("16030300f7010000f3030352cf5ba5156b505355e675ad7986e5f80023c0a073a6289af49091b8cc08205d2082a8984eddeed5c77285bbd93c5a495cbbf1559f0a68a4382e9a1d45012382f400021301010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e3100330047004500170041046f2273840e31f0a506136c82091a73eec94e666edab11b26c6566c1330b508f626bd742cc3763532c95650bb4d76d87b91568065b89cb950674a8a95a90cc0dc002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("026f5d138cb5974335f0066e04220a55cc03676284cc6d0bdbbc22a5b78588da"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b85ea1a9b59594db556453932757e11668a72c21152943dc82e58b9598a4c3e4")), secret(23, privateKey: fromHex("871db7331128c349e445fc575f615fc28c98cc55387c92ba600cc200821441ce"))], pssSalt: fromHex("69f2d38d8f5309b076996b595850ed801398aaaf3569e33c3313d4cab1cbd443c7dea50c4f7557c427328507a67e35f8505078c81e072664c2954b8e07503b30"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-second-client-hello-version", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303406b7427e60ecd022eb4025ab4049049014d8d2e8993ed9609fa9a6ea5b34f3420ce36c638086c2e56d22e4641516e5004677b6bd219b2b7b2c6e07221f20479740012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00208ead87378c6793aa0a8b1e3dd139854afda48e9dbed0020b1d542e7abb8c6221002b00050403040303"), fromHex("140303000101"), fromHex("1603030105010001010303406b7427e60ecd022eb4025ab4049049014d8d2e8993ed9609fa9a6ea5b34f3420ce36c638086c2e56d22e4641516e5004677b6bd219b2b7b2c6e07221f20479740012130313011302cca9cca8c02bc02fc02cc030010000a600000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104955a5873b3183f9d65f1cce50051fcafec2b3b1def26536af4051704ea14d726bb2c8a7c20d06fe19a826bc7b67a3c248611e8640d9d667f5631bfa8c4237cd8002b0003020303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("50b64bfd19a09d66ff35b58fbc1429e46a40f78e50a07882e5d9d1ccf4c8cdb5"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8c5f312cbb6e16ad2929ec1f62c2c547a8f21c5c4f0a718c64b77beb01218d42")), secret(23, privateKey: fromHex("8f6ea6964257095a014bac7ec321bea49ca4cb4efad3cdca9ac8b03ff9ce0090"))], pssSalt: fromHex("f43f65f8634d59faaa24c0a5a0ddee2a1ff28cfb7d7d1e1869b5132cc12d21bf1ee8c0a3060a465c8bf933a82e7a048bb1060a9c2c504c33abd46574bc4ad47f"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-second-client-hello-two-shares", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303112e2bcb7bbc086ea69ba9771c50f910248f6107878239f56379bedfaaa5e6ba20def736ca17b36d7b7c9b3e9e9e76c5b9716ee7d50c44b8d39e8e7facb73390770012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ac6a0522fc6055ea462eb5c9e297a18570ccf88de45c5c1f1780fb81493ce915002b00050403040303"), fromHex("140303000101"), fromHex("160303012b010001270303112e2bcb7bbc086ea69ba9771c50f910248f6107878239f56379bedfaaa5e6ba20def736ca17b36d7b7c9b3e9e9e76c5b9716ee7d50c44b8d39e8e7facb73390770012130313011302cca9cca8c02bc02fc02cc030010000cc00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033006b006900170041043a2233cdba34f4aacce76e3cbb4990bc5a90d2ba261657716456a0a8126fb59de50d575474ad4c6e877c873f3063279d470defd43695be4d8ac75505d3814a21001d0020892611aff893f2a27bb25bff8cf2cddac96cbc23651c7353311cdf632131e012002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("0a17727dc7808baafe8a991d4be67c0286096e215c8eed1fd964c803a883a82b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("cc8668e6da05b731ae18b8581f5cb80b1a4f2f03bdc61c95fa15afcf607f53c8")), secret(23, privateKey: fromHex("6f3552530c39a51a52aa957d4975b351f9386a2ec17805438d368c958ac19d03"))], pssSalt: fromHex("f1c2242cad66450b683fce99f7ef1f4c3d5a419fdb11f69dda2f94abe64b2fb43064a54de7a8d1f00d7a696836c956274e0d85ff0e292e81d5f04930485a23fd"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-second-client-hello-without-key-share", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303b4af182d82ec683bbaa271ee7a8f1180bf1cdae5fa530243f32d95a15e01bbdf20ba4a9ae129fc847041a82982cc1e52220622c8efbcf8205db01b6944cb18cc070012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020a964c03b743bfe5384f7b311e1eb360566ff64ed879e5a1ef329e5d1a0c6bf54002b00050403040303"), fromHex("140303000101"), fromHex("16030300bc010000b80303b4af182d82ec683bbaa271ee7a8f1180bf1cdae5fa530243f32d95a15e01bbdf20ba4a9ae129fc847041a82982cc1e52220622c8efbcf8205db01b6944cb18cc070012130313011302cca9cca8c02bc02fc02cc0300100005d00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31002b00050403040303")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("d89893037fb67ac0da4b222256807ceac9f15aa82d8939aa40c08c975a924c1b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("51d74dec02deb2d3cdb7743684ed5f4e5c2fbe50c99c184e47c7774309e6dfe1")), secret(23, privateKey: fromHex("ff59b8543d90431dec5e7b77256fd572aa58a02d8c9ea126272a6962fa96b7b0"))], pssSalt: fromHex("e15f7a091f8b7d2acff1705e0a475cd0765bac14cca1e1d98a746bdc45077624c66c57d117e2978a0e1431aba6f3ba77575a536bb5f06356fb202cc95e41f465"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-second-client-hello-is-not-a-client-hello", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303caf0fdb58c824d2868adcfd7b7efc39ccb58e37885803d5b6a1f08080419ff7120c099c6abafa4f6dfbf41e04e950a0d4063332ae4f3c8666bd34c7f5d1efb27d90012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002054d80639a86dbdc416918e82e9e72178c46454365edb4f866257dec3afbe4149002b00050403040303"), fromHex("140303000101"), fromHex("1603030024140000200000000000000000000000000000000000000000000000000000000000000000")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-err-second-client-hello-suite illegalParameter
reference13-err-second-client-hello-version illegalParameter
reference13-err-second-client-hello-two-shares illegalParameter
reference13-err-second-client-hello-without-key-share illegalParameter
reference13-err-second-client-hello-is-not-a-client-hello unexpectedMessage
```

<!-- test: tls-handshake-server.reference-handshake-refusals -->
<!-- tier: stdlib -->
Records the server refuses between its flight and the client's Finished, made with the canra reference client: a Finished of the wrong length or value, one that shares its record with a KeyUpdate, application data, a KeyUpdate, close_notify and fatal alerts before the Finished.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("6289f60e0f7abf0e8cda3fb63df9270e0aa02ea39fea3d09ec77cb444f0d98dd"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("c5d25777b89c90186dd1d8b4ee77416352574a465ee3c602e0d62e66d4281dcb")), secret(23, privateKey: fromHex("e77964bc1a3db3a644773750fea834c13129e55455f05cb1cc782fef0d96dc50"))], pssSalt: fromHex("236775649241c711cfcb638cda345f803f8bb15cbe66eeeff20503a4ce2fdf6b4e371924916f19bed43ea3c1d8f6acc3d812334f03031f4ff5752f4be9eb4d62"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-finished-length", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030344421307ff39a1bdea252132d2b118ba9d81e2b4c45ef6d7de1d2579f9ab333120ce6a36455cfab0a9d1e0243d7e87e8127d682892a6773b92e5a3a473289d94620012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201a6a3cac8c8e92301ee809dd50410d5f977b6671a3a0a4bb6d38660b0ff8923d002b00050403040303"), fromHex("140303000101"), fromHex("1703030034a15d3026faa656a0116800a9254c965f8c6b66039f378545c9501ce1666979f0bbd74b232f3fdb6032a54b75d321df0e85bd0ac9")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c5eb23e17c6551de045abb9b23f564e63b92e5a2000bc31dcdfbd8bd2beadf2c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("41de265ae169e9d500003db4fd841dbd1bd48c02d9539d2ce592c228e38f92aa")), secret(23, privateKey: fromHex("fe53fce4b72a7ea0b28c81cfd0b97a1d786f7a319ed13ab40317911a6a1e2aa1"))], pssSalt: fromHex("cbab219bcc72a7f51ec4f3dca72d004cf1f405b1620f4372770bb8d5b986bcd5cb8a25351a765e671ac551347cc56817d347c016aefb238641bdb27d8fa8fd81"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-finished-forged-over-the-whole-value", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303e0184a43e952c04cad69e38f6739abdef2b813ded97e50bf61334d08f84289c7200ae8a66515c50ca5c7c33bfd855b5848d2d855b14ea3194b3f936181c35122480012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002046637829984820c9dd8ccab42d3a0006080bfc9fccf82eb4a7cb2ce7276be757002b00050403040303"), fromHex("140303000101"), fromHex("17030300359625c3930e73406acfb0a22226289e7090d7748ebeba0bb852e6ce065e2f1c0973ffb0d80e22675327978eab57b8f555b2df55a68e")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("a40b03cc4708fcad4e44da286ae7eb7e3b63f49e8c008e7a0301ab540fd82e8a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("59a460e4a935bfe28ba429b63dd31b6b22bda122ae3278567127a714990d13ef")), secret(23, privateKey: fromHex("e1d1ffd1628ccbdee96119b54ffe58e17703dfdf37604a929fc27a68d9a1cb25"))], pssSalt: fromHex("63ac37e0fe3e24db82b82ec0331afc85fbbac06b73b7a33d10b0ebfd5e0bc66632b91dff8a8f86e78b6f7d8807128845712457a9a02c4e34e94e391af48bb93c"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-finished-does-not-end-its-record", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203033707f4256baef3850835c2f3cf82aa02a185f0c32b9ca451ef2656b9c9aa835b208b4a5a5bae1e1d95aa957be9fa14d2789beed1d29a56da41bf1296f6aa5df0db0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002045b9996750b0e65655ab3687096b4afc9ff26a8b0d40423e3412dab93b9bfa10002b00050403040303"), fromHex("140303000101"), fromHex("170303003a6e8b97273d0fb2bd2f85e3c66470ef11e74a0c8130a5a6611e7be998fddf7fd78664376b401cd04e68ad163a5311148c6de13d00db9e0594f870")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("64667ec1e3249f17a50b739d47c51254916754a43828c3e48633f9b7d86030a4"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("984f7de6f00b0dc195cb257bfcfa39510d842324d582b48f92b6ec5300d2bcab")), secret(23, privateKey: fromHex("42c3c3bdcb1196addc2fab5384373f5ffc9f403716f414389276aaccb494522a"))], pssSalt: fromHex("fd5336c52f12ad1393d96d3a509041751709de1bec10c77f73b6dcc5720ac0d843e8ec12c71fcdc9c39790e03067d674259cf359f548bcac4a9e74c55cee88d4"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-application-data-before-the-finished", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203037108e5e53bcd2469e8f2e012b83df75ae71d3edf31edd41fb876640b171ca5e220d462c74186919b3fd347b84c7b4df73d7fa817b363c356f5742f8c9203d7ca7b0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020a61f90e63e88b3ce49453f2614d2d6e35bbe3a995fc1eb0900677aebdccb412b002b00050403040303"), fromHex("140303000101"), fromHex("170303001698b1712a64d4278b782de9b0db2237029969980e9adf")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("33a145af22459df341f18030d260d0f0150a49a81015e873c110b55150722bfa"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("893ec0eb95dcd53dfd532bf1ad311b5d84b2049c61958919a19bc539d1ded446")), secret(23, privateKey: fromHex("8eef7abe235c6f5b228b0674eb94cf0801411c3434a513afdddb7241c9a59d7c"))], pssSalt: fromHex("306828d92e72babf7c78c588491772429190df3c41cbf381d0c254841365b6b7a1a1525d2d27cbfe29a00389a0ca2b0a446e1d2d1f9012366b1d278d2004cc93"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-key-update-before-the-finished", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203030eadb6c9c63b45b4985c5efc4dcd004a36a2fff60c20214ee76a5e10d97a5fbe204ece87c4e1c11e56c706a6237a898df61cfdf53a250450283b9f13327c0a342e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202e9dc98f92f0c9e2372a2a7fc3426be37d6fc96183abcd4e02caf8b0792d3611002b00050403040303"), fromHex("140303000101"), fromHex("1703030016ed518d67aa4ade1d06afc5075ea784d494ec9ffa6181")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("70acabe62105ec6a1c25ecbfd3a1b23bf60a703e7f72b2ee3732fa2bd809bd01"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("693ff020f89863a36a53baa4d4ec36c455ab7e055696256bca8060091cd04193")), secret(23, privateKey: fromHex("1ab603bfaed91f7504162fba95a4f5e2c6ea25ca4c4d2a53a2fe5ab277fff8a4"))], pssSalt: fromHex("25f74d6bfd8b16433f382c64434723254cbd9d4f28c3b1d6143a27dea447b765145f29602a5e05ae0f1ec9a80d8f650db3e5ef4bca2370882daab285c71c2e19"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-close-notify-before-the-handshake-completes", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c6e23c2f7873139789216da7f11b6c1cc699d7da68c8aa3839ef03a0436623812021692cfb0fea7788a705be8659459829e8d1bc69cf92c466bbc69eb3bd8bb3700012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020522a3c06281b515c9f38197cb60714a270d36be4a388407ea07d5688b62d4152002b00050403040303"), fromHex("140303000101"), fromHex("17030300130a8f1546091016d8091dc65acca02d2e7e1cbd")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("79d982c76b2ed3fff05f5b22fef473f353f4e6a288f493925a3d6ea1aab3537a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("91f9697ddee7a92edf057066ea5663548887835965d1bdc7ea2b5acdea429c29")), secret(23, privateKey: fromHex("52ef4b75a32e5e366b563b517b2677d37e3ab6abdf2fd35186a2a808ef18e8f1"))], pssSalt: fromHex("fb83397aa6e526ff6098bc74fe9a320bcee644ac77e808f50d0c26e8333ea3649d550ac7513d90a910215e192d10d4f5f4864980ec6b6af8498a645a16b46a8d"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-fatal-alert-during-the-handshake", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303a40c18ed3e30b4cc60d07658bc222339653eb179d9375161cc3e671cb569dc9c209304b13e0ce979607d9613f2df65f05b871e3abdca719a07d7749cdbb4c35d900012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002028bc8b5c0b11f610b8df47e02b391a01c4d4846ac9899e6c01f98c01a4171d60002b00050403040303"), fromHex("140303000101"), fromHex("1703030013caa1f7252eb254cf62487d9fd91e0abede42e7")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("f02459260a5b20847faa1c00058699b3b3de7fabffd478048ee60364268a8928"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b9728889d7fcbca219315673f99798ef9e63f2351b31f5ddea5cea0068f5f328")), secret(23, privateKey: fromHex("fbd1eaa25e20a00833abb8cc8f504b81cea39f43abea0c821cfa4fbea1db71d8"))], pssSalt: fromHex("c77b8c894600897b351fd72842a03e35f78fc81bf2948f1ae9ecbf7e81e30be857fc9736fb37c68e512ea891432e0b3e6e333566c8f18f3086ae87b502e44cdf"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-alert-of-three-bytes", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303e96ee831b3d485cac92480950123196642ba3df35ccc6a0fd5a4d14108222d8a20e0708348cc84609552441f76e90eaaa9b401f67185bac6267b49e5df814cd91a0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002065f5f49666cc72d95a49384b2db35686bc2f6c9db756aecdea1a3a57e802165d002b00050403040303"), fromHex("140303000101"), fromHex("17030300147e817879acfabd82ce856f43ae3bd36474357a75")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector7'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	vector7()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-err-client-finished-length decodeError
reference13-err-client-finished-forged-over-the-whole-value decryptError
reference13-err-finished-does-not-end-its-record unexpectedMessage
reference13-err-application-data-before-the-finished unexpectedMessage
reference13-err-key-update-before-the-finished unexpectedMessage
reference13-err-close-notify-before-the-handshake-completes alertReceived
reference13-err-fatal-alert-during-the-handshake alertReceived
reference13-err-alert-of-three-bytes decodeError
```

<!-- test: tls-handshake-server.reference-post-handshake-refusals -->
<!-- tier: stdlib -->
Records the server refuses after the handshake, made with the canra reference client: a NewSessionTicket (a client never sends one), a KeyUpdate with a value above 1, a KeyUpdate that shares its record with another message, and a fatal alert.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("888880eac0a88ac306bd29683ef11e3bbcc8b1aa703ff6a9a4ba234e8b38280f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("267ed425db9392a5eb54df82d4976ddf6cb6dee0b7a3f368cafb6829a72026b7")), secret(23, privateKey: fromHex("c1f3e74f13213f9e9ac6831201814a38517bab3dd62528dfcd89638a3b8c7700"))], pssSalt: fromHex("ecad86b1f6a783aaa2bddc7587cc988a8fb113ef09c9a57c9131c2847b8ff9ad43590f6ff3d5acf7da374894184f82db7688121ae9f240a35fcc255a4cf7bd0e"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-new-session-ticket-from-the-client", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303a42ea8dc083abc65edf656446cce14087b7754822fbfac11aba8f96053013d3820ffeb5baa4340903bf007a1cb73a56a039b6afaccb72a43c8a08b66b61871d2820012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202ed32f096e8160e82871d532d04750891bf9c013e618a90cd1213a7e1c99f853002b00050403040303"), fromHex("140303000101"), fromHex("17030300355f01a31c2a1c03fd8fe59d741512af45593620c154623845d2e389589142a7ecb7c55134ba29294a957826d740160c009ab747b2f9"), fromHex("170303002300594fec3ceed06b2597bf4731c7b5d1f50c059b22d2f3be8403874840b77a13fa9d73")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("741633892431ffe2dac6ff85b9858f7d4380f11862edb18148c5c74eff12b321"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("98e2ad862bfad288cb8e5254ae78b3ba459df18eb273a74516185bf0d41ba5a0")), secret(23, privateKey: fromHex("be49d67ca23e87da469b7f466c998019c64d040d231ec510e7c544bcb761e1c8"))], pssSalt: fromHex("48a9e8d3928061812c3009fa3ad27fa78f19e278a83548a3b7d0f73d2f77ea454407261538563190b8311094c4648a681d5109fdd4af8784b26e1ef6db503c46"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-key-update-illegal-value", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303696867015269fe44f9b95f0d4d9091c0d2fd656c322b18245c5b4383534938cf20c831179704a5fe82f3272318b57469a365d8ee9df94ad75f6e650c3e8e854cd10012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ac2d564ffef769d74d5865c5e4591cdf4bd522a9f473a50c7f82bbf7bbe1a609002b00050403040303"), fromHex("140303000101"), fromHex("170303003500f4b1930041d0a96f668ec94231d0df3513b309d94c2824c8c6d205c22e83609ecca8c200278cd986d03c498c9a4ab19f05e107f8"), fromHex("1703030016e3366e11fb21b284ee903c52210bf5f448ac1f5971a9")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("f504ce9e625c89e22aba782e139c27a31ba7de16547c60d94a15806a0900d56c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("bb359e09fc275668bd47ac285d7d3ec4ccf71256a953900654dd6cac29fd73b8")), secret(23, privateKey: fromHex("5854a125cb09887fff248e29ab41f693584a19209c973d10d7385132ac8d63db"))], pssSalt: fromHex("4eb20de83be240c8760799c0881761b19acf08a7b613007dc67ea296a9db9c4602de77bee72192dd04b7d83b76a74bc6dcdc9797b044ba03e1540d85fb129ce4"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-key-update-does-not-end-its-record", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030322a842b62a28e23d42d83a52784637d061605efb56b264450d9e71f2842bf82d2074b59df5d69c739e3850535506d03efc98ab90915c970c25c11a6f65fde4ab8b0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020fd37718837d44109326f8e0e65c638437bca2d921adced946202b66995788c6b002b00050403040303"), fromHex("140303000101"), fromHex("17030300355f65e1726c8fa9e6c70dc42dc89e8a33d1f6b1c182edbd6c55bc4e168d65a240dfb1454cd984cb7e7c5e4c3ee2c355e4b063b15267"), fromHex("170303001b4405dc0f56d5f0c24a0c2d90a5081896ca2822495bda68f4e485c5")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("ac80f95d9894738245739088a13d61e78c99e4ec6d16065b34020f7851d0c336"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("1b418338b1825f81da9afc813fa9cd2973e7e4b4df89642b3c2d42d78899a5a7")), secret(23, privateKey: fromHex("59ba4406a34535c96582ab02a3f64307af8f37da04ed961a5d467868a43dbd12"))], pssSalt: fromHex("f5014846c40b2facd186c2330c08277407f27196304ff6c01b7b09bd261ca80da7e9a2d8562e6291c8d065c3de2ee2543eaec7d9f647b85dce26b2e418f8ff96"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-fatal-alert-after-the-handshake", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203038c87811dc64e4e02f54669ef4034c0b3b9ded53b37fe46c9d4ff6e619637f9da202cfc9a62d6efdfed1f9cdec726ce5b5f24c72f4790805c203be19dfe00c330ed0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f196a92fea06b94d336ddf695dfce60b50fa26bac76c297c770b75ed1123ad7e002b00050403040303"), fromHex("140303000101"), fromHex("17030300352dcd6829d1e77fa6f8646d97ef692f2bfea0ea99a96125fde40b3a9f0d73e799b17e648bc08366a882b4f62f2749541581ffc6e0e4"), fromHex("1703030013ab9cb9f15ebde5a9245874a6e70306ad0b2747")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-err-new-session-ticket-from-the-client unexpectedMessage
reference13-err-client-key-update-illegal-value illegalParameter
reference13-err-client-key-update-does-not-end-its-record unexpectedMessage
reference13-err-fatal-alert-after-the-handshake alertReceived
```

<!-- test: tls-handshake-server.reference-client-authentication-refusals -->
<!-- tier: stdlib -->
Client authentication the server refuses, made with the canra reference client: a Certificate with a non-empty context or an entry extension, a Finished or a CertificateVerify where the Certificate belongs, a CertificateVerify after an empty Certificate, a Certificate that is not a certificate, a CertificateVerify whose scheme the key does not fit, and a chain from an unknown CA whatever certificate_authorities the request lists.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push("\"{toHex(record)}\"")
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "null"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "null"
		negotiated(protocol) gives "\"{toHex(protocol)}\""
	end 'alpn'
end 'renderAlpn'

function renderTrace(trace TlsReplayTrace) returns String
	let state = "connected" if trace.connected else "handshaking"

	return "\{\"state\":\"{state}\",\"version\":{renderVersion(trace.negotiation)},\"cipher_suite\":{renderSuite(trace.negotiation)},\"group\":{renderGroup(trace.negotiation)},\"alpn_protocol_hex\":{renderAlpn(trace.alpn)},\"outbound_records_hex\":{renderRecords(trace.outbound)},\"received_application_data_hex\":\"{toHex(trace.received)}\",\"peer_closed\":{trace.peerClosed},\"peer_authenticated\":{trace.peerAuthenticated}\}"
end 'renderTrace'

function report(id String, config TlsServerConfig, entropy TlsServerEntropy, peer TlsPeerAuthenticator, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsServerReplay.run(config, entropy: entropy, peer: peer, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("7c830aba8759789d77c153e30b79a7a87b00ca2961e6dd4a4e5ca494fc1f600c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("84cb02b522c8fd4471880c04f98f6648e6a8979ff5675e76288ba95a5bddbcd2")), secret(23, privateKey: fromHex("1563e028c0d05b9b6abbb51a09304ef45d3e460b6695de3ac6d16c1654c3276c"))], pssSalt: fromHex("b4b101c9c54eb294396d71e42ba1aba3898dbaeb7df842d37bc56994242a5c84d6cf8b7a4a12c8ee355576f311387589c3f623c0924ad7ecde9faf61eb1d55b9"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-certificate-context", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c8bdeee039e4d8f1c3ec25572b99f47bdb11844f631d9fc8cfd2836e59dde8e820dd3cb2591400c907df41700a52fb9ca2f827a762bdaa063209137223917066dd0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201f7c3f91661c671236eb1e0d1807936ec01b485ceba4c2b58d34f05166f2b43a002b00050403040303"), fromHex("140303000101"), fromHex("170303001a379e6b299adfb37b33b773d4ee624703260b29f5b995fdf9c7e7")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("d90fc11b21d9858b302f73391d9e2b4f8b0230d19e6911948639adc1116e39ba"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("db58d5ba09c66bdc33a30d4aa0b2f864f8e23d7afdc7ed2af8aae5bbe979b183")), secret(23, privateKey: fromHex("d7201df551b83c97cc419f32054eeaaa4e663f4362e552c090b9ded32ac5d6fc"))], pssSalt: fromHex("b3038d8cf0a0a87f02935a87aee1875a9b18b65b6cc77407718434d6b76e50162270ed199439ef3188596bfc213746c27a968cb5956745b22093dea5faa7074e"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-certificate-entry-extension", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203035ffabf5668eb93ae4cc07d7e20de9e2ab9a267697661ae734a0f5ddc9400d8f6208f57ab8552edec8078bcae25170a00a3358c826066160b23aeafc41c219640a20012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209b1cc5819f391c8f014383158c0e5d486a8b1b8cb41737f7146ec5ca1718e764002b00050403040303"), fromHex("140303000101"), fromHex("1703030193bcefc51f85cdad332a5a5dd934ef13f82515a40b8a7cb12568450af32cdaf1514a910fa54d0ab7b26a5a553d8de7fab5b513b0e0936fa515de734241961eb0e9385ce0a9c7e82f041060cebb59a002f360f7d4d0c263b90e66bf3f1d3281eb3a262b002d2014fa59e0fceedfdd5e9870e9330dfe9df739d67eb1afe75cf8fab7e1ab1814afc4f346522f8d565460989aac017eb9008fbb5f8f07dfd796f51e6be6287a542f5fe00b2514c6eeb2cd302924a8550cf7e23c053a5c7c764847e2499383e0380191857f5c444038424bce6d00d80f1b928f66ec548e33aa7362a4c12eafa1e34ed3a5daf764aa60d0dfa427e599561f0378b48fd92c60db5a993ea88cce0c5bdf11d9ea38f0fbfe0e1a097d830d2c5cfcbc3b0610eaf47534336e6ab46b000818e6f0ac2b835138c17096c1ec2f38f57fce95dcc050c0eeae03a0a6e9bee0b032c6fd94183bbbef00dfed459d7bb6dc6e864cf004c79d6017c796f207a89f2c532fca27e0ce4042adc3d5b5be046101dc340c09550daa6dd3bb28cc1d3e76746f5407b37220c05dec89560c3bf48c")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("3919dcfa87024da9a7079288103d4bee600e279cc0c96cc33b03c9dd3859c688"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("20e0aa5ae94194e6d6a417298680453c53a79c5e78fcc41e890b66214bff93be")), secret(23, privateKey: fromHex("8a6f24fc56ce3fcb7dd940325b55fc8741ce6d56f4655cfd67615f714af702e5"))], pssSalt: fromHex("8f488c2904ebe72c33564d203cf97592370c8d9a2e2ff207c63825246efe6a316f2b1d727d04cfd49976ab8e8c94ab05e0eea0621e7c3b667541a7f8fc5bec55"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-finished-instead-of-the-client-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303eb15189f7bddc2692b61e5f476b790134c46791a88b2445a9733b6fadfa0accf205cf2601d847655d9737d5667823fd68471e4b60bbd7430182b494e29824851b00012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020d5c8b229a146c56bcc4301b8ce0096fbe90e073903a1134937085884862c1a14002b00050403040303"), fromHex("140303000101"), fromHex("1703030035ac7521a34c13d08d29d86aaea8467be538bcbf2bfab2fb899fb4afcc6b2988368b68d334c190468fd498ceb56069b1d3d81499f76d")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("0cf54a05ffa50b50ad0668cc00880249c9e94cb15188e2b18ee6086b140b7509"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0cbfc316f60b77da37a28126779c65f9b7e829dc51182f9afe54d29a9ad4d2b9")), secret(23, privateKey: fromHex("c1f8e6f9d266512cab874d676c0f683a3f9020a5675ff005ee8dde3139d12605"))], pssSalt: fromHex("ed21c83d5fe8b274b49021411f54b97b54edd30cf4d24d5bec94df0c1f3657231a9e978026aa3b7ef7351f63755be63c7af98e95d4f9ab5cfae1c3b711e1b22b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-certificate-verify-without-a-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e203033d7ff57e2e80ea4032bb2bea1a953fc7cbb2c23e0a0dcf656005ffd33874283120200ad7dae5214d7a1f4ce6605f728d47ebf7376110a6fab4ba53a3a361a48dd40012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209457fcd7f006234d7674645f48d7446ac2bb7121d44fe0874c2d847e6f6ff67b002b00050403040303"), fromHex("140303000101"), fromHex("170303001a5c40cd6ae62f4fe675dc0cc4042ddf39fb7bc69694e8e69a43b5")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.request, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("f06c6721dd8a50f866165f970d5bb47de05d4c8ca6f327e10de15a05d0835b7a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7b4a0e326b42a9ffd1d8ab8d16b0f708aa946b13d2cce9e4821c6ef124d8551f")), secret(23, privateKey: fromHex("e6722900f02850becfd0a59159f9b244877b2ceeba06122debc8cf1480836af7"))], pssSalt: fromHex("29020a90ee8f4004618ec9b6b60ccac365d3dece43a0edd373838653b7c098a3381c2f8a6b4b3d0ea2ad61a7481c713fbbb920316f4c4003d90fea10381af752"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-certificate-verify-after-an-empty-client-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303c9021c14cc7fbf1b62ae3ba51b6810c40f81aaba5a12e70082e6a3df992f43cd20ea394e50fb09a7224c9f2c7e2134ac00d15e6b7ab109cd55e520c4b290ba772a0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209c17ee4878a26ce29f07bd910a2c40244e24f63096da38e3136b8e1881f45b18002b00050403040303"), fromHex("140303000101"), fromHex("1703030022679631a0f61851749189ff3b062afc92c221a2f6c1119178676e2a214890b0c0e633")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("e0f1c5dd67de64c65eee18af0c460b94fa9eacabce3a0204354595e664d9030f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("69663a15111f6be87017860fa1d19effbef97bfea84bcac37b44b828ddf6ad97")), secret(23, privateKey: fromHex("518a57073fefc13097fc9fb75cddf0e6c76a6598e75c0c1015633166af7d9095"))], pssSalt: fromHex("a3ec6d617e322afbab8583516caba8e5d799fe19fec8d4b285c4f527d056bb42c87895357af2d997bb3697331fde8d55dc4f4f277cef7f8596da6ef7b4aed018"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-certificate-that-is-not-a-certificate", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e2030359f876b94a34343c1504baa762545a7be9e4c2de9afc7d5730290fc48ff7746e2040f95189f43527c1dacbaa804c899a9bf4fa9958e43f91741c9a3bb0fb6b147e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209f4065e5e41f47bf83d36efb260093e96ab20af9fa38d859cb92feeadb7c193a002b00050403040303"), fromHex("140303000101"), fromHex("17030300204b2541d5c6f51c02e5eb0594114ca81b0c05c404f7122973bb527770ebd9cfbb")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("c9ae3f298e3a80cdf2955f9bc89a8ec4cc9ab1734c962da0a830bdac25d6b882"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b5bc727ef10f3a69a82ea7ee952878446d5b09a20c3c5478bb185cde6624eda7")), secret(23, privateKey: fromHex("8b015d808469511c01d21f20df0c9db4c348c0304238ae13e826ac623ca12022"))], pssSalt: fromHex("d5f9547f0dcc318753b42cba70a565b3ccea2b6a1ca2ec620c5f328799fb41eb60a083e10d0ab33aee7f56a3982b1d7690f1363af3207765db9397409cf173c3"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-client-certificate-verify-scheme-does-not-fit-the-key", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303acaa4611b18df31f92c151c6bbe2a2a717bb9d10f36824f50b854de12013bad420443989ed7c27e2b5ff936091f819990eef2d4673529dbf4d8ab47804b555d8f60012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00205ced3061ca47f7c259ca6aa1a2560601bfd5f5603a004e462a81f5c566e5bb30002b00050403040303"), fromHex("140303000101"), fromHex("1703030200c748bf5e800127a568d665162393334dfcb2d9ad0807159ff02b47529ce0423b3a2ad9e54fd943a48107364506f962b4b6b8fd3f0f343f2ba516d3b981751333ef51cdbeebcb37432368684e33647f4825d2b3bb6aa7aa2771e0103e7c654ce34d284fccd6230ab7d2c13fd2a80e36a2bbe24dba7a64285a01d293047208464e7b0723db2c6c1c9c57ecb4698d57051b364c3bbf7a4424bcd211d6a755d11f3dcecf151d91f3353b6dfab90c9fd757b586b3272c167cd757a8e7216415331c2cad4e10de77512ea25e98b925e399a5904597e17fc100987c2dd31cb3f282647846604b98a8e18ba6ebb381b8529263f9dc5ce218d64f48c275f2e44229d1b0b7b258a44fc04c0ca5dc60ded787b41d139f396cbe188abe10f27f44966eb2b81898cba700a00991bb7dc26931a90dda759fa3b20b06b953b443f9e239d627d402ff953a0deb85b672c94d1dc93cd9072688e0a423356e592e1df934df38094bca42d562dfa22b33a639ea894501e0ca54364133e505ffc44e216d2f988df1085939bf5069218ee3bf2980f6f8554a0d3b28074037d74e7af07149a80a9eed9443cc13db25722586eedf1b61303023c1e6fad0964b352a44e64224d6f497ddafbef3879cf41c6b60e51837600e127493cfd9399cffe8a2498c0fff4fd1632c73be5bdf77392d188c756b4e2d6690181f44f3826c1aeba474cd93556d3e5b358112")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.require, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")])
	let entropy = TlsServerEntropy.create(fromHex("745f392bb0742776c576731ca285b80a8d3838ffa11f26733b04bca99c1b801b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("9e7f5a2f6b1ab3fe22a5649e23926f71e7a86fe1dead287b0909eec8ff693685")), secret(23, privateKey: fromHex("264ef95610d339db4c4b9a9eab8e430a5f263b91afe3525ba53abf84d8815929"))], pssSalt: fromHex("8bb7d60bc8fa8a680c4141e677fa3684936417ae22545d899396c5031b289925c82eec7fdf8f635704fc6b30e15e5e6c26b2b76abf4398cf6436d82ee01ca0db"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.clientAuth)
	report("reference13-err-certificate-authorities-do-not-change-the-verdict", config: config, entropy: entropy, peer: peer, inbound: ByteArrays from [fromHex("16030100e6010000e20303272a1ff4a32585e5479388a5a9983eeb557905a7ad9dc3d9ba3669dac54474f1206ad06254636a4e22ad4b1ce779ba56ea0bb55b2532a73318a044271de69c3a5c0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202fb5749897f19f440740f69d49ba5e3f258963c9ae12a8053fea08df2e2b7407002b00050403040303"), fromHex("140303000101"), fromHex("17030301ca3f555d24c967cb6849b87fdfd4b88442446a4ce5617ee0e0dcda07619832ff77223b2b68cd968e38e4c4e2c406e4072bab4d4f2e2d396bc4d4e6decf9cd413519288fda76098e2883c8f9c484012cbe2db24c8df5ed4aa381770cb70fe7353d7c9aa5eb7dc08c0e2c1524f161a6c54527168f808fecc79ca544ffab08cb91a1add4c3db44e18e6de7e1c45f8eeddf2a35710f6829292ce27063a60a011f690e5a3e24331f7608bd5c376ca630227cd9e67cfeb40fad241302a593933c706384ae07d5b1a935eceeb4f724d69c6bf667ba0bfee355a29aac6c2a6918ce7fe731bb347fd3fb506895e51282ac202e23f14609efed7682d15d274c6f55063c9f0251bc94fed142028bf3561b0e1f377f9197ea1a626e7bc890ecafdf898e49384ee9a834776bb1c54e104583600e5eaa8831bc20115e59316ee0b5ed8a214cd119aaa72da3ea4a0319c14e8013fea467e602c06789a5704b4bb3eea41acf25ee9a1a2a327e14ee03dc154392f04bc43109333ad31208f1f923c7f273de09dbb8659bcbfe5de775e048c20ae1b1ad8e77788cad5545c4c73803883fc2e7eeeb40d106ba6bfb5c95dd4f292a25b473da6e221e4c8d35efbc1627a9a29e775f5a2a388ee284406a547171d84bc")], writes: ByteArrays from [fromHex("485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b")], closeNotify: true)
end 'vector7'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	vector6()
	vector7()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
reference13-err-client-certificate-context illegalParameter
reference13-err-client-certificate-entry-extension unsupportedExtension
reference13-err-finished-instead-of-the-client-certificate unexpectedMessage
reference13-err-client-certificate-verify-without-a-certificate unexpectedMessage
reference13-err-certificate-verify-after-an-empty-client-certificate unexpectedMessage
reference13-err-client-certificate-that-is-not-a-certificate badCertificate
reference13-err-client-certificate-verify-scheme-does-not-fit-the-key illegalParameter
reference13-err-certificate-authorities-do-not-change-the-verdict unknownCa
```

<!-- test: tls-handshake-server.a-failure-sends-its-alert-and-stops-the-server -->
<!-- tier: stdlib -->
A defect before any key exists sends the contract's fatal alert in a plaintext record (`15 03 03 00 02 02 <description>`) and stops the server: the same record again throws the same error and queues nothing. A fatal alert from the client, or a close_notify before the handshake completes, is `alertReceived` and sends nothing. `write` and `close` before the client's Finished have verified are refused, although the server's whole flight is already sent.
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

typealias TextArray = Array with String

function joined(items TextArray) returns String
	var text = ""

	for (iter, item) in items.withIterator() 'eachItem'
		if iter.index() > 0 'separator'
			text.append(",")
		end 'separator'

		text.append(item)
	end 'eachItem'

	return "[{text}]"
end 'joined'

function renderRecords(records ByteArrays) returns String
	var texts = TextArray.create()

	for record in records 'eachRecord'
		texts.push(toHex(record))
	end 'eachRecord'

	return joined(texts)
end 'renderRecords'

function started() returns TlsServerHandshake
	let config = TlsServerConfig.defaults(ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")))
	let entropy = try TlsServerEntropy.fresh(config) otherwise panic("no entropy")
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.clientAuth)
	let server = try TlsServerHandshake.create(config, entropy: entropy, peer: peer) otherwise panic("no server")

	return server
end 'started'

function scenario(label String, record ByteArray)
	var server = started()

	try server.receive(record) otherwise (failure) 'refused'
		print("{label} {failure} alert={renderRecords(server.session().takeRecords())}\n")

		try server.receive(record) otherwise (again) 'repeated'
			print("{label} again {again} queued={server.session().takeRecords().count()}\n")
		end 'repeated'
	end 'refused'
end 'scenario'

function oversized() returns ByteArray
	var record = fromHex("1603034001")
	var body = ByteArray.create()
	body.resize(16385)
	record.append(body)

	return record
end 'oversized'

function early() returns ExitCode
	var server = started()

	try server.receive(fromHex("16030100e6010000e203030a4f33a445360c0a6bd954acf36ba3c0af37a5f472a11ebfef047b3b72bb5513208a1c619b434f1a7596cfb37bffa2b819a95fbd4cf30691a396bb9ccad1deb0600012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020570ee01e8453c58a5cf67c5806b7a8eefec379e149e017a921dfb64709a93120002b00050403040303")) otherwise return 1

	print("flight={server.session().takeRecords().count()} connected={server.session().isConnected()}\n")

	try server.session().write(fromHex("00")) otherwise (failure) 'writeRefused'
		print("write-before-the-client-finished {failure}\n")
	end 'writeRefused'

	try server.session().close() otherwise (failure) 'closeRefused'
		print("close-before-the-client-finished {failure}\n")
	end 'closeRefused'

	print("queued={server.session().takeRecords().count()}\n")

	return 0
end 'early'

function main() returns ExitCode
	scenario("fatal-alert-from-the-client", record: fromHex("15030300020228"))
	scenario("close-notify-before-the-handshake-completes", record: fromHex("15030300020100"))
	scenario("record-length-does-not-match", record: fromHex("160303000501"))
	scenario("alert-of-one-byte", record: fromHex("150303000100"))
	scenario("server-hello-from-the-client", record: fromHex("160303000402000000"))
	scenario("unknown-content-type", record: fromHex("630303000100"))
	scenario("application-data-before-keys", record: fromHex("170303000100"))
	scenario("empty-handshake-fragment", record: fromHex("1603030000"))
	scenario("change-cipher-spec-before-the-client-hello", record: fromHex("140303000101"))
	scenario("plaintext-longer-than-16384", record: oversized())

	return early()
end 'main'
```
```exitcode
0
```
```stdout
fatal-alert-from-the-client alertReceived alert=[]
fatal-alert-from-the-client again alertReceived queued=0
close-notify-before-the-handshake-completes alertReceived alert=[]
close-notify-before-the-handshake-completes again alertReceived queued=0
record-length-does-not-match decodeError alert=[15030300020232]
record-length-does-not-match again decodeError queued=0
alert-of-one-byte decodeError alert=[15030300020232]
alert-of-one-byte again decodeError queued=0
server-hello-from-the-client unexpectedMessage alert=[1503030002020a]
server-hello-from-the-client again unexpectedMessage queued=0
unknown-content-type unexpectedMessage alert=[1503030002020a]
unknown-content-type again unexpectedMessage queued=0
application-data-before-keys unexpectedMessage alert=[1503030002020a]
application-data-before-keys again unexpectedMessage queued=0
empty-handshake-fragment unexpectedMessage alert=[1503030002020a]
empty-handshake-fragment again unexpectedMessage queued=0
change-cipher-spec-before-the-client-hello unexpectedMessage alert=[1503030002020a]
change-cipher-spec-before-the-client-hello again unexpectedMessage queued=0
plaintext-longer-than-16384 recordOverflow alert=[15030300020216]
plaintext-longer-than-16384 again recordOverflow queued=0
flight=3 connected=false
write-before-the-client-finished unexpectedMessage
close-before-the-client-finished unexpectedMessage
queued=0
```

<!-- test: tls-handshake-server.production-entropy-is-fresh-and-consistent -->
<!-- tier: stdlib -->
`TlsServerEntropy.fresh` draws a 32-byte random, a 64-byte PSS salt and one private key per group the config lists, 32 bytes each, with the public key the group derives from it; two draws share nothing; a group without a key is `internalError`; a server started from it has sent nothing.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function secretOf(entropy TlsServerEntropy, group TlsGroupCode) returns TlsKeySecret
	return try entropy.secret(group) otherwise panic("no secret for the group")
end 'secretOf'

function main() returns ExitCode
	let config = TlsServerConfig.defaults(ByteArrays from [ByteArray from [1, 2, 3]], key: TlsPrivateKey.ecP256(ByteArray from [1]))
	var first = try TlsServerEntropy.fresh(config) otherwise return 1
	let second = try TlsServerEntropy.fresh(config) otherwise return 1

	print("random={first.random.count()} salt={first.pssSalt.count()} secrets={first.keySecrets.count()}\n")

	for secret in first.keySecrets 'eachSecret'
		print("secret group={secret.group} private={secret.privateKey.count()} public={secret.publicKey.count()}\n")
	end 'eachSecret'

	var x25519First = secretOf(first, group: 29)
	var x25519Second = secretOf(second, group: 29)
	var p256First = secretOf(first, group: 23)
	var p256Second = secretOf(second, group: 23)
	let derivedX25519 = try X25519.publicKey(x25519First.privateKey) otherwise return 2
	let derivedP256 = try EcdhP256.publicKey(p256First.privateKey) otherwise return 2

	print("random-differs={not first.random.equals(second.random)}\n")
	print("salt-differs={not first.pssSalt.equals(second.pssSalt)}\n")
	print("x25519-private-differs={not x25519First.privateKey.equals(x25519Second.privateKey)}\n")
	print("p256-private-differs={not p256First.privateKey.equals(p256Second.privateKey)}\n")
	print("x25519-public-matches={derivedX25519.equals(x25519First.publicKey)}\n")
	print("p256-public-matches={derivedP256.equals(p256First.publicKey)}\n")

	if let unexpected = try first.secret(24) 'found'
		print("unknown-group found {unexpected.group}\n")
	end 'found' else (failure) 'refused'
		print("unknown-group {failure}\n")
	end 'refused'

	let peer = TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.clientAuth)
	var server = try TlsServerHandshake.create(config, entropy: first, peer: peer) otherwise return 3

	print("connected={server.session().isConnected()} drained={server.session().takeRecords().count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
random=32 salt=64 secrets=2
secret group=29 private=32 public=32
secret group=23 private=32 public=65
random-differs=true
salt-differs=true
x25519-private-differs=true
p256-private-differs=true
x25519-public-matches=true
p256-public-matches=true
unknown-group internalError
connected=false drained=0
```

<!-- test: tls-handshake-server.a-configuration-the-machine-refuses -->
<!-- tier: stdlib -->
A server needs a certificate chain and a 32-byte random, and refuses to be built without them (`internalError`). A CertificateRequest cannot list only RSA PKCS #1 schemes, which TLS 1.3 forbids: a configuration that would send one fails when it answers the ClientHello, with the alert it sends. Enabling TLS 1.3 alone changes nothing about a TLS 1.3 handshake.
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

function peer() returns TlsX509Peer
	return TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.clientAuth)
end 'peer'

function key() returns TlsPrivateKey
	return TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b"))
end 'key'

function chain() returns ByteArrays
	return ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")]
end 'chain'

function ready() returns TlsServerConfig
	return TlsServerConfig.defaults(chain(), key: key())
end 'ready'

function bytes(count Count) returns ByteArray
	var filled = ByteArray.create()
	filled.resize(count)

	return filled
end 'bytes'

function created(label String, config TlsServerConfig, random ByteArray)
	let entropy = TlsServerEntropy.create(random, keySecrets: TlsKeySecretArray.create(), pssSalt: bytes(64))

	if let server = try TlsServerHandshake.create(config, entropy: entropy, peer: peer()) 'made'
		print("{label} created connected={server.session().isConnected()}\n")
	end 'made' else (failure) 'refused'
		print("{label} {failure}\n")
	end 'refused'
end 'created'

function answered(label String, config TlsServerConfig)
	var entropy = try TlsServerEntropy.fresh(config) otherwise panic("no entropy")
	var server = try TlsServerHandshake.create(config, entropy: entropy, peer: peer()) otherwise panic("no server")

	try server.receive(fromHex("16030100e6010000e203030a4f33a445360c0a6bd954acf36ba3c0af37a5f472a11ebfef047b3b72bb5513208a1c619b434f1a7596cfb37bffa2b819a95fbd4cf30691a396bb9ccad1deb0600012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020570ee01e8453c58a5cf67c5806b7a8eefec379e149e017a921dfb64709a93120002b00050403040303")) otherwise (failure) 'refused'
		print("{label} {failure} records={server.session().takeRecords().count()}\n")

		return
	end 'refused'

	print("{label} answered records={server.session().takeRecords().count()}\n")
end 'answered'

function main() returns ExitCode
	var requesting = TlsServerConfig.defaults(chain(), key: key())
	requesting.clientAuthentication = TlsClientAuthentication.request
	var pkcs1Only = TlsServerConfig.defaults(chain(), key: key())
	pkcs1Only.clientAuthentication = TlsClientAuthentication.request
	pkcs1Only.clientSignatureAlgorithms = TlsSignatureSchemeCodeArray from [0x0401, 0x0501, 0x0601]
	var onlyTls13 = TlsServerConfig.defaults(chain(), key: key())
	onlyTls13.protocols = TlsProtocolArray from [TlsProtocol.tls13]

	created("a-complete-configuration", config: ready(), random: bytes(32))
	created("no-certificate-chain", config: TlsServerConfig.defaults(ByteArrays.create(), key: key()), random: bytes(32))
	created("a-random-of-31-bytes", config: ready(), random: bytes(31))
	created("a-random-of-33-bytes", config: ready(), random: bytes(33))
	answered("a-client-hello-answered", config: ready())
	answered("a-request-for-a-certificate", config: requesting)
	answered("a-request-listing-only-pkcs1", config: pkcs1Only)
	answered("tls13-enabled", config: onlyTls13)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
a-complete-configuration created connected=false
no-certificate-chain internalError
a-random-of-31-bytes internalError
a-random-of-33-bytes internalError
a-client-hello-answered answered records=3
a-request-for-a-certificate answered records=3
a-request-listing-only-pkcs1 internalError records=3
tls13-enabled answered records=3
```

<!-- test: tls-handshake-server.an-alert-after-the-keys-are-installed-is-encrypted -->
<!-- tier: stdlib -->
A failure after the handshake completes sends its alert under the server's current application write keys. The canra reference client's handshake is replayed to the connected state, its application data record is forged, and the fatal bad_record_mac alert the server sends opens under `server_application_traffic_secret_0` of the same handshake, which the canra reference server derived, as an alert record holding `02 14`.
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

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a canra private key was refused")
end 'secret'

function main() returns ExitCode
	let suite = Tls13CipherSuite.chaCha20Poly1305Sha256
	let config = TlsServerConfig.create(TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], key: TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")), clientAuthentication: TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 1283, 2055, 2052, 2053, 2054, 1025, 1281, 1537], clientAuthorities: ByteArrays.create())
	let entropy = TlsServerEntropy.create(fromHex("b3797dace6f5dfdde198176354da5a242e6ca971ce9cb6306395f384546888eb"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78b23ce497e8080e1e5bca97a466071b99f8204e3b9d1a9e9dfe52fa86dbacc6")), secret(23, privateKey: fromHex("4e994f156d979f4ff3a5deb19eb4d7b2e1580c6660086043de9e446ab11a3381"))], pssSalt: fromHex("4fe158385e7e9c7566674942171e114087c94a2454594c9632b634c98d79e86bfca625cb32ab4a05fba9b6d0733584196d3ff270f94fdb3e6d35f0b03f437634"))
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.clientAuth)
	var server = try TlsServerHandshake.create(config, entropy: entropy, peer: peer) otherwise return 2

	try server.receive(fromHex("16030100e6010000e2030332e1707ee79bb5e98fb04609ffc0969ed8e3b8a1a3fa25e4c8d2d2111030f4b5208da713486a7f372cd80164ce077e6c3cd5ce272198aea8d9f01a71bca742f7650012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002034d72227b61e1051bfdf1f0efba593086f7ec567a401bf0f3abcb8379af29e0f002b00050403040303")) otherwise return 3
	try server.receive(fromHex("140303000101")) otherwise return 4
	try server.receive(fromHex("17030300354173f8c098fa387e217e7f980c6a20bf7c0574c258cfcc81e18b0fc91a9bd53270efb02a8f59f036deb440647fbe434a9e1f6ec47f")) otherwise return 5

	print("connected={server.session().isConnected()} flight={server.session().takeRecords().count()}\n")

	var forged = fromHex("170303003604a1e28c1e2e830344ecca885cdc4f0a06b6df2ad793d8ce296e8fe4c4b982c5b36b7f1ba4066a6b56012e9d627a2275d780ba0a4172")
	let last = forged.count() - 1
	let flipped = (try forged.get(last) otherwise return 6) xor 1
	try forged.set(last, value: flipped) otherwise return 7

	try server.receive(forged) otherwise (failure) 'refused'
		print("failure {failure}\n")

		let records = server.session().takeRecords()
		let alertRecord = try records.get(0) otherwise return 8
		let keys = try Tls13KeySchedule.trafficKeys(suite, trafficSecret: fromHex("dd962e033fab1421beac25be66794c191b2f355c67d82f8f2d4e6d7cf2c4133d")) otherwise return 9
		var reader = try Tls13RecordProtection.create(suite, key: keys.key(), iv: keys.iv()) otherwise return 10
		let opened = try reader.open(alertRecord) otherwise return 11

		print("records={records.count()} prefix={toHex(try alertRecord.slice(0, endIndex: 5) otherwise return 12)} type={opened.contentType().rawValue} plaintext={toHex(opened.plaintext())}\n")
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
connected=true flight=3
failure badRecordMac
records=1 prefix=1703030013 type=21 plaintext=0214
```

<!-- test: tls-handshake-server.our-client-handshakes-with-our-server -->
<!-- tier: stdlib -->
The client of this stdlib and its server handshake with each other in memory, each with the production entropy, the client verifying the server's chain for example.com against a root and the server requiring and verifying the client's chain when asked, both limited to TLS 1.3: each TLS 1.3 suite with and without client authentication, and each server key type (ECDSA P-256, ECDSA P-384, Ed25519, RSA-PSS) with an Ed25519 or RSA client certificate. A line per side prints what it negotiated, whether it authenticated its peer, whether it saw the peer's close_notify and the application data it received (a request, a response); the server's line adds whether its ServerHello carried extended_master_secret, which a TLS 1.3 ServerHello never does.
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

enum ServerKind
	p256
	p384
	ed25519
	rsa
end 'ServerKind'

enum ClientKind
	none
	p256
	ed25519
	rsa
end 'ClientKind'

enum Tweak
	none
	withoutExtendedMasterSecret
	serverSignsPkcs1
	clientSignsPkcs1
	secp256r1Only
end 'Tweak'

function serverCredential(kind ServerKind) returns (ByteArrays, TlsPrivateKey)
	return match kind 'kind'
		p256 gives (ByteArrays from [fromHex("3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")], TlsPrivateKey.ecP256(fromHex("0f9999d9198528f9f986f8315fe0c7aaf7b2aef840198d77b859dc4f4a8c4a2b")))
		p384 gives (ByteArrays from [fromHex("308201763082011da00302010202010b300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663076301006072a8648ce3d020106052b81040022036200043832f60f3e4a462626db06a2f111e8d720f6899694ba2596a07cd906daeb6a968ca938e87ee4c4f9ec57ad8ce1a552b6ab95b1c2d8fa4c25e2058b58e08640a26b2d4f953c297511683247a1f620a0854d3ef5ff6ce43855205dce2b0608c88fa34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203470030440220315764a5d757ce1d4517a871f74df0fadea37633f3a996a71724d28de559e3af02201fc71642214f349b358fc75323241cdce73b2b3cc39809d83833ed991880cb5c")], TlsPrivateKey.ecP384(fromHex("11afb2f77a2dec51c976f3225ec7e0eea79b70dab08e2ee263317047c621cd679b49f41aa42549e4cc3180e6191498a9")))
		ed25519 gives (ByteArrays from [fromHex("308201293081d1a00302010202010d300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c656166302a300506032b657003210011d1a93d4aa46dc06990a15578b6516e837ef6133e5e2fd6d5588089a2539933a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d0403020347003044022052968aac0bfce24da22eae0ce09a8ea82babf6e91409b5986a62f12f74ab70ee022016592ac0afdbcf4dd110fb33b99d5505b00b0888ec45b3a53a4e6f4bb4e9d008")], TlsPrivateKey.ed25519(fromHex("c875a7890a8054f285794a5414fbda9e4693700489f4bd2741908071978533a2")))
		rsa gives (ByteArrays from [fromHex("30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")], TlsPrivateKey.rsa(fromHex("308204be020100300d06092a864886f70d0101010500048204a8308204a40201000282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b302030100010282010024736f7bf865dd98002a19941473c8f3397ea0d208ca6e7bcd4f5892548f1935871e9acbf5b75d34236e97d47374ff93a3a0120303fdbcf71757c7322ece00ba517b0af64a1a436514fcf6c59034f935474db31ecb032063bf92d06eaeb0965959ea916b51f2c9b53824247612563f1640e0ae8a1440a6f3dbb442c0633c0a6f7de7ba71d3e4244b8a7bc1864cda9fc8b12c8e9ddbf99a134e4dd1815700ba34737712ccf781539a9100dad22e10aba85012a56d0104967e14bd5508a287862903a5c84ddfb573a59610a20c8adea62a464d740f62b08163036af3321949e6afc0f430250142f6445c58dfd80c9e93e23d32059656cde2f6763b68788a3d8b2102818100c7604784668dbc3abf35f39f6dd6ea60fdae845e9c91939e9b3ec67335c76bbcccdaf21ab32ce9c484d8a80f482387b6048262c2dc0f98827f8110ef03d8e835787297fd8ae563c6c9bfb20cb4346ba15bb81ebfcfefc05126ec1fb402db7b5ad7d6e3874aeaad1eebebc7a60a0241b684c23df05e6b7724f37a9c1dffe6e3d902818100c33f64247f70fbbb32ecdc24e98d379727c18d05f4dbc9cf0bfbac772b6b4077ef16105ac145484ddf0e93edabf531cfb75af56cf0aee3cd51e6d40cde751b8a3869be1d03fb6722f5c3b8b23e97eb3b6341fef7690b47e39308e34a522c6bb6e4ef64b56569cb8563ed2a924d9faa32c0a9ab8148b8e05a4537d75dcf959c6b028181008ec392726c3cf19bdf6f7293a6c34df145b5c1733e09bffac28fa0eabe879dce03a283da3a92de91a9ceba08b74f39ce4c4c510bd72813bcde303d3fe9315895713a1d3ba049c1b8869e8d902b399e185ce4ea9fcef58e708dbe66b52f969a2de2802d4ca388878eacc098a57c654148c0a796529849bb76ea21d1c869859b8102818100932ce096e67dcb337412b96b15a5fe66717063963ed9c248d116980d886c233f84b8940165388d652da3b67c6ca7ba31825c0d2e5e4ae6aaa9771f16c8456c4a077625f9873be5a5a3ae3236221ab015a71f634b2a2277c122df8b3d45adf84ff8484018c0663c24c38b4e10d4746b7e305e754a52001215d9a15b357ab9a31b0281804264563254965cdc052103249118f22693332397e0a99f0b1a8440cdc21581ed70ebe413945346acd53605a3635fadd427f62e4078236527509c0c6b40e8a2bac85787638bc5864eefa7382f9ce32efd82d0fd3a009d2df06c866f2d18f0775a4c98b6f558c5734aa54570923e544658e0596e40bb52d2618a35e3fe03c0b58e")))
	end 'kind'
end 'serverCredential'

function clientIdentity(kind ClientKind) returns TlsClientIdentity
	return match kind 'kind'
		none gives TlsClientIdentity.anonymous
		p256 gives TlsClientIdentity.certified(ByteArrays from [fromHex("3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91")], key: TlsPrivateKey.ecP256(fromHex("fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da")))
		ed25519 gives TlsClientIdentity.certified(ByteArrays from [fromHex("3082013c3081e2a003020102020176300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c056361726f6c302a300506032b6570032100f004952b22a93a2274111343d8b740acb309a18255094b6ab57d90ad545bc0b4a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020349003046022100af38b2ee559ea1c36ad386fb553c43c108109c6c4ce1ab62dd7e2567987d1644022100a33596c2359214abef9ea098e3ad30ed3bc52d48c3a40d5b4ed558f069298dfd")], key: TlsPrivateKey.ed25519(fromHex("8408cd68c509089a76695eb5c519ff0966fad7246013da6e9619f3032f71e404")))
		rsa gives TlsClientIdentity.certified(ByteArrays from [fromHex("30820234308201daa003020102020175300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300e310c300a06035504030c03626f6230820122300d06092a864886f70d01010105000382010f003082010a0282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d0203010001a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020348003045022035a866be9f42da07b71e1df23a9c60a0ea5620dd55388f5ddb71bb848ab6711e022100a1502d0bfb23d04f46a5d2187bdffc938b1aebe54b36b54622c0c58c154cf2ac")], key: TlsPrivateKey.rsa(fromHex("308204bd020100300d06092a864886f70d0101010500048204a7308204a30201000282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d02030100010282010045df65c5ca9a3fb1da331c19e500efd2d09618922e8fdf0738a28e12b8cdfc4c9965397375730abec9ff878995ea476eacb1a9fb369fa099302309a9e93458b010714db9a90eda99bf17758f15ff1d25557a891368f328e321afa5e5d6af7aaaa6423e8ce10afa5f09c42cae0321f3718bb5c835b19410ff62d640421338dd7ec628dc43300c23e36bb201c57474de8bd382a64ae081a8a81bf0ecce10169ab362fa3b3736633a2045041e393493bd44c7795c88a9e74a348f6b3da074469db759e29bd7054d2ac252f7efbf49b14c40dbd442abf51091580e0e21e2a01322840ac3f8d4634bccd79cfd41fdb615e07bd1cb858d6e76f3b9e94fd032a5ea290502818100e9d14e6b52e82ead2112a1ec53f521fb9f81384fcb6ad79948afab9b434345db1fe0759b6b7cf787091698d23ec129370cb35ff478c359a7582c5e13c23c15ed919b778fb91219b0c09e6e922f62021d26632c33d8206bd78717f606ec43669e37cdaee172360e2b4f08827a6fc43e4cee822784a20e224bd1b5935d0505c35f02818100c5f536aee5b2f848afc370d68fa2ac1503e4f8c3b85631bf9baaadf0c03055612634b819ee8ac36e217be9f35b6d02f3d206bae460e51eba2adef5a7c40ee3ebf8856a94da2f90488c15051e96593087e4d2e331fd490fce4cd8a8f8b20a3d5cfb865ce0d392f85351a2ddb3bc82d878943370e64b4b5dd8d90e72092b0bcbc302818100ddc23afb0555dab72f2fe9cd27cfcd9c45aa9c0aea25fa37923db2a77e42f6a51df597f5811bb3f429c579c9a5e533bed757e5010bec5342a918e35d201236c5ec1ca467f6a259254f7ab3ea07f08f8ab016cd1c9ab61d31fc19d8a8b63a32c08ab29453942bfa824ee20646336ccf9dcd743a20cc465576cd7dc6372a054e47028180008cbbc8248f28ad5aff99f29e977f0ed7e1ece6ee3e1f093f2f6c283d00655c4db4dbc606a98ffd604e6dc9e5391a46ff1051b88ca4563f821af867acddb5b8540a5acf6b03b2cce0c6b62aed93f5d48ac823256264af8c8d1e14ea0be4876318b7b8ce4f081b746fc79a0203c6013fe4797818cba0bbf3fc36540ebe75dbed028180607cc46d66a8c6c0599730342557c5fba4e2c5ce59cb965be8889fc3e1f3b40fda0f8d09a9ba65a55c3df93f1c7540493dfe0345f5060082417442c0f31259fe8923aa19020a143af648d916ac3f11f6449ad7471fc1ccdb6b8cfd95c71fc27a6113efe24a6884f0fb804cd75e94574785436e1593ee4511431b32cf15040e5a")))
	end 'kind'
end 'clientIdentity'

function onlyTls13() returns TlsProtocolArray
	return TlsProtocolArray from [TlsProtocol.tls13]
end 'onlyTls13'

function onlyTls12() returns TlsProtocolArray
	return TlsProtocolArray from [TlsProtocol.tls12]
end 'onlyTls12'

function bothVersions() returns TlsProtocolArray
	return TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12]
end 'bothVersions'

function schemes(tweak Tweak, pkcs1Tweak Tweak, pkcs1 TlsSignatureSchemeCodeArray, preference TlsSignatureSchemeCodeArray) returns TlsSignatureSchemeCodeArray
	if tweak == pkcs1Tweak 'pkcs1Only'
		return pkcs1
	end 'pkcs1Only'

	return preference
end 'schemes'

function groupsFor(tweak Tweak) returns TlsGroupCodeArray
	if tweak == Tweak.secp256r1Only 'single'
		return TlsGroupCodeArray from [0x0017]
	end 'single'

	return TlsGroupCodeArray from [0x001D, 0x0017]
end 'groupsFor'

function shareGroupsFor(tweak Tweak) returns TlsGroupCodeArray
	if tweak == Tweak.secp256r1Only 'single'
		return TlsGroupCodeArray from [0x0017]
	end 'single'

	return TlsGroupCodeArray from [0x001D]
end 'shareGroupsFor'

type OptionalExtendedMasterSecret implements TlsClientHelloSource
	var strips as bool

	static function create(strips bool) returns OptionalExtendedMasterSecret
		return Self{strips: strips}
	end 'create'

	function first(built ByteArray) returns ByteArray
		if not strips 'kept'
			return built
		end 'kept'

		let message = try tlsDecodeClientHello(built.clone()) otherwise panic("the ClientHello the config builds decodes")
		var kept = TlsExtensionArray.create()

		for item in tlsExtensionsOf(message.extensions()) 'eachExtension'
			if item.extensionType() != TlsExtensionKind.extendedMasterSecret.code() 'other'
				kept.push(item)
			end 'other'
		end 'eachExtension'

		let hello = TlsClientHello.create(message.legacyVersion(), random: message.random(), legacySessionId: message.legacySessionId(), cipherSuites: message.cipherSuites(), compressionMethods: message.compressionMethods(), extensions: TlsExtensionBlock.listed(kept))

		return try TlsHandshakeCodec.encode(TlsHandshakeMessage.clientHello(hello)) otherwise panic("a ClientHello without an extension encodes")
	end 'first'

	function second(built ByteArray) returns ByteArray
		return built
	end 'second'
end 'OptionalExtendedMasterSecret'

function renderVersion(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "none"
		accepted or
		keyed gives "772"
		accepted12 or
		keyed12 gives "771"
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "none"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
		accepted12(suite) gives "{suite.rawValue}"
		keyed12(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted or
		accepted12 gives "none"
		keyed(_, group) gives "{group}"
		keyed12(_, group) gives "{group}"
	end 'negotiation'
end 'renderGroup'

function renderAlpn(alpn TlsAlpnChoice) returns String
	return match alpn 'alpn'
		absent gives "none"
		negotiated(protocol) gives toHex(protocol)
	end 'alpn'
end 'renderAlpn'

function serverHelloOffersExtendedMasterSecret(firstRecord ByteArray) returns bool
	let body = tlsRecordPart(firstRecord, start: tlsRecordHeaderBytes, endIndex: firstRecord.count())
	let length = (tlsRecordByte(body, offset: 1) as Count) * 65536 + (tlsRecordByte(body, offset: 2) as Count) * 256 + (tlsRecordByte(body, offset: 3) as Count)
	let message = tlsRecordPart(body, start: 0, endIndex: 4 + length)
	let decoded = try tlsDecodeHandshake(message, protocol: TlsProtocol.tls13) otherwise panic("the first record of a server holds a ServerHello")

	return match decoded 'decoded'
		serverHello(hello) gives TlsExtensionScan.ofBlock(hello.extensions()).types.contains(TlsExtensionKind.extendedMasterSecret.code())
		default panic("the first message of a server is a ServerHello")
	end 'decoded'
end 'serverHelloOffersExtendedMasterSecret'

function messagesOf(record ByteArray) returns ByteArrays
	var messages = ByteArrays.create()
	var offset = tlsRecordHeaderBytes as ElementIndex

	while offset < record.count() 'eachMessage'
		let length = (tlsRecordByte(record, offset: offset + 1) as Count) * 65536 + (tlsRecordByte(record, offset: offset + 2) as Count) * 256 + (tlsRecordByte(record, offset: offset + 3) as Count)

		messages.push(tlsRecordPart(record, start: offset, endIndex: offset + 4 + length))
		offset = offset + 4 + length
	end 'eachMessage'

	return messages
end 'messagesOf'

function signatureSchemeOf(raw ByteArray) returns String
	let message = try tlsDecodeHandshake(raw, protocol: TlsProtocol.tls12) otherwise panic("a plaintext TLS 1.2 flight holds whole messages")

	return match message 'message'
		serverKeyExchange(exchange) gives "{exchange.signatureScheme()}"
		certificateVerify(scheme, _) gives "{scheme}"
		default panic("only signed messages are asked for")
	end 'message'
end 'signatureSchemeOf'

function signatureSchemeIn(record ByteArray, kind TlsHandshakeType) returns String
	for raw in messagesOf(record) 'eachMessage'
		let found = try TlsHandshakeType.fromRawValue(tlsRecordByte(raw, offset: 0)) otherwise panic("a flight holds known message types")

		if found == kind 'wanted'
			return signatureSchemeOf(raw)
		end 'wanted'
	end 'eachMessage'

	return "-"
end 'signatureSchemeIn'

function renderSide(trace TlsReplayTrace) returns String
	return "connected={trace.connected} version={renderVersion(trace.negotiation)} suite={renderSuite(trace.negotiation)} group={renderGroup(trace.negotiation)} alpn={renderAlpn(trace.alpn)} peer-authenticated={trace.peerAuthenticated} peer-closed={trace.peerClosed} records={trace.outbound.count()} received={toHex(trace.received)}"
end 'renderSide'

function renderSchemes(trace TlsLoopbackTrace) returns String
	match trace.server.negotiation 'negotiation'
		accepted12 or
		keyed12 then return " server-key-exchange-scheme={signatureSchemeIn(try trace.server.outbound.first() otherwise panic("a server sent its flight"), kind: TlsHandshakeType.serverKeyExchange)} client-verify-scheme={signatureSchemeIn(try trace.client.outbound.get(1) otherwise panic("a client sent its flight"), kind: TlsHandshakeType.certificateVerify)}"
		pending or
		accepted or
		keyed then return ""
	end 'negotiation'
end 'renderSchemes'

function scenario(label String, serverKind ServerKind, clientKind ClientKind, serverProtocols TlsProtocolArray, clientProtocols TlsProtocolArray, suites TlsCipherSuiteCodeArray, tweak Tweak)
	let (chain, key) = serverCredential(serverKind)
	let mutual = clientKind != ClientKind.none
	let serverConfig = TlsServerConfig.create(serverProtocols, cipherSuites: suites, groups: groupsFor(tweak), signatureAlgorithms: schemes(tweak, pkcs1Tweak: Tweak.serverSignsPkcs1, pkcs1: TlsSignatureSchemeCodeArray from [0x0401], preference: TlsSignatureSchemeCodeArray from [0x0403, 0x0503, 0x0807, 0x0804, 0x0805, 0x0806, 0x0401, 0x0501, 0x0601]), applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], chain: chain, key: key, clientAuthentication: TlsClientAuthentication.require if mutual else TlsClientAuthentication.none, clientSignatureAlgorithms: TlsSignatureSchemeCodeArray from [0x0403, 0x0503, 0x0807, 0x0804, 0x0805, 0x0806, 0x0401, 0x0501, 0x0601], clientAuthorities: ByteArrays.create())
	let clientConfig = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: clientProtocols, cipherSuites: suites, groups: groupsFor(tweak), keyShareGroups: shareGroupsFor(tweak), signatureAlgorithms: schemes(tweak, pkcs1Tweak: Tweak.clientSignsPkcs1, pkcs1: TlsSignatureSchemeCodeArray from [0x0403, 0x0503, 0x0401], preference: TlsSignatureSchemeCodeArray from [0x0403, 0x0804, 0x0807, 0x0503, 0x0805, 0x0806, 0x0401, 0x0501, 0x0601]), applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: clientIdentity(clientKind))
	let serverEntropy = try TlsServerEntropy.fresh(serverConfig) otherwise panic("no server entropy")
	let clientEntropy = try TlsClientEntropy.fresh(clientConfig) otherwise panic("no client entropy")
	let serverPeer = TlsX509Peer.create(ByteArrays from [fromHex("3082017e30820124a003020102020164300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a30163114301206035504030c0b436c69656e7420526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000440dc1caaaee821a08a60dcb74b34befd018659bdb88f3c7b77e9884ebfb821dd99d3cb7909555d3f0ef6f05a87ec68cff0756403703002efe4607c9b92f86252a3633061300f0603551d130101ff040530030101ff301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376301d0603551d0e04160414b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff040403020106300a06082a8648ce3d040302034800304502202b34aae7b62e533d12661be590a736eee7cd71c02124401b1d03f88e0455fa3b022100a9e9fee45aa249f83c6b148e6f1fab78dbfccea918ebcdedf797ec772e634bed")], now: 1767225600, purpose: X509Purpose.clientAuth)
	let clientPeer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	var server = try TlsServerHandshake.create(serverConfig, entropy: serverEntropy, peer: serverPeer) otherwise panic("no server")
	var client = try TlsClientHandshake.create(clientConfig, entropy: clientEntropy, peer: clientPeer, helloSource: OptionalExtendedMasterSecret.create(tweak == Tweak.withoutExtendedMasterSecret)) otherwise panic("no client")
	let request = fromHex("474554202f20485454502f312e310d0a0d0a")
	let response = fromHex("485454502f312e3120323030204f4b0d0a0d0a6f6b")

	if let trace = try TlsLoopback.run(client, server: server, clientWrites: ByteArrays from [request], serverWrites: ByteArrays from [response], closeNotify: true) 'looped'
		let extended = serverHelloOffersExtendedMasterSecret(try trace.server.outbound.first() otherwise panic("a server sent its flight"))

		print("{label} client {renderSide(trace.client)}\n")
		print("{label} server {renderSide(trace.server)} extended-master-secret={extended}{renderSchemes(trace)}\n")
	end 'looped' else (failure) 'refused'
		print("{label} refused {failure}\n")
	end 'refused'
end 'scenario'

function suiteList(suite TlsCipherSuiteCode) returns TlsCipherSuiteCodeArray
	return TlsCipherSuiteCodeArray from [suite]
end 'suiteList'

function main() returns ExitCode
	scenario("chacha20-poly1305", serverKind: ServerKind.p256, clientKind: ClientKind.none, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1303], tweak: Tweak.none)
	scenario("aes-128-gcm", serverKind: ServerKind.p256, clientKind: ClientKind.none, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1301], tweak: Tweak.none)
	scenario("aes-256-gcm", serverKind: ServerKind.p256, clientKind: ClientKind.none, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1302], tweak: Tweak.none)
	scenario("chacha20-poly1305-mutual", serverKind: ServerKind.p256, clientKind: ClientKind.p256, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1303], tweak: Tweak.none)
	scenario("aes-128-gcm-mutual", serverKind: ServerKind.p256, clientKind: ClientKind.p256, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1301], tweak: Tweak.none)
	scenario("aes-256-gcm-mutual", serverKind: ServerKind.p256, clientKind: ClientKind.p256, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1302], tweak: Tweak.none)
	scenario("p384-server", serverKind: ServerKind.p384, clientKind: ClientKind.none, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1302], tweak: Tweak.none)
	scenario("ed25519-server", serverKind: ServerKind.ed25519, clientKind: ClientKind.none, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1301], tweak: Tweak.none)
	scenario("rsa-server-ed25519-client", serverKind: ServerKind.rsa, clientKind: ClientKind.ed25519, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1303], tweak: Tweak.none)
	scenario("p256-server-rsa-client", serverKind: ServerKind.p256, clientKind: ClientKind.rsa, serverProtocols: onlyTls13(), clientProtocols: onlyTls13(), suites: TlsCipherSuiteCodeArray from [0x1301], tweak: Tweak.none)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
chacha20-poly1305 client connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
chacha20-poly1305 server connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=false peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
aes-128-gcm client connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
aes-128-gcm server connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=false peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
aes-256-gcm client connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
aes-256-gcm server connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=false peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
chacha20-poly1305-mutual client connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
chacha20-poly1305-mutual server connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
aes-128-gcm-mutual client connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
aes-128-gcm-mutual server connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
aes-256-gcm-mutual client connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
aes-256-gcm-mutual server connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
p384-server client connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
p384-server server connected=true version=772 suite=4866 group=29 alpn=687474702f312e31 peer-authenticated=false peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
ed25519-server client connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
ed25519-server server connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=false peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
rsa-server-ed25519-client client connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
rsa-server-ed25519-client server connected=true version=772 suite=4867 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
p256-server-rsa-client client connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=485454502f312e3120323030204f4b0d0a0d0a6f6b
p256-server-rsa-client server connected=true version=772 suite=4865 group=29 alpn=687474702f312e31 peer-authenticated=true peer-closed=true records=5 received=474554202f20485454502f312e310d0a0d0a extended-master-secret=false
```

<!-- test: tls-handshake-server.a-user-program-cannot-name-the-server-handshake -->
A program outside `stdlib/` cannot name `TlsServerHandshake`, the handshake state machine: it is `module` code, reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsServerHandshake.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsServerHandshake' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake-server.a-user-program-cannot-name-the-server-replay -->
A program outside `stdlib/` cannot name `TlsServerReplay`, the test seam's driver: it is `module` code, reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsServerReplay.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsServerReplay' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake-server.a-user-program-cannot-name-the-loopback -->
A program outside `stdlib/` cannot name `TlsLoopback`, the driver that connects a client to a server in memory: it is `module` code, reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsLoopback.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsLoopback' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake-server.a-user-program-cannot-name-the-server-configuration -->
A program outside `stdlib/` cannot name `TlsServerConfig`, the server's configuration: it is `module` code, reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsServerConfig.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsServerConfig' is module-scoped and not visible from this directory
```
