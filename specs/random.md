---
feature: random
status: stable
keywords: [random, entropy, Random, fillRandom]
category: stdlib
---

# `Random` — draws from the operating system's random source

## Documentation

| Call | Meaning |
|---|---|
| `Random.draw()` | a uniformly random `RandomDraw` in `0 to u64.max` |
| `Random.below(bound)` | a uniformly random `RandomDraw` in `0 upto bound`; `bound` is a `RandomBound`, `1 to u64.max` |
| `__Builtins.fillRandom(managed)` | library-tier: fills the buffer's live length from the OS; `0` when every byte was filled |

Both `Random` calls throw `RandomError.unavailable` when the operating system refuses to supply random bytes.
The bytes come from `ProcessPrng` on Windows, `getentropy` on macOS, the `getrandom` system call on Linux and
`wasi:random/random`'s `get-random-u64` on wasm32-wasi.

The answers are random, so the cases below assert properties of them rather than values. `fillRandom` is the
intrinsic under both `Random` calls, callable only from a source file under `stdlib/` or `runtime/`, so the
cases that drive it compile as library-tier sources (`<!-- tier: stdlib -->`).

## Tests

<!-- test: random.below-stays-below-its-bound-and-reaches-every-value -->
A thousand draws below 6 each land in `0 upto 6`, and every one of the six values is drawn.
```maxon
typealias FaceCounts = Array with bool

function main() returns ExitCode
	var seen = FaceCounts.create()
	seen.resize(6)

	for _ in 0 upto 1000 'eachDraw'
		let face = try Random.below(6) otherwise return 1

		if face >= 6 'outOfRange'
			return 2
		end 'outOfRange'

		try seen.set(face as ElementIndex, value: true) otherwise return 3
	end 'eachDraw'

	for wasSeen in seen 'eachFace'
		if not wasSeen 'missing'
			return 4
		end 'missing'
	end 'eachFace'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: random.two-draws-differ -->
Two 64-bit draws are equal with probability 2^-64, so two equal draws mean the source is not random.
```maxon
function main() returns ExitCode
	let first = try Random.draw() otherwise return 1
	let second = try Random.draw() otherwise return 2

	if first == second 'same'
		return 3
	end 'same'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: random.draw-reaches-the-top-bit -->
A draw is 64 random bits, so 256 draws all below 2^63 happen with probability 2^-256.
```maxon
typealias DrawTally = int(0 to 256)

function main() returns ExitCode
	var topBitDraws = 0 as DrawTally

	for _ in 0 upto 256 'eachDraw'
		let value = try Random.draw() otherwise return 1

		if value >= 9223372036854775808 as RandomDraw 'topBit'
			topBitDraws = topBitDraws + 1
		end 'topBit'
	end 'eachDraw'

	if topBitDraws > 0 'seen'
		print("top bit seen\n")
	end 'seen'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
top bit seen
```

<!-- test: random.below-a-bound-above-i64-max-reaches-the-top-half -->
A bound above `i64.max` is admitted, and 256 draws below `u64.max` all under 2^63 happen with probability about 2^-256.
```maxon
typealias DrawTally = int(0 to 256)

function main() returns ExitCode
	var topHalfDraws = 0 as DrawTally

	for _ in 0 upto 256 'eachDraw'
		let value = try Random.below(18446744073709551615) otherwise return 1

		if value >= 9223372036854775808 as RandomDraw 'topHalf'
			topHalfDraws = topHalfDraws + 1
		end 'topHalf'
	end 'eachDraw'

	if topHalfDraws > 0 'seen'
		print("top half seen\n")
	end 'seen'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
top half seen
```

<!-- test: random.below-one-is-zero -->
`0 upto 1` holds one value.
```maxon
function main() returns ExitCode
	let only = try Random.below(1) otherwise return 1
	return (42 + only) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: random.below-zero-is-refused -->
A bound of 0 names an empty range, and the bound's type refuses it.
```maxon
function main() returns ExitCode
	let none = try Random.below(0) otherwise return 1
	return none as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:24: Value 0 is outside the range of 'RandomBound' (int(1 to 18446744073709551615))
```

<!-- test: random.fill-random-fills-a-buffer-longer-than-one-request -->
<!-- tier: stdlib -->
A thousand bytes are filled in requests of at most 256, and the last request's bytes are filled too: the last
request is bytes 768 to 999, and 232 bytes drawn at random are all zero with probability 2^-1856.
```maxon
typealias ByteTally = int(0 to 1000)

function main() returns ExitCode
	var bytes = ByteArray.create()
	bytes.resize(1000)

	if __Builtins.fillRandom(bytes.managed) != 0 'refused'
		return 1
	end 'refused'

	var nonZeroInTheLastPiece = 0 as ByteTally

	for (iter, byte) in bytes.withIterator() 'eachByte'
		if iter.index() >= 768 and byte != 0 'filled'
			nonZeroInTheLastPiece = nonZeroInTheLastPiece + 1
		end 'filled'
	end 'eachByte'

	if nonZeroInTheLastPiece == 0 'unfilled'
		return 2
	end 'unfilled'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: random.fill-random-arity-checked -->
<!-- tier: stdlib -->
`fillRandom` takes exactly one buffer.
```maxon
function main() returns ExitCode
	return __Builtins.fillRandom() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.fillRandom' takes exactly 1 argument, but 0 were given
```

<!-- test: random.below-stays-below-large-bounds -->
Draws below assorted bounds stay below them, and a bound of three quarters of 2^64 reaches its top quarter.
```maxon
typealias Tally = int(0 to 100000)

function main() returns ExitCode
	for _ in 0 upto 400 'each'
		let a = try Random.below(3) otherwise return 1
		let b = try Random.below(10) otherwise return 2
		let c = try Random.below(9223372036854775809) otherwise return 3
		let d = try Random.below(18446744073709551614) otherwise return 4
		let e = try Random.below(13835058055282163712) otherwise return 5
		let f = try Random.below(18446744073709551615) otherwise return 6
		let g = try Random.below(4294967297) otherwise return 7
		let h = try Random.below(6148914691236517205) otherwise return 8

		if a >= 3 or b >= 10 or c >= 9223372036854775809 or d >= 18446744073709551614 or e >= 13835058055282163712 or f >= 18446744073709551615 or g >= 4294967297 or h >= 6148914691236517205 'tooBig'
			return 9
		end 'tooBig'
	end 'each'

	var top = 0 as Tally

	for _ in 0 upto 400 'quarter'
		let e = try Random.below(13835058055282163712) otherwise return 10

		if e >= 10376293541461622784 'inTheTopQuarter'
			top = top + 1
		end 'inTheTopQuarter'
	end 'quarter'

	if top == 0 'neverReached'
		return 11
	end 'neverReached'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: random.bytes-returns-exactly-the-count-asked-for -->
Each count is drawn as many times as the vector repeats it: 0, 1, 16, 32, 64, either side of `getentropy`'s 256-byte limit, 1000, past one `getRandomValues` call, a mebibyte, and past one `getrandom` call at 32 MiB.
```maxon
typealias DrawRepeat = int(1 to 1000)
typealias FailureTally = int(0 to 100)

function lengthFailures(id String, count RandomByteCount, repeat DrawRepeat) returns FailureTally
	for _ in 1 to repeat 'eachDraw'
		let drawn = try Random.bytes(count) otherwise return 1

		if drawn.count() != count 'wrongLength'
			print("{id} length {drawn.count()}\n")
			return 1
		end 'wrongLength'
	end 'eachDraw'

	print("{id} length {count}\n")
	return 0
end 'lengthFailures'

function main() returns ExitCode
	let failures = lengthFailures("random-bytes-001", count: 0, repeat: 10) + lengthFailures("random-bytes-002", count: 1, repeat: 100) + lengthFailures("random-bytes-003", count: 16, repeat: 100) + lengthFailures("random-bytes-004", count: 32, repeat: 100) + lengthFailures("random-bytes-005", count: 64, repeat: 100) + lengthFailures("random-bytes-006", count: 256, repeat: 10) + lengthFailures("random-bytes-007", count: 257, repeat: 10) + lengthFailures("random-bytes-008", count: 1000, repeat: 10) + lengthFailures("random-bytes-009", count: 65537, repeat: 3) + lengthFailures("random-bytes-010", count: 1048576, repeat: 1) + lengthFailures("random-bytes-011", count: 33554432, repeat: 1)
	return failures as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
random-bytes-001 length 0
random-bytes-002 length 1
random-bytes-003 length 16
random-bytes-004 length 32
random-bytes-005 length 64
random-bytes-006 length 256
random-bytes-007 length 257
random-bytes-008 length 1000
random-bytes-009 length 65537
random-bytes-010 length 1048576
random-bytes-011 length 33554432
```

<!-- test: random.bytes-draws-differ -->
Two equal 32-byte draws happen with probability 2^-256, and a repeat among a thousand 16-byte draws with probability below 2^-108.
```maxon
function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

typealias DrawRepeat = int(1 to 1000)
typealias FailureTally = int(0 to 100)
typealias DrawSet = Set with String

function uniqueFailures(id String, count RandomByteCount, repeat DrawRepeat) returns FailureTally
	var seen = DrawSet.create()

	for _ in 1 to repeat 'eachDraw'
		let drawn = try Random.bytes(count) otherwise return 1
		let hex = toHex(drawn)

		if seen.contains(hex) 'repeated'
			print("{id} repeated\n")
			return 1
		end 'repeated'

		seen.insert(hex)
	end 'eachDraw'

	print("{id} all unique\n")
	return 0
end 'uniqueFailures'

function main() returns ExitCode
	let failures = uniqueFailures("random-bytes-012", count: 32, repeat: 2) + uniqueFailures("random-bytes-013", count: 16, repeat: 1000)
	return failures as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
random-bytes-012 all unique
random-bytes-013 all unique
```

<!-- test: random.bytes-reach-every-byte-value -->
A byte value missing from 65536 random bytes happens with probability below 2^-360; five draws are checked.
```maxon
typealias DistinctTally = int(0 to 256)
typealias ValueSeen = Array with bool

function distinctValues(drawn ByteArray) returns DistinctTally
	var seen = ValueSeen.create()
	seen.resize(256)

	for byte in drawn 'eachByte'
		try seen.set(byte as ElementIndex, value: true) otherwise panic("a byte indexes 256 slots")
	end 'eachByte'

	var distinct = 0 as DistinctTally

	for wasSeen in seen 'eachValue'
		if wasSeen 'counted'
			distinct = distinct + 1
		end 'counted'
	end 'eachValue'

	return distinct
end 'distinctValues'

function main() returns ExitCode
	for _ in 0 upto 5 'eachDraw'
		let drawn = try Random.bytes(65536) otherwise return 1
		let distinct = distinctValues(drawn)

		if distinct != 256 'missing'
			print("random-bytes-014 {distinct} distinct\n")
			return 2
		end 'missing'
	end 'eachDraw'

	print("random-bytes-014 256 distinct\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
random-bytes-014 256 distinct
```
