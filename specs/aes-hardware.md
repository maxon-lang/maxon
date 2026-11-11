---
feature: aes-hardware
status: stable
keywords: [builtins, __Builtins, aesExpandKey, aesEncryptBlock, aesDecryptBlock, aesCtrXor, aesGcmInit, aesGcmSeal, aesGcmOpen, AES-NI, PCLMULQDQ, PMULL, AESE, AESD, intrinsics, key schedule, hardware aes, fips197, counter mode, ctr, gcm, ghash]
category: system
---

# `__Builtins.aesExpandKey`, `aesEncryptBlock`, `aesDecryptBlock`, `aesCtrXor` and the Galois-mode calls

## Documentation

Eight library-tier intrinsics run the AES block cipher on the CPU's AES instructions (AES-NI on x64, the Armv8
cryptographic extension on arm64). They take buffers and counts and nothing else: the 128-bit block lives only
inside the emitted routine, so the language needs no 128-bit value type.

| Call | Meaning |
|---|---|
| `__Builtins.aesExpandKey(schedule, key)` | writes the key schedule of `key` (16, 24 or 32 bytes) into `schedule` |
| `__Builtins.aesEncryptBlock(output, outputAt, schedule, input, inputAt)` | encrypts the 16 bytes at `inputAt` of `input` into the 16 bytes at `outputAt` of `output` |
| `__Builtins.aesDecryptBlock(output, outputAt, schedule, input, inputAt)` | the inverse cipher of the same shape |
| `__Builtins.aesCtrXor(buffer, bufferAt, schedule, counterBlock, counterOffset, blocks)` | XORs `blocks` (1, 2, 4 or 8) blocks of `buffer` from `bufferAt` with the counter-mode key stream, in place |
| `__Builtins.aesGcmInit(keyBlock, schedule)` | writes the encryption round keys of `schedule` and the table of powers of the hash subkey into `keyBlock` |
| `__Builtins.aesGcmSeal(output, outputAt, keyBlock, nonce, additionalData, plaintext)` | writes the cipher text of `plaintext` and then the 16-byte tag over it and `additionalData` into `output` from `outputAt` |
| `__Builtins.aesGcmOpen(output, outputAt, keyBlock, nonce, additionalData, sealed)` | writes the plain text of `sealed`, a cipher text followed by its tag, into `output` from `outputAt` and answers whether the tag is the one `additionalData` and the cipher text produce |

The Galois-mode calls (GCM, NIST SP 800-38D, with the multiply done by `PCLMULQDQ` on x64 and `PMULL` on arm64) each do
a whole message in one call. A key block is 256 bytes of product table (the hash subkey `E(K, 0)` and its powers up to
the eighth, with their Karatsuba halves) followed by the `16 * (rounds + 1)` bytes of encryption round keys, so it is
`256 + 16 * (rounds + 1)` bytes. `nonce` is exactly 12 bytes. The accumulator, the counter block and the tag mask live in the
frame of the call and are cleared before it returns. The data may be any length up to `2^32 - 2` blocks; a partial last block
of associated data or cipher text is read as exactly the bytes it has, zero-padded to 16 bytes inside the routine, and the
key stream of a partial last data block is applied to exactly those bytes, so no byte of an array past its span is read or
written. `aesGcmSeal` needs `plaintext.count() + 16` bytes of `output` from `outputAt`, `aesGcmOpen` needs
`sealed.count() - 16`, and `sealed` is at least 16 bytes. `output` and the array the data comes from are either the
same bytes at the same address, which transforms the data in place, or regions that do not overlap, a zero-byte region
overlapping nothing wherever it lies; any other overlap is refused. `aesGcmOpen` compares the two tags without a branch or an address that depends on them, and answers `false`
for a mismatch with the plain text already written, which the caller discards.

`aesCtrXor` encrypts the counter blocks `counterBlock` with its last four bytes, read as a big-endian 32-bit counter,
increased by `counterOffset`, `counterOffset + 1`, and so on, and XORs each result into the next 16 bytes of the
buffer. `counterBlock` is a `ByteArray` of exactly 16 bytes: a 12-byte nonce, then the counter. Only that 32-bit word
counts: it wraps at 2^32 and never carries into the nonce, so a caller that must refuse a wrap checks it first. A
group is 1, 2, 4 or 8 blocks; a longer message is walked in groups, the widest first.

`schedule` is a `ByteArray` or a `FixedArray with Byte` of exactly `32 * (rounds + 1)` bytes, `rounds` being 10, 12 or 14 for a 16-, 24- or
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
	let bytes = ByteArray.create(0)
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
	let schedule = ByteArray.create(0)
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
	let sealed = ByteArray.create(0)
	sealed.resize(16)
	__Builtins.aesEncryptBlock(sealed, 0, schedule, plain, 0)
	let opened = ByteArray.create(0)
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
	let bytes = ByteArray.create(0)

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
	let sealed = ByteArray.create(0)
	sealed.resize(16)
	__Builtins.aesEncryptBlock(sealed, 0, schedule, block, 0)

	return sealed
end 'encrypted'

function decrypted(schedule ByteArray, block ByteArray) returns ByteArray
	let opened = ByteArray.create(0)
	opened.resize(16)
	__Builtins.aesDecryptBlock(opened, 0, schedule, block, 0)

	return opened
end 'decrypted'

function matches(software AesBitsliced, schedule ByteArray, seed Count) returns bool
	let plain = bytesFor(seed + 100, count: 16)
	let sealed = encrypted(schedule, block: plain)
	let softwareSealed = try software.cryptBlock(plain, direction: AesDirection.encrypt) otherwise panic("the block is 16 bytes")
	let softwareOpened = try software.cryptBlock(plain, direction: AesDirection.decrypt) otherwise panic("the block is 16 bytes")

	return toHex(sealed) == toHex(softwareSealed) and toHex(decrypted(schedule, block: sealed)) == toHex(plain) and toHex(decrypted(schedule, block: plain)) == toHex(softwareOpened)
end 'matches'

function compare(keyBytes Count) returns Count
	var mismatches = 0 as Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let software = try AesBitsliced.create(key) otherwise panic("the key is valid")
	let schedule = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let input = ByteArray.create(0)
	let output = ByteArray.create(0)
	let block = ByteArray.create(0)

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

	let aligned = ByteArray.create(0)
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

	let inPlace = ByteArray.create(0)
	inPlace.resize(16)
	__Builtins.aesEncryptBlock(inPlace, 0, schedule, block, 0)
	__Builtins.aesEncryptBlock(inPlace, 0, schedule, inPlace, 0)
	let twice = ByteArray.create(0)
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
An output array copied from a literal is the program's own buffer, so the block is stored into the copy and
the literal it started from is unchanged.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	let original = b"0123456789abcdef0123456789abcdef"
	let output = b"0123456789abcdef0123456789abcdef".clone()
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

<!-- test: aes-hardware.error.a-literal-array-is-refused-as-an-output -->
<!-- tier: stdlib -->
A `var` bound to a literal still names the program's constant data, so the block cannot be stored into it.
```maxon
function main() returns ExitCode
	var key = ByteArray.create(0)
	var schedule = ByteArray.create(0)

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	var output = b"0123456789abcdef0123456789abcdef"
	let plain = b"00112233445566778899aabbccddeeff"
	__Builtins.aesEncryptBlock(output, 3, schedule, plain, 0)

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:15:13: 'aesEncryptBlock' needs a writable argument; 'output' is immutable
```

<!-- test: aes-hardware.a-key-of-another-length-ends-the-program -->
<!-- tier: stdlib -->
Twenty bytes are not a key.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let block = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let input = ByteArray.create(0)
	let output = ByteArray.create(0)
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
	var schedule = ByteArray.create(0)
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
	var output = ByteArray.create(0)
	var schedule = ByteArray.create(0)
	__Builtins.aesEncryptBlock(output, 0, schedule, "text", 0)

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:13: '__Builtins.aesEncryptBlock' requires an argument of type ByteArray or FixedArray with Byte, but its argument is String
```

<!-- test: aes-hardware.error.refused-outside-the-library-tiers -->
Outside `stdlib/` and `runtime/` the call is refused at the member it names. It has no public replacement.
```maxon
function main() returns ExitCode
	var schedule = ByteArray.create(0)
	var key = ByteArray.create(0)
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

function seal(output ByteArray, keyBlock ByteArray, nonce ByteArray, data ByteArray)
	__Builtins.aesGcmSeal(output, 0, keyBlock, nonce, nonce, data)
end 'seal'

function open(output ByteArray, keyBlock ByteArray, nonce ByteArray, data ByteArray) returns bool
	return __Builtins.aesGcmOpen(output, 0, keyBlock, nonce, nonce, data)
end 'open'

function main() returns ExitCode
	var schedule = ByteArray.create(0)
	var key = ByteArray.create(0)
	var keyBlock = ByteArray.create(0)
	var nonce = ByteArray.create(0)
	var buffer = ByteArray.create(0)
	__Builtins.aesExpandKey(schedule, key)
	init(keyBlock, schedule: schedule)
	seal(buffer, keyBlock: keyBlock, nonce: nonce, data: buffer)
	_ = open(buffer, keyBlock: keyBlock, nonce: nonce, data: buffer)

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_init', which has no wasm32-wasi implementation
error E3104: <fragment>:7:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_seal', which has no wasm32-wasi implementation
error E3104: <fragment>:11:20: this construct lowers to the runtime entry '__cpufeat_aes_cipher_gcm_open', which has no wasm32-wasi implementation
error E3104: <fragment>:20:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_expand_key', which has no wasm32-wasi implementation
```

<!-- test: aes-hardware.ctr-groups-match-the-bitsliced-cipher -->
<!-- tier: stdlib -->
Every group size (1, 2, 4 and 8 blocks) at three block offsets inside a buffer that starts mid-array, under three
counters (zero, a middle one and the last one that fits), for each key size: the bytes of the group are the
bitsliced key stream XORed in, and the bytes around it are not touched.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	let bytes = ByteArray.create(0)

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
	let block = ByteArray.create(0)
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
	let schedule = ByteArray.create(0)
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let nonce = bytesFor(keyBytes + 7, count: 12)

	for groupIndex in 0 upto 4 'eachGroup'
		let group = 1 shl groupIndex

		for first in 0 upto 3 'eachFirst'
			for mode in 0 upto 3 'eachCounter'
				let counter = counterFor(mode, span: first + group)
				let region = bytesFor(keyBytes + group + first * 3 + mode, count: group * 16)
				let buffer = ByteArray.create(0)
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
	let bytes = ByteArray.create(0)

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
	let block = ByteArray.create(0)
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
	let schedule = ByteArray.create(0)
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let nonce = bytesFor(keyBytes + 7, count: 12)
	let offsets = [0, 3, 4294967298]

	for groupIndex in 0 upto 4 'eachGroup'
		let group = 1 shl groupIndex

		for back in 0 upto group + 1 'eachWrapPosition'
			for offset in offsets 'eachOffset'
				let counter = 4294967296 - back
				let buffer = bytesFor(back + group, count: group * 16)
				let region = buffer.clone()
				__Builtins.aesCtrXor(buffer, 0, schedule, counterBlock(nonce, counter: counter), offset, group)

				let expected = ByteArray.create(0)

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
	let bytes = ByteArray.create(0)

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
	let output = ByteArray.create(0)
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
	let output = ByteArray.create(0)
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
	let bytes = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let buffer = ByteArray.create(0)
	let counterBlock = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let buffer = ByteArray.create(0)
	let counterBlock = ByteArray.create(0)
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let buffer = ByteArray.create(0)
	let counterBlock = ByteArray.create(0)
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
	var buffer = ByteArray.create(0)
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
without associated data and a partial last block) sealed and opened with the GCM intrinsics: the key block, the
associated data and the plain text go in, and the seal answers the published cipher text and tag while the open of
that cipher text and tag accepts and answers the plain text.
```maxon
function digit(symbol Byte) returns Byte
	if symbol >= 0x61 'letter'
		return symbol - 0x57
	end 'letter'

	return symbol - 0x30
end 'digit'

function fromHex(hex String) returns ByteArray
	let bytes = ByteArray.create(0)
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

function keyBlockFor(key ByteArray) returns ByteArray
	let schedule = ByteArray.create(0)
	schedule.resize((key.count() + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let keyBlock = ByteArray.create(0)
	keyBlock.resize(256 + (schedule.count() shr 1))
	__Builtins.aesGcmInit(keyBlock, schedule)

	return keyBlock
end 'keyBlockFor'

function report(id String, keyHex String, nonceHex String, plainHex String, additionalHex String)
	let keyBlock = keyBlockFor(fromHex(keyHex))
	let nonce = fromHex(nonceHex)
	let plain = fromHex(plainHex)
	let additional = fromHex(additionalHex)
	let sealed = ByteArray.create(0)
	sealed.resize(plain.count() + 16)
	__Builtins.aesGcmSeal(sealed, 0, keyBlock, nonce, additional, plain)

	let opened = ByteArray.create(0)
	opened.resize(plain.count())
	let accepted = __Builtins.aesGcmOpen(opened, 0, keyBlock, nonce, additional, sealed)
	print("{id} [{toHex(sealed)}] {accepted} {toHex(opened) == plainHex}\n")
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
	let bytes = ByteArray.create(0)

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function flipped(sealed ByteArray, at Count) returns ByteArray
	let forged = try sealed.slice(0, endIndex: sealed.count()) otherwise panic("a copy of the whole array")
	let original = try forged.get(at) otherwise panic("the forged byte lies inside the array")
	try forged.set(at, value: original xor 1) otherwise panic("the forged byte lies inside the array")

	return forged
end 'flipped'

function refuses(aead AesGcm, nonce ByteArray, sealed ByteArray, additional ByteArray) returns bool
	let opened = try aead.open(nonce, sealed: sealed, additionalData: additional) otherwise return true

	return opened.count() > sealed.count()
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

<!-- test: aes-hardware.gcm-seal-and-open-take-any-length-at-any-offset -->
<!-- tier: stdlib -->
`aesGcmSeal` and `aesGcmOpen` take any length, reading the data from one array and writing the result into another at an
offset, with the bytes around the result framed by values that must stay as they were. For each key size, every data
length from 0 to 300 under every associated data length from 0 to 40: the seal writes the cipher text and tag the bitsliced
backing seals, the open of that answers `true` and writes the plain text, the open in place over the sealed array does
the same, and a tag, cipher text or associated data with one bit changed answers `false`.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function framed(bodyBytes Count) returns ByteArray
	let array = ByteArray.create(0)

	for _ in 0 upto 3 'eachLead'
		array.push(0x5A)
	end 'eachLead'

	array.resize(3 + bodyBytes)

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

function flipped(bytes ByteArray, at Count) returns ByteArray
	let forged = try bytes.slice(0, endIndex: bytes.count()) otherwise panic("a copy of the whole array")
	let original = try forged.get(at) otherwise panic("the forged byte lies inside the array")
	try forged.set(at, value: original xor 1) otherwise panic("the forged byte lies inside the array")

	return forged
end 'flipped'

function opens(keyBlock ByteArray, nonce ByteArray, additional ByteArray, sealed ByteArray) returns bool
	let scratch = ByteArray.create(0)
	scratch.resize(sealed.count() - 16)

	return __Builtins.aesGcmOpen(scratch, 0, keyBlock, nonce, additional, sealed)
end 'opens'

function wrongOutcomes(keyBlock ByteArray, nonce ByteArray, plain ByteArray, additional ByteArray, expected ByteArray) returns Count
	var wrong = 0 as Count
	let bytes = plain.count()

	let written = framed(bytes + 16)
	__Builtins.aesGcmSeal(written, 3, keyBlock, nonce, additional, plain)

	if not frameIntact(written, bodyBytes: bytes + 16) or not bodyOf(written, bodyBytes: bytes + 16).equals(expected) 'sealDiffers'
		wrong = wrong + 1
	end 'sealDiffers'

	let read = framed(bytes)

	if not __Builtins.aesGcmOpen(read, 3, keyBlock, nonce, additional, expected) or not frameIntact(read, bodyBytes: bytes) or not bodyOf(read, bodyBytes: bytes).equals(plain) 'openDiffers'
		wrong = wrong + 1
	end 'openDiffers'

	let inPlace = expected.clone()

	if not __Builtins.aesGcmOpen(inPlace, 0, keyBlock, nonce, additional, inPlace) or not (try inPlace.slice(0, endIndex: bytes) otherwise panic("the plain text lies inside the sealed array")).equals(plain) 'inPlaceDiffers'
		wrong = wrong + 1
	end 'inPlaceDiffers'

	if opens(keyBlock, nonce: nonce, additional: additional, sealed: flipped(expected, at: bytes + 15)) 'tagAccepted'
		wrong = wrong + 1
	end 'tagAccepted'

	if bytes > 0 and opens(keyBlock, nonce: nonce, additional: additional, sealed: flipped(expected, at: bytes - 1)) 'ciphertextAccepted'
		wrong = wrong + 1
	end 'ciphertextAccepted'

	if additional.count() > 0 and opens(keyBlock, nonce: nonce, additional: flipped(additional, at: additional.count() - 1), sealed: expected) 'additionalAccepted'
		wrong = wrong + 1
	end 'additionalAccepted'

	return wrong
end 'wrongOutcomes'

function compare(keyBytes Count) returns Count
	let key = bytesFor(keyBytes, count: keyBytes)
	let schedule = ByteArray.create(0)
	schedule.resize((keyBytes + 28) * 8)
	__Builtins.aesExpandKey(schedule, key)
	let keyBlock = ByteArray.create(0)
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
			wrong = wrong + wrongOutcomes(keyBlock, nonce: nonce, plain: plain, additional: additional, expected: expected)
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

<!-- test: aes-hardware.gcm-literal-arrays-are-copied-before-they-are-written -->
<!-- tier: stdlib -->
A key block or an output copied from a literal is the program's own buffer, so the calls write the copies and leave the
literals they started from unchanged.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	let originalKeys = b"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
	let keyBlock = b"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef".clone()
	__Builtins.aesGcmInit(keyBlock, schedule)

	var keysChanged = false

	for position in 0 upto 432 'eachKeyBlockByte'
		if (try keyBlock.get(position) otherwise 0) != (try originalKeys.get(position) otherwise 1) 'differs'
			keysChanged = true
		end 'differs'
	end 'eachKeyBlockByte'

	let plain = b"0123456789abcdef"
	let originalBuffer = b"0123456789abcdef0123456789abcdef"
	let buffer = b"0123456789abcdef0123456789abcdef".clone()
	__Builtins.aesGcmSeal(buffer, 0, keyBlock, b"0123456789ab", b"hello", plain)

	print("{keysChanged} {buffer.equals(originalBuffer)} {try originalBuffer.get(31) otherwise 0}\n")
	return 0
end 'main'
```
```stdout
true false 102
```
```exitcode
0
```

<!-- test: aes-hardware.error.a-literal-key-block-is-refused-as-the-gcm-init-target -->
<!-- tier: stdlib -->
A key block that names a literal cannot be filled by `aesGcmInit`.
```maxon
function main() returns ExitCode
	var key = ByteArray.create(0)
	var schedule = ByteArray.create(0)

	for position in 0 upto 16 'eachKeyByte'
		key.push(position as Byte)
	end 'eachKeyByte'

	schedule.resize(352)
	__Builtins.aesExpandKey(schedule, key)

	var keyBlock = b"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
	__Builtins.aesGcmInit(keyBlock, schedule)

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:14:13: 'aesGcmInit' needs a writable argument; 'keyBlock' is immutable
```

<!-- test: aes-hardware.a-gcm-nonce-of-another-length-ends-the-program -->
<!-- tier: stdlib -->
A nonce is exactly 12 bytes: one fewer is refused.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
	let nonce = ByteArray.create(0)
	let plain = ByteArray.create(0)
	let additional = ByteArray.create(0)
	let output = ByteArray.create(0)
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	plain.resize(16)
	output.resize(32)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmSeal(output, 0, keyBlock, nonce, additional, plain)
	print("12 bytes fit\n")
	nonce.resize(11)
	__Builtins.aesGcmSeal(output, 0, keyBlock, nonce, additional, plain)

	return 0
end 'main'
```
```stdout
12 bytes fit
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-sealed-array-shorter-than-a-tag-ends-the-program -->
<!-- tier: stdlib -->
`aesGcmOpen` reads a tag from the end of `sealed`, so the array is at least 16 bytes: 16 bytes are an empty cipher text,
15 are refused.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
	let nonce = ByteArray.create(0)
	let sealed = ByteArray.create(0)
	let additional = ByteArray.create(0)
	let output = ByteArray.create(0)
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	sealed.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)

	if not __Builtins.aesGcmOpen(output, 0, keyBlock, nonce, additional, sealed) 'forged'
		print("an empty cipher text has a tag\n")
	end 'forged'

	sealed.resize(15)
	print("{__Builtins.aesGcmOpen(output, 0, keyBlock, nonce, additional, sealed)}\n")

	return 0
end 'main'
```
```stdout
an empty cipher text has a tag
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-output-span-past-the-end-ends-the-program -->
<!-- tier: stdlib -->
`aesGcmSeal` writes the cipher text and the tag, so 32 bytes of plain text need 48 bytes of output from `outputAt`: a
48-byte array holds them at 0 and does not at 1.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
	let nonce = ByteArray.create(0)
	let plain = ByteArray.create(0)
	let additional = ByteArray.create(0)
	let output = ByteArray.create(0)
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	plain.resize(32)
	output.resize(48)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	__Builtins.aesGcmSeal(output, 0, keyBlock, nonce, additional, plain)
	print("the last span fits\n")
	__Builtins.aesGcmSeal(output, 1, keyBlock, nonce, additional, plain)

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

<!-- test: aes-hardware.a-gcm-output-that-partly-overlaps-its-source-ends-the-program -->
<!-- tier: stdlib -->
`aesGcmOpen` over the array it reads is in place at 0, where the two regions are the same bytes, and refused at 8, where
they overlap without being the same.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
	let nonce = ByteArray.create(0)
	let sealed = ByteArray.create(0)
	let additional = ByteArray.create(0)
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	sealed.resize(48)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	_ = __Builtins.aesGcmOpen(sealed, 0, keyBlock, nonce, additional, sealed)
	print("the same bytes are in place\n")
	_ = __Builtins.aesGcmOpen(sealed, 8, keyBlock, nonce, additional, sealed)

	return 0
end 'main'
```
```stdout
the same bytes are in place
```
```exitcode
70
```
```stderr
fatal error: a hardware crypto routine was handed a buffer or schedule of the wrong size
```

<!-- test: aes-hardware.a-gcm-open-of-a-bare-tag-writes-nothing-so-overlaps-nothing -->
<!-- tier: stdlib -->
`aesGcmOpen` over a sealed array of exactly one tag has no plain text to write, so an output position inside the bytes
it reads overlaps nothing: the call answers the verdict, `false` here because the tag of an empty message under this key
is not the all-zero one received, and leaves the array as it was.
```maxon
function main() returns ExitCode
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
	let nonce = ByteArray.create(0)
	let sealed = ByteArray.create(0)
	let additional = ByteArray.create(0)
	key.resize(16)
	schedule.resize(352)
	keyBlock.resize(432)
	nonce.resize(12)
	sealed.resize(16)
	__Builtins.aesExpandKey(schedule, key)
	__Builtins.aesGcmInit(keyBlock, schedule)
	let accepted = __Builtins.aesGcmOpen(sealed, 8, keyBlock, nonce, additional, sealed)
	var untouched = true

	for byte in sealed 'eachByte'
		untouched = untouched and byte == 0
	end 'eachByte'

	print("accepted={accepted} untouched={untouched}\n")

	return 0
end 'main'
```
```stdout
accepted=false untouched=true
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
	let key = ByteArray.create(0)
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
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

<!-- test: aes-hardware.a-gcm-schedule-of-another-size-ends-the-program -->
<!-- tier: stdlib -->
The schedule `aesGcmInit` copies the round keys from is exactly `32 * (rounds + 1)` bytes.
```maxon
function main() returns ExitCode
	let schedule = ByteArray.create(0)
	let keyBlock = ByteArray.create(0)
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
`aesGcmInit` takes two arguments, `aesGcmSeal` and `aesGcmOpen` six.
```maxon
function init(buffer ByteArray)
	__Builtins.aesGcmInit(buffer)
end 'init'

function seal(buffer ByteArray)
	__Builtins.aesGcmSeal(buffer)
end 'seal'

function open(buffer ByteArray) returns bool
	return __Builtins.aesGcmOpen(buffer)
end 'open'

function main() returns ExitCode
	var buffer = ByteArray.create(0)
	init(buffer)
	seal(buffer)
	_ = open(buffer)

	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:13: '__Builtins.aesGcmInit' takes exactly 2 arguments, but 1 was given
error E3036: <fragment>:7:13: '__Builtins.aesGcmSeal' takes exactly 6 arguments, but 1 was given
error E3036: <fragment>:11:20: '__Builtins.aesGcmOpen' takes exactly 6 arguments, but 1 was given
```

<!-- test: aes-hardware.error.gcm-operand-types -->
<!-- tier: stdlib -->
A buffer position takes a `ByteArray` and the offset position an integer.
```maxon
function seal(output ByteArray, keyBlock ByteArray, nonce ByteArray, plain ByteArray)
	__Builtins.aesGcmSeal(output, "text", keyBlock, nonce, nonce, plain)
end 'seal'

function open(output ByteArray, keyBlock ByteArray, nonce ByteArray, sealed ByteArray) returns bool
	return __Builtins.aesGcmOpen(output, 0, keyBlock, "text", nonce, sealed)
end 'open'

function main() returns ExitCode
	var output = ByteArray.create(0)
	var keyBlock = ByteArray.create(0)
	var nonce = ByteArray.create(0)
	var data = ByteArray.create(0)
	seal(output, keyBlock: keyBlock, nonce: nonce, plain: data)
	_ = open(output, keyBlock: keyBlock, nonce: nonce, sealed: data)

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:13: '__Builtins.aesGcmSeal' requires an argument of type int, but its argument is String
error E3005: <fragment>:7:20: '__Builtins.aesGcmOpen' requires an argument of type ByteArray or FixedArray with Byte, but its argument is String
```

<!-- test: aes-hardware.gcm-large-messages-match-the-software-backing -->
<!-- tier: stdlib -->
Messages of 1000, 4096, 16384, 16385 and 65553 bytes under 3000 bytes of associated data, for each key size, cross the
8-block loop many times: the hardware backing seals what the bitsliced backing seals and opens it.
```maxon
function bytesFor(seed Count, count Count) returns ByteArray
	let bytes = ByteArray.create(0)

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

<!-- test: aes-hardware.a-runtime-sized-fixed-array-is-a-buffer -->
<!-- tier: stdlib -->
Every buffer position takes a `FixedArray with Byte` as well as a `ByteArray`: the schedule, the key block, the output
of a block call, a counter-mode buffer, the output of a Galois-mode seal and the sealed input of an open give the bytes
the `ByteArray` calls give.
```maxon
typealias Bytes = FixedArray with Byte

function bytesFor(seed Count, count Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function blank(count Count) returns ByteArray
	let bytes = ByteArray.create(0)
	bytes.resize(count)

	return bytes
end 'blank'

function fixed(count Count) returns Bytes
	return Bytes.create(count)
end 'fixed'

function same(left Bytes, right ByteArray) returns bool
	if left.count() != right.count() 'length'
		return false
	end 'length'

	for position in 0 upto right.count() 'eachByte'
		let a = try left.get(position) otherwise return false
		let b = try right.get(position) otherwise return false

		if a != b 'differs'
			return false
		end 'differs'
	end 'eachByte'

	return true
end 'same'

function main() returns ExitCode
	let key = bytesFor(1, count: 16)
	let plain = bytesFor(2, count: 16)
	let counterBlock = bytesFor(3, count: 16)
	let nonce = bytesFor(4, count: 12)
	let additional = bytesFor(5, count: 20)
	let message = bytesFor(6, count: 40)

	let byteSchedule = blank(352)
	__Builtins.aesExpandKey(byteSchedule, key)
	let fixedSchedule = fixed(352)
	__Builtins.aesExpandKey(fixedSchedule, key)
	print("schedule {same(fixedSchedule, right: byteSchedule)}\n")

	let byteSealed = blank(16)
	__Builtins.aesEncryptBlock(byteSealed, 0, byteSchedule, plain, 0)
	let fixedSealed = fixed(16)
	__Builtins.aesEncryptBlock(fixedSealed, 0, fixedSchedule, plain, 0)
	print("encrypt {same(fixedSealed, right: byteSealed)}\n")

	let opened = fixed(16)
	__Builtins.aesDecryptBlock(opened, 0, fixedSchedule, fixedSealed, 0)
	print("decrypt {same(opened, right: plain)}\n")

	let byteStream = blank(32)
	__Builtins.aesCtrXor(byteStream, 0, byteSchedule, counterBlock, 0, 2)
	let fixedStream = fixed(32)
	__Builtins.aesCtrXor(fixedStream, 0, fixedSchedule, counterBlock, 0, 2)
	print("ctr {same(fixedStream, right: byteStream)}\n")

	let byteKeys = blank(432)
	__Builtins.aesGcmInit(byteKeys, byteSchedule)
	let fixedKeys = fixed(432)
	__Builtins.aesGcmInit(fixedKeys, fixedSchedule)
	print("keys {same(fixedKeys, right: byteKeys)}\n")

	let byteBox = blank(56)
	__Builtins.aesGcmSeal(byteBox, 0, byteKeys, nonce, additional, message)
	let fixedBox = fixed(56)
	__Builtins.aesGcmSeal(fixedBox, 0, fixedKeys, nonce, additional, message)
	print("seal {same(fixedBox, right: byteBox)}\n")

	let recovered = fixed(40)
	let authentic = __Builtins.aesGcmOpen(recovered, 0, fixedKeys, nonce, additional, fixedBox)
	print("open {authentic} {same(recovered, right: message)}\n")

	return 0
end 'main'
```
```stdout
schedule true
encrypt true
decrypt true
ctr true
keys true
seal true
open true true
```
```exitcode
0
```

<!-- test: aes-hardware.a-byte-array-a-generic-body-created-is-a-buffer -->
<!-- tier: stdlib -->
The `Array with Byte` a `Bag with Byte` creates inside its shared generic body strides one byte per element, as a
`ByteArray` created in a concrete body does, so the intrinsics, which treat a buffer as raw bytes, read and write it as
the bytes it holds. The FIPS-197 appendix C.1 vector goes through such an array in every buffer position and comes out
as the published cipher text, and back to the plain text.
```maxon
type Bag uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(0)}
	end 'create'

	function add(value Element)
		items.push(value)
	end 'add'
end 'Bag'

typealias ByteBag = Bag with Byte

function bagOf(bytes ByteArray) returns ByteBag
	let bag = ByteBag.create()

	for byte in bytes 'each'
		bag.add(byte)
	end 'each'

	return bag
end 'bagOf'

function zeros(count Count) returns ByteBag
	let bag = ByteBag.create()

	for _ in 0 upto count 'each'
		bag.add(0)
	end 'each'

	return bag
end 'zeros'

function dump(label String, bag ByteBag)
	print(label)

	for byte in bag.items 'each'
		print(" {byte}")
	end 'each'

	print("\n")
end 'dump'

function main() returns ExitCode
	let key = ByteArray.create(0)
	let plain = ByteArray.create(0)

	for index in 0 upto 16 'eachByte'
		key.push(index as Byte)
		plain.push((index * 17) as Byte)
	end 'eachByte'

	let keyBag = bagOf(key)
	let plainBag = bagOf(plain)
	let schedule = zeros(352)
	let sealed = zeros(16)
	let opened = zeros(16)

	__Builtins.aesExpandKey(schedule.items, keyBag.items)
	__Builtins.aesEncryptBlock(sealed.items, 0, schedule.items, plainBag.items, 0)
	__Builtins.aesDecryptBlock(opened.items, 0, schedule.items, sealed.items, 0)

	print("stride {schedule.items.managed.elementSize()} {sealed.items.managed.elementSize()}\n")
	dump("sealed", bag: sealed)
	dump("opened", bag: opened)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
stride 1 1
sealed 105 196 224 216 106 123 4 48 216 205 183 128 112 180 197 90
opened 0 17 34 51 68 85 102 119 136 153 170 187 204 221 238 255
```

<!-- test: aes-hardware.a-sized-fixed-array-is-a-buffer -->
<!-- tier: stdlib -->
A fixed array whose type states its length is a buffer in every position a `ByteArray` is, and gives the same bytes: it
strides one byte per element, as the runtime-sized form does.
```maxon
typealias Schedule = FixedArray with 352 Byte
typealias Block = FixedArray with 16 Byte
typealias Stream = FixedArray with 32 Byte
typealias Keys = FixedArray with 432 Byte
typealias Box = FixedArray with 56 Byte
typealias Recovered = FixedArray with 40 Byte
typealias Bytes = FixedArray with Byte

function bytesFor(seed Count, count Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto count 'eachByte'
		let mixed = ((index + seed * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function blank(count Count) returns ByteArray
	let bytes = ByteArray.create(0)
	bytes.resize(count)

	return bytes
end 'blank'

function same(left Bytes, right ByteArray) returns bool
	if left.count() != right.count() 'length'
		return false
	end 'length'

	for position in 0 upto right.count() 'eachByte'
		let a = try left.get(position) otherwise return false
		let b = try right.get(position) otherwise return false

		if a != b 'differs'
			return false
		end 'differs'
	end 'eachByte'

	return true
end 'same'

function main() returns ExitCode
	let key = bytesFor(1, count: 16)
	let plain = bytesFor(2, count: 16)
	let counterBlock = bytesFor(3, count: 16)
	let nonce = bytesFor(4, count: 12)
	let additional = bytesFor(5, count: 20)
	let message = bytesFor(6, count: 40)

	let byteSchedule = blank(352)
	__Builtins.aesExpandKey(byteSchedule, key)
	let fixedSchedule = Schedule.create()
	__Builtins.aesExpandKey(fixedSchedule, key)
	print("schedule {same(fixedSchedule, right: byteSchedule)}\n")

	let byteSealed = blank(16)
	__Builtins.aesEncryptBlock(byteSealed, 0, byteSchedule, plain, 0)
	let fixedSealed = Block.create()
	__Builtins.aesEncryptBlock(fixedSealed, 0, fixedSchedule, plain, 0)
	print("encrypt {same(fixedSealed, right: byteSealed)}\n")

	let opened = Block.create()
	__Builtins.aesDecryptBlock(opened, 0, fixedSchedule, fixedSealed, 0)
	print("decrypt {same(opened, right: plain)}\n")

	let byteStream = blank(32)
	__Builtins.aesCtrXor(byteStream, 0, byteSchedule, counterBlock, 0, 2)
	let fixedStream = Stream.create()
	__Builtins.aesCtrXor(fixedStream, 0, fixedSchedule, counterBlock, 0, 2)
	print("ctr {same(fixedStream, right: byteStream)}\n")

	let byteKeys = blank(432)
	__Builtins.aesGcmInit(byteKeys, byteSchedule)
	let fixedKeys = Keys.create()
	__Builtins.aesGcmInit(fixedKeys, fixedSchedule)
	print("keys {same(fixedKeys, right: byteKeys)}\n")

	let byteBox = blank(56)
	__Builtins.aesGcmSeal(byteBox, 0, byteKeys, nonce, additional, message)
	let fixedBox = Box.create()
	__Builtins.aesGcmSeal(fixedBox, 0, fixedKeys, nonce, additional, message)
	print("seal {same(fixedBox, right: byteBox)}\n")

	let recovered = Recovered.create()
	let authentic = __Builtins.aesGcmOpen(recovered, 0, fixedKeys, nonce, additional, fixedBox)
	print("open {authentic} {same(recovered, right: message)}\n")

	return 0
end 'main'
```
```stdout
schedule true
encrypt true
decrypt true
ctr true
keys true
seal true
open true true
```
```exitcode
0
```

<!-- test: aes-hardware.error.a-fixed-array-of-wider-elements-is-not-a-buffer -->
<!-- tier: stdlib -->
A buffer position takes a fixed array only of bytes.
```maxon
typealias Word = bits(64)
typealias Words = FixedArray with Word

function words(count Count) returns Words
	return Words.create(count)
end 'words'

function main() returns ExitCode
	var schedule = words(44)
	var key = ByteArray.create(0)
	__Builtins.aesExpandKey(schedule, key)

	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:13: '__Builtins.aesExpandKey' requires an argument of type ByteArray or FixedArray with Byte, but its argument is Words
```
