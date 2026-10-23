---
feature: tls-handshake-codec
status: stable
keywords: [tls-handshake-codec, TlsHandshakeCodec, TlsHandshakeMessage, TlsExtension, TlsCodecError, TlsEncodeError, TlsAlert, handshake, client hello, server hello, rfc8446, rfc5246, rfc8448, tls, crypto]
category: crypto
---

# `TlsHandshakeCodec` — TLS handshake message encoding and strict decoding

## Documentation

`TlsHandshakeCodec.decode(message, protocol: p)` parses one complete handshake message (the four-byte header and the
body) of a `TlsProtocol`, `tls13` or `tls12`, into a `TlsHandshakeMessage`; `TlsHandshakeCodec.encode(message)` writes
one back, and `encode(decode(m)) == m` for every `m` that decodes. The two protocols differ in which message types exist
and in the layout of Certificate and CertificateRequest, so a message has a case per layout.

| `TlsHandshakeMessage` case | Message |
|---|---|
| `helloRequest`, `serverHelloDone` | TLS 1.2, empty |
| `clientHello(TlsClientHello)` | ClientHello |
| `serverHello(TlsServerHello)`, `helloRetryRequest(TlsServerHello)` | ServerHello; a ServerHello whose random is the HelloRetryRequest value is the second case |
| `newSessionTicket(TlsNewSessionTicket)` | TLS 1.3 NewSessionTicket |
| `encryptedExtensions(extensions)` | TLS 1.3 |
| `tls13Certificate(requestContext, entries:)`, `tls12Certificate(certificates)` | Certificate in each version's layout |
| `tls13CertificateRequest(requestContext, extensions:)`, `tls12CertificateRequest(Tls12CertificateRequest)` | CertificateRequest in each version's layout; the TLS 1.2 one carries certificate_types, signature algorithms and certificate authorities |
| `certificateVerify(scheme, signature:)`, `finished(verifyData)` | both versions |
| `keyUpdate(TlsKeyUpdateRequest)` | TLS 1.3 |
| `serverKeyExchange(Tls12ServerKeyExchange)`, `clientKeyExchange(publicKey)` | TLS 1.2 ECDHE with a named curve |

A `TlsExtension` is a decoded extension (`serverName`, `serverNameAcknowledgement`, `supportedGroups`, `ecPointFormats`,
`signatureAlgorithms`, `applicationProtocols`, `extendedMasterSecret`, `supportedVersions`, `selectedVersion`, `cookie`,
`clientKeyShares`, `serverKeyShare`, `selectedGroup`, `renegotiationInfo`) or `opaque(extensionType, data)`, which keeps
every other extension's bytes. A ClientHello or ServerHello holds a `TlsExtensionBlock`: `absent` when the message ends after
its fixed fields, or `listed(extensions)`.

`decode` throws a `TlsCodecError`: `decodeError` for a malformed message, `illegalParameter` for a well-formed one that is
forbidden (a duplicate extension, an extension outside its RFC 8446 section 4.2 placement, a KeyUpdate value above 1, a
ServerKeyExchange curve type other than named_curve, a ServerHello selecting a version other than TLS 1.3, a duplicate
key_share group), `unexpectedMessage` for a type the protocol lacks, and `missingExtension` for a TLS 1.3
CertificateRequest without signature_algorithms. `error.alert()` is the `TlsAlert` to send. The first failure in wire order
is the one reported, except the three checks that need the whole body. `encode` throws `TlsEncodeError.vectorTooLong` for
a field that does not fit its length prefix, and performs no other validation.

The vectors are the canra `tls-handshake-codec` contract: the handshake messages of RFC 8448, messages OpenSSL 3.0.13
produced, the reference client and server's messages, and the errors. A decode line prints the message as the canra JSON
object; an error line prints the `TlsCodecError` and the alert code point it names. Every decode success is also re-encoded
in the case and compared byte for byte. Every one of the 115 canra vectors has a case: the types leave none unspellable.

## Tests

<!-- test: tls-handshake-codec.decode-rfc8448 -->
`TlsHandshakeCodec.decode` of the 20 handshake messages printed in RFC 8448. A line prints the message as the canra JSON object.
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

function renderProtocolNames(names TlsProtocolNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderProtocolNames'

function renderAuthorities(names TlsAuthorityNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderAuthorities'

function renderSuites(codes TlsCipherSuiteCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSuites'

function renderGroups(codes TlsGroupCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderGroups'

function renderSchemes(codes TlsSignatureSchemeCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSchemes'

function renderVersions(codes TlsVersionCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderVersions'

function renderShare(share TlsKeyShare) returns String
	return "\{\"group\":{share.group()},\"key_exchange_hex\":\"{toHex(share.keyExchange())}\"\}"
end 'renderShare'

function renderShares(shares TlsKeyShareArray) returns String
	var texts = TextArray.create()

	for share in shares 'eachShare'
		texts.push(renderShare(share))
	end 'eachShare'

	return joined(texts)
end 'renderShares'

function renderExtension(item TlsExtension) returns String
	return match item 'form'
		serverName(hostName) gives "\{\"type\":0,\"host_name\":\"{hostName}\"\}"
		serverNameAcknowledgement gives "\{\"type\":0\}"
		supportedGroups(groups) gives "\{\"type\":10,\"named_group_list\":{renderGroups(groups)}\}"
		ecPointFormats(formats) gives "\{\"type\":11,\"ec_point_format_list_hex\":\"{toHex(formats)}\"\}"
		signatureAlgorithms(schemes) gives "\{\"type\":13,\"supported_signature_algorithms\":{renderSchemes(schemes)}\}"
		applicationProtocols(protocols) gives "\{\"type\":16,\"protocol_name_list_hex\":{renderProtocolNames(protocols)}\}"
		extendedMasterSecret gives "\{\"type\":23\}"
		supportedVersions(versions) gives "\{\"type\":43,\"versions\":{renderVersions(versions)}\}"
		selectedVersion(version) gives "\{\"type\":43,\"selected_version\":{version}\}"
		cookie(cookie) gives "\{\"type\":44,\"cookie_hex\":\"{toHex(cookie)}\"\}"
		clientKeyShares(shares) gives "\{\"type\":51,\"client_shares\":{renderShares(shares)}\}"
		serverKeyShare(share) gives "\{\"type\":51,\"server_share\":{renderShare(share)}\}"
		selectedGroup(group) gives "\{\"type\":51,\"selected_group\":{group}\}"
		renegotiationInfo(connection) gives "\{\"type\":65281,\"renegotiated_connection_hex\":\"{toHex(connection)}\"\}"
		opaque(extensionType, data) gives "\{\"type\":{extensionType},\"extension_data_hex\":\"{toHex(data)}\"\}"
	end 'form'
end 'renderExtension'

function renderExtensions(items TlsExtensionArray) returns String
	var texts = TextArray.create()

	for item in items 'eachExtension'
		texts.push(renderExtension(item))
	end 'eachExtension'

	return joined(texts)
end 'renderExtensions'

function renderBlock(block TlsExtensionBlock) returns String
	return match block 'block'
		absent gives "null"
		listed(items) gives renderExtensions(items)
	end 'block'
end 'renderBlock'

function renderEntries(entries TlsCertificateEntryArray) returns String
	var texts = TextArray.create()

	for entry in entries 'eachEntry'
		texts.push("\{\"cert_data_hex\":\"{toHex(entry.certificate())}\",\"extensions\":{renderExtensions(entry.extensions())}\}")
	end 'eachEntry'

	return joined(texts)
end 'renderEntries'

function renderCertificates(certificates TlsCertificateDataArray) returns String
	var texts = TextArray.create()

	for certificate in certificates 'eachCertificate'
		texts.push("\{\"cert_data_hex\":\"{toHex(certificate)}\"\}")
	end 'eachCertificate'

	return joined(texts)
end 'renderCertificates'

function renderServerHello(hello TlsServerHello) returns String
	return "\{\"msg_type\":2,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_echo_hex\":\"{toHex(hello.legacySessionIdEcho())}\",\"cipher_suite\":{hello.cipherSuite()},\"legacy_compression_method\":{hello.compressionMethod()},\"extensions\":{renderBlock(hello.extensions())}\}"
end 'renderServerHello'

function render(message TlsHandshakeMessage) returns String
	return match message 'message'
		helloRequest gives "\{\"msg_type\":0\}"
		clientHello(hello) gives "\{\"msg_type\":1,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_hex\":\"{toHex(hello.legacySessionId())}\",\"cipher_suites\":{renderSuites(hello.cipherSuites())},\"legacy_compression_methods_hex\":\"{toHex(hello.compressionMethods())}\",\"extensions\":{renderBlock(hello.extensions())}\}"
		serverHello(hello) gives renderServerHello(hello)
		helloRetryRequest(hello) gives renderServerHello(hello)
		newSessionTicket(ticket) gives "\{\"msg_type\":4,\"ticket_lifetime\":{ticket.lifetime()},\"ticket_age_add\":{ticket.ageAdd()},\"ticket_nonce_hex\":\"{toHex(ticket.nonce())}\",\"ticket_hex\":\"{toHex(ticket.ticket())}\",\"extensions\":{renderExtensions(ticket.extensions())}\}"
		encryptedExtensions(items) gives "\{\"msg_type\":8,\"extensions\":{renderExtensions(items)}\}"
		tls13Certificate(requestContext, entries) gives "\{\"msg_type\":11,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"certificate_list\":{renderEntries(entries)}\}"
		tls12Certificate(certificates) gives "\{\"msg_type\":11,\"certificate_list\":{renderCertificates(certificates)}\}"
		tls13CertificateRequest(requestContext, items) gives "\{\"msg_type\":13,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"extensions\":{renderExtensions(items)}\}"
		tls12CertificateRequest(request) gives "\{\"msg_type\":13,\"certificate_types_hex\":\"{toHex(request.certificateTypes())}\",\"supported_signature_algorithms\":{renderSchemes(request.signatureSchemes())},\"certificate_authorities_hex\":{renderAuthorities(request.authorities())}\}"
		certificateVerify(scheme, signature) gives "\{\"msg_type\":15,\"algorithm\":{scheme},\"signature_hex\":\"{toHex(signature)}\"\}"
		finished(verifyData) gives "\{\"msg_type\":20,\"verify_data_hex\":\"{toHex(verifyData)}\"\}"
		keyUpdate(request) gives "\{\"msg_type\":24,\"request_update\":{request.rawValue}\}"
		serverKeyExchange(exchange) gives "\{\"msg_type\":12,\"curve_type\":3,\"named_group\":{exchange.namedGroup()},\"public_hex\":\"{toHex(exchange.publicKey())}\",\"algorithm\":{exchange.signatureScheme()},\"signature_hex\":\"{toHex(exchange.signature())}\"\}"
		clientKeyExchange(publicKey) gives "\{\"msg_type\":16,\"public_hex\":\"{toHex(publicKey)}\"\}"
		serverHelloDone gives "\{\"msg_type\":14\}"
	end 'message'
end 'render'

function report(id String, protocol TlsProtocol, hex String)
	if let message = try TlsHandshakeCodec.decode(fromHex(hex), protocol: protocol) 'decoded'
		print("{id} {render(message)}\n")

		if let encoded = try TlsHandshakeCodec.encode(message) 'encoded'
			if toHex(encoded) != hex 'differs'
				print("{id} re-encoding differs\n")
			end 'differs'
		end 'encoded' else (encodeFailure) 'encodeRefused'
			print("{id} encode {encodeFailure}\n")
		end 'encodeRefused'
	end 'decoded' else (failure) 'refused'
		print("{id} decode {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-rfc8448-3-client-client-hello1", protocol: TlsProtocol.tls13, hex: "010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001")
	report("decode-rfc8448-3-server-server-hello1", protocol: TlsProtocol.tls13, hex: "020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304")
	report("decode-rfc8448-3-server-encrypted-extensions1", protocol: TlsProtocol.tls13, hex: "080000240022000a00140012001d00170018001901000101010201030104001c0002400100000000")
	report("decode-rfc8448-3-server-certificate1", protocol: TlsProtocol.tls13, hex: "0b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de10000")
	report("decode-rfc8448-3-server-certificate-verify1", protocol: TlsProtocol.tls13, hex: "0f000084080400805a747c5d88fa9bd2e55ab085a61015b7211f824cd484145ab3ff52f1fda8477b0b7abc90db78e2d33a5c141a078653fa6bef780c5ea248eeaaa785c4f394cab6d30bbe8d4859ee511f602957b15411ac027671459e46445c9ea58c181e818e95b8c3fb0bf3278409d3be152a3da5043e063dda65cdf5aea20d53dfacd42f74f3")
	report("decode-rfc8448-3-server-finished1", protocol: TlsProtocol.tls13, hex: "140000209b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718")
	report("decode-rfc8448-3-server-new-session-ticket1", protocol: TlsProtocol.tls13, hex: "040000c90000001efad6aac502000000b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d3570008002a000400000400")
	report("decode-rfc8448-5-client-client-hello1", protocol: TlsProtocol.tls13, hex: "010000b00303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010000810000000b0009000006736572766572ff01000100000a00080006001d00170018003300260024001d0020e8e8e3f3b93a25ed97a14a7dcacb8a272c6288e585c6484d05262fcad062ad1f002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001")
	report("decode-rfc8448-5-client-client-hello2", protocol: TlsProtocol.tls13, hex: "010001fc0303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00080006001d001700180033004700450017004104a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002d00020101001c00024001001500af00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
	report("decode-rfc8448-5-server-server-hello1", protocol: TlsProtocol.tls13, hex: "020000ac0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c001301000084003300020017002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002b00020304")
	report("decode-rfc8448-5-server-server-hello2", protocol: TlsProtocol.tls13, hex: "020000770303bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b9800130100004f003300450017004104583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d002b00020304")
	report("decode-rfc8448-5-server-encrypted-extensions1", protocol: TlsProtocol.tls13, hex: "080000180016000a0008000600170018001d001c0002400100000000")
	report("decode-rfc8448-6-server-certificate-request1", protocol: TlsProtocol.tls13, hex: "0d000027000024000d0020001e040305030603020308040805080604010501060102010402050206020202")
	report("decode-rfc8448-6-server-certificate1", protocol: TlsProtocol.tls13, hex: "0b00013b000001370001323082012e3081d5a003020102020107300a06082a8648ce3d04030230133111300f060355040313086563647361323536301e170d3136303733303031323430305a170d3236303733303031323430305a30133111300f0603550403130865636473613235363059301306072a8648ce3d020106082a8648ce3d0301070342000408d530161575f4cfe7f154ee3448180086001e88431a79ee62ee6e2f83ef38ba61e9fb37f34e007a7df4d2f5b56d1f04ece45d621f468406f5c3a15158948dd0a31a301830090603551d1304023000300b0603551d0f040403020780300a06082a8648ce3d0403020348003045022100df30fd4507f5edd22c1a6ff86db479ca693feeca3b71b3f9ef556b2937c0594d022062e2a47250d320fea83c7e2dcb5b76a50e0200c09adbd13fee946e513e011d110000")
	report("decode-rfc8448-6-server-certificate-verify1", protocol: TlsProtocol.tls13, hex: "0f00004b040300473045022100d7a4d34bd54f55fee1a89625678c3dd5e5f60dac73ec940c5c7b9304a02084a90220289f595ed488b9ac689a3d192b1a8bb38f34af7874c059c9806a1f38269353e8")
	report("decode-rfc8448-7-client-client-hello1", protocol: TlsProtocol.tls13, hex: "010000dc03034e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d0920a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef00061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d00208e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001")
	report("decode-rfc8448-7-server-server-hello1", protocol: TlsProtocol.tls13, hex: "020000760303e5dd5948c435f7a38f0f0130708dc322d9df09abd4838117c183a7bb6d994f2c20a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef130100002e00330024001d00203e30f0f4ba551afd62768341175f5265e4daf0c8841617aa4fafdd2142320c22002b00020304")
	report("decode-rfc8448-4-server-server-hello1", protocol: TlsProtocol.tls13, hex: "0200005c03033ccfd2dec890222763472ae8136777c9d7358777bb66e91ea5122495f559ea2d00130100003400290002000000330024001d0020121761ee42c333e1b9e77b60dd57c2053cd94512ab47f115e86eff50942cea31002b00020304")
	report("decode-rfc8448-4-server-encrypted-extensions1", protocol: TlsProtocol.tls13, hex: "080000280026000a00140012001d00170018001901000101010201030104001c0002400100000000002a0000")
	report("decode-rfc8448-4-client-client-hello-psk", protocol: TlsProtocol.tls13, hex: "010001fc03031bc3ceb6bbe39cff938355b5a50adb6db21b7a6af649d7b4bc419d7876487d95000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d0020e4ffb68ac05f8d96c99da26698346c6be16482badddafe051a66b4f18d668f0b002a0000002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c0002400100150057000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002900dd00b800b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d357fad6aacb0021203add4fb2d8fdf822a0ca3cf7678ef5e88dae990141c5924d57bb6fa31b9e5f9d")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-rfc8448-3-client-client-hello1 {"msg_type":1,"legacy_version":771,"random_hex":"cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7","legacy_session_id_hex":"","cipher_suites":[4865,4867,4866],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"server"},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23,24,25,256,257,258,259,260]},{"type":35,"extension_data_hex":""},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"99381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c"}]},{"type":43,"versions":[772]},{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]},{"type":45,"extension_data_hex":"0101"},{"type":28,"extension_data_hex":"4001"}]}
decode-rfc8448-3-server-server-hello1 {"msg_type":2,"legacy_version":771,"random_hex":"a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e26928","legacy_session_id_echo_hex":"","cipher_suite":4865,"legacy_compression_method":0,"extensions":[{"type":51,"server_share":{"group":29,"key_exchange_hex":"c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f"}},{"type":43,"selected_version":772}]}
decode-rfc8448-3-server-encrypted-extensions1 {"msg_type":8,"extensions":[{"type":10,"named_group_list":[29,23,24,25,256,257,258,259,260]},{"type":28,"extension_data_hex":"4001"},{"type":0}]}
decode-rfc8448-3-server-certificate1 {"msg_type":11,"certificate_request_context_hex":"","certificate_list":[{"cert_data_hex":"308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de1","extensions":[]}]}
decode-rfc8448-3-server-certificate-verify1 {"msg_type":15,"algorithm":2052,"signature_hex":"5a747c5d88fa9bd2e55ab085a61015b7211f824cd484145ab3ff52f1fda8477b0b7abc90db78e2d33a5c141a078653fa6bef780c5ea248eeaaa785c4f394cab6d30bbe8d4859ee511f602957b15411ac027671459e46445c9ea58c181e818e95b8c3fb0bf3278409d3be152a3da5043e063dda65cdf5aea20d53dfacd42f74f3"}
decode-rfc8448-3-server-finished1 {"msg_type":20,"verify_data_hex":"9b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718"}
decode-rfc8448-3-server-new-session-ticket1 {"msg_type":4,"ticket_lifetime":30,"ticket_age_add":4208372421,"ticket_nonce_hex":"0000","ticket_hex":"2c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d357","extensions":[{"type":42,"extension_data_hex":"00000400"}]}
decode-rfc8448-5-client-client-hello1 {"msg_type":1,"legacy_version":771,"random_hex":"b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f","legacy_session_id_hex":"","cipher_suites":[4865,4867,4866],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"server"},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23,24]},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"e8e8e3f3b93a25ed97a14a7dcacb8a272c6288e585c6484d05262fcad062ad1f"}]},{"type":43,"versions":[772]},{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]},{"type":45,"extension_data_hex":"0101"},{"type":28,"extension_data_hex":"4001"}]}
decode-rfc8448-5-client-client-hello2 {"msg_type":1,"legacy_version":771,"random_hex":"b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f","legacy_session_id_hex":"","cipher_suites":[4865,4867,4866],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"server"},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23,24]},{"type":51,"client_shares":[{"group":23,"key_exchange_hex":"04a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780"}]},{"type":43,"versions":[772]},{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]},{"type":44,"cookie_hex":"71dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0"},{"type":45,"extension_data_hex":"0101"},{"type":28,"extension_data_hex":"4001"},{"type":21,"extension_data_hex":"00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}]}
decode-rfc8448-5-server-server-hello1 {"msg_type":2,"legacy_version":771,"random_hex":"cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c","legacy_session_id_echo_hex":"","cipher_suite":4865,"legacy_compression_method":0,"extensions":[{"type":51,"selected_group":23},{"type":44,"cookie_hex":"71dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0"},{"type":43,"selected_version":772}]}
decode-rfc8448-5-server-server-hello2 {"msg_type":2,"legacy_version":771,"random_hex":"bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b98","legacy_session_id_echo_hex":"","cipher_suite":4865,"legacy_compression_method":0,"extensions":[{"type":51,"server_share":{"group":23,"key_exchange_hex":"04583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d"}},{"type":43,"selected_version":772}]}
decode-rfc8448-5-server-encrypted-extensions1 {"msg_type":8,"extensions":[{"type":10,"named_group_list":[23,24,29]},{"type":28,"extension_data_hex":"4001"},{"type":0}]}
decode-rfc8448-6-server-certificate-request1 {"msg_type":13,"certificate_request_context_hex":"","extensions":[{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]}]}
decode-rfc8448-6-server-certificate1 {"msg_type":11,"certificate_request_context_hex":"","certificate_list":[{"cert_data_hex":"3082012e3081d5a003020102020107300a06082a8648ce3d04030230133111300f060355040313086563647361323536301e170d3136303733303031323430305a170d3236303733303031323430305a30133111300f0603550403130865636473613235363059301306072a8648ce3d020106082a8648ce3d0301070342000408d530161575f4cfe7f154ee3448180086001e88431a79ee62ee6e2f83ef38ba61e9fb37f34e007a7df4d2f5b56d1f04ece45d621f468406f5c3a15158948dd0a31a301830090603551d1304023000300b0603551d0f040403020780300a06082a8648ce3d0403020348003045022100df30fd4507f5edd22c1a6ff86db479ca693feeca3b71b3f9ef556b2937c0594d022062e2a47250d320fea83c7e2dcb5b76a50e0200c09adbd13fee946e513e011d11","extensions":[]}]}
decode-rfc8448-6-server-certificate-verify1 {"msg_type":15,"algorithm":1027,"signature_hex":"3045022100d7a4d34bd54f55fee1a89625678c3dd5e5f60dac73ec940c5c7b9304a02084a90220289f595ed488b9ac689a3d192b1a8bb38f34af7874c059c9806a1f38269353e8"}
decode-rfc8448-7-client-client-hello1 {"msg_type":1,"legacy_version":771,"random_hex":"4e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d09","legacy_session_id_hex":"a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef","cipher_suites":[4865,4867,4866],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"server"},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23,24,25,256,257,258,259,260]},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"8e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63"}]},{"type":43,"versions":[772]},{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]},{"type":45,"extension_data_hex":"0101"},{"type":28,"extension_data_hex":"4001"}]}
decode-rfc8448-7-server-server-hello1 {"msg_type":2,"legacy_version":771,"random_hex":"e5dd5948c435f7a38f0f0130708dc322d9df09abd4838117c183a7bb6d994f2c","legacy_session_id_echo_hex":"a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef","cipher_suite":4865,"legacy_compression_method":0,"extensions":[{"type":51,"server_share":{"group":29,"key_exchange_hex":"3e30f0f4ba551afd62768341175f5265e4daf0c8841617aa4fafdd2142320c22"}},{"type":43,"selected_version":772}]}
decode-rfc8448-4-server-server-hello1 {"msg_type":2,"legacy_version":771,"random_hex":"3ccfd2dec890222763472ae8136777c9d7358777bb66e91ea5122495f559ea2d","legacy_session_id_echo_hex":"","cipher_suite":4865,"legacy_compression_method":0,"extensions":[{"type":41,"extension_data_hex":"0000"},{"type":51,"server_share":{"group":29,"key_exchange_hex":"121761ee42c333e1b9e77b60dd57c2053cd94512ab47f115e86eff50942cea31"}},{"type":43,"selected_version":772}]}
decode-rfc8448-4-server-encrypted-extensions1 {"msg_type":8,"extensions":[{"type":10,"named_group_list":[29,23,24,25,256,257,258,259,260]},{"type":28,"extension_data_hex":"4001"},{"type":0},{"type":42,"extension_data_hex":""}]}
decode-rfc8448-4-client-client-hello-psk {"msg_type":1,"legacy_version":771,"random_hex":"1bc3ceb6bbe39cff938355b5a50adb6db21b7a6af649d7b4bc419d7876487d95","legacy_session_id_hex":"","cipher_suites":[4865,4867,4866],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"server"},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23,24,25,256,257,258,259,260]},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"e4ffb68ac05f8d96c99da26698346c6be16482badddafe051a66b4f18d668f0b"}]},{"type":42,"extension_data_hex":""},{"type":43,"versions":[772]},{"type":13,"supported_signature_algorithms":[1027,1283,1539,515,2052,2053,2054,1025,1281,1537,513,1026,1282,1538,514]},{"type":45,"extension_data_hex":"0101"},{"type":28,"extension_data_hex":"4001"},{"type":21,"extension_data_hex":"000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"},{"type":41,"extension_data_hex":"00b800b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d357fad6aacb0021203add4fb2d8fdf822a0ca3cf7678ef5e88dae990141c5924d57bb6fa31b9e5f9d"}]}
```

<!-- test: tls-handshake-codec.decode-openssl-and-generated -->
`TlsHandshakeCodec.decode` of the messages OpenSSL 3.0.13 produced, the reference client's and server's messages and the short messages built by hand (29 vectors). A line prints the message as the canra JSON object.
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

function renderProtocolNames(names TlsProtocolNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderProtocolNames'

function renderAuthorities(names TlsAuthorityNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderAuthorities'

function renderSuites(codes TlsCipherSuiteCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSuites'

function renderGroups(codes TlsGroupCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderGroups'

function renderSchemes(codes TlsSignatureSchemeCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSchemes'

function renderVersions(codes TlsVersionCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderVersions'

function renderShare(share TlsKeyShare) returns String
	return "\{\"group\":{share.group()},\"key_exchange_hex\":\"{toHex(share.keyExchange())}\"\}"
end 'renderShare'

function renderShares(shares TlsKeyShareArray) returns String
	var texts = TextArray.create()

	for share in shares 'eachShare'
		texts.push(renderShare(share))
	end 'eachShare'

	return joined(texts)
end 'renderShares'

function renderExtension(item TlsExtension) returns String
	return match item 'form'
		serverName(hostName) gives "\{\"type\":0,\"host_name\":\"{hostName}\"\}"
		serverNameAcknowledgement gives "\{\"type\":0\}"
		supportedGroups(groups) gives "\{\"type\":10,\"named_group_list\":{renderGroups(groups)}\}"
		ecPointFormats(formats) gives "\{\"type\":11,\"ec_point_format_list_hex\":\"{toHex(formats)}\"\}"
		signatureAlgorithms(schemes) gives "\{\"type\":13,\"supported_signature_algorithms\":{renderSchemes(schemes)}\}"
		applicationProtocols(protocols) gives "\{\"type\":16,\"protocol_name_list_hex\":{renderProtocolNames(protocols)}\}"
		extendedMasterSecret gives "\{\"type\":23\}"
		supportedVersions(versions) gives "\{\"type\":43,\"versions\":{renderVersions(versions)}\}"
		selectedVersion(version) gives "\{\"type\":43,\"selected_version\":{version}\}"
		cookie(cookie) gives "\{\"type\":44,\"cookie_hex\":\"{toHex(cookie)}\"\}"
		clientKeyShares(shares) gives "\{\"type\":51,\"client_shares\":{renderShares(shares)}\}"
		serverKeyShare(share) gives "\{\"type\":51,\"server_share\":{renderShare(share)}\}"
		selectedGroup(group) gives "\{\"type\":51,\"selected_group\":{group}\}"
		renegotiationInfo(connection) gives "\{\"type\":65281,\"renegotiated_connection_hex\":\"{toHex(connection)}\"\}"
		opaque(extensionType, data) gives "\{\"type\":{extensionType},\"extension_data_hex\":\"{toHex(data)}\"\}"
	end 'form'
end 'renderExtension'

function renderExtensions(items TlsExtensionArray) returns String
	var texts = TextArray.create()

	for item in items 'eachExtension'
		texts.push(renderExtension(item))
	end 'eachExtension'

	return joined(texts)
end 'renderExtensions'

function renderBlock(block TlsExtensionBlock) returns String
	return match block 'block'
		absent gives "null"
		listed(items) gives renderExtensions(items)
	end 'block'
end 'renderBlock'

function renderEntries(entries TlsCertificateEntryArray) returns String
	var texts = TextArray.create()

	for entry in entries 'eachEntry'
		texts.push("\{\"cert_data_hex\":\"{toHex(entry.certificate())}\",\"extensions\":{renderExtensions(entry.extensions())}\}")
	end 'eachEntry'

	return joined(texts)
end 'renderEntries'

function renderCertificates(certificates TlsCertificateDataArray) returns String
	var texts = TextArray.create()

	for certificate in certificates 'eachCertificate'
		texts.push("\{\"cert_data_hex\":\"{toHex(certificate)}\"\}")
	end 'eachCertificate'

	return joined(texts)
end 'renderCertificates'

function renderServerHello(hello TlsServerHello) returns String
	return "\{\"msg_type\":2,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_echo_hex\":\"{toHex(hello.legacySessionIdEcho())}\",\"cipher_suite\":{hello.cipherSuite()},\"legacy_compression_method\":{hello.compressionMethod()},\"extensions\":{renderBlock(hello.extensions())}\}"
end 'renderServerHello'

function render(message TlsHandshakeMessage) returns String
	return match message 'message'
		helloRequest gives "\{\"msg_type\":0\}"
		clientHello(hello) gives "\{\"msg_type\":1,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_hex\":\"{toHex(hello.legacySessionId())}\",\"cipher_suites\":{renderSuites(hello.cipherSuites())},\"legacy_compression_methods_hex\":\"{toHex(hello.compressionMethods())}\",\"extensions\":{renderBlock(hello.extensions())}\}"
		serverHello(hello) gives renderServerHello(hello)
		helloRetryRequest(hello) gives renderServerHello(hello)
		newSessionTicket(ticket) gives "\{\"msg_type\":4,\"ticket_lifetime\":{ticket.lifetime()},\"ticket_age_add\":{ticket.ageAdd()},\"ticket_nonce_hex\":\"{toHex(ticket.nonce())}\",\"ticket_hex\":\"{toHex(ticket.ticket())}\",\"extensions\":{renderExtensions(ticket.extensions())}\}"
		encryptedExtensions(items) gives "\{\"msg_type\":8,\"extensions\":{renderExtensions(items)}\}"
		tls13Certificate(requestContext, entries) gives "\{\"msg_type\":11,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"certificate_list\":{renderEntries(entries)}\}"
		tls12Certificate(certificates) gives "\{\"msg_type\":11,\"certificate_list\":{renderCertificates(certificates)}\}"
		tls13CertificateRequest(requestContext, items) gives "\{\"msg_type\":13,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"extensions\":{renderExtensions(items)}\}"
		tls12CertificateRequest(request) gives "\{\"msg_type\":13,\"certificate_types_hex\":\"{toHex(request.certificateTypes())}\",\"supported_signature_algorithms\":{renderSchemes(request.signatureSchemes())},\"certificate_authorities_hex\":{renderAuthorities(request.authorities())}\}"
		certificateVerify(scheme, signature) gives "\{\"msg_type\":15,\"algorithm\":{scheme},\"signature_hex\":\"{toHex(signature)}\"\}"
		finished(verifyData) gives "\{\"msg_type\":20,\"verify_data_hex\":\"{toHex(verifyData)}\"\}"
		keyUpdate(request) gives "\{\"msg_type\":24,\"request_update\":{request.rawValue}\}"
		serverKeyExchange(exchange) gives "\{\"msg_type\":12,\"curve_type\":3,\"named_group\":{exchange.namedGroup()},\"public_hex\":\"{toHex(exchange.publicKey())}\",\"algorithm\":{exchange.signatureScheme()},\"signature_hex\":\"{toHex(exchange.signature())}\"\}"
		clientKeyExchange(publicKey) gives "\{\"msg_type\":16,\"public_hex\":\"{toHex(publicKey)}\"\}"
		serverHelloDone gives "\{\"msg_type\":14\}"
	end 'message'
end 'render'

function report(id String, protocol TlsProtocol, hex String)
	if let message = try TlsHandshakeCodec.decode(fromHex(hex), protocol: protocol) 'decoded'
		print("{id} {render(message)}\n")

		if let encoded = try TlsHandshakeCodec.encode(message) 'encoded'
			if toHex(encoded) != hex 'differs'
				print("{id} re-encoding differs\n")
			end 'differs'
		end 'encoded' else (encodeFailure) 'encodeRefused'
			print("{id} encode {encodeFailure}\n")
		end 'encodeRefused'
	end 'decoded' else (failure) 'refused'
		print("{id} decode {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-openssl-client-hello-s13", protocol: TlsProtocol.tls13, hex: "010001fc0303f751ab73e2918fd0968d060b060c39b6257be5f3f55a987dc528dc4adf1c15f620317e776e1a6d7e07586842a62b2532335e958b2193c77cb63c05223bb11ebbf50024130213031301c02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100018f00000010000e00000b6578616d706c652e636f6d000b000403000102000a00160014001d0017001e0019001801000101010201030104002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602002b00050403040303002d00020101003300260024001d0020b0bf4f038fa5e263c8f7afd265f76f8c11e84396316ea5c0381ab6cbb3681d65001500d300000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")
	report("decode-openssl-client-hello-s12", protocol: TlsProtocol.tls13, hex: "010000bc030351b7879bfbb8199f5e831bd3193fbe384884b5f5f4e59ed27f7122d31b046ff500001ec02cc030c02bc02fcca9cca8c024c028c023c027009f009e006b006700ff0100007500000010000e00000b6578616d706c652e636f6d000b000403000102000a000c000a001d0017001e00190018002300000010000b000908687474702f312e310016000000170000000d002a0028040305030603080708080809080a080b080408050806040105010601030303010302040205020602")
	report("decode-openssl-server-hello-tls13", protocol: TlsProtocol.tls13, hex: "02000076030386d47a7858f5f29c8320d651797bfe719f680691bd02d315cf6a2c06d2e25d30204c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7130200002e002b0002030400330024001d0020b4b9e08782df18e3c041c3d9748d9c37e8cf746c7517cdd65867a5e245581168")
	report("decode-openssl-ecdsa-aes128-server-hello", protocol: TlsProtocol.tls12, hex: "0200006803032103989ed3f9086c661dc9b690401888ec1f9078a7ac5c79a8c30c57a92f34ba20b308050d5bf63ab65764955f145e9470dc0963a60ecaec3a1869d276a2c0ac95c02b000020ff01000100000b0004030001020010000b000908687474702f312e3100170000")
	report("decode-openssl-ecdsa-aes128-certificate", protocol: TlsProtocol.tls12, hex: "0b00016300016000015d3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197")
	report("decode-openssl-ecdsa-aes128-server-key-exchange", protocol: TlsProtocol.tls12, hex: "0c00006f03001d20d18c246afb94d80a824af74508c81c6f412a213c66b188284f84b862d6a2022804030047304502206a5212b5937969cebb5ebfb24bdc154addb558c22a4ca50a975a3cfbf01eb7d002210092c8f724d28655a968e4a79cec3a2fa9b10d9d77fb6597f0db28c16008c568b2")
	report("decode-openssl-ecdsa-aes128-server-hello-done", protocol: TlsProtocol.tls12, hex: "0e000000")
	report("decode-openssl-rsa-chacha-server-hello", protocol: TlsProtocol.tls12, hex: "020000680303a34cf66730b1fd8653cb37771f320396822bea97bce992ad682ed9c0f2086b1120b55c5e00932f34343926c056980ee0a52f025804ac1fc6e292c089f3411978d7cca8000020ff01000100000b0004030001020010000b000908687474702f312e3100170000")
	report("decode-openssl-rsa-chacha-certificate", protocol: TlsProtocol.tls12, hex: "0b00022f00022c00022930820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8")
	report("decode-openssl-rsa-chacha-server-key-exchange", protocol: TlsProtocol.tls12, hex: "0c00014903001741043499b325ff286c278a89454a197be8ff239d9a20b30f6468f0c8d955c3d076f096bbbce72430c301bbd8827bda1bf59b79c95e75a15d62308d6c0d62f19b706808040100268c303c47294736aeb4f8f69a0be753613cf782a74f6bcbc5b52fb506d033ce9872d6e435aca87f882091cc175102cac902f67546099213fff4f9b151b1ccdce1699eff4ed05fa965d49947b7e8e3903674bb85d9e0c74221c3749a51f88e8862f3208ca29c478c244c8d4c469095a26a08d4b614be6f1b6b610ede2dec3a1ccb25e639fa6fc8b61ce4e72b6c3a8a3625a0019c050b5f02f52cb13d759bd0847611500282e1bb696d8ba3370e5a2b8982a8e955271a07a272d94e9939f99a644e9452acd5e7992146af5bb309439e0f763d1323864002f2c6083e4984b5853fb2866e23b24b2ad780ce430f18f0796fc121f932ab886fcbf075c7816228d0cd")
	report("decode-openssl-client-client-key-exchange", protocol: TlsProtocol.tls12, hex: "100000212024eea276fec56a91b1bac40588a6f5fa1d2ef05531329ac1a961f0ac66d00a33")
	report("decode-openssl-mtls-certificate-request", protocol: TlsProtocol.tls12, hex: "0d000030030102400028040305030603080708080809080a080b0804080508060401050106010303030103020402050206020000")
	report("decode-openssl-mtls-client-certificate", protocol: TlsProtocol.tls12, hex: "0b00017500017200016f3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91")
	report("decode-openssl-mtls-client-certificate-verify", protocol: TlsProtocol.tls12, hex: "0f00004b040300473045022020235be210c5b087a2d8e59637185fa78f49c1934d221f008685f87795bcf41f022100d0cb8c5fe07bfe6a6dead5e975038a4b084576796f25f0925381f81f9de59eb8")
	report("decode-reference-client-hello", protocol: TlsProtocol.tls13, hex: "010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303")
	report("decode-reference-tls12-server-hello", protocol: TlsProtocol.tls12, hex: "0200004a0303f8cc9a232c07e72704215042cdeda1b34ef7731c4f8edae5a65f327c79798d7200cca90000220000000000170000ff01000100000b000201000010000b000908687474702f312e31")
	report("decode-reference-tls12-server-key-exchange", protocol: TlsProtocol.tls12, hex: "0c00007003001d20ce9ea3b46a08880657a253fa2ca2cf0c19eeb169295e2d3d4d3308aa1a537d4a040300483046022100d7df07c21abcb9f23fb5f1d26b1acf0be358b8ad7ef63c494d89056347cbcbd9022100bf26943f2a9b0e7dd5991d493da28497746e0823fb994c2c336f5de058751f95")
	report("decode-key-update-0", protocol: TlsProtocol.tls13, hex: "1800000100")
	report("decode-key-update-1", protocol: TlsProtocol.tls13, hex: "1800000101")
	report("decode-hello-request", protocol: TlsProtocol.tls12, hex: "00000000")
	report("decode-finished-tls12", protocol: TlsProtocol.tls12, hex: "1400000c111111111111111111111111")
	report("decode-certificate-request-tls12", protocol: TlsProtocol.tls12, hex: "0d00001b0201400004040308040010000e300d310b300906035504030c0252")
	report("decode-certificate-empty-tls13", protocol: TlsProtocol.tls13, hex: "0b00000400000000")
	report("decode-certificate-empty-tls12", protocol: TlsProtocol.tls12, hex: "0b000003000000")
	report("decode-encrypted-extensions-empty", protocol: TlsProtocol.tls13, hex: "080000020000")
	report("decode-client-hello-no-extensions", protocol: TlsProtocol.tls12, hex: "0100002903032222222222222222222222222222222222222222222222222222222222222222000002c02b0100")
	report("decode-server-hello-no-extensions", protocol: TlsProtocol.tls12, hex: "020000260303333333333333333333333333333333333333333333333333333333333333333300c02b00")
	report("decode-certificate-verify-empty-signature", protocol: TlsProtocol.tls13, hex: "0f00000404030000")
	report("decode-unknown-extension-in-client-hello", protocol: TlsProtocol.tls13, hex: "010000e603038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008b00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303fafa0000")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-openssl-client-hello-s13 {"msg_type":1,"legacy_version":771,"random_hex":"f751ab73e2918fd0968d060b060c39b6257be5f3f55a987dc528dc4adf1c15f6","legacy_session_id_hex":"317e776e1a6d7e07586842a62b2532335e958b2193c77cb63c05223bb11ebbf5","cipher_suites":[4866,4867,4865,49196,49200,49195,49199,52393,52392,49188,49192,49187,49191,159,158,107,103,255],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"example.com"},{"type":11,"ec_point_format_list_hex":"000102"},{"type":10,"named_group_list":[29,23,30,25,24,256,257,258,259,260]},{"type":35,"extension_data_hex":""},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":22,"extension_data_hex":""},{"type":23},{"type":13,"supported_signature_algorithms":[1027,1283,1539,2055,2056,2057,2058,2059,2052,2053,2054,1025,1281,1537,771,769,770,1026,1282,1538]},{"type":43,"versions":[772,771]},{"type":45,"extension_data_hex":"0101"},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"b0bf4f038fa5e263c8f7afd265f76f8c11e84396316ea5c0381ab6cbb3681d65"}]},{"type":21,"extension_data_hex":"00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"}]}
decode-openssl-client-hello-s12 {"msg_type":1,"legacy_version":771,"random_hex":"51b7879bfbb8199f5e831bd3193fbe384884b5f5f4e59ed27f7122d31b046ff5","legacy_session_id_hex":"","cipher_suites":[49196,49200,49195,49199,52393,52392,49188,49192,49187,49191,159,158,107,103,255],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"example.com"},{"type":11,"ec_point_format_list_hex":"000102"},{"type":10,"named_group_list":[29,23,30,25,24]},{"type":35,"extension_data_hex":""},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":22,"extension_data_hex":""},{"type":23},{"type":13,"supported_signature_algorithms":[1027,1283,1539,2055,2056,2057,2058,2059,2052,2053,2054,1025,1281,1537,771,769,770,1026,1282,1538]}]}
decode-openssl-server-hello-tls13 {"msg_type":2,"legacy_version":771,"random_hex":"86d47a7858f5f29c8320d651797bfe719f680691bd02d315cf6a2c06d2e25d30","legacy_session_id_echo_hex":"4c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7","cipher_suite":4866,"legacy_compression_method":0,"extensions":[{"type":43,"selected_version":772},{"type":51,"server_share":{"group":29,"key_exchange_hex":"b4b9e08782df18e3c041c3d9748d9c37e8cf746c7517cdd65867a5e245581168"}}]}
decode-openssl-ecdsa-aes128-server-hello {"msg_type":2,"legacy_version":771,"random_hex":"2103989ed3f9086c661dc9b690401888ec1f9078a7ac5c79a8c30c57a92f34ba","legacy_session_id_echo_hex":"b308050d5bf63ab65764955f145e9470dc0963a60ecaec3a1869d276a2c0ac95","cipher_suite":49195,"legacy_compression_method":0,"extensions":[{"type":65281,"renegotiated_connection_hex":""},{"type":11,"ec_point_format_list_hex":"000102"},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":23}]}
decode-openssl-ecdsa-aes128-certificate {"msg_type":11,"certificate_list":[{"cert_data_hex":"3082015930820100a00302010202010a300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c6561663059301306072a8648ce3d020106082a8648ce3d030107034200040b019cfdb984d032a96907cca9a63888323695b6404a7ddaacc63b5559364f2ca48f2b5d0e1151b6c0b5d17286d9a522cee437c42f410742ebb8493775fc47e9a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d040302034700304402203bc12c6f77062e5eed59186b72a95e9652f4398685b8845ad8112a5085bb7ab602207c9761426fa260535fb0fa49efe2624a1f34e8e04eb43d8f3c72dd072e736197"}]}
decode-openssl-ecdsa-aes128-server-key-exchange {"msg_type":12,"curve_type":3,"named_group":29,"public_hex":"d18c246afb94d80a824af74508c81c6f412a213c66b188284f84b862d6a20228","algorithm":1027,"signature_hex":"304502206a5212b5937969cebb5ebfb24bdc154addb558c22a4ca50a975a3cfbf01eb7d002210092c8f724d28655a968e4a79cec3a2fa9b10d9d77fb6597f0db28c16008c568b2"}
decode-openssl-ecdsa-aes128-server-hello-done {"msg_type":14}
decode-openssl-rsa-chacha-server-hello {"msg_type":2,"legacy_version":771,"random_hex":"a34cf66730b1fd8653cb37771f320396822bea97bce992ad682ed9c0f2086b11","legacy_session_id_echo_hex":"b55c5e00932f34343926c056980ee0a52f025804ac1fc6e292c089f3411978d7","cipher_suite":52392,"legacy_compression_method":0,"extensions":[{"type":65281,"renegotiated_connection_hex":""},{"type":11,"ec_point_format_list_hex":"000102"},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":23}]}
decode-openssl-rsa-chacha-certificate {"msg_type":11,"certificate_list":[{"cert_data_hex":"30820225308201cba00302010202010c300a06082a8648ce3d040302300f310d300b06035504030c04526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a300f310d300b06035504030c046c65616630820122300d06092a864886f70d01010105000382010f003082010a0282010100980fb5297c6dbb97057a14a202477630baf871afeb1598873f816217be94c5d5f69db47f1cb38ea433cc6df20a2071d12fce4b12f7c44f357c82bae83573a8240420ec7ff7ed2fdd75c89785d558f68d84663e73aaa4c4d45c4601434916cc3eb576b794e589c15c5c0eefb54852ef17f41ea0321ad585779e5c34df68b88e4a180dfeebf9be95476f6f9015ec0d6b51e2523e78fac0682a5c0f5409d6868c59faddc91135f977108adf4fda4a9b2e4740c8ffece5f66f9468aead8395a76c14962637fbfcfdb9014e75b2b9af07691875b56bb80cb023cd6d4d9a6d4fb7f50b8a0ffcd773cef8d7b7ab41d25748c2f53fe2a710047c4488eb9e30eebca677b30203010001a34d304b300c0603551d130101ff04023000300e0603551d0f0101ff04040302078030130603551d25040c300a06082b0601050507030130160603551d11040f300d820b6578616d706c652e636f6d300a06082a8648ce3d04030203480030450221008f6465fe2a8cc5cb795353c12242d8cb423318cce3d16f4211d5e7313fe6916e022032211dd8d9f50b5443107ff9e203161bfaeef8028d75cc6862d584b1905ed1a8"}]}
decode-openssl-rsa-chacha-server-key-exchange {"msg_type":12,"curve_type":3,"named_group":23,"public_hex":"043499b325ff286c278a89454a197be8ff239d9a20b30f6468f0c8d955c3d076f096bbbce72430c301bbd8827bda1bf59b79c95e75a15d62308d6c0d62f19b7068","algorithm":2052,"signature_hex":"268c303c47294736aeb4f8f69a0be753613cf782a74f6bcbc5b52fb506d033ce9872d6e435aca87f882091cc175102cac902f67546099213fff4f9b151b1ccdce1699eff4ed05fa965d49947b7e8e3903674bb85d9e0c74221c3749a51f88e8862f3208ca29c478c244c8d4c469095a26a08d4b614be6f1b6b610ede2dec3a1ccb25e639fa6fc8b61ce4e72b6c3a8a3625a0019c050b5f02f52cb13d759bd0847611500282e1bb696d8ba3370e5a2b8982a8e955271a07a272d94e9939f99a644e9452acd5e7992146af5bb309439e0f763d1323864002f2c6083e4984b5853fb2866e23b24b2ad780ce430f18f0796fc121f932ab886fcbf075c7816228d0cd"}
decode-openssl-client-client-key-exchange {"msg_type":16,"public_hex":"24eea276fec56a91b1bac40588a6f5fa1d2ef05531329ac1a961f0ac66d00a33"}
decode-openssl-mtls-certificate-request {"msg_type":13,"certificate_types_hex":"010240","supported_signature_algorithms":[1027,1283,1539,2055,2056,2057,2058,2059,2052,2053,2054,1025,1281,1537,771,769,770,1026,1282,1538],"certificate_authorities_hex":[]}
decode-openssl-mtls-client-certificate {"msg_type":11,"certificate_list":[{"cert_data_hex":"3082016b30820111a00302010202016e300a06082a8648ce3d04030230163114301206035504030c0b436c69656e7420526f6f74301e170d3235303630313030303030305a170d3236303630313030303030305a3010310e300c06035504030c05616c6963653059301306072a8648ce3d020106082a8648ce3d0301070342000445ed538d789570081e327a105a4f0b3a8c146e68fa5b4fa852c5f006f1294b0ccfb91515422cdd61c2e34f354d7ff50528a1e80aad5b4ea3b0f2c6d4fed662d6a3563054300c0603551d130101ff04023000301f0603551d23041830168014b403e63e7c1c8a1890af5fb031868a5afe1d8376300e0603551d0f0101ff04040302078030130603551d25040c300a06082b06010505070302300a06082a8648ce3d04030203480030450221009a640c79afa46ecd65e2ce7f2f960e2c3963a7a3e1383c34ae3958623f1f1c5002207775c241b743bde620aed311c3c7d90a60587c23e70d1aae306966873f7e7d91"}]}
decode-openssl-mtls-client-certificate-verify {"msg_type":15,"algorithm":1027,"signature_hex":"3045022020235be210c5b087a2d8e59637185fa78f49c1934d221f008685f87795bcf41f022100d0cb8c5fe07bfe6a6dead5e975038a4b084576796f25f0925381f81f9de59eb8"}
decode-reference-client-hello {"msg_type":1,"legacy_version":771,"random_hex":"8be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8","legacy_session_id_hex":"8932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf","cipher_suites":[4867,4865,4866,52393,52392,49195,49199,49196,49200],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"example.com"},{"type":23},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23]},{"type":11,"ec_point_format_list_hex":"00"},{"type":13,"supported_signature_algorithms":[1027,2052,2055,1283,2053,2054,1025,1281,1537]},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68"}]},{"type":43,"versions":[772,771]}]}
decode-reference-tls12-server-hello {"msg_type":2,"legacy_version":771,"random_hex":"f8cc9a232c07e72704215042cdeda1b34ef7731c4f8edae5a65f327c79798d72","legacy_session_id_echo_hex":"","cipher_suite":52393,"legacy_compression_method":0,"extensions":[{"type":0},{"type":23},{"type":65281,"renegotiated_connection_hex":""},{"type":11,"ec_point_format_list_hex":"00"},{"type":16,"protocol_name_list_hex":["687474702f312e31"]}]}
decode-reference-tls12-server-key-exchange {"msg_type":12,"curve_type":3,"named_group":29,"public_hex":"ce9ea3b46a08880657a253fa2ca2cf0c19eeb169295e2d3d4d3308aa1a537d4a","algorithm":1027,"signature_hex":"3046022100d7df07c21abcb9f23fb5f1d26b1acf0be358b8ad7ef63c494d89056347cbcbd9022100bf26943f2a9b0e7dd5991d493da28497746e0823fb994c2c336f5de058751f95"}
decode-key-update-0 {"msg_type":24,"request_update":0}
decode-key-update-1 {"msg_type":24,"request_update":1}
decode-hello-request {"msg_type":0}
decode-finished-tls12 {"msg_type":20,"verify_data_hex":"111111111111111111111111"}
decode-certificate-request-tls12 {"msg_type":13,"certificate_types_hex":"0140","supported_signature_algorithms":[1027,2052],"certificate_authorities_hex":["300d310b300906035504030c0252"]}
decode-certificate-empty-tls13 {"msg_type":11,"certificate_request_context_hex":"","certificate_list":[]}
decode-certificate-empty-tls12 {"msg_type":11,"certificate_list":[]}
decode-encrypted-extensions-empty {"msg_type":8,"extensions":[]}
decode-client-hello-no-extensions {"msg_type":1,"legacy_version":771,"random_hex":"2222222222222222222222222222222222222222222222222222222222222222","legacy_session_id_hex":"","cipher_suites":[49195],"legacy_compression_methods_hex":"00","extensions":null}
decode-server-hello-no-extensions {"msg_type":2,"legacy_version":771,"random_hex":"3333333333333333333333333333333333333333333333333333333333333333","legacy_session_id_echo_hex":"","cipher_suite":49195,"legacy_compression_method":0,"extensions":null}
decode-certificate-verify-empty-signature {"msg_type":15,"algorithm":1027,"signature_hex":""}
decode-unknown-extension-in-client-hello {"msg_type":1,"legacy_version":771,"random_hex":"8be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8","legacy_session_id_hex":"8932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf","cipher_suites":[4867,4865,4866,52393,52392,49195,49199,49196,49200],"legacy_compression_methods_hex":"00","extensions":[{"type":0,"host_name":"example.com"},{"type":23},{"type":65281,"renegotiated_connection_hex":""},{"type":10,"named_group_list":[29,23]},{"type":11,"ec_point_format_list_hex":"00"},{"type":13,"supported_signature_algorithms":[1027,2052,2055,1283,2053,2054,1025,1281,1537]},{"type":16,"protocol_name_list_hex":["687474702f312e31"]},{"type":51,"client_shares":[{"group":29,"key_exchange_hex":"cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68"}]},{"type":43,"versions":[772,771]},{"type":64250,"extension_data_hex":""}]}
```

<!-- test: tls-handshake-codec.decode-errors-1 -->
`TlsHandshakeCodec.decode` of the first 29 malformed messages, each a single defect in an otherwise valid message. A line prints the `TlsCodecError` and the alert code point it names.
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

function renderProtocolNames(names TlsProtocolNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderProtocolNames'

function renderAuthorities(names TlsAuthorityNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderAuthorities'

function renderSuites(codes TlsCipherSuiteCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSuites'

function renderGroups(codes TlsGroupCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderGroups'

function renderSchemes(codes TlsSignatureSchemeCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSchemes'

function renderVersions(codes TlsVersionCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderVersions'

function renderShare(share TlsKeyShare) returns String
	return "\{\"group\":{share.group()},\"key_exchange_hex\":\"{toHex(share.keyExchange())}\"\}"
end 'renderShare'

function renderShares(shares TlsKeyShareArray) returns String
	var texts = TextArray.create()

	for share in shares 'eachShare'
		texts.push(renderShare(share))
	end 'eachShare'

	return joined(texts)
end 'renderShares'

function renderExtension(item TlsExtension) returns String
	return match item 'form'
		serverName(hostName) gives "\{\"type\":0,\"host_name\":\"{hostName}\"\}"
		serverNameAcknowledgement gives "\{\"type\":0\}"
		supportedGroups(groups) gives "\{\"type\":10,\"named_group_list\":{renderGroups(groups)}\}"
		ecPointFormats(formats) gives "\{\"type\":11,\"ec_point_format_list_hex\":\"{toHex(formats)}\"\}"
		signatureAlgorithms(schemes) gives "\{\"type\":13,\"supported_signature_algorithms\":{renderSchemes(schemes)}\}"
		applicationProtocols(protocols) gives "\{\"type\":16,\"protocol_name_list_hex\":{renderProtocolNames(protocols)}\}"
		extendedMasterSecret gives "\{\"type\":23\}"
		supportedVersions(versions) gives "\{\"type\":43,\"versions\":{renderVersions(versions)}\}"
		selectedVersion(version) gives "\{\"type\":43,\"selected_version\":{version}\}"
		cookie(cookie) gives "\{\"type\":44,\"cookie_hex\":\"{toHex(cookie)}\"\}"
		clientKeyShares(shares) gives "\{\"type\":51,\"client_shares\":{renderShares(shares)}\}"
		serverKeyShare(share) gives "\{\"type\":51,\"server_share\":{renderShare(share)}\}"
		selectedGroup(group) gives "\{\"type\":51,\"selected_group\":{group}\}"
		renegotiationInfo(connection) gives "\{\"type\":65281,\"renegotiated_connection_hex\":\"{toHex(connection)}\"\}"
		opaque(extensionType, data) gives "\{\"type\":{extensionType},\"extension_data_hex\":\"{toHex(data)}\"\}"
	end 'form'
end 'renderExtension'

function renderExtensions(items TlsExtensionArray) returns String
	var texts = TextArray.create()

	for item in items 'eachExtension'
		texts.push(renderExtension(item))
	end 'eachExtension'

	return joined(texts)
end 'renderExtensions'

function renderBlock(block TlsExtensionBlock) returns String
	return match block 'block'
		absent gives "null"
		listed(items) gives renderExtensions(items)
	end 'block'
end 'renderBlock'

function renderEntries(entries TlsCertificateEntryArray) returns String
	var texts = TextArray.create()

	for entry in entries 'eachEntry'
		texts.push("\{\"cert_data_hex\":\"{toHex(entry.certificate())}\",\"extensions\":{renderExtensions(entry.extensions())}\}")
	end 'eachEntry'

	return joined(texts)
end 'renderEntries'

function renderCertificates(certificates TlsCertificateDataArray) returns String
	var texts = TextArray.create()

	for certificate in certificates 'eachCertificate'
		texts.push("\{\"cert_data_hex\":\"{toHex(certificate)}\"\}")
	end 'eachCertificate'

	return joined(texts)
end 'renderCertificates'

function renderServerHello(hello TlsServerHello) returns String
	return "\{\"msg_type\":2,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_echo_hex\":\"{toHex(hello.legacySessionIdEcho())}\",\"cipher_suite\":{hello.cipherSuite()},\"legacy_compression_method\":{hello.compressionMethod()},\"extensions\":{renderBlock(hello.extensions())}\}"
end 'renderServerHello'

function render(message TlsHandshakeMessage) returns String
	return match message 'message'
		helloRequest gives "\{\"msg_type\":0\}"
		clientHello(hello) gives "\{\"msg_type\":1,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_hex\":\"{toHex(hello.legacySessionId())}\",\"cipher_suites\":{renderSuites(hello.cipherSuites())},\"legacy_compression_methods_hex\":\"{toHex(hello.compressionMethods())}\",\"extensions\":{renderBlock(hello.extensions())}\}"
		serverHello(hello) gives renderServerHello(hello)
		helloRetryRequest(hello) gives renderServerHello(hello)
		newSessionTicket(ticket) gives "\{\"msg_type\":4,\"ticket_lifetime\":{ticket.lifetime()},\"ticket_age_add\":{ticket.ageAdd()},\"ticket_nonce_hex\":\"{toHex(ticket.nonce())}\",\"ticket_hex\":\"{toHex(ticket.ticket())}\",\"extensions\":{renderExtensions(ticket.extensions())}\}"
		encryptedExtensions(items) gives "\{\"msg_type\":8,\"extensions\":{renderExtensions(items)}\}"
		tls13Certificate(requestContext, entries) gives "\{\"msg_type\":11,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"certificate_list\":{renderEntries(entries)}\}"
		tls12Certificate(certificates) gives "\{\"msg_type\":11,\"certificate_list\":{renderCertificates(certificates)}\}"
		tls13CertificateRequest(requestContext, items) gives "\{\"msg_type\":13,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"extensions\":{renderExtensions(items)}\}"
		tls12CertificateRequest(request) gives "\{\"msg_type\":13,\"certificate_types_hex\":\"{toHex(request.certificateTypes())}\",\"supported_signature_algorithms\":{renderSchemes(request.signatureSchemes())},\"certificate_authorities_hex\":{renderAuthorities(request.authorities())}\}"
		certificateVerify(scheme, signature) gives "\{\"msg_type\":15,\"algorithm\":{scheme},\"signature_hex\":\"{toHex(signature)}\"\}"
		finished(verifyData) gives "\{\"msg_type\":20,\"verify_data_hex\":\"{toHex(verifyData)}\"\}"
		keyUpdate(request) gives "\{\"msg_type\":24,\"request_update\":{request.rawValue}\}"
		serverKeyExchange(exchange) gives "\{\"msg_type\":12,\"curve_type\":3,\"named_group\":{exchange.namedGroup()},\"public_hex\":\"{toHex(exchange.publicKey())}\",\"algorithm\":{exchange.signatureScheme()},\"signature_hex\":\"{toHex(exchange.signature())}\"\}"
		clientKeyExchange(publicKey) gives "\{\"msg_type\":16,\"public_hex\":\"{toHex(publicKey)}\"\}"
		serverHelloDone gives "\{\"msg_type\":14\}"
	end 'message'
end 'render'

function report(id String, protocol TlsProtocol, hex String)
	if let message = try TlsHandshakeCodec.decode(fromHex(hex), protocol: protocol) 'decoded'
		print("{id} accepted {render(message)}\n")
	end 'decoded' else (failure) 'refused'
		print("{id} {failure} {failure.alert().rawValue}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-err-001", protocol: TlsProtocol.tls13, hex: "010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b000504030403")
	report("decode-err-002", protocol: TlsProtocol.tls13, hex: "010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300")
	report("decode-err-003", protocol: TlsProtocol.tls13, hex: "010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc")
	report("decode-err-004", protocol: TlsProtocol.tls13, hex: "010000")
	report("decode-err-005", protocol: TlsProtocol.tls13, hex: "010000e303038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8210000000000000000000000000000000000000000000000000000000000000000000012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303")
	report("decode-err-006", protocol: TlsProtocol.tls13, hex: "010000290303222222222222222222222222222222222222222222222222222222222222222200000001000000")
	report("decode-err-007", protocol: TlsProtocol.tls13, hex: "0100002a030322222222222222222222222222222222222222222222222222222222222222220000011301000000")
	report("decode-err-008", protocol: TlsProtocol.tls13, hex: "0100002a030322222222222222222222222222222222222222222222222222222222222222220000021301000000")
	report("decode-err-009", protocol: TlsProtocol.tls13, hex: "0100002c0303222222222222222222222222222222222222222222222222222222222222222200000213010100000100")
	report("decode-err-010", protocol: TlsProtocol.tls13, hex: "010000f603038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100009b00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300000010000e00000b6578616d706c652e636f6d")
	report("decode-err-011", protocol: TlsProtocol.tls13, hex: "010000e903038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008e00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300000017000e00000b6578616d706c652e636f6d00000474657374")
	report("decode-err-012", protocol: TlsProtocol.tls13, hex: "010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008700170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300000010000e01000b6578616d706c652e636f6d")
	report("decode-err-013", protocol: TlsProtocol.tls13, hex: "010000e303038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008800170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300000011000f00000c6578616d706c652e636f6d2e")
	report("decode-err-014", protocol: TlsProtocol.tls13, hex: "010000d203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100007700170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b0005040304030300000000")
	report("decode-err-015", protocol: TlsProtocol.tls13, hex: "010000de03038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008300000010000e00000b6578616d706c652e636f6d00170000ff01000100000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303000a00020000")
	report("decode-err-016", protocol: TlsProtocol.tls13, hex: "010000e103038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008600000010000e00000b6578616d706c652e636f6d00170000ff01000100000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303000a00050003001d17")
	report("decode-err-017", protocol: TlsProtocol.tls13, hex: "010000d303038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100007800000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b000201000010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303000d000500030403ff")
	report("decode-err-018", protocol: TlsProtocol.tls13, hex: "010000de03038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008300000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00140012040308040807050308050806040105010601003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b000504030403030010000700040002683200")
	report("decode-err-019", protocol: TlsProtocol.tls13, hex: "010000dc03038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008100000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d00140012040308040807050308050806040105010601003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303001000050003006832")
	report("decode-err-020", protocol: TlsProtocol.tls13, hex: "010000e303038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008800000010000e00000b6578616d706c652e636f6dff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b000504030403030017000100")
	report("decode-err-021", protocol: TlsProtocol.tls13, hex: "010000df03038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008400000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00020103")
	report("decode-err-022", protocol: TlsProtocol.tls13, hex: "010000de03038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008300000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b000100")
	report("decode-err-023", protocol: TlsProtocol.tls13, hex: "0100010603038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc030010000ab00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31002b000504030403030033004a0048001d00201111111111111111111111111111111111111111111111111111111111111111001d00202222222222222222222222222222222222222222222222222222222222222222")
	report("decode-err-024", protocol: TlsProtocol.tls13, hex: "010000c203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100006700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31002b00050403040303003300060004001d0000")
	report("decode-err-025", protocol: TlsProtocol.tls13, hex: "010000e803038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008d00000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303002c00020000")
	report("decode-err-026", protocol: TlsProtocol.tls13, hex: "010000e103038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008600000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303000b000100")
	report("decode-err-027", protocol: TlsProtocol.tls13, hex: "020000760303057e882b1c1cefc4dc580326140e0a55e1bba1f6061141383c8354867d7f3329208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf130300002e00330024001d00206323cef9b530ad333e68148191c077d9c6a215d138e4fd45f0f2943f6493f009002b00020303")
	report("decode-err-028", protocol: TlsProtocol.tls13, hex: "0200007e0303057e882b1c1cefc4dc580326140e0a55e1bba1f6061141383c8354867d7f3329208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf130300003600330024001d00206323cef9b530ad333e68148191c077d9c6a215d138e4fd45f0f2943f6493f009002b00020304000a00040002001d")
	report("decode-err-029", protocol: TlsProtocol.tls13, hex: "020000770303057e882b1c1cefc4dc580326140e0a55e1bba1f6061141383c8354867d7f3329208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf130300002f002b0002030400330025001d0020111111111111111111111111111111111111111111111111111111111111111100")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-err-001 decodeError 50
decode-err-002 decodeError 50
decode-err-003 decodeError 50
decode-err-004 decodeError 50
decode-err-005 decodeError 50
decode-err-006 decodeError 50
decode-err-007 decodeError 50
decode-err-008 decodeError 50
decode-err-009 decodeError 50
decode-err-010 illegalParameter 47
decode-err-011 decodeError 50
decode-err-012 decodeError 50
decode-err-013 decodeError 50
decode-err-014 decodeError 50
decode-err-015 decodeError 50
decode-err-016 decodeError 50
decode-err-017 decodeError 50
decode-err-018 decodeError 50
decode-err-019 decodeError 50
decode-err-020 decodeError 50
decode-err-021 decodeError 50
decode-err-022 decodeError 50
decode-err-023 illegalParameter 47
decode-err-024 decodeError 50
decode-err-025 decodeError 50
decode-err-026 decodeError 50
decode-err-027 illegalParameter 47
decode-err-028 illegalParameter 47
decode-err-029 decodeError 50
```

<!-- test: tls-handshake-codec.decode-errors-2 -->
`TlsHandshakeCodec.decode` of the remaining 29 malformed messages. A line prints the `TlsCodecError` and the alert code point it names.
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

function renderProtocolNames(names TlsProtocolNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderProtocolNames'

function renderAuthorities(names TlsAuthorityNameArray) returns String
	var texts = TextArray.create()

	for name in names 'eachName'
		texts.push("\"{toHex(name)}\"")
	end 'eachName'

	return joined(texts)
end 'renderAuthorities'

function renderSuites(codes TlsCipherSuiteCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSuites'

function renderGroups(codes TlsGroupCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderGroups'

function renderSchemes(codes TlsSignatureSchemeCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderSchemes'

function renderVersions(codes TlsVersionCodeArray) returns String
	var texts = TextArray.create()

	for code in codes 'eachCode'
		texts.push("{code}")
	end 'eachCode'

	return joined(texts)
end 'renderVersions'

function renderShare(share TlsKeyShare) returns String
	return "\{\"group\":{share.group()},\"key_exchange_hex\":\"{toHex(share.keyExchange())}\"\}"
end 'renderShare'

function renderShares(shares TlsKeyShareArray) returns String
	var texts = TextArray.create()

	for share in shares 'eachShare'
		texts.push(renderShare(share))
	end 'eachShare'

	return joined(texts)
end 'renderShares'

function renderExtension(item TlsExtension) returns String
	return match item 'form'
		serverName(hostName) gives "\{\"type\":0,\"host_name\":\"{hostName}\"\}"
		serverNameAcknowledgement gives "\{\"type\":0\}"
		supportedGroups(groups) gives "\{\"type\":10,\"named_group_list\":{renderGroups(groups)}\}"
		ecPointFormats(formats) gives "\{\"type\":11,\"ec_point_format_list_hex\":\"{toHex(formats)}\"\}"
		signatureAlgorithms(schemes) gives "\{\"type\":13,\"supported_signature_algorithms\":{renderSchemes(schemes)}\}"
		applicationProtocols(protocols) gives "\{\"type\":16,\"protocol_name_list_hex\":{renderProtocolNames(protocols)}\}"
		extendedMasterSecret gives "\{\"type\":23\}"
		supportedVersions(versions) gives "\{\"type\":43,\"versions\":{renderVersions(versions)}\}"
		selectedVersion(version) gives "\{\"type\":43,\"selected_version\":{version}\}"
		cookie(cookie) gives "\{\"type\":44,\"cookie_hex\":\"{toHex(cookie)}\"\}"
		clientKeyShares(shares) gives "\{\"type\":51,\"client_shares\":{renderShares(shares)}\}"
		serverKeyShare(share) gives "\{\"type\":51,\"server_share\":{renderShare(share)}\}"
		selectedGroup(group) gives "\{\"type\":51,\"selected_group\":{group}\}"
		renegotiationInfo(connection) gives "\{\"type\":65281,\"renegotiated_connection_hex\":\"{toHex(connection)}\"\}"
		opaque(extensionType, data) gives "\{\"type\":{extensionType},\"extension_data_hex\":\"{toHex(data)}\"\}"
	end 'form'
end 'renderExtension'

function renderExtensions(items TlsExtensionArray) returns String
	var texts = TextArray.create()

	for item in items 'eachExtension'
		texts.push(renderExtension(item))
	end 'eachExtension'

	return joined(texts)
end 'renderExtensions'

function renderBlock(block TlsExtensionBlock) returns String
	return match block 'block'
		absent gives "null"
		listed(items) gives renderExtensions(items)
	end 'block'
end 'renderBlock'

function renderEntries(entries TlsCertificateEntryArray) returns String
	var texts = TextArray.create()

	for entry in entries 'eachEntry'
		texts.push("\{\"cert_data_hex\":\"{toHex(entry.certificate())}\",\"extensions\":{renderExtensions(entry.extensions())}\}")
	end 'eachEntry'

	return joined(texts)
end 'renderEntries'

function renderCertificates(certificates TlsCertificateDataArray) returns String
	var texts = TextArray.create()

	for certificate in certificates 'eachCertificate'
		texts.push("\{\"cert_data_hex\":\"{toHex(certificate)}\"\}")
	end 'eachCertificate'

	return joined(texts)
end 'renderCertificates'

function renderServerHello(hello TlsServerHello) returns String
	return "\{\"msg_type\":2,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_echo_hex\":\"{toHex(hello.legacySessionIdEcho())}\",\"cipher_suite\":{hello.cipherSuite()},\"legacy_compression_method\":{hello.compressionMethod()},\"extensions\":{renderBlock(hello.extensions())}\}"
end 'renderServerHello'

function render(message TlsHandshakeMessage) returns String
	return match message 'message'
		helloRequest gives "\{\"msg_type\":0\}"
		clientHello(hello) gives "\{\"msg_type\":1,\"legacy_version\":{hello.legacyVersion()},\"random_hex\":\"{toHex(hello.random())}\",\"legacy_session_id_hex\":\"{toHex(hello.legacySessionId())}\",\"cipher_suites\":{renderSuites(hello.cipherSuites())},\"legacy_compression_methods_hex\":\"{toHex(hello.compressionMethods())}\",\"extensions\":{renderBlock(hello.extensions())}\}"
		serverHello(hello) gives renderServerHello(hello)
		helloRetryRequest(hello) gives renderServerHello(hello)
		newSessionTicket(ticket) gives "\{\"msg_type\":4,\"ticket_lifetime\":{ticket.lifetime()},\"ticket_age_add\":{ticket.ageAdd()},\"ticket_nonce_hex\":\"{toHex(ticket.nonce())}\",\"ticket_hex\":\"{toHex(ticket.ticket())}\",\"extensions\":{renderExtensions(ticket.extensions())}\}"
		encryptedExtensions(items) gives "\{\"msg_type\":8,\"extensions\":{renderExtensions(items)}\}"
		tls13Certificate(requestContext, entries) gives "\{\"msg_type\":11,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"certificate_list\":{renderEntries(entries)}\}"
		tls12Certificate(certificates) gives "\{\"msg_type\":11,\"certificate_list\":{renderCertificates(certificates)}\}"
		tls13CertificateRequest(requestContext, items) gives "\{\"msg_type\":13,\"certificate_request_context_hex\":\"{toHex(requestContext)}\",\"extensions\":{renderExtensions(items)}\}"
		tls12CertificateRequest(request) gives "\{\"msg_type\":13,\"certificate_types_hex\":\"{toHex(request.certificateTypes())}\",\"supported_signature_algorithms\":{renderSchemes(request.signatureSchemes())},\"certificate_authorities_hex\":{renderAuthorities(request.authorities())}\}"
		certificateVerify(scheme, signature) gives "\{\"msg_type\":15,\"algorithm\":{scheme},\"signature_hex\":\"{toHex(signature)}\"\}"
		finished(verifyData) gives "\{\"msg_type\":20,\"verify_data_hex\":\"{toHex(verifyData)}\"\}"
		keyUpdate(request) gives "\{\"msg_type\":24,\"request_update\":{request.rawValue}\}"
		serverKeyExchange(exchange) gives "\{\"msg_type\":12,\"curve_type\":3,\"named_group\":{exchange.namedGroup()},\"public_hex\":\"{toHex(exchange.publicKey())}\",\"algorithm\":{exchange.signatureScheme()},\"signature_hex\":\"{toHex(exchange.signature())}\"\}"
		clientKeyExchange(publicKey) gives "\{\"msg_type\":16,\"public_hex\":\"{toHex(publicKey)}\"\}"
		serverHelloDone gives "\{\"msg_type\":14\}"
	end 'message'
end 'render'

function report(id String, protocol TlsProtocol, hex String)
	if let message = try TlsHandshakeCodec.decode(fromHex(hex), protocol: protocol) 'decoded'
		print("{id} accepted {render(message)}\n")
	end 'decoded' else (failure) 'refused'
		print("{id} {failure} {failure.alert().rawValue}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-err-030", protocol: TlsProtocol.tls13, hex: "020000550303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf130300000d002b0002030400330003001700")
	report("decode-err-031", protocol: TlsProtocol.tls13, hex: "0200005d0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf1303000015003300020017002b00020304001000050003026832")
	report("decode-err-032", protocol: TlsProtocol.tls13, hex: "0800002a002800330024001d00201111111111111111111111111111111111111111111111111111111111111111")
	report("decode-err-033", protocol: TlsProtocol.tls13, hex: "08000006000400050000")
	report("decode-err-034", protocol: TlsProtocol.tls13, hex: "0800000e000c001000080006026832026833")
	report("decode-err-035", protocol: TlsProtocol.tls13, hex: "080000080006000000020000")
	report("decode-err-036", protocol: TlsProtocol.tls13, hex: "08000006000400170000")
	report("decode-err-037", protocol: TlsProtocol.tls13, hex: "0b000009000000050000000000")
	report("decode-err-038", protocol: TlsProtocol.tls13, hex: "0b00000e0000000a000001010004002a0000")
	report("decode-err-039", protocol: TlsProtocol.tls13, hex: "0b0000050000000101")
	report("decode-err-040", protocol: TlsProtocol.tls13, hex: "0d000009000006002f00020000")
	report("decode-err-041", protocol: TlsProtocol.tls13, hex: "1800000102")
	report("decode-err-042", protocol: TlsProtocol.tls13, hex: "180000020100")
	report("decode-err-043", protocol: TlsProtocol.tls13, hex: "040000090000001e0000000000")
	report("decode-err-044", protocol: TlsProtocol.tls13, hex: "0400000d0000001e000000000000000000")
	report("decode-err-045", protocol: TlsProtocol.tls13, hex: "040000120000001e00000000000001010004002d0000")
	report("decode-err-046", protocol: TlsProtocol.tls12, hex: "0c00000901001d010904030000")
	report("decode-err-047", protocol: TlsProtocol.tls12, hex: "0c00000803001d0004030000")
	report("decode-err-048", protocol: TlsProtocol.tls12, hex: "1000000100")
	report("decode-err-049", protocol: TlsProtocol.tls12, hex: "0e00000100")
	report("decode-err-050", protocol: TlsProtocol.tls12, hex: "1800000100")
	report("decode-err-051", protocol: TlsProtocol.tls13, hex: "0e000000")
	report("decode-err-052", protocol: TlsProtocol.tls13, hex: "05000000")
	report("decode-err-053", protocol: TlsProtocol.tls13, hex: "fe000000")
	report("decode-err-054", protocol: TlsProtocol.tls12, hex: "0d0000050000020403")
	report("decode-err-055", protocol: TlsProtocol.tls13, hex: "0f000003040300")
	report("decode-err-056", protocol: TlsProtocol.tls13, hex: "010000e303038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008800000010000e00000b6578616d706c652e636f6d00170000000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303ff0100020200")
	report("decode-err-057", protocol: TlsProtocol.tls13, hex: "020000290303333333333333333333333333333333333333333333333333333333333333333300130100000000")
	report("decode-err-058", protocol: TlsProtocol.tls13, hex: "0200009e0303057e882b1c1cefc4dc580326140e0a55e1bba1f6061141383c8354867d7f3329208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf130300005600330024001d00206323cef9b530ad333e68148191c077d9c6a215d138e4fd45f0f2943f6493f009002b0002030400330024001d00201111111111111111111111111111111111111111111111111111111111111111")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-err-030 decodeError 50
decode-err-031 illegalParameter 47
decode-err-032 illegalParameter 47
decode-err-033 illegalParameter 47
decode-err-034 decodeError 50
decode-err-035 decodeError 50
decode-err-036 illegalParameter 47
decode-err-037 decodeError 50
decode-err-038 illegalParameter 47
decode-err-039 decodeError 50
decode-err-040 missingExtension 109
decode-err-041 illegalParameter 47
decode-err-042 decodeError 50
decode-err-043 decodeError 50
decode-err-044 decodeError 50
decode-err-045 illegalParameter 47
decode-err-046 illegalParameter 47
decode-err-047 decodeError 50
decode-err-048 decodeError 50
decode-err-049 decodeError 50
decode-err-050 unexpectedMessage 10
decode-err-051 unexpectedMessage 10
decode-err-052 unexpectedMessage 10
decode-err-053 unexpectedMessage 10
decode-err-054 decodeError 50
decode-err-055 decodeError 50
decode-err-056 decodeError 50
decode-err-057 decodeError 50
decode-err-058 illegalParameter 47
```

<!-- test: tls-handshake-codec.encode -->
`TlsHandshakeCodec.encode` of 8 messages built with the typed constructors. A line prints the encoded message.
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

function report(id String, message TlsHandshakeMessage)
	if let encoded = try TlsHandshakeCodec.encode(message) 'encoded'
		print("{id} {toHex(encoded)}\n")
	end 'encoded' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("encode-001", message: TlsHandshakeMessage.keyUpdate(TlsKeyUpdateRequest.updateRequested))
	report("encode-002", message: TlsHandshakeMessage.finished(fromHex("0000000000000000000000000000000000000000000000000000000000000000")))
	report("encode-003", message: TlsHandshakeMessage.helloRetryRequest(TlsServerHello.create(771, random: fromHex("cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c"), legacySessionIdEcho: fromHex(""), cipherSuite: 4865, compressionMethod: 0, extensions: TlsExtensionBlock.listed(TlsExtensionArray from [TlsExtension.selectedGroup(23), TlsExtension.selectedVersion(772)]))))
	report("encode-004", message: TlsHandshakeMessage.encryptedExtensions(TlsExtensionArray from [TlsExtension.serverNameAcknowledgement, TlsExtension.applicationProtocols(TlsProtocolNameArray from [fromHex("687474702f312e31")])]))
	report("encode-005", message: TlsHandshakeMessage.clientKeyExchange(fromHex("1111111111111111111111111111111111111111111111111111111111111111")))
	report("encode-006", message: TlsHandshakeMessage.serverHelloDone)
	report("encode-007", message: TlsHandshakeMessage.tls13Certificate(fromHex(""), entries: TlsCertificateEntryArray.create()))
	report("encode-008", message: TlsHandshakeMessage.clientHello(TlsClientHello.create(771, random: fromHex("8be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8"), legacySessionId: fromHex("8932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf"), cipherSuites: TlsCipherSuiteCodeArray from [4867, 4865, 4866, 52393, 52392, 49195, 49199, 49196, 49200], compressionMethods: fromHex("00"), extensions: TlsExtensionBlock.listed(TlsExtensionArray from [TlsExtension.serverName("example.com"), TlsExtension.extendedMasterSecret, TlsExtension.renegotiationInfo(fromHex("")), TlsExtension.supportedGroups(TlsGroupCodeArray from [29, 23]), TlsExtension.ecPointFormats(fromHex("00")), TlsExtension.signatureAlgorithms(TlsSignatureSchemeCodeArray from [1027, 2052, 2055, 1283, 2053, 2054, 1025, 1281, 1537]), TlsExtension.applicationProtocols(TlsProtocolNameArray from [fromHex("687474702f312e31")]), TlsExtension.clientKeyShares(TlsKeyShareArray from [TlsKeyShare.create(29, keyExchange: fromHex("cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68"))]), TlsExtension.supportedVersions(TlsVersionCodeArray from [772, 771])]))))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
encode-001 1800000101
encode-002 140000200000000000000000000000000000000000000000000000000000000000000000
encode-003 020000340303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c00130100000c003300020017002b00020304
encode-004 080000150013000000000010000b000908687474702f312e31
encode-005 10000021201111111111111111111111111111111111111111111111111111111111111111
encode-006 0e000000
encode-007 0b00000400000000
encode-008 010000e203038be81568b6b7b19ae406c4652cbadf27ca911c78ce5956d2be42a2d8a2520db8208932987bdc56d5b0e445aa29f4a14ab2e71e711e52830c94baaf2e7f8ff4dbaf0012130313011302cca9cca8c02bc02fc02cc0300100008700000010000e00000b6578616d706c652e636f6d00170000ff01000100000a00060004001d0017000b00020100000d001400120403080408070503080508060401050106010010000b000908687474702f312e31003300260024001d0020cc2a3a0da9952cf71cce25e0cc56db9ea784eb3a1d4c4ddbc009b411ad235b68002b00050403040303
```

<!-- test: tls-handshake-codec.a-server-hello-with-the-retry-random-is-a-hello-retry-request -->
A ServerHello whose `random` is the RFC 8446 section 4.1.3 HelloRetryRequest value decodes to `helloRetryRequest`; any other ServerHello, TLS 1.3 or TLS 1.2, to `serverHello`.
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

function report(id String, protocol TlsProtocol, hex String)
	if let message = try TlsHandshakeCodec.decode(fromHex(hex), protocol: protocol) 'decoded'
		let kind = match message 'kind'
			helloRetryRequest gives "helloRetryRequest"
			serverHello gives "serverHello"
			helloRequest or
			clientHello or
			newSessionTicket or
			encryptedExtensions or
			tls13Certificate or
			tls12Certificate or
			tls13CertificateRequest or
			tls12CertificateRequest or
			certificateVerify or
			finished or
			keyUpdate or
			serverKeyExchange or
			clientKeyExchange or
			serverHelloDone gives "other"
		end 'kind'

		print("{id} {kind}\n")
	end 'decoded' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decode-rfc8448-3-server-server-hello1", protocol: TlsProtocol.tls13, hex: "020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304")
	report("decode-rfc8448-5-server-server-hello1", protocol: TlsProtocol.tls13, hex: "020000ac0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c001301000084003300020017002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002b00020304")
	report("decode-rfc8448-5-server-server-hello2", protocol: TlsProtocol.tls13, hex: "020000770303bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b9800130100004f003300450017004104583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d002b00020304")
	report("decode-rfc8448-4-server-server-hello1", protocol: TlsProtocol.tls13, hex: "0200005c03033ccfd2dec890222763472ae8136777c9d7358777bb66e91ea5122495f559ea2d00130100003400290002000000330024001d0020121761ee42c333e1b9e77b60dd57c2053cd94512ab47f115e86eff50942cea31002b00020304")
	report("decode-openssl-server-hello-tls13", protocol: TlsProtocol.tls13, hex: "02000076030386d47a7858f5f29c8320d651797bfe719f680691bd02d315cf6a2c06d2e25d30204c1bbc12a765c875c8720ccfe27f4fd9b0c69e5a49e6c91770882b11dbda01d7130200002e002b0002030400330024001d0020b4b9e08782df18e3c041c3d9748d9c37e8cf746c7517cdd65867a5e245581168")
	report("decode-openssl-ecdsa-aes128-server-hello", protocol: TlsProtocol.tls12, hex: "0200006803032103989ed3f9086c661dc9b690401888ec1f9078a7ac5c79a8c30c57a92f34ba20b308050d5bf63ab65764955f145e9470dc0963a60ecaec3a1869d276a2c0ac95c02b000020ff01000100000b0004030001020010000b000908687474702f312e3100170000")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decode-rfc8448-3-server-server-hello1 serverHello
decode-rfc8448-5-server-server-hello1 helloRetryRequest
decode-rfc8448-5-server-server-hello2 serverHello
decode-rfc8448-4-server-server-hello1 serverHello
decode-openssl-server-hello-tls13 serverHello
decode-openssl-ecdsa-aes128-server-hello serverHello
```

<!-- test: tls-handshake-codec.alerts -->
`TlsAlert` is every alert description of RFC 8446 section 6 with its code point, in the order of the canra alert table.
```maxon
function main() returns ExitCode
	for alert in TlsAlert.allCases 'eachAlert'
		print("{alert.name} {alert.rawValue}\n")
	end 'eachAlert'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
closeNotify 0
unexpectedMessage 10
badRecordMac 20
recordOverflow 22
handshakeFailure 40
badCertificate 42
unsupportedCertificate 43
certificateRevoked 44
certificateExpired 45
certificateUnknown 46
illegalParameter 47
unknownCa 48
accessDenied 49
decodeError 50
decryptError 51
protocolVersion 70
insufficientSecurity 71
internalError 80
inappropriateFallback 86
userCanceled 90
noRenegotiation 100
missingExtension 109
unsupportedExtension 110
unrecognizedName 112
badCertificateStatusResponse 113
unknownPskIdentity 115
certificateRequired 116
noApplicationProtocol 120
```

<!-- test: tls-handshake-codec.a-field-longer-than-its-length-prefix-is-refused-when-encoding -->
`TlsHandshakeCodec.encode` throws `TlsEncodeError.vectorTooLong` for a field that does not fit its length prefix (uint8 session id, uint16 ticket and extension block, uint24 handshake body) and writes the largest one that fits. A line prints the encoded length.
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

function filled(count Count) returns ByteArray
	var bytes = ByteArray.create()
	bytes.resize(count)

	return bytes
end 'filled'

function report(id String, message TlsHandshakeMessage)
	if let encoded = try TlsHandshakeCodec.encode(message) 'encoded'
		print("{id} {encoded.count()}\n")
	end 'encoded' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function helloWith(sessionId ByteArray) returns TlsHandshakeMessage
	return TlsHandshakeMessage.clientHello(TlsClientHello.create(771, random: filled(32), legacySessionId: sessionId, cipherSuites: TlsCipherSuiteCodeArray from [4865], compressionMethods: filled(1), extensions: TlsExtensionBlock.absent))
end 'helloWith'

function ticketWith(ticket ByteArray) returns TlsHandshakeMessage
	return TlsHandshakeMessage.newSessionTicket(TlsNewSessionTicket.create(1, ageAdd: 2, nonce: filled(0), ticket: ticket, extensions: TlsExtensionArray.create()))
end 'ticketWith'

function main() returns ExitCode
	report("session-id-255", message: helloWith(filled(255)))
	report("session-id-256", message: helloWith(filled(256)))
	report("ticket-65535", message: ticketWith(filled(65535)))
	report("ticket-65536", message: ticketWith(filled(65536)))
	report("extension-block-65535", message: TlsHandshakeMessage.encryptedExtensions(TlsExtensionArray from [TlsExtension.opaque(1000, data: filled(65531))]))
	report("extension-block-65536", message: TlsHandshakeMessage.encryptedExtensions(TlsExtensionArray from [TlsExtension.opaque(1000, data: filled(65532))]))
	report("finished-16777215", message: TlsHandshakeMessage.finished(filled(16777215)))
	report("finished-16777216", message: TlsHandshakeMessage.finished(filled(16777216)))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
session-id-255 300
session-id-256 vectorTooLong
ticket-65535 65552
ticket-65536 vectorTooLong
extension-block-65535 65541
extension-block-65536 vectorTooLong
finished-16777215 16777219
finished-16777216 vectorTooLong
```
