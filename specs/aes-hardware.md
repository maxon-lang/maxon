---
feature: aes-hardware
status: stable
keywords: [builtins, __Builtins, aesExpandKey, aesEncryptBlock, aesDecryptBlock, aesCtrXor, AES-NI, AESE, AESD, intrinsics, key schedule, hardware aes, fips197, counter mode, ctr]
category: system
---

# `__Builtins.aesExpandKey`, `aesEncryptBlock`, `aesDecryptBlock` and `aesCtrXor`

## Documentation

Four library-tier intrinsics run the AES block cipher on the CPU's AES instructions (AES-NI on x64, the Armv8
cryptographic extension on arm64). They take buffers and counts and nothing else: the 128-bit block lives only
inside the emitted routine, so the language needs no 128-bit value type.

| Call | Meaning |
|---|---|
| `__Builtins.aesExpandKey(schedule, key)` | writes the key schedule of `key` (16, 24 or 32 bytes) into `schedule` |
| `__Builtins.aesEncryptBlock(output, outputAt, schedule, input, inputAt)` | encrypts the 16 bytes at `inputAt` of `input` into the 16 bytes at `outputAt` of `output` |
| `__Builtins.aesDecryptBlock(output, outputAt, schedule, input, inputAt)` | the inverse cipher of the same shape |
| `__Builtins.aesCtrXor(buffer, bufferAt, schedule, counterBlock, counterOffset, blocks)` | XORs `blocks` (1, 2, 4 or 8) blocks of `buffer` from `bufferAt` with the counter-mode key stream, in place |

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
error E3005: <fragment>:5:13: '__Builtins.aesEncryptBlock' requires a ByteArray, but its argument is String
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
function main() returns ExitCode
	var schedule = ByteArray.create()
	var key = ByteArray.create()
	__Builtins.aesExpandKey(schedule, key)

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:5:13: this construct lowers to the runtime entry '__cpufeat_aes_cipher_expand_key', which has no wasm32-wasi implementation
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
