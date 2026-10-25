---
feature: tls-handshake-negotiation
status: stable
keywords: [tls-handshake-negotiation, TlsClientHandshake, tls 1.3, hello retry request, key share, alerts, record layer, key update, rfc8446, rfc8448, tls, crypto]
category: crypto
---

# `TlsClientHandshake` — negotiation, HelloRetryRequest, records and post-handshake messages

## Documentation

The vectors of the canra `tls-handshake-negotiation` spec for the TLS 1.3 client of `specs/tls-handshake.md`:
version and key-share negotiation, HelloRetryRequest, ALPN and EncryptedExtensions, the record layer, change_cipher_spec
and post-handshake messages. A success vector prints the Trace as compact JSON; an error vector prints the
`TlsHandshakeError`, which names the alert the client sends. The cases are library-tier
(`<!-- tier: stdlib/helpers/tls -->`) for the reason `specs/tls-handshake.md` gives, and the canra `ClientConfig` fields a
vector omits take the same defaults. Every one of the 35 canra vectors of this spec has a case: the types leave
none unspellable.

## Tests

<!-- test: tls-handshake-negotiation.hello-retry-request-replays -->
<!-- tier: stdlib/helpers/tls -->
HelloRetryRequest handshakes: the RFC 8448 section 5 trace and the section 7 compatibility-mode trace replay byte for byte, as does an OpenSSL 3.0.13 HelloRetryRequest to secp256r1; a ClientHello with no server answers `handshaking`; the reference server's HelloRetryRequest is answered with a second ClientHello whose key_share is the one secp256r1 share.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.defaults(X509VerifyHost.none)
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0ed02f8e8117efc75ca7ac32aa7e34eda64cdc0ddad154a5e85289f959f63204")), secret(23, privateKey: fromHex("ab5473467e19346ceb0a0414e41da21d4d2445bc3025afe97c4e8dc8d513da39"))], pssSalt: ByteArray.create())
	let peer = TlsUnauthenticatedPeer.create()
	let helloSource = TlsReplacedClientHello.create(fromHex("010000b00303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010000810000000b0009000006736572766572ff01000100000a00080006001d00170018003300260024001d0020e8e8e3f3b93a25ed97a14a7dcacb8a272c6288e585c6484d05262fcad062ad1f002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), retry: ByteArrays from [fromHex("010001fc0303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00080006001d001700180033004700450017004104a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002d00020101001c00024001001500af00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")])
	report("client13-rfc8448-hello-retry-request", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300b0020000ac0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c001301000084003300020017002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002b00020304"), fromHex("160303007b020000770303bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b9800130100004f003300450017004104583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d002b00020304"), fromHex("170303029699bee20baf5b7fc727bfab6223928a381e6d0cf9c4da653f9d2a7b23f7de11cce842d5cf75631763450ffb8b0cc1d238e658af7a12adc86243114ab14a1da2fae42621ce483fb6242eabfaad52566b02b31d2eddedefeb80e66a9900d5f973b40c4fdf74719ecf1b68d7f9c3b6ceb903ca13dd1bb8f8187ae33417e1d152522c5822a1a03ad52c838c55953d610222874cce8e1790b229a2aa0b53c8d377ee720182951dc6181dc5d90bd1f0105ed1e84aa5f75957c6661897079e5ea5007449e3197bdc7c9beeedddeafdd844afa5c315ecfe65e576afe909812880620ec7048b42d7f5c78d76f299d6d82534bdd8f512febc0ed3814aca470cd8000d3e1cb9962b052fbb950df683a52c2ba77ed3713b122937a6e5170964e2ab7969dcd980b3db9b458da7603124d6dc005e4d6e04b4d0c4baf3275db827dbba0a6db09672171fc057b3851d7e026841e2978fbd2346bbefdd0376bb1108fe9acc92189f5650aa5e85d8e8c7b67ac510dba003d3d7e16350bb66d45013efd44c9b607c0d318c4c7d1a1f5cbc57e20611804e3787d7b4a4b5f08ed8fd70bdaeade02260b12ab842ef690b4a3ee7911e841b374ecd5ebbbc2a54d047b600336dd7d0c88b4bc10e58ee6cb656de7247fa20d8e91deb84628608cf80615b62e96c1491c7ac3755eb6901405d3474fe1ac79d106a0cee56c2577fc88480f96cb6b8c681b7b68b53c146093908f350888175bdfb0b1e31ad61e30ba0adfe6d223aa03c0783b5001a57587c328a9afcfcfb978d1cd4328f7d9d60530e630befd96c0c816ee20b0100768ae2a6df51fc68f172740a79af11398ee3be1252491fa9c693479e877f94ab7c5f8cad480203e6ab7b87dd71e8a0729113df17f5eee86ce108d1d72007ec1cd13c85a6c149621e77b7d78d805a30f0be030c315e54"), fromHex("1703030013519fc5075cb0884349759ff9ef6f011bb4c6f2")], writes: ByteArrays.create(), closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.defaults(X509VerifyHost.none)
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("dea00b45695dc781f19d34a62c1afd31ab4369af1e855a3bbb258d8442cde6d7"))], pssSalt: ByteArray.create())
	let peer = TlsUnauthenticatedPeer.create()
	let helloSource = TlsReplacedClientHello.create(fromHex("010000dc03034e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d0920a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef00061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d00208e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), retry: ByteArrays.create())
	report("client13-rfc8448-compatibility-mode", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303e5dd5948c435f7a38f0f0130708dc322d9df09abd4838117c183a7bb6d994f2c20a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef130100002e00330024001d00203e30f0f4ba551afd62768341175f5265e4daf0c8841617aa4fafdd2142320c22002b00020304"), fromHex("140303000101"), fromHex("17030302a248de891d9c3624a67a6c6f0601ab7ac20c1f6a9e14d2e6007e999e130367a8af1bcfea9498fbce19df4505eece3a25da523cbe55ea1b3bda4e91995e455d500a4faa6227b7111e1c8547e2d7c179db215303d25827f3cd18f48f6491328cf5c0f814d388150bd9e9264aae491db6995069bea17665d5e0c817284d4ac21880054c3657331e23a9304dc88a15c04ec80bd3852bf7f9d3c6615b15fac83bbca031c6d2310d9f5d7a4b020a4f7c19062b65c05a1d3264b557ec9d8e0f7cee27e36f793039de8dd96edfca9009e0651034bff31d7f349eece01d99fcb5fcab840d7707c72299c3b5d04564e880a33c5e846c762e3d922bb55303d1d87cc0f06573f17dcb9b8ffd35bbd883c1cb3aa24fcc325005f768ce2fb624ca97b6c4d98e17f35bc2c7940a06100c2d448db7180b2d862164435c9c210e9860394e05aab23ff1b0203f662c588da5bc4411477a30b41136c488a0a63fcab5c15ac613226dae827a1d1fe95ece6b30bcee1560a8d408d264555e760f9bfc624c2c87fd0456c9bfb41bcd1a7b212786d2b67fd57804facfa1eef7cf2919d8b998c9789f763b4d9caa093a9ded43175d46a76b4d54f0ce0c5d2259b607e30a9d241263874fa59d6f570dc40d83a2d83bf9e9850d454c57806535a8998ae0357df92f00b9667344c24114ccc9ef539124b204e7e6e748c30a28a3d1d183997243eaccbbd33b0c1115a0327106a1e6a75271d4983086f632ff0eb8b4c63102cbcef5bb72dae1279d5de8eb19096d8cdb07fa8ea989788fac23e66e0488c193f3f3fea8c8838896bf3ae4b6848d42ced4bdf41abe6fc331b44225e7a1f7d3564147d5458e71aa909cb02be958bbc42e3aa5a27cc6eaf4b6fe51ae4495694d8ab6320aab920183fd5b31a359042fbd67391ec5e4d1892a2e5210141a494e9301b24a113c474c7f2a73457847"), fromHex("1703030013b7257b0fecaf69d4f09e3f891e2a25d1e28845")], writes: ByteArrays.create(), closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [23], keyShareGroups: TlsGroupCodeArray.create(), signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("4d16a47657b0347673f9861ab883c0ac44a5c6c203aaaf52c93c8f10d44531cd"), sessionId: fromHex("6b3b075296470fda9788a006b2faed26d4b69a8f3377e7677d6a47124dab4585"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fb0f3ba5439f5a7b999661c123c92034fe569d6d1531e42154524c996880dabb")), secret(23, privateKey: fromHex("3cfe86b6e6cbf601a46127057d55ac09eb199357817c8456ca77d5002f6b0595"))], pssSalt: fromHex("d3045823cfff2579bcc0cddd43673a51008af03017dcd118b74bd26b6887d9ad4d4766dd8af70cb849b182f90125660c542775ade9aedf037dbbde4490373072"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-openssl-hrr-p256", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c206b3b075296470fda9788a006b2faed26d4b69a8f3377e7677d6a47124dab4585130200000c002b00020304003300020017"), fromHex("140303000101"), fromHex("160303009b020000970303903c01fb298db46b30f4f8b7f75c719f75cd7fa051f547dc4ed6e9c623d48ed1206b3b075296470fda9788a006b2faed26d4b69a8f3377e7677d6a47124dab4585130200004f002b00020304003300450017004104a503818e70208cd7d22ff9d683cd5a3671fe0a131c2482c5fbaa694e6f0dfbd3819d4e0f4cefc7997a29ebf1b7cb0c534d916433234a909c1ba7b2236c32194f"), fromHex("170303004059897edcc420c05ce3e344c9f9a195de0e53b93c77e939d5140de34332f184c4df938c0cd1f30b1c860f07badd2fc1315bace35bdca2c93634512973cc4572b5"), fromHex("170303017bd188e6a7e71d2523370851e05c748b36f35fc2849c8cb171d4a1a9dee197da6193f081bdbf49924bd08240eceff2ce4b02f215e707cc6bb48295f5cb2de84cbcaa8d6e61c7367aaf64a853061f2005375826866b246fd04f2123bedd46e5e3acefd1f0569722a006ed9dfee2a685935dce8faa9822987f84b8b6c2428857b696f307c9add3aae5631929d6b709c3c1d0a880b47254e94ff63bf209e1eacd6c6b7238f00173c24ec47b6ff9fd588b08fdd1a65d280026084d6968e09e3dabbc18e1b44fdf825b9703eb12855cc3c07ba3c7561592b56356add83e47110d483a772db4e1b3fc8165c8ec9b587be0690660db450c1d06e1e2f0f463b426b9bb1316fe3a9b7204415da933434fc0ff73033601645506005d516514caf162e9112cfc596dfd2ea8641107e160cd145895e0337d5c94c31415ed66e2b6a1671705aa50f2fb2468181e62d96999059b4093924d96a22450be9a727bf925d7a5a903637b84b98079d56f6013507c24624438f106e9135dc88889ab52ecf02d"), fromHex("17030300604cbf98e1bfacd973ab715b96f8776192ad325ea4cb252da220c1e248fec1fd250278a89e23fa2fcc677afc7ea9b49613d444741c76639155e95746265e11031e26ac849abdb5106c60c472bb2618e7ace42b3789467b1d3121cb5a56edc8f1a3"), fromHex("170303004572b65753b88669aebba97ac7cda2c35b5553a49b8fef9f2fc5250907f99ab3dd4bb5eaf64eb876a9cf297da08b4be29100ce214c814ec2d9e8b78a954530e2c5c4ffce168c"), fromHex("170303010acc2eb2ff444fd7dbcb3ac150a9ac1db593d0ce51897a6629515773d4c7eede383b5b81023105ccd5ce6e6e524be6a204c0e5d32868d56d5ca7dfee4123bfd7835e711d3a16b06fa0edd109ff3bdee5d65cd2381c5fe66db5c30a18ce4c0c5c56b4fe0f048f4e37fcee273d49daacbcf8aa96da01876aa2ac5f3869e12e0e16fc561558b270fbbcedef1dfa09eb985d5d16a8e8071637d64ce214a08332daeb7cd3fae86f1a322311931f6afb75e1336bab88646cc2f0e087c98a01cca20104a94106e085a47148cc125bbb63634680d0cad114708c916f03b94f9dbbc1836b1e88d66f782986127bc69014a5c40e975b20725ec9445fc418b11f260a996ae0b9c5b094eff29074bf6f23"), fromHex("170303010ab36793807ea969585c833a3d221a65c2731b06ece9c318f96827acd233e01997f77d841689a31cc11454ae4186f871d7f3e404ba208c4a788db7c0c55c9e92d68e23b3903576b69e6630335f198213c0c5a1aea57830b63baa90641ef2ac42b5f427e6b256f46a83c5779c9f04f4deedfc2e865e7688117089d9f5ef64f424449a551cadd0cf438fba1d345486e33c9601f9bb37523aa18bb6f762729325a71cbda6f438148643f40c4d20749f05b34680ac8f8c58e9b86aeb05dd861dec5a23d47558ff9691efd9a3b7ba9ab6febe13659a946c6be2959b24bb29c01df8b695a4f045e8039524a53bce5fea30882e1a964414a903c7ec47f6c836838b4b04baac96f5aff784ee685f45"), fromHex("1703030023849b5b7e5b1132c850ac37f8cef938eb2ce4c0a52ce73353a696c4ca2aad4c53b63d9c")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("5f91619f82a10de8241a6b5be96d022edf1ce171ecd5f747c0ad953130df49d8"), sessionId: fromHex("a3e05a284045b3e96f244878b826c901a030109a220d41cb8f35a5f77b6173f5"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4f37cc47b42e51484cf41f9b311b4ab06a1259b89bd431ac4baeba207d383571")), secret(23, privateKey: fromHex("82557674627aa504404523eeaac7008ca24fe2cf8b1a77835df3e6cc9b19e54b"))], pssSalt: fromHex("d293f2526b4c8cb58cf8194d17bde939af64b25f7667f6d4491c6ea20c97c31c95fcee497d1bd503b3531713ca2acf7fbe80953f240b49a68eb8781fd6cc5d2b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-hello-only", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays.create(), writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("45700b05e591b46d4f3212deecbb226c57e2ac57077056bfee32ada2d0f52a12"), sessionId: fromHex("caad11b6938f1ab08e76025f0fb642e76dd9642f660c5196c7aa5d990ca1d371"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2e07858fb4dafd0728b1be06bdff4af79ca37ba1cc5358f34ec4733868767c27")), secret(23, privateKey: fromHex("9e9ce187b177f26e9486ae235820291e1f0b20971af5879c0bae75d7ecbbf33d"))], pssSalt: fromHex("ec965657d69e5b28a7683c48aaee5e3f579c66e82878f9918b9d15954e0c787a9a428bb09efe21d3ae7f05fd974363548ea7d54bef05eee2240398b3a41ff8c8"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-hello-retry-request", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c20caad11b6938f1ab08e76025f0fb642e76dd9642f660c5196c7aa5d990ca1d371130200000c003300020017002b00020304"), fromHex("140303000101"), fromHex("160303009b0200009703037ea1ea0ed98217b2d037e7616f518745facd9ce1a239dc1b19d0c6d119e89c7a20caad11b6938f1ab08e76025f0fb642e76dd9642f660c5196c7aa5d990ca1d371130200004f00330045001700410480a3e4f3e04ed91c80c47f563987537d2db4d6368305e7d4b71406615acc120cee033722f265345800c9af45921a9b4a771fc9b521edae0c65b14b77ea134a1d002b00020304"), fromHex("1703030218cdc826b76d0961ecf48d86560fb46ad94e3cb15cb1ba5fb1b0c1f47a92f562aeb22f9a3c25a200f038850f89b521de69f10dbdf00403b106b22dfddee44424bcb168bd1c7bfa8840748ed8850a7eabceb7d0f585c78e596f0707b6a0f5c52e707300a771cf6438bb0d246b78f613a88d81a5a44f8061c6af90ad872f3ccfebc2d1d3d61cbd26adf535cff7d2b8c9875d40489926f895f6f161b308eb1765a53d1f7acd20bbd36ddf6b70f28eb034bd6a2b3610fc72a70336bbbe0e5b2c9604d1401954654e926845ae3114fe9cf62607f2e34ffd3e70dfc4e76b096239aa1ad23f855ff3fc8b7280e96167af8982b58a3c44b4dfbcea6d32111fc4f72ac7f832aed0f946b61c2a0df2d7faf7ba41d7c51dc60db75925b20397ace882e71de66c39225eed46c852d1d928c13706bd3384bf8b2f80eafd9980fdf20b2196d12ec72d07fad36b492aad4a25b7d6e88eb2d59d7aead9e24db5315176acb2190798fce667926209c4e600ad03da0b0b095ee94daf56c8770eef9b319561bfeccf25c5822a51724058bcf008f6516dd8b8770546191f6a8862d5df856521700f0816748e68f70d3a0eb6c22d62d214342a0c21f92b228bc28918ef9c6fb6f2a6dd0187344d5e62876c8b501e83c5208f0c311082279e292c0cb449165662e71a9551be8b4bd0f60a0fc0f67fea5ee37d597500b3680227d8ab7d77f49ce7a4bcb9d7e00d37f57a16fdc7b8cbb56b7012c9bcde4ae672044181eeae"), fromHex("1703030039fc8d28b288ec7153ce2f8520fe37b08fc95ec4894733513d8900d91b813cdef248af688d1708ed5fc671b0e746d61a9f3c496377dfb7644039"), fromHex("17030300134c3bca41684bf7a2cc626612b0680bc4bb2d01")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-rfc8448-hello-retry-request {"state":"connected","version":772,"cipher_suite":4865,"group":23,"alpn_protocol_hex":null,"outbound_records_hex":["16030100b4010000b00303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010000810000000b0009000006736572766572ff01000100000a00080006001d00170018003300260024001d0020e8e8e3f3b93a25ed97a14a7dcacb8a272c6288e585c6484d05262fcad062ad1f002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001","1603030200010001fc0303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00080006001d001700180033004700450017004104a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002d00020101001c00024001001500af00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000","1703030035d74f1923c662fd34137c6f502f3dd2b93d951d1b3bc97e42afe23c31abea92fe91b474999e85e3b791ce252fe8c3e9f939a4120cb2","17030300132ea6cdf749196023e2b3a49491695536426047"],"received_application_data_hex":"","peer_closed":true,"peer_authenticated":false}
client13-rfc8448-compatibility-mode {"state":"connected","version":772,"cipher_suite":4865,"group":29,"alpn_protocol_hex":null,"outbound_records_hex":["16030100e0010000dc03034e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d0920a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef00061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d00208e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001","140303000101","170303003532d030e273773a8696c799981af6ced07f87482e81565e394e87c867f33df3d65b7506f1a626af91d4821d5f7a1f210ef8dd3c6d16","17030300130f629155382dba23c4e2c5f7f84e6f2ed3083d"],"received_application_data_hex":"","peer_closed":true,"peer_authenticated":false}
client13-openssl-hrr-p256 {"state":"connected","version":772,"cipher_suite":4866,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100b0010000ac03034d16a47657b0347673f9861ab883c0ac44a5c6c203aaaf52c93c8f10d44531cd206b3b075296470fda9788a006b2faed26d4b69a8f3377e7677d6a47124dab4585000213020100006100000010000e00000b6578616d706c652e636f6d00170000ff01000100000a000400020017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300020000002b00050403040303","140303000101","16030300f5010000f103034d16a47657b0347673f9861ab883c0ac44a5c6c203aaaf52c93c8f10d44531cd206b3b075296470fda9788a006b2faed26d4b69a8f3377e7677d6a47124dab458500021302010000a600000010000e00000b6578616d706c652e636f6d00170000ff01000100000a000400020017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e3100330047004500170041041013dd70303db667c088dc99a291da5f94e868222c2fca941107491ba2321aaa216c964a718eefe80df123f0526b6f541d1ebec9f98b6388b86c64d491fbdbcf002b00050403040303","1703030045cf87d60555bdd0ab12cfeec57a8432718cd9b6ac09d44f52da2247c52ddaf48e7a4df90fe2e5918390e9139044150c25e383a4b47e1c742f3b9703d0796fc8febbc11fef86","17030300365d4c813fc05b1a3e81dffbd6f6472e9bb2adbe8c756578dc83de4e727f0c67a7b59b92302923acd4d7689a92a109b57b3f6227ed45c7","1703030013fcdaa4636b26678d4ab31fb2c1e6fc2d7a8e83"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c","peer_closed":false,"peer_authenticated":true}
client13-hello-only {"state":"handshaking","version":null,"cipher_suite":null,"group":null,"alpn_protocol_hex":null,"outbound_records_hex":["16030100e6010000e203035f91619f82a10de8241a6b5be96d022edf1ce171ecd5f747c0ad953130df49d820a3e05a284045b3e96f244878b826c901a030109a220d41cb8f35a5f77b6173f50012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020947402d640f9c6676911941e996659502b203e6fbdabf95c491ce91ab63f803b002b00050403040303"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":false}
client13-hello-retry-request {"state":"connected","version":772,"cipher_suite":4866,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100d6010000d2030345700b05e591b46d4f3212deecbb226c57e2ac57077056bfee32ada2d0f52a1220caad11b6938f1ab08e76025f0fb642e76dd9642f660c5196c7aa5d990ca1d371000213020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002018b02427f04c0f2f4efc0649b6f40792d99b63c506af3a1ffec38131ec158417002b00050403040303","140303000101","16030300f7010000f3030345700b05e591b46d4f3212deecbb226c57e2ac57077056bfee32ada2d0f52a1220caad11b6938f1ab08e76025f0fb642e76dd9642f660c5196c7aa5d990ca1d37100021302010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104e5b563796841f57371fec5896f256794b262d888b86e9433e2eb34d4f777af2acc962fb302a744819b17585dc122824342099a919c677b936f09c7326d70717a002b00050403040303","1703030045172947724b1fdb9e7b247fed5a841fab335c5984328c71f9bc7863bae5a24c6d2bfa7b2eb708e37b5a9bfeaa5f2a15f2bef98ce2c112bf359c46d3fb2b089c9cb6f7a3a8fd","1703030036a671052cbc7ae3256b4c692026482fd33462699b069bedad72e475447974823b93b0f495003369b645ba9ea68d2d654eea3774c4fce9","1703030013a161ca916524d5a1b5a77906db7a1c3c1ed152"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake-negotiation.negotiated-handshakes -->
<!-- tier: stdlib/helpers/tls -->
Handshakes that negotiate: a server that accepts the P-256 share without a HelloRetryRequest, a server that selects no ALPN protocol, a KeyUpdate that asks the client to update its own keys, and a server flight split over many records.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [23], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("be68698fcc1012f2a5ff34e54eed748868dca7978fc92599044dfa215c120693"), sessionId: fromHex("10828af478b15d27bb7c7f9c08d959b078c9142930c0c9a857b1fa785e54fce7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("530045f8ff48e8221f8c59b0db67a29f29ab3563626e3dbd0e64c3c052098ab2")), secret(23, privateKey: fromHex("4c1ac6e958edfd7f896a3246808d0ac7e08c82036c6d038f464c7b5ff1392ce7"))], pssSalt: fromHex("7047eafcc3a78bc74023086e7fa6532ab5d5844385c9174f0ecf11710aa4b966e241eca0aec0da27ca1b00043f0e57db46a52f2deeb51d05e56a635d972ed9a3"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-aes128-p256-share", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303009b0200009703030b78a51e8ff706bf3039a76f84b50fb01596c8f23b9d2d0b3d64d20518157a222010828af478b15d27bb7c7f9c08d959b078c9142930c0c9a857b1fa785e54fce7130100004f0033004500170041049697a1072deb23ba85b93cdb84fe2c8df02a1417cb5771464c55010cbccad396be1b6da962728f98a55589652a897cb1ad17805a5a79d5be3db44fb4abdfc223002b00020304"), fromHex("140303000101"), fromHex("1703030208fa2c7fe5c8d6ce5dfa4867eb366a05b2f1ce1c697588e230617d67d9bf49d71014a18e009b70d992f02b2c8b0a3917a1b1d06ca1f1911486d19ad6c6f9b711c6e9baa50ec43c4d4ef8330692a413854f2004ded682835ec428a93f3155a1aaf30b3d602efe9517a6bf1592e7e31ea220aa7c8b4a3233918df9b828a4a2f968eee1d26e6fde30a4184931e1d3b1bf6986a24841d0ad50cc0199c921e0dab1d3d5a338e15f0b77b44592612ffcdf742a1cd8d0c4c6be08663224c2fdacf34fe975c303b9af1ee839469a3a0339f72c37261c7aed27e0957411d53bf1f041feac628662464f374ed677145867d64e9a5fd320c034a9ced9e72088b5832dc38b13ebe7072eea7964e8f7cd8008c87753f6a9a271db4948b30348d9e0bd1eea7432ceb9cf4b4ac32e894e13c7bab9644f30e281e0b61af529456419d60572dd70be1dc6bba418abd2cd9cd3108d9aaaaadad93399117cd1d2c5a6d46fbaa4eb52d32fc4e157bbd83deeb3796a2c397e00960b10e5fe19c1df2382ffba62aafa21fe267fc7f79fe0540e641482f866850fac14c33db4baa08a9be2b32d2008c1cbc79553e02e28dc491f24b318d5228f04c8ed46574dc37ed276e917ec8d6f89a8fad31823168d7f81c799aeeaf945ae3a77609ae09b43ffab665ffd1ec612765f8daa8cae49c26c7796c2e8d26fbedab86a76343a96cf5890f4746030b0c4a4477151bcc7771bca1d88a4"), fromHex("1703030039f1e28fb1e966d6717c53750929578137c51ee65b707e7859b6fd8ebff4a5997df06e0528891542549df8745465ac460e7cad9cc5ed737e8f93"), fromHex("17030300139ab431aa6324a68df481fcc97392039cb8cc86")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray.create(), identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("db54227cc81e200975651454fc198a9f4a7a526a4d8654c49b94a470c8deaf27"), sessionId: fromHex("93e36879dd3ee83fb1905fe9283f3c62a2578549fe232b261e8a1b957f422336"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d5a30835c17340a028a726ee0838c2dbd00104839bd73a4cbba36e2d996fff76")), secret(23, privateKey: fromHex("19349f13105b2ec2cf2de1704f754a7c13e30692526ddf1532447f7bd91fb1e1"))], pssSalt: fromHex("fe2d5dbec708832bc4ba8ed8255310782a8a8bf414052bea4ffc01cca493c7810eab6e3b50e5281c983d3840368c887a6bba1fbbe37515c3c4b527b8d5b9a220"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-no-alpn", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603030c57ac74403e3171ccf2b0479806a0435379a961fe5d188b38eaf701e2da49be2093e36879dd3ee83fb1905fe9283f3c62a2578549fe232b261e8a1b957f422336130300002e00330024001d0020603ed9b51ed2be58ff89ecf81a396ba1f0f25c298db41c821e98de1978f49b6e002b00020304"), fromHex("140303000101"), fromHex("17030301f97bff835a162d283daa44f3b9487a3c28113a2f363b950da1d260558b2444077560b5e9f081ca7ef0a726c31dc7bf00c58e54eae0cc0dd719fb0c184c5b7ad6cae4682d38c1a5740670db4998e7caa028fd02756e7be05ed9d4c81b814d823bda2355d835d62208c1dd5d9d92cdb1a340a594544ee7f5445c7d8635116faea59e62b1499e182edeeb65c28a86c442805326b8ed4101e460c0b4bb2d52fa7fadbaf45304862b06ab44b46777973f493c36324a463232475f7158c587fb479ee2ccc3b257f3a35b5c608156816c6b5adaaa773d860852d79647b890e10ca229f907ab133cb654ca34d2ecb89d30b9ef71d3cf7a187ad371421ffa7095411d03855c4d645c9bb504c098252f67afa27f03e587b002daac93054632ca49d1c4a8ea1af3e7eda19282b166473443f483059f543d944892d5306abd24ae92cef84424b0b8b0b5c34d2d06f672472d08fbab21033b47d8f4ec50bbef2c72dca71e8c0444e37d39d63c175daaf40964094add823159bbe9e51cfbac21304efc10e6a681feb7a3143cfb4bd98f1438f327a9e4ad4754ee9aa8c677968c9b700cf653be1449fe1442f45cb9f968929afefaab5742725af5162244b788313268eaad977aff5fb88ce9e777815045c55ab512fe6d94ebc02265d161574977edd9f191274aece8c0607b75f33b5db0bb05856e56c7ddf5facd13c292dddf4f79"), fromHex("1703030039b94514e83a4272ec26bb1759ab1e0a4503b0f01b92771f5990b72200b11b9c7edd388d152a127585675e26146082d809c3c23ecbcedd773029"), fromHex("170303001305eb3a04b6d9d1f5849f0adb22599a1c980d6f")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("29959068e7ca0bc5dfa7be42529f9023de5fc5d90b2ffbb06e2e66e8155a885c"), sessionId: fromHex("a3f9a418e0eddbbfa9500ab84c7a550d3d76e17385f96fe22fb2465d26cb4da4"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6d8f3baf81c7484ea3e1c48ece8f3933c30c90ff09b14e1cecdd01489533d3e9")), secret(23, privateKey: fromHex("fcac5636f773b66d7148c549a282eae780e1ea40fd3e560c26eccee572c2dc0d"))], pssSalt: fromHex("ecb55376a65cc91923172b5734060e8a5ed62a28014512169dcd8ba95722f76c9c5d188da4c09e55b1e89a57e9495eb991cc86bb84510908214774a82ab96653"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-key-update", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030368f0535f581f5213c4d8766cbc64c4536de9b2ce80ea3ffb30c67cb2c2d695d720a3f9a418e0eddbbfa9500ab84c7a550d3d76e17385f96fe22fb2465d26cb4da4130300002e00330024001d0020b90c9770e8a1b9a0b8b2d12a29c8839fa8ac08419b426c35dbf506082935c048002b00020304"), fromHex("140303000101"), fromHex("17030302073e383bf39c0a0c52ff6f116d2bc809a3b1fe82de4fca5b60ae69434f5afa489e50c5bea6c10ea942405779ec229ffc6aff16c29cb712c6fd35d4ba162b6558b0b9b509191f25ff04aee2d486093bb8b70f6ef4953dd959a8de86ae3237fbaa84ec5df095bd605c79b7fdc49b7efcd50a6da00862d33b90dd345774f9aa80efd05a859e50ff9c76a5efc19fc9e7616cc9c2eb63002a440f0cb1b7f3791d375101fd4289eb7d9db544fb6a93f989b9a05010731137c298323ee404a0731c3d1f054203bd649630c2f8400b4461b711a5f2f5d179d7b808ab0bf5c027a98d2e2a011878b39bc73b478470f4e467c381abd3b425c8ef9368ea8c9203c7a14528fc643c366c0ab202c88d75cc300d7471cef7a2172caab185d3fbc2e86ce42a047af2c37a766a74bdafe4ec7ae7c1f3515f27627ac5ec5ecfcd807ca5e8a94a3d8ece11ca58d9eb4a7aa8b54c70c8882b78671a44b27490fd14a245aad24f0e79f24e7fdd0f085fadfad400f32da2d5cb923ea537c22d011577f47f01f530261237319a8622922b43439a0df48fe10101a4d44a77625a76dca7668911e0de60199e4c06defed4333dea6579b8fe63cbd07abbcbf8c029da0e783ba67ff27bf1ed763ca1a6be24380e3392566af3e94817ffc232a1657126e925125ff80c54eb9b444a8742059c0ed109bf93d68a96bc5c7b32d71661f1bf9b554ef837bd37e27377ad2c5ee8e8e0cd26"), fromHex("1703030016ea88ed170b7cf49d5afc2cd90bf3e2013179ba46086d"), fromHex("1703030039fa9b774d8aa77eeb1bc0d9114ce137317ab3f1fdf7be73d4500c826045d2b6d8c8285643ad1b19f71823c952c7da10b3dcee65e16c78c5adc6"), fromHex("1703030013250d55c2682e9e16f452556f07534910916235")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("968732e4a8e7081eb792064d03e2adc669066aed25b858cb853539fd2b700411"), sessionId: fromHex("a2b23253d0763e27067f1dd1f88ece25f8ada063eb3835c84bd5a75b1bbf0989"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6b9a126c8d2c0d6f9af7e07ee6c2b764a2535579ef1256789fa008cbe7a4b783")), secret(23, privateKey: fromHex("7c848afc48273792c8848fe1bfe4238951458707b76fd27b869ded4229ea16d0"))], pssSalt: fromHex("bf249850c19a59d9ef737e2a2edb0ccc1c627a5e1b36b0a8c42f1057bddf209fb6ae2871b0eb89bba19a4b271454041051d9034499eb8f7140488f49b8536a01"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-fragmented-flight", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303006402000076030383083a70f53e9352b9c9718b5df4b8b2a2c0758f79b4e51ebccda4dd731267b920a2b23253d0763e27067f1dd1f88ece25f8ada063eb3835c84bd5a75b1bbf0989130300002e00330024001d002064c8dd3339fe985218464111b9433cf9"), fromHex("1603030016fc315dc450535775da3a6c8828733069002b00020304"), fromHex("140303000101"), fromHex("1703030075a53c918a08cdc90397c3adea0775e9bbea3ca7fba58b2de5e2fc1976cfdf5e02c65681278655e894a04528a277e6fcac44779b436c1fffeda045be87d216558a1797fadfbef18fc3e135108ffbb8e95e7e052e748df1eeedd28c43707976b7f592d843b65ab9117ae129065a00b5de1649ee4bf30f"), fromHex("17030300750ed3df12dc087ce005cc34e793a9865e0c46aad1f0b2e367928d13072df3ccbe3b55b3f0228d4a67a1919b7e141844794eef7739503b10f326b1d0758e6c80fd802e7ce246175d30c3282466a6d21bb6c54feb32150949b47090dff81e84a88f2da5b241dad8bd8c4ab120241c3d4237f05f4b9519"), fromHex("1703030075ed6b7192c90d55ecdd112552241543ca784b14c7a577ec9a2ffb81bc49543fbe00247b9734e735365975365c99e488d1d9938fba9a5338c0bccf11d39b5d7bc764bb3fda537d37282f891ae2420aba7f95e0a0d1fff9829f94283c5b8b21087e7adcf958f11345d54c509f30408aeaec426d25fe31"), fromHex("1703030075b5ab408a739c9f0fbd4a86e48812b735a05f68244f5797e585e23ec94e800e7cdb9104dd1da29653a54fa87a4b9970831e1d2023819a5e409b807d152ff6d7236e8e4ca268838280831da48eca070699169f12dc77ddb0dae15fabaf07f34273f05bc8a813a5a7fe90a2710bfa5c49f2f9d183faa1"), fromHex("1703030075bfc54e52ec07cbe5c86645052e876910840e4a5277994eb15f449a7916453315f596b910c1fa56848734eae1689e85d722667ed59ee30a042704293551ebae3de25317846d93fbeec9f0a2867dba6a2cfb9d9409e5ed969f1d7e4b8e4e8e3ab86418f2fdc41c0412d52506c437be4feadce9d91fdc"), fromHex("1703030013f1c6c254da4ad9893645dea60cf1ed6099655e"), fromHex("17030300390d48e1fca6e90ab2c23d70602aa65277691a2f95ad54d43109efac71ce912f619daebd86f1d6988bb6afc258037b18ed2ec9fec1ab90e1ec0c"), fromHex("1703030013a01558f10f0430b21a72abeec6f97a591197bd")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-aes128-p256-share {"state":"connected","version":772,"cipher_suite":4865,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100f7010000f30303be68698fcc1012f2a5ff34e54eed748868dca7978fc92599044dfa215c1206932010828af478b15d27bb7c7f9c08d959b078c9142930c0c9a857b1fa785e54fce700021301010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104fb675b2babc692ed1c1e2f1100825dbd7736b358e823287e36af618c48943b9939330d4a226a5965df0d2f38530341dea02a672a144066a5a4bb67dabff7431a002b00050403040303","140303000101","17030300359f6206cce98e4a86a6d1a5e27d600ca30512b1b79afe487d7f2aee496e942ede41dad963069be812fd2711ef53713a57fa788a23de","17030300366106b7dbb3e8590c8fd3e2b949ef4b751b6ad81fb1001c36875630206b6d4aa91d6e85382774b971ba9558f5a2f30984e876a32cb6c0","1703030013d7bbfe2da5c009cf20f5d85a40de50231afa2f"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-no-alpn {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":null,"outbound_records_hex":["16030100d7010000d30303db54227cc81e200975651454fc198a9f4a7a526a4d8654c49b94a470c8deaf272093e36879dd3ee83fb1905fe9283f3c62a2578549fe232b261e8a1b957f4223360012130313011302cca9cca8c02bc02fc02cc0300100007800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00140012040308040807050308050806040105010601003300260024001d002047d0cc3e95a2556048c23e6105d9d05e6a3b3178feeebf15d71d96fbaf56804f002b00050403040303","140303000101","170303003520adfd8cd66af5a04cc6159e89454e51a1f7a5bd68a96db03d3c8c0b6634dde727948c59388a19b047bdecb1bf26f26b1bfdc66cd7","17030300365282facb3d227389520e0986c0916a4016960a7bde652fdfec9755405e3d57e395b09bfb0bcd9b2d7b11bc96debbfc1a70507016262c","1703030013e5971416038c5e6c1f7f9228ec1d041d2928b9"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-key-update {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e2030329959068e7ca0bc5dfa7be42529f9023de5fc5d90b2ffbb06e2e66e8155a885c20a3f9a418e0eddbbfa9500ab84c7a550d3d76e17385f96fe22fb2465d26cb4da40012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020113693492ac1a297c9f110449506eb495412c3ec68e1642b354ed03f2849c948002b00050403040303","140303000101","170303003574006648c9a6d43c9074eb2479b33f49ea3f1394d283f4738141e61bb3d0ccbf5a664166dd220ca678e8437738eb6f694edd399f32","17030300367e9738ed48c2fffaea87634811cc91efff203da624e18fa819a5339a0725e9103180475f56ded7d215290e40664d4fc5168881a3b03d","170303001620755d97e3e679ba4481efdda10bf6182af2fa8ad59d","1703030013045733101b1975a1861f60552d06c771f6f52d"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-fragmented-flight {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303968732e4a8e7081eb792064d03e2adc669066aed25b858cb853539fd2b70041120a2b23253d0763e27067f1dd1f88ece25f8ada063eb3835c84bd5a75b1bbf09890012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f2d5d8d06ffd4d858850b6e655e6bb9667c1b6f32d3d35511f3668e7a06d8222002b00050403040303","140303000101","17030300352cba6bfe8c934fc2f3af0d250c42656b073bf9aca76b0fea3d4b729e12f6017199975b5fff3701c140e866d84c1b6df320e094ff57","17030300368173a671cb7b6bdb1e66ce9de4b8a0d0df54b3fce982d16084e5c501250ebc2978fb1781f04942b6cf99d0c55be8f6f140c60a8acb6c","17030300138806a637e0c3937f5855fa85fbc12a24653e95"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake-negotiation.server-hello-errors -->
<!-- tier: stdlib/helpers/tls -->
A ServerHello the client refuses, each with the alert the client sends: a suite it did not offer, a wrong session id echo, a wrong legacy_version, a selected version of TLS 1.2, a missing or unsent key_share, an invalid X25519 share, an extension it did not offer, a compression method, and a version it does not support.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c53166cf3eda3d342f55acc778e906c58a4e4d748d659cd0104a004ae596e8b7"), sessionId: fromHex("3db01516a2e3a4b7038058e017382eaeba1c597031076ebf6dcc519232f4d040"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7b2e3761a9c5c59c4e607ce6d5d89cb4496a453eb5567b4ff943dd43470b9e5d")), secret(23, privateKey: fromHex("c2f7a41cca097096fb0e401c507bbba4392d96f3ab88d439122d1ab44fcd6fc8"))], pssSalt: fromHex("9d352fdec1789a5caeb8bd7c917ef82b96bbaeac545f66a194a8102ee58563cefc8db5ad282e88a76b3cad082ed3560c7df4e2197771dc90ea3fbb53031a538f"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-suite-not-offered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603036eb07f57c7455ad08fcb0b2bcf46458c6f9f429bda5d0a0a5f62cb5dc9ce96f7203db01516a2e3a4b7038058e017382eaeba1c597031076ebf6dcc519232f4d040130200002e00330024001d00207985f3ff69186937cb173676f8caf8d931616c751e5ec24076beb4eef2407d68002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("453fc8f8048aa6511d20b5d07e239c374c6bcddafcc96897c2bc044c3719264a"), sessionId: fromHex("d01f9fb603d5b34fa9d63cf19a7ac0dbc7f20dc3d5665a4d347a92eb8c962e95"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0572f5568b8b6d8dc1287c6d14e2aea5b2285e00873d3f5cb2dddc62f835d499")), secret(23, privateKey: fromHex("9d3a81aca666faa5f501320ad4d1c34d312fe1af1629a556d395cb8f78e4ec99"))], pssSalt: fromHex("81c7b91f7bd3792b24cadef4e790d345aa78757a900eb9fc66434ae278b01ca65e203b8fb06cc893f57f665c6e9a9f948cb121d7957434b4606ba9d2833e3992"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-session-id-echo", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303005a020000560303b81d62c2cde8998490719b16071bfb698225b589ebfa286ef71f88ee30160a1300130300002e00330024001d00202eb63d7b26737f43b78fe107e850db0bf18faf742da3d7438f852416755d8811002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("93fe5b20992fc43551ac92166566c19f2b2a5f7cd8a0b6caef7d8442555ef241"), sessionId: fromHex("2c57318b008ba75bf63d1dc7780cdf424723ec496277378a9c0179e9bc3da960"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("624b59181e175afc82efeab0838205e47478d49f1ac2c05ca93c2a411ef0df44")), secret(23, privateKey: fromHex("f44560de46729fbece2761a8f1c9703b919577022a995fc1ca069e2ceebcb2cd"))], pssSalt: fromHex("cddb2f3005c4b5386138d45b3412b5232a06b20d2c9afccf82ff38d540158487143cca20d23417e0f7099c5334418494699f75382b53a6297ee21cbbb51f9153"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-legacy-version", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030453529ce51fa5ea861bc70f17700244483ddab68998d41f2a8bed780fa087e3d7202c57318b008ba75bf63d1dc7780cdf424723ec496277378a9c0179e9bc3da960130300002e00330024001d00206fecdc089fc25c5f5d2b01aaef9232adb28be519fc6b6057ab5b91b0c85f0130002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("e2f4f8e0a8a0e8a9490b2307609cdbe7842c2cb44437e1a79b0ef051fccb3824"), sessionId: fromHex("897d460bd8452060ea3b1614c2f80d7eb1e683ffb2abce3169dfe2aa4d2742d1"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("07f4da12f46aea9451fecf201bddfd76c516d0e77fb449b73c47985a8a62a927")), secret(23, privateKey: fromHex("4f8cdc2c9947eba90e0100964888ca9d55128a961d585af241675fddab436080"))], pssSalt: fromHex("dfe9a3f60a77642ce3fdc61d370e45b9597b9ec9096c8a1125c0f4ea0cb21ede30f863f9e2d91ff322e8b7dac04fd50da050b48ec0bc8ba67e55c082e2a1eed5"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-selected-version-tls12", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303a8870751b3fad13d8eaa7ca568ac904b8e3ea1e2dbec4a1c859e3c40f1997c4c20897d460bd8452060ea3b1614c2f80d7eb1e683ffb2abce3169dfe2aa4d2742d1130300002e00330024001d00209201a199d7fd3313001d16edff9108ef1aec37885b4fa705f4b5c1801144c72f002b00020303")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("27f33f8d0e92d7ef3773aed9db050df1497bdd65fbdb8db15725df02eb44c009"), sessionId: fromHex("9678d9f40255ac99d17eb611fcfb8b4ae274af1cbf6262daf524ca194fd78b69"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4cae3e67ec3b5fbe1be07cda85294b2b1c533c970ab8520277a8904d7730f25b")), secret(23, privateKey: fromHex("3cb475b08444c034c9290c268a3247097235eeeff969065f9f75204ef91c6099"))], pssSalt: fromHex("311e4ffc2e1eeb13ebdec101513a3ce3bf0f4d18b78bcbb1de76c35dcd332297685b51b824cca34eb26c41c988e09d7020430cc15be3b63dcd49c7a66136139b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-missing-key-share", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300520200004e0303ec3fa1615fdafda6b80089a5ea9e76c3665c812215ca62daab2eea168d37afbf209678d9f40255ac99d17eb611fcfb8b4ae274af1cbf6262daf524ca194fd78b691303000006002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("8bacb1a80e3e0735f06092dc8d31dfcaa91a55b3339be1074a4a6fd3e04d47c2"), sessionId: fromHex("33fce82635a57bf8e1fa9b8f41844cd3723f8092138d2691233d45b5a6d2f754"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4413196d9b5f20a7628b5abff5b102364ca9ccf2ee7aee24796e2dde27f62e6c")), secret(23, privateKey: fromHex("cbeb1dfb3b340653c187edd6bd6b299692a85f9537e88c7181f0957172e9be34"))], pssSalt: fromHex("f577eb235bf9ecf9af07a956e2aa9856e8de17a93da765b233b19a368a96edf59527615b11dfa8481e55ea6c821210eef85ed22ec0b6d8853c6d4337cb1a8ca7"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-key-share-group-not-sent", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303009b0200009703036796f032071ef90a695ba86768d9d23b68a76ed06e167668d130278c03e009d72033fce82635a57bf8e1fa9b8f41844cd3723f8092138d2691233d45b5a6d2f754130300004f0033004500170041040896564819cf001fd947a7d963b280c94987831cb82f34f97617ae4073e2fc8c403dad5c593dd3ac2435ce074604873062e97b721b9a6da3b8906cda3a8043c2002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("4731045a9f4541cab566d40fc3514267ab6499e09b8ae74db11fb6da7e40900b"), sessionId: fromHex("4a8d2b3b51fadbc849e89a2b7aad6f72bb017a1c9c73eab60570e6dd06bf786c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8ba400929f66199288127df108cf114348b09f3f3dfe06c4f8e396aa65f114a6")), secret(23, privateKey: fromHex("eccb996d5445e01b49e23814afad4c7639297910ddc6227e4da5d902308226fb"))], pssSalt: fromHex("3dd24c957ff9ecb330142308b5496baf58e2cd431948a05abc0a1c1b5cefdb084aa8864876481740fc94b017e8845931410d0e5c7f99f510c4c1c0631b624fea"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-x25519-share-length", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030079020000750303e837b2a2d5603c17fe1cde5626087f91c9d01f9671892c307d5c9ec1ed25debc204a8d2b3b51fadbc849e89a2b7aad6f72bb017a1c9c73eab60570e6dd06bf786c130300002d00330023001d001f09090909090909090909090909090909090909090909090909090909090909002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector6'

function vector7()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("3aae951bf9e52c6e4362467c5eb6093a0219571cf284df6c71708c6e060ae979"), sessionId: fromHex("5f54a616d18e7b6048ac2216327088eed14893008b5865ce4ce6055f997f4da0"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("ebfae49573cdfe52acbc78b7bddf98872982ef411f0c5b39190a195e166005c9")), secret(23, privateKey: fromHex("42b04115b1f06a100f08a2310c5ab6196d8c2cb5540cd7a6dc84192bf4180f78"))], pssSalt: fromHex("91d0955df9742822ad1dc2118be134882441dc5e9f54fdd31935033211951571df578a09e2317a190a1936f5c59ef62d1d0adc4eca5c72014837b778572ba8b3"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-x25519-all-zero", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303b5f85c06f82a4ed348c8913d3be85ad69ea152426e04f2a06fbe606aa608ad0f205f54a616d18e7b6048ac2216327088eed14893008b5865ce4ce6055f997f4da0130300002e00330024001d00200000000000000000000000000000000000000000000000000000000000000000002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector7'

function vector8()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("095e94a91e5f8b971c001ad17fe0bf6d7640e02f92a3983f4ee6183600c5d453"), sessionId: fromHex("775a01959913f7266cfd7e5b31b196eeba515fea3a7c748d4cd8222c1f19ae0a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("98278c5d2f8ccdd50a0c52fc424691a0d44d03f6d062ed57d81f21fdd3810884")), secret(23, privateKey: fromHex("258d1f2fc15ee6b6c77ea5c02aa83922b66616d81ffc7a2e2b7b292d277ebb05"))], pssSalt: fromHex("c6191e6bb1de5596608800c984bd8795e4c3b9fd0990cf9a5554d150f2148bba55bd91fdf0bd441e6311f21f748c4d02fc414de5a2fa69c3302d373746db4e01"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-unoffered-extension", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300800200007c0303dcd7d60ba1f3dfe0a1356f49596638d772ce2e4c4375c78d48e38468d91b4e4a20775a01959913f7266cfd7e5b31b196eeba515fea3a7c748d4cd8222c1f19ae0a130300003400330024001d00204f85860614c5c735e1bb2c003ee999fd25dba5e5da6627463010d6f38b53fd53002b00020304002900020000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector8'

function vector9()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("17b2823c526cd8e13d8d9ee51f381cef56572ed49f7a9df9ef6728c1490c1642"), sessionId: fromHex("6d5e3f85f324d7bfc53f49791fc01f23c96ca7a4a9e24059a7f712575b31798c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("88d59b20135f8fcb81f44d7da359aaac0999cde06260aade89e8e3ab722dbe0e")), secret(23, privateKey: fromHex("ff913a1979d56cd3aa01e9a74c438c7667472a9bddc3cd098e6950c12b3f4e95"))], pssSalt: fromHex("50e04ff4c4fcb8cd303ff27a522ef0095c5d00b0cc2675bb8a3047ec432d8dd682aa3579d1992d0c1dc29b6fe96aef831fd33e4c4ee22e4b3f4119af4414eec8"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-compression", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303a823ccb5bb4a5661e8d97633558b783c7a58dce10c1e55211217ee8009801414206d5e3f85f324d7bfc53f49791fc01f23c96ca7a4a9e24059a7f712575b31798c130301002e00330024001d0020a547eedccdeb5e84d3c9e61bb82dedd5c08687ba975d30695c49d16ec128437f002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector9'

function vector10()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("b4d5ad54967dfa10da71982ef230b4e068956a2c1970b3fdf957ac4168d23dd3"), sessionId: fromHex("8ad6ae178848cb0e500c64681dfa587c9157a1160f2530c8b076e84701c93c4f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4cb9ad97cbf9d581e8228e6a1d767af29c637178e54288ee0f336092f731172b")), secret(23, privateKey: fromHex("1dc5dbce24729411d6bcae4969318b99beba4a5a7d1f11972c359513cc6388cf"))], pssSalt: fromHex("548359cc3d62a25eb4d3590adc91a1c268ccd4936a5680722616389a91d52e017c50fd30c7b8da86d7aef29a0a5c02ffb5d8fe5b0133203134bf7adba28456b7"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-protocol-version", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300740200007003021a1098dccfdf9cc7162008ad5e6e4cb7f7fedbc72a5b502c4a659dbf7106ce6a208ad6ae178848cb0e500c64681dfa587c9157a1160f2530c8b076e84701c93c4f130300002800330024001d00209def3ddebe9adbe457ba044926e2d9a53306626ec91b9ed21c69025b6d4c9842")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector10'

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
	return 0
end 'main'
```
```exitcode
0
```
```stdout
client13-err-suite-not-offered illegalParameter
client13-err-session-id-echo illegalParameter
client13-err-legacy-version illegalParameter
client13-err-selected-version-tls12 illegalParameter
client13-err-missing-key-share missingExtension
client13-err-key-share-group-not-sent illegalParameter
client13-err-x25519-share-length illegalParameter
client13-err-x25519-all-zero illegalParameter
client13-err-unoffered-extension unsupportedExtension
client13-err-compression illegalParameter
client13-err-protocol-version protocolVersion
```

<!-- test: tls-handshake-negotiation.hello-retry-request-errors -->
<!-- tier: stdlib/helpers/tls -->
A HelloRetryRequest the client refuses: a second HelloRetryRequest, a group it already sent a share for, a request that changes nothing, a group it did not offer, and a cipher suite the second ServerHello changes.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c112771a3bc22c60748d3c9cdebda4eef4a4949677cb153ef2264081ccde67c5"), sessionId: fromHex("11df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78544ce889128b7e3490ce6932801193b5bf6ee5f9b1d7be1a14ac20e05b198f")), secret(23, privateKey: fromHex("940a6f9a95629e0ba16897601c7ea4dd43e085adef2b7a4f4b88f28ef3dc800e"))], pssSalt: fromHex("09f4ce8e96f612f89ec3d42ec2fcf485650211deb43157e75c2d295dad80b06bea9b322c1c94fcad56beb6f842d00aeea5a838201a9a7ffd73cb370e28ef0347"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-second-hello-retry-request", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c2011df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10130100000c003300020017002b00020304"), fromHex("140303000101"), fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c2011df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10130100000c003300020017002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c112771a3bc22c60748d3c9cdebda4eef4a4949677cb153ef2264081ccde67c5"), sessionId: fromHex("11df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78544ce889128b7e3490ce6932801193b5bf6ee5f9b1d7be1a14ac20e05b198f")), secret(23, privateKey: fromHex("940a6f9a95629e0ba16897601c7ea4dd43e085adef2b7a4f4b88f28ef3dc800e"))], pssSalt: fromHex("09f4ce8e96f612f89ec3d42ec2fcf485650211deb43157e75c2d295dad80b06bea9b322c1c94fcad56beb6f842d00aeea5a838201a9a7ffd73cb370e28ef0347"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-hello-retry-request-shared-group", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c2011df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10130100000c00330002001d002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c112771a3bc22c60748d3c9cdebda4eef4a4949677cb153ef2264081ccde67c5"), sessionId: fromHex("11df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78544ce889128b7e3490ce6932801193b5bf6ee5f9b1d7be1a14ac20e05b198f")), secret(23, privateKey: fromHex("940a6f9a95629e0ba16897601c7ea4dd43e085adef2b7a4f4b88f28ef3dc800e"))], pssSalt: fromHex("09f4ce8e96f612f89ec3d42ec2fcf485650211deb43157e75c2d295dad80b06bea9b322c1c94fcad56beb6f842d00aeea5a838201a9a7ffd73cb370e28ef0347"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-hello-retry-request-no-change", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300520200004e0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c2011df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf101301000006002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c112771a3bc22c60748d3c9cdebda4eef4a4949677cb153ef2264081ccde67c5"), sessionId: fromHex("11df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("78544ce889128b7e3490ce6932801193b5bf6ee5f9b1d7be1a14ac20e05b198f")), secret(23, privateKey: fromHex("940a6f9a95629e0ba16897601c7ea4dd43e085adef2b7a4f4b88f28ef3dc800e"))], pssSalt: fromHex("09f4ce8e96f612f89ec3d42ec2fcf485650211deb43157e75c2d295dad80b06bea9b322c1c94fcad56beb6f842d00aeea5a838201a9a7ffd73cb370e28ef0347"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-hello-retry-request-unknown-group", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c2011df5cede7b7d6498be5e817b45d24f8b6e2ffeb0f65476f3a6f127ead46cf10130100000c003300020018002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4865, 4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("a57002bc6f10f3ec5a2fc6b953f73a80ce79a33aa1644d09747fd44b53f8850b"), sessionId: fromHex("e145af0a1425d2215edde839af353f55659b5abe92241af505b7d12129b8bef7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a87276ba080d7645d41e04c5791da8669192fe50e03cfac563c65e4b54f7bead")), secret(23, privateKey: fromHex("4d66f75d1f025a1ce3d0f1c5b0f124f06ba70e1dae2d1c1c9548568190db4e26"))], pssSalt: fromHex("c6e544d31cc118bbeef579d271d7e996ecaba719015c8f16c5afe050edf9b127cb9d5c500f6cf4d04b71b5470f20dd505afbfe3c8d065d434cef5cb349797280"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-hello-retry-request-suite-change", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030058020000540303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c20e145af0a1425d2215edde839af353f55659b5abe92241af505b7d12129b8bef7130100000c003300020017002b00020304"), fromHex("140303000101"), fromHex("160303009b02000097030314727d840bba62570fc3b11add5db13f8bdcb29bb0cc6f3bf0eab2f878b77a3320e145af0a1425d2215edde839af353f55659b5abe92241af505b7d12129b8bef7130200004f003300450017004104360f979c1e684a27a74a8a3c465291325b227aff2ae512ea1f1597b1270c5baef1dd1270c14c26c17089f8d4af66319923743abc4072c46e864eb032b2456925002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-second-hello-retry-request unexpectedMessage
client13-err-hello-retry-request-shared-group illegalParameter
client13-err-hello-retry-request-no-change illegalParameter
client13-err-hello-retry-request-unknown-group illegalParameter
client13-err-hello-retry-request-suite-change illegalParameter
```

<!-- test: tls-handshake-negotiation.encrypted-extensions-errors -->
<!-- tier: stdlib/helpers/tls -->
An EncryptedExtensions the client refuses: an ALPN protocol it did not offer, an extension it did not offer, and an extension EncryptedExtensions may not carry.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("3faf5a56249046c39eb334c02d78e17b8ab98c820e8df6f10eec4c16fba56c3a"), sessionId: fromHex("c97c2c4df61a2fb1848d2d3d4717615d25e7fcaf842df35b9a58be8d369e1577"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("608aff2d4816f505342143a088ab72c213f3551f3057d4539eca1d6fecec8700")), secret(23, privateKey: fromHex("0463e36520a61b2299dcb3e2a8cfe08149cbd67d916fbbfa46c858e88db37c7b"))], pssSalt: fromHex("f13c6f952bcd011d4115e5a6b4698690e084872a999e8768872e3a97ba9748c510434dd4487c1f7eb083e0ba8e5c14a6c9cf23fea5d2d3e52897ef6be06a349b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-alpn-not-offered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603036d1a3761673070352200fe590bf1cf6cf480974c3ec396c2025a4597bde9674320c97c2c4df61a2fb1848d2d3d4717615d25e7fcaf842df35b9a58be8d369e1577130300002e00330024001d0020be031a82d398d3ca69f85db4c24bb499a3854b05c34b088411508dad6895d67b002b00020304"), fromHex("140303000101"), fromHex("17030302005b7a5629aeddbbe160dba61aefff2e185c75c09051e45231d5c641294ded601b96b0036f2fc9d78c012fd231fbcaf3e1c71f921cad78dc9afa313b2269f4538c631422cb5ceb4493d360098b4ffa2d5c766ce3bb6745a0662786b2b57153cf001498380f9817f0f4d1b90d6c3333e552036dd1f26fcdbb452a77e8dfc5a9350f96a6974410136d89bec8ce799c7caa908025e24130dc1bc962c7b7a9131f3a99c30d7f79d114938f37f081fa40f770f88d3cc68e55f4d3f9d11d3c9c5e3c6c99809d5a3d0172cc4af4a936a2696e89710921d8a8da3fabf6f1689b3f3e5f35bade6960302771337dbdf3e71d20ea6b579ffdf1c68cfdc3d811af3487b237c14f55a6e9e3c600a9aa27ab0beb4bbead2f959e2040ea8ce3a7c084cd2e7f1c8c38461a019dd291d48708e5d5268c5b77427821093d83e3cbd3d53e90f022662ce4f92010da9dba2967bbdeee0cb02b9252d130013355723872a0a9ef40c6f3cf721cce9cc76fb90d396bf1bbe7c01332d9e0f6de561f0bd6fcb423af281aadbdfd2ba49168cf17f6265ba69d4f8a1db319efc916475ee929e6418d16ec0adbc6ef0ad47229b608f65a48bfe6b6544984d4aacf3d77bcdc4071ae46c91a1f7d27d412f07b4972a3ef316087e5402e708bcab704b5d160e7177815496e2d9826d241f1ec8ce9ff1c2c7c4a76085035da515edde8960b98491f0f22af26ea5af4ae3b")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("a0886971340d6796770c75bbecc4f833e5a6e59cd70b9b55cdd9d8472d05a9df"), sessionId: fromHex("edb1fdbb634968546f39a3d72e09d71fea051d13040fa4c7a902c1405242985e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a44c9bb6b319d2aabbcfe7d8971faef040c375740e25053cdbccbb186a1fe291")), secret(23, privateKey: fromHex("0580e52c8119621bea056b8fce29ed232a10a7d66acadc54dee808ce7f610bf1"))], pssSalt: fromHex("ce6ec46a717fe51b980339b392a83f3265a7513e01d82b8bccaf94b00ca6ce51682e90ffb4c161e3ad52a3f33f78cd9ac6a5b643e2726069352d9373d6f4c891"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-encrypted-extensions-unoffered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303bcc9303c8385af83f5b229395e07f7de9581d65fcbcfe321003188671340ac1f20edb1fdbb634968546f39a3d72e09d71fea051d13040fa4c7a902c1405242985e130300002e00330024001d00207cdaa18fe66fc8fe9ecc4ad9d27a49f36d8d6986d04b02ad6d1cdd65df640819002b00020304"), fromHex("140303000101"), fromHex("170303020c2801567bed57df26adbcf1c19e5d5d332d49c7ed74662b920d5a616a6a687e6f671c69d599b197878cb47b5e1cc17b3d005c330ccd1c479b1915471563064e0947bdaf9d00a83e58ad9305dbb6f9fbc1a8377203c49cd40d922935b3072a2ed1fa2bfedf9349c5e3ec7bc1379fd6887cc73896119f14a012b768247e9e64d864594365b0bccdb2c4a20298d5f6b61e8f3282301bd4ac5ad23fd37c80d50f4b83a6d3e4c85a15d051142aa5ca7a0e8971c0206d3fe5c0fc4d4ed1da2c4abb73cb60fbea290d1c96e39be364888905ae914d6d422f5ed5314088f4b777ea8412e048e2c8e94ef23c47017628e91404829807b39d0e01ba68f74d00b7cffe115876622e69c719dd29321254727ab6754a7d69588ab16689e044ca3628de000c3e582536dfa77affcdfc8626767db489bc59f978b6dc6804b3c3e9cf2efc39a30285d1deb46c06ce459e29026bf9df2e458be2266da5c0063b356b6222313cdc075bb1a2e52ed20b24101823ebbcb3bc95271a143eb686f19bd90586d7cfb44ca4588ea8be6c9e841dfc6c84520f29d340c04117fedb51f5102324367d3406e3eb724b5d5629161ee6c7b8fd4688797559ef98dcee2cf8f4cc465d6837c1e9c0a8b05290556d4d900a8a5e7c0385f5563bd398b7c4728c1dc19d465d5496b97f3814e15f69f713fa6fe2839a2b437c22f2d5f17566885e4f9497ead1c4940e18b7c3723489587d72d96b22e3fa28")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("8da3fcb0b9f9f5adff9f10d027b55a223dd76727832e12d9b6a842ba51688d7b"), sessionId: fromHex("227fbd7f0239a4870e13e5173327336941bba368dd88fe6f58072452ebf662d1"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("1292c4424fea17812c95f1aaedd0240ceb2cb952442955d8e64762f4be860fc7")), secret(23, privateKey: fromHex("7eccd3f9a8342b46ea080bb7f024aae2aaa3931fbbfd9d724d6dcf7241587e51"))], pssSalt: fromHex("807aa881771d64ef7f01d7a27ff591d404378123e71630af50cef89a6c4c5e6c08fa72c07ec5c20e9899ac49840d7cea56678e4dfe2c4200b2f5f3a8847dbf16"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-encrypted-extensions-forbidden", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030350a85c3bafc62ec7096ce83f1556c645137461eba011043e42d735b23a6e8bbb20227fbd7f0239a4870e13e5173327336941bba368dd88fe6f58072452ebf662d1130300002e00330024001d00201e62b6b7621f9c54ad11beaf6a1085a45e2282ab5e532780c128512e4ac7af3a002b00020304"), fromHex("140303000101"), fromHex("170303020dc35a2cf6665932296989b46a6e8107e2860c9d81ea525a07fea0d55d6a9bd9587526f473b7ea5e37ad2b812aef3f61edb1bf33a27d46aaabd19591f4ad3d4bf521eb02c7eb4ee0c43ee3843352f3a9316cc864c968baff50b13d807b713922cec702c3f44f554d1000a4e052c4c2b2340002d500f0cdfbe9758f282e2c32bd2dbd30bc32281793f187c21de5bd89d30fcb21170199936a952da10e93cee67c09b7a4d47bde8739715bbe3fcbd61bb55246532ea84785711e2ec8c7139fb85c1d198ffc881dc45ab2ef0c1a64eb0125b9856faa52f3d72490c4584cbe9095fff20b166bc6b914a7bbd03531873b4a37a38d6d59fd0e6f65fff20c61cb8996a4745842a1f66076ae3cfe68c6858b212d8a8d103ec06a66087d3261abe2aa55670fc63cc27012a63b8b6cd18efa90fdec71cf66595bbe3937c79dd3303d3b5a4be38e259d0c3c13a1d10f605e8e043b8928160c21410104ab244ab612969b871b7bb60c2325499a994dc258e56979f73e39acda79e5a01a3b884c6d20199e4ec63a1c5159696cd90a9100b58375a6618f41d347d5fac726f1726b8fd65551b72b0ed76e72c92cd8b55ff1cef256eab566e63568a6ab2bf046f32eec5a05e295bb1d503a117c605e2250dfba295a33a71a515c7774385c01d766b52d6bf93641c0bf1b373e7baa28cc1d9a778a98a9ade0e5c5fd04b490961931a712c3519775715baf08e7eae38a7dc13429c9614e")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-alpn-not-offered illegalParameter
client13-err-encrypted-extensions-unoffered unsupportedExtension
client13-err-encrypted-extensions-forbidden illegalParameter
```

<!-- test: tls-handshake-negotiation.record-and-post-handshake-errors -->
<!-- tier: stdlib/helpers/tls -->
Records and post-handshake messages the client refuses: a tampered record, a change_cipher_spec whose body is not 1, a fatal alert from the server, a ServerHello that does not end its record, application data during the handshake, a KeyUpdate with a value above 1 and a post-handshake CertificateRequest.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("95a8cf2497a9220fb44c402a68f58ec35a8cac858aedaba8b28564519a148090"), sessionId: fromHex("2c1c443456b0db5dd8690a9340f7566b68c52c0c8e072bc064f89930fa9b0e69"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7676278f73d7afc44f19a0c3ac2e33a5f577c80533375ab0549478b9b742d721")), secret(23, privateKey: fromHex("fbda479f771f80d2b11ee7ba65390a1278f26781bf97dc6c861e30c0e664cb13"))], pssSalt: fromHex("059a31f23ed7e517cc019b2a3ff7c84e09658925df1a313933909b0604f0ec93209f3c54c5c4b7c238f2a801355bfc5d281dbe9c265d359bf096b5b9d968b0db"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-record-tampered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303f9a640600d86ced7ed6d227c12af5643e2521a498d47383b060e0f5fd50e397a202c1c443456b0db5dd8690a9340f7566b68c52c0c8e072bc064f89930fa9b0e69130300002e00330024001d002083a26b490afe35943611648b2b2add71391552d0888a3f39ab853deae3d8e648002b00020304"), fromHex("140303000101"), fromHex("1703030206f275c78aab1dc768148de8a87504726eeb33349d9c58cea438071e1d2534323b036e2ba72043db00303f74a32559bd2a0f73dde9a5e4055795950b15c67b0d353c1877ffcc057be6e84cbc2f708689ba1019bc7715a8def8b13ec6b8ba9cb3dd887c066a83c5750f2267b586b15165cc5ac6e9373c2566fcb7568153350652aa034c5e3f23b291b31885d1913645b99b185b663c6ee6ac71f8fe4045aba575cf420f9e09e0f0a92834744dfe0be81aa3ae40c61628308f93554e78b0177451e0fded9b0332aaa9f33bd0015bfc134abb6cec200019d3e068adeaf18f356ec1dd250463661c845e072007f646e787addeb0a69adf0c10f8788783c0eefee1580e0ce6763b1e6e8236332d1bc6573012cf1bcc3078110fdc8c029b02fe323c776c3a57b3f74f62aee924546ad5cf1cbcaf992dc6ee85c2f413bef7e67181b4c98f75f222129c9ebbe23e73a22b4f360a7d96a19a0034eb05d03b011d8f4aca17ab525f664f24d7984e17a3f6776e04b88e81df199a938420376ade626a8f3cea08b3fae9190340a27157f59d6fd2f554e361cdce748f3afc665a58c97ad8e23511117125b07dc29265682468700634bb066611fa4ad7fda682d1cfbd3a40ec10047cf1a8a97bf42e3b7d45c572bc3d0185b8a576ba462ca18c31fbbce9900face074f6f3b5a0f7d48428f7f93bca624f77ac8d8391435ecb18aa382c2b8720b093e657f6665ac3")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("342f9a7946c57c2168904fe502d3ea3508c3ec00ca866cc9426e0c6f47041dc0"), sessionId: fromHex("a50060e567e7ec7eaff625276c51456f0fac1e949b405ebe99f036594c54eb38"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("3c54f1961651b7abd2161a3806aee96913ab15240c4e92a90c857e4e705495fc")), secret(23, privateKey: fromHex("8de3d41b55c390ffba825d2f6fcccbf467c18003c967f55d6eebef35e8b7d838"))], pssSalt: fromHex("da1e0a6ac88b205a646540259b6542394a1037ef622f0a76ccccc41f6c2c2823131ec050e39dddda187e6dc9d11f4c5602c8dbe23dd12abede60db9805fce65f"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-change-cipher-spec-value", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603036f7fe008b664890864201edec5d7a18a7630cbf11e9ec515ceed7761c420898e20a50060e567e7ec7eaff625276c51456f0fac1e949b405ebe99f036594c54eb38130300002e00330024001d0020b4ebfc4693f679f7582bd7837bc811147ccaf7dc0afc29444e48a4501efb4765002b00020304"), fromHex("140303000102")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("2b4a6da2f15ca428f0df96a05f8c58b129c0851f4538dc09e978b870b52e8ca4"), sessionId: fromHex("498c62bfdd0ecb1d5659293dd58c2dc91f781b4754e8a9be7c2f8eba9e446f29"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("8600f4f2c9b6e11fe78c9dfeb08e929c79c6eb00d59a2c4a5cc5bbfa886037b7")), secret(23, privateKey: fromHex("19e7480a9131ac44447eda37af877a15d7050d50f7978f784a068a2461696dde"))], pssSalt: fromHex("2f48563934e6a17159acac4a9c1131b4e48d55ee59b2357fe8cb9140e6efa5e9cec5b44a99b60274a04f1f615d66e3ece72494037db7a3e88a3f135e699fb48f"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-peer-alert", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("15030300020228")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("1f1773faf049e749fec3f5b4806b302ff14c4545aabdf7ef5bf20139bcee0610"), sessionId: fromHex("f88baadfcc172372b9f939005b24b5af87fc986bf8f30508a0b90f7549d296a7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a4f33bb54cfe4cf4eda6a03995417323fc8a20b44995a0499fde8b298b6e7a75")), secret(23, privateKey: fromHex("f6f524ca84227b78ef73bf41de399223de390fc8e8ed5b97fd2ef1a7783669bf"))], pssSalt: fromHex("339770ecd8ffa6ca61cf6f483457d89620bbe3f8e7262a1f3d75e3b3322197b2ecbb44fa4961389910b206720e46c182ca73934572024408e4e140dc4134f1b4"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-server-hello-spans-key-change", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303008002000076030394d9e30c15b69b00401f0256594719c6e9fe0f81b34a754faa265635561c39ca20f88baadfcc172372b9f939005b24b5af87fc986bf8f30508a0b90f7549d296a7130300002e00330024001d00207948230aa0688b0b793c7d75e6fbc302e16b80f9a409dc3c93415690ac95c056002b00020304080000020000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("1082dddcde5a53864cc20e7a21270fa1d1e2d578b137b7f2b513fb142a4fc740"), sessionId: fromHex("b323bf0345b406d5904f544fa4ee5509b7354652faed6e59f7e85d0acafd2d3f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d9fad9a96fa59d2b699189f00571370b06f816501553ca91d74ca14de2bf796f")), secret(23, privateKey: fromHex("a1b7e40806238434186a8d2c06d2e19277142035be5c624f0a5432382b266bfe"))], pssSalt: fromHex("b1ab5731c7e2ce5559369b8227fc0ecb5796bc1f995a04bdd237fc97d4dc94abc756f1d36d699c73e6293d85384a2ef5620c19036a6a566e1b8d8fb80967a693"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-application-data-in-handshake", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603037a2a78edff704be0948db1761e5e8b0bc2ca84cf214a3e6f901e994756b78c8620b323bf0345b406d5904f544fa4ee5509b7354652faed6e59f7e85d0acafd2d3f130300002e00330024001d0020e2965f1b7bf218b951b6103328a30af6ac6fc096c9f1d1cfb40ad5aadad56969002b00020304"), fromHex("140303000101"), fromHex("170303001641169e5756d4c923ab3df447c216c46d5c4c1160c42a")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("2e77380ec0d5199fad2af4f7523bff40ce019758e1f4e6ddb96cc0780a43a53c"), sessionId: fromHex("e6a1615a1a4d05451f70a62cd0c8ae34a90bd5f86d510766994b44a3b2915bec"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("2aab92451d83a72dd60db4cf28de1349a57bd8fe5d43fef864f50a07dd3b9816")), secret(23, privateKey: fromHex("7d3474f75723a3f728500c7b9f0c3c32a3181d7eaf6ccac0bdd6316ca053d783"))], pssSalt: fromHex("9d507e229429f7cd6d3a4467c4c363ea60c15d376dfe8968ee1a59ce10c7c1f61d915a4066c425ec327c274cd2fb79751be66a11eed9ecf04b464466a4d70402"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-key-update-illegal-value", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603035c55cd6970ee88e39554fe78ba801bae4a4ddc2089ff857dbf13efc997017e7920e6a1615a1a4d05451f70a62cd0c8ae34a90bd5f86d510766994b44a3b2915bec130300002e00330024001d0020399774732dec04a13a81b5e0068fde80105b7e535d9aa3e4951fe74fffc9ec0d002b00020304"), fromHex("140303000101"), fromHex("1703030207ba96fc19788fb806c1795504698d43aa0812052c4187d128d57b681363fa548283145bca92fa2bae277c88a0a037cfe149a4a3cee7b4f06cf380120a6f4d9da6eedefdaee3cf66fcf47fb82bc83d0fd3d0ba4d188e42083e463760b4fc36ab79270377dfd74085e8548b5486624e0ee22ffd58667383012e2d81a7cdf2dc2a0209047a043d0d633b1ce78b767f5ad376e667db3885868f27faec215edff9bc96c9e8aee2c48e6c4e44ff16c0d7818707aa99a895fe71760a7dae290eb1441d8167e050e68df4395c1b34a39d2bd23514017d49b79431564014005e3c02cf65e69bd2c63540efe6271f5f6372cf59b744af17f42ec05828ecd13441764fe6c961a553fa565294b1c769d18779f3570de5459e720378d9b674cc0301a67b09667e33b3af1cd06608afe542c866e1c8f5ce8bc6f8c96046955ccc330c29dd6d0f2b262d7140ecef27cc5cc869e7b8973bdba702aeae86541f9151cba355548a3cc322398bff2be5812257fa5f60b27d452c16c6d557e60c70e474f5f4a3973d9db958434d72c686fc914ac1258740f500723786f48ddc73dfb83e2e819a8ada6193d0f6b4e50c0c6848e1bdfe3b7efb459d95b4f5d37c10dc1afa469933d4791c54d51fd0a51985627400e9397583bf81f82c065974f216224aa184c1e9751fa680336d99d0e63c8b54b183fcde517736dd7844e28bc605b3fa1b55fdf1524f956204f86908a7ce9f"), fromHex("170303001666cde4b869fdbe4f55ea94dd57c36b117b2cb09bba20")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("b69cb05ca5f4ad7c1c0325656867ff002c845461a09e485ee3ac3ad1ed214016"), sessionId: fromHex("3b64a688ef6fc9c51262d82a5ab435a392bfb022711008d52100f131b7cbe3dc"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("14c05ca2996cdf275880229357bca60873d3fa957c31e78544a80f451f6b5b48")), secret(23, privateKey: fromHex("12024d1912459470bf50bfd02196bfcfe646f33cc0e0714da5c1a4a29c0c4ef0"))], pssSalt: fromHex("e007e3f82045f16c4345e50d0fac89b0b9a208bd9c54d12c02d1cf2134dd13f2b5415dcaa4a632783846917627aa2bdf0841f61ef622f72cfe6686be3fd0699a"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-post-handshake-certificate-request", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603036015c817c80dbef258c83f7f2ffcb9817f29499594796f93a6dcf8741d1bf41c203b64a688ef6fc9c51262d82a5ab435a392bfb022711008d52100f131b7cbe3dc130300002e00330024001d0020d3f3550acd233528f90382657f2f350b3144a3fcb08c37522d98c740e2ddb70b002b00020304"), fromHex("140303000101"), fromHex("17030302076674f19a49fb63d22dbca713de7641dffcdd5ff663370fe9c1870711a27f0cce5f92f3937d8f218e7df4fbed5048ec8fa8883ac08d2c4116281e34cb0b17c35166e1237c30ece697e6d56bf2af59b6f276e43d825bd41045147bc0c794aba5795e6257f332cd73909fdb8816f0657c224aca38e50b144d17b97527180959d51079124fc93b99c51a7869ac78a528f105166ba1fff7e55b44357a3eeff5380b7d4d0e46230c61da0cabcc036b426a313c845669caa91c2deaf6e1557cab5fa2045adfa546ca7385e306aed6bd39e4bcd662db4fac33949b546e84a7a84e5ef6e753634f8ab069d60d1888b83cd9bcbe9631ef147d1b9b7afb2f064f7f0aca5ef55b9bbbef4a5b6ce1fb86a5fab7e340ca32dcbe57efb56389f6409486155cd1f968e5a00a02e02d85712161bc633501bdba773c3f959d2f12e7255b7da1ec435de431713089df46a4afdcfed17501188f85e35e68b8ef597f1dfbb61a1e917535289ecb760c93341240b916b53304e74cbc5784c4265d5b34ece4dc39e8f6a5240c6bc2e76cf95980a455a8c25f4f24cb7d5075767f2097387abcabb1f328dc1adde62a98163c12be767f272baccade716d6a60ab3a246d52b5640ef03dbf6becd9f47efd8e8950a669a9f00b24f2c8783a2cf5244a6b8078a5ed3dc04d08b50b4810007661031306767218da8c4e9d46c5ae7dedfb73d233826eb669ed0b533fcf0603c95e7455"), fromHex("1703030020fd8cb793bad84630d03bcf057d0023d21a5ac27abc258b57021f4c9ec26b5edc")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-record-tampered badRecordMac
client13-err-change-cipher-spec-value unexpectedMessage
client13-err-peer-alert alertReceived
client13-err-server-hello-spans-key-change unexpectedMessage
client13-err-application-data-in-handshake unexpectedMessage
client13-err-key-update-illegal-value illegalParameter
client13-err-post-handshake-certificate-request unexpectedMessage
```

<!-- test: tls-handshake-negotiation.message-length-limits -->
<!-- tier: stdlib/helpers/tls -->
A handshake message is refused, with InternalError, as soon as its 4-byte header announces more than 65536 bytes, or more than 262144 for a Certificate, before any of its body arrives: a ServerHello header one byte over the cap, a Certificate header one byte over, and a NewSessionTicket header one byte over after the handshake. A ServerHello, a Certificate and a NewSessionTicket header exactly at the cap are not refused: the client waits for the rest.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c12e1b39731fe4ebb9409bd6ecdc9650c250aab10743f2242c54f756b99895a6"), sessionId: fromHex("939f19bc3038c36edfcfa575fae0e5a35aeb9d274a834a7f53aa2a9c2118f66e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fc6371002ed7eb09982d4a0597260157b846868fabda6649fd0785cffbf373b9")), secret(23, privateKey: fromHex("8ecb3154e0305063ca303cc60933291f3623f437ae82bcaecb954d2ed08f0926"))], pssSalt: fromHex("f70418e3a1d452e7b1377c04f6f24e2c2895369e2baae002a004bb3dee65e1048fe9d7f272caa587ab5f2b97c08e659b9e4486ecad75a5ab0162a1059399ca0e"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-message-too-long", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303000402010001")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c12e1b39731fe4ebb9409bd6ecdc9650c250aab10743f2242c54f756b99895a6"), sessionId: fromHex("939f19bc3038c36edfcfa575fae0e5a35aeb9d274a834a7f53aa2a9c2118f66e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fc6371002ed7eb09982d4a0597260157b846868fabda6649fd0785cffbf373b9")), secret(23, privateKey: fromHex("8ecb3154e0305063ca303cc60933291f3623f437ae82bcaecb954d2ed08f0926"))], pssSalt: fromHex("f70418e3a1d452e7b1377c04f6f24e2c2895369e2baae002a004bb3dee65e1048fe9d7f272caa587ab5f2b97c08e659b9e4486ecad75a5ab0162a1059399ca0e"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-message-at-the-length-limit", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303000402010000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("d13b2e8f4fd14699f0c0aa3b7a67ba885be6b3876926f6e7ac800df28d055df2"), sessionId: fromHex("dd41cd5c17bd7f5b912373569906f0869240a963da902228032862175534719a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("68c5fd2b2b436f4952952a5f25d36fb2ee87cf3f94cae4cfc28f3763cd7fa456")), secret(23, privateKey: fromHex("ca54b4674beb994eaff19759636996505228c6a236e98fbbff80a09a5c6eb4da"))], pssSalt: fromHex("96d9d42b93c3d872cdd50578400519bb2951b2e9a0fb460f8f7b46aff60119147a8a5dbd8259e8cfa5c2ad53d0d2b6717c4318cc31b4d3ff970690341061a2fb"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-too-long", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603033aa2e4ed9b8ecaecf3f42cdbda34c64e6249d088313109379a28b783c2d01e6320dd41cd5c17bd7f5b912373569906f0869240a963da902228032862175534719a130300002e00330024001d0020c9bac40f3737aac181d15e55b58f65ec55e87acbd182d8ab632a3304c4bb631e002b00020304"), fromHex("140303000101"), fromHex("170303002ecb2e4040445f69de10aa876a909371ed997e78c67c752b56199ea67ef00ae8b332b7b93522f7f40135509fbf0df8")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("de9e78da369a99dd7fee0f00d594f81749b4148b6c219c3756f1dffd2833d737"), sessionId: fromHex("4ebbea28371a1307cbbf1cc1726bd6ff8463bd7d32eed04e264af2a38061cfe5"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("23a43a477301a77a42db8fdc6bd281525dba321851442c8d750f75a36b1d2f83")), secret(23, privateKey: fromHex("595ffec9e248373c6f10a4ad1537daa62acb80836fbd77098157ed11dcc482c5"))], pssSalt: fromHex("e07f583bdee62e3c9312cf1e2fd6729df1dcd0abff52b1b6c6105d37a25c5ef26741f7d1557f7bd33c6601d1af861b7de803c811bc69ae003bcab72d7a79e03d"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-certificate-at-the-length-limit", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303ebac9e0d3c561c3850c5947ef2f16ec89b7bacd4f0d9045a1fb38da61ddc01c1204ebbea28371a1307cbbf1cc1726bd6ff8463bd7d32eed04e264af2a38061cfe5130300002e00330024001d00201dfe4da5d60f9bc592ec0b8a8feac3232b2152748e0fefd5ad40c88a7ba4b90a002b00020304"), fromHex("140303000101"), fromHex("170303002ebdf9a40e2715f4afedc1475c4b4d6e535b6cfba62604f5788d1c58d81066d476e4293e7d2039e9b946eff789547f")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("7f7fabdd237e1b30a5c94b7c40215ee392762e8e1a7680daa94985aebb7ee9f9"), sessionId: fromHex("4a1030b7872974b2b7f346aac5d9bdcf310e75ab194a9c4f0857624a952eb890"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("0cd74d3fdad7e3f3d1413191329e4316c817f3858671e50cdc06abe9710d4ba9")), secret(23, privateKey: fromHex("c78690e38a45ec25281ae7216bbb20b6dae8b7c3a6488897bb005a5b9ab57d5a"))], pssSalt: fromHex("21e82ed4b89207e778221eaff32dad445ee491c450e484ea4da29ce861b8fca54d05a17aba95528f2f9f55d767b701dfd4e5cec933ddd0cb407abd82336bce26"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-post-handshake-message-too-long", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303c03cd96585df28c7c1385c2a06d749a08b0ec962b9cc4a6c27e7209e50789e46204a1030b7872974b2b7f346aac5d9bdcf310e75ab194a9c4f0857624a952eb890130300002e00330024001d00200622b4e3b69cf903c5de3220c920c1275c52df2f962f3c84291972f90e1e5260002b00020304"), fromHex("140303000101"), fromHex("17030302073f3bdd4f065627a8d472993559ccc0b9fe9c133bec105daaa8dd13e93ce2d23da878a15151d17168fd66fe1e76eea2ddcb5a007f091ccffffd88f338ea28d1595a5ee68afa4022c50ad2ef33b75113cf7922d61568a51bfb3f8b804748c24793cf82b8172d43c63ba0cf915f79be712ca3c20d03ea0f48a90fa6d2dfddf933bfde09a58853c7899f56ea64d20e1c1d7b3faf1b4938bb14999ce881f6a42ceed2a3d24a0e8f6f01be8dafdaa4ce46e8bc9cf03cec57371737a6a09711decb5e32b8c2021f4634db81f2ba8643c402ed185d63916ea171a4d13dbf2ab1cbeb1c3fb114b5ee7c98a197b3c69b158b2057db415700eba3e396192b7fb2617632551f8759bcf86c1a70806211151910241deb742d78353e9d0a7f7f7ea729c39020b2a1bba40f4b30a63138ecda5669471a4a3b2eb7720d53f01ba3980f2c2e2d551fbb7f1f9a8582f248628fadfe6201ca6e1eb5df3ff7d15e9640a766bb85dec80fec7f73d0fc8c28f3b7646fd629a8cc58bfc8fc5c65a415e18d20c83860609a19039302a99258a1552193b832d22d379fabecf50e80d544d57eecf1964b89fa355ad1d45bf25d92e531610373e6cd0770bd6408fa8ef7308b6863f6ff8c2d3ea33c0abf20b456503fed6d545d5a86c76063fad992b1ebc176649830996a6eebbc212686873b653dd61ffa0301e05126b8f3534c5d50438db7ced24da061eda1d9463765d60c6def"), fromHex("1703030015c76eac64f9059bdf46f0c1ec56e1f2d0453a636d3a")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("40be0a8e90882e4da4bc84472785bf081cfa9463f56a400326c36900eeffa091"), sessionId: fromHex("98bdbed21f42063e02a8564ea965ae38b239fd9c7bf40160d965f0157e715bbc"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("345ae71a7e529bc241cb077356d83f90f2563b4b21042dd98e2eef370d76162d")), secret(23, privateKey: fromHex("bf01abca550f08a1d215ada70ff57fbcefdf0f6131d1bacba5ce0e200ba8cacd"))], pssSalt: fromHex("baf75e54324e8ba1cd6a882219f528fbc52d114f727355cc84389f121737331a88e4e43a63da04f90cbf2ee05bdb03f8e1f0c95764eebf2b606d6dcde7e24d16"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-post-handshake-message-at-the-length-limit", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030322dddbc139f3dc4fae2e94ea2be6732e9bbe22ec3e12bdf57e0ef1780004bf8b2098bdbed21f42063e02a8564ea965ae38b239fd9c7bf40160d965f0157e715bbc130300002e00330024001d0020863e2ab283649f3a782c11c12f4458219b4aeda24df55c2f536082d2eb96b76e002b00020304"), fromHex("140303000101"), fromHex("17030302088b2de1683af4389b77561ce0f1daa064a271d2d70773c869d16248654c17172e6b3cec6891856801e4b5f3963c792975f362bacd57f510e4d090fa83392bbc9e4ab21956f65f1237d2fe1a012ce09711dd7620783acb2fbd96976bb57dbfa009a9ed2d4dea7876383302c203b2d55edc4ccc0c5e3353d6cea9d8b1ba0fabb2352be8832ed4608ed9030088dfac60871215e3a93b66be56119154f07c8ece95a67e1e647c3ae2bca8a34641abc0271003f94ab51996cff86076e77de5a59330d798e3de1a255688c1c1ac0791f272b1bd95adb1804e727de4d0cdac40371f8c0915bb39ae9aa36772a4c3bd2df5f9c5541eaf43062883b91d4f04bd0ad6dd6257405c2bf5e39318be25e51a1b808642fc9fa293d9ae47c0477247f03f62274c8a0af7463c9e89e3dc50cd2d7afb515c20493c4f864b8f601dde642a0aab7d692bee14cf672a4de66c8b1896e6f672bc7cca58acf03eca59def2ed3574fa6475fe7fd77e976ddac7e1ad6679b7089ebda2ec61d5076ad217b3e47747c5b3a4e9ad74b89bfb744c3abbd1161a308201138d404f7c8571e42703e092d31b75d82f67f15af02d39c7ef0948ac5df7dbf0701e9c01d15cafbe8d2eb910807febd8d83650f0191e0f7d228f1bdacd3b3eb842756338036a2701f6e8ecb21c03ebb91e66317ed15f05f4d4ba41c58efd3fca0d68ca356eed62a420deb6f0fed6421264752b93b1aa65e6288a"), fromHex("1703030015348d2f99ef12e66f3210a7a3e76e8d7ad12a84b81c")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector5'

function main() returns ExitCode
	vector0()
	vector1()
	vector2()
	vector3()
	vector4()
	vector5()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
client13-err-message-too-long internalError
client13-message-at-the-length-limit {"state":"handshaking","version":null,"cipher_suite":null,"group":null,"alpn_protocol_hex":null,"outbound_records_hex":["16030100e6010000e20303c12e1b39731fe4ebb9409bd6ecdc9650c250aab10743f2242c54f756b99895a620939f19bc3038c36edfcfa575fae0e5a35aeb9d274a834a7f53aa2a9c2118f66e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002034cffdf598b6c46c8cb1b634b71ba8a10ba77c862f3fa1bd7ab57927f5f6670c002b00050403040303"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":false}
client13-err-certificate-too-long internalError
client13-certificate-at-the-length-limit {"state":"handshaking","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303de9e78da369a99dd7fee0f00d594f81749b4148b6c219c3756f1dffd2833d737204ebbea28371a1307cbbf1cc1726bd6ff8463bd7d32eed04e264af2a38061cfe50012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020e3e6a44abd3b191d049d45211407d2ac9728e2df8c1669f9f3ca4f3a4a409043002b00050403040303","140303000101"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":false}
client13-err-post-handshake-message-too-long internalError
client13-post-handshake-message-at-the-length-limit {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e2030340be0a8e90882e4da4bc84472785bf081cfa9463f56a400326c36900eeffa0912098bdbed21f42063e02a8564ea965ae38b239fd9c7bf40160d965f0157e715bbc0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202b4eff3b50b88de335fa70ad400508186c9f7431f1a3fff4fad54b6e1cc2bb74002b00050403040303","140303000101","170303003527122598d321da0fcf8c3c9baea99951b1936b23d6b9afcd8dc59b066f1d3aae9792897021b13d0de907b9650f6e65aec25dcd9fbf","1703030036721853773c5ab91cf1b7327bea8ba3ebe1816c38bf4faa5af91963369596e6a993d9b49589d57d9831d13412e83a48f8dfac9d3e5668","17030300132282c669efe3a38f4d1a2b68492366bf88f130"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":true}
```

<!-- test: tls-handshake-negotiation.no-record-inside-a-handshake-message -->
<!-- tier: stdlib/helpers/tls -->
While a partial post-handshake message is buffered, application data and an alert are UnexpectedMessage; a NewSessionTicket split over two records and followed by application data is accepted.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("26c0ba011517a4dbb77d370ba78b48233925d24612ebb72dd88178da6a872fdc"), sessionId: fromHex("c9cc3dd91512fda2153b4e23e456b340f37ca1d81ab2214cc5b15a8402b17e2c"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b0e66dd3434ebbdcab2997b48a0b5849c1371292d149a542f0429381f06ae119")), secret(23, privateKey: fromHex("0a96902467e7f3c8023c101055fa19c619cfc3c193d51a473dc83da61edf444b"))], pssSalt: fromHex("f58160494b4f03edce6a83032bf7006e75a96386f86eca0ed7a471dd6d8ad17410097dcf0e77cdfdc1fd32e0787f139b574fe698ea358a27a3291ac4c90ff3f6"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-application-data-inside-a-handshake-message", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603030281d3ea295424ddd9a4aabb56f2cd5e0e66af7cfee4e357cd05343eb73e91d020c9cc3dd91512fda2153b4e23e456b340f37ca1d81ab2214cc5b15a8402b17e2c130300002e00330024001d0020847b946e33b5f7d45604585869f48e7c938cfa6989062c49819d3010543b423e002b00020304"), fromHex("140303000101"), fromHex("1703030207406f3b0ffd976b395549f7b1fa863618cb14caa7480a4a42de57a8124172e3cb526532e427527cfddd3f1ed3f8290aad0b02c138703edf31653248677e32f285b2ad850c403c8e509eba2ab50db8dc1247e12471f1117e72c720574216be20df3297c301a87a296375a368515c3656a89579d721d516629c5f2f60fa15ed6ca5ec782e10a227f6d8afae98a8969dd5eef6c9fbfd4590dad1d6610f5c8ec8a837f91952e2b141fbd734381bc44fa2a36fb3944305f780680c09c20e138e7a6abbc0d8336070c135233eeaf55a9bccf0193ef7f76e658a589584458139993d13f201de256119cd1b2e0b03fc5e4d6c0306b2402258c10796fd092b37f2fa4007b7c377bf17405e74da33bc125c54b0e87d1d9722220487642b93dc80cd922010eabbb4f0d0216e88808b14778727ec1a2e9c85dad8fe17a2fb7ab13df39faae7aefcb3b7a8b2d536a82bc945288847adbdfdb7b2a76c38e7b965b1755e263f1ebdfb38a460f0417dc8ba8285d41302c6c5c1c0e08f17d9eb95278d32fc4cd10cf30d42f37e23860b6cc15b4943b53b7f56b67f44e3e67b939762fe2d7ac0b7546bc613d0df0b2587c98b3a6e78a431d699958e0b90797105ee6e82f0d5c8701b3891180ddd1a049ad0d9a0a2083e1221bb1e79927f7c9346ad8b6fca6f242efd531f3fa2b09ebd0bf0a2de2f73278e7bbb7ec4f81e207e2b964236dabe8e083acf06626120561314"), fromHex("1703030013bb45689888759b807590b5e1524fbae8e47f7d"), fromHex("17030300157f436f5f5883d4ade6ecc40090f66a710d708a2f1b")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("7d979233ae8a037fcd35baaa4c52d3a44c1889cbbb6cc9b24581379b78795adf"), sessionId: fromHex("9e6324d468f58b8318980ffccafa7481811ee7be60cc8078ed243736372a139f"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("17847e92f76dd58a2c0f3de1d6c9305906a1229060b42cb2bf54f902b621cd41")), secret(23, privateKey: fromHex("bc3387bb750328e18ada39817a8d9aba48ebb9e639a2b38d79f64a5cd88676c4"))], pssSalt: fromHex("092ce2b0239f40371e66b210d2d47515f946f4997c764bdcd67bebb1c19f0ba09678c52b6fef350675c421b7486876db40b046b7b82bcd1f88c03f676cef88e6"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-alert-inside-a-handshake-message", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603032890e963ff0837c013236620325d58a256e9ec02aacc8e46bea32cb9e97efaf4209e6324d468f58b8318980ffccafa7481811ee7be60cc8078ed243736372a139f130300002e00330024001d0020ab63a09c0f4f84f7a0ed9d098a1742fa1f6a7cc20f740a2a8a77fd6ea7c55061002b00020304"), fromHex("140303000101"), fromHex("1703030208df6bb93b5d947e05f8224c8424284eaa3d1529e68e0e78b7f1139caf0bf93f379439d8b480e2103b5759c4b401826dd6a56516fc284e57c2f9b93006542c7a7b044c8768eb498e5a86255c0f403150f9cc4c4eae8e222616c4fe5e91495114c2d105e5303a570d4d8a3584fdc154cbc67941fc8034f96b6b5a9436a58afb0a9ff115b5baf75f2fe14a2b193d47bc781c66ce4c961b38f7e18639f91959203902a1cc6063791a51f6e3360d5890c4e0ac16309b5954d2d930e6ec903f25fc5e68e5ef1ee3a19afae71bcf5b811ae6fbeae11864afdd5adb6ec5028241675c679b07caa590fcbc2c0234e6c1a96cfa15ed8452418b6274b81f89ea74b51d00729c98b96fbe8e0a8bf8155cb222d442d21564a32e8ffb6ec6d93ba967d69645327f6e0f4a63badf014931f7b91e591a046be07dfcdba3f3620bf5415ebd8e2d135da98f22fe5341d75da3546996cf4e1a773c162b1103fc37f768816d051ff3cbcf484de39eafe95ef53544227977bdbbf529ef1b34229afd6e39ae2d48668918c2519d038e9e74e6489ebb0a57b46d34d23081e7d277e3d9f3792d7d0e4fe20123d04a3dd49e543c92bc7812083a6c67961c46786b02c6508971bb1efcaaaab16f1ad6a279304fee938156b68f63612d8ca8d1ed7810ed9e7ae571968e3b059d2c263621ccc6dcef7472dbb5e6796a22ebc6d4c6f0cd485e5a06b689b2b76fa70d80e785e9990c54d4"), fromHex("170303001383b6d6042027be413276b103faae6ae4d34b68"), fromHex("1703030013b19a43cc1fae3dd1212789bb54081908a5d54c")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("326483f9c296d527f292d39fd4949ba4ef08697c872acd6731b7e1fc46bf87da"), sessionId: fromHex("64a49682df45dcc25793d34f36e8c218df8b20ca354f4258445c12e769923986"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5655239b7a9a09ab6e8debee2973ce4e70ed0e06c42dcc509fa4d46eae18de17")), secret(23, privateKey: fromHex("60263b686bfdb23c1773beee156545725853be5e3745131f3e46e60891fba12f"))], pssSalt: fromHex("c9fd2432ff6ec1f8ac259ffab2ead36c5998d203568de2f9f24fa0cd55a00e3149a3cc0165a1e7c28fa8c27892be7bd3f21d0d9ab5f3169b03ba390c5ed47718"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-handshake-message-split-before-application-data", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603030735827b29e24b8c0431401b5dbfa25c17e418dd581df7c4e6e9348b0385d22a2064a49682df45dcc25793d34f36e8c218df8b20ca354f4258445c12e769923986130300002e00330024001d002078deccb83a0a7153a6dff9f3358306773951cf17b0359ab1f4cc372cb0aea80a002b00020304"), fromHex("140303000101"), fromHex("1703030207da3dfadd6af5e72380a3f9b9f454d84e07d959a8f0ac239fcd2eb3844a4d983f40d9d13cea83c11be951b1d32563361c4352cdd892adf691309df08fa59e2dc4fef08c171f01fb09f42be0910a470ec938363deb172ad46220e4eb4df683399036853de79af7e90b89848fd1b86881b39bf1c088e101c58cc56b16e6990aa8f28437375a675af6db90629b11ecd0f5339f153a8e5f79e9fb8e66ff94780347df3705b2529bb34e19d61580727bcd254135eaf5e197ad9403e55f56e0280ac0e2dbef85c0ae6723cfb28eb3c303c975ec9f23104c33b26fe4a72f1829fba5cdd8e5a0eea2b7766e4d83405cd2df3e380f7d3d36fcfc0b9e478b4a1dab0fc6ab4f0b93d674ebdbedc5ebbf3dd7f283b630a62869b32f21a48baf9bfef922ef37011549042bd8b43d69f97cb9b1e6bd9b7d45b01ea44a1c163817aee3be6894dbf63c15b7b4f1c194c7a43658924287306ffedce9c5465b31e9539fd8173488a511c318c4c97323773dbb0fa2019728802e54c15e91f1450799a359a6ac8f7bb113118bddf672899393d21d3d4cffba529cbf8e6d79c432d5d813ef291577e6c707821ce5ddc3a28e38d523cd60d6bcd4a07f2c1d1be4a422aa0497fcf6b2addaa91fd80a77be5c936d5efc09aeec39f4bf47c6adc9fb06d92199c3c2b18af47d20a5b2b5c9ae8d421793cebdbd04101ca8ee66b22602785d8299345528b1fae043d1446e136c4b64"), fromHex("17030300134c5c2e090940557785e3b8f3789545149a3479"), fromHex("1703030022712d12ac3a248c39912d652a599c26030881c2eee09e4bfd878457ce54e9caf732f6"), fromHex("17030300154be81aefe5d52d125c28aa1cef365b94bcd76d70bd"), fromHex("17030300392b3648fcdd57a978c4854808d097680b15752254e40b51e1a348b60abda8713ec0cb340bce5270daac9e9970e9b934d05ebdb2e793f7802b19"), fromHex("17030300136279fcf929a368f1fb733c91a92067854467bd")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-application-data-inside-a-handshake-message unexpectedMessage
client13-err-alert-inside-a-handshake-message unexpectedMessage
client13-handshake-message-split-before-application-data {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303326483f9c296d527f292d39fd4949ba4ef08697c872acd6731b7e1fc46bf87da2064a49682df45dcc25793d34f36e8c218df8b20ca354f4258445c12e7699239860012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f6a87c6ff5b98f7a59ddb5dc7428245d4c5c2b43ce1fbadae1330834df188625002b00050403040303","140303000101","1703030035d1e550b0ca767704f7875d3533467c3f173dfd601306a75bfd933a0f12687e5e57e4e874ac4016302b38e3869f15c5298282cb7570","17030300369fe31f2aa6f3596072c9814363e570eadf9a8a4bf4df1126dabb16f3641af3d748847d30fc9631cdf911537705561261646f17af6867","17030300135259ba87bdaf31fb16c7e4f9f0a4c1cc1f9c17"],"received_application_data_hex":"6c617465485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake-negotiation.useless-records -->
<!-- tier: stdlib/helpers/tls -->
Records that advance nothing are limited to 16 in a row: sixteen change_cipher_spec records after the ServerHello are dropped and a seventeenth is UnexpectedMessage, a ServerHello between two runs of sixteen restarts the count, and ignored user_canceled alerts after the handshake are counted the same way.
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

function report(id String, config TlsClientConfig, entropy TlsClientEntropy, peer TlsPeerAuthenticator, helloSource TlsClientHelloSource, inbound ByteArrays, writes ByteArrays, closeNotify bool)
	if let trace = try TlsClientReplay.run(config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: inbound, writes: writes, closeNotify: closeNotify) 'replayed'
		print("{id} {renderTrace(trace)}\n")
	end 'replayed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function vector0()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("071d847d977f79d289a4ebdfc230111e667d4ee69b000ddc07446161570665d1"), sessionId: fromHex(""), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("53c8b6d1cdfefaaf17d951d28bcdc0d5af3faefc8954d4ae38e8424301a696a3")), secret(23, privateKey: fromHex("55b7cb271984db936010be43afa78dae8b28f79d975d7a33dabfcb53b232e5cf"))], pssSalt: fromHex("52ee430f8da25a9dafd297a8166c15910a5d2904448b84da8e65ef849c62a5c676b0c99f866f1bfb7a2e0cdee9a274863f4c816d3c4c561ffb7024d330a1d940"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-change-cipher-specs-at-the-limit", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303005a020000560303a4db38772327d732e0efb4d636eeb9602a7585a25849f8f5240bbbdcf9a26f7800130300002e00330024001d002059f3ee6a198db7f8a0deee95a785bcbe5b538ae07abe1bc967c45ababfcf6964002b00020304"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("17030302088069ff54bbda9bf9972b31bd5b08b7f1a14426a2dea936929ceaa21d3f583f071d34667609c281c503ae5daa73eae6debb6faa97eadcbef7672fa7720ac642a59abb8280f85d091209a84e79f5a7811d334f8ce7d410ce83fa279a866e1fd4c4027408a10a22d5d6d218ee92daa6d101eb48d557ecb0bb006b9a5da60fd5f3af619df08ff2d1d0fe69c6251e43b123d4275e1e96f1695eab302e7c1d1e1ce07b5170e1028e2df7a78be0093b26952601dcaad55793d50587bd52da184c9de3bcefcdd1b31c41ab8a3005e8a0a23f3de2002a5960ebbd7249893fc93c28ff3c774af0be6199ae0c2e3f610a6ccc8999f55ca61db2f121848a7f72f976ceb66cb2b5873f2078b36c9966482b675b2e8ee1f8eadce4773aed165deab1d367d989455e6b8b2528c93342edaa11d6eb6c677e300c80f25967d8cb3aafc0dd1b2ec3a1a6c45e065538a94a0762f3458e447854d724a3c6a78e486be92b7b74ffe85c1f2579aec33e8aac81850010ea5357e02206ebbbfaade09cd1d41e90246306d5e743cd1f8692c35713b9f86fe06f01533a2e14c8f4ae727ecc464a0759d642f706de347e1d2101f48845cfb97bfe2d23b688dedd4d47041fffafc3ffec8122005a68e2ae158b658e3e3930cbd1dea12d06cb308f7ce583286b7a65988fdb72f4351f556ad9384bf9f573b119ffc03454ddfbbfd75861bceee300a92bf2480b19ed32c9e0424289171a"), fromHex("170303003964ca4498e854e70708ea586a3cf51945616fcb17a069d98badacd6053e30f917684695bc60e829854c86a561f7feb6bf74fb8ff33848c24041"), fromHex("1703030013e2187960bce4332715474d443436cd43e207db")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("307571c55ecc193d26c2613bd3f5900a1b898289218b0724321f140f2fe02f38"), sessionId: fromHex(""), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("27f1eac314a0dfbd07569018145cdfc0d90732c62dd965c38cb5a86877ec4a69")), secret(23, privateKey: fromHex("d004e756a68dee7f0f938965a59a57029340d2e6e8730d89b4f0d7960a9741f1"))], pssSalt: fromHex("5061b69b418287e22130ae752d6ac5af6f153707472ee1b33f40f3169833bad0b423f66a9eeb9b49e4d338e326bdd47d14b4b7fb18fd2c1e9120a190892504bb"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-too-many-change-cipher-specs", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303005a020000560303757d0e13d63dceb4d3a6b4df9b2a8836afe166e89fbc010bd966e8ef7128627400130300002e00330024001d00209c544b60d60125e709e684ebedd6626a05b545fd9d7003fc5ec6f10e59c7ed1d002b00020304"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("0f7de7dbd9788dea6b08130449ed6ea4c9771c8a5aee1fcb6f1e084fcb3a2405"), sessionId: fromHex(""), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("26c900098f0df54ef7438364778f0d4a9df5428997575a836ec7f0df870682fc")), secret(23, privateKey: fromHex("5496536ce51a51765ae955c2b3ee0183910e36fbee958304bb38a368cf2d0179"))], pssSalt: fromHex("5f8dfc2338c697e75b765acaf5fbc8d0c44188ae46c3bacba4d7e77f652c7ede1060feb28afd8a63d5b4fdc1e7559b1fabec47767eb4a540c5f9c5b8685db7e0"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-change-cipher-specs-restart-after-a-message", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030038020000340303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c00130200000c003300020017002b00020304"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("160303007b020000770303c64f8776dab3ce11660cdb059054b1d57ba41686109e790c8d9003ebd298e9e500130200004f0033004500170041040e5781d4ba50fa52d60de7263953a399fab97a3aa83840d4b2af0f9d7ead3d91d5299ff51ee89aae0431cac9e0ab9afcc88f5e134987edb512244999f5e109c1002b00020304"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("140303000101"), fromHex("170303021838f9c9565625a789c5ed8fd7ca46e206bac5068e9657357b65a47f42ebd172ae257b41d2eb906489a39fd3d445b1af56fbb6573afc73d45df2f0cdf0196b8c7769ab0c6625cdfa4c28bfbe3ec275870c5e8e910dfed2d8813791889dc2035793f0d2bcdc83965fc43b90d2894d45fe6dbf91d9c9fe5876f8b0f384b15915520ba03b5a83467282a336552b807113ddcb31de9bccbff52242e1a7dbec3324a59fcb5ab765c44d340bec93bbee8a978d09db354c86d45170042dbc794a86aee7be1b5d4685e1c27ce9010fea4330b2f62d50e29d4ec28bbc846cf76464e51bd147578f77c09584870ec7d0ad9e2944a452b1873873d69f10e37f4e8dce514f1cd874a70b2925da7ff2df18b1a741d3ff65b748665ce2e19e44203eeb51bc9c63c6a80b7e12689dab21f52af799ec8f24c7c3fc44613784fd97e43a7482fd0953064d582c4670b4d9e25299b807a608e17779bdad3fd01611bbd6364afa8850b786c0430cd3053f30eaf6de9878648fa9ef10415aa9d85b12c9d02c8f8bdfb189c9b00ba96802b172cf85d63050080ef32d56f62e624b77c9ddcf3a9a267372033c42eb42cb4c47d3534fa5dc1232ff406da8e3ec0932bd165a086724642768a31b24ab5615ad45f5fb2417dd185242994de7821e3a6ade12a6c75a240f7136ab01ad66ce5b29e977c6dd9972ee472b0f25e3713be2c81a5240828fc972323a077d24ccba691862a1619ec13903e8745cfc34fabf01dbd5559f"), fromHex("1703030039cb557d163641a137ff7939458b1335c496d39bbd16e6a58e381ec913744646d04493063c80c7ec2baa2e35018e5e7ebe0ec1449705602cb364"), fromHex("170303001398ff4204dd9177e3836406ac054e5dfc3629c1")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("e7e01b20aa6fda5d337354a4ef41b2873e572648f2fa41690d6e4bfa45c64fe6"), sessionId: fromHex("7982184b49a0d5bee6acc2a827baa60e61adc3aa5d67dcad1f03450a14844711"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("3f47aef6fd3ee9548905e68128aa4670fcc02dbfa53ce11e03bf32124ac544a9")), secret(23, privateKey: fromHex("d08acff174b8d540b7b641409610560c2fda5837f15f4f977b3077dbf7d423e3"))], pssSalt: fromHex("28640d0057891695e441132ef51ba07522670b97886b58a78063f78d63ecb56b1de13ce9adf13dcd00c1e7b1f94984c753e413e5f809acf790c2bc60d55650b4"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-ignored-alerts-at-the-limit", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303f5275fd30558cec845c7821353608cf06ec1ecca064fac51a94a838fac72f8ec207982184b49a0d5bee6acc2a827baa60e61adc3aa5d67dcad1f03450a14844711130300002e00330024001d0020669699ce6d5f6ca73268fb7b50b5775be1a50ab3a92b686263c07717c243bb23002b00020304"), fromHex("140303000101"), fromHex("170303020701766aa1286eebc359d1bf71231ba1c40af1c8330371da82b1010929584ae3cca5c0b54ca8f82dc10c6dbe19646b539631b4119cae85422614b28065dda7af7936cb3120499192c1244aac44ae10f517de0e9c5b210e91c59f73bb9ca7dec93085978a48d7c814ad864dd9b173500981e22850cf2b1d9c0c68f30a32f702b2aa5e3e7401d6f6cb69ce861ecc414aa8866dc074d398820f16a0128a120258e07a68333761d684d027a72ea9c7bdea93ceee519fcae06d6bfdd731e5f07ae46573a2e8faa698cef5ce48b1c6d8a5c640fe77fb93b73c4e27b16d374e87b41c48d00bdd68b57e0fc9dd23186de58e9cab54118b592a32b48f6c415a9e7d3a41a83536b7010f2a4c2aade9747d8fe698f83aed6bf4a96d7641bf7ccef2d7af27f0a5b86d004954bfb74969dac4fb46d9eb50284d85466c2ff864e9c805d683f50fe8244793e4c2c3e51f93f0bfb4139c720be6cef767da829abc6ed55b71d19f118a1a3b9a653238c2e12a03f08685c504e0531a2b6c9690df79b52e494015194daf1ea72db1601a23369f5e6f9f1de08b1a6aaba84df0e5654da7376fbeebbd9f17368b05e64e087c6efd52a993446554f1759da34305eae0d7691fba033cbb8a81e7b849b77c3d277b56f396f1eafb1609e3d94df5927a12d0d1e18c849d79e574cb4dd10fea5bb9d7ff64ea82ede2db54767ad58e4e2e44eaf3f135286979f2314f756d2c426680"), fromHex("1703030013afc57c94e72dfcc971042223f29109390a53a8"), fromHex("170303001320779932b591aa96cbc6992fba25ae44cc6368"), fromHex("1703030013025b99aafd33fc47c73c9e58b692152da3350a"), fromHex("1703030013983d2941f6d4ebf0c19076903045145133d551"), fromHex("1703030013a9fc025432fc5adc5a1c5c8366ce5391aa69fe"), fromHex("1703030013a0bfa262a7f02e08cccc214069409e46b75543"), fromHex("1703030013a965f7c1c58601e34bddcc86797db642d28199"), fromHex("170303001308151373f364bb7c0783c5826a555c2be1ccc3"), fromHex("170303001398235ad5de3b33a3cd8f0fd0d3a422100fee29"), fromHex("1703030013bff7776850b7a49da8a4277f574950d9fc4946"), fromHex("1703030013076a84fd8056e7261e606c8bb5f45099501ec7"), fromHex("17030300132298e74de560307343c47a0159846ec4836243"), fromHex("17030300137974421311e2897cc818b68c2f96db2c99d744"), fromHex("1703030013884dcbb38f728092beb3131d1c6306bfb37aa5"), fromHex("1703030013ce1d0bb7e1c314ddec8be6778ed54716bed80a"), fromHex("170303001304a222626a6914a9f30168d807d18a8f335dd2"), fromHex("1703030039f1928f57c3f860f81fcf275d509ebfe597d2e33bf236bb0148a256dd22176f9ad5e92a9a4ee2f8ffadef6efe19608ea8437ae8acf2193e215d"), fromHex("1703030013cb5ba90d40955e75e2d578b8c1094bff2dc9a9")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("f3ce0aae1379ae3cb808f6a19fae8c9ea2ea5fdb73a7ffea2e0c27dcb6927cb0"), sessionId: fromHex("7a5f58c5f3dc677e0d72c96b16714cfdb1f5f1e4f02e146527886ef5be342cb7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("e6a8c0a0e2eece24d44586323942929eb9992c313ca7c1e12766701693ba779a")), secret(23, privateKey: fromHex("09ea0fefac8348791bc91132e202a6603b0f656b791b719dc854c155cb829a74"))], pssSalt: fromHex("5c0d1282c5b8ff705fea63cd821757e07c09f8e9bb376b21235de6f9c6bd18065b6852c49680e17ec77163b8b6b64fa6074ae8d1503eb7d7eed1a3a2c84094d3"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-too-many-ignored-alerts", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303a410727656a4f625814c4c9e715ac7e9ae9c82ed85992599f5dd96130af012eb207a5f58c5f3dc677e0d72c96b16714cfdb1f5f1e4f02e146527886ef5be342cb7130300002e00330024001d0020d4e0efbe486e4a4bc9046795faf87fa4ddb1256bca1a01d043101727a34d2f34002b00020304"), fromHex("140303000101"), fromHex("1703030208e7aa050dab4a5311e991da4e6b41859d320642294f265b56a50bfe6fc7bba9ef6f54338997e16f5ad7a151980fc4e504796e785fe6192dbedfd155532d606142d39c0e6279ab9e81186a4cc15a52ce35013943ed0ddb54cc81eddaf1084126705a6b685abe0c18379c280e9ad1114a24227d6c608bf693aace6d4a24875eb8c0b0ec655d3293efd55a40ff3efd232a5425e92cafac70e3ba8348011fab7318a6095200a9915ea647492c4c422d441ad3d47fd6e36ef1ccdcdcf3124a2c6dbd2952b8fb12dceaaa90ec1812f2700f523e461279b999e6a76a14ac2c6a60041fdfd5a8314e68db7a4112ea8bb0f869e5312cf6e0ae414ad96255f0961ebe715fe2e5261c9926f9f36396b099e0722738d52b1a8685f39e39ec69940847d6c435ae40655678efec638ad70acf923e9ebee7e0bb6abd5df68a445336e620ea5a75cccd53aa87f02243c7ce0581425499ecfcc05063f616c84647d181c362d1c96a5ea769354ca7ecd3feaaa25725c04b0c30f9fc43610fc1bb712c13011ea6dd74857655ab43c09486c36315434b24fab7b8bd2b84f958eaaa8f31900bae63af74e043b021598a5f95efc1a2c7a1f683138990fc98411add8787b4294db17ada8c7e59d0f770629fde3fe745fa62e11f2025292b07a39da35ed2e79da6d9a393b12db103d4adee0e5cb315f55e387b5e287dc3e32625f8209a18a108218d101e22ee38053f4f699cac5e"), fromHex("1703030013f8828da8b9671b298796ecfbd8bf8b013b20e6"), fromHex("17030300132d7ef623764933801582e16b0f8d3aa5481631"), fromHex("1703030013aa512d976b045abcf9aaa6ff55a43bf69fb2d7"), fromHex("1703030013bf87bff8cfe242a203103256cdb77990b31aef"), fromHex("1703030013a4f19deeeb205748ccb8640ed581b548b46f0d"), fromHex("1703030013d46f4c087083a17011c9ba207385027335eaa5"), fromHex("170303001322be486529f6dbced4e6ffb161a81bfcaa6211"), fromHex("1703030013b7ad13a20faa3651f134f2d8408c38f6b69320"), fromHex("17030300135c5bacd24b5af62b63c605dcbc689124cac497"), fromHex("1703030013e696153ab6a0a7ea98f88eb1dc0eade3a3f4bd"), fromHex("17030300134586f2789cd0c4f89c1e2289311e6c069dc27a"), fromHex("170303001367bf561c165c087b42b3746779c445e4728882"), fromHex("1703030013ab3a186f2043617c7968e7674aff8369349019"), fromHex("17030300136572678f4fb63fe4a65c78f7930e4c2484256e"), fromHex("1703030013b5b7e762b6a540c041cd9bd07bef9052d7d27c"), fromHex("1703030013596318564540a33845ac4c2e5dc0e263c2499e"), fromHex("1703030013967ee4826750ca18aa54e785c0680880d24be9")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-change-cipher-specs-at-the-limit {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100c6010000c20303071d847d977f79d289a4ebdfc230111e667d4ee69b000ddc07446161570665d1000012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002061b7af5400c94fd7d8b2cafff45f6e3d0793f161ef22adc2de31ffe433e7ab0e002b00050403040303","1703030035a46f6e300097f307c7a71a0212ec168d4d49ac0b533b5b2a1ddadbe98bec713c215ac20b4cf56e29acd4b0df59f3febb5a220f5b32","17030300367d9c55da8b865da747017aae13e8f7f84289e4d4a1dd7858345ca5e132d6fe5fd7805053c52048b9f6395a7c2472bbd8bb6062fea9dc","17030300135704562ccb1ac9bf1d8aa294fe9401cf4f9915"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-err-too-many-change-cipher-specs unexpectedMessage
client13-change-cipher-specs-restart-after-a-message {"state":"connected","version":772,"cipher_suite":4866,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100b6010000b203030f7de7dbd9788dea6b08130449ed6ea4c9771c8a5aee1fcb6f1e084fcb3a240500000213020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00206cad701313b91a094152aa3729ce2a9cfb28c02752e80c59769475b7e784033a002b00050403040303","16030300d7010000d303030f7de7dbd9788dea6b08130449ed6ea4c9771c8a5aee1fcb6f1e084fcb3a24050000021302010000a800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e310033004700450017004104a760ce50a0560e65b265adac0385aca2b80b9a935a42085608e7f17ca4a517509038b955391167f031680f05291e3561cc9f5b89dc1228c466f022285fe72562002b00050403040303","170303004512531ffc60a947a0e46bb0c030e749441ad6c325e8370e4cebb5ae165111f37d1fbea05f0edd08a21c049d77fff7d8f2b887c20d14284b6b3e1afef10bd210f026ed563c55","1703030036c659d3051480b25df50a7e03bb2a2cf56bf97125e1ce911d1b040ccd9e63815028a54154889217fd442014efeec96352f76a8461308a","17030300137afd5f10186f665a592e6f4d5c0c0d3419eb47"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-ignored-alerts-at-the-limit {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303e7e01b20aa6fda5d337354a4ef41b2873e572648f2fa41690d6e4bfa45c64fe6207982184b49a0d5bee6acc2a827baa60e61adc3aa5d67dcad1f03450a148447110012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020b4e6d3aa432e5caceaa9d932ff45b0daa1dd13e3134b54a5b535f2e1c2dd5f76002b00050403040303","140303000101","1703030035aaaf1105a99bbad167de5c58c1f6c74d65b2ce92edc7e75e9bb899ee951defa8ee2020ba3a0b606def0f45d115679fc4933b13a131","17030300369f05c1d3f4f5963889c32cfbe14db898b5156a62058e476cad33d80e4c9d113050ff444dda928a93ecc7b1b71b5d2c86bca112c956f1","170303001355dfd522888a1be1e136d0cc752464319825a3"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-err-too-many-ignored-alerts unexpectedMessage
```
