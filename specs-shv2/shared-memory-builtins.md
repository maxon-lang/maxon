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
consumer of the ring can be written in Maxon at all: without it the only decoder in the tree is the
bootstrap's, and shv2's own spec runner has to shell out to `bin/maxon.exe monitor` to read a trace
its own compiler produced.

`SharedSegment.create(name, bytes:)` creates a NEW section of exactly that many bytes and maps it into
this process; the mapping is torn down by `close()`. `readWord` / `writeWord` address the mapping in
64-bit words at a byte offset, `copyOut` lifts a byte range out of it, and `segmentName()` answers the
name the section is published under — the one a second process needs in order to map the same bytes.

⚠ **A WORD IS ADDRESSED BY A BYTE OFFSET, NOT BY A WORD INDEX.** The ring's header and its records are
laid out in bytes by a format neither side owns, so an accessor that silently scaled its argument
would put every consumer's reads one stride away from the producer's writes — which is why
`distinct-offsets-hold-distinct-words` writes at 0 and at 8 and reads both back, rather than writing
once and trusting the round trip.

⚠ **`copyOut` IS THE BYTE-LEVEL VIEW, AND IT IS THE ONE THAT PINS THE LAYOUT.** A word written and
read back through this file's own accessors agrees with itself whatever byte order either of them
used; only a byte-level reading can say the section holds the little-endian bytes a decoder written
against the wire format will find there.

**Targets — x64-windows only.** These are Win32 section objects (`CreateFileMapping` /
`MapViewOfFile`), and they carry the same restriction the DebugStream host primitives beside them do.

## Tests

<!-- test: shared-memory-builtins.a-word-round-trips-through-a-mapping -->
<!-- targets: x64-windows -->
A section is created, one word is written at an offset and read back identical. This is the floor: a
mapping that could not be created, or one whose writes went somewhere the reads do not, fails here
before any layout question is asked.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-round-trip", bytes: 4096) otherwise return 3
	segment.writeWord(0, value: 1234567)
	let stored = segment.readWord(0)
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
<!-- targets: x64-windows -->
⭐ **THE OFFSET IS HONOURED RATHER THAN IGNORED.** Two words are written at two offsets and both are
read back. An accessor that dropped its offset, or scaled it as a word index on one side only, would
answer the SAME number twice — which a single round trip cannot see.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-offsets", bytes: 4096) otherwise return 3
	segment.writeWord(0, value: 11)
	segment.writeWord(8, value: 22)
	let first = segment.readWord(0)
	let second = segment.readWord(8)
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
<!-- targets: x64-windows -->
`copyOut` reads the section as BYTES, so it says what `writeWord` actually left there. The word is
`258` = `0x0102`, whose little-endian first four bytes are `2 1 0 0`; a big-endian store, or a write
that landed at another offset, changes every one of them.
```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("maxon-spec-shm-copy-out", bytes: 4096) otherwise return 3
	segment.writeWord(0, value: 258)
	let octets = segment.copyOut(0, byteCount: 4)
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
<!-- targets: x64-windows -->
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
