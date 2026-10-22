---
feature: chacha20-poly1305
status: stable
keywords: [chacha20-poly1305, ChaCha20Poly1305, Aead, AeadError, seal, open, aead, rfc8439, authenticated encryption, crypto]
category: crypto
---

# `ChaCha20Poly1305` — ChaCha20-Poly1305 authenticated encryption

## Documentation

`ChaCha20Poly1305.create(key)` is the RFC 8439 section 2.8 `Aead` for a 32-byte key, with a 12-byte nonce and a
16-byte tag. The `Aead` interface is what `ChaCha20Poly1305` and `AesGcm` implement:

| Call | Meaning |
|---|---|
| `aead.seal(nonce, plaintext: p, additionalData: a)` | the ciphertext of `p` followed by the 16-byte tag over it and `a` |
| `aead.open(nonce, sealed: s, additionalData: a)` | the plaintext of `s`, only when its tag verifies against `a` |

Each throws an `AeadError`, and the first check that fails decides it:

1. a key that is not exactly 32 bytes, at `create`: `invalidKeyLength`;
2. a nonce that is not exactly 12 bytes: `invalidNonceLength`;
3. for `seal`, a plaintext longer than `2^38 - 64` bytes: `plaintextTooLong`;
4. for `open`, sealed input shorter than 16 bytes or a tag that does not verify: `authenticationFailed`.

`open` compares the tag in constant time and returns no plaintext when it fails. A changed ciphertext, tag,
additional data, nonce or key are one error. A nonce MUST NOT be reused under one key.

The vectors are RFC 8439 section 2.8.2 and appendix A.5 and Project Wycheproof's ChaCha20-Poly1305 cases. An
output longer than 64 bytes is pinned by its length and SHA-256 digest.

## Tests

<!-- test: chacha20-poly1305.seal -->
Every `seal` vector whose output is pinned in full, through the `Aead` interface.
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

enum Operation
	seal
	open
end 'Operation'

function errorName(failure AeadError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidNonceLength gives "invalidNonceLength"
		authenticationFailed gives "authenticationFailed"
		plaintextTooLong gives "plaintextTooLong"
	end 'name'
end 'errorName'

function perform(operation Operation, aead Aead, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let nonce = fromHex(nonceHex)
	let additionalData = fromHex(additionalDataHex)

	match operation 'direction'
		seal then return try aead.seal(nonce, plaintext: fromHex(inputHex), additionalData: additionalData)
		open then return try aead.open(nonce, sealed: fromHex(inputHex), additionalData: additionalData)
	end 'direction'
end 'perform'

function performWithKey(operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let aead = try ChaCha20Poly1305.create(fromHex(keyHex))
	return try perform(operation, aead: aead, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex)
end 'performWithKey'

function report(id String, operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String)
	if let output = try performWithKey(operation, keyHex: keyHex, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex) 'done'
		print("{id} [{toHex(output)}]\n")
	end 'done' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("seal-001", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c656d656e206f662074686520636c617373206f66202739393a204966204920636f756c64206f6666657220796f75206f6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73637265656e20776f756c642062652069742e", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("seal-002", operation: Operation.seal, keyHex: "1c9240a5eb55d38af333888604f6b5f0473917c1402b80099dca5cbc207075c0", nonceHex: "000000000102030405060708", inputHex: "496e7465726e65742d4472616674732061726520647261667420646f63756d656e74732076616c696420666f722061206d6178696d756d206f6620736978206d6f6e74687320616e64206d617920626520757064617465642c207265706c616365642c206f72206f62736f6c65746564206279206f7468657220646f63756d656e747320617420616e792074696d652e20497420697320696e617070726f70726961746520746f2075736520496e7465726e65742d447261667473206173207265666572656e6365206d6174657269616c206f7220746f2063697465207468656d206f74686572207468616e206173202fe2809c776f726b20696e2070726f67726573732e2fe2809d", additionalDataHex: "f33388860000000000004e91")
	report("seal-004", operation: Operation.seal, keyHex: "80ba3192c803ce965ea371d5ff073cf0f43b6a2ab576b208426e11409c09b9b0", nonceHex: "4da5bf8dfd5852c1ea12379d", inputHex: "", additionalDataHex: "")
	report("seal-005", operation: Operation.seal, keyHex: "7a4cd759172e02eb204db2c3f5c746227df584fc1345196391dbb9577a250742", nonceHex: "a92ef0ac991dd516a3c6f689", inputHex: "", additionalDataHex: "bd506764f2d2c410")
	report("seal-006", operation: Operation.seal, keyHex: "cc56b680552eb75008f5484b4cb803fa5063ebd6eab91f6ab6aef4916a766273", nonceHex: "99e23ec48985bccdeeab60f1", inputHex: "2a", additionalDataHex: "")
	report("seal-007", operation: Operation.seal, keyHex: "46f0254965f769d52bdb4a70b443199f8ef207520d1220c55e4b70f0fda620ee", nonceHex: "ab0dca716ee051d2782f4403", inputHex: "51", additionalDataHex: "91ca6c592cbcca53")
	report("seal-008", operation: Operation.seal, keyHex: "2f7f7e4f592bb389194989743507bf3ee9cbde1786b6695fe6c025fd9ba4c100", nonceHex: "461af122e9f2e0347e03f2db", inputHex: "5c60", additionalDataHex: "")
	report("seal-009", operation: Operation.seal, keyHex: "c8833dce5ea9f248aa2030eacfe72bffe69a620caf793344e5718fe0d7ab1a58", nonceHex: "61546ba5f1720590b6040ac6", inputHex: "ddf2", additionalDataHex: "88364fc8060518bf")
	report("seal-010", operation: Operation.seal, keyHex: "bd8ed7fb0d607522f04d0b12d42c92570bccc5ba2486953d70ba2e8193f6225a", nonceHex: "d2ab0abb50a8e9fba25429e1", inputHex: "201221", additionalDataHex: "")
	report("seal-011", operation: Operation.seal, keyHex: "1c8b59b17a5ceced31bde97d4cefd9aaaa63362e096e863ec1c89580bca79b7a", nonceHex: "94f32a6dff588f2b5a2ead45", inputHex: "453f95", additionalDataHex: "6c8cf2ab3820b695")
	report("seal-012", operation: Operation.seal, keyHex: "e4912cb75a1174345f1a457366f18885fe8460b06478e04be2f7fb4ec9c113e5", nonceHex: "7aa5ad8bf5254762171ec869", inputHex: "9e4c1d03", additionalDataHex: "")
	report("seal-013", operation: Operation.seal, keyHex: "e05777ef3d989ace7d2abfba452bfded54801dbd5c66e91c0c2ef00479d85572", nonceHex: "b7f526e3fd71cf5720961aec", inputHex: "17bfda03", additionalDataHex: "15d93a96d0e6c5a9")
	report("seal-014", operation: Operation.seal, keyHex: "1a4c4f39abe890e62345c947bcf7de7c2e33bd5ceeda0a0abf0e7ef935ddf3ee", nonceHex: "9447bf85d5b97d8aee0f8e51", inputHex: "c15a593bd0", additionalDataHex: "")
	report("seal-015", operation: Operation.seal, keyHex: "800e9a24791700c9609736695ba2a8b99b2d57f1c3bfb61ed49db1c6c5219583", nonceHex: "3dbe876bd880ec8ea2017043", inputHex: "a7bfd041e3", additionalDataHex: "96224835610b782b")
	report("seal-016", operation: Operation.seal, keyHex: "208c2c376c9430433db20e1a6b7ba817f8ffbfa6827f26759ccede42e591d3ec", nonceHex: "27fb58ec6a21e84696cb8830", inputHex: "af104b5ccd0e", additionalDataHex: "")
	report("seal-017", operation: Operation.seal, keyHex: "2eb168e53b07ab04355ea792fe11a6be2ce9c39cfe15a997076b1e38c17ad620", nonceHex: "b5965470c383fd29fe7eaee7", inputHex: "6fdf2927e169", additionalDataHex: "6d52feb2509f7fbf")
	report("seal-018", operation: Operation.seal, keyHex: "55568158d3a6483f1f7021eab69b703f614251cadc1af5d34a374fdbfc5adac7", nonceHex: "3c4e654d663fa4596dc55bb7", inputHex: "ab85e9c1571731", additionalDataHex: "")
	report("seal-019", operation: Operation.seal, keyHex: "e3c09e7fab1aefb516da6a33022a1dd4eb272c80d540c5da52a730f34d840d7f", nonceHex: "58389375c69ee398de948396", inputHex: "4ee5cda20d4290", additionalDataHex: "84e46be8c0919053")
	report("seal-020", operation: Operation.seal, keyHex: "51e4bf2bad92b7aff1a4bc05550ba81df4b96fabf41c12c7b00e60e48db7e152", nonceHex: "4f07afedfdc3b6c2361823d3", inputHex: "be3308f72a2c6aed", additionalDataHex: "")
	report("seal-021", operation: Operation.seal, keyHex: "1131c1418577a054de7a4ac551950f1a053f9ae46e5b75fe4abd5608d7cddadd", nonceHex: "b4ea666ee119563366484a78", inputHex: "a4c9c2801b71f7df", additionalDataHex: "66c0ae70076cb14d")
	report("seal-022", operation: Operation.seal, keyHex: "e1094967f86d893cdfe2e2e6d5c7ee4dfef67da3c9c5d64e6ad7c1577dcb38c5", nonceHex: "8092fc245b3326cddbd1424c", inputHex: "c37aa791ddd6accf91", additionalDataHex: "")
	report("seal-023", operation: Operation.seal, keyHex: "236f9baee4f9da15beeca40ff4af7c760f254a64bc3a3d7f4fad557e61b68586", nonceHex: "f1ca81338629587acf9372bf", inputHex: "d7f26d5252e1765f5b", additionalDataHex: "8c32f47a386152ec")
	report("seal-024", operation: Operation.seal, keyHex: "4de207a3b70c51e5f23048eed5a5da9bb65e917a69aa93e7c8b4a815cd9724de", nonceHex: "4c15a71dc6791a8c005ad502", inputHex: "f2c54b6b5e490da18659", additionalDataHex: "")
	report("seal-025", operation: Operation.seal, keyHex: "6d667fd79e5fb725f50343dccc4863227c75ee3f7a578476e3e9f32598d81559", nonceHex: "6220527aba88e27f766658b2", inputHex: "0c8c5a252681f2b5b4c0", additionalDataHex: "e1e27ccddb3cb407")
	report("seal-026", operation: Operation.seal, keyHex: "8f4bd94ef73e75d1e068c30b37ead576c5344e093ece1330e9101c82f793cf05", nonceHex: "ec1e2967f0f6979e5f5b07fb", inputHex: "b89812b34d9bced4a0ba07", additionalDataHex: "")
	report("seal-027", operation: Operation.seal, keyHex: "2aa3bc7033351cac51364cdaf6ffac2c20f64046e1550a7b1c65f41800599019", nonceHex: "28cce57a5db2cd206321e340", inputHex: "83016823123484b56095b0", additionalDataHex: "a9bc350eaf2e6e3d")
	report("seal-028", operation: Operation.seal, keyHex: "99b62bd5afbe3fb015bde93f0abf483957a1c3eb3ca59cb50b39f7f8a9cc51be", nonceHex: "9a59fce26df0005e07538656", inputHex: "42baae5978feaf5c368d14e0", additionalDataHex: "")
	report("seal-029", operation: Operation.seal, keyHex: "85f35b6282cff440bc1020c8136ff27031110fa63ec16f1e825118b006b91257", nonceHex: "58dbd4ad2c4ad35dd906e9ce", inputHex: "fdc85b94a4b2a6b759b1a0da", additionalDataHex: "a506e1a5c69093f9")
	report("seal-030", operation: Operation.seal, keyHex: "faf4bfe8019a891c74901b17f4f48cee5cd065d55fdea60118aaf6c4319a0ea5", nonceHex: "b776c3fddba7c81362ce6e1b", inputHex: "8dadff8d60c8e88f604f274833", additionalDataHex: "")
	report("seal-031", operation: Operation.seal, keyHex: "841020d1606edcfc536abfb1a638a7b958e21efc10c386ac45a18493450afd5f", nonceHex: "6d62f159731b140eb18ce074", inputHex: "d6af138f701b801e60c85ffd5c", additionalDataHex: "5a8e1c7aa39810d5")
	report("seal-032", operation: Operation.seal, keyHex: "470f9ce3d2250bd60cbbefdb2e6a1178c012299b5590639c7797b6024fa703d8", nonceHex: "a9ea4d619fe405d04cba7d7a", inputHex: "6ca67dd023fba6507b9f9a1f667e", additionalDataHex: "")
	report("seal-033", operation: Operation.seal, keyHex: "e4b97e91e4c8e85eb7ce0a7f30bf8a0abf4468251e4c6386c0e7aacb8e879aa8", nonceHex: "0e23c942a0c9fb526586eead", inputHex: "b84b3f74cd23064bb426fe2ced2b", additionalDataHex: "eaaaeab26957f9a1")
	report("seal-034", operation: Operation.seal, keyHex: "67119627bd988eda906219e08c0d0d779a07d208ce8a4fe0709af755eeec6dcb", nonceHex: "68ab7fdbf61901dad461d23c", inputHex: "51f8c1f731ea14acdb210a6d973e07", additionalDataHex: "")
	report("seal-035", operation: Operation.seal, keyHex: "e6f1118d41e4b43fb58221b7ed79673834e0d8ac5c4fa60bbc8bc4893a58894d", nonceHex: "d95b3243afaef714c5035b6a", inputHex: "97469da667d6110f9cbda1d1a20673", additionalDataHex: "6453a53384632212")
	report("seal-036", operation: Operation.seal, keyHex: "59d4eafb4de0cfc7d3db99a8f54b15d7b39f0acc8da69763b019c1699f87674a", nonceHex: "2fcb1b38a99e71b84740ad9b", inputHex: "549b365af913f3b081131ccb6b825588", additionalDataHex: "")
	report("seal-037", operation: Operation.seal, keyHex: "b907a45075513fe8a8019edee3f2591487b2a030b03c6e1d771c862571d2ea1e", nonceHex: "118a6964c2d3e380071f5266", inputHex: "55a465644f5b650928cbee7c063214d6", additionalDataHex: "034585621af8d7ff")
	report("seal-038", operation: Operation.seal, keyHex: "3b2458d8176e1621c0cc24c0c0e24c1e80d72f7ee9149a4b166176629616d011", nonceHex: "45aaa3e5d16d2d42dc03445d", inputHex: "3ff1514b1c503915918f0c0c31094a6e1f", additionalDataHex: "")
	report("seal-039", operation: Operation.seal, keyHex: "f60c6a1b625725f76c7037b48fe3577fa7f7b87b1bd5a982176d182306ffb870", nonceHex: "f0384fb876121410633d993d", inputHex: "63858ca3e2ce69887b578a3c167b421c9c", additionalDataHex: "9aaf299eeea78f79")
	report("seal-040", operation: Operation.seal, keyHex: "37ceb574ccb0b701dd11369388ca27101732339f49d8d908ace4b23af0b7ce89", nonceHex: "37270b368f6b1e3e2ca51744", inputHex: "f26991537257378151f4776aad28ae8bd16b", additionalDataHex: "")
	report("seal-041", operation: Operation.seal, keyHex: "68888361919bc10622f45df168e5f6a03bd8e884c0611bea2f34c1882ed9832b", nonceHex: "bfd6ff40f2df8ca7845980cc", inputHex: "ff97f2eefb3401ac31fc8dc1590d1a92cbc1", additionalDataHex: "b8373438ddb2d6c3")
	report("seal-042", operation: Operation.seal, keyHex: "1b35b856b5a86d3403d28fc2103a631d42deca5175cdb0669a5e5d90b2caafc5", nonceHex: "2343de88be6c7196d33b8694", inputHex: "21ef185c3ae9a96fa5eb473878f4d0b242781d", additionalDataHex: "")
	report("seal-043", operation: Operation.seal, keyHex: "d6484e3973f6be8c83ed3208d5be5cfa06fda72fbfdc5b19d09be3f4e4eba29d", nonceHex: "1af1d90e877e11a496efa3df", inputHex: "7335ab04b03e706109ec3ee835db9a246ea0ad", additionalDataHex: "cc4efd8364fb114a")
	report("seal-044", operation: Operation.seal, keyHex: "422add37849d6e4c3dfd8020dc6a07e8a249788f3d6a83b9cb4d802362c97542", nonceHex: "1e7e67be948de7352ffdb727", inputHex: "d7f5e611dd3a2750fb843fc1b6b93087310dc87d", additionalDataHex: "")
	report("seal-045", operation: Operation.seal, keyHex: "cdccfe3f46d782ef47df4e72f0c02d9c7f774def970d23486f11a57f54247f17", nonceHex: "376187894605a8d45e30de51", inputHex: "e28e0e9f9d22463ac0e42639b530f42102fded75", additionalDataHex: "956846a209e087ed")
	report("seal-046", operation: Operation.seal, keyHex: "e79dfc6d2fc465b8439e1c5baccb5d8ef2853899fc19753b397e6c25b35e977e", nonceHex: "f9d6320d7ce51d8ed0677d3a", inputHex: "4f543e7938d1b878dacaeec81dce4899974816813b", additionalDataHex: "")
	report("seal-047", operation: Operation.seal, keyHex: "1d7b8f1d96a1424923aef8a984869d4a777a110990ba465627acf80396c7f376", nonceHex: "50ba1962cdc32a5a2d36e640", inputHex: "5d3efd5767f3c12efd08af9a44e028ae68c9eff843", additionalDataHex: "093053e20261daab")
	report("seal-048", operation: Operation.seal, keyHex: "dd433e28cfbcb5de4ab36a02bf38686d83208771a0e63dcd08b4df1a07ac47a1", nonceHex: "c9cc0a1afc38ec6c30c38c68", inputHex: "8a3e17aba9606dd49e3b1a4d9e5e42f1742373632489", additionalDataHex: "")
	report("seal-049", operation: Operation.seal, keyHex: "a60924101b42ac24154a88de42142b2334cf599176caf4d1226f712dd9172930", nonceHex: "8ba77644b08d65d5e9f31942", inputHex: "c949957e66439deee4b2ac1d4a6c98a6c527b90f52ab", additionalDataHex: "b2a4e12a19a61c75")
	report("seal-050", operation: Operation.seal, keyHex: "1aa42027836965b1e6086fa137f9cf7f1ff48676696829bd281ff81c8ea0a4a9", nonceHex: "4b3dca84ecc407f424f281a9", inputHex: "37252a3eb5c8960f0567e503a9035783b3d0a19a4b9a47", additionalDataHex: "")
	report("seal-051", operation: Operation.seal, keyHex: "5d40db0cc18ef2e42815d3b6245a466a0b30a0f93e318ac10edde3bf8ad98160", nonceHex: "acad618039b317470d21621b", inputHex: "959dde1ef3129b27702c558849e466f2baca1a45bdf4b2", additionalDataHex: "413036411af75745")
	report("seal-052", operation: Operation.seal, keyHex: "0212a8de5007ed87b33f1a7090b6114f9e08cefd9607f2c276bdcfdbc5ce9cd7", nonceHex: "e6b1adf2fd58a8762c65f31b", inputHex: "10f1ecf9c60584665d9ae5efe279e7f7377eea6916d2b111", additionalDataHex: "")
	report("seal-053", operation: Operation.seal, keyHex: "c5bc09565646e7edda954f1f739223dada20b95c44ab033d0fae4b0283d18be3", nonceHex: "6b282ebecc541bcd7834ed55", inputHex: "9222f9018e54fd6de1200806a9ee8e4cc904d29f25cba193", additionalDataHex: "3e8bc5ade182ff08")
	report("seal-054", operation: Operation.seal, keyHex: "9460b3c44ed86e70f3bda66385e1ca10b0c1677ef4f1360532830d17535f996f", nonceHex: "abfaf42e0dba884efcf07823", inputHex: "5c5cce881b93fb7a1b7939af1ffc5f84d3280ada778cca0953", additionalDataHex: "")
	report("seal-055", operation: Operation.seal, keyHex: "c111d6d5d78a071b15ab37cc8c3819199387ab7c1933aa97b1489f6584ba8e2a", nonceHex: "85f18ad8ff72cafee2452ab8", inputHex: "6989c646a10b7c76f4d9f7d574da40e152013cf0dd78f5aa8a", additionalDataHex: "84cdff939391c022")
	report("seal-056", operation: Operation.seal, keyHex: "8a1b1e699a0c4a3e610b10902daedab1bf1ea0d505c47d7842cbcee0d3b1b6e6", nonceHex: "a6f9a8d335fa84c3b27dcd2a", inputHex: "ee6a15fc183108f0877e7f2b8a9615f4b3fc36e1c83440f66aad", additionalDataHex: "")
	report("seal-057", operation: Operation.seal, keyHex: "74b384e6e013ec4172ed7a28a10fb9bb79b4be2a24f6999e3d3caa28e64a8656", nonceHex: "ebc19fc9ecb2339908ea3836", inputHex: "3aa9f7372f056e5a0729752d9a37132d6dd07c56792e1c7582a9", additionalDataHex: "85073f2edc13d3a1")
	report("seal-058", operation: Operation.seal, keyHex: "77d824795d2029f0eb0e0baab5cfeb32f7e93474913a7f95c737a667a3c33314", nonceHex: "f3307430f492d2b8a72d3a81", inputHex: "0c4179a497d8fdd72796fb725692b805d63b7c718359cf10518aee", additionalDataHex: "")
	report("seal-059", operation: Operation.seal, keyHex: "bec5eac68f893951cbd7d1ecd3ee6611130dd9c3f80cddf95111d07d5edd76d1", nonceHex: "342ada4f0c115124b222df80", inputHex: "481433d8b1cd38af4a750e13a64b7a4e8507682b3517595938a20e", additionalDataHex: "73365f6d80edb1d8")
	report("seal-060", operation: Operation.seal, keyHex: "a59c1e13064df8f2b8df77a492b0ca2eae921b52a84b305a3a9a51408a9ecb69", nonceHex: "9544d41ece0c92ef01cfac2d", inputHex: "1c35b898821ba55c2617c25df9e6df2a8002b384902186cd69dfd20e", additionalDataHex: "")
	report("seal-061", operation: Operation.seal, keyHex: "084b5d7365f1a8fec6365939ed741e6ea5893e0318d82ab47500a97d77aaa041", nonceHex: "829f005e980f0a6e2f983eaa", inputHex: "7510016efadc385a71ed689ceb590c8ea9cc1e81b793338bddf5f10c", additionalDataHex: "770f6e6e89a3fe8e")
	report("seal-062", operation: Operation.seal, keyHex: "5a7f850a1d9aafa77d59ae1b731965e8aaec6352280fc76a7b5e23ef3610cfe4", nonceHex: "4946a0d6adea93b82d4332e5", inputHex: "3c161d791f624fb0388e808f0f69ed790dbe4cbd089ebac46627bcf01d", additionalDataHex: "")
	report("seal-063", operation: Operation.seal, keyHex: "e6d5a4246f6f05618b59c8f9ec3ac8068cc0d3f351c571aa52b09cb251f9c2f6", nonceHex: "2f90a65e9e48725de6ffc727", inputHex: "964fc9e0e8355947aa1c2caadd7b3dbef82a1024e623606fac436ef573", additionalDataHex: "f2415377ad283fd8")
	report("seal-064", operation: Operation.seal, keyHex: "09e822123adbb1ed89b79a58619c64853992f8371d46338712f6c91ab11a68bb", nonceHex: "a797205a6cacdd7e47a4789d", inputHex: "80b71bbe833629841bd3aeaeb9db6123e51d367b436fe9d2d3454b62cfad", additionalDataHex: "")
	report("seal-065", operation: Operation.seal, keyHex: "625735fe7f8fc81b0c1edc3d08a78b41268f87a3c68488b674222630c1d587a5", nonceHex: "9d8cdf289dddd09afdc1b02f", inputHex: "67ae1882d0b1c1b2485bec98115ecf53b9b438deb1d0400531705038873a", additionalDataHex: "200a9c95946ff05c")
	report("seal-066", operation: Operation.seal, keyHex: "2eb51c469aa8eb9e6c54a8349bae50a20f0e382711bba1152c424f03b6671d71", nonceHex: "04a9be03508a5f31371a6fd2", inputHex: "b053999286a2824f42cc8c203ab24e2c97a685adcc2ad32662558e55a5c729", additionalDataHex: "")
	report("seal-067", operation: Operation.seal, keyHex: "7f5b74c07ed1b40fd14358fe2ff2a740c116c7706510e6a437f19ea49911cec4", nonceHex: "470a339ecb3219b8b81a1f8b", inputHex: "f45206abc25552b2abc9ab7fa243035fedaaddc3b2293956f1ea6e7156e7eb", additionalDataHex: "374618a06ea98a48")
	report("seal-068", operation: Operation.seal, keyHex: "e1731d5854e1b70cb3ffe8b786a2b3ebf0994370954757b9dc8c7bc5354634a3", nonceHex: "72cfd90ef3026ca22b7e6e6a", inputHex: "b9c554cbc36ac18ae897df7beecac1dbeb4eafa156bb60ce2e5d48f05715e678", additionalDataHex: "")
	report("seal-069", operation: Operation.seal, keyHex: "27d860631b0485a410702fea61bc873f3442260caded4abde25b786a2d97f145", nonceHex: "262880d475f3dac5340dd1b8", inputHex: "6b2604996cd30c14a13a5257ed6cffd3bc5e29d6b97eb1799eb335e281ea451e", additionalDataHex: "2333e5ce0f93b059")
	report("seal-070", operation: Operation.seal, keyHex: "5155dee9aade1cc61ee7e3f92660f7590f5e5ba82f1b59b850e3fa453d2fa6b3", nonceHex: "c26c4b3bfdb97ee6b0f63ca1", inputHex: "2734e08eff8f5c4f84fa0c207f49c7fd78af1ad5123ff81f83f500edf4eda09edf", additionalDataHex: "")
	report("seal-071", operation: Operation.seal, keyHex: "573f08ebbe0cce4ac9618e8c3b224bea0a32f055c6996838a32f527ca3c3b695", nonceHex: "ad8050dc6d122dce3e5639ed", inputHex: "668d5e3f95fe030daf432a5fc5837af3a79c81e94b28d8204c5ee262ab3c9908a7", additionalDataHex: "e99698241c599b5f")
	report("seal-072", operation: Operation.seal, keyHex: "cf0d40a4644e5f51815165d5301b22631f4544c49a1878e3a0a5e8e1aae0f264", nonceHex: "e74a515e7e2102b90bef55d2", inputHex: "973d0c753826bae466cf9abb3493152e9de7819e2bd0c71171346b4d2cebf8041aa3cedc0dfd7b467e26228bc86c9a", additionalDataHex: "")
	report("seal-073", operation: Operation.seal, keyHex: "6cbfd71c645d184cf5d23c402bdb0d25ec54898c8a0273d42eb5be109fdcb2ac", nonceHex: "d4d807341683825b31cd4d95", inputHex: "a98995504df16f748bfb7785ff91eeb3b660ea9ed3450c3d5e7b0e79ef653659a9978d75542ef91c456762215640b9", additionalDataHex: "b3e4064683b02d84")
	report("seal-076", operation: Operation.seal, keyHex: "7d00b48095adfa3272050607b264185002ba99957c498be022770f2ce2f3143c", nonceHex: "87345f1055fd9e2102d50656", inputHex: "e5ccaa441bc814688f8f6e8f28b500b2", additionalDataHex: "02")
	report("seal-077", operation: Operation.seal, keyHex: "6432717f1db85e41ac7836bce25185a080d5762b9e2b18444b6ec72c3bd8e4dc", nonceHex: "87a3163ec0598ad95b3aa713", inputHex: "02cde168fba3f544bbd0332f7adeada8", additionalDataHex: "b648")
	report("seal-078", operation: Operation.seal, keyHex: "7afa0f59dfcb5ad3a76490c5c804327c8d052be737a60fa8bcbf0a2c36630a43", nonceHex: "25b7bdf4a6dcbf7c9a3ec2b3", inputHex: "623e6ba6d3166a338bfcc7af90a230c8", additionalDataHex: "8b71ac")
	report("seal-079", operation: Operation.seal, keyHex: "2ec25b0ec7ac244224e9c7fc2fa5d3ef17809e19fd6e954158dd0d72738a4cc8", nonceHex: "6fb0d1417cdfff4df37db08c", inputHex: "a1c933768a6d573ebf68a99e5e18dae8", additionalDataHex: "3a5ddf40")
	report("seal-080", operation: Operation.seal, keyHex: "0a2cf52371cf9d9f95b10108fc82b4fd6110a8ba9a88a26083685ad29826891a", nonceHex: "2538fc67afb9eab333f83290", inputHex: "0d8c691d044a3978d790432dc71d69f8", additionalDataHex: "9eec540bb0")
	report("seal-081", operation: Operation.seal, keyHex: "307e886b38bb18b445f8a2c6d6f8932492a9cea8d041ba72eb5efdfa70d0b8d2", nonceHex: "a071be999151e2a1c41c81e9", inputHex: "9aba22b495cb7ec887ddaa62019aa14d", additionalDataHex: "56e014d97c74")
	report("seal-082", operation: Operation.seal, keyHex: "dacd51a8a8e4d5905b4cbb947ef4013eb296889353f3c9ee35f5577b26737a51", nonceHex: "3fa378a1befdddd61ae68cf4", inputHex: "e148313883a77da121124d06b1c77dca", additionalDataHex: "bb5a3812f0aefd")
	report("seal-083", operation: Operation.seal, keyHex: "7b5fbbb202c16108fd13066446853a850d8b34e9da40519580da446a922f9162", nonceHex: "aa077a5ce9161bde8d8edc40", inputHex: "da471cd6935a0ca8307ddedc6b959962", additionalDataHex: "f94bb92c1c668a695b")
	report("seal-084", operation: Operation.seal, keyHex: "1ffd101eb97531f6faa821ec4d5c5702725dd033d3b830bb760c4ef27ba983df", nonceHex: "598114e8cf7fbdea8ad29683", inputHex: "28668ca8db535c7e8eb27491ad0fb7cb", additionalDataHex: "2155627ec15a978fbcb2")
	report("seal-085", operation: Operation.seal, keyHex: "d2d0a973d5951af352cbee57ac9dab1c284c99af3b992ce015f219506f64888d", nonceHex: "9acd213570ce9bb9d886c6ef", inputHex: "3f3f0076250352e1b6b5c12cfa12625e", additionalDataHex: "37ad668d4d4fe889949763")
	report("seal-086", operation: Operation.seal, keyHex: "adcc520b381382237d05a6400a7dfbcd0771b6aa9edb7966131ddef6af21f1be", nonceHex: "9183cdf3a8ba7397b6b2d5d5", inputHex: "958295619cf1b36f0b474663c0bc79eb", additionalDataHex: "b334375415f6215c0bf89a9a")
	report("seal-087", operation: Operation.seal, keyHex: "bd534f7adeca466844fb3ba34658be807f15c5291ed6026860a24f179b712c89", nonceHex: "412c3e13ee1f7864bd15ce39", inputHex: "d92f8ce5d8d0ad2eb5f11af02ef63949", additionalDataHex: "2866afff0bcc6135dc63af88c8")
	report("seal-088", operation: Operation.seal, keyHex: "910ade7d324d2c9688439e1f142e0e5f9d130ff832e507fe1985e5a26452a6d0", nonceHex: "9be090dba93deff27adf99ee", inputHex: "6e356094ed9d9a7053c7906c48ba3d9f", additionalDataHex: "ea2575f123268e936c8e4c8c1bb8")
	report("seal-089", operation: Operation.seal, keyHex: "8e34cf73d245a1082a920b86364eb896c4946467bcb3d58929fcb36690e6394f", nonceHex: "6f573aa86baa492ba46596df", inputHex: "16ddd23ff53f3d23c06334487040eb47", additionalDataHex: "bd4cd02fc7502bbdbdf6c9a3cbe8f0")
	report("seal-090", operation: Operation.seal, keyHex: "cb5575f5c7c45c91cf320b139fb594237560d0a3e6f865a67d4f633f2c08f016", nonceHex: "1a6518f02ede1da6809266d9", inputHex: "623b7850c321e2cf0c6fbcc8dfd1aff2", additionalDataHex: "89cce9fb47441d07e0245a66fe8b778b")
	report("seal-091", operation: Operation.seal, keyHex: "a5569e729a69b24ba6e0ff15c4627897436824c941e9d00b2e93fddc4ba77657", nonceHex: "564dee49ab00d240fc1068c3", inputHex: "87b3a4d7b26d8d3203a0de1d64ef82e3", additionalDataHex: "d19f2d989095f7ab03a5fde84416e00c0e")
	report("seal-092", operation: Operation.seal, keyHex: "56207465b4e48e6d04630f4a42f35cfc163ab289c22a2b4784f6f9290330bee0", nonceHex: "df8713e87ec3dbcfad14d53e", inputHex: "e601b38557797da2f8a4106a089d1da6", additionalDataHex: "5e6470facd99c1d81e37cd44015fe19480a2a4d3352a4ff560c0640fdbda")
	report("seal-093", operation: Operation.seal, keyHex: "077433022ab34d380fc192fc24c2edc6301fec6f24442f572a1087ff2e05b39a", nonceHex: "28adcbc74364f26dd4b3108b", inputHex: "03c874eeaaa6fa9f0da62c758fb0ad04", additionalDataHex: "e0100eb116cdc5e22a3b9f9b4126c149595e75107f6e237c69e82960052270")
	report("seal-094", operation: Operation.seal, keyHex: "3937986af86dafc1ba0c4672d8abc46c207062682d9c264ab06d6c5807205130", nonceHex: "8df4b15a888c33286a7b7651", inputHex: "dc9e9eaf11e314182df6a4eba17aec9c", additionalDataHex: "ba446f6f9a0ced22450feb10737d9007fd69abc19b1d4d9049a5551e86ec2b37")
	report("seal-095", operation: Operation.seal, keyHex: "36372abcdb78e0279646ac3d176b9674e9154eecf0d5469c651ec7e16b4c1199", nonceHex: "be40e5f1a11817a0a8fa8949", inputHex: "81ce84ede9b35859cc8c49a8f6be7dc6", additionalDataHex: "d41a828d5e71829247021905402ea257dccbc3b80fcd5675056b68bb59e62e8873")
	report("seal-096", operation: Operation.seal, keyHex: "9f1479ed097d7fe529c11f2f5add9aaff4a1ca0b68997a2cb7f79749bd90aaf4", nonceHex: "84c87dae4eee27730ec35d12", inputHex: "a66747c89e857af3a18e2c79500087ed", additionalDataHex: "3f2dd49bbf09d69a78a3d80ea2566614fc379474196c1aae84583da73d7ff85c6f42ca42056a9792cc1b9fb3c7d261")
	report("seal-097", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf074b7283992e24b1ac0df0d22b555dbe2254d94a43f1de748d3cc6f0d", additionalDataHex: "00000000000000000000000000000000")
	report("seal-100", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f8b48d7c66d1db4e53f20f2dd4aaa241ddab26b5bc0e218b72c3390f2", additionalDataHex: "ffffffffffffffffffffffffffffffff")
	report("seal-103", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63b7074b728b992e24b9ac0df0da2b555db62254d94243f1de7c8d3cc6f8d", additionalDataHex: "00000080000000800000008000000080")
	report("seal-106", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "e5b63bf0f4b7283912e24b1a40df0d223555dbe2a54d94a4bf1de74853cc6f0d", additionalDataHex: "80000000800000008000000080000000")
	report("seal-109", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c48f8b48d7466d1db4653f20f25d4aaa249ddab26bdbc0e218372c339072", additionalDataHex: "ffffff7fffffff7fffffff7fffffff7f")
	report("seal-112", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "1a49c40f0b48d7c6ed1db4e5bf20f2ddcaaa241d5ab26b5b40e218b7ac3390f2", additionalDataHex: "7fffffff7fffffff7fffffff7fffffff")
	report("seal-115", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf08b48d7c692e24b1a3f20f2ddb555dbe2dab26b5b3f1de7482c3390f2", additionalDataHex: "00000000ffffffff00000000ffffffff")
	report("seal-118", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f74b728396d1db4e5c0df0d224aaa241d254d94a4c0e218b7d3cc6f0d", additionalDataHex: "ffffffff00000000ffffffff00000000")
	report("seal-159", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f59d56151de28bef83505f6d89c0b0f7f75b2fa8e6dce386075db283ec85ee62555baffad423af25f66069bb69fb6f4d", additionalDataHex: "ffffffff")
	report("seal-160", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f717f8d5b28032d5c8e8061cd44d71e4f2d55de772fe7a91ce85e410db3e2d8d50d5ddb5400136323fb83f285e40aca2", additionalDataHex: "ffffffff")
	report("seal-172", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "2bfd0d56ece98771756d60d9d9106cd0c6fc106936c7ef347c078fd71c54228164fc903b0438a3978d3a54ef992aa3ae", additionalDataHex: "ffffffff")
	report("seal-193", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "43eadae036f733ea9b5b7eb22aee395db6f51a4d10bc2460810c229651556acf384ad82e3e280cad69f0df25b42b83b0", additionalDataHex: "ffffffff")
	report("seal-195", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "e235b8c21384557085c3f2eb2a8fa36058cffd2af743dacf96b4ae4d51b4e488d6703f49d9d7f2027e4853feb4ca0df7", additionalDataHex: "ffffffff")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seal-001 [d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691]
seal-002 [64a0861575861af460f062c79be643bd5e805cfd345cf389f108670ac76c8cb24c6cfc18755d43eea09ee94e382d26b0bdb7b73c321b0100d4f03b7f355894cf332f830e710b97ce98c8a84abd0b948114ad176e008d33bd60f982b1ff37c8559797a06ef4f0ef61c186324e2b3506383606907b6a7c02b0f9f6157b53c867e4b9166c767b804d46a59b5216cde7a4e99040c5a40433225ee282a1b0a06c523eaf4534d7f83fa1155b0047718cbc546a0d072b04b3564eea1b422273f548271a0bb2316053fa76991955ebd63159434ecebb4e466dae5a1073a6727627097a1049e617d91d361094fa68f0ff77987130305beaba2eda04df997b714d6c6f2c29a6ad5cb4022b02709beead9d67890cbb22392336fea1851f38]
seal-004 [76acb342cf3166a5b63c0c0ea1383c8d]
seal-005 [906fa6284b52f87b7359cbaa7563c709]
seal-006 [3acac27dec0968801e9f6eded69d807522]
seal-007 [c4168310ca45b1f7c66cad4e99e43f72b9]
seal-008 [4d1391e8b61efb39c122195453077b22e5e2]
seal-009 [b60dead0fd4697ec2e5558237719d02437a2]
seal-010 [3cf470a27a69c9d7ee84586f11388c6884e63a]
seal-011 [610925a8a7883eb7e40bc40e2e5922ae95ddc3]
seal-012 [fe6849aa99ad07871b25c27defc31a541bd5c418]
seal-013 [f4710e51b957c6a37b6a4c94996c002186d63b2b]
seal-014 [f711647ff122b12dc38cb79629f84cdbdc2425c09d]
seal-015 [d171f046ead179b1b9c4184378df009019dbb8c249]
seal-016 [9351b1b1b082560785509f60f26b681933d9cdbfd29f]
seal-017 [41abff7b71cc9b5174297c03cf8902d1f706fd008902]
seal-018 [5dfe3440dbb3c3ed7a434e2602d394281e0afa9fb7aa42]
seal-019 [4bd47212941ce3185f1408ee7fbf18f5abad6e2253a1ba]
seal-020 [8e9439a56eeec817fbe8a6ed8fabb1937539dd6c00e90021]
seal-021 [b9b910433af052b04530f51aeee024e0a445a6328fa67a18]
seal-022 [d9d897a9c1c5bb9f01085a430373058f1a12a0d589fd5be68b]
seal-023 [8fdb429d47761cbf8e8ef647ed334fdebbc2bef80be02884e0]
seal-024 [700d35adf5100a22a1de102d992ffaff599b5bddddeb2dfb399b]
seal-025 [04aad66c60e0bf8ebba9c15f69a4d2aef97d7748756ff49d894b]
seal-026 [1c3d53baaa36eaa1d8ec4d4d94ebf960f12433bec43aa86d7e6e6d]
seal-027 [1c8578f8e75203d0336a525910f7a9d5e4df05d7248bd7a8d65e63]
seal-028 [ff7dc203b26c467a6b50db33578c0f2758c2e14e36d4fc106dcb29b4]
seal-029 [9f8816de0994e938d9e53f95d086fc6c9d8fa915fd8423a7cf05072f]
seal-030 [e6b33a74a4ac443bd93f9c1b940c115172bdb02bbad3130fff22790d60]
seal-031 [b0a7500aca45bb15f01ece43890160e83adbec7f6a2ee2ff0215f9ef00]
seal-032 [d3017e0bb1705b380b34cc3334505708e72ca2bd354f487f82f67fbc3acb]
seal-033 [52e9672b416d84d97033796072d0e83839dc1fd9b8b9d1444c40e488d493]
seal-034 [0b29638e1fbdd6df53970be22100422a9134087d67a46e79178d0a93f5e1d2]
seal-035 [32db66c4a3819d81557455e5980fedfeae30dec94e6ad3a9eea06a0d703917]
seal-036 [e9110e9f56ab3ca483500ceabab67a13836ccabf15a6a22a51c1071cfa68fa0c]
seal-037 [e4b113cb775945f3d3a8ae9ec141c00c7c43f16ce096d0dc27c95849dc383b7d]
seal-038 [02cc3acb5ee1fcdd12a03bb857976474d3d83b7463a2c3800fe958c28eaa290813]
seal-039 [35766488d2bc7c2b8d17cbbb9abfad9e6d1f391e657b2738dda08448cba2811ceb]
seal-040 [b621d76a8dacff00b3f840cdf26c894cc5d1e0a21716ed94c0382fa9b0903d15bb68]
seal-041 [e0a745186c1a7b147f74faff2a715df5c19d917baf703e355d4d950e6c05fe8f349f]
seal-042 [d6e0ed54fccef30bd605d72da3320e249a9cb5c68bc6724ec803c43984ce42f6bd09ff]
seal-043 [29e54d608237c3c3609dba16e6edf43842d72fd3365fdcd506aaaa5368661e80e9d99b]
seal-044 [7fe606652d858f595ec2e706754fa3d933fcc83478d59235aa5d03a4c32590e590c04d22]
seal-045 [14f707c446988a4903775ec7acec6da114d43112987d4b147c490d43d376a198cab383f0]
seal-046 [1003f13ea1329cbb187316f64c3ff3a87cf5b96661d2323ad625094bec84790d7958d5583f]
seal-047 [2d48b0834e9ffe3046103ef7a214f02e8e4d33360ed533ad089be229ea606ec0f3fa22eb33]
seal-048 [e9917ff3e64bbe1783579375e75ea823976b35539949074a890669b25105434c75beed3248db]
seal-049 [db4c700513818972b0dc0e531b1c281ca03e40c60dea63f4478bba2af469a7a4dc3b4f141360]
seal-050 [b5f14617491fc923b683e2cc9562d043dd5986b97dbdbd972ce54713c05c4bb4d088c0a30cacd3]
seal-051 [b7ca3879f95140bf6a97b3212218b7bf864a51e5bb0b3efe558fb570145470ea693eb76eb73171]
seal-052 [42f26c56cb4be21d9d8d0c80fc99dde00d75f38074bfe76454aa7e13d48fff7d7557039457040a3a]
seal-053 [123032437b4bfd6920e8f7e7e0087ae4889ebe7a0ad0e9003cf68f179550da63d3b96c2d55411865]
seal-054 [1d218c9f1f9f02f248a6f976a7557057f37d9393d9f213c1f3bc88344c6fdc898feed394fb28511316]
seal-055 [9715d344e8d3f3a3eaa98a9cea57c0cd717c6ef5076027c9ec3056ff5ee0aa8636bb639984edb5236b]
seal-056 [9089bbdb8bcfd124e227bf75c4bfe1cba2004a274fc31aa32358fd2e21c64a019621c68594826cd7b1cd]
seal-057 [796ffb70ab43e7fa79f95583e384524727bb3e47fc45b969f714c3322b4445de5f3c9f18dcc847cc94c3]
seal-058 [49c81d17d67d7ba9954f497d0b0ddc21f3f839c9d2cc198d30bc2c50009899e5b2a9726c8f3556cadfbe84]
seal-059 [4c129fc13cbdd9d3fe81ac755bf4fbea2fdd7e0aca0505a6ee96379cede1d30a03db5d55265d3648bc40d4]
seal-060 [a6fa8f57ddc81d6099f667dd62402b6a5d5b7d05a329298029113169bb24e38b31dbbc3e575b9e3ee076af2a]
seal-061 [fd42cb5cf894f879e3cf751662aaa58a2288cc53548802becaf42359188329438afe1cd7225d0478aa90c773]
seal-062 [402302b56140c4dcc39774732c55883de124ce4bf0a0261cfa1569e2cfe830bfe933a96786cff2dd72b82c4bd5]
seal-063 [d052932bad6e6c4f835f02019e52d7ff807dc2a5aac2040883c79dd3d5655f93396b4d755dc4475721665fed91]
seal-064 [83f5c77396cabd28dfcc002cba0756d4ea5455e0261d847d5708aac21e8d705a05820a21f381d244d40e58d2f16b]
seal-065 [209b7539385c8b19ecd0fd8b5011b2996e316f1942064e68edfa363acbcdfa2f454b9fa2608f780f7c6f9b780fe1]
seal-066 [45c7d6b53acad4abb68876a6e96a48fb59524d2c92c9d8a189c9fd2db91746566d3ca10e311b695f3eae1551652493]
seal-067 [46a80c4187024720084627580080dde5a3f4a11093a7076ed6f3d326bc7b70534d4aa2835a52e72d14df0e4f47f25f]
seal-068 [ea29afa49d36e8760f5fe19723b9811ed5d519934a440f5081ac430b953b0e21222541af46b86533c6b68d2ff108a7ea]
seal-069 [6dad637897544d8bf6be9507ed4d1bb2e954bc427e5de729daf50762846ff2f47b997d93c982189d7095dc794c746232]
seal-070 [f5982b601c7a18fc72a65b218c44974dc564d8314cbe6f87fcf6c6cfbe618b34b1c43632f55760b5d1ed37556a94d049b5]
seal-071 [eaf6810e6ec1cb7a2918856257d1aa3d51a827879146c6337ecf535e9c89b149c5a2950c2f394a3466c345f796323c1aa7]
seal-072 [fba78ae4f9d808a62e3da40be2cb7700c3613d9eb2c529c652e76a432c658d27095f0eb8f940c324981ea935e507f98f046956db3a512908bd7afc8f2ab0a9]
seal-073 [a1ffed80761829ecce242e0e88b138049016bca018da2b6e19986b3e318cae8d806198fb4c527cc39350ebddeac573c4cbf0befda0b70242c640d7cd02d7a3]
seal-076 [7e72f5a185af16a611921b438f749f0b1242c670732334029adfe1c5001651e4]
seal-077 [85f29a719557cdd14d1f8fffab6d9e60732ca32becd515a1ed353f542e999858]
seal-078 [d46e8265a8c6a25393dd956bb44397ade28f3ad9e3ef4a3d94ee07bf538eaafb]
seal-079 [2d3cb2d9303491e264f2904f0e0753f46c1db959362d217b2322b466536bfea0]
seal-080 [a988c03c71b956ff086d0470d706bd34b35d7cbf2beb894b0c746e0730429e15]
seal-081 [32bf95d4c195dbaf58d9af4001c6e57d4393808703d67a90870578046cd8b525]
seal-082 [2a207ca7e9da6b13a229604304d87eb18a6b6afec87d93ec6e8dbe13d84c0f8c]
seal-083 [548a5ca0ae49211cdf30bbdcb1352d31204dacb98f8c8908cc5ea22bb23f901f]
seal-084 [28cedac24f14caa326c7fe401f68a87c2bf1b2c43d3039f8f5ce359c1102f879]
seal-085 [7256e856872ad3a54b34a2a6bdca88383b12e4586e45223f78a6eea811efb863]
seal-086 [852c141b4239a31feeda03550d70a2be5fc59287b92d3fcf7d66f13defb11b0d]
seal-087 [89d6d089c4a255952aca11b24a01ff95f88fa4531204da315e7317970240ce9e]
seal-088 [01ffb343c757b27843d8a900a36ce39da315541b7d6313c6fddf64b303d71d60]
seal-089 [c1b295936d56fadac03e5f742bff73a139c457dbab66382babb3b55800cda5b8]
seal-090 [c84c9bb7c61c1bcb17772a1c500c5095dbadf7a5138ca03459a2cd65831e092f]
seal-091 [94bc80621ed1e71b1fd2b5c3a15e3568333511861796978401598b963722f5b3]
seal-092 [299b5d3f3d03c087209a16e2851431114b454ed198de117e83ec49fa8d8508d6]
seal-093 [1e9687b35fbc8eaa1825ed3847798f760788bf70fd04030ecd1c96d0bc1fcd5d]
seal-094 [605bbf90aeb974f6602bc778056f0dca38ea23d99054b46b42ffe004129d2204]
seal-095 [7b7ce0d824809a70de32562ccf2c2bbd15d44a00ce0d19b4231f921e22bc0a43]
seal-096 [ca82bff3e2f310ccc976672c4415e69b57638c62a5d85ded774f913c813ea032]
seal-097 [000000000000000000000000000000000000000000000000000000000000000039f4fce3026d83789ffd1ee6f2cd7c4f]
seal-100 [ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff37e3399d9ca696799f08f4f72bc0cdd8]
seal-103 [0000008000000080000000800000008000000080000000800000008000000080c152a4b90c548c71dc479edeaf9211bf]
seal-106 [800000008000000080000000800000008000000080000000800000008000000010fee3ecfba9cdf797bae37a626ec83b]
seal-109 [ffffff7fffffff7fffffff7fffffff7fffffff7fffffff7fffffff7fffffff7faf8492c792bf8d8062be74ff6efb3869]
seal-112 [7fffffff7fffffff7fffffff7fffffff7fffffff7fffffff7fffffff7fffffff60d95294a3694cfaa64b2f63bc1f82ec]
seal-115 [00000000ffffffff00000000ffffffff00000000ffffffff00000000ffffffff10fb61272b555bee104f5a71818716d6]
seal-118 [ffffffff00000000ffffffff00000000ffffffff00000000ffffffff0000000060dcd45974bebe032eb7b86c9d063452]
seal-159 [d6ee4ee25d3bdea81e76de8934cc51fb849cfca7685708575dc6df7a01e36a81849cfca7685708575dc6df7a01e36a81831312cbb0f165dc3e8ff52125f48640]
seal-160 [d464e022f259679255ce87f8694190e881128ee8fc759140941e89e93658a96e81128ee8fc759140941e89e93658a96e821312db9826b5e7fe0a9d30c5e28d4f]
seal-172 [088e15a1ac30d236e84be13d641c8ddcb53bc366b84c04e5269ce22ef132a662b53bc366b84c04e5269ce22ef132a662345fc9fe573c136c1be83730500ce662]
seal-193 [da047b7825db1802e8e8e1aac6ba88fc2ff2344b9e99ccdc04d8836d556083412ff2344b9e99ccdc04d8836d55608341973e270a7afcab75348e14dbe19c5156]
seal-195 [7bdb195a00a87e98f6706df3c6db12c1c1c8d32c7966327313600fb655810d06c1c8d32c7966327313600fb655810d06437d1efad21b0865a541b5cab62e2a44]
```

<!-- test: chacha20-poly1305.seal-long-outputs -->
Every `seal` vector whose output is longer than 64 bytes, pinned by its length and SHA-256 digest, through the `Aead` interface.
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

enum Operation
	seal
	open
end 'Operation'

function errorName(failure AeadError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidNonceLength gives "invalidNonceLength"
		authenticationFailed gives "authenticationFailed"
		plaintextTooLong gives "plaintextTooLong"
	end 'name'
end 'errorName'

function perform(operation Operation, aead Aead, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let nonce = fromHex(nonceHex)
	let additionalData = fromHex(additionalDataHex)

	match operation 'direction'
		seal then return try aead.seal(nonce, plaintext: fromHex(inputHex), additionalData: additionalData)
		open then return try aead.open(nonce, sealed: fromHex(inputHex), additionalData: additionalData)
	end 'direction'
end 'perform'

function performWithKey(operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let aead = try ChaCha20Poly1305.create(fromHex(keyHex))
	return try perform(operation, aead: aead, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex)
end 'performWithKey'

function report(id String, operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String)
	if let output = try performWithKey(operation, keyHex: keyHex, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex) 'done'
		print("{id} {output.count()} [{toHex(sha256(output))}]\n")
	end 'done' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("seal-003", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c656d656e206f662074686520636c617373206f66202739393a204966204920636f756c64206f6666657220796f75206f6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73637265656e20776f756c642062652069742e", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("seal-074", operation: Operation.seal, keyHex: "5b1d1035c0b17ee0b0444767f80a25b8c1b741f4b50a4d3052226baa1c6fb701", nonceHex: "d61040a313ed492823cc065b", inputHex: "d096803181beef9e008ff85d5ddc38ddacf0f09ee5f7e07f1e4079cb64d0dc8f5e6711cd4921a7887de76e2678fdc67618f1185586bfea9d4c685d50e4bb9a82", additionalDataHex: "")
	report("seal-075", operation: Operation.seal, keyHex: "97d635c4f47574d9998a90875da1d3a284b755b2d39297a5725235190e10a97e", nonceHex: "d31c21aba175b70de4ebb19c", inputHex: "94ee166d6d6ecf8832437136b4ae805d428864359586d9193a25016293edba443c58e07e7b7195ec5bd84582a9d56c8d4a108c7d7ce34e6c6f8ea1bec0567317", additionalDataHex: "7193f623663321a2")
	report("seal-098", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf074b7283992e24b1ac0df0d22b555dbe2254d94a43f1de748d3cc6f0d20c142fe898fbbe668d4324394434c1b18b58ead710aed9c31db1f2a8a1f1bb2", additionalDataHex: "00000000000000000000000000000000")
	report("seal-099", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf074b7283992e24b1ac0df0d22b555dbe2254d94a43f1de748d3cc6f0d20c142fe898fbbe668d4324394434c1b18b58ead710aed9c31db1f2a8a1f1bb24405c183af94ee1ad630cd931158a6213d48c8fff10d0a1f9ef760188e658802aad55e41a1d99069a18db55c56af7c10a6f21ecc8af9b7ce0a7ea0b67426e925", additionalDataHex: "00000000000000000000000000000000")
	report("seal-101", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f8b48d7c66d1db4e53f20f2dd4aaa241ddab26b5bc0e218b72c3390f2df3ebd0176704419972bcdbc6bbcb3e4e74a71528ef51263ce24e0d575e0e44d", additionalDataHex: "ffffffffffffffffffffffffffffffff")
	report("seal-102", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f8b48d7c66d1db4e53f20f2dd4aaa241ddab26b5bc0e218b72c3390f2df3ebd0176704419972bcdbc6bbcb3e4e74a71528ef51263ce24e0d575e0e44dbbfa3e7c506b11e529cf326ceea759dec2b737000ef2f5e061089fe7719a77fd552aa1be5e266f965e724aa3a95083ef590de13375064831f5815f498bd916da", additionalDataHex: "ffffffffffffffffffffffffffffffff")
	report("seal-104", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63b7074b728b992e24b9ac0df0da2b555db62254d94243f1de7c8d3cc6f8d20c1427e898fbb6668d432c394434c9b18b58e2d710aed1c31db1faa8a1f1b32", additionalDataHex: "00000080000000800000008000000080")
	report("seal-105", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63b7074b728b992e24b9ac0df0da2b555db62254d94243f1de7c8d3cc6f8d20c1427e898fbb6668d432c394434c9b18b58e2d710aed1c31db1faa8a1f1b324405c103af94ee9ad630cd131158a6a13d48c87ff10d0a9f9ef760988e658882aad55ec1a1d990e9a18db5dc56af7c90a6f21e4c8af9b74e0a7ea0367426e9a5", additionalDataHex: "00000080000000800000008000000080")
	report("seal-107", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "e5b63bf0f4b7283912e24b1a40df0d223555dbe2a54d94a4bf1de74853cc6f0da0c142fe098fbbe6e8d4324314434c1b98b58eadf10aed9cb1db1f2a0a1f1bb2", additionalDataHex: "80000000800000008000000080000000")
	report("seal-108", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "e5b63bf0f4b7283912e24b1a40df0d223555dbe2a54d94a4bf1de74853cc6f0da0c142fe098fbbe6e8d4324314434c1b98b58eadf10aed9cb1db1f2a0a1f1bb2c405c1832f94ee1a5630cd939158a621bd48c8ff710d0a1f1ef760180e6588022ad55e4121d99069218db55cd6af7c1026f21ecc0af9b7ce8a7ea0b6f426e925", additionalDataHex: "80000000800000008000000080000000")
	report("seal-110", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c48f8b48d7466d1db4653f20f25d4aaa249ddab26bdbc0e218372c339072df3ebd8176704499972bcd3c6bbcb364e74a71d28ef512e3ce24e05575e0e4cd", additionalDataHex: "ffffff7fffffff7fffffff7fffffff7f")
	report("seal-111", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c48f8b48d7466d1db4653f20f25d4aaa249ddab26bdbc0e218372c339072df3ebd8176704499972bcd3c6bbcb364e74a71d28ef512e3ce24e05575e0e4cdbbfa3efc506b116529cf32eceea7595ec2b737800ef2f56061089f67719a777d552aa13e5e266f165e724a23a950836f590de1b3750648b1f5815fc98bd9165a", additionalDataHex: "ffffff7fffffff7fffffff7fffffff7f")
	report("seal-113", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "1a49c40f0b48d7c6ed1db4e5bf20f2ddcaaa241d5ab26b5b40e218b7ac3390f25f3ebd01f6704419172bcdbcebbcb3e4674a71520ef512634e24e0d5f5e0e44d", additionalDataHex: "7fffffff7fffffff7fffffff7fffffff")
	report("seal-114", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "1a49c40f0b48d7c6ed1db4e5bf20f2ddcaaa241d5ab26b5b40e218b7ac3390f25f3ebd01f6704419172bcdbcebbcb3e4674a71520ef512634e24e0d5f5e0e44d3bfa3e7cd06b11e5a9cf326c6ea759de42b737008ef2f5e0e1089fe7f19a77fdd52aa1bede266f96de724aa3295083efd90de133f506483175815f490bd916da", additionalDataHex: "7fffffff7fffffff7fffffff7fffffff")
	report("seal-116", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf08b48d7c692e24b1a3f20f2ddb555dbe2dab26b5b3f1de7482c3390f220c142fe7670441968d432436bbcb3e418b58ead8ef5126331db1f2a75e0e44d", additionalDataHex: "00000000ffffffff00000000ffffffff")
	report("seal-117", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "65b63bf08b48d7c692e24b1a3f20f2ddb555dbe2dab26b5b3f1de7482c3390f220c142fe7670441968d432436bbcb3e418b58ead8ef5126331db1f2a75e0e44d4405c183506b11e5d630cd93eea759de3d48c8ff0ef2f5e09ef76018719a77fdaad55e415e266f96a18db55ca95083efa6f21ecc750648310a7ea0b68bd916da", additionalDataHex: "00000000ffffffff00000000ffffffff")
	report("seal-119", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f74b728396d1db4e5c0df0d224aaa241d254d94a4c0e218b7d3cc6f0ddf3ebd01898fbbe6972bcdbc94434c1be74a7152710aed9cce24e0d58a1f1bb2", additionalDataHex: "ffffffff00000000ffffffff00000000")
	report("seal-120", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000102030405060708090a0b", inputHex: "9a49c40f74b728396d1db4e5c0df0d224aaa241d254d94a4c0e218b7d3cc6f0ddf3ebd01898fbbe6972bcdbc94434c1be74a7152710aed9cce24e0d58a1f1bb2bbfa3e7caf94ee1a29cf326c1158a621c2b73700f10d0a1f61089fe78e658802552aa1bea1d990695e724aa356af7c10590de1338af9b7cef5815f497426e925", additionalDataHex: "ffffffff00000000ffffffff00000000")
	report("seal-121", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef38c382cf07174142ea564920612997b1c2e38aca2438b588d5459493e97e7fa330ff9bc3b9458297ba0967d86ed090b435103478f2869b93ee29c837e95fb6b9903f3b735b7345428eb93b3db1d9b5187cebb889aa177d83e4f63fc9a5c0596eed939883d06aacdfdea44fdecdf5cb7fc", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-122", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "0001020304050607051e9373", inputHex: "931227274a89d0b3aade7fac62c96262c1e77b8dafd248f10ad37c6ccb69cb7131b041593c8bb8c3db38f39dd8a124c424fce4389dede1d3cb9d46cf95970aea9856b6e313d756197baf4fcb58df275bca8a2188f9e8a1ad04354ede542ddc30e8b735b2f5905f5811799282be94ae842ec126c55d2e667235e9acf1d48798f0", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-123", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "0001020304050607048c3c5f", inputHex: "0df91f31230e8941e700a752fef08c897c511ed618fdf8a378a1f439013b40a48d4634c27d9ada7c0bb6f3fa92e341425903d7ecd0c49bee4c77e84b11f1c721922308642885b813fae364da32eaf120d6a43a74fb1632443667bfea6eef1be73eb1c3c0b5a57cee8dc4feed4a1fb9ae02f7b1695588c3c878451cb6ee0cb3dc", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-124", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715ba428a85431430eada56a2c5dc944b6aa6cef0b056a2eecc51d30838e640615e1458e0943e30f91ba41b4362fa9ed6037b21d14da7b4f76f9f68fa8903138d563ce2590af1201c7cfec2290cfce98a822ebb8d1ed9dc4e20d241755aff91cdfd10fdb69efa0d5c8082692601cbfbb955c7", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-125", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "66115e67ecd3d4178c4c60e713ab4e5e66f8d1f971da17437a2b5e04fbca1671e847139a5f4e3f8e92d7a3b71eb4ff0e50354c0c1580af3662d5f8151e3f7e8264a0085c32ddfcbeb01a8be4c34d53319800ac4ef9d4e4014524bc7cd3387242e774f4d1a7a0521e42ec44844d0bd8b9d73fec959212fd7e8eacf4d984996d9b", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-126", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060726c6961b", inputHex: "e97244259af5a379238da0cad2a5f493655ec0e5024fd553bbb3deb66a94036d106c3d513407b2dd1cc5936c4c9c1e4f4b37b54dec261c601dc99e90680e23e2dc5c9a8d503d8bea49a8cdca3706bfd2a3daa0afb19a70fd3d355fc37c13f3f9e5c8d0864a5f80a780b36d4698ec2ce9ccc27b97ecbe672e41628ebd773acb81", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-127", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "0001020304050607013da060", inputHex: "9453aa159c3d87f17e21e88adabc37e553b904d00eefc66b8e0905e23576fbdc9c7bea9777f3b8368481932534b3344d309e6307cddfe7b3549300dd9cda7efe9d43c8a115912a392904079ee92bcd33099f7022ea94c1e7353b89bfc54de3ceb56f529a1a608bb5a970e1359609d1f56806b37f8605f4c27451da6066fc557a", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-128", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060707db33de", inputHex: "2e1836640d810c2709fb83ccf1aef3a971085d1bbfb58a425abf75ccec70b3abde0e80539e83a82546e7372a19481547053308dd7842675e9c4f61302426da0d71c1da3102031030ed928152be009b15b52f71b5911991d39f68a8658d99729df2bbef31c8989f9604558df9f2aba4b3766c58aaef3548de545ec1f080225a88", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-129", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060702a11942", inputHex: "0ecb4d85c956b5268c9b35a8c63b4e9d3e5cb72b64ef98773841b947bd7d59ef7d0eb0e1c050d49a5424ce7deb527d76087e4746674c958965df32d9e5fb03b46501706128d481217aaeae2f78f9259273358a2954cac0bc2fbfe77447d1d387b9314c6541b69f1270b3438b1042b2b4663e62ba4d49c07ac6f163034afa80af", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-130", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506073c0df637", inputHex: "2e8e45e903bfab32f2f0d49d9a3e449bef6f4093e2722cdab2cf935c1822b830fb5a4056516d560dfc8638c9a57d2927200a56f0b67153271d498e8f08dc888c61ef634f7ae40f4608f96f92fea5a1e5bd45131120098dc5de0378e58f2ddb46fa4aa5adb38fe006bb19b69146382f77a79e06214def547cfb5ce37a7008b9b6", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-131", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130bc672c3", inputHex: "3619cb470af86dceceb6940f2d9abb34c9a9131476053387445ffebbe240d4f9818377855652f46a8219c7f71c3554f8acef8258de4b7d17c0f3d353ac981cc6a13287be1e6b41dc6d133df4ababebdf43d665ce7a4a5c982a0b139cb8202eebc74173e3224a440e4c37d2b595f384290e939ba016df0d49b36cdb4bd91c39", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-132", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "000000001011121303e9b9a4", inputHex: "af205bda819f7451be0f28667d4b01b59ff2daa8173cab52046c3c9e0d989889c5e021ef7afd06e9ce6cc30e3a6ebab509134ba10d10e570c55587c13eee53e73be54804c8539ffbf23b35922b1ca37b9e9bc24ee204837ca5a294ce05d12600c7eff6aee32270db2feff47dc5a04176169e15850628e6035f78994f9f5603", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-133", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130700b982", inputHex: "68c67272036fb652a0182eeb4781358e4704a4a702fd731bf3b3ea994717989e7d9104e0ae81732a8c7e9a82b3d31d541761a366b67c3396f1a6c67e293ddb65a59e42541dda144dc6c78388cfca982e23350958ac5b3d54a1722fd64733577862e1879c9e9445ebdec5315d1706db7ebbedd4c779935e72057e5b0ecde081", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-134", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "0000000010111213019836bb", inputHex: "c483b7334ebe2e879b0c3f9db4fcd9f5219062360d6ce44cdae0f94e04c8345ea7e3ae33855118741dcafe0de4ae98c4e43af7b12b04ee8ab175625823ac040e5abac4403f1d45238adcb8c0cf44bd56917f9f5d93974c82b56951986a9c0450bd9047b5a616e814526ad0580e3ecd8189c9fef2cdb979a22ad3a01930fbd1", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-135", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112131d59f288", inputHex: "bc7f4f15fd1e4c1399740836670abe39a05707be19956ce169b32321759e0f213ae19ad34aa612b3a29f02c4bbac9f785a55a3adfe419ab891bbe0acee9921322ea21002c9dd3dcdd13a7f8554dddc10f9b529ce94be7050937dab76557b7eb17c685aad8f0797e39d62553988989aab1d9764fe431cc1d4c595062ce93ce9", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-136", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130552a411", inputHex: "eaccaa778935ef249e0900149dd889462d2a061486ba102b8caebe465f3959fb3119ebb5689676ffdd6d851a26739e772b54a2f5f473ea9c7e58ccbc4cfc953e8c420b2175d9dd519265630bb79bd87a601b113231a8b16ce54c331347ec04c2b1c9160f38207aa46e96feb06dee883eb422fa14908df300bb1a1ef758c408", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-137", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130c807a72", inputHex: "a76c330e015060a17e64cb7b6d753f201f75be8759fd7539fb92b22aef54c9d3029dba0c15cbf7c95135888319c6b2e6276da21e0c351fd522b29aabb5883a3291d6f427de773b124390ef6fd96621ffbc42dfbf7a34da272cbc9ccb1a498d078033d1ac3bf7e92715948b06d69d5c5039e9164ba9c3a02219ec5908206b3b", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-138", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130397a143", inputHex: "228a7e15bcce13051de9145f77f7f4ff7921828b4f99efc4ff55ee0d9344955b69ec2d4798b0517f0273c4456ae5ffc5929cbe74ddb0da51d4f2b4df7578a31240c88ae922c3c5eca7b97d72d497062050a587447c562b343d5c71921944872f9fd06b8f34b3eb5d4341f5ff8a907dd7c2e1676b81252726ba54814da51eab", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-139", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "000000001011121308cb0f3f", inputHex: "c7d843188ab193dfef5c4daf583f952cd4b195f240fa2e704d021723023c123371a41e87dfc6e6c3874a42f331cf035988a38c72ba2da854b1208f98bf8cc29948169481ab3a402d5fcc7ff78f9e31925576dc3938074b8c5b27960e3afc750ad686563688b7441787288d5256c1301d563b7744843bd1ab4eff5be6f1653d", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-140", operation: Operation.seal, keyHex: "9de836aa579585081f330a7c4036e20e38ef15eff3945184d231867f505fffdf", nonceHex: "00000000101112130d8fcf4e", inputHex: "cfc3db8631c81c69023a3c8a9ad66c35053685144c4fa2a9510add72e211dad9ca5b982e4c194591fdb74116280311d1299ad81227258cb52f079bbcb12aff161d278dec33a326d71276b3de01a8327ee7f45f94179dff18a3fe643e56c30cfd03871c8110ab00f6612b9e17a4647360d7847bb63a3122613c2e7cdddd08ae", additionalDataHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")
	report("seal-141", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "ffffffffffffffffffffffffffffffff415771fda4fbcc55c377f73203e60226")
	report("seal-142", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "f1ffffffffffffffffffffffffffffff615af39eddb5fcd2519190d5507d3b06")
	report("seal-143", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "b5ffffffffffffffffffffffffffffff764e5d82ce7da0d44148484fd96a6107")
	report("seal-144", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "fdffffffffffffffffffffffffffffff2bdbf16d8ea4d39dab8dcb3d4bc4e104")
	report("seal-145", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "a9ffffffffffffffffffffffffffffffaccd5eb31d8fc909e84b0de7de23bb08")
	report("seal-146", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "d2ffffffffffffffffffffffffffffffdd4b933e7b1a7ed93cc7c050db71dc03")
	report("seal-147", operation: Operation.seal, keyHex: "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f", nonceHex: "000102030405060708090a0b", inputHex: "e48caf8a76183327c9561a4651c07c822ccd1642c06607d0d4bc0afb4de15915dbfa3b0b422e77e15c64bf6247031f15fdb643117809821870000adf83834da5", additionalDataHex: "ffffffffffffffffffffffffffffffffa08164425d7642e9e90fc8d5c32d2cf6")
	report("seal-148", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c68ce708bf26aab862d97e1b42f31ef37bb66f8090c149e452ec7f20327eb2ea2e38aca2438b588d5459493e97e7fa330ff9bc23c897df6b00af86931d6c81555103478f2869b93ee29c837e95fb6b9903f3b72debfba2384baa48ceedfedb91", additionalDataHex: "ffffffff")
	report("seal-149", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "40115e67ecd3d4178c4c60e713ab4e5e390ef93aeb61aa307f141323c38e0685fa47139a5f4e3f8e92d7a3b71eb4ff0e259445f4ffc31bce540190edd6ad207876a0085c32ddfcbeb01a8be4c34d5331eda1a5b6139750f973f0d4841baa2cb8", additionalDataHex: "ffffffff")
	report("seal-150", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "19de9b9ec8b247d42bbee2016d6715babc286fd979807951b183a188930ad15edcf0b056a2eecc51d30838e640615e14890e659fd3028c904e65018fdfd6038333d14da7b4f76f9f68fa8903138d563c33b7fb50c3e7ebca970f6f89a88a82d6", additionalDataHex: "ffffffff")
	report("seal-151", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c78ce708bf26aab862d97e1b42f31ef376209eef141691fba5d10eaf581affe62e38aca2438b588d5459493e97e7fa330e73d2dc3bbd954989cb8433b7d6597b5103478f2869b93ee29c837e95fb6b990279d9d218d1e81ac2ce4a6e474403bf", additionalDataHex: "ffffffff")
	report("seal-152", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "49115e67ecd3d4178c4c60e713ab4e5ee02b87aeae8c3da8895f8cb0f6b9cc80f447139a5f4e3f8e92d7a3b71eb4ff0ecc4b7b803a5f8f4647df169080fe567a78a0085c32ddfcbeb01a8be4c34d5331047e9bc2d60bc471602e52f94df95aba", additionalDataHex: "ffffffff")
	report("seal-153", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715ba839f811ad0310c77052f45320b0d9560c4f0b056a2eecc51d30838e640615e1470d6b14fd209fedf261fd1d250d3478d2bd14da7b4f76f9f68fa8903138d563cca6f2f80c2ec9985ff75bfd4278fc6d8", additionalDataHex: "ffffffff")
	report("seal-154", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "fc8ce708bf26aab862d97e1b42f31ef38b79403dfaabc0d8c18d23a3469c13e62e38aca2438b588d5459493e97e7fa330a4b941e6b66fcc2ed7d8cb3e8cc7ffc5103478f2869b93ee29c837e95fb6b9906419f10480a8191a67842ee185e2538", additionalDataHex: "ffffffff")
	report("seal-155", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "fa8ce708bf26aab862d97e1b42f31ef39bcbb8da477d580d772de4229bba7de22938aca2438b588d5459493e97e7fa331e9dedf9dd64a0681bac2969549425bc5603478f2869b93ee29c837e95fb6b991297e6f7fe08dd3b50a9e734a4067f78", additionalDataHex: "ffffffff")
	report("seal-156", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "66115e67ecd3d4178c4c60e713ab4e5e891b797521ba925b24090aaf6c4482bae847139a5f4e3f8e92d7a3b71eb4ff0e6d50c32d05a946cb8cea57c9f1442cb164a0085c32ddfcbeb01a8be4c34d5331a565236fe9fd0dfcab1b13a03c432071", additionalDataHex: "ffffffff")
	report("seal-157", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "ee8ce708bf26aab862d97e1b42f31ef3b9f55bd56e0fd74b46063a96354cfbee3238aca2438b588d5459493e97e7fa3320c78886a6f6292d6cc5fbddb546a2b04d03478f2869b93ee29c837e95fb6b992ccd8388859a547e27c0358045d4f874", additionalDataHex: "ffffffff")
	report("seal-158", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "ef8ce708bf26aab862d97e1b42f31ef3b46fca24d353ff5e49eac51540e840ea3038aca2438b588d5459493e97e7fa333d311e572202011a75e948586fe268b44f03478f2869b93ee29c837e95fb6b99313b1559016e7c493eec86059f703270", additionalDataHex: "ffffffff")
	report("seal-161", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f28ce708bf26aab862d97e1b42f31ef3e68a922c9219d30f07554d7d99f2bde92c38aca2438b588d5459493e97e7fa33e24c07dd98f9b253ab0c318d9b14f6b15303478f2869b93ee29c837e95fb6b99ee460cd3bb95cf00e009ffd06b86ac75", additionalDataHex: "ffffffff")
	report("seal-162", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715badf0599194b0ce890cc1d8eb383b57f38dcf0b056a2eecc51d30838e640615e1435df81077d068077ce805ea592f6f88833d14da7b4f76f9f68fa8903138d563c8f661fc86de3e72d17ea30a3e5aa79dd", additionalDataHex: "ffffffff")
	report("seal-163", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef32e6784d857df07543d0dc72f179935fbede8c8baf01ee2044b162cbb343b355acc29d82327cd93f2bfd918034ed5c42a", additionalDataHex: "ffffffff")
	report("seal-164", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f78ce708bf26aab862d97e1b42f31ef34c6ead26f84a0225d557745d32fc72e72c38aca2438b588d5459493e97e7fa3364db334b69bee579383e61ae742c71bb5303478f2869b93ee29c837e95fb6b9968d138454ad2982a733baff384be2b7f", additionalDataHex: "ffffffff")
	report("seal-165", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "12de9b9ec8b247d42bbee2016d6715ba327f3a1befb4287c17450391ed0eb854d6f0b056a2eecc51d30838e640615e141460d3545c29ddc790711b8e7533698539d14da7b4f76f9f68fa8903138d563caed94d9b4cccba9d491b7588026fe8d0", additionalDataHex: "ffffffff")
	report("seal-166", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f08ce708bf26aab862d97e1b42f31ef34fd8c3757c9f2938dc3b07d85898bfe22a38aca2438b588d5459493e97e7fa336155412415cbdd760142b62c2ec83fbf5503478f2869b93ee29c837e95fb6b996d5f4a2a36a7a0254a477871de5a657b", additionalDataHex: "ffffffff")
	report("seal-167", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1bde9b9ec8b247d42bbee2016d6715ba85b67664ee49fa347fbfd2dd92007c57def0b056a2eecc51d30838e640615e14fb27ee075b3c0f0f682babdde63dad8731d14da7b4f76f9f68fa8903138d563c419e70c84bd96855b141c5db91612cd2", additionalDataHex: "ffffffff")
	report("seal-168", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f28ce708bf26aab862d97e1b42f31ef3df03ca84082f7f70ad8e4004cabd2ce42b38aca2438b588d5459493e97e7fa3328fd413caab1d02bf1c65753aa2ad3b95403478f2869b93ee29c837e95fb6b9924f74a3289ddad78bac3990e5ab8897d", additionalDataHex: "ffffffff")
	report("seal-169", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f38ce708bf26aab862d97e1b42f31ef31ffc31ae69399394b8c338674c3dfde92938aca2438b588d5459493e97e7fa33477ec8cf3ea3d4d5d76d85ad2b7f0bb85603478f2869b93ee29c837e95fb6b994b74c3c11dcfa9869c684bf0dbed517c", additionalDataHex: "ffffffff")
	report("seal-170", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "4f115e67ecd3d4178c4c60e713ab4e5e4156269fe3da101eeb0abf8dda20fe8fff47139a5f4e3f8e92d7a3b71eb4ff0e6aece983e64f97e43ff5295bc884fa7773a0085c32ddfcbeb01a8be4c34d5331a2d909c10a1bdcd318046d320583f6b7", additionalDataHex: "ffffffff")
	report("seal-171", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715baf999461058f6d7733e5cd0d1639d9025cbf0b056a2eecc51d30838e640615e14520a0da50439db00e289e1791342068e24d14da7b4f76f9f68fa8903138d563ce8b3936a14dcbc5a3be38f7f641e87db", additionalDataHex: "ffffffff")
	report("seal-173", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f68ce708bf26aab862d97e1b42f31ef37cc2255decdf8e0fe1373591da0e28e42838aca2438b588d5459493e97e7fa33e291fb4838019c51dfb7141515bb53b15703478f2869b93ee29c837e95fb6b99ee9bf0461b6de10294b2da48e5290975", additionalDataHex: "ffffffff")
	report("seal-174", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c68ce708bf26aab862d97e1b42f31ef37ab66f8090c149e452ec7f20327eb2ea0438aca2438b588d5459493e97e7fa338d2613ea0ef8b656b247373ecec015bc7b03478f2869b93ee29c837e95fb6b99812c18e42d94cb05f942f9633e524f78", additionalDataHex: "ffffffff")
	report("seal-175", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c68ce708bf26aab862d97e1b42f31ef374b66f8090c149e452ec7f20327eb2ea2e38aca2438b588d5459493e97e7fa33acd9ec859e0866620cc24c8a97d5d9f55103478f2869b93ee29c837e95fb6b99a0d3e78bbd641b3147c782d767478331", additionalDataHex: "ffffffff")
	report("seal-176", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f78ce708bf26aab862d97e1b42f31ef34251cd29b0aaa960557c9ea2828334e4e4e231db0a27fac9ec9e744886eb0133c5232142ddf48b3f185140f0fc05f043", additionalDataHex: "ffffffff")
	report("seal-177", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "f58ce708bf26aab862d97e1b42f31ef373bd9f01bf3331b12e31dd14cf11feee1d38aca2438b588d5459493e97e7fa33625c6965f61a1c36118c747076d5b7b76203478f2869b93ee29c837e95fb6b996e56626bd57661655a89ba2d8647ed73", additionalDataHex: "ffffffff")
	report("seal-178", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715bad64add2aa3c5a30a31d9e65e90f93ad1cbf0b056a2eecc51d30838e640615e14de9aeab86144d5464811b2373ba4cc8324d14da7b4f76f9f68fa8903138d563c6423747771a1b21c917bdc314cf84dd6", additionalDataHex: "ffffffff")
	report("seal-179", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef3ec0933f0bfb91218cea0d74e061f559e2d38aca2438b588d5459493e97e7fa338d5b67e0acee534ce2d9791487b1ecb25203478f2869b93ee29c837e95fb6b9981516cee8f822e1fa9dcb7497723b676", additionalDataHex: "ffffffff")
	report("seal-180", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715bacc3492272b8a4b112a4e7d7ccf092692cef0b056a2eecc51d30838e640615e1430ce678e9375b2af0b82c2d2fbd7928c21d14da7b4f76f9f68fa8903138d563c8a77f9418390d5f5d2e8acd48c8b13d9", additionalDataHex: "ffffffff")
	report("seal-181", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef3ee83a14f48db696291080edfcc898b882b38aca2438b588d5459493e97e7fa338ad5f6b0283a8b39ebedce92785da9b65403478f2869b93ee29c837e95fb6b9986dffdbe0b56f66aa0e800cf88cff372", additionalDataHex: "ffffffff")
	report("seal-182", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "34de9b9ec8b247d42bbee2016d6715ba722b6549c9df0f4b04b5f7432203fa54cef0b056a2eecc51d30838e640615e1487de186cd28e43544c73de628fd1d60e21d14da7b4f76f9f68fa8903138d563c3d6786a3c26b240e9519b064f88d575b", additionalDataHex: "ffffffff")
	report("seal-183", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef3e87dd08ed4e4e04c5877616cbb02cabb2938aca2438b588d5459493e97e7fa33874f0401d457e336f4311f1152f957ba5603478f2869b93ee29c837e95fb6b998b450f0ff73b9e65bf34d14ca26b0d7e", additionalDataHex: "ffffffff")
	report("seal-184", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "4c115e67ecd3d4178c4c60e713ab4e5ebb5357ed314ad740b9910fad6f01d781f047139a5f4e3f8e92d7a3b71eb4ff0ec8042b414fdd1bba3a6c936b7ed678797ca0085c32ddfcbeb01a8be4c34d53310031cb03a389508d1d9dd702b3d174b9", additionalDataHex: "ffffffff")
	report("seal-185", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c88ce708bf26aab862d97e1b42f31ef36be436e346f8f2b32f4cbbaef95150ef0438aca2438b588d5459493e97e7fa332fb76b5132e930f6d0acf70875e977b57b03478f2869b93ee29c837e95fb6b9923bd605f11854da59ba93955857b2d71", additionalDataHex: "ffffffff")
	report("seal-186", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "66115e67ecd3d4178c4c60e713ab4e5ef64296975af7fced168181f76c6508e1c947139a5f4e3f8e92d7a3b71eb4ff0e4975060f7ddef4a098699333b30fbf7c45a0085c32ddfcbeb01a8be4c34d53318140e64d918abf97bf98d75a7e08b3bc", additionalDataHex: "ffffffff")
	report("seal-187", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "c58ce708bf26aab862d97e1b42f31ef3783cf9302c7d22914b38aca2e7d374ef1d38aca2438b588d5459493e97e7fa33228f2d23597640d574f8e20c4f6b6bb56203478f2869b93ee29c837e95fb6b992e85262d7a1a3d863ffd2c51bff93171", additionalDataHex: "ffffffff")
	report("seal-188", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef35db72f89d1402b1a0373ff0a9c5cd44b6d67af40798f5455501792953248ec234ca6bfd9ae5c25a3a4d8a62d48a61d53", additionalDataHex: "ffffffff")
	report("seal-189", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef35f215ec87d62a264cadb519b4ac90a7668d1dd03e56eda6399ac7803e7dd22114910cd9a32bdab956d634cbb9d33d361", additionalDataHex: "ffffffff")
	report("seal-190", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060710abb165", inputHex: "dc8ce708bf26aab862d97e1b42f31ef3d15ad590dd0f40ba18acd168f6ac777a0f38aca2438b588d5459493e97e7fa33932a097f1d39a04ad30f1b6c650260bf7003478f2869b93ee29c837e95fb6b999f2002713e55dd19980ad53195903a7b", additionalDataHex: "ffffffff")
	report("seal-191", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "4d115e67ecd3d4178c4c60e713ab4e5e6ee628fc4b5830184cd293364a213e84fe47139a5f4e3f8e92d7a3b71eb4ff0e29db953ad5458fea61f013ea1854fe7572a0085c32ddfcbeb01a8be4c34d5331e1ee75783911c4dd46015783d553f2b5", additionalDataHex: "ffffffff")
	report("seal-192", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "3ede9b9ec8b247d42bbee2016d6715ba8567a7fde812a3aa2f552a33c1718c58e2f0b056a2eecc51d30838e640615e14bb8729fd148f23b2a916b7f40f2f29810dd14da7b4f76f9f68fa8903138d563c013eb732046a44e8707cd9f27873a8d4", additionalDataHex: "ffffffff")
	report("seal-194", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "6a115e67ecd3d4178c4c60e713ab4e5e519cccebf72573dbee8c12f74255d18c0add1035861ffc0b7f40079b969f8c63b2af4fa3ccd16cb38f425c3996140def", additionalDataHex: "ffffffff")
	report("seal-196", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "66115e67ecd3d4178c4c60e713ab4e5e8fab58574a322bac6f394474e4ce7eaec347139a5f4e3f8e92d7a3b71eb4ff0e71532dfb0e9141b00983394722829e7c4fa0085c32ddfcbeb01a8be4c34d5331b966cdb9e2c50a872e727d2eef8592bc", additionalDataHex: "ffffffff")
	report("seal-197", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "61115e67ecd3d4178c4c60e713ab4e5e5efe679ba17384c55eb8cc193666fe8d04608c3503d217aa3f90a9b0e1b3b313bc12d3a3491c8712cf92f212e138329f", additionalDataHex: "ffffffff")
	report("seal-198", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "9064b88a282052a1ee44df05ad213da679f8d1f971da17437a2b5e04fbca167151b2650ec945fec70588bc65a616a5f24f354c0c1580af3662d5f8151e3f7e82dd557ec8a4d63df7274594367bef09cd", additionalDataHex: "ffffffff")
	report("seal-199", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "43115e67ecd3d4178c4c60e713ab4e5eeef67bd4795b74015a3493905d544a86e847139a5f4e3f8e92d7a3b71eb4ff0e3197be28eff843592bd8fc8d578421d664a0085c32ddfcbeb01a8be4c34d5331f9a25e6a03ac086e0c29b8e49a832d16", additionalDataHex: "ffffffff")
	report("seal-200", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "39de9b9ec8b247d42bbee2016d6715ba4092e1f9a22c8b18184d805c128ade57c7f0b056a2eecc51d30838e640615e1464fe8b9bdd215a620973affefe93398528d14da7b4f76f9f68fa8903138d563cde471554cdc43d38d019c1f889cfb8d0", additionalDataHex: "ffffffff")
	report("seal-201", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "6b115e67ecd3d4178c4c60e713ab4e5e1e34412ab0a056e809d5d4b92be1128a4b2a651a62aeab26cf437fb195407574f3583a8c28603b9e3f41241395cbf4f8", additionalDataHex: "ffffffff")
	report("seal-202", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "3fe606108f35869df4c7aa0128464a1265f8d1f971da17437a2b5e04fbca1671fdbe843a0ad9be25055992ab6dcbc9f153354c0c1580af3662d5f8151e3f7e8271599ffc674a7d152794baf8b03265ce", additionalDataHex: "ffffffff")
	report("seal-203", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "6e8eb98cf7fffe4cd683568cf892991564f8d1f971da17437a2b5e04fbca1671c70f5d8b30c64bf2e6d1d613f40e0bf052354c0c1580af3662d5f8151e3f7e824be8464d5d5588c2c41cfe4029f7a7cf", additionalDataHex: "ffffffff")
	report("seal-204", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "66115e67ecd3d4178c4c60e713ab4e5e18f125ef374c1454b680e23427e7dc69e447139a5f4e3f8e92d7a3b71eb4ff0e858b08eb1d581570a7cd1e48593b757568a0085c32ddfcbeb01a8be4c34d53314dbee8a9f10c5e47803c5a21943c79b5", additionalDataHex: "ffffffff")
	report("seal-205", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "15de9b9ec8b247d42bbee2016d6715bacc1629a40cd11eafdf04138b45afe458eff0b056a2eecc51d30838e640615e14340ac9b45a5896a418a8cee8032e078f00d14da7b4f76f9f68fa8903138d563c8eb3577b4abdf1fec1c2a0ee747286da", additionalDataHex: "ffffffff")
	report("seal-206", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "b02ab747a310d6a3bbdb97018a3be8b341f8d1f971da17437a2b5e04fbca1671b7a338bc3423895f0fd96cdb27a787f277354c0c1580af3662d5f8151e3f7e823b44237a59b04a6f2d144488fa5e2bcd", additionalDataHex: "ffffffff")
	report("seal-207", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "40115e67ecd3d4178c4c60e713ab4e5e380ef93aeb61aa307f141323c38e0685f647139a5f4e3f8e92d7a3b71eb4ff0e3f769a30e8951ff2fb365fa780fdde7e7aa0085c32ddfcbeb01a8be4c34d5331f7437a7204c154c5dcc71bce4dfad2be", additionalDataHex: "ffffffff")
	report("seal-208", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "40115e67ecd3d4178c4c60e713ab4e5e060ef93aeb61aa307f141323c38e0685ee47139a5f4e3f8e92d7a3b71eb4ff0e2bca70bfcdf1171ab611d12bed5d627a62a0085c32ddfcbeb01a8be4c34d5331e3ff90fd21a55c2d91e09542205a6eba", additionalDataHex: "ffffffff")
	report("seal-209", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "56115e67ecd3d4178c4c60e713ab4e5e6c7e1312c6774fae7d1e5d0cc609028ff547139a5f4e3f8e92d7a3b71eb4ff0e81c9e61cbeeed5546b1ce5d8fef21a7a79a0085c32ddfcbeb01a8be4c34d533149fc065e52ba9e634ceda1b133f516ba", additionalDataHex: "ffffffff")
	report("seal-210", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "32de9b9ec8b247d42bbee2016d6715ba258d5d3e441683f546beba2e23755f5ccef0b056a2eecc51d30838e640615e149d13fdf8fa899836fa5c410d4ccd25ea21d14da7b4f76f9f68fa8903138d563c27aa6337ea6cff6c23362f0b3b91a4bf", additionalDataHex: "ffffffff")
	report("seal-211", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "2ea8410b4dca8c9d5369a033d8db61e46cf8d1f971da17437a2b5e04fbca1671f0f58e8bba6cf1a52146273d8fe0c4fc5a354c0c1580af3662d5f8151e3f7e827c12954dd7ff3295038b0f6e521968c3", additionalDataHex: "ffffffff")
	report("seal-212", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "17059a7c8883a28b90bd94ae44d1543662f8d1f971da17437a2b5e04fbca1671a23018bf8e68e413e99ac2d4ab3f8df154354c0c1580af3662d5f8151e3f7e822ed70379e3fb2723cb57ea8776c621ce", additionalDataHex: "ffffffff")
	report("seal-213", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "aaa1b258fd4b54b497b520806a66d7aa68f8d1f971da17437a2b5e04fbca167199132a234a8c789bf8544547940ec3f35e354c0c1580af3662d5f8151e3f7e8215f431e5271fbbabda996d1449f76fcc", additionalDataHex: "ffffffff")
	report("seal-214", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "9841cfc927a57dc491ab35427ff935e66ef8d1f971da17437a2b5e04fbca1671a683c8f9f9e6780fda4940ddedd76bf258354c0c1580af3662d5f8151e3f7e822a64d33f9475bb3ff884688e302ec7cd", additionalDataHex: "ffffffff")
	report("seal-215", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "f4ebbe3fca96bc4885b35582c43e0eb3588a85431430eada56a2c5dc944b6aa6b4570e8446e886bcbff82a24f49be5ed42e0943e30f91ba41b4362fa9ed6037b5b76f37550f12572040a9bc1a777edc5", additionalDataHex: "ffffffff")
	report("seal-216", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "42115e67ecd3d4178c4c60e713ab4e5e0b61bf9b7caf83cc34da625593514289e847139a5f4e3f8e92d7a3b71eb4ff0e696a5c7fb9da9cd4a39c8591086db42d64a0085c32ddfcbeb01a8be4c34d5331a15fbc3d558ed7e3846dc1f8c56ab8ed", additionalDataHex: "ffffffff")
	report("seal-217", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1ade9b9ec8b247d42bbee2016d6715ba571a3fca3cda7def4c93d4a382ca3a57eaf0b056a2eecc51d30838e640615e1476cddbee2f185776174f6df3bbe5b38105d14da7b4f76f9f68fa8903138d563ccc7445213ffd302cce2503f5ccb932d4", additionalDataHex: "ffffffff")
	report("seal-218", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "00010203040506072dd4cd40", inputHex: "4b115e67ecd3d4178c4c60e713ab4e5ef28e4d0f20ca1644470c9cdac6000887ed47139a5f4e3f8e92d7a3b71eb4ff0e1464775bacd5c69fe26e1a74968ea27e61a0085c32ddfcbeb01a8be4c34d5331dc51971940818da8c59f5e1d5b89aebe", additionalDataHex: "ffffffff")
	report("seal-219", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715babe31a501536a7c91e4a102cc27cdfe09d2f0b056a2eecc51d30838e640615e14dd9416a12e2f81bdee023d462feef7833dd14da7b4f76f9f68fa8903138d563c672d886e3ecae6e73768534058b276d6", additionalDataHex: "ffffffff")
	report("seal-220", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "97311cd6e2d25a7b4eaa16f0a61ca6246b8a85431430eada56a2c5dc944b6aa695136310b6b6b5c17c9f8c02ba7d0aeb71e0943e30f91ba41b4362fa9ed6037b7a329ee1a0af160fc76d3de7e99102c3", additionalDataHex: "ffffffff")
	report("seal-221", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "34de9b9ec8b247d42bbee2016d6715ba74cf7e9d82b7e8ed9ec965f6ea310951dc104940e08a4222556828eba459f65a4a006d28729d95d79d2372f77aeeab35", additionalDataHex: "ffffffff")
	report("seal-222", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "e72b83514e5e50509070359c1cac7e1c428a85431430eada56a2c5dc944b6aa6dad35950d8a9b55a472f9bb8860a526358e0943e30f91ba41b4362fa9ed6037b35f2a4a1ceb01694fcdd2a5dd5e65a4b", additionalDataHex: "ffffffff")
	report("seal-223", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "8c6165f445443588041b6e044fb6baae728a85431430eada56a2c5dc944b6aa6881a54c09516a1f1cae7b9dd71130ee168e0943e30f91ba41b4362fa9ed6037b673ba931830f023f7115083822ff06c9", additionalDataHex: "ffffffff")
	report("seal-224", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "18e36174545fa7ec9ea9f05d7057c5ca638a85431430eada56a2c5dc944b6aa6434e1c5e71005b690ca5cb8d580b89ed79e0943e30f91ba41b4362fa9ed6037bac6fe1af6719f8a7b7577a680be781c5", additionalDataHex: "ffffffff")
	report("seal-225", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "12de9b9ec8b247d42bbee2016d6715ba54305dff6b61c40b775c352d025c1a56d7f0b056a2eecc51d30838e640615e14bce574e9e11afedbdca021e53bb9188338d14da7b4f76f9f68fa8903138d563c065cea26f1ff998105ca4fe34ce599d6", additionalDataHex: "ffffffff")
	report("seal-226", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "36de9b9ec8b247d42bbee2016d6715ba1132811b2f18321ba99b12432c7f865aa3352cd2d7ac70b4c6f5419767926e20352508ba45bba7410ebe1b8bb925334f", additionalDataHex: "ffffffff")
	report("seal-227", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "31de9b9ec8b247d42bbee2016d6715baff746ef53ec3357cbc3c3ce4ab1d2d51ed9eb456dc9d9b59f656a5d2d974d26a7b8e903e4e8a4cac3e1dffce07c38f05", additionalDataHex: "ffffffff")
	report("seal-228", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "19de9b9ec8b247d42bbee2016d6715babf286fd979807951b183a188930ad15ecef0b056a2eecc51d30838e640615e1464413d71939b9cb0a4d32ef115da9e1021d14da7b4f76f9f68fa8903138d563cdef8a3be837efbea7db940f762861f45", additionalDataHex: "ffffffff")
	report("seal-229", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "19de9b9ec8b247d42bbee2016d6715babd286fd979807951b183a188930ad15ee3f0b056a2eecc51d30838e640615e14f25e78fe1b53ae416d1fbc698522618f0cd14da7b4f76f9f68fa8903138d563c48e7e6310bb6c91bb475d26ff27ee0da", additionalDataHex: "ffffffff")
	report("seal-230", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "3dde9b9ec8b247d42bbee2016d6715bac5629699cfd4d9036cef478ed705be5650f575882c3800f757ea6e0f8c6d47acc6e551e0be2fd7029fa1341352da1ac3", additionalDataHex: "ffffffff")
	report("seal-231", operation: Operation.seal, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", nonceHex: "000102030405060703e76f6f", inputHex: "1fde9b9ec8b247d42bbee2016d6715ba66d624f288f52941ca24865ce96f0d9736ff33a27c23f4976fc74f1fcd82f5cca0ef17caee342362a78c15031335a8a3", additionalDataHex: "ffffffff")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
seal-003 130 [4e54427e462f3beb69677d39865c5da8d57f603a85f7bf71368dce8ec9b9933c]
seal-074 80 [851b89482ee0b20b70f7acf35c759d2b6a67a516507c9319c9aa5c012ca1e5ca]
seal-075 80 [b9adec07f702c275caad08d2ba91dc9c9dd9aa3b28818373f9f2e7409687eab1]
seal-098 80 [c5ad4ee6b34a5b873ed9f14fcc78fd9adc66e0b41231425038baf9827cb5be93]
seal-099 144 [e9d1bccd929b0123514efac98aa4cf02df50c8491f70288ee544edaf3e6062e7]
seal-101 80 [fac08b69b1c9a206dc063557a006d62cb07c658779d8703ab88228760d3d3747]
seal-102 144 [c89f91dee977037bea58a67d3e2e90664a5982ea504083ca3807e6ddfa93c284]
seal-104 80 [5802a6511e7cde8fb80d649c1e14c2516ff7c6ebe9dffce7a069bacef9be3b7e]
seal-105 144 [097226f4724ecaac761e31e6310bab61e7c65d568df991445847968a3bedd3ec]
seal-107 80 [40a7271049daaaaea3923fbddde3821a1292ec2f0ea01d563c5528bdfe879273]
seal-108 144 [4ee42de9f7178271b007c5007f4a0f5de07e30d1e50930718812bb58d8112c37]
seal-110 80 [b7c9561b08f330564f70876371f3162e3c59635780f939a86b2165a9f7039334]
seal-111 144 [0b7db27a442e4ce4aa93ede0997007c4679232a41734df9bbae22a93159a788f]
seal-113 80 [560b75a437410eda545017fc1678e098b79ce897ad3ac84a9c87b586982816bd]
seal-114 144 [23a57bee3f11e81ae5ad5de4390025da80fa459bad5911a580aa332729e429d4]
seal-116 80 [4911edd8e0b86bf02963714e33c20d7f150438d9d63a0be694ac7d1e0887f73b]
seal-117 144 [6a22e5c0b6de388fba612c1a32cd0bbcf59419215a18bd71384b17f610723a61]
seal-119 80 [df7d3d4f711a465f4b667599a1dc65b0f731729acdd499beee700535c8e249a3]
seal-120 144 [e309c76eed8e9e8597a3120012d14518c4f6ff4e95a7ac3e915ccf184a57f659]
seal-121 144 [c64ec1780e3cee707d6d96ef984b7bdc83592e13f9b58f92718d381188f95bbc]
seal-122 144 [035dc33d429d940226cf46d64827d55ecc76632cdc62020d45ffad0dab5ee081]
seal-123 144 [66f663b76b9aa2966a18f90fe5d7154cd2bc5d4f131b496acb698d9985220283]
seal-124 144 [7790ac4561cc3a095c56675c7082a390affa96e2565025ddab328f4d82b26cba]
seal-125 144 [c5e0ddce1708abb223562bdadfff5f4b10f3d70db65e4a396992dcf34863f80a]
seal-126 144 [00d230c320fa4be75af24fc3a2c6f7586af30e24c260a8a020131acb7fdee146]
seal-127 144 [9121fc3e94fd54233ffaa45593079a4037402353516051f7db0bb0a5c28a37f4]
seal-128 144 [b9e245c003511452e6422a8734cf8f565879389800055172ae8982f8179df5e3]
seal-129 144 [3ed153cdab73504bf6b651ee48ef7112dd692eb14c845aa8b2ac9269ca869beb]
seal-130 144 [89623794eb70ec02a770aa8ac3d211e1ce2377a4085e5200212661576de0ad03]
seal-131 143 [f02df510834479a10db663b5f55cd8911fd56e100bc3d755b19eb5296fe373c1]
seal-132 143 [8de9970a5e71f10f9af7df7f708a470e507ac5fd9ab31bb8a538e74288ada7bf]
seal-133 143 [655ef389569f6fab71637c8fea0c05566ed81af2012160faafbc7d35d63a3621]
seal-134 143 [aeda3786299fcd2edf08bce0bbf99e7a745557ef66bd705fdb864cad96d59947]
seal-135 143 [6176c6c2dca3dac3c68f023a24ebcc01e6445b642a174e3ba1f6acde6859dee8]
seal-136 143 [172bc77d7e3819ea268c3e8fcb382a7fc58c5be79a9e42980e2cfd2cce36d1be]
seal-137 143 [58f59838f41f90abc21f4dce6b6668b8db7070d74e0279bdb85ab2a35ba92abc]
seal-138 143 [bff21ca0827bb8028ba8c67914fff87354ddd8eea31ab6a16f1261fba1ee2d11]
seal-139 143 [bcfbc6ab1545700f2527086b147e851c1ac79a0d4b7ebcca472fb37a154512e6]
seal-140 143 [df291682d6fc790d3447dd5cf56af921bd707dc0906916521a7e17b70917eac0]
seal-141 80 [73d55a0825fb1b5854ff6b908db16ea132249b84d3626336429a3cf5b30aa084]
seal-142 80 [80dba1f120d192835d2291578040a3dce6f9256ca605fb23ad6c8925bbeefe90]
seal-143 80 [6d92a8ea911d0d96dad7f2d76f2647e8b612e645140157668298db20a9412d4b]
seal-144 80 [74256b5c9272563dddef4875e4796203302ec71898eae914cd198f0631b7e611]
seal-145 80 [e4a8808a60e6601fee0127a06c85a123a43def6c63441ae28ac2713e78b1cd66]
seal-146 80 [fd9a26c11940edd237add98338a69e6eef534cdb8c2c28521f8eb56d3cd191d6]
seal-147 80 [eb4fddab2e56c36f19607fbf76181dff3b9c2cd17d6bf87ba69b82126bd0132b]
seal-148 112 [c190dc768f5cdac2466e907d6d7f093441b93be673d7524f85016ca6a56a114d]
seal-149 112 [e1b9e0b3ee76744676da78456f291ce43f2e966fd70ce5355235c108d5f0ab52]
seal-150 112 [abf14715482a0ad2aca26ea6a73ff9d18225f1cfdac1811a7b833eb308f4e7b8]
seal-151 112 [b61095719041d6879cb77a72c1142f48a304db32332cdb86fef566d1d14308b8]
seal-152 112 [e1c005969fc1ed4dbcd4540a656348b9a555a455797b0ee23ef2de44e1702113]
seal-153 112 [64504c9c6b2e29923acef85eabd6e9e105456e1b3acdb39ec2eb3b55ada7b09a]
seal-154 112 [8dc729e563babd1abb04179b49c15b3bcb4c83d521f66294f0be69ddd9e3ec1f]
seal-155 112 [fd610c60bf77d71d89625a83a8647cc4353a64fabbe7989b6b7f21e9d605f5d7]
seal-156 112 [ee5554aafd1654bf0dcd6602262d7b878fa26e20a28999c724e8404eb595d575]
seal-157 112 [7df6b6f7b4c070995c32c188d323e909bc6fb630246b5fddcf272ea6a4cc5796]
seal-158 112 [8db8d66aea926db41c2c64b786d27c60c7d475e38381074ab839d6f012905890]
seal-161 112 [c205eb10b78493e92ec37764fe6de7d2bf03b1bbe727b507bc7ef4116c17f9ac]
seal-162 112 [f4a0d4d2bdea69ed6ebbdb3bb40d56785c2306f6032aec792262a5c9458a3019]
seal-163 80 [c69e23b8644cdc21f59b17d076489cddcfb94223f0fc19a304c16c73e555f628]
seal-164 112 [18dfdea39cd258f6fd67137c2df715606aeb2506c72c43a5877093de81a777de]
seal-165 112 [6e333f01522e315f1e8e971fd4f45c773481800dc7d0ea76f10f4802caecb020]
seal-166 112 [d5243ebdcd2103a19fe7f4057392644067e93f030e23b85e56e862270ccad556]
seal-167 112 [50c9488e2b693a75cd147852c7438a6bd6a8130c70d182affd0980d9012441c0]
seal-168 112 [fd1137bd648a5b527fd7e3d4d904da03bcbbeb97b5eaf65fd30466cdf21ba06a]
seal-169 112 [fd9918a9c0474856a1912e7eb87d09057094908c2a02b94a700c76f86ef13669]
seal-170 112 [d6adb868b0c2c8fd06edeaa6e8da19a39c2d4c5a9aad0e51eb7a8c4d89f36425]
seal-171 112 [4ef793091e0954b0e7f4a1a80a39d626519fa16547b4e5f0a6a5b4cf9684e984]
seal-173 112 [16b71b7c49d766f3e76fc1c6850e484e485b69fea0d44c0cc792c01141366d9e]
seal-174 112 [d925e7de4658a6a1e6b62f152b541689a4108d50274404e38aee1cf1f7ba9432]
seal-175 112 [6e187352da29524f61db319fffc96c27a7cad864a60b68d89a80f4679200529c]
seal-176 80 [65a6f08aee4d0209cdd0eeff64d9f9ddde3545a0925fc9f76362fa731f94a6d8]
seal-177 112 [4682bbe0227eb60ba89f2c18db8c71adbbd5271abf0b43a4cf775380915daff9]
seal-178 112 [913fc23456836f2ad7bf4837e30530d6e228fe133a1077050a69608983a9448b]
seal-179 112 [d194aeade43e14319ec44dcff14ef511cc2eafc121baaf64d700ec2c53a65855]
seal-180 112 [b08eaba860c35ebc669a26a4c61dc83bef3e161a9356aec1965b4eb8986d8155]
seal-181 112 [46f43d7e3b804ec84a61eb9c44e4cc9de1b0065d48cf096b7fb88fba61ff82b7]
seal-182 112 [7c40654c2ba794c59b24323328acfc4d6c1adcc26014d14aadb8e4682bd1839d]
seal-183 112 [a663de97d1c5f93b07c48a5a438cbb29d1b8ed75d7c2855666b12f45a942d1d6]
seal-184 112 [7155986159bd86139ca387c1b35cf29fbafe37d7cf6a1ff70f07184a6c7444ca]
seal-185 112 [1e70534798cee76327ee2ab019531de98e8fb406ecc36d857f0e6264f8205f98]
seal-186 112 [5987dc0a91ee6b9d6a182008abd18e3f978c6a755a97a75a7b77928406acd533]
seal-187 112 [82f0cc3ece259249cb5d8e5400210e1d1d7f6f3910d1b2be2d365c5c534e1f82]
seal-188 80 [3e4856b91188b97dd340b4bf5b0820483e191a62c73e644b004955c3ab417815]
seal-189 80 [f4b1566e716162f301898e9fb74ba465fd73a2c5315b3f93366f558eb540639e]
seal-190 112 [ebfb0b449be0e71e20c57e0bfe2b4250ccd829cadb3ec60ab437bd099b027d6b]
seal-191 112 [9558678c198e49aed593e122855f1b922c4b8d8b7e4cb7f41b34959f36cb8e0e]
seal-192 112 [573dec519e6aa964eae89c26028ae9ef6851ea7f8cd2102b2014d3cb8a3f76d2]
seal-194 80 [10ca70fbbecc4a1a34f065f3468eec97c5ecadd7ee25e189600f4118bb6a31d1]
seal-196 112 [49f13b0152251fe75ee88ce5ae29ca18e23413e682ebd8ed19fcfa2f9eb7431d]
seal-197 80 [805ee482de7dba4808189a5f22d17e587a2488798518cb8a5ce1e219dd92aa61]
seal-198 96 [14d010eb97333e5f3059fbfb26cb1459ccf133dbe8e41df5cc84af0458ba842b]
seal-199 112 [0150a9bea5c776f48ae17401e499f42fcc1119f722043f5ad468f215e9e7559b]
seal-200 112 [64820e06fc48b4b688b7df27a684919ed0c07d9fc6b438697af083c4f4050d0d]
seal-201 80 [622a6464007137f78e064fcb840fbdea29db048799d94074e5111789f9e6610e]
seal-202 96 [d6e97ab87de26eaf5286840628cd263a262695947b17f32e393e0b7a15b9cb06]
seal-203 96 [27d61a9a7f79225bd2863580e7eb6f1a51a8cff5cb317907bc142f79234dc74a]
seal-204 112 [2377b17b0ec05dbb635354ccf8a028d491714e2b1a2ded2922f54b2671ee5f86]
seal-205 112 [6f4b4ee861c2679c3c861b2f6b6c3c073e9a6f9f5ab62bf7627f75a19e28e3c4]
seal-206 96 [a050a0cfe58af8bdcfe7a6559eca7ef57aab6068584144a16ef5b80c99dc7c4e]
seal-207 112 [d85d71d5df64b216691c46178cab803fea79bacbb8f60b685d27f06b5290f89a]
seal-208 112 [53b37a2f03f729328e0088952a133f440544b68c871eb86d234f54f78f9c6e6b]
seal-209 112 [6fd8865e0ce7ef490418fea34cc7ac36e00ef18630a579cc07c15bf9ca398b1d]
seal-210 112 [c8d411c206e8976254b44338ca922d9266a59c6c553533b673e813f2a08fc46d]
seal-211 96 [66a715f289d4a989348a85dc68b5ccad5193f8dcfd84ab8873e1464655285e4e]
seal-212 96 [1aaf690406b690782fd46d2ab634f53a9512d4bcbd04b8af127a15f4233fdf0a]
seal-213 96 [1b8c67881eb9dba3d60548ee58c10882de1a5ba2443482e7dea5ac89d6d10ee6]
seal-214 96 [cf6db8e4c4e00d46ee7afc217448ee3d5210ab4b3ab9804c00b21c63c127ed90]
seal-215 96 [2874036cfa63c033d5bc2af6bcc64203c9cbeec3f968d0a4dc58cf4471d2d89f]
seal-216 112 [64a4e4720abf5b9c8f633d59673198878494711d900a65b6bbac2c8fb9cb073e]
seal-217 112 [016ce3e0af16c4d2f3207721d99fdcbb37ab5d0671e7c2243b90ec048273de9c]
seal-218 112 [5f79bb7ab30a94b38c8ae3ba57cd24efc0e990edabc8b9d904c42651e3ca0e83]
seal-219 112 [d1b5afaa98cc2f2abc054787dd5d1a5e2ada104f6be58cfd63cb69406b4e4080]
seal-220 96 [873a89c44bbb8e2a5e0e59825326bf86c4b852efc678724112646a03b4bf1936]
seal-221 80 [c8947d6df2079790eafd750c74fd8dae62ff3c2d552d4034990e1ae6da697da8]
seal-222 96 [b9e39f69ac5e5a324e27670ea72310cdbfb8f5cf1c3ee2844561814e44256302]
seal-223 96 [0e0c7cbf41eac3d9a2bdae4572622ae99bf28bf1bb71233be8d198bb5254982f]
seal-224 96 [f181e15fbbe3f804634291cc66d574f433d17ca3222b8240c182446b3945d700]
seal-225 112 [12ea3b91533f97d73fdc58732b9a193eececb43f6513b3b80369334a27d77988]
seal-226 80 [5f5598da3155baed820bdfaa237dfbd58801ae142d1a5f805cf4947fa0afce49]
seal-227 80 [1aa0876c092a0d1dd17a903432b79e276d09f529caafc0027d905b7fbd82c034]
seal-228 112 [d866f05558064729f57a6b7a9e2f13de9aa5a2a3e29bd618e5e2d22ae1bb345c]
seal-229 112 [1f3b80aff4b2bb92c8ac9bbaf64a13d5e688ae69792ff36778824d71af58e7f5]
seal-230 80 [40e19d94dece3efd2fef7632ee79d7c26c4c1edda5dd351df7e09a110b1d6b84]
seal-231 80 [4dd2964435d25588f53c9ec0e0a7d4f7e50209e3e7d1ce95c6a1133afc146762]
```

<!-- test: chacha20-poly1305.open -->
Every `open` vector that authenticates, through the `Aead` interface.
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

enum Operation
	seal
	open
end 'Operation'

function errorName(failure AeadError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidNonceLength gives "invalidNonceLength"
		authenticationFailed gives "authenticationFailed"
		plaintextTooLong gives "plaintextTooLong"
	end 'name'
end 'errorName'

function perform(operation Operation, aead Aead, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let nonce = fromHex(nonceHex)
	let additionalData = fromHex(additionalDataHex)

	match operation 'direction'
		seal then return try aead.seal(nonce, plaintext: fromHex(inputHex), additionalData: additionalData)
		open then return try aead.open(nonce, sealed: fromHex(inputHex), additionalData: additionalData)
	end 'direction'
end 'perform'

function performWithKey(operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let aead = try ChaCha20Poly1305.create(fromHex(keyHex))
	return try perform(operation, aead: aead, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex)
end 'performWithKey'

function report(id String, operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String)
	if let output = try performWithKey(operation, keyHex: keyHex, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex) 'done'
		print("{id} [{toHex(output)}]\n")
	end 'done' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("open-001", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("open-002", operation: Operation.open, keyHex: "1c9240a5eb55d38af333888604f6b5f0473917c1402b80099dca5cbc207075c0", nonceHex: "000000000102030405060708", inputHex: "64a0861575861af460f062c79be643bd5e805cfd345cf389f108670ac76c8cb24c6cfc18755d43eea09ee94e382d26b0bdb7b73c321b0100d4f03b7f355894cf332f830e710b97ce98c8a84abd0b948114ad176e008d33bd60f982b1ff37c8559797a06ef4f0ef61c186324e2b3506383606907b6a7c02b0f9f6157b53c867e4b9166c767b804d46a59b5216cde7a4e99040c5a40433225ee282a1b0a06c523eaf4534d7f83fa1155b0047718cbc546a0d072b04b3564eea1b422273f548271a0bb2316053fa76991955ebd63159434ecebb4e466dae5a1073a6727627097a1049e617d91d361094fa68f0ff77987130305beaba2eda04df997b714d6c6f2c29a6ad5cb4022b02709beead9d67890cbb22392336fea1851f38", additionalDataHex: "f33388860000000000004e91")
	report("open-003", operation: Operation.open, keyHex: "80ba3192c803ce965ea371d5ff073cf0f43b6a2ab576b208426e11409c09b9b0", nonceHex: "4da5bf8dfd5852c1ea12379d", inputHex: "76acb342cf3166a5b63c0c0ea1383c8d", additionalDataHex: "")
	report("open-004", operation: Operation.open, keyHex: "7a4cd759172e02eb204db2c3f5c746227df584fc1345196391dbb9577a250742", nonceHex: "a92ef0ac991dd516a3c6f689", inputHex: "906fa6284b52f87b7359cbaa7563c709", additionalDataHex: "bd506764f2d2c410")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
open-001 [4c616469657320616e642047656e746c656d656e206f662074686520636c617373206f66202739393a204966204920636f756c64206f6666657220796f75206f6e6c79206f6e652074697020666f7220746865206675747572652c2073756e73637265656e20776f756c642062652069742e]
open-002 [496e7465726e65742d4472616674732061726520647261667420646f63756d656e74732076616c696420666f722061206d6178696d756d206f6620736978206d6f6e74687320616e64206d617920626520757064617465642c207265706c616365642c206f72206f62736f6c65746564206279206f7468657220646f63756d656e747320617420616e792074696d652e20497420697320696e617070726f70726961746520746f2075736520496e7465726e65742d447261667473206173207265666572656e6365206d6174657269616c206f7220746f2063697465207468656d206f74686572207468616e206173202fe2809c776f726b20696e2070726f67726573732e2fe2809d]
open-003 []
open-004 []
```

<!-- test: chacha20-poly1305.open-refuses-forgeries -->
Modified tags, ciphertexts, additional data, nonces and keys, and sealed inputs too short to hold a tag.
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

enum Operation
	seal
	open
end 'Operation'

function errorName(failure AeadError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidNonceLength gives "invalidNonceLength"
		authenticationFailed gives "authenticationFailed"
		plaintextTooLong gives "plaintextTooLong"
	end 'name'
end 'errorName'

function perform(operation Operation, aead Aead, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let nonce = fromHex(nonceHex)
	let additionalData = fromHex(additionalDataHex)

	match operation 'direction'
		seal then return try aead.seal(nonce, plaintext: fromHex(inputHex), additionalData: additionalData)
		open then return try aead.open(nonce, sealed: fromHex(inputHex), additionalData: additionalData)
	end 'direction'
end 'perform'

function performWithKey(operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let aead = try ChaCha20Poly1305.create(fromHex(keyHex))
	return try perform(operation, aead: aead, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex)
end 'performWithKey'

function report(id String, operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String)
	if let output = try performWithKey(operation, keyHex: keyHex, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex) 'done'
		print("{id} [{toHex(output)}]\n")
	end 'done' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("chacha20-poly1305-err-001", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f5409bb729039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-002", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f6409bb729039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-003", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "74409bb729039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-004", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4419bb729039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-005", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409b3729039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-006", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb728039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-007", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb72b039d0814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-008", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d8814ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-009", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0815ac514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-010", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d08148c514054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-011", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac504054323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-012", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514055323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-013", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514056323f44", additionalDataHex: "000102")
	report("chacha20-poly1305-err-014", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514054323f45", additionalDataHex: "000102")
	report("chacha20-poly1305-err-015", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514054323f46", additionalDataHex: "000102")
	report("chacha20-poly1305-err-016", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514054323f04", additionalDataHex: "000102")
	report("chacha20-poly1305-err-017", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d0814ac514054323fc4", additionalDataHex: "000102")
	report("chacha20-poly1305-err-018", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "f4409bb729039d8814ac514054323fc4", additionalDataHex: "000102")
	report("chacha20-poly1305-err-019", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "00000000000000000000000000000000", additionalDataHex: "000102")
	report("chacha20-poly1305-err-020", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "ffffffffffffffffffffffffffffffff", additionalDataHex: "000102")
	report("chacha20-poly1305-err-021", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a28914007a6119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-022", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a2b914007a6119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-023", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995aa9914007a6119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-024", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29904007a6119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-025", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914087a6119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-026", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a7119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-027", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a4119dd3f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-028", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119d53f109bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-029", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f009bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-030", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f129bba21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-031", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109baa21ce9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-032", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21de9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-033", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21ee9a7d6", additionalDataHex: "000102")
	report("chacha20-poly1305-err-034", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21ce9a7d7", additionalDataHex: "000102")
	report("chacha20-poly1305-err-035", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21ce9a7d4", additionalDataHex: "000102")
	report("chacha20-poly1305-err-036", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21ce9a796", additionalDataHex: "000102")
	report("chacha20-poly1305-err-037", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119dd3f109bba21ce9a756", additionalDataHex: "000102")
	report("chacha20-poly1305-err-038", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a29914007a6119d53f109bba21ce9a756", additionalDataHex: "000102")
	report("chacha20-poly1305-err-039", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995a00000000000000000000000000000000", additionalDataHex: "000102")
	report("chacha20-poly1305-err-040", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995affffffffffffffffffffffffffffffff", additionalDataHex: "000102")
	report("chacha20-poly1305-err-041", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e967405a16e8b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-042", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e964405a16e8b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-043", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e9e6405a16e8b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-044", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966415a16e8b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-045", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a96e8b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-046", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e9b44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-047", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16eab44eba92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-048", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44e3a92aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-049", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba93aa47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-050", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba928a47f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-051", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa46f5cea52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-052", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cfa52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-053", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cca52b7a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-054", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cea52b7b", additionalDataHex: "000102")
	report("chacha20-poly1305-err-055", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cea52b78", additionalDataHex: "000102")
	report("chacha20-poly1305-err-056", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cea52b3a", additionalDataHex: "000102")
	report("chacha20-poly1305-err-057", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44eba92aa47f5cea52bfa", additionalDataHex: "000102")
	report("chacha20-poly1305-err-058", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e966405a16e8b44e3a92aa47f5cea52bfa", additionalDataHex: "000102")
	report("chacha20-poly1305-err-059", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e900000000000000000000000000000000", additionalDataHex: "000102")
	report("chacha20-poly1305-err-060", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b", inputHex: "d03bcb3ca52d48d1d203b1e7b1a5995af1a0466a61bb386a2e12d189a2c4ea15e9ffffffffffffffffffffffffffffffff", additionalDataHex: "000102")
	report("chacha20-poly1305-err-070", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d21a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-071", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61961ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-072", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161be10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-073", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "51515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-074", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6")
	report("chacha20-poly1305-err-075", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "")
	report("chacha20-poly1305-err-076", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "060000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-077", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "0700000040414243444546c7", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-078", operation: Operation.open, keyHex: "818182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-079", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "1a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-080", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "1ae10b594f09e26a7e902ecbd0600691d31a8d34648e60db7b86afbc53ef7ec2a4aded51296e08fea9e2b5a736ee62d63dbea45e8ca9671282fafb69da92728b1a71de0a9e060b2905d6a5b67ecd3b3692ddbd7f2d778b8c9803aee328091b58fab324e4fad675945585808b4831d7bc3ff4def08e4b7a9de576d26586cec64b6116", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-081", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "e10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-082", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "070000004041424344454647", inputHex: "", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
chacha20-poly1305-err-001 authenticationFailed
chacha20-poly1305-err-002 authenticationFailed
chacha20-poly1305-err-003 authenticationFailed
chacha20-poly1305-err-004 authenticationFailed
chacha20-poly1305-err-005 authenticationFailed
chacha20-poly1305-err-006 authenticationFailed
chacha20-poly1305-err-007 authenticationFailed
chacha20-poly1305-err-008 authenticationFailed
chacha20-poly1305-err-009 authenticationFailed
chacha20-poly1305-err-010 authenticationFailed
chacha20-poly1305-err-011 authenticationFailed
chacha20-poly1305-err-012 authenticationFailed
chacha20-poly1305-err-013 authenticationFailed
chacha20-poly1305-err-014 authenticationFailed
chacha20-poly1305-err-015 authenticationFailed
chacha20-poly1305-err-016 authenticationFailed
chacha20-poly1305-err-017 authenticationFailed
chacha20-poly1305-err-018 authenticationFailed
chacha20-poly1305-err-019 authenticationFailed
chacha20-poly1305-err-020 authenticationFailed
chacha20-poly1305-err-021 authenticationFailed
chacha20-poly1305-err-022 authenticationFailed
chacha20-poly1305-err-023 authenticationFailed
chacha20-poly1305-err-024 authenticationFailed
chacha20-poly1305-err-025 authenticationFailed
chacha20-poly1305-err-026 authenticationFailed
chacha20-poly1305-err-027 authenticationFailed
chacha20-poly1305-err-028 authenticationFailed
chacha20-poly1305-err-029 authenticationFailed
chacha20-poly1305-err-030 authenticationFailed
chacha20-poly1305-err-031 authenticationFailed
chacha20-poly1305-err-032 authenticationFailed
chacha20-poly1305-err-033 authenticationFailed
chacha20-poly1305-err-034 authenticationFailed
chacha20-poly1305-err-035 authenticationFailed
chacha20-poly1305-err-036 authenticationFailed
chacha20-poly1305-err-037 authenticationFailed
chacha20-poly1305-err-038 authenticationFailed
chacha20-poly1305-err-039 authenticationFailed
chacha20-poly1305-err-040 authenticationFailed
chacha20-poly1305-err-041 authenticationFailed
chacha20-poly1305-err-042 authenticationFailed
chacha20-poly1305-err-043 authenticationFailed
chacha20-poly1305-err-044 authenticationFailed
chacha20-poly1305-err-045 authenticationFailed
chacha20-poly1305-err-046 authenticationFailed
chacha20-poly1305-err-047 authenticationFailed
chacha20-poly1305-err-048 authenticationFailed
chacha20-poly1305-err-049 authenticationFailed
chacha20-poly1305-err-050 authenticationFailed
chacha20-poly1305-err-051 authenticationFailed
chacha20-poly1305-err-052 authenticationFailed
chacha20-poly1305-err-053 authenticationFailed
chacha20-poly1305-err-054 authenticationFailed
chacha20-poly1305-err-055 authenticationFailed
chacha20-poly1305-err-056 authenticationFailed
chacha20-poly1305-err-057 authenticationFailed
chacha20-poly1305-err-058 authenticationFailed
chacha20-poly1305-err-059 authenticationFailed
chacha20-poly1305-err-060 authenticationFailed
chacha20-poly1305-err-070 authenticationFailed
chacha20-poly1305-err-071 authenticationFailed
chacha20-poly1305-err-072 authenticationFailed
chacha20-poly1305-err-073 authenticationFailed
chacha20-poly1305-err-074 authenticationFailed
chacha20-poly1305-err-075 authenticationFailed
chacha20-poly1305-err-076 authenticationFailed
chacha20-poly1305-err-077 authenticationFailed
chacha20-poly1305-err-078 authenticationFailed
chacha20-poly1305-err-079 authenticationFailed
chacha20-poly1305-err-080 authenticationFailed
chacha20-poly1305-err-081 authenticationFailed
chacha20-poly1305-err-082 authenticationFailed
```

<!-- test: chacha20-poly1305.refuses-invalid-lengths -->
Keys and nonces of other lengths for `seal` and `open`, and both at once, which reports the key.
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

enum Operation
	seal
	open
end 'Operation'

function errorName(failure AeadError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidNonceLength gives "invalidNonceLength"
		authenticationFailed gives "authenticationFailed"
		plaintextTooLong gives "plaintextTooLong"
	end 'name'
end 'errorName'

function perform(operation Operation, aead Aead, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let nonce = fromHex(nonceHex)
	let additionalData = fromHex(additionalDataHex)

	match operation 'direction'
		seal then return try aead.seal(nonce, plaintext: fromHex(inputHex), additionalData: additionalData)
		open then return try aead.open(nonce, sealed: fromHex(inputHex), additionalData: additionalData)
	end 'direction'
end 'perform'

function performWithKey(operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String) returns ByteArray throws AeadError
	let aead = try ChaCha20Poly1305.create(fromHex(keyHex))
	return try perform(operation, aead: aead, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex)
end 'performWithKey'

function report(id String, operation Operation, keyHex String, nonceHex String, inputHex String, additionalDataHex String)
	if let output = try performWithKey(operation, keyHex: keyHex, nonceHex: nonceHex, inputHex: inputHex, additionalDataHex: additionalDataHex) 'done'
		print("{id} [{toHex(output)}]\n")
	end 'done' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("chacha20-poly1305-err-061", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-062", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "0001020304050607", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-063", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-064", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-065", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c0d", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-066", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c0d0e0f", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-067", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c0d0e0f1011121314151617", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-068", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c0d0e0f10111213", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-069", operation: Operation.open, keyHex: "202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f", nonceHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", inputHex: "", additionalDataHex: "")
	report("chacha20-poly1305-err-083", operation: Operation.seal, keyHex: "", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-084", operation: Operation.seal, keyHex: "00000000000000000000000000000000", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-085", operation: Operation.seal, keyHex: "00000000000000000000000000000000000000000000000000000000000000", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-086", operation: Operation.seal, keyHex: "000000000000000000000000000000000000000000000000000000000000000000", nonceHex: "070000004041424344454647", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-087", operation: Operation.open, keyHex: "", nonceHex: "070000004041424344454647", inputHex: "def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-088", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-089", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "0000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-090", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "0000000000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-091", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "00000000000000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-092", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "00000000000000000000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-093", operation: Operation.seal, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "000000000000000000000000000000000000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-094", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "0000000000000000000000", inputHex: "def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-095", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "00000000000000000000000000", inputHex: "def08e4b7a9de576d26586cec64b61161ae10b594f09e26a7e902ecbd0600691", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-096", operation: Operation.seal, keyHex: "", nonceHex: "0000000000000000", inputHex: "4c616469657320616e642047656e746c", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	report("chacha20-poly1305-err-097", operation: Operation.open, keyHex: "808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f", nonceHex: "0000000000000000", inputHex: "", additionalDataHex: "50515253c0c1c2c3c4c5c6c7")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
chacha20-poly1305-err-061 invalidNonceLength
chacha20-poly1305-err-062 invalidNonceLength
chacha20-poly1305-err-063 invalidNonceLength
chacha20-poly1305-err-064 invalidNonceLength
chacha20-poly1305-err-065 invalidNonceLength
chacha20-poly1305-err-066 invalidNonceLength
chacha20-poly1305-err-067 invalidNonceLength
chacha20-poly1305-err-068 invalidNonceLength
chacha20-poly1305-err-069 invalidNonceLength
chacha20-poly1305-err-083 invalidKeyLength
chacha20-poly1305-err-084 invalidKeyLength
chacha20-poly1305-err-085 invalidKeyLength
chacha20-poly1305-err-086 invalidKeyLength
chacha20-poly1305-err-087 invalidKeyLength
chacha20-poly1305-err-088 invalidNonceLength
chacha20-poly1305-err-089 invalidNonceLength
chacha20-poly1305-err-090 invalidNonceLength
chacha20-poly1305-err-091 invalidNonceLength
chacha20-poly1305-err-092 invalidNonceLength
chacha20-poly1305-err-093 invalidNonceLength
chacha20-poly1305-err-094 invalidNonceLength
chacha20-poly1305-err-095 invalidNonceLength
chacha20-poly1305-err-096 invalidKeyLength
chacha20-poly1305-err-097 invalidNonceLength
```
