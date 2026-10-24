---
feature: tls-handshake
status: stable
keywords: [tls-handshake, TlsClientHandshake, TlsClientReplay, tls 1.3, client, handshake, state machine, certificate verify, client authentication, rfc8446, rfc8448, tls, crypto]
category: crypto
---

# `TlsClientHandshake` — the TLS 1.3 client handshake state machine

## Documentation

`TlsClientHandshake` is the TLS 1.3 client (RFC 8446) as a state machine over records, with no socket: `receive`
takes one inbound record, and the `TlsSession` it exposes through `session()` drains the records to send
(`takeRecords`), adds application data and close_notify once the handshake is done (`write`, `close`) and drains the
application data (`takeReceived`). It is `module` code in
`stdlib/`, so a program outside `stdlib/` cannot name it (E3088); the cases here are library-tier
(`<!-- tier: stdlib -->`) and reach it, and the test seam, as a stdlib source file does.

It follows the canra `tls-handshake` contract. The ClientHello offers X25519 and secp256r1 (X25519 key share),
the three TLS 1.3 suites and the TLS 1.2 suites, in the extension order the contract fixes; the server is
verified with `X509.verify` for the SNI host and its CertificateVerify with the scheme the server picked; Finished
values are compared in constant time; a CertificateRequest is answered with a Certificate (and
CertificateVerify under the client's own context string); NewSessionTicket is ignored, KeyUpdate is followed,
close_notify is recorded. A defect sends the fatal alert the contract's table names, protected when write keys
exist, and stops the machine: every later `receive` throws the same `TlsHandshakeError`. A ServerHello that
selects TLS 1.2 is checked against the downgrade sentinel and then refused with `protocolVersion`.

Randomness and ephemeral keys come through a `TlsClientEntropy`: `fresh` draws them from `Random.bytes`, `create`
takes them as given, which is how a case replays a transcript. The two settings only a replay of a foreign
transcript needs, skipping the peer's authentication and sending given ClientHello bytes, are not fields of the
machine. Whether the peer is authenticated is the
`TlsPeerAuthenticator` it is built with (`TlsX509Peer` verifies; `TlsUnauthenticatedPeer` skips the chain and the
signature), and which ClientHello it sends is its `TlsClientHelloSource` (`TlsBuiltClientHello` builds it;
`TlsReplacedClientHello` sends given bytes). `TlsUnauthenticatedPeer`, `TlsReplacedClientHello` and the
`TlsClientReplay` that builds the machine for `TlsReplay`, the driver that runs a whole transcript, live in
`stdlib/TlsClientReplay.maxon` and `stdlib/TlsReplay.maxon`, which no production source names.

The vectors are the canra `tls-handshake` contract: RFC 8448 section 3's 1-RTT trace, OpenSSL 3.0.13 transcripts,
the reference client against the reference server, and the errors. A line prints the vector id and the canra
expected object, the Trace as compact JSON or the error name. The canra `ClientConfig` fields a vector omits (the
RFC 8448 vectors carry no randoms, server name or algorithm lists) take the production defaults, and a zero random;
the printed ClientHello replaces the built one. Every one of the 25 canra vectors of this spec has a case: the types
leave none unspellable. Behaviour the vectors do not reach is pinned below them: thirteen more transcripts made with
the canra reference server and checked by the canra reference client (a cookie in a HelloRetryRequest, KeyUpdate
without a reply, fragmented writes, the record rules after the handshake), the ClientHello each host name form builds,
the production entropy, the alert a failure sends and the record layer's refusals, the alert's encryption under the
application keys of RFC 8448, signing under each scheme, and the names a program outside `stdlib/` cannot use.

## Tests

<!-- test: tls-handshake.replay-rfc8448-and-openssl -->
<!-- tier: stdlib -->
The RFC 8448 section 3 1-RTT trace and three OpenSSL 3.0.13 transcripts replay byte for byte: every record the client sends is the record the vector lists, ClientHello, change_cipher_spec, Finished, application data and close_notify included. The RFC 8448 vector replaces the built ClientHello with the printed one and does not authenticate the peer; the OpenSSL vectors verify the server's certificate chain and CertificateVerify, ignore its NewSessionTickets and, in the last, answer its CertificateRequest with a P-256 client certificate.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("49af42ba7f7994852d713ef2784bcbcaa7911de26adc5642cb634540e7ea5005"))], pssSalt: ByteArray.create())
	let peer = TlsUnauthenticatedPeer.create()
	let helloSource = TlsReplacedClientHello.create(fromHex("010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), retry: ByteArrays.create())
	report("client13-rfc8448-1rtt", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303005a020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304"), fromHex("17030302a2d1ff334a56f5bff6594a07cc87b580233f500f45e489e7f33af35edf7869fcf40aa40aa2b8ea73f848a7ca07612ef9f945cb960b4068905123ea78b111b429ba9191cd05d2a389280f526134aadc7fc78c4b729df828b5ecf7b13bd9aefb0e57f271585b8ea9bb355c7c79020716cfb9b1183ef3ab20e37d57a6b9d7477609aee6e122a4cf51427325250c7d0e509289444c9b3a648f1d71035d2ed65b0e3cdd0cbae8bf2d0b227812cbb360987255cc744110c453baa4fcd610928d809810e4b7ed1a8fd991f06aa6248204797e36a6a73b70a2559c09ead686945ba246ab66e5edd8044b4c6de3fcf2a89441ac66272fd8fb330ef8190579b3684596c960bd596eea520a56a8d650f563aad27409960dca63d3e688611ea5e22f4415cf9538d51a200c27034272968a264ed6540c84838d89f72c24461aad6d26f59ecaba9acbbb317b66d902f4f292a36ac1b639c637ce343117b659622245317b49eeda0c6258f100d7d961ffb138647e92ea330faeea6dfa31c7a84dc3bd7e1b7a6c7178af36879018e3f252107f243d243dc7339d5684c8b0378bf30244da8c87c843f5e56eb4c5e8280a2b48052cf93b16499a66db7cca71e4599426f7d461e66f99882bd89fc50800becca62d6c74116dbd2972fda1fa80f85df881edbe5a37668936b335583b599186dc5c6918a396fa48a181d6b6fa4f9d62d513afbb992f2b992f67f8afe67f76913fa388cb5630c8ca01e0c65d11c66a1e2ac4c85977b7c7a6999bbf10dc35ae69f5515614636c0b9b68c19ed2e31c0b3b66763038ebba42f3b38edc0399f3a9f23faa63978c317fc9fa66a73f60f0504de93b5b845e275592c12335ee340bbc4fddd502784016e4b3be7ef04dda49f4b440a30cb5d2af939828fd4ae3794e44f94df5a631ede42c1719bfdabf0253fe5175be898e750edc53370d2b"), fromHex("17030300de3a6b8f90414a97d6959c3487680de5134a2b240e6cffac116e95d41d6af8f6b580dcf3d11d63c758db289a015940252f55713e061dc13e078891a38efbcf5753ad8ef170ad3c7353d16d9da773b9ca7f2b9fa1b6c0d4a3d03f75e09c30ba1e62972ac46f75f7b981be63439b2999ce13064615139891d5e4c5b406f16e3fc181a77ca475840025db2f0a77f81b5ab05b94c01346755f69232c86519d86cbeeac87aac347d143f9605d64f650db4d023e70e952ca49fe5137121c74bc2697687e248746d6df353005f3bce18696129c8153556b3b6c6779b37bf15985684f"), fromHex("17030300432e937e11ef4ac740e538ad36005fc4a46932fc3225d05f82aa1b36e30efaf97d90e6dffc602dcb501a59a8fcc49c4bf2e5f0a21c0047c2abf332540dd032e167c2955d"), fromHex("1703030013b58fd67166ebf599d24720cfbe7efa7a8864a9")], writes: ByteArrays from [fromHex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("05b0a298c4e5b82d48d16e377de4837dcfb5982d34fe9529091ac562a60d2bfa"), sessionId: fromHex("4c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6d4e9f796ef00fa9efc26d433dfc14c685e3da46dfc3f7fe751c41bf89917ee7")), secret(23, privateKey: fromHex("aa3cccff4070ae0755ba31ac006ce61a194d7d0c034de4a5f1762d8a22615dca"))], pssSalt: fromHex("c86de83163ccf08bf15afb2607a163e9a5689177a7cb806f71650a06900e1c627e9f0801684a2001898fc3061623124c95303591dd227a4f682f24394feeed54"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-openssl-aes256", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030386d47a7858f5f29c8320d651797bfe719f680691bd02d315cf6a2c06d2e25d30204c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7130200002e002b0002030400330024001d0020b4b9e08782df18e3c041c3d9748d9c37e8cf746c7517cdd65867a5e245581168"), fromHex("140303000101"), fromHex("1703030026f0ed379b3a506d628bd74668128b605c2532eefd643f342f8420aedda23bac59450ad0cb6797"), fromHex("170303017b7dd0c631775aafc33fb5f88a4dfc1170ac170ee299d822ed2300615349afa4e4a8f91552b581d56c6f4618e7bd6ea0d9c73de4687a08b53c0940b1d5ba5d78d8aece653abbc4b97881cc3bf522d721abbef7ecd50748b6e722c9a02567c01b12b7103af1911027061715d5c5077749627d7523a638f602551166cf4ef1d1e981d3ef7c0f4e1ec40390849e6fc31fc1235d19ebdf05bc477b96a82d00e2ee21862683bf81e2a8c41ab60f4f0bb911d83790f6f7f1824ed0d2337b6639138780781c15588ce1487fbb94467b62f04f0d57ac20d15a79e244965a6b76257e1b5dba1fc9408f06232325b4465deea511ab63f0f0d0db52e3c5dab1286da22e414b869d017b3983da062a11fb0f8732e9e84d8b180cfcc943ce29b04abe4165aa8a1799e4d412514fe4f5775dd3083465b25728ebea725dd0e8770ec88e7ac14c8abbfe9c7e76a427893ae095d9c40702dbb357acf22401d40ad08fb34ef0221d8d7588cc591ba6948275fa52ae301bf070e4c9c473109141c142c7ec61"), fromHex("170303005f468cf21b230c87542f5f7cc8daf2a0c234094f88d848c1d4d5643e30b0a76db00fe6a78e566d8bcf3a39b7bc94b83c7cfd5775feaa39a443a95371eda30668d9c3d472e2266a50c2eb8775e83f3a7404514a55b494d7e2b1bd18b12c2d5cf4"), fromHex("170303004527a7d62928975652354cd50e2b8b9ac7185004f5d03b9dc37db6853546f128daaac0720746748959b6462b7545bc4e6d65c9a04602a88bb685d4d54d61836143308b0fcac7"), fromHex("170303010aacf2144b4e224a2c9b5882a5bc8d67eeaa5f7a855fa33fbed6184c202636cfb6df42815d361bd228ff3db6505098035135a83b7ca4b93521a8d62b48c5f52b9c326b961d53d9f690bd039471f859ac266ac81d7ae017bf8b1a4d98ecab8be2ce1d266e5e84541aae48e1bfe34d024c952859e124bfea6b09d93d3b527575441c5beece80f4d3a37f9b1c43cf7aad6708a4fc72989dfde3712ddf31d4abdcce1dd76dee4182cad0a191056a4038bc8472941bc2f21afdef66a13599a1b40d9c96650ae37b6b5254f2047954bf0dab72757e62fc3ec7bba28e5f2a2ba7114623a3b17c6ffa4fadece8db59c471c1613935d3fbc6525b177a942b2a9fa84c0b734b0a9c2ed80ad1d5fe3677"), fromHex("170303010aca566e8b729ff7fc10b5ac3a9d60a3327586a623422ef419f37ce70341ac5aeada6c94fa1d7a66d3ad3b5a479307c8dfe1014ebb4392d49507dd0ac895632cb13337b5c4da74c58d7dd1e1c85dfae620ee19e31b3c6b749eca5bb30ac6da399e74f701e76283e8a09d287617339fd5ebb05e2e9d9be0929050ed7d04263575b59ef7607ae89722bd4db302c891e30d03953cc77b1be4241b3087b3dbed58e955a68fde450a29923b23feae03732958d0624f96a1ab598800b6c294b615492e231f0040eecbe30a1daea04b75bb4338c09db5e4a1346fc85892ea9e11945894c1828267927290032938ebdbecf0259992b660639f22a1a0c48fb8106065f81c08a26de3f73f3f077aa5ed"), fromHex("17030300234731c1ce7f01af6c7f24a4b9bd8c01818c69ef9999d5e070f01a72207c764559f8b5be")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("3d82a55297769dcbfbd648f4fef03bd239dfa92557326f353a9dd122b62be08e"), sessionId: fromHex("5e8e3d454fbda55714dff42cdb0171dab19a445b27252a7446a9d8535c994950"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("fc9140f9c523231ac6a9606b750fac8cdd0f491046fc54a482882f15f78ff719")), secret(23, privateKey: fromHex("fc7fd7c65df4e3590f6b87edcb14bd757718c4a2505411a561f3062eed47edef"))], pssSalt: fromHex("8966dfa69457ced20809053100d793b8aacb31f91710b241f657b83d92be25937b5f36b6f14c97bcc6038f11374f18a10ff0aed49f5cfaba821395965efdb5e8"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-openssl-chacha", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303f2117efb7d4780e48a9b0af1a4a180436e9ac4d2ba2456a08653ef450e6e1a0f205e8e3d454fbda55714dff42cdb0171dab19a445b27252a7446a9d8535c994950130300002e002b0002030400330024001d002036db1c865aa5e48c9629cf47f13604949b0029295d21ff0b896d8d79fb153a16"), fromHex("140303000101"), fromHex("17030300261d021c139b796ec5f71c04fac5c0975b07b0940683f9d4485be35845e3615d4f31ed8243aacc"), fromHex("1703030247397514fc49f9c60416cd28ee4457e2397b2b585b83c4d48ee78600d4919519c30190547bdf949902f18bcd24ccff8be97059e0564f7ad6d91603a37bc7abd4f0f53a7528e64d9ca7a424b22a894e378ca70a3d2ac3fa5d4bfea8d261081659565aed1649e1e9852bc69eafcbab85cdf33e50100f5d835055d7b53ae3cbcdb1c05d0dbbacae0f2497d69153ae79b0a09b39726e226970e93a0d38c59ad55e07491fe079cfcd548ff003975765e11361ac97b0abe997bd9d5f38995deacb7feff30a27db3ad2b95c0474521b2ff6b7ebcac09e2861d5099efa08e67a8dc762c2048b6931d544cf691334a6dde9e82467840c96979265f1c4548da4c607473ac0d57386ccb2560c817b412d9db32ed23cf082c818fbc0e6c0747478fa5d9a94a3f64b3bae56c6a699538f3f8aa5211b6b2ddd116d23b69ceceb105d8d31686e949cc488cd664f1e9fa75ff0c5c7773198f93558ff1698ebf7ccdf91f6614c155bd9c8232a939dda2c77161b72bf4735de0ed00a173324c371009382f20567473d7afb47fb17ec70a27aa7ad4b53dc67a01212e8fda67d2e03bf68ce169d87871a9d03e10e148be4db9decdb2abe1e4876ec0e68dc2c60b8ee188c90f9eb17879828067268d3651033bb37a9dba24638e0fbc15752dcab5063ad98013e425327b160430a280a5af663e8b20f6121ac2eaa43e0ec7197f4a86b15f44a78ad7aae1bdd16ad5538029dfeb8abc30a532f199192938f76e7799fdd7a5de05d58b46748736c27ad20612ae9c53e29e75d75442ee9f4286fc22b2c92c40b11e0f6e6a48524db62325743affc"), fromHex("17030301197f11efb13df34ac8fcad984c709056677f3491d0a21e50f51ab8fe5aaca143628e15b22afd1210f5346a38d643ca0f1cc0e97ae8c4ea5137bc609e526ce27138ac4bca781efdc7c472c0be57300e3da10b35cfed754a757beb430035c4284f885f6f53f0174f53476c0806f38c0385cf8365d85380a37d21ab5aca43cf80a16a15851c0c46b91c59855a9dd328770ce13c6c4212f17afc9cd1c90bac0d58cf40069c1a6106f0d30dae22a214e04119d73750b9d495cd256be35f3805632c608d90efdb2f49de5585e6c0818a0bf0098add73d8685e9e7a6b47e1022d221cc2c57310b319833eab7d2ec5fc7ec75c40a02f0c72cf1f15d1d641fad8ad7e98e66ff87b4331ab47c0fb6ef6478eac21d5c6ed9a2a2855bfe51f10"), fromHex("17030300350a3b809708f1fa08719cc9a2f9f2c40f319128c62e8fbd4082f6ee4661fc626268187acb90381d45ff01899a0e95903f3b3dc4712e"), fromHex("17030300faefccb083da823dde8fb4fabc21382ebee411b4efcde17a54cd85e490abb3ec927b4b273ed465bdc21512bed747f7a4bc216071c4be3af32a2874ca95003c298c5298574253b933d6d031b0d04e18f3b0a693b4387f804a1b93b728f1a4236632a9043a73a21700afa6a55c01ab67bf37589df29a1498cfb87b4f01fe89fa91300306e01fb13871e451d725cf1bdff76244b0a39b036dd8f84257264f205c501755ae6419ad82952b2a25dc758ef8124955eb37f261249f0f2b2e99e54ee8330f4be68d498b64ba5c0951aa37c0a68f32fd0004fb6812d84729a3cbda2c5bfb0ee59df6cae959242afc2a938e1998ec21c96efbff65085bc62e96"), fromHex("17030300fa7013e55f47f58997a2e870310ef0d76b35f030468912dac1448003a79b7da654a6584b6a9c2a1893eef5415ece4e3d41cfa07d6d3b11b2e414e8a592a1659e1c3dfca31d35406a4a76d27d4d21fe77e64272832308505e239ba65d0b484f782a6ef5fa80a5cb09e69e51cacb66a19406b481c8539a2753ed708291eaf107981caec4807729c075f2190faede4944d6093b0937ea21f9118b97f55e581dde5c48b31a3e6c68a0b399f1d56efcb7a17dd3e21e44d2782a1378df60ca36ba8509f7c87fe6e7fc661cb4797a7edb888794e077993ac2cd2ff0c2bb305d65b1b145f3913b81886613ae34f4da6cd1523659a85642058241bc61bc030f"), fromHex("1703030023a8f346836396bb855e7957e0c69a5206e675dbed48356f9f64ff83dfea6f35eec16693")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.certified(ByteArrays from [fromHex("3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91")], key: TlsPrivateKey.ecP256(fromHex("fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"))))
	let entropy = TlsClientEntropy.create(fromHex("e6c59deab8bda76d3e353e2f90d5a9ac5a675fde7567f42c6321bd8059269474"), sessionId: fromHex("91d6cf1c5edf1944230e1ced98ccc9799ed479c8e034c67ba8866b6b50082139"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("243b83c7dd2f8666e1c632c66af88e146ed3b04dbb54815e59d1c3495f415053")), secret(23, privateKey: fromHex("aa920524898e1f7831768439ddf6a82290616267d8188455d7b5ab64a98d3687"))], pssSalt: fromHex("34a422cc6a70d01aee4b58958a2fe8def4b4d8b5edce09bde488939ed08367d08a31ddce0b9e603b66cab6ed56f940aa06e5fda0d30a2c730d0f5792deae1ed0"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-openssl-mtls-ecdsa-p256", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303a6d31149cca13e071ed9c61261f0f2a4850d082abace05e5388430186a8d89152091d6cf1c5edf1944230e1ced98ccc9799ed479c8e034c67ba8866b6b50082139130200002e002b0002030400330024001d00207bcc72a2db096a923609f6a5ec40a5a9c77066c74358d49e052b9bfa40bc262b"), fromHex("140303000101"), fromHex("17030300268fcfa6afb99be2a739e85cfb0da2dfc32720c282d8f1094dfe33285c36d00af60d87a3b74092"), fromHex("170303003e20d87a6e72d582b8dc45bf1dfdd812669c08ad46389fade4cfa1c0f128ce932263e5d2d5d5e94b7d69d3a3cb8263a2a2b4d832e891cc3e5291bced6b3ad0"), fromHex("170303017b45aeeb18df4c3c7c86e8f2e67f60675783d2f87bff346cc11fdb576ac280d6b99765126be21db44b5be4e26bad4d721783797d6139e78b3a105ab0512bb1d290a49904fe21dd04b7077cfc113f100f8415e53b6a0ee9158917da880011e0d4b9fa49a387a154ec1a546c5bb2b9985fe4df6bc98e5e5027faa2b3ed06b83353a44713e16722f916554c3355020610bab6ef49478aa137b5517fe1e82e8ed949b6f663141edc61a1e8ad6f830fa929e20d56b09feae1e7ee8b2747ee96ef8f8e5f3ea42c5e95a1aba4bc3d7029f35668da344fdc0bd8da6c05c55238f408fab5562276cfc4c4efb389a046cd94dda10b5afec64c2142750289271bc3e00dae45498e2054351ab697233ace428c8d7394a7138f7441c8f02a7d550e18ac9f3b9ab375a6980a1ab439a53965d19bf6da6b2fa8bf3fe51249a49514bcd5385bea72bbb61451e5b8bfdca8cd3031b202f0d8f1dce7521ea8846970fa7d8d72bb210f60ab6d63f5e72b4a8340059749f0432ca656975199738d4c303176b2"), fromHex("1703030060b603dc7c9363ab3cb7951352101af2558a25753946216120e8d896e7c9dcafa61ede0a421ef93a216f5232b03850d46cfedc26a31761976082cc97c625884fba4eee8eae30529f40879b04596e96f2b2f9fab5a4c3a0bebb659639c21e681477"), fromHex("1703030045ff2174af93bdcde6d05bc4d9d420d5b9e463c8855054a7e68c31a78ccd8bc0a27a9bfb1d96a46b36d2da21075e5f8ad731b8930b86c61ebf7a607c3f8e582223de3aebb5aa"), fromHex("170303027aa78e49bfe4d29c1432563ed652196399f2a50663b96fb77cb8c5bcdefaf1e6f0fdabe02112bd0574555ece85b877c8347124bf0fcc3e0c6eeb2bc0bf42c161a954f7a3967c4f7db7304df06420f1cc356376806d8c74192fe9c925c333bc3a14b61401e871c5dda6e277147eaa8049c7b6206f291b2f36cc542334f156aa3a418d781476df615597d89a17dcf8cc761b4bd150835367c606844835ddf37294e18cdd6c3640961bd49ac02d3f4efd027f52ea65acdb35445750e52f8d3ec43804e8b5f8d2bad2df15a877dcbd7a6c3ddcb5ed701172f4bf3b52853aa4f1228353cc2d8172df0b693736dbad5a837ac530bdf04059edb7543960df06d3e7d443b0c811c16aeea475b2d54f0e090ee2e90904c4802c1307957ba8ae0e258eb81e948b3d681359a69e4ca5683796cfb1c30b3d2baf862cdc763d68bc83c3f51e2fdaed8700285dfb88de0ed2a505048ccbc1368c01601b57702d7ce6e6fafee31077e7dab56185238ba8d198190e0320efcfe6143628b0044ea81c7087359fe8fa6ad9ec91fbf77dfd7c68ab0998af97851ac034f1f738b3f1a1fce82402df034d70f9ade53f1e3a369aeeedc7eb315661e7dfd5fd0bb526ad14f875306cb8195239c3694021fdf03126d6851da5b01eb1a00566a6c3a867676d55e5e2f325530345d89f50746b183de6aed77076370ffa9393d8e0d1ffb665c1c20bf75d9b23b427536b6193f248aa7bc72196f01396beacb63363de493ac0f6c6b116ce76be71c94e5a32320a2a8a3c77e76ab6ee0e4180a2e213522e1f1b28a10883147da9a57f9425f2022cf0242d7ce25a9fc8dcd49738f46cac4e579f0a611f6d98d1fa114cb3d14629bfed69d8917ffbcf4f2ad4bb9d185670ef472e64ce32"), fromHex("170303027aab9a6a7b32af93d01abff07a90ea43931a802b7f77e3b8c587147b317b7fe68cfbc4e4ff74ec3f91bdf3b51d729955e76387697acf78aef721651ec5714e12612d37bacc8cf09d5a8ee6edfd1f60189d19e17735160ade3fa9a98a6186967d3c548c3341130fa2d9fd6eda613d223aeaf47f7a136542c64619a54011085cbf64bf9c5021e3a5524c1b84f439fdd706d6ed8b3e48309b99077cd48d1c091788915f81531d276495c3129b5bdb3a7781e71daf029b1900206d3306a5710a8e51d1d9a023882053fcfc4b0f950e48a6b0d87b525b717e9b754062233b94ff87549a96335301bc340e29bb85e4352e77ade7a7f6993a0dc80a26c630b4e09ff762711af464d2d5681258507a28c62d5cf6f7aa25e6a5c9364d480f05c65501ac90ff124b01c2760da8283f3c265653edac0acb1d011c3602c62fbac808957ce8dd135652bc7f7bb8677438c685becfaffd6d085a11b814017d5a4b59b676a7e83034af29bc97e4756be90cbbd7057ebfdd6fb7444583b19d7398256dc7c4a8b1865bf43e337d5fa1fbdb45b87b874b398758f2bc581dd079a2d223c2e13cfc05aa5077bd04ba7d227183fe7ec8d068230f2e0b83cdceedea9f679963cbee007fbe61270cd891acd420288039343c9dc4a26e91029631e8b7304f95d56d23a9ba74356be274cb1f6cfd46e4480b70134077e1a39b608dc978222a514e82ae9a3ae3df28acafc14c1fc2544937883249ea6a183ff2df9a598d0436a60e23063ca4899c2894e477e6834254a3c152d64ba4359ee20e5446cf4d90b155910957fee0468b0767f4f2d37be0b639c8191ec4e35b159ae7d9698fe60eec4b8cc25b8c6b5044413266e3b36ee18b3e4a25d1e9955f489c771e6ceb13a15efcb7"), fromHex("1703030023e9ec77c6f5f4c2172cfb98917f1d6e1ee41539ac724072c10c79ac67d728576d59f6b6")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-rfc8448-1rtt {"state":"connected","version":772,"cipher_suite":4865,"group":29,"alpn_protocol_hex":null,"outbound_records_hex":["16030100c4010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001","170303003575ec4dc238cce60b298044a71e219c56cc77b0517fe9b93c7a4bfc44d87f38f80338ac98fc46deb384bd1caeacab6867d726c40546","1703030043a23f7054b62c94d0affafe8228ba55cbefacea42f914aa66bcab3f2b9819a8a5b46b395bd54a9a20441e2b62974e1f5a6292a2977014bd1e3deae63aeebb21694915e4","1703030013c9872760655666b74d7ff1153efd6db6d0b0e3"],"received_application_data_hex":"000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031","peer_closed":true,"peer_authenticated":false}
client13-openssl-aes256 {"state":"connected","version":772,"cipher_suite":4866,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100d6010000d2030305b0a298c4e5b82d48d16e377de4837dcfb5982d34fe9529091ac562a60d2bfa204c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7000213020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00201353a9977f85ba277ac160d80e2889e181bfffae80f0d05dd858fd9f914dea48002b00050403040303","140303000101","1703030045469381a515c4d916b9ee4d976de5e9467149dc71760b55c20d1e8d254802c285795d0ba7a0dc6c57162057f41b03e222ac2888d73b9731dae9eb3f8b74a84bdc28cd00e394","170303003647960a8b7c3f50ed0ac71dda40fa99b6b1683ecbefbc1d29af840036e272d82f5ed0db62d91e5a340d9fb71596bc9e3d7bb4d2ee6266","17030300132fb70c678c0109a0279c5cd5c3e5b3e064f2c3"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c","peer_closed":false,"peer_authenticated":true}
client13-openssl-chacha {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100d6010000d203033d82a55297769dcbfbd648f4fef03bd239dfa92557326f353a9dd122b62be08e205e8e3d454fbda55714dff42cdb0171dab19a445b27252a7446a9d8535c994950000213030100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00206721ff6a8054681302300874b1fb6f00bad1d583b77852ce87978e018380d835002b00050403040303","140303000101","170303003535ff4a5bbcf888f3126fc1843299dd7bc65f26e7244585caa8650f3e982023d1197b06705ff04fee4098a416a985ea1b8c4ad5dd00","170303003642eaa10062913d2edc70b4f34e32277f4ec6e918dcc6b7e61b6e6dfd8dc6d04f2a9ecf4a318d546807d1c8699130b868d973decff43a","17030300135061030b7f4436ff24d6393712d4a673aa0c49"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c","peer_closed":false,"peer_authenticated":true}
client13-openssl-mtls-ecdsa-p256 {"state":"connected","version":772,"cipher_suite":4866,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303e6c59deab8bda76d3e353e2f90d5a9ac5a675fde7567f42c6321bd80592694742091d6cf1c5edf1944230e1ced98ccc9799ed479c8e034c67ba8866b6b500821390012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ed4b7eb8108ec70cb450eb9ecffd6079555218999feebfc9ad3d09970d892e15002b00050403040303","140303000101","1703030211471efa9fc738d607c1219582e4da013d1d7de508b59253744eabe732b2a8d31414d83b04b5f7d2f318d13f25931b0b2f669c824ad3e93e7b745308336225b94d679fafd96c6fc0a4f2a0116b7b5fdd8b32101ef62cb6557a6d852d9e5274cc0ac322c43800783eb634a8af54fb914685b15b0b3d52b9446a4bce0c16e25e743d7486f9d91d4e710c96027f9197ea4c233ef6b6631af0c2d83677b897fdd5a9fdd1db5071bec940657df9cd022e5bbd571bd1999222f09e87f86cf5b089a694fff35a70bd760073aebca53aaa43ea32dce6d960db2690b070712ae75e2e735eb7f24dfc493e71679dc305dd92beff05fc89fcdf4f81fa25805d35bf030196218505bb91a06a499dd1330568345bd16f2fb1eb63f7af48e1bfceb1709d2c6224204bc747149c9d567308d55a828ae4aa51f31182db62a1d402aac3b8bd5eb6fd19256ec46c3a0cb137b2eaf5be719b8e13e990975102f2eff748214ddc129c28add0661f53f2890c7a1ea732f2a3958b56ed71c91e9cdbcd8234b68425079ba4caf2912f9424eeb8bb9fec82b0401734159048594e176a52bd618b5f4e0c6055333239f5f67debc5962cece5dce27a271a526c07b6f98205b1c4e4e5fb15e31737f55ca3f160a004d120063dff9c2fc633524fa4aa347af38488b911faa1ea1c405859c3adccd828cb4fddc32321668c31f1db7c4b555d1e4ee4c762bf8aef14fe6ebb3f501b2b240cbe5a5c4c5ce81ca35c","1703030036c7feee7e892ed23f1b25ce9fea0f4a6dc667223b19b682c48fe520d365590091c5c33b95ec4a3c8b95a58fd1e0a2bc65e39c029323dd","1703030013bfb6870b861e6c84f06b93b09cbbc6a046f2a3"],"received_application_data_hex":"68656c6c6f2066726f6d206f70656e73736c","peer_closed":false,"peer_authenticated":true}
```

<!-- test: tls-handshake.handshakes-by-server-key -->
<!-- tier: stdlib -->
The reference server's handshakes with each server key type: the default (ECDSA P-256, compatibility mode, ALPN), RSA-PSS with no compatibility mode, ECDSA P-384 and Ed25519.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("8d8670a91906f69d0eacd79ebafb21cf976caa11524bc069ad5836c8cae5351f"), sessionId: fromHex("63c14da4cef5a84f601dc08d20a783f00bd03ec06674a228f859d6b8c595d108"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("e0b3d18c16536c6d5069477250866abf103e9ca3078ae756aaa3afa418cea303")), secret(23, privateKey: fromHex("fda9992b5f9462ace06c78ec3e99130cc3dbfa4a93de5f94d8a11bef9a34112b"))], pssSalt: fromHex("ce73faa225a8d722bd8a7c4dd2450b62349e5f678713b20018027532cc6276cbfd76e229366b97ba041ed415f8601737ea39c52f83cb25e0ee39785e92e79b99"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-default", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030380a6bc4b75610684eace10c3ef78b8e7767b970964ee7dbbc74d2f0e313a71f22063c14da4cef5a84f601dc08d20a783f00bd03ec06674a228f859d6b8c595d108130300002e00330024001d00204659ee4479d6c00d947ddcc5fec21c08fca6c0dd92eee426df263fc681595532002b00020304"), fromHex("140303000101"), fromHex("17030302072d416720dc157045c912442fdf6c15815b0cbc2d6064bb9241be39291f042cc4fdc15097b3bc5a80d304e3cae94a4783b70030c1dcd356c1c3dccafa02aec136279891c5bea3c92928753b9e6d34a3f44f5ee1cc6dfd24dc4fc04e00d1a0e6649985d667da9af311a279ca06d21b218950eb8929e80a222edfa7657a3571d8515520b79f3403dc6aa0ef451ddc1702d0606fb8352024f3bcfb9b9d446370919a8397e93744779f0851db3e8f11ba104c315c1c1fa45240341042bfa05934477a6bcded00b12b1779f7a0c2d42795029fe8e7eb65f48a5fd3246aac9d8af2ab76f55affe159d70830646f8c90db918aaa4338534ba8ab3980b7725209943469d5337af2d1e505df272abc7aaaa45a68fd7883751b82163c2a27eac1c7475e6c9246d6fbf85527ec1de177534aa595eea1b37ffba82e17242844628d00a8b17ebb54e9abbc4dc33b9667d2fd82f301ddbe0be012f7d61b01b24a977f93fe272ac932ca3c37f4a1ce87850481659029ddb778e12985745ee16133743ae685f8a2230e6fcbcd767251c5a22c119bbc8490d032bd101ec7a0328ff59b790c32c68864c998587a68b6cc2c7570c586275f13cc3d1c37bd88048d52ccd39e38f965bddf6385d40b1130d13b3b60001c412a231be678fafed054b8233cfec8adb3c2969f2fee993073e6e211476810be3900238ff35aad1fe22f6f59e7a4cdf340762e97e5b0669dbbe0cb"), fromHex("1703030039c5ac7968a52bcd5f7a2c9f07c735347dbd27db8e535d20c893e76dc7c69d0151bccf475aab5f08240b20120a03a4bbcbc4abee0749ff50009e"), fromHex("170303001344cfbc9b6b841a29e7a9da5aa49ef8b7551aa4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("7332a97fcfae36543323ec66893354088697d903e20d289758bb89e434a71270"), sessionId: fromHex(""), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("4768e759b6fc148808aee8a74a61a3a05d6b29d28904e2a4dcd60e042a8325cb")), secret(23, privateKey: fromHex("b41f3b30a71e42e269c1aea2f189a4c5c0b2bb7f70485eab6bc718c56630750d"))], pssSalt: fromHex("2780810fe718e575ab9685040860199ccfe98cbd31842a7b4432b3a27e7b232c10833345eb026aa31c126871684d478e128c98586e13f51407146d4075583844"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-rsa-pss", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303005a0200005603037b1ac364fbb76aa0501ce133557901aedb62a16debb94d90ffbadae1cde6fb1900130300002e00330024001d0020aa895accba6e557fcd4402ee02479bb02f616dd81779ee63cd4a080857bca747002b00020304"), fromHex("170303038cbb345024e99c6a70d7d7f27d3931cba44f52e510f5326d4d8a009d2497a77e9b77673add77db9810c6920a30472f5d6c0155faac8d127c73682efcd8d888ecbbd7683c154872324be17777dd6de7188664504e4929afbdc00cb9ee7d069b7ae563df42bf375b56c3ec07346731f29c7978144866203b500936d05c4682a8d44db26a6f2c993197bcc8b9df2ac40cc99c856706eb93ea5ff2e304c6b954fe8b2196d8cfef40574ba5a9a2301944bb860e15db0ce699c3c4affd0a4546c7330213d3a674ef248aa33326fab9debc177c7ee74b83e9cebd449369bef35997992d85e088a41f0506855eab84029bb9ef3ab899030110ab5208eff5d9c24147953104f2886acf1d23b501dec4e439b794646363e5f2cc1b131e51f2f65123405f6bf10b2646e096d0a7c48f164d863b9cec1e7f53e76ccdbfdfee10bb3e5b89af83e09f27437d718f44d29e3d8291829e73a1822f2e96ce2c93a2bba85b029d18bb51caff6d3d52bbc5c16a158a030a7b6e25ae85fe5273ae020b4cee64b658975a120f13c9226bc7b3f8c315470d0909ac0bd258aa7295071633de0d7d33ba9956b5ff9f41b8159e693893a6e87e14719a9946e4c6e16fb5af5b73be808f4b378b3b2f0b69b94fcafa8b039b48be4ddb869a1921ce0ca9abb700798e0abac1e7c7357c8ed3a2998c15f6ded583059a344ba4635dbd5a31d21c50e2a98ca84d51bfb77ff428583debd47fb6bd76170a077c68eec0139b905b352c0913125ecacdd5ef81c5afc3de10a7e9ac794afe298df6147c3dabc880ac5c3665dd220201da6e583061200cbff625e968f7091423545675f8ae5752c695171e740ad53479a223f58747ce2919849a7ecfc60f885a97be2e103dfcc9eb5dd2ee9633aa004779c17c9534ac17ed24dcaa9f34d5859e9ef63b805e7af26c84d0be1fcdb1e23f712dea7deefb10a5f34f4f0eea96645dfc47726d2178ed2696641b489058c0cb459978351a3bfe65bc92317254ff12255de475a450eef516a9a0e5f3f304e05acea71ed7b469b2b2c2670da37596aab07dc2a3aa6b3eb250c221e090454e229c0f9600d7f8911b473079e378652a41e6e853e99a4f33d7c313e1d29b874a330ca128bbdfe3469ee087c37e642ca3e792c3a62bec827aa71d3a9ac8770ddd956edd688548e56ff08760474c2bac9c09dbedbd6319031da36d7781779e8585cd843421d0c3b9b294bc77acac2a1534085fbb228c2d4e432e0d58683c80a9249dcc4bcb43d1fbc6a6eda735b187e8380b"), fromHex("1703030039d407d2673755490d27ded67b17e92745575ecec7f0fa51d3d1d7df3bf946e277ac078c90ed00108d663cbc8e1c85d991a2bcbf229e7f7ccce4"), fromHex("17030300138bc45cf63df7250578a8f4bf3d155ae35a6b37")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("20139af6f10baa560b2e6fdcc3275226ac3cefd2163e30dc1a5467ede44b3f5b"), sessionId: fromHex("075d83bd15d7723ef3fe0c301b6d777a5e33bc9ea6c98d6423bb740efa3c31b8"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d2edcfe86824ffad08d9cf0569af53bfc35134ca4cbedd5bc410a555bb61c1cf")), secret(23, privateKey: fromHex("a1e9553a180adffb30eea7c93d7836eeec0253f011d4a6ac66e1e89e686f7de4"))], pssSalt: fromHex("13599447e7a8cef44c7105985949dd4974a2b258380a5d4460654ee5934153500f1e89279af942c045bdade501b31297144c232cb8cebed679239db40a367244"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-ecdsa-p384", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303e335b506be23d0d0ee73d04d92b2bbf15c41242b85efe45a255b1c7ab631c25920075d83bd15d7723ef3fe0c301b6d777a5e33bc9ea6c98d6423bb740efa3c31b8130300002e00330024001d0020f64582b9553df5c5c808562e16620d3b285278aa80bd120cc3ce3ec4508ad916002b00020304"), fromHex("140303000101"), fromHex("1703030245589bea8528004bc873848adb0d087d3f121449edbb48628df09b84efd864739ac734f068d23d2c012f82e0f413a0d80aef05303fdc13233436ed8a80eba984d9b088f48d5b26bf1a42624fc20dc4297b9c04198d9648dafbe4c8ac64b17c5bbce27f22132be8077e2e330c516fcc714f6ee4380fcbda1f4fa9dabfc9987c0b2bf5c21e5fedbc5802d7f7edd78033ffb034cac05677f52b6be1403f0df627a7ff1df88368ce60a63d02ecdfd263c83b764ede6a552a4b7b54d2632b31e4bc762f14fc89e49e4c5d5da322bf922c5f045d257a79cbed13eb79adadd5c19294c19267acde9932a286e4ceb906d0ebb7bf223b7091a412147399b0bab7a85183a1d9b66ffef2b560dc9520107685be381b904ff9dda7f4107a4f6a2f8cc548c9e62e076c740bb2041310c94d370e600c80ea8a56468d1d0bbde5f0a65b56ad2742d1b6b934062dc0f694a03985af247907300a6ff33ee5fd50abfb629ffc69533bbca7fca0f6aeff751be330d3bc92f8be0f13d6c571a842309a2aa601a00d0b2a10f0d6f3246f27098c3adb65e5f7c35fb64c1a0d7772ab6c73e132c5eded8362d98b832f7a96c0cb5f829899f1bcd41ef1d82ebb96d4d85d1d2ea2a45dae950f44ec2bd2c7da1fe01f09ac2e34733c1ace0fa6ae9eba7922aade535e19d292ae04daffc9ee335268a9625c3b526eefdedf3201341b502f7002cb981bffa434a1a42601254a87bd967b3075c772c7fdc7d72267ee1c1c715c39b4107955ef0cf7b56c5f2562d72621bf93a4185cfb614c10b228ff5daa45872f4aa9428207687f18f1d1059164"), fromHex("1703030039f5343fc095259abcc4107f6dededb16f7bb69370508a18e16a9ce315019485eff60d7bfc0670a10428ac118a6c824f03f5b5aa53cd8cb78474"), fromHex("170303001352359134be761187f1b35a5612200543ac8596")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("8ba27c9ed0bc0b69a50a9822fe9633b466093e0650f53934c22b40b0c457bbc0"), sessionId: fromHex("4534183639d7f7505333c4ad386d442e8cf934877dfa02e87704004f3761143e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("ccb01bb377c2fb464fb6d2a6e3fefa726d0afd593791fff8c2a31d39c08b1923")), secret(23, privateKey: fromHex("61a0008655b7fb11f778aa195fc65ca9415b34a4205411542b251025bef6d70f"))], pssSalt: fromHex("27498f350659326ff32a4f538412f155a411b998d022d31bf9dbeee172de5efa22ba82a9f5b4a6dc84242b8287796c421065c1018ef5a7fad76add8d7bac4872"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-ed25519", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603036057317418aa85584b807ed317ece544776e2d24f746816deb90c01dfcf3ffd1204534183639d7f7505333c4ad386d442e8cf934877dfa02e87704004f3761143e130300002e00330024001d0020cf71bd0d570a1c2a3204bc962c16dfb46085c5438df46f964a5cb34ec50f5474002b00020304"), fromHex("140303000101"), fromHex("17030301d0cb08ea3def0c6a07a311683ee66bd09da7507d21fbda11899c559d7da4ca6cec89d7d50bdd4306213d509198f349e36d83ae9977eebfd74a7a2ad49efc0fb3eec16cef95fa8166407b82407c5759e114622a58c72f34b5baf5f32b292b6d5f4631e7915c55910fca8a57701ec36001536f3e44ec65e772a931006a935faccd7193b85ed0597670a404ee3761c5e49c847e1e149378c06c64e57570175827e03d494d2a1373d3b2560c32cc78e8a50a3f135538c55e8f011e4328968d7301aa45259e7901c3bab1f5d48621acb35980eea599eed8dacea1fe0e121c8e55ac3f2dd02798f4a98cc9a22d7d299d3bb75e18a59ff1a281d483127cad5229ec40b1704fd9d9a738eb001aec7542e75cf682b6f2ef5cccfe1a48b9c591df5aae018b09fe7a65a3a4327abeb4933151485c284f8a27d7352c5f60844699a37858688149fd45d803dedc455a1f59ca6d21c53c565a989d0cad3f1642dacfad18a108d751f7505e27b9789ef2d3cd550dcf37bf05f1aac4457dc28bb0e090278b6f15773c265ec8686cecff2f74930572fa12e4755e1b76cc05752921bd6a3b30ca8ca7f12b058d8b9f6ffab229fa73e5f02665f583c146fc0597540e1dce8eb98b53b8eeabb32d5a14d5a2c727da7633e01a5f56"), fromHex("17030300394409282d2bde5daa45503080356c53832a65c6a5f27bacff73992f07251aafb9ef3ccb18a4ba71eb0ef7a116c501cb41a62923898d61d35639"), fromHex("1703030013d0ad917bf0ce279fdb97b10a9ee23b4ae59722")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-default {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e203038d8670a91906f69d0eacd79ebafb21cf976caa11524bc069ad5836c8cae5351f2063c14da4cef5a84f601dc08d20a783f00bd03ec06674a228f859d6b8c595d1080012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020b68b7052409ee184d5e4f1c92d3eeb49ed2e067187dc0f56678b565342e96e1e002b00050403040303","140303000101","170303003565cd17e5c7fedf4b5e978da8b4328c8adba8acda955281e6727a2b56bc95d3a65a4fc10862ef51b8cb621e98f5832764df9e7ccd64","17030300368d50a95bc9d55adeea13fa3ad3f452103d3e7f881e40c71c40a072229cd35193800a9d0ea6b71fbd41025c64a73c4dce9f3a2290f96f","1703030013f0b1a6d466fc6f41fe69f0df2f0551860926af"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-rsa-pss {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100c6010000c203037332a97fcfae36543323ec66893354088697d903e20d289758bb89e434a71270000012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f1699a8239b727ac423c807a82955e87fd4adc9566068fd0a47d88f9b1dea526002b00050403040303","1703030035c6f54b6db84dcef41f78aa17a852d07434a7e157c418d23e54f3df3cf64cc687e3e0fcf2cd64319430407ef0d017976f1d8adecefe","17030300363aa96e4bf167d201807416ee2d029789436c6919736047ea34fa67e972ec3083a533df91a705ace0aba32bf10b2d7adc27afc6f9038d","1703030013a00095464a469796ca27a19b70aa74258add79"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-ecdsa-p384 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e2030320139af6f10baa560b2e6fdcc3275226ac3cefd2163e30dc1a5467ede44b3f5b20075d83bd15d7723ef3fe0c301b6d777a5e33bc9ea6c98d6423bb740efa3c31b80012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020e42c5325f5f6f1aff4e23f8882a41c7fff95244553b17b1016d2ff730e50cd0c002b00050403040303","140303000101","17030300352abfa552574fa93d43db39544fb6be5e31a28d7c4933824af493152cd4983d3d97a8ce1d185f8357f950af871e2c60d7a6845b2069","1703030036e364183661634d91ec3b522a6946a19504d5c8852694011dcd1c15aa0d134d39fd8f7432e03f19d74be4718748891152e76de6ffd56c","1703030013956f3431859c18fefb3ee609e6f32e85b02543"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-ed25519 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e203038ba27c9ed0bc0b69a50a9822fe9633b466093e0650f53934c22b40b0c457bbc0204534183639d7f7505333c4ad386d442e8cf934877dfa02e87704004f3761143e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020ca5fd322e8901762fb0005363b43230cb8bed3e565a6c7083591d33fc176ff77002b00050403040303","140303000101","170303003528694c5573c296073eb64e6b4d1819a9ab50a906ec231310914560da26b02691a0704048174280584551617218a91b6afa0af805a4","1703030036f01fd6d1271e3588a629114a97ad6947d52d7ca93c981b5a3088918c374c855eb1e8631978cffa9cc287632aca00274d45e35badd1d1","1703030013d4bd9757c03fb5d376599a5b3590d306119954"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake.client-authentication -->
<!-- tier: stdlib -->
A CertificateRequest is answered with a Certificate and CertificateVerify under the first of the client's own schemes the request lists and the key fits (ECDSA P-256, RSA-PSS, Ed25519), with an empty Certificate when the client holds none, and with an empty Certificate again when no scheme is common.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("b105bbc63eaa3c05a67c9ff99cfd5ebd632405c4b826203c5c84b0d49fa7514f"), sessionId: fromHex("7f5a73b4c9a9c1127a3811e8a42cb78e9bfa103022ab837bbb99846e21277c83"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("899c7f8f4d220b0fe77404888d75122943a75113cb997c8b16b2d8e70f5cc06c")), secret(23, privateKey: fromHex("85e744243e68cfd69e71b02fbeb0afe55aedfdb9162d99dd9ab1c804423e14a7"))], pssSalt: fromHex("f64c16a5e2ba899a2c8ec7d9c8ee1b6150547800be5ab1237c964cf5786d2c583cec208600ccb7d603e83b5c72f3222f2e8da001da473e302c8e1ada4df5c5e6"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-certificate-request-no-certificate", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603037654a882167018cedfcf81afa7ae1860e79e9564ffdef57f1e6f68a80b3714ad207f5a73b4c9a9c1127a3811e8a42cb78e9bfa103022ab837bbb99846e21277c83130300002e00330024001d0020193e40d0d8f4755917cbf3a0136993f94c2c62369e6dc8dff956e6ff1d01e73a002b00020304"), fromHex("140303000101"), fromHex("1703030221d19473aed5389976985bbb72c981dd214c1b26bd132266a4745acea7796d58c9db5d13673f4743020aa4714b574e09905b70d762580ba31e5bdf1d87b07f9b1bce8625642133920ff40a550d76fb1affc7d2f8b982d7b8eed6a9cfa82831c40332dff9214fc964e0757b614a2881e21538497a1424f7735c97ffbc0bac6bb6b9676a17d88c1862290a2ffd161d0400a4bc363db0234f43e24c821803b92af82fdce1a75c534f329f34e2494a59cc8d775b81c6445dad2039c59308d6b205401c0a014328cb195b0d38e4ac6c0e0fa31790e8a581bb7468fc0b9209ba843e2ac6b97108483e0ee68b5f48cb45c9080d4a3c54d784144928d18e70a836db5b4c6dbf2be250024a3311f04691b401feff16600f300626c42b0ce04c9e9572d83b4dfe9ae94da5b7e52d2d4e49a39226cf8356278f0f231cd35d4d8636c9e60fc222a0e90aedcdc17dc84bef5453fa62bb827ad3c2905e805406825569f089c3e0f222052079959d32983552a4d349afd2363072269a4210e556fe183e7dfeb1eec1ed7b456fd3483445fa0ecc00310615b6a94943e749c91b3989950d0024d6b53a301d80cbb9f7ee3012b664e27f3c6c32f6f6a38243948e7edf1dc0e782f387a547278caa6e7cf25a32790e1d135816940ec2d3cf39d1ad6cc7d00dd665a97dae4852bdb3adea02951e59e1f8a361971a76b7f22bbd03c912dcb23f2d3233795300ab693af396d3f5105c308bcd3c26059e719935cce5d12a387266bf7da0d393ab"), fromHex("1703030039e837bf03a057bd31663b8c7a279e5dc84f4e66dea0a4c3d7fbfcc0adbc60587e7881032a49923573986ef1f51a86c3564101faa50f1370416d"), fromHex("1703030013e2715dc7225e3eff2847e743d6666038ce87a3")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.certified(ByteArrays from [fromHex("3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91")], key: TlsPrivateKey.ecP256(fromHex("fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"))))
	let entropy = TlsClientEntropy.create(fromHex("835493d1ab860aab4127a1b92902eee50f50f8d3a8642f7cf9d786f1bfc23d3e"), sessionId: fromHex("257c5ca785eee444d1f6f1e2720f9e21496c6780fb1a9c0b65e728f902dc6ff7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("011c08581d807f1c4254686c81abe311747c447c924071223251487c2179b471")), secret(23, privateKey: fromHex("6c1a8cdcd384cd37757e5d5f73488bb92243886da08c3d0361dd37f498e81bea"))], pssSalt: fromHex("c9b97b148a5091848a710512e0804d739abbe76c4bec58b50d1f7b9b5e24195d48056d3a616ffe20fe8b75975443beacd43b41de75d29e5f8ab7405fc779cc7c"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-mtls-ecdsa-p256", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030321a17c8a8739217a945930895c77cef92874feaa4ccdb6ec61429bc09919a99320257c5ca785eee444d1f6f1e2720f9e21496c6780fb1a9c0b65e728f902dc6ff7130300002e00330024001d00200475e49aba7bd9d74781ae451b5138063b6c80b110cc28de0d00633f1d5eb52b002b00020304"), fromHex("140303000101"), fromHex("170303021f81b1b55a5f8d641f85ad62f3a8565bd95e2750939d4a696046b85ba2a8e8d194ff4dc15c34678d3bed32d7ecb7b5ae93051c0f4265e6d3c561c91e67469237b83dcca807024eb413e1d3dd3cb0e7d25e44770a17c4d08799c1001ed66eebc964d8a7bbce07a0ff5c220d40a892266ab88da73b9dfd683fe521ca75ca28344caa65cdd2f9433eda50a2c85b18c52890382140a912f4f6d9a8d1fe34641a60571035641363a3d52fb40605c958ef4e1ecdd39271e3dd09ea53211b07dab5afa1a62192a05c369c3e82932b7c219ab8bf886768997da6903f1e70f36d950eedc0fdf71053b55aa88d6015f481a3a38bb38493f1e6de312953cf4925ee0d282d97a471a95de253f7c1482c1f7d3206cf9940417b6c9eaf69d7d8521bd2ec6d9ff41bd1e009ba5a28d44c3f92d2963c029531efec0ed5ce7aca0200e6fddb93d6b6d37038359a2678cea196c84fd31dfa0e914280c3aa029ecb357f7594adc195ceb91f7819bb1d30fe8a0c2c27dec49fb197c850e47bc60aecfc4136a351c0be7221482ac5a659b7b61b324b7b21e51de94405d60e5e44c3945962233b56893c6c4de92c96c584a1fe8757483da8c8eefffe5f2e0bacb06b638c018ae57c61f5896d30c8891deb9faf433f7f3009820eec3b879d1c57625da61cde6430970c1da6fd7959a3308902c1e5b7ea145d60338a04fd6e67dd0ff73fc60d6656e13745e4c88915b2a7e7b32f77cb8e20352c36852d0329987935c2a7363140abf385909d"), fromHex("170303003929e0ab410235f5519c6ab8c8e71ee3803583560e050458f1eb9cefe1107ec52a630cd7bc8cfd06d4457c3497f0a1d29fd5f8379c2db0bf6ada"), fromHex("1703030013229558b9dcdb90cc5c459cf65e896c780d91c2")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.certified(ByteArrays from [fromHex("30820234308201daa003020102020175300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300e310c300a06035504030c03626f6230820122300d06092a864886f70d01010105000382010f003082010a0282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d0203010001a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020348003045022035a866be9f42da07b71e1df23a9c60a0ea5620dd55388f5ddb71bb848ab6711e022100a1502d0bfb23d04f46a5d2187bdffc938b1aebe54b36b54622c0c58c154cf2ac")], key: TlsPrivateKey.rsa(fromHex("308204bd020100300d06092a864886f70d0101010500048204a7308204a30201000282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d02030100010282010045df65c5ca9a3fb1da331c19e500efd2d09618922e8fdf0738a28e12b8cdfc4c9965397375730abec9ff878995ea476eacb1a9fb369fa099302309a9e93458b010714db9a90eda99bf17758f15ff1d25557a891368f328e321afa5e5d6af7aaaa6423e8ce10afa5f09c42cae0321f3718bb5c835b19410ff62d640421338dd7ec628dc43300c23e36bb201c57474de8bd382a64ae081a8a81bf0ecce10169ab362fa3b3736633a2045041e393493bd44c7795c88a9e74a348f6b3da074469db759e29bd7054d2ac252f7efbf49b14c40dbd442abf51091580e0e21e2a01322840ac3f8d4634bccd79cfd41fdb615e07bd1cb858d6e76f3b9e94fd032a5ea290502818100e9d14e6b52e82ead2112a1ec53f521fb9f81384fcb6ad79948afab9b434345db1fe0759b6b7cf787091698d23ec129370cb35ff478c359a7582c5e13c23c15ed919b778fb91219b0c09e6e922f62021d26632c33d8206bd78717f606ec43669e37cdaee172360e2b4f08827a6fc43e4cee822784a20e224bd1b5935d0505c35f02818100c5f536aee5b2f848afc370d68fa2ac1503e4f8c3b85631bf9baaadf0c03055612634b819ee8ac36e217be9f35b6d02f3d206bae460e51eba2adef5a7c40ee3ebf8856a94da2f90488c15051e96593087e4d2e331fd490fce4cd8a8f8b20a3d5cfb865ce0d392f85351a2ddb3bc82d878943370e64b4b5dd8d90e72092b0bcbc302818100ddc23afb0555dab72f2fe9cd27cfcd9c45aa9c0aea25fa37923db2a77e42f6a51df597f5811bb3f429c579c9a5e533bed757e5010bec5342a918e35d201236c5ec1ca467f6a259254f7ab3ea07f08f8ab016cd1c9ab61d31fc19d8a8b63a32c08ab29453942bfa824ee20646336ccf9dcd743a20cc465576cd7dc6372a054e47028180008cbbc8248f28ad5aff99f29e977f0ed7e1ece6ee3e1f093f2f6c283d00655c4db4dbc606a98ffd604e6dc9e5391a46ff1051b88ca4563f821af867acddb5b8540a5acf6b03b2cce0c6b62aed93f5d48ac823256264af8c8d1e14ea0be4876318b7b8ce4f081b746fc79a0203c6013fe4797818cba0bbf3fc36540ebe75dbed028180607cc46d66a8c6c0599730342557c5fba4e2c5ce59cb965be8889fc3e1f3b40fda0f8d09a9ba65a55c3df93f1c7540493dfe0345f5060082417442c0f31259fe8923aa19020a143af648d916ac3f11f6449ad7471fc1ccdb6b8cfd95c71fc27a6113efe24a6884f0fb804cd75e94574785436e1593ee4511431b32cf15040e5a"))))
	let entropy = TlsClientEntropy.create(fromHex("e72f24c199eac43df444138bccb456ae5d07afaf4d6ca2fa6410fa91bd31df77"), sessionId: fromHex("dd236c3d04a84680f0746a81d0fbb08f500d9acd9c6de87e18e2361c315e440b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("50fbe8ba6791be51d42249c8aecfe5ef7c3972e13fb4ffb3bd1b9b035dd8588a")), secret(23, privateKey: fromHex("2ec4329f0a7e580d9dd4eaad50f1518455536212a7b425f4e4ff247f39e6c64a"))], pssSalt: fromHex("4487405e8e4edd434eb92c463868df0e496800e4ebc4ec903f93bcf2e86dd447301b9af23b8237672495d313086169681e401644d11649ae69a99b655e8d5cbf"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-mtls-rsa-pss", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030395264d40b50fde958e5b80a3a4cdd7f10bbc33cce8e6cd6f5ce409301700b46a20dd236c3d04a84680f0746a81d0fbb08f500d9acd9c6de87e18e2361c315e440b130300002e00330024001d0020b8ffcd7d4d0199ea898a751f51dd89b9fa20097d18487ec9e41bb0309074824d002b00020304"), fromHex("140303000101"), fromHex("1703030221e3d389f3f035e23af5f973db6051e811f4890d25542da87de3c9e5b7144b1c82c0187cefe6ad1f92da07008f276fe6123086f4d1b9dae99012c454a79798e1f58cc9666bc6ea7c305b59b012b94ec438bac26c9580762eff6b65d03e79b0913843f381a912ce881beebdec83e3d87ccde3c500cd04ff84e00fec33fffe15c95d7aff7d5a2cd9f94e14cf6863cdc073499ef2cf047a2577ce73c130f273d49989ffc4f8dbe634a8a0f675eb28f2e26c465c9525c8f0021e26bb29c7eedeaf98bd7bb9f708f1dda146c4d8e3c2259c3a2ec2bd6ab357c6b15fde98f8469be757c5b66531aa07dae3ad27f54741086cfb2f40f6256551928b43f49ff47329da2d7385ac3ff145d64af5824813d5b3bc46a3d9d6bd8fb059b0d01f8d04c8eadd4983713ad1a706a31422034ec822d0e9a997683538667ffdfdd730b1f945370f4efc0c45d370305a3a48d12f46ff717737ff0fb7581c2a052309cf918abc63ac3568c7bc51758f083880f2a2f2b0ad31f8800900e53eda33bdfa08407285623c03fadc461dbe3f6f7ebc77febc24348901c6899cf1cf3015a8569b5d29bc9cd6d747ad5c512886562ebfb7f74dc9487fe7f36a0a1ff838467b1f0d212395c9fc848e090ecf44331ddc7a259e4ae9a6f1efde99a2c00dad8c04239fef14c9b76778795d4c90f69c3e2a6e818c86bc4df8e49b6ba1d38592d724ae18a7b03923533453e039235a69f0c1395c8389d01823a7322d901ebf4711d72f9257ec52cdcf4a9942"), fromHex("1703030039b57f73bb0fa93e9765f9cd78d6e315043c8dd515a41b1d047d0170fb074fc264f903bf581daf090c9f005341f0b06efb5ed2fab379cfee2312"), fromHex("1703030013efb87d6df98e9c4852543715600023be22a13b")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.certified(ByteArrays from [fromHex("3082013c3081e2a003020102020176300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c056361726f6c302a300506032b6570032100f004952b22a93a2274111343d8b740acb309a18255094b6ab57d90ad545bc0b4a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020349003046022100af38b2ee559ea1c36ad386fb553c43c108109c6c4ce1ab62dd7e2567987d1644022100a33596c2359214abef9ea098e3ad30ed3bc52d48c3a40d5b4ed558f069298dfd")], key: TlsPrivateKey.ed25519(fromHex("8408cd68c509089a76695eb5c519ff0966fad7246013da6e9619f3032f71e404"))))
	let entropy = TlsClientEntropy.create(fromHex("e6c02af8b224ab22777592bb371d7b6fe488874659be558a23fcc359395b60b2"), sessionId: fromHex("4c4826e42e46d2ddfb26525c528e2b217151ef285cd749b1850df619950177b2"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a2f956d48825fde5723b7002dcbe6373d2d487768cae12e4598f74274dd776f8")), secret(23, privateKey: fromHex("7d1cc4ff7ae3ddbb3a6d0cfc50f28be2381d90c5be0296f13db7aa9ff99c54b0"))], pssSalt: fromHex("2a1b975ee0cb8d69e0037582607bf0ef6dbbe06c6a9a2c3576e083da2c1fa4c8891ffe080813670f251212964126956cd86d4ae89b2e6c7a705251b6c4b4b4de"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-mtls-ed25519", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603031ebc862ee5b3c4c6d9affc54155697776bedbd216c27f067da7c24378ce19c70204c4826e42e46d2ddfb26525c528e2b217151ef285cd749b1850df619950177b2130300002e00330024001d00206124e4051783c3950ae0352459aec0f10af8eefc1f78cabcdc680f8a599e775d002b00020304"), fromHex("140303000101"), fromHex("170303022076a032c31dfa0fc98a6006e265d8ea05edf9f03cd8d486a6b28c74582674741c7ac92e1ab90d45c670c71de5dd5cb106fcdf6f4010678a2a717072cd88f4c8eb999ea77d84f1d795e2fa19686040b82716a782c2224097f605446ff030e42cf508035ec58cce2b10572362d4d6b640040e9dd62fed5c123b8ad0883d69352b5854b00197895af4a9ca6f3a3979254db8f6bb01396a3cadc34b34e73978ce43f0c214cc89cdf9a61358a3bbaef5f719bffecd9bfeba7ff8af7a69feaf35e9c0e00e99ab0a8b547699584210499f64e0efa00f2a55096260db67559ca2e670a13d2d1978f38aeb350e80c47cda3556a353fcfb172b02d59d6ebc1373dd4cc7b6f0bf9324e6660d4e7b49b1b934e0c84339243bac06b5f24e42774e85bbe1626144fab34db48e49840e8c53d0f004a385ae22bc86e00831945298de22fe5600b302671dba86f2aa5aa79315358ac83892fa43842e7eecb4ca5c9033b557d104a2de4fbd44bca03251b2edef80d3309cef92b46811b0cf983ce184128d8c96056c0758fcefa42fd1a91ac84152e36a02957d6b7eccf35cb7dae76ddca267644f7211f8529ca97489bb9dee6247bc5a8fafff719978bf659aeaaa32b3e56223c610a89ef118357d28e44b7734d6995e4a0339fcc377c488444825c96dbe299b3747936f4866f2394acb5d450dbb7dd25b1f69e792d581f9dfdb44f5cea5604c71da55b1f39c7dd723b0fdf95734962e97fc2d9eac0c333e065b51e9fff4910f1b9f9f"), fromHex("170303003963785871866f84e512595a7b92b64f3c69254baf533902cd8e46be82e7ebc871ae74a860391c361561c3bfb698ed73a838eb3194bd0851b641"), fromHex("170303001311367a12b9679fc554346ed76f32691f9c0587")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.certified(ByteArrays from [fromHex("3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91")], key: TlsPrivateKey.ecP256(fromHex("fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da"))))
	let entropy = TlsClientEntropy.create(fromHex("e740a1f831bf063a645c779ddcfa16185ed9bbd23d669ed3ba2a505dcdfadaea"), sessionId: fromHex("52c92e6e0dcd2781bfc08512bd0a18ac78447bfec901becf9fa320a9c0980a3b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5705fb6d78f2779c0906ecc77b06544eb6ac2b6e262676b1a2712913a12206c4")), secret(23, privateKey: fromHex("3db1ad068ee4cb8fa470b439b582893bc4840efa68e196de58efe211140fb787"))], pssSalt: fromHex("162917b98fbf601a32bbfaebdfe0f20bc38b071b5da1e3564bc2562842b46e26ede22d0167da0c52cd5f21f88814e399d620a4d17a834408c11dec913c85f5cf"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-mtls-no-common-scheme", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303ec2cc7c53abcd72bd4782b12d7316b209d5c259c55127df0ff4cde8b49417e102052c92e6e0dcd2781bfc08512bd0a18ac78447bfec901becf9fa320a9c0980a3b130300002e00330024001d0020a0a926aff2edebf9bf6b1fb5f5bfd1128d405552819cc3d5c9af87d504607417002b00020304"), fromHex("140303000101"), fromHex("1703030216dc0bd44f8f128d6ee90abe11190d6c5f123b41060649a903d257228d5390187b470a2fac885184f4ebf76d1df4cf1379e20f7595cae6a3448e2026e439ff52eb00957ed8b0f3eaf7d6c786160172d7b3a6b013f5541c0e1d0118e14118f6a6c65e24052497eb4fff1e2454256cf1cb67fb491f662ea61ade82d06b23463d943da617e09d72b38ab935e025aabf2bd74e8135cce295ddbf472daee42a95ece6b7cfe1bb1c268e73914056377c3176a4ad33486be812dff3c3d88cf49a63e33cb3dad447850205f2c5a5c930b6415093454b8180f21c4ac040a40b659624934abe9ad6b42c737979e14292d3eba95db8496e14b68433207f1aefa4ad6b9a5204f86240d8e88c7291d91bd0e70a11b5d4d4e5070b554f682bd02ca95623c4eab120a136099aa06b4f854a9be42969aa8447f7eb2f0494fcc62b5168bd3ddd09c779f87f801a92d28eb1065d96a7d050327b9a1ff109f7280667088de323cf32702dacb25df72eb358444a3d527279830823ed446dd58cadcd1c117f46749cffcc3f607f7acef914f3888850b7aa7940884e8e31599daff82aee7ea15f1f1182749cd7671d8d02840d5444cc2fdbdec31b004ae5372f1e4f60bcb733ff0a5a9769d3b20d7226eb59863158c67b9b813d75a61b45b99f2b9ae28f8b3d29b483bfea768d1309a36a7b245da440a56f10b391317e31ea0a7cd06d6d1ccfbff44e9ba9ff76a2161535da3b847dba8f762582b52778278c324ed1"), fromHex("1703030039a8814eba5f60e9fb8431bb19bc64b5e829c32c8a6adbb9e8da6586779b0e32668b26ad81b4383d7f2c2de6a90c9808cd14f4aa91297886295f"), fromHex("170303001366278477d73fe4937c8a6addebd03734e1961e")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-certificate-request-no-certificate {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303b105bbc63eaa3c05a67c9ff99cfd5ebd632405c4b826203c5c84b0d49fa7514f207f5a73b4c9a9c1127a3811e8a42cb78e9bfa103022ab837bbb99846e21277c830012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020fb533a966af43703e81b8b6e2abc94c7b02191989a7cd73a128843f1063b3719002b00050403040303","140303000101","170303003d0d3d1b2d5c28b1b517b7b78c5155c1a360ab60fbdd27dfd9563f6c01e5f6eb9bed9257d34761ecbab133d306cb627e777513f93745740f9d504daa0d0a","1703030036e2d075b241fd2f3d16cff89931ca3598a4608d43d1bc45205f0b3a4d9963e8c4237df4262dc087ffa797ac37302ba1a6b755d0a895a4","1703030013cbc236d1b2c8656285de4e991884079c856ae4"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-mtls-ecdsa-p256 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303835493d1ab860aab4127a1b92902eee50f50f8d3a8642f7cf9d786f1bfc23d3e20257c5ca785eee444d1f6f1e2720f9e21496c6780fb1a9c0b65e728f902dc6ff70012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00208de2cf24746f453e0f07e7c7eb8af062df277b48b786b8524996d9a4a0f2536e002b00050403040303","140303000101","1703030200b00e46be406bab835a689870ab876d6bf97c5e34a14f81ed687e2c6c80ec18b1311366c84422fb881b98143ebe13c1f65688ff3a511840f7a2c23fc69b390b92448a317315e367a7afa1c0645d763830a5b72e2e31b345c1324045818fd6ddba3349c8a6fb3bc60199918e459f0837466b4a83855cc1bf756e82d5d0365a415f1685fa281762724bb7af4df6bab59b589f790e9aef63a148ba2ba70017c478346b67cca8fb031bb464e3dd01b304e18d3586837904db692b863856a8ac996bf089f212c08ffc2c3a1eb3c3c99de7e1f60000d98c4682ca64fd71c064ca6608486aceb334c9219ec3f570b577d1d9deace83ee39f863845b96c9dade92a2cd21e183b398323ff8d6274e53a774ed6282260573705004a5809ed4a3b2e9e72c365e2b569aa620deeba2f5180d7fc18b55db1f44ba1ac6e70fd86c102c3491f8336855b89e425dd5b93f76aa62c7fd8a5bf27d5c16f2be809c608c7eba251ede9b9d3a34c9ce3202ea5bc43b030a74b8a44f8e66141a50d59086f95ad4359bdf474cc0052a22d33968def1515aabf80ecafe6e50aa5114f4ee98fc1e0d16c24f002d49e7843da2787da1d6ff5e593c4f9a04b9e66935f3d01f9b2e8a88e753e4faf6d177fe3c960cd24358c54e0ebb43e14d199a3291cf696ed367e4f85ba32d597ba4f8f949ec4e3c778b7b1ca86fe555503812bccd30b48df3f5e87939b019594","17030300360e170676e72734153ad83d39fe7df1f241158ba0db86180dfc6a6b43c139c5318d60eafddba93c12a40d8060e1e8abd3817c9ae19cbc","170303001334fc168488cd7b5ae5d2e7ff20e659625b0772"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-mtls-rsa-pss {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303e72f24c199eac43df444138bccb456ae5d07afaf4d6ca2fa6410fa91bd31df7720dd236c3d04a84680f0746a81d0fbb08f500d9acd9c6de87e18e2361c315e440b0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00204b98aec6ee3c56cd31564f508258bd6ab64490df8a14c744dbd8a4be643ca741002b00050403040303","140303000101","17030303829b910195ecf3cc4e88c3f3700dba2c2edca8bf309cc0c3ed9b4cae35633bac76926a35a8cf64b34902d38a48aef1ae3f4acc164a28591173cc7a6e0aa7e36bc92bbd2b8f91b12f306973892077ecba5f1fec59c045f7465826dd655eacb82682c19be44b6ea1d4ec4e57b9a69844fda020f26991b4fbb1cbb843fa8fee9a10953ef85d7d01d92658caa68f0ea41ca2ac04eb25e0c9e9fb8e777ebf76da57a93c8eef61dccd1c403cfe0dddf457a674dc12fd16c7fed970eddf24916a07c3b08c7e648e294c9a5831b246426980b97814847987ade90e5ae561a07fcd929423d1fa512301a5d18f192e662317af5dc8612d1d1fb210e9ac75ae5bcfbd98502dc5d9dce5912c6fce45d736a14b69697f94fc44137b30bd3d3df7f06375e5255f666279c6a1d17f3b95514169bab03a03879af02ae22abf6eb28f70406cc638025b1e28ad49704289dfa296277c9d3a9bc45ff7b54e2e35e44c8d427898d515cbf3c287dc359465f6ff7424fafa690efb7b968fb3f05f50c49fa4bd2c50d22016e53039f27853195c9035d25f0e2c3c42e78c9a8c5add7730beb80f9ddf2cde1b3b7dcffbfe3c8c68626a36ebd840dd4d9790ce5bbb849ef53c36393de2522671ccaefd078fe660eb42e3d919efd4087dba6e49f4972a7202e5f34645cdba3e5c863b67a038ccd047a4e5393c8cf3b5f8823074728aa320fe5c082d66f71917cd16f518b5368f7398ea2a7cbd687636270e0f9d68fcf243b305e606423039071d05cafe46eff3ba987b4ade63e6f054fbacc06f9980689e03d37c44989bb409c35736c921cc7fb25359a2964996cfef0dea1a927852373a40243ca2b03913f4eb7405e1c4d9a7fcb52a46ff0ab11f82ffdce960dd4173fd082d2e3d545c9b96c0721f58992c9913dc70bba2b62f78be22ff36c9410cdf1b19d75e69f205b5792f0c34a4a41af39305df6f18b20e368c378cdd75e2609b379269b940bd798504aed17592fcba6ffc8eae59697ca96801d736e496cf97bf68367e1b23ee7f818e71b0cb65fdba25a0a6fb9b5a45325892842951caf48453c6a6e049f0084d3a875804aaf886db1c7c81f14be240301de4d5264ed7281400a20594d1d723c7da5a78646b04fe3fc1bad278ad662ab2f9321e42276386f5bf34a005f26eb2c1a73132a859c58b5f9abc940c545b8867fbec3c87000653ac630dd0aced437299d2fe637c119c7b1e97e585e09535771250ed37b645b0f7718b880db860f68efbf10988a3d5d0","17030300366a8465bae1cd5b7dcf3fcd5a7f2b9a2db537daad9125ceaf8e7e81e60c83563aae4c795cbdb319d4efdc893304aa553f95698d7a22ff","1703030013dc376d114bed17329e5d0b757359badb3de2c2"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-mtls-ed25519 {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303e6c02af8b224ab22777592bb371d7b6fe488874659be558a23fcc359395b60b2204c4826e42e46d2ddfb26525c528e2b217151ef285cd749b1850df619950177b20012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00204ba73b0c7dba64fcb668398b7ae43e691480fa2e2fc6cfa66913a538a7a9230f002b00050403040303","140303000101","17030301ca16d65bc93c42fd28c538d805bbd44344bb2be2c7710cbc1b2f52b8951c954bf89c358788bda5f9e2ec4046c3964189e4b28665b6139a933b137638c815f393fd1ba4c554a794fe217c387e9dc7b2326f6de09cfbb397eb041f5ac5d168e288614f4c0d31747781d6601036b49866e328f630c1566cf0cb516ca20bc8abbeed2ba6d0d17da785cc93fe8d3500a0a3ad054e426961e1a7e2bd382385a04c3dfeb8d55911f3e307ec7cd8cf59c6b85a8a4f384f67cb16a2312c4f5764a3e7868a3b03476c30d23c1cba2b19b3593d40416e212ad9cfd911b18248d2db28c7591aed78098b4c8ff810e6765c0b6c20904baa6a7ac1c857c11cd6e9c667bc84e4ec5882271ad48e44b8769c2334da8c1d6946a773b5c1ab201b14beb8d22b9689722e3b9defc41a217c3291c9f104fda09f3ed69217266d24429a5d6d76da0982e44e0252ddd5b227679be0c0f14208ba105e8e54e045c9d81d93a89fe9185515834c8463acc1454773f73484c90144c8da4d8899ff9c94cc411fc0f58c2db6142f5c53fa9cbfc5c00b1987ba198745cc5427859cc93d277e43e2d8247b946e45fd3b78494d89a02b9ea1e5660d7e13bb297455cbfc88ff995205614f69f0036d2f043b222d5d44b738fa039e","17030300364a37c445f6fa84a5ed703dddd8428ccb76a824e5e0435dbb6c1e71a5105a49466065535869e6eccdbf84b7e9a730afc2f508343c16cc","1703030013ab2a04dc5a7db3628cf27f660aebb6ecb11128"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
client13-mtls-no-common-scheme {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303e740a1f831bf063a645c779ddcfa16185ed9bbd23d669ed3ba2a505dcdfadaea2052c92e6e0dcd2781bfc08512bd0a18ac78447bfec901becf9fa320a9c0980a3b0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00202084c8f7c5d87673f10d8e1c90e46127f9508a33d211cc7a1f8f419c672a625b002b00050403040303","140303000101","170303003dd059615a7fc443acf760ef133d1d643bba5dfdb1f5acc1d71fd36594e40e2e2004c2a461d557bd369cbd5bf3293d9541208fae62e90b5c591acb133744","1703030036429c0ef4192e5c4f0dd107c88eec88388c2ff92c1e9034e5f33195d3e05ec6de9f7a97bcf6b58fa5e9f0660122ca785ec43bedf11068","1703030013fe6efd550f2afe4e6979a7f5bca127d5c79a64"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake.certificate-errors -->
<!-- tier: stdlib -->
A Certificate message the client refuses: an empty list is DecodeError, a root it does not trust UnknownCa, an expired leaf CertificateExpired, a leaf for another host name BadCertificate, and a non-empty certificate_request_context IllegalParameter.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("6a2159a68579cbc3d1053a20a72910c6c722ab73db70398c69c83d4a80270544"), sessionId: fromHex("373fd173fc68b28531f4fe9204779784e64faf303eceeaae2eff465c9ee045b7"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("173df7420a74a52426e0f2a6826237f6502bb0c58e10b6fdb5eafb87c5509abd")), secret(23, privateKey: fromHex("aa488f98a99766f4b9a3765fb4ca61b984f20f1d661806c4d3bf34011a0cd570"))], pssSalt: fromHex("afc9f0a03ad1828b962e07f4a88fc6bd5db16b30d5e42bd8ab83a626fd02628818b6ca007eb01baccc6769a86e24119a04e8051aff5f0d427456878dc77ee6e0"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-empty-certificate", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603037c89ca8c7ca296749ceb50c33b9d513583feb327be2622d5338cf5dde3c64a1e20373fd173fc68b28531f4fe9204779784e64faf303eceeaae2eff465c9ee045b7130300002e00330024001d002002e3d1db5191626b7a32e0c86aedbcb60a4565e0256fd8f1cd6b8e920280a00e002b00020304"), fromHex("140303000101"), fromHex("17030300a5b751016f952cff5a0643fea1d6579e67038b948efec41aeca705a88d0e5754f3426dbc9ab7074b634c08be3a1458e6105bece393d792e27a53dcc7c6c934d7e65311417964046b07e4aadc0f7da2c2e15a550dbeac436a43495b878d4f588563b9120474e433939e7fc0e453ac0a3c1aa9292717c57a4cb4ae48eec12d75b56b894e70f8dd61b267afd1b48a27d40486b463d1066cc3ce51e4d95146f84caebaef5a460c44")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("dd56436a4ea2979a36ad5e1cf310b84e12b5f39827f840005a6325e48613b530"), sessionId: fromHex("e0424119d43fc0890a604aa82ea078b878dfc5143e85b48919b7eaa0669c9b51"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5e580ddab845b36619dbe60e6332ec9a68dc2e3a7bb633758570aaca602d87b8")), secret(23, privateKey: fromHex("d09d9d8175b99a0f3b63d8bb14985c5ff48a76181d6ce904c050adc9b3df8e55"))], pssSalt: fromHex("6d3ffd0df847690fcd694e06b935ef0731eadd4a4e2d13bb03aac4bdb3017736138c1dadef76515005a292dac175158df6159a341c8fa90316b1d063830860b0"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("308201313081d8a003020102020102300a06082a8648ce3d0403023010310e300c06035504030c054f74686572301e170d3234303130313030303030305a170d3334303130313030303030305a3010310e300c06035504030c054f746865723059301306072a8648ce3d020106082a8648ce3d030107034200047432c1205849f970ce2416f602690f3d19e6790708c1f3b18f50978b340cbd90510d403ed2cd0b1fd6e43d92da9cf7ab7ad0c5e0bf2e39cbc3176966857e9780a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022100e0bb40fbfa3b426334dd4d96ef7d4d8e7973b1cc725b15bf0870f30a38d66cc60220403efa1ce958392af9fc8e0cebf8676fac6c55cd865874fcef051b635310e43c")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-unknown-ca", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603038d235da535164f6f78a2285d5f459b52ace5992bf84dd5b71ee079121a6a558820e0424119d43fc0890a604aa82ea078b878dfc5143e85b48919b7eaa0669c9b51130300002e00330024001d00208f6bb0f9d6dd295221248a119ec468428bbc4e8210cdd30a1723196a53182f7d002b00020304"), fromHex("140303000101"), fromHex("17030302072a24e94cbb18142d316f0afaabbac37cd31b5dafd4e9f51a2d527c3e5c413f313eea39473e30a3e68d9604219f3e77df7161dab8a7ccf2a05bdd36b21dd64132e50292915e805015fb347cb100bcdd3fbbc7630f945cf8ff957274360dc406d4f9298334eb69f72474eeb35fd97ffb90d02d5043e39e6a9a0cb15aad6d9f30c504b0f752cc1edfcb8a6bc992a0a54ad3e626ecb8e3f6d26657e4b2065ffbb46ba369a5296d7834f139dfaee4812bb8682d812fcbdd57c0b5e79713c949eeb5dd4d3e48d9981436a4c570e69474de749d8b71c174e5f1d4cec4f9296298d200a38346948fda41d60816a535c1726d6eed9576877d37bb2e50f36bb99a9b6c7003797fe054b3c58c4cd07533988914889840bf33e472bc6c0e2e64bb95f9a37091dd73537514b5852b33fb66cc846ca305eb34e057657e7f3fe72dafb36370bf7c4cd919ef2040826fe13bd178115cc7f1d6210fe2a44d5c245a477ab7039be38b405113ff9f0c278bd3c2668258c749dfe7499c5adbafe48090009ddab207fdbe5916410648834e8961ccc4795056e9f16bd7ec3c26599fccf6f88fc8603ce764dc1400e89adc5fc1abc027162911446bd873e942875b0fd21a44be997a43d1f99218b9c7e3e000ff378a1e9e92d9cae7361263d9039f9ba19f557ab62ddaf5bc49a3cb69eb3c18717f34afb52acdc29805543833ba1e7e1a3008907ca815edb388ec4bcfbb9761")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("32c505532af12211342adcf7eb978c09935cfd9a2f9ceba1824518489c7dfc48"), sessionId: fromHex("a173acc6e9845697efa452ee662b180b8c0a159810e9ee184d9363f69f2d231a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("694c1e8f4bd253f09570b196a62d7b43af08753d000b7f5f4ff6761ab3ad553e")), secret(23, privateKey: fromHex("99af622b027293766b96dfc4b764a4d09877f2bead7aa22bb0ac9a97a621d03d"))], pssSalt: fromHex("69120c559a606ae02dd2bd6e8922d93ab1284e90aa1343a3bd572426af18492ed7e7c0f079128b6b0219242650121c9eb205bb72c2467fef76e2cbe42b43cf66"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-expired", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303e274c8f427f184aae42542a76c3205d6d352cb8ddcb190fdc1f2d81c4c862f0720a173acc6e9845697efa452ee662b180b8c0a159810e9ee184d9363f69f2d231a130300002e00330024001d00205e0be75695535e3f1edd09d7d84e9c036365b28d664b381f6ff5d4460319581b002b00020304"), fromHex("140303000101"), fromHex("17030302091f00a9ee63c843b97f3469cf0dc56f63e0021cc81559c2146fdec450afa2cd20bb9da8b66bdc211feceea8eb350554fee8aad45906493055d4ad0f15be4fb7b4590d99a1d021e539bd44482b7d49fddbf3fcc881bc3f114dd23eb1ec5de77fcb1a30683a722a219e682d51a5d49505af347393ca2ca53fe94687121fd57d2b13564c4b5c6f9a604ddfdad9f67d7df7251dc4af4ce8f0c4cc02a2228ff632671f0388408b29bf69393b17ca7b5f3e08ce50955046dc71a479df5b03c3e5df3b515b85fb851ec2e22b8a9906942a0e695c6d66904ab956dea78c8c4ca260583eaf093a381c0801f6158c959ee10fbc09eda852cfb0b4c9717532d2f7b018a9937f30acf664ad913f520872c34103ba13b90603e26ae27fea6c4f7644d0da3281466732a17d538e98802d0521d9ddd4e609a61ea1c663273a1a03c1a79deace85d0aeabdb9f9d252baca62bda4c5db4773c946a4be69dd01fc823bd1e60f63d9f07051552dfa3fb5bc548b11670916752bbb9ca0f0c46b32fa50653773bf86e536aa1b1f7774e06c03cdbbbea9b0e09579a5a2a73b51ef66cfac53efa193af8ca7dbeb6dbe0d2bdc0588cb9093eb2e7044ddf1d1675e311df576c4c899117774b427f4ab05a7979f4c13f651742d6fbd704fbe660f7661cace29dc6fa4b4586a352745f0edd49b755673413cd73c644b2fa27c5462cc72687b41ed28252b6009b7f30dd05734fdd5c864f")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("www.example.org"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("d216f826c56986f935ed5fa62df31b6417dfa6b078c4f4537a0d7826b228e3db"), sessionId: fromHex("473871aaea1800576d3384faf7486af937f790a5f2ad46b378ee3b470dc5c1ca"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("83fa80a9b97dcae81f25ea1301a9fbc37b09cb897606924a8859118adfdb60b5")), secret(23, privateKey: fromHex("270d28347c081769ef8cee642871b3e1dbf849af0948a5a0fb7c837c90ea26d6"))], pssSalt: fromHex("769da7e127f7853d628efe874b90bdc47bb67f38c94ed892d1f55ff89686c0571b7f327074e2a00af903a25b22ce52f5b83338dbff4462c411f782bc3a108f85"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-hostname-mismatch", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303cf606c940e10534a451093e320aa610de5a1eff13352e99b0c0033cd22852f1320473871aaea1800576d3384faf7486af937f790a5f2ad46b378ee3b470dc5c1ca130300002e00330024001d0020d4fe6fa574f10a705221bec66dbdacea9ae6c86785dddd1e227fa3b0a2b8c400002b00020304"), fromHex("140303000101"), fromHex("1703030208fb7c5bd36a6df60cea92849aafc162e4ce0a44729db3ecf04a871fd7200e53f7f1b63311e9e4ebd8b572c7662372e7fee45712ebccafc4ca325fecc9030da81f168739a7a38c1f8f56a7d84a48bec2332b46fa15a498f13896b6610569de8023c95f4de1e0952b1a4193c6912df9935b728434e732f600ed02abf0d22ca1bf543857baa024aaaf1bcc1f1cbc042a65c09642a75d52887224693021284a837efdd8875d4ee59a7a28d9752a01ce111d12c6f036fe16d65208298705a12a14923700d73ee54fb4637e47e22259cb46b5f91c6547c4bfce24e195ae703977dbcf1c06436d8c8699c4e405d01da9fcad09142d72cf433455e8eedc0ef6b9d07b6c12fa4e9a8437c49f6cc0ddc93ff44000718321588f3b9a5ceb6a5b7559ef2c5e3078741c42739bf309a58bc0514a7310b179e712c04d8ac9d3351d007c5e3d5e939072fee7f09347dc6938de5c0a72694dfddfba06c66d87d029f9b01f7163bb548e754f716eac847bf1abfd2da40428f6b43ae8e85effef6934bb01db67d546a476823559257b2d2632e3b06e0cddef30fc3dc71dee1b45a20c65a680c00d14e7a5561ebf3b7ef87552b5b65c12594d8bd365c8145fc89496107e8f1626990cb0ae3004cd7737ca7ac9e36aff9a327deeff53a6451d60700bbf4cd360979ba648c18863ae51e82dc9f41cee6cb086f61f8a9cd68a503a1affab1383515367846b61fc58d9d3b3ecb4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("da4f61f0cf2e15da259eebfa14ce754a9081a2e49dfd07805b7a0a4af1f05373"), sessionId: fromHex("650559bd23ed57a10d443e78511cfcf652e0ed20276e15c2d72acb3e86b2f80d"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("5224b2746e504782de7246cc88f6d16e333b1132fdcf64b9c72111bcbf066183")), secret(23, privateKey: fromHex("0c1434eb8f08beae7cc8be39d9f74f1f189e515bf91dacea22ec35bcad363f39"))], pssSalt: fromHex("7411bb43e11fe16cc0161d6933a2111da425412f559d1b60cb7de4fa65003e0e8540ce14cfe9178fce330d12b28ba5b4f777a89c329e1122df188b2923fd7a83"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-context", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303b563b3f3c5683c121b7aef6774f3238aae8eeddc9a27a0f9655d5ab1ba10c2b520650559bd23ed57a10d443e78511cfcf652e0ed20276e15c2d72acb3e86b2f80d130300002e00330024001d002004f195b78d9c3f1318b38b8e392f4b21edcaee07f9464a76dcdb31201673df0f002b00020304"), fromHex("140303000101"), fromHex("1703030207b56d7726a9146bdba02ea8fbfc729f533e4182236a2bc0a76f2ba9cb2c761d36d15a0bb29f268ae96ebae68c62b43319208870bd6d3fa6ff2fe06cc3467ee9ffc87cf169cf55f791c5c0c13737efb5547dbbce06155bfbf1e322798db966d0ae6e9bb6518be08986d434301d9ab7b20646a9d495126f56d6fc8e5d08e51c6808e31564c5960c065cc1adee84f3ff809d880510c833087cd6302eac4ffac95759ca1307cf7368485dbf7e21d6192b0458cad254dd21e313df506e979d23ea6e9c482a1295d8dfff50dfff7b2395d0d95b420162cbdefb05da59dae1c00ebe1571cbe1ecf5c1bb90bb941779dc72667c091742c86b5370eb469d1b5f0ae6faa3ea06371b53a9e7e455bf8768098437c88898948720d83dd73db9c9ea8122c35b3e2aed2a5796dec46247803d4fe83c4fba18ef39b92ae7fd9c6364ffd0dde4fa50717b2d65c36cf87aaa464b55b3cd57f27d3045117ade1c85a347c5402f7966f7af01402b1afb8ea6675e3b017a677a33dc0b98080cb3bfb1ccda48fd7c9dabd62643c5fa46a0496117a0b6d1ccec77fe0e7792735861160bc88fe469368e12839c21bac40c4be3d0c24b1b8e7640a8c2e535134da4f91c4510b6338a5d4949dabb8ece2c4fd8fc619064894f7d3d1712a682a29c4860a85422239365ae217fcabf2dd9204e5b590be3d768ce5db5e9b54daa433dbfc300be5c760da8af234e140fb113bfbdacd4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-empty-certificate decodeError
client13-err-unknown-ca unknownCa
client13-err-certificate-expired certificateExpired
client13-err-hostname-mismatch badCertificate
client13-err-certificate-context illegalParameter
```

<!-- test: tls-handshake.certificate-verify-and-finished-errors -->
<!-- tier: stdlib -->
A CertificateVerify or Finished the client refuses: a bad signature or Finished value is DecryptError, a scheme that does not fit the key, is not offered or is RSA PKCS #1 is IllegalParameter, a Finished of the wrong length is DecodeError, and a message out of order UnexpectedMessage.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("17b653369d6c1186b0889d6edc13845f5331f94f0a4d72c188e446ef2d67ab4a"), sessionId: fromHex("6a6264655f47b5823027833b16bc90f25c018409372ec4c134d0c6fa0e441401"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d8507b08869c95d046330bf0ddd1a6f12c7f0b11da0bae2190003ce7053330f5")), secret(23, privateKey: fromHex("020211f8a5c6112b60efb8fea9f88436c1b41caa6c2b4c7e097feea6c8d4a2d5"))], pssSalt: fromHex("939551aa5295d6e2ea223fb1b267e4ee755bcd2c0426f011485175f4e66644f717cfa7339af2eba5caebb95e2fd755ed6e0edd79f5acd1836ac151cf0ddead5d"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-verify-signature", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303f6325e5b2db7e50d069ae95ec52a6219bc364719d308b4cf05a154ccb379000c206a6264655f47b5823027833b16bc90f25c018409372ec4c134d0c6fa0e441401130300002e00330024001d0020fee8f0b22350789f809ae0db352ca56064ae83b10af61a6f649da63cdb991e4a002b00020304"), fromHex("140303000101"), fromHex("17030302061c9957b966d5f4fda04916f1931e7d35246d954618ab6bd20479acafe81b9035b4dd93f2c865f8c0f1d5204f5249b17f7814ff67ee73b65c988df41b73beee69da7068aafe80d4f6b66ed6233f6816ea4878e29e8b472804dfde4460de19e1302d344e691e9fad53e3c8ed887316cee62078574b05c8f15eb51903cfceb98e2ea2e38d8938c9ba357f822efc776c0ccb62f248555ebf61337db24c455a75e390ad23b3ae141e5746dd8ace2823e911e6bd03b6df670e86a14484187be4a29e27efbd515374684dc6dedc763df37d2d45fe22ad643033e1597013a3cebfe279b142177a5013ea8536d4fdd20ec8b8eebf45c235967e3cfa69a64c173989ef4e113c69d1b471cacf6b0e8efe70118a6cb9b99f1c69b89cfdd86f543641a117b94118bb4bae0dc28e3ef0c853f12dff0a279fbc8202f2eca590b919e5ea944d6d5efad08e26af62678de98948284b77d9eefdb23113d0e4bfd7da9650c102452c10f2872c853dd11da6fda44995f5c1c95e409d8bd75990dafd3a360d41cb7abe325b14a0048a11924b2783476bd845774de92519cae057b0d20538464875d8490a0fde0dba7e5f1fd03b415d238fd56c12f04c3165f1294c713eafd84e2047734d28092da8d65ce2957c66b4c1c2966fe23dd190f9d8ead380bb3c02f48de460ecbbfb998cd4ef4dc2ca504c90746c9a7848934ff6864502a2ead806908fb9557e460bb0dbebf4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("af8b4383158094c05625975fbd0a9ad71b6251db09876d0990b681ff64c64b1c"), sessionId: fromHex("5858fdd07dc9f29c01546753eb2cd702b744c548b2310da03a41d542279f9f1b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("22a27a33ae2c3f7ce0c37bf2340dd9670984470fa5eaf546eb7d264b02977d06")), secret(23, privateKey: fromHex("c5d07ef8a634c44d63466fa06a7d0e70e15f1af0c36ea8f5881342c62e9e0161"))], pssSalt: fromHex("4b82e00cfa9e397626640a50f1ea04be07aadf36b84aee99b1ae30644339045607524677a25bcac5dbb738508e32aa571ddc9f1cf195ff53cc5143f3eb898870"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-verify-scheme-mismatch", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303ed96869e1373752941889300602b576f01287b22b0769d95265f45575d8e0438205858fdd07dc9f29c01546753eb2cd702b744c548b2310da03a41d542279f9f1b130300002e00330024001d00209a8b770df326f4f23091889bcfbd7b390d5e49b75b914b8ace0870fe6b03e21e002b00020304"), fromHex("140303000101"), fromHex("170303020830ea4c6212984ea8c9b44219eb4649f9961c5f2fef9a61f3c7e6893bffe449dd0ea00529d8f01d7b29e466f0e296c8b9f8fa1eddceb51c6771eeac61163eb60a1db6c9e36f98b3613dea27870b1988eb33de9425f2ad821ed09a6ffd9511f4b12f14acb3485416b6d2e437324664c98f8b3dae05030d78003344028d30be4ad6f43e3b52527c9181b232204c4abc7cf98dcd2e38fae9e3408bcedae9a42524eb4f6f2677092667fffa0a4ed23ee41714de9697428a8916f9bc0825f989ebbcaabcd5b76f9c0cbf97f1252a176dca0b321a49b3e2f41b867bed1d4be3667ae0aa9efffc6718badf8296f7a2cb0415d880a17705db4f02041ebdad3771ec03d63c6c3cfd9f0e8193be4af457e7c5e8d53ed159e2b0bc24fc5be2108af7682630842c0d4d07caa39984881159a59b777bc93ad79bb6d45bd9a7ac94c35f89bbed08af0481cda2237ce15ae9a12fdf1f27339457398429ad35a2bdbf39f82111111831c31c383ac91062d1238210b20a951876580362b896d06360b1bb3fcaa655efe2a09becca6a3a7679904e4ff5668031bec78f2f791e5c420d6d5a096774e4b34f9363c71993ec195ed1010d2519f4d05864c2d4716e6937029a8b3046bc4e8f0c3f034e8ea2d995378776f093653bb8fd838c1ccc0920c7a5387e6dae382809a7ab4cc750d5f47e79d796d1f9eb61fcbed6caa95f1a66fcd069dd9431b560f65ddc0697ca7a103d")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("3ba4fe62f2cd568a76593820f759cfe8ace3d5dc070f8b82c7a9981926b291a2"), sessionId: fromHex("b45ef34c7dfa70195a592b3b212c25f95386aa0a2e18fb26c153c18505b06633"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("b997384a9a43792a6e43b74932bcd8bff2d7b0996a22a289fb30f86f70058aa1")), secret(23, privateKey: fromHex("a7590a58dd08ca373acdb386d847c190d9b302b20ad5cdee94a852ba7aa61c03"))], pssSalt: fromHex("b00f3a2356e22954ffd7e9e652a19c38bca5280b21fef4d8538440700e0bc89043922b46601341b29ddb55eb39f65a12effbab21518ad7c734a209cb47d985bb"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-verify-pkcs1", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303526796a93a1220a265c1e1217f25316754bf176c3c261d7cba8d25392af5c0e220b45ef34c7dfa70195a592b3b212c25f95386aa0a2e18fb26c153c18505b06633130300002e00330024001d00202dfc6f4641d3ca6ff9ed6876018578502187cadc5261ad0fdc6b3d897b24101d002b00020304"), fromHex("140303000101"), fromHex("170303038ce39f4979b1cbdcb551400c1792baf63430cba89358646f19e6d5c39b0702cdcb9b269062445bc1d15bcd77a4fbe00c7f7d0d031dfd5b15bca839d4916382af2ea9ee08a87a593a5814a85768e863a2868607cc0ed055d19873313b3182179d59e62a0551feb6fa1149eb7542460ef64efdebf8a660c6bb63f4dd5165d3e01b03b835b9eb739201469198e2d3589392790457dead0850efee53b22b3e97c022410b32297c6665d691e00b2341629e344ef2f3a15fff5012bf81b691d8d8a1f84f3cbd7dbec81584687664ca5bb868a71948686c07301291547beabad96a44c348a61ddfc909ea238a6b6c9c2e5c28a2ebc71b4cab1744e77e8b05ff0eb6dea52de89f0c0db1bdfb5193dbe1f5b63e251cc790f4e7353b7c09e656e01540aaefdaaff86405501e5a064392d8b000cbbb05bbd4024a00f7ebaacfa79ed6c1d1191487df15849cee8915d2dd76e1d6bff374a523764a48b016af0ee0415a1a66fa7a8b73fc6c06960af1c023789875f07a15bc7a1069d975d92286d494a3c67a79f19f26cb4e5ca929566a3870e346290c30edb9797bd09298a26551be1b77899c438ffb69ac62de6dfe10806a4b3ef76a6520c51610e40cf775f898fb8f604d42fd805a184a1d6a76cfcdddefa733cfa2a1b835ff1b4249912c07dccc3a9902dd79cf5d9692a470f0ff3ca02469c0c412706aa7fc1360c933d70ba406b9cdf844db15b921d058a3931e2ddd95a78b6b67cfc4e30674c05f09ebd090875b9d6be314c21ade4a38abe4cf953ba77af856af877ac824843bc4efc3b6855d9e5f7986310e5a6976fa4532209feb578d3e0604cd7e25afa7c05b24d1f47f9edf2e58834693b04a4935d2997ee336333984c7e290535bc76bc357f11dcaec454af7b88d098721847ef895761bbe6e5b005f60167a94245454517a4c4bd0d4e169bc99a4a4981f96682ef04e0f8bbe3edc1a5eaa0a6da2462eb5acc1a9e6f5279bebad6116a6fab14f7a4a5cdb3dfb0216033b9151e0f0f1c40c02c14030501746ce0cf1b22c1ad303ba84c3f15f6ad653d87ac3ac8c83eca9b8217380e23f82f292d3b6e05529ffc938f35948deaf770e2708f20ec1018ff87275f816a208c3f91f437c1ae725e6f17c14f1664eeadecbfab8b599159c9be988ef77934a1e5f4b106060474b62c422164cf7759fc8674c2cf8087d93ff111e1143f78af21701ff98dd1231a5f409b7f54b71184b53583418cf0d325b354eea0ed759b438387cc4eb3e0e8375465ab5c61636ca156fa241")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("3fc464a3d5018467e82879c0ce1b9ca6a675e491489353bd31d4fa410121d3ae"), sessionId: fromHex("38da28f0e66efe2bb28a447748520c51775b55d9eff2512d45d4c31f1488f8dd"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("addedf05e44f491e72391370cf1e389815506d5061107bc266b2725200e87ccc")), secret(23, privateKey: fromHex("4afb250f875fab08e299de3ab72e039a963e327ecb091b3cb0645ba97baf2f7c"))], pssSalt: fromHex("54763af56539c14ca2d0e5eb9ea64ae0f7eaf98cab746867a2c3ea0e45733c9010eaddc046076e58397981fb88dd43e4011e57ae7192df860046b60d1ae52b09"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-certificate-verify-scheme-not-offered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603030d8b6321a91bee4e365e0559ea38784ea0f8dead7a9772ea9b917bd61808d4302038da28f0e66efe2bb28a447748520c51775b55d9eff2512d45d4c31f1488f8dd130300002e00330024001d00205c1f760f328927ae9d7d85af7770d55a1c031628fe7f26a34bf0409dbab2b057002b00020304"), fromHex("140303000101"), fromHex("170303038c7562e95901d797ebc63e868998c3870e5d17400e0106185b3e31015b1045d04bcda8fc8915dafc66fcdf47b64bd02c8ad6948d9cee983322efac6b9460e446cf8b3a629c1b8d9fc7c27e138570c2e9f4b2de6df1e645a73a041f22473ca9298366e3b56ca0d2bdd2528b3718df97cc93a3df52245139d1a7c47b432b1f5f2b8b2d629436dd86448d173aba0ef64c609190b049b69524099f5859d8b6de6af9bed667a4436a1b6338f63e9c59bb37979be693ac8e8a012ab594c31b3e249b25cb3347b6026e55beb031ef0fe95da6f84f3a1d3512046eb5d38d6276071aac01ff56515ba3eafc060aa210c90b51f828e7b3d5a90f39cb205d918e8bf030b2f3a06aea6f327a63481de13ffdeed37a20cbe160ccb9fa6de934bed725581ba82a6719308f806615c7715749418aa642bb129aff0cfcdc17e126e71aa7fd3d94178b6fea1f5a42b4691112b005ead67cea634aecb9504393b75e4517d716855ffe92bd6208a02e9c3877cc0c194c3bb08056460c55bb86b30e9c58fe5514f8a398ec950f3b92d566031fb335fe68fbacc335eebee2273615a56e7eadfe4a0fc8d51bcfb84a92225ec14df53b71bd474530db91cc613df6a8f4337318c73eb690b2d59f5b51f9603746a2b0c89a77a8368ccf7b289b1827737e61acc85324893217b3d28095c887b4d4b15eb04a0f6b24a1c46ebebe52b8c4b61a4418e7653cc5841dcb865a727e899ee86490104032bb8d76319710d0a8ffffdcbab6cce82d928c96dd087cb5b80d98b910f89280d7a5ccc248e5ef094ed6e41deb5566eae638309d35fb5cedc1d4d34cff9cf55e8b267c90769c7b2562cf6dbf117f8b09bb9f2297d0f36ebe493e69ac483b5c76dc90059a83926bebbcf64ba95f36bb332e9a9c17afa95e1ac958647d7d1f5f34d0eafb617d72297195cf6004c10723ccde974b1890bc9bcc1c3e78f86fac3d7ba8b854667a1755b539a514fa812055e37cb36a3a5638a6589291a6ebe8e3adaf46ce7f122d802c04507f9d15e22da965dd241e390ca87192dd0ba3567e0605f59db81356a403072d9572c72d046b71310959e902a6c2d312af5c1d0119518a64477876907fa5e7830d1bffacc25bf9711dbf3eeb6eef3ebcf123dd07247cef898e2ef1a1316fb8ee67c3f6ba5ed23cb74d008a5b882e40af05dcac45cca461ea3a0183555722f39fc164ee0b413f3e5662ebd0eaaba6d6c8af899001b368a63c77a97fcd826d7585829db287c1364130083da872e66f2effd33f563070e480da")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c7ba02fcdc9eba44226c70e30e8eb8636c32a72d3dbe4ff88424b9e2a70067cb"), sessionId: fromHex("0df3a929f8a3b93eceaa786bb9d896f5a4059e7c7dbec572876f18235bc71950"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("deed82f710c8716bf304a4e832567fa0a04d901d4d65fecd1d9ead08e0c224db")), secret(23, privateKey: fromHex("b6fb8b5e13d0eba684e2d3a7e3a1fd57daed13ca0e18f321a1be5b9524f7de40"))], pssSalt: fromHex("a726f840f15728209b6264ff1f1cdacd7f1d8dcae371f1cafbcd17564ff9e81946daa86ed4d3f417814a094068adeb5cccda1629e60c3e1cfe3d2fe819281eaf"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-finished", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303e51b9556092281a4f69a4ea34720a819b05186ab750edc33992c3bb866231907200df3a929f8a3b93eceaa786bb9d896f5a4059e7c7dbec572876f18235bc71950130300002e00330024001d002019b81dca9d87ce3203307532133ca664d90efa3934501e54ad94e164e344dc1e002b00020304"), fromHex("140303000101"), fromHex("170303020772bc4b47f1bedaadbd8cf5b182f1ae755755155970721dc346e3d808114935e88b0baad8abd81a2cd93a96297215e87a57392867d23ba3286f44203794d4208706347181b89c337e34427b0091547abc6e6a03f81ff6c1ec9cc7d7be9aa11529dddda4686aa3aa3fef0713c1b4274f15f42b76baa9b075061578536d47e97cb524c6041f80fcc1abc333f580fbdb79ca63c333a3f3380104e2bc46b0b4de7b7c01cd125d0a356b2f2f885ce68a09ea9c6f0da00f6b0ae4b316adcba55c5eef6e29e71ac655523328f3a03a9d0dfe60052112814717bd84fbe7b7f3a2e7f9ba4cf31f62ee7c7cbde6e76756c006c5b7c2707a0bd3699830f3814267586e53fc1b27c024412f368c52a29a343333e6332915c468aa436b9245c0a7043d4f659d51f31e13d97ab67a6541f7c59b9a1c810640283187088e10788ac9d34a06bdfdceeae1ecbf63e4305b1a5a455f30de5ae6f22b7d23bbd43ffc0bf435b0dfe3a917cb83efb8b5f816dc8760b85c1ccb7218dd960f570a52005fe19cc546addda87082d782d77fc83a38f4078bbcf379f9c2273d6057cece550f9283d88c6802657c51b75e41c88701b68d8706e95dff26347b66a7e42801b960951257411221495a1293434e2e7c875db83b750779ee15182b2cd8d71cc5ffc947493565fef3b565a0c7c44271f4349cbfe205403e51e3cfba415d0dca3820f73b4fbe16437bc6e37425b685bca8e4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("e27196dcdd140f74999061236d0488e2ed4454f1f5d4ec9cb6aaca5f407e75c5"), sessionId: fromHex("208eca41d4a7d925369365761cd0d1ab00bbc160dff46263ae0c8d67c0e3a86e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7e811a420517fbb3a6320d75288d48bbe7b76b48baec7ae40419b85ecdf9ae9c")), secret(23, privateKey: fromHex("a07af06bb0e099d847a04cecb9d49060a9afc74da2758db97ae88dc6c2af8257"))], pssSalt: fromHex("97b6d65b657c15dcfe3df87c639b8b04ebd66eae8947db248df42d2aee55f428116375035e3867c09f5c0187ec0d17580f1ab8e54a954b695b896cd99bf01ee4"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-finished-length", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303d9549cbad1b25c34c389cd07bfe14c8703cbdcf594a3e05d771291c7c79a402e20208eca41d4a7d925369365761cd0d1ab00bbc160dff46263ae0c8d67c0e3a86e130300002e00330024001d0020bfb9b8ac49a73a523ba6f0e0ea23c28f57a5cafdd6a6d8b910ddb42302bf7375002b00020304"), fromHex("140303000101"), fromHex("1703030206090e017d6ddb40ce024f8900ce9d20834438e1df1665faf19272d68a4d9c3ea39d4295ecb3eae289d4d849294af787a1db634f2b4cb26def189bc0365e40ed4199924664336c0aabf1880b7c517b6d84b932f3c90ddc693faccea7cd664f8e39a0155ddea15df51518ca4d33eee4fee48c353b02a572c95680bc18a8f371298ac20e0f2fe49dada909d43381a8765968ddf51fbd57a1ed52a82dee2c1d0115aad259ccba4d67b1db620ee75fe7f4710c8871b6899028e075f1a4854f4974cf3cef270121cb09eb76297419c62d5e036916c0815f3c36778bfb35514b366b67b9042ca3734fb27e80fce22cde64c63c192a56dd27dac20415059bccd6b13336157f46d0fe39979a30b1b0d0f15df2995e4725fef5b1be0ee2b121bd473d83aa219241dccc42da58096a0810b075243df26102b3d4a332a2ab7c6f3a63ecd74cad6f50d44b735ca3c968cc9086d9cdb3d3a056b095510f92d043900c707a88138a39c20b25a753997fbfec6cafc354cf9af1770c692a2cdf0526efac83747f4db8d8ec9a0eeb645dcd61a2f9b82e36a5c15ba03ec5bbb078cb4ee05d63c2cc4c563ff869c9c9d86141326330a50f83bf3ebd24e4e853926643bfbd5b75dc0827990a45cb1b61456fcadecdf86d976cb175622e2c0ecbeaecdbc8fe13e8c72514bf77d025ecdf7112a32a457d098d2ec580bed624692733ce4b717662755a0ec44cc53858cd9bf2")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("20062f391794d5e9a9c33d7e3a18a2368d9c6a43441ce1fae3e59cb1ff04eed7"), sessionId: fromHex("6bc710460dc760e96c0c6f25e736041fb4b65935f7e3b7a5939bd7b980961ab3"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("462ec62a0e361c0782fbc7cc5039550fc4ebb997ff88448af6ed3df2f52a28eb")), secret(23, privateKey: fromHex("dcbab9845fede3219a6b3aad8ffc293824919553c1450fa057b86faac7aeb2f7"))], pssSalt: fromHex("09ca753c5f7b8201e49d1f4b6c568cddb428352cae963aff5693435cb641bd6499a27f9ee341654ce89a8f952be286bb95c06576242bfbde2f82ece917f27b4d"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client13-err-message-order", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603031dcd31cec6b091d077302f2a0090de6a59276d0d067251e353d201cd461a7913206bc710460dc760e96c0c6f25e736041fb4b65935f7e3b7a5939bd7b980961ab3130300002e00330024001d0020184c941fe9b143c7c7d0868bb5be987c8934c676ac54f2e46f8dd2aa3b4d0854002b00020304"), fromHex("140303000101"), fromHex("170303020636acb5e8a420b1f3b003c033a9791c7ec31bf07e4374f307787cf969aedf06da39e6749ed9666e9ba83763c4e69a93d607863733f22cd4d55ea9e2bf67666fd0422626b98b867a022820e3e945f77f65ec2c0a9160c7cc2bf2cd9dd8c1d2796ce817af4d78380292be443be6cc9af03ef2d6bb96666cb76bd19e2a97430ab0ab4bd2d8c8a9189184534d08d3c00e97971a6707c89069d57c840476f63b6f17c9b1dd73457e973e9f4c7b0747d98ba5a72341474f487d4826f4de07c0b2166e769e3c0fad74e8d508f35bc1fb493ee1c07a1824248e8cf30c446e50978acd3af95743f51840a034a3c4133922e879da3049b3a706ef510b9e9543f3501704a806702627794f258735fc387de9b5bedbd0368b5962c326ebd084b9a109977fa5db472a3d911a790dd4f0aa6fd1b5cb1049dcd60975077cb71cd34a43bd33b6e1459429e106951d62011e517e0fb7f130ccc64bab4bd8839e61c83799054715c4b64c38098731229eb27ff3ef10d647b13c60ba260d17a97882a6ff3158d2a6caf332a973835e03cfde0d82927b3f84ee44da198564b5acc2c61fe2c05e4c0b6ca29007986a6c92c8ef71854218f94095ebcda31402c6464b0c5fbd05d3675b61f03f0e2d2d8e9f9bb5a9b9a38aa4dfc55c0205a6cd4d33cffc8b452f0826c88b86b12c0fd4a6d39b5684d8ffbea50e0cbaa420eb5eab717ac1d33f8ef45ee0a5cae52a34ab9fa9")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
client13-err-certificate-verify-signature decryptError
client13-err-certificate-verify-scheme-mismatch illegalParameter
client13-err-certificate-verify-pkcs1 illegalParameter
client13-err-certificate-verify-scheme-not-offered illegalParameter
client13-err-finished decryptError
client13-err-finished-length decodeError
client13-err-message-order unexpectedMessage
```

<!-- test: tls-handshake.a-server-that-selects-tls12-with-the-downgrade-sentinel-is-refused -->
<!-- tier: stdlib -->
A ServerHello that selects TLS 1.2 and ends its random with the RFC 8446 section 4.1.3 downgrade sentinel, `DOWNGRD` followed by 01 (TLS 1.2) or 00 (TLS 1.1), is IllegalParameter when the client offered TLS 1.3. The two vectors are the canra `tls12-handshake` spec's.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("7d4bec3b0b9d15cbd98d823090cf89e940e463718c6801773bfce6b602e306fe"), sessionId: fromHex("e38fa6686a89e6da03b5d4441a0b760a6b5a66c7a258bd7b9c368507fc40224d"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("79f57dda2ae5389aacf4cb7fb836fece8093bae0fc05e2f2a4c6c2a8cbe03584")), secret(23, privateKey: fromHex("7ddf886d40154dd2588f43d3b842b90c06eca8aac6df3bac543acf77a9fd11fa"))], pssSalt: fromHex("e7f98f56cbffd3e846359d32d45c3e1a9c9541f198beffc2ccdcbf481468ca2d9e39436e2b6d0c2e5b2982420a127f89d2a1f0f8dec2e70af6a54826c198f6f4"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client12-err-downgrade-sentinel", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303022c0200004a0303c2cc32ccfb1ae6cd2d89ddcb8ac66fbe918a274f55fa8ebe444f574e4752440100cca90000220000000000170000ff01000100000b000201000010000b000908687474702f312e310b00016300016000015d3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e7361970c00006f03001d206910e7f7bb58735f34101956e1b784aa724ef2899af15c5b8fa7d17b04cce85c040300473045022100e19224bdd52b6e8c24f251f2fa90ec0f024a11b3fcbc81a9508b96abbae4441102204f9bd15f4a343b28d82e4b86b57680858b22f7fbfdf40800c39e948265b381560e000000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("ad461c530f822b2d23aa8346768d325e06846fe3c40d2860c8fda23cc9d629ca"), sessionId: fromHex("9bc5b138fc7a4fa048db4e6631ab39eb2be8eca62ec9d0fcf8670a4709c4524a"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("3bc8ad0aa3836362fe289d6d649e3aad0704bcae595237e85e7e6d0a5e5b5d47")), secret(23, privateKey: fromHex("576580e55ac3cbfe87b26bb66c38b3a1ad1e12a81daab84e22a80c1c365d2e7c"))], pssSalt: fromHex("7f683eb0230148c5f5254220f31002c1a2f8a34db1ef5db67864f0f8c4c5043baa41771b429d63a45888503a0570601814b5af25dd4d42ab26428b8d04bee524"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("client12-err-downgrade-sentinel-tls11", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303022b0200004a03037b3784c2cde3deae29a98bb1acea807451ffc1ff2dbc5b36444f574e4752440000cca90000220000000000170000ff01000100000b000201000010000b000908687474702f312e310b00016300016000015d3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e7361970c00006e03001d20465d8841df816e9c132bf834ab1959ac4b6b2c081ffe15858ed1cf2e8cdeb44f0403004630440220216008647dcfd8d843ab445313aad0eaa8eec3b80d96aff7019cfaa5b59ba3f302204b3fc7ec44fd245adfa6c280494376a0f842c733587d59aba96c8f838e0881b90e000000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function main() returns ExitCode
	vector0()
	vector1()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
client12-err-downgrade-sentinel illegalParameter
client12-err-downgrade-sentinel-tls11 illegalParameter
```

<!-- test: tls-handshake.reference-handshakes-the-canra-vectors-do-not-list -->
<!-- tier: stdlib -->
Handshakes made with the canra reference server and checked by the canra reference client, for paths the canra vectors do not reach: a HelloRetryRequest with a cookie (the second ClientHello carries the cookie last and the one new share), a HelloRetryRequest that carries only a cookie (the second ClientHello repeats the first and the client waits), a KeyUpdate that asks for no update, an ignored user_canceled alert, two ALPN protocols offered, application data written in fragments of 16384 bytes with an empty write between, and records after close_notify, which are ignored unread.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("8f9d8b09ae1b5fabf0148674f1a82aa0c965892d99cac437318f47c31d09c7ef"), sessionId: fromHex("facd3811a57adffd61ba517befa2442b7575bb914c197fbac07ead77bbb518c9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("e9b00cf4e66e617e36d91f0e0b5748c57a180c4ca2f176b92ed17cbe4a306792")), secret(23, privateKey: fromHex("76882a89864137a38a1d994981578d413563be7c1c3606e677d475a29321aa07"))], pssSalt: fromHex("533d9e7a0afa4243dc1683bd1cda3ecbe0eac8877068383653a57cf8ca552083e24462679a0484cf419f6ef73465235fd5d3d5b5bc0b79603d9744dbb06bbd27"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-hello-retry-request-with-cookie", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("1603030076020000720303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c20facd3811a57adffd61ba517befa2442b7575bb914c197fbac07ead77bbb518c9130200002a002c001a0018c0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffee003300020017002b00020304"), fromHex("140303000101"), fromHex("160303009b0200009703037b180bce70b577964cb586cecc9ce7c1c2af251c7600e40d1ccb9e1eae23374520facd3811a57adffd61ba517befa2442b7575bb914c197fbac07ead77bbb518c9130200004f0033004500170041041b8684aaebb95e43b0465167d17148a8a7ed2e80653bd85238555252f695384f7b16f722e1854d981b248ba31c90a3778870d32e7516ec758995d07fa290519d002b00020304"), fromHex("17030302183e965ce6888a7b9c494c872687dc3e15ea21075bc486714e4e4d7609cf003e3036ac5be05c378f6083a758f49076e395a4c2c409ab4a9e0663e895180b4e98ad5f72db555c79fc9dcf5137b5c8f6297b8b06937834f3c86325d02b37ebe527692830237b6ed66227405e5cbe5a79d436cae00b96d41a9b9efbb86458854d58139627b869ea932a65f1fe339188eb74e0794d95798dd9f8a626967736bb688b2e901ac60b4004d029ce3978982b391df1b4aa9ed13ff22258a08c34793df98ce8d0b938ea65b3de4e8839267ed657f2474e0ed0e853c5de7e0d3c7e3c12101ba7ae7b9636419bc4cad7af5f8b93cc545fec3613804069598c3ae67d11cb1570b1f51c0cf27930a1cd80a0cf96082ec54275d0af7af5597c62aec82dc8305fddb0479103d7605665f3de4b3326560dd9b5d6bdca7f01dc7e556c86e583381ce994f180998b721867cb7f81ff492ada2a505ced9b02331f37c56a72bd06d53cf5d65343d39377241aef948ee8fe640b8ca920cb3201689da0396a63b4394941e0343f32c8afbfd4fe54341d691e1f6a305eb72fe18eda557d7e001cf5118ae79f511c31617110f5d8e74347b73cc381f1d58bd52d2daa28ab121e0deca235e183f1dde9f414645fb706d6644a2ff2cb56800cd62f385b1a55a322635153ed82b050c25dad879324d04f37c7b3b8b9ca8e142d36d058044866ae49e0917726ecfdeab227bb2b3650c1b0aa7579e5e2bb2c149be84507d7fd9841"), fromHex("17030300393db65b0896f3913cbfe41ce67c5634808772631b85dce3f19b5c1808d8fdf29fc841670a9b27830169687a1a35885e68fd4fd0f0997d47a966"), fromHex("170303001379fa0869e30f1c66c8cb78903ad1cc0ac82139")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4866], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("a173673210c3fd6fcc8cbc556df99b892b68a965d22f8ea64d79e8c4aba8ea71"), sessionId: fromHex("5392059235f2e8dbfef14eabeca9881014b202ae1e5262a0ddfcc911883ba8d4"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("a6e3bb0e805b52cdd116f9fb05bb1bba137cfd42e18e29b5927b8daa15038d76")), secret(23, privateKey: fromHex("035266bd86dc25a9aedeada10afc559cc7de1a754e3661a155cfbef87620c960"))], pssSalt: fromHex("32c248ee5f4374410b6d781c3a24a57800b38a75c4c0fa96699f466385b4509df6b78d1366ac46c5e8566dcd75eecda07a038a1b84364bbee1a42276a656cb7a"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-hello-retry-request-with-only-a-cookie", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("16030300700200006c0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c205392059235f2e8dbfef14eabeca9881014b202ae1e5262a0ddfcc911883ba8d41302000024002c001a0018c0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffee002b00020304")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("c1fd848afec3db413aeb9fa1f58a89b2bf1026f55e61458440a5d257f17794a4"), sessionId: fromHex("b47dc3ec41b8c1421c99518e099e55e4fd3b94eceb21b30363ca428aace19456"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("529c6d8c0ebc16a47510ff285877e8a1aea22d4ffb7ed0a85a30e54ae7c555a5")), secret(23, privateKey: fromHex("d7e387127531e19f86cd3ac09ba6612ce983b32ed648d705820957eee260ec17"))], pssSalt: fromHex("66ff9621655a0dd4dfa34fc9b3d68443c88c9e32f6facfa07d0b65adf27319d0b61e727816e440b0772ed78cc39eb41a1a18958d61e96bb80b351b76703cdaa7"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-key-update-not-requested", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303a66ec8d970021c6b01943a176131ead2d587b16d8fd8fa987bcfde00ea126dc820b47dc3ec41b8c1421c99518e099e55e4fd3b94eceb21b30363ca428aace19456130300002e00330024001d0020d74b46728262373cf92cf7993b168882e6724097fc21cffe0e463f1711cd052b002b00020304"), fromHex("140303000101"), fromHex("1703030207667bfd130b6fa8346881f48afde2db326389c276ddc84091a6433c761b6bd7335208cdae64467a0464194d8b3b63f3d4f2c18075b61dcfa1501d753222a125452fd50b25e2c6aed7ff1e75b00df471c22a3362791c4c3f806f9f426cf47409421b50d53bf60b7d55184168caea63677a8771eff301995d53fd5051760f0212274ff00e993d66239acce34c97eca7433449c62946c5aa21c8c9faf421bb69f9f1b738b2acfe34228dc5bbf411905373984d9074902ca29b98c5ec4c2553a0f9598547647b1142a0d26ab4464a12fbd5fb47664551a21e12efdaaa1e68204e4d2af7a34912e4e5caa0bdb88e00e87e79a0348a32d1b547349d078ac7f1fa1df99e0ecf92d278a6a8b835d72cbaf7d8cf629c77b8b656fb01d6dbbf48cc9b16205fe8a69e42f5eb51246e7572ad106ca62d0845baa2a5a2d3a783482b680e786159a7215e70d3a5ab30196a9bf2eb41f6f810662109e5257a9d7af208b5d0e8e0b1f87774e91d7a190172256f25d17da3f58af617ccf69ebd760c00534a75d33cc33a810a1ce2fb0a0d1d78288e3cb1a3ba245743585a805b8557270371ebb6153c564df7a33ff0d793ab6d5bc0a9413d3036a581590f370d78cb5a441ff97ec6ecf2adb4603bd6504476eb72c73941af22ae0e90b56b49ff950b12ee4f5e009543046a0c4f547d4c8fd4c8e65e67b4259d3cc9e5be39a1afae6c9a44701f46ada2881627fd559398"), fromHex("170303001625a0c956113ad7f5f0468dddcf56763fbcb668c207bc"), fromHex("17030300399f6d24f1b7b87ed2f97f20722bd94d6d358f7ec7280b360662994cda55041c3cf36e106bcdede8126c68e3a3131fd0a12ff881aace629baadc"), fromHex("1703030013a4492c294e60908b5ffc1aadc523ab0bc1787d")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("a9473b2d3a7e91cc57c7bf7ff55f64c09455b53f8f02ede2067f76dc9204dd08"), sessionId: fromHex("52d7cfb7cefc18eadc4c3c8cab8cc6920af9f0517a363749ceccd7dbf87f5f7e"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("e1769aec6b8aeb9b2dc690c2f7b7f1bcfc6f6e0626959999015b6350e1faa198")), secret(23, privateKey: fromHex("cc16178a56ddf10db5c425a2c97c46728f7945d212112f2b28fd457274cdda6b"))], pssSalt: fromHex("b2ea8f2dacaa6c46b0606b9cad7a29f8dd680ae8f3cc7ef916555ad724c476aee6f8f9f23de43435f9b3e8160a421f4ded3b587029dd68cdb655aeca42cd4d1b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-user-canceled-is-ignored", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a0200007603031dfee66f448622ca483f5d938048aa0e8830a74d3a9a2dbbc6852ae2c4a4f08c2052d7cfb7cefc18eadc4c3c8cab8cc6920af9f0517a363749ceccd7dbf87f5f7e130300002e00330024001d002076f125d8354152027d299f3f4c880aecb0487ec3f38584ab37b480dbba4e0713002b00020304"), fromHex("140303000101"), fromHex("1703030206d9a54a6a745da4fde936e970b27cf11a8c9850cacb4d0ee00b03d33c3f7b5706cad4ef661f66c91fa04f3c37a1285b8878bfcde8d63d0040ae19883cd4178094a0425fab27fce86d2909c06c312eb6072e7d179a66ab00a2cb96ce85d98ec38e259a27d428a6455a7d9d5bafb072087d65b1548cfd75a39b9010197adbbb6adceafb3beff2816fc37788b24d8851910fa766b147d4e2b33de7818c376a0c83203d4ba708213b9d46dc7eacc010e7aee730c3446516beccd643ea3e2b36c4b5988a39795ff6b623e4788800bd600fdc867ee01bbc339a98b7af5cd823bc2370467cf4a947928e7bbad2893d92d3564520ddc265f5e80a907889126e4619b657fffdcdd810865e956d9541d083e6b42e593353f664c1e9ada03bf737567efefb164ba8840775492416be0a4dabd43431004719a9664ba41a3028d9c0a02b25ea618d881ca5123795b795b864431c2e92344d8c0d4ebf9a87ab7cf07a100a6cb23eb7bdbbf2f0fb4c978191a5f02dba65c3b12379508aca89f67ebca4e18cefb98376cbed83f08b669b743e6f6db1fccb18305b309fd5bb3ba5f3fab8ef4d12d2ceeb301ea7067422c47964b65def93d28347e902402de923364486233b818840922b6f138d33436e10e284e53e23bd27bba33ae674ff8dec5a82115d0571958214b33592e6f7843f7ff6dcac151b05c62abcee8bd2cbee2ea362edfb6dfe35400347d7d6ea50e4"), fromHex("17030300139ad663a9642230a5d85bf6ccbd95ed60572f68"), fromHex("1703030039cc3ae246fe9cbd258f94515ab1504731142c36723e2db2be5924cf12f334fd5954ce5f1505ef4e0ea3eec23342ba02b3e5b534c3e22a487a90"), fromHex("170303001390bad7efccd8be1645fa59b0288f63c786b862")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("6832"), fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("6562d048eced46757daa6fa79462773a798772097be693fb422a6490c9ae1c4d"), sessionId: fromHex("81177d80997929db0fbed678a2347748774646d8d1f635c04ad10dc4c4b1cb90"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("e469d943ada79594e401aa27bc4ee1008d394303c1c468ee5f968fb7f7f071ba")), secret(23, privateKey: fromHex("4267ae195e513c2900d9cbaf139f9080b18e960f70f96e70fcbbb580c61aa0fe"))], pssSalt: fromHex("451dcef590f11073186dc0d28d80442cb27a2fd2d5404ac8635c9f9bf9f7048f28ed1ae774bd2a7f0474a11c391a20d02d9182891f68dc53623b2c317f69623f"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-alpn-two-protocols", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030357e27e6dbe8992b85bc26441696241245c1cdbdbe63626e9f40a6a55c43c056e2081177d80997929db0fbed678a2347748774646d8d1f635c04ad10dc4c4b1cb90130300002e00330024001d0020c4102f4a1243047d6c55ca8acdf61c64b9d9ac348a87a4d21f322c02b3193f38002b00020304"), fromHex("140303000101"), fromHex("1703030207f58a8ff036360201901dbe353f014deca8c16a583a1ad14d1ab5d6753ab2dcb44f0f00692c0843e5a42cb3cf7e95e42d1530ce019faf9c67128e279e80ef3ee9680f97a567b17b76837772954b1d6a3d513669d72148724c43b3767095a9fb476dcd7353abc473602191946fd233935aa13cf4cb108fcebfd32d0a0335401e0fd017095210afa1562d810c0c07ce87d6e5029db37dca797294300123a5bb8efca01cdb3b5aae594f540a05330dae3ea76690905347a578735943e67be9d1e092e3c6a34535b088f9578b70dde5496d21b9592db3d48ef4c1e4e3b61459a6d9c3d86ee0e17a26172b5e322402f2c3cf503756a14cf3cadddb3c72fcedd0a560363d2ab9be9d53a59682e79924693f97d36321c18458d546c212d655c04440f64de183541782d43d73ec145165172289f8fd423bb185aee35b7149033d951444f2d7533227c91b7f5a797f49a478a36297749ac39f4993b0c1c177fc232fbfdd1abea17a28558fb20410ff057e2988932016560baec1ca382d21ceb72abd8ab5ec7215f97b71c97100c00a23ec8169bce804eea30bcbff09abb234b6dc32869f6219762693c11780179836c387e41b1c0b03eed16e0bbff818568a9702ab0de2a2b1e132fd6d754444b89702fcd4d0a407cccfa7584c45447d11e3ff23a1bdaa6cab4777855cedcda57123f1278cedbd9d9a4b7cd041cffa119d33542e5247892c1a1af28b7b6dd3"), fromHex("17030300390eb45d7623dc0aaecf7f4f5fe5f97887b43520012885e72c4e71d87fe8dfbdd0221319e0a44ecbbed905809d2951bf5cebd07a2e5f1cdbd02e"), fromHex("1703030013e0c60ef31190ac56666c9fc8f436a6b4588646")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("df65e104ed31becfaba77917a97e38f040047518a908bfad42ba301bbdfc3ead"), sessionId: fromHex("9eaa66f1360480c0d8f461820836761bb1b79e5ac6f25432061bd43557353f80"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7199a67b4e23dba806052a7c90de7b67815a8993455b3a63de201aec1adfeff3")), secret(23, privateKey: fromHex("8b49885fba79d58993875c9a9ee5f6a437afc8280bc1d40b7a969d69d3640c8b"))], pssSalt: fromHex("cc53ea39424596997336e8bdebddb879dcd7c61f5a868c8d688a3a6b8944eb29bba944b1606bf12e6aa00a8c4b2a3d61d178dc233289a65530fc95e90611bf0b"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-application-data-fragmentation", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030357eaf44384539f52b8084ea9a8aefffddd38a1d4bee999990086bfb01320612b209eaa66f1360480c0d8f461820836761bb1b79e5ac6f25432061bd43557353f80130300002e00330024001d0020adea2133efd2e860b5cf7cf1c6cc2a2fb906c0095c625c27bba0f2112cf83348002b00020304"), fromHex("140303000101"), fromHex("1703030208dc99c0ac786fc16d58036a9db73cb6ce539681422e5fe75e6ea2c664962a0d0e359815938b9a54958a2e09937efc085993b2ee5c9dfed8de7c72ef60c18fa7fb0180d3b7bf86cbcf225dea4fdf07bccaa8cdab149bf347e80eb2ff0747a14f8495d01a2b6c42a9db3ffe196af22969125286fe11874310bd5fd459fd7d156f1e1bfcce965fd32edc43b8bdee69b26192f0cc034620e51c871ac65ebf522dba2be72e62a55cf1d070e16e5a46ff28c2622a7586cd6114c1763f98f9557af61eafa004c254ae5f8864bcbf08376edecef487968067e5f4fd6e37c97f49907c3d631fa78f57d374437bcbc9558df4c9e51be3da4bc7b13dcec44a97425150510abae56454625a6c3456ead4b1a36df5c72ba9cdda4308de80b2757089c20586b4509ab601091a60c78b69e845e0e6f080d1fd8474dbfce3c2c3852763596275ec1a7dd0ab1d831379eb372c6428acf89f57facfb1d6f0ee0636ba6c509265f6c43900b02e62f5e66c4e616103825383f2f5e070743dd6cf13d28e12e2257092c3039a6c60aacc080d3b54d39016a1c871d7d2c15b70ea301e08a213c65d245483fed8bf0f88576f639313039028e212030d02329b7c40889a7ebe2413e22f3f9df99b06ca1745642e7949bd2f0ff27caed5ac58fca15282772ab8a8694eb69dae875de0f6c08732cad44d170c4b5d34a3c6114134487a01f933ba1845b34866e8621a3e05bd647e283a"), fromHex("1703030039b2783825d62863c3041df834d97a04d5c455fce761e3defa9750f46b316659a4005cdd2f4ecdef9370ccd3fa5da7997813dd37d2e0433a710c"), fromHex("1703030013bbbbc5352b892beb01ddaafbfd8345dbb1a7a7")], writes: ByteArrays from [fromHex("ababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab"), fromHex(""), fromHex("cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd")], closeNotify: true)
end 'vector5'

function vector6()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("2832c778b4911a028922536590a3d5b111d976cd2c6b5b0778ae08669e69e542"), sessionId: fromHex("1a6b8ef84df03e655f15dd1dd573d4a6b697ea0a2c77d3b37c2a3353d2ea6c39"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("883a91a5fc0c7591217a6d325a4d7eb7594dac8e30f46cf561d07c142e71f082")), secret(23, privateKey: fromHex("652b86706fae27110861de9343c25252019490361af05e895e04a126cfe757bd"))], pssSalt: fromHex("9e474b0360e442a038aca5fc6e84cfff390519af0a1999c4211969b9562cc22ee4a8e8b199114a85e2985e65f95e964a347117458b83a1791919070e454ea8cc"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-records-after-close-notify-are-ignored", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030397c26bfc6718058745d0319d935ed134cf0186670a3aea89458d3ab70a165c86201a6b8ef84df03e655f15dd1dd573d4a6b697ea0a2c77d3b37c2a3353d2ea6c39130300002e00330024001d0020c309cccc768ccddd7809a4049f053964fb06e08ad6389e46d8aa3a9aba370a34002b00020304"), fromHex("140303000101"), fromHex("17030302089d548a23969fc4f4a069223609087c93c2a2dfb50c21174de68d01755b9e93f2468ce25a573669ee4f8e9cf6bc3b6d3cd1b052e0cd30510b39be7ce7b0dea436c382769e231f80e2264a7c73b0b9ac5a412ac696e66f31b92b70a45936c8b83f81d9768974bde6bd972bf337a8aaa8bdedb7e24ee2e62d73d5185c97aa216e1b51db4e062a656fb8ee8e6bc0255768e795211fab095a9ed2b789846dbe6097835e10b85bf1245c6655f853b08b2c92f432d26aa568b8402288226a2346e3ff6c4de8b7ac4176a13a0928cacab3a489136829a95f4c5d592141df6c90a33ea1c84fe22e2c0d6770decc2d576b2c90b55c374f1d55c9cb825b0a872473a95fc3320c1c2de5f1b15f54473902c2ac881c8b2cd2ddce3c450ff985965bc5076d9d9a94d86d1f17006dccf7fae9b727ee5afa9433b8e762d19cec46a5d86147d39730d94963df0573e4a98ab3cdccb68f666aa109411aeb0deb1dd83daad524a78cddc03a5f202385f3812c7764373d9d896c476f82bcac537d2d134883b3e92ef941b96450bd56a3194b827b124b1e0f2eab5a7fbdada30497fcde48f5610ec4e82f3cabd0083e90ce6c42218ae3cd6831cab553cd7fd7acd974613d55261517f224166cf0af19b3e5a15fc52c74605cb07b195888a7581f5ff7cf1de85e3a594fb6807526b08d26e9ed5e0d6d0e8ad21243332d9e1e2153a470d8c7206d49502391758a050f757df324"), fromHex("1703030039895b7fcca5a57edf20647e8d1df21728807b680840a72adf1073f186889e4a36ceb7e0240d3fc7e920ad720cf77b9cad0c7836cd48faead200"), fromHex("17030300133df906712c4e162e0c88bea57d8fd48c82faa1"), fromHex("1703"), fromHex("150303000100")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
reference13-hello-retry-request-with-cookie {"state":"connected","version":772,"cipher_suite":4866,"group":23,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100d6010000d203038f9d8b09ae1b5fabf0148674f1a82aa0c965892d99cac437318f47c31d09c7ef20facd3811a57adffd61ba517befa2442b7575bb914c197fbac07ead77bbb518c9000213020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020f8d5e1a2ef94905dd5da692a1b6278022bb64eccfe47bc98a634f93ae872bf1d002b00050403040303","140303000101","16030301150100011103038f9d8b09ae1b5fabf0148674f1a82aa0c965892d99cac437318f47c31d09c7ef20facd3811a57adffd61ba517befa2442b7575bb914c197fbac07ead77bbb518c900021302010000c600000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300470045001700410493f2f7024f2ac6b6ad81e6a05d2a3f18710f5f78987bfd5885b46bd7446519268ce1dd2b4497e71847f92eb68a22382886d770b12c22e529f874fbee3717b27b002b00050403040303002c001a0018c0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffee","1703030045dda9b423c60dbadacf9398fca1bbcc614359a62f2f9bb77eef5b561a5aad484bc5710923ab0eb52eae1528c13da436ac73da2358fa323dc263403655b581f3fa7b9092b4cb","1703030036386b667553b8ac5434116618280f925093a45aedb069d5edf2fae87ff9b3696b9ac047f4d47c61f2121ae530c8be7831d5d56b7dfcf1","1703030013ad8c8d8cdaf7f2daa78185e30a9ccb00088c87"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
reference13-hello-retry-request-with-only-a-cookie {"state":"handshaking","version":772,"cipher_suite":4866,"group":null,"alpn_protocol_hex":null,"outbound_records_hex":["16030100d6010000d20303a173673210c3fd6fcc8cbc556df99b892b68a965d22f8ea64d79e8c4aba8ea71205392059235f2e8dbfef14eabeca9881014b202ae1e5262a0ddfcc911883ba8d4000213020100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002097f322bcf1ed775891f8acb167039e0c3b6510540f6a9db4db853f51355ad47a002b00050403040303","140303000101","16030300f4010000f00303a173673210c3fd6fcc8cbc556df99b892b68a965d22f8ea64d79e8c4aba8ea71205392059235f2e8dbfef14eabeca9881014b202ae1e5262a0ddfcc911883ba8d400021302010000a500000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d002097f322bcf1ed775891f8acb167039e0c3b6510540f6a9db4db853f51355ad47a002b00050403040303002c001a0018c0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffeec0ffee"],"received_application_data_hex":"","peer_closed":false,"peer_authenticated":false}
reference13-key-update-not-requested {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303c1fd848afec3db413aeb9fa1f58a89b2bf1026f55e61458440a5d257f17794a420b47dc3ec41b8c1421c99518e099e55e4fd3b94eceb21b30363ca428aace194560012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00206a6320614852e9c79c7ea855700f63add5b0a9c07083dbd6bfb5922f1d874a5b002b00050403040303","140303000101","1703030035e9c137eb7cc23c26d88480b3a862f414b30ea14a1f8ec0d19515561f8aeb924f9e82b11364ea1961c8c4138b95bc64c41e2fe44b62","17030300365015f29d7b5910bf8602abadb725a09f828b4f93fd84e0afcc0b6abeb6b0f341e385cefb38d0bda8ba2e58eb1e45d4928d305060e998","170303001343dc48b59c83bc7988b649f35624ff59851e22"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
reference13-user-canceled-is-ignored {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303a9473b2d3a7e91cc57c7bf7ff55f64c09455b53f8f02ede2067f76dc9204dd082052d7cfb7cefc18eadc4c3c8cab8cc6920af9f0517a363749ceccd7dbf87f5f7e0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d00209512ff0f7895fc90f4b6be5128b67df325f1a4b83b38aaa2ea3ad43418dc591c002b00050403040303","140303000101","1703030035565b770647a8ace5f183fb772c9bcb7a1f31d4c1bc94a804ad3138408cc36c3c91c76f9ef54978c3c7705d5d98912d53bc9cb64fac","170303003670bcf7d640453a7c87466106bd4454f1edc839eef719b497ff7fa212803b22d4e4a5409d6af250879ef5d307f172d97dae4a6510b8cc","17030300132e439f53269f3217088c119722f32f10a892ae"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
reference13-alpn-two-protocols {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e9010000e503036562d048eced46757daa6fa79462773a798772097be693fb422a6490c9ae1c4d2081177d80997929db0fbed678a2347748774646d8d1f635c04ad10dc4c4b1cb900012130313011302cca9cca8c02bc02fc02cc0300100008a00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000e000c02683208687474702f312e31003300260024001d00207a6565e112277a28fd470b5399c3d84adb164fa876b31de58d5559282a9cde56002b00050403040303","140303000101","1703030035e38a1f1ed1146bafdd49650f171fc9e4d4e6fb8b2f31ea8215776069037d96fa3210c79f98a020ef90c4f0a5006f92a7b12e37cd6a","17030300369d559e4be2fca919091c88131322141361801145ecf541568cc4e6690363f28692c785128f7abfa32b4cc40c46b9921715d309fe97a2","1703030013fc819ace80aa40a0139352f426054ac26e0cf5"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
reference13-application-data-fragmentation {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e20303df65e104ed31becfaba77917a97e38f040047518a908bfad42ba301bbdfc3ead209eaa66f1360480c0d8f461820836761bb1b79e5ac6f25432061bd43557353f800012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020c319f8b527b43c9f22b6c7e7a9e08b01e23b96f551a3f0377687d6baefc16f72002b00050403040303","140303000101","17030300358372839164b8ebf18e6b1bf3b5e739a035897ea2f8367702cc56538a13b2e4b6f819830fbbc0f6daca5b37beaf0ebcc9f52e78a1ef","170303401169303c848ea4a35aa0f2f7340af61893e84beedd19dd4bb17cf42f8dc9033f208d18a7a92c09d4ad3b9736d8d3d3c135b8da875bd9ee98216197a675a6fc88bbc0e26add6b32defaf342719407d50d72cf5567b9f77714fc4be365352b438dbbd0e91b3f31d0e449045d08066b8ecdeca99e18e7f13fec5aa87bf6c08c67b5f3fc8de70bdcc3a18dce01e49e3e870b984c628ce508b7a6cd4b6d8663d85816ffd6330ffa4b5dd55034c3b78546315cc50ad18822baec7e266ec214386737bae5e43c117efc98e9e436d7d4257bde44de967737916449a6f889850da6c1684a7212c637f5cf0a0dd23635211c622ed90db1fc4e7a574729000d0da4c6b599e6f54e6a92b3886ad2648308fb679a190b76e929761cc228c5a8a2fc27ee7aef4207a598dc37ac5f1ab2fdd6f8ea44914bb25602d80cf00b4366b2b05ca13ed3838602d1cb1d9fa177fc01ba990ee634f3d10272077f468c4e08a53b182d060d1c3cfca29936ff582a6fac01e8ea8d1ebeebb80bf10594802081052f0e3af6f7c9b7d3209531ed2a6507b838d719c05e552e640bb88f0af901e5fd6f6f7223c11a20cf7e90fab757340aef95e80b606883345e8a5abbaa0d5285cb15b08c3a660affd5ff62a0cd8cae6a7a0d617d545352e54e607bc6772f5eef4835f8f480ef50b0ed5d282c37303f2e10233c783fca156421a9857071df967a290f47003dd1f523853597e215267f252be4bec6569826504abd551ba10afb4e4514380abcd445f8d5d0300e562aa89067fc0271727b9f3a3174213d2031f52f2c705d95ed2e28a2d25ab90e2e642aa87101df4d13aa32420c5799956c403000887c22de0161308f59c1e8be5896bc8c254589a7ce2632e9eeecd92e39da25e3d8a14343aed62d00483e17d70a37b261fb5086a25594c61e7a90c14608401c267dd3316cf23edb69c739881bdb9b47da5b3f0653c188f6eeabbd9f6495ff18559ee7a70f6a329878c16b0bfb6fcfb3d052aca146110aefdcafd6cc7b24ec4de34729c10c27ea0b2731fd6dde94d6bbb4cfb1d0f78dd61b237aecf3e4bf76d0f414c56a9ad8c0b4cc3027b166d3f77929e8a76fb4ddb7fe142f8484046c32a841c56f34b6a81629ee4776ef61fc7eebff7830bcb5c4599ec8f728f610c9a189ea0f4e1ffc8bbb0ee47924c6f03344252ac78dc7233fccfdcce37e45d03d2ac6ded473ea252c215958164864d831f58e8499378822c92fabf7c6d86dda529c63db4944c98668f388648c9b9eb45a2a4b16b9db1df26dde1e2be9ea5d0395d0c56865e3e1d7a855ac8cdea1d4e680cb39156ec76c9ff72de5a06673f217c3183f78c2274b2310d4f5e4ecc10cb4d48f490a9577121f65c2632119a5e104921c7d7529c1e1f2b180e93406656b1bd026627de84e506fbdfcd71836f7158545c09eeca8496006ac7322e089498c997fdd43d749b3091d9f5f34ebb9467df3cd3b390969fd19a6d893b831545457b726102889d9036e9a59f23ebc97811873e5e1d6ed384b1e76d8de017f1c9705c7b3816e93e4890598890462af218280fb0d77f2187ff839144993364e28a75f6f38dafffd7672724e630a678fd097685cdb1c916051e0aec0489e5f30220221478a522a2bb25b5664e257b1a3a00c562bfc28c1903d22e64400d39e5b9bbec24fc312611487ee0af30654bd113c743268c83258d53307abe90c2a9003ffdcae67f5736d42f93d25e94e9477c8048766fbd2172eb23cb1081ae67962fd63811463c3be6fa113ae9483fd921790cf54498fed88d2c52878933ef45b9de172e8b4c8e5aca918f4bbbe7b90a1308765ccef64fdd8b182c9aa4c3121add8676150e7b82a30661979b6aa79950ed78c722b9dc46a8a0fa9252575d685bc5d3a08503be13a5264f02fd8686b644fd9f1bc67f7db6fff64972b0b21536faf504217c23fb00a84493b58f1fa5f84f1aecb8dd1a062391c68ee124b0199a8af40cceaa57b5f2ae68107abcfecb4903bce8f38963951c73c0341df887115d32631a53750ed110582fbb6a577f82b082a82422967a07e71f0d6db3b6f0738ea68a44a2bac7beafa2a7b45ab80e4fdc30223c3cc1116a4ff1ed16a8bb8a0cbedcd8bbcd01447937db0399b9c8a20e8c6a2f327be1ea3ccae451d394c4f07f9a04ab8532388d79b24d3318198086fb5a09abb285b729b913ed55f045c67ecd3ffede487cf0aa3e4b0af5e7b699109d071a1841603eeb72d5904cdf0550042aa1674a40757c2e68a709d833031a8dab15b16b9ee0f37bd67ab8d8924f71c5cd9fe8fb7be02835a71ebc153918b6bee47c71f65a42dad6b2099489dd7828cb0c1780ba08c95948e0371e97147545c61c28710c7eef10d2e3d1214d97f0c8f12812327cbc1909e2b9d2f943d744abee7cbb8627a00b0d2cd79952872bd9970465c168dad1ad4a7622b3e8c03c1a57e0ab639ea6e0bd90bd758726e67f6a297b1584dd7bb2e1a87aaf873f4d808dfa2e6ed51a9915ea9d2a9e5b9997d0aca0c9ad3e25c7dbda5a97020de0cef2fbb9ca00092cd53fce133bce6a3a7b012f8a706fa7795884327c37562036a8ad9c9360bc1062a338f27469fca7177786002ff340d29c7f700dc4f05007a3ce795f158d3d0e18f8acb6a156931b91027fda01f26444dd324bf67ded706e9876a19aaa5b3e26aefbc30125de423fa4e3ea088586234bd7f2f0ad064325035f080594f0f56c3661d4e9c7d2812312637c9152f04937a1ae298a3f5bc3084a27be267a7b0b9e516d8acf404712599c6fb29ed840ebf08573e90f3012a16c4c8ddc35f166e3817467bc79507bf20365a7a83bc78995f203ec817a9e9724ab2fc64b72f498a002f037f6e4dc4f9e1f697844663066ef12cf9cb320a09d3aba91eb217a5f57008f5bccce86dbde353dce4ba820aa40654e15167814faf3e08910ba000aa0605c7f37ac923f65e9eb01c25a252eeb5f17a808768c78bc382ad475f37ce5dfd25520044727a0ba23145696feee49098c02f9ec9360f09e6be5d193684ea11cea15865ebd7fec2d08c458a6e67fb9a8ea8632ce41c68a6475075dad7d259b7cf16fea8bb46d48d721787981ddf634240b9a923aee99bc31c197e8644171d8384fbe4325b9c95f1a753c765046e745ef373e788bd50bdac1c46295854ec9ccc23b65de47aca6c845fc5a5b54332c72b7b1ab24a82dbb069fddf2403c2d94c9dc480fc8fe252cf59d1888c7a8d7bd5576f87c576522324f829879b9f37632b4b1f27a55e6327c869533da34bb55810b68c9b545003e1edec8773a16f6cec5652a17bf03b9ec792d2e376f7ac72acd843dddb68c81cc3556b45246b9e68b72cdba9ecf88d6f2113b3d60307d0b0888bccb090143c9756c85d28eb6400823e8c9a57c40cd8a156b1ad66905e12297fba55b3c8bd3842331769b0278c1ae1e3afb176c2a015abd929898fb98c8f58586f350e9dc10a6e36678e26af7f8895e5174d45b9c31d956cd474a9e6af9443ba942e3f0a25c6cbc850c19148f349bea78c4ad06daae19e98ec157777d431bc4c7e8cb3225385c27dc1f7f8f96a07e964ed5334d7d50d4efdce364496f2a4e9988103c4d21465cf6ed3a052108f836b1bae692c144645714c8e18e5a3fa8b93eb92ea1a92cb9281a628a974e58b57fccbea4eae8162de792379baa1d18122ae5073a199857daa34ee9b35f29ecc7601460db4a02e81934a5ef84b9d48c2b85ff6daaf3a7bc9de76487477c31e8420e1e83d3e7e7cd4c73457c92b7b0a96465b6c400c76860a97a5765dcbb2de09d30136b910f187c449edfc848daab7420ccbcfeff1eb1a789eb02745a5eecc67bada3332cbf4b3dcdbc774af9ed774850a2a0bfc90b3fe79522af78157cecf296f5ac37115e73b5a944f491892ecdd3dba9db42bcef862dfd35243b82debd78087ea0008d2d9268116e76227a2030066b0c56540e6c9735389305626e9655818acc0f50aa8570bcbb1544dae2502665120f90e75a28079b9411e4d5b49987fff0b7ffcb3aa03422d47c56bd6cc5f975cd747e370fdac9c27a70735a83e4d15bcad4065a5173822d87d2c7387d18136c04212d1ac763aa1ace8c53d3a20b8d6e88976f2449e946ef17b0948b9831e4df7fb6022a60488a2d0782f46bc9456a580c801f0576481aced755a98362565e29eb7d6457cb0ac013a6297fed798af2c20f41f413a269ad7b0ab0ff8d74a309762548b3eb2a97d3d5cd2affc04afdaf6a2e0eaf38289be13eb662cc3548903a97d59c0264e9eb4cd3e0508b90ad555b7195b12957b70e63c1f0d6e7c775efdb503b8c19667d29440de3432d3bc629b80feaadc61bf5024748feaaf8a7906c0b61fd698fe136cabf89703e5fba31dd083e11e12fdd37d3d8a04052848dfb886fcd8009ba8463332c2c10507624d19c509a413d2703d1d947e7e46a320fd2ebe8edecfb56dd3dd6d1cbd7638df12807c933233c98b59df706b30ac8887846a8aa0d435626acdf1a0c2b5afdcded84484f7ff71d9b51785b78f30e45c469a2212d2014e241e3038f23cc1e187e7c248a7ceef0cfbc590280dbb5f9754c4b21f785251364cda7b8952513bca51056e611ca578cdfcded83b9dbbca6773998929c3631e0cd4fe5e14cf299ca790684d660ae12f3992a8b4597ca5e0c547753c0f729d23ea83e925228e5e8ac0e5e8be22ab404ed7c9b236145af403eb56e8dc3a5eaa7293d6b369f049872d76dd15e8753de8e662926c5a6c3d5cda92468fec1daacdd3e1488c266ecb89a9523e33c2f1f820021ffbd5e5bac787a075186c14c07ce9f2aa87b3e0dcada6267e810a38844421f63f31aa6f244898c16a1cf6cd5d609ad9477dabc803bfe9ce6de3f30f2407a849466f6081e4371848f34366b8153f698ac3c88ff1b4929d5f5478ae453c954afac9c00928138a50ea0eff68a2d425327174828c08796ee4c9845a9fe00106533879011cd4e56abb31db07979a172f3a23e368aeac0ce9620d022d9de951e8886dd2ee9688462526837ab7c84005ea548a973bc1e5244215e1dc9bd587eec9b4f170364cf2783e60da3d4a0c3071403abd71762ac3e7ec25c5e20a54d98c6d9f5d4424a507112c13e0be164ad1146bac1c7c28aaf092f4fc4688f92465dea6dfe8c978b97a69ce4d191fa4c47c8d516d4cdb0229dbc1d3d5e9a48df05c2e1cf773fec3adb576a82d54ab7fa0626b3d0bfa8916d6e549db7946bbda42d9ef7b134a6573cc3831fd89f3ef55927d417517fe305a540a55a83682a0843127747502fc48d7fb2ffeee1aa36c13460116a943f298c3ac5e0ddfb7d94a53f28195040e2d59010a3a588536c0cf414e892466c20bdf75886ccdb08c784a4e25ab8e93ffe16e74e1db1bd1d1eaf8512f81831dee75ccd1baf4fcd4947c9f20f77c9ab5a0ba5731dcbca9bd9fbbf5196d034557430690ab5a35d55ff5b646a6a5fd3cc365e893a73d221967325e967118d0444f2ca624ee28aabf98cf5e5988eb04931008f20e9cba26b38dd695042d766c0a152ce0eb69f22541d6991f38fdacf67acdff929c47e06e86ca8d2da5853521f3b8034be2f79d1c75062694533f6cc2a9400d9af96be5e91052575be8cc2a612712331043a73e1c7146be2d4555d96abe8c119d1abb05d72adf0a22a0d76dfbf4a7d4947135cc9774b78991e25df00a1b21b6d7f6f341596d11ed9ee81f74a20ac7c7f082c071f660921e8800f8786ff674408942eae250fd085ed30f42c18ade9a61ad67f0dc9547b8baf457032b8995339804e35490c9d836b275d6d24f38d9e145f9933d2fb87badf1fc6b5de7f1d390717b57339acf2778efdca5e95f664e32a6ef47e4d367cb2b08594467b03c8d3b317928d642ea532e4426bffd78ef42799c153a76177914defe79e7c54ffd3b55a2ca4b9b1b7f4ec25a775d214f605d4aab40e6223f5cf6c1b6a66dfe0488e8c72009f566206cedce792b7ae5f822bef2023e6e200ec7ce2cff9f7bc8eafd4ad841736ce6d45918c57485b144d2abfc87a1bb6a5d7ddad6cf6c389957a82f5e7c37a54e276471f6c76cf92eab63aa5dac32d9678a86866fd671dbdef723b5506c1f95f481649930fc39ce7d10a19be0ac5a53102c2ab17377fcd1941709f8d7713e82c454d4453a1dc7571ab7d7295ae20ee89209db0fdb7491c6660329f05c99a8bdc4444dc1ee533cb0b2beec345038ddd5d46cdc52b4910f6526b57faa0050c9a068d2f0faffb3f815ec847955839ff6253aac608129e23c63480e53be94ac5e4b3d51c308ece8dfd3e27ca54993e8391335c0aa7f13675226b0a0beeeffc67d8587e9a87675c69d378120da4458de4a013387a6cbb0076beb787cba54b3ce7d0226391f38b17f4835b738642d3f27f445466a05f87886720f603ffd98f144631e191c5051309816a2a2320bfdc973abda4d8f57cba0cd0009d9d3645d847cb5afca3430303b6a1403b5eccf082e4ba25584650538c1bcfc831e31751c9f85a88c878ec7e8cde8ff51fdda975d5b64ea6f6bab6055b06d0eb6848512b38a3ea4f07be322f78e9ad7c1baf9e54726659ada3c96895dd8ba0f941c543c85bd90523e28f3857e464f1bb14450b57e30403672cbf3f5888ef3bfc1a7194eeebd699042696be7f3e77f518e9e30c86ed72ecb1dda74c85dcfee525497fba329a89f3db3c4e9b055f7618d27bcb69f763544b6ead21bcbe87e45fd94e679f2b12fd4bbf76cfab4ec1e56452c33757ac1b5d06416fecd1cef7a2274893ce13de5a15b329de25e8cf634ce9f9e831dcc1674cc7384440f23c72aefb34575ecc94bfeecdabd198096aa329d55f99e368d82d832b1baf06e429aefd5e00c45b2ee7602f436f6203dfdf96649d64f30be0a98ccef83ca1e85240ab897a6359502f3eb5ee9df19c633e55a1be3ebee05808a9dab386fa4bb0d4a6d4a914316bdbbbe529af994e344084ae97e997de5f8f4fd466e0c7e6c7a3564a6b4bc326ca5eef0c9494769c9a6aff7255c4b60ea00677b5d1f24b564fa2eeaa9427534e52db3a8127c4099482030151e7cde5d94f0874aeaef65dfb43f44fd979dad1649aeb3537643237049d97f3c58e1bbae5e32e3e51eb94b558f8cdd7bd375a6ac308beb66346cdcd70c7115ce92e0cd8eadb9914100d305513c31f7bc5390294a4e9da7069d5bc6cce10d7ae6fef8340cb36b5b315b41344868f45723af0ba9e7812ba9a23d6fe105cdca43902ad065b85479b8f83b497b71bb8a434feb5a7f0ec3188c203d11028271464b3c70ef029bfbe720db06090963aa2baeaec3849dd3dfd85397fbecfc0967134f36b579b21f97a917c78f5fed78b7f0e304d68bb6a8aed4ebef3383141d09814c23b526a1308f3fe1f06f9b2b9acabdb4f1354e774df88864ce4c93f53e0f71e321e9dc0b2d5e4952705f3078a6966ce47fa4268f7ee87aaf47976fb0b1013a4f3a631a66125432570b27cfacbd51350b6740ef254053281c323b2249ccfd8ab3c47b35f00a56be6e76446859eae67eafb8adddbdc31d54350590b58bb75d073217ddde2be8dd73ff3155aaede6b4c467e1e08ad63ef86f70ac27b5616cca3f06d5b28cedd6ac15d9dae8b43b75edfdecc3d588890c4598cc07e908891e8ceb14d74be5c5fe1967c7e9d39387bea827d84e1396714bfb64c28ab08dc0d55456b9d7c4720d0dbf10b18f77790a408a0bf24e8bd2deee0d1ddc5ab11451b9c10e0345b3538bb83678a1f177b9a380dde348d1e299f91723f442b7ea3834573dadaa01c9b7dd1a73433b8e62cabb190e318aa2cfbe91772bd8adf32a37cc40d12ff733bc55f13da2a296cd6a3b4cdc4aa19e3bb78f346d2905b3fe654e5b47f764453db2bb36c0939a6dc6089aa9e790c0c6e9bbd9ace056db3b960a9467e1335b2188d53b6c81614e5e35c418a4565bf217a952705ea215667342b42af2bd8376d36458338455e2620594208c3af7496f3148250e6aa17c2f3f6e900479e921a2b34afb24d277ffa1aeba8bc7844ec7cc361550b49c14cb44da73c1ae12f27b6ad741dabd33bb8dcded25f97f40042a5b0c8b593e3be9e5bb99d7c2fbf2f8525cbd75f76199194d37916d00577f1af4db3b686e837e87d0e87a1ee84693e0a6386ebd8b7cb185f7e6fae9e5a0ed25819885e37947443b744ada415ce858456a0041d9960814131f1803178005d0cf5c26f72c351db250d2067a650830ffc6688aabb2c4d2de7323c1f4377033656ea7d6dc1a170ae3735d146b9041cf7c3bd69ed4014b72b91333671073d0a8c6bea1b7d992bf6d813023461035fb5ff0c67844d686f08738060447f34aa0201842d0ecf66e4d5a71a1105e7d16b299a62fc3cf6565676b0dc6397c67d0717545b465fad241912ac951cbef5794e47be529a32a51462d8505625c990449f6e38ce65ab5c191fef60d7f9cefda01e2017f9611e3c39312125a90ecffdd5130dc2e259bbdf36e92d95fdb7b5e7fe7821f3ea255fb2355ab4d59e6d2326f3cf424d6b50cf4754e6d1aefa97132b5e8139886d78b0507aea3768ec5e54c427801839c203ae2c39791b180c5273d3f0a8bf9959a516768aa228aa7d274ddf4ee4e8ee77b495e86c94be0adde7816b47dd0d268ccdd2d4655fe65fa4cf268dc3e4f97981dc9070676330e55ec6b5862e01a9821e4d314915d94849b128916adf85bc5f1502e24f9eb73d2d1e0f8b910eecc7ff756baafb389805c26f5c56291d0e7d9b5caeda22f2b28050bf080936408a3fe53bb24ee8be121d0d463224561b1910b6477b57b8ad4107208d3be1ea7e2460dbd3d1a109173ab4120beb5c81fcb43b885148591cff622a16b3c68ad916cb0e6462a545589dd9d3bba6ac0395972fa65c1197ef23d079ab4e944ba87520bcc630f9bd713ae3f20b0e64d3470ed6727823c1035dc371b00a33a939d4c2384e13663dd769ff5dffbc293e89ab04b15062899e290361f8541e1c56aec9db427f8d5017710ed3d1e56d182e4b9ace3852acaa89b0fd00c386c9258f4625fe62e0b5d0eb6a25ae42e0b49b7ae912efdaf0904852d87bc38fc73f4cb50fe49ee5650b2630d19f098f85173e65434581378a7881b71f7331ea83e52be093dfdb0924ac4ffb7d1f893e38ada9981c46c90f788e539a9e1801236fa9463fdaef7b8834165dc371b1ca2bbcee4f15144989905bf7151d4ed56ecf06661333e35914a231d49ca52de90ebd2ce2ca24ce768d66b4af3ffaf63e02b09bde127b89eee21a5de491377b5192d7a6eb7771cc441e1fb574ebdb178df9276684f47cebd5552f4cbf076b5397ceb76fd24579c392e89e6830a5c796198878d0eec598d2008f659b15d28812bde3b3ed2b1fee5f1fffbafd43abb9c3d972e82816f1881fefc8ec041da2da4a4445f8127821c230971e3d5541dd6dad38288e6487d878d715285594b4d321e290e882691490c39a2ecae7d83ece0dd3d0c4033978c4f8d9bd7f0ce62e2175207ed8519434a9ccbc4ab30348549368dbfa532b1ced142ff1f1cca27ecfdfa994bfdb93cdc74680a7dd0addd5a252f36dad6acf706bb40599ba0a69425d5100072743e0387ebfabb0af1fbb6e74c8106e1a4bad5c39a33909ed638294477a96b664ddb5a908a67e0e922ddcdcb594caba66943b5f4f4a071f6a9d5bb93d42427759cfe7033e477a937785d492a864279cf4945edb17d13bec538989d39a083df92a3dea5893b278c7b2156bbe726478b69fd7a655aef3f666e40c952411a1bda66723f59051bd236f7298888cc69e5416a7cfcfacc41b4d52cb8f1fc46b0082657b545c70c3e9c80d7b5e99cc33839ce97f030b7176d6392fbb00c4e58f6e56aaa389c061f3847d3162c711cb237ee453f31ef061c29af4f54b5a1eacc5af310f245a358096dee59be4a252468e9a47d29164842648e872ad180ec8105dff1e4dfaee7a9c8ea08bfa8d05475ce819ad14de714366ac98ce428dea5d28ee20444325a4cba039a6618c23843de196a036c6ab4e67b83a04107ddd52ab3f38c21112872545edcae102d0d36dc60b924052cfe82c3d09faf5396dc49d89b176e913feb46528909bfb44fb0dd8d8a0fb6aa5db047bcf9dcf66e0d95eac179748bd12bb426d433f7cb8b96d06ae5e8202e7af05d5c93fe1253f0bb6625ba5c8a3b5c9679381e42be3af0d7663ee70ec59dcbed2ab64356e903b5eea106e1fdfbbd8d878855fa07da1e844a3dedb7ee4d9621b8e0e12148f15ef0210f141849ca4bf7953b2efc32b3e1534dc7061cbc00defb263ec39540f3c44383f2dacd8b99cdcaab96e671a425c600868d1fef24d67e674522b214bfa2f7ac0d610e3da67446e6a5ba38ec0f1cd0e1059fe958129f75815da6331ccad691bd1770de13a5a566ffef671c5b32d601246b8dcf72539129a01a394db637a4e1ef410c90917284fb0146ca6f9a0a5a8094d6025279957776cfce42450afe199c2bec6a77111da22f9ce6b1935158f1bcfd77eeab218922d06f9726c87c71733e9ade71e2a7a73f1ec3b28d313b3404c6eadbc0af237a979fbd71c268ccde7dd6874ce97dd100712be46619963c24adaeba91b2b65125ec17882b8cbf27e7e4a5f351aaeb89ef68d4890fbeffe629ef69ba177da0fcfe3a1d98248ef7a08cc4500f8dd6d2421c4bdce4a29ac0a97bae2336cc630d811c7474df4be128e60e46671d9b8ab8458742538144afde0ce313b216a36f64a3ed66ef218e26c05c1d3285484aea367c5bcd35f0344534be603e8cf5ea0f49d4fa5b0231e921c2724cd0e6eb4a69d70508985e480ddce9f87ccd0909184433bbd7484f64ba2d9b6ae7ccd2b39cf40415b9f7f0165694bba15221db7038c689622b6d549c7d98e9d8ffa65665cc9c43ee2ba68d0260b9e01705ba2d8472217446e753f270332cdb44fe9e3d297954d7d59864f8723de58a8b2fa3f343ec4c611958768a3d6d3170ef8827f299d00b91366e9a3f1764319fedb8fab2bb80106a2dc00a7e044f34099e057aa74ccab153ad53ba1e11be4e96f59b8a6be72ff25d86b097a0182cfc16a94e1f81fee869818b2911dd79b259a17c664d0bcda7ae20c9ca129494dd0f90e850c5ac4912bd0a265b358d09f4e782b9e8e8ff4b1f617f339881ccb0e372f9baebe30d71c85420415435ce52c16254f48a5d68309e388345cde249d6aa3727e5c5df4c556c2b782ff19e171147e29b943e70a62322e9ecdef95b7f43129bba7a07d17d87727e4498ab541885cd31dd04071b83c24d23741c4eb1028f93740ac7d53c799cbcafbaf1b373fa060e0943142578f646b797616a9b7c265c0216423ca36a3964af8820f8efb05b154fe573c87f06d274fad0c2a4c93b5d30871c4a013767ce117799af31c101c3f4f58ae21d25fe73b313fdf784c38a290821f4307b84de2d57c527cbf3201a18a51f5fc911c5198cd1285ba19e071c231f559cbef0d165f3b5e91cf3f50a5cb457f3283773be8bed1a6b56e26bd5bfcba02d0c950cc1dff7f995ee97e0a79a301b6a802fb7a8afddec93160f2d3bf6adfb258fe8f9b4327999b8b7cba1eea29b2ca4f48301b1e7f908d4e7ab8a2364acd275f3349b68499b72475e1d6d18b247f5b2aa7603cd890517dd66e890cd96171d65bc28bd60785e5bae46ff20181ac2feabf786722db3fb5154ecb22e1d6864839c72ba3d63ef099ea4041c954adc7541793c803a7c3b49b9777f8630b28ace8f1a1d05c0298ba8a3c34b60b25cc84acd581fce0817285a83affd4f7e93c70fecc8c1edcba2a3defc9315aed4eba21c0d70d718b88e17d769566806666c8cc5e7d9f347f9ea71a4f48e6e475a901ba4a3694c3215af77e0d7b83937166f931a2e795ec108f56554c571e06bb190b6aed8a27bd1d734b3c2c4a786f29613e2d0c7b9ac08aa4d19097c8912ce13c0e1a21a68db42e78089e8d46ded8709d0f6ffd4b70e13d8dc8fbc50a9714df6fa4f21113b5ce569dce76996f6f60585eb6828ae9b5b55f74c865b71e699d240f0024c6219f151907271d0f86817fe4e993cf39623464c48cc32f87de4b9b559f56a0790dc86fcf68dec2d9d24908c67adb077a0efad06ec2840696bfc4397b9a3c331144e8eb0a7a8e22b2d9e6b5bf562cc075d17330d9113b3124f78d076cb6942fc90535a55a1cb8e8122279d7b0ea9f686963a822b85d290141fda2ff00c96285e5ff01b519584d0b4b65672023c41f9803ac0d2f8b497fe4185ee92cf048892091f1ae3e6987d74ee86c889aa5bd521d90920946379b756258955dfce9203fef242c40c3e5c0a2b7c5536a904b6394ca78ab74d99e14d9eac582aaed9c2066b5130ecf007d69ac7364900972b2710f7b79cfd22b6b76ecb70a4fa1469ee72c5c951e24251e6a173fd4f15ae38ccf3d5954c6c6d68d04551f7d20e2759224f436121dacdb012fbe4a706033b4e43c0039669808f8377c78bc23643244bc70bba15d0cdaa8fb60dc0c66a1a8405df71f571f40a53195a116740ef26c6edd9dcbe16997f274478bac2e5662f4d7f67a7e97f3069389819a4c9cf43ee6722b22a5811d654acd46145a5e975e56839cbf5b2c51373a1ebf05798468422036099105ad6784fe5f338634d868714022a5bbdc9e4e2e862d88f0dfecf8941c7b19e771393a287b4af869ba04a1cd40dffe0475dd0d002f952d203db6e74d76e07e0b66b46c6337508eec7beb2ae67f0c7c7e42b08395940d82639147c6240ad3e1582314eb3107824aa8f91fcd64b67472de5529d7aaa23d1a4fa82ecbd494b3df5c14fd1f3c5ac88c71bab27743a8de3898f96a77f42e81719742e1baee48601ff73f17f51582f94e6184e2445fffba2f2f80bf3975649689419e139c0c92c70fb103d793cc58df12784168ca1c15dfbe032a84dacf1ec3ac599e55dd66768ea21c189ee220fc011c675c7a353890201027b0d9170a19720de083c7631bcb8f9dc8df02d118e2802fd3b34e65a9267a25d417745561aacc351f27665b83cd05b4fe54006b999a0369e5cb7d13002beed685ba37c416bb87f6f8bb418d99c506c1ad28cd6b89bce74441d69fe8b97c89ff126d49fe0faa8cd658fbeb550648b35e2bbccbc7715e0175dfcf4b3a19e68c9a33e0d732b5c0573c2c1706564b8f2078b7576aa20b5aba6419fcb6720580b05a62fd29e421302bb35e9ea4d9dacc281f20a327257e83c1e8fcdc8f6553df9cef7de8065b7c9c668f59420d32273f2fa9b24811f23664828ba3ea6388c2d0756b728ee977c680d53400495774a44b7af7a84532830fafdb08b206008a22eb69e683e5c8ad60ec2fb7b3efb29eaa0da2740057847f014c92ccb125dd916479b5e3f424809d32729f43bdc601e990744e81c1d80fbc9c1abf4fbb39b0d8769611a105fd09b3d0d83b25684eb1bd1f395c0456fff53e8ac67d030fcbe3227da37011bc939e401d914092afc4061e05674d379555fd06dcea59f80c8a791537e509533cb4e901dbc9f5950a19f3c191c6dad331733013a3160a8262d4b30ee81a567e38146d270e56d97043c5ebde332788673815476ae78cf271a8085f9e81bb52639185b5e0ab69945ec9d6b87c8a3a43bdeb8b83f9d3e0f5eb182ed6ec8b77a09e1d1239bb0152c0facaa05500e544f71bf66e7ad5b1ce3e3ce3232199e3dadebbb5a25531049753433075f953b6fa59628398ba095de7eec2d9b933e682550be4a160779bd7531b495302ee587f90a5658b13bcbfb9ecbfbb64e3b2dce423fc073e8822e067936d9c9a5b84998c5fb6f63865da5269755e009fcca18313b9285052c2585ab4b8272bd663f07814b58861a9741e9343a4734d604adb3baf5f2963fda878bd6f2e622bce0dcae54d9da88a5c57f2048977b05262ef266dcbc2fc39607b95d0a15ea31c19d1bcf2e2b21c13e42609a2995082517ed5f401d52493a67979cc5d6276ab8a2a12506137f627af1d22903dc19ff7b99747b40bddc6e247633072f791e8142f216fd37e78eb45ad9ec1992145500a351a22bfec0bef33208c28f5ba900b90f9e43b464808abf670894ac60f81ef74fca4536f68a9129e6acf6720f669d9b80ab31d9f300e1c9159f29dfb0a7550a70be7f19f4ca8ad328cc47c687aecb5b02fbba055802ec45a2cf32ba2ed363376325a20237c9515f2a3f66527ee0d387e507bf6eb23373f735730514ed790a55752a1280c2b70fb5c5d80449029ecea8f8620a71f329c107a072a50c7dd6064d792afaad86507ef01ca172bcf9b271bb3a3ff74c085ac897c142984a4d3ad1b927c8bcc99a2084514f436ce0c56f2933a8af6b43b909e7e86e24c11986c3b121531ec8abb832159bab24dd763fe2ce80736f93273164611d064087124232e0308efb9645afb329bcf0f38cbe38030faec1647a149f81693adc864839a9b41646b9d6c04af4402e9b7cf21bb64bcc2b8581acde1f9bb28c5d752da378973477f98ba2474e437848b44980d3b3361641666829217132ceaafe2025878b5482e26bd0b7046803a75410ff436dcea47359a9a0d82586476637ede7e395868506cad43859edbab07d0a24244a7e4c0592c8bf4ac4ed23a6ae1d9fe4fec798805d6ddacc8d3ebbc234cbf5469dc4d3edc1247c18bcd8ac6d51928a43ab34d30bd1de2a634175719427f4be3d4af1353e58b87137aae3a3f8308438cb50a20e100fcc9ca1d038ffa360e533169b1fa45c80df428b461ecbb4faac6becd17e61aa2b3142d27b660b212559a3bd581ba8c65e1d1ab92b8da70c3ef9a4a64dad069902cf557db09c0d9b52b2e238b79b9c9e789d0bd7c524f8d978c04b0398c1d564e634f85913f9236d34457c9afaa619fbd02fd836e285ddbe8410ece1782fbb7bfa48b69092b00b2dfea062617a0fe59239e971187515dfda68b68429687f63a3334f5b52bf8f86abdd4841ea6f9b5134025a85af272d9df1b9d6fd39204af58f69271ce433bd50d774f80f151266d5feb6b29851ba3b36f021fb05c5983b64e362520920b76c88ea9d3b5469e88cc076f1d2c722cc421e56470ad1a717bb4b4276ac2173d0f900ac5c3ece97ad712f2605f8e8f610a4b3c011ab47a50d19cafd6b549c1aef9322def6c7653842c50949634f813ecf5cb70db344aa71f768885ecc039dd10adfdeb551fffedf396f5d6630fee5da776975c52fc815c8d091ea7e00c066b8b43681cec14745e25a41de11d83957e4d09d1e5af5862dd625159d6a24f8195925a3a76f51f4ab0b9767d72d0313bd81ede661cf7e2aea229db7a5bb0e77820d78cc15c41a50599941d6022f7b39da88c6b5905553b8ceab2f15af20b8b9629d6308dae414538b01c10e1a4b77879ad525fed8d2f6744963b67b0c6562f61b96384d0ccf88ef92c56bf581a58c21467d3d2347138f78e17935640b0f1f75025a8bd6d0140400ce54eb223c6789c455f0d8995db717f140b8c51a4800e987be009a7152e0855e58d6262ea8f29db099db73aa3dfef7f05bd4bb119aea3b688a095432aa96b4ad63ef8ab07a72b57788b4b19bcdda1fbfa3757faa887d1ec5fca7f885471d25d48369d4bbaf2c50542decda76afff17ff81c9e4e5ba2f7bb645416e7bc611a015eae5773380c55a47cb142aad179c0f8cb60153067376a4b102420f199e828af60d637b160754a89dbf15c2522d19bb9e4df43b9371fe0dee9fa64b3a96233399d90b17aab5a2bfcbdd557b2a6cb8300f1e0baee81097cdf9f0718d9aa1b03ac13ab62191dc14d14f038417f0e61bff6157b6fda6db7d5bf0f5937e0b5d7167e78cd66e7d1d66f5f4ac97425bc9b87e4c12b2b71da765db6028db42acf1801dafab57c004402e4b201945f4a38aca020f5b995cfcc73edbbd7294e03f0bf6735ccdaafd12a749c92094f2638272ab734e7de64efd464dc3d6ddd10d9ea843d17b1fb5222b2f7d14e6bc4fc1056cb0ec56182e3bf958f888124886de50a1df524a6d5e2f157b1b026a46b94f42421274f9a28dc476039905b92a3fc8ff995a9f28c98e7f8d7e25bcc1ccbb0c695b55a324ce5edd676912dd19d95f746d5d7886d758c498dbe8cfaad7e521501165d747a8e41a1756b804eee51061fd6a2079114453c71baa298a71c5cadec6f01b1d1ae622eddb10ae12bbb93a5f6059efa2682f6f1e9d05e79aa5725ac11fe48a3183d403cb57bb8f9af2b012e91606f1821fb0dc30dc3833ecf4224b9b73b08ffe2d75589f09f1eae29b12a8ad04f950a32fa12507ddd898547bc8a5e841f883036778766e5a014ea58e723a90b109c4dae70a8285cee8a8b194f4564cc2d0e53d883582b1de25c666191ffe4665c98294ab29dc68977a6ad93e80cd0a0e7e8a29ac7f532ec0c3a882806496300ce40808602d39fca2be3dfbf2502bfefa8755f35307398ffcdb39afaf90d211160d66e93694d7a030e14a2b078426610b691ad00d20b023c2e383166d152018b1db343995b419d4f9f4711ef026c4e50dc7d55fd152e3884e515a03e1af7b9b88849852fe435b12556900a8f0e4a1c4ad30736292f7932db7cb0ccee99790d3ca3a5ae51f6abeab6f1aff2900da3e85a44ce81d6ddb580752b5846f47e3f396965b03741ece3f647e72cef30900474c1262935ad09ea885fcccd21c8ea2ca11706fa8d6b394e825fe831d596e78196de5e1b95636da0e984b35dde177339997e3724b07dabdb1b69ad25e29f3d6cde4ef8b8ace36c46bd942b121a753e735bd2d84a5bd6992dcd9e7a3d1d614be67481d73b07f72b4fd69c4b337e7640867dff1b7c466461f3362ab20e1d305bfd56ddaa57bd3f7b88257ace342d336c7b2dd2e2fefbf423c52a1566fa9b54fb366e86fa5225a8ccb89eaaeecfe3bb5b46e42f4c904fc865c6b64c6d09fac55b03be26c458ac3d4b4b443d7b14ef5845c6b3d57141df39e17500ae69e01d75cc17c06455d81ba9ace3fa1484c4b2586bcfd729408a513eb8ef6eacc297496dabf7722f88bb72465183f24d34e021c86534545eb195dcbe15269022daa4eb47f196843e9b49705df1998a35a3b30259f4808f84f647671684e6aebf0f1bff6b0724e70550246d610be9c48554174b0f19b90f15afdcb1e8c4c2222fcdf9e2eb020ddd2cfd29f4031d52bc7ac8e5693802def356c719ed7da3917c5f40dd107295eea03674d9f2c61ed75e377e8f2e552e817d389d1e445e85d6f10a3e979989f36e0a9c3ac38a44da70bd3672b5644b84c7948aec80e652daff88142fb01d1e6044c6383e554c9015385858a513e7b810922a39b3cf79acdb5d007c1a58c7c2fdc02107e38f09307ac19836c5defb185e7f73907770aacf5b5d10e3cec47a61f4279cecbb0cd5f15e5e388eef4234587027e9ed987e98b2f5e0002f5d549495d187d8b6f8226c69d1cd494e4ba2818392ac7d8a3c53d239aea570084403280ca810d38a3ffa976b85eac5d400898778cdaf00adf673f55950ffce580403add59fc9919aa6a2c4db3873971151f79a077b01135b31c1ac89ed34ddc528e355e293996211a38f98e19f21a8d97e8471eaf263895568ebbe4fc1f6b8cce51792c2d946417281f6584db62dd336ea68d7ca9643a843744ed99f84b06f16087fddc2cfd7a6ca1d13303cc69cbd03007b396614daf4df497a012aa2b13ddffd35546189ecf3a9b466725bb32935d6e3011af8860287ab0827c93a53d3d0c2a3c1fd0c62c0617f804681952cff235d2fccfbc43f4a94de2ecf64174cf9d168fe68b6f6ec0da18a95495db34f1e4868e65c6fb680c029c1ef7dc8cdea6b23f8433d85731f4413814b557a86aa9bc65dc6a89405f5430d8c1451779db8f6ca58d5bb564678bf1e8e2d83f7fe5d47bea3408a2e28c255b691b91e2e6e957e40fc79b745d124d02180f90b328ca6b88d0faac033a724fd83f2dacd634948b5d3e59acc0bb99ea7b162b6b1d096fc520ea0af427e30f63f8132be8732da882bfa738a65661cbe2df25ba9d326340cce9d940aea5a04cda1aaf7f7c41dc0e7f93873e0637d3e22bf431e11c08ef08a4724791636ceeecba879aab117026adfd839c201bed5cd17e4ec279965d5f917d1d07e2b176dec60cd0f7f2f28d3c045d578eb1ecb5aadb3b2a80d882b8eeb86e28abf6327b5134d687fc3562110b80442a60d5ead2715e14b43f5ee07779898a7703dd9a7af82adf3ecfb32d193b4b66845fd534c34193064ec9745618071177fc67d6ec644b8b09018c4438aed433ba69b27f735fcd9ef1517c5617aad4b70d24f8b129a2cd8e5d8bcbc5e82202b115e0ff958795b02a23e95732688ede930122e78903ebd93ba2dfa1ef0e80b01a59f97e48fcc6ce5157016fa4b4d9b57396dbd2c84c157dc136a817f3df3139d98dcf3aa83ad2a50870645a9759264387739b470b02e4269789f8586dbffae9d8df9da304c00279d0d767be8c1d83b5dab0f0a3285dc95f63e3699060941a219ea33988a553d6a1f90172a6d94db1e5c27d3046857afc4ccc4daa3bd00177b7e9940b334aaf6b5311686778fa967607841bf5951c4a4fa1c9da57239b539583c312e123c28e89554c7fe625688eac35f64ba37a73ed0722a572e8195bd30a5b73fab6e3be784a34057798cdaa598bad7f40c0555cb15f8197e88103e79509c2ba975a78fb72d24e6824247a2b594778c147a0622dee5e6eef685353cadc2bf1d3645fccab577399e61901b31e0206c841321d937c512ece522ca4ab53517d2425cbf515601cb1b403a40f667a87c6f2ff2fbf694be118cad793e67629df4c4632c474e5082bae4977ce3b1a39b48856dc2cc166bff8ea861b61b74fb807e2dc603193f348a4cbaf3840c6fce151f02148b2cc82022a3b5e1b0587e33d1f3acccaab08b40c9e321a559249d24ee90364b54f72a730ad7c34fea37a9880226e16ce49da128981ac5008fc024671f7b1a9826f264cc7ed0b64964970ad8a4daeae5692aaaf32a3fb7960d08def6c81ab8716da6e8060b57c8e6dc261ec6e1901b9f2b26da05f0c335566e3a1c31ebf84970321e2a713eabad646151ebc07df52024fd34bb549c94c48a2883e8b3efe1658435f83450ac5556d9aa4f14375bad1d73fc74148c301837aa836d489fd29923e738b578678a2fdfef59842d8bb364f13e3a1dfbbbac804ad501eed1ff767f32e4c39b716b33d11bd528d1c9b85bbdebc311d93c676e8ad6fa950abd38d4dcb9b734825f0bda3c499ab49eab92f79fbdc43cf7d425d8bc8bfc934e7d1d1cb8724024466b15b774a6ab50ba49029f92b65c5de27a2ee8b3020c9a1e018e63e13110e80bc69de03d8183993b69daa75e797cf393a36c5cfb1806f77a28d66966d5e7629d32d1e0168c9985e2cce0e28142c45311551dcfd888bb28901022d33b57ee33e5ea32eeb9e2775b3f1c36c858cee1bca0af790d002e386815aeae63b547bd281f4b3c6e4c841e2669d4dbde786cfda410b9c3ca793d7f3cc04eddf2b1a976c935df3cbc8346723f59d2a0301c4ac664ef5a249ba7924ba7c175317cb5e55cd9e90bf921cd280c83625d670619545eb6c1af2e189eba2127e9c2ed869892b17ba703790b422d92c76be15df43849d4573f804360db08798969bf87fa190f218668665c64a2efdd5f84720933c04ed19059e45d4191e43e22e3277b9477ad2e82b3b56b346b1958b14119d6f609d058622fbbe54259a8335bad9808453b0a99709e886a5c78222eacd1bf74f4e088a5f215d230b0056477d17610821a9c796b9461cebbedc4f9abe7091e7231abbd587feafb7267398e5fcf393f284cf96d103f789883c1863b71cf23728f92bb93fb4b230ae94aca8ba8cfbf707b2b4a604381f213cd46fc6037449fe0b9031daf86c5654bba369ca5b647e27bcea4421f358b4fac30d2df158f9d41cf8e5cca64d1113257f72e8b3aeaf86978debf19a0703bbb312d0d9fcdd77cb2f02b2a98498c1139ba3dacbd9280e220ff1865b03b92562ce858ad2329f482035bffffe06f3e9a1574cb3529cca6d8a3413a6ca45d9ecd4b7355812bdd44dea732e3c60596b2b528b7792230f2abc9fe2a8f9a52422a1d314f5fe310d9c22ff4e54449b11153feda03488e76578d9309f88ccd93b67cc862faadb09c9f69a591697ac61e6b81d2798960ddb73fd67f19318704c3c58a5afa0d8aa33cea83570ee60ea0acf633bf375a5155b8693485e0db09eb65e185b3a0e06267d73c7d79180ae498ad7016dd240dfd5c91a3519385fc20d45b724985a4a0838654d56c3e83ee65506296cd410f7105ada02a9b8353c13bb64bcab222da408d88d35dd615dc0f2f11ee4db66c68f03d2d57c76373012a04f4483597cc80302ee247ba418bfba9c44ca46450fc2abf2d3a2596506bb3b74091adcfbde7887c162bc93eb8cba555333f69f1467c993673f2c06b93e9a1db3f817d315abfdcf762da4a2081eb9b6e70ac90f55ff4298b0611c5eea2023c9efcce608dd7c22382b0e5d948ecc29d65f2419bf0339185eb54007770ae4977a3c7451d44d358329b15bcfbd44bbc2f5701ff9b1ea86e10f008376c273a3b7d99dc84a031c887de9f1d2796f1a80c89175ce45bc7a5f94c24e4232574a62d80029dca0b4030cab174cd26c5d1410693c631913aa3103c4ee28d8461291d046e6ae75c2547270aa2ed28905162c81fb4e130164f7f3bc682004ab6f7f17f634b6ed6be31da30f14efe8dd5f62370e3468698fc23d4bf169efa7824f81eff9f8daee93c9d1a82cbf479ae32dbc662d6b350518a50fcc11560c1ebd33eeb1518c372f69c75f73922cf45b46efb657960f32f9deff67a73d6c1bfb3641ad7df151cd2908fdc54ffe483c3befa5f2c8d21e2b19e314cc06c742ee8eadb1cc98b70d31a688eed5200c0de658d728657b3b3b448f2fb021d29015d495e9cc4ae3b802ef03972b93a443989caab96b3beac0ef684efe6e569412b4c08688e1961127e9b3892f806f92b326eb6cae7caccab0a71e12146961fc904a63c3d780d06146ead5c61db562d9812f333d56486d5a8b3b6e5bd5dc7d769f8a02a3ff9eac1cb6bd7ed9cd3cc6a1d742698e88860817c7b087e8748b492786c120a8abfa81a2c336630c85564ae21019f2bc58829942f493f37e3d23d8b11cfdbaff75718593aebbc7cc3cc4c019eaca055444f6b0a9d47b3f19cd4725ada61804932420a50b58babfca97cc70bd995db7063079ecc736344574ac5182e4d8f6663fee501c5355808c5ec68f2d6ea83ea3a368815749daf710d9dbf4114f324c77b6cd5b35b78a4df1527a81074cf3914b1ca5b688b873df4f0f1bd396e7da80f5a098697f4272135d190ef756ab2c308450bcf6247c25458dbd9ac146db48dfec3c03eddac8eeebec5c1c6ea189b2fcef99019503632d5e27d079f8298490aa0cddd0e0fbf5486fc8a49ac48ef6b82a4f4f8d87273a49cbb6b49489a94c2640fd01ceece3a8614fddde35fdc97148cce4279ff2551def196fd2b0c5b10f47ad355516fc446b166339ac5a394bd4ecb3f631b25a768c24c3ffa7130f865d120ceb33ffdc574d4888207d2b9b6e574baed54d746ba6df0ce7afee0ceef836a21d068c5a3b074eeafecadee3bbfb56008d0fab6947e037e90174a66094f611082d59e819e826c4cb9262d35b9ce1f6e1852a4d6f34e8fe6934af45678c727ac21e88c5d799a19aae74726a187ef98ccbf5173dd93ff583535cbbd34cf0ceed14c8546f6f9f67117db7592233536eef5d8df141b0b4887acc2b86854aa13cdb10c2f87da0a7640a53394b14a089cd366a980095c8266aaf02903035d42ef8ff2b79d8eb52a6301abf69a54127dc3968cb38811f998e6ef27db92c568f4502b586a0f781d2c6ae353078fe9aafb62c0100172cc6dea5dcb852a5daf58025617512998dadead58d6697679212d0301d43fa6593eb66434d1cd074bfdf059d5c5bc4ecc97c00cd880884896608bdd3c5c3292a0da44d281e757b9fa8cbf779fd6a5532f2b2b79b1388f67552209b6f962a4b01db423e74283f4e96815aa85c3ed7064c1071020906031c28d121ececf0487bf8fa99622cce502b90eb2d6e8e6ac83b2be3cf928f3eacbb7a604e9673ed4c57263814dc9d31b5d4c5bf65bedff7f92a39517de81984ce25cece5b7b5f6cfd50d58a62506f106a8906bb1afdf3f14b5ea7778825148c6533cb842ea78e9f7d6aa4e963b341224f99d3dcf68d1b72b839bec9f2980fa1e0365c624da71a5eae3a3285af131dbc87d7dbdccb8d2229710843703773523e47b0412d31a8205d4fc403230cc8b0b962991c9887bad0d2eaabc35c20dfdc936e2b3e64cf8a930b1998324bba455678970f4a580d96ec2c478a4bc9d5afbdebb570b0d340b66af31079cd6c55139cedde3d00426bdb8a29bd03db0c919af70fd0315be58a11a9d04a2401a556ee0e77f4fa0a34e157d319057cf6f80047ac85b342c026077fbb30a59837dd152f3f92381c1a0a2c2ae8baf3cd346de6c7f6f6758d253c4ad8b5a5d8131296f80d4ab3c4ad45fcc409349524ec0233c9129053c252616eb39a8dd82f5b36f3fd3e505f2ffc2364308527e73387c29ff0a010ceeef5af6084da642592e770b7f21a1a662f241d890d81837b6eeef7bd860b81a9808f4062da8834a97df664ff25673efe2b951593987aed4bda1b57fd18153755ae348b1ea1fef65b33af260fbe24ccb53e97bbef1a13f651fc42465a2df0b1c19f1b1322a0ed4af23feede27b10fa2fe515e4c1a4274e7d3807c36b2e71f7c1f854f6ce494e24f949c61412203aca1d1214f15def8ed9c3fab2259fe8ef1577f7507663ab0736001c5e69895059627fc23be456b7a3a16d1b0332a2ce0786d04d2b39e77f5d7f484d93edd04b45058041e3bdf4a4cbd59b5e753cb420e36276985f1dea266b0764e3c8f63c9a2018611217ac73dd1a2c187ff23e3898081b1633d703187ad831a7dc8b6716601f0579a7327e5fdaaaa8a9a3cf4c92f4a454bda2b1edf45343e1920591cd72f365333e7fb2bc604dbe737b228968dc48e11f90dcb574274e4d188ccfca02d650a843f6dbb76d16f89cca323465892a3d2feb99b67e5159b835d30b3adcef7fabe7f79b7b1c561a101b4c42cc08d8a065ce749e8e66b9d0c29dacf31f645172591f26af343c71f31d88143847b02d907af2e88935a76","1703030012cdcf999525231d692d54e6aba3876cd2e043","1703030011836765bf32b649f0d4a7cca1baf244e210","1703034011a43ba4a900946aa8404d82820190956503635a889a6ddf8d13c2a7856de678b6748b1c2af278570ce482afb62fb261af42d2ff553d45b5ee740d15bd72e464f24cf3b14c3df49f43d73e9c3d2595c186dc6658a0ace87b7cce24e9945c4c14be1c7ee1b331214300c80797b59235d6a930e71523985229c19f9214e4c0f512de88387314d90438aa94c25f8cc81a9a8ba09b34c54cfc88207937274e6f78b4446b00e4959ef9b44c264e2081015938679d29ec50c0c6d53ac66861cebbfe7695dc68c151e64dc0781aa47d23be5c412620386a21742789c85f1bbe76d9f73adc8865ec0ed56e39977b1ed6fa0a14e37b8640c5fc9d1bdc3d2fdfe31ddab79c821ad4515ecece13ccc6237a922221435b951c04f96bf9f721556e8623c3c1f15a02b18fa02dcbde02d0c014df7a26e7ed37e5fe2df18b7262df19cd02a5eda01a1ea108412807b45b1c6ab0df694dbc45bd41245c198b2e3bcf2f357e4248c745420c16f96146097ab3d3e1218e0fe5db6c96c918b8bff5a8644de91ef4b705bf68734513385e7f04f50ea67f92c75703c4e64583c05989dffaa8b20e488ca7e9f1e4109ad13c11c1402758b77156ee74ef25411f61149576f5f194f62e004e8fabccbc7b032c8098a66701af09d0aab30e28f864846279e1576c40d7ea31a3fb5b1296912e248c38c259bdf76303648f61e56b0fc0010a5f1006b139af1f5b008eed5ad8a2873e9e6a5912d0f20a355fad78ee6854845834aac7efea90b629de9d620e48aad8981b9f4ca94aaf295695589b9c329c1f4130fc2c927be7f81a3da1d3ca85509e623617eb1f2a28dc226cdef06736958b07f6331684c57cc68fd37bce2ec59bd1b93dba8d07d1b2a394e1b66c35f113e185d6840cd15d6de65a2a8554bddab4910abddfe82135b9ffcae44c5290dc90f32000ce90cf67afd3ede0c9add829aa4406363b5693f74c9e384d720711a62054e51b92db5aaa84ea8fcc85ca13eeb4b0cddc24e4218a2f8e67595d52c1abee541f830a52620752ad672e228287ef0a392c84901e87b818fb2a5876c28ef539115bc202159c693216862df177c400e8c44186668465f1995141edf4791967f43afc58db4dac08193509d10175ffdf975e24df4a9de0f00e90d4caf1e6a52284da335e8aaa9bfe5ed3b0ef13716d3479681aff1f8d378e530056b5a0576c6993b90cdb295339139293c90864358d00c6cf70d64fa42b52217ee348ea62aa7e6c00bd08a460cab1eecbad435eccbef2f16e31d87a184fd0ce10e6ab08d1b85b437435a48050cd70cebe8969ddbe91bc853751b33ba1a138f860e5f285bbe2b6b17313353f22c457a4be06efdf3d406cb43cff081bd468908f43af7dc1eddc5913e14c388542b9e37e56bbe4a72c57a8968ad6c11a870c892d746b57afc517c6d961e9a938890672809f22f55f208496be4e7280e4a00b449e4e548444c306da99b6fc8beaa2fbbeed173454f0be734c9bfe406f26a47d6281317b3270a18cc3acbd3e07431e011e72623bd72b68f46d0b51ad88b6fd69a3e78c3bfe3fdfc2591e37f728ff82beff8cad40432e47c7cdfe1573a9050a1148024c2c2753260102519d96d4ac1f39770247b845dac42d7182a9e74eacf1017ab292fe9131e680147284fc631b5fbe436b5597f26a75cbd2196e9bf6d7350a61e7c5cc7d54e7cb54b06a08342e361f886c95acd46caebe6680020a1e8b14c246c4496a23ed639e3d1aaf5d63d83b683d36694c7c0761d7d86820f24d3625b0b74e1e816af2feb94b77487afabae1c39d56bb2164fb2b2e0d51265893da1d71331a86fbade0f2b20d69fe2cd5ff3a89d3451a3418070c27ca4f9c2732134ea5f0b0dc1712fc4324053cdd9c0f6ac31d205fec0bf7af5b97353bba70c32f2b91a7afb13554ee584340b324a854c79ed5349359d3cf74c8dff332e638824801db1bb3c30e38430fda4e5cbd66b9041d55e82e505306c4103dd6e07d82b3a7fb21443240e0c34ad1e9f1bae37d1b5ccbb7d2be337fc1a65f57b4430279c8237b5463b073fd5f3297b363b1e5e6f553e18d844e69f46469b63d3adecef0590e6629fafeb3e81064553175a29895d7cbb25c3f4e6afb963dfa2bca9ed3f2754c9cd25e33f2081fe7cda2cfd034750712479b7493c6243373a84a57da84de21e065bc143db3c9343e6406f257aa744f875d6e6741342f5b0fecfc759e0c5c1ea8d1370335ee87366a2bbeb23a1fb3646ba176a982586ea187b044dc7b20063da5d0a7577cd5e91623a3a77ab1b3d035b15c3137a0f3e3ee1d4bbcf425daccbbe33196987df6ca93d7e07255c138a9951ebf3708a1227e2ad8f63303ff3bab63a85154f32394a48a74ac91bff41888ca0c29dca79db253161f9281707ad6d0e45f20a2d10c7c3e6996b6d95010f83c4eedf8c8d1f74d137b361f1cb9763ac10323dc4d00420b8245925a2254adfca7413da107c1e21155da81bcfd7ceee426e8a29523ada7e1b9a62d49be09be8ade2e0c9f7df50ed6660fd7cc92817a6438f325e6d4bd177be26236b86fc5471b7516514082f8d6439c59c51f54919b4659ffc726b8c7daf1719259ecdcc110f164d3cbddae33e591a837561bcf9dcd1a00fc9ff077930ae057ddc4cfb0bf7a1c4cb937cefb8d28e4a3a4ad1a39b4a2e0748e42b6e924eac27bc2c2d65a37828714336c4308aeeee0b83738062c7b84548e1b9012556ee7e9d69d18f4064f44af159bfe82897c81860bc2f259a44a5b095ba97135c4c0cef332be376c27675f9408bcf855c38e7243b827053cc26fed23f7e57bd4e76f5182e79771c59fc3d2f2861ccf7540567257e3a9618deb009e73b4769395e0ac2613acefb6f0588d82a1de1ca5a34e9ead2b9343bf08c4e8215abd281720d95750d493915b3f398cbb8bda335d1f7140ac0e472df056b3a3818e9ce99a23d6dddc8525c6372c05977cd00151473b30597970dba4dceac453aed5fd4ed7c55a8b82cb58e94c378c020bd25890761eaf6f7f370db578cf9c57a67994afe5163a9b429e05d1405a7ce4d2e37e11bdaad180cd37b559adc87cd236789889c276b3e2fb9c0be70d6049ce9c3ed8a414e1374ec085092bfd984a76c9be1031d4079202420b9506aed8aa193433c21f7d03f7528a9a10b575e17017984889b379833334f33f7eaba753c18a44c973b99d9914a0b19dbe9788ef5655fd061d9039629f94d5d67512e788c93478b40d144c3b6329e099ce1a9ce3e4cd2f3f80d5344d5a751719cff7573683807f55995568042af291a21bedf8bbab332ff1859356f50a1bd26e19f874be131351a2dc17564ec64deaa551266edb2356cdd23f48b3598db11d22b25a4240b71ae9702f14b193c836a595089a1b8907121de6ffbb358644e2bc144f15491fb7d1b2197b09efdb5914e9f4d149d7698432c22f0bf4e2600cd5606a9e99440dab21ec2dfa66aa7074e99fdfba0ae9e2ace6cacc3f2a4cdbad4dcd9942052347cee22710f3ac338d97eed69b3f2eff86092018abb8602ced0ed09eb2cb7aac396ea4a3338812c7e9318f1a1079d60c666c5f95f73412374bac2d905f62a01f3e45a3e2f7497031691ba84f636b321d695a2e9370cb8c0c10aaf18ca2c9b7ffbd7c803bd0a92074f42595432096fca89b3f5f0f06d5cf79830c0ceea1e7ccc0dfdf6405fa5817aa77df41c412c8ea21eab35a26c6a39d9b67ca3494bf1e82686859a392e89239b3de4eb78a3d6d180d4fd161648d54f927555767cb7963ea1ffde510db425224609ceceb532375ed09fe30c415b37b79139763af0761814c4f0f9881004b9915db48b49d4103c020c6a4b78447f01bd469e2b5be0071b62a86b369f04f421b3adca31212cc05cec319d110674ed7ea0253a03fe99c227af8ea3cc3a8f800c83c1546c77cdd22c3155e50bc80f952dcd3ae03bf93f7b5b3b96a2e4ba01784acea4e0c72c74b02103c0274f1322198f63ff822b09ad26f4162cba5248b851e8385ee64b0cead4609d82e1d7aa71b2528117c9a43caac2575748f750e5bc91efc1c2648e2f4b0e7fbd4e3ba9cf59fe7dc6bdd49f4de601d1005807a2d44561561f2dc0dd2ec94ad0e603e2470f9898784bda6fddecdaaae27338b330b1f14e480de2d07b70d65fbbefca35e9b72a263ad2be150afeb1ff68d2bf130d201b9612d7576d4c03d4bed163683286a659ff1501ba2bc650c10fb49f803507adf034e3d45dd0d159759021bce0c9e539c4762850ee7dedd3f6960adcfadbfcdd2023622b67d457b3f66b566c5079fef24c6ca1aecfdb46f8a1b0f1dde2879ae197fd64228239030463c3bef115be13fda51e30eff72f401748084e7921cf183bd62fd5e29a55b7dc743e0eb35c389dc051d3862bfb1a10d65b5cf1f3d4e352d5fdf960854f53c02891f0adce1333909e5d2c413fa4e5ad6c2b612aaf3b4b3fa5b01f78e8542d3279581e8f3401710f9df44371b90999dbca9a5c8c0bb655c74f3ce2c44ca67827986ca660f853a16e5ad984c17bff9ff3fa467d7f0c87206db97217ef1a4451f80f5a7079d7e86b88583cbfc28e9d038116efa050b1f61d9480f2779d1a9dd1834d702738b1eda8d59d21baed9923afd86f70edd4cd0cdcc13cd8288d7f36e7a19658a0440d669adf01171e91953cd364e3a14abad06855a6a2a789c3a46c2d15758d12a1ffaaec8a290d015759e4ffece51a384ecd98559dd1137e7ec94538813b6117182b79883363d89cd0909e1c285d48c90ad1fceaa3ec79d3d7537d58f23872b95307b894e18289e779bb9148261cdefe5113792a815efa10f3741a31fc10b8e2bf4cb5e13495e48ea1334a65cd7d47290465441fd4a229cec81629194a215880185d2a09ccec64857357686b5f9a2b2906989b425ad6a82c7236351724a0bcfe2bf1e7e04ddaac27f7c9fbb3a24e47be9044c68d4c6dec599da5a1840a1e6a5e2f008050dbd49f646bf34c7a483a24417e69a7842c85ce2ea10b9dc27d7e1fd213f60e4b72cfbba7406ecacfbb9c0b748ebf83229455121fc5c2891b5823583ac4d1a33ade27239567a44621a4c24240a5e1f04f3a212970552fcd344a5f9ff45ced4e55e4883dd5ea80187b8fb14176ac439d2fa21c98b847530d99ed9010b76e83053094b1cdb1b907b4da9edc04f868c6664f734e2fac5cf3277fc7e00e474e6fb735d18182f31f076c110361d4323cfe0587a7d04490fc50361b77088e9b3b8a6e1a052060c931d70b5fdb36fba9dda5718b46ff7cc740a8d8a7455fcfd9c16f1c2952270af9ee89acf5fc8db813f4b648979641cbb0d9ecabf5a88fed8ca7beb034c032c9650dacf51c5174987be01d7736ae676f067884a3c4a1219715a213a4f922d0ceaaae163cd29bda88adf4bcd871c2b6a5d8dc5e3ea403435f30975cc8fff19bd9b030e7ecfb65a1e9adfa902caa4ccfc7456c83b4eca69ebc272cd070a817e31aec7f0842d8b1b8c482b15983e379a931190bb5eb3adefedff638378e9461359e5aab71441528e29574910f69dd8fe56a0f0b75c6df624634b8f20fba20c9aa1e862300256da25ee094fba91138ab273fb06b10c5198779ba04f463cfe1031c67f457004ed1afcfb1171e6149919ac27fb7e455dfff4a86cf000af6ebcf0f3abdd8aadbe566cc21eb114179916049c27ff8c36b2a389e8d5d3e60ccc5b8fdbac10259228450f202f6588bf89ecc7a4ea25bd946c4cfa30a90d55db45a6627d16280f0307e15676ba6b310e0dc84184c9a8b3e9e0200a9ada4658818601a91cd1553d331509c2ee065d9fd80376d0cf553c1ff2695455f4e655529ee033c77a858372c8bdc4f3c534b4bd0742966ce2f9e5b20c401ba5b19dec22c530c3d0a322ddbeb2a68bfd9b375a4816905af24d4f67dfe4a10588ad5f2cd68333db51a7e05013f368d2d96c324537f5fbc030dec07437ca219adef0b21a0ce32a5410e325c26ad67d0f4bc48b572172e2a5ff600f121706f300d5cc11d50c30177de5da073fdc1a13c9227c5bae52ac03bf8521bbd0607c3b237b0f2282987709b23689aff5b761c064ca7e77b23d08f3aa61e5ab77d452480f4b7e460a939fc372e7dc491c432b21963208d59b1d88caef68bf7b6cab094e8dab805069e11feb0af88eb70cdcf2221cf1620af072548d5946eb9fcba10b9ca348295abb04dc0ddb5dbd559c36db2a6fd8a0f1fd0d5132b801681f0c199d84536a5c43eba22d63a8da94939a0881da09990112dd656ab0da6b429682ebef4a39f8aae6d85d72ac683664c123abac2dbfba47df287499b23dcc7f4aba6c7cd3e8ea72da20337b5a7d61ac9340c2bd5ac2c59083582cd0b79356d6766b591862f2f47a5aa9faeeece86b1a1ce74c1e659ec197c562235f8cf22edbd476d2945f68ca467baf51fffdb2a751b94c74e277462b72b6ac32ed6f7fa9027af5059326f5bbe3861e7ee8f58f8f43371ea3d01e7b89f5a56441c912ca0264e9f26e14fff6b30acae9d05e50cbc58e06a0b26a6675173cc55102e14ac06a31630fbed8e0e0730439d9d75a2e89335112c37fb4abebed7d35666ddd2dd6147d6ebabe49f46db6fe81b2b09c4f63754a00429d2fe7e67068dad66aa3b45c6f8aa86d742468aebbf6849251203a72f6786f37ce59b757d6cb74dc2f07212e0015f01a13a1e795ce9f11ed7d198cae54cf6bd18b8782f9c688ae4b5565fdf85293098aa1f46169de7049fa0701b0f65e150fe5159c3bc3e08e64a5cf736ef65b79a70d0d4717f95f88f0ae7ef673b13cd61761e5e52a61225bc4c31c45d7bb0da3d8e1aa1ad04caf33780436d31b5fe5f1a315cfef539c45e6db38cfb8f00fdae7f280275ab749acd1867483ae6e7a16c40209bfcf8f2f44b4f937cb45636f914a779f389531f721b522802491ccf51de8c623102523e308599486884aa523b043c96724cfbc9f9efde0d5a866f4a56e25c89f379c1de35a8ae87651ed118b3a88e1938e2478689368c370cde41eb615e35d0f97ce59908c9b66ce721e518a6eaf3b809b4754f8ccfb2b5416acdf9226084f49a0eea40668ece20b95ea90809e36760fe2def8dbd46d648bba4be838e8e081120ad49569f0b7819477b794b81c605e5f7a5e88d50d02c81e81f97dbd2578335dd2b972e0f7fb836a3880e7981c307c66e8c4a3013b49555b79be27e0576834f90e3fd897493333979a362819b24ecb3d6f7d96f114f85cb0cd66fb3052ae4ae8f7182149602f7cf6ebc2b9233e140bb815406b774be48527db0dc2e95296f4780713478c112a0ed78277ab279d2629e53164a3e2fb1ca16411372c136c3a9b87c6f4d3dd6edddee03c0235888e5753546aef4267a6beba8419b12d1d25208bc4a907ef6c494572f5fc7cabf1bfe7f9fd7c71158cc0fb4156b9e769910dbc11903a0f4111ea251e96d78c567528db5df903175640b88969902877c2c3821a79399e6af56b449461c64a1a1f80bb770bbf71c265df822407d5093b367d3732921f19ab4d7da0e515f252b256404b5a8088d08676ba4f82b35a471c3ac87babd2630d90ebf35bbf7969e176b1945e17322ea515376bc446d004ddf1c0cd4c7d53cbba21542d5db187e15069409307f9461cdb87302b6472ede92d1a3bce71a1f64bd6aeff943db7dd5064dba4b5a59c1f5ee6ba7e24b7b116cd37c0534fbaf0005bb02ec970f17467584c63ad30eef2b546a88be5b009fa66e15cbd398662b05bf5cb5148cdc425c1a9f358dd6ceee0ed686e3dc6deed84ea11ad57d042f9e1a2a54f88b87c0ec86166e15baac4ad2672728116e41ff792ed322762f8a18f799424062883dd15ec856e49fdbe0de3ce29960cf293446e62e87413ecb62020b8a9e8c3a439f744c66423e011b525892b855d2bd129c351432522c6b6b58dbb221c20fde4a824ebfd2a7ee76c0361accddf038e5f9194818256d05e0a232fd8104c3b95d1a16529eb54fe7f668198af5833dc1c03178ce60fbb9e2d8512a40879e7787745d706d3fb1ac69b0837333176b0676d40b1d3e93408b6501d67dc119359a5eb3dc5dca80cf5054f8be2f2ee12f863ad0a778a7087f1d07a478e4c222bec38986b3e55321937ab275bdede5ef5da5f6228a1715ab8759ec71a924e521eb8092b0d42d7d36e862be627e068364ee6590d980cd3822703447502b8caaf5b6e4bf3f97cd964d4a8261d502ef28146fab73d875c193a98a5a1b00306886bc7694e7c6509a06082e8ada6f3614536c07b4d9bdf64547a83c92b28a079c4df02b7352ebe95f88e3f6afec3e893ba9a3a52f16ee07a0c67482cfef9f5c9ed52d58ecee925c01d335f90f9cabcc864513d78b90ae379e05c314ce128e0c02e2f8fbe217ef93dc3bc3d00a9826569af9885c30d614c91e2c11dc80ec6c7abe9faa6beff19391abf218dbb60b7e1082d597dfdb673cb38c02c0c836a9daa1efebca607d74a81c634f182919f467efcfe90d8c7d460362cba126e741bec12c6e276a6a519e4cb84c635e8c0a7d5b77f7004d157c8a15f11888709148c371a4890bef8ce39d275b20d19083548d145382a711a8342c65cf8e688c625c365283980ea968addc51dddd6a815ebe4c009cc0e31402fbc9fdbcf57ec9a3a13e0ff2b91ed09d4abb89a733410f77a9381dc5db6b9da2de622f0e06ecc609d87fb492748ed71735b71a55a45c573e0456287b62d85ed6934b38f36dd17156bec97294ffcb04c3bbf1da25a54fcec984c0007593e9b56ecd62f0f7355fa06f203ae78b0548219b68003a885088f1327f294374cad0243946c1e40e1789c2a8bac8698fdffb8d82374bfd8c29dd03b6cdb894c17bf9667d739fe2fbf1f3cccf5122d76a8dd9b2cf5bca0921f7ebcefa3cfe78709fcfc6cd5d344e834069de7f6eb9bcdb9062ab515745779a5d2133c9156a9ddba1b3f66fddbf799ec84e61bf6b6bcc10b4a51ce5806d3630421c147255167860d6ee6c71497dd1acc5ca236b879ae26a5d82ca4dd6f7aa697510baaab38850143360d6f1f14680d5ded4e83a15301ae59f4eb9c657a6a1d639056ef25d9dd298595d28a4d05f1e371d6a6760a17740d7f002744b17ca67ede346da3c8482b591a65be588bcef7e566ede7388aa9aff55e4302da689c9893ac717da05976d61875742661dd83af486eb0301c9eb67e3bc6b38c991ba60d1a8f5e8e917a11e289b79158b2b44f9911af103953fbb541bb66f7652c58af194a56709a11ad3095f28094a1ac39f4e0ff34cd82c41a223426c02f7a1a64bc9e70e8be4174835e73c39cc65d49670f1bf4cd2c20bbde23503610d99b582f225e7e6b44306acd40be1f2cd0d51f3dfbf6f34037145450ba7126f4f54da1c1bceb6132de78a7ea5069daa40b8e351636a696680bcec1382d73d3da098a2aebdbf5d55f9c9fb90ed7dab94ba4127301655ce6d22fb7d34d4751c99b96d67810f2558af5601b6e8dbcb56c9240ce2b16735c2d49c6bdfe4ff5625f7435f867ac0ca77233c8a2345e1da99126eb4968bcd71600389794e185102951e48c9dd816695697df79ae636b8db66d1421a21a0ce78c087751a607de192aea29f98eee523821e2e38954206d264baf665d37639cbe69b5d594db93f44c756151e9e8b8f51f4a32daa04ffae6b0145d4721eadf38b2f05cbb26105c6c68c0965931f8ddace0c2484fc3d563c6b46ed90a67a037eed1fa0bd63b458f78edddea5f0707242494e32bc4d250998c5b841bad4819128f6897b4f357b628dcb4df84d16fa7283e3475e194beaef92db50cea84651fe70b9e95c8aed788ef2595093d94927e810f4cc386bc825b9b915acf1bd8e4041d05bf9e38cd2b4f8700fc52bc56c702ed68e7cbe6f90a3dd86664e14cdbcbbccbe068dd6875315a9909f7dc4371f42751177d5abf90ab26cb238c7a4463d387114a0972d0f9872ef9cb0fdfa29298c40a26fa2f8fc8f5fdfd437b224925155c6ebc6516e22c8cd2d8b0ec9712294fc6eef01396eb7b431862197a65f9f30634f0387f8787bfded75d2583508601186c396d0806fec4e147f89acca431bca1c290f26e06361ecb8ff9b9d3be3d68b8d27dfae6f8ac37c4df4658872c03e1ab0d1377e63340dff55d600db8821247cb7082a217eb14e97aa7dc53c5a65ac556286260cfc2825007b87f63dfcc5cbb559008024a2d47d8214a07326ff72bdc2211e65c2b20de4df99d9d5978d0763aefc787914c8a1f966a9a53fb58eebe7bf2e2f3b8b84b0714e0477e6532188c5d0b692235fef0a492ed10548b5651633c04d0007b3a7581504741186832c1f4d701d4d3d7e31afa846c5bc3329529268b5dca6a2f971e275dffcaf70f933b470301072f3d4f32a6a5a030890c96fc581c89864a00ff860ba2794943bf204323906c395724916f78375eeef4142d4cb1c53d5931607972b23a4f911f4b0dd71a0bacb599596a726902eb2b54f362c07157bddf7a9de14b96ee3687582a8168cce53fac12be8ffadc9da8be96fc0a05377347fd271aede84fdaf0081e9d7e2c7d64bc8261dcf4f4cafd29bbc68374cc1192a2157ed24dc480ccf735142ebbc931284bba6b3c99bb5cd122221a560ac76ecd3695f67900f2ebf164ab67bbc19c2ea31b2429f21dc3ef7d94266573c763f293c74a643b789e95545199e584c52e7049e1feaba76cc7bef33e2b6114c568d8000fc7d2bbcb6c440a380d0e7c3b804f61a72458ed9493719388ef3fc00c166ab90b80195b0cc46783b8055cd8ed7e16abe8bce13877f3f77496855965da15b5857b649624aaaf1266a099155b17638aa13fa9ac7169c71b9dd7d8fa04127f1852d41c38889a65a95b1106fd7aa6af3d7971af04e437f03a13ba7085957c8759034cc91253dc6af0bbb13b0a065b0c9769a8e8df03d93d20710288175a53792113f62aceb11c6afffbb2e34e4688736f4e6daa4b6c2290746bb7398c29aae0c33f4d0cbb9872fb1a1f144c40b8ab2886d9f712a412719acb2e672206d276e6f074b6239e1d0bf06d365962e6bae06cb61cae44cf2f8977fc1cc3c756ea8cba028c8f685c2bca91ffb9957b847ed9766174a1f3ec1764f9b1e6115d28becaf608cb22a0bf4f7cc7d7656ab08da6abd5ab0b82ea275e0788812f3c11544738ce377ba1a76b1cdfbdea2f66594edf7480280e40ac64791f333e50d6061dd40960c13264a4b23dc09338cc5a42c4674812a143a1d366fc75ed3bf9d82d169cb87157afcdacd9d16e11e097b1e5e208604d2b11ac261f0367851b5f413948df92375e4add4903a5d039a5b34e3e99f7229b287955eb5c0c71e496167d4ede6f9bb559999c6b28d704c42ad88b6910336e73c48d8514ffe3d78b1e09580ceca500b13d128f51d2c343df01c58d28959e4bac4f3a4e3334b8057e385eff6527a20ba45e6f8c52530f1f8d65745f03b7a8327c4398623a9aa2396f7a7a3691486752625c4ce6117f714136b0835ff6c7cdcd821d9fc03f76eeeb72f636ee8202e5f7715491b0bdd357cdbd35e97a7e0eae22b7dcbb2a105ae3c39e07ff346a5958abee8b05590a674c00183d3052dbafdfec2742ab53072c5c6eaaa9133115537b2c35d19f223c3c7e4b9f8bc0e80db4fbb1deea483b8fb31835433fb5320a5e14289cd6bdbc93d9712d71ba20bed5d6ad9d4a2cd3f7e7db22aac6c57022530ea42d8013aef970438cf8f56ce3a79fa4896aaf9dd32902cfcaf7258b79c5aee3b8c013c0eea36e241abf49a4ea34db701acedfdc1964b8bdb701778fad3c15cd71b272ed3405ddedcfd348e903fd445feb7030e2c3bb74d9e74a69b7b5439c82fb41c99ed5a22b07d355dfee94dddc897eceb7653a4279a6abbb1e7d4def0d226b1e9feba27fb2a98be592071b2312c448f9e5ea667c4e8b5c8fe92d1549f50dfd7378adb3ae8e4ce95d1769b8ee7e744cb6435022c822d4c98066c711d066700a48bacae6c0ad98c7394ba913200825c98fb042e6ee04605ce4cf19c03fdf00684ec6230af07f935a5d1ce16be06ddefdc471bc6a7e36fef7af2d4e86b67f69acda5907cdba0440c7eb09ed0757b4dbfb00b84921a1a0345c082822e90c2baa5a73e14cdddd3f3990fef323ffc709583ea26c556158f0b6015bc6b83d82c0a913b991dbd21f85b6cbedf875ce481677fa3a1fcc1726bd58fb3159e9e801d942ec24a70a34fb24f2422251d729a9028a0dde42408d24335dd3e24b369fce241a97aeb5563d2ed31e1ce73e4a4604ae6501796ef5d20412d79c93dad1c88c2c0a492d64155b044411709d86047c30a3686974c6486b941173bd15f2d369af796d9bb5d50a0a6159aeabbe6895d808d045428a5243c3978cf9ca5b3b47d91e2a8d834044c0bfb5608c63434a7ef327e89ce1f497edaca5611a365d3dc230eb7442323834731646d5854c0153a2a63ee52a4a70196e23854d59ba53eab4ab4d131e8a7365f2c90ff810dc5d096f424f7c28adbe0b3b5879a26602da3cd004af878c8a4f12a10596152652ea0264e130b53bbb2e3e112af106699ef7e32b84f6a18c21e98764cc52e9b1766139abbbcf79d094553fb3496002258a32b93c0d8ac768f58702500eae159bcb229bd5e42f9d5e5f32cdb6d1b41ec493be64b98a3d220d151e29a297739ec8a9a99c29f99cfd6bee8e9335de024baca6efabd6f3a9d095b582b4d96d3033ec1bfb072e82a7c75e2df640ceed12c362ed589eacd3c9edf33cb4f18735a925eaa06a242416aa4ea0f4ddef93307dd3f992e2bf83fe25d94991aa8aa23cf6548c0b800934ba0510d6156d432b6e7d3103bbd0db16a96b0f0e708c7edbbcbb02f36199bef872d4ead6da93aae57758b3484481ce682ca79aee83cd8cbafd9a7790a0291b090cd84b65cd54bd0dab695f0eede4f57d1f5244a39a79f1ddaf7271f6ee5fe3b09bcedeeb92b11d1f6edc021408e85a8da90acfd6aba0bd757a30670fad5f509108445448d5136a21399935f18674cd9d4fc9485b4f779297649e97fb9bb17ec3021386c8eebc3c18f4ed7e919169768deda1e2f2a37056ab9c33e3bc3ad3a7c6a253ec94cf95c84dddf18f347ee5ccedd07d195c3c8d330fffe1c378e3c4e6335e4f82cc7ec021e8eacde40525d02abf9ab26448cc656296afae07fd4ae957d2480f5999f5ef36ec32739cf7592f6bbdd0c975851c409573b1eeff2b2cb6d1df0914075825df9cb2165c9cb5837b988698bc926715fca3f1492bdd2075594f20e3919947d0559203c05cafd41580bf8c0dfe7da45ade5031b367b7ecae217fbfaef6c6136339c09563bd77dd9959d24015ad5ba4a427d772636662762627ac8a67f66b36ed804c7633bc311f06e817fee81e243d53518ab16347f25e6d0e8813c1b45f26b155727585cc9975842aa6e0b4f70dca1af7c6ebea7f45e539aa7293b27a0db7180ee8b4b7b9126339f272567ad849dc7ff5d3b55db3f5971de6898ef0955ee24f03541510a95c963ec9f62aaabce2c1608bd0b20afe17552f79931972cfe9148c51d65719f8e1da00f86e886fdd2a242e545308d486cc931aba6ff819a64196999eb397bfc947c275151b8c94e9b7b911e8789ccb30e54d3ed94a37576076b505740375330a50e76e7dd611994ec2930b4c67388d32410a6caf7eda343515bbbb252278f6095b174cbb6f0c701477dd235edd6ab177edf603c18357902fac2bec1e29d9aeef51a3b40ffdb7fbeea321f8ef37d30d56cd500eb5a2166a12a7e12daa67071dc2505b3f0bc65575b3ec371a5436550165629c2f768608235d8847324a2d12cf483ec4a04be55ffe2e57ed45d10430fe61629af27104db19f3012d1d4f46f17a7585be307bb8822f6b56b25202e8ad814e41ba807bb9f3ed3cc98ef962bd8dba78582dc5c8f971cfeb4d8284aec4842a2182ddd4f7ab1715cf2fcbe2cfc6af98e2b88df0b6fc5d9ad9dcb23d9baf1ae9f31f82a95a7f4dbf208dc59db062b8fb510dcd345691de57c4f108c008302da37811a9af63fb7086798438b2947d105d3eddd052909355dd82672a003102b643de0c170d7f0b066570576003e5b423bfa047b68b346cd05063ea4239a0325dd45beabb50b86c243df2c3d3084bc17ec710ae013152aa09ab37295cd7a4770b1dadfb5f097a0d85b64f1724547cbd6447bf73ee7d1ff8769fdee5700686e0c53739c266f23ca190e5ea3a4a1fe7ba849c1002b95727bf7fd96fed51d20eacdde52be3ab20f6f0ca08f3c2024dce89369fb5680de6a941677b49ad0ca62953b19b8db998b33422721a25aefaf66d1d97b18fb78bd5ffd55ab709b038fdc7de17d8833f1cb5634eb98454b9f41b91c93e4509da10724eabcd4ff428622fd8269eb90ea14433098e674ded6f7395d4c4d65f82c11c20fd1c1734f28161bf1453917608283bfe80a6ffaf1ae515eae41915ff69bfe239da33d6ca9c2318b19e323a80e353dbfb91c251688e33e152720bd869605a367a8ba331faa110b69d5de1f344eae5852e93a0ad21c790748e1e366736754dfb9701f6c214fed237a06a8f923ac528ce08365067f574f38b606dc1dea5b840075dc68efdaeb98f9b7bbd2a52717cfb9db9058bcfa7dfc74f274481b079d606d646ad6057ed8037e17125a1804275c09cabc6aa991a8e463919338a48768e9cd364f76506951f0878fea8798f285659cb170e00dae6a2b548699d67a8be737a6526b35b3481d37f908fa29dded02940ee0918fce2bc043b9e325ebe259ae0e25c60882e91f400efcf0a81f4ba6ec5e558cdbb1973c6b62f90f7e84d40303bc1151264389343b17f994123cd2e96055ba0a9c2b1c7d92682a377391c99e5431a7ebf8f75e5aba4000506f51bd4ee19d42a98f18992f64875a1b2a1b753d92d8426ede7f41558e2e4376217c02d8c536fd00266a1b2805e74169610eb0c15c90c5b459aac78f159cd3ad2d1bfa5ed88814279e5a196913f9ca8fdf074f0971461ad32f5f719e0acacd3efb2a3f6684ab8266fb7850f30bfbf24605d3e23aec6e5e5d5049ffb3df48168296be4ae7c145b256c8d1bb52ca757cf46d70bd27241c7d9051149511d14c72d57363cc3483695ade94d48b69dc9e5d5e322f2c93889655641e427b1c71e577baa0e96afb16ec21cb95ed5de91b697aee0925de378328b79888ccee5c39773b4bb87aa3eedd93d54e197c2f533c45171ded7d801a39eb46da67e62efdbd97927e18b9e54b2c3a726635660b52cfc1a0bb613181ba461055e053e72a9d6df534a8422b0412eb29b1e6722951e6ba2e95e4ec48fb1475ce5c07a76f7e1255921a08e250acd2415b4df63e280f7587258c1ed92bdb759ba90c32d4e63840c4ef40c49dd58f3abff007a36835ca2f7b34783b37b535f1f5a13b0e136d3e11489ca2339b256dc0f9078bf548ae3da4120d7df4909c8435def32ada5681e5de41484c65aa065c49ec3dcdc0b7c041d0e6b42ab533684e1acd6028dcb6eac04f7952b59ffa836f8cc9df5bd317567894c30434fc6000e47cb671af8e03cf4e8b5997b2397f87ff9bad83107dbf4a1eda817b3b251df7cf894de8378bc6559f9a7454fe0a59f75ff90f6e9613ef884a0d74e3fc46737037f1507f0d3753c1f34089a1143f1a2b9c83e83671b3ae94da32068cff6f0d582508df87f08078fd9665a9a9217d97e47331e928a08e6a6a43396f02417e1b54f76186671fa2f9858703db5bb8770c87407e06676220e2190b471b6420e71eebcdc5fc28e7aaf45ecbf869c431840f07fc9348424add427410bbc3d7b35021da42e3cae5710565bbc93bad66a3a43139710b686e167c6eec8df5e704c8eaa470d9a0d37e17620a5e53efdca3e39f7ef31b28ce2740bff49ad26e08b6ba236a6294af3aa253b356aba78aa76a2722e0b8086fb831d9c911860a5cd52c5cc26024554de2ae287b534667b62729cc9122d9cee492d9e7e25ca7dd458df77b03cd58227eb9b52d33fc3f181b137f6cde449eb489358d2e757dc3f142e2792b31d03591c12ca9449603473bfa9b509de6b186c773ae8edec44ab25a597e3cc52080c9a573416d42eadcd0d9c368cbc33b6a02616e67eee6d7db4044df637c05918c81b15416b1686b34c5f1cb33afbd649a5c0a6b63c001aceb17801e02aa2886b31ec2dc1a17b62ec0efb71b341a6db61302f551506e14ed83db9d525103ce6684a06b161b3d62ccfeaa5555a5ad7f7567f6e6173bbf56a76b7533b4b9a60914f2a45178625abef752f8e0a471aacacfe7525f8d22805967ac45e70d5d6f89294e8f38467b6e4fbed1040a52e2e7b7fb40edfc69d3e9bd0935eaae37ef3fdbc492108685daab27a04cc945cbbba32c9fd4b45a6ebad7445b3307a87dc15918196948b2c6f0ba39252d00dba2d4139d4e645767a964c84dac6254cbc6e250e85aa32fece91d1b4060cf8d917b2f07cccf96b5d41d07a00c011c85187052c39e606d644d9edcdc9fee726d633c3d10eee09f2b2f5afd120906606b9c5fe08cf0c0c1cf8ccea4153c95cf8b1083ba3f70c5941e72e67b4e1c4c1c2f38f75014b13962de7d746f3804bbc435dc7f6895d478eecfde46443a8d3723edbdd9cd65e37c2e979a274c4ff2466726712238fca7c7b187dbcaecb99637c6c050debab23a6906d354636a7515463b168962a84fa2a13a17fee602686386803885fd41b14e7743f2e32a20f1cd9a86833c84e5988df163b725095230de64a97e2911745357fd3074e942cc8d9e370e33895f4395f866ae7e4a7e2a16798c15cc924770db066b65702661e6ca38c58876d3aa75a7ff1a6b720c315a006ce7a3e771964fc4dcebc6a35107b6d3aa5d7d9568a8a832b8964b7ee50ad4d2a8757d89f7293d43963cac0befcbd2e5c9fa75860477a4630897e3bd4a2290bc3780e282e7f796483754ca79dd09637ee142439d0aa3a679d7c24713c9d2fd4753e435c84f68268c839173c59f7e357288f656e75fbde817f89cee788e26904aec247774ff440b593432b5141012d4bbe61303fd5d09dac03177abaa78b9ccaffb387f1517191dc2798d7e723d8b08faece9467e4ef6fb5a1ceec42539a1f9441c990f2f19085b326317db4fce70263747d465b36dc5a0eb0ab004bc5e2e990a940305020256602e97535b6641e83487c862ce3456087f7dd20b594ec2f8463963fdc09a3c6597fcfb6e41816d79ec0e326b82b60bed9336feae15f4795a3a7754dff8461c4cfb556e72bb9e766745c0d3dfcc6733db8d72f6a7ad5cca56e7c02bf57ac9045d8cb87c1a5c3a6640d422a24a301b520bf518cecc375a8ab46ca6a1d89f07f0d8c5dad51c350ff2d9fe7896dffe3ebd0333b757c1e1fda884eaa5af7ccbe0986097c9430c2d9e0b6f9814311a5445d05e16a3f093c5213ee1f0f1595959f83ff7980599bf3f3d0c4334a4057e51aff12f1a30fffa0dc69e199aa58abd1a1631cc9248206f3b3fbfb34eebbc2c6bcfb07ccc4fa0d7fa71075c09da28c123acff954a77b92e02a7ec7c69183c3233c0a8dc43cfdbd08792ece8f979959b2d3747b823a24152f891d3791eca2d96fc344187fe000ac0ab73c95f0c595cd0c59288af90c029585127059e271696232e34f83394f29778f82502f4e05789443c0884047bee9d505835df40a4f6261c7a7963454e488865bb998851ccceba5c9ee053fe03e2fb60ef9b743d4bcd435531f5952e5f30d4147ef9469d9c266cc5c8e6e1131eefd6a9d98743a055e4f1bd2fbc9f57ef6cb370debcb93d903fa1b7ee34b469b9b4065da08053a3aa60eb65f5d44be26623159f2eaf587be023ce490db86ac277ebbe8e08119aa6a2d636286f47bf03065dfebc080331fc2910d3fbdf1520f0312cf78c1e53da3765517e1b00a847faf8d096df2c6b70fbc11aa5727d629fe32a7d85374c3fa4d599815430078cf5e3fe6ecff47ab5c31eb606bc4de48a6b25155bf458ce33920854372a57a9560babce99f017b85f11eee6d17cbd4f9b283715612959615027cff8c17553040bf89864c05411c41710d9c52e515db0dab81c773bc21a11eebef74f15294cf44bfc6239d3571dc9eb404ddc49fffa64ab1f4e32188fce1cf890cfbc04f8d2e98710a3bfe176f08a0a112d3c0869f9798fba4c1422fa43c97dd09a473576a9483ee153675b64b48c26c15d6c37d34f93906f5b93ac929eeba01c3d55f3577af8589c10adf5606580bb4f655ed1ac658fa590d2f40feb395872d636d1b37e2bdc08d0fbdd84fe80021f973814e29cb426eb8d741dec446eb269a9e9484b3bbd7b56135376f518960ccd8f252d105a227459488c8354cd0d6d07eca502f92b14b311cc5b6b240a3e82d58fe426f845312404647dd40f8045b9018586e02af94b8ca08fa3640aa2c9ef80b9608a92c568738894812499c8e52f187d5b8fb8cb97e22698ffd7170cb36573c320bc74443077aefdccf3a78c218d6a18bc9dc26b451e41a5a81919c98cae84770269a8fef469802f615f9fb81a1270647cda0befffeb06c5fd653c4cd24cf580445905cb0df5a0de6173740c5df4ce44cb029ab718e5b8482ef2f81a856eb866f0a3a8fce17a6176108d1f43dfe29fb03fb0bbc3326765cc32ca762c40f7ce927b9df00b9f737ef501fcd82231c8ea8e448347a6e833c71847e98f6535ef581b3e569517062b0f9ca8620c9955ba39ed05d99b3e4d9131eac17bd6cdbb6a7a41fa58b710e3b30b966b3a9b84f59deb57706e7c8d4dc61eabdf88f335a0a5d73b29c7eaa08c67167ecf5ecefff8b0f4590c9af5d38907061937611cf63c2a689b280a329cd97c5867951aeb7bfc032752d629e58e050504ed6ad23db8ccf5e1bac729d74482dfbaa049a3ffafee3c0e71798a183e86476e3977c3c133d72b9b290330df89bc91461c20def6ec39bdc1eb1acb9214b34b2364b1ef7df39d0c0a5cea325dc20aa595d8fc2f12212ee1edd8b70590fcd4d421887460422abbe6935a7bdb3fc90dbfe2940edd23db4b25fc9dcccc9daa14c90c24a76a4a4fe720d89e5c9fed87f0668cb2f8216b9679e5dfaf2715898257a91100ed8f7ee4c82fed075b52d1b318c2349adb370ff9337e93628f118dac7ee2f02206a056da80e0d91587a4f3dfdfebe3b9b61ec520a0d7a520624c475f7af12c1d254a1332f72da69e543a1f27c0448e52af3549b91bb6e21a094af787376b2811ec51c7899cc8d75492c5227613e7eef16ba07a73bd46046a7f8358fd517c4cd4cf482adc33d7a2871461884e553c0e7633b1186f8bbea487532d7d796ab8c16840425956ec345f4158535cbcc72d182050826b3c174302524ae51e4625038ca59c08fbcf5fc9f09629e0c8173ab7aa2cd84a06e2d9818ec9a52234ed8a768d21f33887796cdfba00cea067ee13c3c4b07c4e756174b92196ad9cb96ce48337cc2c927ed193ff4e06f2bed2579a4e8b60e7671bc83f94d908638d7594864fab16c3685edde0bf1d649f54af4eeb11ba568d0cb6162eb3ac89c1e0f772eb60e521a8e8e16e626fb7cff510f4ecb6e075ab6f480710ee515436e9cca62da397467c82940e0c802a34a8b06efef3a8e0bc2af701dc8da55186b4781da58e7d051a3cf3d34f273fd88e6e412e1706ed77fd0551d6037bfaf0ecabf68d0fee4e92d3f921f06059f98d3dd47836d06b6684870b787845b32f6c4dac70e8b41971d7e25a066126789a1706ceea994cc1ef8b4971a4651317d815ecf16ac4ec202ef574d98254a5b8c32f9ac1de40784417f0270f5a542c0f058b23b930f68c78697f322a6cfefa63e3736fd38625a85cfa0f753f92017b220c16d1f0c1f0563605191df0d3ca1574ca56a0a2fe580e8b550bb50f29cc1d64153c7ac40843e3663ecb33ddf8812e53e6dd6db0e51d23d80cce94b241edb7402b64fbaa3b877969ec50fb25db0575001898a4b4e06bf51031ded626ac8aa7b5919423ff1a58a4c381c96d4604fbba1aebc53a02a9a7991854325914ee196ea180a85335ec785e4e8f735f5475e3140174351cadc9522cf21899dd49c3d3e1f120a2b785e5f267a776316a9f74f8e11b7cb5743e0019b45db33d3a101e3e6e3b7160da0a26dfa67d0056c50b1faebe909b32e4e574cdf677c30caeb8527e869fb071d2dd73ff1c5553a7b10e77fa642cfb5928d1c4eb80e53c5313d834110c3631872fb6da8f78db005cb80a5ccb18710ccf50b3eb9810d437b4710aefeafafe530315cc67393fd641ea509251359aadbb451c1aef3317b424f48dc0144a675d955c42a2c329fb91ed2027189623ad639f4922ad34dab512b7dcbfffb3f0e93dedcb9e82f5a3ffe1825c30c9f389d3f64d46e3eb7db98866dd927be800f03850f8f129ab3abe0bb47c2fee92d427f65bbd808b4bcb11dd7957d39649bba8270c08ddb14b5e861c8a248878956241bd68d0f2a4e4671b09a00ebad01fcf3efce03f5889acd6846368ed741ac03bfe600b493cd85163104fdf0fab136447355d2ec95c05f5df6be5b9b5acca5682a0256471b61be5a2e6c1b9c75f52ddcc7e6e1f156f63b3ff3c505b388aa51c0a6d8390bc773eae550b2fec2a1f768a354c7beeb2046e8682d800961b06ace7b5b7c92c22d379ecd32b5ad8fedfdfa82bf1631d2e04f5d9fe981d3e68704656d22672785837926f9238c265864cbd890fe7fb48a528c810ae7e14da06729a62495a82392f7cabaff9604b9fcb78d88ff2b6d0c8a490b9a8ff19d46ffdbc851131fda4962f9e1bbd43a4ee2f360399e5b87de107570b330a512497cd50c15192e6bf16e499131234cd333894a79dbad89c5731e45612f774a4f2afc252e04b1aaee781fd7050048c7bb39d296d1127e28b4b4092095349ea884fce436e5baee77b1578b56b1fa327f9846d5fe470c2f7bf5fd25697a5421c007bfc386fa84fbb16691619f311794a166d1ee29759fbe9503f3858a579a04d1a8833ae2ba93dec2593b67511884e710234ac6413558b17821da6efce1be695f463cbe68370fb20b0ca05e5fd16266c51bdd55ab7e7f70289d80f380f01212dec90d17b11322a7b56f5e213cef05a63949d27138dd592ea247712722cc5159984f675ff7e19fcae83fcfd360538e0f05c423485ff762b6ac005336b39c3d1ef431b8890bcaa80d70f4a4c66a4f0e05d801ca71e7c5d38773dc207cdb460ae617734ee4eb776712e3f0422e8709c43b63da7d8604b2ea2030fe7e1b6dfa7e778692084cafc43f6358ab83579e98e005e7937968ffc41e9b2d40f69aa893b4f7576e77f99819f4a1cd1a88ff527f3c574b7d3ee43066f6eba3bd7972e8e462f0f31a20fb21d61d029dfc91b342cb787f335fc9aad298a4b9be88562cd3a27d58b9ef5e52a370d009bca309117c778be636f0c69ebb0dab3131254e88ca287ab1ae01e46c49e797fbd7a9ca582ae81e64baf6c12dac97580c6050b6b9f4a8142f85f6dc337dee54ada3ebf60dd7dc0bbec45f48b980010e78e6beb82462d8b5754bc5a9d60b17fd6d6e9f39d9bd5c973df3182c23cc32c2ad98b5885a08107b5c0874713aab30ff5b1e7f2e0fac10386527841330308a571b5eed652e223fae7d4b60d0548f4d493a65fc7954c0dc7848fb9cdf2074746d1bbdf69657758a83984cebe357eac37c61ab9fccd05858f28d6e31223a6b7b79878e48ce57cdcadd97c19049b70de467f3ac5eebbac18719defe3a96ffe72e9de13ff3136c400ed56337cdafdfac3b4e07f56d6f28a6bbd36379bc3a214a63b5f6a59e106ab5065c4ca640e104314ea1c09037a72241dc5311c9ff4ca87f645c4d7d43b2dd112908a0a3e09bbaed4cb50e0d75ade685aa4125be1491b20c07155171eec1c213fcf57fd26f51373ba5affd516e866d8f4dff87a9024c7dd6cf8b7332c9a18331ca93ff82a9eb69492caecfe4be1d923911133b7e2c224ff7e6498aaf6b453ed22ea9e245a74fd24193613c0f2828d74e84002a43356b599470d0b76b511f1f53ebc8cc34c7fb244a56393409c144670ed80ff527a5c4248168d407d5c85d94838e3f779e0f0593e266c2606f2d8d6a0fb9ee4ed7f277bd9dafd47fee17fc1c01c95faabcafdbfbd8a460d46f5013c4188fb4c3c0692644dbade8a4eb0406f0053e53dc5413bb6e027d5288523b3c60e27dc6e0dd5712b29ef92c07dd2f003ed45a2a662d2e95fcdd63315777ba32f8ed2f671a9f7203459d4300aec0a491e73efedf5966dbfc35fc1487b78f47f118e799aebcad3bc81fafc884e6e9f938bdb2b393ba990dfa1fe349d0af14a4ca82a0f6dad789c331737d984a6eff780853aa0177f8f4cd9d1488314b2d6640bc5fb915daba5113c22934e63ab29b693d5a7b8718f5b78f5bc24ea57ed535bf13dadb59fccb86ba62584dabf73ca3068a95c619456d2f5efc567c70ba3ba4d2711e446f07cd0df6faf4e6d0881da7f1b10c97c7e4dd5866fdb8debce74f382eeae112f70ef4ef6fcfe058c77fc89346eeba8c0a28db0e244393fc23b675e65ed472aee1e0634c8c4d70f2db2af9f1d74417f63e243f709a6d5db544d90a09af23411d1a7dfeb642d71dc39367590a2fcbfc9598ff55730a263fd22df66b195fd2e6492f6f07bb3ac854724378f5dbb60aca1343ea79cf5743d32e4741a88034dc38dc709bdfb43745789970a82225b1c45cf45272b4dfc290563920ba5e9478369e187b8ab2d89910bc556b260388426cad044fba0a4ee4f3f44ba6a0eda0dfd653f43ee3565ab8fe120d9c3d90738a3d958c700b17c227fa2191fe0843fa1c1929e0fb36701f862a07fba5d2efd2fb06674b33f98a9849e9b4dcddbf5a1c10e9769fafcc5de2e6b4e32edebb6aea4e8d4cca649179790e93abdbef241e978d2fb724459022d9d90c854e0e50b5df3b285dd6c4ba01f954599ae6c0d78ed910c3a49a49d73022a370f4de88f1e99abf43c137904574f73e23083bb05036ecde1932ed6c8078ded39820b2a64ff149104710ead75ab2aa5938f0a47f305ca6210f72ad2a83c6f7c5d8858ef0c411b5a0915f0341145d1d59ad17b7ddaaf437e0146d8094fc5ffb4d5fe459b99d96f445341a84f373782a27d8de2a820ff6761bea633d6c4bb9a85537b1ea7385c261020e6d3775facc61ee95707ba158d5debc973206f098a307154a5c3893d6d672b2af2f7395734142c96f017b65aa29ca437ef74b5e46c9f12205","1703030013dcb0d62fca666326e2b67295e2ea8d84766fd5"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
reference13-records-after-close-notify-are-ignored {"state":"connected","version":772,"cipher_suite":4867,"group":29,"alpn_protocol_hex":"687474702f312e31","outbound_records_hex":["16030100e6010000e203032832c778b4911a028922536590a3d5b111d976cd2c6b5b0778ae08669e69e542201a6b8ef84df03e655f15dd1dd573d4a6b697ea0a2c77d3b37c2a3353d2ea6c390012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020e7f8603c5467cc1f1f63af52e28e4e3b9b861e356fdfeb404c5a4e80cd7a635c002b00050403040303","140303000101","1703030035620a667b4eb0e92345842af8a71084c904743e7af1582c91f55b64cd8483b66980bc12453df1ffbde5a980e2c010a283c4e4019c38","1703030036a7d0d842934069141e5844156084289a1574d96cc7936520ca2fea9fdd632795166f6ad2bdba8e65f6637118c8e13bd7a7373a54b929","170303001307cd11c4b04ac9ac8a5b123dc21f9438bbaeb1"],"received_application_data_hex":"485454502f312e3120323030204f4b0d0a436f6e74656e742d4c656e6774683a20320d0a0d0a6f6b","peer_closed":true,"peer_authenticated":true}
```

<!-- test: tls-handshake.reference-refusals-the-canra-vectors-do-not-list -->
<!-- tier: stdlib -->
Refusals made with the canra reference server and checked by the canra reference client: a Certificate entry extension the client did not offer, a Finished that does not end its record, a fatal alert and a change_cipher_spec after the handshake, a KeyUpdate that does not end its record, and a plaintext handshake record once the keys are installed.
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
	end 'negotiation'
end 'renderVersion'

function renderSuite(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending gives "null"
		accepted(suite) gives "{suite.rawValue}"
		keyed(suite, _) gives "{suite.rawValue}"
	end 'negotiation'
end 'renderSuite'

function renderGroup(negotiation TlsNegotiation) returns String
	return match negotiation 'negotiation'
		pending or
		accepted gives "null"
		keyed(_, group) gives "{group}"
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
	let entropy = TlsClientEntropy.create(fromHex("d46cdd33916b60f9d8cfdb9852f0cf1e4921b562ae6d5aab767ac5238580905e"), sessionId: fromHex("e51e8db5ee4e493f8a4d4bd5742f43c004ee4982ac72faffb8b46e13b36e60b4"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("6219d03c5eb3c7de30447a10d2d8d32315d39291eb9febdc638d90d1669610f5")), secret(23, privateKey: fromHex("18fa0d88be4881b7749bd690571f2c5f22db7e5cadfa98c29c535a1ebff22e37"))], pssSalt: fromHex("499c0d427e5b75ca085fa11f067cd7fa7f8fd30b8c5823fac98082f7923034544296cb13c627a12cf0a1c29c383d225931629fdeeb7f92a63680d892d6f9a6c6"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-certificate-entry-extension-not-offered", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303888b3b22f35337083d20ee7e12e75309c7fd5b48d34240300faf8cb415cbac3420e51e8db5ee4e493f8a4d4bd5742f43c004ee4982ac72faffb8b46e13b36e60b4130300002e00330024001d002079b9e49601d74b0827f8f0a6212df32b242651a3722712303f869ad2ce16c662002b00020304"), fromHex("140303000101"), fromHex("170303020c64e915adf55a17d4c0190ea8ea48aedd861f3efab546096048dca594b37afbf8abd0db0734654bc525b5e2e568ee3ddf33dbeadfdaec2153ca3408b1b7dd92fbaac3cb57adccda5023569310fa52a6355dc1182deb24759499f0dcdb17b86135ffdf0524c9f7875a7d6d1d55b1a859263dfe23e72087f58475863abd8e180d0941f9828894103c9ee8483d1c436ddb16d17e31c5e4d7f7a5b6d2f580bbfd3dff0f06f095c7302657c541203b662b14d5eb2a8221789a48d43e21f9139d5ee260bbee0e611c6de129910af615f625676e09b0bcc6f15be10d32166b4e2324862f1180d251e9d9f547f3fcdfe81d4c91376d28082be831ea6cac54220c5ace9d87206c36276a2af1831fee5946a8a1fd47d8a31c5f43d3a621954fa099dfcdf525014e999e4578eb9bd8efd7a1be052d9df2cb744bd7ff9e21e97a139b8ad21bf9a2083f91521ac7840745706b6072f06ba0c6c685b7de942b1f3683ba7059b737bab573d4d327445665a2e4a7e84558a2ad6636b540134dd017dbd49f8dc6ddb812869bd4d22bba40b4b4f48fa12f0be9305641a78a60f13e9b86d1a815e3b5cc9f247b7b1d9f20722ebbde39b0e7c51b6786d5b07442b334319f5bc413dd9493c3fd53785df531c0ac69e49b0e64830249e51ad68f98c66a863a7b630a7b8048e98132d3e01e9f8943ee7fd73b4895087d467c943b541c2e632c23179ce138277ec3772f68da0c768fb49cc4")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector0'

function vector1()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("d544ba70202f0e6da6a851259deea9aeb4782adf114c2a1a4d60160168ef1269"), sessionId: fromHex("d79022401687b7d8805659aaa1a5a2078dcfa743d18255b0f110bbe9e692d2c9"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("26a8cba2774efdc69b995e3ad139dd45149012d2f276dff0551621a9c9f13f59")), secret(23, privateKey: fromHex("daddf2cf527608749614517617c0cd92d414f2de58c8b2ed9eeec0620dd0379b"))], pssSalt: fromHex("19697172eee954f8594a202dbc2178baa3a92a6c9e5ce1307dbbe212c375a6dfdb1aa3d8f373ac5da810ba1860399809bd4693430f49c4297d8ccdbcc4da2162"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-finished-does-not-end-its-record", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303ea6d3c9b1fe2fa0f38b306aad1c1b16a12d99d8433b511ce4c68c2400a79507a20d79022401687b7d8805659aaa1a5a2078dcfa743d18255b0f110bbe9e692d2c9130300002e00330024001d002093f94e1785039dcd03fd21b79ceddae14c26773be0003144c158d8a4f881f869002b00020304"), fromHex("140303000101"), fromHex("170303020d5d237caaee7c1be96b6d09046af5a1370fd837b2f9c2de89ab13f6db37433d63cc88c1b030e5e1a4c1023addce659a733b7b91739a02eebd9dfe1c291438269b7063cb5f508e373af21056e809c36ce4a13b0d0112014d41053a19ff5617ec86b8d9c60644013f66100b2dd7e48dac9054037f39b9909fc7d4f66f9433379d168e8388b83ec218b709456da796d463c551172d39295164a50df58a6c6127e62e8979b32959a8de716eb5d992637f3ee039bf69646e39dd107dc47d7011065142b381702067cea9d3099ba7ba0cfdbc4de4fba75ce5858bd063e80da248f382c4ca3878f91c595d75cf5ceac7299ae464bc90becca315194ad4c4ec2ead927c823d51a5e1deebbe41f084fa7c8c46682c1022f568de596eb80217fec4bf682463f665fbd81884cbf0a6d4f4a9b3c029f653e2cf66eca7b1758cac788238d0d2e52733d2af6a63ade34ca1593f4f4b1a997ae181d85fd3888cd8aa114b6b4a3117dae3160dcf93770dc07c9d39f16ac4f7ac6fb8297394426de50bf51cf7bfc3d38b6c2f18235aac46f45d923e096961937e6a967e91e445298b52f56240c580253708194c7351ed0e6a18ad7bf955223b0000e69d61ee421517cb6e9ca6edd0d08afefdd1467da6d1c3c40942bc61d389f19f5b48596c97921b3270af60df822a5c1e93a95ee3356af07b8d78c8574a6e558330052a5c1c0a25fe76894e72f07dee31adde4ecab964c61baf24cc")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector1'

function vector2()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("f3ab1166cfcdf24a2ef871fee8e873b0b51ea259def7de26d52c223e14d1aa63"), sessionId: fromHex("654650a9e3e2a2b6d61af18128a2cdb22366896ac02a1fbfcb19df46ab07ec18"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("cf6b8c551714deff574fcd79ac0d1d6d087edbe22a9c826f6985bbfb7b3173e8")), secret(23, privateKey: fromHex("9ef1140584a5cc8cbabc7f2a40be221103c21074b3b291631f6f8bd3c0bf4596"))], pssSalt: fromHex("752a5a58139264f1cbac1ea8667e653cfd0ce626ff8a631ca84f8f876bcca2f4f62c14cf0f758e313d384341bfa454d2b7dc6d957fda8b9910730a6efddd9f18"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-fatal-alert-after-the-handshake", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303ddf5fa276da03d92c82fc74b9c8a700aba37dcf6f42fb7767d846a12956d4f6720654650a9e3e2a2b6d61af18128a2cdb22366896ac02a1fbfcb19df46ab07ec18130300002e00330024001d00201eb29ba2b840e978ec07acb14d8b760fae073d08951f81779a9aaa047cdfbd66002b00020304"), fromHex("140303000101"), fromHex("1703030208837d145151dabf18b26d89418e421d9f8d070e3687c79069f4dd38d6b11bc8095f0d251a295151197fdb82b5e99228ab28185ba97568384d432200a4c3311e9c0a3c0476c70b199b176b7d8065433186c859b7e33a3bb6c2c9f223ec037e3f86ca1b9ebab10de12efc1d2caf93624fbc22021f311a912fa616bf55abfb68230751ce8204c6e1d04fdb2b906cf9c514fadbd2d8ddbbd68ab35104801f73d0ec35ce2e9be362d38a9caa079b6c43af6089e2b108b21f89496004ffdff6adf072f88a803e14ac22ff72b65f8fac0ea8e4aa4d35bef02a15b11a572c9e3eb44b2bfe62b52e5ae92ab34522ddbb0fd7eff19864065428aaa9fec412d212e424521ef5585b80e83544f8d7da754326f4c68c677378ed9cc82877b598c5d093d77881909b8b05b5fe42e11506713a0c117d0ddcc09ad32e149403372e78bedd19ecbaee0327e9e8f1c2b10202fb1880b1afbcbef5c744c79117213c751bedbc80402d6cd93e7d952bf72685dfe2a3e82b8fa1f2078c96b5bd3005f06605f5acbdb51fde103c3688a974362732042b69d4dc3b0e5c834209a0201dbaf03b84fd7642e6db3127137fb475cb45deba6bde825aeec0b5492cf864c55b7447e2e647fb65f9b2d45118f628a2c1e8f63431ce66e0f5abf6933d742b57a2c2dfa6c622125ce4397477a2e515b7e173f34e56846e53a51abca104d103816a9eb719e11d71b84439442948bf64648049"), fromHex("1703030013b1cf7231c38940ee4ecf75bab31e41db294059")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector2'

function vector3()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("ec7b33c43958258d41e6424319b64a2e36a197bb7c9e95273f3b11ed033fef39"), sessionId: fromHex("083d47e23f227a1a711f4a62d3ebe4fe95cf826f11ca10af1fe8b86f71f1c92b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("d338a290bc091ef80a1cd6d36257aef84e44039c12bd1c83b3e30bef0cb60fb6")), secret(23, privateKey: fromHex("18f5a6e8a6d126be557a9bf3326094cd84a6e59e1c4bfafe254623e3f3dcd1dd"))], pssSalt: fromHex("37d932c38863dc83d0b35b5a1308a1955e277fa30d1789495d32663fdb434f1a4f6e618b1c0d907429f07dd28cc4df641c5c13aed6378f4b43c894eb86b6201e"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-change-cipher-spec-after-the-handshake", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303968d8a32a92757ea9815ca9917eb0fdf667f11fa48c155a698c5f3ddca381c4420083d47e23f227a1a711f4a62d3ebe4fe95cf826f11ca10af1fe8b86f71f1c92b130300002e00330024001d002054d04a05141627da9212f8c993100c21554bf60de780f0aa90e5ad8b4354254e002b00020304"), fromHex("140303000101"), fromHex("17030302083a189452121b4d0a87e4d58a41769ebfcf87295480a9ad56d603d6a1105909c627aa0ec70b042256458358a1e182c2b5eb9d2425400e77f0b9ff604365b14e91895b0e18e05306ebf70f10da3d45bf86e55083fd0d0a2b01156698a259abbaaf97bb67ab01d7eea060e83a90222fe1b828129acc2a06d03e60f2633140a937a85fa7d23fe14d84517849987d964d4e232825ff7af7a3ee5d239cf0cf1851bf354602c040132f2e801feb2880ba23ee4987d29c8d6272d56a3d9234a2e62cfe186719f8c34cf9fd9e819ec6bae3e62ddc1cbba1247262e1bc5770202df7003e49a448418cac7d699dc9f7ca6e42624fd35a19830b7937cbf24a40d329a7a2e9edea07d62953b0cdf8f4057f1c4a9a1175463c2bd721e2766daa906da4b10ebe351498131db5e2d0163b1334a8411f9fb04b5298b93fff858cace1ce580078dd2c386f911adf4bc1965542f5ba0c0b124eaa5298edff24518c86cdded8d4c8b7213624380a1ad54e656f16e897a8101e260c30f19b46028e52fbede8642a0155557ec63876a8a8df8e4ada4ad89462dbf01cba1eefcd27075000262b67980a5519d46a2b9491406c6a64c48727fe0028279657ea6e8ca9e82c8f04f026aae18221b5617172f87bf32919dc3956be350da7ada5706f44dd9d3c939d6d90fec5b056d73198e121bf9f128b769baa66b9385e96b2ebee6491e749af9911e2b239d273ba992adb836642c8"), fromHex("140303000101")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector3'

function vector4()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("fa4b555dd9fac265b6fa5b5b870e694398b973ca0cbbde77a1a3b158b0be25d3"), sessionId: fromHex("32892e92ff961be126752d20df5125e9c63e4469b448134ef415851607349a24"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("7d45760a2961464b24dbb14cb59366bbdb800d30fda9048531d935ebba955a20")), secret(23, privateKey: fromHex("62e4f943252dc22a75e4ae0b0b1b09d6e713c584427e07dc79446153f65e19eb"))], pssSalt: fromHex("c18f96c4b08fc34b489a51b213bcc3d9fd8b6eb97e2b593a8f2473f6bd8f97a96122863cb2584d28755936dcf28101b0eee03ce80f6e76056a049cf00e8aad02"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-key-update-does-not-end-its-record", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a020000760303b362c1468e8c08474112326e7dbe0850620d35f0973957467f60c558f44a6c4f2032892e92ff961be126752d20df5125e9c63e4469b448134ef415851607349a24130300002e00330024001d0020183af25358d7df2637e5df8acaeb192ec85d8b5166530f37041cf5e3f41bbc00002b00020304"), fromHex("140303000101"), fromHex("17030302062dc7bdfa29157c1fe6c0b935a21d2148976f8196c04222faab511f556be1fb6650c50a4e38cb04caa89bc52ce059e8eabe58d176a80abea294793c682dab797e991dee31fa026b9fe91c1c98e9a487a180eb935d92da8714f80ab007b92037d53566f7a891432188f4a1b0f045c7ac54a6215213e1676522bc0ec8aa2b0e90866334ae3836eab0c08866ec9161d1d24e60c859bc017e4eb605d5ca9768ad3f8311a487aae63d9ef5b82d58c0c79f16701f4bd6b4c0281d48bf1b3217fdd3c811ca72ab11ceaa2e593ee9fcc2cbdecffb85be841c53e897323276667b8b4c94a2b6477c47fa52ce9a6a728234fcc4fa0cdabbfc5360d03e1c3daa0bd2f698be6991b7d5bc1a080d368e20ae85912b66dc3ac31a5886f28f3268d360bd305651da2c164c189d347adb1a11b9c3fb2dfac275d273194440f7d1d0c85ada2be376718fbef67da330263aded6e0460dbbec8a57ea2e2b17bec350d22601d492746e972e7e3394741bea8c179bf47c8c4ce7e82c46d874c716d3f8a57fcfed296ec4594056ba98f6d6718d46389a8c4a7cad5376e600d5d9ab4c35d12d54c1559b30de9244e44ecec355d308b4f3733d7a0444995ff2bea79d0d5d532747ea2b98bd9ec17af57e65296fc08566724dc93657751b6466a1224490544c1fac3a13858539633096bc996ab28c2a7b2827c182ed1a9b0c7bb941af67c5f6c41c5a397fb25f1ab9992eb1fa"), fromHex("170303001b6d55a3eb8cd48c8d78ef0c9f4784781fe22e32b51cd800d9177367")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
end 'vector4'

function vector5()
	let config = TlsClientConfig.create(X509VerifyHost.named("example.com"), protocols: TlsProtocolArray from [TlsProtocol.tls13, TlsProtocol.tls12], cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], groups: TlsGroupCodeArray from [29, 23], keyShareGroups: TlsGroupCodeArray from [29], signatureAlgorithms: TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537], applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: TlsClientIdentity.anonymous)
	let entropy = TlsClientEntropy.create(fromHex("cb82a0e3b7f22ad7f6da17d11e73d51a733d8c3bef09261de580a3d43ecf490a"), sessionId: fromHex("35650eeaa9532a367da5e118ad6bbc7d0dee22fa3dd52132e27ace80bd80c62b"), keySecrets: TlsKeySecretArray from [secret(29, privateKey: fromHex("98ab3adf86249c2aecd07a0ff9875d5cd90dfa7951920463134b8dd5d97e0373")), secret(23, privateKey: fromHex("ed1e9731aa4c78379f88558dfe4a4fac36c03b6d76e4e4646996b3cbb41e3185"))], pssSalt: fromHex("71dcf3e870be4d8e17d7a998adb70785c9c7a7ad4a4be45f038fddfc1682c68f77a11db82077b2a3f56e7da047d47e30090ccb2a55726cace2479c64d62efdde"))
	let peer = TlsX509Peer.create(ByteArrays from [fromHex("3082012f3081d6a003020102020101300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3234303130313030303030305a170d3334303130313030303030305a300f310d300b06035504030c04526f6f743059301306072a8648ce3d020106082a8648ce3d0301070342000469fd1970f970c21034e836c954aefc78dffef4a3d72650f1ec5e70559cc0663d0722d3408f27d8be5a08ceb432c37b7049cc8758c88c3d85e4db83aa471b62b7a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff040403020106300a06082a8648ce3d0403020348003045022053bac2d2f62299dd459d79bce22d671aeeb6ee3ed03a2f344be8a8392fd47b3e022100d8f59bc3b1ba00dc51028c7f1b8e99d86d4bb4126e22717c32988498c07f64d7")], now: 1767225600, purpose: X509Purpose.serverAuth)
	let helloSource = TlsBuiltClientHello.create()
	report("reference13-err-handshake-record-in-plaintext-after-the-keys", config: config, entropy: entropy, peer: peer, helloSource: helloSource, inbound: ByteArrays from [fromHex("160303007a02000076030376c4e69b9a0916d99529d7503f40b8b2ffc982e889e7366c8c75688504ed06222035650eeaa9532a367da5e118ad6bbc7d0dee22fa3dd52132e27ace80bd80c62b130300002e00330024001d0020d40985833d94983ebc58b432cc3c4ff3f6dc3ba2e2f6bc86f9ac33dc0d063f53002b00020304"), fromHex("160303000414000000")], writes: ByteArrays from [fromHex("474554202f20485454502f312e310d0a486f73743a206578616d706c652e636f6d0d0a0d0a")], closeNotify: true)
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
reference13-err-certificate-entry-extension-not-offered unsupportedExtension
reference13-err-finished-does-not-end-its-record unexpectedMessage
reference13-err-fatal-alert-after-the-handshake alertReceived
reference13-err-change-cipher-spec-after-the-handshake unexpectedMessage
reference13-err-key-update-does-not-end-its-record unexpectedMessage
reference13-err-handshake-record-in-plaintext-after-the-keys unexpectedMessage
```

<!-- test: tls-handshake.the-client-hello-the-config-builds -->
<!-- tier: stdlib -->
The ClientHello a `TlsClientConfig` builds: server_name only for a DNS name, with one trailing dot removed, never for an IP address or no name; the TLS 1.2 extensions (extended_master_secret, renegotiation_info, ec_point_formats) only when TLS 1.2 is offered; key_share and supported_versions only when TLS 1.3 is; ALPN only when protocols are given. A line prints the extension types in order, the host name sent and the versions offered.
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

function typesOf(hello TlsClientHello) returns String
	var texts = TextArray.create()

	for item in tlsExtensionsOf(hello.extensions()) 'eachExtension'
		texts.push("{item.extensionType()}")
	end 'eachExtension'

	return joined(texts)
end 'typesOf'

function nameOf(hello TlsClientHello) returns String
	var name = "-"

	for item in tlsExtensionsOf(hello.extensions()) 'eachExtension'
		match item 'form'
			serverName(hostName) then name = hostName
			serverNameAcknowledgement or
			supportedGroups or
			ecPointFormats or
			signatureAlgorithms or
			applicationProtocols or
			extendedMasterSecret or
			supportedVersions or
			selectedVersion or
			cookie or
			clientKeyShares or
			serverKeyShare or
			selectedGroup or
			renegotiationInfo or
			opaque then continue
		end 'form'
	end 'eachExtension'

	return name
end 'nameOf'

function versionsOf(hello TlsClientHello) returns String
	let scan = TlsExtensionScan.ofBlock(hello.extensions())
	var texts = TextArray.create()

	for version in scan.supportedVersions 'eachVersion'
		texts.push("{version}")
	end 'eachVersion'

	return joined(texts)
end 'versionsOf'

function describe(label String, config TlsClientConfig)
	let keys = TlsKeySecretArray from [secret(29, privateKey: fromHex("e0b3d18c16536c6d5069477250866abf103e9ca3078ae756aaa3afa418cea303")), secret(23, privateKey: fromHex("fda9992b5f9462ace06c78ec3e99130cc3dbfa4a93de5f94d8a11bef9a34112b"))]
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: keys, pssSalt: ByteArray.create())
	let built = try tlsBuildClientHello(config, entropy: entropy) otherwise panic("the ClientHello was refused")
	let message = try TlsHandshakeCodec.decode(built, protocol: TlsProtocol.tls13) otherwise panic("the ClientHello does not decode")

	match message 'message'
		clientHello(hello) then print("{label} types={typesOf(hello)} name={nameOf(hello)} versions={versionsOf(hello)}\n")
		default panic("the built message is not a ClientHello")
	end 'message'
end 'describe'

function secret(group TlsGroupCode, privateKey ByteArray) returns TlsKeySecret
	return try TlsKeySecret.create(group, privateKey: privateKey) otherwise panic("a private key was refused")
end 'secret'

function named(name String) returns TlsClientConfig
	return TlsClientConfig.defaults(X509VerifyHost.named(name))
end 'named'

function onlyTls13() returns TlsClientConfig
	let defaults = named("example.com")

	return TlsClientConfig.create(defaults.peerName, protocols: TlsProtocolArray from [TlsProtocol.tls13], cipherSuites: defaults.cipherSuites, groups: defaults.groups, keyShareGroups: defaults.keyShareGroups, signatureAlgorithms: defaults.signatureAlgorithms, applicationProtocols: defaults.applicationProtocols, identity: defaults.identity)
end 'onlyTls13'

function onlyTls12() returns TlsClientConfig
	let defaults = named("example.com")

	return TlsClientConfig.create(defaults.peerName, protocols: TlsProtocolArray from [TlsProtocol.tls12], cipherSuites: defaults.cipherSuites, groups: defaults.groups, keyShareGroups: defaults.keyShareGroups, signatureAlgorithms: defaults.signatureAlgorithms, applicationProtocols: defaults.applicationProtocols, identity: defaults.identity)
end 'onlyTls12'

function withProtocol() returns TlsClientConfig
	let defaults = named("example.com")

	return TlsClientConfig.create(defaults.peerName, protocols: defaults.protocols, cipherSuites: defaults.cipherSuites, groups: defaults.groups, keyShareGroups: defaults.keyShareGroups, signatureAlgorithms: defaults.signatureAlgorithms, applicationProtocols: TlsProtocolNameArray from [fromHex("687474702f312e31")], identity: defaults.identity)
end 'withProtocol'

function main() returns ExitCode
	describe("dns-name", config: named("example.com"))
	describe("dns-name-with-alpn", config: withProtocol())
	describe("trailing-dot", config: named("example.com."))
	describe("ipv4-address", config: named("192.0.2.1"))
	describe("ipv6-address", config: named("2001:db8::1"))
	describe("no-name", config: TlsClientConfig.defaults(X509VerifyHost.none))
	describe("tls13-only", config: onlyTls13())
	describe("tls12-only", config: onlyTls12())

	return 0
end 'main'
```
```exitcode
0
```
```stdout
dns-name types=[0,23,65281,10,11,13,51,43] name=example.com versions=[772,771]
dns-name-with-alpn types=[0,23,65281,10,11,13,16,51,43] name=example.com versions=[772,771]
trailing-dot types=[0,23,65281,10,11,13,51,43] name=example.com versions=[772,771]
ipv4-address types=[23,65281,10,11,13,51,43] name=- versions=[772,771]
ipv6-address types=[23,65281,10,11,13,51,43] name=- versions=[772,771]
no-name types=[23,65281,10,11,13,51,43] name=- versions=[772,771]
tls13-only types=[0,10,13,51,43] name=example.com versions=[772]
tls12-only types=[0,23,65281,10,11,13] name=example.com versions=[]
```

<!-- test: tls-handshake.production-entropy-is-fresh-and-consistent -->
<!-- tier: stdlib -->
`TlsClientEntropy.fresh` draws a 32-byte random, a 32-byte session id, a 64-byte PSS salt and one private key per group the config lists, 32 bytes each, with the public key the group derives from it; two draws share nothing; a handshake started from it sends one record, a plaintext handshake record with the legacy record version 0x0301 whose ClientHello carries that random and session id and the nine default suites.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function prefixHex(bytes ByteArray, count Count) returns String
	return toHex(try bytes.slice(0, endIndex: count) otherwise panic("the record is shorter than its prefix"))
end 'prefixHex'

function secretOf(entropy TlsClientEntropy, group TlsGroupCode) returns TlsKeySecret
	return try entropy.secret(group) otherwise panic("no secret for the group")
end 'secretOf'

function main() returns ExitCode
	let config = TlsClientConfig.defaults(X509VerifyHost.named("example.com"))
	var first = try TlsClientEntropy.fresh(config) otherwise return 1
	let second = try TlsClientEntropy.fresh(config) otherwise return 1

	print("random={first.random.count()} session={first.sessionId.count()} salt={first.pssSalt.count()} secrets={first.keySecrets.count()}\n")

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
	print("session-differs={not first.sessionId.equals(second.sessionId)}\n")
	print("x25519-private-differs={not x25519First.privateKey.equals(x25519Second.privateKey)}\n")
	print("p256-private-differs={not p256First.privateKey.equals(p256Second.privateKey)}\n")
	print("x25519-public-matches={derivedX25519.equals(x25519First.publicKey)}\n")
	print("p256-public-matches={derivedP256.equals(p256First.publicKey)}\n")

	let peer = TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.serverAuth)
	var client = try TlsClientHandshake.create(config, entropy: first, peer: peer, helloSource: TlsBuiltClientHello.create()) otherwise return 3
	let records = client.session().takeRecords()
	let record = try records.get(0) otherwise return 4
	let framed = readOrderedWord(record, off: 3, width: 2, limit: record.count(), order: ByteOrder.bigEndian) == (record.count() - 5) as ByteWord
	let body = try record.slice(5, endIndex: record.count()) otherwise return 5
	let message = try TlsHandshakeCodec.decode(body, protocol: TlsProtocol.tls13) otherwise return 6

	print("hello-records={records.count()} prefix={prefixHex(record, count: 3)} framed={framed}\n")

	match message 'message'
		clientHello(hello) then print("hello-random-matches={hello.random().equals(first.random)} session-matches={hello.legacySessionId().equals(first.sessionId)} suites={hello.cipherSuites().count()}\n")
		default panic("the first record does not hold a ClientHello")
	end 'message'

	print("connected={client.session().isConnected()} drained={client.session().takeRecords().count()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
random=32 session=32 salt=64 secrets=2
secret group=29 private=32 public=32
secret group=23 private=32 public=65
random-differs=true
session-differs=true
x25519-private-differs=true
p256-private-differs=true
x25519-public-matches=true
p256-public-matches=true
hello-records=1 prefix=160301 framed=true
hello-random-matches=true session-matches=true suites=9
connected=false drained=0
```

<!-- test: tls-handshake.a-failure-sends-its-alert-and-stops-the-client -->
<!-- tier: stdlib -->
A defect before any key exists sends the contract's fatal alert in a plaintext record (`15 03 03 00 02 02 <description>`) and stops the client: the same record again throws the same error and queues nothing. A fatal alert from the server, or a close_notify before the handshake completes, is `alertReceived` and sends nothing. `write` and `close` before the handshake completes are refused.
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

function started() returns TlsClientHandshake
	let config = TlsClientConfig.defaults(X509VerifyHost.named("example.com"))
	let entropy = try TlsClientEntropy.fresh(config) otherwise panic("no entropy")
	let peer = TlsX509Peer.create(ByteArrays.create(), now: 0, purpose: X509Purpose.serverAuth)
	var client = try TlsClientHandshake.create(config, entropy: entropy, peer: peer, helloSource: TlsBuiltClientHello.create()) otherwise panic("no client")
	_ = client.session().takeRecords()

	return client
end 'started'

function scenario(label String, record ByteArray)
	var client = started()

	try client.receive(record) otherwise (failure) 'refused'
		print("{label} {failure} alert={renderRecords(client.session().takeRecords())}\n")

		try client.receive(record) otherwise (again) 'repeated'
			print("{label} again {again} queued={client.session().takeRecords().count()}\n")
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
	var client = started()

	try client.session().write(fromHex("00")) otherwise (failure) 'writeRefused'
		print("write-before-connected {failure}\n")
	end 'writeRefused'

	try client.session().close() otherwise (failure) 'closeRefused'
		print("close-before-connected {failure}\n")
	end 'closeRefused'

	print("queued={client.session().takeRecords().count()}\n")

	return 0
end 'early'

function main() returns ExitCode
	scenario("fatal-alert-from-the-server", record: fromHex("15030300020228"))
	scenario("close-notify-before-the-handshake-completes", record: fromHex("15030300020100"))
	scenario("record-length-does-not-match", record: fromHex("160303000501"))
	scenario("alert-of-one-byte", record: fromHex("150303000100"))
	scenario("client-hello-from-the-server", record: fromHex("160303000401000000"))
	scenario("unknown-content-type", record: fromHex("630303000100"))
	scenario("application-data-before-keys", record: fromHex("170303000100"))
	scenario("empty-handshake-fragment", record: fromHex("1603030000"))
	scenario("change-cipher-spec-before-the-server-hello", record: fromHex("140303000101"))
	scenario("plaintext-longer-than-16384", record: oversized())

	return early()
end 'main'
```
```exitcode
0
```
```stdout
fatal-alert-from-the-server alertReceived alert=[]
fatal-alert-from-the-server again alertReceived queued=0
close-notify-before-the-handshake-completes alertReceived alert=[]
close-notify-before-the-handshake-completes again alertReceived queued=0
record-length-does-not-match decodeError alert=[15030300020232]
record-length-does-not-match again decodeError queued=0
alert-of-one-byte decodeError alert=[15030300020232]
alert-of-one-byte again decodeError queued=0
client-hello-from-the-server unexpectedMessage alert=[1503030002020a]
client-hello-from-the-server again unexpectedMessage queued=0
unknown-content-type unexpectedMessage alert=[1503030002020a]
unknown-content-type again unexpectedMessage queued=0
application-data-before-keys unexpectedMessage alert=[1503030002020a]
application-data-before-keys again unexpectedMessage queued=0
empty-handshake-fragment unexpectedMessage alert=[1503030002020a]
empty-handshake-fragment again unexpectedMessage queued=0
change-cipher-spec-before-the-server-hello unexpectedMessage alert=[1503030002020a]
change-cipher-spec-before-the-server-hello again unexpectedMessage queued=0
plaintext-longer-than-16384 recordOverflow alert=[15030300020216]
plaintext-longer-than-16384 again recordOverflow queued=0
write-before-connected unexpectedMessage
close-before-connected unexpectedMessage
queued=0
```

<!-- test: tls-handshake.an-alert-after-the-keys-are-installed-is-encrypted -->
<!-- tier: stdlib -->
A failure after the handshake completes sends its alert under the client's current application write keys. The RFC 8448 section 3 trace is replayed to the connected state, its application data record is forged, and the fatal bad_record_mac alert the client sends opens under `client_application_traffic_secret_0` of the same RFC as an alert record holding `02 14`.
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

function main() returns ExitCode
	let suite = Tls13CipherSuite.aes128GcmSha256
	let config = TlsClientConfig.defaults(X509VerifyHost.none)
	let secret = try TlsKeySecret.create(29, privateKey: fromHex("49af42ba7f7994852d713ef2784bcbcaa7911de26adc5642cb634540e7ea5005")) otherwise return 1
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: TlsKeySecretArray from [secret], pssSalt: ByteArray.create())
	let helloSource = TlsReplacedClientHello.create(fromHex("010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), retry: ByteArrays.create())
	var client = try TlsClientHandshake.create(config, entropy: entropy, peer: TlsUnauthenticatedPeer.create(), helloSource: helloSource) otherwise return 2

	_ = client.session().takeRecords()

	try client.receive(fromHex("160303005a020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304")) otherwise return 3
	try client.receive(fromHex("17030302a2d1ff334a56f5bff6594a07cc87b580233f500f45e489e7f33af35edf7869fcf40aa40aa2b8ea73f848a7ca07612ef9f945cb960b4068905123ea78b111b429ba9191cd05d2a389280f526134aadc7fc78c4b729df828b5ecf7b13bd9aefb0e57f271585b8ea9bb355c7c79020716cfb9b1183ef3ab20e37d57a6b9d7477609aee6e122a4cf51427325250c7d0e509289444c9b3a648f1d71035d2ed65b0e3cdd0cbae8bf2d0b227812cbb360987255cc744110c453baa4fcd610928d809810e4b7ed1a8fd991f06aa6248204797e36a6a73b70a2559c09ead686945ba246ab66e5edd8044b4c6de3fcf2a89441ac66272fd8fb330ef8190579b3684596c960bd596eea520a56a8d650f563aad27409960dca63d3e688611ea5e22f4415cf9538d51a200c27034272968a264ed6540c84838d89f72c24461aad6d26f59ecaba9acbbb317b66d902f4f292a36ac1b639c637ce343117b659622245317b49eeda0c6258f100d7d961ffb138647e92ea330faeea6dfa31c7a84dc3bd7e1b7a6c7178af36879018e3f252107f243d243dc7339d5684c8b0378bf30244da8c87c843f5e56eb4c5e8280a2b48052cf93b16499a66db7cca71e4599426f7d461e66f99882bd89fc50800becca62d6c74116dbd2972fda1fa80f85df881edbe5a37668936b335583b599186dc5c6918a396fa48a181d6b6fa4f9d62d513afbb992f2b992f67f8afe67f76913fa388cb5630c8ca01e0c65d11c66a1e2ac4c85977b7c7a6999bbf10dc35ae69f5515614636c0b9b68c19ed2e31c0b3b66763038ebba42f3b38edc0399f3a9f23faa63978c317fc9fa66a73f60f0504de93b5b845e275592c12335ee340bbc4fddd502784016e4b3be7ef04dda49f4b440a30cb5d2af939828fd4ae3794e44f94df5a631ede42c1719bfdabf0253fe5175be898e750edc53370d2b")) otherwise return 4

	print("connected={client.session().isConnected()} flight={client.session().takeRecords().count()}\n")

	try client.receive(fromHex("17030300de3a6b8f90414a97d6959c3487680de5134a2b240e6cffac116e95d41d6af8f6b580dcf3d11d63c758db289a015940252f55713e061dc13e078891a38efbcf5753ad8ef170ad3c7353d16d9da773b9ca7f2b9fa1b6c0d4a3d03f75e09c30ba1e62972ac46f75f7b981be63439b2999ce13064615139891d5e4c5b406f16e3fc181a77ca475840025db2f0a77f81b5ab05b94c01346755f69232c86519d86cbeeac87aac347d143f9605d64f650db4d023e70e952ca49fe5137121c74bc2697687e248746d6df353005f3bce18696129c8153556b3b6c6779b37bf15985684f")) otherwise return 5

	var forged = fromHex("17030300432e937e11ef4ac740e538ad36005fc4a46932fc3225d05f82aa1b36e30efaf97d90e6dffc602dcb501a59a8fcc49c4bf2e5f0a21c0047c2abf332540dd032e167c2955d")
	let last = forged.count() - 1
	let flipped = (try forged.get(last) otherwise return 6) xor 1
	try forged.set(last, value: flipped) otherwise return 7

	try client.receive(forged) otherwise (failure) 'refused'
		print("failure {failure}\n")

		let records = client.session().takeRecords()
		let alertRecord = try records.get(0) otherwise return 8
		let keys = try Tls13KeySchedule.trafficKeys(suite, trafficSecret: fromHex("9e40646ce79a7f9dc05af8889bce6552875afa0b06df0087f792ebb7c17504a5")) otherwise return 9
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
connected=true flight=1
failure badRecordMac
records=1 prefix=1703030013 type=21 plaintext=0214
```

<!-- test: tls-handshake.records-the-server-sends-after-the-handshake -->
<!-- tier: stdlib -->
Records the server sends once the handshake is done, sealed with the server application keys of RFC 8448 section 3. close_notify is recorded and no other alert is: a warning or fatal alert other than user_canceled is `alertReceived`, an alert of three bytes `decodeError`. A KeyUpdate that asks for nothing is accepted, in one record or split over two; a HelloRequest, a message type nobody defines and a NewSessionTicket that is cut short are `unexpectedMessage`, `unexpectedMessage` and `decodeError`; a partial handshake message followed by an alert is `unexpectedMessage`. A line prints the verdict and whether the peer closed.
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

typealias ContentTypeArray = Array with TlsContentType

function connected() returns TlsClientHandshake
	let config = TlsClientConfig.defaults(X509VerifyHost.none)
	let secret = try TlsKeySecret.create(29, privateKey: fromHex("49af42ba7f7994852d713ef2784bcbcaa7911de26adc5642cb634540e7ea5005")) otherwise panic("key")
	let entropy = TlsClientEntropy.create(fromHex("0000000000000000000000000000000000000000000000000000000000000000"), sessionId: ByteArray.create(), keySecrets: TlsKeySecretArray from [secret], pssSalt: ByteArray.create())
	let helloSource = TlsReplacedClientHello.create(fromHex("010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), retry: ByteArrays.create())
	var client = try TlsClientHandshake.create(config, entropy: entropy, peer: TlsUnauthenticatedPeer.create(), helloSource: helloSource) otherwise panic("client")

	try client.receive(fromHex("160303005a020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304")) otherwise panic("server hello")
	try client.receive(fromHex("17030302a2d1ff334a56f5bff6594a07cc87b580233f500f45e489e7f33af35edf7869fcf40aa40aa2b8ea73f848a7ca07612ef9f945cb960b4068905123ea78b111b429ba9191cd05d2a389280f526134aadc7fc78c4b729df828b5ecf7b13bd9aefb0e57f271585b8ea9bb355c7c79020716cfb9b1183ef3ab20e37d57a6b9d7477609aee6e122a4cf51427325250c7d0e509289444c9b3a648f1d71035d2ed65b0e3cdd0cbae8bf2d0b227812cbb360987255cc744110c453baa4fcd610928d809810e4b7ed1a8fd991f06aa6248204797e36a6a73b70a2559c09ead686945ba246ab66e5edd8044b4c6de3fcf2a89441ac66272fd8fb330ef8190579b3684596c960bd596eea520a56a8d650f563aad27409960dca63d3e688611ea5e22f4415cf9538d51a200c27034272968a264ed6540c84838d89f72c24461aad6d26f59ecaba9acbbb317b66d902f4f292a36ac1b639c637ce343117b659622245317b49eeda0c6258f100d7d961ffb138647e92ea330faeea6dfa31c7a84dc3bd7e1b7a6c7178af36879018e3f252107f243d243dc7339d5684c8b0378bf30244da8c87c843f5e56eb4c5e8280a2b48052cf93b16499a66db7cca71e4599426f7d461e66f99882bd89fc50800becca62d6c74116dbd2972fda1fa80f85df881edbe5a37668936b335583b599186dc5c6918a396fa48a181d6b6fa4f9d62d513afbb992f2b992f67f8afe67f76913fa388cb5630c8ca01e0c65d11c66a1e2ac4c85977b7c7a6999bbf10dc35ae69f5515614636c0b9b68c19ed2e31c0b3b66763038ebba42f3b38edc0399f3a9f23faa63978c317fc9fa66a73f60f0504de93b5b845e275592c12335ee340bbc4fddd502784016e4b3be7ef04dda49f4b440a30cb5d2af939828fd4ae3794e44f94df5a631ede42c1719bfdabf0253fe5175be898e750edc53370d2b")) otherwise panic("server flight")

	return client
end 'connected'

function serverWriter() returns Tls13RecordProtection
	let suite = Tls13CipherSuite.aes128GcmSha256
	let keys = try Tls13KeySchedule.trafficKeys(suite, trafficSecret: fromHex("a11af9f05531f856ad47116b45a950328204b4f44bfb6b3a4b4f1f3fcb631643")) otherwise panic("keys")

	return try Tls13RecordProtection.create(suite, key: keys.key(), iv: keys.iv()) otherwise panic("writer")
end 'serverWriter'

function scenario(label String, types ContentTypeArray, payloads ByteArrays)
	var client = connected()
	var writer = serverWriter()
	var verdict = "accepted"

	for (iter, payload) in payloads.withIterator() 'eachRecord'
		let contentType = try types.get(iter.index()) otherwise panic("a payload has a content type")
		let record = try writer.seal(contentType, plaintext: payload) otherwise panic("seal")

		try client.receive(record) otherwise (failure) 'refused'
			verdict = "{failure}"
			break
		end 'refused'
	end 'eachRecord'

	print("{label} {verdict} closed={client.session().hasPeerClosed()}\n")
end 'scenario'

function single(label String, contentType TlsContentType, payload String)
	scenario(label, types: ContentTypeArray from [contentType], payloads: ByteArrays from [fromHex(payload)])
end 'single'

function main() returns ExitCode
	single("close-notify", contentType: TlsContentType.alert, payload: "0100")
	single("user-canceled", contentType: TlsContentType.alert, payload: "015a")
	single("unknown-warning-alert", contentType: TlsContentType.alert, payload: "017f")
	single("fatal-alert", contentType: TlsContentType.alert, payload: "0228")
	single("alert-of-three-bytes", contentType: TlsContentType.alert, payload: "010000")
	single("hello-request", contentType: TlsContentType.handshake, payload: "00000000")
	single("unknown-handshake-type", contentType: TlsContentType.handshake, payload: "ff000000")
	single("malformed-new-session-ticket", contentType: TlsContentType.handshake, payload: "040000020000")
	single("key-update-asking-for-nothing", contentType: TlsContentType.handshake, payload: "1800000100")
	scenario("message-split-over-two-records", types: ContentTypeArray from [TlsContentType.handshake, TlsContentType.handshake], payloads: ByteArrays from [fromHex("18000001"), fromHex("00")])
	scenario("partial-message-then-alert", types: ContentTypeArray from [TlsContentType.handshake, TlsContentType.alert], payloads: ByteArrays from [fromHex("180000"), fromHex("0100")])

	return 0
end 'main'
```
```exitcode
0
```
```stdout
close-notify accepted closed=true
user-canceled accepted closed=false
unknown-warning-alert alertReceived closed=false
fatal-alert alertReceived closed=false
alert-of-three-bytes decodeError closed=false
hello-request unexpectedMessage closed=false
unknown-handshake-type unexpectedMessage closed=false
malformed-new-session-ticket decodeError closed=false
key-update-asking-for-nothing accepted closed=false
message-split-over-two-records accepted closed=false
partial-message-then-alert unexpectedMessage closed=false
```

<!-- test: tls-handshake.a-private-key-signs-for-each-scheme-that-fits-it -->
<!-- tier: stdlib -->
`TlsPrivateKey.sign` signs under every scheme that fits the key and refuses the others: the signature verifies under the certificate's key and not for another message (ECDSA P-256, Ed25519, and RSA under the three RSA-PSS and three PKCS #1 v1.5 schemes), a P-384 signature is the canra `ecdsa-sign-p384` vector's, a key and a scheme that do not fit are `internalError`, and an RSA-PSS salt shorter than the digest is `internalError`.
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

typealias SchemeArray = Array with TlsSignatureScheme

function exercise(label String, certificate ByteArray, key TlsPrivateKey, schemes SchemeArray, salt ByteArray)
	let parsed = try X509.parse(certificate) otherwise panic("the certificate does not parse")
	var publicKey = parsed.publicKey()
	let message = "content to sign".toByteArray()
	let other = "content to forge".toByteArray()

	for scheme in schemes 'eachScheme'
		let signature = try key.sign(scheme, message: message, salt: salt) otherwise panic("the key refused to sign")

		try scheme.requireSignature(publicKey, message: message, signature: signature) otherwise (failure) 'rejected'
			print("{label} {scheme.name} rejected {failure}\n")
		end 'rejected'

		try scheme.requireSignature(publicKey, message: other, signature: signature) otherwise (failure) 'forged'
			print("{label} {scheme.name} {scheme.code()} verifies, other message {failure}\n")
		end 'forged'
	end 'eachScheme'
end 'exercise'

function main() returns ExitCode
	let salt = fromHex("4487405e8e4edd434eb92c463868df0e496800e4ebc4ec903f93bcf2e86dd447301b9af23b8237672495d313086169681e401644d11649ae69a99b655e8d5cbf")

	exercise("p256", certificate: fromHex("3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"), key: TlsPrivateKey.ecP256(fromHex("fd7e8e00621840dac0226ea921c19c18e46a79d944d6a3308b3f64cf009d82da")), schemes: SchemeArray from [TlsSignatureScheme.ecdsaSecp256r1Sha256], salt: salt)
	exercise("ed25519", certificate: fromHex("3082013c3081e2a003020102020176300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c056361726f6c302a300506032b6570032100f004952b22a93a2274111343d8b740acb309a18255094b6ab57d90ad545bc0b4a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020349003046022100af38b2ee559ea1c36ad386fb553c43c108109c6c4ce1ab62dd7e2567987d1644022100a33596c2359214abef9ea098e3ad30ed3bc52d48c3a40d5b4ed558f069298dfd"), key: TlsPrivateKey.ed25519(fromHex("8408cd68c509089a76695eb5c519ff0966fad7246013da6e9619f3032f71e404")), schemes: SchemeArray from [TlsSignatureScheme.ed25519], salt: salt)
	exercise("rsa", certificate: fromHex("30820234308201daa003020102020175300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300e310c300a06035504030c03626f6230820122300d06092a864886f70d01010105000382010f003082010a0282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d0203010001a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d0403020348003045022035a866be9f42da07b71e1df23a9c60a0ea5620dd55388f5ddb71bb848ab6711e022100a1502d0bfb23d04f46a5d2187bdffc938b1aebe54b36b54622c0c58c154cf2ac"), key: TlsPrivateKey.rsa(fromHex("308204bd020100300d06092a864886f70d0101010500048204a7308204a30201000282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d02030100010282010045df65c5ca9a3fb1da331c19e500efd2d09618922e8fdf0738a28e12b8cdfc4c9965397375730abec9ff878995ea476eacb1a9fb369fa099302309a9e93458b010714db9a90eda99bf17758f15ff1d25557a891368f328e321afa5e5d6af7aaaa6423e8ce10afa5f09c42cae0321f3718bb5c835b19410ff62d640421338dd7ec628dc43300c23e36bb201c57474de8bd382a64ae081a8a81bf0ecce10169ab362fa3b3736633a2045041e393493bd44c7795c88a9e74a348f6b3da074469db759e29bd7054d2ac252f7efbf49b14c40dbd442abf51091580e0e21e2a01322840ac3f8d4634bccd79cfd41fdb615e07bd1cb858d6e76f3b9e94fd032a5ea290502818100e9d14e6b52e82ead2112a1ec53f521fb9f81384fcb6ad79948afab9b434345db1fe0759b6b7cf787091698d23ec129370cb35ff478c359a7582c5e13c23c15ed919b778fb91219b0c09e6e922f62021d26632c33d8206bd78717f606ec43669e37cdaee172360e2b4f08827a6fc43e4cee822784a20e224bd1b5935d0505c35f02818100c5f536aee5b2f848afc370d68fa2ac1503e4f8c3b85631bf9baaadf0c03055612634b819ee8ac36e217be9f35b6d02f3d206bae460e51eba2adef5a7c40ee3ebf8856a94da2f90488c15051e96593087e4d2e331fd490fce4cd8a8f8b20a3d5cfb865ce0d392f85351a2ddb3bc82d878943370e64b4b5dd8d90e72092b0bcbc302818100ddc23afb0555dab72f2fe9cd27cfcd9c45aa9c0aea25fa37923db2a77e42f6a51df597f5811bb3f429c579c9a5e533bed757e5010bec5342a918e35d201236c5ec1ca467f6a259254f7ab3ea07f08f8ab016cd1c9ab61d31fc19d8a8b63a32c08ab29453942bfa824ee20646336ccf9dcd743a20cc465576cd7dc6372a054e47028180008cbbc8248f28ad5aff99f29e977f0ed7e1ece6ee3e1f093f2f6c283d00655c4db4dbc606a98ffd604e6dc9e5391a46ff1051b88ca4563f821af867acddb5b8540a5acf6b03b2cce0c6b62aed93f5d48ac823256264af8c8d1e14ea0be4876318b7b8ce4f081b746fc79a0203c6013fe4797818cba0bbf3fc36540ebe75dbed028180607cc46d66a8c6c0599730342557c5fba4e2c5ce59cb965be8889fc3e1f3b40fda0f8d09a9ba65a55c3df93f1c7540493dfe0345f5060082417442c0f31259fe8923aa19020a143af648d916ac3f11f6449ad7471fc1ccdb6b8cfd95c71fc27a6113efe24a6884f0fb804cd75e94574785436e1593ee4511431b32cf15040e5a")), schemes: SchemeArray from [TlsSignatureScheme.rsaPssRsaeSha256, TlsSignatureScheme.rsaPssRsaeSha384, TlsSignatureScheme.rsaPssRsaeSha512, TlsSignatureScheme.rsaPkcs1Sha256, TlsSignatureScheme.rsaPkcs1Sha384, TlsSignatureScheme.rsaPkcs1Sha512], salt: salt)

	var p384 = TlsPrivateKey.ecP384(fromHex("6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5"))
	let signed = try p384.sign(TlsSignatureScheme.ecdsaSecp384r1Sha384, message: fromHex("73616d706c65"), salt: salt) otherwise return 1

	print("p384 sample matches {toHex(signed) == "306602310094edbb92a5ecb8aad4736e56c691916b3f88140666ce9fa73d64c4ea95ad133c81a648152e44acf96e36dd1e80fabe4602310099ef4aeb15f178cea1fe40db2603138f130e740a19624526203b6351d0a3a94fa329c145786e679e7b82c71a38628ac8"}\n")

	if let _ = try p384.sign(TlsSignatureScheme.ed25519, message: fromHex("00"), salt: salt) 'accepted'
		print("p384 key with ed25519 scheme signed\n")
	end 'accepted' else (failure) 'mismatch'
		print("p384 key with ed25519 scheme {failure}\n")
	end 'mismatch'

	if let _ = try p384.sign(TlsSignatureScheme.rsaPssRsaeSha256, message: fromHex("00"), salt: salt) 'accepted'
		print("p384 key with rsa scheme signed\n")
	end 'accepted' else (failure) 'rsaScheme'
		print("p384 key with rsa scheme {failure}\n")
	end 'rsaScheme'

	var rsaKey = TlsPrivateKey.rsa(fromHex("308204bd020100300d06092a864886f70d0101010500048204a7308204a30201000282010100b4ce089a881ca602e8f42fe171f946cd7480617120b3013becf99188e60f79e0231cd898bce1150d858988318af87bba9f146f468208fcf2119c2c672b3bce84337b91c630fcd0063e5639dcccd8b3cd491161826171e200c6240a48d7b6abea22d729a00853ced0cee0d799de7644580294af4de0dd96bb6064f28cc17e29a077668f9317dc31f691d41df9b9e1a472b98f6df35eaf208d6873b529efbf861c2da3631268fbeb639d95295767b62bd968a39ae1f8ccba5cf07ab1f6e064f91f25dbd829a92ea4472ad282a1d13acee1e4bdfc12eab1ba3dd2a0b7e96941533b5e1ac7fd625ca4144abde73096e9248e341d58626576e02fa2675e3dbf65265d02030100010282010045df65c5ca9a3fb1da331c19e500efd2d09618922e8fdf0738a28e12b8cdfc4c9965397375730abec9ff878995ea476eacb1a9fb369fa099302309a9e93458b010714db9a90eda99bf17758f15ff1d25557a891368f328e321afa5e5d6af7aaaa6423e8ce10afa5f09c42cae0321f3718bb5c835b19410ff62d640421338dd7ec628dc43300c23e36bb201c57474de8bd382a64ae081a8a81bf0ecce10169ab362fa3b3736633a2045041e393493bd44c7795c88a9e74a348f6b3da074469db759e29bd7054d2ac252f7efbf49b14c40dbd442abf51091580e0e21e2a01322840ac3f8d4634bccd79cfd41fdb615e07bd1cb858d6e76f3b9e94fd032a5ea290502818100e9d14e6b52e82ead2112a1ec53f521fb9f81384fcb6ad79948afab9b434345db1fe0759b6b7cf787091698d23ec129370cb35ff478c359a7582c5e13c23c15ed919b778fb91219b0c09e6e922f62021d26632c33d8206bd78717f606ec43669e37cdaee172360e2b4f08827a6fc43e4cee822784a20e224bd1b5935d0505c35f02818100c5f536aee5b2f848afc370d68fa2ac1503e4f8c3b85631bf9baaadf0c03055612634b819ee8ac36e217be9f35b6d02f3d206bae460e51eba2adef5a7c40ee3ebf8856a94da2f90488c15051e96593087e4d2e331fd490fce4cd8a8f8b20a3d5cfb865ce0d392f85351a2ddb3bc82d878943370e64b4b5dd8d90e72092b0bcbc302818100ddc23afb0555dab72f2fe9cd27cfcd9c45aa9c0aea25fa37923db2a77e42f6a51df597f5811bb3f429c579c9a5e533bed757e5010bec5342a918e35d201236c5ec1ca467f6a259254f7ab3ea07f08f8ab016cd1c9ab61d31fc19d8a8b63a32c08ab29453942bfa824ee20646336ccf9dcd743a20cc465576cd7dc6372a054e47028180008cbbc8248f28ad5aff99f29e977f0ed7e1ece6ee3e1f093f2f6c283d00655c4db4dbc606a98ffd604e6dc9e5391a46ff1051b88ca4563f821af867acddb5b8540a5acf6b03b2cce0c6b62aed93f5d48ac823256264af8c8d1e14ea0be4876318b7b8ce4f081b746fc79a0203c6013fe4797818cba0bbf3fc36540ebe75dbed028180607cc46d66a8c6c0599730342557c5fba4e2c5ce59cb965be8889fc3e1f3b40fda0f8d09a9ba65a55c3df93f1c7540493dfe0345f5060082417442c0f31259fe8923aa19020a143af648d916ac3f11f6449ad7471fc1ccdb6b8cfd95c71fc27a6113efe24a6884f0fb804cd75e94574785436e1593ee4511431b32cf15040e5a"))

	if let _ = try rsaKey.sign(TlsSignatureScheme.rsaPssRsaeSha512, message: fromHex("00"), salt: fromHex("0102")) 'accepted'
		print("rsa key with a 2 byte salt signed\n")
	end 'accepted' else (failure) 'shortSalt'
		print("rsa key with a 2 byte salt {failure}\n")
	end 'shortSalt'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
p256 ecdsaSecp256r1Sha256 1027 verifies, other message decryptError
ed25519 ed25519 2055 verifies, other message decryptError
rsa rsaPssRsaeSha256 2052 verifies, other message decryptError
rsa rsaPssRsaeSha384 2053 verifies, other message decryptError
rsa rsaPssRsaeSha512 2054 verifies, other message decryptError
rsa rsaPkcs1Sha256 1025 verifies, other message decryptError
rsa rsaPkcs1Sha384 1281 verifies, other message decryptError
rsa rsaPkcs1Sha512 1537 verifies, other message decryptError
p384 sample matches true
p384 key with ed25519 scheme internalError
p384 key with rsa scheme internalError
rsa key with a 2 byte salt internalError
```

<!-- test: tls-handshake.the-default-peer-trusts-the-operating-system-roots -->
<!-- tier: stdlib -->
`TlsX509Peer.system` verifies a chain against the roots `TrustStore.systemRoots` returns, at the wall clock's time, for the purpose it is given. The roots are the machine's own, so the case asserts that there are some.
```maxon
function main() returns ExitCode
	let peer = try TlsX509Peer.system(X509Purpose.serverAuth) otherwise return 1

	print("roots={peer.roots.count() > 0} now={peer.now > 0} purpose={peer.purpose.name} authenticates={peer.authenticates()}\n")

	return 0
end 'main'
```
```exitcode
0
```
```stdout
roots=true now=true purpose=serverAuth authenticates=true
```

<!-- test: tls-handshake.a-user-program-cannot-name-the-replay-seam -->
A program outside `stdlib/` cannot name `TlsClientReplay`, the test seam's driver: it is `module` code, and the replay seam is reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsClientReplay.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsClientReplay' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake.a-user-program-cannot-name-the-unauthenticated-peer -->
A program outside `stdlib/` cannot name `TlsUnauthenticatedPeer`, the peer that skips every check: it is `module` code, and the replay seam is reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsUnauthenticatedPeer.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsUnauthenticatedPeer' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake.a-user-program-cannot-name-the-replaced-client-hello -->
A program outside `stdlib/` cannot name `TlsReplacedClientHello`, the source that replaces the ClientHello: it is `module` code, and the replay seam is reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsReplacedClientHello.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsReplacedClientHello' is module-scoped and not visible from this directory
```

<!-- test: tls-handshake.a-user-program-cannot-name-the-client-handshake -->
A program outside `stdlib/` cannot name `TlsClientHandshake`, the handshake state machine: it is `module` code, and the replay seam is reached only by library-tier source.
```maxon
function main() returns ExitCode
	let seam = TlsClientHandshake.create()

	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:13: type 'TlsClientHandshake' is module-scoped and not visible from this directory
```
