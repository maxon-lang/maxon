---
feature: sha2
status: stable
keywords: [sha256, sha384, sha512, sha2, Digest, HashAlgorithm, hash, crypto, digest, incremental]
category: crypto
---

# SHA-2 — SHA-384, SHA-512 and the incremental `Digest`

## Documentation

| Call | Meaning |
|---|---|
| `sha256(data)` | the 32-byte SHA-256 digest of `data` |
| `sha384(data)` | the 48-byte SHA-384 digest of `data` |
| `sha512(data)` | the 64-byte SHA-512 digest of `data` |
| `Digest.create(algorithm)` | a fresh incremental state for a `HashAlgorithm`: `sha256`, `sha384` or `sha512` |
| `digest.update(data)` | appends `data` to the message; an empty `data` changes nothing |
| `digest.finalize()` | the digest of every byte passed to `update`, in order |

The digests follow FIPS 180-4. A `Digest` gives the one-shot digest of the concatenation of its chunks, however
the message was split. Using a `Digest` after `finalize()` is a programming error.

The vectors are the NIST CAVP short-message entries at the padding boundaries and the FIPS 180-4 examples, with
the digests of RFC 6234 section 8.5. Each line prints a vector's id and the digest computed for it.

## Tests

<!-- test: sha2.one-shot-digests -->
Every one-shot vector of the three algorithms: the empty message, `abc`, the two-block FIPS 180-4 example, and the four padding boundaries (55, 56, 63, 64 bytes for SHA-256; 111, 112, 127, 128 for SHA-384 and SHA-512).
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

function digestOf(algorithm HashAlgorithm, data ByteArray) returns ByteArray
	return match algorithm 'oneShot'
		sha256 gives sha256(data)
		sha384 gives sha384(data)
		sha512 gives sha512(data)
	end 'oneShot'
end 'digestOf'

function report(id String, algorithm HashAlgorithm, dataHex String)
	print("{id} [{toHex(digestOf(algorithm, data: fromHex(dataHex)))}]\n")
end 'report'

function main() returns ExitCode
	report("sha256-001", algorithm: HashAlgorithm.sha256, dataHex: "")
	report("sha256-002", algorithm: HashAlgorithm.sha256, dataHex: "616263")
	report("sha256-003", algorithm: HashAlgorithm.sha256, dataHex: "6162636462636465636465666465666765666768666768696768696a68696a6b696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071")
	report("sha256-004", algorithm: HashAlgorithm.sha256, dataHex: "3ebfb06db8c38d5ba037f1363e118550aad94606e26835a01af05078533cc25f2f39573c04b632f62f68c294ab31f2a3e2a1a0d8c2be51")
	report("sha256-005", algorithm: HashAlgorithm.sha256, dataHex: "2d52447d1244d2ebc28650e7b05654bad35b3a68eedc7f8515306b496d75f3e73385dd1b002625024b81a02f2fd6dffb6e6d561cb7d0bd7a")
	report("sha256-006", algorithm: HashAlgorithm.sha256, dataHex: "e2f76e97606a872e317439f1a03fcd92e632e5bd4e7cbc4e97f1afc19a16fde92d77cbe546416b51640cddb92af996534dfd81edb17c4424cf1ac4d75aceeb")
	report("sha256-007", algorithm: HashAlgorithm.sha256, dataHex: "5a86b737eaea8ee976a0a24da63e7ed7eefad18a101c1211e2b3650c5187c2a8a650547208251f6d4237e661c7bf4c77f335390394c37fa1a9f9be836ac28509")
	report("sha384-001", algorithm: HashAlgorithm.sha384, dataHex: "")
	report("sha384-002", algorithm: HashAlgorithm.sha384, dataHex: "616263")
	report("sha384-003", algorithm: HashAlgorithm.sha384, dataHex: "61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475")
	report("sha384-004", algorithm: HashAlgorithm.sha384, dataHex: "a04f390a9cc2effad05db80d9076a8d4b6cc8bba97b27b423670b290b8e69c2b187230011c1481ac88d090f39154659494db5e410851c6e8b2b8a93717cae76037e0881978124fe7e1a0929d8891491f4e99646cc94062dc82411fa66130eda46560e75b98048236439465125e737b")
	report("sha384-005", algorithm: HashAlgorithm.sha384, dataHex: "f419494c3c6d0727b3395a483a2167182a7252f4fd099c2d4b71b053f94bb8b3adf3b51e8460cfec084ce9415c95798fbae4975c208c544645b54c44d2b97f2ecfce5c805be61f5ba1d35dcc07afdd51a87baa990506668cf710e18be9b0ebf943f366fa29c69f7a6616de72a3353b66")
	report("sha384-006", algorithm: HashAlgorithm.sha384, dataHex: "dbed7612448d46cbe0a384d1c93233f02ffd1c984ba765299518656d3723b766c1658d4b1e7047cdc729459e366ef9349efc40cbd990f2a9a24db7a5045e1dea12dce8f9d9f2aaed933f93031e7b8959ac5e7bf6bbbdf30b48f7eb783f8fe292371a2f245c5c94b4acae160767a20ce7c0ea7723d97691d8eedda9efd1fe2d")
	report("sha384-007", algorithm: HashAlgorithm.sha384, dataHex: "3bf52cc5ee86b9a0190f390a5c0366a560b557000dbe5115fd9ee11630a62769011575f15881198f227876e8fe685a6939bc8b89fd48a34ec5e71e131462b2886794dffa68ccc6d564733e67ffef25e627c6f4b5460796e3bce67bf58ca6e8e555bc916a8531697ac948b90dc8616f25101db90b50c3d3dbc9e21e42ff387187")
	report("sha512-001", algorithm: HashAlgorithm.sha512, dataHex: "")
	report("sha512-002", algorithm: HashAlgorithm.sha512, dataHex: "616263")
	report("sha512-003", algorithm: HashAlgorithm.sha512, dataHex: "61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475")
	report("sha512-004", algorithm: HashAlgorithm.sha512, dataHex: "324533e685f1852e358eea8ea8b81c288b3f3beb1f2bc2b8d3fdbac318382e3d7120de30c9c237aa0a34831deb1e5e060a7969cd3a9742ec1e64b354f7eb290cba1c681c66cc7ea994fdf5614f604d1a2718aab581c1c94931b1387e4b7dc73635bf3a7301174075fa70a9227d85d3")
	report("sha512-005", algorithm: HashAlgorithm.sha512, dataHex: "518985977ee21d2bf622a20567124fcbf11c72df805365835ab3c041f4a9cd8a0ad63c9dee1018aa21a9fa3720f47dc48006f1aa3dba544950f87e627f369bc2793ede21223274492cceb77be7eea50e5a509059929a16d33a9f54796cde5770c74bd3ecc25318503f1a41976407aff2")
	report("sha512-006", algorithm: HashAlgorithm.sha512, dataHex: "c13e6ca3abb893aa5f82c4a8ef754460628af6b75af02168f45b72f8f09e45ed127c203bc7bb80ff0c7bd96f8cc6d8110868eb2cfc01037d8058992a6cf2effcbfe498c842e53a2e68a793867968ba18efc4a78b21cdf6a11e5de821dcabab14921ddb33625d48a13baffad6fe8272dbdf4433bd0f7b813c981269c388f001")
	report("sha512-007", algorithm: HashAlgorithm.sha512, dataHex: "fd2203e467574e834ab07c9097ae164532f24be1eb5d88f1af7748ceff0d2c67a21f4e4097f9d3bb4e9fbf97186e0db6db0100230a52b453d421f8ab9c9a6043aa3295ea20d2f06a2f37470d8a99075f1b8a8336f6228cf08b5942fc1fb4299c7d2480e8e82bce175540bdfad7752bc95b577f229515394f3ae5cec870a4b2f8")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sha256-001 [e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855]
sha256-002 [ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad]
sha256-003 [248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1]
sha256-004 [6595a2ef537a69ba8583dfbf7f5bec0ab1f93ce4c8ee1916eff44a93af5749c4]
sha256-005 [cfb88d6faf2de3a69d36195acec2e255e2af2b7d933997f348e09f6ce5758360]
sha256-006 [18041bd4665083001fba8c5411d2d748e8abbfdcdfd9218cb02b68a78e7d4c23]
sha256-007 [42e61e174fbb3897d6dd6cef3dd2802fe67b331953b06114a65c772859dfc1aa]
sha384-001 [38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b]
sha384-002 [cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7]
sha384-003 [09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039]
sha384-004 [e7089d72945cef851e689b4409cfb63d135f0b5cdfb0dac6c3a292dd70371ab4b79da1997d7992906ac7213502662920]
sha384-005 [aead8688c58c6ba4e9cadb4756b465dce0fb06f1cfaa478197f2ea89414e47e9572034adfed160703c79b82b3fd7ab78]
sha384-006 [fb531a1ed181c732311e56f4b56ed91dcacc0dd6bf1eb4a44be6f87dd7cb1ef9dfb0310f4a79eaaa3f32bf3914d8624e]
sha384-007 [12b6cb35eda92ee37356ddee77781a17b3d90e563824a984faffc6fdd1693bd7626039635563cfc3b9a2b00f9c65eefd]
sha512-001 [cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e]
sha512-002 [ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f]
sha512-003 [8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909]
sha512-004 [3b26c5170729d0814153becb95f1b65cd42f9a6d0649d914e4f69d938b5e9dc041cd0f5c8da0b484d7c7bc7b1bdefb08fe8b1bfedc81109345bc9e9a399feedf]
sha512-005 [c00926a374cde55b8fbd77f50da1363da19744d3f464e07ce31794c5a61b6f9c85689fa1cfe136553527fd876be91673c2cac2dd157b2defea360851b6d92cf4]
sha512-006 [6e56f77f6883d0bd4face8b8d557f144661989f66d51b1fe4b8fc7124d66d9d20218616fea1bcf86c08d63bf8f2f21845a3e519083b937e70aa7c358310b5a7c]
sha512-007 [a21b1077d52b27ac545af63b32746c6e3c51cb0cb9f281eb9f3580a6d4996d5c9917d2a6e484627a9d5a06fa1b25327a9d710e027387fc3e07d7c4d14c6086cc]
```

<!-- test: sha2.digest-gives-the-one-shot-digest-however-the-message-is-split -->
Zero chunks, an empty chunk, empty chunks between others, a byte at a time, and splits mid-block and at a block's end all give the one-shot digest of the concatenation.
```maxon
typealias ChunkHexList = Array with String

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

function report(id String, algorithm HashAlgorithm, chunks ChunkHexList)
	var digest = Digest.create(algorithm)

	for chunk in chunks 'eachChunk'
		digest.update(fromHex(chunk))
	end 'eachChunk'

	print("{id} [{toHex(digest.finalize())}]\n")
end 'report'

function main() returns ExitCode
	report("sha256-chunked-001", algorithm: HashAlgorithm.sha256, chunks: ChunkHexList.create())
	report("sha256-chunked-002", algorithm: HashAlgorithm.sha256, chunks: [""])
	report("sha256-chunked-003", algorithm: HashAlgorithm.sha256, chunks: ["61", "62", "63"])
	report("sha256-chunked-004", algorithm: HashAlgorithm.sha256, chunks: ["", "6162", "", "63", ""])
	report("sha256-chunked-005", algorithm: HashAlgorithm.sha256, chunks: ["61", "62636462636465636465666465666765666768666768696768696a68696a6b696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071"])
	report("sha256-chunked-006", algorithm: HashAlgorithm.sha256, chunks: ["6162636462636465636465666465666765666768666768696768696a68696a6b", "696a6b6c6a6b6c6d6b6c6d6e6c6d6e6f6d6e6f706e6f7071"])
	report("sha256-chunked-007", algorithm: HashAlgorithm.sha256, chunks: ["5a86b737eaea8ee976a0a24da63e7ed7eefad18a101c1211e2b3650c5187c2a8a650547208251f6d4237e661c7bf4c77f335390394c37fa1a9f9be836ac285", "09"])
	report("sha256-chunked-008", algorithm: HashAlgorithm.sha256, chunks: ["e2f76e97606a872e317439f1a03fcd92e632e5bd4e7cbc4e97f1afc19a16fde9", "2d77cbe546416b51640cddb92af996534dfd81edb17c4424cf1ac4d75aceeb"])
	report("sha256-chunked-009", algorithm: HashAlgorithm.sha256, chunks: ["61", "62", "63", "64", "62", "63", "64", "65", "63", "64", "65", "66", "64", "65", "66", "67", "65", "66", "67", "68", "66", "67", "68", "69", "67", "68", "69", "6a", "68", "69", "6a", "6b", "69", "6a", "6b", "6c", "6a", "6b", "6c", "6d", "6b", "6c", "6d", "6e", "6c", "6d", "6e", "6f", "6d", "6e", "6f", "70", "6e", "6f", "70", "71"])
	report("sha384-chunked-001", algorithm: HashAlgorithm.sha384, chunks: ChunkHexList.create())
	report("sha384-chunked-002", algorithm: HashAlgorithm.sha384, chunks: [""])
	report("sha384-chunked-003", algorithm: HashAlgorithm.sha384, chunks: ["61", "62", "63"])
	report("sha384-chunked-004", algorithm: HashAlgorithm.sha384, chunks: ["", "6162", "", "63", ""])
	report("sha384-chunked-005", algorithm: HashAlgorithm.sha384, chunks: ["61", "626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"])
	report("sha384-chunked-006", algorithm: HashAlgorithm.sha384, chunks: ["61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f", "696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"])
	report("sha384-chunked-007", algorithm: HashAlgorithm.sha384, chunks: ["3bf52cc5ee86b9a0190f390a5c0366a560b557000dbe5115fd9ee11630a62769011575f15881198f227876e8fe685a6939bc8b89fd48a34ec5e71e131462b2886794dffa68ccc6d564733e67ffef25e627c6f4b5460796e3bce67bf58ca6e8e555bc916a8531697ac948b90dc8616f25101db90b50c3d3dbc9e21e42ff3871", "87"])
	report("sha384-chunked-008", algorithm: HashAlgorithm.sha384, chunks: ["dbed7612448d46cbe0a384d1c93233f02ffd1c984ba765299518656d3723b766c1658d4b1e7047cdc729459e366ef9349efc40cbd990f2a9a24db7a5045e1dea", "12dce8f9d9f2aaed933f93031e7b8959ac5e7bf6bbbdf30b48f7eb783f8fe292371a2f245c5c94b4acae160767a20ce7c0ea7723d97691d8eedda9efd1fe2d"])
	report("sha384-chunked-009", algorithm: HashAlgorithm.sha384, chunks: ["61", "62", "63", "64", "65", "66", "67", "68", "62", "63", "64", "65", "66", "67", "68", "69", "63", "64", "65", "66", "67", "68", "69", "6a", "64", "65", "66", "67", "68", "69", "6a", "6b", "65", "66", "67", "68", "69", "6a", "6b", "6c", "66", "67", "68", "69", "6a", "6b", "6c", "6d", "67", "68", "69", "6a", "6b", "6c", "6d", "6e", "68", "69", "6a", "6b", "6c", "6d", "6e", "6f", "69", "6a", "6b", "6c", "6d", "6e", "6f", "70", "6a", "6b", "6c", "6d", "6e", "6f", "70", "71", "6b", "6c", "6d", "6e", "6f", "70", "71", "72", "6c", "6d", "6e", "6f", "70", "71", "72", "73", "6d", "6e", "6f", "70", "71", "72", "73", "74", "6e", "6f", "70", "71", "72", "73", "74", "75"])
	report("sha512-chunked-001", algorithm: HashAlgorithm.sha512, chunks: ChunkHexList.create())
	report("sha512-chunked-002", algorithm: HashAlgorithm.sha512, chunks: [""])
	report("sha512-chunked-003", algorithm: HashAlgorithm.sha512, chunks: ["61", "62", "63"])
	report("sha512-chunked-004", algorithm: HashAlgorithm.sha512, chunks: ["", "6162", "", "63", ""])
	report("sha512-chunked-005", algorithm: HashAlgorithm.sha512, chunks: ["61", "626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"])
	report("sha512-chunked-006", algorithm: HashAlgorithm.sha512, chunks: ["61626364656667686263646566676869636465666768696a6465666768696a6b65666768696a6b6c666768696a6b6c6d6768696a6b6c6d6e68696a6b6c6d6e6f", "696a6b6c6d6e6f706a6b6c6d6e6f70716b6c6d6e6f7071726c6d6e6f707172736d6e6f70717273746e6f707172737475"])
	report("sha512-chunked-007", algorithm: HashAlgorithm.sha512, chunks: ["fd2203e467574e834ab07c9097ae164532f24be1eb5d88f1af7748ceff0d2c67a21f4e4097f9d3bb4e9fbf97186e0db6db0100230a52b453d421f8ab9c9a6043aa3295ea20d2f06a2f37470d8a99075f1b8a8336f6228cf08b5942fc1fb4299c7d2480e8e82bce175540bdfad7752bc95b577f229515394f3ae5cec870a4b2", "f8"])
	report("sha512-chunked-008", algorithm: HashAlgorithm.sha512, chunks: ["c13e6ca3abb893aa5f82c4a8ef754460628af6b75af02168f45b72f8f09e45ed127c203bc7bb80ff0c7bd96f8cc6d8110868eb2cfc01037d8058992a6cf2effc", "bfe498c842e53a2e68a793867968ba18efc4a78b21cdf6a11e5de821dcabab14921ddb33625d48a13baffad6fe8272dbdf4433bd0f7b813c981269c388f001"])
	report("sha512-chunked-009", algorithm: HashAlgorithm.sha512, chunks: ["61", "62", "63", "64", "65", "66", "67", "68", "62", "63", "64", "65", "66", "67", "68", "69", "63", "64", "65", "66", "67", "68", "69", "6a", "64", "65", "66", "67", "68", "69", "6a", "6b", "65", "66", "67", "68", "69", "6a", "6b", "6c", "66", "67", "68", "69", "6a", "6b", "6c", "6d", "67", "68", "69", "6a", "6b", "6c", "6d", "6e", "68", "69", "6a", "6b", "6c", "6d", "6e", "6f", "69", "6a", "6b", "6c", "6d", "6e", "6f", "70", "6a", "6b", "6c", "6d", "6e", "6f", "70", "71", "6b", "6c", "6d", "6e", "6f", "70", "71", "72", "6c", "6d", "6e", "6f", "70", "71", "72", "73", "6d", "6e", "6f", "70", "71", "72", "73", "74", "6e", "6f", "70", "71", "72", "73", "74", "75"])
	return 0
end 'main'
```
```exitcode
0
```
```stdout
sha256-chunked-001 [e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855]
sha256-chunked-002 [e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855]
sha256-chunked-003 [ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad]
sha256-chunked-004 [ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad]
sha256-chunked-005 [248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1]
sha256-chunked-006 [248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1]
sha256-chunked-007 [42e61e174fbb3897d6dd6cef3dd2802fe67b331953b06114a65c772859dfc1aa]
sha256-chunked-008 [18041bd4665083001fba8c5411d2d748e8abbfdcdfd9218cb02b68a78e7d4c23]
sha256-chunked-009 [248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1]
sha384-chunked-001 [38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b]
sha384-chunked-002 [38b060a751ac96384cd9327eb1b1e36a21fdb71114be07434c0cc7bf63f6e1da274edebfe76f65fbd51ad2f14898b95b]
sha384-chunked-003 [cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7]
sha384-chunked-004 [cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7]
sha384-chunked-005 [09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039]
sha384-chunked-006 [09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039]
sha384-chunked-007 [12b6cb35eda92ee37356ddee77781a17b3d90e563824a984faffc6fdd1693bd7626039635563cfc3b9a2b00f9c65eefd]
sha384-chunked-008 [fb531a1ed181c732311e56f4b56ed91dcacc0dd6bf1eb4a44be6f87dd7cb1ef9dfb0310f4a79eaaa3f32bf3914d8624e]
sha384-chunked-009 [09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039]
sha512-chunked-001 [cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e]
sha512-chunked-002 [cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e]
sha512-chunked-003 [ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f]
sha512-chunked-004 [ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f]
sha512-chunked-005 [8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909]
sha512-chunked-006 [8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909]
sha512-chunked-007 [a21b1077d52b27ac545af63b32746c6e3c51cb0cb9f281eb9f3580a6d4996d5c9917d2a6e484627a9d5a06fa1b25327a9d710e027387fc3e07d7c4d14c6086cc]
sha512-chunked-008 [6e56f77f6883d0bd4face8b8d557f144661989f66d51b1fe4b8fc7124d66d9d20218616fea1bcf86c08d63bf8f2f21845a3e519083b937e70aa7c358310b5a7c]
sha512-chunked-009 [8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909]
```

<!-- test: sha2.a-million-letters -->
RFC 6234 TEST3: a million repetitions of `a`, hashed in one call and as a thousand chunks of a thousand.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

typealias Repetition = int(0 to 1000000)

function letters(count Repetition) returns ByteArray
	var bytes = ByteArray.create()

	for _ in 0 upto count 'eachLetter'
		bytes.push(0x61)
	end 'eachLetter'

	return bytes
end 'letters'

function oneShot(algorithm HashAlgorithm, data ByteArray) returns ByteArray
	return match algorithm 'oneShot'
		sha256 gives sha256(data)
		sha384 gives sha384(data)
		sha512 gives sha512(data)
	end 'oneShot'
end 'oneShot'

function chunked(algorithm HashAlgorithm, chunk ByteArray) returns ByteArray
	var digest = Digest.create(algorithm)

	for _ in 0 upto 1000 'eachChunk'
		digest.update(chunk)
	end 'eachChunk'

	return digest.finalize()
end 'chunked'

function main() returns ExitCode
	let million = letters(1000000)
	let thousand = letters(1000)

	for algorithm in HashAlgorithm.allCases 'eachAlgorithm'
		print("{algorithm.name} one-shot [{toHex(oneShot(algorithm, data: million))}]\n")
		print("{algorithm.name} chunked [{toHex(chunked(algorithm, chunk: thousand))}]\n")
	end 'eachAlgorithm'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
sha256 one-shot [cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0]
sha256 chunked [cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0]
sha384 one-shot [9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985]
sha384 chunked [9d0e1809716474cb086e834e310a4a1ced149e9c00f248527972cec5704c2a5b07b8b3dc38ecc4ebae97ddd87f3d8985]
sha512 one-shot [e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b]
sha512 chunked [e718483d0ce769644e2e42c7bc15b4638e1f98b13b2044285632a803afa973ebde0ff244877ea60a4cb0432ce577c31beb009c5c2c49aa2e4eadb217ad8cc09b]
```

<!-- test: sha2.digest-panics-when-finalized-twice -->
A second `finalize()` on one `Digest` is a programming error, so it panics rather than returning a digest.
```maxon
function main() returns ExitCode
	var d = Digest.create(HashAlgorithm.sha256)
	_ = d.finalize()
	_ = d.finalize()
	return 0
end 'main'
```
```exitcode
1
```
```stdout
```
```stderr
panic at Sha2.maxon:429: Digest.finalize was called after finalize
Stack trace:
  in Digest.requireOpen
  in Digest.finalize
  in main
  in mrt_start
```

<!-- test: sha2.digest-panics-on-update-after-finalize -->
An `update()` after `finalize()` is a programming error, so it panics rather than extending a finished message.
```maxon
function main() returns ExitCode
	var d = Digest.create(HashAlgorithm.sha256)
	_ = d.finalize()
	d.update([0x61])
	return 0
end 'main'
```
```exitcode
1
```
```stdout
```
```stderr
panic at Sha2.maxon:429: Digest.update was called after finalize
Stack trace:
  in Digest.requireOpen
  in Digest.update
  in main
  in mrt_start
```
