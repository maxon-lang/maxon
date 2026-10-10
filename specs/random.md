---
feature: random
status: stable
keywords: [random, entropy, Random, fillRandom, __Builtins]
category: stdlib
---

# `Random` — draws from the operating system's random source

## Documentation

| Call | Meaning |
|---|---|
| `Random.draw()` | a uniformly random `RandomDraw` in `0 to u64.max` |
| `Random.below(bound)` | a uniformly random `RandomDraw` in `0 upto bound`; `bound` is a `RandomBound`, `1 to u64.max` |
| `__Builtins.fillRandom(managed)` | fills the buffer's live length from the OS; `0` when every byte was filled |

Both `Random` calls throw `RandomError.unavailable` when the operating system refuses to supply random bytes.
The bytes come from `ProcessPrng` on Windows, `getentropy` on macOS, the `getrandom` system call on Linux and
`wasi:random/random`'s `get-random-u64` on wasm32-wasi.

The answers are random, so the cases below assert properties of them rather than values.

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
