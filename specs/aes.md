---
feature: aes
status: stable
keywords: [aes, Aes, AesError, AesKeyStreamError, AesCounter, encryptBlock, decryptBlock, xorKeyStream, block cipher, counter mode, fips197, constant-time, crypto]
category: crypto
---

# `Aes` — the AES block cipher

## Documentation

| Call | Meaning |
|---|---|
| `Aes.create(key)` | the AES cipher for a 16-, 24- or 32-byte key: AES-128, AES-192 or AES-256 |
| `aes.encryptBlock(block)` | the FIPS 197 cipher of one 16-byte block |
| `aes.decryptBlock(block)` | the FIPS 197 inverse cipher of one 16-byte block |
| `aes.xorKeyStream(nonce, counter: c, data: d)` | `d` XORed with the counter-mode keystream: block `j` is the cipher of the 12-byte `nonce` followed by `c + j` as a 32-bit big-endian counter |

`create` throws `AesError.invalidKeyLength` for a key of any other length. `encryptBlock` and `decryptBlock`
throw `AesError.invalidBlockLength` for a block that is not exactly 16 bytes. `xorKeyStream` is its own
inverse; it throws `AesKeyStreamError.invalidNonceLength` for a nonce that is not exactly 12 bytes, then
`AesKeyStreamError.counterOverflow` when `d` needs a block counter above `2^32 - 1`. `Aes` runs in constant
time: no table lookup and no branch depends on the key, the block or the data.

The vectors are FIPS 197 appendices B and C, the NIST AESAVS known-answer tests and the NIST SP 800-38A CTR
examples.

## Tests

<!-- test: aes.encrypt-block -->
FIPS 197 appendices B and C and the AESAVS GFSbox, KeySbox, VarKey and VarTxt known answers, for each key size.
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

enum Direction
	encrypt
	decrypt
end 'Direction'

function errorName(failure AesError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidBlockLength gives "invalidBlockLength"
	end 'name'
end 'errorName'

function cipher(direction Direction, keyHex String, blockHex String) returns ByteArray throws AesError
	let aes = try Aes.create(fromHex(keyHex))
	let block = fromHex(blockHex)

	match direction 'operation'
		encrypt then return try aes.encryptBlock(block)
		decrypt then return try aes.decryptBlock(block)
	end 'operation'
end 'cipher'

function report(id String, direction Direction, keyHex String, blockHex String)
	if let output = try cipher(direction, keyHex: keyHex, blockHex: blockHex) 'ciphered'
		print("{id} [{toHex(output)}]\n")
	end 'ciphered' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("encrypt-001", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f", blockHex: "00112233445566778899aabbccddeeff")
	report("encrypt-002", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f1011121314151617", blockHex: "00112233445566778899aabbccddeeff")
	report("encrypt-003", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", blockHex: "00112233445566778899aabbccddeeff")
	report("encrypt-004", direction: Direction.encrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("encrypt-005", direction: Direction.encrypt, keyHex: "00000000000000000000000000000000", blockHex: "f34481ec3cc627bacd5dc3fb08f273e6")
	report("encrypt-006", direction: Direction.encrypt, keyHex: "00000000000000000000000000000000", blockHex: "58c8e00b2631686d54eab84b91f0aca1")
	report("encrypt-007", direction: Direction.encrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "1b077a6af4b7f98229de786d7516b639")
	report("encrypt-008", direction: Direction.encrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "941a4773058224e1ef66d10e0a6ee782")
	report("encrypt-009", direction: Direction.encrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "014730f80ac625fe84f026c60bfd547d")
	report("encrypt-010", direction: Direction.encrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "91fbef2d15a97816060bee1feaa49afe")
	report("encrypt-011", direction: Direction.encrypt, keyHex: "10a58869d74be5a374cf867cfb473859", blockHex: "00000000000000000000000000000000")
	report("encrypt-012", direction: Direction.encrypt, keyHex: "febd9a24d8b65c1c787d50a4ed3619a9", blockHex: "00000000000000000000000000000000")
	report("encrypt-013", direction: Direction.encrypt, keyHex: "e9f065d7c13573587f7875357dfbb16c53489f6a4bd0f7cd", blockHex: "00000000000000000000000000000000")
	report("encrypt-014", direction: Direction.encrypt, keyHex: "c88f5b00a4ef9a6840e2acaf33f00a3bdc4e25895303fa72", blockHex: "00000000000000000000000000000000")
	report("encrypt-015", direction: Direction.encrypt, keyHex: "c47b0294dbbbee0fec4757f22ffeee3587ca4730c3d33b691df38bab076bc558", blockHex: "00000000000000000000000000000000")
	report("encrypt-016", direction: Direction.encrypt, keyHex: "fca02f3d5011cfc5c1e23165d413a049d4526a991827424d896fe3435e0bf68e", blockHex: "00000000000000000000000000000000")
	report("encrypt-017", direction: Direction.encrypt, keyHex: "80000000000000000000000000000000", blockHex: "00000000000000000000000000000000")
	report("encrypt-018", direction: Direction.encrypt, keyHex: "ffffffffffffffffffffffffffffffff", blockHex: "00000000000000000000000000000000")
	report("encrypt-019", direction: Direction.encrypt, keyHex: "800000000000000000000000000000000000000000000000", blockHex: "00000000000000000000000000000000")
	report("encrypt-020", direction: Direction.encrypt, keyHex: "ffffffffffffffffffffffffffffffffffffffffffffffff", blockHex: "00000000000000000000000000000000")
	report("encrypt-021", direction: Direction.encrypt, keyHex: "8000000000000000000000000000000000000000000000000000000000000000", blockHex: "00000000000000000000000000000000")
	report("encrypt-022", direction: Direction.encrypt, keyHex: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", blockHex: "00000000000000000000000000000000")
	report("encrypt-023", direction: Direction.encrypt, keyHex: "00000000000000000000000000000000", blockHex: "80000000000000000000000000000000")
	report("encrypt-024", direction: Direction.encrypt, keyHex: "00000000000000000000000000000000", blockHex: "ffffffffffffffffffffffffffffffff")
	report("encrypt-025", direction: Direction.encrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "80000000000000000000000000000000")
	report("encrypt-026", direction: Direction.encrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "ffffffffffffffffffffffffffffffff")
	report("encrypt-027", direction: Direction.encrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "80000000000000000000000000000000")
	report("encrypt-028", direction: Direction.encrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "ffffffffffffffffffffffffffffffff")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
encrypt-001 [69c4e0d86a7b0430d8cdb78070b4c55a]
encrypt-002 [dda97ca4864cdfe06eaf70a0ec0d7191]
encrypt-003 [8ea2b7ca516745bfeafc49904b496089]
encrypt-004 [3925841d02dc09fbdc118597196a0b32]
encrypt-005 [0336763e966d92595a567cc9ce537f5e]
encrypt-006 [08a4e2efec8a8e3312ca7460b9040bbf]
encrypt-007 [275cfc0413d8ccb70513c3859b1d0f72]
encrypt-008 [067cd9d3749207791841562507fa9626]
encrypt-009 [5c9d844ed46f9885085e5d6a4f94c7d7]
encrypt-010 [1bc704f1bce135ceb810341b216d7abe]
encrypt-011 [6d251e6944b051e04eaa6fb4dbf78465]
encrypt-012 [f4a70d8af877f9b02b4c40df57d45b17]
encrypt-013 [0956259c9cd5cfd0181cca53380cde06]
encrypt-014 [a67cf333b314d411d3c0ae6e1cfcd8f5]
encrypt-015 [46f2fb342d6f0ab477476fc501242c5f]
encrypt-016 [179a49c712154bbffbe6e7a84a18e220]
encrypt-017 [0edd33d3c621e546455bd8ba1418bec8]
encrypt-018 [a1f6258c877d5fcd8964484538bfc92c]
encrypt-019 [de885dc87f5a92594082d02cc1e1b42c]
encrypt-020 [dd8a493514231cbf56eccee4c40889fb]
encrypt-021 [e35a6dcb19b201a01ebcfa8aa22b5759]
encrypt-022 [4bf85f1b5d54adbc307b0a048389adcb]
encrypt-023 [3ad78e726c1ec02b7ebfe92b23d9ec34]
encrypt-024 [3f5b8cc9ea855a0afa7347d23e8d664e]
encrypt-025 [6cd02513e8d4dc986b4afe087a60bd0c]
encrypt-026 [b13db4da1f718bc6904797c82bcf2d32]
encrypt-027 [ddc6bf790c15760d8d9aeb6f9a75fd4e]
encrypt-028 [acdace8078a32b1a182bfa4987ca1347]
```

<!-- test: aes.decrypt-block -->
FIPS 197 appendices B and C inverted, and the AESAVS known answers decrypted, for each key size.
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

enum Direction
	encrypt
	decrypt
end 'Direction'

function errorName(failure AesError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidBlockLength gives "invalidBlockLength"
	end 'name'
end 'errorName'

function cipher(direction Direction, keyHex String, blockHex String) returns ByteArray throws AesError
	let aes = try Aes.create(fromHex(keyHex))
	let block = fromHex(blockHex)

	match direction 'operation'
		encrypt then return try aes.encryptBlock(block)
		decrypt then return try aes.decryptBlock(block)
	end 'operation'
end 'cipher'

function report(id String, direction Direction, keyHex String, blockHex String)
	if let output = try cipher(direction, keyHex: keyHex, blockHex: blockHex) 'ciphered'
		print("{id} [{toHex(output)}]\n")
	end 'ciphered' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("decrypt-001", direction: Direction.decrypt, keyHex: "000102030405060708090a0b0c0d0e0f", blockHex: "69c4e0d86a7b0430d8cdb78070b4c55a")
	report("decrypt-002", direction: Direction.decrypt, keyHex: "000102030405060708090a0b0c0d0e0f1011121314151617", blockHex: "dda97ca4864cdfe06eaf70a0ec0d7191")
	report("decrypt-003", direction: Direction.decrypt, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", blockHex: "8ea2b7ca516745bfeafc49904b496089")
	report("decrypt-004", direction: Direction.decrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "3925841d02dc09fbdc118597196a0b32")
	report("decrypt-005", direction: Direction.decrypt, keyHex: "00000000000000000000000000000000", blockHex: "dc43be40be0e53712f7e2bf5ca707209")
	report("decrypt-006", direction: Direction.decrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "4f354592ff7c8847d2d0870ca9481b7c")
	report("decrypt-007", direction: Direction.decrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "623a52fcea5d443e48d9181ab32c7421")
	report("decrypt-008", direction: Direction.decrypt, keyHex: "da84367f325d42d601b4326964802e8e", blockHex: "bba071bcb470f8f6586e5d3add18bc66")
	report("decrypt-009", direction: Direction.decrypt, keyHex: "b0ab0a6a818baef2d11fa33eac947284fb7d748cfb75e570", blockHex: "d2eafd86f63b109b91f5dbb3a3fb7e13")
	report("decrypt-010", direction: Direction.decrypt, keyHex: "797f8b3d176dac5b7e34a2d539c4ef367a16f8635f6264737591c5c07bf57a3e", blockHex: "a74289fe73a4c123ca189ea1e1b49ad5")
	report("decrypt-011", direction: Direction.decrypt, keyHex: "ffffffffffffffff8000000000000000", blockHex: "32cd652842926aea4aa6137bb2be2b5e")
	report("decrypt-012", direction: Direction.decrypt, keyHex: "ffffffffffffffffffffffff800000000000000000000000", blockHex: "e34ec71d6128d4871865d617c30b37e3")
	report("decrypt-013", direction: Direction.decrypt, keyHex: "ffffffffffffffffffffffffffffffff80000000000000000000000000000000", blockHex: "e3714e94a5778955cc0346358e94783a")
	report("decrypt-014", direction: Direction.decrypt, keyHex: "00000000000000000000000000000000", blockHex: "41f992a856fb278b389a62f5d274d7e9")
	report("decrypt-015", direction: Direction.decrypt, keyHex: "000000000000000000000000000000000000000000000000", blockHex: "c8aa80a7850675bc007c46df06b49868")
	report("decrypt-016", direction: Direction.decrypt, keyHex: "0000000000000000000000000000000000000000000000000000000000000000", blockHex: "77f392089042e478ac16c0c86a0b5db5")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
decrypt-001 [00112233445566778899aabbccddeeff]
decrypt-002 [00112233445566778899aabbccddeeff]
decrypt-003 [00112233445566778899aabbccddeeff]
decrypt-004 [3243f6a8885a308d313198a2e0370734]
decrypt-005 [6a118a874519e64e9963798a503f1d35]
decrypt-006 [51719783d3185a535bd75adc65071ce1]
decrypt-007 [761c1fe41a18acf20d241650611d90f1]
decrypt-008 [00000000000000000000000000000000]
decrypt-009 [00000000000000000000000000000000]
decrypt-010 [00000000000000000000000000000000]
decrypt-011 [00000000000000000000000000000000]
decrypt-012 [00000000000000000000000000000000]
decrypt-013 [00000000000000000000000000000000]
decrypt-014 [ffffffffffffffff8000000000000000]
decrypt-015 [ffffffffffffffff8000000000000000]
decrypt-016 [ffffffffffffffff8000000000000000]
```

<!-- test: aes.refuses-invalid-lengths -->
Keys of other lengths, blocks of other lengths, and both at once, which reports the key.
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

enum Direction
	encrypt
	decrypt
end 'Direction'

function errorName(failure AesError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidBlockLength gives "invalidBlockLength"
	end 'name'
end 'errorName'

function cipher(direction Direction, keyHex String, blockHex String) returns ByteArray throws AesError
	let aes = try Aes.create(fromHex(keyHex))
	let block = fromHex(blockHex)

	match direction 'operation'
		encrypt then return try aes.encryptBlock(block)
		decrypt then return try aes.decryptBlock(block)
	end 'operation'
end 'cipher'

function report(id String, direction Direction, keyHex String, blockHex String)
	if let output = try cipher(direction, keyHex: keyHex, blockHex: blockHex) 'ciphered'
		print("{id} [{toHex(output)}]\n")
	end 'ciphered' else (failure) 'refused'
		print("{id} {errorName(failure)}\n")
	end 'refused'
end 'report'

function main() returns ExitCode
	report("aes-err-001", direction: Direction.encrypt, keyHex: "", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-002", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-003", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f10", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-004", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f10111213141516", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-005", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-006", direction: Direction.encrypt, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-007", direction: Direction.encrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "")
	report("aes-err-008", direction: Direction.encrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "000000000000000000000000000000")
	report("aes-err-009", direction: Direction.encrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "0000000000000000000000000000000000")
	report("aes-err-010", direction: Direction.encrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "0000000000000000000000000000000000000000000000000000000000000000")
	report("aes-err-011", direction: Direction.encrypt, keyHex: "0000000000000000000000000000000000000000", blockHex: "000000000000000000000000000000")
	report("aes-err-012", direction: Direction.decrypt, keyHex: "", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-013", direction: Direction.decrypt, keyHex: "000102030405060708090a0b0c0d0e0f10", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-014", direction: Direction.decrypt, keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20", blockHex: "3243f6a8885a308d313198a2e0370734")
	report("aes-err-015", direction: Direction.decrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "")
	report("aes-err-016", direction: Direction.decrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "000000000000000000000000000000")
	report("aes-err-017", direction: Direction.decrypt, keyHex: "2b7e151628aed2a6abf7158809cf4f3c", blockHex: "0000000000000000000000000000000000000000000000000000000000000000")
	report("aes-err-018", direction: Direction.decrypt, keyHex: "0000000000000000000000000000000000000000", blockHex: "000000000000000000000000000000")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aes-err-001 invalidKeyLength
aes-err-002 invalidKeyLength
aes-err-003 invalidKeyLength
aes-err-004 invalidKeyLength
aes-err-005 invalidKeyLength
aes-err-006 invalidKeyLength
aes-err-007 invalidBlockLength
aes-err-008 invalidBlockLength
aes-err-009 invalidBlockLength
aes-err-010 invalidBlockLength
aes-err-011 invalidKeyLength
aes-err-012 invalidKeyLength
aes-err-013 invalidKeyLength
aes-err-014 invalidKeyLength
aes-err-015 invalidBlockLength
aes-err-016 invalidBlockLength
aes-err-017 invalidBlockLength
aes-err-018 invalidKeyLength
```

<!-- test: aes.xor-key-stream -->
Every vector of the Canra `aes-ctr` spec whose counter fits `AesCounter`: the NIST SP 800-38A CTR examples
for each key size in both directions, whose 32-bit counter carries from its last byte into the one before;
prefixes ending inside and at the end of a block; counters carried across two and three bytes, counter 0 and
the last counter `2^32 - 1`; empty data; and keys, nonces and data running past block `2^32 - 1` that are
refused, the key before the nonce and the nonce before the counter. The two vectors with counter `2^32` do
not fit `AesCounter`, so the type refuses them at compile time.
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

function keyErrorName(failure AesError) returns String
	return match failure 'name'
		invalidKeyLength gives "invalidKeyLength"
		invalidBlockLength panic("Aes.create checks no block")
	end 'name'
end 'keyErrorName'

function streamErrorName(failure AesKeyStreamError) returns String
	return match failure 'name'
		invalidNonceLength gives "invalidNonceLength"
		counterOverflow gives "counterOverflow"
	end 'name'
end 'streamErrorName'

function report(id String, keyHex String, nonceHex String, counter AesCounter, dataHex String)
	if let aes = try Aes.create(fromHex(keyHex)) 'keyed'
		if let output = try aes.xorKeyStream(fromHex(nonceHex), counter: counter, data: fromHex(dataHex)) 'streamed'
			print("{id} [{toHex(output)}]\n")
		end 'streamed' else (failure) 'refused'
			print("{id} {streamErrorName(failure)}\n")
		end 'refused'
	end 'keyed' else (failure) 'rejected'
		print("{id} {keyErrorName(failure)}\n")
	end 'rejected'
end 'report'

function main() returns ExitCode
	report("xor-key-stream-001", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
	report("xor-key-stream-002", keyHex: "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
	report("xor-key-stream-003", keyHex: "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
	report("xor-key-stream-004", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee")
	report("xor-key-stream-005", keyHex: "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "1abc932417521ca24f2b0459fe7e6e0b090339ec0aa6faefd5ccc2c6f4ce8e941e36b26bd1ebc670d1bd1d665620abf74f78a7f6d29809585a97daec58c6b050")
	report("xor-key-stream-006", keyHex: "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6")
	report("xor-key-stream-007", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6bc1bee22e409f96e93d7e117393172aae")
	report("xor-key-stream-008", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6bc1bee22e409f96e93d7e117393172a")
	report("xor-key-stream-009", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "6b")
	report("xor-key-stream-010", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4244504319, dataHex: "")
	report("xor-key-stream-011", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 16777215, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
	report("xor-key-stream-012", keyHex: "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 65535, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51")
	report("xor-key-stream-013", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "6bc1bee22e409f96e93d7e117393172a")
	report("xor-key-stream-014", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "6b")
	report("xor-key-stream-015", keyHex: "8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967294, dataHex: "6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51")
	report("xor-key-stream-016", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "6bc1bee22e409f96e93d7e117393172a")
	report("xor-key-stream-017", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "")
	report("xor-key-stream-018", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-001", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "0000000000000000000000000000000000")
	report("aes-ctr-err-002", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967295, dataHex: "0000000000000000000000000000000000000000000000000000000000000000")
	report("aes-ctr-err-003", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 4294967294, dataHex: "000000000000000000000000000000000000000000000000000000000000000000")
	report("aes-ctr-err-006", keyHex: "", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-007", keyHex: "2b7e151628aed2a6abf7158809cf4f", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-008", keyHex: "2b7e151628aed2a6abf7158809cf4f3c00", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-009", keyHex: "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914df", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-010", keyHex: "603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff400", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafb", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-011", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-012", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-013", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fa", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-014", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafbfc", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-015", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff", counter: 0, dataHex: "00000000000000000000000000000000")
	report("aes-ctr-err-016", keyHex: "2b7e151628aed2a6abf7158809cf4f", nonceHex: "f0f1f2f3f4f5f6f7f8f9fa", counter: 4294967295, dataHex: "0000000000000000000000000000000000")
	report("aes-ctr-err-017", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", nonceHex: "f0f1f2f3f4f5f6f7", counter: 4294967295, dataHex: "0000000000000000000000000000000000")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
xor-key-stream-001 [874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee]
xor-key-stream-002 [1abc932417521ca24f2b0459fe7e6e0b090339ec0aa6faefd5ccc2c6f4ce8e941e36b26bd1ebc670d1bd1d665620abf74f78a7f6d29809585a97daec58c6b050]
xor-key-stream-003 [601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6]
xor-key-stream-004 [6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710]
xor-key-stream-005 [6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710]
xor-key-stream-006 [6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710]
xor-key-stream-007 [874d6191b620e3261bef6864990db6ce98]
xor-key-stream-008 [874d6191b620e3261bef6864990db6ce]
xor-key-stream-009 [87]
xor-key-stream-010 []
xor-key-stream-011 [8d4e90eec6a695a5fdf93fdef74efe9df9a02f8960af3aab2deb37c2f6b49031795d790820f900cad5c62a1e9bdb80b9d99899bde561000aaaa4fa1339f4e769]
xor-key-stream-012 [c05611164b5bb6bb6e3321c3317927b39b4906065e04b614e67d9c3334096e61]
xor-key-stream-013 [5720de614e98a465919711117cf295e8]
xor-key-stream-014 [57]
xor-key-stream-015 [bf957dfd0f973ac50d5a20047b9ee11f92df49f01c73de9a6be9bc582273c0fd]
xor-key-stream-016 [22e52fb177d865b2f7c6b512692d114d]
xor-key-stream-017 []
xor-key-stream-018 [3ce1608360d83bf378aa6f000f6182c2]
aes-ctr-err-001 counterOverflow
aes-ctr-err-002 counterOverflow
aes-ctr-err-003 counterOverflow
aes-ctr-err-006 invalidKeyLength
aes-ctr-err-007 invalidKeyLength
aes-ctr-err-008 invalidKeyLength
aes-ctr-err-009 invalidKeyLength
aes-ctr-err-010 invalidKeyLength
aes-ctr-err-011 invalidNonceLength
aes-ctr-err-012 invalidNonceLength
aes-ctr-err-013 invalidNonceLength
aes-ctr-err-014 invalidNonceLength
aes-ctr-err-015 invalidNonceLength
aes-ctr-err-016 invalidKeyLength
aes-ctr-err-017 invalidNonceLength
```
