---
feature: hmac
status: stable
keywords: [hmac, mac, constantTimeEqual, constant-time, HashAlgorithm, sha256, sha384, sha512, crypto, authentication]
category: crypto
---

# HMAC and constant-time comparison

## Documentation

| Call | Meaning |
|---|---|
| `hmac(algorithm, key: key, message: message)` | the full-length HMAC (RFC 2104, FIPS 198-1) of `message` under `key`, over the `HashAlgorithm`'s hash: 32, 48 or 64 bytes |
| `constantTimeEqual(a, b: b)` | `true` when `a` and `b` have the same length and the same bytes, in time that depends only on their lengths |

A key longer than the hash's block size (64 bytes for SHA-256, 128 for SHA-384 and SHA-512) is hashed first; a
shorter one, the empty key included, is zero-padded to the block size. A received tag is checked with
`constantTimeEqual`, never with an ordinary comparison, which stops at the first difference.

The vectors are RFC 4231 test cases 1 to 7 (case 5 at full length), the Wycheproof empty-message cases, the NIST
CAVP block-sized keys and the empty key.

## Tests

<!-- test: hmac.tags -->
Every HMAC vector for the three algorithms: RFC 4231 cases 1 to 7, an empty message, a key exactly one block long, and an empty key with an empty message.
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

function report(id String, algorithm HashAlgorithm, keyHex String, messageHex String)
	print("{id} [{toHex(hmac(algorithm, key: fromHex(keyHex), message: fromHex(messageHex)))}]\n")
end 'report'

function main() returns ExitCode
	report("hmac-001", algorithm: HashAlgorithm.sha256, keyHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", messageHex: "4869205468657265")
	report("hmac-002", algorithm: HashAlgorithm.sha384, keyHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", messageHex: "4869205468657265")
	report("hmac-003", algorithm: HashAlgorithm.sha512, keyHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", messageHex: "4869205468657265")
	report("hmac-004", algorithm: HashAlgorithm.sha256, keyHex: "4a656665", messageHex: "7768617420646f2079612077616e7420666f72206e6f7468696e673f")
	report("hmac-005", algorithm: HashAlgorithm.sha384, keyHex: "4a656665", messageHex: "7768617420646f2079612077616e7420666f72206e6f7468696e673f")
	report("hmac-006", algorithm: HashAlgorithm.sha512, keyHex: "4a656665", messageHex: "7768617420646f2079612077616e7420666f72206e6f7468696e673f")
	report("hmac-007", algorithm: HashAlgorithm.sha256, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")
	report("hmac-008", algorithm: HashAlgorithm.sha384, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")
	report("hmac-009", algorithm: HashAlgorithm.sha512, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")
	report("hmac-010", algorithm: HashAlgorithm.sha256, keyHex: "0102030405060708090a0b0c0d0e0f10111213141516171819", messageHex: "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd")
	report("hmac-011", algorithm: HashAlgorithm.sha384, keyHex: "0102030405060708090a0b0c0d0e0f10111213141516171819", messageHex: "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd")
	report("hmac-012", algorithm: HashAlgorithm.sha512, keyHex: "0102030405060708090a0b0c0d0e0f10111213141516171819", messageHex: "cdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcdcd")
	report("hmac-013", algorithm: HashAlgorithm.sha256, keyHex: "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", messageHex: "546573742057697468205472756e636174696f6e")
	report("hmac-014", algorithm: HashAlgorithm.sha384, keyHex: "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", messageHex: "546573742057697468205472756e636174696f6e")
	report("hmac-015", algorithm: HashAlgorithm.sha512, keyHex: "0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c0c", messageHex: "546573742057697468205472756e636174696f6e")
	report("hmac-016", algorithm: HashAlgorithm.sha256, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374")
	report("hmac-017", algorithm: HashAlgorithm.sha384, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374")
	report("hmac-018", algorithm: HashAlgorithm.sha512, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "54657374205573696e67204c6172676572205468616e20426c6f636b2d53697a65204b6579202d2048617368204b6579204669727374")
	report("hmac-019", algorithm: HashAlgorithm.sha256, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e")
	report("hmac-020", algorithm: HashAlgorithm.sha384, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e")
	report("hmac-021", algorithm: HashAlgorithm.sha512, keyHex: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", messageHex: "5468697320697320612074657374207573696e672061206c6172676572207468616e20626c6f636b2d73697a65206b657920616e642061206c6172676572207468616e20626c6f636b2d73697a6520646174612e20546865206b6579206e6565647320746f20626520686173686564206265666f7265206265696e6720757365642062792074686520484d414320616c676f726974686d2e")
	report("hmac-022", algorithm: HashAlgorithm.sha256, keyHex: "1e225cafb90339bba1b24076d4206c3e79c355805d851682bc818baa4f5a7779", messageHex: "")
	report("hmac-023", algorithm: HashAlgorithm.sha384, keyHex: "ee8df067857df2300fa71a10c30997178bb3796127b5ece5f2ccc170932be0e78ea9b0a5936c09157e671ce7ec9fc510", messageHex: "")
	report("hmac-024", algorithm: HashAlgorithm.sha512, keyHex: "5365244bb43f23f18dfc86c09d62db4741138bec1fbddc282d295e0a098eb5c3e37bd6f4cc16d5ce7d77b1d474a1eb4db313cc0c24e48992ac125196549df9a8", messageHex: "")
	report("hmac-025", algorithm: HashAlgorithm.sha256, keyHex: "992868504d2564c4fb47bcbd4ae482d8fb0e8e56d7b81864e61986a0e25682daeb5b50177c095edc9e971da95c3210c376e723365ac33d1b4f391817f4c35124", messageHex: "ed4f269a8851eb3154771516b27228155200778049b2dc1963f3ac32ba46ea1387cfbb9c39151a2cc406cdc13c3c9860a27eb0b7fe8a7201ad11552afd041e33f70e53d97c62f17194b66117028fa9071cc0e04bd92de4972cd54f719010a694e414d4977abed7ca6b90ba612df6c3d467cded85032598a48546804f9cf2ecfe")
	report("hmac-026", algorithm: HashAlgorithm.sha384, keyHex: "b619d9d07461c11bc9fb66117d61ed90001366bbffdbff583556777584b0d65244af5c7bdbf3b7358d7c791b966cc809760e57398d1896ace72d26cc59a6904fcd92365edafb8af7986c7d90b2af3bfdbcdb1593c78fbe8e3378bbb0c519152bf9cb51c19a02a12a8fd35cb6f8b3ac337a828711d6c8e0c4c13e1e6af090cdae", messageHex: "5a81e711adfe5077dd8c8b57c95e8e1f3de39f4fc448c523bd3e7c72b1fdacd6e489dc0d2a34a39ffc6460c1cb962b7a94a30c04b5426a75ffcfc69f0c4ba934d3a3da2e7935d56d6b9079a2a97b016d653a35c2cc0ce19124f887a617c951ce4e58493b4209cc294f983cc20b16f63fd52e8451b1ad13bf5342275079818deb")
	report("hmac-027", algorithm: HashAlgorithm.sha512, keyHex: "e9e4480d1c4a621e0c4e1505992556347a7ab34fd2b289910474766cc969116f8040d96dc5f66cdc4454fa7bcfb9f838af19195038467ab8a16e1cbc12e598e6fd250e21b2145f1e2e859cf73400be12a0c69749f7100847429875351d5a76970b9ccf700c2ca3ad72e9e4c0f0840e8cf488158136989b0891f867211350134a", messageHex: "b82eefb2081bd14dab0e9e345248a34ade73f3291886b91ea3e8cc742fd884f6ee0ccdaf4c9879f4db12dba58cf491af2541a1d5ef6cc8b1af750ef5d8559ef7ff9cd56d8f599974be3aecd8c0f4c08f3ae50d86f9f822a1e4ca39fd2f0b4d78d22630733a24d8d63ecdf9555411daf205a761c39ef46ff6292e74129bc13a7f")
	report("hmac-028", algorithm: HashAlgorithm.sha256, keyHex: "", messageHex: "")
	report("hmac-029", algorithm: HashAlgorithm.sha384, keyHex: "", messageHex: "")
	report("hmac-030", algorithm: HashAlgorithm.sha512, keyHex: "", messageHex: "")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hmac-001 [b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7]
hmac-002 [afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59cfaea9ea9076ede7f4af152e8b2fa9cb6]
hmac-003 [87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cdedaa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854]
hmac-004 [5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843]
hmac-005 [af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649]
hmac-006 [164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737]
hmac-007 [773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe]
hmac-008 [88062608d3e6ad8a0aa2ace014c8a86f0aa635d947ac9febe83ef4e55966144b2a5ab39dc13814b94e3ab6e101a34f27]
hmac-009 [fa73b0089d56a284efb0f0756c890be9b1b5dbdd8ee81a3655f83e33b2279d39bf3e848279a722c806b485a47e67c807b946a337bee8942674278859e13292fb]
hmac-010 [82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b]
hmac-011 [3e8a69b7783c25851933ab6290af6ca77a9981480850009cc5577c6e1f573b4e6801dd23c4a7d679ccf8a386c674cffb]
hmac-012 [b0ba465637458c6990e5a8c5f61d4af7e576d97ff94b872de76f8050361ee3dba91ca5c11aa25eb4d679275cc5788063a5f19741120c4f2de2adebeb10a298dd]
hmac-013 [a3b6167473100ee06e0c796c2955552bfa6f7c0a6a8aef8b93f860aab0cd20c5]
hmac-014 [3abf34c3503b2a23a46efc619baef897f4c8e42c934ce55ccbae9740fcbc1af4ca62269e2a37cd88ba926341efe4aeea]
hmac-015 [415fad6271580a531d4179bc891d87a650188707922a4fbb36663a1eb16da008711c5b50ddd0fc235084eb9d3364a1454fb2ef67cd1d29fe6773068ea266e96b]
hmac-016 [60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54]
hmac-017 [4ece084485813e9088d2c63a041bc5b44f9ef1012a2b588f3cd11f05033ac4c60c2ef6ab4030fe8296248df163f44952]
hmac-018 [80b24263c7c1a3ebb71493c1dd7be8b49b46d1f41b4aeec1121b013783f8f3526b56d037e05f2598bd0fd2215d6a1e5295e64f73f63f0aec8b915a985d786598]
hmac-019 [9b09ffa71b942fcb27635fbcd5b0e944bfdc63644f0713938a7f51535c3a35e2]
hmac-020 [6617178e941f020d351e2f254e8fd32c602420feb0b8fb9adccebb82461e99c5a678cc31e799176d3860e6110c46523e]
hmac-021 [e37b6a775dc87dbaa4dfa9f96e5e3ffddebd71f8867289865df5a32d20cdc944b6022cac3c4982b10d5eeb55c3e4de15134676fb6de0446065c97440fa8c6a58]
hmac-022 [b175b57d89ea6cb606fb3363f2538abd73a4c00b4a1386905bac809004cf1933]
hmac-023 [a655184daf3346ffc6629d493c8442644e4996a2799e42e3306fa6f5b0967b6cf3a6f819bab89bce297d1d1a5907b2d0]
hmac-024 [d0a556bd1afa8df1ebf9e3ee683a8a2450a7c83eba2daf2e2ff2f953f0cd64da216e67134cf55578b205c8a1e241ba1369516a5ef4298b9c1d31e9d59fc04fe4]
hmac-025 [2f8321f416b9bb249f113b13fc12d70e1668dc332839c10daa5717896cb70ddf]
hmac-026 [8b4258be4c094aa4056f332ede8c733772664b088ba22ef8caae7cefd77eceb35e83af8d9c1283cbbffe4a372b699c21]
hmac-027 [90093bdcc45da7338bd2efe92e30933b14f75582739c747f7572b3270b104f33af0c939e3c8ae53b2066fc8c97ccf38785cd2ec3d79e6946499d36121e44a3e7]
hmac-028 [b613679a0814d9ec772f95d778c35fc5ff1697c493715653c6c712144292c5ad]
hmac-029 [6c1f2ee938fad2e24bd91298474382ca218c75db3d83e114b3d4367776d14d3551289e75e8209cd4b792302840234adc]
hmac-030 [b936cee86c9f87aa5d3c6f2e84cb5a4239a5fe50480a6ec66b70ab5b1f4ac6730c6c515421b327ec1d69402e53dfb49ad7381eb067b338fd7b0cb22247225d47]
```

<!-- test: hmac.constant-time-equal -->
Equal inputs, a difference in the first, the last or every byte, two empty inputs, and inputs of different lengths, one a prefix of the other.
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

function report(id String, aHex String, bHex String)
	print("{id} {constantTimeEqual(fromHex(aHex), b: fromHex(bHex))}\n")
end 'report'

function main() returns ExitCode
	report("cte-001", aHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", bHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")
	report("cte-002", aHex: "00", bHex: "00")
	report("cte-003", aHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", bHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e20")
	report("cte-004", aHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", bHex: "800102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")
	report("cte-005", aHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", bHex: "fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0efeeedecebeae9e8e7e6e5e4e3e2e1e0")
	report("cte-006", aHex: "", bHex: "")
	report("cte-007", aHex: "", bHex: "00")
	report("cte-008", aHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", bHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e")
	report("cte-009", aHex: "000102030405060708090a0b0c0d0e0f", bHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
cte-001 true
cte-002 true
cte-003 false
cte-004 false
cte-005 false
cte-006 true
cte-007 false
cte-008 false
cte-009 false
```
