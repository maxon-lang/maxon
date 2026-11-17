---
feature: hasher
status: stable
keywords: [Hasher, KeyedHasher, HashValue, HashDigest, HashSeed, MAXON_HASH_TIER, MAXON_HASH_SEED, seed, AES, hash tier, hash, digest, combine, combined, empty, finalize, resume, avalanche]
category: crypto
---

# `Hasher` and `KeyedHasher` — the process fold and the seeded 64-bit fold

## Documentation

`stdlib/Hasher.maxon` holds two hashes that are one arithmetic and differ in who chooses the seed. Either way a
digest depends on the seed, the steps folded and the hash tier the process runs on.

`Hasher` is the process fold. It starts from the seed this process drew at startup, which no program can name,
and it answers 32 bits:

```
let hasher = Hasher.create()
hasher.combineBytes(bytes)
let hash = hasher.finalize()
```

`finalize()` returns a `HashValue`, the width and type `String.hash()` returns: `String.hash()`,
`Character.hash()` and `Array.hash()` are this fold, so `"abc".hash()` is a `Hasher.create()` that
`combineBytes("abc".toByteArray())` and then `finalize()`. Two folds over equal steps finalize equal within
a process. A fold from the process seed has no state and no 64-bit digest a program can hold: `Hasher` has no
`empty`, `combined`, `combinedBytes` or `resume`, `create` takes no seed, and `finalize` answers 32 bits.

`KeyedHasher` is the fold under a seed the program holds, and it answers 64 bits. Every entry takes the
`HashSeed`; none has a default:

| Call | Meaning |
|---|---|
| `KeyedHasher.create(seed)` | a hasher whose fold starts from `seed` |
| `KeyedHasher.empty(seed)` | the state of a fold under `seed` with nothing in it |
| `KeyedHasher.combined(state, value)`, `KeyedHasher.combinedBytes(state, bytes)` | one step: `value` or `bytes` folded into `state`, which is the seed of that step |
| `KeyedHasher.resume(state)` | a hasher that continues from a state `finalize` returned |
| `hasher.combine(value)`, `hasher.combineBytes(bytes)` | the same steps on a mutable hasher |
| `hasher.finalize()` | the running state, a `HashDigest`: `bits(64)`, every bit of which carries hash |

The result type of the keyed fold is `HashDigest`, not `HashValue`, the 32-bit surface `Hashable` answers on.

### Seeds

| Call | Meaning |
|---|---|
| `HashSeed.random()` | the process's next seed: 64 bits of fresh OS entropy, or under a pinned `MAXON_HASH_SEED` the next seed of the sequence the pin fixes |
| `seed.toBytes()` | the seed as exactly 8 bytes |
| `HashSeed.fromBytes(bytes)` | the seed those 8 bytes spell; any other count throws `HashSeedError` |

Two keyed folds given one seed and the same steps reach one digest. A seed written with `toBytes` and read back
with `fromBytes` is the same seed, which is how a seed outlives its process: the compiler's library cache
stores one.

There is no `HashSeed.process()`. The process seed is not readable: no public API returns its bits, and a fold
with no steps is not the seed it started from. `KeyedHasher.empty(seed)` and
`KeyedHasher.create(seed).finalize()` are different words from the little-endian word `seed.toBytes()` spells,
and `Hasher.create().finalize()` is not part of the process seed either. A program that must store a seed beside
its digests draws its own with `HashSeed.random()`.

Unpinned, with `MAXON_HASH_SEED` unset or empty, every `HashSeed.random()` is fresh entropy from the operating
system. Under a pinned `MAXON_HASH_SEED`, the n-th `HashSeed.random()` of a process, whichever thread makes it, is
the same seed on every run and on every tier, no draw is the pinned seed itself, and two pins fix two different
sequences. How a pin maps to its seeds is unspecified, so the cases below compare
draws and never pin one. Every call is a draw that advances the sequence: two calls in one expression are two
draws, and so is a call whose result is discarded.

### Two faces of the keyed fold

```
let digest = KeyedHasher.combined(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: name), value: name.count())
```

`KeyedHasher.empty(seed)` is the state of a fold with nothing in it, and `combined(state, value)` and
`combinedBytes(state, bytes)` take a state and return the next one. The state is the seed of the step that
consumes it, so every step is delimited: `"ab"` then `"c"` does not reach the digest of `"abc"`.
`create(seed)` is `resume(empty(seed))` and `combine` is `state = combined(state, …)`, so the object face and the
pure face are one fold. The pure face is what a nested call or a `match` arm needs, where a mutable hasher
has no expression position. `Hasher` has the object face alone.

No state outside the seed and the steps enters a digest: equal seeds and equal steps give equal digests in
any process that runs on the same tier.

### Hash tiers

The tier is chosen once, before `main`:

| Tier | Mechanism | Chosen when |
|---|---|---|
| `aes` | AES rounds (`AESENC` on x64, `AESE`+`AESMC` on arm64) | the CPU has AES |
| `software` | portable Maxon | otherwise, and always on wasm32-wasi |

`MAXON_HASH_TIER=aes|software` forces a tier, and an empty value is the same as an unset one. A value
that names no tier, and a tier the host cannot run, are refused before `main` with exit 1 and one line on
stderr:

```
MAXON_HASH_TIER=<value> is not a hash tier: expected aes or software
MAXON_HASH_TIER=<value> is not available on this host: its best hash tier is <best>
```

The tiers are different hashes: one seed and one message give a different digest on each. `software` is one
function on every target: one seed and one sequence of steps reach one digest on every host.

### No stability promise

A digest is a property of a process: its seed, its tier and its host. Nothing keeps one beyond the
process without keeping the seed and the tier with it, and a digest that must be the same everywhere is a
SHA-256 digest (`Sha2`). The cases below therefore pin relations between digests and never a digest's
value, with one exception: the `software` tier is the same function on every target, so the one case that
pins both a seed and that tier through the environment pins the digests that function reaches.

## Tests

<!-- test: one-seed-gives-one-digest -->
Two hashers built from one `HashSeed.random()` and fed the same steps reach one digest, and so does the
pure face started from `KeyedHasher.empty(seed)`. A seed that did not reach the fold, or a fold that read
state from outside it, would let the two disagree.
```maxon
function objectFold(seed HashSeed) returns HashDigest
	let hasher = KeyedHasher.create(seed)
	hasher.combineBytes("hello".toByteArray())
	hasher.combine(42)
	return hasher.finalize()
end 'objectFold'

function main() returns ExitCode
	let seed = HashSeed.random()
	let first = objectFold(seed)
	let second = objectFold(seed)
	let pure = KeyedHasher.combined(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "hello".toByteArray()), value: 42)

	print("two hashers agree: {first == second}\n")
	print("the pure face agrees: {first == pure}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
two hashers agree: true
the pure face agrees: true
```

<!-- test: two-seeds-give-two-digests -->
Two seeds drawn with `HashSeed.random()` give two different empty states and two different digests of every
message, the empty one included. Two 64-bit draws make an equal pair a chance of about 2^-64, so an equal
pair is a seed that did not reach the fold.
```maxon
typealias MessageList = Array with String

function main() returns ExitCode
	let one = HashSeed.random()
	let other = HashSeed.random()

	print("the seeds differ: {not one.toBytes().equals(other.toBytes())}\n")
	print("the empty states differ: {KeyedHasher.empty(one) != KeyedHasher.empty(other)}\n")

	let messages = MessageList from ["", "a", "hello", "a message longer than one sixteen-byte block"]

	for message in messages 'eachMessage'
		let bytes = message.toByteArray()
		let underOne = KeyedHasher.combinedBytes(KeyedHasher.empty(one), bytes: bytes)
		let underOther = KeyedHasher.combinedBytes(KeyedHasher.empty(other), bytes: bytes)
		print("{bytes.count()} bytes differ: {underOne != underOther}\n")
	end 'eachMessage'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
the seeds differ: true
the empty states differ: true
0 bytes differ: true
1 bytes differ: true
5 bytes differ: true
44 bytes differ: true
```

<!-- test: a-seed-survives-its-bytes -->
`toBytes()` is exactly 8 bytes, and `fromBytes` of them is the same seed: it gives the same empty state and
the same digest. A byte array of any other length names no seed and throws.
```maxon
function refused(bytes ByteArray) returns bool
	let seed = try HashSeed.fromBytes(bytes) otherwise return true
	print("{bytes.count()} bytes were taken as a seed of {seed.toBytes().count()}\n")
	return false
end 'refused'

function main() returns ExitCode
	let seed = HashSeed.random()
	let bytes = seed.toBytes()
	print("bytes={bytes.count()}\n")

	let back = try HashSeed.fromBytes(bytes) otherwise panic("8 bytes are a seed")
	let message = "a seed survives its bytes".toByteArray()

	print("same bytes: {back.toBytes().equals(bytes)}\n")
	print("same empty state: {KeyedHasher.empty(back) == KeyedHasher.empty(seed)}\n")
	print("same digest: {KeyedHasher.combinedBytes(KeyedHasher.empty(back), bytes: message) == KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: message)}\n")

	let seven = bytes.clone()
	_ = try seven.pop() otherwise panic("8 bytes have a last")
	let nine = bytes.clone()
	nine.push(0)

	print("0 refused: {refused(ByteArray.create(0))}\n")
	print("7 refused: {refused(seven)}\n")
	print("9 refused: {refused(nine)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bytes=8
same bytes: true
same empty state: true
same digest: true
0 refused: true
7 refused: true
9 refused: true
```

<!-- test: the-fold-separates-the-parts -->
Each step's state is the seed of the next, so the boundary between two steps is part of what is hashed:
`"ab"` then `"c"`, `"a"` then `"bc"` and `"abc"` are three different folds under one `HashSeed`, through
both faces of `KeyedHasher`. A fold over a flat byte stream reaches one digest for all three.
```maxon
typealias PartArray = Array with String

function objectFold(parts PartArray, seed HashSeed) returns HashDigest
	let hasher = KeyedHasher.create(seed)

	for part in parts 'each'
		hasher.combineBytes(part.toByteArray())
	end 'each'

	return hasher.finalize()
end 'objectFold'

function pureFold(parts PartArray, seed HashSeed) returns HashDigest
	var state = KeyedHasher.empty(seed)

	for part in parts 'each'
		state = KeyedHasher.combinedBytes(state, bytes: part.toByteArray())
	end 'each'

	return state
end 'pureFold'

function main() returns ExitCode
	let seed = HashSeed.random()
	let whole = PartArray from ["abc"]
	let abThenC = PartArray from ["ab", "c"]
	let aThenBc = PartArray from ["a", "bc"]

	print("object whole/ab-c: {objectFold(whole, seed: seed) != objectFold(abThenC, seed: seed)}\n")
	print("object whole/a-bc: {objectFold(whole, seed: seed) != objectFold(aThenBc, seed: seed)}\n")
	print("object ab-c/a-bc: {objectFold(abThenC, seed: seed) != objectFold(aThenBc, seed: seed)}\n")
	print("pure whole/ab-c: {pureFold(whole, seed: seed) != pureFold(abThenC, seed: seed)}\n")
	print("pure whole/a-bc: {pureFold(whole, seed: seed) != pureFold(aThenBc, seed: seed)}\n")
	print("pure ab-c/a-bc: {pureFold(abThenC, seed: seed) != pureFold(aThenBc, seed: seed)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
object whole/ab-c: true
object whole/a-bc: true
object ab-c/a-bc: true
pure whole/ab-c: true
pure whole/a-bc: true
pure ab-c/a-bc: true
```

<!-- test: one-bit-avalanches -->
Flipping one bit of the input moves about half of the 64 output bits of a `KeyedHasher` fold under one
`HashSeed.random()`. For every length on either side of
the hash's block boundaries, each bit of each byte is flipped in turn: the digests differ by at least 16
bits on average and never by none. A step that multiplies its way upward leaves the low bits of a flip in
the last byte nearly untouched and fails here; a zero is a byte the hash did not read. A zero-length
message has no bit to flip, so the lengths start at 1.
```maxon
typealias LengthList = Array with Count

function bytesFor(length Count) returns ByteArray
	let bytes = ByteArray.create(0)

	for index in 0 upto length 'eachByte'
		let mixed = ((index + length * 977 + 1) * 2654435761 + 12345) mod 4294967296
		bytes.push(((mixed shr 11) and 0xFF) as Byte)
	end 'eachByte'

	return bytes
end 'bytesFor'

function bitsSet(pattern HashDigest) returns Count
	var rest = pattern
	var count = 0 as Count

	while rest != 0 'eachBit'
		count = count + ((rest and 1) as Count)
		rest = rest shr 1
	end 'eachBit'

	return count
end 'bitsSet'

function main() returns ExitCode
	let lengths = LengthList from [1, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 300]
	let masks = ByteArray from [1, 2, 4, 8, 16, 32, 64, 128]
	let seed = HashSeed.random()

	for length in lengths 'eachLength'
		let bytes = bytesFor(length)
		let digest = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: bytes)
		var moved = 0 as Count
		var flips = 0 as Count
		var unmoved = 0 as Count

		for index in 0 upto length 'eachByte'
			let original = try bytes.get(index) otherwise panic("the index is inside the message")

			for mask in masks 'eachMask'
				let flipped = bytes.clone()
				try flipped.set(index, value: original xor mask) otherwise panic("the index is inside the message")
				let distance = bitsSet(digest xor KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: flipped))
				moved = moved + distance
				flips = flips + 1

				if distance == 0 'unchanged'
					unmoved = unmoved + 1
				end 'unchanged'
			end 'eachMask'
		end 'eachByte'

		print("{length} mean>=16: {moved >= flips * 16} never 0: {unmoved == 0}\n")
	end 'eachLength'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
1 mean>=16: true never 0: true
15 mean>=16: true never 0: true
16 mean>=16: true never 0: true
17 mean>=16: true never 0: true
31 mean>=16: true never 0: true
32 mean>=16: true never 0: true
33 mean>=16: true never 0: true
63 mean>=16: true never 0: true
64 mean>=16: true never 0: true
65 mean>=16: true never 0: true
127 mean>=16: true never 0: true
128 mean>=16: true never 0: true
129 mean>=16: true never 0: true
300 mean>=16: true never 0: true
```

<!-- test: the-process-seed-differs-between-processes -->
<!-- unsupported-targets: wasm32-wasi -->
Each process draws its own seed at startup, so the digest of one message differs from one process to the
next. The program re-executes itself three times and each child prints the digest of a fixed message under
its process seed, which a program can observe where it cannot read the seed. The child's `Hasher` answers 32
bits, printed as eight hexadecimal digits; three 32-bit draws that all agree are a chance of about 2^-64, so
an agreement is a seed that was not drawn. Each child's
`MAXON_HASH_SEED` is set empty, which draws a seed: a harness that pins the case's own seed in the
environment would otherwise hand every child the one seed. Not on `wasm32-wasi`, where the subprocess band
is refused at compile time (`subprocess-unsupported.md`).
```maxon
function spelled(hash HashValue) returns String
	return "{hash:08x}"
end 'spelled'

function child() returns ExitCode
	let hasher = Hasher.create()
	hasher.combineBytes("one message".toByteArray())
	print("{spelled(hasher.finalize())}\n")
	return 0
end 'child'

function spawnChild(me FilePath) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": ""])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let first = spawnChild(me)
	let second = spawnChild(me)
	let third = spawnChild(me)

	print("children exit={first.exitCode()} {second.exitCode()} {third.exitCode()}\n")
	print("children printed a hash: {first.stdout.count() > 1 and second.stdout.count() > 1 and third.stdout.count() > 1}\n")
	print("not all equal: {not (first.stdout == second.stdout and second.stdout == third.stdout)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0 0
children printed a hash: true
not all equal: true
```

<!-- test: each-tier-is-its-own-hash -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
`MAXON_HASH_TIER` forces the tier, so every tier is exercised on every host. The parent draws one seed and
re-executes itself twice per tier with the seed's bytes in `SEED`; each child folds one message from that
seed with `KeyedHasher`. A tier the host can run answers the same digest twice, a tier it cannot run is refused before `main`
naming the host's best tier, the tiers the host can run give pairwise different digests, and an empty
`MAXON_HASH_TIER` is the host's best tier. The answers are written so they are the same on every host. Not
on `wasm32-wasi`, where the subprocess band is refused at compile time and the tier is always `software`.
```maxon
typealias TierList = Array with String

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

function child() returns ExitCode
	let hex = try Process.environmentVariable("SEED") otherwise return 3
	let seed = try HashSeed.fromBytes(fromHex(hex)) otherwise return 4
	let digest = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "each tier is its own hash".toByteArray())
	print("{digest:016x}\n")
	return 0
end 'child'

function hostRuns(tier String) returns bool
	if tier == "aes" 'aes'
		return __Builtins.cpuHasAes()
	end 'aes' else if tier == "software" 'software'
		return true
	end 'software'

	panic("'{tier}' is not one of the tiers this case runs")
end 'hostRuns'

function bestTier() returns String
	if hostRuns("aes") 'aes'
		return "aes"
	end 'aes'

	return "software"
end 'bestTier'

function runUnder(me FilePath, tier String, seedHex String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_TIER": tier, "SEED": seedHex])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'runUnder'

function answeredAsTheHostAllows(run CollectedOutput, tier String) returns bool
	if hostRuns(tier) 'runs'
		return run.exitCode() == 0 and run.stderr == "" and run.stdout.count() > 1
	end 'runs'

	return run.exitCode() == 1 and run.stdout == "" and run.stderr == "MAXON_HASH_TIER={tier} is not available on this host: its best hash tier is {bestTier()}\n"
end 'answeredAsTheHostAllows'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let seedHex = toHex(HashSeed.random().toBytes())
	let tiers = TierList from ["aes", "software"]
	let digests = StringArray.create(0)

	for tier in tiers 'eachTier'
		let first = runUnder(me, tier: tier, seedHex: seedHex)
		let second = runUnder(me, tier: tier, seedHex: seedHex)
		print("{tier} answers as the host allows: {answeredAsTheHostAllows(first, tier: tier) and answeredAsTheHostAllows(second, tier: tier)}\n")
		print("{tier} is the same twice: {first.stdout == second.stdout}\n")

		if hostRuns(tier) 'runs'
			digests.push(first.stdout)
		end 'runs'
	end 'eachTier'

	var distinct = true

	for (outer, one) in digests.withIterator() 'eachOne'
		for (inner, other) in digests.withIterator() 'eachOther'
			if outer.index() < inner.index() and one == other 'same'
				distinct = false
			end 'same'
		end 'eachOther'
	end 'eachOne'

	print("the tiers give different digests: {distinct}\n")

	let unset = runUnder(me, tier: "", seedHex: seedHex)
	let best = runUnder(me, tier: bestTier(), seedHex: seedHex)
	print("an empty tier is the best tier: {unset.exitCode() == 0 and unset.stdout == best.stdout}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aes answers as the host allows: true
aes is the same twice: true
software answers as the host allows: true
software is the same twice: true
the tiers give different digests: true
an empty tier is the best tier: true
```

<!-- test: an-unknown-tier-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A `MAXON_HASH_TIER` that names no tier is refused before `main`: the child writes nothing to stdout, one
line to stderr and exits 1. Silently falling back to the best tier would hide a misspelt setting. The program
re-executes itself with `fast` as the value and prints what the child did. Not on `wasm32-wasi`, where the
subprocess band is refused at compile time.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		if CommandLine.args().count() > 8 'neverReached'
			print("{"abc".hash():08x}\n")
		end 'neverReached'

		print("main ran\n")
		return 0
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_TIER": "fast"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child stdout=[{run.stdout}]\n")
	print("child stderr=[{run.stderr}]\n")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
child stdout=[]
child stderr=[MAXON_HASH_TIER=fast is not a hash tier: expected aes or software
]
child exit=1
```

<!-- test: a-pinned-seed-is-the-process-seed-in-every-process -->
<!-- unsupported-targets: wasm32-wasi -->
`MAXON_HASH_SEED` set to 16 hexadecimal digits is the process seed instead of a random draw: the digits are
read as the seed's 8 bytes in order, upper case as well as lower. The program re-executes itself with the
seed pinned, and each child prints its `Hasher` fold of `"abc"` under its process seed, which a program can
observe where it cannot read the seed, and whether that fold is `"abc".hash()`. Two children pinned to
`0123456789ABCDEF` and `0123456789abcdef` print one hash: the pin is the process seed, whatever case it is
written in, so one seed, one tier and one program reproduce one hash in every process. A child pinned to
`fedcba9876543210` prints another. The parent prints the hashes only as equalities, because the tier, and with
it the hash, differs between hosts. Not on `wasm32-wasi`, where the subprocess band is refused at compile time.
```maxon
function child() returns ExitCode
	let hasher = Hasher.create()
	hasher.combineBytes("abc".toByteArray())
	let folded = hasher.finalize()
	print("{folded:08x}\n{folded == "abc".hash()}\n")
	return 0
end 'child'

function spawnPinned(me FilePath, seed String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": seed])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnPinned'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let upper = spawnPinned(me, seed: "0123456789ABCDEF")
	let lower = spawnPinned(me, seed: "0123456789abcdef")
	let other = spawnPinned(me, seed: "fedcba9876543210")

	print("children exit={upper.exitCode()} {lower.exitCode()} {other.exitCode()}\n")
	print("children stderr=[{upper.stderr}] [{lower.stderr}] [{other.stderr}]\n")
	print("a hash was printed: {upper.stdout.count() > 1}\n")
	print("the child's fold is its string hash: {upper.stdout.endsWith("\ntrue\n")}\n")
	print("upper and lower case are one seed: {upper.stdout == lower.stdout}\n")
	print("another seed, another hash: {upper.stdout != other.stdout}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0 0
children stderr=[] [] []
a hash was printed: true
the child's fold is its string hash: true
upper and lower case are one seed: true
another seed, another hash: true
```

<!-- test: a-malformed-seed-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
A `MAXON_HASH_SEED` that is not exactly 16 hexadecimal digits is refused before `main`: the child writes
nothing to stdout, one line to stderr and exits 1. Reading a short value as a partial seed would pin a seed
the setting does not spell. The program re-executes itself with `0123` as the value and prints what the
child did. Not on `wasm32-wasi`, where the subprocess band is refused at compile time.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		if CommandLine.args().count() > 8 'neverReached'
			print("{"abc".hash():08x}\n")
		end 'neverReached'

		print("main ran\n")
		return 0
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": "0123"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child stdout=[{run.stdout}]\n")
	print("child stderr=[{run.stderr}]\n")
	print("child exit={run.exitCode()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
child stdout=[]
child stderr=[MAXON_HASH_SEED=0123 is not a hash seed: expected 16 hexadecimal digits
]
child exit=1
```

<!-- test: an-unknown-tier-is-refused-by-the-environment -->
<!-- env: MAXON_HASH_TIER=fast -->
The same refusal on every lane, `wasm32-wasi` included: the harness launches the program itself with
`MAXON_HASH_TIER=fast`, so `main` never runs, stdout is empty, stderr holds the one line and the exit code is 1.
The program hashes only on a path it never takes, and is refused all the same: a program that contains the
hash runtime validates both variables before `main`, whether or not it reaches a hash.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 8 'neverReached'
		print("{"abc".hash():08x}\n")
	end 'neverReached'

	print("main ran\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
MAXON_HASH_TIER=fast is not a hash tier: expected aes or software
```

<!-- test: the-sha-tier-is-refused-like-any-unknown-tier -->
<!-- env: MAXON_HASH_TIER=sha -->
There is no `sha` tier: the name is refused exactly as `fast` is, on every lane and on every host whatever
instructions its CPU has. `main` never runs, stdout is empty, stderr holds the one line naming the value and
the two tiers there are, and the exit code is 1.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 8 'neverReached'
		print("{"abc".hash():08x}\n")
	end 'neverReached'

	print("main ran\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
MAXON_HASH_TIER=sha is not a hash tier: expected aes or software
```

<!-- test: a-pinned-seed-from-the-environment-is-not-readable-from-the-empty-fold -->
<!-- env: MAXON_HASH_SEED=0123456789abcdef -->
<!-- env: MAXON_HASH_TIER=software -->
`MAXON_HASH_SEED` pins the process seed on every lane, `wasm32-wasi` included, and on the software tier,
which every lane can run. The harness launches the program with the seed in lower case, so the process
seed's 8 bytes are the digits read in order and the little-endian word they spell is `0xefcdab8967452301`.
A `Hasher` that has folded nothing answers neither half of that word, `0x67452301` or `0xefcdab89`: a program
that prints it would print part of the seed.
```maxon
function main() returns ExitCode
	let created = Hasher.create().finalize()

	print("created is not the seed's low half: {created != 0x67452301}\n")
	print("created is not the seed's high half: {created != 0xefcdab89}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
created is not the seed's low half: true
created is not the seed's high half: true
```

<!-- test: a-fold-with-no-steps-is-not-its-seed -->
A seed is not recoverable from the fold that starts at it. For the seed 8 fixed bytes spell, the empty
state `KeyedHasher.empty(seed)` and the state of a hasher that has folded nothing, `KeyedHasher.create(seed).finalize()`,
are both different from the little-endian word those bytes spell, which is what a fold that returned
its seed would answer. The two faces still agree with each other, and carrying the state across a
boundary with `finalize()` and `resume()` still continues the fold exactly: the resumed hasher reaches the
state the pure face reaches from the carried value.
```maxon
function main() returns ExitCode
	let spelled = ByteArray from [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]
	let seed = try HashSeed.fromBytes(spelled) otherwise panic("8 bytes are a seed")
	let word = 0xefcdab8967452301 as HashDigest
	let created = KeyedHasher.create(seed)
	let carried = created.finalize()

	print("empty is not the seed word: {KeyedHasher.empty(seed) != word}\n")
	print("created is not the seed word: {carried != word}\n")
	print("the faces agree: {carried == KeyedHasher.empty(seed)}\n")

	let resumed = KeyedHasher.resume(carried)
	resumed.combineBytes("lo".toByteArray())
	resumed.combine(7)

	let pure = KeyedHasher.combined(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "lo".toByteArray()), value: 7)
	print("resumed continues the fold: {resumed.finalize() == pure}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty is not the seed word: true
created is not the seed word: true
the faces agree: true
resumed continues the fold: true
```

<!-- test: the-process-seed-has-no-public-accessor -->
No public API serialises the process seed. `HashSeed.process()` is not a member of `HashSeed`, so a call
to it is refused as any call to a static member the type does not have is: E3004, an undefined function. A
program that needs a seed beside its digests draws one with `HashSeed.random()`.
```maxon
function main() returns ExitCode
	let seed = HashSeed.process()
	print("{seed.toBytes().count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:22: call to undefined function 'HashSeed.process'
```

<!-- test: the-software-tier-is-one-function-on-every-target -->
<!-- env: MAXON_HASH_SEED=0123456789abcdef -->
<!-- env: MAXON_HASH_TIER=software -->
The `software` tier is one function on every target: one seed and one sequence of steps reach one digest on
x64-windows, x64-linux, arm64-macos, arm64-linux and wasm32-wasi alike. The harness launches the program on
that tier under a pinned seed, and no `unsupported-targets` line excludes a lane, so every lane prints
these hashes and none may print another: a 32-bit `Hasher` fold of bytes and a word from the process seed,
the hash of a `String`, the hash of an `Array with int`, and a `KeyedHasher` fold from a seed spelled by fixed
bytes. A target whose
software fold differs, in how a multiply carries, in the byte order a word is read in or in how a tail is
padded, reddens here on its own lane.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerList = Array with Integer

function main() returns ExitCode
	let message = ByteArray from [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14]
	let spelled = ByteArray from [0xF0, 0xDE, 0xBC, 0x9A, 0x78, 0x56, 0x34, 0x12]
	let explicit = try HashSeed.fromBytes(spelled) otherwise panic("8 bytes are a seed")

	let processFold = Hasher.create()
	processFold.combineBytes(message)
	processFold.combine(0x0123456789ABCDEF)
	print("process fold: {processFold.finalize():08x}\n")

	print("abc: {"abc".hash():08x}\n")
	print("array: {IntegerList from [1, 2, 3, 4, 5].hash():08x}\n")

	let seededFold = KeyedHasher.create(explicit)
	seededFold.combineBytes(message)
	seededFold.combine(0x0123456789ABCDEF)
	print("seeded fold: {seededFold.finalize():016x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
process fold: a8d32430
abc: 27978d62
array: c0f337cb
seeded fold: aad21b3d9e3c1ee5
```

<!-- test: the-aes-tier-is-refused-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- env: MAXON_HASH_TIER=aes -->
The best tier on `wasm32-wasi` is software, so forcing `aes` there names a tier the host cannot run: `main`
never runs, stderr holds the one line naming the best tier, and the exit code is 1. Only `wasm32-wasi` is
pinned, because on a native lane whether `aes` is available is a property of the CPU.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 8 'neverReached'
		print("{"abc".hash():08x}\n")
	end 'neverReached'

	print("main ran\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
MAXON_HASH_TIER=aes is not available on this host: its best hash tier is software
```

<!-- test: a-program-that-never-hashes-does-not-read-the-hash-variables -->
<!-- env: MAXON_HASH_SEED=0123 -->
<!-- env: MAXON_HASH_TIER=fast -->
The two variables configure the hash, so only a program that contains the hash runtime reads them. A
program with no hash in it has no seed and no tier to set: launched with a malformed seed and an unknown
tier, it runs and exits 0 on every lane, `wasm32-wasi` included.
```maxon
function main() returns ExitCode
	print("main ran\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
main ran
```

<!-- test: an-environment-assignment-reaches-the-program-whatever-its-name -->
<!-- env: a=x -->
<!-- env: code=7 -->
An `env` assignment reaches the program unchanged on every lane, whatever name it sets: a one-letter name
such as `a` or a word such as `code` is the program's own, and no runner's use of a name of its own may
change the value the program reads.
```maxon
function main() returns ExitCode
	let a = try Process.environmentVariable("a") otherwise return 2
	let code = try Process.environmentVariable("code") otherwise return 3
	print("a={a}\n")
	print("code={code}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
a=x
code=7
```

<!-- test: an-environment-assignment-reaches-the-program-under-a-read-only-shell-name -->
<!-- env: PPID=424242 -->
<!-- env: UID=4242 -->
An `env` assignment reaches the program even under a name a shell keeps read-only, such as `PPID` or `UID`:
the program reads the value the case assigns, not the one a runner's shell would have exported for it.
```maxon
function main() returns ExitCode
	let parent = try Process.environmentVariable("PPID") otherwise return 2
	let user = try Process.environmentVariable("UID") otherwise return 3
	print("PPID={parent}\n")
	print("UID={user}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
PPID=424242
UID=4242
```

<!-- test: resume-continues-a-finalized-fold -->
A `KeyedHasher`'s `finalize()` yields the running state and `resume()` takes it back, so carrying the value
across a boundary is not a different hash: the resumed hasher reaches the state the pure face reaches from the
carried value. Without that pair a caller that threads the running value through its own structures would
need a second copy of the fold.
```maxon
function main() returns ExitCode
	let first = KeyedHasher.create(HashSeed.random())
	first.combineBytes("hel".toByteArray())
	let carried = first.finalize()

	let second = KeyedHasher.resume(carried)
	second.combineBytes("lo".toByteArray())

	print("resumed is the pure step: {second.finalize() == KeyedHasher.combinedBytes(carried, bytes: "lo".toByteArray())}\n")
	print("resumed moved the state: {KeyedHasher.combinedBytes(carried, bytes: "lo".toByteArray()) != carried}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
resumed is the pure step: true
resumed moved the state: true
```

<!-- test: the-two-faces-are-one-fold -->
`KeyedHasher.empty(seed)`/`KeyedHasher.combined` and `KeyedHasher.create(seed)`/`combine` are the same
arithmetic reached two ways under one seed, so each pair below agrees. Two independent copies of a step agree
with themselves forever and only disagree with each other, which is why the pin is the cross-face comparison.
```maxon
function main() returns ExitCode
	let seed = HashSeed.random()
	let byObject = KeyedHasher.create(seed)
	byObject.combineBytes("hello".toByteArray())
	byObject.combine(42)

	let byValue = KeyedHasher.combined(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "hello".toByteArray()), value: 42)

	print("empty states agree: {KeyedHasher.create(seed).finalize() == KeyedHasher.empty(seed)}\n")
	print("folds agree: {byObject.finalize() == byValue}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
empty states agree: true
folds agree: true
```

<!-- test: a-caller-added-length-tag-separates-them -->
A caller can tag each part with its length — fold the bytes, then fold the count — as
`ContentHash.mixBytes` does, over a `KeyedHasher` under one seed. Under that policy the three groupings of the
same three bytes reach three different digests.
```maxon
typealias PartArray = Array with String

function taggedFold(state HashDigest, text String) returns HashDigest
	let bytes = text.toByteArray()
	let hasher = KeyedHasher.resume(state)
	hasher.combineBytes(bytes)
	hasher.combine(bytes.count() as HashDigest)
	return hasher.finalize()
end 'taggedFold'

function foldParts(parts PartArray, seed HashSeed) returns HashDigest
	var state = KeyedHasher.create(seed).finalize()

	for part in parts 'each'
		state = taggedFold(state, text: part)
	end 'each'

	return state
end 'foldParts'

function main() returns ExitCode
	let seed = HashSeed.random()
	let whole = foldParts(["abc"], seed: seed)
	let abThenC = foldParts(["ab", "c"], seed: seed)
	let aThenBc = foldParts(["a", "bc"], seed: seed)

	print("whole/ab-c: {whole != abThenC}\n")
	print("whole/a-bc: {whole != aThenBc}\n")
	print("ab-c/a-bc: {abThenC != aThenBc}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole/ab-c: true
whole/a-bc: true
ab-c/a-bc: true
```

<!-- test: a-pure-fold-nests-where-a-hasher-cannot -->
`mixBytes` written as one expression, the inner fold feeding the outer, reaches the digests the object-face
tagged fold above reaches for each grouping. A refactor that let the two spellings drift apart reddens here.
```maxon
typealias PartArray = Array with String

function mixBytes(state HashDigest, bytes ByteArray) returns HashDigest
	return KeyedHasher.combined(KeyedHasher.combinedBytes(state, bytes: bytes), value: bytes.count() as HashDigest)
end 'mixBytes'

function pureParts(parts PartArray, seed HashSeed) returns HashDigest
	var state = KeyedHasher.empty(seed)

	for part in parts 'each'
		state = mixBytes(state, bytes: part.toByteArray())
	end 'each'

	return state
end 'pureParts'

function objectParts(parts PartArray, seed HashSeed) returns HashDigest
	let hasher = KeyedHasher.create(seed)

	for part in parts 'each'
		let bytes = part.toByteArray()
		hasher.combineBytes(bytes)
		hasher.combine(bytes.count() as HashDigest)
	end 'each'

	return hasher.finalize()
end 'objectParts'

function main() returns ExitCode
	let seed = HashSeed.random()

	print("whole={pureParts(["abc"], seed: seed) == objectParts(["abc"], seed: seed)}\n")
	print("ab-c={pureParts(["ab", "c"], seed: seed) == objectParts(["ab", "c"], seed: seed)}\n")
	print("a-bc={pureParts(["a", "bc"], seed: seed) == objectParts(["a", "bc"], seed: seed)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
whole=true
ab-c=true
a-bc=true
```

<!-- test: a-folded-word-never-forgets-the-state -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
`KeyedHasher.combined(state, value)` is a digest of both, on every tier: no word folded into a state may erase it. The
parent re-executes itself once per tier the host runs, with `MAXON_HASH_TIER` forcing it, and each child
folds ten words into 64 states drawn from `HashSeed.random()`. The words are the ones a multiply-and-fold
step is most likely to cancel against: 0, 1, all ones, wyhash's four secret constants and three common
64-bit mixing constants. For each word the 64 results are pairwise different, and at one state the ten
words give ten different results. A word that zeroes an operand of the step makes every state reach one
digest, so two different states collide and a key salted with that word loses what it was salted onto.
Each child's report is compared with the one an intact hash gives, so the summary is the same on every host,
and a tier that differs is named with what its child wrote. Not on `wasm32-wasi`, where the subprocess band
is refused at compile time and the tier is always `software`.
```maxon
typealias DigestList = Array with HashDigest
typealias TierList = Array with String

let StateCount = 64 as Count

function allDistinct(digests DigestList) returns bool
	for (outer, one) in digests.withIterator() 'eachOne'
		for (inner, other) in digests.withIterator() 'eachOther'
			if outer.index() < inner.index() and one == other 'same'
				return false
			end 'same'
		end 'eachOther'
	end 'eachOne'

	return true
end 'allDistinct'

function words() returns DigestList
	return DigestList from [0, 1, 0xFFFFFFFFFFFFFFFF, 0xa0761d6478bd642f, 0xe7037ed1a0b428db, 0x8ebc6af09c88c6e3, 0x589965cc75374cc3, 0x9E3779B97F4A7C15, 0xC2B2AE3D27D4EB4F, 0xD6E8FEB86659FD93]
end 'words'

function child() returns ExitCode
	let states = DigestList.create(0)

	for _ in 0 upto StateCount 'eachState'
		states.push(KeyedHasher.empty(HashSeed.random()))
	end 'eachState'

	print("states distinct={allDistinct(states)}\n")

	for (iter, word) in words().withIterator() 'eachWord'
		let folded = DigestList.create(0)

		for state in states 'eachState'
			folded.push(KeyedHasher.combined(state, value: word))
		end 'eachState'

		print("w={iter.index()} distinct={allDistinct(folded)}\n")
	end 'eachWord'

	let atOneState = DigestList.create(0)
	let oneSeed = HashSeed.random()

	for word in words() 'eachWord'
		atOneState.push(KeyedHasher.combined(KeyedHasher.empty(oneSeed), value: word))
	end 'eachWord'

	print("words distinct={allDistinct(atOneState)}\n")
	return 0
end 'child'

function intactReport() returns String
	var report = "states distinct=true\n"

	for index in 0 upto words().count() 'eachWord'
		report.append("w={index} distinct=true\n")
	end 'eachWord'

	report.append("words distinct=true\n")
	return report
end 'intactReport'

function hostRuns(tier String) returns bool
	if tier == "aes" 'aes'
		return __Builtins.cpuHasAes()
	end 'aes' else if tier == "software" 'software'
		return true
	end 'software'

	panic("'{tier}' is not one of the tiers this case runs")
end 'hostRuns'

function runUnder(me FilePath, tier String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_TIER": tier])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'runUnder'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let tiers = TierList from ["aes", "software"]
	var kept = true

	for tier in tiers 'eachTier'
		if not hostRuns(tier) 'absent'
			continue
		end 'absent'

		let run = runUnder(me, tier: tier)

		if run.exitCode() != 0 or run.stdout != intactReport() 'forgot'
			kept = false
			print("tier={tier} exit={run.exitCode()}\n{run.stdout}{run.stderr}")
		end 'forgot'
	end 'eachTier'

	print("all tiers keep the state: {kept}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
all tiers keep the state: true
```

<!-- test: the-software-tier-has-no-seed-independent-collision -->
<!-- unsupported-targets: wasm32-wasi -->
No message may cancel the state the software tier has folded so far. The software tier keys each 16-byte
pair's first word with a public constant before the multiply-and-fold, so a word equal to that constant
with its low bit flipped would make one operand 1, and the fold of 1 and anything is the same constant:
everything before it, the seed included, would be forgotten. The program re-executes itself with
`MAXON_HASH_TIER=software`; the child builds two 32-byte messages that start with that word and differ only
in the second word, and two 64-byte messages that start each 16-byte lane of the long-input loop with its
lane's word and differ only in the second word of each lane. For two seeds drawn from `HashSeed.random()`
each pair must reach two digests. Not on `wasm32-wasi`, where the subprocess band is refused at compile time.
```maxon
typealias Word = int(0 to u64.max)
typealias WordList = Array with Word

let ShortLaneKey = 0xe7037ed1a0b428db as Word
let SecondLaneKey = 0x8ebc6af09c88c6e3 as Word
let ThirdLaneKey = 0x589965cc75374cc3 as Word
let WordBytes = 8
let TailBytes = 16
let TailByte = 0x22
let FirstMiddle = 0x11
let SecondMiddle = 0x33

function pushWord(bytes ByteArray, word Word)
	for index in 0 upto WordBytes 'eachByte'
		bytes.push(((word shr (index * 8)) and 0xFF) as Byte)
	end 'eachByte'
end 'pushWord'

function pushRun(bytes ByteArray, value Byte, count Count)
	for _ in 0 upto count 'eachByte'
		bytes.push(value)
	end 'eachByte'
end 'pushRun'

function message(lanes WordList, middle Byte) returns ByteArray
	let bytes = ByteArray.create(0)

	for lane in lanes 'eachLane'
		pushWord(bytes, word: lane xor 1)
		pushRun(bytes, value: middle, count: WordBytes)
	end 'eachLane'

	pushRun(bytes, value: TailByte, count: TailBytes)
	return bytes
end 'message'

function collides(lanes WordList, seed HashSeed) returns bool
	let first = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: message(lanes, middle: FirstMiddle))
	let second = KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: message(lanes, middle: SecondMiddle))
	return first == second
end 'collides'

function child() returns ExitCode
	let short = WordList from [ShortLaneKey]
	let long = WordList from [ShortLaneKey, SecondLaneKey, ThirdLaneKey]

	for draw in 1 to 2 'eachSeed'
		let seed = HashSeed.random()
		print("seed {draw}: {message(short, middle: FirstMiddle).count()} bytes collide={collides(short, seed: seed)}\n")
		print("seed {draw}: {message(long, middle: FirstMiddle).count()} bytes collide={collides(long, seed: seed)}\n")
	end 'eachSeed'

	return 0
end 'child'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_TIER": "software"])
	let run = try Subprocess.runConfiguration(config) otherwise return 4

	print("child exit={run.exitCode()} stderr=[{run.stderr}]\n{run.stdout}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
child exit=0 stderr=[]
seed 1: 32 bytes collide=false
seed 1: 64 bytes collide=false
seed 2: 32 bytes collide=false
seed 2: 64 bytes collide=false
```

<!-- test: equal-process-folds-finalize-equal -->
Two `Hasher.create()` folds over equal steps finalize equal within a process, and a fold over different steps
finalizes differently: other bytes, other bytes at the same boundary, and the same bytes split at another
boundary. The state is not readable, so the 32-bit answer is the whole observable; three different pairs
that all agreed would be a chance of about 2^-96.
```maxon
function fold(first String, second String) returns HashValue
	let hasher = Hasher.create()
	hasher.combineBytes(first.toByteArray())
	hasher.combine(42)
	hasher.combineBytes(second.toByteArray())
	return hasher.finalize()
end 'fold'

function main() returns ExitCode
	let reference = fold("abc", second: "def")

	print("equal steps agree: {reference == fold("abc", second: "def")}\n")
	print("other bytes differ: {reference != fold("abc", second: "deg")}\n")
	print("other first part differs: {reference != fold("abd", second: "def")}\n")
	print("another boundary differs: {reference != fold("ab", second: "cdef")}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
equal steps agree: true
other bytes differ: true
other first part differs: true
another boundary differs: true
```

<!-- test: a-process-fold-finalizes-to-a-hash-value -->
`Hasher.finalize()` returns the type `String.hash()` returns, `HashValue`: a function that takes a `HashValue`
accepts it with no cast, the value is the hash a `String` of the same bytes answers, and it is at most 8
hexadecimal digits wide.
```maxon
function accept(value HashValue) returns HashValue
	return value
end 'accept'

function main() returns ExitCode
	let hasher = Hasher.create()
	hasher.combineBytes("abc".toByteArray())

	let value = accept(hasher.finalize())

	print("the fold is the hash of the string: {value == "abc".hash()}\n")
	print("at most 8 digits: {"{value:x}".byteLength() <= 8}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the fold is the hash of the string: true
at most 8 digits: true
```

<!-- test: a-process-fold-is-not-a-hash-digest -->
`Hasher.finalize()` is not a `HashDigest`: no 64-bit digest of the process fold exists to hold, so a
`HashDigest` parameter refuses it as it refuses any `HashValue`, and the diagnostic asks for the cast every
`HashValue` needs there.
```maxon
function asDigest(digest HashDigest) returns HashDigest
	return digest
end 'asDigest'

function main() returns ExitCode
	let hasher = Hasher.create()
	hasher.combineBytes("abc".toByteArray())
	print("{asDigest(hasher.finalize())}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:9:10: argument type mismatch for 'digest': expected 'HashDigest', got 'HashValue' — cast the argument with 'as HashDigest'
```

<!-- test: the-hash-methods-are-the-process-fold -->
`String.hash()`, `Character.hash()` and `Array.hash()` are the `Hasher` fold: the `finalize()` of a
`Hasher.create()` that folds the same bytes with `combineBytes`. A `String`, the empty one included, a
`Character` of several bytes, and a `ByteArray` of the same bytes as the `String` each agree with it.
```maxon
function folded(bytes ByteArray) returns HashValue
	let hasher = Hasher.create()
	hasher.combineBytes(bytes)
	return hasher.finalize()
end 'folded'

function main() returns ExitCode
	let message = "a message of the process fold"
	let empty = ""
	let euro = '€'

	print("string: {message.hash() == folded(message.toByteArray())}\n")
	print("empty string: {empty.hash() == folded(ByteArray.create(0))}\n")
	print("character: {euro.hash() == folded("€".toByteArray())}\n")
	print("byte array: {message.toByteArray().hash() == folded(message.toByteArray())}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
string: true
empty string: true
character: true
byte array: true
```

<!-- test: a-process-fold-has-no-empty-state -->
`Hasher` has no `empty`: a fold from the process seed has no state a program can hold, so `Hasher.empty()` is
refused as any call to a static member the type does not have is, E3004 with no diagnostic of its own.
```maxon
function main() returns ExitCode
	let state = Hasher.empty()
	print("{state}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:21: call to undefined function 'Hasher.empty'
```

<!-- test: a-process-fold-has-no-pure-combined-step -->
`Hasher` has no `combined`: the pure step takes and returns a state, and the process fold has none to take.
```maxon
function main() returns ExitCode
	let state = Hasher.combined(1, value: 2)
	print("{state}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:21: call to undefined function 'Hasher.combined'
```

<!-- test: a-process-fold-has-no-pure-combined-bytes-step -->
`Hasher` has no `combinedBytes`, for the reason it has no `combined`.
```maxon
function main() returns ExitCode
	let state = Hasher.combinedBytes(1, bytes: "abc".toByteArray())
	print("{state}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:21: call to undefined function 'Hasher.combinedBytes'
```

<!-- test: a-process-fold-cannot-be-resumed -->
`Hasher` has no `resume`: with no 64-bit state to carry out of the fold, there is none to carry back in.
```maxon
function main() returns ExitCode
	let hasher = Hasher.resume(1)
	hasher.combine(2)
	print("{hasher.finalize()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3004: <fragment>:3:22: call to undefined function 'Hasher.resume'
```

<!-- test: a-process-fold-takes-no-seed -->
`Hasher.create()` takes no seed: the process fold is the one that starts from the process seed, and a fold from
a seed the program holds is a `KeyedHasher`. The argument is refused as an argument to any function that takes
none is, E3036 with no diagnostic of its own.
```maxon
function main() returns ExitCode
	let seed = HashSeed.random()
	let hasher = Hasher.create(seed)
	hasher.combineBytes("abc".toByteArray())
	print("{hasher.finalize()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:4:22: 'Hasher.create' expects 0 argument(s) but 1 were provided
```

<!-- test: a-keyed-hasher-needs-a-seed-to-create -->
`KeyedHasher.create` has no default seed: a keyed fold names the seed it starts from, and a call with none is
refused as any call that omits a required argument is, E3036 with no diagnostic of its own.
```maxon
function main() returns ExitCode
	let hasher = KeyedHasher.create()
	hasher.combineBytes("abc".toByteArray())
	print("{hasher.finalize()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:27: 'KeyedHasher.create' expects 1 argument(s) but 0 were provided
```

<!-- test: a-keyed-hasher-needs-a-seed-for-its-empty-state -->
`KeyedHasher.empty` has no default seed either: the empty state of a fold is the seed's own, so there is none
until a seed is named.
```maxon
function main() returns ExitCode
	let state = KeyedHasher.empty()
	print("{state}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:26: 'KeyedHasher.empty' expects 1 argument(s) but 0 were provided
```

<!-- test: a-pinned-process-draws-the-same-random-seeds-on-every-run -->
<!-- unsupported-targets: wasm32-wasi -->
Under a pinned `MAXON_HASH_SEED`, the n-th `HashSeed.random()` of a process is the same on every run: the pin
fixes the whole sequence a process draws, not one value. The program re-executes itself twice with one pin,
and each child prints the 8 bytes of four successive draws. The two children print the same four lines. A
draw that ignored the pin would make the two runs agree with a chance of about 2^-256. Not on `wasm32-wasi`,
where the subprocess band is refused at compile time.
```maxon
let DrawsPerChild = 4
let SpelledSeedLength = 17

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function child() returns ExitCode
	for _ in 0 upto DrawsPerChild 'eachDraw'
		print("{toHex(HashSeed.random().toBytes())}\n")
	end 'eachDraw'

	return 0
end 'child'

function spawnPinned(me FilePath) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": "0123456789abcdef"])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnPinned'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let first = spawnPinned(me)
	let second = spawnPinned(me)

	print("children exit={first.exitCode()} {second.exitCode()}\n")
	print("each child printed four seeds: {first.stdout.count() == DrawsPerChild * SpelledSeedLength and second.stdout.count() == DrawsPerChild * SpelledSeedLength}\n")
	print("the two runs drew one sequence: {first.stdout == second.stdout}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0
each child printed four seeds: true
the two runs drew one sequence: true
```

<!-- test: one-pin-draws-the-same-random-seeds-on-every-tier -->
<!-- tier: stdlib -->
<!-- unsupported-targets: wasm32-wasi -->
The sequence a pin fixes does not depend on the hash tier: one pin draws the same `HashSeed.random()` seeds under
`MAXON_HASH_TIER=aes` and `MAXON_HASH_TIER=software`, though the two tiers are different hashes. The program
re-executes itself under one pin once per tier, and each child prints the 8 bytes of four successive draws. On a
host that runs `aes` the two children print the same four lines; on a host that does not, the `aes` child is
refused before `main` naming the host's best tier, and there is nothing to compare. The answers are written so
they are the same on every host. Not on `wasm32-wasi`, where the subprocess band is refused at compile time and
the tier is always `software`.
```maxon
let DrawsPerChild = 4
let SpelledSeedLength = 17

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function child() returns ExitCode
	for _ in 0 upto DrawsPerChild 'eachDraw'
		print("{toHex(HashSeed.random().toBytes())}\n")
	end 'eachDraw'

	return 0
end 'child'

function hostRunsAes() returns bool
	return __Builtins.cpuHasAes()
end 'hostRunsAes'

function runUnder(me FilePath, tier String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": "0123456789abcdef", "MAXON_HASH_TIER": tier])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'runUnder'

function drewFourSeeds(run CollectedOutput) returns bool
	return run.exitCode() == 0 and run.stderr == "" and run.stdout.count() == DrawsPerChild * SpelledSeedLength
end 'drewFourSeeds'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let aes = runUnder(me, tier: "aes")
	let software = runUnder(me, tier: "software")

	let aesAsTheHostAllows = drewFourSeeds(aes) if hostRunsAes() else aes.exitCode() == 1 and aes.stdout == "" and aes.stderr == "MAXON_HASH_TIER=aes is not available on this host: its best hash tier is software\n"

	print("aes answers as the host allows: {aesAsTheHostAllows}\n")
	print("software drew four seeds: {drewFourSeeds(software)}\n")
	print("the tiers drew one sequence: {not hostRunsAes() or aes.stdout == software.stdout}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
aes answers as the host allows: true
software drew four seeds: true
the tiers drew one sequence: true
```

<!-- test: two-pins-draw-different-random-seeds -->
<!-- unsupported-targets: wasm32-wasi -->
Two pins fix two sequences: a process pinned to `0123456789abcdef` and one pinned to `fedcba9876543210` draw
different `HashSeed.random()` seeds, the first draw already. The program re-executes itself once per pin, and
each child prints the 8 bytes of four successive draws. How a pin maps to its seeds is unspecified, so only
the difference is asserted. Not on `wasm32-wasi`, where the subprocess band is refused at compile time.
```maxon
let DrawsPerChild = 4
let SpelledSeedLength = 17

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function child() returns ExitCode
	for _ in 0 upto DrawsPerChild 'eachDraw'
		print("{toHex(HashSeed.random().toBytes())}\n")
	end 'eachDraw'

	return 0
end 'child'

function spawnPinned(me FilePath, seed String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": seed])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnPinned'

function firstDraw(run CollectedOutput) returns String
	return try run.stdout.split("\n").get(0) otherwise panic("a split has a first piece")
end 'firstDraw'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		return child()
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let one = spawnPinned(me, seed: "0123456789abcdef")
	let other = spawnPinned(me, seed: "fedcba9876543210")

	print("children exit={one.exitCode()} {other.exitCode()}\n")
	print("each child printed four seeds: {one.stdout.count() == DrawsPerChild * SpelledSeedLength and other.stdout.count() == DrawsPerChild * SpelledSeedLength}\n")
	print("the first draws differ: {firstDraw(one) != firstDraw(other)}\n")
	print("the sequences differ: {one.stdout != other.stdout}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0
each child printed four seeds: true
the first draws differ: true
the sequences differ: true
```

<!-- test: every-random-seed-call-is-a-draw -->
<!-- unsupported-targets: wasm32-wasi -->
Every `HashSeed.random()` call is a draw, so the compiler neither merges two calls nor drops one whose result
is discarded. The program re-executes itself three times. Two children run under one pin: the first prints
draws 1, 2 and 3, then whether draws 4 and 5, written in one expression, are equal; the second makes the same
draws with the second one discarded through `_ =`, and prints draws 1 and 3. A third child runs unpinned, its
`MAXON_HASH_SEED` set empty, and makes the first child's draws. In each run of the first kind the three draws
differ and so do the two of the one expression, and the discarding child's two lines are the pinned first
child's draws 1 and 3, where a dropped draw would make them its draws 1 and 2. A discard through `_ =` is not
refused: a draw is an effect. Not on `wasm32-wasi`, where the subprocess band is refused at compile time.
```maxon
let SpelledDrawLength = 16

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

function child(mode String) returns ExitCode
	if mode == "every" 'every'
		print("{toHex(HashSeed.random().toBytes())}\n")
		print("{toHex(HashSeed.random().toBytes())}\n")
		print("{toHex(HashSeed.random().toBytes())}\n")
		print("{HashSeed.random().toBytes().equals(HashSeed.random().toBytes())}\n")
		return 0
	end 'every' else if mode == "discarding" 'discarding'
		print("{toHex(HashSeed.random().toBytes())}\n")
		_ = HashSeed.random()
		print("{toHex(HashSeed.random().toBytes())}\n")
		return 0
	end 'discarding'

	return 3
end 'child'

function spawnUnder(me FilePath, mode String, seed String) returns CollectedOutput
	let argv = StringArray.create(0)
	argv.push("child")
	argv.push(mode)

	let config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_HASH_SEED": seed])
	return try Subprocess.runConfiguration(config) otherwise panic("the child could not be spawned")
end 'spawnUnder'

function line(run CollectedOutput, at Count) returns String
	return try run.stdout.split("\n").get(at) otherwise "<no line {at}>"
end 'line'

function drawsDiffer(run CollectedOutput) returns bool
	let first = line(run, at: 0)
	let second = line(run, at: 1)
	let third = line(run, at: 2)
	let spelled = first.count() == SpelledDrawLength and second.count() == SpelledDrawLength and third.count() == SpelledDrawLength

	return spelled and first != second and second != third and first != third and line(run, at: 3) == "false"
end 'drawsDiffer'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'iAmTheChild'
		let mode = try CommandLine.args().get(2) otherwise return 4
		return child(mode)
	end 'iAmTheChild'

	let me = try Process.executablePath() otherwise return 2
	let pinned = spawnUnder(me, mode: "every", seed: "0123456789abcdef")
	let discarding = spawnUnder(me, mode: "discarding", seed: "0123456789abcdef")
	let unpinned = spawnUnder(me, mode: "every", seed: "")

	print("children exit={pinned.exitCode()} {discarding.exitCode()} {unpinned.exitCode()}\n")
	print("pinned, every draw differs: {drawsDiffer(pinned)}\n")
	print("unpinned, every draw differs: {drawsDiffer(unpinned)}\n")
	print("the discarded draw advanced the sequence: {line(discarding, at: 0) == line(pinned, at: 0) and line(discarding, at: 1) == line(pinned, at: 2)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
children exit=0 0 0
pinned, every draw differs: true
unpinned, every draw differs: true
the discarded draw advanced the sequence: true
```

<!-- test: an-unpinned-process-draws-fresh-random-seeds-on-every-lane -->
<!-- env: MAXON_HASH_SEED= -->
An empty `MAXON_HASH_SEED` is no pin, on every lane, `wasm32-wasi` included: the program runs, and each
`HashSeed.random()` is fresh entropy. Three draws in one process differ from each other, key three different
empty states, and give three different digests of one message; none of them keys the process fold, so the
32-bit hash of `"abc"` under a draw is not `"abc".hash()`. The process runs where `every-random-seed-call-is-a-draw`
needs a subprocess, so this is the case that reaches the unpinned arm on `wasm32-wasi`. Two of three 64-bit
draws that agree are a chance of about 2^-62, so an equal pair is a draw that repeated itself.
```maxon
function digestUnder(seed HashSeed) returns HashDigest
	return KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "an unpinned draw".toByteArray())
end 'digestUnder'

function keysTheProcessFold(seed HashSeed) returns bool
	return hashValueOfDigest(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "abc".toByteArray())) == "abc".hash()
end 'keysTheProcessFold'

function main() returns ExitCode
	let first = HashSeed.random()
	let second = HashSeed.random()
	let third = HashSeed.random()

	let bytesDiffer = not first.toBytes().equals(second.toBytes()) and not second.toBytes().equals(third.toBytes()) and not first.toBytes().equals(third.toBytes())
	let emptiesDiffer = KeyedHasher.empty(first) != KeyedHasher.empty(second) and KeyedHasher.empty(second) != KeyedHasher.empty(third) and KeyedHasher.empty(first) != KeyedHasher.empty(third)
	let digestsDiffer = digestUnder(first) != digestUnder(second) and digestUnder(second) != digestUnder(third) and digestUnder(first) != digestUnder(third)

	print("the draws differ: {bytesDiffer}\n")
	print("the draws key different empty states: {emptiesDiffer}\n")
	print("the draws give different digests: {digestsDiffer}\n")
	print("no draw keys the process fold: {not keysTheProcessFold(first) and not keysTheProcessFold(second) and not keysTheProcessFold(third)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the draws differ: true
the draws key different empty states: true
the draws give different digests: true
no draw keys the process fold: true
```

<!-- test: concurrent-pinned-draws-never-repeat-an-index -->
<!-- procs: 4 -->
<!-- env: MAXON_HASH_SEED=0123456789abcdef -->
Under a pin the n-th draw of a process is the n-th seed of the pin's sequence, and the n is one counter the whole
process shares, so draws made at once on several processors still each take an index of their own. Four services,
on a scheduler of four processors, are each sent one request before any reply is awaited, and each makes 2000
draws and replies with their 8 bytes, one line per draw. The 8000 lines are 8000 different seeds. Two draws that
read the counter before either advanced it would take one index and draw one seed, which the pin makes the same
bytes, so a counter that is not advanced atomically shows as a repeated line. A set of every line counts them.
```maxon
typealias Tally = int(0 to 1000000)
typealias DrawerHandleArray = Array with Drawer.handle
typealias DrawsPromise = Promise with String
typealias DrawsPromiseArray = Array with DrawsPromise
typealias DrawnSet = Set with String

let drawers = 4
let drawsEach = 2000
let SpelledSeedLength = 16

function toHex(bytes ByteArray) returns String
	var text = ""

	for byte in bytes 'eachByte'
		text.append("{byte:02x}")
	end 'eachByte'

	return text
end 'toHex'

type Drawer
	var made as Tally

	static function create() returns Self
		return Self{made: 0}
	end 'create'

	export function draw(count Tally) returns String
		var lines = ""

		for _ in 0 upto count 'eachDraw'
			lines.append("{toHex(HashSeed.random().toBytes())}\n")
			self.made = self.made + 1
		end 'eachDraw'

		return lines
	end 'draw'
end 'Drawer'

function main() returns ExitCode
	let handles = DrawerHandleArray.create(0)

	for _ in 0 upto drawers 'spawnEach'
		handles.push(spawn Drawer.create())
	end 'spawnEach'

	let replies = DrawsPromiseArray.create(0)

	for handle in handles 'sendEach'
		replies.push(try handle.draw(drawsEach) otherwise panic("an open mailbox with room takes the send"))
	end 'sendEach'

	let seen = DrawnSet.create()
	var lines = 0 as Tally
	var spelled = true

	for reply in replies 'collect'
		let text = await reply

		for line in text.split("\n") 'eachLine'
			if line != "" 'aDraw'
				spelled = spelled and line.count() == SpelledSeedLength
				seen.insert(line)
				lines = lines + 1
			end 'aDraw'
		end 'eachLine'
	end 'collect'

	print("every service drew its share: {lines == drawers * drawsEach}\n")
	print("every line spells a seed: {spelled}\n")
	print("no two draws repeat a seed: {(seen.count() as Tally) == lines}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
every service drew its share: true
every line spells a seed: true
no two draws repeat a seed: true
```

<!-- test: a-pinned-draw-is-not-the-pin -->
<!-- env: MAXON_HASH_SEED=0123456789abcdef -->
A pin fixes the sequence of draws, and no draw is the pin itself, on every lane, `wasm32-wasi` included. Under the
pin `0123456789abcdef`, whose 8 bytes `01 23 45 67 89 AB CD EF` are the process seed, two draws differ from each
other and from the pin's bytes, and key empty states different from each other and from the one the pin's own
seed keys. The pin's own seed keys the process fold: the 32-bit hash of `"abc"` under it is `"abc".hash()`. That
control is what lets the next line fail: neither draw keys the process fold. The pin's bytes are spelled because
the case sets them; no draw's bytes are, since how a pin maps to its seeds is unspecified. Under a pin every line
is the same on every run, so an answer that is wrong is wrong on every run.
```maxon
function keysTheProcessFold(seed HashSeed) returns bool
	return hashValueOfDigest(KeyedHasher.combinedBytes(KeyedHasher.empty(seed), bytes: "abc".toByteArray())) == "abc".hash()
end 'keysTheProcessFold'

function main() returns ExitCode
	let spelled = ByteArray from [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF]
	let pinned = try HashSeed.fromBytes(spelled) otherwise panic("8 bytes are a seed")
	let first = HashSeed.random()
	let second = HashSeed.random()

	print("the draws differ: {not first.toBytes().equals(second.toBytes())}\n")
	print("the first is not the pin: {not first.toBytes().equals(spelled)}\n")
	print("the second is not the pin: {not second.toBytes().equals(spelled)}\n")
	print("the draws key different folds: {KeyedHasher.empty(first) != KeyedHasher.empty(second)}\n")
	print("no draw keys the pinned fold: {KeyedHasher.empty(first) != KeyedHasher.empty(pinned) and KeyedHasher.empty(second) != KeyedHasher.empty(pinned)}\n")
	print("the pin keys the process fold: {keysTheProcessFold(pinned)}\n")
	print("no draw keys the process fold: {not keysTheProcessFold(first) and not keysTheProcessFold(second)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
the draws differ: true
the first is not the pin: true
the second is not the pin: true
the draws key different folds: true
no draw keys the pinned fold: true
the pin keys the process fold: true
no draw keys the process fold: true
```
