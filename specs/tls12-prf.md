---
feature: tls12-prf
status: stable
keywords: [tls12-prf, Tls12Prf, Tls12CipherSuite, Tls12KeyBlock, Tls12Sender, Tls12PrfError, prf, master secret, extended master secret, key block, rfc5246, rfc7627, tls, crypto]
category: crypto
---

# `Tls12Prf` — the TLS 1.2 PRF and the keys derived with it

## Documentation

`Tls12Prf` is the pseudorandom function of RFC 5246 section 5 with HMAC-SHA-256 or HMAC-SHA-384, and the
derivations built on it for the ECDHE suites with AEAD ciphers. A `Tls12CipherSuite` (the six suites of
RFC 5288 and RFC 7905, with the code points 0xC02B to 0xCCA9 as raw values) selects the PRF hash and the key
and IV sizes.

| Call | Meaning |
|---|---|
| `Tls12Prf.prf(hash, secret: s, label: l, seed: d, length: n)` | `n` bytes of `P_hash(s, l ‖ d)`; `hash` is `sha256` or `sha384` |
| `Tls12Prf.masterSecret(suite, preMasterSecret: p, clientRandom: c, serverRandom: s)` | the 48-byte master secret of RFC 5246 section 8.1 |
| `Tls12Prf.extendedMasterSecret(suite, preMasterSecret: p, sessionHash: h)` | the 48-byte master secret of RFC 7627, bound to the session hash |
| `Tls12Prf.keyBlock(suite, masterSecret: m, clientRandom: c, serverRandom: s)` | a `Tls12KeyBlock`: the client and server write keys and implicit IVs (server random first in the seed) |
| `Tls12Prf.finishedVerifyData(suite, masterSecret: m, sender: x, handshakeHash: h)` | the 12-byte `verify_data` of the `Tls12Sender`'s Finished message |
| `Tls12Prf.handshakeHash(suite, messages: m)` | the PRF hash of the handshake messages, each with its 4-byte header |

A call throws a `Tls12PrfError`, and the first check that fails decides it: `unsupportedAlgorithm` for a `prf`
hash other than SHA-256 and SHA-384, `invalidLabel` for an empty or non-printable-ASCII `prf` label,
`invalidPreMasterSecretLength` for an empty pre-master secret, `invalidRandomLength` for a random that is not 32
bytes, `invalidSessionHashLength` and `invalidHandshakeHashLength` for a hash of another length than the
suite's, and `invalidMasterSecretLength` for a master secret that is not 48 bytes. `handshakeHash` cannot fail.

The vectors are the canra `tls12-prf` contract: the TLS working group PRF vectors, NIST CAVS SP 800-135 TLS
1.2 vectors, the master secrets and key blocks of TLS 1.2 handshakes with OpenSSL 3.0.13 and generated vectors for
every suite, every length from 0 to 148 around the block boundaries and the refusals.

The 5 canra vectors that name a value the Maxon types cannot spell have no case: `prf-err-001` (hash `sha1`), `prf-err-004` (suite `TLS_AES_128_GCM_SHA256`), `prf-err-005` (suite `TLS_RSA_WITH_AES_128_GCM_SHA256`), `prf-err-012` (sender `Client`), `prf-err-015` (suite `TLS_CHACHA20_POLY1305_SHA256`).

## Tests

<!-- test: tls12-prf.prf -->
`Tls12Prf.prf`: the TLS working group vectors, a whole NIST key block, lengths from 0 to 148 and every block boundary, empty and long secrets, and the refused hash and labels. Vectors prf-tlswg-sha256 to prf-err-016 (18).
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

function report(id String, hash HashAlgorithm, secretHex String, label String, seedHex String, length Tls12PrfLength)
	if let result = try Tls12Prf.prf(hash, secret: fromHex(secretHex), label: label, seed: fromHex(seedHex), length: length) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("prf-tlswg-sha256", hash: HashAlgorithm.sha256, secretHex: "9bbe436ba940f017b17652849a71db35", label: "test label", seedHex: "a0ba9f936cda311827a6f796ffd5198c", length: 100)
	report("prf-tlswg-sha384", hash: HashAlgorithm.sha384, secretHex: "b80b733d6ceefcdc71566ea48e5567df", label: "test label", seedHex: "cd665cf6a8447dd6ff8b27555edb7465", length: 148)
	report("prf-nist-sha256", hash: HashAlgorithm.sha256, secretHex: "8f1f02936082d2746987155f33941d6c5b33d5e3454d855e0860915fc1a497f8a0a9d39891a7270c60303dfff98e3829", label: "key expansion", seedHex: "3c39eb395b3ee4cf681772d9bf563c5bde7116688a927c8bea1d55c6f0bed7a9d6abd02b8314daf219ead0520085999a227e3ef1218acd3dc8f1d6898ca36b3c", length: 128)
	report("prf-nist-sha384", hash: HashAlgorithm.sha384, secretHex: "2c83548b60308b13d91b701992c36ac91f4b4aa9a23daa26cd5df1faeda39a43cc8bcc4a1faeecdc29490c2ea4534246", label: "key expansion", seedHex: "2a7eec98be7ffd77e6dfc17f16cfe3c66e59952ff5aa03c33a25ac5adf7f833791d1a103770a68cd4318a38ff7f53ec76019dad95e29dfc3b30cce83cc1611d0", length: 128)
	report("prf-gen-001", hash: HashAlgorithm.sha256, secretHex: "c52f477dfeaa5a5fb3380b95bebc99781852c278", label: "test label", seedHex: "024b546df73ac2ccb00deff32b4f46ec", length: 0)
	report("prf-gen-002", hash: HashAlgorithm.sha384, secretHex: "2a5cf88ed2e542102a469ffe204b82a4a2a50df2", label: "test label", seedHex: "5ce6d70778f77674a9c2924944367864", length: 1)
	report("prf-gen-003", hash: HashAlgorithm.sha256, secretHex: "6d48d43e11d2249c0ae3e5342098d5c754423100", label: "test label", seedHex: "d97e5b756ffc03c5a425b22e103e3a34", length: 31)
	report("prf-gen-004", hash: HashAlgorithm.sha384, secretHex: "00854cad63061bf0d8ef8b48d6924407fb920f2d", label: "test label", seedHex: "f36ff1621b13b81b7b9a6389d3ae0a33", length: 32)
	report("prf-gen-005", hash: HashAlgorithm.sha256, secretHex: "38f84a71e97e5372ca07f0177651a1f356987106", label: "test label", seedHex: "a1f2e2b224e94a4fc81e66b8d6878b2d", length: 33)
	report("prf-gen-006", hash: HashAlgorithm.sha384, secretHex: "2d51bf7fa16c5fc0ecc1278f99e62b265b0411cb", label: "test label", seedHex: "4062561c2a3111c7be496dd8416261f4", length: 48)
	report("prf-gen-007", hash: HashAlgorithm.sha256, secretHex: "605dfbedd97df921862c0ad2dc4b81baab489946", label: "test label", seedHex: "c310eb726b1bc6f84e8fe59c229c729a", length: 64)
	report("prf-gen-008", hash: HashAlgorithm.sha384, secretHex: "4c74f3d36baae8a2c741627e370201c83cc72434", label: "test label", seedHex: "9448613367d1daf69545bcc6b8f96f57", length: 65)
	report("prf-gen-009", hash: HashAlgorithm.sha256, secretHex: "576ce8ac48043356a074cbb718ae8e0692af7284", label: "test label", seedHex: "ef12c763add1c11925f120838112c5fa", length: 100)
	report("prf-gen-empty-secret", hash: HashAlgorithm.sha256, secretHex: "", label: "x", seedHex: "", length: 32)
	report("prf-gen-long-secret", hash: HashAlgorithm.sha384, secretHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", label: "client finished", seedHex: "1b133408db39267987434253e9bdf86acb6e8a3573c69c7ce4493457684dce2aad1fb3a5ca300c7cb12d2c0a3b9cd80c", length: 12)
	report("prf-err-002", hash: HashAlgorithm.sha512, secretHex: "00", label: "x", seedHex: "", length: 12)
	report("prf-err-003", hash: HashAlgorithm.sha256, secretHex: "00", label: "", seedHex: "", length: 12)
	report("prf-err-016", hash: HashAlgorithm.sha384, secretHex: "00", label: String.from(fromHex("636166c3a9")), seedHex: "", length: 12)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
prf-tlswg-sha256 e3f229ba727be17b8d122620557cd453c2aab21d07c3d495329b52d4e61edb5a6b301791e90d35c9c9a46b4e14baf9af0fa022f7077def17abfd3797c0564bab4fbc91666e9def9b97fce34f796789baa48082d122ee42c5a72e5a5110fff70187347b66
prf-tlswg-sha384 7b0c18e9ced410ed1804f2cfa34a336a1c14dffb4900bb5fd7942107e81c83cde9ca0faa60be9fe34f82b1233c9146a0e534cb400fed2700884f9dc236f80edd8bfa961144c9e8d792eca722a7b32fc3d416d473ebc2c5fd4abfdad05d9184259b5bf8cd4d90fa0d31e2dec479e4f1a26066f2eea9a69236a3e52655c9e9aee691c8f3a26854308d5eaa3be85e0990703d73e56f
prf-nist-sha256 8983d8a3a4687781f29b37a1be09db33dac94117992c996e77fb8298207e0aafea51bc829727c3488a5847348612690c3c234ea6914b15afb9f03e57f88e4388b036a57c2c59ed9a2a94bf035f4f29eeddc94bde25bb8f41b9e0c97932aaf461759e1053a884375f5563cc6f54a0102498329c2f96785e7df80f63afa3717784
prf-nist-sha384 9a2d8422e831490312f2c635c91ac647e31a14723ae6b53821c8b41497501a7d3a71f3b44d33b7bb7522116e1e3dc75c72a8ab1be8d349101e2e499fe9ec2037530756e80b14d2635968dfa08160ddcb95dfd56aacd1e70f5c7090d6423ab1e345b01a221680d3f061bc42cc1b06c73ad1776578b5a0719d5fee6b7492facad3
prf-gen-001 
prf-gen-002 3a
prf-gen-003 5b3ba3f687a28f6a59cf3c3e14378a47013fdec9e40ac8ca7bd50960b92d56
prf-gen-004 24ab0f8e1a3a1aa87c8e5f85d2221e3ac09193b779df59d5a3550e114e5ad3c9
prf-gen-005 3ae5177028c8bb5219f3fdc11cb3c945f5daf99b8f415f1aa0edd32324af7fc8da
prf-gen-006 c9cad45e8ef27a6d629c6ce8623716d88b0982255460bcfbed69991c4f395300151520e16975dfd8896107938b1b3fbb
prf-gen-007 860e61ee0a18e69db511b998f99db2c44ecb69a5c8089ef6242c175a82693a7ded0bc29391f8692593d754446df83738d67402807cb1829ab23df2a64ad17029
prf-gen-008 7ee9f20f3928b78de2c00937191d20e1c0ad5e733303c940fcff91a6b0b90739137131b46a7a09c8a1b46a0a5a6e1c3a5dae00629e586bde1ec7083ff9b7d71f25
prf-gen-009 459d0580b528747667f7fb29548521339c52a743b3a89c5ec1b6fa2f0629ba2a2275ab1a45d3901bf9195faeae5fd765c807a2ffc7166130cbfd896651d42663ae8a53091ddac469fdb47e80e53e4aaadfd0c4057b1107400eff7bee972101ae1795ebd2
prf-gen-empty-secret 4fcbb82c96fe43d5a59ca22372d8d5a4d2d02bcf9e585801d23c7b0226d5be8b
prf-gen-long-secret ed8fce68ae8cc72268aa2eee
prf-err-002 unsupportedAlgorithm
prf-err-003 invalidLabel
prf-err-016 invalidLabel
```

<!-- test: tls12-prf.master-secret -->
`Tls12Prf.masterSecret`: the NIST CAVS SP 800-135 vectors for both hashes, every suite, and the refused pre-master secrets and randoms. Vectors master-secret-nist-001 to prf-err-014 (13).
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

function report(id String, suite Tls12CipherSuite, preMasterSecretHex String, clientRandomHex String, serverRandomHex String)
	if let result = try Tls12Prf.masterSecret(suite, preMasterSecret: fromHex(preMasterSecretHex), clientRandom: fromHex(clientRandomHex), serverRandom: fromHex(serverRandomHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("master-secret-nist-001", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "f8938ecc9edebc5030c0c6a441e213cd24e6f770a50dda07876f8d55da062bcadb386b411fd4fe4313a604fce6c17fbc", clientRandomHex: "36c129d01a3200894b9179faac589d9835d58775f9b5ea3587cb8fd0364cae8c", serverRandomHex: "f6c9575ed7ddd73e1f7d16eca115415812a43c2b747daaaae043abfb50053fce")
	report("master-secret-nist-002", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "b9664b35350787f891966cc494cc29340fa6e7d2db0e85ef648b640129749988fcf7c49c17989b041745d768203a554e", clientRandomHex: "2bde249495f104e65b3b07cc88e940cd0d31c7ab6e3990dc253ff32826eea720", serverRandomHex: "cf08c14548ac478165b03c1c08bba0ff5959a0ded3da678f03febae4a28c05cd")
	report("master-secret-nist-003", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "3d72d5e9646ef7b72e13fed5152f406e1e2a697daeee4f06d06dee0809d673fb23a49e23b3d89743581eb5d3ea172619", clientRandomHex: "fa945b0a920a2959bef98524cb930c4a168ebf9d0f8853e05e66bb512a8bdcd7", serverRandomHex: "264cb1fda44e3762131d8251aa8f39a80308ab4dad6153d5af076c0011190a2d")
	report("master-secret-nist-004", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "5752929f45b56fd50de0bfee06a68f13ff4a089833951053c84de732940f07cb1100c17e8866e338e5bcc5fd911e4804", clientRandomHex: "b70779034a1a82e579075fa010a803d46a5133ca0dc84e911dfbb4bd8f15dce9", serverRandomHex: "d0d3014066670b1c0003b82f45e130f3f60be8eaab2f07f71199de5ae9cf73d6")
	report("master-secret-nist-005", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "a5e2642633f5b8c81ad3fe0c2fe3a8e5ef806b06121dd10df4bb0fe857bfdcf522558e05d2682c9a80c741a3aab1716f", clientRandomHex: "abe4bf5527429ac8eb13574d2709e8012bd1a113c6d3b1d3aa2c3840518778ac", serverRandomHex: "cb6e0b3eb02976b6466dfa9651c2919414f1648fd3a7838d02153e5bd39535b6")
	report("master-secret-nist-006", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "232a3e2a13c1d85a2fb290a63f701f5d3eaa592fa9828ee355c67e7d1aeff2939efe734cb9a860215d6d1733b7b6574f", clientRandomHex: "a1c148c1b0ca25075dc7fbcb6521749c4f5ac9bf1b27e5cf0fb4530d29e2c80c", serverRandomHex: "0ee8ec33cd65274b0dc34919d944ee2c61343f64ed44189e67e2f4b760c70c79")
	report("master-secret-nist-007", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "53190431150c5c4415cfef6a7025e7df33d4dcf0fa6c95b3b4c690eefe667ed28babe6d0da463f5259fb9c66db0f2b4e", clientRandomHex: "26758640c585d3590e2294147cdfab2769f0e79bd46e2183d544eeb9cfa3bfa4", serverRandomHex: "03f3d8cc3e0b76369d4e6150ed54acbc7f2858abf663ba5cd65a4a790e0fc47a")
	report("master-secret-nist-008", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "5fd2bd73b89a9e0f1360c8c411571271fdf52eb136650a2fdd4adbd56470aab50effcaffbfa763c0811481d3cb474bcd", clientRandomHex: "a36f1586a2cd1245345555f66a856b25d05dd7e4629416a795eb78d5c29d0779", serverRandomHex: "b74b1a3b54ad68d3d0feb12fa4d14f5cb7ffe886814e977f4d213a88d99b7419")
	report("master-secret-openssl-012", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "da5b19b829b626c3c68ff644033ac80c5e362c8237b5a02469ada620a4ab6577", clientRandomHex: "6fb8b9b7648ca4112a6199104eaaea9cdefd855dfdf171fcfa5fcc087cb0a4e4", serverRandomHex: "1f34f5d4898846eca5cc8dcce487c81c6385ce6ad5a0dfd9ee8a62d34d5db3af")
	report("master-secret-openssl-013", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "d42957133240751d9d15376065211523ba27c7ba53729173d6f36a4e68810a53", clientRandomHex: "ca94648841adb259a29f7463f18236975690b9a42051cc927acdc4999d0cbecb", serverRandomHex: "885d9d5707f60fe1145de29709f6a6f8df7927c8442c92d5444f574e47524401")
	report("master-secret-gen-x25519", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, preMasterSecretHex: "977c760ea67e2c531ab9a051edbea1cb32402861483bc8e90fbe527ee5f0e1db", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	report("prf-err-006", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "00", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df273", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	report("prf-err-014", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
master-secret-nist-001 202c88c00f84a17a20027079604787461176455539e705be730890602c289a5001e34eeb3a043e5d52a65e66125188bf
master-secret-nist-002 918b60d280f8471784392edbb9dd1fef7a78f823672a78fdb62822707e717a6d1ef54ca58f0b3b84a6fa092eb27089dd
master-secret-nist-003 794a9637f27c39b4b544665712760d6cf1a54f5b05f809ce16297f5cf2c7dc37874fdd472302ab16c99adf5648ef154c
master-secret-nist-004 90c6c091fd384c5384fef2a2cb9ab65ab69c1edeb8539d2a1354281285c277671ea82544f248dbce7a9a62748fbd119f
master-secret-nist-005 b4d49bfa87747fe815457bc3da15073d6ac73389e703079a3503c09e14bd559a5b3c7c601c7365f6ea8c68d3d9596827
master-secret-nist-006 2a1d6063b37a458b07746a7b04ed54c9ddeef5f2e488050dfb4b0f38cc5b178e3c20e6a72307efc859bd2e1c24f790c7
master-secret-nist-007 b7afa42cc017bb244746206750f1ade1fa7040e3503eff6eb1c31c040533238aa802cebccf3b21bb030cd8beb9d11720
master-secret-nist-008 222c037d460c0029884735dba6a4ae01d356c66f3234333ee5d39070f0553d16d1371dd6560e0268a1c96aa3351d65b4
master-secret-openssl-012 8cb3d619c9310d2081b1119f36c09a7acc789700bc9a989bd44716b5a113727f1f2cbc75c045321c75a14b9247a9dcc7
master-secret-openssl-013 2d5197d6e341c9777ea35f7a6d01c025dfe2dc22bf60bf174dc1858c9731230b18b1fe23591e4cd7262234b342beade8
master-secret-gen-x25519 ddc7d0a9f41373978ddfd3afe911bc7ad7aa49dae7eb592b9ef43d0affe14aa413e3a8e906c69b49e9774cbf0608befa
prf-err-006 invalidRandomLength
prf-err-014 invalidPreMasterSecretLength
```

<!-- test: tls12-prf.extended-master-secret -->
`Tls12Prf.extendedMasterSecret`: the master secrets of OpenSSL 3.0.13 handshakes from OpenSSL's key log, every suite, and the refused pre-master secrets and session hashes. Vectors extended-master-secret-openssl-001 to prf-err-013 (18).
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

function report(id String, suite Tls12CipherSuite, preMasterSecretHex String, sessionHashHex String)
	if let result = try Tls12Prf.extendedMasterSecret(suite, preMasterSecret: fromHex(preMasterSecretHex), sessionHash: fromHex(sessionHashHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("extended-master-secret-openssl-001", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "7f4b7dac6afe80d755d5307efea4fe1953fc4b59b48e3cccc8996433611f651c", sessionHashHex: "aef8ba1085be6aeaa3d878f010aca672d2573dad7739d91bcbec45a26ae8deff")
	report("extended-master-secret-openssl-002", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, preMasterSecretHex: "fd51ce1dda14e7c546270e96c102b160de4ae7114e789f61aa7de9c5029fdf26", sessionHashHex: "07ea6a03db2b04070f067d425c6a1916474f781b1f49dd03d9f35b973962038fc0739929a577206a4efa0e3d0453e6cc")
	report("extended-master-secret-openssl-003", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, preMasterSecretHex: "81362aeb7c26fae652c67370772926550e78ba59d42871472cd0884401629171", sessionHashHex: "6d5a35d5f2d1d2ee7e339473184fb86b4659f50cb80defcd4baae352fd05d07e")
	report("extended-master-secret-openssl-004", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "02db3375d650a3183df4243ed9c5b78f8c93c71b8504e9d38ae6d74297475e2c", sessionHashHex: "126dbad22a99185b9c79d6c7ffd294ef909147c1b82dd041582a8c1ce98704bce6cc77ead24cba12a1027277906866af")
	report("extended-master-secret-openssl-005", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "b4ee6d1b0aee6f2a9c95e86ca772e092e25d6d8c44550e9442d8f16c013b262a", sessionHashHex: "be643787c21b08a899ef815f37b3d4d2da038b92ff5f5416631b4dc6d0110848")
	report("extended-master-secret-openssl-006", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "0bc69336451cc116914cb7ec9b84fb530038336286e2e6143ab0135024d5e710", sessionHashHex: "811273c485a64f388933afb8cb3e1212ebae4cdcb59cd30a2c397c0cd6e4486a")
	report("extended-master-secret-openssl-007", suite: Tls12CipherSuite.ecdheRsaAes128GcmSha256, preMasterSecretHex: "46064fe36ac0eaad1ac3630cd635b24f97ca7d678bcef47630957e9e27ad0e64", sessionHashHex: "07e5f03de3ce127d46c11304fa8dea263a71d3c573462f34c395fb6202f91871")
	report("extended-master-secret-openssl-008", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "0cecade0bec4aff921094b3d1d284a6763ab8f67c110d24ddc0811e05efd6d3c", sessionHashHex: "c42c0f93956bced2958e16480d61407e4e299abccb0bc86747b768bcf5985e35b2e47911e09575839b2a3c6cf93891ba")
	report("extended-master-secret-openssl-009", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, preMasterSecretHex: "dc19d7773f0ea3e52789925a46dfbe63d232ce3656adb56d93a5000a8d765a17", sessionHashHex: "71040cddbf394a2a0e40866088f8cdd4dba5b5e246b7d415f327e82805fb4b53")
	report("extended-master-secret-openssl-010", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "80db158e0709492cc0aa05fbbbe57b04d4e6aae22fb12261ea2dde312b53653e", sessionHashHex: "fc84de6721acf449e8a9e61e3ae85c133c692d887a57c885648999ee8b6965a0")
	report("extended-master-secret-openssl-011", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "be6cc5c108d2cea57abba22dea3b3e044cb86e0e3e7009b99f50099a4fe49f33", sessionHashHex: "c23c43056f5951b3bf7d6589fc6163d0704e1740df1c917bd4030c8960a8d178")
	report("extended-master-secret-openssl-014", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, preMasterSecretHex: "e9b84e3bb3cd255a86f7265e18d7a5a95a05f1e120b311f75084d27995541518", sessionHashHex: "240a6b2ac975eb6a7793007eda8d3bc77dc5c094f95a0ffca7ecf0ec055bae3ead40b381e976e5d5c713d2de48c0bd62")
	report("extended-master-secret-openssl-015", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "93d845e247eb1618d670d446ddeb2da4d28bd8a7b76fa1fa47f8708d34a89a4a", sessionHashHex: "ca57ef355c5d4c8a95098087dcd2e9415dda9d432f1e10df03f63a132f2b7c129e595d6cbc9d50347866798c9df2fc96")
	report("extended-master-secret-openssl-016", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, preMasterSecretHex: "a56df5a03a2910c396d05fb39955f2a7a47cbc8b96857f8730e31940b1a30b6a", sessionHashHex: "57c5bf68b33e252cc2d43e07c42f58c488dc66096f1de4e046d770e7f926acca")
	report("extended-master-secret-openssl-017", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, preMasterSecretHex: "7d54b2ceb03eee75ae725bd81d7f1f6977db54b8a66b7b8c07fc619e6ac6ae7a", sessionHashHex: "3345e9a4c8f64a61615ed9194ac1dff07fae4fe24c99fff487be3e03fc341b2d")
	report("extended-master-secret-gen-sha384", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "977c760ea67e2c531ab9a051edbea1cb32402861483bc8e90fbe527ee5f0e1db", sessionHashHex: "1b133408db39267987434253e9bdf86acb6e8a3573c69c7ce4493457684dce2aad1fb3a5ca300c7cb12d2c0a3b9cd80c")
	report("prf-err-010", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, preMasterSecretHex: "00", sessionHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("prf-err-013", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, preMasterSecretHex: "", sessionHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
extended-master-secret-openssl-001 e53e7a77c80593dff70c279386c70252ad07b60f871ab2bfc621a65dbfaf20078d5714188ea43936c53b244e667212ed
extended-master-secret-openssl-002 8f6257ab8fb6e4d2f0e5190730508ffed3acc27f733e3a27681dd6c91562325f0e119b145aef5db36058a1f0093394bc
extended-master-secret-openssl-003 40c2ab8a91278d8643011657d068f9d6bb12621645b974c65d9908c381b2022162cba6d1de0c4bf7fe23e2e47a244fb7
extended-master-secret-openssl-004 f1e3e84daca2875b98ebd288288925c02655148b116b3be9da474916000f749052d34995fdbc11f6e81a9d51b3357b8e
extended-master-secret-openssl-005 eb3fdfe373be61a8758bd94fea9bfe34750946dd40da94ee48cf6dc45c4936166f6b3564fcb462aa22d8df70da0d1870
extended-master-secret-openssl-006 e339ac4b2139033b3b832223bf5a6482ed86b584029364004e42228d33b72e1259c8f381263b142fa55907e27723a1de
extended-master-secret-openssl-007 56c2e0c0c503e2720223b0ad73e69502d8501da657d7642c05fac5e540ff099ac216fc6c34d7cc47b0bd0f6eacd9c9be
extended-master-secret-openssl-008 309d98a66cf7277c7218bf3d8c6477d8fe266011a1670a29d8c2326b2189dfffd11f997fdfa387bda575b1c443b4c5ab
extended-master-secret-openssl-009 ee260cccc7fe3ea673d45eeb7e27402efd83eaf18501e2580509c84f343634dbb9f1aba5a6198fd7b4c2774631e1947f
extended-master-secret-openssl-010 8f36e4d06c82eb4c86b64b10e5c02685f5799eaa8feb0d0e5c321e282a088625fc7aa4f5842b9b6bba1e4ca82e9f4298
extended-master-secret-openssl-011 4182c0c75609e8a62b2704309314438a8e9c1141ba53e26f39d070edbeef22db45109278820c4bac9b1bdeff39736ae8
extended-master-secret-openssl-014 0d75a97718c5da387104fcdc736150e5bf8f240f01781697daba461fd2cf6c3ab8eed7d826ffd8fe659237c6a840af0f
extended-master-secret-openssl-015 8130c39e0f7fd6dcf0055a26c15b2c0ce5dce614cd2b28564a8656fb101c8ca538629d049aa7d71deb41dad20b6df207
extended-master-secret-openssl-016 cc6512b35a8c6c9577dcc72e3e36c47d9e74edde5ef84bb157f48cc82cab6fb7e822b9a16208d1eb7538157f765ba7fa
extended-master-secret-openssl-017 08a0441861cc356418e967cd12e129834c49eddec2206e984cf4207303a416ff7f2dffbbb391dd97124a72fb173bd2bd
extended-master-secret-gen-sha384 a1673bc1d0e35730b29c6d989245c121205c04fe5790433cab14dbff54d84117fba82a02a82dc9395af0e92f0a99e5a5
prf-err-010 invalidSessionHashLength
prf-err-013 invalidPreMasterSecretLength
```

<!-- test: tls12-prf.key-block -->
`Tls12Prf.keyBlock`: the NIST CAVS key blocks, the key blocks of OpenSSL 3.0.13 handshakes and every suite's, and the refused master secrets and randoms. A line prints the client write key, server write key, client write IV and server write IV. Vectors key-block-nist-001 to prf-err-008 (30).
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

function report(id String, suite Tls12CipherSuite, masterSecretHex String, clientRandomHex String, serverRandomHex String)
	if let result = try Tls12Prf.keyBlock(suite, masterSecret: fromHex(masterSecretHex), clientRandom: fromHex(clientRandomHex), serverRandom: fromHex(serverRandomHex)) 'derived'
		print("{id} {toHex(result.clientWriteKey())} {toHex(result.serverWriteKey())} {toHex(result.clientWriteIv())} {toHex(result.serverWriteIv())}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("key-block-nist-001", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "202c88c00f84a17a20027079604787461176455539e705be730890602c289a5001e34eeb3a043e5d52a65e66125188bf", clientRandomHex: "62e1fd91f23f558a605f28478c58cf72637b89784d959df7e946d3f07bd1b616", serverRandomHex: "ae6c806f8ad4d80784549dff28a4b58fd837681a51d928c3e30ee5ff14f39868")
	report("key-block-nist-002", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "918b60d280f8471784392edbb9dd1fef7a78f823672a78fdb62822707e717a6d1ef54ca58f0b3b84a6fa092eb27089dd", clientRandomHex: "284806ac208958e6fff30ebf99d908cb00fc5af915a19a5b34cac30340031f65", serverRandomHex: "d2ec44801e16c23e9d4cfe4c5c238523860dcca2b3f7dea5409fb31fceefa713")
	report("key-block-nist-003", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "794a9637f27c39b4b544665712760d6cf1a54f5b05f809ce16297f5cf2c7dc37874fdd472302ab16c99adf5648ef154c", clientRandomHex: "0ef8bd7b2ec845c1757a50638eb43350b56696973cb7f0fd4ca5337b811fc951", serverRandomHex: "f3b23d9557df8fd110c136adb7d22c1111621360cd3259334477afb7556bd1e2")
	report("key-block-nist-004", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "90c6c091fd384c5384fef2a2cb9ab65ab69c1edeb8539d2a1354281285c277671ea82544f248dbce7a9a62748fbd119f", clientRandomHex: "781c8ce7911b303372a7d521cd3cfa9ac60d0dc021f59108404a20c2e274636d", serverRandomHex: "64dc931f151f20481b1974d94b02c2d3b8100f9f9a6fa32b75c759f7e55c0b11")
	report("key-block-nist-005", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "b4d49bfa87747fe815457bc3da15073d6ac73389e703079a3503c09e14bd559a5b3c7c601c7365f6ea8c68d3d9596827", clientRandomHex: "954b5fe1849c2ede177438261f099a2fcd884d001b9fe1de754364b1f6a6dd8e", serverRandomHex: "1b1c8568344a65c30828e7483c0e353e2c68641c9551efae6927d9cd627a107c")
	report("key-block-nist-006", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "2a1d6063b37a458b07746a7b04ed54c9ddeef5f2e488050dfb4b0f38cc5b178e3c20e6a72307efc859bd2e1c24f790c7", clientRandomHex: "dce4c7973f3b0468d944de1870025bcc130e7832fd3fbfcd40ad287b13ee28f9", serverRandomHex: "c68423e2743b91bf9e2e1078bab5c5c884bd70983e0272343279e41738e943b5")
	report("key-block-nist-007", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "b7afa42cc017bb244746206750f1ade1fa7040e3503eff6eb1c31c040533238aa802cebccf3b21bb030cd8beb9d11720", clientRandomHex: "890dbe80be36186bee57eb71c3f93715c4f5fdf36bb54c1454f114ca4fbab739", serverRandomHex: "365453c3dea5d2db96a4013dc74aa512c62c9d00f47e37025a3fda9c975050b6")
	report("key-block-nist-008", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "222c037d460c0029884735dba6a4ae01d356c66f3234333ee5d39070f0553d16d1371dd6560e0268a1c96aa3351d65b4", clientRandomHex: "2a55b61c85cbf66e0e5a6dc54b7b4e695552550121abb1e8ad9b65e335441c94", serverRandomHex: "163ef353e186f0d3537614ddd1a95563ac701d24cfbd2588e47e36b6a01d9ebd")
	report("key-block-openssl-001", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "e53e7a77c80593dff70c279386c70252ad07b60f871ab2bfc621a65dbfaf20078d5714188ea43936c53b244e667212ed", clientRandomHex: "23722ca4db8048201a0470bcfbe11ff435d2fe5855b768c432a0c668057f4006", serverRandomHex: "2103989ed3f9086c661dc9b690401888ec1f9078a7ac5c79a8c30c57a92f34ba")
	report("key-block-openssl-002", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, masterSecretHex: "8f6257ab8fb6e4d2f0e5190730508ffed3acc27f733e3a27681dd6c91562325f0e119b145aef5db36058a1f0093394bc", clientRandomHex: "13e9b3828908e22a6e033f8bce042fd71e1bf4f7cdff3f606fee278522030620", serverRandomHex: "bb62b521c4e5b2d1f08c3bb616aee04956e2326fcddca305f9560c2637f1dbee")
	report("key-block-openssl-003", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "40c2ab8a91278d8643011657d068f9d6bb12621645b974c65d9908c381b2022162cba6d1de0c4bf7fe23e2e47a244fb7", clientRandomHex: "1e4e5d58a83892e3ba0b648622545b618940a344a4308ef9f578b10586ef8d85", serverRandomHex: "a34cf66730b1fd8653cb37771f320396822bea97bce992ad682ed9c0f2086b11")
	report("key-block-openssl-004", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "f1e3e84daca2875b98ebd288288925c02655148b116b3be9da474916000f749052d34995fdbc11f6e81a9d51b3357b8e", clientRandomHex: "4e47b0bbfd89ffbcd1a8eb5ae0b187649990325f76a67d0f1b5ba20eac397e2c", serverRandomHex: "864b461b5d0f0c4ade3686ffc74a66b758e4123a33e4dea39b266166d7c789f9")
	report("key-block-openssl-005", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "eb3fdfe373be61a8758bd94fea9bfe34750946dd40da94ee48cf6dc45c4936166f6b3564fcb462aa22d8df70da0d1870", clientRandomHex: "78f8ce40e99ab0ebaf9291eea9197b82bf55ec68d2fb971902e9e90552193c03", serverRandomHex: "9fd5edcc6b23af39a8f85d87ff40bfbd858c3584425e13b8d7018c9c9eb7c002")
	report("key-block-openssl-006", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "e339ac4b2139033b3b832223bf5a6482ed86b584029364004e42228d33b72e1259c8f381263b142fa55907e27723a1de", clientRandomHex: "51b7879bfbb8199f5e831bd3193fbe384884b5f5f4e59ed27f7122d31b046ff5", serverRandomHex: "44ef539c70703f9d348495c8ec71cc09da04cd58637725b2444f574e47524401")
	report("key-block-openssl-007", suite: Tls12CipherSuite.ecdheRsaAes128GcmSha256, masterSecretHex: "56c2e0c0c503e2720223b0ad73e69502d8501da657d7642c05fac5e540ff099ac216fc6c34d7cc47b0bd0f6eacd9c9be", clientRandomHex: "c3f05a79edced4752687314a54cf311f36f022ad31c080a7f8e261e6ab70d060", serverRandomHex: "4ad0449bb1981754d24cdda6a43c84fb979e8140660774c4444f574e47524401")
	report("key-block-openssl-008", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "309d98a66cf7277c7218bf3d8c6477d8fe266011a1670a29d8c2326b2189dfffd11f997fdfa387bda575b1c443b4c5ab", clientRandomHex: "ac35e964ba1569ac14b2710b844e1c9daf80c06dacda3a7602d61f43530b597c", serverRandomHex: "8c91c53c9631774b504800d8f9810ddb8929d10ba5822e5d444f574e47524401")
	report("key-block-openssl-009", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "ee260cccc7fe3ea673d45eeb7e27402efd83eaf18501e2580509c84f343634dbb9f1aba5a6198fd7b4c2774631e1947f", clientRandomHex: "c2f611f74d6c85143c56cdd1cf2ea86748e45f364d4b8684bb8c7b87072ffdfb", serverRandomHex: "d73ace53d25280148e57ec6a1d9754de66acf7f3d73c2489444f574e47524401")
	report("key-block-openssl-010", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "8f36e4d06c82eb4c86b64b10e5c02685f5799eaa8feb0d0e5c321e282a088625fc7aa4f5842b9b6bba1e4ca82e9f4298", clientRandomHex: "bcaefd826c9fff37f2d723d9f79df8c89325edbfb2646e07ead42413ef8f3e44", serverRandomHex: "31f1b35ce516f73491bc0bb2cf8a143ba92778623f4fe9f5444f574e47524401")
	report("key-block-openssl-011", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "4182c0c75609e8a62b2704309314438a8e9c1141ba53e26f39d070edbeef22db45109278820c4bac9b1bdeff39736ae8", clientRandomHex: "892f12f0f77f10412b548aef94bb8093d194ee160dba2dff8508bcf733cc6b3f", serverRandomHex: "13829dc62cf9f864482b12b2acc2840deefe65499ec9a2b2444f574e47524401")
	report("key-block-openssl-012", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "8cb3d619c9310d2081b1119f36c09a7acc789700bc9a989bd44716b5a113727f1f2cbc75c045321c75a14b9247a9dcc7", clientRandomHex: "6fb8b9b7648ca4112a6199104eaaea9cdefd855dfdf171fcfa5fcc087cb0a4e4", serverRandomHex: "1f34f5d4898846eca5cc8dcce487c81c6385ce6ad5a0dfd9ee8a62d34d5db3af")
	report("key-block-openssl-013", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "2d5197d6e341c9777ea35f7a6d01c025dfe2dc22bf60bf174dc1858c9731230b18b1fe23591e4cd7262234b342beade8", clientRandomHex: "ca94648841adb259a29f7463f18236975690b9a42051cc927acdc4999d0cbecb", serverRandomHex: "885d9d5707f60fe1145de29709f6a6f8df7927c8442c92d5444f574e47524401")
	report("key-block-openssl-014", suite: Tls12CipherSuite.ecdheEcdsaAes256GcmSha384, masterSecretHex: "0d75a97718c5da387104fcdc736150e5bf8f240f01781697daba461fd2cf6c3ab8eed7d826ffd8fe659237c6a840af0f", clientRandomHex: "38eed984605805e0b3fadca90e8799b86916ee83a3665e97596e7a33163bbee3", serverRandomHex: "e8485c5d82ca805cf88c704d3651d6ca51bd93cb84076e0740d848dbe0f23f1c")
	report("key-block-openssl-015", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "8130c39e0f7fd6dcf0055a26c15b2c0ce5dce614cd2b28564a8656fb101c8ca538629d049aa7d71deb41dad20b6df207", clientRandomHex: "cacdaa582aa2b1e5d7af39c53d7dbddfcaf869e20ebdda523de8ccdbd56be971", serverRandomHex: "6f162fa1551c746b3afe12022244d7f00037b0bce18474ab4d99ad363de03d2d")
	report("key-block-openssl-016", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "cc6512b35a8c6c9577dcc72e3e36c47d9e74edde5ef84bb157f48cc82cab6fb7e822b9a16208d1eb7538157f765ba7fa", clientRandomHex: "1088fb6db2a1c0abbcfbfdcd36701ac07a2f2302b97c1a59daa16fb23a86920b", serverRandomHex: "30128ded8b84ee5f5ff7fb433de8328dac41345a0dedd10b444f574e47524401")
	report("key-block-openssl-017", suite: Tls12CipherSuite.ecdheEcdsaChaCha20Poly1305Sha256, masterSecretHex: "08a0441861cc356418e967cd12e129834c49eddec2206e984cf4207303a416ff7f2dffbbb391dd97124a72fb173bd2bd", clientRandomHex: "51e23e58a8ecb8e0d6ed6000e59120328a6a6ef4a9a7b23cf03e7781b1bc4a39", serverRandomHex: "65c55280bbf44445013ace3ba5ede40ab27650ea39a83764444f574e47524401")
	report("key-block-gen-1", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	report("key-block-gen-2", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	report("key-block-gen-3", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	report("prf-err-007", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e400")
	report("prf-err-008", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca", clientRandomHex: "943df23ba831c066cb7b4b0a2b1d4af18426b05f0d906d6e3fb524970df27339", serverRandomHex: "3a2c8da3049ccf0eb73458fa2cf034d2b03ab15903a429b6b940a963c5b8e8e4")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
key-block-nist-001 d06139889fffac1e3a71865f504aa5d0 d2a2e89506c6f2279b670c3e1b74f531 016a2530 c51a3a0f
key-block-nist-002 1910e1496131f7ef05a7a568fffe7a3d 3073e4f08500c97fce69d1c42a4ccf75 53e52bc1 ce84b760
key-block-nist-003 04ae3b74f9a6770d654b5db2116d9b76 52ee5ab023f7731509a2910d9c28862f 0170f7fe ad6077c3
key-block-nist-004 63a90ddc34e66faa4059194d5a27544b 2d1335cf09ca4c7c0e561a0110ea5fe0 6fc82d24 4b391f4b
key-block-nist-005 10fd89ef689c7ef033387b8a8f3e5e8e7c11f680f6bdd71fbac3246a73e98d45 d03185dde686e6b2369e4503e9dc5a6d2cee3e2bf2fa3f41d3de57dff3e197c8 a9d5f74c c2d27711
key-block-nist-006 de9f2ce42403605743e84e71b2fba535bbb2020e5899d98773c140c27abbfef4 b4754ff01ea10e38604ec621f48a4af14715ee81b0ba10bdbebb5104bbc24110 04a2962b 0e7f5c2e
key-block-nist-007 f5d7e815131466cbb4d111507763d0e624be5637c209df31b2d894d36188c4a2 bfe4f7d9c5c98b8884d3714793b980321a9eb27397f0e00d6612a03e37c61f96 6696597d b68397a2
key-block-nist-008 690bef3fc09d99725557eb72d6b1b4386e52b57e2868ce63d6fd0b14edfc7195 bbb07e477a1630fc1e5ee7c4a426106a442fbd7d4710d849e5bb4c681a09d4f3 21af1431 1bba7bde
key-block-openssl-001 ff5373c156cbba379bfc61151d837483 164b80ed18c8b5e0c935c82e3d56b077 7c05dba2 db9400d6
key-block-openssl-002 a77fd211fe348eef7269b17f94a2097c7e50fc195fd26eca4fc4b2b033cfa39e 081b34e3295a0a114c155e085fa3d23b955483bb78429344fe925d61fa073dce e6f3eb98 c759780c
key-block-openssl-003 cc344e6b1b030497cd050b16f613a8eb8dff8c38dc0737e1094ad7ab23b771fa e5e6001a6b3c7ef3f10e602df1a9d60e10f94c56a6aeecef18c7f82ed63d1eed bd24795f8ca412dee12d68ea 76918e523b6ceadee0503778
key-block-openssl-004 689903de875ea8b0f9b06e4e30e851f3ac2e5fb848c85873be50219aa01d7994 4c8a3b6064c705a914adc27bd6b1df98aae816b3b7e3655f64ac2c54df794713 4ae0e4c8 0af92380
key-block-openssl-005 d7cc1bbe7b9a2d61f7d1f8531e8824bd1c1c13ac2fcedcd67b65d7c9891b4477 3a94dc090507cfc0f2ac38cb2a938781c323aede2acf4f7970faf95624f849c9 ac4a0e5fa2a9133f65070bbd b153109e872aa8d74b6ee650
key-block-openssl-006 1b3a31377e9001c5fd92f03824624e96d43b8818db23e81ba481ea8ae5f865f5 11b4cd45bbbede9b5ad2f5648c8fd88553afdd9b3f3a46cd37b64135499262ee 4b15192476a05c990f9a4476 012fba90ef3be76b64139d5a
key-block-openssl-007 64daa7280bec43dc8fb52f932765eb52 1f32c4aebcf788bfdcde32481c83e18d 75e360b6 da3ce3eb
key-block-openssl-008 44bf8e9d78483b038e8ab3e29e3ac0df9c702240f4dd0063fcfeb3a2e109b968 2821fc6da611b6360bb3c7370c02c9fa73b880046bd94d041b54c4f21b2f5d54 e13844fb ff65b6ab
key-block-openssl-009 92b4a2ace191e3db2d94c47144dfd56b8ef1b9e8ce72bca0fada114b715c7bb2 8423e361ae58484a519dcef5f8c0ab678e164f2ec8653a522d5e87438399bb3c 2a1eb6992d642a2022e7831f 6b15d9baaf93cf8350516a69
key-block-openssl-010 d94278ea0d92af29fc1275e7d81083f63346d83d847c0cfa7933b2a07e161507 ad82bf06137e4a362fe1d3c70f809c2c136e0913e22e5a048f093df772c1cec6 61678ce2301fad760b00f456 2550beb5eb41db0c068825ab
key-block-openssl-011 70534f62dbc5bde84fb47c9e41fb56db9c1bc0b57f4608e49a159956bf8c3782 1f537a5bb86298691c65620cc051408933cb213e1293a97eeeda1eb142c19c22 a91e49834fe23d3bf5137677 1b84b41bfa34ae4106977a4e
key-block-openssl-012 2cb5ea49859d2122ab690f94b5bd2237 6e93443fbe533a4efa71c8a5a980842f 267e0522 3d7ad406
key-block-openssl-013 9adfaf8e242b4d00bf01375f12aada8503d7b3f7a09b0407fb716405eec58de6 1c4211aa18a76acf738b92823c07073b736d65815fc0714c4c77e945781705d9 ca9f38cf7cfecea108d78d9b 9d30e5358e0bbf3ebd660c93
key-block-openssl-014 e30fa27aea57a04f5937c829580b4f69e2fc0b1f54a1a0c1f1bd22e5b592d063 abb64866a29d6f2226d5d2ee5670eebfc7be0e166f03ed4aa5e035c7268359bd 535bde83 f656e20d
key-block-openssl-015 bd793008683549334225bed2de305d110accd7e52eacd8041a5d7dfe8ae78311 c185604c6baff8161433c7cd25a9bce4389566495d40ce90015bde3807ed6089 d4d182fb 7485897b
key-block-openssl-016 7ac3000d483e073c25221b85f652d5db34529437312a790c20e540319429ff55 f7fe54cb770092962ff478dbce414b86d079af2327b72a98403dab7e89f642b5 a3e17a57e5a7b7df84aca221 95c481a52e8bef269fc6479b
key-block-openssl-017 dc223dbf46bc654633c0d08929be6444361bbac5c852f9b36ae2f2fad66e13bf 26d377c0979eb6972edb815effd7eb8c0fba696d4b30f94deab5c611f6183aad 57d9a37ec5289f358cbc79cc c046850fa38078308487bd61
key-block-gen-1 1aff270544dd87e8c170233c6bb55537 9cfb4318b64f3254b21c017955213c78 24506824 66e0b272
key-block-gen-2 857b29ab13122754e4244d5c94208ccde340c7cad1c4137cffb2a5480ccca380 c47d6bbca2c189b0584b022eaf65eeaa285435f871b36a6391b84ff30d724ee6 01f125c2 e4b3fbaf
key-block-gen-3 1aff270544dd87e8c170233c6bb555379cfb4318b64f3254b21c017955213c78 2450682466e0b2726f2eea5f83ca90fbf4072c3e6d04bd7ef1d802ca5cf6d173 c39ca3ddc8f5535a967f409e cf634dbcacd679df64674dd5
prf-err-007 invalidRandomLength
prf-err-008 invalidMasterSecretLength
```

<!-- test: tls12-prf.finished-verify-data -->
`Tls12Prf.finishedVerifyData`: both senders under every suite, and the refused master secrets and handshake hashes. Vectors finished-gen-1-client to prf-err-011 (8).
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

function report(id String, suite Tls12CipherSuite, masterSecretHex String, sender Tls12Sender, handshakeHashHex String)
	if let result = try Tls12Prf.finishedVerifyData(suite, masterSecret: fromHex(masterSecretHex), sender: sender, handshakeHash: fromHex(handshakeHashHex)) 'derived'
		print("{id} {toHex(result)}\n")
	end 'derived' else (failure) 'refused'
		print("{id} {failure}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("finished-gen-1-client", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.client, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("finished-gen-1-server", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.server, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("finished-gen-2-client", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.client, handshakeHashHex: "1b133408db39267987434253e9bdf86acb6e8a3573c69c7ce4493457684dce2aad1fb3a5ca300c7cb12d2c0a3b9cd80c")
	report("finished-gen-2-server", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.server, handshakeHashHex: "1b133408db39267987434253e9bdf86acb6e8a3573c69c7ce4493457684dce2aad1fb3a5ca300c7cb12d2c0a3b9cd80c")
	report("finished-gen-3-client", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.client, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("finished-gen-3-server", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.server, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("prf-err-009", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, masterSecretHex: "", sender: Tls12Sender.client, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	report("prf-err-011", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, masterSecretHex: "31385cf5184404091875810ae6319743ebfee7a0ab9772330e8b56f1d4b8910f2c1554771e5db653700649bb862cca20", sender: Tls12Sender.server, handshakeHashHex: "dfff30d188055f20aaa1610f29102805528465bbad91709ded764e8899f5ad2b")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
finished-gen-1-client 16f5bf43f7655e87fd024f7d
finished-gen-1-server e49c7177a70ecaa591b253ff
finished-gen-2-client fc6f9a0fc041d039aa074138
finished-gen-2-server 40e04890e2854752ede3eac0
finished-gen-3-client 16f5bf43f7655e87fd024f7d
finished-gen-3-server e49c7177a70ecaa591b253ff
prf-err-009 invalidMasterSecretLength
prf-err-011 invalidHandshakeHashLength
```

<!-- test: tls12-prf.handshake-hash -->
`Tls12Prf.handshakeHash` with each PRF hash, over several messages and over none. Vectors handshake-hash-gen-1 to handshake-hash-gen-3 (3).
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

function report(id String, suite Tls12CipherSuite, messages ByteArrays)
	let result = Tls12Prf.handshakeHash(suite, messages: messages)
	print("{id} {toHex(result)}\n")
end 'report'

function main() returns ExitCode
	report("handshake-hash-gen-1", suite: Tls12CipherSuite.ecdheEcdsaAes128GcmSha256, messages: ByteArrays from [fromHex("0100000100"), fromHex("0e000000")])
	report("handshake-hash-gen-2", suite: Tls12CipherSuite.ecdheRsaAes256GcmSha384, messages: ByteArrays from [fromHex("0100000100"), fromHex("0e000000")])
	report("handshake-hash-gen-3", suite: Tls12CipherSuite.ecdheRsaChaCha20Poly1305Sha256, messages: ByteArrays from [fromHex("0100000100"), fromHex("0e000000")])
	return 0
end 'main'
```
```exitcode
0
```
```stdout
handshake-hash-gen-1 71c3c14e7d38c7c296e8528cd24ace39d9a8460c5e0c32d1fdf6434afe08561d
handshake-hash-gen-2 720b684bd346c0cc1e19c5ee6a9083ac84493bc875e2593b21bfef303b16b234f81ae10a88c15dbeedd5ff59db38e024
handshake-hash-gen-3 71c3c14e7d38c7c296e8528cd24ace39d9a8460c5e0c32d1fdf6434afe08561d
```

<!-- test: tls12-prf.a-tls13-suite-is-refused -->
A TLS 1.3 suite is not a `Tls12CipherSuite`: the compiler refuses the call, so no call can name a suite the TLS 1.2 PRF does not define.
```maxon
function main() returns ExitCode
	var secret = ByteArray.create()
	secret.resize(48)

	let master = try Tls12Prf.extendedMasterSecret(Tls13CipherSuite.aes128GcmSha256, preMasterSecret: secret, sessionHash: secret) otherwise return 1
	print("{master.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:28: argument type mismatch for 'suite': expected 'Tls12CipherSuite', got 'Tls13CipherSuite'
```
