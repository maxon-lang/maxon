---
feature: shared-memory-builtins
status: experimental
keywords: [shared-memory, SharedSegment, mapping, ring, debugstream, monitor, ipc, win32]
category: system
---

# `stdlib/SharedMemory.maxon` — a named section two processes both map

## Documentation

A DebugStream trace does not travel down a pipe. The traced child writes fixed-size records into a
ring in a NAMED shared section, and the process watching it maps the same section by name and decodes
what it finds there. `stdlib/SharedMemory.maxon` is the door to that section, and it exists so a
consumer of the ring can be written in Maxon at all: without it the tree holds no decoder for the
ring.

`SharedSegment.create(name, bytes:)` creates a NEW section of exactly that many bytes and maps it into
this process; the mapping is torn down by `close()`. `readWord` / `writeWord` address the mapping in
64-bit words at a byte offset, `copyOut` lifts a byte range out of it, and `segmentName()` answers the
name the section is published under — the one a second process needs in order to map the same bytes.

Every accessor is bounds-checked against the size the section was created with: a `readWord` or
`writeWord` whose 8 bytes, or a `copyOut` whose `offset + byteCount`, would reach past the end throws
`SharedMemoryError.outOfBounds`, so the three carry `throws SharedMemoryError` and are called with `try`.

⛔ **A NAME IS HELD TO WHAT EVERY LANE CAN PUBLISH, ON EVERY LANE.** `create` throws
`SharedMemoryError.invalidName` before any host call for a name that is empty, longer than 31 bytes, or
holds a `/`, a `\` or a NUL byte. 31 bytes is Darwin's `PSHMNAMLEN`, the smallest limit of the lanes;
a `/` is a directory separator under `/dev/shm`, a `\` names a Win32 object namespace, and a NUL ends
the name the host sees short of the one `segmentName()` answers. Refusing only where a host would
refuse would let a program that runs on Windows fail on macOS with a bare `createFailed`.

⚠ **A WORD IS ADDRESSED BY A BYTE OFFSET, NOT BY A WORD INDEX.** The ring's header and its records are
laid out in bytes by a format neither side owns, so an accessor that silently scaled its argument
would put every consumer's reads one stride away from the producer's writes — which is why
`distinct-offsets-hold-distinct-words` writes at 0 and at 8 and reads both back, rather than writing
once and trusting the round trip.

⚠ **`copyOut` IS THE BYTE-LEVEL VIEW, AND IT IS THE ONE THAT PINS THE LAYOUT.** A word written and
read back through this file's own accessors agrees with itself whatever byte order either of them
used; only a byte-level reading can say the section holds the little-endian bytes a decoder written
against the wire format will find there.

**Targets — every lane that provides the `sharedMemory` host facility.** A section is a Win32 section
object (`CreateFileMapping` / `MapViewOfFile`) on x64-windows and a `/dev/shm` mapping on the POSIX
lanes; `wasm32-wasi` has no such object, so the compiler refuses `__shm_*` there with E3104 and these
cases are reported as skipped rather than run.

## Tests

<!-- test: shared-memory-builtins.a-word-round-trips-through-a-mapping -->
A section is created, one word is written at an offset and read back identical. This is the floor: a
mapping that could not be created, or one whose writes went somewhere the reads do not, fails here
before any layout question is asked.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-round-trip", bytes: 4096) otherwise return 3
	try segment.writeWord(0, value: 1234567) otherwise return 4
	let stored = try segment.readWord(0) otherwise return 5
	segment.close()
	print("stored={stored}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
stored=1234567
```
```exitcode
0
```

<!-- test: shared-memory-builtins.distinct-offsets-hold-distinct-words -->
⭐ **THE OFFSET IS HONOURED RATHER THAN IGNORED.** Two words are written at two offsets and both are
read back. An accessor that dropped its offset, or scaled it as a word index on one side only, would
answer the SAME number twice — which a single round trip cannot see.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-offsets", bytes: 4096) otherwise return 3
	try segment.writeWord(0, value: 11) otherwise return 4
	try segment.writeWord(8, value: 22) otherwise return 5
	let first = try segment.readWord(0) otherwise return 6
	let second = try segment.readWord(8) otherwise return 7
	segment.close()
	print("first={first} second={second}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
first=11 second=22
```
```exitcode
0
```

<!-- test: shared-memory-builtins.copy-out-answers-the-bytes-a-write-word-left -->
`copyOut` reads the section as BYTES, so it says what `writeWord` actually left there. The word is
`258` = `0x0102`, whose little-endian first four bytes are `2 1 0 0`; a big-endian store, or a write
that landed at another offset, changes every one of them.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-copy-out", bytes: 4096) otherwise return 3
	try segment.writeWord(0, value: 258) otherwise return 8
	let octets = try segment.copyOut(0, byteCount: 4) otherwise return 9
	segment.close()
	let b0 = try octets.get(0) otherwise return 4
	let b1 = try octets.get(1) otherwise return 5
	let b2 = try octets.get(2) otherwise return 6
	let b3 = try octets.get(3) otherwise return 7
	print("count={octets.count()} b0={b0} b1={b1} b2={b2} b3={b3}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
count=4 b0=2 b1=1 b2=0 b3=0
```
```exitcode
0
```

<!-- test: shared-memory-builtins.a-named-segment-closes-without-leaking -->
Every section is published under a name a second process can map, so `segmentName()` must answer a
real one. Four create/close cycles run in a case that exits 0: the suite's leak gate is exit **101**,
so a `close()` that unmapped nothing, or a segment record the cycle never released, turns this case
red without any assertion of its own having to know how the mapping is held.
```maxon
typealias NameTally = int(0 to u64.max)

function main() returns ExitCode
	var named = 0 as NameTally
	for cycle in 0 upto 4 'eachCycle'
		var segment = try SharedSegment.create("maxon-spec-shm-name-{cycle}", bytes: 4096) otherwise return 3
		if segment.segmentName().byteLength() > 0 'published'
			named = named + 1
		end 'published'

		segment.close()
	end 'eachCycle'

	print("named={named}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
named=4
```
```exitcode
0
```

<!-- test: shared-memory-builtins.a-name-some-lane-cannot-publish-is-refused-on-every-lane -->
⛔ **THE NAME RULE IS THE SAME ON EVERY LANE, WHATEVER THE HOST WOULD HAVE ACCEPTED.** A name of
exactly 31 bytes is published; a 32-byte name, an empty one, and names holding `/`, `\` or a NUL are
each refused with `invalidName` (7). Win32 alone publishes every one of them but the `\` name (5), and
refuses that one as `createFailed` (3), so every refused line reads 7 only because the rule runs ahead of
the host.
```maxon
function createOutcome(name String) returns ExitCode
	var segment = try SharedSegment.create(name, bytes: 4096) otherwise (e) 'refused'
		return match e 'why'
			invalidName gives 7
			createFailed gives 3
			mapFailed or
			outOfBounds gives 4
		end 'why'
	end 'refused'

	segment.close()
	return 5 as ExitCode
end 'createOutcome'

function main() returns ExitCode
	let atLimit = createOutcome("maxon-spec-shm-31-bytes-exactly")
	let pastLimit = createOutcome("maxon-spec-shm-32-bytes-exactly!")
	let empty = createOutcome("")
	let slash = createOutcome("maxon-spec/shm")
	let backslash = createOutcome("maxon-spec\\shm")
	let nul = createOutcome("maxon-spec\0shm")
	print("at-limit={atLimit} past-limit={pastLimit} empty={empty} slash={slash} backslash={backslash} nul={nul}\n")
	return 0 as ExitCode
end 'main'
```
```stdout
at-limit=5 past-limit=7 empty=7 slash=7 backslash=7 nul=7
```
```exitcode
0
```

<!-- test: shared-memory-builtins.a-word-read-past-the-segment-throws -->
⛔ **AN ACCESS THAT REACHES PAST THE SEGMENT THROWS `outOfBounds` INSTEAD OF READING WHATEVER LIES BEYOND
IT.** A word is 8 bytes, so offset 4090 in a 4096-byte section starts inside the mapping and ends 2 bytes
past it. Exit 7 is reachable only through the `outOfBounds` arm; a read that succeeded returns 5.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-read-past-end", bytes: 4096) otherwise return 3
	let word = try segment.readWord(4090) otherwise (e) 'refused'
		segment.close()
		return match e 'why'
			outOfBounds gives 7
			createFailed or
			mapFailed or
			invalidName gives 4
		end 'why'
	end 'refused'

	segment.close()
	print("read {word} past the end\n")
	return 5 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: shared-memory-builtins.a-word-written-past-the-segment-throws -->
The write twin of the read above: a store that straddles the end would overwrite memory the section
does not own, so it is refused by the same bound before any byte moves.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-write-past-end", bytes: 4096) otherwise return 3
	try segment.writeWord(4090, value: 1) otherwise (e) 'refused'
		segment.close()
		return match e 'why'
			outOfBounds gives 7
			createFailed or
			mapFailed or
			invalidName gives 4
		end 'why'
	end 'refused'

	segment.close()
	return 5 as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: shared-memory-builtins.copy-out-past-the-segment-throws -->
`copyOut`'s bound is `offset + byteCount`: 4000 + 200 ends 104 bytes past a 4096-byte section, although
the offset alone is inside it.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-copy-past-end", bytes: 4096) otherwise return 3
	let octets = try segment.copyOut(4000, byteCount: 200) otherwise (e) 'refused'
		segment.close()
		return match e 'why'
			outOfBounds gives 7
			createFailed or
			mapFailed or
			invalidName gives 4
		end 'why'
	end 'refused'

	segment.close()
	print("copied {octets.count()} bytes past the end\n")
	return 5 as ExitCode
end 'main'
```
```exitcode
7
```
