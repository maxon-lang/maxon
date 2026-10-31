---
feature: aes-hardware
status: stable
keywords: [builtins, __Builtins, aesExpandKey, aesEncryptBlock, aesDecryptBlock, aesCtrXor, aesGcmInit, aesGcmHash, aesGcmEncrypt, aesGcmDecrypt, aesGcmFinish, AES-NI, PCLMULQDQ, PMULL, AESE, AESD, intrinsics, key schedule, hardware aes, fips197, counter mode, ctr, gcm, ghash]
category: system
---

# `__Builtins.aesExpandKey`, `aesEncryptBlock`, `aesDecryptBlock`, `aesCtrXor` and the Galois-mode calls

## Documentation

Nine library-tier intrinsics run the AES block cipher on the CPU's AES instructions (AES-NI on x64, the Armv8
cryptographic extension on arm64). They take buffers and counts and nothing else: the 128-bit block lives only
inside the emitted routine, so the language needs no 128-bit value type.

| Call | Meaning |
|---|---|
| `__Builtins.aesExpandKey(schedule, key)` | writes the key schedule of `key` (16, 24 or 32 bytes) into `schedule` |
| `__Builtins.aesEncryptBlock(output, outputAt, schedule, input, inputAt)` | encrypts the 16 bytes at `inputAt` of `input` into the 16 bytes at `outputAt` of `output` |
| `__Builtins.aesDecryptBlock(output, outputAt, schedule, input, inputAt)` | the inverse cipher of the same shape |
| `__Builtins.aesCtrXor(buffer, bufferAt, schedule, counterBlock, counterOffset, blocks)` | XORs `blocks` (1, 2, 4 or 8) blocks of `buffer` from `bufferAt` with the counter-mode key stream, in place |
| `__Builtins.aesGcmInit(keyBlock, schedule)` | writes the encryption round keys of `schedule` and the table of powers of the hash subkey into `keyBlock` |
| `__Builtins.aesGcmHash(message, keyBlock, data, dataAt, length)` | folds `length` bytes of `data` from `dataAt` into the GHASH accumulator of `message`, a partial last block zero-padded to 16 bytes |
| `__Builtins.aesGcmEncrypt(buffer, bufferAt, keyBlock, message, length)` | encrypts `length` bytes of `buffer` in place in counter mode and folds the cipher text, a partial last block zero-padded to 16 bytes, into the accumulator |
| `__Builtins.aesGcmDecrypt(buffer, bufferAt, keyBlock, message, length)` | folds `length` bytes of cipher text, a partial last block zero-padded to 16 bytes, into the accumulator and decrypts them in place |
| `__Builtins.aesGcmFinish(message, keyBlock, additionalBytes, ciphertextBytes)` | folds in the length block and leaves the tag in the accumulator |

The Galois-mode calls (GCM, NIST SP 800-38D, with the multiply done by `PCLMULQDQ` on x64 and `PMULL` on arm64) keep
their state in two `ByteArray`s the caller owns, because a routine takes four values. A key block is 256 bytes of
product table (the hash subkey `E(K, 0)` and its powers up to the eighth, with their Karatsuba halves) followed by the
`16 * (rounds + 1)` bytes of encryption round keys, so it is `256 + 16 * (rounds + 1)` bytes. A message block is 48
bytes: the accumulator (16 bytes, zero at the start), the counter block (a 12-byte nonce and the 32-bit big-endian
counter of the last block already used) and the tag mask (the cipher of the counter block with counter 1). Encrypting
or decrypting advances the counter by the number of blocks it processed, a partial last block counting as one, wrapping
inside its 32-bit word, and leaves the accumulator at the product after the last cipher text block; the first data block
uses the counter after the one the message block holds. `aesGcmFinish` leaves the 16-byte tag in the first 16 bytes of the
message block. The routines take any length: the partial last block of associated data or cipher text is read as exactly
the bytes it has, zero-padded to 16 bytes inside the routine, and the key stream of a partial last data block is applied
to exactly those bytes, so no byte of the array past the span is read or written. A call that ends in a partial block is
the last call on its message block.

`aesCtrXor` encrypts the counter blocks `counterBlock` with its last four bytes, read as a big-endian 32-bit counter,
increased by `counterOffset`, `counterOffset + 1`, and so on, and XORs each result into the next 16 bytes of the
buffer. `counterBlock` is a `ByteArray` of exactly 16 bytes: a 12-byte nonce, then the counter. Only that 32-bit word
counts: it wraps at 2^32 and never carries into the nonce, so a caller that must refuse a wrap checks it first. A
group is 1, 2, 4 or 8 blocks; a longer message is walked in groups, the widest first.

`schedule` is a `ByteArray` of exactly `32 * (rounds + 1)` bytes, `rounds` being 10, 12 or 14 for a 16-, 24- or
32-byte key: the first half is the encryption round keys, the second half the decryption round keys of the
equivalent inverse cipher. The block calls read the round count from the schedule's length. Every call checks its
buffers once on entry: a key or schedule of another size, or a block that does not lie inside its array, ends the
program with `fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size`. An output
array that shares its buffer with another array is copied before it is written. The instructions take the same time
for every input, so no branch or address depends on the key or the data.

The routines are part of a program only when it calls one. wasm32-wasi has no AES instructions: a call is refused
with E3104. Every `__Builtins` member is library-tier: a call from outside `stdlib/` and `runtime/` is E3185.

## Tests

<!-- test: aes-hardware.fips-197-vectors -->
<!-- tier: stdlib -->
FIPS 197 appendices B and C for each key size: the cipher text, the plain text recovered from it, the last
encryption round key (appendix A.1 for the 128-bit key) and the decryption schedule's first and last round keys,
which are the encryption schedule's last and first.
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

function scheduleFor(key ByteArray) returns ByteArray
	var schedule = ByteArray.create()
	schedule.resize((key.count() + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)

	return schedule
end 'scheduleFor'

function sliceHex(bytes ByteArray, from Count) returns String
	var text = ""

	for position in from upto from + 16 'eachByte'
		text.append("{(try bytes.get(position) otherwise 0):02x}")
	end 'eachByte'

	return text
end 'sliceHex'

function vector(name String, keyHex String, plainHex String)
	let key = fromHex(keyHex)
	let schedule = scheduleFor(key)
	let plain = fromHex(plainHex)
	var sealed = ByteArray.create()
	sealed.resize(16)
	__Builtins.aesEncryptBlock(sealed, 0, schedule, plain, 0)
	var opened = ByteArray.create()
	opened.resize(16)
	__Builtins.aesDecryptBlock(opened, 0, schedule, sealed, 0)

	let half = schedule.count() shr 1
	let lastEncryption = sliceHex(schedule, from: half - 16)
	let firstDecryption = sliceHex(schedule, from: half)
	let lastDecryption = sliceHex(schedule, from: schedule.count() - 16)
	let firstEncryption = sliceHex(schedule, from: 0)

	print("{name} {toHex(sealed)} {toHex(opened) == plainHex}\n")
	print("{name} {firstEncryption == sliceHex(key, from: 0)} {firstDecryption == lastEncryption} {lastDecryption == firstEncryption}\n")
end 'vector'

function main() returns ExitCode
	vector("c1", keyHex: "000102030405060708090a0b0c0d0e0f", plainHex: "00112233445566778899aabbccddeeff")
	vector("c2", keyHex: "000102030405060708090a0b0c0d0e0f1011121314151617", plainHex: "00112233445566778899aabbccddeeff")
	vector("c3", keyHex: "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", plainHex: "00112233445566778899aabbccddeeff")
	vector("b", keyHex: "2b7e151628aed2a6abf7158809cf4f3c", plainHex: "3243f6a8885a308d313198a2e0370734")

	let appendixA = scheduleFor(fromHex("2b7e151628aed2a6abf7158809cf4f3c"))
	print("{sliceHex(appendixA, from: 160)}\n")
	return 0
end 'main'
```
```stdout
c1 69c4e0d86a7b0430d8cdb78070b4c55a true
c1 true true true
c2 dda97ca4864cdfe06eaf70a0ec0d7191 true
c2 true true true
c3 8ea2b7ca516745bfeafc49904b496089 true
c3 true true true
b 3925841d02dc09fbdc118597196a0b32 true
b true true true
d014f9a8c9ee2589e13f0cc8b6630ca6
```
```exitcode
0
```

<!-- test: aes-hardware.matches-the-bitsliced-cipher -->
<!-- tier: stdlib -->
Sixty-four pseudo-random blocks under each key size: the hardware cipher answers what the software `AesBitsliced`
answers, in both directions, and decrypts what it encrypted.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function encrypted(schedule ByteArray, block ByteArray) returns ByteArray
	var sealed = ByteArray.create()
	sealed.resize(16)
	__Builtins.aesEncryptBlock(sealed, 0, schedule, block, 0)

	return sealed
end 'encrypted'

function decrypted(schedule ByteArray, block ByteArray) returns ByteArray
	var opened = ByteArray.create()
	opened.resize(16)
	__Builtins.aesDecryptBlock(opened, 0, schedule, block, 0)

	return opened
end 'decrypted'

function matches(software AesBitsliced, schedule ByteArray, seed Count) returns bool
	let plain = bytesFor(seed + 100, count: 16)
	let sealed = encrypted(schedule, block: plain)
	let softwareSealed = try software.encryptBlock(plain) otherwise panic("the block is 16 bytes")
	let softwareOpened = try software.decryptBlock(plain) otherwise panic("the block is 16 bytes")

	return toHex(sealed) == toHex(softwareSealed) and toHex(decrypted(schedule, block: sealed)) == toHex(plain) and toHex(decrypted(schedule, block: plain)) == toHex(softwareOpened)
end 'matches'

function compare(keyBytes Count) returns Count
	var mismatches = 0 as Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let software = try AesBitsliced.create(key) otherwise panic("the key is valid")
	var schedule = ByteArray.create()
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)

	for seed in 0 upto 64 'eachBlock'
		if not matches(software, schedule: schedule, seed: seed) 'differs'
			mismatches = mismatches + 1
		end 'differs'
	end 'eachBlock'

	return mismatches
end 'compare'

function main() returns ExitCode
	print("{compare(16)} {compare(24)} {compare(32)}\n")
	return 0
end 'main'
```
```stdout
0 0 0
```
```exitcode
0
```

<!-- test: aes-hardware.blocks-at-offsets-leave-the-rest-of-the-array-alone -->
<!-- tier: stdlib -->
The input block is read from inside a longer array and the output block is written inside another: the bytes
around it are not touched, and encrypting in place over the same array gives the same block.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var input = ByteArray.create()
	var output = ByteArray.create()
	var block = ByteArray.create()

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	for position in 0 upto 40 'eachInputByte'
		input.push(((position * 17) and 0xFF) as Byte)
		output.push(0xEE)
	end 'eachInputByte'

	for position in 9 upto 25 'eachBlockByte'
		block.push(try input.get(position) otherwise 0)
	end 'eachBlockByte'

	var aligned = ByteArray.create()
	aligned.resize(16)
	__Builtins.aesEncryptBlock(aligned, 0, schedule, block, 0)
	__Builtins.aesEncryptBlock(output, 5, schedule, input, 9)

	var same = true
	var untouched = true

	for position in 5 upto 21 'eachBlockOutput'
		if (try output.get(position) otherwise 0) != (try aligned.get(position - 5) otherwise 1) 'wrong'
			same = false
		end 'wrong'
	end 'eachBlockOutput'

	for position in 0 upto 5 'eachPrefixByte'
		if (try output.get(position) otherwise 0) != 0xEE 'changed'
			untouched = false
		end 'changed'
	end 'eachPrefixByte'

	for position in 21 upto 40 'eachSuffixByte'
		if (try output.get(position) otherwise 0) != 0xEE 'changed'
			untouched = false
		end 'changed'
	end 'eachSuffixByte'

	var inPlace = ByteArray.create()
	inPlace.resize(16)
	__Builtins.aesEncryptBlock(inPlace, 0, schedule, block, 0)
	__Builtins.aesEncryptBlock(inPlace, 0, schedule, inPlace, 0)
	var twice = ByteArray.create()
	twice.resize(16)
	__Builtins.aesEncryptBlock(twice, 0, schedule, aligned, 0)

	var inPlaceSame = true

	for position in 0 upto 16 'eachInPlaceByte'
		if (try inPlace.get(position) otherwise 0) != (try twice.get(position) otherwise 1) 'wrong'
			inPlaceSame = false
		end 'wrong'
	end 'eachInPlaceByte'

	print("{same} {untouched} {inPlaceSame}\n")
	return 0
end 'main'
```
```stdout
true true true
```
```exitcode
0
```

<!-- test: aes-hardware.a-literal-array-is-copied-before-it-is-written -->
<!-- tier: stdlib -->
An output array that shares its buffer with the program image is detached before the block is stored, so the
call neither faults nor changes the literal it started from.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	let original = b"0123456789abcdef0123456789abcdef"
	var output = b"0123456789abcdef0123456789abcdef"
	let plain = b"00112233445566778899aabbccddeeff"
	__Builtins.aesEncryptBlock(output, 3, schedule, plain, 0)

	var changed = false
	var kept = true

	for position in 0 upto 32 'eachByte'
		let now = try output.get(position) otherwise 0
		let before = try original.get(position) otherwise 1

		if now != before 'differs'
			changed = true

			if position < 3 or position > 18 'outside'
				kept = false
			end 'outside'
		end 'differs'
	end 'eachByte'

	print("{changed} {kept} {try original.get(3) otherwise 0}\n")
	return 0
end 'main'
```
```stdout
true true 51
```
```exitcode
0
```

<!-- test: aes-hardware.a-key-of-another-length-ends-the-program -->
<!-- tier: stdlib -->
Twenty bytes are not a key.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	key.resize(20)
	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	return 0
end 'main'
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-schedule-of-another-length-ends-the-program -->
<!-- tier: stdlib -->
A schedule is exactly `32 * (rounds + 1)` bytes: one byte more is refused, for the expansion and for a block.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var block = ByteArray.create()
	key.resize(16)
	schedule.resize(353)
	block.resize(16)
	__Builtins.aesEncryptBlock(block, 0, schedule, block, 0)

	return 0
end 'main'
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-block-past-the-end-ends-the-program -->
<!-- tier: stdlib -->
The block must lie wholly inside its array: the last start that fits is the length minus sixteen.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var input = ByteArray.create()
	var output = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	input.resize(32)
	output.resize(32)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesDecryptBlock(output, 16, schedule, input, 16)
	print("the last block fits\n")
	__Builtins.aesDecryptBlock(output, 17, schedule, input, 0)

	return 0
end 'main'
```
```stdout
the last block fits
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.error.arity-checked -->
<!-- tier: stdlib -->
`aesExpandKey` takes two arguments and the block calls five.
```maxon
function main() returns ExitCode
	var schedule = ByteArray.create()
	__Builtins.aesExpandKey(schedule)

	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:4:13: '__Builtins.aesExpandKey' takes exactly 2 arguments, but 1 was given
```

<!-- test: aes-hardware.error.operand-types -->
<!-- tier: stdlib -->
A buffer position takes a `ByteArray` and an offset position an integer.
```maxon
function main() returns ExitCode
	var output = ByteArray.create()
	var schedule = ByteArray.create()
	__Builtins.aesEncryptBlock(output, 0, schedule, "text", 0)

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:13: '__Builtins.aesEncryptBlock' requires an argument of type ByteArray, but its argument is String
```

<!-- test: aes-hardware.error.refused-outside-the-library-tiers -->
Outside `stdlib/` and `runtime/` the call is refused at the member it names. It has no public replacement.
```maxon
function main() returns ExitCode
	var schedule = ByteArray.create()
	var key = ByteArray.create()
	__Builtins.aesExpandKey(schedule, key)

	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:5:13: call to '__Builtins.aesExpandKey' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: aes-hardware.error.rejected-on-wasm -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
WASI has no AES instructions, so this lane refuses the call at its own span with `E3104`.
```maxon
function init(keyBlock ByteArray, schedule ByteArray)
	__Builtins.aesGcmInit(keyBlock, schedule)
end 'init'

function hash(message ByteArray, keyBlock ByteArray, data ByteArray)
	__Builtins.aesGcmHash(message, keyBlock, data, 0, 16)
end 'hash'

function encrypt(buffer ByteArray, keyBlock ByteArray, message ByteArray)
	__Builtins.aesGcmEncrypt(buffer, 0, keyBlock, message, 16)
end 'encrypt'

function decrypt(buffer ByteArray, keyBlock ByteArray, message ByteArray)
	__Builtins.aesGcmDecrypt(buffer, 0, keyBlock, message, 16)
end 'decrypt'

function finish(message ByteArray, keyBlock ByteArray)
	__Builtins.aesGcmFinish(message, keyBlock, 0, 0)
end 'finish'

function main() returns ExitCode
	var schedule = ByteArray.create()
	var key = ByteArray.create()
	var keyBlock = ByteArray.create()
	var message = ByteArray.create()
	var buffer = ByteArray.create()
	__Builtins.aesExpandKey(schedule, key)
	init(keyBlock, schedule: schedule)
	hash(message, keyBlock: keyBlock, data: buffer)
	encrypt(buffer, keyBlock: keyBlock, message: message)
	decrypt(buffer, keyBlock: keyBlock, message: message)
	finish(message, keyBlock: keyBlock)

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_init', which has no wasm32-wasi implementation
error E3104: <fragment>:7:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_hash', which has no wasm32-wasi implementation
error E3104: <fragment>:11:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_encrypt', which has no wasm32-wasi implementation
error E3104: <fragment>:15:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_decrypt', which has no wasm32-wasi implementation
error E3104: <fragment>:19:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_finish', which has no wasm32-wasi implementation
error E3104: <fragment>:28:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_expand_key', which has no wasm32-wasi implementation
```

<!-- test: aes-hardware.ctr-groups-match-the-bitsliced-cipher -->
<!-- tier: stdlib -->
Every group size (1, 2, 4 and 8 blocks) at three block offsets inside a buffer that starts mid-array, under three
counters (zero, a middle one and the last one that fits), for each key size: the bytes of the group are the
bitsliced key stream XORed in, and the bytes around it are not touched.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function counterBlock(nonce ByteArray, counter Count) returns ByteArray
	var block = ByteArray.create()
	block.append(nonce)
	block.push(((counter shr 24) and 0xFF) as Byte)
	block.push(((counter shr 16) and 0xFF) as Byte)
	block.push(((counter shr 8) and 0xFF) as Byte)
	block.push((counter and 0xFF) as Byte)

	return block
end 'counterBlock'

function counterFor(mode Count, span Count) returns Count
	if mode == 0 'zero'
		return 0
	end 'zero'

	if mode == 1 'middle'
		return 12345
	end 'middle'

	return 4294967296 - span
end 'counterFor'

function mismatches(keyBytes Count) returns Count
	var wrong = 0 as Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let software = try AesBitsliced.create(key) otherwise panic("the key is valid")
	var schedule = ByteArray.create()
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let nonce = bytesFor(keyBytes + 7, count: 12)

	for groupIndex in 0 upto 4 'eachGroup'
		let group = 1 shl groupIndex

		for first in 0 upto 3 'eachFirst'
			for mode in 0 upto 3 'eachCounter'
				let counter = counterFor(mode, span: first + group)
				let region = bytesFor(keyBytes + group + first * 3 + mode, count: group * 16)
				var buffer = ByteArray.create()
				buffer.append(b"abcde")
				buffer.append(region)
				buffer.append(b"xyz")
				__Builtins.aesCtrXor(buffer, 5, schedule, counterBlock(nonce, counter: counter), first, group)
				let shifted = (counter + first) as AesCounter
				let expected = try software.xorKeyStream(nonce, counter: shifted, data: region) otherwise panic("the counter fits")

				if toHex(buffer) != "6162636465{toHex(expected)}78797a" 'differs'
					wrong = wrong + 1
				end 'differs'
			end 'eachCounter'
		end 'eachFirst'
	end 'eachGroup'

	return wrong
end 'mismatches'

function main() returns ExitCode
	print("{mismatches(16)} {mismatches(24)} {mismatches(32)}\n")
	return 0
end 'main'
```
```stdout
0 0 0
```
```exitcode
0
```

<!-- test: aes-hardware.ctr-counter-wraps-inside-its-word -->
<!-- tier: stdlib -->
A group whose counters pass 2^32 - 1 wraps to zero inside the last four bytes of the counter block and never carries
into the nonce: block `j` is the cipher of the nonce followed by `(counter + offset + j) mod 2^32`. An offset past 2^32 counts
the same as its low 32 bits. Every group size is tried from counters that wrap at each of its positions.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function counterBlock(nonce ByteArray, counter Count) returns ByteArray
	var block = ByteArray.create()
	block.append(nonce)
	block.push(((counter shr 24) and 0xFF) as Byte)
	block.push(((counter shr 16) and 0xFF) as Byte)
	block.push(((counter shr 8) and 0xFF) as Byte)
	block.push((counter and 0xFF) as Byte)

	return block
end 'counterBlock'

function mismatches(keyBytes Count) returns Count
	var wrong = 0 as Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let software = try AesBitsliced.create(key) otherwise panic("the key is valid")
	var schedule = ByteArray.create()
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let nonce = bytesFor(keyBytes + 7, count: 12)
	let offsets = [0, 3, 4294967298]

	for groupIndex in 0 upto 4 'eachGroup'
		let group = 1 shl groupIndex

		for back in 0 upto group + 1 'eachWrapPosition'
			for offset in offsets 'eachOffset'
				let counter = 4294967296 - back
				var buffer = bytesFor(back + group, count: group * 16)
				let region = buffer.clone()
				__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock(nonce, counter: counter), offset, group)

				var expected = ByteArray.create()

				for block in 0 upto group 'eachBlock'
					let word = ((counter + offset + block) mod 4294967296) as AesCounter
					let plain = try region.slice(block * 16, endIndex: block * 16 + 16) otherwise panic("the block lies inside the region")
					let single = try software.xorKeyStream(nonce, counter: word, data: plain) otherwise panic("the counter fits")
					expected.append(single)
				end 'eachBlock'

				if toHex(buffer) != toHex(expected) 'differs'
					wrong = wrong + 1
				end 'differs'
			end 'eachOffset'
		end 'eachWrapPosition'
	end 'eachGroup'

	return wrong
end 'mismatches'

function main() returns ExitCode
	print("{mismatches(16)} {mismatches(24)} {mismatches(32)}\n")
	return 0
end 'main'
```
```stdout
0 0 0
```
```exitcode
0
```

<!-- test: aes-hardware.xor-key-stream-matches-the-bitsliced-cipher -->
<!-- tier: stdlib -->
Every length from 0 to 300 bytes covers each group size and the partial last block. Under 128-, 192- and
256-bit keys, from counter zero and from the last counter the length allows, `Aes.xorKeyStream` and
`Aes.xorKeyStreamPastFirstBlock` (with an existing prefix in the output) answer what the bitsliced cipher answers, and a
counter one past that refuses.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function refusal(failure AesKeyStreamError) returns String
	return match failure 'name'
		invalidNonceLength gives "invalidNonceLength"
		counterOverflow gives "counterOverflow"
	end 'name'
end 'refusal'

function streamed(cipher Aes, nonce ByteArray, counter Count, data ByteArray) returns String
	let first = counter as AesCounter
	let output = try cipher.xorKeyStream(nonce, counter: first, data: data) otherwise (failure) 'refused'
		return refusal(failure)
	end 'refused'

	return toHex(output)
end 'streamed'

function streamedBySoftware(cipher AesBitsliced, nonce ByteArray, counter Count, data ByteArray) returns String
	let first = counter as AesCounter
	let output = try cipher.xorKeyStream(nonce, counter: first, data: data) otherwise (failure) 'refused'
		return refusal(failure)
	end 'refused'

	return toHex(output)
end 'streamedBySoftware'

function pastMask(cipher Aes, nonce ByteArray, counter Count, data ByteArray) returns String
	let first = counter as AesCounter
	var output = ByteArray.create()
	output.push(9)
	output.push(8)
	output.push(7)
	let (low, high) = try cipher.xorKeyStreamPastFirstBlock(nonce, counter: first, data: data, output: output) otherwise (failure) 'refused'
		return refusal(failure)
	end 'refused'

	return "{toHex(output)} {low:016x} {high:016x}"
end 'pastMask'

function pastMaskBySoftware(cipher AesBitsliced, nonce ByteArray, counter Count, data ByteArray) returns String
	let first = counter as AesCounter
	var output = ByteArray.create()
	output.push(9)
	output.push(8)
	output.push(7)
	let (low, high) = try cipher.xorKeyStreamPastFirstBlock(nonce, counter: first, data: data, output: output) otherwise (failure) 'refused'
		return refusal(failure)
	end 'refused'

	return "{toHex(output)} {low:016x} {high:016x}"
end 'pastMaskBySoftware'

function mismatches(keyBytes Count) returns Count
	var wrong = 0 as Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let software = try AesBitsliced.create(key) otherwise panic("the key is valid")
	let cipher = try Aes.create(key) otherwise panic("the key is valid")
	let nonce = bytesFor(keyBytes + 7, count: 12)

	for length in 0 to 300 'eachLength'
		let data = bytesFor(length + keyBytes, count: length)
		let blocks = (length + 15) shr 4
		let last = 4294967296 - blocks if blocks > 0 else 0
		let lastWithMask = 4294967296 - blocks - 1

		if streamed(cipher, nonce: nonce, counter: 0, data: data) != streamedBySoftware(software, nonce: nonce, counter: 0, data: data) 'fromZero'
			wrong = wrong + 1
		end 'fromZero'

		if streamed(cipher, nonce: nonce, counter: last, data: data) != streamedBySoftware(software, nonce: nonce, counter: last, data: data) 'fromLast'
			wrong = wrong + 1
		end 'fromLast'

		if blocks >= 2 and streamed(cipher, nonce: nonce, counter: last + 1, data: data) != streamedBySoftware(software, nonce: nonce, counter: last + 1, data: data) 'pastLast'
			wrong = wrong + 1
		end 'pastLast'

		if pastMask(cipher, nonce: nonce, counter: 1, data: data) != pastMaskBySoftware(software, nonce: nonce, counter: 1, data: data) 'maskFromOne'
			wrong = wrong + 1
		end 'maskFromOne'

		if pastMask(cipher, nonce: nonce, counter: lastWithMask, data: data) != pastMaskBySoftware(software, nonce: nonce, counter: lastWithMask, data: data) 'maskFromLast'
			wrong = wrong + 1
		end 'maskFromLast'
	end 'eachLength'

	return wrong
end 'mismatches'

function main() returns ExitCode
	print("{mismatches(16)} {mismatches(24)} {mismatches(32)}\n")
	let cipher = try Aes.create(bytesFor(1, count: 16)) otherwise panic("the key is valid")
	print("{streamed(cipher, nonce: bytesFor(2, count: 12), counter: 4294967295, data: bytesFor(3, count: 17))}\n")
	return 0
end 'main'
```
```stdout
0 0 0
counterOverflow
```
```exitcode
0
```

<!-- test: aes-hardware.nist-sp-800-38a-ctr-vectors -->
NIST SP 800-38A appendix F.5.1, F.5.3 and F.5.5: the four-block message under each key size. The standard's initial
counter block `f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff` is the 12-byte nonce `f0f1f2f3f4f5f6f7f8f9fafb` and the 32-bit counter
`0xfcfdfeff`; the four blocks do not carry out of that counter.
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

function vector(keyHex String, expectedHex String)
	let cipher = try Aes.create(fromHex(keyHex)) otherwise panic("the key is valid")
	let plain = fromHex("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710")
	let sealed = try cipher.xorKeyStream(fromHex("f0f1f2f3f4f5f6f7f8f9fafb"), counter: 0xfcfdfeff, data: plain) otherwise panic("the nonce is 12 bytes")
	let opened = try cipher.xorKeyStream(fromHex("f0f1f2f3f4f5f6f7f8f9fafb"), counter: 0xfcfdfeff, data: sealed) otherwise panic("the nonce is 12 bytes")

	print("{toHex(sealed) == expectedHex} {toHex(opened) == toHex(plain)}\n")
end 'vector'

function main() returns ExitCode
	vector("2b7e151628aed2a6abf7158809cf4f3c", expectedHex: "874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff5ae4df3edbd5d35e5b4f09020db03eab1e031dda2fbe03d1792170a0f3009cee")
	vector("8e73b0f7da0e6452c810f32b809079e562f8ead2522c6b7b", expectedHex: "1abc932417521ca24f2b0459fe7e6e0b090339ec0aa6faefd5ccc2c6f4ce8e941e36b26bd1ebc670d1bd1d665620abf74f78a7f6d29809585a97daec58c6b050")
	vector("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4", expectedHex: "601ec313775789a5b7a7f504bbf3d228f443e3ca4d62b59aca84e990cacaf5c52b0930daa23de94ce87017ba2d84988ddfc9c58db67aada613c2dd08457941a6")
	return 0
end 'main'
```
```stdout
true true
true true
true true
```
```exitcode
0
```

<!-- test: aes-hardware.a-group-of-three-blocks-ends-the-program -->
<!-- tier: stdlib -->
A group is 1, 2, 4 or 8 blocks.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var buffer = ByteArray.create()
	var counterBlock = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	buffer.resize(64)
	counterBlock.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock, 0, 2)
	print("two blocks fit\n")
	__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock, 0, 3)

	return 0
end 'main'
```
```stdout
two blocks fit
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-group-past-the-end-ends-the-program -->
<!-- tier: stdlib -->
The whole group must lie inside the buffer: four blocks starting at byte 16 of an 80-byte array fit and starting at
byte 17 do not.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var buffer = ByteArray.create()
	var counterBlock = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	buffer.resize(80)
	counterBlock.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesCtrXor(buffer, 16, schedule, counterBlock, 0, 4)
	print("the last group fits\n")
	__Builtins.aesCtrXor(buffer, 17, schedule, counterBlock, 0, 4)

	return 0
end 'main'
```
```stdout
the last group fits
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-counter-block-of-another-length-ends-the-program -->
<!-- tier: stdlib -->
The counter block is exactly 16 bytes.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var buffer = ByteArray.create()
	var counterBlock = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	buffer.resize(16)
	counterBlock.resize(12)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock, 0, 1)

	return 0
end 'main'
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.error.ctr-arity-checked -->
<!-- tier: stdlib -->
`aesCtrXor` takes six arguments.
```maxon
function main() returns ExitCode
	var buffer = ByteArray.create()
	__Builtins.aesCtrXor(buffer)

	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:4:13: '__Builtins.aesCtrXor' takes exactly 6 arguments, but 1 was given
```

<!-- test: aes-hardware.gcm-intrinsics-match-the-mcgrew-viega-vectors -->
<!-- tier: stdlib -->
The McGrew-Viega test cases 1 to 12 (AES-128, AES-192 and AES-256 with the zero and the Feldman key, with and
without associated data and a partial last block) built by hand from the GCM intrinsics: the key block, the message
block, the associated data, the whole blocks of data, a zero-padded last block, and the length block. Each ends
with the seal answering the published ciphertext and tag and the open of that ciphertext answering the plain text and
the same tag.
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

function counterBlock(nonce ByteArray, counter Count) returns ByteArray
	var block = ByteArray.create()
	block.append(nonce)
	block.push(((counter shr 24) and 0xFF) as Byte)
	block.push(((counter shr 16) and 0xFF) as Byte)
	block.push(((counter shr 8) and 0xFF) as Byte)
	block.push((counter and 0xFF) as Byte)

	return block
end 'counterBlock'

function scheduleFor(key ByteArray) returns ByteArray
	var schedule = ByteArray.create()
	schedule.resize((key.count() + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)

	return schedule
end 'scheduleFor'

function paddedFrom(source ByteArray, from Count) returns ByteArray
	var block = ByteArray.create()
	block.append(try source.slice(from, endIndex: source.count()) otherwise panic("the tail starts inside the array"))
	block.growFilled(16, value: 0)

	return block
end 'paddedFrom'

function hashWhole(message ByteArray, keyBlock ByteArray, data ByteArray)
	let whole = (data.count() shr 4) shl 4
	__Builtins.aesGcmHash(message, keyBlock, data, 0, whole)

	if data.count() > whole 'partial'
		__Builtins.aesGcmHash(message, keyBlock, paddedFrom(data, from: whole), 0, 16)
	end 'partial'
end 'hashWhole'

function run(decrypting bool, keyHex String, nonceHex String, inputHex String, additionalHex String) returns (ByteArray, ByteArray)
	let nonce = fromHex(nonceHex)
	let input = fromHex(inputHex)
	let additional = fromHex(additionalHex)
	let schedule = scheduleFor(fromHex(keyHex))
	var keyBlock = ByteArray.create()
	keyBlock.resize(256 + (schedule.count() shr 1))
	__Builtins.aesGcmInit(keyBlock, schedule)

	var message = ByteArray.create()
	message.resize(16)
	message.append(counterBlock(nonce, counter: 1))
	message.growFilled(48, value: 0)
	__Builtins.aesEncryptBlock(message, 32, schedule, message, 16)
	hashWhole(message, keyBlock: keyBlock, data: additional)

	let whole = (input.count() shr 4) shl 4
	let tailBlocks = 2 + (whole shr 4)
	var output = ByteArray.create()
	output.append(input)

	if decrypting 'decrypting'
		__Builtins.aesGcmDecrypt(output, 0, keyBlock, message, whole)

		if input.count() > whole 'partial'
			__Builtins.aesGcmHash(message, keyBlock, paddedFrom(input, from: whole), 0, 16)
			var tail = paddedFrom(output, from: whole)
			__Builtins.aesCtrXor(tail, 0, schedule, counterBlock(nonce, counter: tailBlocks), 0, 1)
			output.truncate(whole)
			output.append(try tail.slice(0, endIndex: input.count() - whole) otherwise panic("the tail is a block"))
		end 'partial'
	end 'decrypting' else 'encrypting'
		__Builtins.aesGcmEncrypt(output, 0, keyBlock, message, whole)

		if input.count() > whole 'partial'
			var tail = paddedFrom(output, from: whole)
			__Builtins.aesCtrXor(tail, 0, schedule, counterBlock(nonce, counter: tailBlocks), 0, 1)
			output.truncate(whole)
			output.append(try tail.slice(0, endIndex: input.count() - whole) otherwise panic("the tail is a block"))
			__Builtins.aesGcmHash(message, keyBlock, paddedFrom(output, from: whole), 0, 16)
		end 'partial'
	end 'encrypting'

	__Builtins.aesGcmFinish(message, keyBlock, additional.count(), input.count())

	let tag = try message.slice(0, endIndex: 16) otherwise panic("the tag is the first block of the message")

	return (output, tag)
end 'run'

function report(id String, keyHex String, nonceHex String, plainHex String, additionalHex String)
	let (ciphertext, tag) = run(false, keyHex: keyHex, nonceHex: nonceHex, inputHex: plainHex, additionalHex: additionalHex)
	let (plain, openedTag) = run(true, keyHex: keyHex, nonceHex: nonceHex, inputHex: toHex(ciphertext), additionalHex: additionalHex)
	print("{id} [{toHex(ciphertext)}{toHex(tag)}] {toHex(plain) == plainHex} {toHex(openedTag) == toHex(tag)}\n")
end 'report'

function main() returns ExitCode
	report("seal-001", keyHex: "00000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "", additionalHex: "")
	report("seal-002", keyHex: "00000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "00000000000000000000000000000000", additionalHex: "")
	report("seal-003", keyHex: "feffe9928665731c6d6a8f9467308308", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255", additionalHex: "")
	report("seal-004", keyHex: "feffe9928665731c6d6a8f9467308308", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39", additionalHex: "feedfacedeadbeeffeedfacedeadbeefabaddad2")
	report("seal-005", keyHex: "000000000000000000000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "", additionalHex: "")
	report("seal-006", keyHex: "000000000000000000000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "00000000000000000000000000000000", additionalHex: "")
	report("seal-007", keyHex: "feffe9928665731c6d6a8f9467308308feffe9928665731c", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255", additionalHex: "")
	report("seal-008", keyHex: "feffe9928665731c6d6a8f9467308308feffe9928665731c", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39", additionalHex: "feedfacedeadbeeffeedfacedeadbeefabaddad2")
	report("seal-009", keyHex: "0000000000000000000000000000000000000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "", additionalHex: "")
	report("seal-010", keyHex: "0000000000000000000000000000000000000000000000000000000000000000", nonceHex: "000000000000000000000000", plainHex: "00000000000000000000000000000000", additionalHex: "")
	report("seal-011", keyHex: "feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255", additionalHex: "")
	report("seal-012", keyHex: "feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308", nonceHex: "cafebabefacedbaddecaf888", plainHex: "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39", additionalHex: "feedfacedeadbeeffeedfacedeadbeefabaddad2")

	return 0
end 'main'
```
```stdout
seal-001 [58e2fccefa7e3061367f1d57a4e7455a] true true
seal-002 [0388dace60b6a392f328c2b971b2fe78ab6e47d42cec13bdf53a67b21257bddf] true true
seal-003 [42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091473f59854d5c2af327cd64a62cf35abd2ba6fab4] true true
seal-004 [42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e0915bc94fbc3221a5db94fae95ae7121a47] true true
seal-005 [cd33b28ac773f74ba00ed1f312572435] true true
seal-006 [98e7247c07f0fe411c267e4384b0f6002ff58d80033927ab8ef4d4587514f0fb] true true
seal-007 [3980ca0b3c00e841eb06fac4872a2757859e1ceaa6efd984628593b40ca1e19c7d773d00c144c525ac619d18c84a3f4718e2448b2fe324d9ccda2710acade2569924a7c8587336bfb118024db8674a14] true true
seal-008 [3980ca0b3c00e841eb06fac4872a2757859e1ceaa6efd984628593b40ca1e19c7d773d00c144c525ac619d18c84a3f4718e2448b2fe324d9ccda27102519498e80f1478f37ba55bd6d27618c] true true
seal-009 [530f8afbc74536b9a963b4f1c4cb738b] true true
seal-010 [cea7403d4d606b6e074ec5d3baf39d18d0d1c8a799996bf0265b98b5d48ab919] true true
seal-011 [522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662898015adb094dac5d93471bdec1a502270e3cc6c] true true
seal-012 [522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f66276fc6ece0f4e1768cddf8853bb2d551b] true true
```
```exitcode
0
```

<!-- test: aes-hardware.gcm-seal-and-open-match-the-software-backing -->
<!-- tier: stdlib -->
`AesGcm` chooses the CPU's instructions once, in `create`. For each key size, associated data of 0, 1, 13, 16, 17, 31, 32,
33, 127, 128 and 129 bytes and every data length from 0 to 300 bytes (the 8-block loop, the single blocks and the partial last
block), the sealed bytes equal the bitsliced backing's, each backing opens the other's output, and the first
backing refuses a forged tag, ciphertext and associated data every time.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function flipped(sealed ByteArray, at Count) returns ByteArray
	var forged = try sealed.slice(0, endIndex: sealed.count()) otherwise panic("a copy of the whole array")
	let original = try forged.get(at) otherwise panic("the forged byte lies inside the array")
	try forged.set(at, value: original xor 1) otherwise panic("the forged byte lies inside the array")

	return forged
end 'flipped'

function refuses(aead AesGcm, nonce ByteArray, sealed ByteArray, additional ByteArray) returns bool
	_ = try aead.open(nonce, sealed: sealed, additionalData: additional) otherwise return true

	return false
end 'refuses'

function mismatches(keyBytes Count) returns (Count, Count, Count)
	let key = bytesFor(keyBytes, count: keyBytes)
	let hardware = try AesGcm.create(key) otherwise panic("the key is valid")
	let software = try AesGcm.build(key, bitsliced: true) otherwise panic("the key is valid")
	let nonce = bytesFor(keyBytes + 7, count: 12)
	let additionalLengths = [0, 1, 13, 16, 17, 31, 32, 33, 127, 128, 129]
	var wrong = 0 as Count
	var forgeries = 0 as Count
	var refused = 0 as Count

	for additionalBytes in additionalLengths 'eachAdditional'
		let additional = bytesFor(additionalBytes + 3, count: additionalBytes)

		for dataBytes in 0 to 300 'eachData'
			let data = bytesFor(dataBytes + 11, count: dataBytes)
			let sealedByHardware = try hardware.seal(nonce, plaintext: data, additionalData: additional) otherwise panic("sealed")
			let sealedBySoftware = try software.seal(nonce, plaintext: data, additionalData: additional) otherwise panic("sealed")

			if not sealedByHardware.equals(sealedBySoftware) 'differs'
				wrong = wrong + 1
			end 'differs'

			let openedByHardware = try hardware.open(nonce, sealed: sealedBySoftware, additionalData: additional) otherwise panic("opened")
			let openedBySoftware = try software.open(nonce, sealed: sealedByHardware, additionalData: additional) otherwise panic("opened")

			if not openedByHardware.equals(data) or not openedBySoftware.equals(data) 'lost'
				wrong = wrong + 1
			end 'lost'

			forgeries = forgeries + 1

			if refuses(hardware, nonce: nonce, sealed: flipped(sealedByHardware, at: sealedByHardware.count() - 1), additional: additional) 'tag'
				refused = refused + 1
			end 'tag'

			if dataBytes > 0 'hasData'
				forgeries = forgeries + 1

				if refuses(hardware, nonce: nonce, sealed: flipped(sealedByHardware, at: dataBytes - 1), additional: additional) 'ciphertext'
					refused = refused + 1
				end 'ciphertext'
			end 'hasData'

			if additionalBytes > 0 'hasAdditional'
				forgeries = forgeries + 1

				if refuses(hardware, nonce: nonce, sealed: sealedByHardware, additional: flipped(additional, at: additionalBytes - 1)) 'additional'
					refused = refused + 1
				end 'additional'
			end 'hasAdditional'
		end 'eachData'
	end 'eachAdditional'

	return (wrong, forgeries, refused)
end 'mismatches'

function hardwareExpected() returns bool
	#if arch(x64) or arch(arm64)
		return __Builtins.cpuHasAes()
	#else
		return false
	#endif
end 'hardwareExpected'

function main() returns ExitCode
	let probe = try AesGcm.create(bytesFor(1, count: 16)) otherwise panic("the key is valid")
	print("{probe.usesHardware() == hardwareExpected()}\n")

	for keyBytes in [16, 24, 32] 'eachKey'
		let (wrong, forgeries, refused) = mismatches(keyBytes)
		print("{keyBytes} {wrong} {forgeries == refused}\n")
	end 'eachKey'

	return 0
end 'main'
```
```stdout
true
16 0 true
24 0 true
32 0 true
```
```exitcode
0
```

<!-- test: aes-hardware.gcm-calls-continue-where-the-last-stopped -->
<!-- tier: stdlib -->
`aesGcmEncrypt` and `aesGcmDecrypt` take the accumulator and the counter from the message block and leave them advanced there:
twenty blocks in one call, or split into two calls at any block, produce the same bytes, the same accumulator and the counter
`1 + 20`. Decrypting the ciphertext gives the plain text and the accumulator the encryption ended with, and `aesGcmHash` of
the ciphertext in the same split calls does too.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function freshMessage() returns ByteArray
	var message = ByteArray.create()
	message.append(bytesFor(5, count: 16))
	message.append(bytesFor(6, count: 12))
	message.push(0)
	message.push(0)
	message.push(0)
	message.push(1)
	message.append(bytesFor(7, count: 16))

	return message
end 'freshMessage'

function main() returns ExitCode
	let key = bytesFor(1, count: 32)
	var schedule = ByteArray.create()
	schedule.resize(480)
	__Builtins.aesExpandKey(schedule, key)
	var keyBlock = ByteArray.create()
	keyBlock.resize(256 + 240)
	__Builtins.aesGcmInit(keyBlock, schedule)

	let plain = bytesFor(2, count: 320)
	var whole = freshMessage()
	var reference = ByteArray.create()
	reference.append(plain)
	__Builtins.aesGcmEncrypt(reference, 0, keyBlock, whole, 320)

	var hashed = freshMessage()
	__Builtins.aesGcmHash(hashed, keyBlock, reference, 0, 320)

	var mismatches = 0 as Count

	if toHex(try whole.slice(0, endIndex: 16) otherwise panic("inside")) != toHex(try hashed.slice(0, endIndex: 16) otherwise panic("inside")) 'hashDiffers'
		mismatches = mismatches + 1
	end 'hashDiffers'

	for split in 0 to 20 'eachSplit'
		var message = freshMessage()
		var buffer = ByteArray.create()
		buffer.append(bytesFor(3, count: 5))
		buffer.append(plain)
		__Builtins.aesGcmEncrypt(buffer, 5, keyBlock, message, split * 16)
		__Builtins.aesGcmEncrypt(buffer, 5 + split * 16, keyBlock, message, (20 - split) * 16)

		if toHex(try buffer.slice(5, endIndex: 325) otherwise panic("inside")) != toHex(reference) or toHex(message) != toHex(whole) 'encryptDiffers'
			mismatches = mismatches + 1
		end 'encryptDiffers'

		var opening = freshMessage()
		var opened = ByteArray.create()
		opened.append(reference)
		__Builtins.aesGcmDecrypt(opened, 0, keyBlock, opening, split * 16)
		__Builtins.aesGcmDecrypt(opened, split * 16, keyBlock, opening, (20 - split) * 16)

		if toHex(opened) != toHex(plain) or toHex(opening) != toHex(whole) 'decryptDiffers'
			mismatches = mismatches + 1
		end 'decryptDiffers'

		var splitHash = freshMessage()
		__Builtins.aesGcmHash(splitHash, keyBlock, reference, 0, split * 16)
		__Builtins.aesGcmHash(splitHash, keyBlock, reference, split * 16, (20 - split) * 16)

		if toHex(try splitHash.slice(0, endIndex: 16) otherwise panic("inside")) != toHex(try hashed.slice(0, endIndex: 16) otherwise panic("inside")) 'hashSplitDiffers'
			mismatches = mismatches + 1
		end 'hashSplitDiffers'
	end 'eachSplit'

	let counter = try whole.slice(16, endIndex: 32) otherwise panic("inside")
	let nonceHex = toHex(try freshMessage().slice(16, endIndex: 28) otherwise panic("inside"))
	print("{mismatches} {toHex(counter) == "{nonceHex}00000015"}\n")

	return 0
end 'main'
```
```stdout
0 true
```
```exitcode
0
```

<!-- test: aes-hardware.gcm-intrinsics-take-partial-blocks -->
<!-- tier: stdlib -->
`aesGcmHash`, `aesGcmEncrypt` and `aesGcmDecrypt` take any length, with the array they read and write framed by bytes that must
stay as they were. For each key size, every data length from 0 to 300 under every associated data length from 0 to 40: the
message block built by hand from the intrinsics ends in the cipher text and tag the bitsliced backing seals, the counter has
advanced by the number of blocks the data occupies, a partial last block counting as one, and decrypting that cipher text gives
the plain text and the same tag.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function counterBlock(nonce ByteArray, counter Count) returns ByteArray
	var block = ByteArray.create()
	block.append(nonce)
	block.push(((counter shr 24) and 0xFF) as Byte)
	block.push(((counter shr 16) and 0xFF) as Byte)
	block.push(((counter shr 8) and 0xFF) as Byte)
	block.push((counter and 0xFF) as Byte)

	return block
end 'counterBlock'

function framed(body ByteArray) returns ByteArray
	var array = ByteArray.create()

	for _ in 0 upto 3 'eachLead'
		array.push(0x5A)
	end 'eachLead'

	array.append(body)

	for _ in 0 upto 17 'eachTrail'
		array.push(0xA5)
	end 'eachTrail'

	return array
end 'framed'

function frameIntact(array ByteArray, bodyBytes Count) returns bool
	for position in 0 upto 3 'eachLead'
		if (try array.get(position) otherwise 0) != 0x5A 'leadChanged'
			return false
		end 'leadChanged'
	end 'eachLead'

	for position in 3 + bodyBytes upto 3 + bodyBytes + 17 'eachTrail'
		if (try array.get(position) otherwise 0) != 0xA5 'trailChanged'
			return false
		end 'trailChanged'
	end 'eachTrail'

	return true
end 'frameIntact'

function bodyOf(array ByteArray, bodyBytes Count) returns ByteArray
	return try array.slice(3, endIndex: 3 + bodyBytes) otherwise panic("the body lies inside the frame")
end 'bodyOf'

function stateFor(schedule ByteArray, nonce ByteArray, additional ByteArray, keyBlock ByteArray) returns ByteArray
	var message = ByteArray.create()
	message.resize(16)
	message.append(counterBlock(nonce, counter: 1))
	message.growFilled(48, value: 0)
	__Builtins.aesEncryptBlock(message, 32, schedule, message, 16)
	__Builtins.aesGcmHash(message, keyBlock, framed(additional), 3, additional.count())

	return message
end 'stateFor'

function tagOf(message ByteArray) returns ByteArray
	return try message.slice(0, endIndex: 16) otherwise panic("the tag is the first block of the message")
end 'tagOf'

function counterAdvancedBy(message ByteArray, nonce ByteArray, blocks Count) returns bool
	let counter = try message.slice(16, endIndex: 32) otherwise panic("the counter block is the second block of the message")

	return counter.equals(counterBlock(nonce, counter: 1 + blocks))
end 'counterAdvancedBy'

function mismatches(schedule ByteArray, keyBlock ByteArray, nonce ByteArray, plain ByteArray, additional ByteArray, expected ByteArray) returns Count
	var wrong = 0 as Count
	let bytes = plain.count()
	let blocks = (bytes + 15) shr 4
	var sealing = stateFor(schedule, nonce: nonce, additional: additional, keyBlock: keyBlock)
	var written = framed(plain)
	__Builtins.aesGcmEncrypt(written, 3, keyBlock, sealing, bytes)
	__Builtins.aesGcmFinish(sealing, keyBlock, additional.count(), bytes)
	var produced = ByteArray.create()
	produced.append(bodyOf(written, bodyBytes: bytes))
	produced.append(tagOf(sealing))

	if not frameIntact(written, bodyBytes: bytes) or not produced.equals(expected) or not counterAdvancedBy(sealing, nonce: nonce, blocks: blocks) 'sealDiffers'
		wrong = wrong + 1
	end 'sealDiffers'

	let ciphertext = bodyOf(written, bodyBytes: bytes)
	var opening = stateFor(schedule, nonce: nonce, additional: additional, keyBlock: keyBlock)
	var read = framed(ciphertext)
	__Builtins.aesGcmDecrypt(read, 3, keyBlock, opening, bytes)
	__Builtins.aesGcmFinish(opening, keyBlock, additional.count(), bytes)

	if not frameIntact(read, bodyBytes: bytes) or not bodyOf(read, bodyBytes: bytes).equals(plain) or not tagOf(opening).equals(tagOf(sealing)) or not counterAdvancedBy(opening, nonce: nonce, blocks: blocks) 'openDiffers'
		wrong = wrong + 1
	end 'openDiffers'

	return wrong
end 'mismatches'

function compare(keyBytes Count) returns Count
	let key = bytesFor(keyBytes, count: keyBytes)
	var schedule = ByteArray.create()
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	var keyBlock = ByteArray.create()
	keyBlock.resize(256 + (schedule.count() shr 1))
	__Builtins.aesGcmInit(keyBlock, schedule)
	let software = try AesGcm.build(key, bitsliced: true) otherwise panic("the key is valid")
	let nonce = bytesFor(keyBytes + 7, count: 12)
	var wrong = 0 as Count

	for additionalBytes in 0 to 40 'eachAdditional'
		let additional = bytesFor(additionalBytes + 3, count: additionalBytes)

		for dataBytes in 0 to 300 'eachData'
			let plain = bytesFor(dataBytes + 11, count: dataBytes)
			let expected = try software.seal(nonce, plaintext: plain, additionalData: additional) otherwise panic("sealed")
			wrong = wrong + mismatches(schedule, keyBlock: keyBlock, nonce: nonce, plain: plain, additional: additional, expected: expected)
		end 'eachData'
	end 'eachAdditional'

	return wrong
end 'compare'

function main() returns ExitCode
	print("{compare(16)} {compare(24)} {compare(32)}\n")
	return 0
end 'main'
```
```stdout
0 0 0
```
```exitcode
0
```

<!-- test: aes-hardware.gcm-counter-wraps-inside-its-word -->
<!-- tier: stdlib -->
A message block whose counter is `0xFFFFFFF8` encrypts twenty blocks with the counters `0xFFFFFFF9` to `0xFFFFFFFF` and then
`0` to `0xC`: the key stream is `aesEncryptBlock` of each counter block and the nonce is never carried into. `aesGcmDecrypt`
and a call split inside the first group of eight give the same bytes and leave the message block at the counter `0xC`.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function counterBlock(nonce ByteArray, counter Count) returns ByteArray
	var block = ByteArray.create()
	block.append(nonce)
	block.push(((counter shr 24) and 0xFF) as Byte)
	block.push(((counter shr 16) and 0xFF) as Byte)
	block.push(((counter shr 8) and 0xFF) as Byte)
	block.push((counter and 0xFF) as Byte)

	return block
end 'counterBlock'

function messageAt(nonce ByteArray, counter Count) returns ByteArray
	var message = ByteArray.create()
	message.append(bytesFor(5, count: 16))
	message.append(counterBlock(nonce, counter: counter))
	message.append(bytesFor(7, count: 16))

	return message
end 'messageAt'

function main() returns ExitCode
	let key = bytesFor(1, count: 16)
	var schedule = ByteArray.create()
	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)
	var keyBlock = ByteArray.create()
	keyBlock.resize(256 + 176)
	__Builtins.aesGcmInit(keyBlock, schedule)

	let nonce = bytesFor(6, count: 12)
	let start = 4294967288
	let blocks = 20
	var zeros = ByteArray.create()
	zeros.resize(blocks * 16)
	var stream = ByteArray.create()
	stream.resize(blocks * 16)

	for block in 0 upto blocks 'eachBlock'
		__Builtins.aesEncryptBlock(stream, block * 16, schedule, counterBlock(nonce, counter: (start + 1 + block) mod 4294967296), 0)
	end 'eachBlock'

	var whole = messageAt(nonce, counter: start)
	var encrypted = ByteArray.create()
	encrypted.append(zeros)
	__Builtins.aesGcmEncrypt(encrypted, 0, keyBlock, whole, blocks * 16)

	var split = messageAt(nonce, counter: start)
	var pieces = ByteArray.create()
	pieces.append(zeros)
	__Builtins.aesGcmEncrypt(pieces, 0, keyBlock, split, 3 * 16)
	__Builtins.aesGcmEncrypt(pieces, 3 * 16, keyBlock, split, (blocks - 3) * 16)

	var opening = messageAt(nonce, counter: start)
	var opened = ByteArray.create()
	opened.append(stream)
	__Builtins.aesGcmDecrypt(opened, 0, keyBlock, opening, blocks * 16)

	let finalCounter = "{toHex(nonce)}0000000c"
	let wrapped = toHex(encrypted) == toHex(stream) and toHex(pieces) == toHex(stream) and toHex(opened) == toHex(zeros)
	let counters = toHex(try whole.slice(16, endIndex: 32) otherwise panic("inside")) == finalCounter and toHex(try split.slice(16, endIndex: 32) otherwise panic("inside")) == finalCounter and toHex(try opening.slice(16, endIndex: 32) otherwise panic("inside")) == finalCounter
	print("{wrapped} {counters}\n")

	return 0
end 'main'
```
```stdout
true true
```
```exitcode
0
```

<!-- test: aes-hardware.gcm-literal-arrays-are-copied-before-they-are-written -->
<!-- tier: stdlib -->
A key block, a message block or a buffer that shares its storage with the program image is detached before it is written,
so the calls neither fault nor change the literal they started from.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	let originalKeys = b"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
	var keyBlock = b"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
	__Builtins.aesGcmInit(keyBlock, schedule)

	var keysChanged = false

	for position in 0 upto 432 'eachKeyBlockByte'
		if (try keyBlock.get(position) otherwise 0) != (try originalKeys.get(position) otherwise 1) 'differs'
			keysChanged = true
		end 'differs'
	end 'eachKeyBlockByte'

	let originalMessage = b"0123456789abcdef0123456789abcdef0123456789abcdef"
	var message = b"0123456789abcdef0123456789abcdef0123456789abcdef"
	var buffer = b"0123456789abcdef0123456789abcdef"
	let originalBuffer = b"0123456789abcdef0123456789abcdef"
	__Builtins.aesGcmEncrypt(buffer, 0, keyBlock, message, 32)
	__Builtins.aesGcmFinish(message, keyBlock, 5, 32)

	print("{keysChanged} {message.equals(originalMessage)} {buffer.equals(originalBuffer)} {try originalMessage.get(0) otherwise 0} {try originalBuffer.get(31) otherwise 0}\n")
	return 0
end 'main'
```
```stdout
true false false 48 102
```
```exitcode
0
```

<!-- test: aes-hardware.a-gcm-key-block-of-another-size-ends-the-program -->
<!-- tier: stdlib -->
A key block is 256 bytes of product table followed by the round keys of the schedule, which for a schedule of
`32 * (rounds + 1)` bytes is `256 + 16 * (rounds + 1)` bytes: one more is refused.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	print("the right size fits\n")
	keyBlock.resize(433)
	__Builtins.aesGcmInit(keyBlock, schedule)

	return 0
end 'main'
```
```stdout
the right size fits
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-message-block-of-another-size-ends-the-program -->
<!-- tier: stdlib -->
A message block is exactly 48 bytes: the accumulator, the counter block and the tag mask.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	var message = ByteArray.create()
	key.resize(32)
	schedule.resize(480)
	keyBlock.resize(496)
	message.resize(48)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmFinish(message, keyBlock, 0, 0)
	print("48 bytes fit\n")
	message.resize(47)
	__Builtins.aesGcmFinish(message, keyBlock, 0, 0)

	return 0
end 'main'
```
```stdout
48 bytes fit
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-hash-span-past-the-end-ends-the-program -->
<!-- tier: stdlib -->
`aesGcmHash` takes any length, but the whole span must lie inside the array: 17 bytes from byte 31 of a 48-byte array fit,
18 bytes from there do not.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	var message = ByteArray.create()
	var data = ByteArray.create()
	key.resize(24)
	schedule.resize(416)
	keyBlock.resize(464)
	message.resize(48)
	data.resize(48)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmHash(message, keyBlock, data, 31, 17)
	print("a block and a byte fit\n")
	__Builtins.aesGcmHash(message, keyBlock, data, 31, 18)

	return 0
end 'main'
```
```stdout
a block and a byte fit
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-span-past-the-end-ends-the-program -->
<!-- tier: stdlib -->
The whole span must lie inside the buffer: 32 bytes from byte 16 of a 48-byte array fit, from byte 17 they do not.
```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	var message = ByteArray.create()
	var buffer = ByteArray.create()
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	message.resize(48)
	buffer.resize(48)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmDecrypt(buffer, 16, keyBlock, message, 32)
	print("the last span fits\n")
	__Builtins.aesGcmDecrypt(buffer, 17, keyBlock, message, 32)

	return 0
end 'main'
```
```stdout
the last span fits
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-schedule-of-another-size-ends-the-program -->
<!-- tier: stdlib -->
The schedule `aesGcmInit` copies the round keys from is exactly `32 * (rounds + 1)` bytes.
```maxon
function main() returns ExitCode
	var schedule = ByteArray.create()
	var keyBlock = ByteArray.create()
	schedule.resize(353)
	keyBlock.resize(432)
	__Builtins.aesGcmInit(keyBlock, schedule)

	return 0
end 'main'
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.error.gcm-arity-checked -->
<!-- tier: stdlib -->
`aesGcmInit` takes two arguments, `aesGcmFinish` four, and `aesGcmHash`, `aesGcmEncrypt` and `aesGcmDecrypt` five.
```maxon
function init(buffer ByteArray)
	__Builtins.aesGcmInit(buffer)
end 'init'

function hash(buffer ByteArray)
	__Builtins.aesGcmHash(buffer)
end 'hash'

function encrypt(buffer ByteArray)
	__Builtins.aesGcmEncrypt(buffer)
end 'encrypt'

function decrypt(buffer ByteArray)
	__Builtins.aesGcmDecrypt(buffer)
end 'decrypt'

function finish(buffer ByteArray)
	__Builtins.aesGcmFinish(buffer)
end 'finish'

function main() returns ExitCode
	var buffer = ByteArray.create()
	init(buffer)
	hash(buffer)
	encrypt(buffer)
	decrypt(buffer)
	finish(buffer)

	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:13: '__Builtins.aesGcmInit' takes exactly 2 arguments, but 1 was given
error E3036: <fragment>:7:13: '__Builtins.aesGcmHash' takes exactly 5 arguments, but 1 was given
error E3036: <fragment>:11:13: '__Builtins.aesGcmEncrypt' takes exactly 5 arguments, but 1 was given
error E3036: <fragment>:15:13: '__Builtins.aesGcmDecrypt' takes exactly 5 arguments, but 1 was given
error E3036: <fragment>:19:13: '__Builtins.aesGcmFinish' takes exactly 4 arguments, but 1 was given
```

<!-- test: aes-hardware.error.gcm-operand-types -->
<!-- tier: stdlib -->
A buffer position takes a `ByteArray` and an offset or length position an integer.
```maxon
function finish(message ByteArray, keyBlock ByteArray)
	__Builtins.aesGcmFinish(message, keyBlock, "text", 0)
end 'finish'

function hash(message ByteArray, keyBlock ByteArray)
	__Builtins.aesGcmHash(message, keyBlock, "text", 0, 0)
end 'hash'

function main() returns ExitCode
	var message = ByteArray.create()
	var keyBlock = ByteArray.create()
	finish(message, keyBlock: keyBlock)
	hash(message, keyBlock: keyBlock)

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: '__Builtins.aesGcmFinish' requires an argument of type int, but its argument is String
error E3005: <fragment>:7:13: '__Builtins.aesGcmHash' requires an argument of type ByteArray, but its argument is String
```

<!-- test: aes-hardware.gcm-large-messages-match-the-software-backing -->
<!-- tier: stdlib -->
Messages of 1000, 4096, 16384, 16385 and 65553 bytes under 3000 bytes of associated data, for each key size, cross the
8-block loop many times: the hardware backing seals what the bitsliced backing seals and opens it.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	var bytes = ByteArray.create()

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function mismatches(keyBytes Count) returns Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let hardware = try AesGcm.create(key) otherwise panic("the key is valid")
	let software = try AesGcm.build(key, bitsliced: true) otherwise panic("the key is valid")
	let nonce = bytesFor(keyBytes + 7, count: 12)
	let additional = bytesFor(5, count: 3000)
	let lengths = [1000, 4096, 16384, 16385, 65553]
	var wrong = 0 as Count

	for dataBytes in lengths 'eachLength'
		let data = bytesFor(dataBytes, count: dataBytes)
		let sealedByHardware = try hardware.seal(nonce, plaintext: data, additionalData: additional) otherwise panic("sealed")
		let sealedBySoftware = try software.seal(nonce, plaintext: data, additionalData: additional) otherwise panic("sealed")
		let openedByHardware = try hardware.open(nonce, sealed: sealedBySoftware, additionalData: additional) otherwise panic("opened")

		if not sealedByHardware.equals(sealedBySoftware) or not openedByHardware.equals(data) 'differs'
			wrong = wrong + 1
		end 'differs'
	end 'eachLength'

	return wrong
end 'mismatches'

function main() returns ExitCode
	print("{mismatches(16)} {mismatches(24)} {mismatches(32)}\n")
	return 0
end 'main'
```
```stdout
0 0 0
```
```exitcode
0
```
