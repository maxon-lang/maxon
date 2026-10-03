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
| `Random.draw()` | a uniformly random `RandomDraw` in `0 to i64.max` |
| `Random.below(bound)` | a uniformly random `RandomDraw` in `0 upto bound`; `bound` is a `RandomBound`, `1 to i64.max` |
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
Two 63-bit draws are equal with probability 2^-63, so two equal draws mean the source is not random.
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
error E3005: <fragment>:3:24: Value 0 is outside the range of 'RandomBound' (int(1 to 9223372036854775807))
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
