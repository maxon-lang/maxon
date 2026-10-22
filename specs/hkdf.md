---
feature: hkdf
status: stable
keywords: [hkdf, hkdfExtract, hkdfExpand, HkdfError, HkdfOutputLength, kdf, key derivation, HashAlgorithm, crypto]
category: crypto
---

# HKDF — extract-and-expand key derivation

## Documentation

| Call | Meaning |
|---|---|
| `hkdfExtract(algorithm, salt: salt, inputKeyMaterial: ikm)` | the pseudorandom key `HMAC(salt, ikm)` (RFC 5869 section 2.2); an empty salt is the hash's length in zero bytes |
| `hkdfExpand(algorithm, pseudoRandomKey: prk, info: info, length: n)` | `n` bytes of output keying material (RFC 5869 section 2.3) |
| `hkdf(algorithm, salt: salt, inputKeyMaterial: ikm, info: info, length: n)` | `hkdfExpand` of `hkdfExtract`, in one call |

`length` is an `HkdfOutputLength`. A length above 255 times the hash's length (8160 for SHA-256, 12240 for
SHA-384, 16320 for SHA-512) throws `HkdfError.outputTooLong`; `hkdf` checks it before deriving anything. A
length of 0 gives empty bytes.

The vectors are RFC 5869 appendix A.1 to A.3 and Project Wycheproof's HKDF cases. A maximum-length output is
pinned by its length and its SHA-256 digest.

## Tests

<!-- test: hkdf.extract -->
RFC 5869 A.1 to A.3, and an all-zero salt of the hash's length giving the PRK an empty salt gives.
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

function report(id String, algorithm HashAlgorithm, saltHex String, ikmHex String)
	print("{id} [{toHex(hkdfExtract(algorithm, salt: fromHex(saltHex), inputKeyMaterial: fromHex(ikmHex)))}]\n")
end 'report'

function main() returns ExitCode
	report("extract-001", algorithm: HashAlgorithm.sha256, saltHex: "000102030405060708090a0b0c", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b")
	report("extract-002", algorithm: HashAlgorithm.sha256, saltHex: "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf", ikmHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f")
	report("extract-003", algorithm: HashAlgorithm.sha256, saltHex: "", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b")
	report("extract-004", algorithm: HashAlgorithm.sha256, saltHex: "0000000000000000000000000000000000000000000000000000000000000000", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
extract-001 [077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5]
extract-002 [06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244]
extract-003 [19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04]
extract-004 [19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04]
```

<!-- test: hkdf.expand -->
RFC 5869 A.1 to A.3, and a length of 0.
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

function errorName(failure HkdfError) returns String
	return match failure 'name'
		outputTooLong gives "outputTooLong"
	end 'name'
end 'errorName'

function report(id String, algorithm HashAlgorithm, prkHex String, infoHex String, length HkdfOutputLength)
	if let okm = try hkdfExpand(algorithm, pseudoRandomKey: fromHex(prkHex), info: fromHex(infoHex), length: length) 'expanded'
		print("{id} [{toHex(okm)}]\n")
	end 'expanded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("expand-001", algorithm: HashAlgorithm.sha256, prkHex: "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5", infoHex: "f0f1f2f3f4f5f6f7f8f9", length: 42)
	report("expand-002", algorithm: HashAlgorithm.sha256, prkHex: "06a6b88c5853361a06104c9ceb35b45cef760014904671014a193f40c15fc244", infoHex: "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", length: 82)
	report("expand-003", algorithm: HashAlgorithm.sha256, prkHex: "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04", infoHex: "", length: 42)
	report("expand-004", algorithm: HashAlgorithm.sha256, prkHex: "077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5", infoHex: "f0f1f2f3f4f5f6f7f8f9", length: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
expand-001 [3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865]
expand-002 [b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87]
expand-003 [8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8]
expand-004 []
```

<!-- test: hkdf.expand-refuses-a-length-past-255-blocks -->
One byte past `255 * HashLen` for each algorithm.
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

function errorName(failure HkdfError) returns String
	return match failure 'name'
		outputTooLong gives "outputTooLong"
	end 'name'
end 'errorName'

function report(id String, algorithm HashAlgorithm, prkHex String, infoHex String, length HkdfOutputLength)
	if let okm = try hkdfExpand(algorithm, pseudoRandomKey: fromHex(prkHex), info: fromHex(infoHex), length: length) 'expanded'
		print("{id} [{toHex(okm)}]\n")
	end 'expanded' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("hkdf-err-004", algorithm: HashAlgorithm.sha256, prkHex: "19ef24a32c717b167f33a91d6f648bdf96596776afdb6377ac434c1c293ccb04", infoHex: "", length: 8161)
	report("hkdf-err-005", algorithm: HashAlgorithm.sha384, prkHex: "10e40cf072a4c5626e43dd22c1cf727d4bb140975c9ad0cbc8e45b40068f8f0ba57cdb598af9dfa6963a96899af047e5", infoHex: "", length: 12241)
	report("hkdf-err-006", algorithm: HashAlgorithm.sha512, prkHex: "fd200c4987ac491313bd4a2a13287121247239e11c9ef82802044b66ef357e5b194498d0682611382348572a7b1611de54764094286320578a863f36562b0df6", infoHex: "", length: 16321)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hkdf-err-004 outputTooLong
hkdf-err-005 outputTooLong
hkdf-err-006 outputTooLong
```

<!-- test: hkdf.derive -->
RFC 5869 A.1 to A.3, Wycheproof empty, zero and long salts for each algorithm, and a length of 0 for each algorithm.
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

function errorName(failure HkdfError) returns String
	return match failure 'name'
		outputTooLong gives "outputTooLong"
	end 'name'
end 'errorName'

function report(id String, algorithm HashAlgorithm, saltHex String, ikmHex String, infoHex String, length HkdfOutputLength)
	if let okm = try hkdf(algorithm, salt: fromHex(saltHex), inputKeyMaterial: fromHex(ikmHex), info: fromHex(infoHex), length: length) 'derived'
		print("{id} [{toHex(okm)}]\n")
	end 'derived' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("derive-001", algorithm: HashAlgorithm.sha256, saltHex: "000102030405060708090a0b0c", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", infoHex: "f0f1f2f3f4f5f6f7f8f9", length: 42)
	report("derive-002", algorithm: HashAlgorithm.sha256, saltHex: "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf", ikmHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f", infoHex: "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", length: 82)
	report("derive-003", algorithm: HashAlgorithm.sha256, saltHex: "", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", infoHex: "", length: 42)
	report("derive-004", algorithm: HashAlgorithm.sha256, saltHex: "", ikmHex: "3ee1fc0d8fac49d494c4a1b8cf6bf290a4a2c19a27c3ab1914d0d21c841577e0", infoHex: "4dc991623624fadef207587e42776cf3e0fdf4e5", length: 32)
	report("derive-005", algorithm: HashAlgorithm.sha256, saltHex: "0000000000000000000000000000000000000000000000000000000000000000", ikmHex: "3ee1fc0d8fac49d494c4a1b8cf6bf290a4a2c19a27c3ab1914d0d21c841577e0", infoHex: "4dc991623624fadef207587e42776cf3e0fdf4e5", length: 32)
	report("derive-006", algorithm: HashAlgorithm.sha384, saltHex: "", ikmHex: "24aeff2645e3e0f5494a9a102778c43a", infoHex: "", length: 20)
	report("derive-007", algorithm: HashAlgorithm.sha384, saltHex: "", ikmHex: "d04f44faa4542b34f958d8a863801f2c", infoHex: "ff6b5b655cbf2421a66d2f28408395a2ce57264f76bd60befd51cbfd9474faa47a97a9aa0f0e4338635633023cb36da12a3051f1f369355b687304e74c5218b315", length: 80)
	report("derive-008", algorithm: HashAlgorithm.sha384, saltHex: "", ikmHex: "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", infoHex: "6a5253cb802bf83c3b3941aabc08d524beedf003", length: 32)
	report("derive-009", algorithm: HashAlgorithm.sha384, saltHex: "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", ikmHex: "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", infoHex: "6a5253cb802bf83c3b3941aabc08d524beedf003", length: 32)
	report("derive-010", algorithm: HashAlgorithm.sha384, saltHex: "1ce459b88ed2529c24ade0334476e89f869de08e7814604027a9aa1cbbed99b5691898ea46c8ca8f51ab970d6bffb6983a8158b0b82bdbcb98e3014e41d6e625c7", ikmHex: "b77c1b4a865339d78c8e3ed7454ac6823b678d81126ce613c9e82ffab9e38ea88328d9eab8e88b409c1cedba7a74e8d7", infoHex: "6a5253cb802bf83c3b3941aabc08d524beedf003", length: 32)
	report("derive-011", algorithm: HashAlgorithm.sha512, saltHex: "", ikmHex: "24aeff2645e3e0f5494a9a102778c43a", infoHex: "", length: 20)
	report("derive-012", algorithm: HashAlgorithm.sha512, saltHex: "", ikmHex: "d04f44faa4542b34f958d8a863801f2c", infoHex: "ff6b5b655cbf2421a66d2f28408395a2ce57264f76bd60befd51cbfd9474faa47a97a9aa0f0e4338635633023cb36da12a3051f1f369355b687304e74c5218b315", length: 80)
	report("derive-013", algorithm: HashAlgorithm.sha512, saltHex: "", ikmHex: "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", infoHex: "6b478027231379b6bbed6c61732aad5636b5aab2", length: 32)
	report("derive-014", algorithm: HashAlgorithm.sha512, saltHex: "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000", ikmHex: "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", infoHex: "6b478027231379b6bbed6c61732aad5636b5aab2", length: 32)
	report("derive-015", algorithm: HashAlgorithm.sha512, saltHex: "bd37d35e915d6a3431e8ab0fe1f1a3d129d9b6dfcdcb627ae7eb6d15068145cff05fa37e6bca0721e9ae696281759f6f1f6784db7c8d30c4b75342ec6d6a1cc2af", ikmHex: "ddba77f227f237c791c07ee7e1ce9d933ecad3614212cc98d4f12afc90a3b21f4c7c3b4c54f44ad7d1e8ce45cd36aad5cb0bbf84e90cb054a1e7dc558690c5fa", infoHex: "6b478027231379b6bbed6c61732aad5636b5aab2", length: 32)
	report("derive-016", algorithm: HashAlgorithm.sha256, saltHex: "000102030405060708090a0b0c", ikmHex: "0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b", infoHex: "f0f1f2f3f4f5f6f7f8f9", length: 0)
	report("derive-017", algorithm: HashAlgorithm.sha384, saltHex: "", ikmHex: "24aeff2645e3e0f5494a9a102778c43a", infoHex: "", length: 0)
	report("derive-018", algorithm: HashAlgorithm.sha512, saltHex: "", ikmHex: "24aeff2645e3e0f5494a9a102778c43a", infoHex: "", length: 0)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
derive-001 [3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865]
derive-002 [b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87]
derive-003 [8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8]
derive-004 [f41f703259063d401de67cea9192038ddddc71ede5cdb383aad71894c1a39e8c]
derive-005 [f41f703259063d401de67cea9192038ddddc71ede5cdb383aad71894c1a39e8c]
derive-006 [4b7045423d9156424b0b85d95a7d602fba3924b1]
derive-007 [77565d63e3ca67928bb69a3ac37b19014f77a48a0faae7310730b5186f31498e9797120eab415d26d0084662174cd1d589f0c6d8226ce9870ebdd19b84838e047bb5613eb530a3ebd50be02b2e984a74]
derive-008 [e376973755ee66c610613b236f3e1f999ed724d5651c79fdbd0389180be39cda]
derive-009 [e376973755ee66c610613b236f3e1f999ed724d5651c79fdbd0389180be39cda]
derive-010 [7d3f1a70a9190cd64613f5eb9d18b6f050b4113cba21a99b68c0af395dd7601e]
derive-011 [dd2599840b09699c6200b5cba79002b3aa75c61b]
derive-012 [76ebb3133cbf07afb653393bea4e4ecdab9d8e8fcc5e79e67d5346312ce9e46e937b3393170b811e25f7621923f74e519b604ae2b9884fe2d7202782ee9e20100f95341d8b6ff1f39db23bab7b7edcd6]
derive-013 [1a4d9bb4aebb50780a57fe8e922dfbf5ba6018236b2196a0f96d704b063f739e]
derive-014 [1a4d9bb4aebb50780a57fe8e922dfbf5ba6018236b2196a0f96d704b063f739e]
derive-015 [54b3b9a5d7f1713194b8e0621d8d33c6f2034b5005d47bbc90e5883d6af9bc42]
derive-016 []
derive-017 []
derive-018 []
```

<!-- test: hkdf.derive-the-maximum-length -->
`255 * HashLen` bytes for each algorithm, pinned by length and SHA-256 digest.
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

function errorName(failure HkdfError) returns String
	return match failure 'name'
		outputTooLong gives "outputTooLong"
	end 'name'
end 'errorName'

function report(id String, algorithm HashAlgorithm, saltHex String, ikmHex String, infoHex String, length HkdfOutputLength)
	if let okm = try hkdf(algorithm, salt: fromHex(saltHex), inputKeyMaterial: fromHex(ikmHex), info: fromHex(infoHex), length: length) 'derived'
		print("{id} {okm.count()} [{toHex(sha256(okm))}]\n")
	end 'derived' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("derive-019", algorithm: HashAlgorithm.sha256, saltHex: "ccdc54836e6046d5c851f917c37b0baeabd225e996b8f71d452cff5036345c3e", ikmHex: "91556a70e96dccb6a2fbee28250bdbd5", infoHex: "2b90f142b2310b23", length: 8160)
	report("derive-020", algorithm: HashAlgorithm.sha384, saltHex: "be99dc17920c2da449ed11592d1a134f5d97b631ce06a8b4070113ca5f18e6aa7b129a2c8d2120b38779c6ec8ac43e08", ikmHex: "d19f24915e80b5ba3b0a9f87b9acd21b", infoHex: "aecafc9c85b224c2", length: 12240)
	report("derive-021", algorithm: HashAlgorithm.sha512, saltHex: "7ea9bd3ed65fdbec456e4859209d0e87336b0a04bd342fccede6fa4cb12331789a9af13d3c96e955a74550883aa5a80e055ad7682a26c8b2343088d05f15f80d", ikmHex: "83c6fa101a922bf4e0ab38c1853b82f8", infoHex: "c893d162bf0036e4", length: 16320)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
derive-019 8160 [a14afe5e70c25e95339abe6739f41cd27b0ab5f87480ee7e22edb5d419abaeab]
derive-020 12240 [ab5f06d6c2745381d8ae492f43ed951d772d8146c5d9fc0a2616adc78f2bef7d]
derive-021 16320 [f1d2571780c8a83db38dd812b9cab70a8c2e66bfaf1a2fb3632a154ce63a524a]
```

<!-- test: hkdf.derive-refuses-a-length-past-255-blocks -->
One byte past `255 * HashLen` for each algorithm.
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

function errorName(failure HkdfError) returns String
	return match failure 'name'
		outputTooLong gives "outputTooLong"
	end 'name'
end 'errorName'

function report(id String, algorithm HashAlgorithm, saltHex String, ikmHex String, infoHex String, length HkdfOutputLength)
	if let okm = try hkdf(algorithm, salt: fromHex(saltHex), inputKeyMaterial: fromHex(ikmHex), info: fromHex(infoHex), length: length) 'derived'
		print("{id} [{toHex(okm)}]\n")
	end 'derived' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("hkdf-err-001", algorithm: HashAlgorithm.sha256, saltHex: "d5efc88adf3d5afc970284aab51690bdfedfa40be98e374efa3060ccf97fc650", ikmHex: "db89f54af757f8c7e57248a1718105b1", infoHex: "134f085797b1ae2e", length: 8161)
	report("hkdf-err-002", algorithm: HashAlgorithm.sha384, saltHex: "519ba8ec8bbccca06a7dbb54ea2dd6fc7074a7caa700101092272ec85113d0fff39a32d960d4ccc4990a3467ec935bd8", ikmHex: "2ea7f38feeb05b81dcae938af2f8e5ab", infoHex: "7bd521a6fc9c4e24", length: 12241)
	report("hkdf-err-003", algorithm: HashAlgorithm.sha512, saltHex: "c4aed79aa346a7914b43668ddc839a249652dcf11b8aa051bcd1796d1ef5e5f766380b4349333359a1177d0c082b1f0de90bdff1fb60646ede4f08a2b1f63786", ikmHex: "68563b0e902eb3cc65fbf7f115845b12", infoHex: "b0aaceb3b0b8c885", length: 16321)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hkdf-err-001 outputTooLong
hkdf-err-002 outputTooLong
hkdf-err-003 outputTooLong
```
