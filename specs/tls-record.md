---
feature: tls-record
status: stable
keywords: [tls-record, Tls13RecordProtection, Tls12RecordProtection, TlsRecordError, TlsContentType, TlsOpenedRecord, record protection, aead, rfc8446, rfc5246, rfc5288, rfc7905, tls, crypto]
category: crypto
---

# `Tls13RecordProtection` and `Tls12RecordProtection` — TLS record protection

## Documentation

A record protection holds the AEAD key, the IV and the sequence number of one direction of a connection, and
seals or opens one record at a time. `Tls13RecordProtection.create(suite, key: k, iv: v)` takes a `Tls13CipherSuite`
and the 12-byte IV of `Tls13KeySchedule.trafficKeys`; `Tls12RecordProtection.create(suite, key: k, iv: v)` takes a
`Tls12CipherSuite` and the implicit IV of `Tls12Prf.keyBlock` (4 bytes for AES-GCM, 12 for ChaCha20-Poly1305). The
optional `sequenceNumber:` of `create` is the number of the first record, 0 by default; each successful
`seal` or `open` moves to the next number, and a refused one leaves it where it was. A key or IV of the wrong
length for the suite is `TlsRecordError.invalidKeyLength` or `invalidIvLength` at `create`.

| Call | Meaning |
|---|---|
| `tls13.seal(contentType, plaintext: p, paddingLength: n)` | the TLS 1.3 record of `p`: inner plaintext `p ‖ type ‖ n zero bytes`, the record header as additional data, `IV xor sequence number` as the nonce |
| `tls13.open(record)` | a `TlsOpenedRecord`: the `contentType()` and `plaintext()` of the record, with the padding removed |
| `tls12.seal(contentType, plaintext: p)` | the TLS 1.2 record of `p`: AES-GCM with the sequence number as the explicit nonce, or ChaCha20-Poly1305 with `IV xor sequence number` |
| `tls12.open(record)` | a `TlsOpenedRecord`; the explicit nonce of an AES-GCM record is taken as sent |

A `TlsContentType` is `changeCipherSpec` (20), `alert` (21), `handshake` (22) or `applicationData` (23); a
record carries only the last three. Each call throws a `TlsRecordError`, and the first check that fails decides it.
`seal`: `invalidContentType` for `changeCipherSpec`, `recordOverflow` past 2^14 bytes of content (2^14 + 1 with the
type byte and padding in TLS 1.3), `emptyFragment` for an empty handshake or alert. `open`: `decodeError` when
the record is shorter than its header or its length field is wrong, `protocolVersion` (TLS 1.2 only: the version is
not 0x0303), `unexpectedMessage` for another outer content type, an inner plaintext that is all zeros or whose
type is not 21 to 23, `recordOverflow` for a body over 2^14 + 256 bytes (TLS 1.3) or 2^14 + 2048 (TLS 1.2) or an
opened plaintext over its limit, and `badRecordMac` for any failed authentication, including a body too short
for its tag. The TLS 1.3 record version is not checked; it is authenticated as part of the header. A protection
whose last number, 2^64 - 1, has been used throws `sequenceNumberExhausted`.

The vectors are the canra `tls-record` contract: every protected record of RFC 8448 sections 3 to 7, records
sent by OpenSSL 3.0.13 and generated vectors for each suite at sequence numbers 0, 1, 255, 2^48 + 7 and 2^64 - 1,
padding, empty and maximum-size records and the refusals. A record too large for the harness is built in the case by
sealing the stated content directly with `AesGcm` or `ChaCha20Poly1305`.

The 3 canra vectors that name a value the Maxon types cannot spell have no case: `record-err-016` (content type 0), `record-err-020` (suite `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`), `record-err-031` (suite `TLS_AES_128_GCM_SHA256`).

## Tests

<!-- test: tls-record.open-tls13-1 -->
`Tls13RecordProtection.open`: every protected record of RFC 8448 sections 3 to 7, OpenSSL's TLS 1.3 records, padding, every suite, sequence numbers up to 2^64 - 1, the largest records and every refusal. A line prints the inner content type and the plaintext, or the length and SHA-256 of one over 4000 bytes. Vectors tls13-open-rfc8448-3-1 to tls13-open-gen-2-1 (40).
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

function openTls13(suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray) returns TlsOpenedRecord throws TlsRecordError
	var protection = try Tls13RecordProtection.create(suite, key: fromHex(keyHex), iv: fromHex(ivHex), sequenceNumber: sequenceNumber)

	return try protection.open(record)
end 'openTls13'

function report(id String, suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray)
	if let opened = try openTls13(suite, keyHex: keyHex, ivHex: ivHex, sequenceNumber: sequenceNumber, record: record) 'opened'
		let plaintext = opened.plaintext()

		if plaintext.count() > 4000 'long'
			print("{id} {opened.contentType().rawValue} {plaintext.count()} {toHex(sha256(plaintext))}\n")
		end 'long' else 'short'
			print("{id} {opened.contentType().rawValue} {toHex(plaintext)}\n")
		end 'short'
	end 'opened' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("tls13-open-rfc8448-3-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "3fce516009c21727d0f2e4e86ee403bc", ivHex: "5d313eb2671276ee13000b30", sequenceNumber: 0, record: fromHex("17030302a2d1ff334a56f5bff6594a07cc87b580233f500f45e489e7f33af35edf7869fcf40aa40aa2b8ea73f848a7ca07612ef9f945cb960b4068905123ea78b111b429ba9191cd05d2a389280f526134aadc7fc78c4b729df828b5ecf7b13bd9aefb0e57f271585b8ea9bb355c7c79020716cfb9b1183ef3ab20e37d57a6b9d7477609aee6e122a4cf51427325250c7d0e509289444c9b3a648f1d71035d2ed65b0e3cdd0cbae8bf2d0b227812cbb360987255cc744110c453baa4fcd610928d809810e4b7ed1a8fd991f06aa6248204797e36a6a73b70a2559c09ead686945ba246ab66e5edd8044b4c6de3fcf2a89441ac66272fd8fb330ef8190579b3684596c960bd596eea520a56a8d650f563aad27409960dca63d3e688611ea5e22f4415cf9538d51a200c27034272968a264ed6540c84838d89f72c24461aad6d26f59ecaba9acbbb317b66d902f4f292a36ac1b639c637ce343117b659622245317b49eeda0c6258f100d7d961ffb138647e92ea330faeea6dfa31c7a84dc3bd7e1b7a6c7178af36879018e3f252107f243d243dc7339d5684c8b0378bf30244da8c87c843f5e56eb4c5e8280a2b48052cf93b16499a66db7cca71e4599426f7d461e66f99882bd89fc50800becca62d6c74116dbd2972fda1fa80f85df881edbe5a37668936b335583b599186dc5c6918a396fa48a181d6b6fa4f9d62d513afbb992f2b992f67f8afe67f76913fa388cb5630c8ca01e0c65d11c66a1e2ac4c85977b7c7a6999bbf10dc35ae69f5515614636c0b9b68c19ed2e31c0b3b66763038ebba42f3b38edc0399f3a9f23faa63978c317fc9fa66a73f60f0504de93b5b845e275592c12335ee340bbc4fddd502784016e4b3be7ef04dda49f4b440a30cb5d2af939828fd4ae3794e44f94df5a631ede42c1719bfdabf0253fe5175be898e750edc53370d2b"))
	report("tls13-open-rfc8448-3-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "dbfaa693d1762c5b666af5d950258d01", ivHex: "5bd3c71b836e0b76bb73265f", sequenceNumber: 0, record: fromHex("170303003575ec4dc238cce60b298044a71e219c56cc77b0517fe9b93c7a4bfc44d87f38f80338ac98fc46deb384bd1caeacab6867d726c40546"))
	report("tls13-open-rfc8448-3-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 0, record: fromHex("17030300de3a6b8f90414a97d6959c3487680de5134a2b240e6cffac116e95d41d6af8f6b580dcf3d11d63c758db289a015940252f55713e061dc13e078891a38efbcf5753ad8ef170ad3c7353d16d9da773b9ca7f2b9fa1b6c0d4a3d03f75e09c30ba1e62972ac46f75f7b981be63439b2999ce13064615139891d5e4c5b406f16e3fc181a77ca475840025db2f0a77f81b5ab05b94c01346755f69232c86519d86cbeeac87aac347d143f9605d64f650db4d023e70e952ca49fe5137121c74bc2697687e248746d6df353005f3bce18696129c8153556b3b6c6779b37bf15985684f"))
	report("tls13-open-rfc8448-3-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "17422dda596ed5d9acd890e3c63f5051", ivHex: "5b78923dee08579033e523d9", sequenceNumber: 0, record: fromHex("1703030043a23f7054b62c94d0affafe8228ba55cbefacea42f914aa66bcab3f2b9819a8a5b46b395bd54a9a20441e2b62974e1f5a6292a2977014bd1e3deae63aeebb21694915e4"))
	report("tls13-open-rfc8448-3-5", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 1, record: fromHex("17030300432e937e11ef4ac740e538ad36005fc4a46932fc3225d05f82aa1b36e30efaf97d90e6dffc602dcb501a59a8fcc49c4bf2e5f0a21c0047c2abf332540dd032e167c2955d"))
	report("tls13-open-rfc8448-3-6", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "17422dda596ed5d9acd890e3c63f5051", ivHex: "5b78923dee08579033e523d9", sequenceNumber: 1, record: fromHex("1703030013c9872760655666b74d7ff1153efd6db6d0b0e3"))
	report("tls13-open-rfc8448-3-7", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 2, record: fromHex("1703030013b58fd67166ebf599d24720cfbe7efa7a8864a9"))
	report("tls13-open-rfc8448-4-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "920205a5b7bf2115e6fc5c2942834f54", ivHex: "6d475f0993c8e564610db2b9", sequenceNumber: 0, record: fromHex("1703030017ab1df420e75c457a7cc5d2844f76d5aee4b4edbf049be0"))
	report("tls13-open-rfc8448-4-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "27c6bdc0a3dcea39a47326d79bc9e4ee", ivHex: "9569ecdd4d0536705e9ef725", sequenceNumber: 0, record: fromHex("1703030061dc48237b4b879f50d0d4d262ea8b4716eb40ddc1eb957e11126e8a7149c2d012d37a7115957e64ce30008b9e0323f2c05a9c1c77b4f37849a695ab255060a33fee770ca95cb8486bfd0843b87024865ca35cc41c4e515c64dcb1369f98635bc7a5"))
	report("tls13-open-rfc8448-4-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "920205a5b7bf2115e6fc5c2942834f54", ivHex: "6d475f0993c8e564610db2b9", sequenceNumber: 1, record: fromHex("1703030015aca6fc944841298df99593725f9bf9754429b12f09"))
	report("tls13-open-rfc8448-4-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "b1530806f4adfeac83f1413032bbfa82", ivHex: "eb50c16be7654abf99dd06d9", sequenceNumber: 0, record: fromHex("170303003500f8b467d14cf22a4b3f0b6ae0d8e6cc8d08e0db3515ef5c2bdf1922eafbb70009964716d834fb70c3d2a56c5b1f5f6bdba6c333cf"))
	report("tls13-open-rfc8448-4-5", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "3cf122f301c6358ca7989553250efd72", ivHex: "ab1aec26aa78b8fc1176b9ac", sequenceNumber: 0, record: fromHex("1703030043b1cebce242aa201be9ae5e1cb2a9aa4b33d4e866af1edb068919237741aa031d7a74d491c99b9d4e232b74206bc6fbaa04fe78be44a9b4f54320a17eb76992afac3103"))
	report("tls13-open-rfc8448-4-6", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "e857c690a34c5a9129d833619684f95e", ivHex: "0685d6b561aab9ef1013faf9", sequenceNumber: 0, record: fromHex("1703030043275e9f20acff57bc000657d3867df039cccf79047884cf75771746f740b5a83f462a0954c3581393a203a25a7dd14141ef1a37900cdb62ff62dee1ba39ab2590cbf194"))
	report("tls13-open-rfc8448-4-7", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "3cf122f301c6358ca7989553250efd72", ivHex: "ab1aec26aa78b8fc1176b9ac", sequenceNumber: 1, record: fromHex("17030300130facce3246bdfc6369838d6a82ae6de5d422dc"))
	report("tls13-open-rfc8448-4-8", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "e857c690a34c5a9129d833619684f95e", ivHex: "0685d6b561aab9ef1013faf9", sequenceNumber: 1, record: fromHex("17030300135b18af444e8e1eec7158fb62d8f2577d37ba5d"))
	report("tls13-open-rfc8448-5-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "4646bfac1712c426cd78d8a24a8a6f6b", ivHex: "c7d395c08d62f297d13768ea", sequenceNumber: 0, record: fromHex("170303029699bee20baf5b7fc727bfab6223928a381e6d0cf9c4da653f9d2a7b23f7de11cce842d5cf75631763450ffb8b0cc1d238e658af7a12adc86243114ab14a1da2fae42621ce483fb6242eabfaad52566b02b31d2eddedefeb80e66a9900d5f973b40c4fdf74719ecf1b68d7f9c3b6ceb903ca13dd1bb8f8187ae33417e1d152522c5822a1a03ad52c838c55953d610222874cce8e1790b229a2aa0b53c8d377ee720182951dc6181dc5d90bd1f0105ed1e84aa5f75957c6661897079e5ea5007449e3197bdc7c9beeedddeafdd844afa5c315ecfe65e576afe909812880620ec7048b42d7f5c78d76f299d6d82534bdd8f512febc0ed3814aca470cd8000d3e1cb9962b052fbb950df683a52c2ba77ed3713b122937a6e5170964e2ab7969dcd980b3db9b458da7603124d6dc005e4d6e04b4d0c4baf3275db827dbba0a6db09672171fc057b3851d7e026841e2978fbd2346bbefdd0376bb1108fe9acc92189f5650aa5e85d8e8c7b67ac510dba003d3d7e16350bb66d45013efd44c9b607c0d318c4c7d1a1f5cbc57e20611804e3787d7b4a4b5f08ed8fd70bdaeade02260b12ab842ef690b4a3ee7911e841b374ecd5ebbbc2a54d047b600336dd7d0c88b4bc10e58ee6cb656de7247fa20d8e91deb84628608cf80615b62e96c1491c7ac3755eb6901405d3474fe1ac79d106a0cee56c2577fc88480f96cb6b8c681b7b68b53c146093908f350888175bdfb0b1e31ad61e30ba0adfe6d223aa03c0783b5001a57587c328a9afcfcfb978d1cd4328f7d9d60530e630befd96c0c816ee20b0100768ae2a6df51fc68f172740a79af11398ee3be1252491fa9c693479e877f94ab7c5f8cad480203e6ab7b87dd71e8a0729113df17f5eee86ce108d1d72007ec1cd13c85a6c149621e77b7d78d805a30f0be030c315e54"))
	report("tls13-open-rfc8448-5-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "2f1f918663d590e7421149a29d94b0b6", ivHex: "414d5485235e1a688793bd74", sequenceNumber: 0, record: fromHex("1703030035d74f1923c662fd34137c6f502f3dd2b93d951d1b3bc97e42afe23c31abea92fe91b474999e85e3b791ce252fe8c3e9f939a4120cb2"))
	report("tls13-open-rfc8448-5-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "a7eb2a0525eb4331d58fcbf9f7ca2e9c", ivHex: "86e8be227c1bd2b3e39cb444", sequenceNumber: 0, record: fromHex("17030300132ea6cdf749196023e2b3a49491695536426047"))
	report("tls13-open-rfc8448-5-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "f27a5d97bd25550c4823b0f3e5d29388", ivHex: "0dd631f7b71cbbc797c35fe7", sequenceNumber: 0, record: fromHex("1703030013519fc5075cb0884349759ff9ef6f011bb4c6f2"))
	report("tls13-open-rfc8448-6-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "6cb6e60619d8c7355c5d4c4bc2be90d5", ivHex: "64f239530c3b888fde85e0be", sequenceNumber: 0, record: fromHex("17030302166d0a7ac079b32a94aa68c4e2893e8bd0d3c185f549c236fbbce3d647f08f3c94a2bf424d8708883605ad8955f97718b0213dead13dfb23ebb8381da582756612bcb5a5d40847719fbe9f179bfae656f3ecfd59a4c0d35132ce418a7e46f6b6a60622f8a6c06b28d83360163563be9c37f97eb902326924a72b3ed8c8381277d1581cab9c3715ac2401398467ad7ebfab3d0c3419e750104f7d62c5027901f2e4cd4ca5b8071eb03d3c732d83215066dfc4d291d4c1ff3b8d7e4298f677d4d51dea1168d8f16cb27ba40266313a1fedf9e23cc77f765450f9e96f05d08f3da245b14d4946f07ec81eed6d56f26bd574f0b7f7c7047037c16fce3b23754e662fad73e2b7213f6af296769c99a1d38e6232e0ec8dc4f84d6aa6f7de3887be0057862f9018e0ab396705aa4090ab5f2dff6325a557e7320d4effd46bb4f997d163207cce6665294aa4465541e3fe37ee7350659ea550d6dcb6af3c518852c7a14c3cc15bc32b3273bdf1751da184203135b117d300204fb12d58ca9ac34b68eca27030832f7a4b46d2a55757f63fe8f6e85ac47469e6198da88a64586bf23c69590de822263be75fd836847240c48f8c145cd6bd698962e7edc234ebe59231351eef8d7652cf3b08ab3af6e5ec74c58a8da34b39f9b0d6c4279a9a1f82071729e7059dd7f7b95b9433c4684ce1891a6d33432d52eddb0b8cee9181d403eccc12991f1ad4aa62c36049713a7bb135fdda6661a05a93f8c16f"))
	report("tls13-open-rfc8448-6-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "916948f728d9823fa41a004d083f217f", ivHex: "64153d79bac9ea10ca5a0a88", sequenceNumber: 0, record: fromHex("1703030280b46a63934e673841abaf267403bc677f6b6d2a1e2f12bb5f62683bfe36a82673f06d6287ddd609bcf2f5fd3225923d24af3c76682c180ee571a17ca4bfbe2f510dc9a0e1fca5cff2cee87d11cb531a6ef90bf5309a6b63bbbc0b88ea45103a4304091543859fa11ec032ed873444cd5185ead5f6a76420f0f0286acef802c8e4788c23275f1b06da600f4a7decd0bc59d7bef10e649ae32690397fc3d4ed6f30f801d8cd569b71ad4fa05ea7cf2ac2dfa150d220505d4011b34d09d53853eba61a101e4f8dca47d8171a884b19259a3dd48c5ac141983edc77814d25e7f66bbbdb9096839266e06561828ecfb27eafd4e9e81a0b96e3bfa42dae5ad80359b9a6661402c3a2104177030106dbd8f65bb6a0159d512eb13af22a259f313bd58c2e21fe053d57f2a962b0a4ea682c96f70b79b560136192823b27be6a2fb7b1c751ccc0e3303615541485b7b307b423332c11efa80b72f9b80a53e53f7bb38a3af4c59f8008bad0544e5614e688ff57bccd6935f81f447f420c1c1bf4058818e90bf5dc716ccae42524856df8250bcdbd7af65f82dd53061d024f6d2ff5c11e3792a9a70e0ee2a3c20a1b968ac391f8f92831135d25242ada2fe241c2653ec996339dfa12dfae7a3373df88b07ca27aef6dc266a25f13f75c76039c1f46fd7a53ae6399c999f4b2aee18e480d6d12bfae226bbdc92a6ad50b4d3bac7abc3b3651eb5be56f33bf41127b3ca886dc714a50d1490357bd40d9fd6be42209a4ddb9ebb2987e29f120f05814614d2c79320015b461fe7324447670a1af5f65caed15b474ab7faa495016adf808e53b94ef54afbb0e0a3a2732ab597f7d5923c77386aa5124731f8cc73e703b341c175a454939a77ab64313c15cf3fe03c4f33842564976"))
	report("tls13-open-rfc8448-6-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "cdc09c806aa8f86dfcd51efc44a0c039", ivHex: "6ef852e78b46d913668e53e7", sequenceNumber: 0, record: fromHex("1703030013e4ad7d44c29245339d355962c779b89ef44c58"))
	report("tls13-open-rfc8448-6-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "88b3123ddecadf8c1ba298e2c18176b0", ivHex: "4e0978513f9de8327c08e4f3", sequenceNumber: 0, record: fromHex("17030300131decc5d6e64bba8a6f21b4fd077497da2a90cb"))
	report("tls13-open-rfc8448-7-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "041091fdab29f2c8abfb156dc5fc8d54", ivHex: "7464d791685de05998fcbadb", sequenceNumber: 0, record: fromHex("17030302a248de891d9c3624a67a6c6f0601ab7ac20c1f6a9e14d2e6007e999e130367a8af1bcfea9498fbce19df4505eece3a25da523cbe55ea1b3bda4e91995e455d500a4faa6227b7111e1c8547e2d7c179db215303d25827f3cd18f48f6491328cf5c0f814d388150bd9e9264aae491db6995069bea17665d5e0c817284d4ac21880054c3657331e23a9304dc88a15c04ec80bd3852bf7f9d3c6615b15fac83bbca031c6d2310d9f5d7a4b020a4f7c19062b65c05a1d3264b557ec9d8e0f7cee27e36f793039de8dd96edfca9009e0651034bff31d7f349eece01d99fcb5fcab840d7707c72299c3b5d04564e880a33c5e846c762e3d922bb55303d1d87cc0f06573f17dcb9b8ffd35bbd883c1cb3aa24fcc325005f768ce2fb624ca97b6c4d98e17f35bc2c7940a06100c2d448db7180b2d862164435c9c210e9860394e05aab23ff1b0203f662c588da5bc4411477a30b41136c488a0a63fcab5c15ac613226dae827a1d1fe95ece6b30bcee1560a8d408d264555e760f9bfc624c2c87fd0456c9bfb41bcd1a7b212786d2b67fd57804facfa1eef7cf2919d8b998c9789f763b4d9caa093a9ded43175d46a76b4d54f0ce0c5d2259b607e30a9d241263874fa59d6f570dc40d83a2d83bf9e9850d454c57806535a8998ae0357df92f00b9667344c24114ccc9ef539124b204e7e6e748c30a28a3d1d183997243eaccbbd33b0c1115a0327106a1e6a75271d4983086f632ff0eb8b4c63102cbcef5bb72dae1279d5de8eb19096d8cdb07fa8ea989788fac23e66e0488c193f3f3fea8c8838896bf3ae4b6848d42ced4bdf41abe6fc331b44225e7a1f7d3564147d5458e71aa909cb02be958bbc42e3aa5a27cc6eaf4b6fe51ae4495694d8ab6320aab920183fd5b31a359042fbd67391ec5e4d1892a2e5210141a494e9301b24a113c474c7f2a73457847"))
	report("tls13-open-rfc8448-7-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "62d13c13ffd7402fc1c09e3d163665cb", ivHex: "7166f20028bf146dcfbd5a40", sequenceNumber: 0, record: fromHex("170303003532d030e273773a8696c799981af6ced07f87482e81565e394e87c867f33df3d65b7506f1a626af91d4821d5f7a1f210ef8dd3c6d16"))
	report("tls13-open-rfc8448-7-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "33d7f9709756c966488ad4438437e673", ivHex: "c5f30d34b0e91b7d6c8eea65", sequenceNumber: 0, record: fromHex("17030300130f629155382dba23c4e2c5f7f84e6f2ed3083d"))
	report("tls13-open-rfc8448-7-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "edc4cbd0041c28cc7167441d7ca53e6a", ivHex: "bf6c7d8e0a9545b427dcf139", sequenceNumber: 0, record: fromHex("1703030013b7257b0fecaf69d4f09e3f891e2a25d1e28845"))
	report("tls13-open-openssl-aes256-1", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d84907aaf55220b853f53610dc5068fa5cd4ee35d6d73dd1c652335ea90123f9", ivHex: "05994999fe2798d8551174f6", sequenceNumber: 0, record: fromHex("170303010aacf2144b4e224a2c9b5882a5bc8d67eeaa5f7a855fa33fbed6184c202636cfb6df42815d361bd228ff3db6505098035135a83b7ca4b93521a8d62b48c5f52b9c326b961d53d9f690bd039471f859ac266ac81d7ae017bf8b1a4d98ecab8be2ce1d266e5e84541aae48e1bfe34d024c952859e124bfea6b09d93d3b527575441c5beece80f4d3a37f9b1c43cf7aad6708a4fc72989dfde3712ddf31d4abdcce1dd76dee4182cad0a191056a4038bc8472941bc2f21afdef66a13599a1b40d9c96650ae37b6b5254f2047954bf0dab72757e62fc3ec7bba28e5f2a2ba7114623a3b17c6ffa4fadece8db59c471c1613935d3fbc6525b177a942b2a9fa84c0b734b0a9c2ed80ad1d5fe3677"))
	report("tls13-open-openssl-aes256-2", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d84907aaf55220b853f53610dc5068fa5cd4ee35d6d73dd1c652335ea90123f9", ivHex: "05994999fe2798d8551174f6", sequenceNumber: 1, record: fromHex("170303010aca566e8b729ff7fc10b5ac3a9d60a3327586a623422ef419f37ce70341ac5aeada6c94fa1d7a66d3ad3b5a479307c8dfe1014ebb4392d49507dd0ac895632cb13337b5c4da74c58d7dd1e1c85dfae620ee19e31b3c6b749eca5bb30ac6da399e74f701e76283e8a09d287617339fd5ebb05e2e9d9be0929050ed7d04263575b59ef7607ae89722bd4db302c891e30d03953cc77b1be4241b3087b3dbed58e955a68fde450a29923b23feae03732958d0624f96a1ab598800b6c294b615492e231f0040eecbe30a1daea04b75bb4338c09db5e4a1346fc85892ea9e11945894c1828267927290032938ebdbecf0259992b660639f22a1a0c48fb8106065f81c08a26de3f73f3f077aa5ed"))
	report("tls13-open-openssl-aes256-3", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d84907aaf55220b853f53610dc5068fa5cd4ee35d6d73dd1c652335ea90123f9", ivHex: "05994999fe2798d8551174f6", sequenceNumber: 2, record: fromHex("17030300234731c1ce7f01af6c7f24a4b9bd8c01818c69ef9999d5e070f01a72207c764559f8b5be"))
	report("tls13-open-openssl-chacha-1", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "a17fe8d3df921a8ae95dd2278c0087bad9d4cc6c9df3adad861e3267d2dc0bfb", ivHex: "2b659c3dc8bff260a2430b8d", sequenceNumber: 0, record: fromHex("17030300faefccb083da823dde8fb4fabc21382ebee411b4efcde17a54cd85e490abb3ec927b4b273ed465bdc21512bed747f7a4bc216071c4be3af32a2874ca95003c298c5298574253b933d6d031b0d04e18f3b0a693b4387f804a1b93b728f1a4236632a9043a73a21700afa6a55c01ab67bf37589df29a1498cfb87b4f01fe89fa91300306e01fb13871e451d725cf1bdff76244b0a39b036dd8f84257264f205c501755ae6419ad82952b2a25dc758ef8124955eb37f261249f0f2b2e99e54ee8330f4be68d498b64ba5c0951aa37c0a68f32fd0004fb6812d84729a3cbda2c5bfb0ee59df6cae959242afc2a938e1998ec21c96efbff65085bc62e96"))
	report("tls13-open-openssl-chacha-2", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "a17fe8d3df921a8ae95dd2278c0087bad9d4cc6c9df3adad861e3267d2dc0bfb", ivHex: "2b659c3dc8bff260a2430b8d", sequenceNumber: 1, record: fromHex("17030300fa7013e55f47f58997a2e870310ef0d76b35f030468912dac1448003a79b7da654a6584b6a9c2a1893eef5415ece4e3d41cfa07d6d3b11b2e414e8a592a1659e1c3dfca31d35406a4a76d27d4d21fe77e64272832308505e239ba65d0b484f782a6ef5fa80a5cb09e69e51cacb66a19406b481c8539a2753ed708291eaf107981caec4807729c075f2190faede4944d6093b0937ea21f9118b97f55e581dde5c48b31a3e6c68a0b399f1d56efcb7a17dd3e21e44d2782a1378df60ca36ba8509f7c87fe6e7fc661cb4797a7edb888794e077993ac2cd2ff0c2bb305d65b1b145f3913b81886613ae34f4da6cd1523659a85642058241bc61bc030f"))
	report("tls13-open-openssl-chacha-3", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "a17fe8d3df921a8ae95dd2278c0087bad9d4cc6c9df3adad861e3267d2dc0bfb", ivHex: "2b659c3dc8bff260a2430b8d", sequenceNumber: 2, record: fromHex("1703030023a8f346836396bb855e7957e0c69a5206e675dbed48356f9f64ff83dfea6f35eec16693"))
	report("tls13-open-gen-1-0", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: fromHex("1703030023c069ec3f541927d8c80b9a2c6385e8781aa8e2709d24a413900be2d69c6ecc901631a2"))
	report("tls13-open-gen-1-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482"))
	report("tls13-open-gen-1-255", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, record: fromHex("17030300238e58eeec1b5f91deff2eb9e489de2f5d378b04d11fd82261965fa264805e29344e00eb"))
	report("tls13-open-gen-1-663", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, record: fromHex("170303002359e1c796a853c5b5099508f6bb77cf9df44783f83dca0f4f21eb8bb6ac6e84a94d6055"))
	report("tls13-open-gen-1-615", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, record: fromHex("1703030023e2aaa3e4657bbe9220a0c63a88c17945cdfbf5f4109598b9825ff731a3f258b1e5cec5"))
	report("tls13-open-gen-2-0", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: fromHex("1703030023aa09f0f32f5bf3ee965a4b433f12e7ae9e584ce3a51f1085a41267d7d7e0afb53913f5"))
	report("tls13-open-gen-2-1", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("170303002355fd6a595275dd2e92e58558a545a236db87ef0cb6e7ccf89008bd5c01c2d1d6e8909a"))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tls13-open-rfc8448-3-1 22 080000240022000a00140012001d00170018001901000101010201030104001c00024001000000000b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de100000f000084080400805a747c5d88fa9bd2e55ab085a61015b7211f824cd484145ab3ff52f1fda8477b0b7abc90db78e2d33a5c141a078653fa6bef780c5ea248eeaaa785c4f394cab6d30bbe8d4859ee511f602957b15411ac027671459e46445c9ea58c181e818e95b8c3fb0bf3278409d3be152a3da5043e063dda65cdf5aea20d53dfacd42f74f3140000209b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718
tls13-open-rfc8448-3-2 22 14000020a8ec436d677634ae525ac1fcebe11a039ec17694fac6e98527b642f2edd5ce61
tls13-open-rfc8448-3-3 22 040000c90000001efad6aac502000000b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d3570008002a000400000400
tls13-open-rfc8448-3-4 23 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031
tls13-open-rfc8448-3-5 23 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031
tls13-open-rfc8448-3-6 21 0100
tls13-open-rfc8448-3-7 21 0100
tls13-open-rfc8448-4-1 23 414243444546
tls13-open-rfc8448-4-2 22 080000280026000a00140012001d00170018001901000101010201030104001c0002400100000000002a00001400002048d3e0e1b3d907c6acff145e16090388c77b05c050b634ab1a88bbd0dd1a34b2
tls13-open-rfc8448-4-3 22 05000000
tls13-open-rfc8448-4-4 22 140000207230a9c952c25cd6138fc5e6628308c41c5335dd81b9f96bcea50fd32bda416d
tls13-open-rfc8448-4-5 23 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031
tls13-open-rfc8448-4-6 23 000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031
tls13-open-rfc8448-4-7 21 0100
tls13-open-rfc8448-4-8 21 0100
tls13-open-rfc8448-5-1 22 080000180016000a0008000600170018001d001c00024001000000000b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de100000f0000840804008033ab13d4462707231b5dcae6c8190b63d1dabc74f28c395370da0b07e5b83066d0246a31acd95df475bfd799a4a70d33ad93d3a317a9b2c0d237a5685b219e774112e391a247607d1aeff1bbd0a39f382ee1a5fe88ae99ec59228e6497e45d48ce275a6d5ef40d169fb6f9d33b052ed3dcdd6b5a48baafffbcb290128415bd38140000208863e6bfb0420a927fa27f34336a70ae426e968e3eb884945b96856dba3976d1
tls13-open-rfc8448-5-2 22 1400002023f52fdb0709a55bd7f79b991f25484087bcfd4d4380b12326a52a28b2e368e1
tls13-open-rfc8448-5-3 21 0100
tls13-open-rfc8448-5-4 21 0100
tls13-open-rfc8448-6-1 22 080000240022000a00140012001d00170018001901000101010201030104001c00024001000000000d000027000024000d0020001e0403050306030203080408050806040105010601020104020502060202020b00013b000001370001323082012e3081d5a003020102020107300a06082a8648ce3d04030230133111300f060355040313086563647361323536301e170d3136303733303031323430305a170d3236303733303031323430305a30133111300f0603550403130865636473613235363059301306072a8648ce3d020106082a8648ce3d0301070342000408d530161575f4cfe7f154ee3448180086001e88431a79ee62ee6e2f83ef38ba61e9fb37f34e007a7df4d2f5b56d1f04ece45d621f468406f5c3a15158948dd0a31a301830090603551d1304023000300b0603551d0f040403020780300a06082a8648ce3d0403020348003045022100df30fd4507f5edd22c1a6ff86db479ca693feeca3b71b3f9ef556b2937c0594d022062e2a47250d320fea83c7e2dcb5b76a50e0200c09adbd13fee946e513e011d1100000f00004b040300473045022100d7a4d34bd54f55fee1a89625678c3dd5e5f60dac73ec940c5c7b9304a02084a90220289f595ed488b9ac689a3d192b1a8bb38f34af7874c059c9806a1f38269353e81400002093b70cdf4781985b96345caac701b4e750d3042df1a689d8faca812251113c11
tls13-open-rfc8448-6-2 22 0b0001bf000001bb0001b6308201b23082011ba003020102020101300d06092a864886f70d01010b05003011310f300d06035504031306636c69656e74301e170d3136303733303031323335395a170d3236303733303031323335395a3011310f300d06035504031306636c69656e7430819f300d06092a864886f70d010101050003818d0030818902818100c38175e004a68d093f823b9c379d201fbc0bb7a1c791905e3fbf76847e44e751ebbcd360bd945c81e5222bcc8846d3a8a0f93e9bf5bebabd92edf1de1ff19021703e7ab6c0901513f97e39b111f09c9348971c7b211984a754cd45fe095af0ea4236829bccf7a7fe9b2888e78ab477690a5b9e1ccbe91c6a4a0f97a7e02842010203010001a31a301830090603551d1304023000300b0603551d0f040403020780300d06092a864886f70d01010b0500038181001a7a5a018532b022af0767d486160cff2d167a1915d23835b54594916dc680be5d2e626076c5d52722ebcc775d7d99f980be2fc94d34acf6cc00ba90cbcfb0608aa1e7e3971ef0c07a41d47ad8345d1f81fe418a1cf41054429fd217bd777dc1cf08f05df90799c659361e0f1a8ee4ac0f7897420bdbc823da80a2f2ba23081c00000f00008408040080186b2223b503a759c35dba0e9721b4b579138d5f0f5e6ec7feaaf27f3ad7f386c2c7bd7cb2be52fbf5ed8393f406ee79369692ec7ac695651d858219e672a8eb7b2a677b640b46ab630edc5f3f2f8272b9c0d906f81f84ddc5b8c7bcf955c78a3cf99e5016f73e04eb7dfcb28833f13e8f75ec2ff3581e2f098ad4157fd6d6ad140000209afe2ba2f63a09d229d8a429e5b37ffd9fcc73bdb5911b82425972aa2892440f
tls13-open-rfc8448-6-3 21 0100
tls13-open-rfc8448-6-4 21 0100
tls13-open-rfc8448-7-1 22 080000240022000a00140012001d00170018001901000101010201030104001c00024001000000000b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de100000f00008408040080a2301a68dd1ceee6938fe9d40c46b9201b34d59952a37e06523a39cf8ba6c9c8b68ae94492af780516ed7b73c82812e99dd3fabea45e09d9c6848721c2808c61501b0c75e7fcaba5f78bef68a2c2b69b19558b3e40387eea93d25c7781c1cc00e9f519f7e2e4adb73e76d66089000a2dc866c2ed30bba50a0d457f19dc6eb9f314000020c8c3a8f1bff5274061f4bc3a7caffbdc9616094ca625caa65f8e76ed46db74d3
tls13-open-rfc8448-7-2 22 14000020692cab155cc6c100ead60733d0617f6fb09b71aa1e8c9accbbbc9e8ed336c1dd
tls13-open-rfc8448-7-3 21 0100
tls13-open-rfc8448-7-4 21 0100
tls13-open-openssl-aes256-1 22 040000f500001c20429ff6c008000000000000000000e0ea7755fca18da8d4a36a6b69da81986a55eb18a9d0cf51b36ea2200d61767a8c95276de4f10617ce5c26e88346629e4184c79ac98d5ed632606c9ed9b77e05b29c4725d33cb88e45a464e21a974d7aaf61e390764593f2d1abe6e77fb67108d8d703e365c68e8bbbfd3d9108aa54bdf1ca93bba833fe6e37f83214c5a14910f1cae83552d8131e22a00529e4d8252fea424eb381510fb642f1769602428fdcaef9bf495093b285dc1e61b089b3d968886d4cef744d818db02491fdab854c7b0d9f989d6342d98b418715b9545175a81ed2318fa63ccde38ce3038b50f8229d190000
tls13-open-openssl-aes256-2 22 040000f500001c20b2a39b1e08000000000000000100e0ea7755fca18da8d4a36a6b69da81986afcd26bd9414627939a0edd91fce13e92aa68fcf9ce8edf5d1abf2f625c587fd28aba5bb85d8b55e670ffc0e4ffd244d3b5e05c7f75f50f0147e70ecaf9d5d98fc3612a839b2e74a8a6811d88eaa674c87d449854a2d21e3b3b1ef8710e995ea01fab71dbd6f02cac0e3b91c17bb43d36149208be13cbe28de92c6f54d7bea0b8382988ab0b199b4c829f272a87ed1f1a251b8687c73b04e580cfc271db8e956db82391867537c639f6ea1a31ccdb57de4d12130cb8b881bfd4970da90a31ef36289a56aaf38f1d5d3e284cedb049cf310000
tls13-open-openssl-aes256-3 23 68656c6c6f2066726f6d206f70656e73736c
tls13-open-openssl-chacha-1 22 040000e500001c20e0dd97f808000000000000000000d010d0974b818a76a521adb6437007c88735705ebbfd491e5c44abd1ca0f039ce84c29e639c7f41d6cd3cd09abafa9c6cb90449b5b2cb450f2b32d95c7a0e611592391eb92b1d9e760b1936f79566cd119c60f79138398c135d9e36ccecd4bd2eb68f007bb58c1e80e84ab2c6c0c8b24c83431c16a490a42cdaf6e10a74b0f534a2bcbf873d21bc2d7e5a24a8da3cadfaa2429b0cc19effd02fe503f2bf5b2f72323e0d0845d871f27ee4ee60d65fa7e5756acf2aeef0c05b35d383b9205768afa48fe505a63ebd94bf45e8a140138a5a90000
tls13-open-openssl-chacha-2 22 040000e500001c2019f49a7908000000000000000100d010d0974b818a76a521adb6437007c8871a3c83c0df6181e1f5e04afcd6b539f884abbc57ac2784ebab29440a176ce92ac1ddb33c5aa6866cba142bd5744e6edfc73457bf628ca2ef5073852459a0b556a75aa227eeca9399252b632ca334f2552b230e44cf20990ae3d5800e2e8e977d4f7e8f170ba9f351bfcb69e0d68ab3bfc10bcb82df5ee06c2aabb083ee3dfcf666f410385260eb1cdda8ce656749b265b6aa462604c92592a6d7e0b28d52f3d29a97e6fb91e3f85e6058e7909942c9d1ce17d8b8e60efa30b97cbd9aea6331f30000
tls13-open-openssl-chacha-3 23 68656c6c6f2066726f6d206f70656e73736c
tls13-open-gen-1-0 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-1-1 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-1-255 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-1-663 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-1-615 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-2-0 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-2-1 23 474554202f20485454502f312e310d0a0d0a
```

<!-- test: tls-record.open-tls13-2 -->
`Tls13RecordProtection.open`: every protected record of RFC 8448 sections 3 to 7, OpenSSL's TLS 1.3 records, padding, every suite, sequence numbers up to 2^64 - 1, the largest records and every refusal. A line prints the inner content type and the plaintext, or the length and SHA-256 of one over 4000 bytes. Vectors tls13-open-gen-2-255 to record-err-019 (25).
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

function fill(byteHex String, count Count) returns ByteArray
	let byte = try fromHex(byteHex).get(0) otherwise panic("a fill byte")
	var bytes = ByteArray.create()

	for _ in 0 upto count 'eachCopy'
		bytes.push(byte)
	end 'eachCopy'

	return bytes
end 'fill'

typealias Sequence = int(0 to u64.max)

type Sealer
	var aead as Aead

	static function aesGcm(key ByteArray) returns Sealer
		return Self{aead: try AesGcm.create(key) otherwise panic("a case key of an AES size")}
	end 'aesGcm'

	static function chaCha20Poly1305(key ByteArray) returns Sealer
		return Self{aead: try ChaCha20Poly1305.create(key) otherwise panic("a case key of 32 bytes")}
	end 'chaCha20Poly1305'

	function seal(nonce ByteArray, plaintext ByteArray, additionalData ByteArray) returns ByteArray
		return try aead.seal(nonce, plaintext: plaintext, additionalData: additionalData) otherwise panic("a case plaintext within the AEAD limit")
	end 'seal'
end 'Sealer'

function content(headHex String, byteHex String, count Count, tailHex String) returns ByteArray
	var bytes = fromHex(headHex)
	bytes.append(fill(byteHex, count: count))
	bytes.append(fromHex(tailHex))

	return bytes
end 'content'

function bigEndian(value Sequence) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto 8 'eachByte'
		bytes.push(((value shr ((7 - index) * 8)) and 255) as Byte)
	end 'eachByte'

	return bytes
end 'bigEndian'

function xorNonce(iv ByteArray, sequenceNumber Sequence) returns ByteArray
	let sequenceBytes = bigEndian(sequenceNumber)
	var nonce = ByteArray.create()

	for (iter, byte) in iv.withIterator() 'eachByte'
		if iter.index() < 4 'prefix'
			nonce.push(byte)
		end 'prefix' else 'masked'
			nonce.push(byte xor (try sequenceBytes.get(iter.index() - 4) otherwise panic("eight sequence bytes")))
		end 'masked'
	end 'eachByte'

	return nonce
end 'xorNonce'

function header(typeByte Byte, length Count) returns ByteArray
	var bytes = ByteArray from [typeByte, 3, 3]
	bytes.push((length shr 8) as Byte)
	bytes.push((length and 255) as Byte)

	return bytes
end 'header'

function sealed13(sealer Sealer, ivHex String, sequenceNumber Sequence, inner ByteArray) returns ByteArray
	let additionalData = header(23, length: inner.count() + 16)
	var record = additionalData.clone()
	record.append(sealer.seal(xorNonce(fromHex(ivHex), sequenceNumber: sequenceNumber), plaintext: inner, additionalData: additionalData))

	return record
end 'sealed13'

function raw(headerHex String, byteHex String, count Count) returns ByteArray
	var bytes = fromHex(headerHex)
	bytes.append(fill(byteHex, count: count))

	return bytes
end 'raw'

function openTls13(suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray) returns TlsOpenedRecord throws TlsRecordError
	var protection = try Tls13RecordProtection.create(suite, key: fromHex(keyHex), iv: fromHex(ivHex), sequenceNumber: sequenceNumber)

	return try protection.open(record)
end 'openTls13'

function report(id String, suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray)
	if let opened = try openTls13(suite, keyHex: keyHex, ivHex: ivHex, sequenceNumber: sequenceNumber, record: record) 'opened'
		let plaintext = opened.plaintext()

		if plaintext.count() > 4000 'long'
			print("{id} {opened.contentType().rawValue} {plaintext.count()} {toHex(sha256(plaintext))}\n")
		end 'long' else 'short'
			print("{id} {opened.contentType().rawValue} {toHex(plaintext)}\n")
		end 'short'
	end 'opened' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("tls13-open-gen-2-255", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, record: fromHex("1703030023d9397d4fefbddcc29b5dc9956b0aa90235f9a979ce134f2c98cbb97f71cdc148b73c26"))
	report("tls13-open-gen-2-663", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, record: fromHex("1703030023dd5e8fbbd958ecc44607bcc923018a146a0d4f73fd1d3bc4cbc6086be49313a7b16635"))
	report("tls13-open-gen-2-615", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, record: fromHex("170303002345e70253c1e9de390123824140bdc8126965015b4b132536d52800ba2d531d06e3cab6"))
	report("tls13-open-gen-3-0", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: fromHex("17030300232bc288b08f66d7603dccddad53dad5c12e48e14bdb55b7aaffb0c4cefa2add4b7ac14e"))
	report("tls13-open-gen-3-1", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("17030300231913efb96a07fa860aa216acd1651692d83176007e80f469b52a06ade81ed717f8e3f8"))
	report("tls13-open-gen-3-255", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, record: fromHex("17030300232fb890686fc91e4987024ca65ad2ca1eae1d9b7fb3fd26ef8be7a1783fa41ce38abdd4"))
	report("tls13-open-gen-3-663", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, record: fromHex("1703030023fdfa80651503ea7c6e0014d5e7dad779e89ead2c0ed38b8b0467ed13d7134788682b80"))
	report("tls13-open-gen-3-615", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, record: fromHex("1703030023089d75881584451e7e7ed2928c04eef26e26cf93f79166726f61752f3d7465507ee782"))
	report("tls13-open-padding", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 3, record: fromHex("1703030052938f8845daecd8b1a5cbe252214a17ec15f3e2aab9b66a0972e357ba1b4d839b86537225d6aee69da83f9f017eb171c049b2e66313ff3670709a593dc6d419083cd5c53f22e7102662c66019bf373d4958f4"))
	report("tls13-open-maximum", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 9, record: sealed13(Sealer.aesGcm(fromHex("d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 9, inner: content("", byteHex: "61", count: 16384, tailHex: "17")))
	report("tls13-open-maximum-padding", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed13(Sealer.chaCha20Poly1305(fromHex("d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, inner: content("010215", byteHex: "00", count: 16382, tailHex: "")))
	report("tls13-open-alert", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 4, record: fromHex("170303001890ccd8da7d3a367e6a8f2d08a7fcc463a6804b67ca4a3cba"))
	report("record-err-001", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 2, record: fromHex("1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482"))
	report("record-err-002", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409402"))
	report("record-err-003", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1703010023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482"))
	report("record-err-004", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("170303000f000000000000000000000000000000"))
	report("record-err-005", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: raw("1703034101", byteHex: "00", count: 16641))
	report("record-err-006", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed13(Sealer.chaCha20Poly1305(fromHex("d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, inner: content("", byteHex: "61", count: 16385, tailHex: "17")))
	report("record-err-007", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed13(Sealer.aesGcm(fromHex("1b1e5a3dc833c5d14e881acf5480a3ad")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, inner: content("", byteHex: "00", count: 30, tailHex: "")))
	report("record-err-008", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed13(Sealer.aesGcm(fromHex("1b1e5a3dc833c5d14e881acf5480a3ad")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, inner: content("", byteHex: "00", count: 0, tailHex: "")))
	report("record-err-009", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed13(Sealer.aesGcm(fromHex("1b1e5a3dc833c5d14e881acf5480a3ad")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, inner: content("", byteHex: "00", count: 1, tailHex: "14")))
	report("record-err-010", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1603030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482"))
	report("record-err-011", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d4094"))
	report("record-err-012", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, record: fromHex("1703"))
	report("record-err-019", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 0, record: fromHex("1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482"))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tls13-open-gen-2-255 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-2-663 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-2-615 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-3-0 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-3-1 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-3-255 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-3-663 23 474554202f20485454502f312e310d0a0d0a
tls13-open-gen-3-615 23 474554202f20485454502f312e310d0a0d0a
tls13-open-padding 22 14000020aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
tls13-open-maximum 23 16384 f3336bea752b5a28743033dd2c844a4a63fba08871aaee2586a2bf2d69be83a2
tls13-open-maximum-padding 21 0102
tls13-open-alert 21 0100
record-err-001 badRecordMac
record-err-002 badRecordMac
record-err-003 badRecordMac
record-err-004 badRecordMac
record-err-005 recordOverflow
record-err-006 recordOverflow
record-err-007 unexpectedMessage
record-err-008 unexpectedMessage
record-err-009 unexpectedMessage
record-err-010 unexpectedMessage
record-err-011 decodeError
record-err-012 decodeError
record-err-019 invalidIvLength
```

<!-- test: tls-record.open-tls12 -->
`Tls12RecordProtection.open`: OpenSSL's TLS 1.2 records under AES-128-GCM, AES-256-GCM and ChaCha20-Poly1305, an explicit nonce taken as sent, the largest record and every refusal. A line prints the content type and the plaintext, or the length and SHA-256 of one over 4000 bytes. Vectors tls12-open-openssl-ecdsa-aes128-0 to record-err-033 (16).
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

function fill(byteHex String, count Count) returns ByteArray
	let byte = try fromHex(byteHex).get(0) otherwise panic("a fill byte")
	var bytes = ByteArray.create()

	for _ in 0 upto count 'eachCopy'
		bytes.push(byte)
	end 'eachCopy'

	return bytes
end 'fill'

typealias Sequence = int(0 to u64.max)

type Sealer
	var aead as Aead

	static function aesGcm(key ByteArray) returns Sealer
		return Self{aead: try AesGcm.create(key) otherwise panic("a case key of an AES size")}
	end 'aesGcm'

	static function chaCha20Poly1305(key ByteArray) returns Sealer
		return Self{aead: try ChaCha20Poly1305.create(key) otherwise panic("a case key of 32 bytes")}
	end 'chaCha20Poly1305'

	function seal(nonce ByteArray, plaintext ByteArray, additionalData ByteArray) returns ByteArray
		return try aead.seal(nonce, plaintext: plaintext, additionalData: additionalData) otherwise panic("a case plaintext within the AEAD limit")
	end 'seal'
end 'Sealer'

function content(headHex String, byteHex String, count Count, tailHex String) returns ByteArray
	var bytes = fromHex(headHex)
	bytes.append(fill(byteHex, count: count))
	bytes.append(fromHex(tailHex))

	return bytes
end 'content'

function bigEndian(value Sequence) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto 8 'eachByte'
		bytes.push(((value shr ((7 - index) * 8)) and 255) as Byte)
	end 'eachByte'

	return bytes
end 'bigEndian'

function xorNonce(iv ByteArray, sequenceNumber Sequence) returns ByteArray
	let sequenceBytes = bigEndian(sequenceNumber)
	var nonce = ByteArray.create()

	for (iter, byte) in iv.withIterator() 'eachByte'
		if iter.index() < 4 'prefix'
			nonce.push(byte)
		end 'prefix' else 'masked'
			nonce.push(byte xor (try sequenceBytes.get(iter.index() - 4) otherwise panic("eight sequence bytes")))
		end 'masked'
	end 'eachByte'

	return nonce
end 'xorNonce'

function header(typeByte Byte, length Count) returns ByteArray
	var bytes = ByteArray from [typeByte, 3, 3]
	bytes.push((length shr 8) as Byte)
	bytes.push((length and 255) as Byte)

	return bytes
end 'header'

function sealed12(sealer Sealer, ivHex String, sequenceNumber Sequence, typeByte Byte, plaintext ByteArray, explicitNonce bool) returns ByteArray
	var additionalData = bigEndian(sequenceNumber)
	additionalData.append(header(typeByte, length: plaintext.count()))

	var body = ByteArray.create()
	var nonce = xorNonce(fromHex(ivHex), sequenceNumber: sequenceNumber)

	if explicitNonce 'explicit'
		body = bigEndian(sequenceNumber)
		nonce = fromHex(ivHex)
		nonce.append(body)
	end 'explicit'

	body.append(sealer.seal(nonce, plaintext: plaintext, additionalData: additionalData))

	var record = header(typeByte, length: body.count())
	record.append(body)

	return record
end 'sealed12'

function raw(headerHex String, byteHex String, count Count) returns ByteArray
	var bytes = fromHex(headerHex)
	bytes.append(fill(byteHex, count: count))

	return bytes
end 'raw'

function openTls12(suite Tls12CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray) returns TlsOpenedRecord throws TlsRecordError
	var protection = try Tls12RecordProtection.create(suite, key: fromHex(keyHex), iv: fromHex(ivHex), sequenceNumber: sequenceNumber)

	return try protection.open(record)
end 'openTls12'

function report(id String, suite Tls12CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, record ByteArray)
	if let opened = try openTls12(suite, keyHex: keyHex, ivHex: ivHex, sequenceNumber: sequenceNumber, record: record) 'opened'
		let plaintext = opened.plaintext()

		if plaintext.count() > 4000 'long'
			print("{id} {opened.contentType().rawValue} {plaintext.count()} {toHex(sha256(plaintext))}\n")
		end 'long' else 'short'
			print("{id} {opened.contentType().rawValue} {toHex(plaintext)}\n")
		end 'short'
	end 'opened' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("tls12-open-openssl-ecdsa-aes128-0", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "164b80ed18c8b5e0c935c82e3d56b077", ivHex: "db9400d6", sequenceNumber: 0, record: fromHex("1603030028bcd8909ae9a17fd50d8ca1343018bef9bdb4f090a6e0a700e0ff087e4cc13287e2087d797061ee70"))
	report("tls12-open-openssl-ecdsa-aes128-1", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "164b80ed18c8b5e0c935c82e3d56b077", ivHex: "db9400d6", sequenceNumber: 1, record: fromHex("170303002abcd8909ae9a17fd6257db1598c783c40524483b5023fb27e9f59a251edb817cc3661267b7f873adb9061"))
	report("tls12-open-openssl-ecdsa-aes256-0", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, keyHex: "081b34e3295a0a114c155e085fa3d23b955483bb78429344fe925d61fa073dce", ivHex: "c759780c", sequenceNumber: 0, record: fromHex("1603030028ea49d2c119cd7cf5eec5b73f2ccb182663d6c77e055667f6c41c44e4e88eb5261e2dc3655e956480"))
	report("tls12-open-openssl-ecdsa-aes256-1", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, keyHex: "081b34e3295a0a114c155e085fa3d23b955483bb78429344fe925d61fa073dce", ivHex: "c759780c", sequenceNumber: 1, record: fromHex("170303002aea49d2c119cd7cf62ad654bc3cbc11ccbdc102871bc3a50f50a42228ef36c4f53f5d0dddd2111e28e486"))
	report("tls12-open-openssl-rsa-chacha-0", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "e5e6001a6b3c7ef3f10e602df1a9d60e10f94c56a6aeecef18c7f82ed63d1eed", ivHex: "76918e523b6ceadee0503778", sequenceNumber: 0, record: fromHex("1603030020e45a59e7f7e8b5b9cdd484cdfc839b274ca0c874e4444c627d0c9397ad203e2d"))
	report("tls12-open-openssl-rsa-chacha-1", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "e5e6001a6b3c7ef3f10e602df1a9d60e10f94c56a6aeecef18c7f82ed63d1eed", ivHex: "76918e523b6ceadee0503778", sequenceNumber: 1, record: fromHex("17030300227a6ffa0a766df319810ccceeb85b53175475149536a6378b2d97b17814fcafa24796"))
	report("tls12-open-maximum", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 7, record: sealed12(Sealer.chaCha20Poly1305(fromHex("d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 7, typeByte: 23, plaintext: content("", byteHex: "62", count: 16384, tailHex: ""), explicitNonce: false))
	report("record-err-021", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 6, record: fromHex("170303002a0000000000000005ab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde2"))
	report("record-err-022", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, record: fromHex("170303002a00000000000000ffab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde2"))
	report("record-err-023", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, record: fromHex("17030300170000000000000000000000000000000000000000000000"))
	report("record-err-024", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, record: fromHex("170301002a0000000000000005ab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde2"))
	report("record-err-025", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 0, record: raw("1703034801", byteHex: "00", count: 18433))
	report("record-err-026", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: sealed12(Sealer.chaCha20Poly1305(fromHex("d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115")), ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, typeByte: 23, plaintext: content("", byteHex: "62", count: 16385, tailHex: ""), explicitNonce: false))
	report("record-err-027", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, record: fromHex("140303002a0000000000000005ab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde2"))
	report("record-err-032", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, record: fromHex("170303002a0000000000000005ab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde200"))
	report("record-err-033", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, record: fromHex("170303000f000000000000000000000000000000"))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tls12-open-openssl-ecdsa-aes128-0 22 1400000c7a5105dab2e55bf43fbca44b
tls12-open-openssl-ecdsa-aes128-1 23 68656c6c6f2066726f6d206f70656e73736c
tls12-open-openssl-ecdsa-aes256-0 22 1400000ccc7491c8da2fc8ace320ded4
tls12-open-openssl-ecdsa-aes256-1 23 68656c6c6f2066726f6d206f70656e73736c
tls12-open-openssl-rsa-chacha-0 22 1400000c0d7d12c85265cacc4a35759c
tls12-open-openssl-rsa-chacha-1 23 68656c6c6f2066726f6d206f70656e73736c
tls12-open-maximum 23 16384 e33f24140499430b048f6600af4f41f3ccb0cb766d9f7661124cf8ba4b827523
record-err-021 badRecordMac
record-err-022 badRecordMac
record-err-023 badRecordMac
record-err-024 protocolVersion
record-err-025 recordOverflow
record-err-026 recordOverflow
record-err-027 unexpectedMessage
record-err-032 decodeError
record-err-033 badRecordMac
```

<!-- test: tls-record.seal-tls13 -->
`Tls13RecordProtection.seal`: the records of RFC 8448 sections 3 and 5, every suite, padding, sequence numbers up to 2^64 - 1, the largest records and every refusal. A line prints the record, or the length and SHA-256 of one over 4000 bytes. Vectors tls13-seal-rfc8448-3-1 to record-err-018 (35).
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

function fill(byteHex String, count Count) returns ByteArray
	let byte = try fromHex(byteHex).get(0) otherwise panic("a fill byte")
	var bytes = ByteArray.create()

	for _ in 0 upto count 'eachCopy'
		bytes.push(byte)
	end 'eachCopy'

	return bytes
end 'fill'

function sealTls13(suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, contentType TlsContentType, plaintext ByteArray, paddingLength TlsPaddingLength) returns ByteArray throws TlsRecordError
	var protection = try Tls13RecordProtection.create(suite, key: fromHex(keyHex), iv: fromHex(ivHex), sequenceNumber: sequenceNumber)

	return try protection.seal(contentType, plaintext: plaintext, paddingLength: paddingLength)
end 'sealTls13'

function report(id String, suite Tls13CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, contentType TlsContentType, plaintext ByteArray, paddingLength TlsPaddingLength)
	if let record = try sealTls13(suite, keyHex: keyHex, ivHex: ivHex, sequenceNumber: sequenceNumber, contentType: contentType, plaintext: plaintext, paddingLength: paddingLength) 'sealed'
		if record.count() > 4000 'long'
			print("{id} {record.count()} {toHex(sha256(record))}\n")
		end 'long' else 'short'
			print("{id} {toHex(record)}\n")
		end 'short'
	end 'sealed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("tls13-seal-rfc8448-3-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "3fce516009c21727d0f2e4e86ee403bc", ivHex: "5d313eb2671276ee13000b30", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("080000240022000a00140012001d00170018001901000101010201030104001c00024001000000000b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de100000f000084080400805a747c5d88fa9bd2e55ab085a61015b7211f824cd484145ab3ff52f1fda8477b0b7abc90db78e2d33a5c141a078653fa6bef780c5ea248eeaaa785c4f394cab6d30bbe8d4859ee511f602957b15411ac027671459e46445c9ea58c181e818e95b8c3fb0bf3278409d3be152a3da5043e063dda65cdf5aea20d53dfacd42f74f3140000209b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "dbfaa693d1762c5b666af5d950258d01", ivHex: "5bd3c71b836e0b76bb73265f", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("14000020a8ec436d677634ae525ac1fcebe11a039ec17694fac6e98527b642f2edd5ce61"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("040000c90000001efad6aac502000000b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d3570008002a000400000400"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "17422dda596ed5d9acd890e3c63f5051", ivHex: "5b78923dee08579033e523d9", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-5", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 1, contentType: TlsContentType.applicationData, plaintext: fromHex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f3031"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-6", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "17422dda596ed5d9acd890e3c63f5051", ivHex: "5b78923dee08579033e523d9", sequenceNumber: 1, contentType: TlsContentType.alert, plaintext: fromHex("0100"), paddingLength: 0)
	report("tls13-seal-rfc8448-3-7", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "9f02283b6c9c07efc26bb9f2ac92e356", ivHex: "cf782b88dd83549aadf1e984", sequenceNumber: 2, contentType: TlsContentType.alert, plaintext: fromHex("0100"), paddingLength: 0)
	report("tls13-seal-rfc8448-5-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "4646bfac1712c426cd78d8a24a8a6f6b", ivHex: "c7d395c08d62f297d13768ea", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("080000180016000a0008000600170018001d001c00024001000000000b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de100000f0000840804008033ab13d4462707231b5dcae6c8190b63d1dabc74f28c395370da0b07e5b83066d0246a31acd95df475bfd799a4a70d33ad93d3a317a9b2c0d237a5685b219e774112e391a247607d1aeff1bbd0a39f382ee1a5fe88ae99ec59228e6497e45d48ce275a6d5ef40d169fb6f9d33b052ed3dcdd6b5a48baafffbcb290128415bd38140000208863e6bfb0420a927fa27f34336a70ae426e968e3eb884945b96856dba3976d1"), paddingLength: 0)
	report("tls13-seal-rfc8448-5-2", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "2f1f918663d590e7421149a29d94b0b6", ivHex: "414d5485235e1a688793bd74", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("1400002023f52fdb0709a55bd7f79b991f25484087bcfd4d4380b12326a52a28b2e368e1"), paddingLength: 0)
	report("tls13-seal-rfc8448-5-3", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "a7eb2a0525eb4331d58fcbf9f7ca2e9c", ivHex: "86e8be227c1bd2b3e39cb444", sequenceNumber: 0, contentType: TlsContentType.alert, plaintext: fromHex("0100"), paddingLength: 0)
	report("tls13-seal-rfc8448-5-4", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "f27a5d97bd25550c4823b0f3e5d29388", ivHex: "0dd631f7b71cbbc797c35fe7", sequenceNumber: 0, contentType: TlsContentType.alert, plaintext: fromHex("0100"), paddingLength: 0)
	report("tls13-seal-gen-1-0", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-1-1", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-1-255", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-1-663", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-1-615", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-2-0", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-2-1", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-2-255", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-2-663", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-2-615", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-3-0", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-3-1", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-3-255", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 255, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-3-663", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 281474976710663, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-gen-3-615", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 18446744073709551615, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"), paddingLength: 0)
	report("tls13-seal-padding", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 3, contentType: TlsContentType.handshake, plaintext: fromHex("14000020aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), paddingLength: 29)
	report("tls13-seal-empty-application-data", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex(""), paddingLength: 0)
	report("tls13-seal-padding-only", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex(""), paddingLength: 16384)
	report("tls13-seal-maximum", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 9, contentType: TlsContentType.applicationData, plaintext: fill("61", count: 16384), paddingLength: 0)
	report("record-err-013", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fill("61", count: 16385), paddingLength: 0)
	report("record-err-014", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fill("61", count: 16384), paddingLength: 1)
	report("record-err-015", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.changeCipherSpec, plaintext: fromHex("01"), paddingLength: 0)
	report("record-err-017", suite: Tls13CipherSuite.aes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex(""), paddingLength: 3)
	report("record-err-018", suite: Tls13CipherSuite.aes256GcmSha384, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("00"), paddingLength: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tls13-seal-rfc8448-3-1 17030302a2d1ff334a56f5bff6594a07cc87b580233f500f45e489e7f33af35edf7869fcf40aa40aa2b8ea73f848a7ca07612ef9f945cb960b4068905123ea78b111b429ba9191cd05d2a389280f526134aadc7fc78c4b729df828b5ecf7b13bd9aefb0e57f271585b8ea9bb355c7c79020716cfb9b1183ef3ab20e37d57a6b9d7477609aee6e122a4cf51427325250c7d0e509289444c9b3a648f1d71035d2ed65b0e3cdd0cbae8bf2d0b227812cbb360987255cc744110c453baa4fcd610928d809810e4b7ed1a8fd991f06aa6248204797e36a6a73b70a2559c09ead686945ba246ab66e5edd8044b4c6de3fcf2a89441ac66272fd8fb330ef8190579b3684596c960bd596eea520a56a8d650f563aad27409960dca63d3e688611ea5e22f4415cf9538d51a200c27034272968a264ed6540c84838d89f72c24461aad6d26f59ecaba9acbbb317b66d902f4f292a36ac1b639c637ce343117b659622245317b49eeda0c6258f100d7d961ffb138647e92ea330faeea6dfa31c7a84dc3bd7e1b7a6c7178af36879018e3f252107f243d243dc7339d5684c8b0378bf30244da8c87c843f5e56eb4c5e8280a2b48052cf93b16499a66db7cca71e4599426f7d461e66f99882bd89fc50800becca62d6c74116dbd2972fda1fa80f85df881edbe5a37668936b335583b599186dc5c6918a396fa48a181d6b6fa4f9d62d513afbb992f2b992f67f8afe67f76913fa388cb5630c8ca01e0c65d11c66a1e2ac4c85977b7c7a6999bbf10dc35ae69f5515614636c0b9b68c19ed2e31c0b3b66763038ebba42f3b38edc0399f3a9f23faa63978c317fc9fa66a73f60f0504de93b5b845e275592c12335ee340bbc4fddd502784016e4b3be7ef04dda49f4b440a30cb5d2af939828fd4ae3794e44f94df5a631ede42c1719bfdabf0253fe5175be898e750edc53370d2b
tls13-seal-rfc8448-3-2 170303003575ec4dc238cce60b298044a71e219c56cc77b0517fe9b93c7a4bfc44d87f38f80338ac98fc46deb384bd1caeacab6867d726c40546
tls13-seal-rfc8448-3-3 17030300de3a6b8f90414a97d6959c3487680de5134a2b240e6cffac116e95d41d6af8f6b580dcf3d11d63c758db289a015940252f55713e061dc13e078891a38efbcf5753ad8ef170ad3c7353d16d9da773b9ca7f2b9fa1b6c0d4a3d03f75e09c30ba1e62972ac46f75f7b981be63439b2999ce13064615139891d5e4c5b406f16e3fc181a77ca475840025db2f0a77f81b5ab05b94c01346755f69232c86519d86cbeeac87aac347d143f9605d64f650db4d023e70e952ca49fe5137121c74bc2697687e248746d6df353005f3bce18696129c8153556b3b6c6779b37bf15985684f
tls13-seal-rfc8448-3-4 1703030043a23f7054b62c94d0affafe8228ba55cbefacea42f914aa66bcab3f2b9819a8a5b46b395bd54a9a20441e2b62974e1f5a6292a2977014bd1e3deae63aeebb21694915e4
tls13-seal-rfc8448-3-5 17030300432e937e11ef4ac740e538ad36005fc4a46932fc3225d05f82aa1b36e30efaf97d90e6dffc602dcb501a59a8fcc49c4bf2e5f0a21c0047c2abf332540dd032e167c2955d
tls13-seal-rfc8448-3-6 1703030013c9872760655666b74d7ff1153efd6db6d0b0e3
tls13-seal-rfc8448-3-7 1703030013b58fd67166ebf599d24720cfbe7efa7a8864a9
tls13-seal-rfc8448-5-1 170303029699bee20baf5b7fc727bfab6223928a381e6d0cf9c4da653f9d2a7b23f7de11cce842d5cf75631763450ffb8b0cc1d238e658af7a12adc86243114ab14a1da2fae42621ce483fb6242eabfaad52566b02b31d2eddedefeb80e66a9900d5f973b40c4fdf74719ecf1b68d7f9c3b6ceb903ca13dd1bb8f8187ae33417e1d152522c5822a1a03ad52c838c55953d610222874cce8e1790b229a2aa0b53c8d377ee720182951dc6181dc5d90bd1f0105ed1e84aa5f75957c6661897079e5ea5007449e3197bdc7c9beeedddeafdd844afa5c315ecfe65e576afe909812880620ec7048b42d7f5c78d76f299d6d82534bdd8f512febc0ed3814aca470cd8000d3e1cb9962b052fbb950df683a52c2ba77ed3713b122937a6e5170964e2ab7969dcd980b3db9b458da7603124d6dc005e4d6e04b4d0c4baf3275db827dbba0a6db09672171fc057b3851d7e026841e2978fbd2346bbefdd0376bb1108fe9acc92189f5650aa5e85d8e8c7b67ac510dba003d3d7e16350bb66d45013efd44c9b607c0d318c4c7d1a1f5cbc57e20611804e3787d7b4a4b5f08ed8fd70bdaeade02260b12ab842ef690b4a3ee7911e841b374ecd5ebbbc2a54d047b600336dd7d0c88b4bc10e58ee6cb656de7247fa20d8e91deb84628608cf80615b62e96c1491c7ac3755eb6901405d3474fe1ac79d106a0cee56c2577fc88480f96cb6b8c681b7b68b53c146093908f350888175bdfb0b1e31ad61e30ba0adfe6d223aa03c0783b5001a57587c328a9afcfcfb978d1cd4328f7d9d60530e630befd96c0c816ee20b0100768ae2a6df51fc68f172740a79af11398ee3be1252491fa9c693479e877f94ab7c5f8cad480203e6ab7b87dd71e8a0729113df17f5eee86ce108d1d72007ec1cd13c85a6c149621e77b7d78d805a30f0be030c315e54
tls13-seal-rfc8448-5-2 1703030035d74f1923c662fd34137c6f502f3dd2b93d951d1b3bc97e42afe23c31abea92fe91b474999e85e3b791ce252fe8c3e9f939a4120cb2
tls13-seal-rfc8448-5-3 17030300132ea6cdf749196023e2b3a49491695536426047
tls13-seal-rfc8448-5-4 1703030013519fc5075cb0884349759ff9ef6f011bb4c6f2
tls13-seal-gen-1-0 1703030023c069ec3f541927d8c80b9a2c6385e8781aa8e2709d24a413900be2d69c6ecc901631a2
tls13-seal-gen-1-1 1703030023efe4105ea60a398d07c6ccc11d1ae8876149cd1bcdc41d4eddef90d3908eb28d409482
tls13-seal-gen-1-255 17030300238e58eeec1b5f91deff2eb9e489de2f5d378b04d11fd82261965fa264805e29344e00eb
tls13-seal-gen-1-663 170303002359e1c796a853c5b5099508f6bb77cf9df44783f83dca0f4f21eb8bb6ac6e84a94d6055
tls13-seal-gen-1-615 1703030023e2aaa3e4657bbe9220a0c63a88c17945cdfbf5f4109598b9825ff731a3f258b1e5cec5
tls13-seal-gen-2-0 1703030023aa09f0f32f5bf3ee965a4b433f12e7ae9e584ce3a51f1085a41267d7d7e0afb53913f5
tls13-seal-gen-2-1 170303002355fd6a595275dd2e92e58558a545a236db87ef0cb6e7ccf89008bd5c01c2d1d6e8909a
tls13-seal-gen-2-255 1703030023d9397d4fefbddcc29b5dc9956b0aa90235f9a979ce134f2c98cbb97f71cdc148b73c26
tls13-seal-gen-2-663 1703030023dd5e8fbbd958ecc44607bcc923018a146a0d4f73fd1d3bc4cbc6086be49313a7b16635
tls13-seal-gen-2-615 170303002345e70253c1e9de390123824140bdc8126965015b4b132536d52800ba2d531d06e3cab6
tls13-seal-gen-3-0 17030300232bc288b08f66d7603dccddad53dad5c12e48e14bdb55b7aaffb0c4cefa2add4b7ac14e
tls13-seal-gen-3-1 17030300231913efb96a07fa860aa216acd1651692d83176007e80f469b52a06ade81ed717f8e3f8
tls13-seal-gen-3-255 17030300232fb890686fc91e4987024ca65ad2ca1eae1d9b7fb3fd26ef8be7a1783fa41ce38abdd4
tls13-seal-gen-3-663 1703030023fdfa80651503ea7c6e0014d5e7dad779e89ead2c0ed38b8b0467ed13d7134788682b80
tls13-seal-gen-3-615 1703030023089d75881584451e7e7ed2928c04eef26e26cf93f79166726f61752f3d7465507ee782
tls13-seal-padding 1703030052938f8845daecd8b1a5cbe252214a17ec15f3e2aab9b66a0972e357ba1b4d839b86537225d6aee69da83f9f017eb171c049b2e66313ff3670709a593dc6d419083cd5c53f22e7102662c66019bf373d4958f4
tls13-seal-empty-application-data 17030300119000080c1f9cc7df62e48ae00a4b890aae
tls13-seal-padding-only 16406 b7c5825f0aec06f0e228c4ed50a8f42fa0cf78dbfa24ceb6732151ae5e273285
tls13-seal-maximum 16406 f416500a218ad97cdfe86787d7963d4d86061cebb4c84636813c07c12010b673
record-err-013 recordOverflow
record-err-014 recordOverflow
record-err-015 invalidContentType
record-err-017 emptyFragment
record-err-018 invalidKeyLength
```

<!-- test: tls-record.seal-tls12 -->
`Tls12RecordProtection.seal`: AES-GCM records whose explicit nonce is the sequence number, ChaCha20-Poly1305 records, the largest record and every refusal. A line prints the record, or the length and SHA-256 of one over 4000 bytes. Vectors tls12-seal-openssl-rsa-chacha-0 to record-err-030 (10).
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

function fill(byteHex String, count Count) returns ByteArray
	let byte = try fromHex(byteHex).get(0) otherwise panic("a fill byte")
	var bytes = ByteArray.create()

	for _ in 0 upto count 'eachCopy'
		bytes.push(byte)
	end 'eachCopy'

	return bytes
end 'fill'

function sealTls12(suite Tls12CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, contentType TlsContentType, plaintext ByteArray) returns ByteArray throws TlsRecordError
	var protection = try Tls12RecordProtection.create(suite, key: fromHex(keyHex), iv: fromHex(ivHex), sequenceNumber: sequenceNumber)

	return try protection.seal(contentType, plaintext: plaintext)
end 'sealTls12'

function report(id String, suite Tls12CipherSuite, keyHex String, ivHex String, sequenceNumber TlsSequenceNumber, contentType TlsContentType, plaintext ByteArray)
	if let record = try sealTls12(suite, keyHex: keyHex, ivHex: ivHex, sequenceNumber: sequenceNumber, contentType: contentType, plaintext: plaintext) 'sealed'
		if record.count() > 4000 'long'
			print("{id} {record.count()} {toHex(sha256(record))}\n")
		end 'long' else 'short'
			print("{id} {toHex(record)}\n")
		end 'short'
	end 'sealed' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("tls12-seal-openssl-rsa-chacha-0", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "e5e6001a6b3c7ef3f10e602df1a9d60e10f94c56a6aeecef18c7f82ed63d1eed", ivHex: "76918e523b6ceadee0503778", sequenceNumber: 0, contentType: TlsContentType.handshake, plaintext: fromHex("1400000c0d7d12c85265cacc4a35759c"))
	report("tls12-seal-openssl-rsa-chacha-1", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "e5e6001a6b3c7ef3f10e602df1a9d60e10f94c56a6aeecef18c7f82ed63d1eed", ivHex: "76918e523b6ceadee0503778", sequenceNumber: 1, contentType: TlsContentType.applicationData, plaintext: fromHex("68656c6c6f2066726f6d206f70656e73736c"))
	report("tls12-seal-gcm-128", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 5, contentType: TlsContentType.applicationData, plaintext: fromHex("474554202f20485454502f312e310d0a0d0a"))
	report("tls12-seal-gcm-256-large-seq", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "e43a3fa9", sequenceNumber: 18446744073709551615, contentType: TlsContentType.handshake, plaintext: fromHex("1400000cbbbbbbbbbbbbbbbbbbbbbbbb"))
	report("tls12-seal-chacha", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 1, contentType: TlsContentType.alert, plaintext: fromHex("0100"))
	report("tls12-seal-empty", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "f6722a5090cbc7dc31d002ea", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex(""))
	report("tls12-seal-maximum", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fill("62", count: 16384))
	report("record-err-028", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fill("61", count: 16385))
	report("record-err-029", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, keyHex: "1b1e5a3dc833c5d14e881acf5480a3ad", ivHex: "e43a3fa9", sequenceNumber: 0, contentType: TlsContentType.alert, plaintext: fromHex(""))
	report("record-err-030", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, keyHex: "d9d159e27cb8628111f956632d6e7357494a0447ab01680551a3ff412ffbc115", ivHex: "e43a3fa9", sequenceNumber: 0, contentType: TlsContentType.applicationData, plaintext: fromHex("00"))
	return 0
end 'main'
```
```exitcode
0
```
```stdout
tls12-seal-openssl-rsa-chacha-0 1603030020e45a59e7f7e8b5b9cdd484cdfc839b274ca0c874e4444c627d0c9397ad203e2d
tls12-seal-openssl-rsa-chacha-1 17030300227a6ffa0a766df319810ccceeb85b53175475149536a6378b2d97b17814fcafa24796
tls12-seal-gcm-128 170303002a0000000000000005ab5736b7769688e15b099d015bbcc37de6d82f91e545ac96ce42aeb60e84f84efde2
tls12-seal-gcm-256-large-seq 1603030028ffffffffffffffff2f68f8da9d9f91403495c84f130f5aee7e1be48c95d4e3949fdd4ecca4297095
tls12-seal-chacha 15030300125f56fca939501b61436851e76280099ddd1a
tls12-seal-empty 1703030010dd6f6629c5b48e4325a91f1c80928500
tls12-seal-maximum 16413 c306b88a9a111a228498c41fcb11eba3c2a6af213ed2c71e5de99d58a79806b9
record-err-028 recordOverflow
record-err-029 emptyFragment
record-err-030 invalidIvLength
```

<!-- test: tls-record.tls13-sequence-numbers -->
A `Tls13RecordProtection` numbers its records from the number given to `create`, 0 by default: each record it seals or opens takes the next number, a refused call keeps the number, and after 2^64 - 1 it refuses to go on.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function opened(label String, protection Tls13RecordProtection, record ByteArray)
	if let result = try protection.open(record) 'opened'
		print("{label} {result.contentType().rawValue} {toHex(result.plaintext())}\n")
	end 'opened' else (failure) 'refused'
		print("{label} {failure}\n")
	end 'refused'
end 'opened'

function sealed(label String, protection Tls13RecordProtection, contentType TlsContentType, message ByteArray)
	if let record = try protection.seal(contentType, plaintext: message) 'sealed'
		print("{label} {record.count()}\n")
	end 'sealed' else (failure) 'refused'
		print("{label} {failure}\n")
	end 'refused'
end 'sealed'

function main() returns ExitCode
	var key = ByteArray.create()
	key.resize(16)
	var iv = ByteArray.create()
	iv.resize(12)
	let suite = Tls13CipherSuite.aes128GcmSha256
	let message = "ping".toByteArray()

	var writer = try Tls13RecordProtection.create(suite, key: key, iv: iv) otherwise return 1
	let first = try writer.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	let second = try writer.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1

	var atOne = try Tls13RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 1) otherwise return 1
	let numbered = try atOne.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("second is number one: {toHex(second) == toHex(numbered)}\n")
	print("first and second differ: {toHex(first) != toHex(second)}\n")

	var refusing = try Tls13RecordProtection.create(suite, key: key, iv: iv) otherwise return 1

	sealed("seal", protection: refusing, contentType: TlsContentType.changeCipherSpec, message: message)

	let afterRefusal = try refusing.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("a refused seal keeps the number: {toHex(afterRefusal) == toHex(first)}\n")

	var reader = try Tls13RecordProtection.create(suite, key: key, iv: iv) otherwise return 1
	opened("second first", protection: reader, record: second)
	opened("first", protection: reader, record: first)
	opened("second", protection: reader, record: second)
	opened("first again", protection: reader, record: first)

	var last = try Tls13RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 18446744073709551615) otherwise return 1
	let final = try last.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("final {final.count()}\n")

	sealed("seal", protection: last, contentType: TlsContentType.applicationData, message: message)

	opened("open", protection: last, record: final)

	var lastReader = try Tls13RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 18446744073709551615) otherwise return 1
	opened("last", protection: lastReader, record: final)
	opened("beyond", protection: lastReader, record: final)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second is number one: true
first and second differ: true
seal invalidContentType
a refused seal keeps the number: true
second first badRecordMac
first 23 70696e67
second 23 70696e67
first again badRecordMac
final 26
seal sequenceNumberExhausted
open sequenceNumberExhausted
last 23 70696e67
beyond sequenceNumberExhausted
```

<!-- test: tls-record.tls12-sequence-numbers -->
A `Tls12RecordProtection` numbers its records from the number given to `create`, 0 by default: each record it seals or opens takes the next number, a refused call keeps the number, and after 2^64 - 1 it refuses to go on.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function opened(label String, protection Tls12RecordProtection, record ByteArray)
	if let result = try protection.open(record) 'opened'
		print("{label} {result.contentType().rawValue} {toHex(result.plaintext())}\n")
	end 'opened' else (failure) 'refused'
		print("{label} {failure}\n")
	end 'refused'
end 'opened'

function sealed(label String, protection Tls12RecordProtection, contentType TlsContentType, message ByteArray)
	if let record = try protection.seal(contentType, plaintext: message) 'sealed'
		print("{label} {record.count()}\n")
	end 'sealed' else (failure) 'refused'
		print("{label} {failure}\n")
	end 'refused'
end 'sealed'

function main() returns ExitCode
	var key = ByteArray.create()
	key.resize(16)
	var iv = ByteArray.create()
	iv.resize(4)
	let suite = Tls12CipherSuite.ecdheRsaAes128GcmSha256
	let message = "ping".toByteArray()

	var writer = try Tls12RecordProtection.create(suite, key: key, iv: iv) otherwise return 1
	let first = try writer.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	let second = try writer.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1

	var atOne = try Tls12RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 1) otherwise return 1
	let numbered = try atOne.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("second is number one: {toHex(second) == toHex(numbered)}\n")
	print("first and second differ: {toHex(first) != toHex(second)}\n")

	var refusing = try Tls12RecordProtection.create(suite, key: key, iv: iv) otherwise return 1

	sealed("seal", protection: refusing, contentType: TlsContentType.changeCipherSpec, message: message)

	let afterRefusal = try refusing.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("a refused seal keeps the number: {toHex(afterRefusal) == toHex(first)}\n")

	var reader = try Tls12RecordProtection.create(suite, key: key, iv: iv) otherwise return 1
	opened("second first", protection: reader, record: second)
	opened("first", protection: reader, record: first)
	opened("second", protection: reader, record: second)
	opened("first again", protection: reader, record: first)

	var last = try Tls12RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 18446744073709551615) otherwise return 1
	let final = try last.seal(TlsContentType.applicationData, plaintext: message) otherwise return 1
	print("final {final.count()}\n")

	sealed("seal", protection: last, contentType: TlsContentType.applicationData, message: message)

	opened("open", protection: last, record: final)

	var lastReader = try Tls12RecordProtection.create(suite, key: key, iv: iv, sequenceNumber: 18446744073709551615) otherwise return 1
	opened("last", protection: lastReader, record: final)
	opened("beyond", protection: lastReader, record: final)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
second is number one: true
first and second differ: true
seal invalidContentType
a refused seal keeps the number: true
second first badRecordMac
first 23 70696e67
second 23 70696e67
first again badRecordMac
final 33
seal sequenceNumberExhausted
open sequenceNumberExhausted
last 23 70696e67
beyond sequenceNumberExhausted
```

<!-- test: tls-record.a-tls13-suite-is-refused-by-tls12 -->
A TLS 1.3 suite is not a `Tls12CipherSuite`: the compiler refuses a `Tls12RecordProtection` created with one.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	key.resize(16)
	var iv = ByteArray.create()
	iv.resize(4)

	var protection = try Tls12RecordProtection.create(Tls13CipherSuite.aes128GcmSha256, key: key, iv: iv) otherwise return 1
	let record = try protection.seal(TlsContentType.handshake, plaintext: key) otherwise return 1
	print("{record.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:45: argument type mismatch for 'suite': expected 'Tls12CipherSuite', got 'Tls13CipherSuite'
```
