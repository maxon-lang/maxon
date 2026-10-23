---
feature: tls13-key-schedule
status: stable
keywords: [tls13-key-schedule, Tls13KeySchedule, Tls13CipherSuite, Tls13Secrets, Tls13TrafficKeys, Tls13KeyScheduleError, key schedule, hkdf, rfc8446, rfc8448, tls, crypto]
category: crypto
---

# `Tls13KeySchedule` — the TLS 1.3 key schedule

## Documentation

`Tls13KeySchedule` is the key schedule of RFC 8446 section 7 for a full (EC)DHE handshake without a
pre-shared key. A `Tls13CipherSuite` (`aes128GcmSha256`, `aes256GcmSha384` or `chaCha20Poly1305Sha256`, with the
code points 0x1301 to 0x1303 as raw values) selects the hash and the AEAD key size, so no function takes a
suite name that could be wrong.

| Call | Meaning |
|---|---|
| `Tls13KeySchedule.expandLabel(suite, secret: s, label: l, context: c, length: n)` | `HKDF-Expand-Label`: `n` bytes, the label without its `"tls13 "` prefix |
| `Tls13KeySchedule.deriveSecret(suite, secret: s, label: l, transcriptHash: h)` | `Derive-Secret` given the transcript hash, as many bytes as the hash |
| `Tls13KeySchedule.transcriptHash(suite, messages: m)` | the hash of the handshake messages, each with its 4-byte header |
| `Tls13KeySchedule.messageHash(suite, clientHello: m)` | the synthetic `message_hash` message that replaces the first ClientHello after a HelloRetryRequest |
| `Tls13KeySchedule.keySchedule(suite, sharedSecret: z, helloHash: h1, serverFinishedHash: h2)` | a `Tls13Secrets`: the early, handshake and master secrets, the four traffic secrets and the exporter master secret |
| `Tls13KeySchedule.trafficKeys(suite, trafficSecret: s)` | a `Tls13TrafficKeys`: the AEAD `key()` and the 12-byte `iv()` |
| `Tls13KeySchedule.finishedVerifyData(suite, baseKey: k, transcriptHash: h)` | the `verify_data` of a Finished message |
| `Tls13KeySchedule.nextApplicationTrafficSecret(suite, secret: s)` | the KeyUpdate step `application_traffic_secret_N+1` |

Every secret and hash is as long as the suite's hash (32 or 48 bytes). A call throws a
`Tls13KeyScheduleError`, and the first check that fails decides it: a secret of the wrong length is
`invalidSecretLength`, a label that is empty, longer than 249 bytes or not printable ASCII is `invalidLabel`, a
context over 255 bytes is `contextTooLong`, a length over 255 times the hash's is `outputTooLong`, a transcript
hash of the wrong length is `invalidTranscriptHashLength`, and an empty (EC)DHE shared secret is
`invalidSharedSecretLength`. `transcriptHash` and `messageHash` cannot fail.

The vectors are the canra `tls13-key-schedule` contract: every derivation, traffic key and Finished of RFC 8448
sections 3 to 7, the key schedules of TLS 1.3 handshakes with OpenSSL 3.0.13 and generated vectors for the SHA-384 and
ChaCha20-Poly1305 suites, the boundary lengths, KeyUpdate and the refusals.

The 4 canra vectors that name a value the Maxon types cannot spell have no case: `ks-err-001` (suite `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`), `ks-err-002` (suite `tls_aes_128_gcm_sha256`), `ks-err-003` (suite `TLS_AES_128_CCM_SHA256`), `ks-err-018` (suite `TLS_CHACHA20_POLY1305_SHA384`).

## Tests

<!-- test: tls13-key-schedule.derive-secret -->
`Tls13KeySchedule.deriveSecret`: every RFC 8448 derivation whose context is a transcript hash, the OpenSSL handshakes, both hash widths and the refused secrets, labels and hashes. Vectors derive-secret-001 to ks-err-020 (44).
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

function report(id String, suite Tls13CipherSuite, secretHex String, label String, transcriptHashHex String)
	if let result = try Tls13KeySchedule.deriveSecret(suite, secret: fromHex(secretHex), label: label, transcriptHash: fromHex(transcriptHashHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("derive-secret-001", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-002", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1dc826e93606aa6fdc0aadc12f741b01046aa6b99f691ed221a9f0ca043fbeac", label: "c hs traffic", transcriptHashHex: "860c06edc07858ee8e78f0e7428c58edd6b43f2ca3e6e95f02ed063cf0e1cad8")
	report("derive-secret-003", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1dc826e93606aa6fdc0aadc12f741b01046aa6b99f691ed221a9f0ca043fbeac", label: "s hs traffic", transcriptHashHex: "860c06edc07858ee8e78f0e7428c58edd6b43f2ca3e6e95f02ed063cf0e1cad8")
	report("derive-secret-004", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1dc826e93606aa6fdc0aadc12f741b01046aa6b99f691ed221a9f0ca043fbeac", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-005", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "18df06843d13a08bf2a449844c5f8a478001bc4d4c627984d5a41da8d0402919", label: "c ap traffic", transcriptHashHex: "9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13")
	report("derive-secret-006", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "18df06843d13a08bf2a449844c5f8a478001bc4d4c627984d5a41da8d0402919", label: "s ap traffic", transcriptHashHex: "9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13")
	report("derive-secret-007", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "18df06843d13a08bf2a449844c5f8a478001bc4d4c627984d5a41da8d0402919", label: "exp master", transcriptHashHex: "9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13")
	report("derive-secret-009", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "18df06843d13a08bf2a449844c5f8a478001bc4d4c627984d5a41da8d0402919", label: "res master", transcriptHashHex: "209145a96ee8e2a122ff810047cc952684658d6049e86429426db87c54ad143d")
	report("derive-secret-010", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "9b2188e9b2fc6d64d71dc329900e20bb41915000f678aa839cbb797cb7d8332c", label: "c e traffic", transcriptHashHex: "08ad0fa05d7c7233b1775ba2ff9f4c5b8b59276b7f227f13a976245f5d960913")
	report("derive-secret-011", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "9b2188e9b2fc6d64d71dc329900e20bb41915000f678aa839cbb797cb7d8332c", label: "e exp master", transcriptHashHex: "08ad0fa05d7c7233b1775ba2ff9f4c5b8b59276b7f227f13a976245f5d960913")
	report("derive-secret-012", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "9b2188e9b2fc6d64d71dc329900e20bb41915000f678aa839cbb797cb7d8332c", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-013", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "005cb112fd8eb4ccc623bb88a07c64b3ede1605363fc7d0df8c7ce4ff0fb4ae6", label: "c hs traffic", transcriptHashHex: "f736cb34fe25e701551bee6fd24c1cc7102a7daf9405cb15d97aafe16f757d03")
	report("derive-secret-014", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "005cb112fd8eb4ccc623bb88a07c64b3ede1605363fc7d0df8c7ce4ff0fb4ae6", label: "s hs traffic", transcriptHashHex: "f736cb34fe25e701551bee6fd24c1cc7102a7daf9405cb15d97aafe16f757d03")
	report("derive-secret-015", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "005cb112fd8eb4ccc623bb88a07c64b3ede1605363fc7d0df8c7ce4ff0fb4ae6", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-016", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "e2d32d4ed66dd37897a0e80c84107503ce58bf8aad4cb55a5002d77ecb890ece", label: "c ap traffic", transcriptHashHex: "b0aeffc46a2cfe33114e6fd7d51f9f04b1ca3c497dab08934a774a9d9ad7dbf3")
	report("derive-secret-017", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "e2d32d4ed66dd37897a0e80c84107503ce58bf8aad4cb55a5002d77ecb890ece", label: "s ap traffic", transcriptHashHex: "b0aeffc46a2cfe33114e6fd7d51f9f04b1ca3c497dab08934a774a9d9ad7dbf3")
	report("derive-secret-018", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "e2d32d4ed66dd37897a0e80c84107503ce58bf8aad4cb55a5002d77ecb890ece", label: "exp master", transcriptHashHex: "b0aeffc46a2cfe33114e6fd7d51f9f04b1ca3c497dab08934a774a9d9ad7dbf3")
	report("derive-secret-020", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "e2d32d4ed66dd37897a0e80c84107503ce58bf8aad4cb55a5002d77ecb890ece", label: "res master", transcriptHashHex: "c3c122e0bd907a4a3ff6112d8fd53dbf89c773d9552e8b6b9d56d361b3a97bf6")
	report("derive-secret-022", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "ce022e5e6e81e50736d773f2d3adfce8220d049bf510f0dbfac927ef4243b148", label: "c hs traffic", transcriptHashHex: "8aa8e828ec2f8a884fec95a3139de01c15a3daa7ff5bfc3f4bfcc21b438d7bf8")
	report("derive-secret-023", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "ce022e5e6e81e50736d773f2d3adfce8220d049bf510f0dbfac927ef4243b148", label: "s hs traffic", transcriptHashHex: "8aa8e828ec2f8a884fec95a3139de01c15a3daa7ff5bfc3f4bfcc21b438d7bf8")
	report("derive-secret-024", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "ce022e5e6e81e50736d773f2d3adfce8220d049bf510f0dbfac927ef4243b148", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-025", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1131545d0baf79ddce9b87f06945781a57dd18ef378dcd2060f8f9a569027ed8", label: "c ap traffic", transcriptHashHex: "50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da")
	report("derive-secret-026", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1131545d0baf79ddce9b87f06945781a57dd18ef378dcd2060f8f9a569027ed8", label: "s ap traffic", transcriptHashHex: "50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da")
	report("derive-secret-027", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1131545d0baf79ddce9b87f06945781a57dd18ef378dcd2060f8f9a569027ed8", label: "exp master", transcriptHashHex: "50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da")
	report("derive-secret-029", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "1131545d0baf79ddce9b87f06945781a57dd18ef378dcd2060f8f9a569027ed8", label: "res master", transcriptHashHex: "0e8b349158b855fdcd0c11dbbc4e83e43caa6e483c6c65df53151888e50165f4")
	report("derive-secret-031", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "d995243674fb6400d7d37bc0e9861bdbd9ed095601dcf2994874f2803de22e39", label: "c hs traffic", transcriptHashHex: "88ebc042bd0d5a643b22fca7a47defd4007dfe184949a6261c596c4e002a74a2")
	report("derive-secret-032", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "d995243674fb6400d7d37bc0e9861bdbd9ed095601dcf2994874f2803de22e39", label: "s hs traffic", transcriptHashHex: "88ebc042bd0d5a643b22fca7a47defd4007dfe184949a6261c596c4e002a74a2")
	report("derive-secret-033", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "d995243674fb6400d7d37bc0e9861bdbd9ed095601dcf2994874f2803de22e39", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-034", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "57c15d7b9d441b3d40a9c6ea8a3d730e07b3a1ea7a3339ed7070b9a74a3f4f28", label: "c ap traffic", transcriptHashHex: "5177a29af5a17f9b4933e431851d128345366c1720d38f8f0465eeeae6740372")
	report("derive-secret-035", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "57c15d7b9d441b3d40a9c6ea8a3d730e07b3a1ea7a3339ed7070b9a74a3f4f28", label: "s ap traffic", transcriptHashHex: "5177a29af5a17f9b4933e431851d128345366c1720d38f8f0465eeeae6740372")
	report("derive-secret-036", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "57c15d7b9d441b3d40a9c6ea8a3d730e07b3a1ea7a3339ed7070b9a74a3f4f28", label: "exp master", transcriptHashHex: "5177a29af5a17f9b4933e431851d128345366c1720d38f8f0465eeeae6740372")
	report("derive-secret-038", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "57c15d7b9d441b3d40a9c6ea8a3d730e07b3a1ea7a3339ed7070b9a74a3f4f28", label: "res master", transcriptHashHex: "391d004bd84c831b1582444414b4dc8064010ecc76f37f88bfeb1e88fe135c25")
	report("derive-secret-040", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "f91761354a67e9b07c6dcc3a55707efa69c4519d8040e5f215121e0df69afa4a", label: "c hs traffic", transcriptHashHex: "745c55bac399310b7b5a7c81a2c130b4d56dff6f68c3ab477857601e01f1f8d1")
	report("derive-secret-041", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "f91761354a67e9b07c6dcc3a55707efa69c4519d8040e5f215121e0df69afa4a", label: "s hs traffic", transcriptHashHex: "745c55bac399310b7b5a7c81a2c130b4d56dff6f68c3ab477857601e01f1f8d1")
	report("derive-secret-042", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "f91761354a67e9b07c6dcc3a55707efa69c4519d8040e5f215121e0df69afa4a", label: "derived", transcriptHashHex: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
	report("derive-secret-043", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "628112dae2f702488063e42de6c850a5c0820b90903e00abc31875da03d4bc5b", label: "c ap traffic", transcriptHashHex: "0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4")
	report("derive-secret-044", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "628112dae2f702488063e42de6c850a5c0820b90903e00abc31875da03d4bc5b", label: "s ap traffic", transcriptHashHex: "0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4")
	report("derive-secret-045", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "628112dae2f702488063e42de6c850a5c0820b90903e00abc31875da03d4bc5b", label: "exp master", transcriptHashHex: "0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4")
	report("derive-secret-047", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "628112dae2f702488063e42de6c850a5c0820b90903e00abc31875da03d4bc5b", label: "res master", transcriptHashHex: "a021d3a05bd418a772813875ef79b0af68c5123215427ab7333f8c27722a9fd5")
	report("derive-secret-gen-001", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", label: "c hs traffic", transcriptHashHex: "7e9f0b05adf354768b59445931f579662f7c0f98be7514f2b12fab38ef4bb82fb9f4b311e2c7a14dd0d499107edbf23b")
	report("derive-secret-gen-002", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "s ap traffic", transcriptHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-010", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: String.from(fromHex("632068732074726166666963c3a9")), transcriptHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-014", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", label: "c hs traffic", transcriptHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-020", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "", transcriptHashHex: "")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
derive-secret-001 6f2615a108c702c5678f54fc9dbab69716c076189c48250cebeac3576c3611ba
derive-secret-002 b3eddb126e067f35a780b3abf45e2d8f3b1a950738f52e9600746a0e27a55a21
derive-secret-003 b67b7d690cc16c4e75e54213cb2d37b4e9c912bcded9105d42befd59d391ad38
derive-secret-004 43de77e0c77713859a944db9db2590b53190a65b3ee2e4f12dd7a0bb7ce254b4
derive-secret-005 9e40646ce79a7f9dc05af8889bce6552875afa0b06df0087f792ebb7c17504a5
derive-secret-006 a11af9f05531f856ad47116b45a950328204b4f44bfb6b3a4b4f1f3fcb631643
derive-secret-007 fe22f881176eda18eb8f44529e6792c50c9a3f89452f68d8ae311b4309d3cf50
derive-secret-009 7df235f2031d2a051287d02b0241b0bfdaf86cc856231f2d5aba46c434ec196c
derive-secret-010 3fbbe6a60deb66c30a32795aba0eff7eaa10105586e7be5c09678d63b6caab62
derive-secret-011 b2026866610937d7423e5be90862ccf24c0e6091186d34f812089ff5be2ef7df
derive-secret-012 5f1790bbd82c5e7d376ed2e1e52f8e6038c9346db61b43be9a52f77ef3998e80
derive-secret-013 2faac08f851d35fea3604fcb4de82dc62c9b164a70974d0462e27f1ab278700f
derive-secret-014 fe927ae271312e8bf0275b581c54eef020450dc4ecffaa05a1a35d27518e7803
derive-secret-015 e2f16030251df0874ba19b9aba257610bc6d531c1dd206df0ca6e84ae2a26742
derive-secret-016 2abbf2b8e381d23dbebe1dd2a7d16a8bf484cb4950d23fb7fb7fa8547062d9a1
derive-secret-017 cc21f1bf8feb7dd5fa505bd9c4b468a9984d554a993dc49e6d285598fb672691
derive-secret-018 3fd93d4ffddc98e64b14dd107aedf8ee4add23f4510f58a4592d0b201bee56b4
derive-secret-020 5e95bdf1f89005ea2e9aa0ba85e728e3c19c5fe0c699e3f5bee59faebd0b5406
derive-secret-022 158aa7ab8855073582b41d674b4055cabcc534728f659314861b4e08e2011566
derive-secret-023 3403e781e2af7b6508da28574f6e95a1abf162de83a97927c37672a4a0cef8a1
derive-secret-024 ad1cbcd3a0dc7053eeb3ed3a47901d16a9fc63a73c64beb567481a7dfb3a2cb3
derive-secret-025 75ecf4b972525aa0dcd057c9944d4cd5d82671d8843141d7dc2a4ff15a21dc51
derive-secret-026 5c74f87df04225db0f8209c9de6429e49435fdefa7cad61864874d12f31cfc8d
derive-secret-027 7c06d3ae106a3a374ace4837b3985cac67780a6e2c5c04b58319d584df09d223
derive-secret-029 09170c6d472721566f9cf99b08699daff561ec8fb22d5a32c3f94ce009b69975
derive-secret-031 cec7a30c6872070f22a7eeb065768db67c45e29533db879908ce6dc66f5911de
derive-secret-032 8b02d3c00442a2722c4098ebe8675b23e801510f0d7ed778d8eb0b8f42a19a5e
derive-secret-033 74575526b07c81a9c1b17e6b34e0e6d084747a61f396f597ebb92c0736ec60e8
derive-secret-034 73c2e890fa8d067258d6d50fa92fe456b098cf00d9727eed91e8892ef4e6f860
derive-secret-035 c49a91faf57f8c545d5048a015bf849ff63942e4a7edcd319f8b438a97c52e21
derive-secret-036 052e39795e5f2be6e4e0974cfdd86c6a7afe3e57e5589810a3cccf642958beb2
derive-secret-038 1006dccbf40eb4eb978bff0392a9e452a4fbad58aa14784d5a241c6b49daccfb
derive-secret-040 2c3cb24a1081edb59518ee6861e89a6b72b3801afe7713e4cbbc21c0795bf831
derive-secret-041 cace3d555cc1c577cf970cff28cf978d6a9800085442e18d695b50f3151d18c8
derive-secret-042 5da12dc47835ba73fdd994b14ab7e63cc63f0d79162f6756e9a46756c8b2b642
derive-secret-043 743e4c6b56cf3909d1b06d01956ccd2c4b37758449aec41d98dae44924eaa299
derive-secret-044 b6b8144aa335ed3059c0c9c8f0ecabf7afc94af6643bdecdfd9210188fab7451
derive-secret-045 fb69121cea334db459e12272d179baca2369b643d11a6ac72b8b27a5c964feb1
derive-secret-047 0b5d4407cea0a42a3a81dd477647b7fe9180db297e5114f1ad8796b4dc475004
derive-secret-gen-001 6650e43c3f7e43437eabcacded64dcc978de66cf794c9ca08a3771492931624eb688d5ea9fe5d49105bcee143fe86e58
derive-secret-gen-002 c4a930367e1e9d3a30edd51e160a9ffd176bb1baea014335376ad3b0257d3850
ks-err-010 invalidLabel
ks-err-014 invalidTranscriptHashLength
ks-err-020 invalidSecretLength
```

<!-- test: tls13-key-schedule.expand-label -->
`Tls13KeySchedule.expandLabel`: RFC 8448 derivations whose context is not a transcript hash, the boundary labels, contexts and lengths, and the refusals. Vectors expand-label-001 to ks-err-019 (12).
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

function report(id String, suite Tls13CipherSuite, secretHex String, label String, contextHex String, length HkdfOutputLength)
	if let result = try Tls13KeySchedule.expandLabel(suite, secret: fromHex(secretHex), label: label, context: fromHex(contextHex), length: length) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("expand-label-001", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "7df235f2031d2a051287d02b0241b0bfdaf86cc856231f2d5aba46c434ec196c", label: "resumption", contextHex: "0000", length: 32)
	report("expand-label-gen-001", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "key", contextHex: "", length: 0)
	report("expand-label-gen-002", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", label: "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", contextHex: "ababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababababab", length: 48)
	report("expand-label-gen-003", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "c e traffic", contextHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b", length: 1)
	report("expand-label-gen-004", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "exporter", contextHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b", length: 4000)
	report("ks-err-004", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", label: "key", contextHex: "", length: 16)
	report("ks-err-008", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "", contextHex: "", length: 16)
	report("ks-err-009", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", contextHex: "", length: 16)
	report("ks-err-011", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "key", contextHex: "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", length: 16)
	report("ks-err-012", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: "key", contextHex: "", length: 8161)
	report("ks-err-013", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", label: "key", contextHex: "", length: 12241)
	report("ks-err-019", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", label: String.from(fromHex("7461620968657265")), contextHex: "", length: 16)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
expand-label-001 4ecd0eb6ec3b4d87f5d6028f922ca4c5851a277fd41311c9e62d2c9492e1c4f3
expand-label-gen-001 
expand-label-gen-002 d1ae86b3bbaa2c6744ffff12db4600e435c86e58070d3687af8c63b1800c3b778f3ba44981f3afa69c669b81d034ee69
expand-label-gen-003 d6
expand-label-gen-004 bd047de7b3f782ef30a54c6fac0bcf320741940bc9d50375a89c5d5d093a678e2f0ddff3ab23ee1ace7659146f1a7c710ea7f8eddb171606ff87a01a83f6d7af7cfb4157b28c170be61bf92ac4a44d21177827ccff20fee39ec647b45bf90b4d2623e91c4ad0bf2ed6429e7ae3e8b9c878edbb3e588183a4daafd28d12ac92fa73401890d5c4fb8fe7adef16e579ed9fa5882348e9d294d2e03fa0462cc28efa411b4ef197e3777287d4ccde4c6f7c1eedf2143de4056d44ec8d3568ea0ed2d6c69be0fc8ded257db363c333724298689aee9b82a4d6b4775a270d2cc0673c7def2530dbc55385856a3744df818b6ce9759e4da617c8b61b1b8d2b4d5e39b2099937f5c99fac9e95daabdb9441d2b650d7b5fd7b10dc28c1c1902246996c7006fbed112c870086151c87b00221f77f6cb252d86d4eb904ddd2321f5537a0648ec8d3362ef2e2c0f249cf9088f5b808e97a7924d85f4747ef33393daa12cce9a4d25b81abb62c1b29bfd4e692a2a32103c220ce998fd6bf2fbb57fa9fe54f90465a49631deeac2ffbcd15457d70c158d8d726bc3a7818f70283cb91940c1233953cc9785c098d1f5d0d6c2f240a64a377a40e8add9c02cfcd948363355afac799c53f702e10e697711d0e4832178a70df24c171e81a062870ebd503f1908ea9e14734620ba24b334945a883b9f954431b29199f23480816e99b5ef759e044d3b24fb9fb3c3bf2e18a826c89e5a194f0257da8932bc821518a60de175c9cb0a28b0deb33155de88bed3fafb26cf88d2e35b2c724cc43abe76404d1a9871f6e0705339c732740e5e0e6f33ddfbc3084fdd281332947c1b7cbe8af4f764a51a8a2c96219cab13ed4bf5debde7f2d5bfb13295ff9b35637b343f7569e08b975ccfa986f34c99ee89f5b9f732485986ac11a72aa46b4457a8ac01425d88103106fdc2c912cb7139a266205a97c5501f2d12b478c32a5f03c5cbacfff0b430ecbdd7145ad6d8ff3e5c0da04cc378477f18c706e455ddfc1a2923fc1176acb2f2c9de06a19f72850f3668008b5382a64bc4e98a752e9c409b108675a6230566d6626bbfc78887b2e0a1ac85b4056ff5d19e0e2e941cd377ced8bffcd2f10f75a87fc777c119de72c6cdbed07342c25283193d9814ab200909ebac79c52e503fbc82fdea4ad583dcd13907273550ebcfb9d57b0dd7009371a9ab8fb54b8f655e82110cd67c944e76b3613ffdb294f8b6ebdecdc7ccede0aa1dfe9fd7d1271fee4c13183a38f355c46978a082755c1227fff956474d216d912117d1fa9045be8fa1566109a601ac2db24da25a86c090d9977dd00f036a704ae5eb075b4405b35d4a3f381a4cdd2fb608cd121ed8c598a23ab510098a3b84b5bd582d17f788d3746dfcc2c3b43ebc4c88894d70167c00523c3ce770dfa32766e8bf5d3b8fcbba892af3ffd55fc5e2a45a2c8d564cf32e78ad94449f40c9a95983207e6a691fdd8006d4d68754dabc9010a6078ec3050e1ec78e5c6ff2f9081c0307e6a29b1cdfa63789ea988b0d1dd75cae432121ecab215b42d21630ee9b2c5261cb4e78b0806ee12a3e0d2b7194e81909b12ffb90e8577e53450a34db9f7c9a1b5dceb36f3736821924789cc41b57d9231758cec07d42380476d21cd43f72d73fdd5d40c0e05d5199c0e9ddcb9883f36d9c2cb5e002902f0f30ada69aad3fdb4f7b9b769f8408ac6f9cd40f5fe9039830830c56bd7b348437abbead010e1615059676c064298ef20d94f2d8ed48bdebabc9514084b3a4739397495993e2988cbc6105a23e607884941b17e1ff07ff0556cf6d8944612a16cf9d8965b86177944a3edbfa12553e4856eec2ed1384f7bb5fabce7beeb98cd0f965ee5b0ecb87e012702baff4adae35b148f07277dab391ce07006681f861531791d4d93c37d03036158577dbcf5219420affb90876724a1f55c44fa08619cd4380674b06e6af37645b7e6054731e48d4662345ca1ee7f76ccb16b743ed6f6384f4e63cdc19e8a06f9c4a91dfc8c0b659e2f104e2f2a06d040b9482ea5ac05698eeb304627e63319901d0c0d73ecbce6300dccdacf54c1545ffd8143f8c8e23dcd9d6d3d9dad1b6fb03f8640c5f9846662f6d5b659f651b85c5be6b9e41d9b685601d1a6dcc753f60056c853a5f89ed6dfe66b2321348531f4b964f1583b326cafe2fb073f83cc8e140568d1aee947f48f925e023e25dfcfa0192d0ac00f6e6752ce812862828b2a2858bd7c1971d3c26c3ac93f4d3d73dee0d09483785c920410e67b024b2f65af739e2cab76d62f182d156ce400ceb1fa70a547808bfe680ef3a7657ddbedccee95c86f4904aa38f3cdd6e3a6c8a35ddff6ab7ad4599dd7fe09b60a2dfd7eaca8c9cd9f9ab11e2deb443b12986f3da87e1017bc4bcce21c7bcc20c0bec1ebc07edbd0a79f4f42b4e95422d0fb8ef89172cfab542cee6098b3d22fbd7a9a47117d0e068a1e522cd0e8fdd41c8345b240d8ebfad05f5a292ad0fe0cbb501b3856eef6909f46fd46b68377e7ff445c7e86095069a765c4353117763f4b94fef4c45c6138a2f365bf079106095bffa478eee676edb165be01ece7519ad43755a9fabb6b4936697ad66874fc1d0836ccc9edaca35e5f1d23d926c81aad4efe29295f4a47ef62b72681fcf735a8bfd8642bbc985ddb4ad22f66c24c8de325b565cb7c3979fe8f0fd6203b6f662f58f0a4dba9300126b526742511b94c76f63dca639bd693aac39c4866ce62da7806424e386376fee68f7cde92b237a93f0c82c9362e1fba41a23b613e1218090d8528cb7f9f1f8920d9c7bd23af455491a508d0f8d55b4b8766860872f035dd36725f974b5b12d07bd3f791ef2dcb7fef9ab849e224d8c40eb2f1bb4bc7edd5516af05bcb62448e73b2ecdc8ffa753b98286f243e29a52d8af4639e7c7d5cdc0f34513bd3847589398383b62fb94229c2a682a806dfb1e08b973fbb6a4c8c0287dada31621f41bf48a23597f82fe5d9d1e6ed2244888fbd8df08ff77050cc1aeb7923fb0ea33be1482249c1c130a62a8ff81942c663d4cb83482835564300f23afaf9891ec7490538e5c6a80d789904fac432945edbcc07db38d9d807b8dbac1cb130595cab7c56378ad395718a2510e0c5560603b3fe9ef41bcb97e4f19b9f03eb5d39f79a3508caaa728b1edfe03f38cb39acfbc4b44b7250b56779a0a8ab5ed3e87ed59ec2bc8d0257d28cf4ece9b0a67fe216eb725eda4e6b300fb1460ec794b65037739121281cbcb347209d2247bad89108e12aa0a99b463d40f1c3b336e35eee788aee3952e4d7caefd6cd429381cd981a370a52098aeff94a2d498a5c005b2e3140ea2fafcd7a8334085be97bc5f1dbc923c388782e6115c1f90a2402810111c57d7d66c83cb674210bf9f44636d67c63829b58ef73366a73cacff2be7f8796ab93c599e061389b64cc6b1c5304e5aad61cedefeecde788a827d0411632ac53c51568859c4cfaf0e6f1557ba75359c68e2a8cb65ac81cdbb70008439f339bc7b6f30bea49805592ebaba7b7119db8caa25f0aadcdf73634eb9401b4252069fa5e797ff40608211f55108088d54f16f887f8d19711e6721b0258193066d160ff896fdc39cdf09fc3bdf4e2e3a9128785f7cafa61c47689fc5a3657da592e381be49bc5c8c5585260fb44a4f7c3cfc5eb859ef89a94eeab97108137183988d7424358a6d594196fa7268d2b4266d6e73d6c60989e0de6f36b5511776d71d16cf3d8411dbd495ebc2051298498b63e12d521f7c19640438942e58b52b973b6bcae9f280110d5ff8258ac5fa6bc11e26ad88ab6d3a8346e3b431159a87277e70fdd23ca1956f970bd4f00861be32ac196ae395a18833989200cd9ccc6b9cb992b1c289b12b626cd2ba1f7633a9de9619a7d389f3f0dca81cc33fd10853a0cae17b661ba1cd07bce7660a1d89b088248ac804f780068e66492b4e710e84190f9d801f39db22e177c9c20a43d7c8fe41bca40adf7b61aff336dc6d5102965e0ed10cdb7716dab22dc288d2297e4a1996c2b2beda27d6042194553232fc1e9f69e2244dfc66d4b32ab725c3e3e6ced0aa99ea4542871fe43a258cd429e3685fef7d133e974543c914a926704c6331863413a3b35f29fe33ce77df5c7cd63949d3ef54379876beab4e06b3fd2e1cc1ec983241ddb9725e25553e48916981b3497469cad837f1335086fc4b8c87f0d6a1b59fde129824f1eddb0016fcf94b62efc025e66d52cfeacd74ce39db87f35157c8aa85d3b9efb7222a369b77905c18b964fe81333a4f55dcecbd3a5a3891784a7d93e5205305303e510634ba7bf1387c1eaf2f9110af449a166b64dff82c343197e830c964e5e64a8a0f2ad321b25d0d3b30127afead6d56f9219bf8e1d2efeb0b6d656658fc360ce986b6e355e4d914b472b100bc781f3b97c0cdc5aa5cac9b08e8eef65dfe085a372c4e435f890a441f1b758983656ed60ee3484be3e7f8ff9bc8c8dfbfe67e9fbc0f45c5b999efe07394ba380ecb946d51b3e31ee4f448e08141c1b972f6a8d94564ce2520253e28c26e883555311219327aaab773e707fefda09d1d9008ab6396ddb473bff9aae80d89196f83bf9e6c01ca978b7b9b156a7241f109ab3372838df29ca9770dbce055a793b3cbb235c03dc6a5bff4c613f7d38a73ce77d660012e571a1e4c0e5f1e706bc6411295bf3dda1c163f212e0748255cee18ad0454176c6c077d9e24f76f23b866533a6cf3fe3385a38117b29f09b0563d50cbd4a4b634dba9b571fe201d7a99672cba40e6c533156175f365f5aa9a28888e181e5fe7cf96191731b9b1f4b346ef54d5eca296a2f0a2ee2a888a81b8f8a255d3aa699514ad4bb597f77788ac23dd066309b91aed5138d45da3e1a207818330c0fc3518f135499b7e11f38885925864f68f1200366815b6e615641435581187cd819623ee07d708c9a98aa33d0c5e6bd804c4db2d61c41fe20dc19daf34c5f21ea6a425a0427528ebcac7c3d4adcb11137ac1ded3016a363f7e18070ae041adbb1f7abc291e8f2356858db3c0aedac23f7e0e7bd9099a72810ee811d0ebe6e1a13087a3e9f83d48733ea74aaa784a3644884ec7be3e7f36a62d08fba900c4adba6e5eb7e3bf69509868eff053643cb7eb53157fe1bf693fda8d96e5d880c0a2262103412a9a4949e6ee6fd7615583a5376907d0bc85f3c92e82a7721720334c10aa58801a416d2cdead87743816a8f289339f4b8ddab63b49b5c4398dee8e697067a5556740024d2c1688879482edc6bfd363071a6000668f5bf6d390d8ac64be03c31b81f3dc3a8e872a56a68a2dbcffbe27903548868513d08745ecda9fa55c7a93c1a47c642bf49e94eefb3888c6fdea6196037d8be1ebab3d4283a8e75483e5159392c00af1eb6eb4cb85d8693e25d996aef18b39657bec38d223a31003568d8864c6c3f98582ac03059ffe017b5e8c11ddb2b25214ff22c1c1f37ef35d15232bae0d58cac19f701e05550e68be3b6f014daca871ac7dcadd7b77b2d592c46b2245b5d0004716ea86dd4adf24c86bcfa8a7d69a6c39b9db174a0011c643ab47cd9e61a84300f0be0d52295a0671a5d5c2deb5b3807f1d78c8a8c93424f35575d7c5fe79beb11c2dce002b4
ks-err-004 invalidSecretLength
ks-err-008 invalidLabel
ks-err-009 invalidLabel
ks-err-011 contextTooLong
ks-err-012 outputTooLong
ks-err-013 outputTooLong
ks-err-019 invalidLabel
```

<!-- test: tls13-key-schedule.transcript-hash -->
`Tls13KeySchedule.transcriptHash` over the messages of RFC 8448 and the OpenSSL handshakes, and over no message at all. Vectors transcript-hash-001 to transcript-hash-gen-002 (12).
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

function report(id String, suite Tls13CipherSuite, messages ByteArrays)
	let result = Tls13KeySchedule.transcriptHash(suite, messages: messages)
	print("{id} {toHex(result)}\n")
end 'report'

function main() returns ExitCode
	report("transcript-hash-001", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304")])
	report("transcript-hash-002", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000c00303cb34ecb1e78163ba1c38c6dacb196a6dffa21a8d9912ec18a2ef6283024dece7000006130113031302010000910000000b0009000006736572766572ff01000100000a00140012001d0017001800190100010101020103010400230000003300260024001d002099381de560e4bd43d23d8e435a7dbafeb3c06e51c13cae4d5413691e529aaf2c002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("020000560303a6af06a4121860dc5e6e60249cd34c95930c8ac5cb1434dac155772ed3e2692800130100002e00330024001d0020c9828876112095fe66762bdbf7c672e156d6cc253b833df1dd69b1b04e751f0f002b00020304"), fromHex("080000240022000a00140012001d00170018001901000101010201030104001c0002400100000000"), fromHex("0b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de10000"), fromHex("0f000084080400805a747c5d88fa9bd2e55ab085a61015b7211f824cd484145ab3ff52f1fda8477b0b7abc90db78e2d33a5c141a078653fa6bef780c5ea248eeaaa785c4f394cab6d30bbe8d4859ee511f602957b15411ac027671459e46445c9ea58c181e818e95b8c3fb0bf3278409d3be152a3da5043e063dda65cdf5aea20d53dfacd42f74f3"), fromHex("140000209b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718")])
	report("transcript-hash-003", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010001fc03031bc3ceb6bbe39cff938355b5a50adb6db21b7a6af649d7b4bc419d7876487d95000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d0020e4ffb68ac05f8d96c99da26698346c6be16482badddafe051a66b4f18d668f0b002a0000002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c0002400100150057000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002900dd00b800b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d357fad6aacb0021203add4fb2d8fdf822a0ca3cf7678ef5e88dae990141c5924d57bb6fa31b9e5f9d"), fromHex("0200005c03033ccfd2dec890222763472ae8136777c9d7358777bb66e91ea5122495f559ea2d00130100003400290002000000330024001d0020121761ee42c333e1b9e77b60dd57c2053cd94512ab47f115e86eff50942cea31002b00020304")])
	report("transcript-hash-004", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010001fc03031bc3ceb6bbe39cff938355b5a50adb6db21b7a6af649d7b4bc419d7876487d95000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d0020e4ffb68ac05f8d96c99da26698346c6be16482badddafe051a66b4f18d668f0b002a0000002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c0002400100150057000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000002900dd00b800b22c035d829359ee5ff7af4ec900000000262a6494dc486d2c8a34cb33fa90bf1b0070ad3c498883c9367c09a2be785abc55cd226097a3a982117283f82a03a143efd3ff5dd36d64e861be7fd61d2827db279cce145077d454a3664d4e6da4d29ee03725a6a4dafcd0fc67d2aea70529513e3da2677fa5906c5b3f7d8f92f228bda40dda721470f9fbf297b5aea617646fac5c03272e970727c621a79141ef5f7de6505e5bfbc388e93343694093934ae4d357fad6aacb0021203add4fb2d8fdf822a0ca3cf7678ef5e88dae990141c5924d57bb6fa31b9e5f9d"), fromHex("0200005c03033ccfd2dec890222763472ae8136777c9d7358777bb66e91ea5122495f559ea2d00130100003400290002000000330024001d0020121761ee42c333e1b9e77b60dd57c2053cd94512ab47f115e86eff50942cea31002b00020304"), fromHex("080000280026000a00140012001d00170018001901000101010201030104001c0002400100000000002a0000"), fromHex("1400002048d3e0e1b3d907c6acff145e16090388c77b05c050b634ab1a88bbd0dd1a34b2")])
	report("transcript-hash-005", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("fe000020de7420cc7426d2f6b221edcc9c4bdc9bb0ab048b3ddd2411da7e3a01baea6c7e"), fromHex("020000ac0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c001301000084003300020017002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002b00020304"), fromHex("010001fc0303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00080006001d001700180033004700450017004104a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002d00020101001c00024001001500af00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("020000770303bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b9800130100004f003300450017004104583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d002b00020304")])
	report("transcript-hash-006", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("fe000020de7420cc7426d2f6b221edcc9c4bdc9bb0ab048b3ddd2411da7e3a01baea6c7e"), fromHex("020000ac0303cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c001301000084003300020017002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002b00020304"), fromHex("010001fc0303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010001cd0000000b0009000006736572766572ff01000100000a00080006001d001700180033004700450017004104a6da7392ec591e17abfd535964b99894d13befb221b3def2ebe3830eac8f0151812677c4d6d2237e85cf01d6910cfb83954e76ba7352830534159897e8065780002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002c0074007271dcd04bb88bc3189119398a00000000eefafc76c146b823b096f8aacad365dd0030953f4edf625636e5f21bb2e23fcc654b1b5b40318d10d137abcbb87574e36e8a1f025f7dfa5d6e50781b5eda4aa15b0c8be778257d16aa3030e9e7841dd9e4c0342267e8ca0caf571fb2b7cff0f934b0002d00020101001c00024001001500af00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), fromHex("020000770303bb341d847fd789c47c387172dc0c9bf147fccacb5043d86ca4c598d3ff571b9800130100004f003300450017004104583e054b7a66672ae020ad9d2686fcc85b5ad41a134a0f03ee72b893052bd85b4c8de6776f5b04ac07d83540eab3e3d9c547bc6528c4317d294686093a6cad7d002b00020304"), fromHex("080000180016000a0008000600170018001d001c0002400100000000"), fromHex("0b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de10000"), fromHex("0f0000840804008033ab13d4462707231b5dcae6c8190b63d1dabc74f28c395370da0b07e5b83066d0246a31acd95df475bfd799a4a70d33ad93d3a317a9b2c0d237a5685b219e774112e391a247607d1aeff1bbd0a39f382ee1a5fe88ae99ec59228e6497e45d48ce275a6d5ef40d169fb6f9d33b052ed3dcdd6b5a48baafffbcb290128415bd38"), fromHex("140000208863e6bfb0420a927fa27f34336a70ae426e968e3eb884945b96856dba3976d1")])
	report("transcript-hash-007", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000bc03036a472236328b83af40386d3a3e1f1ce624fa4ed89ab865a4ff0f4144ce3ae2330000061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d0020089cc2671f738d9a671e5b2e464981d05b76e361aa22aea91f1d49ca10a7a362002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("0200005603033b50fdf1c3d572e40e68953e7fff4e2758459c59afa0582c0ea032874255fe6e00130100002e00330024001d00206c2e50e865919a6b5a12dfaf918f92b442567b0f89bc54478c6921366658f062002b00020304")])
	report("transcript-hash-008", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000bc03036a472236328b83af40386d3a3e1f1ce624fa4ed89ab865a4ff0f4144ce3ae2330000061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d0020089cc2671f738d9a671e5b2e464981d05b76e361aa22aea91f1d49ca10a7a362002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("0200005603033b50fdf1c3d572e40e68953e7fff4e2758459c59afa0582c0ea032874255fe6e00130100002e00330024001d00206c2e50e865919a6b5a12dfaf918f92b442567b0f89bc54478c6921366658f062002b00020304"), fromHex("080000240022000a00140012001d00170018001901000101010201030104001c0002400100000000"), fromHex("0d000027000024000d0020001e040305030603020308040805080604010501060102010402050206020202"), fromHex("0b00013b000001370001323082012e3081d5a003020102020107300a06082a8648ce3d04030230133111300f060355040313086563647361323536301e170d3136303733303031323430305a170d3236303733303031323430305a30133111300f0603550403130865636473613235363059301306072a8648ce3d020106082a8648ce3d0301070342000408d530161575f4cfe7f154ee3448180086001e88431a79ee62ee6e2f83ef38ba61e9fb37f34e007a7df4d2f5b56d1f04ece45d621f468406f5c3a15158948dd0a31a301830090603551d1304023000300b0603551d0f040403020780300a06082a8648ce3d0403020348003045022100df30fd4507f5edd22c1a6ff86db479ca693feeca3b71b3f9ef556b2937c0594d022062e2a47250d320fea83c7e2dcb5b76a50e0200c09adbd13fee946e513e011d110000"), fromHex("0f00004b040300473045022100d7a4d34bd54f55fee1a89625678c3dd5e5f60dac73ec940c5c7b9304a02084a90220289f595ed488b9ac689a3d192b1a8bb38f34af7874c059c9806a1f38269353e8"), fromHex("1400002093b70cdf4781985b96345caac701b4e750d3042df1a689d8faca812251113c11")])
	report("transcript-hash-009", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000dc03034e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d0920a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef00061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d00208e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("020000760303e5dd5948c435f7a38f0f0130708dc322d9df09abd4838117c183a7bb6d994f2c20a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef130100002e00330024001d00203e30f0f4ba551afd62768341175f5265e4daf0c8841617aa4fafdd2142320c22002b00020304")])
	report("transcript-hash-010", suite: Tls13CipherSuite.aes128GcmSha256, messages: ByteArrays from [fromHex("010000dc03034e640a3f2c2738f09c9418bd78edccd7559d0531199276d4d92a0e9ee9d77d0920a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef00061301130313020100008d0000000b0009000006736572766572ff01000100000a00140012001d00170018001901000101010201030104003300260024001d00208e7292cf3056dbb0d25fcbe55c107dc9bbf83dd9708f39203ba341249a7d9b63002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001"), fromHex("020000760303e5dd5948c435f7a38f0f0130708dc322d9df09abd4838117c183a7bb6d994f2c20a80c165581a8e0d06c0018d54d3a06dd32cfd4051eb026fad3fd0ba99269e6ef130100002e00330024001d00203e30f0f4ba551afd62768341175f5265e4daf0c8841617aa4fafdd2142320c22002b00020304"), fromHex("080000240022000a00140012001d00170018001901000101010201030104001c0002400100000000"), fromHex("0b0001b9000001b50001b0308201ac30820115a003020102020102300d06092a864886f70d01010b0500300e310c300a06035504031303727361301e170d3136303733303031323335395a170d3236303733303031323335395a300e310c300a0603550403130372736130819f300d06092a864886f70d010101050003818d0030818902818100b4bb498f8279303d980836399b36c6988c0c68de55e1bdb826d3901a2461eafd2de49a91d015abbc9a95137ace6c1af19eaa6af98c7ced43120998e187a80ee0ccb0524b1b018c3e0b63264d449a6d38e22a5fda430846748030530ef0461c8ca9d9efbfae8ea6d1d03e2bd193eff0ab9a8002c47428a6d35a8d88d79f7f1e3f0203010001a31a301830090603551d1304023000300b0603551d0f0404030205a0300d06092a864886f70d01010b05000381810085aad2a0e5b9276b908c65f73a7267170618a54c5f8a7b337d2df7a594365417f2eae8f8a58c8f8172f9319cf36b7fd6c55b80f21a03015156726096fd335e5e67f2dbf102702e608ccae6bec1fc63a42a99be5c3eb7107c3c54e9b9eb2bd5203b1c3b84e0a8b2f759409ba3eac9d91d402dcc0cc8f8961229ac9187b42b4de10000"), fromHex("0f00008408040080a2301a68dd1ceee6938fe9d40c46b9201b34d59952a37e06523a39cf8ba6c9c8b68ae94492af780516ed7b73c82812e99dd3fabea45e09d9c6848721c2808c61501b0c75e7fcaba5f78bef68a2c2b69b19558b3e40387eea93d25c7781c1cc00e9f519f7e2e4adb73e76d66089000a2dc866c2ed30bba50a0d457f19dc6eb9f3"), fromHex("14000020c8c3a8f1bff5274061f4bc3a7caffbdc9616094ca625caa65f8e76ed46db74d3")])
	report("transcript-hash-gen-001", suite: Tls13CipherSuite.aes256GcmSha384, messages: ByteArrays.create())
	report("transcript-hash-gen-002", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, messages: ByteArrays from [fromHex("0100000100"), fromHex("02000000")])
	return 0
end 'main'
```
```exitcode
0
```
```stdout
transcript-hash-001 860c06edc07858ee8e78f0e7428c58edd6b43f2ca3e6e95f02ed063cf0e1cad8
transcript-hash-002 9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13
transcript-hash-003 f736cb34fe25e701551bee6fd24c1cc7102a7daf9405cb15d97aafe16f757d03
transcript-hash-004 b0aeffc46a2cfe33114e6fd7d51f9f04b1ca3c497dab08934a774a9d9ad7dbf3
transcript-hash-005 8aa8e828ec2f8a884fec95a3139de01c15a3daa7ff5bfc3f4bfcc21b438d7bf8
transcript-hash-006 50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da
transcript-hash-007 88ebc042bd0d5a643b22fca7a47defd4007dfe184949a6261c596c4e002a74a2
transcript-hash-008 5177a29af5a17f9b4933e431851d128345366c1720d38f8f0465eeeae6740372
transcript-hash-009 745c55bac399310b7b5a7c81a2c130b4d56dff6f68c3ab477857601e01f1f8d1
transcript-hash-010 0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4
transcript-hash-gen-001 38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b
transcript-hash-gen-002 c38079b44f415ed04c56dbf5337be9c8fbe97f84ce66165f21f9b2a86e4eda3c
```

<!-- test: tls13-key-schedule.message-hash -->
`Tls13KeySchedule.messageHash`: the synthetic message that replaces the first ClientHello after a HelloRetryRequest. Vectors message-hash-rfc8448-5 to message-hash-gen-001 (2).
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

function report(id String, suite Tls13CipherSuite, clientHelloHex String)
	let result = Tls13KeySchedule.messageHash(suite, clientHello: fromHex(clientHelloHex))
	print("{id} {toHex(result)}\n")
end 'report'

function main() returns ExitCode
	report("message-hash-rfc8448-5", suite: Tls13CipherSuite.aes128GcmSha256, clientHelloHex: "010000b00303b0b1c5a5aa37c5919f2ed1d5c6fff7fcb7849716945a2b8cee9258a346677b6f000006130113031302010000810000000b0009000006736572766572ff01000100000a00080006001d00170018003300260024001d0020e8e8e3f3b93a25ed97a14a7dcacb8a272c6288e585c6484d05262fcad062ad1f002b0003020304000d0020001e040305030603020308040805080604010501060102010402050206020202002d00020101001c00024001")
	report("message-hash-gen-001", suite: Tls13CipherSuite.aes256GcmSha384, clientHelloHex: "0100000100")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
message-hash-rfc8448-5 fe000020de7420cc7426d2f6b221edcc9c4bdc9bb0ab048b3ddd2411da7e3a01baea6c7e
message-hash-gen-001 fe000030fd334e9f72f8fc359e20a9e1224e035d20a3a6fc8af705f2b53e6065c4a6b36be4719c91bb30c453f08d0a63d6762f68
```

<!-- test: tls13-key-schedule.secret-ladder -->
`Tls13KeySchedule.keySchedule`: the whole ladder for RFC 8448 sections 3, 5, 6 and 7, the OpenSSL handshakes and both hash widths. A line prints the early, handshake, client and server handshake traffic, master, client and server application traffic and exporter master secrets. Vectors key-schedule-rfc8448-3 to ks-err-017 (19).
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

function report(id String, suite Tls13CipherSuite, sharedSecretHex String, helloHashHex String, serverFinishedHashHex String)
	if let result = try Tls13KeySchedule.keySchedule(suite, sharedSecret: fromHex(sharedSecretHex), helloHash: fromHex(helloHashHex), serverFinishedHash: fromHex(serverFinishedHashHex)) 'derived'
		print("{id} {toHex(result.earlySecret())} {toHex(result.handshakeSecret())} {toHex(result.clientHandshakeTrafficSecret())} {toHex(result.serverHandshakeTrafficSecret())} {toHex(result.masterSecret())} {toHex(result.clientApplicationTrafficSecret0())} {toHex(result.serverApplicationTrafficSecret0())} {toHex(result.exporterMasterSecret())}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("key-schedule-rfc8448-3", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "8bd4054fb55b9d63fdfbacf9f04b9f0d35e6d63f537563efd46272900f89492d", helloHashHex: "860c06edc07858ee8e78f0e7428c58edd6b43f2ca3e6e95f02ed063cf0e1cad8", serverFinishedHashHex: "9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13")
	report("key-schedule-rfc8448-5", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "c142ce13ca11b5c2233652e63ad3d97844f1621fbfb9de69d547dc8fedeabeb4", helloHashHex: "8aa8e828ec2f8a884fec95a3139de01c15a3daa7ff5bfc3f4bfcc21b438d7bf8", serverFinishedHashHex: "50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da")
	report("key-schedule-rfc8448-6", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "7dc114f6475dfa7977be736ef7cbebc48c70329e8e9a74b4d7033c43f9597d4f", helloHashHex: "88ebc042bd0d5a643b22fca7a47defd4007dfe184949a6261c596c4e002a74a2", serverFinishedHashHex: "5177a29af5a17f9b4933e431851d128345366c1720d38f8f0465eeeae6740372")
	report("key-schedule-rfc8448-7", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "eef790559077db5bb63b6684e4169f051e8fb34ce59bafce2f9c8ee68cc4eb79", helloHashHex: "745c55bac399310b7b5a7c81a2c130b4d56dff6f68c3ab477857601e01f1f8d1", serverFinishedHashHex: "0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4")
	report("key-schedule-openssl-1", suite: Tls13CipherSuite.aes256GcmSha384, sharedSecretHex: "5e28506937647e13afb5584e7f47edb99fd05901b4b9d94e97baf174860d2335", helloHashHex: "1a9c068808b2e54387e71fa47c66985f6be75614c7a76c3ef76bac26ba3c70e229ef5f26cc37815b930a20b2231df2c3", serverFinishedHashHex: "98eaf6016adfa9a631474eb548a67870b0c0c9f075727199359238692f54a655a13ebd8ceed3381d9e7e9ad656af3d2c")
	report("key-schedule-openssl-2", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "f761065addf2d8f899082ac9960b0d779e9c185d6302bedacee082ca8f228c3a", helloHashHex: "46d6180062547e12b895ce5d7c29a791f9ef45cef63bee71a13d5a668cd4789f", serverFinishedHashHex: "6b03728b16c6a722c8a16ac075fa7e32b299f66462ee403d331e6de63419e0e8")
	report("key-schedule-openssl-3", suite: Tls13CipherSuite.aes256GcmSha384, sharedSecretHex: "627ee01e018235b8ce316aaceb2b5620cd8cfdac7a077c059f35391d49040a0c", helloHashHex: "eaf3ff978a2d6061cba54e328dd90d42959319db37e1b88bbab60e519d5f0352bdf1b671f4b05ab0c43494cc229749e6", serverFinishedHashHex: "beef432beb383cae9411ed22035ac55087341471bf001199de4a8228e8fa2226f1db83523d15141d28f80f1ce64ab622")
	report("key-schedule-openssl-4", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "d139846e4176dffbfff57c8d5bc73ae41efa871ce5bb63fa6845f13b725e417b", helloHashHex: "4f4181793ebd3b3c1dedb13554d50f3643ec17bdfa798f9dd286e102acf0d9d9", serverFinishedHashHex: "637769ab8f24cd40521668f09304094557a0699050a125488a53c27161a399e9")
	report("key-schedule-openssl-5", suite: Tls13CipherSuite.aes256GcmSha384, sharedSecretHex: "96a5285767144aab4ad8ddd5b1501d760658bfd6383cec17446fa026fc74e653", helloHashHex: "57865b5e4b03fb0a5524642bd509612dfed56ef379f394c7fae7c8f78e2853f778996b5d1952ea35128efc1867e01ac1", serverFinishedHashHex: "735498ccc20c45daac9cd2792c6399a9b754fd849a38e8b54de18be72bcd31954f785cdd076d4f231361a93da91e31a0")
	report("key-schedule-openssl-6", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "ab444dc35a46195806407cbca312e1119a36043b20feff686108d9540932c7d0", helloHashHex: "2c79dfc58b492db952e3b9a04ac9ac13e116686f952b3987e52c18bbc0b49c71", serverFinishedHashHex: "61946b890d291b28432d568016b6c39a393977f9d03cdd5d526f6757d678f539")
	report("key-schedule-openssl-7", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "edd24d7dedad037716f4807dd80a93ec1af37b68164d6ecfb3e4bd062152bd11", helloHashHex: "89a99a5247bd22860349adfb17b41e083149add7e179c671d6733e66cc663bc7", serverFinishedHashHex: "dc33c71612f86b15880f447de6d87ecc682ac868d118614086940aed9186478f")
	report("key-schedule-openssl-8", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "ae2bab3f7e3767000bd9a6a35ef33323865dcc0c2503a52e45a9f45ad4d6f228", helloHashHex: "78163617370fc7508736b8ecc3e6a4cd32a9ef10bedbd5dfdde1eb6210daaa56", serverFinishedHashHex: "77018f9696fa211b35807de9ca1b63346e1e8392d43a5c66db5673c02f4486e2")
	report("key-schedule-openssl-9", suite: Tls13CipherSuite.aes256GcmSha384, sharedSecretHex: "e3db47169e4820976fc1bcf680b74a5d284a0e0caebba3d529c92c84dc3e7a4e", helloHashHex: "c00daea6299dc0788ca565a2c8b91bc62a4ddceeb874997a3eae79a65303f37b0cf9d90dd12ea319908a64212100f021", serverFinishedHashHex: "8869b678c583b8abadcb20c98fa4dedc99aa74e3f050e99f39fe1f1a3cc3fd9624579076ef48573d62568e35dfa9d19c")
	report("key-schedule-openssl-10", suite: Tls13CipherSuite.aes256GcmSha384, sharedSecretHex: "9d46245d1a6f7f12476fc2bbbd60602f77795be9e5f98adaa324447b8afcdc3c", helloHashHex: "fb7ec929674b6ce7fa3e2419be820b687ebb8f8173574a94774862992c9f7fe17804dea6ca980654bc1adf3df96e5833", serverFinishedHashHex: "59e91a90402d811c049ec416c59898c69fd89e15c20a5fc7d9a4acc88c2192c210668191cd44429d37185bde241b805e")
	report("key-schedule-openssl-11", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "15864c3d1ad853e9ea10660fa2d232ab09e14edcc2d417f5d9eb1ed279987c2c", helloHashHex: "565a45adf379766b5445c2b6064ae22a1cb6bedba35e544b373a2d743a13f459", serverFinishedHashHex: "2cb559d0f8078f2c1db110574171ce555efe0e742610ee741b6c99adc32d5824")
	report("key-schedule-openssl-12", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "3b2de07375db5abd1b8271d2fa4b104f7dd1b712b257f2787ff7aaf7c0acf75b", helloHashHex: "7206ee6ff61f26417208d5c0437dc7536cbdea04b6113b10e2c6bc70b34a04fe", serverFinishedHashHex: "8b81c53b2f426fee17800f66f74393f45a2fc39796e3921a289a9b2116bebb89")
	report("key-schedule-gen-001", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, sharedSecretHex: "01", helloHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b", serverFinishedHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-016", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "01", helloHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b", serverFinishedHashHex: "7e9f0b05adf354768b59445931f579662f7c0f98be7514f2b12fab38ef4bb82fb9f4b311e2c7a14dd0d499107edbf23b")
	report("ks-err-017", suite: Tls13CipherSuite.aes128GcmSha256, sharedSecretHex: "", helloHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b", serverFinishedHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
key-schedule-rfc8448-3 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 1dc826e93606aa6fdc0aadc12f741b01046aa6b99f691ed221a9f0ca043fbeac b3eddb126e067f35a780b3abf45e2d8f3b1a950738f52e9600746a0e27a55a21 b67b7d690cc16c4e75e54213cb2d37b4e9c912bcded9105d42befd59d391ad38 18df06843d13a08bf2a449844c5f8a478001bc4d4c627984d5a41da8d0402919 9e40646ce79a7f9dc05af8889bce6552875afa0b06df0087f792ebb7c17504a5 a11af9f05531f856ad47116b45a950328204b4f44bfb6b3a4b4f1f3fcb631643 fe22f881176eda18eb8f44529e6792c50c9a3f89452f68d8ae311b4309d3cf50
key-schedule-rfc8448-5 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a ce022e5e6e81e50736d773f2d3adfce8220d049bf510f0dbfac927ef4243b148 158aa7ab8855073582b41d674b4055cabcc534728f659314861b4e08e2011566 3403e781e2af7b6508da28574f6e95a1abf162de83a97927c37672a4a0cef8a1 1131545d0baf79ddce9b87f06945781a57dd18ef378dcd2060f8f9a569027ed8 75ecf4b972525aa0dcd057c9944d4cd5d82671d8843141d7dc2a4ff15a21dc51 5c74f87df04225db0f8209c9de6429e49435fdefa7cad61864874d12f31cfc8d 7c06d3ae106a3a374ace4837b3985cac67780a6e2c5c04b58319d584df09d223
key-schedule-rfc8448-6 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a d995243674fb6400d7d37bc0e9861bdbd9ed095601dcf2994874f2803de22e39 cec7a30c6872070f22a7eeb065768db67c45e29533db879908ce6dc66f5911de 8b02d3c00442a2722c4098ebe8675b23e801510f0d7ed778d8eb0b8f42a19a5e 57c15d7b9d441b3d40a9c6ea8a3d730e07b3a1ea7a3339ed7070b9a74a3f4f28 73c2e890fa8d067258d6d50fa92fe456b098cf00d9727eed91e8892ef4e6f860 c49a91faf57f8c545d5048a015bf849ff63942e4a7edcd319f8b438a97c52e21 052e39795e5f2be6e4e0974cfdd86c6a7afe3e57e5589810a3cccf642958beb2
key-schedule-rfc8448-7 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a f91761354a67e9b07c6dcc3a55707efa69c4519d8040e5f215121e0df69afa4a 2c3cb24a1081edb59518ee6861e89a6b72b3801afe7713e4cbbc21c0795bf831 cace3d555cc1c577cf970cff28cf978d6a9800085442e18d695b50f3151d18c8 628112dae2f702488063e42de6c850a5c0820b90903e00abc31875da03d4bc5b 743e4c6b56cf3909d1b06d01956ccd2c4b37758449aec41d98dae44924eaa299 b6b8144aa335ed3059c0c9c8f0ecabf7afc94af6643bdecdfd9210188fab7451 fb69121cea334db459e12272d179baca2369b643d11a6ac72b8b27a5c964feb1
key-schedule-openssl-1 7ee8206f5570023e6dc7519eb1073bc4e791ad37b5c382aa10ba18e2357e716971f9362f2c2fe2a76bfd78dfec4ea9b5 d4181ef4e3fd1b6295a15e2a349cc901f8e47b0d7cedd017e0b90ea4c0aac0adce7b29b4517bf866691bac086d593f21 d7bde2bfde7ee88f29d86f0a1eee5d7db835b3f3b86cae1795efd41ea2819e76a293a445d6b7ab9911110d7e7863b618 4ccf25694c30d0d90222a416101def99ef2ad662875d3eaad1e842235eae536c857cbaf8009eed7d4ebad6dcad52767a 5d37b710482a96610b7ef44e10229ffbfea0b8a02c4de0cb2429815fe49662d4ebd1a750898751ab4614558805190dd0 b8bcbb9f884cd0b2b01b00adbfaa681306ead8b99e7b74b5e68d4df9fce3d9723ee0ff6237000efaebc8d54e294913a7 92144d9639656cc496aa50cc12caa4a16a181fd3bd8e51e3fc33a6e6543d848a056985c6a7b9e6034e53ffdcc1193c6a aa9c12a297b0e92b0bfa1771527ede5541343474d2eb96b3167633103293c92e08b6c8d04978d59e9d991e3d9575bc97
key-schedule-openssl-2 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 535afa38b21fe132cea5090280cc069ac55753c007025910579cdb8851dc95f7 cd0fa85ea77cdab1ddb32582f9c4633b109c68499dc70b205deb8176fd557a24 2b8fc244ced0a89d3ee5a27af68175151d4c03f730f7bb67c74cd422cb014e58 ac017360db4a0026cb143134d2d358befebb39eb5aa2cd7614ebc54a568eadaf b87de3808a3ae653d3a277842a6164615d0ec5a0bef6c1eb662f2cb5662eb55b 3fbfe34f353cc2119e7056c5fa055a7dd845bdac992bc6a367018e121c69bf96 1c41740c05d18ece2ea63c1cc7aaebe16a327c20e5cb3668ecdc08d18bc3b6cb
key-schedule-openssl-3 7ee8206f5570023e6dc7519eb1073bc4e791ad37b5c382aa10ba18e2357e716971f9362f2c2fe2a76bfd78dfec4ea9b5 26d2381a6008af51bfe534566c3664583d4f92e1abc5266f72569783837e1e6c4150c8c140801b5854b3889ae687a3f9 b8f6ccbda9bbb47a4401c38f8bf701174623e8781431b515345e5f12028039bdbb253b9366936923da87109c19a0564e f007292c9a5cb8e46c16d07d402217d9953f46ba87654e3738086757ac822e6494cb673f5bd073fbebca73cef3f9ea4a f061fe2e70c10d1f43cb7fdda929f39b25109bc4fa15c5344ce0d7e9bbe8b3b90308d31951f876fe3e0866e222e386a7 657c13a3eadd7bdae913f1d774ab007b0bcb2916951a541666244688fb72d28844e1277ce07face3c35dcea6d79dbe68 4dd62544f1d133e5de0f7441d1b10cf66ed082eb4f0254d0fed9cf20a13d688585c0f9f846a3a0f3633a7d50edf99fee 1c0fcaed4edd142ef793f6570b79c157244f6ce1b578a53c9154475e2b66c6b130456a4521b8b14d5b43506512bb17d1
key-schedule-openssl-4 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 25e329b81b477811b0f64364aa01657c06ca1ffa987138977bf0ce78a0527a64 3d1bdb13b1317dfdd392698507119f1c607c3c3e1df8f20c2142cc69045860ab 0a02c54cfe2f8cefa88f69018d7d84b4a907a1522dfd713ff4153eb335efbf4d 8f2231f96e5241acf2b4d29211763ef3239e5a1f3cf51718932b3aa8fd1ea537 bbc4c3c0c49fd0a19d3343735ea1462f91aa398a98d888c6a24cde325f403bcb 30e589cdb85c343ff31af2ca04e082f7ef88f7b436f6e5a3e9b0bfd95e8ef909 b8171c70ff416b4e7a6cb764b1cdf1aea161ae4428310fcb2009f478568d9dda
key-schedule-openssl-5 7ee8206f5570023e6dc7519eb1073bc4e791ad37b5c382aa10ba18e2357e716971f9362f2c2fe2a76bfd78dfec4ea9b5 031c48af8879e3e1c82e278cc300719f32a7bb5e97a7209a37fb0c32f63e17922ceda8b8ae7f6e699589e869d979a7ca 4e9c26fb338a0d8b2daa56cdbbd4d82bd5b274969b0ec2c95e29a3720e8b68e2be5b83f6e8d1f04dc77d9f3b2fa342dd 6f56c89a8125e4247c3ecd9a7a567ff18b05ecae44c030668f4a0b99be6b0bc911478ebf7a2a41f065b8605f84b4d88e b51414b06e39cddaae4d720bb5c7826b1ce7408eb3875371d427a597b865421c3b6c58950856880abaab26b91e7b968d 7bb0a00b14410edd78eeae74a03a7e2b7ad28d5c2fdc394b7e85fec359c49882fcd1fb78be954727ede80a3540d46632 a23712c8aba7e1e525c930e26b131c4ade6a1ce17393222ec3fcd3e99cc49928c02205240f78260a68b629df6e306c65 f35683d16658323022b9b78e8082670443fb0469da856dc90ee8e75697f6c84e1abf808c03db36a0d43351fc56e2623c
key-schedule-openssl-6 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 6243d366ee5c5582011dafe8adcd65138536e4b4190b080714424f075f5b12d8 c1b1cc252ee971a98016f58f2c55130b7f85c9ffa4d8ce8d743f43eeba289ba2 c5d693dd816c7479dd1988d8d184f0d80e3a3a4a9a96bcb57beec7962681fdce a68a9a917a4b0d72e93b8ea954e8fb8744c20562d3153f072eec79d14035c4f1 ffde792cfd2e937835a6d038cb06eb2fad3353852dabb1cb1ef6495efff8e28e 7e3419140d26e8e8fb6b48aa633262a4be238c7455b7d21e800b86431df0c13a f2eb2f5cfad781040fa04d6134bab7dac5976bc859723c3721e2e523be41ee52
key-schedule-openssl-7 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 2b2b828f9d44f415db8125ed80ded2cbbab77f3c9a00357f497010aed8370ef5 b4ffcfc31a14891f010ee2648e1903a2dc5fb9c47cf1b68e1f5c32f4e4d4c615 e001804fe56eff076524ebcf9949fc265bcd6625f5eaf0576c52ea3ef91d8605 dc9edd517317df64cbb67040fa46ceaec1e55ee26e38148df8809c548b78a982 9f5c9d981cd2407eb3d546c12e69cdce934373f1f3170ed17faa25fdbfe78139 b98a85eedcbc23f8daa71b480d6fa62888c35f57f2b8a19fcface1a1d8c50cdc d6118fb13b50cd7a2243db4a85f2bb082048e6ad11968af980be830fda2da0de
key-schedule-openssl-8 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a e49bf57a03b1826ebdabceaf5a4279f61cce0fce4d34eb7d0cb78954cd74f733 16395c7477f1238ed72a3e030025978a5e3c148ea227a0cae7d00b0953dc8fc3 d0caa31dbe7225b2264a34de2872905b4845a56f8af1511ec2d95c63816e116e 3f50da6e27089338d3e88cc54a1ec32fc1e7037d55b086afdf8812a3627488d2 4118b409935758b646c7a3f2ab4fb35046b6d1b49a875e036aa00bd392c50db6 8d9399c886f589e6cc9c44d0bcceefff134e73f95eb0c9590a40a2b738077b43 d64981bd60846d397881d88bf6b3dcd8831343646c57ad18008f29404d3c58de
key-schedule-openssl-9 7ee8206f5570023e6dc7519eb1073bc4e791ad37b5c382aa10ba18e2357e716971f9362f2c2fe2a76bfd78dfec4ea9b5 2291337bb15e34a127f7d6ab5edede73e19db998c818ced5f9fe69889f844e2916dd15cd90caad83b2aa33c3d50cef58 b21337c5f62acb0ff63eb74a19dc69ac5ed463bf517a8f7604c735fd5880783956f9b48d3eca1bab51a157013348f928 595e36684d28a949b3d3fb9655bb60da165cc92138a5b1c6ad93fcd25c90216aa4991ab82a93ea3989893fd479495865 e1bcb3c3f7cd3cd9760397f4c6fef71321a049a3f68e758bddb310997a0cf6a27cac5ab116852025797664ca0acbd6d8 100a967ff36297cb81a47cae7d163bffb112ed76ea7b899d73abc40e30705f8efb17d04e2d165c935658912ba1399cf8 9b6cc5274e6e4afad1d1466cc0d419dea2dde9328505ff976d36d46a627da80b37c622ad297814300b995a370d3ddd40 47b6fdf07b52d42f17bd9ae5be06f0a8f5f1c0ef4d417bf2c7a5b7a76c2c4d539afded74d0583d3af7576da5f2322612
key-schedule-openssl-10 7ee8206f5570023e6dc7519eb1073bc4e791ad37b5c382aa10ba18e2357e716971f9362f2c2fe2a76bfd78dfec4ea9b5 8592b2c37497e1edfbe3740d33d6caee701b87c9e2797e92bd30923912de17b6fb0400a17d198ec5e0903337de73dd6a 2d17bbb2c73255c6720f6775b0916044bcffb91c777e5e6827e14f143bab39b9fa932fc9c748f90c5d5160cb9fa7d88e 38943255fde2a2120a4f47105db37272902c9b34e2f87174fcd22bfa29a993d3ef4b082efb983028b7bdb2bc88f46592 a154cc84fa92a96251a86cabaf5cec6d4c8e874a89a884abd4e79eab67b23e7831d2aa77140edd7d966341187a2d9c42 cfb252a645901f816856b5ba77a7c500bb80bda44612cd8b7ff0aaddc2e2d3acbc5fc13ee597545ef8641a7367f86b73 b873d0b3ac70ae82f53099daa5ae076333b577757ea7139ef860d37f0617c436ed339654ecc00683efa2695300d224c3 23f3da55a05036ee79643df58d2fc8d2b20df4107065e9f66a686cc04a0622475639c72d045d901f2fd8e2b38d33d0b1
key-schedule-openssl-11 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a a398770ac44d6cb96c3d665670c956ca88d7547201883d8f01a1b6487af47ef2 d6d34be895c4a289195db02c086bce6d2114513d8cfae5cad11a62337958cdd2 91caa2fd2e6d88efe799d2d5e97fe53dd7121b64663ba157193e882992b9b818 c32e39e116751cd027bafc463501ca5de62c3bcc70089627f0f6d275039c5d9a c2a3946fdf1eefc92d2ddd11ba18260a6719f4018bc413eecd72197316be6b26 432429428c27c5255a46743a47dc5b6bcc3a8cb5bd0263203c21a93c18611583 d6d97daa06029f6dceb9e7594311a56ac91847c30ccd08a0c2de55b94ed87d2f
key-schedule-openssl-12 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a 6ddbc15d2d6ad2dd20ac199c19f98232d031a7eb3618fa01cbe3c266050d7892 466bcf4344ad13335374c9a6d4ac06f5409791af95be671408f8f3f13783af05 e5714246066b63749ddd096583d119586d2aa6b63be9ddc3ae8e17fc432652fe dba8609c092ea7ec350f4d9111c6bf1726ab8ddf5d945909ed46f7d8e120b1e2 aaf62164b59b3f20def3621ea01fc6060183dba477e76c1c0df06c203773a724 3817f90a9187e441f3b0b3616d1d6199d0ea9c0637ef1e642770a2bea77ac0a5 5b22e9ea2b432789cb1cf8903936fafdb8ea6bc8a71c91904a1e8122d3d196f9
key-schedule-gen-001 33ad0a1c607ec03b09e6cd9893680ce210adf300aa1f2660e1b22e10f170f92a a32f5c1ab543729e1a7065fe64b1c522120833d75a5ea815ffbe413b50e5c102 7bb13c6ba6ce6c7514819626f8358978b2dbc03135af9c3b1d436c5f57401c6a 553ea8e22836204240dcbd5125b8bc767ca945101f8dc4798686d28991ac736e a78ed02c8cae2f26bb47deab2e08e6f45554319f1fc0c4bd30aea6b9e03ba260 fe2e10d89f025cbc7180b3385e479acd5ef4691018f3798c0a7f57f6b552efd6 78170f27c44891eca71fbefefedd37e34d7ccba84e22e1c15b67078f2b099629 475d687851ed2155241cba2cfca71568e762053c81011eb90a9993a60d982c9e
ks-err-016 invalidTranscriptHashLength
ks-err-017 invalidSharedSecretLength
```

<!-- test: tls13-key-schedule.traffic-keys -->
`Tls13KeySchedule.trafficKeys`: the key and IV of every traffic secret of RFC 8448 and the OpenSSL handshakes, for each suite. A line prints the key then the IV. Vectors traffic-keys-001 to ks-err-005 (50).
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

function report(id String, suite Tls13CipherSuite, trafficSecretHex String)
	if let result = try Tls13KeySchedule.trafficKeys(suite, trafficSecret: fromHex(trafficSecretHex)) 'derived'
		print("{id} {toHex(result.key())} {toHex(result.iv())}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("traffic-keys-001", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "b67b7d690cc16c4e75e54213cb2d37b4e9c912bcded9105d42befd59d391ad38")
	report("traffic-keys-002", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "a11af9f05531f856ad47116b45a950328204b4f44bfb6b3a4b4f1f3fcb631643")
	report("traffic-keys-003", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "b3eddb126e067f35a780b3abf45e2d8f3b1a950738f52e9600746a0e27a55a21")
	report("traffic-keys-004", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "9e40646ce79a7f9dc05af8889bce6552875afa0b06df0087f792ebb7c17504a5")
	report("traffic-keys-005", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "3fbbe6a60deb66c30a32795aba0eff7eaa10105586e7be5c09678d63b6caab62")
	report("traffic-keys-006", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "fe927ae271312e8bf0275b581c54eef020450dc4ecffaa05a1a35d27518e7803")
	report("traffic-keys-007", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "cc21f1bf8feb7dd5fa505bd9c4b468a9984d554a993dc49e6d285598fb672691")
	report("traffic-keys-008", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "2faac08f851d35fea3604fcb4de82dc62c9b164a70974d0462e27f1ab278700f")
	report("traffic-keys-009", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "2abbf2b8e381d23dbebe1dd2a7d16a8bf484cb4950d23fb7fb7fa8547062d9a1")
	report("traffic-keys-010", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "3403e781e2af7b6508da28574f6e95a1abf162de83a97927c37672a4a0cef8a1")
	report("traffic-keys-011", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "5c74f87df04225db0f8209c9de6429e49435fdefa7cad61864874d12f31cfc8d")
	report("traffic-keys-012", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "158aa7ab8855073582b41d674b4055cabcc534728f659314861b4e08e2011566")
	report("traffic-keys-013", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "75ecf4b972525aa0dcd057c9944d4cd5d82671d8843141d7dc2a4ff15a21dc51")
	report("traffic-keys-014", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "8b02d3c00442a2722c4098ebe8675b23e801510f0d7ed778d8eb0b8f42a19a5e")
	report("traffic-keys-015", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "c49a91faf57f8c545d5048a015bf849ff63942e4a7edcd319f8b438a97c52e21")
	report("traffic-keys-016", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "cec7a30c6872070f22a7eeb065768db67c45e29533db879908ce6dc66f5911de")
	report("traffic-keys-017", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "73c2e890fa8d067258d6d50fa92fe456b098cf00d9727eed91e8892ef4e6f860")
	report("traffic-keys-018", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "cace3d555cc1c577cf970cff28cf978d6a9800085442e18d695b50f3151d18c8")
	report("traffic-keys-019", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "b6b8144aa335ed3059c0c9c8f0ecabf7afc94af6643bdecdfd9210188fab7451")
	report("traffic-keys-020", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "2c3cb24a1081edb59518ee6861e89a6b72b3801afe7713e4cbbc21c0795bf831")
	report("traffic-keys-021", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "743e4c6b56cf3909d1b06d01956ccd2c4b37758449aec41d98dae44924eaa299")
	report("traffic-keys-openssl-1-s-hs", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "4ccf25694c30d0d90222a416101def99ef2ad662875d3eaad1e842235eae536c857cbaf8009eed7d4ebad6dcad52767a")
	report("traffic-keys-openssl-1-c-ap", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "b8bcbb9f884cd0b2b01b00adbfaa681306ead8b99e7b74b5e68d4df9fce3d9723ee0ff6237000efaebc8d54e294913a7")
	report("traffic-keys-openssl-2-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "2b8fc244ced0a89d3ee5a27af68175151d4c03f730f7bb67c74cd422cb014e58")
	report("traffic-keys-openssl-2-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "b87de3808a3ae653d3a277842a6164615d0ec5a0bef6c1eb662f2cb5662eb55b")
	report("traffic-keys-openssl-3-s-hs", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "f007292c9a5cb8e46c16d07d402217d9953f46ba87654e3738086757ac822e6494cb673f5bd073fbebca73cef3f9ea4a")
	report("traffic-keys-openssl-3-c-ap", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "657c13a3eadd7bdae913f1d774ab007b0bcb2916951a541666244688fb72d28844e1277ce07face3c35dcea6d79dbe68")
	report("traffic-keys-openssl-4-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "0a02c54cfe2f8cefa88f69018d7d84b4a907a1522dfd713ff4153eb335efbf4d")
	report("traffic-keys-openssl-4-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "bbc4c3c0c49fd0a19d3343735ea1462f91aa398a98d888c6a24cde325f403bcb")
	report("traffic-keys-openssl-5-s-hs", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "6f56c89a8125e4247c3ecd9a7a567ff18b05ecae44c030668f4a0b99be6b0bc911478ebf7a2a41f065b8605f84b4d88e")
	report("traffic-keys-openssl-5-c-ap", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "7bb0a00b14410edd78eeae74a03a7e2b7ad28d5c2fdc394b7e85fec359c49882fcd1fb78be954727ede80a3540d46632")
	report("traffic-keys-openssl-6-s-hs", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "c5d693dd816c7479dd1988d8d184f0d80e3a3a4a9a96bcb57beec7962681fdce")
	report("traffic-keys-openssl-6-c-ap", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "ffde792cfd2e937835a6d038cb06eb2fad3353852dabb1cb1ef6495efff8e28e")
	report("traffic-keys-openssl-7-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "e001804fe56eff076524ebcf9949fc265bcd6625f5eaf0576c52ea3ef91d8605")
	report("traffic-keys-openssl-7-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "9f5c9d981cd2407eb3d546c12e69cdce934373f1f3170ed17faa25fdbfe78139")
	report("traffic-keys-openssl-8-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "d0caa31dbe7225b2264a34de2872905b4845a56f8af1511ec2d95c63816e116e")
	report("traffic-keys-openssl-8-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "4118b409935758b646c7a3f2ab4fb35046b6d1b49a875e036aa00bd392c50db6")
	report("traffic-keys-openssl-9-s-hs", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "595e36684d28a949b3d3fb9655bb60da165cc92138a5b1c6ad93fcd25c90216aa4991ab82a93ea3989893fd479495865")
	report("traffic-keys-openssl-9-c-ap", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "100a967ff36297cb81a47cae7d163bffb112ed76ea7b899d73abc40e30705f8efb17d04e2d165c935658912ba1399cf8")
	report("traffic-keys-openssl-10-s-hs", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "38943255fde2a2120a4f47105db37272902c9b34e2f87174fcd22bfa29a993d3ef4b082efb983028b7bdb2bc88f46592")
	report("traffic-keys-openssl-10-c-ap", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "cfb252a645901f816856b5ba77a7c500bb80bda44612cd8b7ff0aaddc2e2d3acbc5fc13ee597545ef8641a7367f86b73")
	report("traffic-keys-openssl-11-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "91caa2fd2e6d88efe799d2d5e97fe53dd7121b64663ba157193e882992b9b818")
	report("traffic-keys-openssl-11-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "c2a3946fdf1eefc92d2ddd11ba18260a6719f4018bc413eecd72197316be6b26")
	report("traffic-keys-openssl-12-s-hs", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "e5714246066b63749ddd096583d119586d2aa6b63be9ddc3ae8e17fc432652fe")
	report("traffic-keys-openssl-12-c-ap", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "aaf62164b59b3f20def3621ea01fc6060183dba477e76c1c0df06c203773a724")
	report("traffic-keys-after-update-client", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "fcdfcc72725aaee48bf64e4fd8b749cdbdbab39d90da0b26e2245ca6ea167207")
	report("traffic-keys-after-update-server", suite: Tls13CipherSuite.aes128GcmSha256, trafficSecretHex: "51921b8aa3001976eb401d0a4319a8516416a6c56001a357e5d162031e84f916")
	report("traffic-keys-gen-chacha", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, trafficSecretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb")
	report("traffic-keys-gen-aes256", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d")
	report("ks-err-005", suite: Tls13CipherSuite.aes256GcmSha384, trafficSecretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
traffic-keys-001 3fce516009c21727d0f2e4e86ee403bc 5d313eb2671276ee13000b30
traffic-keys-002 9f02283b6c9c07efc26bb9f2ac92e356 cf782b88dd83549aadf1e984
traffic-keys-003 dbfaa693d1762c5b666af5d950258d01 5bd3c71b836e0b76bb73265f
traffic-keys-004 17422dda596ed5d9acd890e3c63f5051 5b78923dee08579033e523d9
traffic-keys-005 920205a5b7bf2115e6fc5c2942834f54 6d475f0993c8e564610db2b9
traffic-keys-006 27c6bdc0a3dcea39a47326d79bc9e4ee 9569ecdd4d0536705e9ef725
traffic-keys-007 e857c690a34c5a9129d833619684f95e 0685d6b561aab9ef1013faf9
traffic-keys-008 b1530806f4adfeac83f1413032bbfa82 eb50c16be7654abf99dd06d9
traffic-keys-009 3cf122f301c6358ca7989553250efd72 ab1aec26aa78b8fc1176b9ac
traffic-keys-010 4646bfac1712c426cd78d8a24a8a6f6b c7d395c08d62f297d13768ea
traffic-keys-011 f27a5d97bd25550c4823b0f3e5d29388 0dd631f7b71cbbc797c35fe7
traffic-keys-012 2f1f918663d590e7421149a29d94b0b6 414d5485235e1a688793bd74
traffic-keys-013 a7eb2a0525eb4331d58fcbf9f7ca2e9c 86e8be227c1bd2b3e39cb444
traffic-keys-014 6cb6e60619d8c7355c5d4c4bc2be90d5 64f239530c3b888fde85e0be
traffic-keys-015 88b3123ddecadf8c1ba298e2c18176b0 4e0978513f9de8327c08e4f3
traffic-keys-016 916948f728d9823fa41a004d083f217f 64153d79bac9ea10ca5a0a88
traffic-keys-017 cdc09c806aa8f86dfcd51efc44a0c039 6ef852e78b46d913668e53e7
traffic-keys-018 041091fdab29f2c8abfb156dc5fc8d54 7464d791685de05998fcbadb
traffic-keys-019 edc4cbd0041c28cc7167441d7ca53e6a bf6c7d8e0a9545b427dcf139
traffic-keys-020 62d13c13ffd7402fc1c09e3d163665cb 7166f20028bf146dcfbd5a40
traffic-keys-021 33d7f9709756c966488ad4438437e673 c5f30d34b0e91b7d6c8eea65
traffic-keys-openssl-1-s-hs eac5c6d32526d4421c78d8e07fad58d4f4249dc60493bd3fc32a8b7eda953e7a 69d46d13a0047e4ce043ae78
traffic-keys-openssl-1-c-ap 0f66a4e1a2e560a5e3a1319162ceaf094285103e4dccef493faeb37250563f74 45923ddac5dd4e5817a281b2
traffic-keys-openssl-2-s-hs 85996abe13490c75c0cd749c515781097d2db3348465b477823f489316b5956c 764ebda094423fd80ac26419
traffic-keys-openssl-2-c-ap dc7578959c5d80318980ee1ac5499d0c4f5a0d6e5ced5655cbb8057a8c520fe1 7981b55ff0ca828c6d304738
traffic-keys-openssl-3-s-hs e05aec9708d455e1c02ce516d1782460ad1e03e446a29dddab143fa0be498fdd c4cb32cc95a39eb71692abfe
traffic-keys-openssl-3-c-ap fc239877086257c2f0ac746e542c1d7d1e629b90564c34f0335894fe0f18cfdb 06d24700e96ffbd799473913
traffic-keys-openssl-4-s-hs 7ae060170f1c96614fa73d6b7daf6b66d651eeeae8ed1960b4f5c4cfb7c6527a 80b9eeee76014a870017db5a
traffic-keys-openssl-4-c-ap 458cd56bf985ea2fbed2b0f1915e62981ead710568815bb329013f06870b99f5 4d380dd822619a6b380d171a
traffic-keys-openssl-5-s-hs 7ff358e54caa97067f36d0cc56dcc4495f26126405bbd43075aab4e188da19b1 1cb279b7f6bf91b73eac6975
traffic-keys-openssl-5-c-ap 98b0343ab134595b252edf62b8f34cd722733ab4f302f7bb166ccd73b461d7ad f2ec4440813a5b6567f66b15
traffic-keys-openssl-6-s-hs ba3f5590cff47b284d1a723ee2ef8208 b086450536abad1b586553e2
traffic-keys-openssl-6-c-ap b4ebcb610e53c7b78d6b1db364a27903 8ce03d52d7ac0aeff36bfd7a
traffic-keys-openssl-7-s-hs 25cdaeb012783eae98badb135fdeb7a8816935d495d4642020211d3888f67448 1ed205f72a090eaad40fa4bf
traffic-keys-openssl-7-c-ap a70071362b21b3df015935187f28870d0686a769515d747d54ca21c957ca733c 85d80895e23773f0de12aca1
traffic-keys-openssl-8-s-hs e5e68fc687b0dd8c3ffcbf98b962b31a80a1626aed72912936b5c1d42b07233b 1060277962cb1c7c07de0266
traffic-keys-openssl-8-c-ap 50d2a3f232d0ed6f3d3183849fcb75fec18efccf382c4165e3f72bbbb900ccce 80a7be2d406e0511bbe43e28
traffic-keys-openssl-9-s-hs 8706a90429e8186aba04eaed29220e58ef8c1bc9ec1abd3abeb6fb27a161b6e8 c7c264a2e8f3c4e68bb8c84b
traffic-keys-openssl-9-c-ap f5f1774c8485359734986780339c496b09fd4fccc90239ad4f0d12aac57a2d13 1f9ad7f6fdc67ceab574e676
traffic-keys-openssl-10-s-hs cdbe994a561d85c9e96a3abb1128d8ff16ef5e7e52465a25fbaede82cbc813c4 7671d61be870acd920785dd1
traffic-keys-openssl-10-c-ap 146c935b6f0080af0eebf411b0689961cb5f1e6ea5070abc26512ee9b0cbe4ff 835c6ab4c715daf9149f30a6
traffic-keys-openssl-11-s-hs e51766ba98a69bc8d03562a07e9b8eaaf85f166705df5f55fb3779916d82a03e 7f769c24107af2876e324457
traffic-keys-openssl-11-c-ap ee084deb032ff4296326218529e635d0e02b2d4bb0ab860bca12879c31c1bf40 28004f6a530075899042c45f
traffic-keys-openssl-12-s-hs 9cd72a2f9e8eb931c871d24a63cd3dd64313068d95a2af0ad2dd5448d51c2238 cb9783419781a3d41bc8175a
traffic-keys-openssl-12-c-ap 0809ca176efcad67d3c3284cf613b12b96947a683cbd34d3d6a9a792dc45e540 0e505a13e2f719c8c28b7a65
traffic-keys-after-update-client 3879d82f5f14056e623f2ce5bfc66fce 5dfb2c5938c3379b6cc5d1f2
traffic-keys-after-update-server 2e63be99d67b39097feb9786cf7a15a0 628a0a8298ac953baef4255a
traffic-keys-gen-chacha 3e0750e1bd8ed5418aed4b19bc0ada1526d1128aeb0423c317903bb572df0c85 a2b787eee23e9b3c46a34932
traffic-keys-gen-aes256 c727a866bd561d841f81a8a4966d7f31cbe6625c23c4b34572c011b2adbfa7f3 daaaf3d6b6e67c9e1267afda
ks-err-005 invalidSecretLength
```

<!-- test: tls13-key-schedule.finished-verify-data -->
`Tls13KeySchedule.finishedVerifyData`: the Finished messages of RFC 8448 and the PSK binder, and the refused keys and hashes. Vectors finished-001 to ks-err-015 (15).
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

function report(id String, suite Tls13CipherSuite, baseKeyHex String, transcriptHashHex String)
	if let result = try Tls13KeySchedule.finishedVerifyData(suite, baseKey: fromHex(baseKeyHex), transcriptHash: fromHex(transcriptHashHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("finished-001", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "b67b7d690cc16c4e75e54213cb2d37b4e9c912bcded9105d42befd59d391ad38", transcriptHashHex: "edb7725fa7a3473b031ec8ef65a2485493900138a2b91291407d7951a06110ed")
	report("finished-002", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "b3eddb126e067f35a780b3abf45e2d8f3b1a950738f52e9600746a0e27a55a21", transcriptHashHex: "9608102a0f1ccc6db6250b7b7e417b1a000eaada3daae4777a7686c9ff83df13")
	report("finished-003", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "69fe131a3bbad5d63c64eebcc30e395b9d8107726a13d074e389dbc8a4e47256", transcriptHashHex: "63224b2e4573f2d3454ca84b9d009a04f6be9e05711a8396473aefa01e924a14")
	report("finished-004", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "fe927ae271312e8bf0275b581c54eef020450dc4ecffaa05a1a35d27518e7803", transcriptHashHex: "04055455ef74b2322ba366cbc4cfe02723434b37b9b3671bb5a3006056d8f02f")
	report("finished-005", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "2faac08f851d35fea3604fcb4de82dc62c9b164a70974d0462e27f1ab278700f", transcriptHashHex: "9f23a00c1f08fc18807b8b68237b565df20d4c50dd8e49ef612e1a5bb16c5867")
	report("finished-006", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "3403e781e2af7b6508da28574f6e95a1abf162de83a97927c37672a4a0cef8a1", transcriptHashHex: "5614a09dfce10c45e7a58394bd6dabbefaa1d838e7f4b74966fada310ef2e77b")
	report("finished-007", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "158aa7ab8855073582b41d674b4055cabcc534728f659314861b4e08e2011566", transcriptHashHex: "50f63cbf36b0dd049e7a0ba27d6455745ea2aaac54bb167f9950b2b7ce9509da")
	report("finished-008", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "8b02d3c00442a2722c4098ebe8675b23e801510f0d7ed778d8eb0b8f42a19a5e", transcriptHashHex: "ab7bb7151312882a263c45d29e2cfb389a64ff781e227d5e5b58976653837d63")
	report("finished-009", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "cec7a30c6872070f22a7eeb065768db67c45e29533db879908ce6dc66f5911de", transcriptHashHex: "c201e184ef389b8239f5d2e374fc2a69bd8d279667ad3f889bfaa665315eab9f")
	report("finished-010", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "cace3d555cc1c577cf970cff28cf978d6a9800085442e18d695b50f3151d18c8", transcriptHashHex: "84f6da398addc92323375433a00768c17c85d4d118931aa57aceb1aa18a1a61f")
	report("finished-011", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "2c3cb24a1081edb59518ee6861e89a6b72b3801afe7713e4cbbc21c0795bf831", transcriptHashHex: "0707dcac7b2fa428cc7f691694a2590c806aaa5c0cf5087ed5385012e7f96cd4")
	report("finished-gen-001", suite: Tls13CipherSuite.aes256GcmSha384, baseKeyHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d", transcriptHashHex: "7e9f0b05adf354768b59445931f579662f7c0f98be7514f2b12fab38ef4bb82fb9f4b311e2c7a14dd0d499107edbf23b")
	report("finished-gen-002", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, baseKeyHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", transcriptHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-006", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, baseKeyHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040", transcriptHashHex: "a68199b65248f7758cc4bec306a5ebebd634444ee12a2dfe2562f1c488dfde9b")
	report("ks-err-015", suite: Tls13CipherSuite.aes128GcmSha256, baseKeyHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb", transcriptHashHex: "")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
finished-001 9b9b141d906337fbd2cbdce71df4deda4ab42c309572cb7fffee5454b78f0718
finished-002 a8ec436d677634ae525ac1fcebe11a039ec17694fac6e98527b642f2edd5ce61
finished-003 3add4fb2d8fdf822a0ca3cf7678ef5e88dae990141c5924d57bb6fa31b9e5f9d
finished-004 48d3e0e1b3d907c6acff145e16090388c77b05c050b634ab1a88bbd0dd1a34b2
finished-005 7230a9c952c25cd6138fc5e6628308c41c5335dd81b9f96bcea50fd32bda416d
finished-006 8863e6bfb0420a927fa27f34336a70ae426e968e3eb884945b96856dba3976d1
finished-007 23f52fdb0709a55bd7f79b991f25484087bcfd4d4380b12326a52a28b2e368e1
finished-008 93b70cdf4781985b96345caac701b4e750d3042df1a689d8faca812251113c11
finished-009 9afe2ba2f63a09d229d8a429e5b37ffd9fcc73bdb5911b82425972aa2892440f
finished-010 c8c3a8f1bff5274061f4bc3a7caffbdc9616094ca625caa65f8e76ed46db74d3
finished-011 692cab155cc6c100ead60733d0617f6fb09b71aa1e8c9accbbbc9e8ed336c1dd
finished-gen-001 4ff8842559394d3e335bf4a27b838b37a477bd99e95d3235024fdc684f001f85a0e3697d02f0a29509a785e036350efd
finished-gen-002 6385eed55d5bf96fa00d370961513710c1b9db0d7f70618b7faba6ed84d1ffe7
ks-err-006 invalidSecretLength
ks-err-015 invalidTranscriptHashLength
```

<!-- test: tls13-key-schedule.next-application-traffic-secret -->
`Tls13KeySchedule.nextApplicationTrafficSecret`: the KeyUpdate step, repeated, for each suite, and a refused secret. Vectors next-secret-gen-001 to ks-err-007 (9).
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

function report(id String, suite Tls13CipherSuite, secretHex String)
	if let result = try Tls13KeySchedule.nextApplicationTrafficSecret(suite, secret: fromHex(secretHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("next-secret-gen-001", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb")
	report("next-secret-gen-002", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "a1bcaabd59c9d324ba0e4899a5e80babbc17b815e3a9a562b6d5e31e7558ea3c")
	report("next-secret-gen-003", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "8e9e72691f76289e5629594736eeed41054ddac2e514577f91ad85f2898340d7c590329ab7f0fd5fba1b632d5a40976d")
	report("next-secret-gen-004", suite: Tls13CipherSuite.aes256GcmSha384, secretHex: "aff79c32d9088d5fb2e050b4d069d8564dc8e1a598c0bf2ca78e9962e60583f8f650ac02b40aa0df91be0e7f49a26cb2")
	report("next-secret-gen-005", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, secretHex: "8a96d59209dce1d7e94e4752d2072eb1c5cf9be9f377ec4cb8ade01220c040bb")
	report("next-secret-gen-006", suite: Tls13CipherSuite.chaCha20Poly1305Sha256, secretHex: "a1bcaabd59c9d324ba0e4899a5e80babbc17b815e3a9a562b6d5e31e7558ea3c")
	report("next-secret-rfc8448-client", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "9e40646ce79a7f9dc05af8889bce6552875afa0b06df0087f792ebb7c17504a5")
	report("next-secret-rfc8448-server", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "a11af9f05531f856ad47116b45a950328204b4f44bfb6b3a4b4f1f3fcb631643")
	report("ks-err-007", suite: Tls13CipherSuite.aes128GcmSha256, secretHex: "")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
next-secret-gen-001 a1bcaabd59c9d324ba0e4899a5e80babbc17b815e3a9a562b6d5e31e7558ea3c
next-secret-gen-002 44f159bc45cee645abc4af911bcbbdb7fcb6d90941f0b161fa29000c8cb6af95
next-secret-gen-003 aff79c32d9088d5fb2e050b4d069d8564dc8e1a598c0bf2ca78e9962e60583f8f650ac02b40aa0df91be0e7f49a26cb2
next-secret-gen-004 62830b2055a1984e3c60985674c731acc66428e0f8a23d90060d3b622eb28e7ebc204c1e879ae36c4d0552e1079e4b7e
next-secret-gen-005 a1bcaabd59c9d324ba0e4899a5e80babbc17b815e3a9a562b6d5e31e7558ea3c
next-secret-gen-006 44f159bc45cee645abc4af911bcbbdb7fcb6d90941f0b161fa29000c8cb6af95
next-secret-rfc8448-client fcdfcc72725aaee48bf64e4fd8b749cdbdbab39d90da0b26e2245ca6ea167207
next-secret-rfc8448-server 51921b8aa3001976eb401d0a4319a8516416a6c56001a357e5d162031e84f916
ks-err-007 invalidSecretLength
```

<!-- test: tls13-key-schedule.a-tls12-suite-is-refused -->
A TLS 1.2 suite is not a `Tls13CipherSuite`: the compiler refuses it, so no call can name a suite the TLS 1.3 key schedule does not define.
```maxon
function main() returns ExitCode
	var secret = ByteArray.create()
	secret.resize(32)

	let keys = try Tls13KeySchedule.trafficKeys(Tls12CipherSuite.ecdheRsaAes128GcmSha256, trafficSecret: secret) otherwise return 1
	print("{keys.key().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:34: argument type mismatch for 'suite': expected 'Tls13CipherSuite', got 'Tls12CipherSuite'
```
